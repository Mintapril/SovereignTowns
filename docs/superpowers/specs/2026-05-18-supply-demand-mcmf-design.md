# 招兵 / 调兵 MCMF 统一调度重构

**Date**: 2026-05-18
**Status**: Design approved, ready for implementation
**Predecessor**: 本次重构在 [2026-05-17 StPartyComponent 实例化重构](2026-05-17-stpartycomponent-instance-refactor-design.md) 之上进行，不修改 Component 层

---

## 1. 背景与目标

### 1.1 现状

mod 的核心功能 = **管理多个 settlement 的兵力组成**，涉及四个动作：

| 动作 | 当前实现 | 行数 |
|------|---------|-----|
| 在首府原地招兵（notable 兑换） | `CapitalInPlaceRecruiter` | — |
| 派征兵队跨村招兵 | `RecruitmentPlanner` (289) + `RecruitmentDispatcher` (226) | 515 |
| 监狱招俘 | `PrisonerRecruitmentManager` (252) | 252 |
| friendly settlement 间调兵 | `TransferDispatcher` (131) | 131 |
| **以上全部的中央协调** | `CapitalLogisticsManager` (489) | 489 |
| **以上全部的兵种匹配评分** | `TroopTemplateMatcher` (231) | 231 |
| 升级 (只在首府发生) | `GarrisonXpInjector` (218) + `TroopUpgradeService` (252) | 470 |

总计 **~2100 行** 算法代码，决策逻辑高度分散。

### 1.2 当前问题（用户痛点）

1. **同一类决策被切碎到 3-4 处**：
   - "该不该招兵" 分散在 `RecruitmentPlanner` / `RecruitmentDispatcher` / `CapitalLogisticsManager.CoordinateRecruitment` / `CapitalInPlaceRecruiter`（4 处）
   - "该不该升级" 分散在 `GarrisonXpInjector` / `CapitalLogisticsManager.TryUpgradeCapital` / `TroopUpgradeService`（3 处）
   - "从谁调到谁" 分散在 `CapitalLogisticsManager.FindBestSource` / `TransferDispatcher` / `CapitalLogisticsManager` priority 计算（3 处）

2. **CapitalLogisticsManager 是一个 489 行的星形枢纽**，做 5 件事：BuildNodes → AccountInFlightParties → TryUpgradeCapital → CoordinateRecruitment → DispatchTransfers。`DispatchTransfers` 含三层嵌套循环（demands × sources × 动态容量）。

3. **多维评分爆炸**：村庄筛选 3 维（兵源 / 距离 / 风险）、调兵 3 维（距离 / 容量 / 惩罚）、升级 N 维。八个权重常数分散在多处，无法全局调优。

4. **「等升完才调」的玩家体感问题**：当前 `demand` 算法把 rule 当**硬约束** — 首府里 tier 没达标的兵不算可调 supply。结果 castle 长期空虚，玩家以为 mod 不工作。等首府兵全升完才一次调拨，效率低。

### 1.3 重构目标

- 决策点从 **8+ 处收敛到 1 次 MCMF Solve**
- 算法代码量从 **~2100 行降到 ~700 行**（-66%）
- castle **第 1 天就开始收兵**，不再等升级
- 保持「首府是决策与升级中心」语义不动

---

## 2. 设计决策（已与用户对齐）

| # | 决策点 | 选定方案 |
|---|---|---|
| Q1 | 算法选型 | **MCMF（最小费用最大流）**，独立算法库 |
| Q2 | 招兵与调兵合并 | **合并到同一次 MCMF Solve**（四种 source 统一抽象） |
| Q3 | 兵种粒度 | **A2 — 按 role 分桶**（infantry / archer / cavalry，3 桶）；不再细到 exact stringId 维度 |
| Q4 | exact template 模式去留 | **保留**，但只用于 `matchPenalty` 函数中的 lookup（判断 bucket 是否在 template 的升级树上） |
| Q5 | 升级路径 | **保持独立 hourly tick，只在首府升级**。`GarrisonXpInjector` 不动。`CapitalLogisticsManager.TryUpgradeCapital` 删除（不再由 daily 调度触发） |
| Q6 | 调兵物理路径 | **任意 friendly settlement 间均可**（"首府中心"= 决策中心，不是物理起点）。与现有 `DispatchTransfers` 行为一致 |
| Q7 | recruiter 派遣聚合 | **一个 town 一个 recruiter**：`VillageNotableSource(town)` 节点 capacity 聚合该 town 下所有村庄 notable，派遣时 recruiter 跑该 town 的所有村庄（不跨 town） |
| Q8 | 软化 rule 的权重 | **进 `GlobalConfig.Thresholds`** 作为可调参数：`W_TIER`、`W_HARD`、`LENIENCY`、`U_UNMET`、`RecruiterOverhead`、`TransferOverhead` |
| Q9 | dispatcher 校验失败处理 | **静默 skip，下个 daily tick 重算**。不做熔断（YAGNI，后续按需加） |
| Q10 | 迭代提升机制 | **castle 现有兵也是 source**（同 settlement 既是 demand 又是 source）。MCMF 自然在 capital 有更优兵时把 castle 的"将就兵"流回首府升级 |
| Q11 | **已知 trade-off** | 「只能在首府升级」约束下，castle 收到的低 tier 兵不会就地升级；只能靠迭代提升机制（capital 有更好兵时反向替换）。用户已确认接受 |

---

## 3. 核心抽象：供需匹配

**关键洞察**：招兵 / 调兵 / 俘虏招募 / 在地招募，物理本质都是同一件事 — **让某个 settlement 的兵力靠近 desired**。它们的区别只是"供给的来源不同"：

| 供给类型 | 容量 | 运输成本 | 可达性 |
|---------|-----|---------|-------|
| 同 settlement notable（in-place） | 小，看本地 notable | 0 | 仅本 settlement |
| 同 town 范围村庄 notable（recruiter party） | 大 | distance + RecruiterOverhead | 任意 friendly settlement |
| 同 settlement 监狱（prisoner） | 看俘虏数 + dailyConformity | 0 | **仅本 settlement** |
| friendly settlement 已有兵（transfer） | 看 surplus | distance + TransferOverhead | 任意 friendly settlement |

把全部四种当作 MCMF 图里的「带容量的 source 节点」，cost 函数表达"运输代价 + 不匹配惩罚 + 紧急度衰减"，则**一次 MCMF Solve 同时拍板四种动作**。

---

## 4. 算法选型：MCMF

### 4.1 为什么 MCMF

- **全局最优**：数学保证总 cost 最小（distance + 不匹配惩罚加权）
- **逻辑集中**：算法外部接口仅 `Solve(sources, demands, edges)`，所有策略编码在 cost 函数里
- **规模友好**：clan 顶配 ~20-30 节点，跑 < 1ms
- **stateless**：每天构图 + Solve，无快照状态机问题；dispatcher 失败下一轮自然重算
- **解耦**：MCMF 算法本身一行不改，调优全在 cost 函数里

### 4.2 算法库设计

新增独立文件 `src/Algorithm/MinCostFlow.cs`（~120 行，可参考 SPFA + 增广路径教科书实现）：

```csharp
public sealed class MinCostFlow
{
    public void AddNode(int id);
    public void AddEdge(int from, int to, int capacity, int cost);
    // 返回每条边的流量；总流量自动 maximize 且总 cost minimize
    public Dictionary<(int from, int to), int> Solve(int source, int sink);
}
```

**不依赖任何游戏类**。完全可单测（用纯算例验证最优性）。

### 4.3 GraphBuilder（图构造层）

新增 `src/Algorithm/SupplyDemandGraph.cs`（~150 行），职责：
1. 收集 clan 所有 friendly settlement
2. 为每个 settlement 计算 demand bucket 与 surplus bucket（按 role × tier 分桶，详见 §5）
3. 为每个 settlement 注册 4 种 source 节点
4. 计算每条边的 cost（见 §5.3）
5. 调 `MinCostFlow.Solve` 并把结果解码为 `List<DispatchInstruction>`

`DispatchInstruction` 是 discriminated union：
- `InPlaceRecruit(settlement, role, count)`
- `RecruiterParty(town, destinationSettlement, role, count)`
- `PrisonerConvert(settlement, role, count)`
- `TransferParty(srcSettlement, dstSettlement, role, count)`

---

## 5. 图设计

### 5.1 节点定义

对每个 clan 的所有 friendly settlement（含 capital）：

**Demand 节点**（每 settlement × 每 role 一个）：
```
DemandNode(settlement, role)
  capacity = max(0, desired_in_role - current_in_role - inflight_in_role)
```

`desired_in_role` 由 `TownGarrisonRule` 解析（generic mode 直接给 role 比例；exact mode 按 template 反推每 role 期望数）。

**Source 节点**（每 settlement 最多 5 种）：

```
InPlaceSource(settlement, role)
  capacity = sum(notable.AvailableTroopCount where notable in settlement
                 and notable provides role)
  notable 只指 town/castle 本地 notable，不含 villages

PrisonSource(settlement, role)
  capacity = min(transferablePrisoners_in_role, dailyConformityBudget)
  仅 friendly settlement 监狱内符合 rule 的俘虏

VillageNotableSource(town, role)
  capacity = sum(notable.AvailableTroopCount where notable in town.Villages
                 and notable provides role)
  按 town 聚合（Q7）：一个 town 一个虚拟 source

GarrisonSurplusSource(settlement, role)
  capacity = max(0, current_in_role - desired_in_role)
  即此 settlement 在该 role 上的真实多余兵

SuperBypass
  唯一节点，与所有 DemandNode 之间有一条 cost = U_UNMET 的边，
  代表"实在没合理路径就让需求未满足"
```

**全局 source/sink**（MCMF 需要）：
- `SuperSource` → 各 Source 节点（capacity = source.capacity, cost = 0）
- 各 DemandNode → `SuperSink`（capacity = demand.capacity, cost = 0）

### 5.2 边定义（哪些 source 能流到哪些 demand）

```
InPlaceSource(s, r)        → DemandNode(s, r)               允许
InPlaceSource(s, r)        → DemandNode(other, r)           禁止（in-place 仅本地）
PrisonSource(s, r)         → DemandNode(s, r)               允许
PrisonSource(s, r)         → DemandNode(other, r)           禁止（俘虏仅本地转化）
VillageNotableSource(t, r) → DemandNode(any, r)             允许（招兵后由 recruiter 运送）
GarrisonSurplusSource(s,r) → DemandNode(other, r)           允许
GarrisonSurplusSource(s,r) → DemandNode(s, r)               禁止（自己流向自己无意义）
```

**跨 role 流动**：默认禁止（infantry 不流向 archer demand）。降级容忍由 cost 控制，不由跨 role 流动控制。

### 5.3 cost 公式

```
edgeCost(source, demand) =
    distance(source.settlement, demand.settlement)         // 1
  + overhead(source.kind)                                  // 2
  + matchPenalty(source.bucket, demand.rule)               // 3
    * (1 - demand.deficitRatio * LENIENCY)                 // 4
```

**(1) distance**：使用 vanilla `Campaign.Current.Models.MapDistanceModel.GetDistance`。in-place / prisoner 同 settlement 时 = 0。

**(2) overhead**：常数，控制"派队的固定成本"：
```
RecruiterOverhead   # VillageNotableSource 边的额外成本（派 recruiter party 的代价）
TransferOverhead    # GarrisonSurplusSource 边的额外成本（派 transfer party 的代价）
0                   # InPlaceSource / PrisonSource（即时完成，无 party 开销）
```

**(3) matchPenalty**：量化 source bucket 与 demand rule 的不匹配程度：

```csharp
int matchPenalty(Bucket b, TownGarrisonRule rule):
    // role 完全不符合：硬罚（但不无穷，紧急时仍可调）
    if (!rule.AllowsRole(b.role)):
        return W_HARD

    if (rule.Mode == Generic):
        // bucket 的 minTier 低于 rule.MinTier：按差距罚
        int tierGap = max(0, rule.MinTier - b.minTier)
        return W_TIER * tierGap

    else /* Exact template mode */:
        // bucket 代表 character 是否在 rule.ExactTemplates 的升级树上
        // （由 TroopTemplateMatcher.IsInUpgradeTreeOf(b.representative, rule.ExactTemplates) 判定）
        if (b 在某 template 的升级树上):
            int tierGap = max(0, template.targetTier - b.minTier)
            return W_TIER * tierGap
        else:
            return W_HARD
```

**(4) deficitRatio 衰减项**：这是解决「等升完才调」的核心：

```
demand.deficitRatio = demand.capacity / desired_in_role  // ∈ [0, 1]
```

- castle 完全空（deficitRatio=1.0）→ matchPenalty 乘 `(1 - 1.0 * LENIENCY)` ≈ 极小 → **任何兵都欢迎**
- castle 80% 满（deficitRatio=0.2）→ matchPenalty 乘 `(1 - 0.2 * LENIENCY)` ≈ 接近原值 → **挑兵种**

`LENIENCY` 推荐默认 0.8。

### 5.4 unmet bypass（避免「宁缺勿滥过头」）

为防止 MCMF 强行用极不合适的 source 填满（例如 `W_HARD` 的桶仍然流），加一条：

```
SuperBypass → DemandNode(any)   capacity = ∞   cost = U_UNMET
```

`U_UNMET` 应略小于「最远距离 + 最大 overhead + 满 matchPenalty 之和」。意思是：宁可让 demand 不满足，也不要发起"明显不值得"的派遣。

推荐 `U_UNMET = W_HARD * 2`。需 playtest 微调。

### 5.5 bucket 拆分细节

每个 settlement 的 garrison roster 按 (role) 分 3 桶（infantry / archer / cavalry）：

```
Bucket {
  role: TroopRole         // infantry / archer / cavalry，由 GenericTroopMatcher.GetRole 给
  count: int
  minTier: int            // 桶内最低 tier
  representative: CharacterObject  // 选最低 tier 的一个代表，用于 exact template lookup
}
```

派遣时按 vanilla `TroopTransferHelper.LowestTierFirst` 顺序取兵，与桶的 `minTier` 评估一致。

**为什么用 minTier 评估而非 avgTier**：保守原则。如果桶里有 t2 和 t5 混合，按 t2 评估意味着"如果 rule 要求 t3+，这个桶被罚"，避免误把 t2 算成可调。

---

## 6. 分批调拨与迭代提升

### 6.1 分批调拨

不需要任何额外算法。**daily 重算 supply 天然达成分批**：

| Day | Capital 状态 | MCMF 看到 InPlaceSource 容量 | 派遣行为 |
|-----|------------|----------------------------|---------|
| 1 | 招到 30（t2-t3 混合） | infantry: 25, archer: 5 | 调 30 到 castle |
| 2 | 又招到 40（部分升 t4） | infantry: 35, archer: 10 | 调 45 |
| 3 | 招满 + 升级若干 | infantry: 40, archer: 15 | 调 55 |

不需要"等"。每天评估当前 capital 有什么，能调就调。

### 6.2 迭代提升（关键机制）

让每个 settlement 同时是 source 和 demand：
- 当 castle 已被 t2 兵填满，capital 又招到 t5 完美兵时：
  - castle 的 `GarrisonSurplusSource(castle, infantry, minTier=2)` 容量 = 0（因为 current = desired），但...
  - **特殊规则**：如果某 settlement 的 current 兵中 minTier < rule.MinTier 的部分，视为"可让位 surplus"

具体在 GraphBuilder 里：

```csharp
// 计算 GarrisonSurplusSource 时
foreach (var bucket in garrisonBuckets(s, role)):
    if (bucket.minTier < rule.MinTier):
        // 这部分是"不达标的将就兵"，允许流回 capital（即使 settlement 看似不"surplus"）
        surplusCapacity += bucket.count
```

但要严控不空 castle：**castle 的 `GarrisonSurplusSource → other DemandNode` 的边只在以下条件存在**：
- demand settlement 是 capital，且
- capital 有同 role 高 tier 兵作为 InPlaceSource 准备流向 castle 同 role demand

简化实现：**让 MCMF 自然处理**。castle 低 tier 兵向 capital 的反向流，cost 设为正常 `distance + TransferOverhead + matchPenalty(rule of capital)`。只有当替换后总 cost 真正降低时，MCMF 才会触发这条边。

### 6.3 「只能在首府升级」约束下的代价

- castle 收到 t2 infantry → 留在 castle 不升级
- 直到 capital 招到 t5 → 「迭代提升」触发：t5 从 capital 调到 castle，t2 从 castle 调回 capital 升级
- **代价**：castle 在被替换前持续是低 tier。短期战力 < 理想。

**用户已接受此 trade-off**。理由：玩家体感上「castle 有兵」 > 「castle 长期空虚等理想兵」。

---

## 7. dispatcher 退化为执行器

所有 dispatcher 不再做"该不该派"的决策。接收来自 GraphBuilder 的 `DispatchInstruction`，执行 4 件事：**校验、组队/兑换、扣费、出发**。

### 7.1 RecruitmentDispatcher

**输入**：`RecruiterParty(town, destination, role, count)` instruction

**职责**：
1. 校验：feature flag、围城、party 上限、foodGuard、capital 食粮
2. 从 capital garrison 按 EscortRatio 抽护卫
3. 创建 `StRecruiterPartyComponent`，注资、买粮
4. 设置首站为该 town 第一个村，注册到 `PartyLifecycleManager`，出发

**内部不再做**：
- 村庄候选筛选（已被 MCMF 决定）
- 多轮 fallback 规划
- 兵种匹配评分

**目标行数**：~80（当前 226）

### 7.2 TransferDispatcher

**输入**：`TransferParty(src, dst, role, count)` instruction

**职责**：
1. 校验：同氏族、非围城、party 上限
2. 从 src garrison 按 LowestTierFirst 取 count 个 role 兵
3. 创建 `StTransferPartyComponent`，注资、买粮
4. 设置目标 dst，注册到 `PartyLifecycleManager`，出发

**目标行数**：~50（当前 131）

### 7.3 PrisonerRecruitmentManager

**输入**：`PrisonerConvert(settlement, role, count)` instruction

**职责**：
1. 从 prison roster 按 role 过滤候选俘虏
2. 累加 XP 直到 count 个可招（vanilla conformity model）
3. 原子转换：garrison +N → prison -N，扣 conformity XP

**内部不再做**：
- 主动扫描决定该不该招
- 跨 settlement 协调

**目标行数**：~60（当前 252）

### 7.4 CapitalInPlaceRecruiter

**输入**：`InPlaceRecruit(settlement, role, count)` instruction

**职责**：
1. 收集 settlement 本地 notable 中可招 role 的兵源
2. 按 notable 兵源贪心取 count 个（一个 notable 兑完了取下一个）
3. 扣金币，加入 garrison

**目标行数**：~30

### 7.5 共同模式

所有 dispatcher 校验失败 → **静默 skip + 写一条 Logger.Info**。不抛异常、不熔断。下一个 daily tick MCMF 重算时，supply 没被消费，demand 仍在，自然重试。

---

## 8. 文件级改动清单

### 8.1 删除

- `src/Recruitment/RecruitmentPlanner.cs`（289 行 → 0）
  - 村庄筛选 + 多维评分逻辑全部被 MCMF cost 函数吸收
- `src/Managers/CapitalLogisticsManager.cs` 中的 `FindBestSource` / `CoordinateRecruitment` / `DispatchTransfers` / `TryUpgradeCapital` 方法
  - 仅保留"构造 GraphBuilder 输入 + 派发 instruction"骨架

### 8.2 新增

- `src/Algorithm/MinCostFlow.cs`（~120 行）
  - 教科书 SPFA + 增广路径实现
  - 完全无游戏依赖，可单测
- `src/Algorithm/SupplyDemandGraph.cs`（~150 行）
  - GraphBuilder：从 clan 状态构造节点和边
  - Decoder：把 MCMF flow 结果翻译为 `DispatchInstruction`
- `src/Algorithm/MatchPolicy.cs`（~60 行）
  - `matchPenalty(bucket, rule)` 纯函数
  - `bucketize(roster)` 把 roster 拆成 3 桶
  - 完全无游戏依赖，可单测
- `src/Algorithm/DispatchInstruction.cs`（~30 行）
  - 4 种 instruction 的 discriminated union（C# record + sealed types）

### 8.3 修改

| 文件 | 当前行数 | 目标行数 | 变化 |
|------|---------|---------|------|
| `src/Managers/CapitalLogisticsManager.cs` | 489 | ~80 | 仅保留"daily 入口：构图 → Solve → 派发" |
| `src/Recruitment/RecruitmentDispatcher.cs` | 226 | ~80 | 退化为执行器 |
| `src/Transfer/TransferDispatcher.cs` | 131 | ~50 | 退化为执行器 |
| `src/Recruitment/PrisonerRecruitmentManager.cs` | 252 | ~60 | 退化为执行器 |
| `src/Capital/CapitalInPlaceRecruiter.cs`（如存在；若不存在则新建） | ? | ~30 | 退化为执行器 |
| `src/Evaluators/TroopTemplateMatcher.cs`（或当前位置） | 231 | ~120 | 删除多维评分函数 `ScoreUpgradeTarget` 及其调用链；仅保留 `IsInUpgradeTreeOf` 和 `MatchesRule` 两个 lookup |
| `src/Configuration/GlobalConfig.cs` `PartyThresholds` | 现有 | +10 | 新增 6 个权重字段 |
| `WebUI/index.html` `thresholdSpecs` | 现有 | +6 | 新增 6 个 slider |

### 8.4 不动

- 所有 `StPartyComponent` 子类及其内部状态机
- `GarrisonXpInjector` / `TroopUpgradeService`（升级仍只在首府发生，与 MCMF 完全解耦）
- `PartyLifecycleManager`
- `RecruitmentDispatcher.cs` 中已存在的「派 recruiter 时如何选村庄」的子算法（若有跨村漫游逻辑，**改为单 town 漫游**：recruiter 只访问指令中指定 town 的村庄。如需新写一个 `WithinTownVillagePicker`，约 20 行）

### 8.5 PartyThresholds 新增字段

```csharp
public class PartyThresholds
{
    // 现有字段不动 ...

    // MCMF 软化 rule 权重
    public int W_HARD            = 1000;  // role 不符 / template 不在升级树上的硬罚
    public int W_TIER            = 50;    // 每差 1 tier 的罚分
    public float LENIENCY        = 0.8f;  // deficitRatio 衰减强度
    public int U_UNMET           = 2000;  // unmet bypass cost（保 = W_HARD * 2）

    // MCMF 派队 overhead
    public int RecruiterOverhead = 100;   // VillageNotableSource 边额外成本
    public int TransferOverhead  = 50;    // GarrisonSurplusSource 边额外成本
}
```

bump `CurrentConfigVersion`。pre-release 阶段无需迁移代码（用户已确认）。

---

## 9. 不变项（守住的边界）

参考 `CLAUDE.md` § Hard invariants：

1. `TargetFramework = net472`
2. `SaveBaseId = 1_900_000_000`，`LocalSaveId` 不重用/不重排
3. GameModels 在 `OnGameStart` 添加
4. 事件处理器全部 try-catch
5. `HourlyTickPartyEvent` 首行过滤 PartyComponent 类型
6. JSON 用 `Newtonsoft.Json`
7. **「首府唯一中心」语义**：
   - 招兵 source（InPlaceSource / VillageNotableSource / PrisonSource）虽然每个 settlement 都可以有，但 capital 是核心：capital 是唯一持有最完整 demand 表的 settlement，CapitalLogisticsManager 是 daily 决策唯一入口
   - **升级仍只在首府发生**：`GarrisonXpInjector` 行为不变
   - Capital 失守时 `PartyLifecycleManager.MigrateAllOrDisband` 行为不变

---

## 10. 已知 trade-off

1. **castle 拿到的低 tier 兵在 castle 不会升级**。
   - **缓解**：迭代提升机制（§6.2）让 capital 在有更优兵时自动反向替换
   - **代价**：替换前 castle 战力低于理想；replacement 也消耗 transfer overhead

2. **`LENIENCY` / `U_UNMET` / `W_HARD` / `W_TIER` 需 playtest 调优**。
   - 默认值是直觉初值。开局几天观察日志判断是否需要调整
   - 建议在 WebConfig UI 暴露为 slider，方便调参

3. **MCMF 输出可能"分散派遣"**：例如 capital 同时需要派 recruiter 到 3 个 town。这是合理的（每 town 一个 recruiter），但 party 数量会上升。受 `RecruiterPartyMaxCount` 限制 — 若上限达到，dispatcher 静默 skip 多余的，下日重试。

4. **prison source 受 daily conformity 限制**，capacity 计算要保守。某些情况下 MCMF 算的 prisoner 流量会比实际可转化的多。结果是 dispatcher 转化到一半因 conformity 耗尽而中止 — 写日志，下日继续。

5. **「跨 role 流动禁止」可能造成 demand 不满足**：例如 castle 缺 archer 而 capital 只有 infantry。MCMF 走 SuperBypass → demand 未满。这是设计的：tactical 替换需要玩家或 rule 调整决定，算法不擅自做。

---

## 11. 实施步骤建议

按以下顺序分阶段实施，每阶段独立可验证：

### Phase 1：算法库（无游戏依赖，可单测）
1. 新建 `src/Algorithm/MinCostFlow.cs` — 实现 + 至少 3 个单测用例（含教科书 transportation problem）
2. 新建 `src/Algorithm/MatchPolicy.cs` — `matchPenalty` 函数 + `bucketize` 函数 + 单测
3. 新建 `src/Algorithm/DispatchInstruction.cs`

**验证**：算法库单测全过；`dotnet build` 0 warnings。

### Phase 2：GraphBuilder（与游戏对接，但不接入 daily 流）
4. 新建 `src/Algorithm/SupplyDemandGraph.cs`
5. 在 CapitalLogisticsManager 加一个**调试入口**（不替换主流程），daily tick 时也构图 + Solve，但只把结果写日志，不实际派遣

**验证**：游戏内观察日志，确认 MCMF 输出的 instruction 看起来合理。与现有派遣对比。

### Phase 3：替换 dispatchers 内部决策
6. 重写 `TransferDispatcher` 为执行器（输入 instruction）
7. 重写 `RecruitmentDispatcher` 为执行器
8. 重写 `PrisonerRecruitmentManager` 为执行器
9. 修整 `CapitalInPlaceRecruiter` 为执行器

**验证**：每个 dispatcher 单独可调用 + 日志正常。`dotnet build` 0 warnings。

### Phase 4：切换 CapitalLogisticsManager
10. 删除 `CapitalLogisticsManager` 内的 `FindBestSource` / `CoordinateRecruitment` / `DispatchTransfers` / `TryUpgradeCapital` 等方法
11. 新主流程：daily tick → 构图 → Solve → 派发各 instruction 到对应 dispatcher
12. 删除 `RecruitmentPlanner.cs`
13. 简化 `TroopTemplateMatcher`：删除 `ScoreUpgradeTarget` 及其调用方

**验证**：游戏内启动一局，观察 1-2 小时游戏时间，confirm：
- castle 第 1 天就开始收兵（即便 capital 兵 tier 不齐）
- 日志显示 MCMF 决策路径清晰
- 无 vanilla 异常

### Phase 5：参数暴露
14. 在 `PartyThresholds` 加 6 个新字段，bump `CurrentConfigVersion`
15. 在 `WebUI/index.html` `thresholdSpecs` 加 6 个 slider
16. 删除任何残留的"等升完再调"硬约束代码

---

## 12. 关键 reference

### 现有代码 reference

- `SovereignTowns/src/Managers/CapitalLogisticsManager.cs` — 主重构目标
- `SovereignTowns/src/Recruitment/RecruitmentPlanner.cs` — 待删除
- `SovereignTowns/src/Configuration/GlobalConfig.cs` — `PartyThresholds` 所在
- `SovereignTowns/_research/` — vanilla 反编译参考（API 签名查询，不要凭记忆调 vanilla）
- `SovereignTowns/WebUI/index.html` — `thresholdSpecs` 数组

### 概念 reference

- MCMF 教科书实现：SPFA（Bellman-Ford queue 变体）+ 增广路径
- 经典 transportation problem：sources 总容量 ≥ demands 总容量时，min cost max flow 给出最优分配

### CLAUDE.md 强约束

实施前**必读** `C:\Users\rangt\Desktop\workspace\CLAUDE.md`，特别注意：
- "Hard invariants (do not violate)" § 全部 8 条
- "Lifecycle gotchas" § 全部 3 条
- "Working norms" § 中"_research 优先于猜 vanilla API"

---

## 13. 给 implementing agent 的指示

1. **按 §11 的 Phase 顺序推进**。每个 Phase 内部可并行，跨 Phase 不可。
2. **每个 Phase 结束**先 `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`，0 errors / 0 warnings 才进下一 Phase。
3. **MCMF 算法库的单测**：因为项目无单测框架，可临时在算法库内写一个 `MinCostFlow.SelfTest()` 方法包含 3-5 个硬编码 transportation problem 用例，启动时调用并 Logger.Info 输出。每个用例的最优 cost 应用穷举或手算验证。开发完成后可移除自检调用但保留方法供回归。
4. **不要凭记忆调 vanilla API**。所有 `Campaign.Current.Models.X` / `MobileParty.X` / `Hero.X` 调用先在 `SovereignTowns/_research/` 下 grep 实际签名。
5. **不要重新设计 cost 函数**。§5.3 的公式是经用户讨论确认的；微调权重默认值可，但公式结构不动。
6. **`StPartyComponent` 子类不要碰**。本重构在 dispatcher 层及以下，不涉及 Component 内部状态机。
7. **遇到歧义先看 CLAUDE.md，再看本文档，再看相邻 spec（2026-05-17 StPartyComponent 重构）**。
8. **每个 Phase 结束写一个 commit**，message 格式与项目历史一致（参考 `git log --oneline` 看风格，例如 `B17.x T?: MCMF Phase 1 - algorithm library`）。具体 task 号由 implementing agent 接管时与用户对齐。
9. **若发现本文档与现有代码冲突**（例如某文件名实际不存在 / 行数大幅不符 / 假设的 API 不存在），**停下来记录冲突**，不要硬改。

---

**END OF SPEC**
