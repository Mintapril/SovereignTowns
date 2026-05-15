using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Decisions;
using SovereignTowns.Economy;
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
///   1. 派遣：仅当 homeTown == 当前首府 时；从首府 garrison 按 <see cref="EscortRatio"/>
///      抽低 Tier 兵作基础护卫（征兵队不受 50 floor 限制 — 算多少抽多少，0 也派遣）；
///      规划首个目标村庄；<see cref="MobileParty.SetMoveGoToSettlement"/> 出发。
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

    // B7.24/B7.25：征兵队基础护卫按 garrison 百分比抽（10%）；不受 50 floor 限制 — 算多少抽多少，0 也派遣。
    private const float EscortRatio = 0.10f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    /// <summary>每支招募队本次旅程已访问过的 village（避免立即回头）。瞬态。</summary>
    private readonly Dictionary<MobileParty, HashSet<Settlement>> _visitedPerParty = new();

    public RecruitmentManager(PartyLifecycleManager lifecycle, CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;

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

            // B7.15 multi-clan：仅该 town 所属 clan 的当前首府能派征兵队。
            // 玩家 + AI 各走各的 CapitalManager；不在 registry 的 clan（如 AI 未接管）直接拒绝。
            if (_capitalRegistry != null)
            {
                var mgr = _capitalRegistry.GetForSettlement(homeTown.Settlement);
                if (mgr is null)
                {
                    Logger.Debug($"  RecruitmentManager: '{homeTown.Name}' 不在受管 clan 名单，跳过派遣");
                    return false;
                }
                var capitalSettlement = mgr.GetCapitalSettlement();
                if (capitalSettlement == null || homeTown.Settlement != capitalSettlement)
                {
                    Logger.Debug($"  RecruitmentManager: '{homeTown.Name}' 非该 clan 当前首府，跳过派遣");
                    return false;
                }
            }
            else
            {
                Logger.Warn($"  RecruitmentManager: capitalRegistry == null，跳过首府校验（兼容模式）");
            }

            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();

            // B1 #7: pause when food trend below threshold (first-cause: before any limit check)
            if (FoodGuard.IsRecruitmentPausedForFood(homeTown, rule, "RecruitmentManager"))
                return false;

            if (!_lifecycle.CanCreateAnotherParty(homeTown.Settlement, PartyKind))
            {
                Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 已达征兵队上限，跳过");
                return false;
            }

            // B7.23：撤紧急模式 — mod 不应自作主张突破玩家配置的 Tier 过滤。
            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: null,
                matchingRule: rule);
            if (candidates.Count == 0)
            {
                Logger.Warn($"  RecruitmentManager: '{homeTown.Name}' 无可招募村庄候选 — 周边 village notable 没有符合规则 (Tier {rule.MinTier}-{rule.MaxTier} / 比例非零兵种) 的兵。考虑放宽 MinTier。");
                return false;
            }
            var target = candidates[0];

            // B7.25：征兵队不受 50 floor 限制 — garrison × 10% 算多少抽多少，0 也允许派遣裸车。
            int garrisonForEscort = homeTown.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            int escortRequested = (int)Math.Round(garrisonForEscort * EscortRatio);
            TroopRoster? escortRoster = null;
            int escortActual = 0;
            if (escortRequested > 0)
            {
                escortRoster = TroopRoster.CreateDummyTroopRoster();
                escortActual = ExtractLowTierEscort(homeTown, escortRoster, escortRequested);
                if (escortActual <= 0)
                {
                    escortRoster = null;
                    Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 抽不到护卫（garrison={garrisonForEscort}），0 护卫派遣");
                }
                else
                {
                    Logger.Info($"  RecruitmentManager: 抽 {escortActual} 名低 Tier 护卫从 '{homeTown.Name}' (garrison {garrisonForEscort} × {EscortRatio:P0})");
                }
            }
            else
            {
                Logger.Info($"  RecruitmentManager: '{homeTown.Name}' garrison={garrisonForEscort}，escort 计算为 0，裸车派遣");
            }

            // B7.27：派出征兵队前先预检玩家金币 + 扣初始本钱。AI clan 跳过扣费（保留 AI 阵营战役经济不被破坏）。
            bool isPlayerClanDispatch = homeTown.OwnerClan == Clan.PlayerClan;
            if (isPlayerClanDispatch)
            {
                if (!ModTreasury.CanAfford(DefaultInitialGold))
                {
                    Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 玩家金币不足 ({Hero.MainHero?.Gold ?? 0} < {DefaultInitialGold})，跳过派遣");
                    if (escortRoster != null && escortActual > 0)
                    {
                        TryRestoreEscort(homeTown, escortRoster);
                    }
                    return false;
                }
                if (!ModTreasury.Charge(ExpenseCategory.RecruiterSeed, DefaultInitialGold, $"recruiter_seed home={homeTown.Settlement.StringId}"))
                {
                    Logger.Info($"  RecruitmentManager: '{homeTown.Name}' ModTreasury.Charge 拒绝，跳过派遣");
                    if (escortRoster != null && escortActual > 0)
                    {
                        TryRestoreEscort(homeTown, escortRoster);
                    }
                    return false;
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
                // 注：扣费已发生但 party 没创建出来 → 损失 1000 denar；记 Warn 让玩家可查日志
                if (isPlayerClanDispatch)
                {
                    Logger.Warn($"  RecruitmentManager: 1000 denar 已扣但 party 创建失败 — 玩家损失");
                }
                return false;
            }

            party.SetMoveGoToSettlement(target.VillageSettlement, MobileParty.NavigationType.Default, false);
            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, PartyKind);
            _visitedPerParty[party] = new HashSet<Settlement>();

            // B7.27：通知 scheduler "刚派遣，首站已选"。后续招到人后由 OnHourlyTickParty 流程接管。
            try
            {
                var dispatchCapitalMgr = _capitalRegistry?.GetForSettlement(homeTown.Settlement);
                if (dispatchCapitalMgr != null)
                {
                    dispatchCapitalMgr.RecruiterScheduler.RecordVisit(homeTown.Settlement);  // 标记首府"刚出门"
                    // target 已由 RankCandidates 选过，预占让其他征兵队知道
                    float etaHours = ((party.GetPosition2D - target.VillageSettlement.GetPosition2D).Length) / Math.Max(party.Speed, 0.1f);
                    dispatchCapitalMgr.RecruiterScheduler.PreemptiveBook(target.VillageSettlement, party, etaHours);
                }
            }
            catch (Exception schedEx)
            {
                Logger.Warn("RecruiterScheduler first-hop bookkeeping failed: " + schedEx.Message);
            }

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
    /// 规划巡回的下一站。B7.27：优先走 ClanRecruiterScheduler（多队互补）；失败回退原 RankCandidates 路径。
    /// </summary>
    private Settlement? PlanNextHop(MobileParty party, Settlement home)
    {
        try
        {
            // B7.27：优先用 scheduler（统一管理多队预占）
            var capitalMgr = _capitalRegistry?.GetForSettlement(home);
            if (capitalMgr != null)
            {
                var next = capitalMgr.RecruiterScheduler.PickNextVillage(party);
                if (next != null) return next;
            }

            // 回退：scheduler 返回 null（或 registry 不可用） → 走原 RankCandidates 路径，但同样应用本趟 visited 排除
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

            // B7.20：单兵金币折扣硬编码 0.5（半价）—— 不再可配置，避免破坏游戏数值平衡。
            // AI 招兵强制免费（AI 领主金币本来就紧张，按玩家公式扣会让 AI 阵营破产）。
            const float CostDiscount = 0.5f;
            bool isAiClan = home.OwnerClan != Clan.PlayerClan;
            int costPerRecruit = isAiClan ? 0 : Math.Max(1, (int)Math.Round(DefaultGoldPerRecruit * CostDiscount));

            // B7.20：volunteer 倍率硬编码 2.0 —— vanilla maxIdx 通常 0–2（1–3 个 slot），
            // ×2 后扩大可拉取的 slot 范围（最多 = volunteerTypes.Length - 1 即 5，全 6 slot）。
            const float VolunteerMul = 2.0f;

            int spent = 0;
            int candidatesScanned = 0;
            var scoredCandidates = new List<(CharacterObject Troop, CharacterObject[] Slots, int SlotIndex, float Score)>();
            var garrisonRoster = home.Town?.GarrisonParty?.MemberRoster;
            int targetTotal = Math.Max(1, rule?.TargetTotalCount ?? 1);

            // B7.23：撤紧急模式 — 不绕过 Tier。

            // B7.22 Fix per-role 饱和：预测「征兵队回家合并后」各 role 总人数。
            // 公式：projected_role = garrison.role + recruiter.role(已有) + gainedThisCall
            // 若 projected_role >= target_role → 跳过该 role 候选。
            var snapGarrison = SovereignTowns.Evaluators.GenericTroopMatcher.Snapshot(garrisonRoster);
            var snapRecruiter = SovereignTowns.Evaluators.GenericTroopMatcher.Snapshot(recruitingParty.MemberRoster);
            int rTargetCav = (int)Math.Round((rule?.CavalryRatio  ?? 0f) * targetTotal);
            int rTargetHa  = (int)Math.Round((rule?.HorseArcherRatio ?? 0f) * targetTotal);
            int rTargetInf = (int)Math.Round((rule?.InfantryRatio ?? 0f) * targetTotal);
            int rTargetRng = (int)Math.Round((rule?.RangedRatio ?? 0f) * targetTotal);
            int rGainCav = 0, rGainHa = 0, rGainInf = 0, rGainRng = 0;

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

                // B7.14: 把 vanilla 的 slot 上限按倍率扩大，但永远不超过 volunteerTypes 实际长度。
                // maxIdx 是 inclusive 索引，所以 slotCount = maxIdx + 1；× 倍率后再 -1 得新 inclusive 上限。
                int effectiveMaxIdx = Math.Min(
                    volunteerTypes.Length - 1,
                    Math.Max(maxIdx, (int)Math.Round((maxIdx + 1) * VolunteerMul) - 1));

                for (int i = 0; i < volunteerTypes.Length && i <= effectiveMaxIdx; i++)
                {
                    var troop = volunteerTypes[i];
                    if (troop == null) continue;

                    candidatesScanned++;
                    // B7.21 Fix B：紧急模式用放宽 tier 的副本判定，但 score 仍按原 rule 计算（避免分桶失真）
                    if (rule != null && !TroopTemplateMatcher.MatchesRule(troop, rule)) continue;
                    float score = TroopTemplateMatcher.ScoreCandidate(troop, rule, garrisonRoster, targetTotal);
                    if (float.IsNegativeInfinity(score)) continue;
                    scoredCandidates.Add((troop, volunteerTypes, i, score));
                }
            }

            scoredCandidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));

            foreach (var candidate in scoredCandidates)
            {
                int cost = costPerRecruit; // B7.20：硬编码 50% 折扣（5 denar/兵），AI 免费
                if (budgetRemaining < cost) break;
                if (leaderGold < cost) break;
                if (candidate.Slots[candidate.SlotIndex] == null) continue;

                // B7.22 Fix per-role 饱和检查
                var roleOfCand = SovereignTowns.Evaluators.GenericTroopMatcher.GetRole(candidate.Troop);
                int projected, target;
                switch (roleOfCand)
                {
                    case SovereignTowns.Evaluators.GenericTroopRole.Cavalry:  projected = snapGarrison.Cavalry  + snapRecruiter.Cavalry  + rGainCav; target = rTargetCav; break;
                    case SovereignTowns.Evaluators.GenericTroopRole.HorseArcher: projected = snapGarrison.HorseArcher + snapRecruiter.HorseArcher + rGainHa; target = rTargetHa; break;
                    case SovereignTowns.Evaluators.GenericTroopRole.Infantry: projected = snapGarrison.Infantry + snapRecruiter.Infantry + rGainInf; target = rTargetInf; break;
                    case SovereignTowns.Evaluators.GenericTroopRole.Ranged: projected = snapGarrison.Ranged + snapRecruiter.Ranged + rGainRng; target = rTargetRng; break;
                    default: continue;
                }
                if (projected >= target) continue;

                try
                {
                    recruitingParty.AddElementToMemberRoster(candidate.Troop, 1, false);
                    candidate.Slots[candidate.SlotIndex] = null!;
                    switch (roleOfCand)
                    {
                        case SovereignTowns.Evaluators.GenericTroopRole.Cavalry:  rGainCav++; break;
                        case SovereignTowns.Evaluators.GenericTroopRole.HorseArcher: rGainHa++; break;
                        case SovereignTowns.Evaluators.GenericTroopRole.Infantry: rGainInf++; break;
                        case SovereignTowns.Evaluators.GenericTroopRole.Ranged: rGainRng++; break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: AddElementToMemberRoster threw for '{candidate.Troop.StringId}': {ex.Message}");
                    continue;
                }

                // B7.27：玩家氏族走 ModTreasury 统一记账；AI clan 已在上方设 cost = 0，跳过扣费
                if (!isAiClan)
                {
                    ModTreasury.Charge(ExpenseCategory.RecruiterWage, cost, $"recruit village={village.StringId} troop={candidate.Troop.StringId}");
                }
                // AI clan：保持原行为（不扣费），cost 已是 0

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

            // B7.27：通知 scheduler 本次访问，更新该 village 的 LastRecruitedAt
            if (recruited > 0)
            {
                try
                {
                    var visitCapitalMgr = _capitalRegistry?.GetForSettlement(home);
                    visitCapitalMgr?.RecruiterScheduler.RecordVisit(village);
                }
                catch (Exception ex) { Logger.Warn("RecruiterScheduler.RecordVisit (per-village) failed: " + ex.Message); }
            }
            Logger.Info($"  Recruiter '{recruitingParty.Name}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar）");
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitFromTargetVillage failed", ex);
        }
        return recruited;
    }

    /// <summary>
    /// 从首府 GarrisonParty 抽 <paramref name="want"/> 个低 Tier 兵进入 <paramref name="escortRoster"/>。
    /// 返回实际抽出的人数。低 Tier 优先（高 Tier 留守）。
    /// B7.24：不受 MinDef 限制；调用方决定 want（按 garrison 百分比，征兵队不设 50 floor）。
    ///
    /// B2 重构（2026-05-14）：实现搬运至 SovereignTowns.Common.TroopTransferHelper（LowestTierFirst）。
    /// 注：原实现用 sourceRoster.RemoveTroop + escortRoster.AddToCounts(+take)；
    ///     helper 改用对称的 AddToCounts(-take)/AddToCounts(+take)，对非 Hero / 0-Xp / 0-Wounded
    ///     的基础兵种 net 效果等价。
    /// </summary>
    private static int ExtractLowTierEscort(Town homeTown, TroopRoster escortRoster, int want)
    {
        var sourceRoster = homeTown?.GarrisonParty?.MemberRoster;
        if (sourceRoster == null || escortRoster == null) return 0;
        return TroopTransferHelper.TransferFromGarrison(
            sourceRoster, escortRoster, want, TroopTransferHelper.SortStrategy.LowestTierFirst);
    }

    /// <summary>派遣失败时把已抽离的护卫还回首府 garrison。
    /// B2 重构（2026-05-14）：实现搬运至 SovereignTowns.Common.TroopTransferHelper。</summary>
    private static void TryRestoreEscort(Town homeTown, TroopRoster escortRoster)
    {
        var sourceRoster = homeTown?.GarrisonParty?.MemberRoster;
        if (sourceRoster == null || escortRoster == null) return;
        TroopTransferHelper.TransferBackToGarrison(escortRoster, sourceRoster);
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
