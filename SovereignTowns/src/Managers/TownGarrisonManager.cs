using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Decisions;
using SovereignTowns.Evaluators;
using SovereignTowns.Llm;
using SovereignTowns.Recruitment;
using SovereignTowns.Upgrades;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Managers;

/// <summary>
/// MVP 1 规划模式：每日对每个玩家自有 Town 评估 (规则 + 风险 + 兵员组成 + 差距)。
/// 仅写日志，**不创建任何 MobileParty**。后续 MVP 阶段把"差距"转为 GarrisonDemand 投给 RecruitmentManager / GarrisonTransferManager。
/// </summary>
public sealed class TownGarrisonManager
{
    private readonly RecruitmentManager? _recruitmentManager;
    private readonly LLMReasoningService? _llmService;
    private readonly CapitalManager? _capitalManager;
    private readonly Transfer.CastleSupportManager? _castleSupportManager;
    // 当前 (B1) 未直接使用；CastleSupportManager 已通过 ctor 持有它。保留字段以备未来 case
    // 需要直派 transfer 时的 quick-access (避免再扩 CastleSupportManager 公开面)。
    private readonly Transfer.GarrisonTransferManager? _transferManager;

    public TownGarrisonManager(
        RecruitmentManager? recruitmentManager = null,
        LLMReasoningService? llmService = null,
        CapitalManager? capitalManager = null,
        Transfer.CastleSupportManager? castleSupportManager = null,
        Transfer.GarrisonTransferManager? transferManager = null)
    {
        _recruitmentManager = recruitmentManager;
        _llmService = llmService;
        _capitalManager = capitalManager;
        _castleSupportManager = castleSupportManager;
        _transferManager = transferManager;
    }

    /// <summary>
    /// 由 SovereignTownsCampaignBehavior 的 DailyTick 调用。
    /// 整个方法用 try-catch 包裹，任意 Town 评估失败不影响其它 Town。
    /// </summary>
    public void EvaluateAll()
    {
        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.AutoGarrison)
            {
                Logger.Debug("EvaluateAll skipped: AutoGarrison feature disabled");
                return;
            }

            var capital = _capitalManager?.GetCapital();
            if (capital == null)
            {
                Logger.Info("EvaluateAll skipped: no capital town (player owns 0 towns, or system disabled)");
                return;
            }
            if (capital.OwnerClan != Clan.PlayerClan)
            {
                Logger.Warn($"EvaluateAll: capital '{capital.Name}' no longer owned by player; waiting for OnSettlementOwnerChanged switch");
                return;
            }

            Logger.Info($"EvaluateAll: capital='{capital.Name}' (single-capital mode)");
            try
            {
                EvaluateOne(capital);
            }
            catch (Exception ex)
            {
                Logger.Error($"EvaluateOne failed for capital '{capital.Name}'", ex);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("EvaluateAll outer failure", ex);
        }
    }

    private void EvaluateOne(Town town)
    {
        var rule = ConfigurationManager.GetRuleFor(town);
        var risk = RiskAssessmentService.Assess(town.Settlement);
        var snap = TroopCompositionEvaluator.Snapshot(town.GarrisonParty?.MemberRoster);
        var genericSnap = GenericTroopMatcher.Snapshot(town.GarrisonParty?.MemberRoster);

        var multiplier = risk.Level >= RiskLevel.High ? rule.WartimeMultiplier : rule.PeacetimeMultiplier;
        var effectiveTarget = (int)Math.Round(rule.TargetTotalCount * multiplier);
        var totalGap = effectiveTarget - snap.Total;

        var targetCavalry  = (int)Math.Round(rule.CavalryRatio   * effectiveTarget);
        var targetInfantry = (int)Math.Round(rule.InfantryRatio  * effectiveTarget);
        var targetArchers  = (int)Math.Round(rule.ArcherRatio    * effectiveTarget);
        var targetCrossbow = (int)Math.Round(rule.CrossbowRatio  * effectiveTarget);
        var targetThrower  = (int)Math.Round(rule.ThrowerRatio   * effectiveTarget);
        var targetTier1 = GenericTroopMatcher.TargetCount(rule.Tier1Ratio, effectiveTarget);
        var targetTier2 = GenericTroopMatcher.TargetCount(rule.Tier2Ratio, effectiveTarget);
        var targetTier3 = GenericTroopMatcher.TargetCount(rule.Tier3Ratio, effectiveTarget);
        var targetTier4 = GenericTroopMatcher.TargetCount(rule.Tier4Ratio, effectiveTarget);
        var targetTier5 = GenericTroopMatcher.TargetCount(rule.Tier5Ratio, effectiveTarget);
        var targetTier6 = GenericTroopMatcher.TargetCount(rule.Tier6Ratio, effectiveTarget);

        Logger.Info(
            $"Town '{town.Name}' risk={risk.Level}({risk.Score:F2}, {risk.Reason}) " +
            $"target={effectiveTarget}(x{multiplier:F1}) current={snap.Total} gap={totalGap} " +
            $"siege={town.IsUnderSiege} foodChange={town.FoodChange:F2}");

        Logger.Info($"  composition: {snap.ToOneLineSummary()}");
        Logger.Info(
            $"  per-type gap: cav={targetCavalry - snap.Cavalry} inf={targetInfantry - snap.Infantry} " +
            $"arc={targetArchers - snap.Archers} crb={targetCrossbow - snap.Crossbows} thr={targetThrower - snap.Throwers}");
        Logger.Info(
            $"  per-tier gap: t1={targetTier1 - genericSnap.Tier1} t2={targetTier2 - genericSnap.Tier2} " +
            $"t3={targetTier3 - genericSnap.Tier3} t4={targetTier4 - genericSnap.Tier4} " +
            $"t5={targetTier5 - genericSnap.Tier5} t6={targetTier6 - genericSnap.Tier6}");

        if (snap.Total < rule.MinimumDefenders)
        {
            Logger.Warn($"  ALERT: garrison {snap.Total} < minimumDefenders {rule.MinimumDefenders}");
        }
        if (town.FoodChange < rule.FoodSafetyThreshold)
        {
            Logger.Warn($"  ALERT: foodChange {town.FoodChange:F2} < threshold {rule.FoodSafetyThreshold:F2} — recruitment should pause");
        }

        // 上一轮 LLM 推理结果（如果有）
        var townId = town.Settlement?.StringId ?? town.Name?.ToString() ?? "<unknown>";
        var pendingLlmAdvice = town.Settlement != null ? LlmAutoExecuteBridge.ConsumePendingAdvice(town.Settlement) : null;
        if (pendingLlmAdvice != null)
        {
            Logger.Info($"  consuming LLM advice for '{town.Name}': action={pendingLlmAdvice.Action} target={pendingLlmAdvice.TargetSettlement} mag={pendingLlmAdvice.MagnitudeSuggested}");
            // MVP 6: LLM 建议日志化 + 审计；实际行为仍由规则引擎驱动以保稳定性
            // 后续版本：将 LLM advice 转为 GarrisonDecision 并优先于规则引擎执行
        }

        // 规则引擎决策 + 审计 + 派发到 Manager
        var decisions = RuleBasedFallbackDecisionMaker.Decide(town);
        var inputSummary = $"town={townId} risk={risk.Level} target={effectiveTarget} current={snap.Total} gap={totalGap}";

        // MVP 6: fire-and-forget 启动下一轮 LLM 推理（**绝不在当轮等待**）
        if (_llmService != null && town.Settlement != null)
        {
            LlmAutoExecuteBridge.TryStartReasoning(_llmService, town.Settlement, inputSummary);
        }

        foreach (var d in decisions)
        {
            Logger.Info($"  decision: {d.Kind} priority={d.Priority} magnitude={d.Magnitude} reason='{d.Reason}'");

            bool dispatched = false;
            string? rejectionReason = null;
            if (d.Kind == GarrisonActionKind.RequestRecruitment && _recruitmentManager != null)
            {
                dispatched = _recruitmentManager.TryDispatchRecruiter(town, d);
                rejectionReason = dispatched ? null : "RecruitmentManager declined (limit / disabled / no candidate)";
            }
            else if (d.Kind == GarrisonActionKind.RequestUpgrade && rule.AllowAutoUpgrade)
            {
                var budgetCap = Math.Max(rule.BudgetLimit / 4, 500);
                var report = TroopUpgradeService.TryUpgradeGarrison(town, budgetCap, maxUpgradesPerCall: 20);
                dispatched = report.DidWork;
                if (!dispatched) rejectionReason = $"upgraded=0 skipped={report.CandidatesSkipped} (no qualifying troops or out of budget)";
                else
                {
                    Logger.Info($"  upgrade: town='{town.Name}' upgraded={report.UpgradedUnits} xp={report.XpSpent} gold={report.GoldSpent}");
                }
            }
            else if (d.Kind == GarrisonActionKind.RequestUpgrade && !rule.AllowAutoUpgrade)
            {
                rejectionReason = "AllowAutoUpgrade=false in rule";
            }
            else if (_recruitmentManager == null && d.Kind == GarrisonActionKind.RequestRecruitment)
            {
                rejectionReason = "RecruitmentManager not yet wired (MVP 1 mode)";
            }
            else if (d.Kind == GarrisonActionKind.RequestTransferIn
                     && _castleSupportManager != null
                     && ConfigurationManager.Current.EnabledFeatures.CastleSupport)
            {
                int n = _castleSupportManager.TryDispatchForDemand(town, d.Magnitude);
                dispatched = n > 0;
                rejectionReason = dispatched ? null : "no feasible donor / already at transfer limit";
            }
            else if (d.Kind == GarrisonActionKind.RequestTransferIn
                     && !ConfigurationManager.Current.EnabledFeatures.CastleSupport)
            {
                rejectionReason = "CastleSupport feature disabled";
            }
            else
            {
                rejectionReason = $"MVP 3: action '{d.Kind}' not yet implemented";
            }

            DecisionAuditLogger.LogRule(
                decisionType: d.Kind.ToString(),
                inputSummary: inputSummary,
                decisionJson: $"{{\"kind\":\"{d.Kind}\",\"priority\":{d.Priority},\"magnitude\":{d.Magnitude},\"reason\":\"{AuditHelpers.EscapeJson(d.Reason)}\"}}",
                accepted: dispatched,
                rejectionReason: rejectionReason);
        }
    }

    private static List<Town> ListPlayerOwnedTowns()
    {
        var result = new List<Town>();
        var playerClan = Clan.PlayerClan;
        if (playerClan is null) return result;
        foreach (var t in Town.AllTowns)
        {
            if (t.IsTown && t.OwnerClan == playerClan)
            {
                result.Add(t);
            }
        }
        return result;
    }
}
