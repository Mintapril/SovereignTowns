using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Templates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;

namespace SovereignTowns.Evaluators;

/// <summary>
/// Single entry point for troop matching. Generic mode uses only role sliders plus the tier band;
/// exact mode uses the per-stringId template: a troop matches if it is, or can upgrade into,
/// one of the concrete template target troops.
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

        return ScoreExactCandidate(troop, rule, currentRoster, targetTotal);
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

        return ScoreExactUpgradeTarget(source, directTarget, rule, currentRoster, targetTotal);
    }

    public static bool CanUpgradeToTarget(CharacterObject? source, CharacterObject? finalTarget)
    {
        if (source is null || finalTarget is null) return false;
        var key = (source, finalTarget);
        if (EvaluatorCache.CanUpgradeCache.TryGetValue(key, out var cached)) return cached;
        // 递归走 Core(...) 不二次进入缓存，确保 visited 防环逻辑只在一次顶层调用上下文中生效。
        var result = CanUpgradeToTargetCore(source, finalTarget, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        EvaluatorCache.CanUpgradeCache[key] = result;
        return result;
    }

    public static Dictionary<CharacterObject, int> GetExactTemplateDeficits(
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int effectiveTarget)
    {
        var deficits = TroopTemplateModeService.ResolveExactTemplate(rule, effectiveTarget);
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

        // 仅检查模板里有哪些 target stringId（不需要 effectiveTarget 计算具体人数）。
        foreach (var target in TroopTemplateModeService.ResolveExactTemplateTargets(rule))
        {
            if (CanUpgradeToTarget(troop, target)) return true;
        }

        return false;
    }

    private static bool MatchesGenericTemplate(CharacterObject? troop, TownGarrisonRule rule)
    {
        if (!BaseEligible(troop, rule)) return false;
        return GenericTroopMatcher.MatchesRule(troop, rule);
    }

    private static float ScoreExactCandidate(
        CharacterObject? troop,
        TownGarrisonRule rule,
        TroopRoster? currentRoster,
        int effectiveTarget)
    {
        if (!BaseEligible(troop, rule) || troop is null) return float.NegativeInfinity;
        if (effectiveTarget <= 0) effectiveTarget = Math.Max(1, rule.TargetTotalCount);

        var deficits = GetExactTemplateDeficits(rule, currentRoster, effectiveTarget);
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
        return GenericTroopMatcher.ScoreCandidate(troop, rule, currentRoster, rule.TargetTotalCount);
    }

    private static float ScoreExactUpgradeTarget(
        CharacterObject? source,
        CharacterObject? directTarget,
        TownGarrisonRule rule,
        TroopRoster? currentRoster,
        int effectiveTarget)
    {
        if (!BaseEligible(directTarget, rule) || directTarget is null) return float.NegativeInfinity;
        if (effectiveTarget <= 0) effectiveTarget = Math.Max(1, rule.TargetTotalCount);

        var template = TroopTemplateModeService.ResolveExactTemplate(rule, effectiveTarget);
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
        return GenericTroopMatcher.ScoreCandidate(directTarget, rule, currentRoster, rule.TargetTotalCount);
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

    private static bool BaseEligible(CharacterObject? troop, TownGarrisonRule rule)
    {
        if (!TroopTemplateModeService.IsValidTemplateTroop(troop, rule)) return false;
        if (troop is null) return false;
        if (IsListed(troop, rule.BannedTroopIds)) return false;
        return true;
    }

    private static bool CanUpgradeToTargetCore(
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
                // 递归内部继续走 Core(...)，保留同一 visited HashSet 防环。
                if (CanUpgradeToTargetCore(next, finalTarget, visited)) return true;
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
