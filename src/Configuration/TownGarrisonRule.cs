using System.Collections.Generic;

namespace SovereignTowns.Configuration;

/// <summary>
/// 单个 Town 驻军规则的 POCO。可被 GlobalConfig.GlobalDefaults 引用（首府规则）。所有字段必须可由
/// System.Text.Json 直接序列化/反序列化（公开属性 + 公开 setter）。
/// </summary>
public sealed class TownGarrisonRule
{
    // 2026-05-28: TargetTotalCount 已删除。驻军目标人数完全由 MCMF 决定
    // (UnifiedGarrisonSolver: perCityCapacity 公式,基于 GarrisonWageBudgetRatio × 收入 / 威胁 / 战略加权分配)，
    // 玩家无需手设数字。
    // 老存档 JSON 中的 TargetTotalCount key 会被 Newtonsoft 自动忽略并在下次保存时 drop。

    // 2026-05-29: CavalryRatio / HorseArcherRatio / InfantryRatio / RangedRatio 已删除。
    // 驻军组成不再按 role 划分目标 —— solver 只追总头数，role 分布由 vanilla 招募 / 升级链决定。
    // 老存档 JSON 中残留这 4 个 key 会被 Newtonsoft 自动忽略并在下次保存时 drop。
    // 仅保留三种文化限制（GenericCultureFilter）+ AllowedCultureIds + 优先/禁用列表 + AllowNoble/PrisonerConversion/AutoUpgrade。

    /// <summary>
    /// 通用文化过滤策略（通用匹配始终开启）。仅作用于<b>玩家氏族</b>首府的招募 —— AI 氏族沿用
    /// <see cref="AllowedCultureIds"/>（由 AiCulturePresets 写入），不受此字段影响。取值：
    /// <list type="bullet">
    ///   <item><c>"PlayerCulture"</c>（默认）：只招玩家氏族文化的兵种。</item>
    ///   <item><c>"CapitalCulture"</c>：只招首府所在定居点本身文化的兵种（被征服的异文化城可招当地兵）。</item>
    ///   <item><c>"Any"</c>：不按文化过滤，任何文化都可招。</item>
    /// </list>
    /// 用字符串而非 enum：规避 Newtonsoft / System.Text.Json 对 enum 序列化口径不一致的坑；
    /// 未知值由 <see cref="Evaluators.GenericTroopMatcher.ResolveRequiredCultureId"/> 按 PlayerCulture 兜底。
    /// 通用文化过滤：仅允许指定文化的兵种作为驻军候选。
    /// </summary>
    public string GenericCultureFilter { get; set; } = "PlayerCulture";

    /// <summary>显式允许的文化 stringId 列表（空 = 全部允许）。</summary>
    public List<string> AllowedCultureIds { get; set; } = new();

    // PR-5'(2026-05-24)：MinTier / MaxTier 已删除。solver TierDefs 单段 cap 由 perCityCapacity 公式决定，
    // tier 自由由 MCMF 选择（通用匹配 + 无硬 tier 约束）。
    // 已有 JSON 残留字段被 Newtonsoft 忽略并在下次保存时 drop。

    /// <summary>优先招募的兵种 stringId 列表。注意：本字段允许玩家显式指定，但 mod 默认 **不预填任何兵种 id**（RBM 兼容硬规则）。</summary>
    public List<string> PriorityTroopIds { get; set; } = new();

    /// <summary>禁止出现在驻军中的兵种 stringId 列表。默认也是空。</summary>
    public List<string> BannedTroopIds { get; set; } = new();

    /// <summary>允许招募贵族兵种（如 noble line）。</summary>
    public bool AllowNobleTroops { get; set; } = true;

    /// <summary>允许将俘虏转化为驻军（受游戏内忠诚度规则约束）。</summary>
    public bool AllowPrisonerConversion { get; set; } = true;

    /// <summary>
    /// 主动出击后必须留在城内的实际驻军比例（不含民兵）。
    /// 仅约束 SallyForth 抽兵，不参与招募、调拨或驻军目标计算。
    /// </summary>
    public float MinimumDefenderRatio { get; set; } = 0.20f;

    // 2026-05-28: WartimeMultiplier / PeacetimeMultiplier 已删除。它们是 TargetTotalCount
    // 的乘数，TargetTotalCount 死后这俩也无用。威胁/战时强度由 FiscalAutonomyConfig.ThreatWeightX
    // 系列（Safe/Low/Medium/High/Critical）驱动，作用在 MCMF holding edge value 通道。
    // 老存档 JSON 中的 WartimeMultiplier / PeacetimeMultiplier key 会被 Newtonsoft 自动忽略。

    /// <summary>外派征兵队单次到村招募预算。</summary>
    public int BudgetLimit { get; set; } = 5000;

    /// <summary>当 Town.FoodChange 低于此阈值时暂停招募，避免饿城。</summary>
    public float FoodSafetyThreshold { get; set; } = -2.0f;

    // B7.19：DailyTroopXpBonus 字段已删除，每日驻军 XP 注入数值改为按兵营建筑等级派生
    // （见 GarrisonXpInjector.ComputeXpFromBarracks），不再可配置 — 避免破坏游戏数值平衡。
    // 老存档 JSON 中残留的 DailyTroopXpBonus key 会被 Newtonsoft 自动忽略并在下次保存时 drop。

    /// <summary>构造一份带默认值的规则实例（与各属性的 default 初始化一致）。</summary>
    public static TownGarrisonRule CreateDefault() => new TownGarrisonRule();

    /// <summary>
    /// 深拷贝（含 List&lt;string&gt; 字段独立复制），用于从 GlobalDefaults 克隆出
    /// per-settlement override 的初始值。手写实现避免任何 JSON 依赖与往返序列化开销。
    /// </summary>
    public TownGarrisonRule Clone() => new TownGarrisonRule
    {
        GenericCultureFilter = string.IsNullOrEmpty(this.GenericCultureFilter) ? "PlayerCulture" : this.GenericCultureFilter,
        AllowedCultureIds = new List<string>(this.AllowedCultureIds ?? new List<string>()),
        PriorityTroopIds = new List<string>(this.PriorityTroopIds ?? new List<string>()),
        BannedTroopIds = new List<string>(this.BannedTroopIds ?? new List<string>()),
        AllowNobleTroops = this.AllowNobleTroops,
        AllowPrisonerConversion = this.AllowPrisonerConversion,
        MinimumDefenderRatio = this.MinimumDefenderRatio,
        BudgetLimit = this.BudgetLimit,
        FoodSafetyThreshold = this.FoodSafetyThreshold,
    };
}
