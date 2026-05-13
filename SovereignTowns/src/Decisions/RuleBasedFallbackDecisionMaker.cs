using System;
using System.Collections.Generic;
using System.Globalization;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem.Settlements;
using Debug = TaleWorlds.Library.Debug;

namespace SovereignTowns.Decisions;

/// <summary>
/// Coarse classification of the action a decision maker is recommending. Numeric values are stable
/// so callers may persist or wire them across save/load boundaries.
/// </summary>
public enum GarrisonActionKind
{
    /// <summary>No action required.</summary>
    DoNothing = 0,
    /// <summary>Dispatch a recruitment effort to fill the garrison (MVP 2 will execute).</summary>
    RequestRecruitment = 1,
    /// <summary>Pull troops in from another player-owned settlement (MVP 3.5).</summary>
    RequestTransferIn = 2,
    /// <summary>Upgrade existing low-tier garrison troops in place (MVP 3).</summary>
    RequestUpgrade = 3,
    /// <summary>Disband surplus garrison beyond the configured target (when AutoDisbandExcess is enabled).</summary>
    RequestDisbandExcess = 4,
    /// <summary>Food change has dropped below the safety threshold — recruitment should pause / be audited.</summary>
    AlertLowFood = 5,
    /// <summary>The town is currently under siege.</summary>
    AlertUnderSiege = 6,
    /// <summary>Garrison size has fallen below the configured minimum defender floor.</summary>
    AlertCriticalDefenders = 7
}

/// <summary>
/// A single garrison decision suggested by the rule engine. Immutable value type.
/// The contract intentionally exposes only a coarse, structured shape; the concrete execution
/// (recruitment plan, transfer order, audit log entry, ...) is the caller's responsibility.
/// </summary>
public readonly struct GarrisonDecision
{
    /// <summary>Construct a new <see cref="GarrisonDecision"/>.</summary>
    /// <param name="kind">Coarse classification of what should happen.</param>
    /// <param name="priority">Urgency in the range 0..100; higher is more urgent.</param>
    /// <param name="magnitude">Number of troops involved (recruitment / transfer / upgrade / disband). 0 for alerts that don't carry a count.</param>
    /// <param name="reason">Short human-readable explanation, intended for audit logging.</param>
    public GarrisonDecision(GarrisonActionKind kind, int priority, int magnitude, string reason)
    {
        Kind = kind;
        Priority = priority;
        Magnitude = magnitude;
        Reason = reason ?? string.Empty;
    }

    /// <summary>Coarse classification of the recommended action.</summary>
    public GarrisonActionKind Kind { get; }

    /// <summary>Urgency in the range 0..100; higher is more urgent.</summary>
    public int Priority { get; }

    /// <summary>Number of troops involved in this decision, or 0 for pure alerts.</summary>
    public int Magnitude { get; }

    /// <summary>Short human-readable explanation, intended for audit logging.</summary>
    public string Reason { get; }
}

/// <summary>
/// Stateless, deterministic, LLM-free rule engine that produces <see cref="GarrisonDecision"/>s
/// from the current world state of a player-owned <see cref="Town"/>.
///
/// <para>
/// This type is the fallback path when the (optional) LLM advisor is disabled or unreachable, and
/// therefore *itself* must be reliable: it does not allocate parties, does not mutate vanilla
/// state, does not log (callers own the audit trail), does not write files, and never throws —
/// any internal failure degrades to <see cref="Array.Empty{T}"/> after a single
/// <see cref="TaleWorlds.Library.Debug.Print(string, int, Debug.DebugColor)"/> note.
/// </para>
///
/// <para>
/// Every public method is O(1) with respect to the campaign map (it only inspects the single
/// supplied town plus its garrison roster) and is safe to call from an HourlyTick.
/// </para>
/// </summary>
public static class RuleBasedFallbackDecisionMaker
{
    /// <summary>
    /// Produce zero or more <see cref="GarrisonDecision"/>s for a single player-owned town,
    /// sorted by <see cref="GarrisonDecision.Priority"/> descending.
    /// </summary>
    /// <param name="town">The town to evaluate; <c>null</c> is treated as "no decisions".</param>
    /// <returns>
    /// A read-only list of decisions. Empty (<see cref="Array.Empty{T}"/>) when nothing needs to
    /// happen, when <paramref name="town"/> is <c>null</c>, or when the rule engine itself fails.
    /// </returns>
    public static IReadOnlyList<GarrisonDecision> Decide(Town town)
    {
        try
        {
            if (town is null)
            {
                return Array.Empty<GarrisonDecision>();
            }

            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule is null)
            {
                return Array.Empty<GarrisonDecision>();
            }

            var risk = RiskAssessmentService.Assess(town.Settlement);
            var snap = TroopCompositionEvaluator.Snapshot(town.GarrisonParty?.MemberRoster);

            var features = ConfigurationManager.Current?.EnabledFeatures ?? new EnabledFeatures();

            float multiplier = risk.Level >= RiskLevel.High
                ? rule.WartimeMultiplier
                : rule.PeacetimeMultiplier;
            int target = (int)Math.Round(rule.TargetTotalCount * multiplier);
            int gap = target - snap.Total;

            var decisions = new List<GarrisonDecision>(8);

            // ---- High-priority alerts ----------------------------------------------------------
            if (town.IsUnderSiege)
            {
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.AlertUnderSiege,
                    priority: 100,
                    magnitude: 0,
                    reason: "under siege"));
            }

            if (snap.Total < rule.MinimumDefenders)
            {
                int shortfall = rule.MinimumDefenders - snap.Total;
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.AlertCriticalDefenders,
                    priority: 95,
                    magnitude: shortfall,
                    reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "garrison {0} < minimumDefenders {1}",
                        snap.Total, rule.MinimumDefenders)));
            }

            if (town.FoodChange < rule.FoodSafetyThreshold)
            {
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.AlertLowFood,
                    priority: 70,
                    magnitude: 0,
                    reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "foodChange={0:F2}",
                        town.FoodChange)));
            }

            // ---- Recruitment -------------------------------------------------------------------
            // Honor AutoRecruitment for the strong path; the weaker path still surfaces a low-priority
            // intent so callers/UI can see a recommendation even when auto-recruit is off.
            if (gap >= 10 && features.AutoRecruitment)
            {
                int priority = 50 + Math.Min(gap, 100) / 2;
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.RequestRecruitment,
                    priority: priority,
                    magnitude: gap,
                    reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "gap={0} target={1}", gap, target)));
            }
            else if (gap >= 5)
            {
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.RequestRecruitment,
                    priority: 30,
                    magnitude: gap,
                    reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "gap={0} target={1}", gap, target)));
            }

            // ---- Upgrade existing low-tier troops ---------------------------------------------
            if (snap.Tier1To2 > snap.Total * 0.3 && rule.AllowAutoUpgrade)
            {
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.RequestUpgrade,
                    priority: 40,
                    magnitude: snap.Tier1To2 / 2,
                    reason: "too many low-tier troops"));
            }

            // ---- Disband surplus ---------------------------------------------------------------
            if (snap.Total > target * 1.2 && rule.AutoDisbandExcess)
            {
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.RequestDisbandExcess,
                    priority: 20,
                    magnitude: snap.Total - target,
                    reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "current {0} > 1.2 * target {1}", snap.Total, target)));
            }

            // ---- Cross-settlement transfer-in (intent only) -----------------------------------
            // CastleSupportManager decides if a feasible donor exists; this is the demand signal.
            if (gap >= 30 && features.CastleSupport)
            {
                decisions.Add(new GarrisonDecision(
                    GarrisonActionKind.RequestTransferIn,
                    priority: 35,
                    magnitude: gap,
                    reason: string.Format(
                        CultureInfo.InvariantCulture,
                        "large gap={0} — request transfer-in", gap)));
            }

            if (decisions.Count == 0)
            {
                return Array.Empty<GarrisonDecision>();
            }

            decisions.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
            return decisions;
        }
        catch (Exception ex)
        {
            // Cannot use Logger here by design (callers own the audit trail); fall back to vanilla
            // Debug.Print so the failure is at least visible in rgl_log.txt at low verbosity.
            try
            {
                Debug.Print(
                    "[SovereignTowns] RuleBasedFallbackDecisionMaker.Decide failed: " + ex.Message,
                    0,
                    Debug.DebugColor.Red);
            }
            catch
            {
                // Swallow — the fallback must never throw to the caller.
            }
            return Array.Empty<GarrisonDecision>();
        }
    }
}
