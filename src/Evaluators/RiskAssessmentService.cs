using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Evaluators;

/// <summary>
/// Discrete risk band derived from the underlying numeric score.
/// </summary>
public enum RiskLevel
{
    Safe = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>
/// Immutable risk assessment value: a discrete level, a numeric score
/// (0.0 ~ 10.0+, higher == more dangerous), and a short human-readable reason
/// suitable for audit logging.
/// </summary>
public readonly struct RiskAssessment
{
    /// <summary>
    /// Construct a new <see cref="RiskAssessment"/>.
    /// </summary>
    /// <param name="level">Discrete risk band.</param>
    /// <param name="score">Numeric risk score (higher is more dangerous).</param>
    /// <param name="reason">Short human-readable reason, used in audit logs.</param>
    public RiskAssessment(RiskLevel level, float score, string reason)
    {
        Level = level;
        Score = score;
        Reason = reason;
    }

    /// <summary>Discrete risk band.</summary>
    public RiskLevel Level { get; }

    /// <summary>Numeric risk score (0.0 ~ 10.0+, higher is more dangerous).</summary>
    public float Score { get; }

    /// <summary>Short human-readable explanation, intended for audit logging.</summary>
    public string Reason { get; }
}

/// <summary>
/// Stateless evaluator that converts vanilla Bannerlord <see cref="Settlement"/>
/// signals into a <see cref="RiskAssessment"/>. All methods are pure and
/// thread-safe; no fields, no caching, no event subscriptions, no logging —
/// callers own the audit trail.
/// </summary>
public static class RiskAssessmentService
{
    /// <summary>
    /// Assess the current risk of the given <paramref name="settlement"/>.
    /// Has no side effects and is safe to call from any thread.
    /// A <c>null</c> input returns <see cref="RiskLevel.Safe"/> with score 0
    /// and reason <c>"null settlement"</c>.
    /// </summary>
    /// <param name="settlement">The settlement to evaluate; may be <c>null</c>.</param>
    /// <returns>An immutable <see cref="RiskAssessment"/> describing current risk.</returns>
    public static RiskAssessment Assess(Settlement settlement)
    {
        if (settlement == null)
        {
            return new RiskAssessment(RiskLevel.Safe, 0f, "null settlement");
        }

        if (!settlement.IsActive)
        {
            return new RiskAssessment(RiskLevel.Safe, 0f, "inactive settlement");
        }

        if (settlement.IsUnderSiege)
        {
            return new RiskAssessment(RiskLevel.Critical, 10f, "under siege");
        }

        float threat = SanitizeIntensity(settlement.NearbyLandThreatIntensity);
        float ally = SanitizeIntensity(settlement.NearbyLandAllyIntensity);
        float ratio = threat / Math.Max(1f, ally + 1f);
        if (float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = 0f;

        string reason = $"threat/ally ratio {ratio:F2}";

        if (ratio >= 3.0f)
        {
            return new RiskAssessment(RiskLevel.High, ratio, reason);
        }

        if (ratio >= 1.5f)
        {
            return new RiskAssessment(RiskLevel.Medium, ratio, reason);
        }

        if (ratio >= 0.5f)
        {
            return new RiskAssessment(RiskLevel.Low, ratio, reason);
        }

        return new RiskAssessment(RiskLevel.Safe, ratio, reason);
    }

    private static float SanitizeIntensity(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
        return value;
    }
}
