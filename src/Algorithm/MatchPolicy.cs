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

        if (rule.UseGenericMatching)
        {
            return GenericTroopMatcher.TargetCount(GenericTroopMatcher.RoleRatio(rule, role), desiredTotal);
        }

        var template = TroopTemplateModeService.ResolveExactTemplate(rule, desiredTotal);
        int total = 0;
        foreach (var kv in template)
        {
            if (GenericTroopMatcher.GetRole(kv.Key) == role)
                total += Math.Max(0, kv.Value);
        }
        return total;
    }

    public static bool AllowsRole(TownGarrisonRule rule, GenericTroopRole role)
    {
        if (rule == null || role == GenericTroopRole.Unknown) return false;
        if (rule.UseGenericMatching)
            return GenericTroopMatcher.RoleRatio(rule, role) > 0f;

        foreach (var target in TroopTemplateModeService.ResolveExactTemplateTargets(rule))
        {
            if (GenericTroopMatcher.GetRole(target) == role) return true;
        }
        return false;
    }

    public static int MatchPenalty(TroopBucket bucket, TownGarrisonRule rule, int hardPenalty, int tierPenalty)
    {
        hardPenalty = Math.Max(0, hardPenalty);
        tierPenalty = Math.Max(0, tierPenalty);

        if (rule == null || bucket.Count <= 0) return hardPenalty;
        if (!AllowsRole(rule, bucket.Role)) return hardPenalty;

        if (rule.UseGenericMatching)
        {
            int tierGap = Math.Max(0, rule.MinTier - bucket.MinTier);
            return tierPenalty * tierGap;
        }

        var representative = bucket.Representative;
        if (representative == null) return hardPenalty;

        int best = int.MaxValue;
        foreach (var target in TroopTemplateModeService.ResolveExactTemplateTargets(rule))
        {
            if (!TroopTemplateMatcher.CanUpgradeToTarget(representative, target)) continue;
            int tierGap = Math.Max(0, GenericTroopMatcher.GetTierBucket(target) - bucket.MinTier);
            best = Math.Min(best, tierPenalty * tierGap);
        }

        return best == int.MaxValue ? hardPenalty : best;
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
        if (bucket.MinTier < rule.MinTier) return true;
        if (rule.UseGenericMatching) return false;
        if (bucket.Representative == null) return true;
        return TroopTemplateModeService.ResolveExactTemplateTargets(rule)
            .All(target => !TroopTemplateMatcher.CanUpgradeToTarget(bucket.Representative, target));
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
