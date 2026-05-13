using System;
using System.Globalization;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 防御反应等级。从 None（无需反应）到 Critical（即将被攻击），
/// 用作 GarrisonManager / PatrolManager 等上层调度的优先级输入。
/// </summary>
public enum DefenseUrgency
{
    /// <summary>无需防御反应。</summary>
    None = 0,
    /// <summary>警觉（仅记录，不调度兵力）。</summary>
    Low = 1,
    /// <summary>派出小队应对。</summary>
    Medium = 2,
    /// <summary>派出全副驻军主力。</summary>
    High = 3,
    /// <summary>紧急 — 即将/正在被攻击。</summary>
    Critical = 4
}

/// <summary>
/// 防御需求评估结果。不可变 readonly struct，可在任意 Tick 安全复制返回。
/// </summary>
public readonly struct DefenseDemand
{
    /// <summary>构造一份防御需求快照。</summary>
    public DefenseDemand(DefenseUrgency urgency, int neededTroops, string suggestedFormation, string reason)
    {
        Urgency = urgency;
        NeededTroops = neededTroops < 0 ? 0 : neededTroops;
        SuggestedFormation = suggestedFormation ?? "Mixed";
        Reason = reason ?? "";
    }

    /// <summary>防御反应紧急度。</summary>
    public DefenseUrgency Urgency { get; }

    /// <summary>建议从驻军中派出的人数（>=0）。</summary>
    public int NeededTroops { get; }

    /// <summary>建议编队风格："Mixed" / "Cavalry-Heavy" / "Defense-Only"。</summary>
    public string SuggestedFormation { get; }

    /// <summary>简短原因，供审计日志使用。</summary>
    public string Reason { get; }
}

/// <summary>
/// 无状态防御需求评估器。综合 <see cref="RiskAssessmentService"/>、
/// 当前驻军强度与 <see cref="TownGarrisonRule.WartimeMultiplier"/> 给出 <see cref="DefenseDemand"/>。
/// O(1)、无副作用，可在任意 Tick 调用。
/// </summary>
public static class SettlementDefenseDemandEvaluator
{
    private const string FormationMixed = "Mixed";
    private const string FormationCavalryHeavy = "Cavalry-Heavy";
    private const string FormationDefenseOnly = "Defense-Only";

    /// <summary>
    /// 评估某 Town 的防御需求。
    /// 入参为 null / 非玩家所有 / 非活跃时返回 <see cref="DefenseUrgency.None"/>。
    /// 任何异常都被吞掉并返回 None — 调用方负责审计 trail。
    /// </summary>
    public static DefenseDemand Evaluate(Town town)
    {
        try
        {
            if (town == null)
            {
                return new DefenseDemand(DefenseUrgency.None, 0, FormationMixed, "null town");
            }

            var settlement = town.Settlement;
            if (settlement == null)
            {
                return new DefenseDemand(DefenseUrgency.None, 0, FormationMixed, "null settlement");
            }

            if (town.OwnerClan != Clan.PlayerClan)
            {
                return new DefenseDemand(DefenseUrgency.None, 0, FormationMixed, "not player owned");
            }

            var risk = RiskAssessmentService.Assess(settlement);
            var rule = ConfigurationManager.GetRuleFor(town);
            int garrisonCount = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            float threat = settlement.NearbyLandThreatIntensity;
            float ally = settlement.NearbyLandAllyIntensity;
            float wartimeMul = rule?.WartimeMultiplier ?? 1f;
            if (wartimeMul < 1f) wartimeMul = 1f;

            if (town.IsUnderSiege)
            {
                return new DefenseDemand(
                    DefenseUrgency.Critical,
                    garrisonCount,
                    FormationDefenseOnly,
                    "under siege");
            }

            var ci = CultureInfo.InvariantCulture;

            if (risk.Level >= RiskLevel.High)
            {
                int needed = (int)Math.Min(garrisonCount * 0.8f, threat * 10f * wartimeMul);
                string formation = (ally > threat * 0.5f) ? FormationMixed : FormationCavalryHeavy;
                string reason = "high risk threat=" + threat.ToString("F1", ci) +
                                " ally=" + ally.ToString("F1", ci);
                return new DefenseDemand(DefenseUrgency.High, needed, formation, reason);
            }

            if (risk.Level >= RiskLevel.Medium)
            {
                int needed = (int)Math.Min(garrisonCount * 0.4f, threat * 5f * wartimeMul);
                string reason = "medium risk threat=" + threat.ToString("F1", ci);
                return new DefenseDemand(DefenseUrgency.Medium, needed, FormationMixed, reason);
            }

            if (risk.Level >= RiskLevel.Low)
            {
                return new DefenseDemand(DefenseUrgency.Low, 0, FormationMixed, "minor threat");
            }

            return new DefenseDemand(DefenseUrgency.None, 0, FormationMixed, "safe");
        }
        catch (Exception ex)
        {
            Logger.Error("SettlementDefenseDemandEvaluator.Evaluate failed", ex);
            return new DefenseDemand(DefenseUrgency.None, 0, FormationMixed, "evaluation error");
        }
    }
}
