using System.Collections.Generic;

namespace SovereignTowns.Configuration;

/// <summary>
/// 整个 mod 的全局配置根对象。直接对应 Modules/SovereignTowns/Configs/global.json 的顶层 JSON 结构。
/// 兼容版本通过 ConfigVersion 标识；将来字段变更时由 ConfigurationManager 负责迁移。
/// </summary>
public sealed class GlobalConfig
{
    /// <summary>当前配置 schema 版本号。与 ConfigurationManager.CurrentConfigVersion 比对。</summary>
    public int ConfigVersion { get; set; } = 1;

    /// <summary>上次写盘时的 UTC 时间戳（ISO 8601, "O" format）。仅作记录用。</summary>
    public string LastModified { get; set; } = "";

    /// <summary>未在 PerSettlementOverrides 出现的 Town 使用此规则。</summary>
    public TownGarrisonRule GlobalDefaults { get; set; } = TownGarrisonRule.CreateDefault();

    /// <summary>键为 Settlement.StringId 的逐 Town 覆盖规则。</summary>
    public Dictionary<string, TownGarrisonRule> PerSettlementOverrides { get; set; } = new();

    /// <summary>逐特性开关。各 MVP 阶段渐进开启。</summary>
    public EnabledFeatures EnabledFeatures { get; set; } = new();

    /// <summary>
    /// 每日为驻军中俘虏累加的 conformity XP 数量（每名俘虏每天）。
    /// 与 ImprovedGarrisons 行为一致，默认 5。
    /// </summary>
    public int DailyPrisonerConformityAmount { get; set; } = 5;

    /// <summary>
    /// 征兵队出发时从首府 GarrisonParty 抽取多少低 Tier 兵作为基础护卫。
    /// 0 = 不带护卫（队伍出发为空）；建议 ≥ 6 以确保至少能击败小股劫匪。
    /// </summary>
    public int RecruiterEscortSize { get; set; } = 10;

    /// <summary>
    /// 同一 village 被征兵队招过之后多少小时内不再被列为候选（等待 vanilla 刷兵）。
    /// vanilla volunteer 大约每 24h 刷新 1 槽位，72h ≈ 完整 6 槽位中刷 3 槽位，避免空跑。
    /// </summary>
    public int VillageCooldownHours { get; set; } = 72;

    /// <summary>
    /// 征兵队队伍总人数（含基础护卫）达到此值时立刻终止巡回并回首府。
    /// 与 RecruiterEscortSize 的关系：实际新招募人数 = 阈值 - RecruiterEscortSize。
    /// 默认 30 = 10 护卫 + 20 新兵。
    /// </summary>
    public int RecruiterReturnThreshold { get; set; } = 30;

    /// <summary>构造一份纯默认配置（用于首次安装 / 配置丢失回退）。</summary>
    public static GlobalConfig CreateDefault() => new GlobalConfig
    {
        ConfigVersion = ConfigurationManager.CurrentConfigVersion,
        LastModified = "",
        GlobalDefaults = TownGarrisonRule.CreateDefault(),
        PerSettlementOverrides = new Dictionary<string, TownGarrisonRule>(),
        EnabledFeatures = new EnabledFeatures()
    };
}

/// <summary>
/// 顶层特性开关。MVP 1 仅 AutoGarrison 默认开启；其余跟随路线图阶段逐步打开。
/// </summary>
public sealed class EnabledFeatures
{
    /// <summary>MVP 1：自动维持驻军规模。</summary>
    public bool AutoGarrison { get; set; } = true;

    /// <summary>MVP 2：自动招募新兵。</summary>
    public bool AutoRecruitment { get; set; } = false;

    /// <summary>MVP 4：自动派出巡逻队。</summary>
    public bool AutoPatrol { get; set; } = false;

    /// <summary>MVP 3.5：对玩家归属城堡的同等支持。</summary>
    public bool CastleSupport { get; set; } = false;

    /// <summary>MVP 5.5：启用 LLM 推理建议（仅出主意，不动手）。</summary>
    public bool LlmReasoning { get; set; } = false;

    /// <summary>MVP 6：允许 LLM 直接执行决策（动手）。</summary>
    public bool LlmAutoExecute { get; set; } = false;

    /// <summary>无巡逻队时附近有敌对势力则出城攻击。默认关闭以保稳。</summary>
    public bool SallyForth { get; set; } = false;

    /// <summary>战利品：俘虏兵种匹配首府 rule 非零桶 → 直接进首府驻军。</summary>
    public bool AutoRecruitMatchingPrisoners { get; set; } = true;

    /// <summary>战利品：剩余非匹配俘虏 → 自动卖到最近自家 town。</summary>
    public bool AutoSellNonMatchingPrisoners { get; set; } = true;

    /// <summary>战利品：战斗后缴获的装备/物品 → 自动卖到最近自家 town。</summary>
    public bool AutoSellLoot { get; set; } = true;
}
