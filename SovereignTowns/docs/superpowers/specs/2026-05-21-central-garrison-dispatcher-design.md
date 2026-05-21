# 设计文档：中央驻军调度器（Central Garrison Dispatcher）

- 日期：2026-05-21
- 目标：把"每个定居点养多少兵 / 多少工资 / 多少兵力"的决策权从玩家配置收归一个**中央调度器**。调度器对受管氏族的全部城镇 + 城堡（首府与非首府一视同仁）统一决策，并参考首府拥有者的当前态势。玩家默认不能再设目标；开 `AllowManualGarrisonTargets` 后可设，但调度器仍给出评估。
- 决策（建议，待用户确认 —— 可推翻）：见 §0。
- 依赖：本设计**依赖**财政自治 spec（`2026-05-21-fiscal-autonomy-design.md`）的金库（§3.1）、`STClanFinanceModel`（§3.2）、战时缓冲（§3.5）；并**取代**其可负担瀑布（§3.3 `AffordabilityPlanner`）。修订关系见 §9。
- 反编译来源：`_research/Vanilla/`（`DefaultSettlementTaxModel` / `DefaultPartyWageModel` 等，已在财政自治 spec 引用）；`src/Algorithm/MinCostFlow.cs`、`SupplyDemandGraph.cs`（现有路由 MCMF）。

---

## 0. 设计决策

| 决策 | 取值 | 理由 |
|---|---|---|
| 调度器算法 | **两段式 MCMF**：分配 MCMF + 路由 MCMF | 见 §2。分配是"按预算最大化总防御价值"，凸费用单商品流是对的工具且高效 |
| 首府/非首府 | 分配阶段**一视同仁** | 中央调度的本质：首府与城堡在同一张分配图里按价值竞争氏族预算；首府只多一个 `strategic` 加权 |
| 玩家设目标 | 默认**禁止**；`AllowManualGarrisonTargets` 开启后允许 + 调度器评估 | §7 |
| `TargetTotalCount`/`TargetPower` | 降级为**手动模式专属输入** | auto 模式下由分配 MCMF 决定，这两个字段不参与 |
| 与财政自治的关系 | 取代其 §3.3 瀑布；其余部分保留 | §9 |

---

## 1. 背景与动机

### 1.1 现状

- 首府目标：`TownGarrisonRule.TargetTotalCount`（玩家设，默认 150）× 风险乘数 → `DesiredTotal`。
- 非首府目标：`BranchRule.TargetPower`（玩家设）→ `DesiredPower`。
- `CapitalLogisticsManager` → `SupplyDemandGraph`（MCMF）把兵源路由去填这些目标。
- 财政自治 spec 计划加 `AffordabilityPlanner`（贪心瀑布）对目标**封顶**。

问题：目标是玩家逐城手填的固定数字。这既要求玩家懂行，又无法响应态势（繁荣度、威胁、金库），还让"每城一个数"无法做全局权衡 —— 富城和边境城堡争同一笔氏族预算时，没有机制做最优分配。

### 1.2 本设计改变什么

调度器的职权从"**怎么填缺口**"扩张到"**缺口该是多少**"。`TargetTotalCount` / `TargetPower` 不再是 auto 模式的输入；调度器从**氏族态势**派生每个定居点的目标：

- **可负担预算**（财政自治：税+关税派生的可持续收入 × 比例）—— 预算天花板。
- **威胁**（每定居点风险）—— 兵力往哪需要。
- **战略价值**（首府 / 繁荣度）—— 同等威胁下谁更值得守。
- **首府拥有者态势**（金库余额、是否交战、王国战线数）—— §5。

---

## 2. 调度器架构：两段式 MCMF

`CapitalLogisticsManager`（每受管氏族、每日一次）依次跑两段，两段都是**单商品** MCMF，复用现有 `MinCostFlow` 求解器：

```
Pass A  分配 MCMF (GarrisonAllocationSolver, 新增)
        商品 = 驻军头数(预算换算)。把氏族工资预算按"防御价值"分配到各城/堡。
        产出：每定居点目标头数(→ 工资、兵力随之派生)。
   ↓ 目标头数喂入
Pass B  路由 MCMF (SupplyDemandGraph, 现有)
        把 Pass A 定的目标用兵源(notable 志愿兵 / 驻军超额 / 征兵队)填满。
```

为什么分两段、而不是一张多商品图：分配（哪城养多大）与路由（兵从哪来）是不同商品（金币预算 vs 兵员）、不同时间尺度（稳态策略 vs 当日调动）。合成一张图就是多商品流（整数多商品流 NP-hard）。两段各自单商品、各自快。路由 MCMF **完全不动**（不引入预算节点 —— 见财政自治 spec §3.4 的否决理由）。

---

## 3. Pass A —— 分配 MCMF

### 3.1 图结构

商品 = **驻军头数**。预算换算成头数上限：`budgetCap = floor(clanWageBudget / wagePerTroop)`，其中 `clanWageBudget` 来自财政自治（`GarrisonWageBudgetRatio × 可持续收入` + 战时缓冲抽取），`wagePerTroop` 取保守满级单兵工资（财政自治 §3.3）。

| 节点 | 说明 |
|---|---|
| `superSource` / `superSink` | 流的起止 |
| `budgetNode` | 预算汇集点 |
| `unspentNode` | 未花掉的预算（零损失出口） |
| `S.tier[k]` | 每定居点 S 的价值层节点（见 §3.2） |

| 边 | 容量 | 费用 |
|---|---|---|
| `superSource → budgetNode` | `budgetCap` | 0 |
| `budgetNode → unspentNode` | ∞ | 0 |
| `unspentNode → superSink` | ∞ | 0 |
| `budgetNode → S.tier[k]` | 该层头数跨度 | `-round(value_k)`（见 §4）|
| `S.tier[k] → superSink` | 该层头数跨度 | 0 |

求解 `superSource→superSink` 的最小费用最大流。`budgetCap` 全部流出：价值层（负费用）按价值从高到低被填满，剩余流入 `unspentNode`（费用 0）。

**关键（advisor 修正）：surplus 层费用必须严格 > 0**（不是 0）。否则 surplus 层与 `unspentNode` 都是 0 费用，MCMF 无差别，可能无谓堆兵。surplus 层费用取小正数（如 +1/头），MCMF 就严格偏好"预算留着不花"而非过度驻军。

### 3.2 每定居点的价值层

每个定居点 S 的驻军头数轴切成 3 段（core 段再离散成 K 个子层让凸递减平滑）：

| 层 | 头数区间 | 边费用 |
|---|---|---|
| **floor** | `0 .. MinGarrisonFloor` | `-(FLOOR_BASE × threat(S) × strategic(S))`，`FLOOR_BASE` 大（默认 1000），保证任何 floor 都压过任何 core；预算极紧时 floor 之间仍按 threat×strategic 排序 |
| **core** | `MinGarrisonFloor .. adequate(S)`，离散成 K 个子层（默认 K=5） | 每子层 `-round(coreValue(slice))`，见 §4 |
| **surplus** | `adequate(S) .. hardCap(S)` | `+SURPLUS_COST`（小正数，默认 +1）—— MCMF 避开 |

- `hardCap(S)`：vanilla 驻军 `PartySizeLimit`，取不到则配置 `MaxGarrisonHardCap`（默认 400）。仅用于把图做有界。
- 图很小（§8），离散 K=5 已足够平滑。

---

## 4. 价值函数（设计核心）

> advisor：图结构是易事，整个"智能调度"的成败全在价值函数。这里给定具体公式与默认权重，全部 tunable。

第 k 个驻军（在定居点 S）的边际防御价值：

```
value(k, S):
  k ≤ MinGarrisonFloor          → FLOOR_BASE × threat(S) × strategic(S)        # floor，FLOOR_BASE=1000
  MinGarrisonFloor < k ≤ adequate(S)
                                → CORE_BASE × diminishing(k,S) × threat(S) × strategic(S)   # CORE_BASE=100
  k > adequate(S)               → surplus 层，边费用 = +SURPLUS_COST（不按 value）

diminishing(k,S): 在 [MinGarrisonFloor, adequate(S)] 上从 1.0 线性降到 0.2
                  （core 离散成 K 子层时，取每子层中点的 diminishing 值）

threat(S):    RiskAssessmentService.Assess(S).Level 映射
              Safe→0.5  Low→1.0  Medium→2.0  High→4.0  Critical→8.0

strategic(S): (S.IsCapital ? 1.3 : 1.0) × clamp(S.Prosperity / 4000, 0.5, 1.5)

adequate(S):  clamp( AdequateBase + Prosperity/AdequateProsperityDivisor + 威胁附加,
                     MinGarrisonFloor, hardCap(S) )
              默认 AdequateBase=60, AdequateProsperityDivisor=80;
              威胁附加 = round(NearbyLandThreatIntensity × AdequateThreatWeight)，默认 AdequateThreatWeight=8
```

说明：

- `threat` 的 Safe=0.5（非 0）—— 即使无威胁的城也给 core 一点价值，不会被分配彻底归零（floor 之上仍愿留少量）。
- `value` 为 float，作 MCMF 整数费用时 `-round(value)`；floor 用大常数压过全部 core。
- **城堡天然被照顾到（advisor 重点）**：城堡无税基、自身收入≈0，但边境城堡 `threat(S)` 高 → core 价值高 → 分配 MCMF 从**氏族池子预算**按价值给它一份。这正是贪心瀑布需要靠 `MinGarrisonFloor` + 氏族池子特判才能做到的事，分配 MCMF 自然做到 —— 这是 MCMF 胜过瀑布的最强论据。
- 已**故意从 v1 排除**的因子（advisor 点 2，防止 scope 膨胀）：`isBorderFief`（需邻接计算，且与 `threat` 部分重叠）、玩家主队位置折减（主队移动频繁 → 目标震荡 → 误触发遣散）。留 v2，需迟滞处理。

---

## 5. 首府拥有者态势输入

> advisor 点 2：锁定 v1 输入清单，别变垃圾桶。

v1 固定 5 项，分两类用途：

| 输入 | 用途 |
|---|---|
| 氏族可持续收入（税+关税） | 算 `clanWageBudget` 基数 |
| 氏族金库余额 | 战时缓冲抽取额（财政自治 §3.5） |
| 氏族是否交战 | 启用缓冲抽取；war 下 `clanWageBudget` 取 `max(常规预算, 全额配置)` |
| 每定居点风险 `RiskAssessmentService` | `threat(S)` |
| 每定居点繁荣度 | `strategic(S)` + `adequate(S)` |

王国战线数等更广的态势 v1 不接 —— `clanWageBudget` 已是氏族总额、对战线数不敏感即可。**已排除**：玩家主队位置（见 §4）。

---

## 6. Pass B —— 路由 MCMF（现有，几乎不动）

`SupplyDemandGraph` 保持现状。唯一改动：`BuildSettlementStates` 里每个定居点的目标头数来自 **Pass A 的输出**，不再来自 `ComputeDesiredTarget(rule, risk)`：

- 首府：`DesiredTotal` = Pass A 目标头数。
- 非首府：`DesiredPower` = `headsToPower(Pass A 目标头数)`（头数 × `GarrisonPowerEvaluator` 参考单兵 power）。
- `IsOtherOwnedBranch`（同氏族他人持有）节点不变 —— 调度器不管别人的城。

路由 MCMF 的兵源、费用、求解一律不动。

---

## 7. 手动模式与评估

配置开关 `AllowManualGarrisonTargets`（默认 **false**）。

### 7.1 auto 模式（默认）

- Pass A 决定一切。`TownGarrisonRule.TargetTotalCount` / `BranchRule.TargetPower` **不参与**驻军规模。
- 控制面板：这两个旋钮**隐藏 / 置灰**；改为展示调度器算出的目标 + "为什么是这个数"分解（floor/core 价值、threat、strategic、预算占用）。

### 7.2 manual 模式（开关开启）

- `TargetTotalCount` / `TargetPower` 重新生效，作为 **Pass B 的路由目标**。
- Pass A **仍然跑**，但只产出**评估**，不覆盖玩家。每定居点评估对象：

  ```
  GarrisonAssessment { settlement, playerTarget, recommendedTarget,
                       dailyWageDelta,           // (playerTarget - recommended) × wagePerTroop
                       loopClosesProjection }    // 沿用财政自治 §2 闭环核算
  ```

  例："你设 200，推荐 90，每天多付 ~880 工资，金库 N 天后转负。"

- **遣散超额在 manual 模式对该定居点禁用（advisor 点 4）**：玩家主动选择过度驻军，评估告诉他代价，调度器不和他对抗。auto 模式下遣散超额照常（财政自治 §3.4）。

评估在控制面板 + WebUI 的财务/评估视图展示（双端，memory `feedback_control_panel_dual_surface`）。

---

## 8. 性能

两段都是小图上的单商品 MCMF：

- **Pass A** 节点数 ≈ `4 + 定居点数 × (1 floor + K core + 1 surplus)`。一个 15 领地的氏族、K=5 → ≈ 4 + 15×7 ≈ **110 节点 / ~220 边**。
- **Pass B** 同量级（现有，已在跑）。
- 现有 `MinCostFlow`（SSP + Johnson 势 + self-test）解这种规模是微秒级。
- 每受管氏族每日 1 次。开 AI 接管时 N 个氏族 × 2 段，仍是每日总计几毫秒级。

性能非问题。图规模由 `hardCap(S)` 与固定 K 保证有界。

---

## 9. 对财政自治 spec / plan 的修订

本设计与已写的财政自治 spec/plan 有交叠，明确如下，避免实现时 Task 5 撞车：

| 财政自治条目 | 状态 |
|---|---|
| spec §3.1 金库 / §3.2 `STClanFinanceModel` / §3.5 战时缓冲 | **保留**，本设计依赖 |
| spec §3.3 `AffordabilityPlanner` 贪心瀑布 | **被取代** —— 换成本设计 Pass A 分配 MCMF |
| spec §2 闭环核算 | **仍有效** —— Pass A 输出须通过同一闭环检验（预算 = 比例×收入，按构造满足）|
| plan Task 1（配置）| 增补：加 `AllowManualGarrisonTargets` 开关 + §4 价值函数 tunables；`TargetTotalCount`/`TargetPower` 改为手动模式专属 |
| plan Task 5（`AffordabilityPlanner` + 集成）| **重写** —— 改为 `GarrisonAllocationSolver`（Pass A 分配 MCMF）+ 喂入 Pass B |
| plan Task 6（遣散超额）| 增补：阈值读 Pass A 输出；manual 模式禁用遣散 |
| plan Task 2/3/4、7/8/9 | **保留**（金库、模型、扣费改道、UI 去重/旋钮/验证）|

实现顺序上，本设计的 Pass A 取代财政自治 plan 的 Task 5；其余 Task 不变。建议把财政自治 plan 的 Task 5/6 按本 spec 重新出 plan。

---

## 10. 数据流与时序

```
ST 每日 tick → CapitalLogisticsManager.EvaluateAll → 每受管氏族:
  ├─ 收集态势(§5)：可持续收入、金库余额、是否交战、每领地风险/繁荣度
  ├─ Pass A: GarrisonAllocationSolver
  │    建分配图(§3) → MinCostFlow.Solve → 每定居点目标头数
  ├─ auto 模式: 目标头数直接喂 Pass B
  │  manual 模式: 目标头数 → GarrisonAssessment(§7.2); Pass B 用玩家 TargetTotalCount/Power
  ├─ Pass B: SupplyDemandGraph(现有) → 路由指令 → 执行招募/调拨
  └─ 遣散超额(auto 模式; 财政自治 §3.4)
```

`clanWageBudget` 现算（不依赖金库当天是否已结算），与财政自治 spec §4 时序说明一致。

---

## 11. 配置与控制面板

新增配置（挂 `GlobalConfig`，与财政自治 `FiscalAutonomyConfig` 并列或并入它）：

| 字段 | 默认 | 含义 |
|---|---|---|
| `AllowManualGarrisonTargets` | false | 是否允许玩家手设目标（§7）|
| `ValueCoreBase` | 100 | core 价值基数 |
| `ValueFloorBase` | 1000 | floor 价值基数 |
| `SurplusEdgeCost` | 1 | surplus 层边费用（严格正）|
| `AdequateBase` | 60 | `adequate(S)` 基数 |
| `AdequateProsperityDivisor` | 80 | 繁荣度对 `adequate` 的贡献除数 |
| `AdequateThreatWeight` | 8 | 威胁强度对 `adequate` 的权重 |
| `CoreTierСount` (K) | 5 | core 段离散子层数 |
| `MaxGarrisonHardCap` | 400 | 取不到 vanilla `PartySizeLimit` 时的兜底硬上限 |

- `threat` 映射表（Safe..Critical → 0.5..8）可作为高级项暴露或先固定。
- 控制面板：auto 模式隐藏 `TargetTotalCount`/`TargetPower`，展示调度器目标 + 分解；manual 模式显示评估。双端同源（依赖财政自治 plan Task 7 的单一 spec 真源）。

## 12. 风险与范围外

- **价值权重需实战调参**：§4 默认值是初值，须靠启动游戏看日志（CLAUDE.md 无单测）校准 —— 调度器日志应打印每定居点的 floor/core 价值、threat、strategic、最终目标。
- **范围外**：`isBorderFief`、玩家主队位置折减（§4，留 v2）；跨氏族调度（仍每氏族一个调度器）；工坊/商队收入（财政自治候选功能 D）。
- **风险**：`adequate(S)` / `hardCap(S)` 若配置成 `adequate > hardCap` 会让 core 段为空 —— 校验须保证 `MinGarrisonFloor ≤ adequate ≤ hardCap`。
- manual 模式下玩家可设到 `hardCap` 以上 —— Pass B 路由目标应再被 `hardCap` 钳一次，避免无界。
