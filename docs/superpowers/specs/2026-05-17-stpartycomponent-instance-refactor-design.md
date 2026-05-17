# StPartyComponent 实例化重构

**Date**: 2026-05-17
**Status**: Design approved, awaiting implementation plan

## 1. 背景与目标

### 1.1 现状

SovereignTowns mod 当前用 4 个 Manager 类管理 4 种部队：
- `PatrolManager` (~730 行) — 巡逻队
- `RecruitmentManager` (~870 行) — 征兵队
- `SallyForthManager` (~750 行) — 主动出击队
- `GarrisonTransferManager` (~380 行) — 调拨队

每个 Manager 同时承担 4 种职责：
- **工厂**：决定何时创建新部队 + 调用 vanilla `CreateParty`
- **事件分派**：订阅 `HourlyTickPartyEvent` / `MapEventEnded`，遍历所有 party、用 `is *PartyComponent` 过滤本类
- **状态机**：根据 party 当前位置、目标、兵员判定下一步行为
- **状态 holder**：用 `Dictionary<MobileParty, T>` 持有 per-party 瞬态状态（如 `RecruitmentManager._visitedPerParty`、`SallyForthManager._lastSallyEndedAt` / `_enemySustainedTicks` / `_forceReturnLogged` / `_targetLostLogged`）

### 1.2 现状的问题

1. **状态散落** — 同一支队伍的瞬态状态分布在 Manager 的多个 Dict 里，靠 `MobileParty` 作 key 拼接。party 销毁时需手动清理这些 Dict，已经踩过坑（见 `RecruitmentManager.OnMobilePartyDestroyed`、`SallyForthManager._forceReturnLogged.Remove(party)`）。
2. **职责过载** — 一个文件做 4 件事，单文件 700+ 行，单方法（如 `RecruitmentManager.OnHourlyTickParty`）超 150 行。
3. **样板重复** — 每个 Manager 都重复写 `is XXXComponent`、try-catch 隔离、受管 clan 校验、`SetMoveGoToSettlement` 包装、MergeAndDisband。
4. **新增部队类型成本高** — 添加一种新部件类型需改 4 个文件：新 Component + 新 Manager + `CampaignBehavior` 注册分派 + `Lifecycle` 注册 kind。
5. **巡逻队特殊** — 当前 `PatrolPartyComponent` 是 vanilla 类型，跨越 vanilla `Settlement.PatrolParty` 槽位与 `AllPatrolParties` 集合，与其他 3 种 ST 自定义 component 行为不对称。

### 1.3 重构目标

把 4 种部队从"过程式 Manager + 状态 Dict"改造为"每支队伍一个 `StPartyComponent` 实例，状态与行为集中在该实例内部"。重构后：

- 同一支队伍的所有数据与行为在一个 Component 类里
- 销毁即释放，不再需要手动清状态 Dict
- 新增部件类型 = 新增一个 Component 类
- 4 种部件完全对称（都继承 `StPartyComponent : CustomPartyComponent`）

## 2. 设计决策（已与用户对齐）

| # | 决策点 | 选定方案 |
|---|---|---|
| Q1 | vanilla 自动 spawn 的 `PatrolPartyComponent` 如何处理 | **共存**：vanilla 自带巡逻队继续 spawn，与 ST 巡逻队互不干涉；不引入 Harmony |
| Q2 | Component 内部状态表达形式 | **显式 enum + switch**（征兵队 / 出击队）；调拨队隐式（状态少） |
| Q3 | `InitialMemberCount`（出发兵员快照）存放位置 | **移到 Component 作为 `[SaveableField]`**（持久化，不再是 fallback） |
| Q4 | Manager 类的去处 | **瘦身为 Dispatcher**：保留文件，只剩"调度时机判断 + 创建实例 + 注册到 Lifecycle" |
| Q5 | 现有 5 处 `is PatrolPartyComponent` 的改造 | **改为 `is StPatrolPartyComponent`**：vanilla 自动 spawn 的 patrol 不进 ST 战利品 / UI 对话 / Lifecycle 跟踪 |
| 验证 | 每步迁移的验证关卡 | `dotnet build` 0 errors / 0 warnings + 全文 grep 无残留旧类型引用；**跳过游戏内 smoke test**（用户决定） |

## 3. 架构

### 3.1 类型层次

```
vanilla:  CustomPartyComponent : PartyComponent
ST:       StPartyComponent : CustomPartyComponent           ← abstract 基类
          ├── StPatrolPartyComponent : StPartyComponent     ← sealed 替代 vanilla PatrolPartyComponent
          ├── StRecruiterPartyComponent : StPartyComponent  ← sealed 替代 RecruitingPartyComponent
          ├── StTransferPartyComponent : StPartyComponent   ← sealed 替代 TransferPartyComponent
          └── StSallyPartyComponent : StPartyComponent      ← sealed 替代 SallyForthPartyComponent
```

命名约定：前缀 `St`，与 vanilla 类（无前缀）明确区分。旧类（`RecruitingPartyComponent` 等）直接删除（pre-release，无存档兼容要求）。

### 3.2 文件组织

```
src/Parties/
  StPartyComponent.cs              ← 抽象基类
  StPatrolPartyComponent.cs        ← 新建
  StRecruiterPartyComponent.cs     ← 重写
  StTransferPartyComponent.cs      ← 重写
  StSallyPartyComponent.cs         ← 重写

src/Patrol/PatrolDispatcher.cs           ← 由 PatrolManager.cs 瘦身改名
src/Recruitment/RecruitmentDispatcher.cs ← 由 RecruitmentManager.cs 瘦身改名
src/Transfer/TransferDispatcher.cs       ← 由 GarrisonTransferManager.cs 瘦身改名
src/SallyForth/SallyDispatcher.cs        ← 由 SallyForthManager.cs 瘦身改名

src/Lifecycle/PartyLifecycleManager.cs   ← 单一事件路由中心
src/SaveSystem/SovereignTownsTypeDefiner.cs ← 新增 LocalId 4/5/6/7/8
```

### 3.3 职责划分

| 层 | 单元 | 职责 |
|---|---|---|
| 路由 | `SovereignTownsCampaignBehavior` | 订阅 vanilla 事件 → 转发给 `PartyLifecycleManager`（party 事件）或 Dispatcher（settlement 事件） |
| 路由 | `PartyLifecycleManager` | `OnHourlyTickParty` / `OnMapEventEnded` 内部分派给 `component.OnXxx(party)`；保留既有的 idle 检测兜底 |
| 工厂 | `PatrolDispatcher` 等 4 个 | `OnHourlyTickSettlement` → 判断该城是否需要派遣 → `new StXxxComponent + MobileParty.CreateParty` → `_lifecycle.RegisterTrackedParty` |
| 实例 | `StXxxPartyComponent` | 持有自己的数据 + 状态机 + 行为方法（`OnHourlyTick` / `OnMapEventEnded` / `OnDestroyed`） |
| 通用 | `StPartyComponent` 基类 | 受管 clan 校验、回城解散判定（调用 `PartyReturnConditionChecker`）、`ReturnToHome` / `DefaultMergeAndDisband` 等模板方法 |

## 4. `StPartyComponent` 抽象基类

```csharp
public abstract class StPartyComponent : CustomPartyComponent
{
    // ── 持久化字段（基类用 [10, 20)）──
    [SaveableField(10)] private Settlement? _homeSettlement;
    [SaveableField(11)] private int _initialMemberCount;
    [CachedData] private TextObject? _cachedName;

    // ── vanilla CustomPartyComponent 抽象成员 ──
    public override Settlement HomeSettlement => _homeSettlement!;
    public override Hero? PartyOwner => _homeSettlement?.OwnerClan?.Leader;
    public abstract override TextObject Name { get; }
    public abstract override bool AvoidHostileActions { get; }

    public int InitialMemberCount => _initialMemberCount;

    /// 子类工厂在 MobileParty.CreateParty 之后立即调用，快照出发兵员数。
    public void SnapshotInitialMembers(MobileParty self)
        => _initialMemberCount = self.MemberRoster?.TotalManCount ?? 0;

    // ── 通用调度（Template Method 模式）──
    public void OnHourlyTick(MobileParty self)
    {
        try {
            if (!ValidateAliveAndManaged(self, out var capital)) return;
            if (IsAtHome(self)) { OnArrivedHome(self); return; }
            OnHourlyTickCore(self, capital);
        } catch (Exception ex) {
            Logger.Error($"{GetType().Name}.OnHourlyTick failed for {SafeName(self)}", ex);
        }
    }

    public void OnMapEventEnded(MapEvent ev, MobileParty self)
    {
        try {
            if (!ValidateAliveAndManaged(self, out _)) return;
            if (AppliesReturnDisbandCondition
                && PartyReturnConditionChecker.ShouldReturnAndDisband(self, _initialMemberCount, out var r, out var detail)) {
                Logger.Info($"{GetType().Name}.MapEventEnded: {SafeName(self)} return-disband ({r}: {detail})");
                ReturnToHome(self);
                return;
            }
            OnMapEventEndedCore(ev, self);
        } catch (Exception ex) {
            Logger.Error($"{GetType().Name}.OnMapEventEnded failed for {SafeName(self)}", ex);
        }
    }

    public virtual void OnDestroyed(MobileParty self, PartyBase? destroyer) { }

    // ── 子类必须 / 可以实现的差异化部分 ──
    protected abstract void OnHourlyTickCore(MobileParty self, Settlement capital);
    protected virtual void OnMapEventEndedCore(MapEvent ev, MobileParty self) { }
    protected virtual void OnArrivedHome(MobileParty self) => DefaultMergeAndDisband(self);
    protected virtual bool AppliesReturnDisbandCondition => true;  // 调拨队 override 为 false

    // ── 基类提供的通用动作 ──
    protected bool ValidateAliveAndManaged(MobileParty self, out Settlement capital);
    protected bool IsAtHome(MobileParty self);
    protected void ReturnToHome(MobileParty self);
    protected int MergeNonHeroTroopsToHome(MobileParty self);
    protected void DefaultMergeAndDisband(MobileParty self);

    /// 构造函数透传 vanilla CustomPartyComponent 既有 protected 形参
    /// (homeSettlement, name, owner, partyMountStringId, partyHarnessStringId,
    ///  customPartyBaseSpeed, avoidHostileActions, args, leader)，
    /// 子类各自工厂方法填入合适默认值。本类不引入新构造形参。
    protected StPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _homeSettlement = home;
    }
}
```

**SaveableField 槽位约定**：基类 [10, 20)；子类 [20, ∞)。每个子类有独立的 saveable type，槽位号互不影响，但留出基类区间方便后续基类加字段。

## 5. 4 个 Component 的具体形态

### 5.1 `StTransferPartyComponent`（最简单 — 隐式状态）

```csharp
public sealed class StTransferPartyComponent : StPartyComponent
{
    [SaveableField(20)] private Settlement? _source;
    [SaveableField(21)] private Settlement? _destination;

    public Settlement? Source => _source;
    public Settlement? Destination => _destination;
    public override TextObject Name => /* "调拨队 - {source.Name} → {dest.Name}" */;
    public override bool AvoidHostileActions => true;
    protected override bool AppliesReturnDisbandCondition => false;  // 调拨队不应用回城解散判定

    public static MobileParty? CreateForRoute(Settlement source, Settlement dest, TroopRoster troops);

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        // 隐式状态机：
        //   - dest owner 变更 → 改返安全 fallback
        //   - 已到 dest → 注入 garrison + 解散（由基类 IsAtHome → OnArrivedHome → DefaultMergeAndDisband 兜底）
        //   - dest 危急 → 改返 source（不解散，等到 source 再 disband）
    }
}
```

### 5.2 `StSallyPartyComponent`（中等 — 显式 enum）

```csharp
public sealed class StSallyPartyComponent : StPartyComponent
{
    public enum SallyPhase { Engaging, Returning }

    [SaveableField(20)] private MobileParty? _targetParty;
    [SaveableField(21)] private CampaignTime _departureTime;
    [SaveableField(22)] private SallyPhase _phase = SallyPhase.Engaging;

    public MobileParty? TargetParty { get => _targetParty; set => _targetParty = value; }
    public CampaignTime DepartureTime => _departureTime;
    public SallyPhase Phase => _phase;
    public override bool AvoidHostileActions => false;
    public override TextObject Name => /* "出击队 - {home.Name}" */;

    public static MobileParty? CreateForTown(Town home, MobileParty target);

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        switch (_phase) {
            case SallyPhase.Engaging:
                if (TaskExpired(self)) { TransitionToReturning(self); return; }                // > MaxSallyHours
                if (TargetEnteredSettlement()) { TransitionToReturning(self); return; }         // vanilla 追击 bug
                if (TargetLostOrDead()) { ReleaseAi(self); }                                    // 释放 vanilla AI 接管
                return;
            case SallyPhase.Returning:
                /* base.IsAtHome 检测接管解散 */
                return;
        }
    }

    /// 战后无条件回家（sally 任务特性 — 比 ShouldReturnAndDisband 更激进，覆盖基类判定）
    protected override void OnMapEventEndedCore(MapEvent ev, MobileParty self)
    {
        TransitionToReturning(self);
    }

    public override void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        // 救援残兵到 home（沿用现有 OnMobilePartyDestroyed 逻辑）
    }

    private void TransitionToReturning(MobileParty self) {
        _phase = SallyPhase.Returning;
        ReleaseAiAndReturnHome(self);
    }
}
```

**注意**：sally 在 `OnMapEventEndedCore` 中无条件回家，这比基类的 `ShouldReturnAndDisband` 判定更激进。基类 `OnMapEventEnded` 会**先**判 `ShouldReturnAndDisband`（命中即回家），未命中再调 `OnMapEventEndedCore`（sally 仍然回家）。两条路径都达成同样效果。

### 5.3 `StRecruiterPartyComponent`（最复杂 — 显式 enum + 实例集合）

```csharp
public sealed class StRecruiterPartyComponent : StPartyComponent
{
    public enum RecruiterPhase { Dispatching, AtVillage, Travelling, Returning }

    [SaveableField(20)] private int _recruitedThisTrip;
    [SaveableField(21)] private Settlement? _assignedTarget;
    [SaveableField(22)] private RecruiterPhase _phase = RecruiterPhase.Dispatching;

    [CachedData] private HashSet<Settlement>? _visitedThisTrip;
    private HashSet<Settlement> VisitedThisTrip => _visitedThisTrip ??= new();

    public int RecruitedThisTrip => _recruitedThisTrip;
    public Settlement? AssignedTarget => _assignedTarget;
    public RecruiterPhase Phase => _phase;
    public override bool AvoidHostileActions => true;
    public override TextObject Name => /* "征兵队 - {home.Name}" */;

    public static MobileParty? CreateForTown(Town home, TroopRoster? initialEscort);

    public void RecordRecruited(int count) => _recruitedThisTrip += count;
    public void SetAssignedTarget(Settlement? target) => _assignedTarget = target;

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        switch (_phase) {
            case RecruiterPhase.Dispatching: HandleDispatching(self); break;
            case RecruiterPhase.AtVillage:   HandleAtVillage(self); break;
            case RecruiterPhase.Travelling:  HandleTravelling(self); break;
            case RecruiterPhase.Returning:   /* base.IsAtHome 接管 */ break;
        }
    }

    // _visitedThisTrip [CachedData] — 重载后丢失（瞬态，无需持久化）
}
```

**关键收益**：原 `RecruitmentManager._visitedPerParty` 全局 `Dictionary<MobileParty, HashSet<Settlement>>` + `OnMobilePartyDestroyed` 清理代码完全消失。

### 5.4 `StPatrolPartyComponent`（vanilla 替代）

```csharp
public sealed class StPatrolPartyComponent : StPartyComponent
{
    // 不继承 vanilla PatrolPartyComponent，因此：
    //   - 不出现在 MobileParty.AllPatrolParties
    //   - 不绑定 Settlement.PatrolParty 槽位
    //   - 不触发 vanilla 巡逻 AI（我们已主动接管）

    public override TextObject Name => /* "巡逻队 - {home.Name}" */;
    public override bool AvoidHostileActions => false;

    public static MobileParty? CreateForTown(Settlement home, PartyTemplateObject? template);

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        // 1. 防御响应（scheduler.GetDefenseTarget — 首府被围 → MergeGarrison，否则 → Defense）
        // 2. 支援出击战斗（FindSupportableSallyBattle）
        // 3. 抵达侦测 → RecordVisit + PickNextStop
        // 4. 卡死保护
    }
}
```

**`PartyTemplateObject` 处理**：保留 vanilla 的 `settlement_patrol_template_level_{1,2,3}` 模板用于初始化兵种构成（按城镇文化适配）。我们手动 `MBObjectManager.Instance.GetObject<PartyTemplateObject>(...)` 取模板，从 garrison 抽兵替换/补充 roster（沿用现有 `TransferTroopsFromGarrison` 路径），不再走 vanilla `PatrolPartyComponent.CreatePatrolParty` 自动初始化。

## 6. Dispatcher 4 个

每个 Dispatcher 只剩三件事：**调度时机判断 → 创建实例 → 注册到 Lifecycle**。

```csharp
public sealed class PatrolDispatcher
{
    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public void OnHourlyTickSettlement(Settlement settlement)
    {
        try {
            if (!ShouldCreatePatrolFor(settlement)) return;
            var party = StPatrolPartyComponent.CreateForTown(settlement, template);
            if (party == null) return;
            _lifecycle.RegisterTrackedParty(party, settlement, KindPatrol);
        } catch (Exception ex) { Logger.Error(...); }
    }
}
```

Dispatcher **不再有**：HourlyTickParty 转发、MapEventEnded 转发、状态机分支、Heal/Merge/Retreat 判定、`PartyMergeService` 调用。

**Dispatcher 间通信约束**（沿用现有）：
- `CapitalLogisticsManager` 仍调用 `RecruitmentDispatcher.TryDispatchRecruiter(...)` / `TransferDispatcher.TryDispatchTransfer(...)`
- `StPatrolPartyComponent` 调用 `_sallyDispatcher?.GetActiveCombatSallyParties(clan)`（支援判定）

**`PartyMergeService` 注入方式**：改为 process-wide singleton。新增静态属性 `PartyMergeService.Instance`，在 `SovereignTownsCampaignBehavior.OnSessionLaunched` 阶段（`PartyLifecycleManager` 实例化之后）调用 `PartyMergeService.Initialize(lifecycle)` 一次。基类、Dispatcher、`PartyLifecycleManager` 自身（含 `MigrateAllOrDisband` / `MigrateByHomeSettlement` 两处当前 `new PartyMergeService(this)` 调用）全部改用 `PartyMergeService.Instance`。原 5 处 `new PartyMergeService(_lifecycle)` 全部删除。

## 7. 事件路由收敛

### 7.1 改前（`SovereignTownsCampaignBehavior.cs`）

```csharp
private void OnHourlyTickParty(MobileParty party) {
    _lifecycle?.OnHourlyTickParty(party);
    _recruitmentManager?.OnHourlyTickParty(party);
    _transferManager?.OnHourlyTickParty(party);
    _patrolManager?.OnHourlyTickParty(party);
    _sallyForthManager?.OnHourlyTickParty(party);
}

private void OnMapEventEnded(MapEvent mapEvent) {
    _battleLootManager?.OnMapEventEnded(mapEvent);
    _sallyForthManager?.OnMapEventEnded(mapEvent);
    _patrolManager?.OnMapEventEnded(mapEvent);
    _recruitmentManager?.OnMapEventEnded(mapEvent);
}
```

### 7.2 改后

```csharp
private void OnHourlyTickParty(MobileParty party) {
    _lifecycle?.OnHourlyTickParty(party);   // ← 内部分派给 component.OnHourlyTick(party)
}

private void OnMapEventEnded(MapEvent mapEvent) {
    _battleLootManager?.OnMapEventEnded(mapEvent);
    _lifecycle?.OnMapEventEnded(mapEvent);  // ← 内部分派给 component.OnMapEventEnded(...)
}

// settlement 事件仍按现状分派给 Dispatcher
private void OnHourlyTickSettlement(Settlement settlement) {
    _patrolDispatcher?.OnHourlyTickSettlement(settlement);
    _sallyDispatcher?.OnHourlyTickSettlement(settlement);
}
```

### 7.3 `PartyLifecycleManager` 内部

```csharp
public void OnHourlyTickParty(MobileParty party)
{
    DoIdleCheck(party);  // 既有的 24h force-return / 36h disband 兜底保留
    if (party.PartyComponent is StPartyComponent stc) {
        try { stc.OnHourlyTick(party); }
        catch (Exception ex) { Logger.Error($"OnHourlyTick failed for {party.Name}", ex); }
    }
}

public void OnMapEventEnded(MapEvent ev)
{
    foreach (var side in new[] { ev.AttackerSide, ev.DefenderSide }) {
        if (side?.Parties == null) continue;
        foreach (var uop in side.Parties) {
            var mp = uop.Party?.MobileParty;
            if (mp?.PartyComponent is StPartyComponent stc && mp.IsActive) {
                try { stc.OnMapEventEnded(ev, mp); }
                catch (Exception ex) { Logger.Error($"OnMapEventEnded failed for {mp.Name}", ex); }
            }
        }
    }
}
```

`Lifecycle.RebuildFromCampaign` 同步简化：不再分 4 路扫描，统一 `is StPartyComponent` 一处过滤。`TrackedPartyMeta.InitialMemberCount` 字段删除，`GetInitialMemberCount` 方法删除（基类的 `InitialMemberCount` 属性替代）。

## 8. SaveSystem 改动

`SovereignTownsTypeDefiner.cs`：

```csharp
protected override void DefineClassTypes()
{
    // 旧的 LocalId 1/2/3 弃用（pre-release，无存档兼容）
    AddClassDefinition(typeof(Parties.StPartyComponent), 4);            // 抽象基类（vanilla 要求）
    AddClassDefinition(typeof(Parties.StPatrolPartyComponent), 5);
    AddClassDefinition(typeof(Parties.StRecruiterPartyComponent), 6);
    AddClassDefinition(typeof(Parties.StTransferPartyComponent), 7);
    AddClassDefinition(typeof(Parties.StSallyPartyComponent), 8);
}
```

`SaveBaseId = 1_900_000_000` 不变。`ConfigVersion = 15` 不变（上一轮 B15 已 bump）。

## 9. vanilla `PatrolPartyComponent` 引用改造（5 处）

| 文件 | 改动 |
|---|---|
| `Battle/BattleLootManager.cs:76, 92` | `is PatrolPartyComponent` → `is StPatrolPartyComponent` |
| `Battle/BattleLootHandler.cs:446` | 同上 |
| `Lifecycle/PartyLifecycleManager.cs` 读档分支 | 合并到统一 `is StPartyComponent` 路径 |
| `Ui/STPartyDialogRegistration.cs:92-93` | `is PatrolPartyComponent` → `is StPatrolPartyComponent` |

**语义**：vanilla 自动 spawn 的 `PatrolPartyComponent` 不进入 ST 的 BattleLoot 战利品处置、UI 自定义对话、Lifecycle 跟踪。vanilla 自管的归 vanilla，ST 自管的归 ST。

## 10. 迁移顺序

按风险递增分 5 个独立 commit，每步以 `dotnet build` 0 errors / 0 warnings + 全文 grep 无残留旧类型引用为关卡。

| Step | 内容 | 改动量估算 |
|---|---|---|
| 0 | 基础设施：`PartyMergeService` singleton 改造 + 新建 `StPartyComponent` 抽象基类 + `TypeDefiner` 加 LocalId 4 注册基类 | ~400 行（基类 ~300 + singleton 改造 ~100，含 5 处 `new PartyMergeService(_lifecycle)` 删除） |
| 1 | 迁移 Transfer：`StTransferPartyComponent` 替代 `TransferPartyComponent`，`GarrisonTransferManager` 瘦身为 `TransferDispatcher`；`TypeDefiner` 加 LocalId 7 | ~250 行 |
| 2 | 迁移 Sally：`StSallyPartyComponent` 替代 `SallyForthPartyComponent`，`SallyForthManager` 瘦身为 `SallyDispatcher`；`TypeDefiner` 加 LocalId 8 | ~400 行 |
| 3 | 迁移 Recruiter：`StRecruiterPartyComponent` 替代 `RecruitingPartyComponent`（`_visitedThisTrip` 实例化），`RecruitmentManager` 瘦身为 `RecruitmentDispatcher`；`TypeDefiner` 加 LocalId 6 | ~600 行 |
| 4 | 迁移 Patrol：新建 `StPatrolPartyComponent` 完全替代 vanilla `PatrolPartyComponent` 使用路径，`PatrolManager` 瘦身为 `PatrolDispatcher`，5 处 `is PatrolPartyComponent` 改为 `is StPatrolPartyComponent`，`Lifecycle.RebuildFromCampaign` 简化为单一 `is StPartyComponent` 扫描；`TypeDefiner` 加 LocalId 5 | ~500 行 |

每步如果验证不通过，回滚该 commit 重做；前序步骤不受影响。Step 0 完成后基类与 singleton 已就绪，但**没有任何 Component 子类**，因此第一次构建会通过（基类是 abstract，无实例化），但 4 个旧 Component 仍正常工作（继承未变）。Step 1-4 逐一替换。

## 11. 删除清单（重构完成后）

```
src/Parties/RecruitingPartyComponent.cs       — 删
src/Parties/TransferPartyComponent.cs         — 删
src/Parties/SallyForthPartyComponent.cs       — 删
src/Patrol/PatrolManager.cs                   — 替换为 PatrolDispatcher.cs
src/Recruitment/RecruitmentManager.cs         — 替换为 RecruitmentDispatcher.cs
src/Transfer/GarrisonTransferManager.cs       — 替换为 TransferDispatcher.cs
src/SallyForth/SallyForthManager.cs           — 替换为 SallyDispatcher.cs
src/Common/PartyReturnConditionChecker.cs     — 保留（基类调用）
src/Lifecycle/PartyLifecycleManager.cs        — 大幅瘦身（TrackedPartyMeta.InitialMemberCount 删）
```

`CLAUDE.md` Architecture 章节同步：4 个 Manager 改 Dispatcher，Layer 3 改 "Dispatchers + StPartyComponent base"。

## 12. 验证策略

- 编译验证：每步 `dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug` 必须 0 errors / 0 warnings
- 静态检查：每步 `grep` 全文确认旧类型引用为 0
- 游戏内 smoke test：**跳过**（用户决定）

## 13. 与上一轮 B15 改动的衔接

上一轮 B15 已完成的（保留）：
- `PartyThresholds` 字段重构（`PartyReturnSizeRatio` / `PartyReturnWoundedRatio` / `RecruiterReturnRecruitedCount` / `SallyCreateMinPartyCount`）
- `PartyReturnConditionChecker` 工具类 — 基类 `OnMapEventEnded` 直接调用
- `PartyLifecycleManager.GetInitialMemberCount` — Step 1 后移除（基类的 `InitialMemberCount` 属性替代）
- `TrackedPartyMeta.InitialMemberCount` 字段同步删除
