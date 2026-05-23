# P3 前瞻调度 + 派发风险 + 可配置 tick —— 设计 spec (2026-05-22)

> 现状提示(当前工作区):本文件是 P3 设计记录,不是最新 handoff。
> 当前实现已经删除 legacy `SupplyDemandGraph` 路径,统一由时间展开 solver 权威派发;
> `MergedSolverMode` / `ShadowMerged` / `LegacyOnly` 等灰度术语已过时。当前实现契约见
> `audits/mcmf-merge-handoff.md`。

> 本文是「P3 时间展开 MCMF」一揽子重构的设计 spec,写给实现者。
> 范围 = 4 个部分(A 可配置 tick / B 时间展开 solver / C 威胁预测器 / D 派发风险)。
> 前置阅读:`mcmf-merge-handoff.md`(合并 solver 现状 —— 本 spec 在其之上叠加)。
> 落地后本 spec 的内容应并入 `mcmf-merge-handoff.md` 或独立成 P3 交接文档。

---

## 1. 目的

合并 solver(`UnifiedGarrisonSolver`)目前是 **单 tick、确定性、纯空间** 的:每个 daily
tick 看当前状态、为「现在」求一次全局最优。两个缺陷:

1. **时间盲**:不能为可预测威胁提前招兵 / 调兵;和平期遣散后威胁回来又得重招(churn)。
2. **危险盲**:routing 成本只含距离,不含「这条路线上有没有敌军」—— 征兵队 / 调拨队
   被派进敌军附近送死(用户实测 bug:地方部队近在首府,征兵队仍十几人出门被歼)。

本重构:
- **B + C(P3)**:把合并图沿时间轴展开 T 个 tick 副本,一次 Solve 解 T-tick、只执行
  tick 0(receding-horizon MPC);未来各 tick 的威胁由预测器(C)提供。治时间盲。
- **D(派发风险)**:把「派发队伍自身的路途危险」作为一个成本 / 否决维度,接入派发层
  与合并 solver。治危险盲。**独立于 P3,且修当前 live bug。**
- **A(可配置 tick)**:首府后勤评估从固定 daily 改成可配置间隔(默认 6h)。

## 2. 已拍板决策(2026-05-22 与用户确认)

- 4 个部分**全做**。
- 派发风险覆盖 **征兵队 + 调拨队**;**出击队(sally)不纳入**。
- **不做**「在途遇敌撤回」(队伍已出门后的状态机改动)—— YAGNI,先把不该出门的拦住。
  (注:`OnWarDeclared` 已有「目标村变敌对则征兵队撤退」的窄触发,本次不扩展。)
- 威胁预测器范围 = **机制 + 威胁预测**(不做繁荣 / 预算预测,边际价值低)。
- 遣散速率上限归一成 **每天** 口径,换 tick 间隔不漂移。
- 派发风险用 **soft skip**(风险高就本 tick 不派、下 tick 再试),不硬禁用。
- tick 默认 **6h**,合法区间 1~24h。
- **P3 路线 = option A:真时间展开 + 软预算**(2026-05-22 二次拍板)。起因:时间展开
  图无法硬约束预算(多商品约束,见 §6.0)。代价:丢硬预算上限、且 T=1 不再等于今天
  行为(见 §6.5)。备选 B(预测塑形单 tick)/ C(外层迭代)未选。

## 3. 安全总则(贯穿全部 4 部分)

合并 solver **至今未在游戏内验证**(`MergedSolverMode=1` ShadowMerged)。P3 在其上叠 T
倍,基座 bug 会被放大。因此:

- **P3 全程 behind flag、影子运行**:受 `MergedSolverMode` 管控(ShadowMerged 时只记日志、
  不派发)。⚠️ option A 下 **没有「T=1 ≡ 今天」的同-solver 回退**(见 §6.5)——
  灰度安全网只有 `MergedSolverMode`(ShadowMerged 影子 / LegacyOnly 全关)。
- 各部分分增量交付,每个增量可编译、可独立停下。
- 改动 vanilla API 一律先引用 `SovereignTowns/_research/` 反编译源(CLAUDE.md 工作规范)。
- 提交走 master,但**提交动作需用户明确要求**。

---

## 4. Part A —— 可配置 tick 间隔

### 4.1 现状

`SovereignTownsCampaignBehavior.OnDailyTick` → `_capitalLogisticsManager.EvaluateAll()`
(`CampaignEvents.DailyTickEvent`)。`OnDailyTick` 还做 WebConfig 同步 + 每日活动汇总弹窗。

### 4.2 设计

- 新配置 `FiscalAutonomyConfig.CapitalLogisticsTickHours`(int,默认 `6`,读取时 clamp [1,24])。
- 新增 tick 入口:订阅 `CampaignEvents.HourlyTickEvent`,handler 内用 **无状态**门控 ——
  `(long)CampaignTime.Now.ToHours % CapitalLogisticsTickHours == 0` 时调 `EvaluateAll()`。
  无状态 → 存档 / 读档后相位自动对齐,无需持久化计数器。
- `OnDailyTick` 中 `EvaluateAll()` 那一行**移除**;其余职责(WebConfig 同步、每日活动
  汇总)留在 `OnDailyTick` 不动。
- 「一个 tick」从此 = `CapitalLogisticsTickHours` 小时。P3 的 T、遣散速率都以此为单位。

### 4.3 「每 tick」语义连带改动

- **遣散速率**:`MergedDisbandPerTickCap`(handoff §3.4 / §5)**重命名为
  `MergedDisbandPerDayCap`**(语义改为「每天」,默认仍 `20`)。`UnifiedGarrisonSolver`
  建 disbandGate 正常段时,把每 tick 上限算成
  `perTickCap = round(MergedDisbandPerDayCap × CapitalLogisticsTickHours / 24)`。
  这样换 tick 间隔不改变实际每日遣散速率。(pre-release,无需迁移;handoff §3.4/§5 同步更新。)
- 叙述节流(`DispatcherBudgetDeltaFraction`)、legacy `DisbandExcessGarrisons` 的比例门
  (`DisbandExcessThreshold`)是**比例**不是**速率**,触发更勤但无害,不动。
- `CapitalLogisticsTickHours` 影响 live 行为(非 behind 影子)→ 须补双端 UI(见 §9)。

---

## 5. Part D —— 派发风险

### 5.1 D-infra:`HostilePartyScanner`

新只读静态类(Layer 2 Evaluators),`SovereignTowns.Evaluators` 命名空间。

- 职责:扫 `MobileParty`(经 `Campaign.Current.MobileParties` / `MobileParty.All`),
  筛出对给定 clan / settlement **敌对**的军事队伍(`IsHostileTo` / map faction at war),
  提供两类查询:
  - `HostileStrengthNear(Vec2 point, float radius, IFaction friendly)` → 某点半径内敌对兵力强度。
  - `EnumerateConvergingHostiles(Settlement target)` → 朝某定居点移动的敌对 army/party
    + 各自估计 ETA(供 Part C 用)。
- ETA 估算:`距离 / 队伍速度`(vanilla `MobileParty.Speed`);朝向判定优先用
  `MobileParty.TargetSettlement` / AI 行为,取不到则用「位置正在接近」近似。
- 纯只读、无副作用、可跨线程(镜像 `RiskAssessmentService` 风格)。
- 引用 `_research/` 中 vanilla `MobileParty` / `Army` / `MapFaction` 的反编译源。

### 5.2 D1:派发层风险否决(修 live bug)

- `CapitalLogisticsManager.ExecuteRecruiterDispatch` 与 `ExecuteTransferDispatch` 加风险检查
  —— 这两个方法 **legacy(`mcmf`)与 merged 共用**(均经 `ExecuteInstructionList`),
  一处改动两条路径都生效;且 M6 删 legacy 后照样留用,非 throwaway。
- 风险评分:对征兵队取 `路线端点 + 沿途采样点`(首府、各目标村、村↔首府直线中点)的
  `HostileStrengthNear`;对调拨队取源↔目的端点 + 中点。取最大值作 routeRisk 分。
- routeRisk 分 ≥ `DispatchRiskVetoThreshold` → **soft skip**:本方法返回 false
  (`ExecuteInstructionList` 已有 skip 语义,与「队伍上限已满」同等处理),下个 tick 重评。
  记一条 `DISPATCH-RISK skipped` 日志。
- **出击队不经此路径**,天然不受影响。
- 受 `DispatchRiskEnabled`(默认 `true` —— 这是 bug 修复)管控,可一键回退。
- D1 直接改 live 行为 → 须补双端 UI(见 §9);本人无法做 in-game 验证,须用户实测。

### 5.3 D2:合并 solver 的派发风险成本项

- `UnifiedGarrisonSolver` 建图时,招募 origin→transit 边、调拨边的 `routing` 成本加一项:
  `routing = RoutingDistance + overhead + routeRiskSurcharge`。
- `routeRiskSurcharge` = `HostilePartyScanner` 评出的 routeRisk 分 × `DispatchRiskCostScale`,
  量级须与 routing(数百~千)可比,使 solver 软性权衡:危险路线 → 改选安全村 / 转首府
  原地招募(routing=0,零暴露)/ 不招(需求流向 bypass)。**软成本,非硬门。**
- D2 只影响合并 solver(影子态),不改 live 行为。
- D2 + B 叠加后自动获得「跨 tick 的派发风险」:见 §6.5。

---

## 6. Part B —— 时间展开 solver

> **2026-05-22 修订**:原 §6.4「τ=0 硬预算」假设经推导**不成立**(见 §6.0)。
> 用户拍板走 **option A:真时间展开 + 软预算**。本节按 option A 重写。

### 6.0 为什么软预算(关键设计约束)

时间展开图里,一个兵跨 tick 守军必须保留 `(settlement, role)` 身份 —— 否则 solver
能在预算闸节点「免费传送 + 改兵种」、绕开 routing 成本,招募/调拨决策全被腐蚀。

而硬预算 = 给「全氏族每 tick 守军总和」设上限。单商品 min-cost-flow 要给「一组保留
身份的并行边之和」设上限,只能让它们穿过一个公共节点 —— 公共节点必然合并身份。
**这是一个多商品约束,单商品 MCMF 表达不了。** 今天的单 tick solver 能硬预算,只因兵
在该 tick 后即进 sink 终止、身份在 budgetGate 上游已被 decode;兵一旦跨 tick 留存,
此技巧失效(τ=0 也不行,不只 τ≥1)。

→ P3 **全程软预算**:工资建模成每兵每 tick 的成本,holding 边费用含 `wage`。一个兵
占某 tier iff `value(tier) > wage`(再叠 routing 比较 → `value > routing + wage`)。
**没有硬预算上限。** 风险:理论上多数情形软 ≈ 今天的硬(value tier 递减 + surplus
负值 + 战时预算 boost 已把守军塑形到 ≈adequate,硬上限很少 binding),**但这是对真实
游戏分布的断言、非经验** —— 合并 solver 至今未 in-game 验证。若实测穷氏族过度养兵
→ 回退 `MergedSolverMode`,或启用 §6.7 外层迭代。

### 6.1 总体

`UnifiedGarrisonSolver` 重构为 T 层时间展开图(T = `MergedHorizonTicks`)。单商品 = 兵,
跨 `superSource→superSink` 守恒。一次 `MinCostFlow.Solve`,decode **只产 tick 0 指令**
(receding-horizon MPC)。建图逻辑抽成 `BuildLayer(ctx, τ, forecast)` 叠 T 份 + 连跨时间边。

### 6.2 节点

- `superSource` / `superSink`。
- `G[S,R,τ]` —— 兵在 settlement S、role R、tick 边界 τ。τ=0..T。首府 R∈{Cav,HA,Inf,Rng},
  分支 R=Inf 占位。
- `Transit[R,τ]` —— 招募兵抵首府,role R,tick τ。τ=0..T-1。
- `RecOrigin[src,R]` —— 招募兵源**单池**(src = 候选村 V 或首府 InPlace notable),
  **不带 τ** —— 反映 snapshot 村的志愿兵存量(避免 per-τ 复刻 → over-supply + SSP O(T²))。
- `disbandGate[S,τ]` —— **每城**每 tick 一个(懒创建,M4 两段 bypass)。注:不限非保护城
  —— 保护城也要一个,只是正常段容量设 0(见 §6.3 可行性)。

### 6.3 边与 K 平衡规则

#### K 平衡规则(先读这条 —— 否则 solver 会遣散所有兵)

holding 边的真实费用 `wage − value` 常为负(value > wage 才值得守军),而 `MinCostFlow`
拒负费用 → 须加 `K` 偏移。但偏移**不能逐边随意加**:留存兵走 T 条 holding 边累积 T·K、
遣散只走少数边 → K 不抵消、反成主导 → solver 把兵全遣散。这是 §6 的 #1 技术风险。

**修法(2026-05-22 拍板 option (1),advisor 复核通过)——
每条边的 K 分量 = `K ×(该边「代理」的、兵在 [0,T) 生命期里的 tick 数)`:**

- holding 边代理它所守的 **1** 个 tick → `1·K`(同时令 `K + wage − value ≥ 0`)。
- 招募入边(兵在 τ_a 抵达)代理抵达前的 **τ_a** 个未守军 tick → `τ_a·K`。
- 调拨 / 转发边(在途 d 个 tick)代理在途的 **d** 个 tick → `d·K`。
- τ 遣散边代理遣散后的 **T−τ** 个 tick → `(T−τ)·K`。
- 未招募出口代理全部 **T** 个 tick → `T·K`。
- 边界边(`superSource→G[·,0]`、`G[·,T]→sink`、`Transit→G`、`disbandGate→sink`)代理 **0** 个 → `0`。

`[0,T)` 的每个 tick 被「守军」或「非守军」恰好划分一次 → 每条 source→sink 路径的 K 分量
之和 **恒 = T·K** → 公共偏移抵消,`MinCostFlow` 最小化 Σ 真实费用。`MinCostFlow` 一行不改。

> ⚠️ SSP 沿**残差图**增广,残差反向边费用 = `−正向费用`,中途增广路径的 K 和 ≠ T·K ——
> **这不影响最终流的正确性**(终态总费用 = `T·K·totalFlow + Σ真实费用`,totalFlow 由
> 图结构定恒为总供给,见 §6.1),但**实现里任何「逐条增广路径」的断言 / 日志都不能假设
> 路径 K 和 = T·K**。

#### 边表(费用 = K 分量 + 真实费用,两者都列出)

| 边 | 容量 | K 分量 | 真实费用 |
|---|---|---|---|
| `superSource → G[S,R,0]` | 当前 (S,R) 头数 | 0 | 0 |
| `superSource → RecOrigin[src,R]` | 该源 role R 志愿兵数 | 0 | 0 |
| `RecOrigin[V,R] → Transit[R,τ_a]`(村;每个 τ_a ∈ [ETA_V, T-1],对应 dispatch tick τ=τ_a−ETA_V) | 志愿兵数 | `τ_a·K` | `routeRisk(@τ=τ_a−ETA_V) + recruiterOverhead` |
| `RecOrigin[InPlace,R] → Transit[R,τ]`(每个 τ ∈ [0,T-1]) | 志愿兵数 | `τ·K` | 0 |
| `RecOrigin[src,R] → superSink`(未招募出口) | 志愿兵数 | `T·K` | 0 |
| `Transit[R,τ] → G[capital,R,τ]`(招募兵留首府) | 大 | 0 | 0 |
| `Transit[R,τ] → G[branch,Inf,τ+d]`(转发分支,d=ETA_xfer;仅落 ≤ T-1) | 大 | `d·K` | `routeRisk + transferOverhead` |
| **holding** `G[S,R,τ] → G[S,R,τ+1]`,按 tier 拆 floor/core_k/surplus | 各 tier 容量 | `1·K` | `wage − value(tier, forecast.ThreatAt(S,τ))` |
| **调拨** `G[S,R,τ] → G[S',R,τ+d]`(现有驻军跨城,d=ETA_xfer;仅落 ≤ T) | 大 | `d·K` | `routeRisk + transferOverhead` |
| `G[S,R,τ] → disbandGate[S,τ]` | 该 (S,R) 头数 | `(T−τ)·K` | 0 |
| `disbandGate[S,τ] → superSink` 正常段 | 每-tick 上限(保护城 = 0) | 0 | 0 |
| `disbandGate[S,τ] → superSink` 溢出段 | 大 | 0 | `overflowPenalty` |
| **时域出口** `G[S,R,T] → superSink` | 大 | 0 | 0 |

- `wage` = 满级单兵工资(`WagePerTroopAtMaxTier`);wage ≪ routing ≪ K。
- value:沿用 `Merged*` tier 口径,threat 乘子取 `forecast.ThreatAt(S,τ)`。K ≫ maxValue(20M ≫ ~47K)
  → `1·K + wage − value > 0`,holding 边非负。
- **软预算:无 budgetGate** —— holding 边费用里的 `wage` 是唯一(且很弱)的预算信号(见 §6.0)。
- **可行性(每兵必有出口)**:时间展开图要求兵守恒 —— 每个 `G[S,R,τ]`(τ<T)必须能到 sink。
  出口 = holding tiers + 调拨 + disband。**disbandGate 对每城都建**(含保护城),保护城的
  正常段容量设 0 → 只剩溢出段(`overflowPenalty` 极大)→ 兵物理塞不下时仍可出图、但 solver
  绝不主动用。这统一了 §6 的 disband 处理、保证 max-flow 恒可行。
- 整数范围:单条边费用 ≤ ~`T·K`,T≤64、K=20M → ≤ ~1.3G,在 int 内;`MinCostFlow` 的
  Dijkstra 距离 / Johnson 势用 `long`(已是)。**实现增量须对 `MinCostFlow.cs` 加一处
  DEBUG-only 断言钩子(~5 行 `#if DEBUG` 计数器 / `Debug.Assert`,生产路径不动):验证
  `reduced < 0 → 0` 钳位在真实图上从不触发**(触发即编码有 bug)。
- siege 隔离:τ=0 即被围的城 —— 其 `G[S,R,τ]` 不连调拨边、不被招募兵汇入;disbandGate
  仍建(可行性要求)但正常段 cap=0 —— 与下方保护态一致,仅 overflow 兜底、solver 不主动
  遣散。holding 链直通 `G[S,R,T]→sink` 保可行。siege 是 τ=0 当前事实,不预测未来围城起止。
- routeRisk(D2)接 `RouteRiskSurcharge`,计入「真实费用」列;在飞/调拨边按各自 τ 评估
  → 跨 tick 自动生效。

### 6.4 ETA 与「在飞」

- ETA(ticks) = `round(行程小时 / CapitalLogisticsTickHours)`,行程 = `RoutingDistance /
  参考速度`。role-blind、近似(沿用 handoff §3.6 容忍度)。
- 复合路径(村→首府→分支)= 两条独立边,各按自己的 ETA 落 layer;总到达天然累计
  (村边落 `Transit[τ+ETA_V]`,转发边再落 `G[branch, τ+ETA_V+ETA_xfer]`)。
- τ+ETA 超出 horizon → **不建该边**:招募兵在 horizon 内无价值,solver 自然不招。

### 6.5 ⚠️ 后果:T=1 ≠ 今天的行为

option A 是真时间展开,**T=1 不再等于今天的单 tick solver**:T=1 时 horizon 只有
tick 0,村招募(ETA≥1)的在飞边落点 > T-1 → 不建 → **T=1 只能原地招募、永不派征兵队**。
因此:

- **「T=1 ≡ 今天、可安全灰度」这个性质作废**(原 §3 那条同步删除)。
- 时间展开 solver 要正常工作,`MergedHorizonTicks` 必须 ≥ 典型征兵队 ETA(行程数天 /
  6h tick ≈ 12~20 ticks)→ 默认值设 `16`(不是 1)。
- 灰度安全网只靠 `MergedSolverMode`:ShadowMerged 影子跑、不派发;LegacyOnly 完全关。
  P3 不再有「同一 solver 退化成旧行为」的回退。

### 6.6 decode(只取 layer 0)

- 招募指令:`RecOrigin[V,R,0]→Transit` / `RecOrigin[InPlace,R,0]→Transit` 的流。
- 调拨指令:`G[S,R,0]→G[S',·]` 流 + `Transit[R,0]→G[branch,·]` 流。
- `Disband[S]`:`G[S,R,0]→disbandGate[S,0]` 流。
- `Target[S]`:Σ_R holding 边 `G[S,R,0]→G[S,R,1]` 的流(= tick 0 守军头数)。
- layer 1..T-1 仅塑形,不产指令。

### 6.7 备选:外层迭代硬预算(本轮不做)

若实测软预算导致穷氏族过度养兵:P3 外层二分一个 wage 乘子,反复整解直到 τ=0 守军
总数 ≤ `budgetTroopCap`。代价:单 clan 单 tick ~5× 求解,且 wage→守军总数非单调
→ 收敛不保证。**本轮不实现**,留作 §6.0 风险兑现时的 fallback。

### 6.8 简化口径(YAGNI)

- 预测只调威胁 → 改 holding 边 value;**不模拟未来战损 / 掉员**。
- 单商品头数、首府 role 拆分,沿用今天口径。
- 在飞 / 留存边 role-blind 近似(沿用 handoff §3.6)。
- 招募兵源单池(`RecOrigin[V,R]` 不带 τ),反映 snapshot 村志愿兵存量;不模拟村庄志愿兵
  随 tick 再生 —— 近似:长 horizon 末段会低估可招量(偏保守,可接受)。

### 6.9 图规模与性能

- 节点数 ≈ `(城×role + 中转 + 招募源 + disbandGate) × (T+1)`;5-fief 氏族、T=16
  约数千节点、边略多。`MinCostFlow` SSP 多项式,daily/6h tick 预算内(handoff §9 已估)。
- 实现增量须验证:① T=16 单 clan solve 耗时可接受;② §6.3 的大额整数费用下
  `MinCostFlow` 的 Dijkstra/Johnson 不溢出、解正确。

---

## 7. Part C —— 威胁预测器

### 7.1 `IHorizonForecast` 接口

```
RiskLevel ThreatAt(Settlement s, int tick);   // tick 0 = 当前;tick>0 = 投影
```

- `BuildLayer(ctx, τ, forecast)` 经此拿每 tick 的威胁等级 → 算 tier value 的 threat 乘子。
- 预算暂不前瞻(§2 决策):τ≥1 预算 = 当前值(flat),接口不含 budget。

### 7.2 两个实现

- `FlatForecast`:`ThreatAt(s, τ) = RiskAssessmentService.Assess(s).Level`,所有 τ 相同。
  —— P3 机制(增量 B2)先用它验证时间展开正确、不破坏现有行为。
- `ThreatForecast`:tick 0 用 `RiskAssessmentService.Assess`;tick>0 用
  `HostilePartyScanner.EnumerateConvergingHostiles(s)` —— 对每个朝 s 移动的敌对 army/party
  按 ETA(ticks)把对应及之后 tick 的威胁等级上调(强度越大、ETA 落在窗口内越早,调得越高)。
  宣战时机不可预测 → 只吃「已存在的敌对单位正在移动」这一确定信号。

### 7.3 选择

- 新配置 `FiscalAutonomyConfig.MergedForecastMode` 枚举 `{ Flat, Threat }`(Newtonsoft
  序列化为整数),默认 `Flat`。`Threat` 时合并 solver 用 `ThreatForecast`。
- 退化情形 T=1 时只有 tick 0,forecast 无未来 tick 可调 → 该配置不起作用。

---

## 8. 配置一览(`FiscalAutonomyConfig` 新增 / 改名)

| 字段 | 类型 / 默认 | 含义 | 影响 live? |
|---|---|---|---|
| `CapitalLogisticsTickHours` | int `6`,clamp[1,24] | 首府后勤评估间隔(小时) | **是** |
| `MergedHorizonTicks` | int `16`,clamp[1,64] sanity 界 | P3 时域 T(tick 数)。须 ≥ 典型征兵队 ETA,否则只能原地招募(§6.5);`1` 是退化值非「今天行为」。有效上限由预测质量定(威胁过 ~14 天即噪声) | 否(影子) |
| `MergedForecastMode` | enum `Flat` | `Flat`/`Threat` —— P3 各 tick 威胁来源 | 否(影子) |
| `MergedDisbandPerTickCap` → `MergedDisbandPerDayCap` | int `20` | 改名 + 改「每天」口径(§4.3) | 否(影子) |
| `DispatchRiskEnabled` | bool `true` | 派发风险否决(D1)总开关 / 一键回退 | **是** |
| `DispatchRiskVetoThreshold` | float,初值待调 | D1 routeRisk 否决阈值 | **是** |
| `DispatchRiskScanRadius` | float,初值待调(~30 地图单位) | `HostilePartyScanner` 扫描半径 | **是** |
| `DispatchRiskCostScale` | int,初值待调 | D2 routeRisk→成本 的标度乘子 | 否(影子) |

> 阈值类旋钮放 `FiscalAutonomyConfig` 还是 `PartyThresholds`(CLAUDE.md「gate 类入
> `PartyThresholds`」)在 writing-plans 阶段定;`Merged*` 家族现都在 `FiscalAutonomyConfig`。

---

## 9. 增量与依赖顺序

| 增量 | 内容 | 依赖 | 修什么 |
|---|---|---|---|
| **A** | 可配置 tick 间隔 + `MergedDisbandPerDayCap` 改名 | 无 | tick 粒度 |
| **D-infra** | `HostilePartyScanner` | 无 | 地基 |
| **D1** | 派发层风险否决(征兵 / 调拨) | D-infra | **用户 live bug** |
| **D2** | 派发风险成本项接入合并 solver | D-infra | MergedOnly 时的优雅版 |
| **B** | 时间展开 solver:`UnifiedGarrisonSolver` 改建 §6 的 T 层软预算图 + decode layer 0 + `FlatForecast` | A | P3 机制(影子) |
| **C** | `ThreatForecast` 接入 | D-infra, B | P3「燃料」 |

A + D-infra + D1 + D2 = **Plan 1(已完成)**。B + C = **Plan 2**。
⚠️ option A 下 B **不是「重构 + T=1 逐位一致」** —— 时间展开软预算图(§6)与今天的
demand-tier→budgetGate 图结构不同,B 是新建图构造。B 的验证不能靠「T=1 == 今天」,
只能靠编译 + 影子日志 trace + §6.9 的性能/整数自检。Plan 2 的子增量由 writing-plans 定。

**双端 UI(handoff「控制面板两端同步」)**:影响 live 的旋钮(`CapitalLogisticsTickHours`、
`DispatchRisk*`)须补 Gauntlet 控制面板 + WebUI;纯影子的 `Merged*` / `MergedForecastMode`
沿用 handoff §5 的「behind flag、UI 留到终态切换」策略。UI 增量列在最后,可与 C 并行。

## 10. 回滚与验证

- **回滚**:`DispatchRiskEnabled=false` 关派发风险;`MergedSolverMode=0`(LegacyOnly)
  整体回 legacy —— 这是关 P3 的唯一开关(option A 下 `MergedHorizonTicks` 不能用来「关
  P3」,见 §6.5);`CapitalLogisticsTickHours=24` 回每日 tick。
- **验证**:本人无法做 in-game 验证。可交付 = 编译通过(0 errors、warnings 不增)+
  影子日志读码 trace。**D1 / Part A 直接改 live 行为,本人无法验证 —— 须用户实测**
  (确认征兵队不再被派进敌军、6h tick 评估节奏正常)。

## 11. 不在本次范围(out of scope)

- 「在途遇敌撤回」—— 队伍出门后的状态机 flee/return(用户明确不做)。
- 出击队(sally)的派发风险。
- 繁荣 / 预算的 T-tick 预测(只做威胁)。
- handoff §9 的 M6 切换、Phase 2(Prisoner 接入)—— 与本 spec 平行的独立工作。
