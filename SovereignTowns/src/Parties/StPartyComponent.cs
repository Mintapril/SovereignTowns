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

    // ── vanilla CustomPartyComponent 抽象成员 ──
    // 注：Name cache 留给子类（每个子类的 Name 文案不同），基类不持有 _cachedName。
    public override Settlement HomeSettlement
    {
        get
        {
            if (_homeSettlement == null)
                throw new InvalidOperationException($"{GetType().Name}.HomeSettlement is null — likely caused by corrupted save or missing SaveableField(10).");
            return _homeSettlement;
        }
    }

    /// <summary>
    /// 仅用于"必须容忍 null"的诊断 / 恢复路径（B16.4a P1-7 修复）：
    /// 例如 <see cref="SovereignTowns.Lifecycle.PartyLifecycleManager.RebuildFromCampaign"/>
    /// 在 collect 阶段需要安静跳过损坏存档中 _homeSettlement 为 null 的 component。
    /// 常规运行路径请仍走 <see cref="HomeSettlement"/>，由其抛出明确异常以便定位损坏数据。
    /// </summary>
    public Settlement? HomeSettlementOrNull => _homeSettlement;

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
            OnHourlyTickCore(self, capital!);
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

    /// MobilePartyDestroyed 路由入口，由 PartyLifecycleManager 单点调用。
    /// 默认行为：尝试将残余兵员合并入 home garrison（或 clan capital 作为 fallback）。
    /// 子类 override 时必须调用 base.OnDestroyed 并将副作用放在 finally 块中（如 SallyDispatcher 通知）。
    public virtual void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        try
        {
            var home = HomeSettlementOrNull;
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
                Logger.Info($"{GetType().Name}.OnDestroyed: '{PartyNameFormatter.SafeName(self)}' home unavailable, no rescue");
            else
            {
                int rescued = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, rescueTarget, $"{GetType().Name}.OnDestroyed");
                if (rescued > 0)
                    Logger.Info($"{GetType().Name}.OnDestroyed: rescued {rescued} survivors to '{rescueTarget.Name}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnDestroyed failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

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

        // B16.4a P1-7：用 OrNull 保持原 null 防御语义；HomeSettlement 在损坏存档下会抛诊断异常。
        var home = HomeSettlementOrNull;
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
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return false;
        return self.CurrentSettlement == home || self.LastVisitedSettlement == home;
    }

    /// 把 party 设回 home 方向（vanilla AI 接管移动）。
    protected void ReturnToHome(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
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
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
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
