# P3 前瞻调度 + 派发风险 + 可配置 tick —— 设计 spec (2026-05-22)

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

## 3. 安全总则(贯穿全部 4 部分)

合并 solver **至今未在游戏内验证**(`MergedSolverMode=1` ShadowMerged)。P3 在其上叠 T
倍,基座 bug 会被放大。因此:

- **P3 全程 behind flag、影子运行**:受 `MergedSolverMode` 管控(ShadowMerged 时只记日志、
  不派发);`MergedHorizonTicks` 默认 `1`。
- **T=1 必须与当前 `UnifiedGarrisonSolver` 行为逐位一致** —— B 是重构 + 扩展,不是改写。
  T=1 时不建任何跨时间边,退化为今天的单层图。
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

### 6.1 总体

把 `UnifiedGarrisonSolver` 从「单层图」重构为「T 层时间展开图」。T 由
`FiscalAutonomyConfig.MergedHorizonTicks` 给定(默认 `1`)。一次 `MinCostFlow.Solve`,
decode **只产 tick 0 的指令**(receding-horizon MPC)。

### 6.2 重构方式(不改写)

- 把现有建图逻辑抽成 `BuildLayer(ctx, τ, forecast)` —— 产 tick-τ 的 demand-tier 节点 +
  transit 节点,tier value 由 `forecast.ThreatAt(s, τ)` 决定。
- `Solve` 改为:叠 T 份 `BuildLayer` + 连跨时间边。
- **T=1 时不连任何跨时间边,图与今天逐位一致** —— 这是回归基线,实现增量 B1 须验证。

### 6.3 跨时间边(P3 真正的新增)

- **驻军留存边**:settlement S、role R 在 tick τ 的驻军,构成 tick τ+1 的「现有驻军」
  origin 集合。引入 `GarrisonCarry[S,R,τ]` 节点:τ 层填入的 (S,R) 兵汇入它,它再作
  τ+1 层的 origin(可 Stay 进 τ+1 的 (S,R) tier、Transfer 去他城、或经 disbandGate 遣散)。
- **在飞边**:tick τ 派出的征兵队 / 调拨队,在 tick `τ + ETA` 才汇入目标 demand。
  ETA(ticks) = `round(行程小时 / CapitalLogisticsTickHours)`,行程由 `RoutingDistance / 速度`
  估;role-blind、近似(与 handoff §3.6 既有近似容忍度一致)。
- **τ=0 origin**:真实当前驻军(`MatchPolicy.Bucketize`)。
- **时域出口**:`GarrisonCarry[S,R,T-1] → superSink`。

### 6.4 预算:τ=0 硬、τ≥1 软(关键近似)

单商品 min-cost-flow 无法在保持 (S,R) 身份的同时,对「跨全部 (S,R) 的每 tick 总预算」
做硬约束(预算闸合并身份)。因此:

- **τ=0 层**:沿用今天的硬 `budgetGate`(budgetTroopCap 头数上限)—— 我们真正执行的
  就是 tick 0,预算必须准。
- **τ≥1 层**:**不建硬 budgetGate**。靠 value tier 的 diminishing + surplus 负值天然把
  驻军收敛到 ≈ adequate(即预算无约束时的最优点)。若真实预算紧于 adequate,规划期会
  略微高估可承担兵力 → tick 0 决策略偏「乐观预招」。这是有意的、文档化的近似 ——
  下个 tick 的 receding-horizon 重解会在新 τ=0 重新硬约束,自我纠偏。

> ⚠️ 时间展开图的精确节点 / 边编码(尤其 §6.4 预算耦合 + §6.3 留存边)是本重构 **#1
> 技术风险**。本 spec 给设计意图;精确编码在 writing-plans 阶段锁定,并经 advisor 复核。

### 6.5 decode

- 只把 **layer 0** 的边 decode 成真指令:τ=0 派出的征兵 / 调拨、τ=0 的遣散。
- `Target[S]` = layer 0 填入该城的 demand 之和。
- layer 1..T-1 仅「塑形」(影响 τ=0 决策),不产指令。
- decode 逻辑 = 今天 `UnifiedSolverResult` 的 decode,加一个「只取 layer 0」过滤。

### 6.6 简化口径(YAGNI)

- 预测只调威胁 → 改 tier value(「兵值不值得放这」),**不模拟未来战损 / 掉员**。
- 单商品头数、首府 role 拆分,全部沿用合并 solver 现有口径。
- 在飞 / 留存边 role-blind 近似,沿用 handoff §3.6 容忍度。

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
- T=1(`MergedHorizonTicks=1`)时 forecast 无意义(只有 tick 0)→ 该配置不起作用。

---

## 8. 配置一览(`FiscalAutonomyConfig` 新增 / 改名)

| 字段 | 类型 / 默认 | 含义 | 影响 live? |
|---|---|---|---|
| `CapitalLogisticsTickHours` | int `6`,clamp[1,24] | 首府后勤评估间隔(小时) | **是** |
| `MergedHorizonTicks` | int `1`,clamp[1,64] sanity 界 | P3 时域 T(tick 数);`1`=当前行为。**有效**上限由预测质量定(威胁预测过 ~14 天即噪声 → 实际有效 T ≈ 14×24/`CapitalLogisticsTickHours`) | 否(影子) |
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
| **B1** | `UnifiedGarrisonSolver` 重构成 `BuildLayer`,T=1 逐位一致 | A | 纯重构,零行为变化 |
| **D2** | 派发风险成本项接入合并 solver | D-infra, B1 | MergedOnly 时的优雅版 |
| **B2** | T 层叠加 + 跨时间边 + `FlatForecast` | B1 | P3 机制(影子) |
| **C** | `ThreatForecast` 接入 | D-infra, B2 | P3「燃料」 |

A + D-infra + D1 **最先做**:独立于 P3、修 live bug、是后续地基。

**双端 UI(handoff「控制面板两端同步」)**:影响 live 的旋钮(`CapitalLogisticsTickHours`、
`DispatchRisk*`)须补 Gauntlet 控制面板 + WebUI;纯影子的 `Merged*` / `MergedForecastMode`
沿用 handoff §5 的「behind flag、UI 留到终态切换」策略。UI 增量列在最后,可与 C 并行。

## 10. 回滚与验证

- **回滚**:`DispatchRiskEnabled=false` 关派发风险;`MergedHorizonTicks=1` 关 P3;
  `MergedSolverMode=0` 整体回 legacy;`CapitalLogisticsTickHours=24` 回每日 tick。
- **验证**:本人无法做 in-game 验证。可交付 = 编译通过(0 errors、warnings 不增)+
  影子日志读码 trace。**D1 / Part A 直接改 live 行为,本人无法验证 —— 须用户实测**
  (确认征兵队不再被派进敌军、6h tick 评估节奏正常)。

## 11. 不在本次范围(out of scope)

- 「在途遇敌撤回」—— 队伍出门后的状态机 flee/return(用户明确不做)。
- 出击队(sally)的派发风险。
- 繁荣 / 预算的 T-tick 预测(只做威胁)。
- handoff §9 的 M6 切换、Phase 2(Prisoner 接入)—— 与本 spec 平行的独立工作。
