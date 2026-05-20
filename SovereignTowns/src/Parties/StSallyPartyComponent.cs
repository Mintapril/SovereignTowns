using System;
using SovereignTowns.Common;
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
///
/// 设计要点：
///   - 战后无条件回家（OnMapEventEndedCore），比 ShouldReturnAndDisband 更激进 — 任务特性。
///   - 到达 home 走 DestroyAndUntrack（不是 DisbandAndUntrack），与 sally "瞬时部队" 语义对齐。
///   - 销毁回调救援存活兵到 home（home 失守则改救到当前首府）。
///   - HomeSettlement 失守等异常路径由基类 ValidateAliveAndManaged 统一处理。
///
/// SaveableField 槽位：基类占 [10, 20)；本类占 [20, 23)。
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
            // B16.4a P1-7：Name 必须容忍 _homeSettlement 为 null（损坏存档 / 序列化未完成时调用），
            // 用 HomeSettlementOrNull 而非 HomeSettlement 以避免抛 InvalidOperationException。
            var home = HomeSettlementOrNull;
            var n = new TextObject("{=ST_SallyPartyName}Sally — {SETTLEMENT}");
            n.SetTextVariable("SETTLEMENT",
                home?.Name ?? new TextObject("{=ST_Common_Unknown}unknown"));
            _cachedName = n;
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => false;

    // B17.4 A1：sally 是冲锋型任务，被玩家拦下让冲锋失败 — 跳过 player-target hold。
    protected override bool AvoidsPlayerTargetHold => true;

    protected override Economy.ExpenseCategory GetExpenseCategoryForKind() => Economy.ExpenseCategory.SallySeed;

    protected override bool ShouldReplenishFoodEnRoute => false;  // 单目的地短命任务，无沿途补给

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

    /// <summary>
    /// 工厂：创建出击队伍。失败返回 null，不抛。
    /// 兵员由调用方（SallyDispatcher）通过 TroopTransferHelper 注入 + SnapshotInitialMembers。
    /// </summary>
    public static MobileParty? CreateForTown(Town homeTown, MobileParty? initialTarget = null)
    {
        if (homeTown == null)
        {
            Logger.Error("StSallyPartyComponent.CreateForTown: homeTown is null");
            return null;
        }
        try
        {
            var settlement = homeTown.Settlement;
            if (settlement == null)
            {
                Logger.Error("StSallyPartyComponent.CreateForTown: homeTown.Settlement is null");
                return null;
            }
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

            var nameObj = new TextObject("{=ST_SallyPartyName}Sally — {SETTLEMENT}");
            nameObj.SetTextVariable("SETTLEMENT", settlement.Name);

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
            // B7.22：0 攻击性 — sally 仍会攻击指定 target（通过 SetMoveEngageParty），但不会半路追散兵
            // B16.4a P1-9：不再静默吞异常。Aggressiveness 是 vanilla 属性，setter 失败极罕见，
            // 但若失败可能让 sally 队伍主动追散兵，需 warn 出来定位。
            try { mobileParty.Aggressiveness = 0f; }
            catch (Exception ex) { Logger.Warn($"StSallyPartyComponent.CreateForTown: failed to set Aggressiveness=0 for '{stringId}': {ex.Message}"); }

            // 注：troops 由 SallyDispatcher 通过 TroopTransferHelper.TransferFromGarrison 注入 + SnapshotInitialMembers
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
                        Logger.Warn($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' away {hoursAway:F1}h > {MaxSallyHours}h, force return to '{HomeSettlementOrNull?.Name}'");
                        _forceReturnLogged = true;
                    }
                    TransitionToReturning(self);
                    return;
                }
                // 2) target 进入 settlement → vanilla 追击 bug 规避，立即回家
                var target = _targetParty;
                if (target != null && target.IsActive && target.CurrentSettlement != null)
                {
                    Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' target '{PartyNameFormatter.SafeName(target)}' entered '{target.CurrentSettlement.Name}', returning home");
                    TransitionToReturning(self);
                    return;
                }
                // 3) target null/dead → 任务结束，直接返航；不要释放 vanilla AI 让出击队游走。
                if (target == null || !target.IsActive)
                {
                    if (!_targetLostLogged)
                    {
                        Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' target lost, returning home");
                        _targetLostLogged = true;
                    }
                    TransitionToReturning(self);
                }
                else
                {
                    // 目标恢复 → 清状态，下次丢失时重新 log
                    _targetLostLogged = false;
                    // Issue #3：每小时重申追击 + 再次锁定。SetMoveEngageParty 通常是 sticky，
                    // 但 vanilla AI 在地图事件 / 路径阻断时可能切换 behavior，这里再保险一次。
                    try
                    {
                        self.Ai?.SetDoNotMakeNewDecisions(true);
                        self.SetMoveEngageParty(target, MobileParty.NavigationType.Default);
                    }
                    catch (Exception reEx) { Logger.Warn($"StSallyParty re-engage failed: {reEx.Message}"); }
                }
                return;
            case SallyPhase.Returning:
                // base.IsAtHome 接管解散
                return;
        }
    }

    /// <summary>
    /// 战后无条件回家（覆盖基类的 ShouldReturnAndDisband 路径只是先于此方法）。
    /// 即使 ShouldReturnAndDisband 命中（基类已 ReturnToHome），未命中也走这里同样回家。
    /// sally 任务特性 — 比 ShouldReturnAndDisband 更激进。
    /// </summary>
    protected override void OnMapEventEndedCore(MapEvent ev, MobileParty self)
    {
        TransitionToReturning(self);
    }

    /// <summary>
    /// sally 到达 home → 转兵 + destroy（注意是 destroy，不是 disband — sally 是瞬时部队，
    /// 用 DestroyAndUntrack 直接销毁，不留 disbanded 残体）。
    /// </summary>
    protected override void OnArrivedHome(MobileParty self)
    {
        // B16.4a P1-7：保留 null 防御分支 —— 用 HomeSettlementOrNull 避免抛诊断异常。
        var home = HomeSettlementOrNull;
        if (home == null)
        {
            PartyMergeService.Instance.DisbandAndUntrack(self, "StSallyPartyComponent null home");
            return;
        }
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, home, "StSallyPartyComponent.OnArrivedHome");
        Logger.Info($"StSallyParty: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into '{home.Name}', destroying");
        NotifyDispatcherEnded();
        PartyMergeService.Instance.DestroyAndUntrack(self, "StSallyPartyComponent.OnArrivedHome", deferIfInMapEvent: true);
    }

    /// <summary>
    /// 销毁回调：委托基类救援残兵，并在 finally 中通知 SallyDispatcher 周期结束。
    /// </summary>
    public override void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        try { base.OnDestroyed(self, destroyer); }
        finally { NotifyDispatcherEnded(); }
    }

    private void TransitionToReturning(MobileParty self)
    {
        _phase = SallyPhase.Returning;
        try { self.Ai?.SetDoNotMakeNewDecisions(false); }
        catch (Exception ex) { Logger.Error("SetDoNotMakeNewDecisions(false) failed", ex); }
        ReturnToHome(self);  // 基类提供
    }

    /// <summary>
    /// 通知 SallyDispatcher 本 settlement 的 sally 周期已结束（清冷却 + 持续可见计数）。
    /// 通过 SovereignTownsCampaignBehavior.SallyDispatcher 静态 accessor 拿到 dispatcher。
    /// </summary>
    private void NotifyDispatcherEnded()
    {
        try
        {
            // B16.4a P1-7：保留 null 防御 —— 用 OrNull 优雅跳过。
            var home = HomeSettlementOrNull;
            if (home == null) return;
            var dispatcher = SovereignTowns.Campaign.SovereignTownsCampaignBehavior.SallyDispatcher;
            dispatcher?.NotifySallyEnded(home);
        }
        catch (Exception ex) { Logger.Warn($"NotifyDispatcherEnded: {ex.Message}"); }
    }
}
