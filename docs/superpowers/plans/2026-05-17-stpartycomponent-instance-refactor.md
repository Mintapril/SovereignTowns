# StPartyComponent 实例化重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 4 种 mod 部队（巡逻 / 征兵 / 调拨 / 出击）从"过程式 Manager + 状态 Dict"重构为"每支队伍一个 `StPartyComponent` 实例，状态与行为集中在该实例内部"。

**Architecture:** 新建抽象基类 `StPartyComponent : CustomPartyComponent`，提供 Template Method 编排（受管 clan 校验 / 回城解散判定 / merge 路径）；4 个 sealed 子类各自实现差异化逻辑。原 4 个 Manager 瘦身为"只决定何时何地派遣"的 Dispatcher。`PartyMergeService` 改为 process-wide singleton。`PartyLifecycleManager` 成为唯一事件路由中心，单点分派给 `component.OnHourlyTick(party)` / `component.OnMapEventEnded(...)`。

**Tech Stack:** C# .NET Framework 4.7.2; Bannerlord v1.3.15 vanilla `TaleWorlds.CampaignSystem` API；vanilla `[SaveableField]` + `[CachedData]` 序列化系统；`Newtonsoft.Json`。

**Spec:** [docs/superpowers/specs/2026-05-17-stpartycomponent-instance-refactor-design.md](../specs/2026-05-17-stpartycomponent-instance-refactor-design.md)

**Pre-baseline:** 上一轮 B15 改动（`PartyReturnConditionChecker` 新建、`PartyThresholds` 重构等）已编译通过但未提交。Step 0 开始前先把这些改动单独 commit 为 baseline。

---

## Verification Conventions (本 plan 通用)

**没有单元测试**（CLAUDE.md 明说："There are no unit tests"），TDD 模式不适用。每步验证模式：

1. **改代码**
2. **`dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug`** — 必须 0 errors / 0 warnings
3. **`grep` 残留检查**（每 Task 内具体说明 grep 什么模式）
4. **代码自检**（按 spec 对照改动逻辑）

每个 Step 结束最后**一个 commit**，commit message 用现有项目风格 `B16.<step>: <summary>`（B15 是上一轮）。

**游戏内 smoke test 跳过**（用户决定）。

---

## Pre-Step: 把 B15 baseline 落地

**Files:**
- Modify: `git add` 现有 B15 改动并 commit

- [ ] **P.1 — 检查工作目录状态**

```bash
git status --short
```

Expected：列出 B15 改动的文件（GlobalConfig.cs / ConfigurationManager.cs / PatrolManager.cs / RecruitmentManager.cs / SallyForthManager.cs / Models/STPartySizeLimitModel.cs / Lifecycle/PartyLifecycleManager.cs / Recruitment/RecruitingPartyComponent.cs(若有) / WebUI/index.html / Common/PartyReturnConditionChecker.cs 等）+ 已提交的 spec doc。

- [ ] **P.2 — 全部 stage**

```bash
git add SovereignTowns/
```

- [ ] **P.3 — Commit baseline**

```bash
git commit -m "$(cat <<'EOF'
B15: 通用回城解散条件（PartyReturnSizeRatio/WoundedRatio）+ 阈值绝对数化

- 统一 size<ratio / wounded>ratio 判定（PartyReturnConditionChecker）
- 判定时机搬到 MapEventEnded（非 HourlyTick）
- 删除 PatrolMergeMemberRatio / PatrolHeal* / SallyRetreatMemberRatio 等近似重复
- 征兵返航与出击下限改绝对数（RecruiterReturnRecruitedCount=50 / SallyCreateMinPartyCount=30）
- PatrolReserveAfterCreationRatio 0.27 → 0.8
- ConfigVersion 14 → 15

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **P.4 — 验证 baseline 编译**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected：`0 errors / 0 warnings`，commit 后 `git status --short` 应只剩 spec doc 等已提交的状态。

---

## Step 0 — 基础设施：`PartyMergeService` singleton + `StPartyComponent` 抽象基类

**目标**：搭建后续 4 个 Step 共用的基础设施。完成后没有任何 Component 子类，旧 4 个 Component 继续工作不变。

**Files:**
- Modify: `SovereignTowns/src/Lifecycle/PartyMergeService.cs`
- Modify: `SovereignTowns/src/Patrol/PatrolManager.cs`
- Modify: `SovereignTowns/src/Recruitment/RecruitmentManager.cs`
- Modify: `SovereignTowns/src/SallyForth/SallyForthManager.cs`
- Modify: `SovereignTowns/src/Transfer/GarrisonTransferManager.cs`
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
- Create: `SovereignTowns/src/Parties/StPartyComponent.cs`
- Modify: `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`

---

### Task 0.1: `PartyMergeService` 改 singleton

- [ ] **Step 1: 改写 `PartyMergeService.cs`**

将整个文件替换为：

```csharp
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Lifecycle;

/// <summary>
/// Process-wide singleton. 通过 <see cref="Initialize"/> 在 OnSessionLaunched 注入 lifecycle 引用，
/// 之后所有调用方使用 <see cref="Instance"/> 直接访问，避免每个 Manager 自带一份实例。
/// </summary>
public sealed class PartyMergeService
{
    private static PartyMergeService? _instance;
    public static PartyMergeService Instance =>
        _instance ?? throw new InvalidOperationException(
            "PartyMergeService.Initialize must be called once during OnSessionLaunched before Instance access");

    public static void Initialize(PartyLifecycleManager lifecycle)
    {
        if (lifecycle is null) throw new ArgumentNullException(nameof(lifecycle));
        _instance = new PartyMergeService(lifecycle);
    }

    /// 仅用于测试 / 卸载场景（mod unload 时清空，下次 Initialize 重建）。
    public static void ResetForReload() => _instance = null;

    private readonly PartyLifecycleManager _lifecycle;

    private PartyMergeService(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public int MergeNonHeroTroopsIntoGarrison(MobileParty? party, Settlement? targetSettlement, string context)
    {
        // ── 此方法体保持现状不变（原 line 24-101）──
        try
        {
            var targetTown = targetSettlement?.Town;
            var targetGarrison = targetTown?.GarrisonParty;
            if (targetSettlement != null && targetTown != null && targetGarrison == null)
            {
                try
                {
                    targetSettlement.AddGarrisonParty();
                    targetGarrison = targetTown.GarrisonParty;
                    Logger.Info($"{context}: rebuilt missing GarrisonParty for '{targetSettlement.Name}' before merge");
                }
                catch (Exception addEx)
                {
                    Logger.Error($"{context}: AddGarrisonParty failed for '{targetSettlement.Name}'", addEx);
                }
            }

            var targetRoster = targetGarrison?.MemberRoster;
            var sourceRoster = party?.MemberRoster;
            if (targetRoster == null || sourceRoster == null)
            {
                Logger.Warn($"{context}: cannot merge party '{party?.Name}' into '{targetSettlement?.Name}' (missing roster/garrison)");
                return 0;
            }

            int transferred = 0;
            var snapshot = new List<TroopRosterElement>(sourceRoster.GetTroopRoster());
            foreach (var element in snapshot)
            {
                if (element.Character == null || element.Character.IsHero) continue;
                if (element.Number <= 0) continue;

                try
                {
                    targetRoster.AddToCounts(element.Character, element.Number, false, element.WoundedNumber, element.Xp);
                }
                catch (Exception addEx)
                {
                    Logger.Warn($"{context}: AddToCounts failed for '{element.Character.StringId}' x{element.Number}; element skipped: {addEx.Message}");
                    continue;
                }

                try
                {
                    sourceRoster.RemoveTroop(element.Character, element.Number, default, 0);
                    transferred += element.Number;
                }
                catch (Exception removeEx)
                {
                    Logger.Error($"{context}: RemoveTroop failed for '{element.Character.StringId}' x{element.Number}; rolling back garrison add", removeEx);
                    try { targetRoster.RemoveTroop(element.Character, element.Number, default, 0); }
                    catch (Exception rollbackEx)
                    {
                        Logger.Error($"{context}: rollback also failed — duplicate troops in garrison may persist", rollbackEx);
                    }
                }
            }

            return transferred;
        }
        catch (Exception ex)
        {
            Logger.Error($"{context}: MergeNonHeroTroopsIntoGarrison failed", ex);
            return 0;
        }
    }

    public void DisbandAndUntrack(MobileParty? party, string context)
    {
        if (party == null) return;
        try { DisbandPartyAction.StartDisband(party); }
        catch (Exception ex)
        {
            Logger.Error($"{context}: StartDisband failed for '{party.Name}'; will still untrack to avoid index leak", ex);
        }
        try { _lifecycle.UntrackParty(party); }
        catch (Exception untrackEx)
        {
            Logger.Error($"{context}: UntrackParty also failed for '{party.Name}'", untrackEx);
        }
    }

    public bool DestroyAndUntrack(MobileParty? party, string context, bool deferIfInMapEvent = true)
    {
        if (party == null) return false;

        try
        {
            if (deferIfInMapEvent && party.MapEvent != null)
            {
                Logger.Warn($"{context}: '{party.Name}' is in MapEvent, deferring destroy");
                return false;
            }
        }
        catch { }

        try
        {
            DestroyPartyAction.Apply(null, party);
            _lifecycle.UntrackParty(party);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{context}: DestroyPartyAction failed for '{party.Name}', falling back to disband", ex);
            try
            {
                DisbandPartyAction.StartDisband(party);
                _lifecycle.UntrackParty(party);
                return true;
            }
            catch (Exception fallbackEx)
            {
                Logger.Error($"{context}: fallback disband failed for '{party.Name}'", fallbackEx);
                return false;
            }
        }
    }
}
```

**关键差异 vs 现状**：
- 构造从 `public` 改 `private`
- 新增 static `Instance` / `Initialize` / `ResetForReload`
- 业务方法体（`MergeNonHeroTroopsIntoGarrison` / `DisbandAndUntrack` / `DestroyAndUntrack`）完全保持现状

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：会有 5 个 errors（5 处 `new PartyMergeService(...)` 调用了已变 private 的构造）。这是预期的 — Task 0.2 修复。

---

### Task 0.2: 替换 5 处 `new PartyMergeService(...)` 调用为 `PartyMergeService.Instance`

- [ ] **Step 1: `PatrolManager.cs` 替换**

```bash
grep -n "_mergeService = new PartyMergeService" SovereignTowns/src/Patrol/PatrolManager.cs
```

找到的行（约 line 71）改为：

```csharp
        _mergeService = PartyMergeService.Instance;
```

注：`_mergeService` 字段保留不删（避免改动业务方法引用），只是改成持有 singleton 引用。

- [ ] **Step 2: `RecruitmentManager.cs` 替换**

```bash
grep -n "_mergeService = new PartyMergeService" SovereignTowns/src/Recruitment/RecruitmentManager.cs
```

同上：约 line 68 改为 `_mergeService = PartyMergeService.Instance;`

- [ ] **Step 3: `SallyForthManager.cs` 替换**

```bash
grep -n "_mergeService = new PartyMergeService" SovereignTowns/src/SallyForth/SallyForthManager.cs
```

约 line 76 改为 `_mergeService = PartyMergeService.Instance;`

- [ ] **Step 4: `GarrisonTransferManager.cs` 替换**

```bash
grep -n "_mergeService = new PartyMergeService" SovereignTowns/src/Transfer/GarrisonTransferManager.cs
```

约 line 40 改为 `_mergeService = PartyMergeService.Instance;`

- [ ] **Step 5: `PartyLifecycleManager.cs` 替换两处**

```bash
grep -n "var mergeService = new PartyMergeService" SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs
```

应找到 2 处（`MigrateAllOrDisband` 与 `MigrateByHomeSettlement`），分别改为：

```csharp
            var mergeService = PartyMergeService.Instance;
```

- [ ] **Step 6: grep 验证 5 处 new 全部清除**

```bash
grep -rn "new PartyMergeService" SovereignTowns/src/
```

Expected：**无任何匹配**。

- [ ] **Step 7: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：仍然有 1 个 runtime 风险（Initialize 尚未调用），但**编译应通过**（Initialize 是 static 方法，Instance 访问也是 static，构造期使用 `PartyMergeService.Instance` 不会出编译错误）。如果还有 build error 就修。

---

### Task 0.3: 在 `OnSessionLaunched` 初始化 singleton

- [ ] **Step 1: `SovereignTownsCampaignBehavior.cs:144-145` 之后插入 Initialize**

找到现有：

```csharp
            _lifecycle = new PartyLifecycleManager();
            _lifecycle.Initialize();
```

在 `_lifecycle.Initialize();` 之后**立即**插入：

```csharp
            // B16.0：PartyMergeService 改为 singleton — 所有调用方通过 Instance 访问。必须在
            // _lifecycle 构造之后、任何 Manager 构造（它们的字段初始化器会读 Instance）之前。
            SovereignTowns.Lifecycle.PartyMergeService.Initialize(_lifecycle);
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`。

---

### Task 0.4: 新建 `StPartyComponent` 抽象基类

- [ ] **Step 1: 创建 `SovereignTowns/src/Parties/StPartyComponent.cs`**

```csharp
using System;
using SovereignTowns.Common;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 4 种 ST 部件的抽象基类。提供 Template Method 编排：
///   - OnHourlyTick / OnMapEventEnded 由基类编排"通用前置 → 子类核心 → 通用后置"
///   - 受管 clan 校验、回城解散判定、Merge 流程全部在基类
///   - 子类只 override `*Core` 与 enum 状态机
///
/// SaveableField 槽位约定：基类用 [10, 20)；子类用 [20, +∞)。
/// </summary>
public abstract class StPartyComponent : CustomPartyComponent
{
    // ── 持久化字段 ──
    [SaveableField(10)] private Settlement? _homeSettlement;
    [SaveableField(11)] private int _initialMemberCount;

    // ── 缓存字段（不存档）──
    [CachedData] private TextObject? _cachedName;

    // ── vanilla CustomPartyComponent 抽象成员 ──
    public override Settlement HomeSettlement => _homeSettlement!;
    public override Hero? PartyOwner => _homeSettlement?.OwnerClan?.Leader;
    public abstract override TextObject Name { get; }
    public abstract override bool AvoidHostileActions { get; }

    /// 出发时兵员快照，用于"当前 / 出发 &lt; ratio"判定。
    public int InitialMemberCount => _initialMemberCount;

    /// 子类工厂在 MobileParty.CreateParty 之后立即调用，快照出发兵员数。
    /// 调用前 party.MemberRoster 必须已包含初始 troops。
    public void SnapshotInitialMembers(MobileParty self)
        => _initialMemberCount = self?.MemberRoster?.TotalManCount ?? 0;

    // ── 通用调度（Template Method 模式）──

    /// vanilla HourlyTickPartyEvent 路由入口，由 PartyLifecycleManager 单点调用。
    public void OnHourlyTick(MobileParty self)
    {
        if (self == null) return;
        try
        {
            if (!ValidateAliveAndManaged(self, out var capital)) return;
            if (IsAtHome(self)) { OnArrivedHome(self); return; }
            OnHourlyTickCore(self, capital);
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnHourlyTick failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    /// vanilla MapEventEnded 路由入口，由 PartyLifecycleManager 单点调用。
    public void OnMapEventEnded(MapEvent ev, MobileParty self)
    {
        if (self == null) return;
        try
        {
            if (!ValidateAliveAndManaged(self, out _)) return;
            if (AppliesReturnDisbandCondition
                && PartyReturnConditionChecker.ShouldReturnAndDisband(self, _initialMemberCount, out var reason, out var detail))
            {
                Logger.Info($"{GetType().Name}.MapEventEnded: '{PartyNameFormatter.SafeName(self)}' return-disband ({reason}: {detail})");
                ReturnToHome(self);
                return;
            }
            OnMapEventEndedCore(ev, self);
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnMapEventEnded failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    /// MobilePartyDestroyed 路由入口，由 PartyLifecycleManager 单点调用。默认 no-op；子类可救援残兵等。
    public virtual void OnDestroyed(MobileParty self, PartyBase? destroyer) { }

    // ── 子类必须 / 可以实现的差异化部分 ──

    /// 子类的状态机核心。基类已确保：party.IsActive、受管 clan 合法、不在 home。
    protected abstract void OnHourlyTickCore(MobileParty self, Settlement capital);

    /// 战后自定义行为（基类已先判 ShouldReturnAndDisband，命中即回家，未到此方法）。默认 no-op。
    protected virtual void OnMapEventEndedCore(MapEvent ev, MobileParty self) { }

    /// 到达 home 时的处理。默认：转兵进 garrison + 解散。
    protected virtual void OnArrivedHome(MobileParty self) => DefaultMergeAndDisband(self);

    /// 是否应用回城解散条件（PartyReturnConditionChecker）。调拨队 override 为 false。
    protected virtual bool AppliesReturnDisbandCondition => true;

    // ── 基类提供的通用动作（protected，子类可直接调用）──

    /// 校验 party.IsActive + 受管 clan + home 仍属本 clan。返回 true 表示后续逻辑可继续；
    /// false 时 capital 输出为 null，调用方应立即 return（基类调度路径会自动 return）。
    /// 失去归属 / home 失守等异常路径在此处理（DisbandAndUntrack 或 MergeGarrison 到 fallback capital）。
    protected bool ValidateAliveAndManaged(MobileParty self, out Settlement? capital)
    {
        capital = null;
        if (!self.IsActive) return false;

        var partyClan = self.ActualClan;
        if (partyClan == null)
        {
            Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' has null ActualClan; disbanding");
            PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} null ActualClan");
            return false;
        }

        var registry = CapitalRegistry.Instance;
        if (registry == null) return false;

        var home = HomeSettlement;
        capital = registry.GetCapitalForClan(partyClan);
        if (home == null) return false;

        if (home.OwnerClan != partyClan)
        {
            if (capital != null)
            {
                Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' home '{home.Name}' lost; merging at capital '{capital.Name}'");
                MergeToFallback(self, capital);
            }
            else
            {
                Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' home '{home.Name}' lost and no fallback capital; disbanding");
                PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} lost home");
            }
            capital = null;
            return false;
        }

        if (capital == null) return false;  // managed clan 但当前无首府
        return true;
    }

    /// party 当前位置是否在 home。基类判定：CurrentSettlement == home OR LastVisitedSettlement == home。
    protected bool IsAtHome(MobileParty self)
    {
        var home = HomeSettlement;
        if (home == null) return false;
        return self.CurrentSettlement == home || self.LastVisitedSettlement == home;
    }

    /// 把 party 设回 home 方向（vanilla AI 接管移动）。
    protected void ReturnToHome(MobileParty self)
    {
        var home = HomeSettlement;
        if (home == null) return;
        try { self.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false); }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.ReturnToHome SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    /// 转兵进 home garrison + 解散 + untrack。
    protected void DefaultMergeAndDisband(MobileParty self)
    {
        var home = HomeSettlement;
        if (home == null)
        {
            PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} null home in DefaultMergeAndDisband");
            return;
        }
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, home, $"{GetType().Name}.DefaultMergeAndDisband");
        Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into '{home.Name}', disbanding");
        PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name}.DefaultMergeAndDisband");
    }

    /// 转兵进 fallback settlement + 解散 + untrack（home 失守时调用）。
    protected void MergeToFallback(MobileParty self, Settlement fallback)
    {
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, fallback, $"{GetType().Name}.MergeToFallback");
        Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into fallback '{fallback.Name}', disbanding");
        PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name}.MergeToFallback");
    }

    // ── 构造函数：透传 vanilla CustomPartyComponent 既有 protected 形参 ──
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

**注意点**：
- `CapitalRegistry.Instance` 已存在（grep 验证：`SovereignTowns/src/Capital/CapitalRegistry.cs` 有静态 `Instance`）
- `PartyNameFormatter.SafeName` 已存在（`SovereignTowns/src/Common/PartyNameFormatter.cs`）
- 用 `using SovereignTowns.Lifecycle;` 引入 `PartyMergeService`

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`。基类编译通过但**还没人继承它**，所以不会有 SaveableTypeDefiner 报错（vanilla 容忍 abstract 不能实例化但要 type 注册）。

---

### Task 0.5: 在 `SovereignTownsTypeDefiner` 注册基类 LocalId 4

- [ ] **Step 1: 修改 `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`**

替换 `DefineClassTypes` 方法体为：

```csharp
    protected override void DefineClassTypes()
    {
        // local id 1: 招募队伍组件
        AddClassDefinition(typeof(Parties.RecruitingPartyComponent), 1);

        // local id 2: 调拨队伍组件
        AddClassDefinition(typeof(Parties.TransferPartyComponent), 2);

        // local id 3: 出击队伍组件
        AddClassDefinition(typeof(Parties.SallyForthPartyComponent), 3);

        // local id 4: StPartyComponent 抽象基类（vanilla SaveSystem 要求抽象基类也注册）
        AddClassDefinition(typeof(Parties.StPartyComponent), 4);
    }
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`。

---

### Task 0.6: Step 0 总验证 + commit

- [ ] **Step 1: 全文 grep 验证清单**

```bash
grep -rn "new PartyMergeService" SovereignTowns/src/
```
Expected: 0 matches

```bash
grep -rn "PartyMergeService.Instance" SovereignTowns/src/
```
Expected: ≥ 5 matches（前面 5 处替换的地方）

```bash
grep -rn "class StPartyComponent" SovereignTowns/src/
```
Expected: 1 match (`src/Parties/StPartyComponent.cs:public abstract class StPartyComponent`)

- [ ] **Step 2: 最终编译**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`ok dotnet build: 1 projects, 0 errors, 0 warnings`

- [ ] **Step 3: Commit Step 0**

```bash
git add SovereignTowns/src/Lifecycle/PartyMergeService.cs \
        SovereignTowns/src/Patrol/PatrolManager.cs \
        SovereignTowns/src/Recruitment/RecruitmentManager.cs \
        SovereignTowns/src/SallyForth/SallyForthManager.cs \
        SovereignTowns/src/Transfer/GarrisonTransferManager.cs \
        SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs \
        SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs \
        SovereignTowns/src/Parties/StPartyComponent.cs \
        SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs

git commit -m "$(cat <<'EOF'
B16.0: 基础设施 — PartyMergeService singleton + StPartyComponent 抽象基类

- PartyMergeService 改 process-wide singleton（Initialize/Instance/ResetForReload），
  5 处 new PartyMergeService(_lifecycle) 替换为 Instance；OnSessionLaunched 注入 lifecycle
- 新建 StPartyComponent : CustomPartyComponent 抽象基类
  Template Method 编排（OnHourlyTick/OnMapEventEnded），子类只 override *Core
- SovereignTownsTypeDefiner 注册基类 LocalId 4
- 暂未引入任何 Component 子类；旧 4 个 Component 仍正常工作

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Step 1 — 迁移 Transfer（最简单，作为概念验证）

**目标**：新建 `StTransferPartyComponent` 替代 `TransferPartyComponent`；`GarrisonTransferManager` 瘦身为 `TransferDispatcher`；删除旧文件。

**Files:**
- Create: `SovereignTowns/src/Parties/StTransferPartyComponent.cs`
- Modify: `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`
- Replace: `SovereignTowns/src/Transfer/GarrisonTransferManager.cs` → `SovereignTowns/src/Transfer/TransferDispatcher.cs`
- Modify: `SovereignTowns/src/Models/STPartySizeLimitModel.cs`（类型引用）
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`（`RebuildFromCampaign` 分支）
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`（移除 `_transferManager?.OnHourlyTickParty` 转发）
- Modify: `SovereignTowns/src/Managers/CapitalLogisticsManager.cs`（构造形参类型 + 调用点改名）
- Delete: `SovereignTowns/src/Parties/TransferPartyComponent.cs`

---

### Task 1.1: 新建 `StTransferPartyComponent`

- [ ] **Step 1: 创建 `SovereignTowns/src/Parties/StTransferPartyComponent.cs`**

```csharp
using System;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 调拨队伍组件。隐式状态（不用 enum），由 Source / Destination / TargetSettlement 推断。
/// 不应用 ShouldReturnAndDisband 判定 — 兵员是货物，到达前不应中途解散。
/// </summary>
public sealed class StTransferPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_transfer_";

    [SaveableField(20)] private Settlement? _source;
    [SaveableField(21)] private Settlement? _destination;
    [CachedData] private TextObject? _cachedName;

    public Settlement? Source => _source;
    public Settlement? Destination => _destination;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var srcName = _source?.Name?.ToString() ?? "未知";
            var dstName = _destination?.Name?.ToString() ?? "未知";
            _cachedName = new TextObject("{=ST_TransferPartyName}调拨队 - " + srcName + " → " + dstName);
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => true;
    protected override bool AppliesReturnDisbandCondition => false;  // 调拨队不应用回城解散判定

    private StTransferPartyComponent(
        Settlement source, Settlement destination,
        TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(source, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _source = source;
        _destination = destination;
    }

    /// 工厂：创建调拨队伍。失败返回 null，不抛。
    public static MobileParty? CreateForRoute(Settlement source, Settlement destination, TroopRoster troops)
    {
        if (source == null || destination == null || troops == null) return null;
        try
        {
            var ownerClan = source.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"StTransferPartyComponent.CreateForRoute: source '{source.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(source.GatePosition, 1f, ownerClan, troops, emptyPrisoners);

            var nameObj = new TextObject(
                "{=ST_TransferPartyName}调拨队 - " + source.Name + " → " + destination.Name);

            var component = new StTransferPartyComponent(
                source: source, destination: destination,
                name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: true,
                args: args, leader: null);

            var stringId = StringIdPrefix + source.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"StTransferPartyComponent.CreateForRoute: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }
            try { mobileParty.Aggressiveness = 0f; } catch { }

            component.SnapshotInitialMembers(mobileParty);

            Logger.Info($"StTransferPartyComponent: created '{stringId}' for '{source.StringId}' → '{destination.StringId}'");
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StTransferPartyComponent.CreateForRoute: unexpected exception", ex);
            return null;
        }
    }

    /// 隐式状态机：dest owner 变更 / dest 危险 → 改返 source；正常情况由基类 IsAtHome 接管。
    /// 注意：基类的 `home` 是 _source（构造时传入），所以 IsAtHome 自动检测"回到 source"。
    /// 到达 destination 不会触发 IsAtHome，要单独检测。
    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        var dest = _destination;
        if (dest == null) return;

        // 1) 已到达 destination → 注入 garrison + 解散（不走基类的 OnArrivedHome，因为 home == source）
        if (self.LastVisitedSettlement == dest)
        {
            DeliverAndDisband(self, dest);
            return;
        }

        var partyClan = self.ActualClan ?? _source?.OwnerClan ?? dest.OwnerClan;

        // 2) destination owner 变更 → 改返安全 fallback
        if (partyClan != null && dest.OwnerClan != partyClan)
        {
            var fallback = ResolveSafeFallback(partyClan);
            if (fallback != null)
            {
                if (self.LastVisitedSettlement == fallback)
                {
                    Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed; merging into fallback '{fallback.Name}'");
                    DeliverAndDisband(self, fallback);
                }
                else if (self.TargetSettlement != fallback)
                {
                    Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed; rerouting to '{fallback.Name}'");
                    try { self.SetMoveGoToSettlement(fallback, MobileParty.NavigationType.Default, false); }
                    catch (Exception ex) { Logger.Error("rerouting failed", ex); }
                }
            }
            else
            {
                Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed and no safe fallback; disbanding");
                PartyMergeService.Instance.DisbandAndUntrack(self, "StTransferPartyComponent destination lost");
            }
            return;
        }

        // 3) destination 极端危险 → 改返 source（不解散）
        var risk = RiskAssessmentService.Assess(dest);
        if (risk.Level >= RiskLevel.Critical)
        {
            var src = _source;
            if (src != null && self.TargetSettlement != src)
            {
                Logger.Warn($"StTransferParty '{self.Name}': 目的地 '{dest.Name}' risk={risk.Level}，改返 '{src.Name}'");
                try { self.SetMoveGoToSettlement(src, MobileParty.NavigationType.Default, false); }
                catch (Exception ex) { Logger.Error("reroute to source failed", ex); }
            }
        }
    }

    /// 调拨队的 IsAtHome 含义复用为"已抵达 source"——意味着 dest 危险被改回，到家后解散。
    /// 已通过 OnHourlyTickCore 分支 1 单独处理 LastVisitedSettlement == destination 的"到 dest"路径。
    /// 此处覆盖 OnArrivedHome 为返 source 时的 DeliverAndDisband。
    protected override void OnArrivedHome(MobileParty self)
    {
        var src = _source;
        if (src == null) { base.OnArrivedHome(self); return; }
        DeliverAndDisband(self, src);
    }

    private void DeliverAndDisband(MobileParty self, Settlement target)
    {
        int delivered = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, target, "StTransferPartyComponent.DeliverAndDisband");
        Logger.Info($"StTransferParty '{self.Name}': 注入 {delivered} 名兵员到 '{target?.Name}' 驻军，解散队伍");
        PartyMergeService.Instance.DisbandAndUntrack(self, "StTransferPartyComponent.DeliverAndDisband");
    }

    private Settlement? ResolveSafeFallback(Clan partyClan)
    {
        try
        {
            if (partyClan == null) return null;
            var src = _source;
            if (src != null && src.OwnerClan == partyClan) return src;
            return CapitalRegistry.Instance?.GetCapitalForClan(partyClan);
        }
        catch { return null; }
    }
}
```

**关键点**：
- `_source` 同时作为基类的 `_homeSettlement`（构造时传入）。基类 `HomeSettlement` 返回的就是 source。
- "到达 destination" 不走基类 `IsAtHome`（home=source ≠ dest），独立检测。
- "改返 source 后到达 source" 走基类 `IsAtHome` → 覆盖的 `OnArrivedHome` 把 source 当 target 解散。

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`。

---

### Task 1.2: 在 `SovereignTownsTypeDefiner` 注册 `StTransferPartyComponent` LocalId 7

- [ ] **Step 1: 修改 `DefineClassTypes`**

替换 `DefineClassTypes` 方法体为：

```csharp
    protected override void DefineClassTypes()
    {
        // 旧 local id 1/2/3 弃用 — 但 RecruitingPartyComponent / SallyForthPartyComponent 仍存在，
        // 在 Step 3/2 之前继续注册，避免读档时旧档 component 反序列化失败。
        AddClassDefinition(typeof(Parties.RecruitingPartyComponent), 1);
        AddClassDefinition(typeof(Parties.SallyForthPartyComponent), 3);
        // TransferPartyComponent 已被 StTransferPartyComponent 替代，旧类已删，保留 id 占位避免 id collision
        // 注：vanilla 不强制 id 连续，可直接跳过

        AddClassDefinition(typeof(Parties.StPartyComponent), 4);
        AddClassDefinition(typeof(Parties.StTransferPartyComponent), 7);
    }
```

注：`TransferPartyComponent` 的 `AddClassDefinition(typeof(Parties.TransferPartyComponent), 2);` 行**删除**（Task 1.7 会删 .cs 文件，先在此移除 type 注册）。

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`（`TransferPartyComponent` 文件还存在，编译还能找到类型；只是不注册它）。

---

### Task 1.3: `GarrisonTransferManager` 瘦身为 `TransferDispatcher`

- [ ] **Step 1: 重命名文件**

```bash
git mv SovereignTowns/src/Transfer/GarrisonTransferManager.cs SovereignTowns/src/Transfer/TransferDispatcher.cs
```

- [ ] **Step 2: 完全替换文件内容**

打开新 `TransferDispatcher.cs`，全文替换为：

```csharp
using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Transfer;

/// <summary>
/// 调拨队 Dispatcher（B16.1）：从原 GarrisonTransferManager 瘦身而来。
/// 只负责"消费 TransferTask → 抽兵 → 创建 StTransferPartyComponent → 注册到 Lifecycle"。
/// 所有"在飞中"的状态机搬到 StTransferPartyComponent.OnHourlyTickCore。
/// </summary>
public sealed class TransferDispatcher
{
    private const string PartyKind = PartyLifecycleManager.KindTransfer;

    private readonly PartyLifecycleManager _lifecycle;

    public TransferDispatcher(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// 主入口：由 CapitalLogisticsManager 调用，把一个 TransferTask 转换为真实运输队伍。
    public bool TryDispatchTransfer(TransferTask task)
    {
        try
        {
            if (task == null) { Logger.Warn("TryDispatchTransfer: task is null"); return false; }
            var source = task.Source;
            var destination = task.Destination;
            var requested = task.RequestedTroops;

            if (source == null || destination == null)
            {
                Logger.Warn("TryDispatchTransfer: task.Source/Destination is null");
                return false;
            }
            if (requested <= 0) return false;
            if (source == destination) return false;
            if (source.OwnerClan == null || source.OwnerClan != destination.OwnerClan)
            {
                Logger.Warn($"  TransferDispatcher: cross-clan transfer rejected ({source.Name} -> {destination.Name})");
                return false;
            }
            if (!ConfigurationManager.Current.EnabledFeatures.TroopTransfers)
            {
                Logger.Debug($"  TransferDispatcher: skipped '{source.Name}' -> '{destination.Name}' — TroopTransfers disabled");
                return false;
            }
            if (!_lifecycle.CanCreateAnotherParty(source, PartyKind))
            {
                Logger.Info($"  TransferDispatcher: '{source.Name}' 已达调拨队上限，跳过");
                return false;
            }

            var sourceTown = source.Town;
            var sourceGarrison = sourceTown?.GarrisonParty;
            var sourceRoster = sourceGarrison?.MemberRoster;
            if (sourceTown == null || sourceGarrison == null || sourceRoster == null)
            {
                Logger.Warn($"  TransferDispatcher: source '{source.Name}' has no Town/GarrisonParty/MemberRoster");
                return false;
            }

            int totalAvailable = sourceRoster.TotalManCount;
            if (totalAvailable < requested)
            {
                Logger.Info($"  TransferDispatcher: '{source.Name}' total={totalAvailable} < req({requested}), 跳过");
                return false;
            }

            var transferRoster = TroopRoster.CreateDummyTroopRoster();
            int extracted = TroopTransferHelper.TransferFromGarrison(
                sourceRoster, transferRoster, requested, TroopTransferHelper.SortStrategy.LowestTierFirst);

            if (extracted <= 0)
            {
                Logger.Warn($"  TransferDispatcher: '{source.Name}' extracted 0 troops (req={requested}, available={totalAvailable})");
                return false;
            }

            var party = StTransferPartyComponent.CreateForRoute(source, destination, transferRoster);
            if (party == null)
            {
                Logger.Warn($"  TransferDispatcher: CreateForRoute 返回 null ({source.Name} -> {destination.Name})");
                TroopTransferHelper.TransferBackToGarrison(transferRoster, sourceRoster);
                return false;
            }

            _lifecycle.RegisterTrackedParty(party, source, PartyKind);
            try { party.SetMoveGoToSettlement(destination, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error("SetMoveGoToSettlement initial failed", ex); }

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchTransfer",
                inputSummary: $"source={source.StringId} dest={destination.StringId} requested={requested} extracted={extracted} priority={task.Priority:F2} reason={task.Reason}",
                decisionJson: $"{{\"source\":\"{source.StringId}\",\"dest\":\"{destination.StringId}\",\"requested\":{requested},\"extracted\":{extracted},\"priority\":{task.Priority:F2}}}",
                accepted: true);
            Logger.Info($"  TransferDispatcher: 派出调拨队 '{source.Name}' -> '{destination.Name}' (兵员={extracted}, priority={task.Priority:F1})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchTransfer failed", ex);
            return false;
        }
    }
}
```

**关键差异 vs 原 `GarrisonTransferManager`**：
- 删除 `OnHourlyTickParty` 方法（状态机搬到 component）
- 删除 `_mergeService` 字段（component 自己调 Instance）
- 删除 `ExtractLowestTierTroops`（用 helper）/ `DeliverAndDisband` / `ResolveSafeFallback` / `TryRestoreToSource`（搬到 component 或用 helper）
- 类名 `GarrisonTransferManager` → `TransferDispatcher`

- [ ] **Step 3: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

Expected：会有 errors（`GarrisonTransferManager` 在 `SovereignTownsCampaignBehavior` / `CapitalLogisticsManager` / `STPartySizeLimitModel` 等处被引用）。Task 1.4-1.6 修复。

---

### Task 1.4: 更新 `CapitalLogisticsManager` 构造参数类型

- [ ] **Step 1: 找出 GarrisonTransferManager 引用**

```bash
grep -rn "GarrisonTransferManager" SovereignTowns/src/
```

- [ ] **Step 2: `CapitalLogisticsManager.cs` 替换类型**

把 `GarrisonTransferManager` 全部改为 `TransferDispatcher`（包括字段、构造形参、调用方法）。`TryDispatchTransfer` 方法名不变。

- [ ] **Step 3: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

Expected：CapitalLogisticsManager 错误消失；仍有 SovereignTownsCampaignBehavior 和其他文件的错误。

---

### Task 1.5: 更新 `SovereignTownsCampaignBehavior` 中的引用与事件转发

- [ ] **Step 1: 字段类型改名**

打开 `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`，找：

```csharp
    private GarrisonTransferManager? _transferManager;
```

改为：

```csharp
    private TransferDispatcher? _transferDispatcher;
```

字段名 `_transferManager` → `_transferDispatcher`（全文 replace_all）。

- [ ] **Step 2: 构造点改名**

找：

```csharp
            _transferManager = new GarrisonTransferManager(_lifecycle);
```

改为：

```csharp
            _transferDispatcher = new TransferDispatcher(_lifecycle);
```

- [ ] **Step 3: `_capitalLogisticsManager` 构造形参改名**

找：

```csharp
            _capitalLogisticsManager = new CapitalLogisticsManager(
                _capitalRegistry,
                _recruitmentManager,
                _transferManager);
```

改为：

```csharp
            _capitalLogisticsManager = new CapitalLogisticsManager(
                _capitalRegistry,
                _recruitmentManager,
                _transferDispatcher);
```

- [ ] **Step 4: 移除 `OnHourlyTickParty` 中的 transfer 转发**

找：

```csharp
            _recruitmentManager?.OnHourlyTickParty(party);
            _transferManager?.OnHourlyTickParty(party);
            _patrolManager?.OnHourlyTickParty(party);
            _sallyForthManager?.OnHourlyTickParty(party);
```

**删除** `_transferManager?.OnHourlyTickParty(party);` 这一行（dispatcher 不再有此方法；component 由 lifecycle 单点路由 — 但 lifecycle 当前还不分派到 component，要等 Task 1.6）。

注：保留 `_recruitmentManager` / `_patrolManager` / `_sallyForthManager` 的转发，它们在后续 Step 才迁移。

- [ ] **Step 5: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

Expected：CampaignBehavior 错误消失；还有 STPartySizeLimitModel + PartyLifecycleManager 等错误。

---

### Task 1.6: `PartyLifecycleManager` 单点分派 + `RebuildFromCampaign` 收编 `StTransferPartyComponent`

- [ ] **Step 1: `OnHourlyTickParty` 加 Component 分派**

打开 `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`，找到 `OnHourlyTickParty(MobileParty party)` 方法（约 line 435）。在 `try { ... }` 末尾（也就是既有 idle 检测代码之后）追加：

```csharp
            // B16.1：单点路由到 StPartyComponent.OnHourlyTick
            if (party.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
            {
                try { stc.OnHourlyTick(party); }
                catch (Exception ex) { Logger.Error($"StPartyComponent.OnHourlyTick failed for '{PartyNameFormatter.SafeName(party)}'", ex); }
            }
```

- [ ] **Step 2: 新增 `OnMapEventEnded(MapEvent)` 方法**

在 `PartyLifecycleManager.cs` 中找 `OnHourlyTickParty` 方法之后，加入：

```csharp
    /// <summary>
    /// B16.1：vanilla MapEventEnded 路由入口。由 SovereignTownsCampaignBehavior 转发，
    /// 单点分派给所有参战 StPartyComponent。
    /// </summary>
    public void OnMapEventEnded(TaleWorlds.CampaignSystem.MapEvents.MapEvent ev)
    {
        if (ev == null) return;
        try
        {
            HandleSideEndOfEvent(ev.AttackerSide, ev);
            HandleSideEndOfEvent(ev.DefenderSide, ev);
        }
        catch (Exception ex)
        {
            Logger.Error("PartyLifecycleManager.OnMapEventEnded failed", ex);
        }
    }

    private void HandleSideEndOfEvent(TaleWorlds.CampaignSystem.MapEvents.MapEventSide? side, TaleWorlds.CampaignSystem.MapEvents.MapEvent ev)
    {
        if (side?.Parties == null) return;
        try
        {
            foreach (var uop in side.Parties)
            {
                MobileParty? mp = null;
                try { mp = uop.Party?.MobileParty; }
                catch { continue; }
                if (mp == null || !mp.IsActive) continue;
                if (mp.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
                {
                    try { stc.OnMapEventEnded(ev, mp); }
                    catch (Exception ex) { Logger.Error($"StPartyComponent.OnMapEventEnded failed for '{PartyNameFormatter.SafeName(mp)}'", ex); }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HandleSideEndOfEvent iteration failed", ex);
        }
    }
```

- [ ] **Step 3: `RebuildFromCampaign` 中加 `StTransferPartyComponent` 分支**

找 `RebuildFromCampaign` 方法的 1) RecruitingPartyComponent / TransferPartyComponent 分支块（约 line 175-210）。在 `else if (comp is TransferPartyComponent tp)` 之后**追加**新分支：

```csharp
                            else if (comp is SovereignTowns.Parties.StTransferPartyComponent stp)
                            {
                                var home = stp.Source;
                                if (home == null) { skipped++; continue; }
                                int mc = PartyNameFormatter.SafeMemberCount(party);
                                _tracked[party] = new TrackedPartyMeta(home, KindTransfer, now, party.TargetSettlement, mc, SafeActualClan(party, home), mc);
                                transfers++;
                            }
```

**保留**旧 `else if (comp is TransferPartyComponent tp)` 分支不删 — Task 1.7 删除旧类后此分支自动失效（编译错误）才能彻底删除。

- [ ] **Step 4: `SovereignTownsCampaignBehavior.OnMapEventEnded` 加 lifecycle 转发**

打开 `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`，找 `OnMapEventEnded`（约 line 339-350）：

```csharp
    private void OnMapEventEnded(MapEvent mapEvent)
    {
        try
        {
            _battleLootManager?.OnMapEventEnded(mapEvent);
            _sallyForthManager?.OnMapEventEnded(mapEvent);
            _patrolManager?.OnMapEventEnded(mapEvent);
            _recruitmentManager?.OnMapEventEnded(mapEvent);
        }
        catch (Exception ex)
        {
            Logger.Error("OnMapEventEnded forwarding failed", ex);
        }
    }
```

改为：

```csharp
    private void OnMapEventEnded(MapEvent mapEvent)
    {
        try
        {
            _battleLootManager?.OnMapEventEnded(mapEvent);
            _lifecycle?.OnMapEventEnded(mapEvent);   // ← B16.1：单点路由到 StPartyComponent
            // 仍保留旧 Manager 转发，它们在后续 Step 才迁移
            _sallyForthManager?.OnMapEventEnded(mapEvent);
            _patrolManager?.OnMapEventEnded(mapEvent);
            _recruitmentManager?.OnMapEventEnded(mapEvent);
        }
        catch (Exception ex)
        {
            Logger.Error("OnMapEventEnded forwarding failed", ex);
        }
    }
```

- [ ] **Step 5: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

Expected：还有 STPartySizeLimitModel 错误。

---

### Task 1.7: `STPartySizeLimitModel` 改类型引用

- [ ] **Step 1: 替换类型引用**

打开 `SovereignTowns/src/Models/STPartySizeLimitModel.cs`，找：

```csharp
            if (comp is TransferPartyComponent transfer)
```

改为：

```csharp
            if (comp is StTransferPartyComponent transfer)
```

`ComputeTransferLimit` 形参类型同步改：

```csharp
    private static int ComputeTransferLimit(MobileParty? party, TransferPartyComponent transfer)
```

改为：

```csharp
    private static int ComputeTransferLimit(MobileParty? party, StTransferPartyComponent transfer)
```

方法体内 `transfer.Source` 调用保持不变（`StTransferPartyComponent` 提供同名属性）。

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`（旧 `TransferPartyComponent.cs` 文件还存在但已无引用）。

---

### Task 1.8: 删除旧 `TransferPartyComponent.cs` + 清理 `RebuildFromCampaign` 旧分支

- [ ] **Step 1: 删除旧文件**

```bash
git rm SovereignTowns/src/Parties/TransferPartyComponent.cs
```

- [ ] **Step 2: `PartyLifecycleManager.RebuildFromCampaign` 删除旧 `TransferPartyComponent` 分支**

找 Task 1.6 Step 3 保留的旧分支：

```csharp
                            else if (comp is TransferPartyComponent tp)
                            {
                                var home = tp.Source;
                                if (home == null) { skipped++; continue; }
                                int mc = PartyNameFormatter.SafeMemberCount(party);
                                _tracked[party] = new TrackedPartyMeta(home, KindTransfer, now, party.TargetSettlement, mc, SafeActualClan(party, home), mc);
                                transfers++;
                            }
```

**删除**这一段（编译应该已经报错）。

- [ ] **Step 3: grep 全文残留检查**

```bash
grep -rn "TransferPartyComponent" SovereignTowns/src/
```

Expected：**仅 `StTransferPartyComponent` 匹配，无单独 `TransferPartyComponent`**。
（注：`StTransferPartyComponent` 包含 `TransferPartyComponent` 子串，匹配只要看是否前缀 `St` 即可。）

可以用更严格的：

```bash
grep -rn '\bTransferPartyComponent\b' SovereignTowns/src/ | grep -v StTransferPartyComponent
```

Expected: 0 matches.

- [ ] **Step 4: 最终编译**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`ok dotnet build: 1 projects, 0 errors, 0 warnings`

---

### Task 1.9: Step 1 commit

- [ ] **Step 1: Commit**

```bash
git add SovereignTowns/

git commit -m "$(cat <<'EOF'
B16.1: 迁移 Transfer — StTransferPartyComponent + TransferDispatcher

- 新建 StTransferPartyComponent : StPartyComponent（隐式状态机）
  AppliesReturnDisbandCondition=false（调拨队不解散）
- GarrisonTransferManager.cs → TransferDispatcher.cs（瘦身为工厂）
  删除 OnHourlyTickParty / DeliverAndDisband / ExtractLowestTierTroops（搬到 component）
- TypeDefiner 注册 LocalId 7（旧 LocalId 2 弃用）
- PartyLifecycleManager 新增 OnMapEventEnded + 单点分派到 StPartyComponent
- CampaignBehavior 移除 _transferManager OnHourlyTickParty 转发
- STPartySizeLimitModel 改类型引用
- 删除旧 TransferPartyComponent.cs

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 2: 验证 commit 内容**

```bash
git show --stat HEAD
```

Expected: 包含 `StTransferPartyComponent.cs` (create) + `TransferDispatcher.cs` (rename from `GarrisonTransferManager.cs`) + `TransferPartyComponent.cs` (delete) + 数个 modify。

---

## Step 2 — 迁移 Sally

**目标**：新建 `StSallyPartyComponent` 替代 `SallyForthPartyComponent`；`SallyForthManager` 瘦身为 `SallyDispatcher`；删除旧文件。

**Files:**
- Create: `SovereignTowns/src/Parties/StSallyPartyComponent.cs`
- Modify: `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`
- Replace: `SovereignTowns/src/SallyForth/SallyForthManager.cs` → `SovereignTowns/src/SallyForth/SallyDispatcher.cs`
- Modify: `SovereignTowns/src/Models/STPartySizeLimitModel.cs`
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
- Modify: `SovereignTowns/src/Patrol/PatrolManager.cs`（`GetActiveCombatSallyParties` 调用方）
- Modify: `SovereignTowns/src/Battle/BattleLootManager.cs`（如有 `SallyForthPartyComponent` 引用）
- Modify: `SovereignTowns/src/Ui/STPartyDialogRegistration.cs`（如有 `SallyForthPartyComponent` 引用）
- Delete: `SovereignTowns/src/Parties/SallyForthPartyComponent.cs`

---

### Task 2.1: 新建 `StSallyPartyComponent`

- [ ] **Step 1: Grep 旧 `SallyForthManager` 中的 sally 业务逻辑入口**

```bash
grep -n "_lastSallyEndedAt\|_enemySustainedTicks\|_forceReturnLogged\|_targetLostLogged\|MaxSallyHours\|ReleaseAiAndReturnHome\|TaskExpired" SovereignTowns/src/SallyForth/SallyForthManager.cs | head -30
```

记录关键字段与方法（用于搬到 component）。

- [ ] **Step 2: 创建 `SovereignTowns/src/Parties/StSallyPartyComponent.cs`**

```csharp
using System;
using System.Collections.Generic;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 出击队伍组件（B16.2）。显式 enum 状态机（Engaging / Returning）。
/// 战后无条件回家（覆盖基类的 ShouldReturnAndDisband 判定 — 任务特性）。
/// </summary>
public sealed class StSallyPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_sally_";
    private const float MaxSallyHours = 12f;

    public enum SallyPhase { Engaging, Returning }

    [SaveableField(20)] private MobileParty? _targetParty;
    [SaveableField(21)] private CampaignTime _departureTime;
    [SaveableField(22)] private SallyPhase _phase = SallyPhase.Engaging;
    [CachedData] private TextObject? _cachedName;
    [CachedData] private bool _forceReturnLogged;
    [CachedData] private bool _targetLostLogged;

    public MobileParty? TargetParty { get => _targetParty; set => _targetParty = value; }
    public CampaignTime DepartureTime => _departureTime;
    public SallyPhase Phase => _phase;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var settlementName = HomeSettlement?.Name?.ToString() ?? "未知";
            _cachedName = new TextObject("{=ST_SallyPartyName}出击队 - " + settlementName);
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => false;

    private StSallyPartyComponent(
        Settlement home, MobileParty? initialTarget,
        TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _targetParty = initialTarget;
        _departureTime = CampaignTime.Now;
    }

    /// 工厂：创建出击队伍。失败返回 null。
    /// 注意：CapitalRegistry.ShouldChargeClan + ModTreasury.Charge 由调用方 (SallyDispatcher) 完成。
    public static MobileParty? CreateForTown(Town homeTown, MobileParty? initialTarget = null)
    {
        if (homeTown == null) { Logger.Error("StSallyPartyComponent.CreateForTown: homeTown is null"); return null; }
        try
        {
            var settlement = homeTown.Settlement;
            if (settlement == null) { Logger.Error("StSallyPartyComponent.CreateForTown: homeTown.Settlement is null"); return null; }
            var ownerClan = settlement.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"StSallyPartyComponent.CreateForTown: town '{settlement.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyTroops = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(settlement.GatePosition, 1f, ownerClan, emptyTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_SallyPartyName}出击队 - " + settlement.Name);

            var component = new StSallyPartyComponent(
                home: settlement, initialTarget: initialTarget,
                name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: false,
                args: args, leader: null);

            var stringId = StringIdPrefix + settlement.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"StSallyPartyComponent.CreateForTown: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }
            try { mobileParty.Aggressiveness = 0f; } catch { }

            // 注：troops 由 SallyDispatcher 通过 TransferTroopsFromGarrison 注入 + SnapshotInitialMembers
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StSallyPartyComponent.CreateForTown: unexpected exception", ex);
            return null;
        }
    }

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        switch (_phase)
        {
            case SallyPhase.Engaging:
                // 1) 超时 → 强制回家
                var hoursAway = (CampaignTime.Now - _departureTime).ToHours;
                if (hoursAway > MaxSallyHours)
                {
                    if (!_forceReturnLogged)
                    {
                        Logger.Warn($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' away {hoursAway:F1}h > {MaxSallyHours}h, force return to '{HomeSettlement?.Name}'");
                        _forceReturnLogged = true;
                    }
                    TransitionToReturning(self);
                    return;
                }
                // 2) target 进入 settlement → vanilla AI bug 规避，立即回家
                var target = _targetParty;
                if (target != null && target.IsActive && target.CurrentSettlement != null)
                {
                    Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' target '{PartyNameFormatter.SafeName(target)}' entered '{target.CurrentSettlement.Name}', returning home");
                    TransitionToReturning(self);
                    return;
                }
                // 3) target null/dead → 释放 vanilla AI 接管
                if (target == null || !target.IsActive)
                {
                    if (!_targetLostLogged)
                    {
                        Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' target lost, releasing AI for re-decision");
                        _targetLostLogged = true;
                    }
                    try { self.Ai?.SetDoNotMakeNewDecisions(false); }
                    catch (Exception aiEx) { Logger.Error("SetDoNotMakeNewDecisions(false) failed", aiEx); }
                }
                else
                {
                    _targetLostLogged = false;
                }
                return;
            case SallyPhase.Returning:
                // base.IsAtHome 接管解散
                return;
        }
    }

    /// 战后无条件回家（覆盖基类的 ShouldReturnAndDisband 路径只是先于此方法）。
    /// 即使 ShouldReturnAndDisband 命中（基类已 ReturnToHome），未命中也走这里同样回家。
    protected override void OnMapEventEndedCore(MapEvent ev, MobileParty self)
    {
        TransitionToReturning(self);
    }

    /// sally 到达 home → 转兵 + destroy（注意是 destroy，不是 disband — 与原 SallyForthManager.TransferAndDestroy 对齐）。
    protected override void OnArrivedHome(MobileParty self)
    {
        var home = HomeSettlement;
        if (home == null)
        {
            PartyMergeService.Instance.DisbandAndUntrack(self, "StSallyPartyComponent null home");
            return;
        }
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, home, "StSallyPartyComponent.OnArrivedHome");
        Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into '{home.Name}', destroying");
        PartyMergeService.Instance.DestroyAndUntrack(self, "StSallyPartyComponent.OnArrivedHome", deferIfInMapEvent: false);
    }

    /// 销毁回调：救援存活兵到 home garrison（沿用现有 OnMobilePartyDestroyed 逻辑）。
    public override void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        try
        {
            var home = HomeSettlement;
            var partyClan = self.ActualClan ?? home?.OwnerClan;
            Settlement? rescueTarget = null;
            var registry = CapitalRegistry.Instance;
            if (registry != null && partyClan != null)
            {
                if (home != null && home.OwnerClan == partyClan && registry.IsManagedClanWithCapital(partyClan))
                    rescueTarget = home;
                else
                    rescueTarget = registry.GetCapitalForClan(partyClan);
            }

            if (rescueTarget == null)
            {
                Logger.Info($"StSallyParty.OnDestroyed: '{PartyNameFormatter.SafeName(self)}' home unavailable, no rescue");
                return;
            }

            int rescued = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, rescueTarget, "StSallyPartyComponent.OnDestroyed");
            if (rescued > 0)
                Logger.Info($"StSallyParty.OnDestroyed: rescued {rescued} survivors to '{rescueTarget.Name}'");
        }
        catch (Exception ex)
        {
            Logger.Error($"StSallyPartyComponent.OnDestroyed failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    private void TransitionToReturning(MobileParty self)
    {
        _phase = SallyPhase.Returning;
        try { self.Ai?.SetDoNotMakeNewDecisions(false); }
        catch (Exception ex) { Logger.Error("SetDoNotMakeNewDecisions(false) failed", ex); }
        ReturnToHome(self);  // 基类提供
    }
}
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`。

---

### Task 2.2: TypeDefiner 注册 LocalId 8 + 移除旧 SallyForthPartyComponent 注册

- [ ] **Step 1: 修改 `DefineClassTypes`**

替换为：

```csharp
    protected override void DefineClassTypes()
    {
        // 仅剩 RecruitingPartyComponent 待 Step 3 迁移
        AddClassDefinition(typeof(Parties.RecruitingPartyComponent), 1);
        AddClassDefinition(typeof(Parties.StPartyComponent), 4);
        AddClassDefinition(typeof(Parties.StTransferPartyComponent), 7);
        AddClassDefinition(typeof(Parties.StSallyPartyComponent), 8);
    }
```

注：`SallyForthPartyComponent` 注册行删除（Task 2.7 删 .cs 文件之前先在此移除）。

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected：`0 errors / 0 warnings`（旧类文件还在）。

---

### Task 2.3: `SallyForthManager` 瘦身为 `SallyDispatcher`

- [ ] **Step 1: 重命名文件**

```bash
git mv SovereignTowns/src/SallyForth/SallyForthManager.cs SovereignTowns/src/SallyForth/SallyDispatcher.cs
```

- [ ] **Step 2: 全文替换为**：

```csharp
using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.SallyForth;

/// <summary>
/// 出击队 Dispatcher（B16.2）：由 SallyForthManager 瘦身而来。
/// 只负责"何时何地派遣出击队"：评估敌方威胁、扣 ModTreasury、抽兵创建 StSallyPartyComponent。
/// 所有"在飞中"的状态机搬到 StSallyPartyComponent。
///
/// 保留接口：GetActiveCombatSallyParties(Clan) — 供 StPatrolPartyComponent 支援判定查询。
/// </summary>
public sealed class SallyDispatcher
{
    private const float DetectionRadius = 50f;
    private const int InitialSallyGold = 100;
    private const float SallyCooldownHours = 24f;
    private const int MinSustainedTicks = 3;

    private static float SallyExtractionRatio
        => ConfigurationManager.Current?.Thresholds?.SallyExtractionRatio ?? 0.60f;
    private static float SallyTargetPartySizeMultiplier
        => ConfigurationManager.Current?.Thresholds?.SallyTargetPartySizeMultiplier ?? 2.0f;
    private static int SallyCreateMinPartyCount
        => ConfigurationManager.Current?.Thresholds?.SallyCreateMinPartyCount ?? 30;

    private readonly Dictionary<Settlement, CampaignTime> _lastSallyEndedAt = new();
    private readonly Dictionary<Settlement, int> _enemySustainedTicks = new();

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public SallyDispatcher(PartyLifecycleManager lifecycle, CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }

    public void OnHourlyTickSettlement(Settlement settlement)
    {
        if (settlement == null || !settlement.IsTown) return;
        try
        {
            var owningMgr = _capitalRegistry?.GetForSettlement(settlement);
            if (owningMgr is null) return;
            var usableCapital = _capitalRegistry?.GetCapitalForClan(owningMgr.OwnerClan);
            if (usableCapital == null) return;
            if (!ConfigurationManager.Current.EnabledFeatures.SallyForth) return;
            if (usableCapital.Town == null) return;
            if (settlement.IsUnderSiege) return;
            if (!_lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindSallyForth)) return;

            var garrison = settlement.Town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;
            var target = FindBestEnemyTarget(settlement);
            if (target == null)
            {
                _enemySustainedTicks.Remove(settlement);
                return;
            }
            if (_lastSallyEndedAt.TryGetValue(settlement, out var lastEnd))
            {
                var hoursSinceLast = (CampaignTime.Now - lastEnd).ToHours;
                if (hoursSinceLast < SallyCooldownHours)
                {
                    Logger.Debug($"SallyDispatcher '{PartyNameFormatter.SafeName(settlement)}': 冷却中 ({hoursSinceLast:F1}h < {SallyCooldownHours}h)");
                    return;
                }
            }
            int prevTicks = _enemySustainedTicks.TryGetValue(settlement, out var p) ? p : 0;
            int newTicks = prevTicks + 1;
            _enemySustainedTicks[settlement] = newTicks;
            if (newTicks < MinSustainedTicks)
            {
                Logger.Debug($"SallyDispatcher '{PartyNameFormatter.SafeName(settlement)}': 敌方 '{PartyNameFormatter.SafeName(target)}' 已见 {newTicks}/{MinSustainedTicks} 小时");
                return;
            }

            TryCreateSallyParty(settlement, garrison!, garrisonCount, target);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyDispatcher.OnHourlyTickSettlement failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    /// 供 StPatrolPartyComponent 查询：本氏族当前正在 MapEvent 中战斗的 sally。
    public List<MobileParty> GetActiveCombatSallyParties(Clan clan)
    {
        var result = new List<MobileParty>();
        if (clan == null) return result;
        try
        {
            foreach (var party in MobileParty.AllCustomParties)
            {
                if (party == null || !party.IsActive) continue;
                if (party.PartyComponent is not StSallyPartyComponent sc) continue;
                if (sc.HomeSettlement?.OwnerClan != clan) continue;
                if (party.MapEvent == null) continue;
                result.Add(party);
            }
        }
        catch (Exception ex) { Logger.Error("GetActiveCombatSallyParties failed", ex); }
        return result;
    }

    /// 通知本 settlement 的 sally 周期已结束（StSallyPartyComponent 在 destroy 时调用）。
    public void NotifySallyEnded(Settlement home)
    {
        if (home == null) return;
        try
        {
            _lastSallyEndedAt[home] = CampaignTime.Now;
            _enemySustainedTicks.Remove(home);
        }
        catch { }
    }

    private void TryCreateSallyParty(Settlement settlement, MobileParty garrison, int garrisonCount, MobileParty target)
    {
        try
        {
            var ruleSally = settlement.Town != null ? ConfigurationManager.GetRuleFor(settlement.Town) : null;
            float minimumDefenderRatio = ruleSally?.MinimumDefenderRatio ?? Configuration.TownGarrisonRule.CreateDefault().MinimumDefenderRatio;
            int minDef = GarrisonThresholdMath.CountFromRatio(garrisonCount, minimumDefenderRatio, 0);
            int extractable = Math.Max(0, garrisonCount - minDef);
            int byGarrisonRatio = GarrisonThresholdMath.CountFromRatio(garrisonCount, SallyExtractionRatio, 0);
            int targetMen = Math.Max(0, target.MemberRoster?.TotalManCount ?? 0);
            int byTarget = Math.Max(0, (int)Math.Ceiling(targetMen * SallyTargetPartySizeMultiplier));
            int sallySize = Math.Min(byTarget, Math.Min(extractable, byGarrisonRatio));
            int createMin = SallyCreateMinPartyCount;
            if (sallySize < createMin)
            {
                Logger.Debug($"SallyDispatcher: '{settlement.Name}' sallySize={sallySize} < {createMin}, 抽兵过少");
                return;
            }
            if (settlement.Town == null) return;

            bool shouldChargeSally = CapitalRegistry.ShouldChargeClan(settlement.OwnerClan);
            if (shouldChargeSally)
            {
                if (!ModTreasury.CanAfford(InitialSallyGold))
                {
                    Logger.Info($"SallyDispatcher: '{settlement.Name}' 玩家金币不足 (need {InitialSallyGold})");
                    return;
                }
                if (!ModTreasury.Charge(ExpenseCategory.SallySeed, InitialSallyGold, $"sally_seed home={settlement.StringId}"))
                {
                    Logger.Info($"SallyDispatcher: '{settlement.Name}' ModTreasury.Charge 拒绝");
                    return;
                }
            }

            var sallyParty = StSallyPartyComponent.CreateForTown(settlement.Town, target);
            if (sallyParty == null)
            {
                Logger.Warn($"SallyDispatcher: CreateForTown returned null for '{settlement.Name}'");
                return;
            }

            int moved = TroopTransferHelper.TransferFromGarrison(
                garrison.MemberRoster, sallyParty.MemberRoster, sallySize, TroopTransferHelper.SortStrategy.HighestTierFirst);
            if (moved < createMin)
            {
                Logger.Warn($"SallyDispatcher: '{settlement.Name}' transferred only {moved} troops < createMin {createMin}, aborting");
                TroopTransferHelper.TransferBackToGarrison(sallyParty.MemberRoster, garrison.MemberRoster);
                PartyMergeService.Instance.DestroyAndUntrack(sallyParty, "SallyDispatcher rollback", deferIfInMapEvent: false);
                return;
            }

            // ★ 兵员注入完成后立即 snapshot 出发兵员
            if (sallyParty.PartyComponent is StSallyPartyComponent sc) sc.SnapshotInitialMembers(sallyParty);

            try
            {
                sallyParty.Ai?.SetDoNotMakeNewDecisions(true);
                sallyParty.SetMoveEngageParty(target, MobileParty.NavigationType.Default);
                sallyParty.ShouldJoinPlayerBattles = false;
            }
            catch (Exception aiEx) { Logger.Error($"SallyDispatcher: AI directive failed", aiEx); }

            _lifecycle.RegisterTrackedParty(sallyParty, settlement, PartyLifecycleManager.KindSallyForth);

            DecisionAuditLogger.LogRule(
                decisionType: "create_sally_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} moved={moved} target={target.StringId}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{sallyParty.StringId}\",\"target\":\"{target.StringId}\",\"moved\":{moved}}}",
                accepted: true);
            Logger.Info($"SallyDispatcher: created sally for '{settlement.Name}' (moved={moved}, target='{PartyNameFormatter.SafeName(target)}')");
        }
        catch (Exception ex) { Logger.Error($"SallyDispatcher.TryCreateSallyParty failed for '{PartyNameFormatter.SafeName(settlement)}'", ex); }
    }

    private static MobileParty? FindBestEnemyTarget(Settlement settlement)
    {
        try
        {
            var ownFaction = settlement.MapFaction;
            if (ownFaction == null) return null;
            MobileParty? best = null;
            float bestStrength = float.MaxValue;
            var search = MobileParty.StartFindingLocatablesAroundPosition(settlement.GetPosition2D, DetectionRadius);
            for (var c = MobileParty.FindNextLocatable(ref search); c != null; c = MobileParty.FindNextLocatable(ref search))
            {
                if (!c.IsActive || c == MobileParty.MainParty) continue;
                var faction = c.MapFaction;
                if (faction == null || !faction.IsAtWarWith(ownFaction)) continue;
                var strength = 0f;
                try { strength = c.MemberRoster?.TotalManCount ?? 0; } catch { }
                if (strength < bestStrength) { bestStrength = strength; best = c; }
            }
            return best;
        }
        catch (Exception ex) { Logger.Error($"FindBestEnemyTarget failed for '{PartyNameFormatter.SafeName(settlement)}'", ex); return null; }
    }
}
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

Expected：会有 errors（SovereignTownsCampaignBehavior 字段类型、Patrol 调用 GetActiveCombatSallyParties、STPartySizeLimitModel、Lifecycle.RebuildFromCampaign、BattleLootHandler / BattleLootManager / STPartyDialogRegistration 仍引用 `SallyForthPartyComponent`）。Task 2.4-2.8 逐一修复。

---

### Task 2.4: 更新 SovereignTownsCampaignBehavior

- [ ] **Step 1: 字段类型 + 名字改名**

字段 `_sallyForthManager` → `_sallyDispatcher`，类型 `SallyForthManager` → `SallyDispatcher`（全文 replace_all）。

构造点：`new SallyForthManager(_lifecycle, _capitalRegistry, _battleLootManager)` → `new SallyDispatcher(_lifecycle, _capitalRegistry)`（注：`SallyDispatcher` 构造不再要 `_battleLootManager`，那个之前是 sally manager 内部用的，dispatcher 不需要）。

- [ ] **Step 2: 移除 `_sallyForthManager?.OnHourlyTickParty(party)` 和 `_sallyForthManager?.OnMapEventEnded(mapEvent)` 转发**

`OnHourlyTickParty` 方法体改为：

```csharp
            _recruitmentManager?.OnHourlyTickParty(party);
            _patrolManager?.OnHourlyTickParty(party);
            // sally / transfer 已迁移到 component；由 lifecycle 单点路由
```

`OnMapEventEnded` 方法体改为：

```csharp
            _battleLootManager?.OnMapEventEnded(mapEvent);
            _lifecycle?.OnMapEventEnded(mapEvent);
            _patrolManager?.OnMapEventEnded(mapEvent);
            _recruitmentManager?.OnMapEventEnded(mapEvent);
            // sally / transfer 已迁移到 component；由 lifecycle 单点路由
```

- [ ] **Step 3: 处理 sally rescue lambda（line 176）**

找现有：

```csharp
                CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, (party, destroyer) =>
                {
                    try { _sallyForthManager?.OnMobilePartyDestroyed(party, destroyer); }
                    catch (Exception lambdaEx) { Logger.Error("MobilePartyDestroyed sally rescue lambda threw", lambdaEx); }
                });
```

改为：

```csharp
                CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, (party, destroyer) =>
                {
                    try
                    {
                        if (party?.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
                            stc.OnDestroyed(party, destroyer);
                    }
                    catch (Exception lambdaEx) { Logger.Error("MobilePartyDestroyed component-dispatch lambda threw", lambdaEx); }
                });
```

- [ ] **Step 4: 调用 `_patrolManager` 构造时 sally 引用改名**

找：

```csharp
            _patrolManager = new PatrolManager(_lifecycle, _capitalRegistry, _sallyForthManager, _battleLootManager);
```

改为：

```csharp
            _patrolManager = new PatrolManager(_lifecycle, _capitalRegistry, _sallyDispatcher, _battleLootManager);
```

- [ ] **Step 5: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

---

### Task 2.5: 更新 `PatrolManager` 中的 sally 引用类型

- [ ] **Step 1: 字段类型 + 构造形参类型替换**

`SovereignTowns/src/Patrol/PatrolManager.cs`：

字段 `private readonly SovereignTowns.SallyForth.SallyForthManager? _sallyForthManager;` 改为：

```csharp
    private readonly SovereignTowns.SallyForth.SallyDispatcher? _sallyDispatcher;
```

构造形参 + 字段赋值同步改 `_sallyForthManager` → `_sallyDispatcher`（全文 replace_all）。

`GetActiveCombatSallyParties` 方法签名不变，但 sally party 的类型从 `SallyForthPartyComponent` 改为 `StSallyPartyComponent` — Step 2.6 在 dispatcher 实现内已完成此改名。

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

---

### Task 2.6: 更新 `STPartySizeLimitModel`、`Lifecycle.RebuildFromCampaign`、其余 SallyForthPartyComponent 引用

- [ ] **Step 1: `STPartySizeLimitModel.cs`**

```csharp
            if (comp is SallyForthPartyComponent sally)
```

→

```csharp
            if (comp is StSallyPartyComponent sally)
```

形参类型同步：

```csharp
    private static int ComputeSallyLimit(MobileParty? party, SallyForthPartyComponent sally)
```

→

```csharp
    private static int ComputeSallyLimit(MobileParty? party, StSallyPartyComponent sally)
```

- [ ] **Step 2: `PartyLifecycleManager.RebuildFromCampaign` 加 `StSallyPartyComponent` 分支**

在 `else if (comp is SallyForthPartyComponent sp)` 旁追加（保留旧分支待 Task 2.8 删）：

```csharp
                            else if (comp is SovereignTowns.Parties.StSallyPartyComponent stsp)
                            {
                                var home = stsp.HomeSettlement;
                                if (home == null) { skipped++; continue; }
                                int mc = PartyNameFormatter.SafeMemberCount(party);
                                _tracked[party] = new TrackedPartyMeta(home, KindSallyForth, now, party.TargetSettlement, mc, SafeActualClan(party, home), mc);
                                sallyforths++;
                            }
```

- [ ] **Step 3: grep 其余 `SallyForthPartyComponent` 引用并改造**

```bash
grep -rn '\bSallyForthPartyComponent\b' SovereignTowns/src/ | grep -v StSallyPartyComponent
```

清理所有匹配：`BattleLootHandler.cs`、`BattleLootManager.cs`、`STPartyDialogRegistration.cs` 等。统一改为 `StSallyPartyComponent`（语义按 spec §9 — "ST 自管的"）。

- [ ] **Step 4: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

---

### Task 2.7: 在 `StSallyPartyComponent.OnDestroyed` / `OnArrivedHome` 调用 `SallyDispatcher.NotifySallyEnded`

由于 dispatcher 持有 `_lastSallyEndedAt` / `_enemySustainedTicks`，sally 销毁/到家时需要通知它清理。

- [ ] **Step 1: 在 `StSallyPartyComponent.cs` 加 dispatcher 引用 helper**

在 `StSallyPartyComponent` 类内加：

```csharp
    private void NotifyDispatcherEnded()
    {
        try
        {
            var home = HomeSettlement;
            if (home == null) return;
            // SallyDispatcher 注入路径：通过 SovereignTowns.Campaign.SovereignTownsCampaignBehavior 提供静态 accessor
            var dispatcher = SovereignTowns.Campaign.SovereignTownsCampaignBehavior.SallyDispatcher;
            dispatcher?.NotifySallyEnded(home);
        }
        catch { }
    }
```

修改 `OnArrivedHome`：

```csharp
    protected override void OnArrivedHome(MobileParty self)
    {
        var home = HomeSettlement;
        if (home == null)
        {
            PartyMergeService.Instance.DisbandAndUntrack(self, "StSallyPartyComponent null home");
            return;
        }
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, home, "StSallyPartyComponent.OnArrivedHome");
        Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into '{home.Name}', destroying");
        NotifyDispatcherEnded();
        PartyMergeService.Instance.DestroyAndUntrack(self, "StSallyPartyComponent.OnArrivedHome", deferIfInMapEvent: false);
    }
```

修改 `OnDestroyed` 末尾追加：

```csharp
            NotifyDispatcherEnded();
```

- [ ] **Step 2: 在 `SovereignTownsCampaignBehavior` 暴露静态 accessor**

加字段访问器：

```csharp
    private static SallyDispatcher? _staticSallyDispatcher;
    public static SallyDispatcher? SallyDispatcher => _staticSallyDispatcher;
```

在 OnSessionLaunched 构造之后赋值：

```csharp
            _sallyDispatcher = new SallyDispatcher(_lifecycle, _capitalRegistry);
            _staticSallyDispatcher = _sallyDispatcher;
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `0 errors / 0 warnings`。

---

### Task 2.8: 删除旧 `SallyForthPartyComponent.cs` + 清理 `RebuildFromCampaign` 旧分支 + commit

- [ ] **Step 1: 删除旧 component**

```bash
git rm SovereignTowns/src/Parties/SallyForthPartyComponent.cs
```

- [ ] **Step 2: `PartyLifecycleManager.RebuildFromCampaign` 删除旧 `SallyForthPartyComponent` 分支**

找到 Task 2.6 Step 2 保留的旧分支并删除：

```csharp
                            else if (comp is SallyForthPartyComponent sp)
                            {
                                ...
                            }
```

- [ ] **Step 3: grep 残留检查**

```bash
grep -rn '\bSallyForthPartyComponent\b' SovereignTowns/src/ | grep -v StSallyPartyComponent
```

Expected: 0 matches.

```bash
grep -rn '\bSallyForthManager\b' SovereignTowns/src/ | grep -v SallyDispatcher
```

Expected: 0 matches.

- [ ] **Step 4: 最终编译**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `0 errors / 0 warnings`。

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/

git commit -m "$(cat <<'EOF'
B16.2: 迁移 Sally — StSallyPartyComponent + SallyDispatcher

- 新建 StSallyPartyComponent : StPartyComponent（显式 SallyPhase enum 状态机）
  战后无条件回家 (OnMapEventEndedCore)；销毁救援残兵 (OnDestroyed)
- SallyForthManager.cs → SallyDispatcher.cs（瘦身为工厂 + 评估时机）
  保留 GetActiveCombatSallyParties / NotifySallyEnded 接口
- TypeDefiner 注册 LocalId 8（旧 LocalId 3 弃用）
- CampaignBehavior 移除 _sallyForthManager OnHourlyTickParty/OnMapEventEnded 转发
  MobilePartyDestroyed lambda 改为统一 StPartyComponent.OnDestroyed 路由
  新增 static SallyDispatcher accessor（供 component 通知 cooldown 重置）
- PatrolManager 构造形参类型 SallyForthManager → SallyDispatcher
- STPartySizeLimitModel / BattleLoot* / STPartyDialogRegistration 改类型引用
- 删除旧 SallyForthPartyComponent.cs

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Step 3 — 迁移 Recruiter

**目标**：新建 `StRecruiterPartyComponent` 替代 `RecruitingPartyComponent`；`RecruitmentManager` 瘦身为 `RecruitmentDispatcher`；`_visitedThisTrip` 从全局 Dict 移入实例。

**Files:**
- Create: `SovereignTowns/src/Parties/StRecruiterPartyComponent.cs`
- Modify: `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`
- Replace: `SovereignTowns/src/Recruitment/RecruitmentManager.cs` → `SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs`
- Modify: `SovereignTowns/src/Models/STPartySizeLimitModel.cs`
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
- Modify: `SovereignTowns/src/Managers/CapitalLogisticsManager.cs`
- Modify: 其他引用 RecruitingPartyComponent / RecruitmentManager 处
- Delete: `SovereignTowns/src/Parties/RecruitingPartyComponent.cs`

---

### Task 3.1: 新建 `StRecruiterPartyComponent`

完整代码量太大，分 2 个 step：先建类骨架 + state enum，再实现 handler 方法。

- [ ] **Step 1: 创建 `StRecruiterPartyComponent.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 征兵队伍组件（B16.3）。显式 enum 状态机 + 实例级 _visitedThisTrip 集合。
/// 替代旧 RecruitingPartyComponent + RecruitmentManager._visitedPerParty 全局 Dict。
/// </summary>
public sealed class StRecruiterPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_recruit_";
    private const int DefaultGoldPerRecruit = 10;
    private const int CandidateBatchSize = 8;
    private const float PlanMaxDistance = 100f;

    public enum RecruiterPhase
    {
        Dispatching = 0,
        AtVillage = 1,
        Travelling = 2,
        Returning = 3,
    }

    [SaveableField(20)] private int _recruitedThisTrip;
    [SaveableField(21)] private Settlement? _assignedTarget;
    [SaveableField(22)] private RecruiterPhase _phase = RecruiterPhase.Dispatching;
    [CachedData] private TextObject? _cachedName;
    [CachedData] private HashSet<Settlement>? _visitedThisTrip;

    private HashSet<Settlement> VisitedThisTrip => _visitedThisTrip ??= new HashSet<Settlement>();

    public int RecruitedThisTrip => _recruitedThisTrip;
    public Settlement? AssignedTarget => _assignedTarget;
    public RecruiterPhase Phase => _phase;

    private static int ReturnRecruitedCount
        => ConfigurationManager.Current?.Thresholds?.RecruiterReturnRecruitedCount ?? 50;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var s = HomeSettlement?.Name?.ToString() ?? "未知";
            _cachedName = new TextObject("{=ST_RecruiterPartyName}征兵队 - " + s);
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => true;

    public void RecordRecruited(int count) { if (count > 0) _recruitedThisTrip += count; }
    public void SetAssignedTarget(Settlement? target) => _assignedTarget = target;

    private StRecruiterPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
    }

    /// 工厂：创建征兵队伍。初始 escort 由 dispatcher 抽取后传入。
    public static MobileParty? CreateForTown(Town homeTown, TroopRoster? initialEscort = null)
    {
        if (homeTown == null) return null;
        try
        {
            var settlement = homeTown.Settlement;
            if (settlement == null) return null;
            var ownerClan = settlement.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null) return null;

            var startingTroops = initialEscort ?? TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(settlement.GatePosition, 1f, ownerClan, startingTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_RecruiterPartyName}征兵队 - " + settlement.Name);

            var component = new StRecruiterPartyComponent(
                home: settlement, name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: true,
                args: args, leader: null);

            var stringId = StringIdPrefix + settlement.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null) return null;
            try { mobileParty.Aggressiveness = 0f; } catch { }

            component.SnapshotInitialMembers(mobileParty);
            Logger.Info($"StRecruiterPartyComponent: created '{stringId}' for '{settlement.StringId}'");
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StRecruiterPartyComponent.CreateForTown failed", ex);
            return null;
        }
    }

    /// 详细状态机实现见 Task 3.1 Step 2。
    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        switch (_phase)
        {
            case RecruiterPhase.Dispatching: HandleDispatching(self); break;
            case RecruiterPhase.AtVillage:   HandleAtVillage(self); break;
            case RecruiterPhase.Travelling:  HandleTravelling(self); break;
            case RecruiterPhase.Returning:   /* base.IsAtHome 接管 → OnArrivedHome → DefaultMergeAndDisband */ break;
        }
    }

    // ── handler 占位 — Task 3.1 Step 2 实现 ──
    private void HandleDispatching(MobileParty self) { }
    private void HandleAtVillage(MobileParty self) { }
    private void HandleTravelling(MobileParty self) { }
}
```

- [ ] **Step 2: 实现 3 个 handler + helper 方法**

把骨架末尾的 3 个 handler 替换为完整实现（搬自原 `RecruitmentManager.OnHourlyTickParty` 的状态机分支）。

由于完整实现近 200 行，**实现指导**（实施者按指导补全）：

**`HandleDispatching(self)`**：
- 判断 home == self.CurrentSettlement 或 LastVisitedSettlement，且 `_recruitedThisTrip == 0` 且 `VisitedThisTrip.Count == 0`
- 调用 `ResolveDepartureTarget(self)` 取首站目标 → `MoveTo(self, target)` → `_phase = Travelling`
- 无候选则保持 Dispatching，下一 tick 再试

**`HandleAtVillage(self)`**：
- 取 `currentSettlement = self.CurrentSettlement ?? self.LastVisitedSettlement`
- 若 `currentSettlement.IsVillage && currentSettlement != home`：
  - `IsRecruitmentTargetStillValid(currentSettlement, home)` 校验
  - `RecruitFromTargetVillage(self, currentSettlement, home)` → `RecordRecruited(n)` + `RecruitmentCooldown.MarkRecruited(...)`
  - `VisitedThisTrip.Add(currentSettlement)`
  - 阈值检查 `_recruitedThisTrip >= ReturnRecruitedCount` → `_phase = Returning; MoveTo(self, home)`
  - 否则 `PlanNextHop()` 取下一站 → `MoveTo(self, next)` → `_phase = Travelling`

**`HandleTravelling(self)`**：
- 检查累计阈值 `_recruitedThisTrip >= ReturnRecruitedCount` → `_phase = Returning; MoveTo(self, home)`
- 目标失效 / 风险高 → 重新规划 (`PlanNextHop`)
- 抵达目标 village（`self.CurrentSettlement == _assignedTarget`）→ `_phase = AtVillage`

**helper 方法**（搬自 `RecruitmentManager`）：
- `MoveTo(self, dest, reason)` — `SetAssignedTarget(dest) + self.SetMoveGoToSettlement`
- `ResolveDepartureTarget(self)` — 优先用 `_assignedTarget` 已未访问 + 合法，否则 `PlanNextHop`
- `PlanNextHop(self)` — 先用 ClanRecruiterScheduler，回退 RecruitmentPlanner.RankCandidates，应用 VisitedThisTrip 排除
- `IsRecruitmentTargetStillValid(village, home)`
- `RecruitFromTargetVillage(self, village, home)` — 与原 RecruitmentManager 同名方法**逐字搬运**（200 行），含 ModTreasury 扣费、bucket per-role 饱和检查等

**到家解散**：override `OnArrivedHome`：

```csharp
    protected override void OnArrivedHome(MobileParty self)
    {
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, HomeSettlement, "StRecruiterPartyComponent.OnArrivedHome");
        Logger.Info($"StRecruiterParty: '{PartyNameFormatter.SafeName(self)}' 转入 {transferred} 名兵员到 '{HomeSettlement?.Name}'，解散");
        PartyMergeService.Instance.DisbandAndUntrack(self, "StRecruiterPartyComponent.OnArrivedHome");
    }
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `0 errors / 0 warnings`。

---

### Task 3.2: TypeDefiner 注册 LocalId 6 + 移除旧 RecruitingPartyComponent 注册

- [ ] **Step 1: 修改 `DefineClassTypes`**

```csharp
    protected override void DefineClassTypes()
    {
        AddClassDefinition(typeof(Parties.StPartyComponent), 4);
        AddClassDefinition(typeof(Parties.StRecruiterPartyComponent), 6);
        AddClassDefinition(typeof(Parties.StTransferPartyComponent), 7);
        AddClassDefinition(typeof(Parties.StSallyPartyComponent), 8);
    }
```

注：删 `AddClassDefinition(typeof(Parties.RecruitingPartyComponent), 1);` — Task 3.8 删 .cs 之前先在此清理。

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `0 errors / 0 warnings`（旧 RecruitingPartyComponent.cs 还在）。

---

### Task 3.3: `RecruitmentManager` 瘦身为 `RecruitmentDispatcher`

- [ ] **Step 1: 重命名 + 全文替换**

```bash
git mv SovereignTowns/src/Recruitment/RecruitmentManager.cs SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs
```

全文替换为（瘦身版，只保留 `TryDispatchRecruiter` 工厂方法）：

```csharp
using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 征兵队 Dispatcher（B16.3）：由 RecruitmentManager 瘦身而来。
/// 只负责"何时何地派遣征兵队"：FoodGuard 校验、抽护卫、扣 ModTreasury、创建 StRecruiterPartyComponent。
/// 所有"在飞中"的状态机搬到 StRecruiterPartyComponent。
/// </summary>
public sealed class RecruitmentDispatcher
{
    private const string PartyKind = PartyLifecycleManager.KindRecruiter;
    private const int DefaultInitialGold = 1000;
    private const int CandidateBatchSize = 8;
    private const float PlanMaxDistance = 100f;

    private static float EscortRatio
        => ConfigurationManager.Current?.Thresholds?.RecruiterEscortRatio ?? 0.10f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public RecruitmentDispatcher(PartyLifecycleManager lifecycle, CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }

    public bool TryDispatchRecruiter(Town homeTown, int requestedMagnitude, string reason)
    {
        try
        {
            if (homeTown?.Settlement == null) return false;
            if (requestedMagnitude <= 0) return false;
            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment)
            {
                Logger.Debug($"  RecruitmentDispatcher: skipped '{homeTown.Name}' — AutoRecruitment disabled");
                return false;
            }

            if (_capitalRegistry != null)
            {
                var mgr = _capitalRegistry.GetForSettlement(homeTown.Settlement);
                if (mgr is null)
                {
                    Logger.Debug($"  RecruitmentDispatcher: '{homeTown.Name}' 不在受管 clan 名单");
                    return false;
                }
                var capitalSettlement = _capitalRegistry.GetCapitalForClan(mgr.OwnerClan);
                if (capitalSettlement == null || homeTown.Settlement != capitalSettlement)
                {
                    Logger.Debug($"  RecruitmentDispatcher: '{homeTown.Name}' 非该 clan 当前首府");
                    return false;
                }
            }

            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? Configuration.TownGarrisonRule.CreateDefault();
            if (FoodGuard.IsRecruitmentPausedForFood(homeTown, rule, "RecruitmentDispatcher")) return false;

            if (!_lifecycle.CanCreateAnotherParty(homeTown.Settlement, PartyKind))
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 已达征兵队上限");
                return false;
            }

            var candidates = RecruitmentPlanner.RankCandidates(homeTown, PlanMaxDistance, CandidateBatchSize, null, rule);
            if (candidates.Count == 0)
            {
                Logger.Warn($"  RecruitmentDispatcher: '{homeTown.Name}' 无可招募村庄候选");
                return false;
            }
            var target = candidates[0];

            int garrisonForEscort = homeTown.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            int escortRequested = (int)Math.Round(garrisonForEscort * EscortRatio);
            TroopRoster? escortRoster = null;
            int escortActual = 0;
            if (escortRequested > 0)
            {
                escortRoster = TroopRoster.CreateDummyTroopRoster();
                escortActual = TroopTransferHelper.TransferFromGarrison(
                    homeTown.GarrisonParty!.MemberRoster, escortRoster, escortRequested, TroopTransferHelper.SortStrategy.LowestTierFirst);
                if (escortActual <= 0) escortRoster = null;
            }

            bool shouldChargeDispatch = CapitalRegistry.ShouldChargeClan(homeTown.OwnerClan);
            if (shouldChargeDispatch)
            {
                if (!ModTreasury.CanAfford(DefaultInitialGold))
                {
                    Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 玩家金币不足");
                    if (escortRoster != null && escortActual > 0)
                        TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty!.MemberRoster);
                    return false;
                }
                if (!ModTreasury.Charge(ExpenseCategory.RecruiterSeed, DefaultInitialGold, $"recruiter_seed home={homeTown.Settlement.StringId}"))
                {
                    if (escortRoster != null && escortActual > 0)
                        TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty!.MemberRoster);
                    return false;
                }
            }

            var party = StRecruiterPartyComponent.CreateForTown(homeTown, escortRoster);
            if (party == null)
            {
                if (escortRoster != null && escortActual > 0)
                    TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty!.MemberRoster);
                if (shouldChargeDispatch)
                    Logger.Warn($"  RecruitmentDispatcher: 1000 denar 已扣但 party 创建失败");
                return false;
            }

            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, PartyKind);

            // 出发首站直接由 component 在 OnHourlyTick 内 ResolveDepartureTarget；这里只移动到首站让 OnHourlyTick 触发 AtVillage
            try
            {
                if (party.PartyComponent is StRecruiterPartyComponent rp)
                {
                    rp.SetAssignedTarget(target.VillageSettlement);
                    party.SetMoveGoToSettlement(target.VillageSettlement, MobileParty.NavigationType.Default, false);
                }
            }
            catch (Exception ex) { Logger.Error("initial dispatch SetMove failed", ex); }

            try
            {
                var dispatchCapitalMgr = _capitalRegistry?.GetForSettlement(homeTown.Settlement);
                if (dispatchCapitalMgr != null)
                {
                    dispatchCapitalMgr.RecruiterScheduler.RecordVisit(homeTown.Settlement);
                    float etaHours = ((party.GetPosition2D - target.VillageSettlement.GetPosition2D).Length) / Math.Max(party.Speed, 0.1f);
                    dispatchCapitalMgr.RecruiterScheduler.PreemptiveBook(target.VillageSettlement, party, etaHours);
                }
            }
            catch (Exception schedEx) { Logger.Warn("RecruiterScheduler first-hop bookkeeping failed: " + schedEx.Message); }

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchRecruiter",
                inputSummary: $"home={homeTown.Settlement.StringId} requested={requestedMagnitude} candidates={candidates.Count} target={target.VillageSettlement.StringId} escort={escortActual}",
                decisionJson: $"{{\"home\":\"{homeTown.Settlement.StringId}\",\"target\":\"{target.VillageSettlement.StringId}\",\"escort\":{escortActual},\"reason\":\"{AuditHelpers.EscapeJson(reason)}\"}}",
                accepted: true);
            Logger.Info($"  RecruitmentDispatcher: 派出征兵队 '{homeTown.Name}' → '{target.VillageSettlement.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchRecruiter failed", ex);
            return false;
        }
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -10
```

Expected：会有 errors（其他文件还引用 RecruitmentManager）。

---

### Task 3.4-3.8: 其他文件更新 + 删除旧文件 + commit

参考 Task 1.4-1.9 的同等动作：

- [ ] **Task 3.4**: `CapitalLogisticsManager` 改类型 `RecruitmentManager` → `RecruitmentDispatcher`
- [ ] **Task 3.5**: `SovereignTownsCampaignBehavior` 字段名 `_recruitmentManager` → `_recruitmentDispatcher`、构造改名、移除 `OnHourlyTickParty`/`OnMapEventEnded` 中的 `_recruitmentManager` 转发
- [ ] **Task 3.6**: `STPartySizeLimitModel` 改类型 `RecruitingPartyComponent` → `StRecruiterPartyComponent`
- [ ] **Task 3.7**: `PartyLifecycleManager.RebuildFromCampaign` 加 `StRecruiterPartyComponent` 分支，删除旧 `RecruitingPartyComponent` 分支
- [ ] **Task 3.8**: 其他散落的 `RecruitingPartyComponent` 引用全部清理（grep `\bRecruitingPartyComponent\b | grep -v St`）
- [ ] **Task 3.9**: `git rm SovereignTowns/src/Parties/RecruitingPartyComponent.cs`
- [ ] **Task 3.10**: 最终 `dotnet build` 通过 + grep 残留 0
- [ ] **Task 3.11**: Commit

```bash
git add SovereignTowns/

git commit -m "$(cat <<'EOF'
B16.3: 迁移 Recruiter — StRecruiterPartyComponent + RecruitmentDispatcher

- 新建 StRecruiterPartyComponent : StPartyComponent
  显式 RecruiterPhase enum（Dispatching/AtVillage/Travelling/Returning）
  _visitedThisTrip 从全局 Dict 移入 [CachedData] 实例字段
  消除 OnMobilePartyDestroyed 的 _visitedPerParty 清理代码
- RecruitmentManager.cs → RecruitmentDispatcher.cs（瘦身）
  保留 TryDispatchRecruiter 工厂；状态机搬到 component
- TypeDefiner 注册 LocalId 6（旧 LocalId 1 弃用）
- CapitalLogisticsManager 字段类型改名
- 删除旧 RecruitingPartyComponent.cs

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Step 4 — 迁移 Patrol（最复杂，vanilla 替代）

**目标**：新建 `StPatrolPartyComponent` 完全替代 vanilla `PatrolPartyComponent` 使用路径；`PatrolManager` 瘦身为 `PatrolDispatcher`；5 处 `is PatrolPartyComponent` 改为 `is StPatrolPartyComponent`；`Lifecycle.RebuildFromCampaign` 简化为单一 `is StPartyComponent` 扫描。

**Files:**
- Create: `SovereignTowns/src/Parties/StPatrolPartyComponent.cs`
- Modify: `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`（加 LocalId 5）
- Replace: `SovereignTowns/src/Patrol/PatrolManager.cs` → `SovereignTowns/src/Patrol/PatrolDispatcher.cs`
- Modify: `SovereignTowns/src/Battle/BattleLootManager.cs:76, 92`
- Modify: `SovereignTowns/src/Battle/BattleLootHandler.cs:446`
- Modify: `SovereignTowns/src/Ui/STPartyDialogRegistration.cs:92-93`
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`（`RebuildFromCampaign` 简化为单一 `is StPartyComponent` 扫描；可同步移除 `TrackedPartyMeta.InitialMemberCount` 字段、`GetInitialMemberCount` 方法）
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`（移除 `_patrolManager?.OnHourlyTickParty/OnMapEventEnded` 转发；改名 `_patrolManager` → `_patrolDispatcher`）

---

### Task 4.1: 新建 `StPatrolPartyComponent`

- [ ] **Step 1: 创建 `StPatrolPartyComponent.cs`**

继承 `StPartyComponent`，状态隐式（无需 enum — 巡逻队主要由 scheduler 驱动）。完整代码骨架：

```csharp
using System;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using SovereignTowns.SallyForth;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 巡逻队组件（B16.4）。完全替代 vanilla PatrolPartyComponent 使用路径。
/// 不出现在 MobileParty.AllPatrolParties，不绑定 Settlement.PatrolParty 槽位，不触发 vanilla 巡逻 AI。
/// vanilla 自动 spawn 的巡逻队仍以 PatrolPartyComponent 形式存在（共存策略，不互相干涉）。
/// </summary>
public sealed class StPatrolPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_patrol_";
    private const float InitiativeResetHours = 4f;

    [CachedData] private TextObject? _cachedName;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var s = HomeSettlement?.Name?.ToString() ?? "未知";
            _cachedName = new TextObject("{=ST_PatrolPartyName}巡逻队 - " + s);
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => false;

    private StPatrolPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader) { }

    /// 工厂：创建 ST 巡逻队（替代 vanilla PatrolPartyComponent.CreatePatrolParty）。
    /// 注：兵员注入 + SnapshotInitialMembers 由 PatrolDispatcher 完成。
    public static MobileParty? CreateForTown(Settlement home, PartyTemplateObject? template)
    {
        if (home == null) return null;
        try
        {
            var ownerClan = home.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null) return null;

            // 不再用 vanilla PatrolPartyComponent.CreatePatrolParty — 自己构造
            // template 仅用于参考（按 settlement 文化的理想兵种构成），实际兵从 garrison 抽取
            var startingTroops = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(home.GatePosition, 1f, ownerClan, startingTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_PatrolPartyName}巡逻队 - " + home.Name);

            var component = new StPatrolPartyComponent(
                home: home, name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: false,
                args: args, leader: null);

            var stringId = StringIdPrefix + home.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null) return null;
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StPatrolPartyComponent.CreateForTown failed", ex);
            return null;
        }
    }

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        var registry = CapitalRegistry.Instance;
        var partyClan = self.ActualClan;
        if (partyClan == null || registry == null) return;
        var capitalMgr = registry.GetForClan(partyClan);
        if (capitalMgr == null) return;
        var scheduler = capitalMgr.PatrolScheduler;

        // 1) 防御响应
        var defenseTarget = scheduler.GetDefenseTarget(self);
        if (defenseTarget != null)
        {
            if (defenseTarget.OwnerClan != capitalMgr.OwnerClan)
            {
                Logger.Warn($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defense target flipped owner mid-tick — skip");
            }
            else if (defenseTarget == capital)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' capital under siege — MergeGarrison");
                DefaultMergeAndDisband(self);  // 基类提供
                return;
            }
            else
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defending '{PartyNameFormatter.SafeName(defenseTarget)}'");
                SafeSetMoveDefendSettlement(self, defenseTarget);
                SafeSetInitiative(self, 0.3f, 0.7f, InitiativeResetHours);
                return;
            }
        }

        // 2) 支援出击战斗
        var sallyDispatcher = SovereignTowns.Campaign.SovereignTownsCampaignBehavior.SallyDispatcher;
        if (sallyDispatcher != null)
        {
            var supportSally = FindSupportableSallyBattle(self, capitalMgr, sallyDispatcher);
            if (supportSally != null)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' supporting sally '{PartyNameFormatter.SafeName(supportSally)}'");
                try { self.SetMoveEngageParty(supportSally, MobileParty.NavigationType.Default); }
                catch (Exception ex) { Logger.Error("SetMoveEngageParty failed", ex); }
                return;
            }
        }

        // 3) 抵达侦测 → RecordVisit + PickNextStop
        var visited = self.LastVisitedSettlement;
        if (visited != null && visited.OwnerClan == capitalMgr.OwnerClan && scheduler.TryMarkArrival(self, visited))
        {
            scheduler.RecordVisit(visited);
            var next = scheduler.PickNextStop(self);
            var dest = next ?? capital;
            try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error("SetMoveGoToSettlement failed", ex); }
            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived '{PartyNameFormatter.SafeName(visited)}', next='{PartyNameFormatter.SafeName(dest)}'");
            return;
        }

        // 4) 卡死保护
        var stuckTimeout = ConfigurationManager.Current.ClanPatrol.StuckTimeoutHours;
        if (scheduler.IsStuck(self, stuckTimeout))
        {
            var next = scheduler.PickNextStop(self);
            var dest = next ?? capital;
            try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error("SetMoveGoToSettlement failed", ex); }
            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {stuckTimeout}h — re-pick");
        }
    }

    private static MobileParty? FindSupportableSallyBattle(MobileParty self, Capital.CapitalManager capitalMgr, SallyDispatcher sallyDispatcher)
    {
        try
        {
            var threshold = ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours;
            var sallies = sallyDispatcher.GetActiveCombatSallyParties(capitalMgr.OwnerClan);
            if (sallies.Count == 0) return null;
            var partyPos = self.GetPosition2D;
            float partySpeed = Math.Max(self.Speed, 0.1f);
            MobileParty? best = null;
            float bestEta = float.MaxValue;
            foreach (var sally in sallies)
            {
                try
                {
                    if (sally.MapEvent == null) continue;
                    float distance = (partyPos - sally.GetPosition2D).Length;
                    float eta = distance / partySpeed;
                    if (eta < threshold && eta < bestEta) { bestEta = eta; best = sally; }
                }
                catch { }
            }
            return best;
        }
        catch (Exception ex) { Logger.Error("FindSupportableSallyBattle failed", ex); return null; }
    }

    private static void SafeSetMoveDefendSettlement(MobileParty party, Settlement home)
    {
        try { party.SetMoveDefendSettlement(home, false, MobileParty.NavigationType.Default); }
        catch (Exception ex)
        {
            Logger.Error("SetMoveDefendSettlement failed", ex);
            try { party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false); }
            catch (Exception fb) { Logger.Error("fallback failed", fb); }
        }
    }

    private static void SafeSetInitiative(MobileParty party, float attack, float avoid, float hours)
    {
        try { party.Ai?.SetInitiative(attack, avoid, hours); }
        catch (Exception ex) { Logger.Error("SetInitiative failed", ex); }
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `0 errors / 0 warnings`。

---

### Task 4.2-4.6: 同步 TypeDefiner / Dispatcher / 5 处类型引用改造 / 删除 / commit

参考 Task 1.x / 2.x / 3.x 的同等动作：

- [ ] **Task 4.2**: TypeDefiner 加 LocalId 5（`StPatrolPartyComponent`），最终清理为：

```csharp
    protected override void DefineClassTypes()
    {
        AddClassDefinition(typeof(Parties.StPartyComponent), 4);
        AddClassDefinition(typeof(Parties.StPatrolPartyComponent), 5);
        AddClassDefinition(typeof(Parties.StRecruiterPartyComponent), 6);
        AddClassDefinition(typeof(Parties.StTransferPartyComponent), 7);
        AddClassDefinition(typeof(Parties.StSallyPartyComponent), 8);
    }
```

- [ ] **Task 4.3**: `PatrolManager.cs` → `PatrolDispatcher.cs` 瘦身。保留 `TryCreatePatrolParty(settlement)` 工厂逻辑（含 cap 检查、抽兵、scheduler 首站）；状态机（防御响应 / 支援 / 抵达 / 卡死）已搬到 component。`CountExistingPatrolsAtHome` 改为遍历 `MobileParty.AllCustomParties` + `is StPatrolPartyComponent` 过滤。

- [ ] **Task 4.4**: 5 处 `is PatrolPartyComponent` 改为 `is StPatrolPartyComponent`：
  - `Battle/BattleLootManager.cs:76, 92`
  - `Battle/BattleLootHandler.cs:446`
  - `Ui/STPartyDialogRegistration.cs:92-93`
  - `Lifecycle/PartyLifecycleManager.cs` 读档分支（合并到统一 `is StPartyComponent` 扫描）

- [ ] **Task 4.5**: `PartyLifecycleManager.RebuildFromCampaign` 大幅简化为单一 `is StPartyComponent` 扫描：

替换为：

```csharp
            try
            {
                var customs = MobileParty.AllCustomParties;
                if (customs != null)
                {
                    foreach (var party in customs)
                    {
                        try
                        {
                            if (party == null) continue;
                            if (party.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
                            {
                                var home = stc.HomeSettlement;
                                if (home == null) { skipped++; continue; }
                                int mc = PartyNameFormatter.SafeMemberCount(party);
                                string kind = stc switch
                                {
                                    SovereignTowns.Parties.StRecruiterPartyComponent => KindRecruiter,
                                    SovereignTowns.Parties.StTransferPartyComponent  => KindTransfer,
                                    SovereignTowns.Parties.StSallyPartyComponent     => KindSallyForth,
                                    SovereignTowns.Parties.StPatrolPartyComponent    => KindPatrol,
                                    _ => null!,
                                };
                                if (kind == null!) continue;
                                _tracked[party] = new TrackedPartyMeta(home, kind, now, party.TargetSettlement, mc, SafeActualClan(party, home), mc);
                                switch (kind) {
                                    case KindRecruiter: recruiters++; break;
                                    case KindTransfer: transfers++; break;
                                    case KindSallyForth: sallyforths++; break;
                                    case KindPatrol: patrols++; break;
                                }
                            }
                        }
                        catch (Exception oneEx) { Logger.Error($"RebuildFromCampaign: failed for '{PartyNameFormatter.SafeName(party)}'", oneEx); }
                    }
                }
            }
            catch (Exception ex) { Logger.Error("RebuildFromCampaign: AllCustomParties enumeration failed", ex); }
            // 删除原 vanilla AllPatrolParties 扫描分支（StPatrolPartyComponent 不在 AllPatrolParties 中）
```

同时**删除**：
- `TrackedPartyMeta.InitialMemberCount` 字段
- `GetInitialMemberCount(party)` 方法
- 各处传 `initialMembers` 参数的构造调用（改为 `new TrackedPartyMeta(home, kind, now, target, mc, clan)` — 6 个参数）

- [ ] **Task 4.6**: `SovereignTownsCampaignBehavior` 字段 `_patrolManager` → `_patrolDispatcher`，构造点改名 `PatrolDispatcher(_lifecycle, _capitalRegistry, _sallyDispatcher)`（注意：dispatcher 不需要 `_battleLootManager`，那是 manager 内部用的）。移除 `OnHourlyTickParty`/`OnMapEventEnded` 中的 `_patrolManager` 转发。

最终 `OnHourlyTickParty` 应简化为：

```csharp
    private void OnHourlyTickParty(MobileParty party)
    {
        try
        {
            DrainWebConfigSync();
            // B16: 所有 4 种 component 由 PartyLifecycleManager 单点路由
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTickParty failed", ex);
        }
    }
```

实际上既然方法体只剩 `DrainWebConfigSync`，可考虑保留 / 删除取决于该 sync drain 是否仍有意义。**保留** drain，因为 web 配置生效需要在 game thread sync。

`OnMapEventEnded` 简化为：

```csharp
    private void OnMapEventEnded(MapEvent mapEvent)
    {
        try
        {
            _battleLootManager?.OnMapEventEnded(mapEvent);
            _lifecycle?.OnMapEventEnded(mapEvent);
        }
        catch (Exception ex)
        {
            Logger.Error("OnMapEventEnded forwarding failed", ex);
        }
    }
```

- [ ] **Task 4.7**: grep 残留检查

```bash
grep -rn '\bPatrolPartyComponent\b' SovereignTowns/src/ | grep -v StPatrolPartyComponent
```

Expected: 0 matches（vanilla 自动 spawn 的 PatrolPartyComponent 不再被 ST 代码引用）。

```bash
grep -rn '\bPatrolManager\b' SovereignTowns/src/ | grep -v PatrolDispatcher
```

Expected: 0 matches.

```bash
grep -rn "AllPatrolParties" SovereignTowns/src/
```

Expected: 0 matches.

```bash
grep -rn "GetInitialMemberCount" SovereignTowns/src/
```

Expected: 0 matches.

```bash
grep -rn "TrackedPartyMeta.*InitialMemberCount\|InitialMemberCount" SovereignTowns/src/Lifecycle/
```

Expected: 0 matches.

- [ ] **Task 4.8**: 最终编译

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `ok dotnet build: 1 projects, 0 errors, 0 warnings`

- [ ] **Task 4.9**: 同步更新 CLAUDE.md Architecture 章节

打开 `CLAUDE.md`，找 "## Architecture in one screen"，把 Layer 3 改为：

```
Layer 3  Dispatchers       : CapitalManager (★), CapitalLogisticsManager, RecruitmentDispatcher, PrisonerRecruitmentManager,
                              PatrolDispatcher, TransferDispatcher, SallyDispatcher,
                              PartyLifecycleManager
Layer 3b Component instances: StPartyComponent (abstract base),
                              StPatrolPartyComponent / StRecruiterPartyComponent /
                              StTransferPartyComponent / StSallyPartyComponent (each instance owns its own state machine)
```

并在 "Hard invariants" 中更新第 7 条提到的 `*PartyComponent` 子类名为 `StRecruiter`/`StTransfer`/`StSally`/`StPatrol`PartyComponent。

- [ ] **Task 4.10**: Commit

```bash
git add SovereignTowns/ CLAUDE.md

git commit -m "$(cat <<'EOF'
B16.4: 迁移 Patrol — StPatrolPartyComponent 完全替代 vanilla PatrolPartyComponent

- 新建 StPatrolPartyComponent : StPartyComponent
  独立巡逻队类型，不进 MobileParty.AllPatrolParties / Settlement.PatrolParty
  vanilla 自动 spawn 巡逻队继续存在（共存策略）
- PatrolManager.cs → PatrolDispatcher.cs（瘦身）
  CountExistingPatrolsAtHome 改用 AllCustomParties + is StPatrolPartyComponent
- TypeDefiner 注册 LocalId 5（旧 vanilla PatrolPartyComponent 不再使用）
- 5 处 is PatrolPartyComponent → is StPatrolPartyComponent
  (BattleLootManager x2, BattleLootHandler, STPartyDialogRegistration)
- PartyLifecycleManager.RebuildFromCampaign 简化为单一 is StPartyComponent 扫描
  删除 TrackedPartyMeta.InitialMemberCount + GetInitialMemberCount
- SovereignTownsCampaignBehavior OnHourlyTickParty/OnMapEventEnded 全部转发完成；
  仅保留 _lifecycle?.OnMapEventEnded + _battleLootManager?.OnMapEventEnded
- 同步 CLAUDE.md Architecture 章节（Manager → Dispatcher，Layer 3b component instances）

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## 重构完成 — 最终验证

- [ ] **F.1: 全文 grep 验证清单（确认全部清除）**

```bash
grep -rn '\bRecruitingPartyComponent\b' SovereignTowns/src/ | grep -v StRecruiter
grep -rn '\bTransferPartyComponent\b' SovereignTowns/src/ | grep -v StTransfer
grep -rn '\bSallyForthPartyComponent\b' SovereignTowns/src/ | grep -v StSally
grep -rn '\bPatrolManager\b' SovereignTowns/src/ | grep -v PatrolDispatcher
grep -rn '\bRecruitmentManager\b' SovereignTowns/src/ | grep -v RecruitmentDispatcher
grep -rn '\bSallyForthManager\b' SovereignTowns/src/ | grep -v SallyDispatcher
grep -rn '\bGarrisonTransferManager\b' SovereignTowns/src/ | grep -v TransferDispatcher
grep -rn "new PartyMergeService" SovereignTowns/src/
grep -rn "GetInitialMemberCount" SovereignTowns/src/
grep -rn "AllPatrolParties" SovereignTowns/src/
```

Expected: **每一条都返回 0 matches**。

- [ ] **F.2: 最终编译**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug 2>&1 | tail -5
```

Expected: `ok dotnet build: 1 projects, 0 errors, 0 warnings`

- [ ] **F.3: 文件计数对比 baseline**

```bash
git log --oneline B15..HEAD
```

Expected: 5 个 commit（B16.0 - B16.4）+ baseline 之上的 spec doc。

```bash
git diff --shortstat B15..HEAD -- SovereignTowns/src/
```

Expected: 约 -2000 行代码（Manager 瘦身 + 旧 Component 删除）+ ~1500 行新代码（基类 + 4 个 St*Component + Dispatcher）= 净减少约 500 行。

---

## Spec Coverage Self-Review

| Spec 章节 | 实现位置 |
|---|---|
| §3.1 类型层次 | Step 0 (基类) + Step 1-4 (4 个子类) |
| §3.2 文件组织 | 全部 Step 完成后符合 |
| §3.3 职责划分 | Step 0 + 各 Step 内 Dispatcher 重构 |
| §4 StPartyComponent 基类 | Task 0.4 |
| §5.1 StTransferPartyComponent | Task 1.1 |
| §5.2 StSallyPartyComponent | Task 2.1 |
| §5.3 StRecruiterPartyComponent | Task 3.1 |
| §5.4 StPatrolPartyComponent | Task 4.1 |
| §6 Dispatcher + PartyMergeService singleton | Task 0.1-0.3 + 各 Step 内 Dispatcher 替换 |
| §7 事件路由收敛 | Task 1.6 + 各 Step 内 Behavior 转发移除 |
| §8 SaveSystem 改动 | Task 0.5 / 1.2 / 2.2 / 3.2 / 4.2 |
| §9 vanilla PatrolPartyComponent 5 处引用改造 | Task 4.4 |
| §10 迁移顺序 5 step | 整体结构 |
| §11 删除清单 | 各 Step 末尾 `git rm` |
| §12 验证策略 | 各 Task 末尾 dotnet build + grep |
| §13 与 B15 衔接 | Pre-Step + Task 4.5 |
