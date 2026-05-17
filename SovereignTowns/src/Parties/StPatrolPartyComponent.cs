using System;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
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
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 巡逻队组件（B16.4）。完全替代 vanilla PatrolPartyComponent 使用路径。
/// 不出现在 MobileParty.AllPatrolParties，不绑定 Settlement.PatrolParty 槽位，不触发 vanilla 巡逻 AI。
/// vanilla 自动 spawn 的巡逻队仍以 PatrolPartyComponent 形式存在（共存策略，不互相干涉）。
///
/// 隐式状态机（无 enum）：行为由 scheduler 调度 + 防御响应 + 支援战斗 + 卡死保护四条规则驱动。
///
/// SaveableField 槽位：基类占 [10, 20)；本类不持有额外持久化字段。
/// </summary>
public sealed class StPatrolPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_patrol_";
    private const float InitiativeResetHours = 4f;

    [CachedData] private TextObject? _cachedName;
    // B17.4 B6：防御日志去抖 — 只在 target 切换时 Info，否则 Debug。
    [CachedData] private Settlement? _lastLoggedDefenseTarget;
    // B17.4 A5：连续 stuck 计数 — scheduler 重发指令后仍卡死多少 hour 后触发瞬移。
    [CachedData] private int _stuckHoursAfterReissue;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var s = HomeSettlementOrNull?.Name?.ToString() ?? "未知";
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

    /// <summary>
    /// 工厂：创建 ST 巡逻队（替代 vanilla PatrolPartyComponent.CreatePatrolParty）。
    /// 兵员注入 + SnapshotInitialMembers + 首站规划全部由 PatrolDispatcher 完成。
    /// template 参数仅作语义占位（按 settlement 文化挑兵种），实际不传给 vanilla CreatePatrolParty。
    /// </summary>
    public static MobileParty? CreateForTown(Settlement home, PartyTemplateObject? template)
    {
        if (home == null) return null;
        try
        {
            var ownerClan = home.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"StPatrolPartyComponent.CreateForTown: home '{home.StringId}' has no OwnerClan/Leader");
                return null;
            }

            // 不再用 vanilla PatrolPartyComponent.CreatePatrolParty — 自己构造
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
            if (mobileParty == null)
            {
                Logger.Error($"StPatrolPartyComponent.CreateForTown: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StPatrolPartyComponent.CreateForTown failed", ex);
            return null;
        }
    }

    // ── 状态机核心 ────────────────────────────────────────

    /// <summary>
    /// 巡逻队是循环型 — 到达 home 只是路线上的一站，不解散。
    /// 把 OnHourlyTickCore 的 4 条规则（防御 / 支援 / 抵达 / 卡死）原样跑一遍，
    /// 让 scheduler 在 capital 落定后立即派下一站（首府被围攻仍能经"防御→DefaultMergeAndDisband"分支正确处理）。
    /// </summary>
    protected override void OnArrivedHome(MobileParty self)
    {
        var registry = CapitalRegistry.Instance;
        var partyClan = self.ActualClan;
        if (partyClan == null || registry == null) return;
        var capital = registry.GetCapitalForClan(partyClan);
        if (capital == null) return;
        OnHourlyTickCore(self, capital);
    }

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol) return;

        var registry = CapitalRegistry.Instance;
        var partyClan = self.ActualClan;
        if (partyClan == null || registry == null) return;
        var capitalMgr = registry.GetForClan(partyClan);
        if (capitalMgr == null) return;
        var scheduler = capitalMgr.PatrolScheduler;

        // 1) 防御响应（B7.26）
        var defenseTarget = scheduler.GetDefenseTarget(self);
        if (defenseTarget != null)
        {
            if (defenseTarget.OwnerClan != capitalMgr.OwnerClan)
            {
                Logger.Warn($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defense target '{PartyNameFormatter.SafeName(defenseTarget)}' flipped owner mid-tick — skip");
                // fall through to normal tick logic
            }
            else if (defenseTarget == capital)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' capital '{PartyNameFormatter.SafeName(capital)}' under siege — MergeGarrison");
                DefaultMergeAndDisband(self);  // 基类提供
                return;
            }
            else
            {
                // B17.4 B6：日志去抖 — 只在 defense target 切换时 Info，否则 Debug。
                if (_lastLoggedDefenseTarget != defenseTarget)
                {
                    Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defending '{PartyNameFormatter.SafeName(defenseTarget)}' (under siege)");
                    _lastLoggedDefenseTarget = defenseTarget;
                }
                else
                {
                    Logger.Debug($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' still defending '{PartyNameFormatter.SafeName(defenseTarget)}'");
                }
                SafeSetMoveDefendSettlement(self, defenseTarget);
                SafeSetInitiative(self, attack: 0.3f, avoid: 0.7f, hours: InitiativeResetHours);

                // B17.4 B8：若 defense 目标 IsUnderSiege/IsUnderRaid 已转 false，立刻让 scheduler PickNextStop（不等下一 hour）。
                try
                {
                    bool stillThreat = defenseTarget.IsUnderSiege || (defenseTarget.IsVillage && defenseTarget.Village?.VillageState == Village.VillageStates.BeingRaided);
                    if (!stillThreat)
                    {
                        var nextEarly = scheduler.PickNextStop(self);
                        if (nextEarly != null && nextEarly != defenseTarget)
                        {
                            try { self.SetMoveGoToSettlement(nextEarly, MobileParty.NavigationType.Default, false); }
                            catch (Exception ex) { Logger.Error($"early-return SetMoveGoToSettlement failed", ex); }
                            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defense target safe — early-return to '{nextEarly.Name}'");
                            _lastLoggedDefenseTarget = null;  // 重置去抖状态
                        }
                    }
                }
                catch (Exception threatEx) { Logger.Warn($"early-return threat check failed: {threatEx.Message}"); }
                return;
            }
        }

        // 离开 defense 路径 → 重置日志去抖
        _lastLoggedDefenseTarget = null;

        // 2) 支援出击战斗（B7.27）
        var sallyDispatcher = SovereignTowns.Campaign.SovereignTownsCampaignBehavior.SallyDispatcher;
        if (sallyDispatcher != null)
        {
            var supportSally = FindSupportableSallyBattle(self, capitalMgr, sallyDispatcher);
            if (supportSally != null)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' supporting sally '{PartyNameFormatter.SafeName(supportSally)}' (ETA < {ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours:F1}h)");
                try { self.SetMoveEngageParty(supportSally, MobileParty.NavigationType.Default); }
                catch (Exception ex) { Logger.Error($"SetMoveEngageParty failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(supportSally)}'", ex); }
                return;
            }
        }

        // 3) 抵达侦测 → RecordVisit + PickNextStop
        var visited = self.LastVisitedSettlement;
        if (visited != null && visited.OwnerClan == capitalMgr.OwnerClan
            && scheduler.TryMarkArrival(self, visited))
        {
            scheduler.RecordVisit(visited);
            var next = scheduler.PickNextStop(self);
            var dest = next ?? capital;
            try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(dest)}'", ex); }
            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived '{PartyNameFormatter.SafeName(visited)}', next='{PartyNameFormatter.SafeName(dest)}'");
            return;
        }

        // 4) 卡死保护
        var stuckTimeout = ConfigurationManager.Current.ClanPatrol.StuckTimeoutHours;
        if (scheduler.IsStuck(self, stuckTimeout))
        {
            // B17.4 A5：scheduler 重发指令后还卡死 → 累计 hours。一段瞬移阈值后强制传送回 home.GatePosition（IG BoundedParty.IfStuckPortToHome）。
            float teleportHours = ConfigurationManager.Current?.Thresholds?.StuckTeleportHours ?? 0f;
            if (teleportHours > 0 && _stuckHoursAfterReissue >= teleportHours)
            {
                try
                {
                    var home = HomeSettlementOrNull;
                    if (home != null)
                    {
                        // IG BoundedParty.cs:53 用的是 mobileParty.Position（不是 Position2D）。
                        // IG 是发布的 mod，证明 v1.3.15 该 setter 公开可写。
                        self.Position = home.GatePosition;
                        Logger.Warn($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {teleportHours}h after re-issue — teleport to '{home.Name}' GatePosition (二段救济)");
                        _stuckHoursAfterReissue = 0;
                        return;
                    }
                }
                catch (Exception tpEx) { Logger.Error($"二段瞬移失败 for '{PartyNameFormatter.SafeName(self)}'", tpEx); }
            }

            var next = scheduler.PickNextStop(self);
            var dest = next ?? capital;
            try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(dest)}'", ex); }
            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {stuckTimeout}h — re-pick next='{PartyNameFormatter.SafeName(dest)}' (stuck cycles since reissue={_stuckHoursAfterReissue})");
            _stuckHoursAfterReissue++;
        }
        else
        {
            _stuckHoursAfterReissue = 0;  // 不再 stuck，重置计数
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// B7.27：判定本 patrol 是否能在某 sally 战斗结束前抵达。返回最近的可支援目标，无则 null。
    /// 简单算法：ETA = 距离 / 速度，ETA &lt; SupportEtaThresholdHours 即可。
    /// </summary>
    private static MobileParty? FindSupportableSallyBattle(MobileParty self, CapitalManager capitalMgr, SallyDispatcher sallyDispatcher)
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
                    if (sally.MapEvent == null) continue;  // 双重保险
                    float distance = (partyPos - sally.GetPosition2D).Length;
                    float eta = distance / partySpeed;
                    if (eta < threshold && eta < bestEta)
                    {
                        bestEta = eta;
                        best = sally;
                    }
                }
                catch { /* 单 sally 失败不影响其他 */ }
            }
            return best;
        }
        catch (Exception ex)
        {
            Logger.Error("FindSupportableSallyBattle failed", ex);
            return null;
        }
    }

    /// <summary>
    /// v1.3.15 SetMoveDefendSettlement(Settlement, bool isAvoidingTrouble, NavigationType)。
    /// 真正切换 vanilla AI 到"防守此 settlement"——这是 Defense 模式生效的关键。
    /// 失败时退而求其次 SetMoveGoToSettlement，避免完全无指令。
    /// </summary>
    private static void SafeSetMoveDefendSettlement(MobileParty party, Settlement home)
    {
        try
        {
            party.SetMoveDefendSettlement(home, false, MobileParty.NavigationType.Default);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveDefendSettlement failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(home)}'", ex);
            try
            {
                party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
            }
            catch (Exception fallbackEx)
            {
                Logger.Error($"Defense fallback SetMoveGoToSettlement also failed for '{PartyNameFormatter.SafeName(party)}'", fallbackEx);
            }
        }
    }

    private static void SafeSetInitiative(MobileParty party, float attack, float avoid, float hours)
    {
        try
        {
            party.Ai?.SetInitiative(attack, avoid, hours);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetInitiative failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }
}
