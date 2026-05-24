using System.Collections.Generic;

namespace SovereignTowns.Configuration;

/// <summary>
/// 按 vanilla 阵营文化为 AI 城提供的 <see cref="TownGarrisonRule"/> 预设。
/// 仅在 <see cref="ConfigurationManager.GetRuleFor"/> 内、且
/// <see cref="EnabledFeatures.ApplyToAiSettlementsToo"/> = true、且 town 属于 AI clan 的路径上被查询。
/// 玩家城（OwnerClan == PlayerClan）永远走 GlobalDefaults 路径，不受本表影响。
///
/// <para>
/// ratio 设计基于 vanilla 1.3.15 各阵营 roster 招牌成分（社区共识）：
///   - Vlandia：重骑士 + 弩手 + voulgier 抗骑步兵，无 bow archer tree
///   - Sturgia：shieldwall 重步兵 + throwing axe，少量 druzhinnik 精锐骑
///   - Aserai：mameluke horse archer + faris javelin + 步弓 benchmark
///   - Khuzait：horse archer + lancer，roster 极度偏骑兵 / 骑射
///   - Battania：fian champion 弓手霸权 + spearman 护翼 + wildling javelin
///   - Empire：combined arms，legionary pilum 按默认编队归步兵
/// </para>
///
/// <para>
/// 与 <see cref="Evaluators.GenericTroopMatcher.GetRole"/> 分类规则配套：优先采用
/// CharacterObject.DefaultFormationClass。HorseArcher 独立成桶；投掷兵不再独立，
/// 会按游戏默认编队落入步兵 / 远程 / 骑兵 / 骑射。
/// </para>
///
/// 共享字段：UseGenericMatching=true、MinTier=1、MaxTier=6，并通过 AllowedCultureIds
/// 限定为 AI clan 自己的文化。即：通用匹配仍只按 role + tier 选兵，但候选兵必须同文化。
/// 其他字段沿用 <see cref="TownGarrisonRule.CreateDefault"/>（含 TargetTotalCount=150、AllowNobleTroops=true 等）。
/// </summary>
/// <remarks>
/// AI 氏族非首府不读这里 — 它们的 BranchRule.TargetPower 由
/// <see cref="SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeAiVanillaTargetPower"/>
/// 动态算（复用 vanilla FactionHelper 公式），LowTierMinFraction 沿用全局 BranchDefaults。
/// preset 中所有字段只对 AI 首府生效。
/// </remarks>
public static class AiCulturePresets
{
    public const string CultureVlandia = "vlandia";
    public const string CultureSturgia = "sturgia";
    public const string CultureAserai = "aserai";
    public const string CultureKhuzait = "khuzait";
    public const string CultureBattania = "battania";
    public const string CultureEmpire = "empire";

    private static readonly Dictionary<string, (float Cav, float HorseArcher, float Inf, float Ranged)> _ratios = new()
    {
        [CultureVlandia]  = (0.30f, 0.00f, 0.35f, 0.35f),
        [CultureSturgia]  = (0.10f, 0.00f, 0.75f, 0.15f),
        [CultureAserai]   = (0.25f, 0.25f, 0.20f, 0.30f),
        [CultureKhuzait]  = (0.25f, 0.45f, 0.15f, 0.15f),
        [CultureBattania] = (0.05f, 0.00f, 0.50f, 0.45f),
        [CultureEmpire]   = (0.15f, 0.05f, 0.55f, 0.25f),
    };

    /// <summary>
    /// 按 culture stringId 取 preset；未识别 culture（mod 新文化 / bandit / neutral 等）返回 null，
    /// 调用方继续 fall through 到 GlobalDefaults。
    /// 返回值是新实例（<see cref="TownGarrisonRule.Clone"/>），调用方修改不会污染下次查询。
    /// </summary>
    public static TownGarrisonRule? TryGet(string? cultureStringId)
    {
        if (string.IsNullOrEmpty(cultureStringId)) return null;
        if (!_ratios.TryGetValue(cultureStringId!, out var r)) return null;

        var rule = TownGarrisonRule.CreateDefault();
        // PR-5'(2026-05-24): UseGenericMatching/MinTier/MaxTier removed from TownGarrisonRule; generic matching is always on.
        rule.AllowedCultureIds = new List<string> { cultureStringId! };
        rule.CavalryRatio = r.Cav;
        rule.HorseArcherRatio = r.HorseArcher;
        rule.InfantryRatio = r.Inf;
        rule.RangedRatio = r.Ranged;
        return rule;
    }
}
