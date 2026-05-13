using System;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 兵种粗分类。判定全部基于 vanilla 运行时属性（IsHero/Tier/IsMounted/IsRanged/HasThrowingWeapon），
/// 不依赖任何 stringId 硬编码，对 RBM 等覆盖兵种 xml 的 mod 完全友好。
/// </summary>
public enum TroopType
{
    Hero = 0,
    Noble = 1,
    HorseArcher = 2,
    Cavalry = 3,
    Crossbow = 4,
    Archer = 5,
    Thrower = 6,
    Infantry = 7
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
        int archers,
        int crossbows,
        int throwers,
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
        Archers = archers;
        Crossbows = crossbows;
        Throwers = throwers;
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
    public int Archers { get; }
    public int Crossbows { get; }
    public int Throwers { get; }
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
            TroopType.Crossbow    => Crossbows,
            TroopType.Archer      => Archers,
            TroopType.Thrower     => Throwers,
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
        sb.Append(" arc=").Append(Archers.ToString(ci));
        sb.Append(" crb=").Append(Crossbows.ToString(ci));
        sb.Append(" thr=").Append(Throwers.ToString(ci));
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
    /// 按粗规则把单个角色分类。判定顺序：Hero -> Tier&gt;=5 Noble -> Mounted+Ranged -> Mounted ->
    /// Ranged+ThrowingWeapon -> Ranged+Crossbow -> Ranged(Bow) -> Infantry 兜底。
    ///
    /// 弓/弩区分基于 vanilla <c>Equipment.HasWeaponOfClass(WeaponClass.Crossbow)</c>（v1.3.15 验证）。
    /// </summary>
    public static TroopType GetTroopType(CharacterObject character)
    {
        if (character is null) return TroopType.Infantry;
        if (character.IsHero) return TroopType.Hero;
        if (character.Tier >= 5) return TroopType.Noble;
        if (character.IsMounted && character.IsRanged) return TroopType.HorseArcher;
        if (character.IsMounted) return TroopType.Cavalry;
        if (character.IsRanged)
        {
            if (character.HasThrowingWeapon()) return TroopType.Thrower;
            if (HasCrossbowWeapon(character)) return TroopType.Crossbow;
            return TroopType.Archer;
        }
        return TroopType.Infantry;
    }

    /// <summary>
    /// 判断该角色的默认战斗装备是否携带弩（区分弓兵 / 弩兵）。
    /// 任何异常都吞掉并返回 false，绝不让分类逻辑抛出。
    /// </summary>
    private static bool HasCrossbowWeapon(CharacterObject character)
    {
        try
        {
            var equipment = character.FirstBattleEquipment;
            if (equipment == null) return false;
            return equipment.HasWeaponOfClass(WeaponClass.Crossbow);
        }
        catch (Exception ex)
        {
            Logger.Debug($"HasCrossbowWeapon threw for '{character?.StringId}': {ex.Message}");
            return false;
        }
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
        int archers = 0, crossbows = 0, throwers = 0, infantry = 0;
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

                if (!ch.IsHero && ch.Tier >= 5)
                    nobles += n;

                switch (GenericTroopMatcher.GetRole(ch))
                {
                    case GenericTroopRole.Cavalry:
                        if (ch.IsRanged) horseArchers += n;
                        cavalry += n;
                        break;
                    case GenericTroopRole.Crossbow: crossbows += n; break;
                    case GenericTroopRole.Archer: archers += n; break;
                    case GenericTroopRole.Thrower: throwers += n; break;
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
            archers: archers,
            crossbows: crossbows,
            throwers: throwers,
            infantry: infantry,
            wounded: wounded,
            tier1To2: tier12,
            tier3To4: tier34,
            tier5Plus: tier5p);

        Logger.Debug("[Composition] " + snapshot.ToOneLineSummary());
        return snapshot;
    }
}
