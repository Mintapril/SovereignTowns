using System;
using System.Collections.Generic;
using SovereignTowns.Algorithm;
using SovereignTowns.Configuration;
using SovereignTowns.Templates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.ObjectSystem;

namespace SovereignTowns.Evaluators;

/// <summary>
/// Single entry point for troop matching. Uses role sliders plus the tier band;
/// a troop matches if it satisfies the generic role rules for the active rule.
/// </summary>
public static class TroopTemplateMatcher
{
    public static bool MatchesRule(CharacterObject? troop, TownGarrisonRule? rule)
    {
        if (rule is null) return false;
        // PR-5'(2026-05-24): UseGenericMatching removed; always use generic matching.
        return MatchesGenericTemplate(troop, rule);
    }

    public static float ScoreCandidate(
        CharacterObject? troop,
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int targetTotal)
    {
        if (rule is null) return float.NegativeInfinity;
        // PR-5'(2026-05-24): UseGenericMatching removed; always use generic matching.
        return ScoreGenericTemplateCandidate(troop, rule, currentRoster);
    }

    public static float ScoreUpgradeTarget(
        CharacterObject? source,
        CharacterObject? directTarget,
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int targetTotal)
    {
        if (rule is null) return float.NegativeInfinity;
        // PR-5'(2026-05-24): UseGenericMatching removed; always use generic matching.
        return ScoreGenericTemplateUpgradeTarget(directTarget, rule, currentRoster);
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

    /// <summary>
    /// Role a recruit should fill under the active rule. Uses the troop's
    /// current battlefield role via GenericTroopMatcher.
    /// </summary>
    public static GenericTroopRole GetServiceRole(CharacterObject? troop, TownGarrisonRule? rule)
    {
        if (troop is null) return GenericTroopRole.Unknown;
        // PR-5'(2026-05-24): UseGenericMatching removed; always use generic matching.
        return GenericTroopMatcher.GetRole(troop);
    }

    public static bool CanServeRole(CharacterObject? troop, TownGarrisonRule? rule, GenericTroopRole role)
    {
        if (role == GenericTroopRole.Unknown) return false;
        return GetServiceRole(troop, rule) == role;
    }

    /// <summary>
    /// 招募志愿者单一匹配入口。两模式共用：
    /// <list type="bullet">
    ///   <item><b>GarrisonRole</b>：rule + assignedRole(可选) + 玩家面板文化过滤。匹配逻辑同既有
    ///         <see cref="MatchesRule"/> + <see cref="CanServeRole"/>。</item>
    ///   <item><b>HonorGuardPrecise</b>：志愿者 troopId 是模板中的（仍有 deficit），或可升级到模板某 troopId
    ///         （IG 算法：递归走 <see cref="CharacterObject.UpgradeTargets"/>，目标命中 = 接受）。</item>
    /// </list>
    /// 调用方需传入"剩余 deficit"——本函数不修改其内容，仅按 deficit 大于 0 决定是否接受。
    /// </summary>
    public static bool IsAcceptableVolunteer(
        CharacterObject? troop,
        TownGarrisonRule? rule,
        GenericTroopRole assignedRole,
        RecruiterMode mode,
        IReadOnlyDictionary<string, int>? preciseTemplateDeficit)
    {
        if (troop is null) return false;
        if (rule != null && !BaseEligible(troop, rule)) return false;

        switch (mode)
        {
            case RecruiterMode.GarrisonRole:
                if (rule != null && !MatchesGenericTemplate(troop, rule)) return false;
                if (assignedRole != GenericTroopRole.Unknown
                    && !CanServeRole(troop, rule, assignedRole)) return false;
                return true;

            case RecruiterMode.HonorGuardPrecise:
                if (preciseTemplateDeficit == null || preciseTemplateDeficit.Count == 0) return false;
                foreach (var kv in preciseTemplateDeficit)
                {
                    if (kv.Value <= 0) continue;
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    var target = MBObjectManager.Instance?.GetObject<CharacterObject>(kv.Key);
                    if (target == null) continue;
                    if (CanUpgradeToTarget(troop, target)) return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// HonorGuardPrecise 模式专用：志愿者命中模板的具体 troopId（升级链终点）。
    /// 用于 deficit 递减——按模板插入顺序遍历，首个命中的 troopId 即被消耗。
    /// 调用方应在调用前自行确认 <see cref="IsAcceptableVolunteer"/> 已返回 true。
    /// </summary>
    public static string? PickPreciseTemplateMatch(
        CharacterObject? troop,
        IReadOnlyDictionary<string, int>? preciseTemplateDeficit)
    {
        if (troop is null || preciseTemplateDeficit == null) return null;
        foreach (var kv in preciseTemplateDeficit)
        {
            if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
            var target = MBObjectManager.Instance?.GetObject<CharacterObject>(kv.Key);
            if (target == null) continue;
            if (CanUpgradeToTarget(troop, target)) return kv.Key;
        }
        return null;
    }

    private static bool MatchesGenericTemplate(CharacterObject? troop, TownGarrisonRule rule)
    {
        if (!BaseEligible(troop, rule)) return false;
        return GenericTroopMatcher.MatchesRule(troop, rule);
    }

    private static float ScoreGenericTemplateCandidate(
        CharacterObject? troop,
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        if (!BaseEligible(troop, rule)) return float.NegativeInfinity;
        // 传 0 → ScoreCandidate 用中性 fallback（snapshot.Total+1），目标实际由 MCMF 决定。
        return GenericTroopMatcher.ScoreCandidate(troop, rule, currentRoster, 0);
    }

    private static float ScoreGenericTemplateUpgradeTarget(
        CharacterObject? directTarget,
        TownGarrisonRule rule,
        TroopRoster? currentRoster)
    {
        if (!BaseEligible(directTarget, rule)) return float.NegativeInfinity;
        return GenericTroopMatcher.ScoreCandidate(directTarget, rule, currentRoster, 0);
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
