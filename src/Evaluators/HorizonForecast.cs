using System;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>
/// P3 时域预测:给时间展开 solver 提供每 tick 的威胁等级。
/// 详见 audits/2026-05-22-p3-lookahead-design.md §7。
/// </summary>
public interface IHorizonForecast
{
    /// <summary>定居点 s 在 <paramref name="tick"/> 的威胁等级。tick 0 = 当前;tick>0 = 投影。</summary>
    RiskLevel ThreatAt(Settlement s, int tick);
}

/// <summary>
/// 威胁预测(§7.2):tick 0 = 当前威胁;tick>0 = 把「ETA ≤ tick 的逼近敌军」按健康兵力
/// 总和映射成威胁等级,与当前威胁取较高者。宣战时机不可测 → 只吃已存在的逼近敌军。
/// </summary>
public sealed class ThreatForecast : IHorizonForecast
{
    private readonly float _radius;
    private readonly int _tickHours;

    public ThreatForecast(float radius, int tickHours)
    {
        _radius = radius;
        _tickHours = tickHours;
    }

    public RiskLevel ThreatAt(Settlement s, int tick)
    {
        if (s == null) return RiskLevel.Safe;
        var baseline = RiskLevel.Low;
        try
        {
            baseline = RiskAssessmentService.Assess(s).Level;
            if (tick <= 0) return baseline;

            float arrived = 0f;
            foreach (var h in HostilePartyScanner.EnumerateConvergingHostiles(s, _radius, _tickHours))
                if (h.EtaTicks <= tick) arrived += h.Strength;
            if (arrived <= 0f) return baseline;
            // 健康兵力总和 → 威胁等级。阈值为初值,须 in-game 调。
            RiskLevel bumped =
                arrived >= 150f ? RiskLevel.Critical :
                arrived >= 80f  ? RiskLevel.High :
                arrived >= 30f  ? RiskLevel.Medium :
                                  RiskLevel.Low;
            return (RiskLevel)Math.Max((int)baseline, (int)bumped);
        }
        catch (Exception ex)
        {
            Logger.Error($"ThreatForecast.ThreatAt failed for '{s?.StringId}'", ex);
            return baseline;
        }
    }
}
