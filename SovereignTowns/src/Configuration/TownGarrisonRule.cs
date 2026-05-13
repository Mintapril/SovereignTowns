using System.Collections.Generic;

namespace SovereignTowns.Configuration;

/// <summary>
/// 单个 Town 驻军规则的 POCO。可被 GlobalConfig.GlobalDefaults 或
/// GlobalConfig.PerSettlementOverrides[settlementStringId] 引用。所有字段必须可由
/// System.Text.Json 直接序列化/反序列化（公开属性 + 公开 setter）。
/// </summary>
public sealed class TownGarrisonRule
{
    /// <summary>目标驻军总人数（不含贵族军官/英雄）。</summary>
    public int TargetTotalCount { get; set; } = 80;

    /// <summary>
    /// true = 使用文化无关的兵种/Tier 比例匹配；false = 使用 ImprovedGarrisons 风格的具体兵员模板。
    /// </summary>
    public bool UseGenericMatching { get; set; } = true;

    /// <summary>
    /// ImprovedGarrisons 风格的具体兵员模板：CharacterObject.StringId -> 目标数量。
    /// 当 <see cref="UseGenericMatching"/> 为 false 时，招募和升级会按升级树匹配这些目标兵种。
    /// </summary>
    public Dictionary<string, int> ExactTroopTemplate { get; set; } = new();

    /// <summary>骑兵兵种占比。Cavalry + Infantry + Archer + Crossbow + Thrower 期望约等于 1.0。</summary>
    public float CavalryRatio { get; set; } = 0.20f;

    /// <summary>步兵（含盾兵 / 长矛 / 双手）兵种占比。</summary>
    public float InfantryRatio { get; set; } = 0.50f;

    /// <summary>弓手兵种占比。</summary>
    public float ArcherRatio { get; set; } = 0.20f;

    /// <summary>弩手兵种占比。</summary>
    public float CrossbowRatio { get; set; } = 0.05f;

    /// <summary>投掷兵（标枪 / 飞斧）占比。</summary>
    public float ThrowerRatio { get; set; } = 0.05f;

    /// <summary>允许招募的最低 Tier（含）。通用匹配模式下作为硬边界，与 MaxTier 一起圈定可招募范围。</summary>
    /// <remarks>B7.10: 之前还有 Tier1..6Ratio 用于按 tier 分桶；用户决策简化为只看 5 个 role 比例，
    /// tier 维度仅保留 MinTier/MaxTier 硬边界。</remarks>
    public int MinTier { get; set; } = 2;

    /// <summary>允许招募的最高 Tier（含）。与 MinTier 配对使用。</summary>
    public int MaxTier { get; set; } = 5;

    /// <summary>true = 仅允许 Town 所属王国文化的兵种；false = 不做文化限制。通用匹配默认不限制文化/阵营。</summary>
    public bool RestrictToFactionCultures { get; set; } = false;

    /// <summary>显式允许的文化 stringId 列表（空 = 全部允许，受 RestrictToFactionCultures 进一步约束）。</summary>
    public List<string> AllowedCultureIds { get; set; } = new();

    /// <summary>优先招募的兵种 stringId 列表。注意：本字段允许玩家显式指定，但 mod 默认 **不预填任何兵种 id**（RBM 兼容硬规则）。</summary>
    public List<string> PriorityTroopIds { get; set; } = new();

    /// <summary>禁止出现在驻军中的兵种 stringId 列表。默认也是空。</summary>
    public List<string> BannedTroopIds { get; set; } = new();

    /// <summary>允许在缺人时招募 Tier &lt; MinTier 的填充兵。</summary>
    public bool AllowLowTierFiller { get; set; } = true;

    /// <summary>允许招募贵族兵种（如 noble line）。</summary>
    public bool AllowNobleTroops { get; set; } = true;

    /// <summary>允许将俘虏转化为驻军（受游戏内忠诚度规则约束）。</summary>
    public bool AllowPrisonerConversion { get; set; } = true;

    /// <summary>允许自动升级低级兵种为高级兵种。</summary>
    public bool AllowAutoUpgrade { get; set; } = true;

    /// <summary>true = 不解散玩家手动放入的兵；false = 强制以规则覆盖现有驻军。</summary>
    public bool PreserveExisting { get; set; } = true;

    /// <summary>true = 当驻军超过 TargetTotalCount 时自动遣散多余兵员。</summary>
    public bool AutoDisbandExcess { get; set; } = false;

    /// <summary>无论目标人数为何，至少保留的最少防守人数（被围时硬下限）。</summary>
    public int MinimumDefenders { get; set; } = 30;

    /// <summary>当所属王国处于战争状态时，TargetTotalCount 的乘数。</summary>
    public float WartimeMultiplier { get; set; } = 1.5f;

    /// <summary>和平时期 TargetTotalCount 的乘数。</summary>
    public float PeacetimeMultiplier { get; set; } = 1.0f;

    /// <summary>单日招募预算上限（denar）。超过则当日停止招募。</summary>
    public int BudgetLimit { get; set; } = 5000;

    /// <summary>当 Town.FoodChange 低于此阈值时暂停招募，避免饿城。</summary>
    public float FoodSafetyThreshold { get; set; } = -2.0f;

    /// <summary>每日给驻军每个非 hero 兵员注入的固定 XP（与 vanilla DailyTroopXpBonusModel 加成叠加）。</summary>
    public int DailyTroopXpBonus { get; set; } = 5;

    /// <summary>构造一份带默认值的规则实例（与各属性的 default 初始化一致）。</summary>
    public static TownGarrisonRule CreateDefault() => new TownGarrisonRule();

    /// <summary>
    /// 深拷贝（含 List&lt;string&gt; 字段独立复制），用于从 GlobalDefaults 克隆出
    /// per-settlement override 的初始值。手写实现避免任何 JSON 依赖与往返序列化开销。
    /// </summary>
    public TownGarrisonRule Clone() => new TownGarrisonRule
    {
        TargetTotalCount = this.TargetTotalCount,
        UseGenericMatching = this.UseGenericMatching,
        ExactTroopTemplate = new Dictionary<string, int>(this.ExactTroopTemplate ?? new Dictionary<string, int>()),
        CavalryRatio = this.CavalryRatio,
        InfantryRatio = this.InfantryRatio,
        ArcherRatio = this.ArcherRatio,
        CrossbowRatio = this.CrossbowRatio,
        ThrowerRatio = this.ThrowerRatio,
        MinTier = this.MinTier,
        MaxTier = this.MaxTier,
        RestrictToFactionCultures = this.RestrictToFactionCultures,
        AllowedCultureIds = new List<string>(this.AllowedCultureIds ?? new List<string>()),
        PriorityTroopIds = new List<string>(this.PriorityTroopIds ?? new List<string>()),
        BannedTroopIds = new List<string>(this.BannedTroopIds ?? new List<string>()),
        AllowLowTierFiller = this.AllowLowTierFiller,
        AllowNobleTroops = this.AllowNobleTroops,
        AllowPrisonerConversion = this.AllowPrisonerConversion,
        AllowAutoUpgrade = this.AllowAutoUpgrade,
        PreserveExisting = this.PreserveExisting,
        AutoDisbandExcess = this.AutoDisbandExcess,
        MinimumDefenders = this.MinimumDefenders,
        WartimeMultiplier = this.WartimeMultiplier,
        PeacetimeMultiplier = this.PeacetimeMultiplier,
        BudgetLimit = this.BudgetLimit,
        FoodSafetyThreshold = this.FoodSafetyThreshold,
        DailyTroopXpBonus = this.DailyTroopXpBonus,
    };
}
