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

    // B7.20：DailyPrisonerConformityAmount 字段已删除，改为按城镇地牢建筑等级派生
    // （PrisonerRecruitmentManager.ComputeConformityFromDungeon），不再可配置。

    // B7.24：RecruiterEscortSize 字段已删除 — 改为按 garrison × 10% 派生（RecruitmentManager.EscortRatio）。
    // 算出来 < 50 时 0 护卫派遣，征兵队不被驻军下限限制（用户明确）。

    /// <summary>
    /// 同一 village 被征兵队招过之后多少小时内不再被列为候选（等待 vanilla 刷兵）。
    /// vanilla volunteer 大约每 24h 刷新 1 槽位，72h ≈ 完整 6 槽位中刷 3 槽位，避免空跑。
    /// </summary>
    public int VillageCooldownHours { get; set; } = 72;

    /// <summary>
    /// 征兵队队伍总人数（含基础护卫）达到此值时立刻终止巡回并回首府。
    /// </summary>
    public int RecruiterReturnThreshold { get; set; } = 30;

    // B7.20：RecruiterVolunteerMultiplier (硬编码 2.0) 与 RecruiterCostDiscount (硬编码 0.5)
    // 已删除可配置入口 — 直接写死到 RecruitmentManager.RecruitFromTargetVillage 内的常量。

    /// <summary>
    /// B7.26：全氏族巡逻调度配置。由 ClanPatrolScheduler 消费。
    /// 旧配置文件无此字段 → ConfigurationManager.TryLoadFromDisk 反序列化后 ??= 兜底默认值。
    /// 不升 ConfigVersion（当前 11）— 纯加字段(additive-only)兼容旧 JSON；
    /// 后续若修改 ClanPatrolConfig 内字段语义/重命名属性，**必须** bump ConfigVersion。
    /// </summary>
    public ClanPatrolConfig ClanPatrol { get; set; } = new ClanPatrolConfig();

    /// <summary>
    /// B7.27：全氏族征兵调度配置。由 ClanRecruiterScheduler 消费。
    /// 旧配置文件无此字段 → ConfigurationManager.TryLoadFromDisk 反序列化后 ??= 兜底默认值。
    /// </summary>
    public ClanRecruiterConfig ClanRecruiter { get; set; } = new ClanRecruiterConfig();

    /// <summary>构造一份纯默认配置（用于首次安装 / 配置丢失回退）。</summary>
    public static GlobalConfig CreateDefault() => new GlobalConfig
    {
        ConfigVersion = ConfigurationManager.CurrentConfigVersion,
        LastModified = "",
        GlobalDefaults = TownGarrisonRule.CreateDefault(),
        PerSettlementOverrides = new Dictionary<string, TownGarrisonRule>(),
        EnabledFeatures = new EnabledFeatures(),
        ClanPatrol = new ClanPatrolConfig(),
        ClanRecruiter = new ClanRecruiterConfig()
    };
}

/// <summary>
/// 顶层特性开关。MVP 1 仅 AutoGarrison 默认开启；其余跟随路线图阶段逐步打开。
/// </summary>
public sealed class EnabledFeatures
{
    /// <summary>MVP 1：自动维持驻军规模。</summary>
    public bool AutoGarrison { get; set; } = true;

    /// <summary>MVP 2：自动招募新兵。B7.18：默认 true，与 SuppressVanillaGarrisonRecruitment 配套
    /// （否则关掉 vanilla 又不开 ST → 驻军永远 0）。</summary>
    public bool AutoRecruitment { get; set; } = true;

    /// <summary>MVP 4：自动派出巡逻队。</summary>
    public bool AutoPatrol { get; set; } = false;

    /// <summary>MVP 3.5：对玩家归属城堡的同等支持（跨城调拨给城堡补充兵力）。
    /// B7.18：默认 true — 城堡不能自己招募，关闭后城堡兵力永远不增长。</summary>
    public bool CastleSupport { get; set; } = true;

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

    /// <summary>
    /// B7.14：在玩家拥有的城镇/城堡上把 vanilla 的 <c>Town.GarrisonAutoRecruitmentIsEnabled</c>
    /// 设为 false。仅在 <see cref="AutoRecruitment"/> 同时开启时生效。生效后 vanilla 不再自己从 notable VolunteerTypes 拉兵进 GarrisonParty，
    /// 也不再自动升级 GarrisonParty 兵种。本 Mod 的 RecruitingParty 是唯一招兵来源。
    /// Notable 自身的 VolunteerTypes slot 仍正常刷新（这是玩家手动招兵 + RecruitingParty 的供给）。
    /// vanilla 民兵每日自然生成不受影响。
    /// </summary>
    public bool SuppressVanillaGarrisonRecruitment { get; set; } = true;

    /// <summary>
    /// B7.14：抑制范围扩到已由 ST 接管且有可用首府的 AI clan 的城镇 + 城堡。
    /// 开启后这些 AI clan 的城镇不再由 vanilla 自动补兵，改走 ST 的首府/驻军/招募/调拨/出击/俘虏路径；
    /// 巡逻仍保持玩家专属。慎用：会改变 AI 阵营驻军增长速度，影响战役平衡。默认 false。
    /// </summary>
    public bool ApplyToAiSettlementsToo { get; set; } = false;

    /// <summary>
    /// B7.27：玩家金币不足时（&lt; amount）拒绝扣款 / 派遣 / 升级。开启时 mod 自动暂停可推迟的支出，
    /// 防止"派出去又因没钱半截失败"的混乱体验。关闭时允许金币负余额（与 vanilla 玩家自身行为一致）。
    /// </summary>
    public bool PauseSpendingWhenBroke { get; set; } = true;
}

/// <summary>
/// 全氏族巡逻调度配置（B7.26）。
/// </summary>
public sealed class ClanPatrolConfig
{
    /// <summary>ETA 估算的余量小时（防御他队抢同一站点的预占时长 = ETA + 此值）。</summary>
    public float EtaBufferHours { get; set; } = 1.0f;

    /// <summary>单段路超过此时长视为卡死，强制重选下一站。</summary>
    public float StuckTimeoutHours { get; set; } = 12.0f;

    /// <summary>同一定居点的最小回访间隔（防 K=1 巡逻队反复同点）。</summary>
    public float MinVisitGapHours { get; set; } = 4.0f;

    /// <summary>被劫掠的村庄是否从候选集合排除。</summary>
    public bool AvoidRaidedVillages { get; set; } = true;

    /// <summary>
    /// 距离评分权重（小时/Vec2 unit）：score = -hoursSinceVisit + 此值 × ‖partyPos - settlementPos‖₂。
    /// 使用 GetPosition2D 的 Vec2 直接欧式距离（非 Campaign.MapDistanceModel），值越大越偏好近处。
    /// </summary>
    public float DistanceWeightHoursPerTile { get; set; } = 0.5f;

    /// <summary>
    /// B7.27：巡逻队判定能否"在战斗结束前抵达 sally 战斗"的 ETA 阈值。
    /// ETA = 距离 / 巡逻队速度 &lt; 此值 → 转去支援。
    /// </summary>
    public float SupportEtaThresholdHours { get; set; } = 2.0f;
}

/// <summary>
/// 全氏族征兵调度配置（B7.27）。与 ClanPatrolConfig 同构。
/// </summary>
public sealed class ClanRecruiterConfig
{
    /// <summary>ETA 估算的余量小时。</summary>
    public float EtaBufferHours { get; set; } = 1.0f;

    /// <summary>单段路超过此时长视为卡死，强制重选下一站村庄。</summary>
    public float StuckTimeoutHours { get; set; } = 12.0f;

    /// <summary>同一村庄的最小回访间隔（防多支征兵队反复同点）。</summary>
    public float MinVisitGapHours { get; set; } = 4.0f;

    /// <summary>距离评分权重（小时/Vec2 unit）。</summary>
    public float DistanceWeightHoursPerTile { get; set; } = 0.5f;
}
