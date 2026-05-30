using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>朝某定居点逼近的敌对单位:健康兵力 + 估计到达 tick 数。</summary>
public readonly struct ConvergingHostile
{
    public ConvergingHostile(float strength, int etaTicks)
    {
        Strength = strength;
        EtaTicks = etaTicks;
    }
    public float Strength { get; }
    public int EtaTicks { get; }
}

/// <summary>
/// 只读、无副作用的敌对队伍空间扫描器。基于 vanilla
/// MobileParty.StartFindingLocatablesAroundPosition 定位 API
/// (现有用例见 SallyForth/SallyDispatcher.FindBestEnemyTarget)。
/// 派发风险(Part D)与威胁预测(Part C)共用。
/// </summary>
public static class HostilePartyScanner
{
    /// <summary>
    /// <paramref name="point"/> 周围 <paramref name="radius"/> 内、对
    /// <paramref name="friendlyFaction"/> 敌对的军事队伍「健康兵力」之和
    /// (TotalManCount − TotalWounded)。0 = 安全。无副作用,可跨线程。
    /// </summary>
    public static float HostileStrengthNear(Vec2 point, float radius, IFaction friendlyFaction)
    {
        if (friendlyFaction == null || radius <= 0f) return 0f;
        float total = 0f;
        try
        {
            var search = MobileParty.StartFindingLocatablesAroundPosition(point, radius);
            for (var p = MobileParty.FindNextLocatable(ref search);
                 p != null;
                 p = MobileParty.FindNextLocatable(ref search))
            {
                if (!IsHostileMilitary(p, friendlyFaction)) continue;
                total += HealthyStrength(p);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HostilePartyScanner.HostileStrengthNear failed", ex);
        }
        return total;
    }

    /// <summary>
    /// 朝 <paramref name="target"/> 逼近的敌对军事队伍(以 target 为 TargetSettlement)
    /// + 各自估计 ETA(tick 数)。ETA = 直线距离 / 队伍速度 / tickHours。无副作用。
    /// 供 ThreatForecast(Part C)用。
    /// </summary>
    public static List<ConvergingHostile> EnumerateConvergingHostiles(
        Settlement target, float radius, int tickHours)
    {
        var result = new List<ConvergingHostile>();
        if (target == null) return result;
        var friendly = target.MapFaction;
        if (friendly == null) return result;
        tickHours = Math.Max(1, tickHours);
        try
        {
            var pos = target.GetPosition2D;
            var search = MobileParty.StartFindingLocatablesAroundPosition(pos, Math.Max(1f, radius));
            for (var p = MobileParty.FindNextLocatable(ref search);
                 p != null;
                 p = MobileParty.FindNextLocatable(ref search))
            {
                if (!IsHostileMilitary(p, friendly)) continue;
                if (p.TargetSettlement != target) continue;   // 只取明确以 target 为目标的
                float dist = (p.GetPosition2D - pos).Length;
                float speed = Math.Max(0.1f, p.Speed);
                int eta = (int)Math.Round(dist / speed / tickHours);
                result.Add(new ConvergingHostile(HealthyStrength(p), Math.Max(0, eta)));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"HostilePartyScanner.EnumerateConvergingHostiles failed for '{target?.StringId}'", ex);
        }
        return result;
    }

    /// <summary>该队伍是否为对 <paramref name="friendly"/> 敌对的、非玩家主队的活动队伍。</summary>
    private static bool IsHostileMilitary(MobileParty p, IFaction friendly)
    {
        if (p == null || !p.IsActive) return false;
        if (p == MobileParty.MainParty) return false;
        var f = p.MapFaction;
        if (f == null || f == friendly) return false;
        return f.IsAtWarWith(friendly);
    }

    /// <summary>队伍健康兵力 = TotalManCount − TotalWounded(镜像 SallyDispatcher 口径)。</summary>
    private static float HealthyStrength(MobileParty p)
    {
        try
        {
            var roster = p.MemberRoster;
            if (roster == null) return 0f;
            return Math.Max(0, roster.TotalManCount - roster.TotalWounded);
        }
        catch (Exception ex)
        {
            Logger.Warn($"HostilePartyScanner.HealthyStrength failed for '{p?.StringId}'", ex);
            return 0f;
        }
    }
}
