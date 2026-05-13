using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Templates;

/// <summary>
/// 模板/匹配模式的统一辅助。模板始终是 ImprovedGarrisons 风格的具体兵员模板
/// (CharacterObject.StringId -> 目标数量)；切换 <see cref="TownGarrisonRule.UseGenericMatching"/>
/// 只改变后台匹配逻辑（generic = 同 Role+Tier 任意阵营；exact = 必须能升级到具体目标兵），
/// 不会改写模板内容。
/// </summary>
public static class TroopTemplateModeService
{
    /// <summary>纯标志位切换；模板内容保持不变。</summary>
    public static void SetUseGenericMatching(TownGarrisonRule? rule, bool useGeneric)
    {
        if (rule is null) return;
        rule.UseGenericMatching = useGeneric;
    }

    /// <summary>把规则中的 <see cref="TownGarrisonRule.ExactTroopTemplate"/> 解析成有效的
    /// CharacterObject -> count 字典；缺失/无效条目被跳过。</summary>
    public static Dictionary<CharacterObject, int> ResolveExactTemplate(TownGarrisonRule? rule)
    {
        var result = new Dictionary<CharacterObject, int>();
        if (rule?.ExactTroopTemplate is null || rule.ExactTroopTemplate.Count == 0) return result;

        foreach (var kv in rule.ExactTroopTemplate)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0) continue;
            CharacterObject? troop = null;
            try { troop = MBObjectManager.Instance.GetObject<CharacterObject>(kv.Key); }
            catch { troop = null; }

            if (troop is null) continue;
            if (!IsValidTemplateTroop(troop, rule)) continue;
            result[troop] = result.TryGetValue(troop, out var existing) ? existing + kv.Value : kv.Value;
        }

        return result;
    }

    /// <summary>
    /// 过滤兵种是否可作为模板/匹配候选。除原有规则（hero / 非 soldier / 非 regular / militia 关键字 /
    /// 贵族开关）外，新增以下隐藏 / 不可招募过滤：
    /// <list type="bullet">
    ///   <item><c>HiddenInEncyclopedia</c>：DRM 等 mod 通过 XML 标记隐藏 vanilla 兵种的标准做法。</item>
    ///   <item><c>IsObsolete</c>：vanilla 标记的废弃兵种。</item>
    ///   <item><c>Culture.IsBandit</c>：bandit / minor faction 兵种（looters、forest_bandits、
    ///         sea_raiders 等），玩家在任何 town 都无法招募。与 ImprovedGarrisons
    ///         GarrisonRecruiterPartyManager 的过滤逻辑一致。</item>
    /// </list>
    /// </summary>
    public static bool IsValidTemplateTroop(CharacterObject? troop, TownGarrisonRule? rule)
    {
        if (troop is null) return false;
        try
        {
            if (troop.IsHero || !troop.IsSoldier || !troop.IsRegular) return false;
            if (troop.HiddenInEncyclopedia) return false;
            if (troop.IsObsolete) return false;
            var culture = troop.Culture;
            if (culture is null || culture.IsBandit) return false;
            if (!string.IsNullOrEmpty(troop.StringId)
                && troop.StringId.IndexOf("militia", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            if (rule != null && !rule.AllowNobleTroops && TroopClassifier.IsNoble(troop)) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
