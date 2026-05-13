using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace SovereignTowns.Evaluators;

/// <summary>
/// Culture-agnostic troop template matcher. It intentionally ignores faction/culture and
/// compares a unit only by broad battlefield role plus tier, matching the user-facing
/// "通用匹配" controls.
/// </summary>
public enum GenericTroopRole
{
    Unknown = 0,
    Cavalry = 1,
    Infantry = 2,
    Archer = 3,
    Crossbow = 4,
    Thrower = 5
}

public readonly struct GenericCompositionSnapshot
{
    public GenericCompositionSnapshot(
        int total,
        int cavalry,
        int infantry,
        int archer,
        int crossbow,
        int thrower,
        int tier1,
        int tier2,
        int tier3,
        int tier4,
        int tier5,
        int tier6)
    {
        Total = total;
        Cavalry = cavalry;
        Infantry = infantry;
        Archer = archer;
        Crossbow = crossbow;
        Thrower = thrower;
        Tier1 = tier1;
        Tier2 = tier2;
        Tier3 = tier3;
        Tier4 = tier4;
        Tier5 = tier5;
        Tier6 = tier6;
    }

    public int Total { get; }
    public int Cavalry { get; }
    public int Infantry { get; }
    public int Archer { get; }
    public int Crossbow { get; }
    public int Thrower { get; }
    public int Tier1 { get; }
    public int Tier2 { get; }
    public int Tier3 { get; }
    public int Tier4 { get; }
    public int Tier5 { get; }
    public int Tier6 { get; }

    public int CountOf(GenericTroopRole role) => role switch
    {
        GenericTroopRole.Cavalry => Cavalry,
        GenericTroopRole.Infantry => Infantry,
        GenericTroopRole.Archer => Archer,
        GenericTroopRole.Crossbow => Crossbow,
        GenericTroopRole.Thrower => Thrower,
        _ => 0
    };

    public int TierCount(int tier) => tier switch
    {
        1 => Tier1,
        2 => Tier2,
        3 => Tier3,
        4 => Tier4,
        5 => Tier5,
        _ => Tier6
    };
}

public static class GenericTroopMatcher
{
    public static GenericTroopRole GetRole(CharacterObject? troop)
    {
        if (troop == null) return GenericTroopRole.Unknown;

        try
        {
            if (troop.IsHero) return GenericTroopRole.Unknown;

            // Horse archers intentionally land in the cavalry bucket for the generic template.
            if (troop.IsMounted) return GenericTroopRole.Cavalry;

            if (SafeHasThrowingWeapon(troop) || HasWeaponClass(troop, WeaponClass.Javelin)
                || HasWeaponClass(troop, WeaponClass.ThrowingAxe)
                || HasWeaponClass(troop, WeaponClass.ThrowingKnife)
                || troop.DefaultFormationClass == FormationClass.Skirmisher)
            {
                return GenericTroopRole.Thrower;
            }

            if (troop.IsRanged)
            {
                return HasWeaponClass(troop, WeaponClass.Crossbow)
                    ? GenericTroopRole.Crossbow
                    : GenericTroopRole.Archer;
            }
        }
        catch
        {
            return GenericTroopRole.Infantry;
        }

        return GenericTroopRole.Infantry;
    }

    public static int GetTierBucket(CharacterObject? troop)
    {
        if (troop == null) return 1;
        try
        {
            if (troop.Tier <= 1) return 1;
            if (troop.Tier >= 6) return 6;
            return troop.Tier;
        }
        catch
        {
            return 1;
        }
    }

    public static bool MatchesRule(CharacterObject? troop, TownGarrisonRule? rule)
    {
        if (troop == null || rule == null) return false;

        try
        {
            if (troop.IsHero) return false;
            if (!troop.IsRegular) return false;
            if (!rule.AllowNobleTroops && TroopClassifier.IsNoble(troop)) return false;
            if (IsListed(troop, rule.BannedTroopIds)) return false;

            int tier = GetTierBucket(troop);
            if (tier < rule.MinTier || tier > rule.MaxTier) return false;
            // B7.10: TierRatio bucket weighting removed — MinTier/MaxTier band is the only tier filter.

            var role = GetRole(troop);
            if (role == GenericTroopRole.Unknown) return false;
            return RoleRatio(rule, role) > 0f;
        }
        catch
        {
            return false;
        }
    }

    public static float ScoreCandidate(
        CharacterObject? troop,
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int targetTotal)
    {
        if (!MatchesRule(troop, rule) || troop == null || rule == null) return float.NegativeInfinity;

        var snapshot = Snapshot(currentRoster);
        if (targetTotal <= 0) targetTotal = Math.Max(rule.TargetTotalCount, snapshot.Total + 1);

        var role = GetRole(troop);

        // B7.10: scoring now uses role gap only. Tier weighting was removed; MinTier/MaxTier
        // already constrained the candidate pool in MatchesRule.
        int roleTarget = (int)Math.Round(RoleRatio(rule, role) * targetTotal);
        int roleDeficit = roleTarget - snapshot.CountOf(role);

        float score = 0f;
        if (roleDeficit > 0) score += roleDeficit * 2f;
        score += RoleRatio(rule, role);
        if (IsListed(troop, rule.PriorityTroopIds)) score += 100f;
        return score;
    }

    public static GenericCompositionSnapshot Snapshot(TroopRoster? roster)
    {
        if (roster == null) return default;

        int total = 0;
        int cav = 0, inf = 0, arc = 0, crb = 0, thr = 0;
        int t1 = 0, t2 = 0, t3 = 0, t4 = 0, t5 = 0, t6 = 0;

        foreach (var elem in roster.GetTroopRoster())
        {
            var ch = elem.Character;
            if (ch == null || ch.IsHero || elem.Number <= 0) continue;

            total += elem.Number;
            switch (GetRole(ch))
            {
                case GenericTroopRole.Cavalry: cav += elem.Number; break;
                case GenericTroopRole.Infantry: inf += elem.Number; break;
                case GenericTroopRole.Archer: arc += elem.Number; break;
                case GenericTroopRole.Crossbow: crb += elem.Number; break;
                case GenericTroopRole.Thrower: thr += elem.Number; break;
            }

            switch (GetTierBucket(ch))
            {
                case 1: t1 += elem.Number; break;
                case 2: t2 += elem.Number; break;
                case 3: t3 += elem.Number; break;
                case 4: t4 += elem.Number; break;
                case 5: t5 += elem.Number; break;
                default: t6 += elem.Number; break;
            }
        }

        return new GenericCompositionSnapshot(total, cav, inf, arc, crb, thr, t1, t2, t3, t4, t5, t6);
    }

    public static float RoleRatio(TownGarrisonRule rule, GenericTroopRole role) => role switch
    {
        GenericTroopRole.Cavalry => rule.CavalryRatio,
        GenericTroopRole.Infantry => rule.InfantryRatio,
        GenericTroopRole.Archer => rule.ArcherRatio,
        GenericTroopRole.Crossbow => rule.CrossbowRatio,
        GenericTroopRole.Thrower => rule.ThrowerRatio,
        _ => 0f
    };

    public static int TargetCount(float ratio, int targetTotal)
    {
        if (ratio <= 0f || targetTotal <= 0) return 0;
        return (int)Math.Round(ratio * targetTotal);
    }

    private static bool IsListed(CharacterObject troop, IReadOnlyCollection<string>? ids)
    {
        if (ids == null || ids.Count == 0) return false;
        var stringId = troop.StringId;
        if (string.IsNullOrEmpty(stringId)) return false;
        foreach (var id in ids)
        {
            if (string.Equals(id, stringId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool SafeHasThrowingWeapon(CharacterObject troop)
    {
        try { return troop.HasThrowingWeapon(); }
        catch { return false; }
    }

    private static bool HasWeaponClass(CharacterObject troop, WeaponClass weaponClass)
    {
        try
        {
            var equipment = troop.FirstBattleEquipment;
            return equipment != null && equipment.HasWeaponOfClass(weaponClass);
        }
        catch
        {
            return false;
        }
    }
}
