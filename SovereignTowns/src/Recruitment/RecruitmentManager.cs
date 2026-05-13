using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Decisions;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Recruitment;

/// <summary>
/// MVP 2 业务核心 — 多目标巡回 + 兵种匹配 + 冷却 + 出发护卫。
///
/// 工作流：
///   1. 派遣：仅当 homeTown == 当前首府 时；从首府 garrison 抽 <see cref="GlobalConfig.RecruiterEscortSize"/>
///      个低 Tier 兵作基础护卫；规划首个目标村庄；<see cref="SetMoveGoToSettlement"/> 出发。
///   2. HourlyTick：
///      - 抵达 home → TransferAndDisband
///      - 抵达任一非 home village → RecruitFromTargetVillage + 标记冷却 → 检查阈值/规划下一站
///      - 总人数 ≥ <see cref="GlobalConfig.RecruiterReturnThreshold"/> → 回首府
///      - 候选枯竭 → 回首府
///      - 目标风险高 → 回首府
/// </summary>
public sealed class RecruitmentManager
{
    private const string PartyKind = PartyLifecycleManager.KindRecruiter;
    private const int DefaultInitialGold = 1000;
    private const int DefaultGoldPerRecruit = 10;

    /// <summary>规划下一目标时一次返回多少候选（取头部第一个未访问的）。</summary>
    private const int CandidateBatchSize = 8;

    /// <summary>规划候选时的最大距离（与原值一致）。</summary>
    private const float PlanMaxDistance = 100f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalManager? _capitalManager;

    /// <summary>每支招募队本次旅程已访问过的 village（避免立即回头）。瞬态。</summary>
    private readonly Dictionary<MobileParty, HashSet<Settlement>> _visitedPerParty = new();

    public RecruitmentManager(PartyLifecycleManager lifecycle, CapitalManager? capitalManager = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalManager = capitalManager;

        // 订阅销毁事件，确保 _visitedPerParty 不会因招募队战斗死亡 / 被俘而泄漏。
        try
        {
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
        }
        catch (Exception ex)
        {
            Logger.Warn($"RecruitmentManager: MobilePartyDestroyed.AddNonSerializedListener failed: {ex.Message}");
        }
    }

    private void OnMobilePartyDestroyed(MobileParty party, TaleWorlds.CampaignSystem.Party.PartyBase? destroyerParty)
    {
        if (party is null) return;
        try
        {
            if (_visitedPerParty.Remove(party))
            {
                Logger.Debug($"RecruitmentManager: cleaned _visitedPerParty entry for destroyed '{party.StringId}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"RecruitmentManager.OnMobilePartyDestroyed failed for '{party?.StringId}': {ex.Message}");
        }
    }

    /// <summary>
    /// 处理一条 RequestRecruitment 决策：仅在 homeTown 是当前首府时派遣。
    /// </summary>
    public bool TryDispatchRecruiter(Town homeTown, GarrisonDecision decision)
    {
        try
        {
            if (homeTown?.Settlement == null) return false;
            if (decision.Kind != GarrisonActionKind.RequestRecruitment) return false;

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment)
            {
                Logger.Debug($"  RecruitmentManager: skipped '{homeTown.Name}' — AutoRecruitment disabled");
                return false;
            }

            // 首府校验：仅首府能派征兵队。CapitalManager 缺失时回退（兼容旧调用），但记录 Warn。
            if (_capitalManager != null)
            {
                var capitalSettlement = _capitalManager.GetCapitalSettlement();
                if (capitalSettlement == null || homeTown.Settlement != capitalSettlement)
                {
                    Logger.Debug($"  RecruitmentManager: '{homeTown.Name}' 非当前首府，跳过派遣");
                    return false;
                }
            }
            else
            {
                Logger.Warn($"  RecruitmentManager: capitalManager == null，跳过首府校验（兼容模式）");
            }

            if (!_lifecycle.CanCreateAnotherParty(homeTown.Settlement, PartyKind))
            {
                Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 已达征兵队上限，跳过");
                return false;
            }

            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();
            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: null,
                matchingRule: rule);
            if (candidates.Count == 0)
            {
                Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 无可招募村庄候选");
                return false;
            }
            var target = candidates[0];

            // 基础护卫：从首府 GarrisonParty 抽 RecruiterEscortSize 个低 Tier 兵
            int escortRequested = Math.Max(0, ConfigurationManager.Current?.RecruiterEscortSize ?? 0);
            TroopRoster? escortRoster = null;
            int escortActual = 0;
            if (escortRequested > 0)
            {
                escortRoster = TroopRoster.CreateDummyTroopRoster();
                escortActual = ExtractLowTierEscort(homeTown, escortRoster, escortRequested, rule);
                if (escortActual <= 0)
                {
                    escortRoster = null;
                    Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 首府兵力不足抽护卫，0 护卫出发");
                }
                else
                {
                    Logger.Info($"  RecruitmentManager: 抽 {escortActual} 名低 Tier 护卫从 '{homeTown.Name}'");
                }
            }

            var party = RecruitingPartyComponent.CreateForTown(homeTown, DefaultInitialGold, escortRoster);
            if (party == null)
            {
                Logger.Warn($"  RecruitmentManager: CreateForTown 返回 null for '{homeTown.Name}'");
                // 护卫已抽离 → 还回 garrison
                if (escortRoster != null && escortActual > 0)
                {
                    TryRestoreEscort(homeTown, escortRoster);
                }
                return false;
            }

            party.SetMoveGoToSettlement(target.VillageSettlement, MobileParty.NavigationType.Default, false);
            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, PartyKind);
            _visitedPerParty[party] = new HashSet<Settlement>();

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchRecruiter",
                inputSummary: $"home={homeTown.Settlement.StringId} candidates={candidates.Count} target={target.VillageSettlement.StringId} escort={escortActual}",
                decisionJson: $"{{\"home\":\"{homeTown.Settlement.StringId}\",\"target\":\"{target.VillageSettlement.StringId}\",\"priority\":{target.PriorityScore:F2},\"estimatedTroops\":{target.EstimatedAvailableTroops},\"escort\":{escortActual}}}",
                accepted: true);

            Logger.Info($"  RecruitmentManager: 派出征兵队 '{homeTown.Name}' → '{target.VillageSettlement.Name}' (priority={target.PriorityScore:F1}, escort={escortActual})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchRecruiter failed", ex);
            return false;
        }
    }

    /// <summary>
    /// HourlyTickPartyEvent 转发：只处理 RecruitingPartyComponent 队伍。多目标巡回状态机。
    /// </summary>
    public void OnHourlyTickParty(MobileParty party)
    {
        try
        {
            if (party?.PartyComponent is not RecruitingPartyComponent rp) return;
            if (!party.IsActive) return;

            var home = rp.HomeSettlement;
            if (home == null) return;

            // 1. 已经回到 home
            if (party.LastVisitedSettlement == home)
            {
                TransferAndDisband(party, home);
                return;
            }

            var currentSettlement = party.CurrentSettlement ?? party.LastVisitedSettlement;
            var targetSettlement = party.TargetSettlement;

            // 2. 停泊在某个非 home village 且与 TargetSettlement 一致 → 招募 + 标记冷却 + 规划下一站
            if (currentSettlement != null
                && currentSettlement != home
                && currentSettlement.IsVillage
                && (targetSettlement == null || currentSettlement == targetSettlement))
            {
                int recruited = RecruitFromTargetVillage(party, currentSettlement, home);
                if (recruited > 0)
                {
                    RecruitmentCooldown.MarkRecruited(currentSettlement);
                }
                if (_visitedPerParty.TryGetValue(party, out var visited))
                {
                    visited.Add(currentSettlement);
                }

                // 招完检查阈值：到达 ReturnThreshold 立刻回首府
                int returnThreshold = Math.Max(1, ConfigurationManager.Current?.RecruiterReturnThreshold ?? 30);
                int memberCount = party.MemberRoster?.TotalManCount ?? 0;
                if (memberCount >= returnThreshold)
                {
                    Logger.Info($"  Recruiter '{party.Name}' 总人数 {memberCount} ≥ 阈值 {returnThreshold}，回 '{home.Name}'");
                    party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
                    return;
                }

                // 规划下一站
                var next = PlanNextHop(party, home);
                if (next != null)
                {
                    int visitedCount = _visitedPerParty.TryGetValue(party, out var vs) ? (vs?.Count ?? 0) : 0;
                    Logger.Info($"  Recruiter '{party.Name}' 巡回下一站：'{next.Name}' (此前 visited={visitedCount})");
                    party.SetMoveGoToSettlement(next, MobileParty.NavigationType.Default, false);
                }
                else
                {
                    Logger.Info($"  Recruiter '{party.Name}' 候选枯竭，回 '{home.Name}'");
                    party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
                }
                return;
            }

            // 3. 在路上 - 检查阈值（即使没到下一个村也可能因之前累积已满）
            {
                int returnThreshold = Math.Max(1, ConfigurationManager.Current?.RecruiterReturnThreshold ?? 30);
                int memberCount = party.MemberRoster?.TotalManCount ?? 0;
                if (memberCount >= returnThreshold && (targetSettlement == null || targetSettlement != home))
                {
                    Logger.Info($"  Recruiter '{party.Name}' 在途中达到阈值 {memberCount}/{returnThreshold}，回 '{home.Name}'");
                    party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
                    return;
                }
            }

            // 4. 目标村庄风险高 → 回城
            if (targetSettlement != null && targetSettlement != home)
            {
                var risk = RiskAssessmentService.Assess(targetSettlement);
                if (risk.Level >= RiskLevel.High)
                {
                    Logger.Warn($"  Recruiter '{party.Name}': 目标 '{targetSettlement.Name}' risk={risk.Level}，紧急回城");
                    party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTickParty failed", ex);
        }
    }

    /// <summary>
    /// 规划巡回的下一站：排除已 visited（本趟）以及当前在路上的 target（避免重复路径）。
    /// 兵种匹配 + 冷却由 RecruitmentPlanner 内部完成。返回 null = 候选枯竭。
    /// </summary>
    private Settlement? PlanNextHop(MobileParty party, Settlement home)
    {
        try
        {
            var homeTown = home.Town;
            if (homeTown == null) return null;
            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();

            var exclude = new HashSet<Settlement>();
            if (_visitedPerParty.TryGetValue(party, out var visited))
            {
                foreach (var s in visited) exclude.Add(s);
            }

            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: exclude,
                matchingRule: rule);

            if (candidates.Count == 0) return null;
            return candidates[0].VillageSettlement;
        }
        catch (Exception ex)
        {
            Logger.Error("PlanNextHop failed", ex);
            return null;
        }
    }

    /// <summary>
    /// 抵达 village 的实际招募动作。返回实际招到的人数（用于冷却登记判定）。
    /// </summary>
    private int RecruitFromTargetVillage(MobileParty recruitingParty, Settlement village, Settlement home)
    {
        int recruited = 0;
        try
        {
            if (village?.Notables == null) return 0;
            var ownerHero = home.OwnerClan?.Leader;
            if (ownerHero == null) return 0;
            var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) return 0;

            var rule = ConfigurationManager.GetRuleFor(home.Town);
            int budgetRemaining = Math.Max(0, rule?.BudgetLimit ?? 5000);
            int leaderGold = ownerHero.Gold;

            int spent = 0;
            int candidatesScanned = 0;
            var scoredCandidates = new List<(CharacterObject Troop, CharacterObject[] Slots, int SlotIndex, float Score)>();
            var garrisonRoster = home.Town?.GarrisonParty?.MemberRoster;
            int targetTotal = Math.Max(1, rule?.TargetTotalCount ?? 1);

            foreach (var notable in village.Notables)
            {
                if (notable == null) continue;
                if (!notable.CanHaveRecruits) continue;

                var volunteerTypes = notable.VolunteerTypes;
                if (volunteerTypes == null || volunteerTypes.Length == 0) continue;

                int maxIdx;
                try
                {
                    maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: MaximumIndexHeroCanRecruitFromHero threw for notable '{notable.Name}': {ex.Message}");
                    continue;
                }
                if (maxIdx < 0) continue;

                for (int i = 0; i < volunteerTypes.Length && i <= maxIdx; i++)
                {
                    var troop = volunteerTypes[i];
                    if (troop == null) continue;

                    candidatesScanned++;
                    if (rule != null && !TroopTemplateMatcher.MatchesRule(troop, rule)) continue;
                    float score = TroopTemplateMatcher.ScoreCandidate(troop, rule, garrisonRoster, targetTotal);
                    if (float.IsNegativeInfinity(score)) continue;
                    scoredCandidates.Add((troop, volunteerTypes, i, score));
                }
            }

            scoredCandidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));

            foreach (var candidate in scoredCandidates)
            {
                int cost = DefaultGoldPerRecruit;
                if (budgetRemaining < cost) break;
                if (leaderGold < cost) break;
                if (candidate.Slots[candidate.SlotIndex] == null) continue;

                try
                {
                    recruitingParty.AddElementToMemberRoster(candidate.Troop, 1, false);
                    candidate.Slots[candidate.SlotIndex] = null!;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: AddElementToMemberRoster threw for '{candidate.Troop.StringId}': {ex.Message}");
                    continue;
                }

                try
                {
                    ownerHero.ChangeHeroGold(-cost);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: ChangeHeroGold(-{cost}) threw: {ex.Message}");
                }

                budgetRemaining -= cost;
                leaderGold -= cost;
                spent += cost;
                recruited++;
            }

            DecisionAuditLogger.LogRule(
                decisionType: "RecruitFromVillage",
                inputSummary: $"home={home.StringId} village={village.StringId} notables={village.Notables.Count} candidates={candidatesScanned}",
                decisionJson: $"{{\"home\":\"{home.StringId}\",\"village\":\"{village.StringId}\",\"recruited\":{recruited},\"spent\":{spent},\"budgetRemaining\":{budgetRemaining}}}",
                accepted: recruited > 0);

            Logger.Info($"  Recruiter '{recruitingParty.Name}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar）");
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitFromTargetVillage failed", ex);
        }
        return recruited;
    }

    /// <summary>
    /// 从首府 GarrisonParty 抽 <paramref name="want"/> 个低 Tier 兵进入 <paramref name="escortRoster"/>，
    /// 但严格保留 <c>rule.MinimumDefenders</c> 不被击穿；返回实际抽出的人数。
    /// 与 <see cref="GarrisonTransferManager"/> 相同的"低 Tier 优先"策略（高 Tier 留守）。
    /// </summary>
    private static int ExtractLowTierEscort(Town homeTown, TroopRoster escortRoster, int want, TownGarrisonRule rule)
    {
        int extracted = 0;
        try
        {
            var garrison = homeTown?.GarrisonParty;
            var sourceRoster = garrison?.MemberRoster;
            if (sourceRoster == null) return 0;

            int minDefenders = Math.Max(0, rule?.MinimumDefenders ?? 0);
            int total = sourceRoster.TotalManCount;
            if (total <= minDefenders) return 0;

            List<TroopRosterElement> elements;
            try
            {
                elements = sourceRoster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0)
                    .OrderBy(e => e.Character.Tier)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Error("ExtractLowTierEscort: GetTroopRoster/sort failed", ex);
                return 0;
            }

            foreach (var elem in elements)
            {
                if (extracted >= want) break;
                var ch = elem.Character;
                if (ch == null) continue;

                int currentSourceTotal = sourceRoster.TotalManCount;
                int floor = currentSourceTotal - minDefenders;
                if (floor <= 0) break;

                int needed = want - extracted;
                int take = Math.Min(elem.Number, Math.Min(needed, floor));
                if (take <= 0) break;

                try
                {
                    sourceRoster.RemoveTroop(ch, take, default, 0);
                    escortRoster.AddToCounts(ch, take, false, 0, 0);
                    extracted += take;
                }
                catch (Exception ex)
                {
                    Logger.Error($"ExtractLowTierEscort: per-element transfer failed for '{ch.StringId}' take={take}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ExtractLowTierEscort failed", ex);
        }
        return extracted;
    }

    /// <summary>派遣失败时把已抽离的护卫还回首府 garrison。</summary>
    private static void TryRestoreEscort(Town homeTown, TroopRoster escortRoster)
    {
        try
        {
            var garrison = homeTown?.GarrisonParty;
            var sourceRoster = garrison?.MemberRoster;
            if (sourceRoster == null || escortRoster == null) return;
            foreach (var elem in escortRoster.GetTroopRoster())
            {
                if (elem.Character == null || elem.Character.IsHero) continue;
                sourceRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
            }
            escortRoster.RemoveIf(e => e.Character != null && !e.Character.IsHero);
        }
        catch (Exception ex)
        {
            Logger.Error("TryRestoreEscort failed", ex);
        }
    }

    private void TransferAndDisband(MobileParty recruiter, Settlement home)
    {
        try
        {
            var town = home.Town;
            if (town == null)
            {
                Logger.Warn($"  Recruiter '{recruiter.Name}' 回到非 Town settlement '{home.Name}'，直接解散");
                DisbandPartyAction.StartDisband(recruiter);
                _lifecycle.UntrackParty(recruiter);
                _visitedPerParty.Remove(recruiter);
                return;
            }

            var garrison = town.GarrisonParty;
            var recruiterRoster = recruiter.MemberRoster;
            int transferred = 0;

            if (garrison != null && recruiterRoster != null)
            {
                var elements = recruiterRoster.GetTroopRoster();
                foreach (var elem in elements)
                {
                    if (elem.Character == null || elem.Character.IsHero) continue;
                    garrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                    transferred += elem.Number;
                }
                recruiterRoster.RemoveIf(e => e.Character != null && !e.Character.IsHero);
            }

            DecisionAuditLogger.LogRule(
                decisionType: "TransferRecruitedTroops",
                inputSummary: $"home={home.StringId} recruiter={recruiter.StringId} transferred={transferred}",
                decisionJson: $"{{\"home\":\"{home.StringId}\",\"transferred\":{transferred}}}",
                accepted: true);

            Logger.Info($"  Recruiter '{recruiter.Name}': 转入 {transferred} 名兵员到 '{home.Name}' 驻军，解散队伍");

            DisbandPartyAction.StartDisband(recruiter);
            _lifecycle.UntrackParty(recruiter);
            _visitedPerParty.Remove(recruiter);
        }
        catch (Exception ex)
        {
            Logger.Error("TransferAndDisband failed", ex);
        }
    }
}
