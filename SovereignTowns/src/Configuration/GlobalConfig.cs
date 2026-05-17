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

    // B7.24：RecruiterEscortSize 字段已删除 — 改为按 garrison × RecruiterEscortRatio 派生。
    // 征兵队护卫不设固定人数 floor，也不被驻军下限限制（用户明确）。

    /// <summary>
    /// 同一 village 被征兵队招过之后多少小时内不再被列为候选（等待 vanilla 刷兵）。
    /// vanilla volunteer 大约每 24h 刷新 1 槽位，72h ≈ 完整 6 槽位中刷 3 槽位，避免空跑。
    /// </summary>
    public int VillageCooldownHours { get; set; } = 72;

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

    /// <summary>
    /// 把原先散落在各 Manager 内的"人数 / 比例"硬编码常量统一抽出来，让玩家在网页面板调。
    /// 默认值保持与改造前一致 — 不调整面板的玩家不会感知差别。
    /// </summary>
    public PartyThresholds Thresholds { get; set; } = new PartyThresholds();

    /// <summary>构造一份纯默认配置（用于首次安装 / 配置丢失回退）。</summary>
    public static GlobalConfig CreateDefault() => new GlobalConfig
    {
        ConfigVersion = ConfigurationManager.CurrentConfigVersion,
        LastModified = "",
        GlobalDefaults = TownGarrisonRule.CreateDefault(),
        PerSettlementOverrides = new Dictionary<string, TownGarrisonRule>(),
        EnabledFeatures = new EnabledFeatures(),
        ClanPatrol = new ClanPatrolConfig(),
        ClanRecruiter = new ClanRecruiterConfig(),
        Thresholds = new PartyThresholds(),
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

    /// <summary>跨城镇/城堡调拨兵力。关闭后非首府 settlement 只能依赖已有驻军。</summary>
    public bool TroopTransfers { get; set; } = true;

    /// <summary>无巡逻队时附近有敌对势力则出城攻击。默认关闭以保稳。</summary>
    public bool SallyForth { get; set; } = false;

    /// <summary>战利品：俘虏兵种匹配首府 rule 非零桶 → 直接进首府驻军。</summary>
    public bool AutoRecruitMatchingPrisoners { get; set; } = true;

    /// <summary>战利品：剩余非匹配俘虏 → 自动卖到最近自家 town。</summary>
    public bool AutoSellNonMatchingPrisoners { get; set; } = true;

    /// <summary>战利品：战斗后缴获的装备/物品 → 自动卖到最近自家 town。</summary>
    public bool AutoSellLoot { get; set; } = true;

    /// <summary>
    /// B7.14：在受管氏族拥有的城镇/城堡上把 vanilla 的 <c>Town.GarrisonAutoRecruitmentIsEnabled</c>
    /// 设为 false。仅在 <see cref="AutoRecruitment"/> 同时开启时生效。生效后 vanilla 不再自己从 notable VolunteerTypes 拉兵进 GarrisonParty，
    /// 也不再自动升级 GarrisonParty 兵种。本 Mod 的 RecruitingParty 是唯一招兵来源。
    /// Notable 自身的 VolunteerTypes slot 仍正常刷新（这是玩家手动招兵 + RecruitingParty 的供给）。
    /// vanilla 民兵每日自然生成不受影响。
    /// </summary>
    public bool SuppressVanillaGarrisonRecruitment { get; set; } = true;

    /// <summary>
    /// B7.14：抑制范围扩到已由 ST 接管且有可用首府的 AI clan 的城镇 + 城堡。
    /// 开启后这些 AI clan 的城镇不再由 vanilla 自动补兵，改走 ST 的首府/驻军/招募/调拨/出击/俘虏/巡逻路径。
    /// 慎用：会改变 AI 阵营驻军增长速度和野外防卫行为，影响战役平衡。默认 false。
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

/// <summary>
/// 队伍创建和调度阈值。所有人数阈值均使用实际驻军（Town.GarrisonParty，不含民兵）
/// 的比例派生，避免大城与小城共用固定人数造成调度失真。
/// </summary>
public sealed class PartyThresholds
{
    // ── 通用回城解散条件（适用于本 mod 创建的非调拨部队：巡逻 / 征兵 / 主动出击） ──
    /// <summary>当前实际兵员低于出发时兵员 × 此比例 → 回首府解散。默认 0.5。</summary>
    public float PartyReturnSizeRatio { get; set; } = 0.5f;

    /// <summary>当前受伤兵员 / 当前全部兵员 高于此比例 → 回首府解散。默认 0.3。</summary>
    public float PartyReturnWoundedRatio { get; set; } = 0.3f;

    // ── 巡逻队（PatrolDispatcher） ──────────────────────────────────────────
    /// <summary>创建巡逻队后首府必须保留的实际驻军比例。默认 0.8（高保留，避免抽空驻军）。</summary>
    public float PatrolReserveAfterCreationRatio { get; set; } = 0.8f;

    /// <summary>新建巡逻队时从首府驻军抽走 garrison × 此比例的兵员。原硬编码 15/150 = 0.10。</summary>
    public float PatrolTroopBatchRatio { get; set; } = 0.10f;

    // ── 征兵队（RecruitmentDispatcher） ────────────────────────────────────
    /// <summary>派出征兵队时从首府驻军抽取的护卫比例（0–1）。原硬编码 = 0.10（10%）。</summary>
    public float RecruiterEscortRatio { get; set; } = 0.10f;

    /// <summary>本趟实际招募人数（不含护卫）达到此值即返航。默认 50。</summary>
    public int RecruiterReturnRecruitedCount { get; set; } = 50;

    // ── 调拨 / 调度（CapitalLogisticsManager） ─────────────────────────────
    /// <summary>预计驻军低于 DesiredTarget × 此比例 → 视为危急缺口。原硬编码 36/150 ≈ 0.24。</summary>
    public float TransferCriticalProjectedRatio { get; set; } = 0.24f;

    /// <summary>从源城驻军中按此比例抽取（再受 dest demand 与 MaxTroopsPerTask 钳制）。原硬编码 = 0.30。</summary>
    public float TransferRatio { get; set; } = 0.30f;

    /// <summary>单次调拨最多搬运 source garrison × 此比例的兵员。原硬编码 100/150 ≈ 0.67。</summary>
    public float TransferMaxTroopsPerTaskRatio { get; set; } = 0.67f;

    /// <summary>算出的调拨人数低于 source garrison × 此比例则放弃任务（除非缺口已临界）。原硬编码 20/150 ≈ 0.13。</summary>
    public float TransferMinTroopRatio { get; set; } = 0.13f;

    /// <summary>派征兵队的缺口下限：capital garrison × 此比例。原硬编码 10/150 ≈ 0.07。</summary>
    public float RecruitmentMinDemandRatio { get; set; } = 0.07f;

    // ── 主动出击（SallyDispatcher） ─────────────────────────────────────
    /// <summary>主动出击时抽取当前实际驻军的比例。原硬编码 = 0.60。</summary>
    public float SallyExtractionRatio { get; set; } = 0.60f;

    /// <summary>主动出击人数 = 目标敌军人数 × 此倍数，再受驻军抽取比例和保留比例钳制。默认 = 2.0。</summary>
    public float SallyTargetPartySizeMultiplier { get; set; } = 2.0f;

    /// <summary>出击队创建下限：计算后得到的出击队人数低于此值时不出击。默认 30。</summary>
    public int SallyCreateMinPartyCount { get; set; } = 30;
}
