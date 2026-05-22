# 方案2:双层 MCMF 合并 — 交接文档 (2026-05-22)

> 本文是这次"双层 MCMF 合并"大重构的**唯一权威文档**,写给没有上下文的接手者。
> 涵盖:重构目的、当前实现状态、合并 solver 工作原理、配置、**测试清单**、
> 与原设计的偏差、未完成工作。读完即可独立接手。
> 旧的 `mcmf-merge-design.md` / `mcmf-merge-feasibility.md` / `phase*.md` 等已删除
> (内容并入本文或属已完成的历史,git history 仍可追溯)。

---

## 1. 这是什么

SovereignTowns 的驻军调度原本是**两层 MCMF**:

- **Pass A** = `GarrisonAllocationSolver` —— 按氏族工资预算 + 价值函数,跨城分配
  每城目标头数 `Target`。
- **Pass B** = `SupplyDemandGraph` —— 拿 Pass A 的 `Target` 当 demand,路由兵员
  (招募 / 跨城调拨)。

两层是**单向单次交接**:Pass A 解完把 `Target` 喂给 Pass B,结果永不回流。缺陷:
Pass A 分配时对"这个目标在路由上可不可行 / 划不划算"完全瞎 —— 它可能把目标分给
一座没有兵源、招募成本极高的城。

**方案2** = 把 Pass A 与 Pass B 合并成**单一 `MinCostFlow` 图、单次求解**,让
allocation 与 routing 联合求全局最优。新 solver = `UnifiedGarrisonSolver`。

合并按里程碑 M1–M6 分阶段落地,**parallel-run**(新旧并跑、可只比对不派发)是
核心安全网。

---

## 2. 当前状态(读这一节就知道进度)

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M1 | 合并 solver 骨架:建图 + Solve + 按边类别统计;parallel-run 三态 | ✅ 已提交 `b756bce` |
| M2 | decode → Target / 指令 / 每城遣散 | ✅ 代码完成,**未提交** |
| M3 | value 用独立 `Merged*` 常数(routing-可比标度) | ✅ 代码完成,**未提交** |
| M4 | 两段 bypass(每 tick 遣散速率限制) | ✅ 代码完成,**未提交** |
| M6-派发 | `MergedOnly` 自动模式真派发接线 | ✅ 代码完成,**未提交** |
| #1 | 在飞 inbound 扣减 demand-tier 容量 | ✅ 代码完成,**未提交** |
| #2 | 排除"他人持有的同氏族分支" | ✅ 代码完成,**未提交** |
| M5 | manual 模式合并 solver 影子接线(手动目标作 demand 容量) | ✅ 代码完成,**未提交** |
| M6-切换 | 翻默认到 `MergedOnly`、删 legacy 路由/遣散代码 | ⏳ 未做(见 §9) |
| EWMA | value 输入指数平滑(目标稳定性) | ⏳ 未做(见 §9) |

**Build**:`dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug` —— 0 errors,
57 warnings(基线,无新增)。**未做 in-game 验证**(见 §7 测试清单)。

**未提交改动**(4 个文件,`git diff HEAD`):
- `src/Algorithm/UnifiedGarrisonSolver.cs`
- `src/Configuration/FiscalAutonomyConfig.cs`
- `src/Managers/CapitalLogisticsManager.cs`
- `audits/mcmf-merge-design.md`(旧设计文档 —— 本文落地后应连同删除)

> ⚠️ **提交规则**:用户要求"提交动作需用户明确要求"。**未经用户明示不要 commit。**

---

## 3. 合并 solver 工作原理(`UnifiedGarrisonSolver.Solve`)

单商品、头数货币、单次 `MinCostFlow.Solve`。入口:
`UnifiedGarrisonSolver.Solve(CapitalManager manager, Settlement capitalSettlement,
GarrisonAllocationResult? passA)`。`passA` 非 null 时复用其预算 / 头数上限,避免双算。

### 3.1 图结构

**固定节点**:`superSource`、`superSink`、`budgetGate`、`bypass`、`bypassOverflow`。

**transit 节点**:每首府每 role 一个(`{Cav, HA, Inf, Rng}` 共 4 个)。招募兵的
中转点 —— 招募兵必先抵首府再决定留首府 / 转发分支。**按 role 拆分是必需的**:
role-blind 的单 transit 会让 Cav 招募兵填进 Inf 缺口。

**demand-tier 节点**:每 (城, role, tier)。
- 首府:role ∈ 4 种;tier ∈ {floor, core-1..K, surplus}。每 role 容量经
  `MatchPolicy.DesiredCount` 按首府规则比例拆。
- 分支:单一占位 role(`Infantry`),头数口径;tier 同上。
- tier 容量:`floor = MinGarrisonFloor`;core 段 = `adequate − floor` 按
  `CoreTierCount` 整数等分;surplus 段 = `hardCap − adequate`。
  (`floor` / `adequate` / `hardCap` 来自 `GarrisonAllocationSolver` 的同名 helper。)

**origin 节点**:
- 现有驻军源:每 (城, role, bucket),来自 `MatchPolicy.Bucketize(驻军 roster)`。
- 招募源:InPlace(首府 notable 志愿兵)、Village(每候选村每 role,候选村由
  `SupplyDemandGraph.EnumerateRecruitmentVillages` 枚举)。

**disbandGate 节点**:每个**非保护城**一个(懒创建,见 §3.4)。

### 3.2 费用模型(关键)

`K = 20_000_000` 是费用偏移 —— `MinCostFlow` 拒绝负费用,故所有"入 demand"的边
费用写成 `K − value`。全程整数运算,K 大小不影响 routing-vs-value 比较的正确性。

tier value(无量纲乘子 `threat` / `strat` / `dim` 不参与重定标):
- floor tier:`MergedValueFloorBase × threat × strat`
- core-k tier:`MergedValueCoreBase × dim × threat × strat`(`dim` 随 k 递减)
- surplus tier:`−max(1, MergedSurplusEdgeCost)`(**负值** —— surplus 留驻略亏本)

边费用:

| 边 | 费用 |
|---|---|
| 现有驻军 origin → **本城** demand-tier(Stay) | `K − value` |
| 现有驻军 origin → **他城** demand-tier(Transfer) | `routing + K − value` |
| 现有驻军 origin → 本城 disbandGate | `K` |
| disbandGate → bypass(正常段) | `0`(总遣散费 = K) |
| disbandGate → bypassOverflow(溢出段) | `MergedBypassOverflowPenalty`(总费 = K + 罚分) |
| 招募 origin → transit(其首府) | `routing`(Village:`距离+McmfRecruiterOverhead`;InPlace:`0`) |
| 招募 origin → bypass | `K` |
| transit → 本首府 demand-tier | `K − value` |
| transit → 分支 demand-tier(Transfer) | `routing + K − value` |
| demand-tier → budgetGate / budgetGate → superSink / bypass(Overflow) → superSink | `0` |

`routing` = `RoutingDistance(地图直线距离) + overhead`。

**核心判据**:一个兵驻进某 demand 的路径总费用 = `routing + K − value`;**驻进
iff 总费用 < K ⇔ value > 总 routing**。预算紧张时 `budgetGate` 容量
(`budgetTroopCap`)成为瓶颈,solver 保留 `value − routing` 最高的兵,其余进
bypass。**disband 与预算配给都由此涌现** —— 没有独立的 disband 规则。

### 3.3 decode 契约(`UnifiedSolverResult`)

每条边一一对应至多一个动作,无复合边:

| 产出 | 来源 |
|---|---|
| `Target[城]` 每城目标头数 | Σ 流入该城所有 demand-tier(读 `dt→budgetGate` 流);每 fief 预播种 0 |
| `RecruiterPartyInstruction` | Village `origin→transit` 流 |
| `InPlaceRecruitInstruction` | InPlace `origin→transit` 流 |
| `TransferPartyInstruction` | 现有驻军 `origin→他城tier` 流 + `transit→分支tier` 流(按 (源,目标,role) 聚合) |
| `Disband[城]` 每城遣散头数 | 现有驻军 `origin→disbandGate` 流 |
| 统计 | `TotalFlow / TotalCost / DemandFilled / Stay/Transfer/Recruit/BypassFlow` 等 |

### 3.4 M4 两段 bypass(遣散速率限制)

遣散销毁真兵不可逆,必须限速。**编码为图约束**(不是 decode 截断 —— 截断会让
Target 与实际派发不符,下 tick 拿错状态重算 → 颤动)。

每个非保护城一个 `disbandGate` 节点:
- **正常段** `disbandGate→bypass`:容量 = `MergedDisbandPerDayCap` 按 `CapitalLogisticsTickHours` 折算出的每-tick 上限(`round(perDay × tickHours / 24)`),费用 0。
- **溢出段** `disbandGate→bypassOverflow`:容量足量大,费用 = `MergedBypassOverflowPenalty`。

费用排序(同一 surplus 兵的去向):`正常遣散 K` < `surplus 留驻 K+surplusEdgeCost`
< `溢出遣散 K+penalty`。后果:
- solver **优先**用正常段遣散 surplus 兵(K < K+1)。
- 正常段(每 tick 上限)耗尽后,solver 转而让兵**留在 surplus 层**(K+1 < K+1000),
  下 tick 再遣散 → **超额遣散被限制在 `MergedDisbandPerDayCap` 折算出的每-tick 上限**。
- 溢出段只在兵**物理塞不下**(demand-tier 全满 + 正常段满,即现有兵 ≫ hardCap)
  时启用 —— 保证 max-flow 恒可行,不会因无处可去而非最大流。

`MergedDisbandPerDayCap = 0` 是合法配置 → 无正常段 → 只在溢出(塞不下)时遣散,
即"绝不为预算主动遣散"。

### 3.5 保护态(siege / 高危不被遣散)

`IsProtectedFromDisband(城) = !DisbandUnaffordableExcess || AllowManualGarrisonTargets
|| IsUnderSiege || risk≥High`(`AllowManualGarrisonTargets` 项见 §3.8 —— manual 模式
全城保护,镜像 legacy `DisbandExcessGarrisons` Gate 2)。
保护态城的现有驻军 origin **不连 disbandGate** → 永不被遣散。

围城隔离另有 4 处检查:① 围城中的城的驻军整体不入图(不作 origin);② 不向围城
中的城调兵;③ 围城中的首府不招募;④ transit 不向围城中的分支转发。

### 3.6 #1 在飞 inbound 扣减

`CollectInFlightInbound(clan)` 扫 `MobileParty.AllCustomParties`,把本氏族在飞
ST 队(`StTransferPartyComponent` / `StRecruiterPartyComponent` /
`StSallyPartyComponent`)的头数按目标定居点汇总。Transfer 回程(目标==源)记到源城,
否则记到目的城;Recruiter/Sally 记到各自 home。

建图时按 **floor-first** 削减该城 demand-tier 容量 —— 在途兵将填满高优先 tier,
solver 只补其上方缺口,避免对已在途运量重复下指令。**role-blind 近似**(汇总总
头数,不区分兵种)—— M-stage 可接受。

### 3.7 #2 排除他人持有的分支

`towns` 列表排除"`Settlement.Owner != capital.Owner`(且两者非 null)的非首府"
—— 即同氏族其他领主(封臣)持有的独立封地。这类城**既不建 demand 也不建 origin**。

> ⚠️ legacy `SupplyDemandGraph` 对这类分支仍保留为 Garrison surplus 抽兵源;
> 合并 solver 取**全排除**的简化口径,代价是不再从他人持有分支抽超额兵。单领主
> 氏族(典型玩家氏族)永不触发。**改动此处前勿"只建 origin 不建 demand"** ——
> 那会让 solver 把同氏族封臣的驻军纳入 disbandGate 遣散候选,等于擅自遣散他人部队。

### 3.8 M5 manual 模式(玩家手动目标驱动)

`AllowManualGarrisonTargets = true` 时合并 solver 仍照常建图 / 求解,仅两处改动:

- **demand 容量**:每城 demand-tier 的 `adequate` 与 `hardCap` 都改成玩家手动目标
  `UnifiedGarrisonSolver.ComputeManualTarget`(城镇 = `TargetTotalCount × 风险乘子`;
  城堡 = `BranchRule.TargetPower` 当头数;均 clamp `MaxGarrisonHardCap`)。`floor` 收窄到
  ≤ 手动目标。后果:`surplusSpan = hardCap − adequate = 0` → 无 surplus 层,solver
  路由上限 = 玩家目标。**value 函数 / budgetGate / routing 口径全不变** —— "手动目标"
  只改 demand **容量**,不改值域。

  > ⚠️ **castle `TargetPower` 单位差异**:`ComputeManualTarget` 把 `BranchRule.TargetPower`
  > 直接当头数(与控制面板 `StashAssessments` 一致),而 legacy Pass B
  > `SupplyDemandGraph.BuildSettlementStates` 视作 power 单位。两者数量级差 ~3×
  > (平均 tier3 perTroopPower≈3-4)。M5 影子期 `MERGED-DIFF mode=manual` 的 castle
  > Δtarget 会反映该差异;**M6 切换前若沿用旧 `TargetPower` 设置直接翻 MergedOnly,
  > castle 驻军会膨胀 ~3×** —— M6 需重新选值,或经 UI 把 `BranchRule` 字段改名 / 改语义为头数。

- **遣散关闭**:`IsProtectedFromDisband` 在 manual 模式对全城返回 true(见 §3.5)→
  现有驻军 origin 不连 disbandGate → 永不遣散(镜像 legacy `DisbandExcessGarrisons`
  Gate 2)。超过手动目标的现有驻军无出边 → 0 流留驻(min-cost-**max**-flow 不强制
  发送全部供给,留驻合法、不破坏可行性)。

manual 模式下合并 solver 只走影子路径(`RunMergedShadow`,`ShadowMerged` 与
`MergedOnly` 均经此)——**不派发**,legacy 仍权威。`ComputeManualTarget` 与控制面板
`CapitalLogisticsManager.StashAssessments` 展示的"玩家目标"共用同一口径(单一来源,
否则面板展示值与 solver 实采容量乖离)。

---

## 4. parallel-run 三态(`MergedSolverMode`)

`FiscalAutonomyConfig.MergedSolverMode` 枚举(Newtonsoft 序列化为**整数**):

| 值 | 名 | 行为 |
|---|---|---|
| `0` | `LegacyOnly` | 仅跑旧两层、派发旧结果(= 合并前行为)。合并 solver 完全不跑。 |
| `1` | `ShadowMerged` | 旧两层照常派发;**额外**跑合并 solver,只记 `MERGED-SHADOW` / `MERGED-DIFF` 差异日志,**不派发**。 |
| `2` | `MergedOnly` | **自动模式**:合并 solver 权威派发(`MERGED-DISPATCH` 日志),跳过 legacy 路由 + 遣散。**manual 模式**:legacy 权威派发 + 合并 solver 影子运行(M5 —— 玩家手动目标作 demand 容量,见 §3.8)。 |

派发分流在 `CapitalLogisticsManager.EvaluateClan`:`MergedOnly && !manualMode`
→ `RunMergedDispatch`(成功则 `return`,跳过 legacy);其余 → legacy 派发 +
`RunMergedShadow`。`MergedOnly` 下合并 solver 跑不成(无 fief / 首府不符)会
**回退 legacy**,保证该 tick 仍有调度。

**当前 `global.json` 设的是 `1`(ShadowMerged)。**

---

## 5. 配置(`FiscalAutonomyConfig` 的 `Merged*` 字段)

均在 `global.json` 的 `FiscalAutonomy` 段。新增字段缺失时 Newtonsoft 用 C# 默认值
(pre-release 不需要 ConfigVersion 迁移)。

| 字段 | C# 默认 | 含义 |
|---|---|---|
| `MergedSolverMode` | `0` LegacyOnly | parallel-run 三态(见 §4)。**当前 global.json = `1`**。 |
| `MergedValueFloorBase` | `3000` | floor tier 单兵 value 基常数(routing-可比标度)。**当前 global.json = `3000`**。 |
| `MergedValueCoreBase` | `800` | core tier 单兵 value 基常数。**当前 global.json = `1100`**(M3 playtest 调过)。 |
| `MergedSurplusEdgeCost` | `1` | surplus tier value = `−此值`。符号 = disband 行为开关:`>0` 时改逻辑会让 surplus 兵留下。 |
| `MergedDisbandPerDayCap` | `20` | 每城每【天】经正常段遣散的头数上限;solver 按 `CapitalLogisticsTickHours` 折算成每-tick 上限。`0` = 关正常段(只溢出遣散)。 |
| `MergedBypassOverflowPenalty` | `1000` | 溢出段附加费用。**须 > `MergedSurplusEdgeCost`**,否则两段 bypass 失效。 |
| `CapitalLogisticsTickHours` | `6` | 首府后勤评估间隔(小时),clamp [1,24]。「一个 tick = 多久」由它定。见 audits/2026-05-22-p3-lookahead-design.md。 |
| `DispatchRiskEnabled` | `true` | 派发风险否决总开关(Part D)。`false` = 一键回退。 |
| `DispatchRiskScanRadius` | `30` | `HostilePartyScanner` 扫描半径(地图单位)。须 in-game 调。 |
| `DispatchRiskVetoThreshold` | `60` | D1 否决阈值:路线敌对健康兵力 ≥ 此值 → 本 tick 不派征兵/调拨。须 in-game 调。 |
| `DispatchRiskCostScale` | `10` | D2 路线风险→成本 标度乘子。须 in-game 调。 |

> `Merged*` 与 Pass A 的同名 `Value*` 常数**独立** —— 调合并 solver 标度不扰动
> legacy Pass A。`Merged*` 标度必须与 routing(村→首府距离+overhead,约数百~千)
> 同量级,否则 core/surplus 招募恒亏本 → solver 欠驻军。

`Merged*` 旋钮目前**未接入控制面板 / WebUI**(整个合并 solver 仍是 pre-release、
behind `MergedSolverMode`)。用户直接改 `global.json`。切到 `MergedOnly` 终态时
应补上双端 UI(用户标准要求"控制面板功能两端同步")。

---

## 6. 文件地图

| 文件 | 角色 |
|---|---|
| `src/Algorithm/UnifiedGarrisonSolver.cs` | **合并 solver 本体**。`Solve()` + `UnifiedSolverResult`。建图 / Solve / decode 全在此。 |
| `src/Algorithm/MinCostFlow.cs` | MCMF 引擎(SSP + Dijkstra + Johnson)。**直接复用,未改**。拒负费用、cap≤0 边跳过。 |
| `src/Algorithm/GarrisonAllocationSolver.cs` | legacy Pass A。M1 把 `ClanWageBudget`/`HardCapFor`/`AdequateFor`/`WagePerTroopAtMaxTier` 改 `internal` 供合并 solver 复用。manual 模式下它仍提供 `GarrisonAssessment` 的推荐值(passA)。 |
| `src/Algorithm/SupplyDemandGraph.cs` | legacy Pass B。M1 把 `EnumerateRecruitmentVillages`/`EnumerateVolunteerTroops`/`CollectInFlightRecruiterVillages`/`BucketizeCharacters` 改 `internal` 供合并 solver 复用。`MergedOnly` 终态删。 |
| `src/Managers/CapitalLogisticsManager.cs` | 日调度入口。`EvaluateClan` 分流 legacy / merged;`RunMergedShadow`(影子)/ `RunMergedDispatch`(真派发)/ `ExecuteMergedInstructions` / `ExecuteMergedDisband`;`ExecuteInstructionList` / `DisbandFromGarrison` 为 legacy 与 merged 共用。 |
| `src/Configuration/FiscalAutonomyConfig.cs` | 配置 DTO,含 `MergedSolverMode` 枚举 + 全部 `Merged*` 字段。 |

调用链:`SovereignTownsCampaignBehavior.OnDailyTick` → `CapitalLogisticsManager.EvaluateAll`
→ 每 clan `EvaluateClan` → `RunPassA` → narrate/stash → `RunMcmf`(legacy Pass B)
→ 按 `MergedSolverMode` 分流派发。

---

## 7. 测试清单(in-game,未做)

进游戏后日志在 `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\`。

### Phase 1 — ShadowMerged 回归(`MergedSolverMode = 1`,即当前)
1. 跑数游戏日,确认 legacy 仍正常派发(征兵队/调拨/遣散照旧)。
2. 确认 `MERGED-SHADOW` + `MERGED-DIFF` 日志照常打印,无异常 / 无 `RunMergedShadow failed`。
3. 注意:#1 在飞 inbound 现在会削减 merged tier → 有在飞队的城 `merged.Target`
   会比 M3 时略低;M4 两段 bypass 使 `MERGED-DIFF merged disband` 受速率限制。
   这两项差异是预期的,不是 bug。

### Phase 2 — MergedOnly 切换(把 `global.json` 的 `MergedSolverMode` 改成 `2`)
合并 solver 接管派发。重点看日志:
4. `MERGED-DISPATCH ...` 取代 `MERGED-SHADOW`;`CapitalLogistics MERGED execution:
   accepted=.. skipped=.. unmet=..`。
5. 征兵队 / 调拨是否真按 merged 指令生成(对照 `MERGED-DISPATCH` 的指令摘要)。
6. **`MERGED-DISPATCH disband` 行 —— 预期每个非保护城每天遣散 ≈`MergedDisbandPerDayCap`**
   (默认 20;6h tick 下每 tick ≈5),只要 surplus 层还有兵。**这是设计行为**:合并
   solver **没有** legacy 的"超 `effectiveTarget × 1.2` 阈值才遣散"门 —— 每 tick
   都会从 surplus 段削到速率上限。若觉得太激进 → 调小 `MergedDisbandPerDayCap`
   或设 `0`(只在塞不下 hardCap 时遣散)。
7. 确认**不再出现** legacy `DisbandExcessGarrisons` 日志(MergedOnly+auto 跳过它)。
8. 几游戏日后,各城驻军头数应收敛到 `merged.Target`。

### 遣散速率验证
9. 找 / 造一座超额驻军的城,确认 MergedOnly 下每 tick 掉 ~20 而非一次清空。
   某城远超 hardCap 时单 tick 可 >20(溢出段,针对物理塞不下的兵,符合预期)。

### 保护态验证
10. 围城中或 risk≥High 的城在 MergedOnly 下也不应被遣散。
11. 若 solve 后风险上升,应见 `MERGED-DISPATCH disband deferred (risk≥High)` /
    `(under siege)` 日志(派发前重查门限,关 solve→dispatch 竞态)。

### 已知盲区 — 多城存档
12. 当前 playtest 存档是**单城玩家氏族** → 跨城调拨(`transit→分支`、现有驻军
    跨城)与 #2 他人持有分支过滤**从未被实跑**。要测调拨需一个 ≥2 城的氏族:
    确认日志出现 `T:源>目标` 调拨指令、`MERGED-DISPATCH` 的 transfer 计数 >0。

### manual 模式验证(M5)
13. 把 `AllowManualGarrisonTargets` 设 true(任意 `MergedSolverMode ≥ 1`):确认
    `MERGED-SHADOW` + `MERGED-DIFF mode=manual` 日志照常打印 —— manual 模式不再跳过
    影子运行。`MERGED-DIFF merged disband` 行**不应出现**(manual 全城保护,见 §3.8)。
    legacy 仍权威派发(manual 下 merged 始终影子)。

### 回滚
14. 任何异常,把 `MergedSolverMode` 改回 `0`(LegacyOnly)即完全恢复合并前行为。

---

## 8. 与原设计的偏差 / 已知近似

- **EWMA 未做**:原设计 §5 的 value 输入指数平滑(目标稳定性"治本")**未实现**。
  当前靠"现有驻军留本城 routing≈0、招新兵 routing>0"的天然迟滞 + #1 在飞粘性防颤。
  若 MergedOnly 下观察到目标 tick 间抖动 → 补 EWMA。
- **legacy 未删**:原设计 M6 要删 `DisbandExcessGarrisons` / legacy 路由 / stockpile。
  当前**全保留**作 fallback —— merged 派发路径 0 runtime,删 legacy 须等 Phase 2
  测试通过。
- **#1 role-blind**:在飞 inbound 不区分兵种(见 §3.6)。
- **#2 全排除**:他人持有分支被全排除,而 legacy 还会抽其 surplus(见 §3.7)。
- **遣散更激进**:merged 无"1.2× 阈值"门,每 tick 从 surplus 削到速率上限(见 §7.6)。
- **`wagePerTroop` 近似**:取首府满级 tier 单值(沿用 legacy,可接受)。

---

## 9. 未完成工作

**接 Phase 2 测试之后**:
- **M6 切换**:ShadowMerged 跑满 ≥1 游戏内季度、差异稳定可解释后,翻默认到
  `MergedOnly`,删 `DisbandExcessGarrisons` / legacy 路由 / stockpile 代码;
  `Merged*` 旋钮补双端 UI。

**其他 deferred**:
- **EWMA**:见 §8。
- **Phase 2 — Prisoner 接入 MCMF**:`PrisonerConvertInstruction` +
  `ExecuteMcmfInstruction` switch case 已留钩子;需加 prisoner 兵源枚举 + 执行器。
  合并 solver 侧需加 prisoner origin。
- **manual-knob 回退口径(P2,暂不修)**:`SupplyDemandGraph.BuildSettlementStates`
  在 settlement 缺席 Pass A 结果时的 auto-mode 回退仍调 manual 旋钮口径
  (`ComputeDesiredTarget` / `branch.TargetPower`)。P0 修复后该回退近乎死代码,
  advisor 同意推迟。若日志出现 settlement 缺席 Pass A,再统一到 `adequate` 口径。
- **Phase 3 — 时间展开 MCMF(滚动时域 MPC)**:把合并图沿时间轴展开 T 个 tick
  副本 + 跨时间边(留存边 / 在飞边),一次 Solve 解 T-tick、只执行 tick 0,即
  receding-horizon MPC。图规模随 T **线性**(非指数),min-cost-flow 多项式,
  T=7~14 在 daily tick 预算内。买到真前瞻(提前为可预测威胁招兵);买不到真
  随机性(吃确定性预测,随机需场景树)。真瓶颈是 threat/prosperity/budget 的
  T-tick 预测质量。前置依赖已预留:合并 solver 建图逻辑应可参数化时域
  (`BuildGraphForTick(state, tick)`),叠 T 份即可,不重写。
  > 表格 / 精确 MDP 不可行:N 城 × 驻军组成 × 在飞队 × 经济的联合状态空间天文级。
  > 时间展开流网络从不枚举状态组合 —— 这正是它能做而表格 MDP 不能做之处。

---

## 10. 硬约束提醒(改这块代码前必读)

- `MinCostFlow` **拒绝负费用** —— 任何"入 demand"边费用必须 `K − value ≥ 0`;
  `K=20M` 远高于任何 value。`cap ≤ 0` 的边被静默跳过。
- 每个事件入口(`EvaluateClan` 等)body 包 `try/catch` —— 异常绝不外逃进 vanilla。
- 合并 solver 是**纯只读**:只读游戏状态 + 产 `UnifiedSolverResult`,绝不派发 /
  改状态。派发只在 `CapitalLogisticsManager` 的 `Execute*` 方法。
- `RunMergedShadow` 严格只读,绝不调 Narrate/Stash/dispatcher(legacy 已在其调用前
  全套跑完,重复 stash 会 double-stash)。
- 提交走 master,但**提交动作需用户明确要求**。
