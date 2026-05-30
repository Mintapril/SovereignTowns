using System;
using System.Collections.Generic;
using SovereignTowns.Algorithm;
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
    // T1 重整 2026-05-18：seed gold 统一到 StPartyComponent.DefaultSeedGold，删除 RecruiterSeedGold 配置项。

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
    /// 由 CapitalLogisticsManager 请求派出一支定向征兵队。仅在 <paramref name="homeTown"/> 是该 clan
    /// 当前首府时派遣。
    /// <list type="bullet">
    ///   <item><paramref name="itinerary"/> — MCMF 选定的多站村庄行程（已按地理最近邻排序）。</item>
    ///   <item><paramref name="role"/> — 定向招募兵种。</item>
    ///   <item><paramref name="tripTarget"/> — 本趟招募人数目标。</item>
    ///   <item><paramref name="mode"/> — GarrisonRole / HonorGuardPrecise。</item>
    ///   <item><paramref name="preciseTemplate"/> — HonorGuardPrecise 模式下的模板快照；GarrisonRole 模式 ignore。</item>
    /// </list>
    /// cap 桶按 mode 选 KindRecruiter / KindHonorGuardRecruiter，PartyLifecycleManager 守门。
    /// </summary>
    public bool TryDispatchRecruiter(
        Town homeTown, IReadOnlyList<Settlement> itinerary,
        GenericTroopRole role, int tripTarget,
        RecruiterMode mode, IReadOnlyDictionary<string, int>? preciseTemplate,
        string reason)
    {
        string partyKind = mode == RecruiterMode.HonorGuardPrecise
            ? PartyLifecycleManager.KindHonorGuardRecruiter
            : PartyLifecycleManager.KindRecruiter;
        try
        {
            if (homeTown?.Settlement == null) return false;

            Settlement? firstStop = null;
            int stops = itinerary?.Count ?? 0;
            for (int i = 0; i < stops; i++)
            {
                if (itinerary![i] != null) { firstStop = itinerary[i]; break; }
            }
            if (firstStop == null) return false;

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

            // PR-5'(2026-05-24): IsClanAtWar removed from GarrisonAllocationSolver; war-buffer block deleted.

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

            if (!_lifecycle.CanCreateAnotherParty(homeTown.Settlement, partyKind))
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 已达征兵队上限，跳过");
                return false;
            }

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
                    $"recruiter_seed home={homeTown.Settlement.StringId}",
                    firstStop))
                {
                    if (escortRoster != null && escortActual > 0 && homeTown.GarrisonParty?.MemberRoster != null)
                        TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty.MemberRoster);
                    PartyMergeService.Instance.DestroyAndUntrack(party, "RecruitmentDispatcher seed failed rollback", deferIfInMapEvent: false);
                    return false;
                }
            }

            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, partyKind);

            // 出发首站 + 装载 MCMF 行程 + 模式 + 精确模板（HG 模式）：
            // 后续状态机由 StRecruiterPartyComponent.OnHourlyTickCore 接管。
            bool initialDispatchOk = false;
            try
            {
                if (party.PartyComponent is StRecruiterPartyComponent rp)
                {
                    rp.SetMode(mode, preciseTemplate);
                    rp.SetItinerary(itinerary, tripTarget);
                    rp.SetAssignedRole(role);
                    rp.SetAssignedTarget(firstStop);
                    party.SetMoveGoToSettlement(firstStop, MobileParty.NavigationType.Default, false);
                    rp.TransitionTo(StRecruiterPartyComponent.RecruiterPhase.Travelling);
                    initialDispatchOk = true;
                }
                else
                {
                    Logger.Error($"  RecruitmentDispatcher: created party '{party.StringId}' has unexpected component '{party.PartyComponent?.GetType().Name ?? "<null>"}'");
                }
            }
            catch (Exception ex) { Logger.Error("initial dispatch SetMove failed", ex); }
            if (!initialDispatchOk)
            {
                if (escortRoster != null && escortActual > 0 && homeTown.GarrisonParty?.MemberRoster != null)
                    TroopTransferHelper.TransferBackToGarrison(party.MemberRoster, homeTown.GarrisonParty.MemberRoster);
                PartyMergeService.Instance.DestroyAndUntrack(party, "RecruitmentDispatcher initial dispatch failed", deferIfInMapEvent: false);
                return false;
            }

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchRecruiter",
                inputSummary: $"home={homeTown.Settlement.StringId} role={role} mode={mode} tripTarget={tripTarget} stops={stops} first={firstStop.StringId} escort={escortActual}",
                decisionJson: $"{{\"home\":\"{homeTown.Settlement.StringId}\",\"role\":\"{role}\",\"mode\":\"{mode}\",\"tripTarget\":{tripTarget},\"stops\":{stops},\"first\":\"{firstStop.StringId}\",\"escort\":{escortActual},\"reason\":\"{AuditHelpers.EscapeJson(reason)}\"}}",
                accepted: true);

            Logger.Info($"  RecruitmentDispatcher: 派出征兵队 '{homeTown.Name}' → role={role} mode={mode} 行程 {stops} 站，首站 '{firstStop.Name}' (escort={escortActual})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchRecruiter failed", ex);
            return false;
        }
    }
}
