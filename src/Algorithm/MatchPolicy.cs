using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Templates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;

namespace SovereignTowns.Algorithm;

public readonly struct TroopBucket
{
    public TroopBucket(GenericTroopRole role, int count, int minTier, CharacterObject? representative)
    {
        Role = role;
        Count = count < 0 ? 0 : count;
        MinTier = minTier <= 0 ? 1 : minTier;
        Representative = representative;
    }

    public GenericTroopRole Role { get; }
    public int Count { get; }
    public int MinTier { get; }
    public CharacterObject? Representative { get; }
}

public static class MatchPolicy
{
    public static readonly GenericTroopRole[] Roles =
    {
        GenericTroopRole.Cavalry,
        GenericTroopRole.HorseArcher,
        GenericTroopRole.Infantry,
        GenericTroopRole.Ranged
    };

    public static List<TroopBucket> Bucketize(TroopRoster? roster)
    {
        var byRole = new Dictionary<GenericTroopRole, (int Count, int MinTier, CharacterObject? Representative)>();
        if (roster == null) return new List<TroopBucket>();

        foreach (var element in roster.GetTroopRoster())
        {
            var character = element.Character;
            if (character == null || character.IsHero || element.Number <= 0) continue;

            var role = GenericTroopMatcher.GetRole(character);
            if (role == GenericTroopRole.Unknown) continue;

            int tier = GenericTroopMatcher.GetTierBucket(character);
            if (!byRole.TryGetValue(role, out var current))
            {
                byRole[role] = (element.Number, tier, character);
                continue;
            }

            var representative = current.Representative;
            int minTier = current.MinTier;
            if (tier < minTier)
            {
                minTier = tier;
                representative = character;
            }

            byRole[role] = (current.Count + element.Number, minTier, representative);
        }

        var result = new List<TroopBucket>();
        foreach (var kv in byRole)
        {
            result.Add(new TroopBucket(kv.Key, kv.Value.Count, kv.Value.MinTier, kv.Value.Representative));
        }
        return result;
    }

    public static int DesiredCount(TownGarrisonRule rule, GenericTroopRole role, int desiredTotal)
    {
        if (rule == null || desiredTotal <= 0) return 0;
        // PR-5'(2026-05-24): UseGenericMatching removed; always use generic matching.
        return GenericTroopMatcher.TargetCount(GenericTroopMatcher.RoleRatio(rule, role), desiredTotal);
    }

    public static bool AllowsRole(TownGarrisonRule rule, GenericTroopRole role)
    {
        if (rule == null || role == GenericTroopRole.Unknown) return false;
        // PR-5'(2026-05-24): UseGenericMatching removed; always use generic matching.
        return GenericTroopMatcher.RoleRatio(rule, role) > 0f;
    }

    public static int MatchPenalty(TroopBucket bucket, TownGarrisonRule rule, int hardPenalty, int tierPenalty)
    {
        hardPenalty = Math.Max(0, hardPenalty);
        tierPenalty = Math.Max(0, tierPenalty);

        if (rule == null || bucket.Count <= 0) return hardPenalty;
        if (!AllowsRole(rule, bucket.Role)) return hardPenalty;
        // PR-5'(2026-05-24): UseGenericMatching/MinTier removed; no tier-gap penalty from min-tier.
        return 0;
    }

    public static int EdgeCost(float distance, int overhead, int matchPenalty, float deficitRatio, float leniency)
    {
        float clampedDeficit = Clamp(deficitRatio, 0f, 1f);
        float clampedLeniency = Clamp(leniency, 0f, 1f);
        float penaltyScale = Clamp(1f - clampedDeficit * clampedLeniency, 0f, 1f);
        int distanceCost = distance <= 0f ? 0 : (int)Math.Round(distance);
        int scaledPenalty = (int)Math.Round(Math.Max(0, matchPenalty) * penaltyScale);
        return Math.Max(0, distanceCost) + Math.Max(0, overhead) + scaledPenalty;
    }

    public static bool IsLowQualityForRule(TroopBucket bucket, TownGarrisonRule rule)
    {
        if (rule == null || bucket.Count <= 0) return false;
        // PR-5'(2026-05-24): MinTier/UseGenericMatching/ExactTroopTemplate removed; no low-quality check.
        return false;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
