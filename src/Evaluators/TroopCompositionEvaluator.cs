using System;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 兵种粗分类。判定主要基于 vanilla 运行时的 DefaultFormationClass，
/// 不依赖任何 stringId 硬编码，对 RBM 等覆盖兵种 xml 的 mod 完全友好。
/// </summary>
public enum TroopType
{
    Hero = 0,
    Noble = 1,
    HorseArcher = 2,
    Cavalry = 3,
    Ranged = 4,
    Infantry = 5
}

/// <summary>
/// 一份 <see cref="TroopRoster"/> 的兵种构成快照，无状态、可复制。
/// </summary>
public readonly struct CompositionSnapshot
{
    public CompositionSnapshot(
        int total,
        int heroes,
        int nobles,
        int horseArchers,
        int cavalry,
        int ranged,
        int infantry,
        int wounded,
        int tier1To2,
        int tier3To4,
        int tier5Plus)
    {
        Total = total;
        Heroes = heroes;
        Nobles = nobles;
        HorseArchers = horseArchers;
        Cavalry = cavalry;
        Ranged = ranged;
        Infantry = infantry;
        Wounded = wounded;
        Tier1To2 = tier1To2;
        Tier3To4 = tier3To4;
        Tier5Plus = tier5Plus;
    }

    public int Total { get; }
    public int Heroes { get; }
    public int Nobles { get; }
    public int HorseArchers { get; }
    public int Cavalry { get; }
    public int Ranged { get; }
    public int Infantry { get; }
    public int Wounded { get; }
    public int Tier1To2 { get; }
    public int Tier3To4 { get; }
    public int Tier5Plus { get; }

    /// <summary>
    /// 该 <paramref name="type"/> 占 <see cref="Total"/> 的比例。Total=0 时返回 0，避免除零。
    /// </summary>
    public float RatioOf(TroopType type)
    {
        if (Total <= 0) return 0f;
        var count = type switch
        {
            TroopType.Hero        => Heroes,
            TroopType.Noble       => Nobles,
            TroopType.HorseArcher => HorseArchers,
            TroopType.Cavalry     => Cavalry,
            TroopType.Ranged      => Ranged,
            TroopType.Infantry    => Infantry,
            _                     => 0
        };
        return (float)count / Total;
    }

    public string ToOneLineSummary()
    {
        var sb = new StringBuilder(96);
        var ci = CultureInfo.InvariantCulture;
        sb.Append("total=").Append(Total.ToString(ci));
        sb.Append(" cav=").Append(Cavalry.ToString(ci));
        sb.Append(" ha=").Append(HorseArchers.ToString(ci));
        sb.Append(" inf=").Append(Infantry.ToString(ci));
        sb.Append(" rng=").Append(Ranged.ToString(ci));
        sb.Append(" noble=").Append(Nobles.ToString(ci));
        sb.Append(" hero=").Append(Heroes.ToString(ci));
        sb.Append(" wounded=").Append(Wounded.ToString(ci));
        sb.Append(" t12=").Append(Tier1To2.ToString(ci));
        sb.Append(" t34=").Append(Tier3To4.ToString(ci));
        sb.Append(" t5+=").Append(Tier5Plus.ToString(ci));
        return sb.ToString();
    }
}

/// <summary>
/// 无状态的兵种构成评估器。所有方法均为 static，可在任意线程调用，但 vanilla TroopRoster 本身非线程安全，
/// 调用方应保证只在主线程或 roster 持有者允许的上下文中调用。
/// </summary>
public static class TroopCompositionEvaluator
{
    /// <summary>
    /// 按粗规则把单个角色分类。Hero / 高阶贵族统计优先；普通兵种委托给
    /// <see cref="GenericTroopMatcher.GetRole"/>，即按 Bannerlord 默认编队分类。
    /// </summary>
    public static TroopType GetTroopType(CharacterObject character)
    {
        if (character is null) return TroopType.Infantry;
        if (character.IsHero) return TroopType.Hero;
        if (TroopClassifier.IsNoble(character)) return TroopType.Noble;
        return GenericTroopMatcher.GetRole(character) switch
        {
            GenericTroopRole.HorseArcher => TroopType.HorseArcher,
            GenericTroopRole.Cavalry => TroopType.Cavalry,
            GenericTroopRole.Ranged => TroopType.Ranged,
            _ => TroopType.Infantry,
        };
    }

    /// <summary>
    /// 对一份 roster 做兵种统计。<paramref name="roster"/> 为 null 时返回全零 snapshot。
    /// 总数与伤兵数采用 vanilla 的 <see cref="TroopRoster.TotalManCount"/> / <see cref="TroopRoster.TotalWounded"/>，
    /// 与逐元素累加保持一致。
    /// </summary>
    public static CompositionSnapshot Snapshot(TroopRoster? roster)
    {
        if (roster is null) return default;

        int heroes = 0, nobles = 0, horseArchers = 0, cavalry = 0;
        int ranged = 0, infantry = 0;
        int tier12 = 0, tier34 = 0, tier5p = 0;

        var elements = roster.GetTroopRoster();
        if (elements != null)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var ch = element.Character;
                if (ch is null) continue;

                int n = element.Number;
                if (n <= 0) continue;

                if (!ch.IsHero && TroopClassifier.IsNoble(ch))
                    nobles += n;

                switch (GenericTroopMatcher.GetRole(ch))
                {
                    case GenericTroopRole.HorseArcher: horseArchers += n; break;
                    case GenericTroopRole.Cavalry: cavalry += n; break;
                    case GenericTroopRole.Ranged: ranged += n; break;
                    case GenericTroopRole.Infantry: infantry += n; break;
                    default:
                        if (ch.IsHero) heroes += n;
                        break;
                }

                int tier = ch.Tier;
                if (tier >= 5)      tier5p += n;
                else if (tier >= 3) tier34 += n;
                else if (tier >= 1) tier12 += n;
            }
        }

        int total = roster.TotalManCount;
        int wounded = roster.TotalWounded;

        var snapshot = new CompositionSnapshot(
            total: total,
            heroes: heroes,
            nobles: nobles,
            horseArchers: horseArchers,
            cavalry: cavalry,
            ranged: ranged,
            infantry: infantry,
            wounded: wounded,
            tier1To2: tier12,
            tier3To4: tier34,
            tier5Plus: tier5p);

        Logger.Debug("[Composition] " + snapshot.ToOneLineSummary());
        return snapshot;
    }
}
