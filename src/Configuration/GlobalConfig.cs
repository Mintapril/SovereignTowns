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

    /// <summary>首府（capital town，由 CapitalRegistry 标记）的驻军规则。模板 / 比例 / Tier 等所有字段。</summary>
    public TownGarrisonRule GlobalDefaults { get; set; } = TownGarrisonRule.CreateDefault();

    /// <summary>所有非首府（branch town / castle）共享的极简规则。
    /// 玩家氏族用此字段的 TargetPower；AI 氏族走 GarrisonPowerEvaluator.ComputeAiVanillaTargetPower。</summary>
    public BranchRule BranchDefaults { get; set; } = BranchRule.CreateDefault();

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
    /// 把原先散落在各 Manager 内的"人数 / 比例"硬编码常量统一抽出来，让玩家在网页面板调。
    /// 默认值保持与改造前一致 — 不调整面板的玩家不会感知差别。
    /// </summary>
    public PartyThresholds Thresholds { get; set; } = new PartyThresholds();

    /// <summary>建筑等级 → 队伍加成系数。军营 / 哨所等级派生各类队伍并发上限与驻军 XP。</summary>
    public BuildingBonusConfig BuildingBonus { get; set; } = new BuildingBonusConfig();

    /// <summary>财政自治 + 中央驻军调度器配置。金库预算、遣散超额、调度器价值函数等所有财政自治旋钮。</summary>
    public FiscalAutonomyConfig FiscalAutonomy { get; set; } = new FiscalAutonomyConfig();

    /// <summary>构造一份纯默认配置（用于首次安装 / 配置丢失回退）。</summary>
    public static GlobalConfig CreateDefault() => new GlobalConfig
    {
        ConfigVersion = ConfigurationManager.CurrentConfigVersion,
        LastModified = "",
        GlobalDefaults = TownGarrisonRule.CreateDefault(),
        BranchDefaults = BranchRule.CreateDefault(),
        EnabledFeatures = new EnabledFeatures(),
        ClanPatrol = new ClanPatrolConfig(),
        Thresholds = new PartyThresholds(),
        BuildingBonus = new BuildingBonusConfig(),
        FiscalAutonomy = new FiscalAutonomyConfig(),
    };
}

/// <summary>
/// 顶层特性开关。各功能由独立开关控制，不再保留额外的自动驻军总闸。
/// </summary>
public sealed class EnabledFeatures
{
    /// <summary>自动补充驻军兵源。开启后 ST 执行首府原地招募、外派征兵、俘虏转化和驻军训练。</summary>
    public bool AutoRecruitment { get; set; } = true;

    /// <summary>MVP 4：自动派出巡逻队。</summary>
    public bool AutoPatrol { get; set; } = false;

    /// <summary>跨城镇/城堡调拨兵力。关闭后非首府 settlement 只能依赖已有驻军。</summary>
    public bool TroopTransfers { get; set; } = true;

    /// <summary>附近有敌对势力且满足驻军、冷却和持续可见条件时出城攻击。默认关闭以保稳。</summary>
    public bool SallyForth { get; set; } = false;

    // T2 (doc §20 #2)：3 个战利品集中处理 toggle（AutoRecruitMatchingPrisoners / AutoSellNonMatchingPrisoners / AutoSellLoot）
    // 已删除。所有 ST 队伍现在走基类自资金路径（§14 队伍资金）：到 settlement.Town 时由 TryEconomicMaintenance.SellLootAtSettlement
    // 自动卖物品入 _teamFunds；销毁时余款由 TryRefundOnDestroy 退还 home 所有者。

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

    /// <summary>A2：每日活动汇总 InformationManager 弹窗（"今日招/调/巡逻 N 人"）。默认 true。</summary>
    public bool ShowDailySummary { get; set; } = true;

    /// <summary>
    /// 2026-05-18：详细诊断日志开关。开启后 Logger.MinLevel = Debug，落盘所有 [DIAG] 行
    /// （hourly tick 入口状态、状态机 phase 迁移、scheduler 决策、PartyLifecycleManager 进展检测、
    /// 食物维护跳过原因等）。默认 false（线上以 Info 起步避免巨型日志文件）。
    /// 配置变更后由 <see cref="ConfigurationManager"/> 在主线程立即调用 Logger.SetMinLevel 热生效。
    /// </summary>
    public bool VerboseLogging { get; set; } = false;
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
    /// <summary>新建巡逻队时从首府驻军抽走 garrison × 此比例的兵员。原硬编码 15/150 = 0.10。</summary>
    public float PatrolTroopBatchRatio { get; set; } = 0.10f;

    /// <summary>
    /// 2026-05-18：巡逻队出发的最少兵员数（hard floor）。比例算出的 batch 低于此值时延迟创建，
    /// 等驻军积攒到能一次抽够人为止。避免 3-人驻军派 1-人巡逻队遇到劫匪秒灭的 case。
    /// 默认 50；范围 [0, 500]。0 = 不闸（兼容老行为）。
    /// </summary>
    public int PatrolMinDispatchSize { get; set; } = 50;

    /// <summary>
    /// 调度器口径下每支 MCMF 派出的巡逻队规模 —— patrol sink 容量
    /// = (哨所派生的巡逻队上限余量) × 此值。默认 50。
    /// </summary>
    public int PatrolTargetSize { get; set; } = 50;

    // ── 征兵队（RecruitmentDispatcher） ────────────────────────────────────
    /// <summary>派出征兵队时从首府驻军抽取的护卫比例（0–1）。原硬编码 = 0.10（10%）。</summary>
    public float RecruiterEscortRatio { get; set; } = 0.10f;

    /// <summary>本趟实际招募人数（不含护卫）达到此值即返航。默认 50。</summary>
    public int RecruiterReturnRecruitedCount { get; set; } = 50;

    /// <summary>MCMF 招募图每个首府纳入的候选村数:取距首府最近的 K 个合格村
    /// (非围城 / 非交战 / 非冷却 / 非在飞征兵队目标)作为 per-village source。默认 250 —— 原版
    /// 全图约 210 个村,默认即"全图可达村全部纳入";征兵队实际跋涉多远由边的距离费用 +
    /// McmfUnmetCost 决定,不靠此 cap。范围 [4, 300]:调低纯为限制超大 mod 地图的 MCMF 求解规模。</summary>
    public int RecruiterVillageCandidateCap { get; set; } = 250;

    // ── 调拨 / 调度（CapitalLogisticsManager） ─────────────────────────────
    /// <summary>单次调拨最多搬运 source garrison × 此比例的兵员。原硬编码 100/150 ≈ 0.67。
    /// STPartySizeLimitModel 据此派生调拨队 PartySizeLimit。</summary>
    public float TransferMaxTroopsPerTaskRatio { get; set; } = 0.67f;

    /// <summary>派征兵队的缺口下限：capital garrison × 此比例。原硬编码 10/150 ≈ 0.07。</summary>
    public float RecruitmentMinDemandRatio { get; set; } = 0.07f;

    // ── 主动出击（SallyDispatcher） ─────────────────────────────────────
    /// <summary>主动出击时抽取当前实际驻军的比例。原硬编码 = 0.60。</summary>
    public float SallyExtractionRatio { get; set; } = 0.60f;

    /// <summary>主动出击人数 = 目标敌军人数 × 此倍数，再受驻军抽取比例和保留比例钳制。默认 = 2.0。</summary>
    public float SallyTargetPartySizeMultiplier { get; set; } = 2.0f;

    /// <summary>出击队创建下限：计算后得到的出击队人数低于此值时不出击。默认 30。</summary>
    public int SallyCreateMinPartyCount { get; set; } = 30;

    // ── B17 借鉴 IG 沉淀 ──
    /// <summary>A3：派征兵队要求首府驻军 ≥ 此值（IG 边界：空 garrison 派征兵队即裸车送死）。
    /// 默认 0 = 不闸（保留 B7.24 "用户明确不要 floor" 的产品决定）。玩家想保护可调到 1+。
    /// 闸门代码仍在，default=0 时是 no-op。</summary>
    public int RecruiterMinHomeGarrison { get; set; } = 0;

    /// <summary>A6：所有 ST party 的 prisoner roster 上限，超过后每 hour 随机踢出非英雄。
    /// 实际由 <see cref="SovereignTowns.Parties.StPartyComponent.TryEnforcePrisonerCap"/> 应用于
    /// recruiter / transfer / patrol / sally 全部子类。原 IG MobileGarrison.CheckIfPrisonersIsAboveThreshold。默认 30。
    /// 0 = 关闭俘虏 cap。</summary>
    public int PartyPrisonerCap { get; set; } = 30;

    /// <summary>A5：scheduler.IsStuck 重发指令后仍卡死多少 hour 触发二段瞬移到 home.GatePosition。0 关闭。默认 24。</summary>
    public float StuckTeleportHours { get; set; } = 24f;

    /// <summary>
    /// 2026-05-18 v4：巡逻队最长存活小时数（兜底）。到点强制回家解散，防御沿路 Village.Bound 异常、
    /// 战时 village.Party.ItemRoster 异常等极端场景。0 关闭兜底（接受"终身巡逻"风险）。
    /// 默认 720h = 30 天。范围 [0, 720]，但 &lt;24 不实用（短于一次完整巡回）。
    /// </summary>
    public float PatrolMaxLifetimeHours { get; set; } = 720f;

    /// <summary>B5：(deferred) 食物补给已 deferred — 保留字段留作未来 hook。</summary>
    public float FoodReplenishMinDays { get; set; } = 2f;

    /// <summary>B5：(deferred) 食物补给已 deferred — 保留字段留作未来 hook。</summary>
    public float FoodReplenishTopUpDays { get; set; } = 5f;

    // ── DeepSeek audit 2026-05-18 新增（R1/R2/R3/R4/H7-H10） ─────────────

    /// <summary>R1：空闲多少小时后强制遣返回 home。原 PartyLifecycleManager 硬编码 24。范围 [1, 720]。</summary>
    public float IdleHoursBeforeForceReturn { get; set; } = 24f;

    /// <summary>R1：空闲多少小时后直接解散。原硬编码 36。必须 ≥ IdleHoursBeforeForceReturn。范围 [1, 720]。</summary>
    public float IdleHoursBeforeDisband { get; set; } = 36f;

    /// <summary>R2：Sally 触发的搜索半径（Vec2 单位）。原硬编码 50f。范围 [10, 500]。</summary>
    public float SallyDetectionRadius { get; set; } = 50f;

    /// <summary>R2：Sally 出击结束后的冷却小时数。原硬编码 24f。范围 [0, 168]。</summary>
    public float SallyCooldownHours { get; set; } = 24f;

    /// <summary>R2：敌方需连续可见 N 个 hourly tick 才触发 Sally。原硬编码 3。范围 [1, 48]。</summary>
    public int SallyMinSustainedTicks { get; set; } = 3;

    /// <summary>R4：自动升级触发：低 Tier (T1+T2) 占总兵比例 ≥ 此值时尝试升级。原硬编码 0.30。范围 [0, 1]。</summary>
    public float AutoUpgradeMinTierRatio { get; set; } = 0.30f;

    /// <summary>R4：自动升级预算最小值（实际 = max(BudgetLimit/4, 此值)）。原硬编码 500。范围 [0, 50000]。</summary>
    public int AutoUpgradeMinBudget { get; set; } = 500;

    /// <summary>R4：自动升级单次最大升级数。原 TryUpgradeGarrison(maxUpgradesPerCall:20) 硬编码。范围 [1, 500]。</summary>
    public int AutoUpgradeMaxPerCall { get; set; } = 20;

    // T1 重整 2026-05-18：4 类 ST 队伍 seed gold 统一到 StPartyComponent.DefaultSeedGold (2000)，
    // 不再可配置；删除 RecruiterSeedGold / SallySeedGold / TransferSeedGold 三字段（H7/H8 历史项）。

    // ── MCMF solver（UnifiedGarrisonSolver）──────────────────────────────────
    /// <summary>VillageNotableSource 派 recruiter 的固定成本。默认 100。</summary>
    public int McmfRecruiterOverhead { get; set; } = 100;

    /// <summary>GarrisonSurplusSource 派 transfer 的固定成本。默认 50。</summary>
    public int McmfTransferOverhead { get; set; } = 50;
}
