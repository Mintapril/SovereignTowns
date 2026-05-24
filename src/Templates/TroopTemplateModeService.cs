using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Templates;

/// <summary>
/// 模板/匹配模式的统一辅助。PR-5'(2026-05-24) 后仅剩 Generic 模式（UseGenericMatching/ExactTroopTemplate 已删除）。
/// 旧 Exact 路径方法保留签名但均返回空集合，避免删除破坏仍存在的调用点编译。
/// </summary>
public static class TroopTemplateModeService
{
    /// <summary>PR-5'(2026-05-24): UseGenericMatching removed — no-op. Generic matching is always on.</summary>
    public static void SetUseGenericMatching(TownGarrisonRule? rule, bool useGeneric)
    {
        // no-op: field removed from TownGarrisonRule
    }

    /// <summary>
    /// PR-5'(2026-05-24): ExactTroopTemplate removed from TownGarrisonRule. Always returns empty dict.
    /// Kept for call-site compilation compatibility.
    /// </summary>
    public static Dictionary<CharacterObject, int> ResolveExactTemplate(TownGarrisonRule? rule, int effectiveTarget)
    {
        return new Dictionary<CharacterObject, int>();
    }

    /// <summary>
    /// PR-5'(2026-05-24): ExactTroopTemplate removed from TownGarrisonRule. Always returns empty set.
    /// Kept for call-site compilation compatibility.
    /// </summary>
    public static HashSet<CharacterObject> ResolveExactTemplateTargets(TownGarrisonRule? rule)
    {
        return new HashSet<CharacterObject>();
    }

    /// <summary>
    /// 过滤兵种是否可作为模板/匹配候选。除原有规则（hero / 非 soldier / 非 regular / 民兵兵种 /
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
            // RBM 安全：用文化的民兵兵种槽做对象身份判定，不依赖 stringId 子串匹配
            // （RBM 等 mod 会改 stringId，子串匹配会漏判）。culture 在上方已非 null。
            if (troop == culture.MeleeMilitiaTroop
                || troop == culture.RangedMilitiaTroop
                || troop == culture.MeleeEliteMilitiaTroop
                || troop == culture.RangedEliteMilitiaTroop)
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
