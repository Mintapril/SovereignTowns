using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Templates;

/// <summary>
/// 模板/匹配模式的统一辅助。Exact 模式使用按 stringId 精确指定的兵员模板
/// (CharacterObject.StringId -> 目标数量)；Generic 模式不读取具体模板，只读取兵种滑条和 Tier 范围。
/// 切换 <see cref="TownGarrisonRule.UseGenericMatching"/> 不会改写模板内容。
/// </summary>
public static class TroopTemplateModeService
{
    /// <summary>纯标志位切换；模板内容保持不变。</summary>
    public static void SetUseGenericMatching(TownGarrisonRule? rule, bool useGeneric)
    {
        if (rule is null) return;
        rule.UseGenericMatching = useGeneric;
    }

    /// <summary>
    /// B7.20：把规则中的 <see cref="TownGarrisonRule.ExactTroopTemplate"/>（占比 0..1）
    /// 解析为 CharacterObject -&gt; 实际目标人数 字典：<c>count = round(ratio × effectiveTarget)</c>。
    /// 缺失 / 无效 / count=0 的条目跳过。
    /// </summary>
    public static Dictionary<CharacterObject, int> ResolveExactTemplate(TownGarrisonRule? rule, int effectiveTarget)
    {
        var result = new Dictionary<CharacterObject, int>();
        if (rule?.ExactTroopTemplate is null || rule.ExactTroopTemplate.Count == 0) return result;
        if (effectiveTarget <= 0) return result;

        foreach (var kv in rule.ExactTroopTemplate)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0f) continue;
            CharacterObject? troop = null;
            try { troop = MBObjectManager.Instance.GetObject<CharacterObject>(kv.Key); }
            catch { troop = null; }

            if (troop is null) continue;
            if (!IsValidTemplateTroop(troop, rule)) continue;
            int count = (int)System.Math.Round(kv.Value * effectiveTarget);
            if (count <= 0) continue;
            result[troop] = result.TryGetValue(troop, out var existing) ? existing + count : count;
        }

        return result;
    }

    /// <summary>
    /// 仅返回模板中有效的 CharacterObject 集合（不计算具体人数）。
    /// 用于 MatchesExactTemplate 之类的"只需 keys 是否包含"的判定路径，避开 effectiveTarget 依赖。
    /// </summary>
    public static HashSet<CharacterObject> ResolveExactTemplateTargets(TownGarrisonRule? rule)
    {
        var result = new HashSet<CharacterObject>();
        if (rule?.ExactTroopTemplate is null || rule.ExactTroopTemplate.Count == 0) return result;
        foreach (var kv in rule.ExactTroopTemplate)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0f) continue;
            CharacterObject? troop = null;
            try { troop = MBObjectManager.Instance.GetObject<CharacterObject>(kv.Key); }
            catch { troop = null; }
            if (troop is null) continue;
            if (!IsValidTemplateTroop(troop, rule)) continue;
            result.Add(troop);
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
    ///         sea_raiders 等），玩家在任何 town 都无法招募。</item>
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
            if (rule?.AllowedCultureIds != null && rule.AllowedCultureIds.Count > 0)
            {
                var cultureId = culture.StringId ?? "";
                var allowed = false;
                foreach (var id in rule.AllowedCultureIds)
                {
                    if (string.Equals(id, cultureId, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed) return false;
            }
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
