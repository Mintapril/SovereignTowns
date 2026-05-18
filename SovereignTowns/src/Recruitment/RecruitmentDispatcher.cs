using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
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
/// 征兵队 Dispatcher（B16.3）。
///
/// 只负责"何时何地派遣征兵队"：FoodGuard 校验、capital 校验、扣 ModTreasury、抽护卫、
/// 创建 <see cref="StRecruiterPartyComponent"/> 并出发首站。
///
/// 所有"在飞中"状态机（多目标巡回 / 阈值检查 / 战后回家 / per-village 招募）
/// 在 <see cref="StRecruiterPartyComponent"/> 内；HourlyTick / MapEventEnded 由
/// <see cref="PartyLifecycleManager"/> 单点路由到 component。
/// </summary>
public sealed class RecruitmentDispatcher
{
    private const string PartyKind = PartyLifecycleManager.KindRecruiter;
    // H9/H10 (DeepSeek audit 2026-05-18)：常量改读 PartyThresholds。
    // T1 重整 2026-05-18：seed gold 统一到 StPartyComponent.DefaultSeedGold，删除 RecruiterSeedGold 配置项。
    private const int CandidateBatchSizeDefault = 8;
    private const float PlanMaxDistanceDefault = 100f;

    private static int CandidateBatchSize
        => ConfigurationManager.Current?.Thresholds?.RecruitmentCandidateBatchSize ?? CandidateBatchSizeDefault;
    private static float PlanMaxDistance
        => ConfigurationManager.Current?.Thresholds?.RecruitmentPlanMaxDistance ?? PlanMaxDistanceDefault;

    private static float EscortRatio
        => ConfigurationManager.Current?.Thresholds?.RecruiterEscortRatio ?? 0.10f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public RecruitmentDispatcher(PartyLifecycleManager lifecycle, CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }

    /// <summary>
    /// 由 CapitalLogisticsManager 请求派出征兵队。仅在 <paramref name="homeTown"/> 是该 clan 当前首府时派遣。
    /// </summary>
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

            // B7.15 multi-clan：仅该 town 所属 clan 的当前首府能派征兵队。
            if (_capitalRegistry != null)
            {
                var mgr = _capitalRegistry.GetForSettlement(homeTown.Settlement);
                if (mgr is null)
                {
                    Logger.Debug($"  RecruitmentDispatcher: '{homeTown.Name}' 不在受管 clan 名单，跳过派遣");
                    return false;
                }
                var capitalSettlement = _capitalRegistry.GetCapitalForClan(mgr.OwnerClan);
                if (capitalSettlement == null || homeTown.Settlement != capitalSettlement)
                {
                    Logger.Debug($"  RecruitmentDispatcher: '{homeTown.Name}' 非该 clan 当前首府，跳过派遣");
                    return false;
                }
            }
            else
            {
                Logger.Warn($"  RecruitmentDispatcher: capitalRegistry == null，跳过首府校验（兼容模式）");
            }

            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();

            // B17.4 S1：围城下不派征兵队（"开门即送"）。
            if (homeTown.IsUnderSiege)
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' is under siege — skip dispatch");
                return false;
            }

            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(homeTown, rule, "RecruitmentDispatcher"))
                return false;

            if (!_lifecycle.CanCreateAnotherParty(homeTown.Settlement, PartyKind))
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 已达征兵队上限，跳过");
                return false;
            }

            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: null,
                matchingRule: rule);
            // B17.4 B2：第一轮无候选 → 第二轮扩大到 Thresholds.RecruitmentFallbackMaxDistance。
            if (candidates.Count == 0)
            {
                float fallbackDist = ConfigurationManager.Current?.Thresholds?.RecruitmentFallbackMaxDistance ?? 0f;
                if (fallbackDist > PlanMaxDistance)
                {
                    Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 第一轮无候选，第二轮扩大到 {fallbackDist}");
                    candidates = RecruitmentPlanner.RankCandidates(
                        homeTown,
                        maxDistance: fallbackDist,
                        maxResults: CandidateBatchSize,
                        excludeSettlements: null,
                        matchingRule: rule);
                }
            }
            if (candidates.Count == 0)
            {
                Logger.Warn($"  RecruitmentDispatcher: '{homeTown.Name}' 无可招募村庄候选 — 周边 village notable 没有符合规则 (Tier {rule.MinTier}-{rule.MaxTier} / 比例非零兵种) 的兵。考虑放宽 MinTier。");
                return false;
            }
            var target = candidates[0];

            // B17.4 A3：空 garrison floor — 0 兵裸车遭遇即没。
            // 模型默认 RecruiterMinHomeGarrison=0（用户明确"不要 floor"），fallback 与模型对齐。
            int garrisonForEscort = homeTown.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            int minHomeGarrison = ConfigurationManager.Current?.Thresholds?.RecruiterMinHomeGarrison ?? 0;
            if (garrisonForEscort < minHomeGarrison)
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' garrison={garrisonForEscort} < RecruiterMinHomeGarrison={minHomeGarrison}, skip");
                return false;
            }

            // 注：原先有一段 "GarrisonParty == null 时重建" 分支，因 garrisonForEscort 已由 GarrisonParty 派生，
            // `garrisonForEscort > 0 && GarrisonParty == null` 不可达，已删除。下面 escort 抽兵分支自身包含 null 兜底。

            int escortRequested = (int)Math.Round(garrisonForEscort * EscortRatio);
            TroopRoster? escortRoster = null;
            int escortActual = 0;
            if (escortRequested > 0 && homeTown.GarrisonParty != null)
            {
                escortRoster = TroopRoster.CreateDummyTroopRoster();
                escortActual = TroopTransferHelper.TransferFromGarrison(
                    homeTown.GarrisonParty.MemberRoster, escortRoster, escortRequested, TroopTransferHelper.SortStrategy.LowestTierFirst);
                if (escortActual <= 0)
                {
                    escortRoster = null;
                    Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 抽不到护卫（garrison={garrisonForEscort}），0 护卫派遣");
                }
                else
                {
                    Logger.Info($"  RecruitmentDispatcher: 抽 {escortActual} 名低 Tier 护卫从 '{homeTown.Name}' (garrison {garrisonForEscort} × {EscortRatio:P0})");
                }
            }
            else
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' garrison={garrisonForEscort}，escort 计算为 0，裸车派遣");
            }

            // T1 重整 (doc §20 #1)：新流程"先创建 party → helper 扣款 + 注资 + 买粮"。
            // ModTreasury.Charge / Refund 全部由 helper 内部完成，外部不再单独扣款 + 退款。
            var party = StRecruiterPartyComponent.CreateForTown(homeTown, escortRoster);
            if (party == null)
            {
                Logger.Warn($"  RecruitmentDispatcher: CreateForTown 返回 null for '{homeTown.Name}'");
                if (escortRoster != null && escortActual > 0)
                {
                    if (homeTown.GarrisonParty?.MemberRoster != null)
                        TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty.MemberRoster);
                }
                return false;
            }

            // T1 重整：统一走基类 helper 处理"扣款 + 注资 + 买粮"。
            // 玩家路径扣款失败 → 把护卫还 garrison 并销毁 recruiter party；AI 路径不会失败。
            if (party.PartyComponent is StRecruiterPartyComponent stc)
            {
                if (!StPartyComponent.TrySeedAndBuyInitialFood(
                    stc, party, homeTown.Settlement,
                    ExpenseCategory.RecruiterSeed,
                    homeTown.OwnerClan,
                    $"recruiter_seed home={homeTown.Settlement.StringId}"))
                {
                    if (escortRoster != null && escortActual > 0 && homeTown.GarrisonParty?.MemberRoster != null)
                        TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty.MemberRoster);
                    PartyMergeService.Instance.DestroyAndUntrack(party, "RecruitmentDispatcher seed failed rollback", deferIfInMapEvent: false);
                    return false;
                }
            }

            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, PartyKind);

            // 出发首站：设 _assignedTarget + SetMoveGoToSettlement + 转 Travelling 阶段。
            // 后续状态机由 StRecruiterPartyComponent.OnHourlyTickCore 接管。
            try
            {
                if (party.PartyComponent is StRecruiterPartyComponent rp)
                {
                    rp.SetAssignedTarget(target.VillageSettlement);
                    party.SetMoveGoToSettlement(target.VillageSettlement, MobileParty.NavigationType.Default, false);
                    rp.TransitionTo(StRecruiterPartyComponent.RecruiterPhase.Travelling);
                }
            }
            catch (Exception ex) { Logger.Error("initial dispatch SetMove failed", ex); }

            // B7.27：通知 scheduler 首站已选。
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
            catch (Exception schedEx)
            {
                Logger.Warn("RecruiterScheduler first-hop bookkeeping failed: " + schedEx.Message);
            }

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchRecruiter",
                inputSummary: $"home={homeTown.Settlement.StringId} requested={requestedMagnitude} candidates={candidates.Count} target={target.VillageSettlement.StringId} escort={escortActual}",
                decisionJson: $"{{\"home\":\"{homeTown.Settlement.StringId}\",\"target\":\"{target.VillageSettlement.StringId}\",\"priority\":{target.PriorityScore:F2},\"estimatedTroops\":{target.EstimatedAvailableTroops},\"escort\":{escortActual},\"reason\":\"{AuditHelpers.EscapeJson(reason)}\"}}",
                accepted: true);

            Logger.Info($"  RecruitmentDispatcher: 派出征兵队 '{homeTown.Name}' → '{target.VillageSettlement.Name}' (priority={target.PriorityScore:F1}, escort={escortActual})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchRecruiter failed", ex);
            return false;
        }
    }
}
