# 方案2:双层 MCMF 合并 — 详细设计 (2026-05-22)

前置:可行性结论与已锁定决策见 `mcmf-merge-feasibility.md`。本文给出合并图的精确
规格、价值标度方案、decode 契约、验证骨架、文件级改动与里程碑。

## 0. 决策回顾(已锁定)

1. disband 涌现(删 `DisbandExcessGarrisons` 6-Gate 方法)。
2. stockpile 删除(删 `IsRecruitmentStockpile` 相关代码)。
3. 目标稳定性:EWMA 平滑 value 输入 + 在飞粘性,两者都做。
4. manual 模式保留 Pass A 本体作 shadow 推荐引擎。

---

## 1. 合并图:精确规格

单商品、头数货币、单次 `MinCostFlow.Solve`。

### 1.1 节点

- `superSource` / `superSink`。
- `budgetGate` —— 总驻军预算闸。
- `bypass` —— "不入驻军"汇集点(不招募 / 被裁量遣散)。
- `capital-transit` —— **每首府每 role 一个**(4 个/首府);招募兵的中转节点。使单图能
  正确表达"招募→经首府→转发分支" —— 复合边无法 decode,必须建真实中转节点。按 role
  拆分是必需的:transit 汇集多村同 role 招募兵,其出边 `transit(role R) → 首府 R-demand`
  必须只承载 role R 流;role-blind 的单一 transit 会让 Cav 招募兵填进 Inf 缺口。
- **demand-tier 节点**:每 (settlement, role, tier)。
  - 首府:role ∈ {Cav,HA,Inf,Rng};tier ∈ {floor, core-1..K, surplus}。
  - 分支:role = 单一占位(头数口径);tier ∈ {floor, core-1..K, surplus}。
  - tier 容量沿用 Pass A:floor=`MinGarrisonFloor`,core 段 `adequate−floor` 按
    `CoreTierCount` 整数等分,surplus 段 `hardCap−adequate`。首府每 role 容量经
    `MatchPolicy.DesiredCount` 按规则比例拆分。
- **origin 节点**:
  - 现有驻军源:每 (settlement, role, tier-bucket),来自 `MatchPolicy.Bucketize(garrison roster)`。
  - 招募源:InPlace(首府 notable 志愿兵,per role)、Village(per-village per role,
    沿用 Phase 1 的 per-village 枚举)。[Prisoner 留 Phase 2。]

注:不再有独立的 "Garrison surplus source" 概念 —— 现有驻军源即超额机制:某驻军兵
流向他城 demand = transfer;流向 `bypass` = disband。

### 1.2 边、容量、费用

origin 分两类,接法不同 —— **每条边一一对应一个可 decode 的动作,无复合边**:
- **现有驻军 origin** 直连 demand-tier(兵已在城,无中转)。
- **招募 origin**(InPlace/Village)经其首府的 `capital-transit` 入图(招募兵必先抵
  首府,再决定留首府或转发分支)。

`K − value(tier)` 偏移单独落在"入 demand-tier"那一条边上(value 是 tier 专属);
routing 拆到路径各边、天然非负。`K ≥ max(value)` 即保证入 demand 边非负 —— routing
在各自边上自带非负,不需 K 覆盖。

| 边 | 容量 | 费用 |
|---|---|---|
| `superSource → origin` | origin 桶大小 | `0` |
| 现有驻军 `origin → demand-tier`(本城,role 匹配) | min(桶, tier 余量) | `K − value` |
| 现有驻军 `origin → demand-tier`(他城,role 匹配) | min(桶, tier 余量) | `transferRouting + K − value` |
| 现有驻军 `origin → bypass` | origin 桶大小 | `K` |
| 招募 `origin → capital-transit`(其首府) | origin 桶大小 | `recruitRouting` |
| 招募 `origin → bypass` | origin 桶大小 | `K` |
| `capital-transit → 本首府 demand-tier`(role 匹配) | tier 余量 | `K − value` |
| `capital-transit → 分支 demand-tier` | tier 余量 | `transferRouting + K − value` |
| `demand-tier → budgetGate` | tier 容量 | `0` |
| `budgetGate → superSink` | `budgetTroopCap` | `0` |
| `bypass → superSink` | 大数 | `0` |

- 求解口径 = min-cost-max-flow:`superSource→origin` 被最大流饱和,每个 origin 兵单位
  要么经(transit→)demand-tier 被驻,要么流到 `bypass`(disband / 不招)。`origin→bypass`
  恒可用,保证每个 origin 都有出路;`capital-transit` 因此不需要自己的 bypass —— solver
  不会把无法离开 transit 的流推进去。
- 招募兵入驻某 demand 的路径总费用 = `recruitRouting (+ transferRouting) + K − value`;
  入驻 iff 该总费用 `< K` ⇔ **`value > 总 routing`**,且沿途 tier 有余量、`budgetGate`
  有余量。预算紧张时 solver 保留 `value − 总routing` 最高的兵到 `budgetTroopCap`,
  其余进 bypass。disband 与预算配给都由此涌现。
- `K`(费用偏移)≥ `max(value)`,保证入 demand 边费用非负(MinCostFlow 硬约束)。

### 1.3 routing 费用口径

routing 拆到路径各边;每条边一一对应"至多一个动作",无复合边、无一边拆两指令。

- **现有驻军兵 留本城**:origin 直连本城 demand-tier,无 routing 边,费用纯 `K − value`。
  decode:无指令(兵留在原地)。
- **现有驻军兵 → 他城**:origin 直连他城 demand-tier,`transferRouting = Distance(本城,
  他城) + McmfTransferOverhead`。decode:TransferInstruction。
- **招募 origin → `capital-transit`**:`recruitRouting`。Village 源 = `Distance(村,首府)
  + McmfRecruiterOverhead`;InPlace 源 = `McmfInPlaceOverhead`(小)。decode:Village 入边
  → RecruiterInstruction(把兵带到首府);InPlace 入边 → InPlaceRecruitInstruction。
- **`capital-transit → 本首府 demand-tier`**:routing = 0,费用纯 `K − value`。decode:
  无指令 —— 招募指令已由"origin→transit"入边产出,此边只表示"招来的兵留在首府"。
- **`capital-transit → 分支 demand-tier`**:`transferRouting = Distance(首府,分支) +
  McmfTransferOverhead`。decode:TransferInstruction(首府→分支)。

注:`capital-transit` 出边按 demand 所属城读流量即可,无需追溯兵来自哪个 origin —— 招募
量已在入边读出、转发量已在出边读出,两者独立计账,不存在流分解歧义。

### 1.4 单位

全程头数。分支 demand 直接吃头数(删除 `HeadsToPower`)。预算 `budgetTroopCap =
clanWageBudget / wagePerTroop` 已是头数,沿用。`wagePerTroop` 仍取首府满级 tier 单值
(已知近似,可接受)。

### 1.5 实现注记:建图时域可参数化

合并 solver 的建图逻辑写成纯函数 `BuildGraphForTick(state, tick)` —— 给定一个状态
快照与 tick 序号产出子图,而非把"当前 tick"硬编进建图流程。M1–M6 只调用
`BuildGraphForTick(now, 0)`,行为不变。此约束的唯一目的是让 Phase 3(时间展开
MCMF,见 §11)成为"循环 t 调同一函数 + 加跨时间边",而非重写建图。代价为零 ——
只是把建图函数隐式的"当前"参数显式化。

---

## 2. 价值标度统一(核心难点)

**问题**:Pass A 现有 value 量级 ~0–15.6M(`CostOffset=20M`);routing 费用 ~百–数千
(`McmfUnmetCost=2000`)。合并后 `value − routing` 里 routing 沦为噪声。

**方案**:把 value 函数输出**重定标到 routing-cost 单位** —— "一个兵驻在此 tier 值得
跋涉多少距离单位"。校准锚点:
- floor-tier 单兵 value ≈ 数千(值得跨大半地图招)。
- core-tier 单兵 value ≈ 数百~低千(随 dim 递减)。
- surplus-tier 单兵 value ≈ 接近 0(只有极近兵源才划算)。

**改动**:重新推导三个基常数 `ValueFloorBase` / `ValueCoreBase` / `SurplusEdgeCost`,
使输出落在 ~[0, 数千];threat/strat/dim 是无量纲乘子,不变;`K`(费用偏移)取
`≥ max(value)` 即可(约数千~万级,远小于 20M)—— routing 在独立边上自带非负,K 不需
覆盖它(见 §1.2)。

**surplus value 的符号是 disband 行为开关**:
- surplus value > 0 → 现有驻军兵(routing=0)可留在 surplus 段 → disband 只削到 `hardCap`。
- surplus value ≤ 0 → surplus 段现有兵被 disband → 削到 `adequate`。
这是一个需 playtest 定的调参点,文档标记为 tuning 项。

**bypass-overflow 的"大惩罚"标度同属本节 tuning**:§4 两段 bypass 中,溢出段费用
`K + penalty`。临界推导 —— 若某 surplus tier value = `−V`(允许为负),则"留 surplus"
费用 `K + V`、"溢出 disband"费用 `K + penalty`;要让 solver 优先把 surplus 当海绵留兵,
须 **`penalty > |min(surplus value)|`**,否则两段 bypass 形同虚设(solver 直接走溢出段、
遣散上限失效)。故 penalty 与 surplus 负 value 的绝对值同量级,随重定标一起 tune。

**这一节代码改动小、playtest 不可压缩** —— 必须经 §5 parallel-run 回归验证。

---

## 3. decode:流 → 对外契约

decode 一一对应:**每条原始边的流量映射到至多一个动作**。无复合边、无一边拆两指令、
无需流分解 —— 每个指令量都能从某一条边的流量直接读出。

| 契约项 | 来源边 |
|---|---|
| `Target` 每城头数 | Σ 流入该城所有 demand-tier;每 fief 预播种 0 |
| RecruiterInstruction(含 TargetVillage) | Village `origin → capital-transit` 边的流,按源村 |
| InPlaceRecruitInstruction | InPlace `origin → capital-transit` 边的流 |
| TransferInstruction | `capital-transit → 分支 demand-tier` 的流 + 现有驻军 `origin → 他城 demand-tier` 的流 |
| (无指令) | `capital-transit → 本首府 demand-tier`、现有驻军留本城 demand 的流 |
| disband 指令 | 现有驻军 `origin → bypass` 边的流(遣散上限是图约束,非 decode 截断 —— 见 §4) |
| `Breakdown` 诊断串 | 每城 threat/strat/floor/adequate/hardCap,建图时算出 |
| `Budget` / `BudgetTroopCap` | `clanWageBudget` / `budgetTroopCap`,同今天 |
| `Unmet`/`TotalFlow`/`TotalCost`/`SettlementCount` | 合并流统计 |

招募兵经首府转发分支 = 两条真实边:`Village origin→transit` 出 RecruiterInstruction
(兵带到首府),`transit→分支demand` 出 TransferInstruction(首府→分支)。两条边各读
各的流,无歧义 —— 这正是 §1.1 建 `capital-transit` 中转节点的目的。

---

## 4. Gate 编码(disband 涌现的保护项)

siege / high-risk / `DisbandUnaffordableExcess=false` 三种情况下,该城现有驻军不可
被 disband。**编码**:这些城的现有驻军 origin 获一条 `origin → 本城 demand` 的
**绕过 `budgetGate`、无上限、cost=0** 的"保护保留边"(直接到 superSink)。后果:
保护态的现有兵恒被留(不占预算闸),预算闸只对"可裁量兵"(招募 + 非保护现有兵)生效。
代价:保护态可使总驻军超预算 —— 这正是"玩家选择不裁军则氏族超付"的正确语义。

> 子问题:feature-flag-off 时全部现有驻军走保护边,招募仍过 budgetGate。
> 可裁量预算 = `max(0, budgetTroopCap − 保护兵数)`。此交互在 M4 详化。

**每 tick 遣散上限 = 图约束,不是 decode 截断**:遣散销毁真兵不可逆,需限速。错误
做法是 decode 时截断 `origin→bypass` 流 —— 会令 Target(solver 以为留下的)与实际
派发(截断后多留的)不符,下 tick 拿错误现状重算 → 颠簸。正确做法是把每城"现有
驻军 → bypass"拆两段(如同 demand 分层):
> `origin → bypass`(常规段):容量 = 每城每 tick 遣散上限,费用 `K`。
> `origin → bypass-overflow`(溢出段):容量大数,费用 `K + 大惩罚`。
solver 优先填常规段;只有当某城现有兵实在无处可去(demand-tier 全满 + 常规遣散段
已满,即现有兵 ≫ hardCap 的病态超载)才走溢出段。好处:正常态遣散被限速,且图恒
可行(溢出段保证 max-flow 不会因无处可去而无解)。decode 时两段的流都计作 disband。
> M4 定案:此"两段 bypass"编码 vs "接受单次到位、靠 §5 EWMA 平滑防颤"二者择一。
> 注意单次到位选项有一窗口 EWMA 救不了 —— 首次启用 / 切 MergedOnly 那 tick,多城
> 同时 stocktake 可一次性 disband 数百兵;EWMA 平滑的是 value 输入的 tick 间演化,
> 不平滑起点跳变。若选单次到位,M4 须另设首-tick 软启动 grace 期。两段 bypass 在
> 这一点上是结构性优势(首 tick 也限速)。

注:保护态 origin(siege/risk/flag-off,本节上半)走 cost=0 保护边、不走 bypass。
保护边费用 0 ≪ bypass 的 `K`,solver 永不把保护兵推向 bypass —— 保护与遣散天然
互斥,无需额外约束。

---

## 5. 目标稳定性

- **EWMA(治本)**:value 函数输入(`Prosperity` / `NearbyLandThreatIntensity` /
  `clanWageBudget`)在喂价值函数前做指数平滑。状态 = 持久化 `settlementId/clanId →
  平滑值`;每 daily tick `smoothed = α·raw + (1−α)·smoothed`,α 配置(默认 ~0.25)。
  目标因此跨 tick 平滑,solver 稳态同解。
- **在飞粘性(治标)**:大部分已由 Phase 1 现成设施承担 —— `AccountInFlight`(在飞兵
  计入 inbound、缩减 demand)+ `CollectInFlightRecruiterVillages`(在飞征兵队目标村
  排除出图)。额外可选:对"延续在飞队伍计划"的边给小幅费用折扣。是否需要折扣,
  在 parallel-run 期按实测决定。
  - **M2 必做**:Phase 1 的在飞排除只覆盖 **Village 征兵队**。合并图新增"现有驻军→
    他城 transfer"指令 —— M2 必须把在飞排除扩展到在飞 `StTransferPartyComponent`
    (其目标城),否则会对已在途的运量重复下达指令。InPlace 招募若存在在飞期
    (实施时核实其 duration)则一并扩展;若同-tick 完成、无在飞概念则跳过。

---

## 6. manual 模式

合并 solver 在 manual 模式下用玩家手动目标作 demand-tier 容量。`GarrisonAssessment`
仍需价值函数推荐值 → **保留 `GarrisonAllocationSolver.Solve` 单独跑一次**作 shadow
推荐引擎(几百行、毫秒级),产 `RecommendedTarget`。即"砍掉 Pass A 入路由,Pass A
本体作 shadow 留着"。

---

## 7. parallel-run 验证骨架(M1 交付物)

`FiscalAutonomyConfig` 加 `MergedSolverMode ∈ {LegacyOnly, ShadowMerged, MergedOnly}`:
- **LegacyOnly**:仅跑旧两层,派发旧结果(= 今天行为)。
- **ShadowMerged**:旧 + 新都跑,**派发旧的**,新结果只 log;逐 clan/逐 settlement
  记 `Target` 差异 + 指令集差异到专用日志。
- **MergedOnly**:仅跑新合并 solver,派发新结果。

tuning 期停在 ShadowMerged,直到差异都能解释(或新方案系统性更优)再切 MergedOnly。

---

## 8. 文件级改动清单

**新增**:
- `src/Algorithm/UnifiedGarrisonSolver.cs` —— 合并 solver(建图 + Solve + decode)。
- 平滑状态容器(EWMA);可并入现有 manager 或新建小类。

**改**:
- `CapitalLogisticsManager.cs` —— `EvaluateClan` 重接:parallel-run 三态、调合并
  solver、decode;删 `DisbandExcessGarrisons`(6-Gate 方法整体删除)。
- `SupplyDemandGraph.cs` —— 逻辑迁入合并 solver;`IsRecruitmentStockpile` 相关删除;
  最终该文件被 gut 或删除(M6)。
- `GarrisonAllocationSolver.cs` —— 保留为 manual-mode shadow;value 基常数重定标。
- `FiscalAutonomyConfig.cs` —— 新增:`MergedSolverMode`、EWMA α、每 tick 遣散上限、
  重定标后的 value 常数。`ControlPanelSpecs.cs` 同步暴露(双端)。

**不动**:`MinCostFlow.cs`(直接复用)、`DispatchInstruction.cs`(指令类型够用)、
征兵队/调拨队组件与状态机(执行层不变)。

---

## 9. 实现里程碑

| M | 内容 | 交付/验证 |
|---|---|---|
| M1 | 合并 solver 骨架:建图(含 siege/risk/flag-off 保护边)+ Solve;parallel-run 三态 + 按边语义类别的差异日志 | ShadowMerged 下跑通,不派发 |
| M2 | decode → 完整对外契约(Target/指令/Breakdown/stats);合并 solver 复用 legacy passA 的预算/wage 输入,避免 ShadowMerged 期双算 | 差异日志可读 |
| M3 | value 重定标 + EWMA;tuning loop | playtest 比对差异收敛 |
| M4 | disband 涌现:两段 bypass(每 tick 遣散上限)+ 保护边/预算闸交互详化 | 删 `DisbandExcessGarrisons` |
| M5 | manual 模式 shadow Pass A 接线 | assessment 数据正确 |
| M6 | 切 MergedOnly;删 legacy 路由路径 + stockpile 代码 | ShadowMerged 跑满 ≥1 游戏内季度、差异稳定且条条可解释后才切;终态 |

每个 M 之间 build-verified;M3/M6 需 in-game 回归。

---

## 10. 风险与未决

- **value 重定标 tuning** 是最大不确定项,playtest 不可压缩(需观察数个游戏内季度)。
- feature-flag 保护 × 预算闸的交互(§4 子问题),M4 详化。
- 合并图规模:首府 4 role ×(2+K)tier + 分支 + per-village 源 —— 节点数百、边数千,
  MinCostFlow 在 daily tick 预算内(已确认),但 M1 应实测确认。
- 增广轮数 ∝ 总流量:建图时边容量按大批量给,勿人为切碎(MinCostFlow 性能注意点)。

---

## 11. 未来方向:Phase 3 — 时间展开 MCMF(滚动时域)

当前合并图是**单 tick 短视优化**:解"给定今天状态的最优分配 + 路由",无前瞻。
§5 的 EWMA + 在飞粘性只让目标跟随趋势,是前瞻的廉价仿制,不是真前瞻。

**Phase 3 思路**:把合并图沿时间轴展开 —— 节点 `(settlement, role, tier)` →
`(settlement, role, tier, tick t)`,叠 T 个 tick 副本,加跨时间边(留存边:今天驻军
流到明天同节点;在飞边:今天派出的队伍 t+Δ 才到货)。一次 `Solve` 解整个 T-tick
图,只**执行 tick 0 的决策**,下 tick 拿新状态重解 —— 即滚动时域 MPC,本质是
确定性等价下的 MDP。

- **不会状态爆炸**:图规模随 T **线性**增长(非指数);min-cost-flow 多项式,
  T=7~14 时约"节点数千、边数万",daily tick 预算内。流网络从不枚举状态组合 ——
  这正是它能做、而表格 MDP 不能做之处。
- **买到**:真前瞻(提前为可预测的威胁招兵;不在战争前夕遣散)。
- **买不到**:真随机性 —— 时间展开图吃确定性预测(对未来 threat/prosperity/budget
  取期望)。随机分支需场景树(退回指数)或采样近似。
- **真瓶颈是预测质量**,非求解器:需 threat/prosperity/budget 的 T-tick 预测;
  预测差 → 决策可能劣于单 tick 贪心。
- **前置依赖**:Phase 3 复用合并图,叠 T 份 + 跨时间边即可,接在 M6 之后,不重写。
  §1.5 的"建图时域可参数化"是为此预留的唯一前向兼容约束。

> 表格 / 精确 MDP 不可行:N 个定居点 × 驻军组成 × 在飞队伍 × 经济的联合状态空间
> 是天文级,value/policy iteration 出局。若要逐定居点的*真* MDP:用拉格朗日对偶
> dualize 共享工资预算 → 各点解耦为独立单点 MDP,主循环调价格 λ;或建模为
> restless multi-armed bandit 用 Whittle 指数。两者数学优雅但属研究级工作量,
> 非 mod 增量,本设计不采用。
