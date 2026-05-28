using System.Collections.Generic;

namespace SovereignTowns.Configuration;

/// <summary>
/// 按 vanilla 阵营文化为 AI 城提供的 <see cref="TownGarrisonRule"/> 预设。
/// 仅在 <see cref="ConfigurationManager.GetRuleFor"/> 内、且
/// <see cref="EnabledFeatures.ApplyToAiSettlementsToo"/> = true、且 town 属于 AI clan 的路径上被查询。
/// 玩家城（OwnerClan == PlayerClan）永远走 GlobalDefaults 路径，不受本表影响。
///
/// <para>
/// 2026-05-29 重构：role-ratio 全砍。AI 预设统一仅设
/// <see cref="TownGarrisonRule.AllowedCultureIds"/>，限招本文化兵种。其他字段沿用
/// <see cref="TownGarrisonRule.CreateDefault"/>（AllowNobleTroops=true 等）。
/// </para>
/// </summary>
public static class AiCulturePresets
{
    public const string CultureVlandia  = "vlandia";
    public const string CultureSturgia  = "sturgia";
    public const string CultureAserai   = "aserai";
    public const string CultureKhuzait  = "khuzait";
    public const string CultureBattania = "battania";
    public const string CultureEmpire   = "empire";

    private static readonly HashSet<string> _knownCultures = new()
    {
        CultureVlandia, CultureSturgia, CultureAserai, CultureKhuzait, CultureBattania, CultureEmpire,
    };

    /// <summary>
    /// 已知 culture → rule with AllowedCultureIds=[culture]；未知返 null（fall through 到 GlobalDefaults）。
    /// 返回值是新实例（<see cref="TownGarrisonRule.Clone"/>），调用方修改不会污染下次查询。
    /// </summary>
    public static TownGarrisonRule? TryGet(string? cultureStringId)
    {
        if (string.IsNullOrEmpty(cultureStringId)) return null;
        if (!_knownCultures.Contains(cultureStringId!)) return null;
        var rule = TownGarrisonRule.CreateDefault();
        rule.AllowedCultureIds = new List<string> { cultureStringId! };
        return rule;
    }
}
