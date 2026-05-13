using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 兵种分类工具。当前仅暴露 <see cref="IsNoble"/>——基于升级树拓扑判定一个角色是否在
/// 其文化的 noble line（<c>culture.EliteBasicTroop</c>）升级链上。这与 ImprovedGarrisons
/// 的判定保持一致，正确区分同 Tier 的 common / noble line（例如 Imperial Cataphract 为
/// common，Imperial Elite Cataphract 为 noble，两者 Tier 都为 6）。
/// </summary>
/// <remarks>
/// 历史上本类还有 <c>Classify</c> 返回 <c>DetailedTroopType</c> 的精细分类、文化白名单
/// 工具 <c>IsCultureAllowed</c>，因长期无调用方在 2026-05-13 的清理中删除。Role 分类已由
/// <see cref="GenericTroopMatcher.GetRole"/> 承担。
/// </remarks>
public static class TroopClassifier
{
    /// <summary>
    /// 是否"贵族"兵：基于升级树拓扑判定——若该兵种位于其文化的 <c>EliteBasicTroop</c>
    /// 升级链上（含起点本身），则视为 noble。
    /// null / 无 culture / 无 EliteBasicTroop / 异常 → false。
    /// </summary>
    public static bool IsNoble(CharacterObject? character)
    {
        if (character is null) return false;
        try
        {
            if (character.IsHero) return false;
            var culture = character.Culture;
            if (culture is null) return false;
            var nobleRoot = culture.EliteBasicTroop;
            if (nobleRoot is null) return false;
            return NobleLineReaches(nobleRoot, character, new HashSet<string>(System.StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool NobleLineReaches(CharacterObject source, CharacterObject target, HashSet<string> visited)
    {
        if (source == target) return true;
        var id = source.StringId ?? "";
        if (id.Length > 0 && !visited.Add(id)) return false;
        try
        {
            var upgrades = source.UpgradeTargets;
            if (upgrades is null || upgrades.Length == 0) return false;
            for (int i = 0; i < upgrades.Length; i++)
            {
                var next = upgrades[i];
                if (next != null && NobleLineReaches(next, target, visited)) return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }
}
