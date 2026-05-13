using System;
using SovereignTowns.Audit;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Configuration;

/// <summary>
/// Shared low-food early-out for recruitment paths. Reads <c>town.FoodChange</c> against
/// <see cref="TownGarrisonRule.FoodSafetyThreshold"/>; when paused, emits a single audit
/// entry per call so each blocked entry point is independently visible.
///
/// <para>
/// Not used by transfers (incoming troops do not raise food pressure) or upgrades
/// (no extra mouths). See B1 spec §3.4.
/// </para>
/// </summary>
public static class FoodGuard
{
    /// <summary>
    /// Returns <c>true</c> when the caller MUST early-return because the town's food
    /// trend is below the configured safety threshold. Side effect: when paused, logs
    /// one Info line and one Audit entry (Source=Rule, accepted=false).
    /// </summary>
    /// <param name="town">Target town; null OR a town with null Settlement is treated as "not paused" (caller's existing guards remain authoritative).</param>
    /// <param name="rule">Active rule (caller already resolved via <c>ConfigurationManager.GetRuleFor</c>).</param>
    /// <param name="callerLabel">Short string identifying the call site for audit (e.g. "RecruitmentManager"). Appears verbatim in both Logger output and audit inputSummary.</param>
    public static bool IsRecruitmentPausedForFood(Town town, TownGarrisonRule rule, string callerLabel)
    {
        try
        {
            if (town?.Settlement == null || rule == null) return false;
            if (town.FoodChange >= rule.FoodSafetyThreshold) return false;

            Logger.Info(
                $"FoodGuard: recruitment paused at '{town.Name}' by {callerLabel} " +
                $"(foodChange={town.FoodChange:F2} < threshold={rule.FoodSafetyThreshold:F2})");

            DecisionAuditLogger.LogRule(
                decisionType: "RecruitmentPausedLowFood",
                inputSummary: $"town={town.Settlement.StringId} caller={callerLabel} foodChange={town.FoodChange:F2}",
                decisionJson: $"{{\"threshold\":{rule.FoodSafetyThreshold:F2},\"foodChange\":{town.FoodChange:F2}}}",
                accepted: false,
                rejectionReason: "FoodSafetyThreshold");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"FoodGuard.IsRecruitmentPausedForFood threw for town '{town?.Name}': {ex.Message}");
            return false; // 失败时不阻塞业务路径
        }
    }
}
