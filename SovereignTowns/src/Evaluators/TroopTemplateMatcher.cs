using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Templates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;

namespace SovereignTowns.Evaluators;

/// <summary>
/// Single entry point for troop matching. Generic mode delegates to <see cref="GenericTroopMatcher"/>;
/// exact mode follows the ImprovedGarrisons template idea: a troop matches if it is, or can upgrade
/// into, one of the concrete template target troops.
/// </summary>
public static class TroopTemplateMatcher
{
    public static bool MatchesRule(CharacterObject? troop, TownGarrisonRule? rule)
    {
        if (rule is null) return false;

        if (rule.UseGenericMatching)
        {
            return MatchesGenericTemplate(troop, rule);
        }

        return MatchesExactTemplate(troop, rule);
    }

    public static float ScoreCandidate(
        CharacterObject? troop,
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int targetTotal)
    {
        if (rule is null) return float.NegativeInfinity;

        if (rule.UseGenericMatching)
        {
            return ScoreGenericTemplateCandidate(troop, rule, currentRoster);
        }

        return ScoreExactCandidate(troop, rule, currentRoster);
    }

    public static float ScoreUpgradeTarget(
        CharacterObject? source,
        CharacterObject? directTarget,
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int targetTotal)
    {
        if (rule is null) return float.NegativeInfinity;

        if (rule.UseGenericMatching)
        {
            return ScoreGenericTemplateUpgradeTarget(directTarget, rule, currentRoster);
        }

        return ScoreExactUpgradeTarget(source, directTarget, rule, currentRoster);
    }

    public static bool CanUpgradeToTarget(CharacterObject? source, CharacterObject? finalTarget)
    {
        if (source is null || finalTarget is null) return false;
        return CanUpgradeToTarget(source, finalTarget, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public static Dictionary<CharacterObject, int> GetExactTemplateDeficits(
        TownGarrisonRule? rule,
        TroopRoster? currentRoster)
    {
        var deficits = TroopTemplateModeService.ResolveExactTemplate(rule);
        if (deficits.Count == 0 || currentRoster is null) return deficits;

        var rosterElements = currentRoster.GetTroopRoster()
            .Where(e => e.Character != null && e.Number > 0)
            .OrderByDescending(e => e.Character.Tier)
            .ToList();

        foreach (var element in rosterElements)
        {
            var character = element.Character;
            int remaining = element.Number;
            if (character is null || remaining <= 0) continue;

            foreach (var target in deficits.Keys.OrderByDescending(t => t.Tier).ToList())
            {
                if (remaining <= 0) break;
                if (!CanUpgradeToTarget(character, target)) continue;

                int used = Math.Min(remaining, deficits[target]);
                remaining -= used;
                deficits[target] -= used;
                if (deficits[target] <= 0) deficits.Remove(target);
            }
        }

        return deficits;
    }

    private static bool MatchesExactTemplate(CharacterObject? troop, TownGarrisonRule rule)
    {
        if (!BaseEligible(troop, rule)) return false;

        foreach (var target in TroopTemplateModeService.ResolveExactTemplate(rule).Keys)
        {
            if (CanUpgradeToTarget(troop, target)) return true;
        }

        return false;
    }

    private static bool MatchesGenericTemplate(CharacterObject? troop, TownGarrisonRule rule)
    {
        if (!BaseEligible(troop, rule)) return false;

        var templateBuckets = GetGenericTemplateBuckets(rule);
        if (templateBuckets.Count == 0)
        {
            // Backward-compatible fallback for old configs before the player opens the template editor.
            return GenericTroopMatcher.MatchesRule(troop, rule);
        }

        foreach (var bucket in templateBuckets.Keys)
        {
            if (CanReachRoleTier(troop, bucket.Item1, bucket.Item2, bucket.Item3)) return true;
        }

        return false;
    }

    private static float ScoreExactCandidate(
        CharacterObject? troop,
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        if (!BaseEligible(troop, rule)) return float.NegativeInfinity;

        var deficits = GetExactTemplateDeficits(rule, currentRoster);
        if (deficits.Count == 0) return float.NegativeInfinity;

        float best = float.NegativeInfinity;
        foreach (var kv in deficits)
        {
            var target = kv.Key;
            if (!CanUpgradeToTarget(troop, target)) continue;

            float score = kv.Value * 10f;
            score += target.Tier * 2f;
            score += troop == target ? 20f : Math.Max(0, troop.Tier) * 0.25f;
            if (best < score) best = score;
        }

        return best;
    }

    private static float ScoreGenericTemplateCandidate(
        CharacterObject? troop,
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        if (!BaseEligible(troop, rule)) return float.NegativeInfinity;

        var deficits = GetGenericTemplateDeficits(rule, currentRoster);
        if (deficits.Count == 0)
        {
            return GenericTroopMatcher.ScoreCandidate(troop, rule, currentRoster, rule.TargetTotalCount);
        }

        float best = float.NegativeInfinity;
        foreach (var kv in deficits)
        {
            var role = kv.Key.Item1;
            int tier = kv.Key.Item2;
            bool noble = kv.Key.Item3;
            int gap = kv.Value;
            if (gap <= 0) continue;
            if (!CanReachRoleTier(troop, role, tier, noble)) continue;

            float score = gap * 10f + tier * 2f;
            if (GenericTroopMatcher.GetRole(troop) == role) score += 5f;
            if (GenericTroopMatcher.GetTierBucket(troop) == tier) score += 20f;
            best = Math.Max(best, score);
        }

        return best;
    }

    private static float ScoreExactUpgradeTarget(
        CharacterObject? source,
        CharacterObject? directTarget,
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        if (!BaseEligible(directTarget, rule)) return float.NegativeInfinity;

        var template = TroopTemplateModeService.ResolveExactTemplate(rule);
        if (template.Count == 0) return float.NegativeInfinity;

        float best = float.NegativeInfinity;
        foreach (var kv in template)
        {
            var finalTarget = kv.Key;
            int desired = kv.Value;
            if (desired <= 0) continue;
            if (!CanUpgradeToTarget(directTarget, finalTarget)) continue;

            int alreadyOnPathAfterDirectTarget = CountRosterOnPathAtOrAboveTier(
                currentRoster,
                finalTarget,
                GenericTroopMatcher.GetTierBucket(directTarget));
            if (alreadyOnPathAfterDirectTarget >= desired && directTarget != finalTarget)
            {
                continue;
            }

            int gap = Math.Max(0, desired - alreadyOnPathAfterDirectTarget);
            float score = gap * 10f + finalTarget.Tier * 2f + directTarget.Tier;
            if (directTarget == finalTarget) score += 30f;
            if (source != null && CanUpgradeToTarget(source, finalTarget)) score += 5f;
            if (best < score) best = score;
        }

        return best;
    }

    private static float ScoreGenericTemplateUpgradeTarget(
        CharacterObject? directTarget,
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        if (!BaseEligible(directTarget, rule)) return float.NegativeInfinity;

        var templateBuckets = GetGenericTemplateBuckets(rule);
        if (templateBuckets.Count == 0)
        {
            return GenericTroopMatcher.ScoreCandidate(directTarget, rule, currentRoster, rule.TargetTotalCount);
        }

        float best = float.NegativeInfinity;
        foreach (var kv in templateBuckets)
        {
            var role = kv.Key.Item1;
            int tier = kv.Key.Item2;
            bool noble = kv.Key.Item3;
            int desired = kv.Value;
            if (desired <= 0) continue;
            if (!CanReachRoleTier(directTarget, role, tier, noble)) continue;

            int alreadyOnPath = CountRosterCanReachRoleTierAtOrAboveTier(
                currentRoster,
                role,
                tier,
                noble,
                GenericTroopMatcher.GetTierBucket(directTarget));
            if (alreadyOnPath >= desired
                && !(GenericTroopMatcher.GetRole(directTarget) == role && GenericTroopMatcher.GetTierBucket(directTarget) == tier))
            {
                continue;
            }

            int gap = Math.Max(0, desired - alreadyOnPath);
            float score = gap * 10f + tier * 2f + directTarget.Tier;
            if (GenericTroopMatcher.GetRole(directTarget) == role) score += 5f;
            if (GenericTroopMatcher.GetTierBucket(directTarget) == tier) score += 30f;
            best = Math.Max(best, score);
        }

        return best;
    }

    private static Dictionary<Tuple<GenericTroopRole, int, bool>, int> GetGenericTemplateBuckets(TownGarrisonRule rule)
    {
        var result = new Dictionary<Tuple<GenericTroopRole, int, bool>, int>();
        foreach (var kv in TroopTemplateModeService.ResolveExactTemplate(rule))
        {
            var role = GenericTroopMatcher.GetRole(kv.Key);
            if (role == GenericTroopRole.Unknown) continue;
            int tier = GenericTroopMatcher.GetTierBucket(kv.Key);
            bool noble = TroopClassifier.IsNoble(kv.Key);
            var key = Tuple.Create(role, tier, noble);
            result[key] = result.TryGetValue(key, out var existing) ? existing + kv.Value : kv.Value;
        }
        return result;
    }

    private static Dictionary<Tuple<GenericTroopRole, int, bool>, int> GetGenericTemplateDeficits(
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        var deficits = GetGenericTemplateBuckets(rule);
        if (deficits.Count == 0 || currentRoster is null) return deficits;

        var rosterElements = currentRoster.GetTroopRoster()
            .Where(e => e.Character != null && e.Number > 0)
            .OrderByDescending(e => e.Character.Tier)
            .ToList();

        foreach (var element in rosterElements)
        {
            var character = element.Character;
            int remaining = element.Number;
            if (character is null || remaining <= 0) continue;

            foreach (var bucket in deficits.Keys.OrderByDescending(k => k.Item2).ToList())
            {
                if (remaining <= 0) break;
                if (!CanReachRoleTier(character, bucket.Item1, bucket.Item2, bucket.Item3)) continue;

                int used = Math.Min(remaining, deficits[bucket]);
                remaining -= used;
                deficits[bucket] -= used;
                if (deficits[bucket] <= 0) deficits.Remove(bucket);
            }
        }

        return deficits;
    }

    private static int CountRosterOnPathAtOrAboveTier(
        TroopRoster? roster,
        CharacterObject finalTarget,
        int minTier)
    {
        if (roster is null) return 0;

        int total = 0;
        foreach (var element in roster.GetTroopRoster())
        {
            var character = element.Character;
            if (character is null || element.Number <= 0) continue;
            if (GenericTroopMatcher.GetTierBucket(character) < minTier) continue;
            if (CanUpgradeToTarget(character, finalTarget)) total += element.Number;
        }
        return total;
    }

    private static int CountRosterCanReachRoleTierAtOrAboveTier(
        TroopRoster? roster,
        GenericTroopRole role,
        int tier,
        bool noble,
        int minTier)
    {
        if (roster is null) return 0;

        int total = 0;
        foreach (var element in roster.GetTroopRoster())
        {
            var character = element.Character;
            if (character is null || element.Number <= 0) continue;
            if (GenericTroopMatcher.GetTierBucket(character) < minTier) continue;
            if (CanReachRoleTier(character, role, tier, noble)) total += element.Number;
        }
        return total;
    }

    private static bool BaseEligible(CharacterObject? troop, TownGarrisonRule rule)
    {
        if (!TroopTemplateModeService.IsValidTemplateTroop(troop, rule)) return false;
        if (troop is null) return false;
        if (IsListed(troop, rule.BannedTroopIds)) return false;
        return true;
    }

    private static bool CanUpgradeToTarget(
        CharacterObject source,
        CharacterObject finalTarget,
        HashSet<string> visited)
    {
        if (source == finalTarget) return true;

        var sourceId = source.StringId ?? source.Name?.ToString() ?? "";
        if (sourceId.Length > 0 && !visited.Add(sourceId)) return false;

        try
        {
            var targets = source.UpgradeTargets;
            if (targets is null || targets.Length == 0) return false;

            for (int i = 0; i < targets.Length; i++)
            {
                var next = targets[i];
                if (next is null) continue;
                if (CanUpgradeToTarget(next, finalTarget, visited)) return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool CanReachRoleTier(
        CharacterObject? source,
        GenericTroopRole role,
        int targetTier,
        bool requireNoble)
    {
        if (source is null) return false;
        // 升级树同一 line 不会跨 noble/common —— 在入口判一次足以决定整条搜索。
        if (TroopClassifier.IsNoble(source) != requireNoble) return false;
        return CanReachRoleTier(source, role, targetTier, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool CanReachRoleTier(
        CharacterObject source,
        GenericTroopRole role,
        int targetTier,
        HashSet<string> visited)
    {
        int sourceTier = GenericTroopMatcher.GetTierBucket(source);
        if (sourceTier == targetTier && GenericTroopMatcher.GetRole(source) == role)
        {
            return true;
        }
        if (sourceTier >= targetTier) return false;

        var sourceId = source.StringId ?? source.Name?.ToString() ?? "";
        if (sourceId.Length > 0 && !visited.Add(sourceId)) return false;

        try
        {
            var targets = source.UpgradeTargets;
            if (targets is null || targets.Length == 0) return false;

            for (int i = 0; i < targets.Length; i++)
            {
                var next = targets[i];
                if (next is null) continue;
                if (CanReachRoleTier(next, role, targetTier, visited)) return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
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
}
