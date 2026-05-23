using System.Collections.Generic;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>一条数值 / bool 参数的元数据。对应 WebUI 的 *Specs 条目。</summary>
public sealed class SpecEntry
{
    public string Root;        // "GlobalDefaults" / "Thresholds" / "ClanPatrol" / "" (=GlobalConfig 根)
    public string Key;         // 属性名
    public string LabelZh, LabelEn, HintZh, HintEn;
    public bool IsBool;        // true=开关行
    public double Min, Max, Step;
    public bool Discrete;      // 整数
    public double? Def;        // 出厂默认值（用于「恢复默认」）；null=无
    public bool Advanced;      // 开发者级旋钮，默认折叠

    /// <summary>
    /// 条件可见：非空时，此 spec 仅在 <c>GlobalConfig.FiscalAutonomy.AllowManualGarrisonTargets == true</c>
    /// 才在控制面板 / WebUI 渲染（值 "AllowManualGarrisonTargets"）。auto 模式下驻军目标由调度器
    /// (Pass A)决定，手动旋钮无意义 —— 隐藏避免误导。其他值保留供将来扩展。
    ///
    /// 注意：手动驻军目标共有两个被门控的旋钮，走两套机制 ——
    /// (1) <c>GlobalDefaults.TargetTotalCount</c> 在 spec 列表内，由此字段门控（由
    ///     <see cref="SettingsGroupVM"/> 与 WebUI settingsGroups getter 过滤）；
    /// (2) <c>BranchDefaults.TargetPower</c> 是 <see cref="BranchesTabVM"/> 的硬编码行，
    ///     由该 VM 的 <c>ManualMode</c> 标志 + prefab/WebUI 的 IsVisible/x-show 门控。
    /// 新增第三个手动门控旋钮时，两处都要顾及。
    /// </summary>
    public string RequiresManualMode;
}

public sealed class SpecGroup
{
    public string Key, LabelZh, LabelEn, HintZh, HintEn;
    public bool Advanced;      // 整组高级（如 mcmf）
    public List<SpecEntry> Specs = new List<SpecEntry>();
}

public static class ControlPanelSpecs
{
    public static IReadOnlyList<SpecGroup> AllGroups { get; } = BuildGroups();

    private static List<SpecGroup> BuildGroups()
    {
        return new List<SpecGroup>
        {
            // ── 1. targets ────────────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "targets",
                LabelZh = "目标预算", LabelEn = "Targets & budget",
                HintZh  = "驻军目标、预算和安全阈值；这些值决定每个领地想维持多少兵以及什么时候暂停招募。",
                HintEn  = "Garrison targets, budgets and safety thresholds; these decide how many troops each holding should maintain and when to pause recruitment.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="GlobalDefaults", Key="TargetTotalCount",
                        LabelZh="目标驻军总数", LabelEn="Target garrison size",
                        HintZh="驻军应维持的兵员数。仅在「财政自治」分组开启「允许手动驻军目标」后生效；自动模式下驻军规模由调度器按防御价值与预算分配决定。",
                        HintEn="The number of troops a garrison should maintain. Effective only when \"Allow manual garrison targets\" is enabled in the Fiscal Autonomy group; in auto mode the garrison size is decided by the dispatcher from defensive value and budget.",
                        Min=50, Max=500, Discrete=true, Step=1, Def=150,
                        RequiresManualMode="AllowManualGarrisonTargets" },

                    new SpecEntry { Root="GlobalDefaults", Key="MinimumDefenderRatio",
                        LabelZh="最少防守比例", LabelEn="Minimum defender ratio",
                        HintZh="仅约束「主动出击」：出击队不会让实际驻军跌破此比例。征兵队 / 跨城调拨 / 招募节奏均不受此限",
                        HintEn="Constrains sally-forth only: a sally party will not pull the actual garrison below this fraction. Recruiter parties, cross-town transfers and recruitment pacing are not bound by it.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.20 },

                    new SpecEntry { Root="GlobalDefaults", Key="BudgetLimit",
                        LabelZh="招募预算基准", LabelEn="Recruitment budget baseline",
                        HintZh="外派征兵队单次到村招募预算；自动升级预算也按此派生",
                        HintEn="Per-trip village recruitment budget for a dispatched recruiter party; the auto-upgrade budget is derived from it too.",
                        Min=0, Max=50000, Discrete=true, Step=1, Def=5000 },

                    new SpecEntry { Root="GlobalDefaults", Key="WartimeMultiplier",
                        LabelZh="高威胁目标乘数", LabelEn="High-threat target multiplier",
                        HintZh="城镇威胁评估达到 High/Critical 时的目标人数倍数",
                        HintEn="Target headcount multiplier when the town threat assessment reaches High/Critical.",
                        Min=0.5, Max=2.0, Discrete=false, Step=0.01, Def=1.5 },

                    new SpecEntry { Root="GlobalDefaults", Key="PeacetimeMultiplier",
                        LabelZh="常态目标乘数", LabelEn="Normal target multiplier",
                        HintZh="城镇威胁评估低于 High 时的目标人数倍数",
                        HintEn="Target headcount multiplier when the town threat assessment is below High.",
                        Min=0.5, Max=2.0, Discrete=false, Step=0.01, Def=1.0 },

                    new SpecEntry { Root="GlobalDefaults", Key="FoodSafetyThreshold",
                        LabelZh="食物安全阈值", LabelEn="Food safety threshold",
                        HintZh="Town.FoodChange 低于此值暂停招募",
                        HintEn="Pause recruitment when Town.FoodChange drops below this value.",
                        Min=-50, Max=50, Discrete=false, Step=0.5, Def=-2.0 },
                },
            },

            // ── 2. recruitment ────────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "recruitment",
                LabelZh = "招募", LabelEn = "Recruitment",
                HintZh  = "外派征兵队、村庄候选搜索和回访节奏。",
                HintEn  = "Dispatched recruiter parties, village candidate search and revisit pacing.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="", Key="VillageCooldownHours",
                        LabelZh="村庄招募冷却", LabelEn="Village recruitment cooldown",
                        HintZh="同一村庄被招过后多少小时不再候选 (12–240)",
                        HintEn="How many hours a village stays out of the candidate pool after being recruited from (12–240).",
                        Min=12, Max=240, Discrete=true, Step=1, Def=72 },

                    new SpecEntry { Root="Thresholds", Key="RecruitmentMinDemandRatio",
                        LabelZh="征兵：派遣缺口比例", LabelEn="Recruitment: dispatch demand ratio",
                        HintZh="剩余缺口低于首府实际驻军 × 此比例不再派征兵队",
                        HintEn="No more recruiter parties are dispatched once the remaining shortfall is below capital actual garrison × this fraction.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.07 },

                    new SpecEntry { Root="Thresholds", Key="RecruiterEscortRatio",
                        LabelZh="征兵队：护卫比例", LabelEn="Recruiter: escort ratio",
                        HintZh="派出征兵队时从首府驻军抽取的护卫比例",
                        HintEn="Fraction of the capital garrison taken as escort when dispatching a recruiter party.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.10 },

                    new SpecEntry { Root="Thresholds", Key="RecruiterReturnRecruitedCount",
                        LabelZh="征兵队：招募返航人数", LabelEn="Recruiter: recruited count to return",
                        HintZh="本趟实际招募人数（不含护卫）达到此值即返航",
                        HintEn="The recruiter party returns once it has actually recruited this many troops this trip (escort not counted).",
                        Min=1, Max=500, Discrete=true, Step=1, Def=50 },

                    new SpecEntry { Root="Thresholds", Key="RecruiterMinHomeGarrison",
                        LabelZh="征兵队：首府最低驻军", LabelEn="Recruiter: minimum home garrison",
                        HintZh="派征兵队前首府实际驻军必须 ≥ 此值；0 = 关闭限制，允许 0 兵裸车",
                        HintEn="Before a recruiter party is dispatched, the capital actual garrison must be >= this value; 0 = no limit, allowing a bare 0-troop dispatch.",
                        Min=0, Max=500, Discrete=true, Step=1, Def=0 },

                    new SpecEntry { Root="Thresholds", Key="RecruiterVillageCandidateCap",
                        LabelZh="征兵：MCMF 候选村数上限", LabelEn="Recruitment: MCMF candidate village cap",
                        HintZh="MCMF 招募图每个首府纳入的候选村数 —— 取距首府最近的 K 个合格村作为可招募来源。原版全图约 210 村，默认 250 即纳入全图；征兵队实际跋涉多远由边距离费用 + 未满足成本决定。调低纯为限制超大 mod 地图的求解规模",
                        HintEn="Number of candidate villages the MCMF recruitment graph includes per capital — the K nearest eligible villages. Vanilla Calradia has ~210 villages, so the default 250 includes the whole map; how far a recruiter actually treks is bounded by edge distance cost + unmet-demand cost. Lower it only to limit solve size on very large modded maps.",
                        Min=4, Max=300, Discrete=true, Step=1, Def=250, Advanced=true },
                },
            },

            // ── 3. patrol_transfer ────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "patrol_transfer",
                LabelZh = "巡逻调拨", LabelEn = "Patrol & transfer",
                HintZh  = "巡逻队创建、巡逻站点选择、出击支援和领地间兵力调拨。",
                HintEn  = "Patrol party creation, patrol stop selection, sally support and inter-holding troop transfers.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="Thresholds", Key="PatrolTroopBatchRatio",
                        LabelZh="巡逻队：每次抽兵比例", LabelEn="Patrol: troops drawn per batch",
                        HintZh="新建巡逻队时从首府实际驻军抽走的比例",
                        HintEn="Fraction of the capital actual garrison drawn when creating a new patrol party.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.10 },

                    new SpecEntry { Root="Thresholds", Key="PatrolMinDispatchSize",
                        LabelZh="巡逻队：最少出发人数", LabelEn="Patrol: minimum dispatch size",
                        HintZh="按比例算出的人数低于此值时延迟创建，等驻军积攒到能一次抽够人为止。避免小驻军派出 1-3 人巡逻队送死；0 = 不限制",
                        HintEn="If the ratio-derived headcount is below this value, creation is delayed until the garrison has grown enough to draw a full batch at once. Prevents a small garrison from sending a 1-3-man patrol to its death; 0 = no limit.",
                        Min=0, Max=500, Discrete=true, Step=1, Def=50 },

                    new SpecEntry { Root="ClanPatrol", Key="AvoidRaidedVillages",
                        LabelZh="巡逻：避开被劫掠村庄", LabelEn="Patrol: avoid raided villages",
                        HintZh="开启后，被劫掠中的村庄不会被选为普通巡逻访问点",
                        HintEn="When on, a village under raid is not chosen as a normal patrol visit stop.",
                        IsBool=true, Def=1.0, Advanced=false,
                        Min=0, Max=1, Step=1 },

                    // ClanPatrol scheduler — adv=true
                    new SpecEntry { Root="ClanPatrol", Key="EtaBufferHours",
                        LabelZh="巡逻调度：ETA 缓冲", LabelEn="Patrol scheduling: ETA buffer",
                        HintZh="站点预占时长 = 预计到达时间 + 此值",
                        HintEn="Stop reservation duration = estimated time of arrival + this value.",
                        Min=0, Max=168, Discrete=false, Step=0.5, Def=1.0, Advanced=true },

                    new SpecEntry { Root="ClanPatrol", Key="StuckTimeoutHours",
                        LabelZh="巡逻调度：卡死超时", LabelEn="Patrol scheduling: stuck timeout",
                        HintZh="单段路超过此时长视为卡死，强制重选下一站",
                        HintEn="A single leg exceeding this duration is treated as stuck, forcing reselection of the next stop.",
                        Min=1, Max=720, Discrete=false, Step=1, Def=12, Advanced=true },

                    new SpecEntry { Root="ClanPatrol", Key="MinVisitGapHours",
                        LabelZh="巡逻调度：回访间隔", LabelEn="Patrol scheduling: revisit gap",
                        HintZh="同一定居点的最小回访间隔",
                        HintEn="Minimum gap before revisiting the same settlement.",
                        Min=0, Max=720, Discrete=false, Step=1, Def=4, Advanced=true },

                    new SpecEntry { Root="ClanPatrol", Key="DistanceWeightHoursPerTile",
                        LabelZh="巡逻调度：距离评分权重", LabelEn="Patrol scheduling: distance scoring weight",
                        HintZh="站点评分中距离项的权重，值越大越偏好近处",
                        HintEn="Weight of the distance term in stop scoring; higher favours nearer stops.",
                        Min=0, Max=100, Discrete=false, Step=0.1, Def=0.5, Advanced=true },

                    new SpecEntry { Root="ClanPatrol", Key="SupportEtaThresholdHours",
                        LabelZh="巡逻调度：支援 ETA 阈值", LabelEn="Patrol scheduling: support ETA threshold",
                        HintZh="巡逻队预计到达时间 ≤ 此值时转去支援主动出击",
                        HintEn="A patrol party diverts to support a sally when its ETA is <= this value.",
                        Min=0, Max=168, Discrete=false, Step=0.5, Def=2.0, Advanced=true },

                    // Transfer thresholds
                    new SpecEntry { Root="Thresholds", Key="TransferMaxTroopsPerTaskRatio",
                        LabelZh="调拨：单次上限比例", LabelEn="Transfer: per-task cap ratio",
                        HintZh="单次调拨最多搬运源城实际驻军 × 此比例（调拨队 PartySizeLimit 据此派生）",
                        HintEn="A single transfer moves at most source actual garrison × this fraction (the transfer party PartySizeLimit is derived from it).",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.67 },
                },
            },

            // ── 4. sally ──────────────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "sally",
                LabelZh = "主动出击", LabelEn = "Sally forth",
                HintZh  = "敌军搜索、出击规模、冷却和持续可见判定。",
                HintEn  = "Enemy search, sally size, cooldown and sustained-visibility checks.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="Thresholds", Key="SallyDetectionRadius",
                        LabelZh="主动出击：搜索半径", LabelEn="Sally: detection radius",
                        HintZh="主动出击在城周围搜索敌方目标的地图半径",
                        HintEn="Map radius within which a sally searches for enemy targets around the settlement.",
                        Min=10, Max=500, Discrete=false, Step=5, Def=50 },

                    new SpecEntry { Root="Thresholds", Key="SallyCooldownHours",
                        LabelZh="主动出击：冷却小时", LabelEn="Sally: cooldown hours",
                        HintZh="上次出击结束后的冷却时长（游戏内小时）",
                        HintEn="Cooldown after the previous sally ends (in-game hours).",
                        Min=0, Max=168, Discrete=false, Step=1, Def=24 },

                    new SpecEntry { Root="Thresholds", Key="SallyMinSustainedTicks",
                        LabelZh="主动出击：连续可见小时数", LabelEn="Sally: sustained-visibility hours",
                        HintZh="敌方需在视野内连续存在 N 个游戏内小时才触发出击，避免一进检测圈就冲",
                        HintEn="The enemy must stay continuously visible for N in-game hours before a sally triggers, so a sally is not launched the instant a target enters the detection ring.",
                        Min=1, Max=48, Discrete=true, Step=1, Def=3 },

                    new SpecEntry { Root="Thresholds", Key="SallyExtractionRatio",
                        LabelZh="主动出击：驻军上限比例", LabelEn="Sally: garrison cap ratio",
                        HintZh="主动出击人数不会超过当前实际驻军 × 此比例",
                        HintEn="A sally never takes more than current actual garrison × this fraction.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.60 },

                    new SpecEntry { Root="Thresholds", Key="SallyTargetPartySizeMultiplier",
                        LabelZh="主动出击：目标兵力倍数", LabelEn="Sally: target strength multiplier",
                        HintZh="出击队目标人数 = 敌方目标部队人数 × 此倍数",
                        HintEn="Sally party target headcount = enemy target party headcount × this multiplier.",
                        Min=0, Max=5, Discrete=false, Step=0.25, Def=2.0 },

                    new SpecEntry { Root="Thresholds", Key="SallyCreateMinPartyCount",
                        LabelZh="主动出击：创建下限人数", LabelEn="Sally: minimum creation size",
                        HintZh="计算后得到的出击队人数低于此值时不出击",
                        HintEn="No sally is launched if the computed sally party headcount is below this value.",
                        Min=1, Max=500, Discrete=true, Step=1, Def=30 },
                },
            },

            // ── 5. mcmf (Advanced=true) ───────────────────────────────────────────────
            new SpecGroup
            {
                Key = "mcmf",
                LabelZh = "MCMF 调度", LabelEn = "MCMF scheduling",
                HintZh  = "供需图调度器的匹配罚分、未满足成本和派队固定成本 —— 开发者级旋钮，普通玩家无需调整。",
                HintEn  = "Supply-demand graph solver matching penalties, unmet-demand cost and per-party overhead — developer-level knobs, ordinary players need not adjust.",
                Advanced = true,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="Thresholds", Key="McmfRecruiterOverhead",
                        LabelZh="MCMF：征兵队固定成本", LabelEn="MCMF: recruiter party overhead",
                        HintZh="派出一支征兵队的固定成本",
                        HintEn="Fixed cost of dispatching one recruiter party.",
                        Min=0, Max=10000, Discrete=true, Step=1, Def=100, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="McmfTransferOverhead",
                        LabelZh="MCMF：调拨队固定成本", LabelEn="MCMF: transfer party overhead",
                        HintZh="派出一支调拨队的固定成本",
                        HintEn="Fixed cost of dispatching one transfer party.",
                        Min=0, Max=10000, Discrete=true, Step=1, Def=50, Advanced=true },
                },
            },

            // ── 6. lifecycle ──────────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "lifecycle",
                LabelZh = "生命周期升级", LabelEn = "Lifecycle & upgrade",
                HintZh  = "队伍返航、俘虏上限、卡死保护、空闲解散和驻军升级。",
                HintEn  = "Party return, prisoner cap, stuck protection, idle disband and garrison upgrades.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="Thresholds", Key="PartyReturnSizeRatio",
                        LabelZh="通用：回城解散兵员比例", LabelEn="General: return-and-disband size ratio",
                        HintZh="本 Mod 除调拨外的部队当前兵员 / 出发兵员 低于此值则回首府解散",
                        HintEn="A mod party (other than transfer parties) returns to the capital and disbands when its current headcount / starting headcount falls below this value.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.5 },

                    new SpecEntry { Root="Thresholds", Key="PartyReturnWoundedRatio",
                        LabelZh="通用：回城解散受伤比例", LabelEn="General: return-and-disband wounded ratio",
                        HintZh="本 Mod 除调拨外的部队当前受伤兵员 / 全部兵员 高于此值则回首府解散",
                        HintEn="A mod party (other than transfer parties) returns to the capital and disbands when its wounded / total headcount rises above this value.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.3 },

                    new SpecEntry { Root="Thresholds", Key="PartyPrisonerCap",
                        LabelZh="通用：俘虏上限", LabelEn="General: prisoner cap",
                        HintZh="所有本 Mod 部队（征兵/调拨/巡逻/出击）俘虏上限，超过后每游戏内小时随机释放非英雄俘虏；0 = 关闭",
                        HintEn="Prisoner cap for all mod parties (recruiter/transfer/patrol/sally); above it, non-hero prisoners are released at random each in-game hour; 0 = off.",
                        Min=0, Max=500, Discrete=true, Step=1, Def=30 },

                    new SpecEntry { Root="Thresholds", Key="StuckTeleportHours",
                        LabelZh="通用：卡死瞬移阈值（真实小时）", LabelEn="General: stuck-teleport threshold (real hours)",
                        HintZh="首次检测到卡死起累计真实小时，超过此值后把队伍瞬移到首府城门；0 关闭",
                        HintEn="Real hours accumulated since a stuck state was first detected; past this value the party is teleported to the capital gate; 0 = off.",
                        Min=0, Max=168, Discrete=false, Step=1, Def=24, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="PatrolMaxLifetimeHours",
                        LabelZh="巡逻队：最长存活小时（兜底）", LabelEn="Patrol: maximum lifetime hours (failsafe)",
                        HintZh="巡逻队从创建起累计此小时后强制回家解散，用于防御极端异常场景；0 关闭兜底（接受终身巡逻）。720 = 30 天",
                        HintEn="A patrol party is forced home and disbanded after this many hours since creation, as a failsafe against extreme edge cases; 0 disables the failsafe (accepting lifelong patrols). 720 = 30 days.",
                        Min=0, Max=720, Discrete=false, Step=1, Def=720, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="IdleHoursBeforeForceReturn",
                        LabelZh="生命周期：空闲遣返小时", LabelEn="Lifecycle: idle hours before forced return",
                        HintZh="本 Mod 队伍空闲此时长（游戏内小时）后强制遣返回首府（必须 ≥ 1）",
                        HintEn="A mod party idle for this long (in-game hours) is forced to return to the capital (must be >= 1).",
                        Min=1, Max=720, Discrete=true, Step=1, Def=24 },

                    new SpecEntry { Root="Thresholds", Key="IdleHoursBeforeDisband",
                        LabelZh="生命周期：空闲解散小时", LabelEn="Lifecycle: idle hours before disband",
                        HintZh="本 Mod 队伍空闲此时长（游戏内小时）后直接解散（必须 ≥ 遣返小时）",
                        HintEn="A mod party idle for this long (in-game hours) is disbanded outright (must be >= the forced-return hours).",
                        Min=1, Max=720, Discrete=true, Step=1, Def=36 },

                    new SpecEntry { Root="Thresholds", Key="AutoUpgradeMinTierRatio",
                        LabelZh="升级：触发 Tier 比例", LabelEn="Upgrade: trigger low-tier ratio",
                        HintZh="T1+T2 兵占总兵比例 ≥ 此值时尝试升级",
                        HintEn="An upgrade is attempted when T1+T2 troops make up at least this fraction of the total.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.30 },

                    new SpecEntry { Root="Thresholds", Key="AutoUpgradeMinBudget",
                        LabelZh="升级：单次最低预算", LabelEn="Upgrade: minimum budget per call",
                        HintZh="实际预算 = max(招募预算基准 / 4, 此值)",
                        HintEn="Effective budget = max(recruitment budget baseline / 4, this value).",
                        Min=0, Max=50000, Discrete=true, Step=1, Def=500 },

                    new SpecEntry { Root="Thresholds", Key="AutoUpgradeMaxPerCall",
                        LabelZh="升级：单次最大升级数", LabelEn="Upgrade: max upgrades per call",
                        HintZh="单次升级最多升 N 个兵",
                        HintEn="At most N troops are upgraded per call.",
                        Min=1, Max=500, Discrete=true, Step=1, Def=20 },
                },
            },

            // ── 7. building_bonus ─────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "building_bonus",
                LabelZh = "建筑加成", LabelEn = "Building bonuses",
                HintZh  = "军营(Barracks)等级派生征兵 / 调拨 / 出击队的并发上限与驻军每日 XP;哨所(Guard House)等级派生巡逻队并发上限。上限 = 基础值 + 建筑等级 × 每级增量。",
                HintEn  = "Barracks level drives recruiter / transfer / sally concurrent caps and daily garrison XP; Guard House level drives the patrol cap. Cap = base + building level × per-level increment.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="BuildingBonus", Key="RecruiterBaseCap",
                        LabelZh="征兵队：上限基础值", LabelEn="Recruiter: base cap",
                        HintZh="征兵队并发上限的基础值（军营 0 级时的上限）",
                        HintEn="Base value of the recruiter concurrent cap (the cap at barracks level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="RecruiterCapPerBarracksLevel",
                        LabelZh="征兵队：军营每级增量", LabelEn="Recruiter: cap per barracks level",
                        HintZh="军营每升 1 级，征兵队并发上限 +N",
                        HintEn="Each barracks level adds N to the recruiter concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="TransferBaseCap",
                        LabelZh="调拨队：上限基础值", LabelEn="Transfer: base cap",
                        HintZh="调拨队并发上限的基础值（军营 0 级时的上限）",
                        HintEn="Base value of the transfer concurrent cap (the cap at barracks level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="TransferCapPerBarracksLevel",
                        LabelZh="调拨队：军营每级增量", LabelEn="Transfer: cap per barracks level",
                        HintZh="军营每升 1 级，调拨队并发上限 +N",
                        HintEn="Each barracks level adds N to the transfer concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="SallyBaseCap",
                        LabelZh="出击队：上限基础值", LabelEn="Sally: base cap",
                        HintZh="出击队并发上限的基础值（军营 0 级时的上限）",
                        HintEn="Base value of the sally concurrent cap (the cap at barracks level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="SallyCapPerBarracksLevel",
                        LabelZh="出击队：军营每级增量", LabelEn="Sally: cap per barracks level",
                        HintZh="军营每升 1 级，出击队并发上限 +N",
                        HintEn="Each barracks level adds N to the sally concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="PatrolBaseCap",
                        LabelZh="巡逻队：上限基础值", LabelEn="Patrol: base cap",
                        HintZh="巡逻队并发上限的基础值（哨所 0 级时的上限）",
                        HintEn="Base value of the patrol concurrent cap (the cap at Guard House level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="PatrolCapPerGuardHouseLevel",
                        LabelZh="巡逻队：哨所每级增量", LabelEn="Patrol: cap per Guard House level",
                        HintZh="哨所每升 1 级，巡逻队并发上限 +N",
                        HintEn="Each Guard House level adds N to the patrol concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="GarrisonXpBasePerDay",
                        LabelZh="驻军：每日 XP 基础值", LabelEn="Garrison: base daily XP",
                        HintZh="驻军每兵每日 XP 的基础值（军营 0 级时的值）",
                        HintEn="Base per-troop daily garrison XP (the value at barracks level 0).",
                        Min=0, Max=50, Discrete=true, Step=1, Def=5 },

                    new SpecEntry { Root="BuildingBonus", Key="GarrisonXpPerBarracksLevel",
                        LabelZh="驻军：军营每级 XP 增量", LabelEn="Garrison: daily XP per barracks level",
                        HintZh="军营每升 1 级，驻军每兵每日 XP +N",
                        HintEn="Each barracks level adds N to per-troop daily garrison XP.",
                        Min=0, Max=50, Discrete=true, Step=1, Def=5 },
                },
            },

            // ── 8. fiscal_autonomy ────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "fiscal_autonomy",
                LabelZh = "财政自治", LabelEn = "Fiscal autonomy",
                HintZh  = "受管氏族的金库、驻军工资预算、超额遣散和中央驻军调度器。预算 = 工资预算比例 × 受管领地税收；调度器按防御价值把预算分到各城/堡。开启「允许手动驻军目标」后回到玩家手设目标 + 评估。",
                HintEn  = "A managed clan's treasury, garrison wage budget, excess disbanding and the central garrison dispatcher. Budget = wage-budget ratio × managed-holding tax income; the dispatcher allocates the budget across holdings by defensive value. Enabling \"Allow manual garrison targets\" returns to player-set targets plus an assessment.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="FiscalAutonomy", Key="GarrisonWageBudgetRatio",
                        LabelZh="驻军工资预算比例（仅和平期）", LabelEn="Garrison wage budget ratio (peacetime only)",
                        HintZh="和平期驻军工资预算 = 此比例 × 受管领地可持续收入（税+关税），比例越高养兵越多。注意：战时此旋钮不生效——交战且金库有余额时，预算自动取「全额充足驻军工资」，恒保证养满每城充足驻军。",
                        HintEn="Peacetime garrison wage budget = this fraction × managed-holding sustainable income (tax + tariffs); higher sustains more troops. Note: this knob does NOT apply in war — while at war with a non-empty treasury the budget auto-jumps to the full adequate-garrison wage, always funding every town's adequate garrison.",
                        Min=0.1, Max=1.0, Discrete=false, Step=0.05, Def=0.55 },

                    new SpecEntry { Root="FiscalAutonomy", Key="MinGarrisonFloor",
                        LabelZh="驻军保底头数", LabelEn="Minimum garrison floor",
                        HintZh="每座城/堡无论预算多紧都至少分配的兵员数。调度器优先填满此保底再分配其余预算。",
                        HintEn="The headcount each town/castle is allocated regardless of how tight the budget is. The dispatcher fills this floor first, then allocates the rest of the budget.",
                        Min=0, Max=500, Discrete=true, Step=1, Def=40 },

                    new SpecEntry { Root="FiscalAutonomy", Key="DisbandExcessThreshold",
                        LabelZh="超额遣散阈值", LabelEn="Disband-excess threshold",
                        HintZh="和平期当某城实际驻军 > 可承担目标 × 此倍数时，从低 Tier 起遣散超额兵员。1.2 = 超出 20% 才遣散。",
                        HintEn="In peacetime, when a town's actual garrison exceeds the affordable target × this multiplier, excess troops are disbanded low-tier-first. 1.2 = disband only once 20% over.",
                        Min=1.0, Max=3.0, Discrete=false, Step=0.1, Def=1.2 },

                    new SpecEntry { Root="FiscalAutonomy", Key="DisbandUnaffordableExcess",
                        LabelZh="启用超额遣散", LabelEn="Disband unaffordable excess",
                        HintZh="开启后，和平期超出可承担目标的驻军会被自动遣散以止血。手动驻军目标模式下此项对该领地始终不生效。",
                        HintEn="When on, garrisons exceeding the affordable target are disbanded in peacetime to stop the bleed. Always disabled per-holding while manual garrison targets are active.",
                        IsBool=true, Def=1.0, Advanced=false,
                        Min=0, Max=1, Step=1 },

                    new SpecEntry { Root="FiscalAutonomy", Key="AllowManualGarrisonTargets",
                        LabelZh="允许手动驻军目标", LabelEn="Allow manual garrison targets",
                        HintZh="开启后，「目标预算」分组的「目标驻军总数」与「非首府驻军」的「目标兵力」重新生效作为路由目标；调度器只产出评估（推荐值 vs 你的设定、每日工资差额）。关闭则调度器全权决定驻军规模。",
                        HintEn="When on, \"Target garrison size\" (Targets group) and \"Target strength\" (Branches tab) become effective routing targets again; the dispatcher only produces an assessment (recommended vs your setting, daily wage delta). When off, the dispatcher fully decides garrison size.",
                        IsBool=true, Def=0.0, Advanced=false,
                        Min=0, Max=1, Step=1 },

                    // —— 价值函数 tunables（开发者级，Advanced=true）——
                    new SpecEntry { Root="FiscalAutonomy", Key="AdequateBase",
                        LabelZh="价值函数：充足目标基数", LabelEn="Value function: adequate-target base",
                        HintZh="充足驻军目标的基数：充足目标 = clamp(此基数 + 繁荣度/繁荣除数 + 威胁附加, 保底头数, 硬上限)。须落在 [保底头数, 硬上限] 内。",
                        HintEn="Base of the adequate garrison target: adequate = clamp(this base + prosperity/divisor + threat add-on, floor, hard cap). Must fall within [floor, hard cap].",
                        Min=0, Max=2000, Discrete=true, Step=1, Def=60, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="AdequateProsperityDivisor",
                        LabelZh="价值函数：充足繁荣度除数", LabelEn="Value function: adequate prosperity divisor",
                        HintZh="繁荣度对充足目标的贡献除数：繁荣度 ÷ 此值。值越大繁荣度影响越小。",
                        HintEn="Divisor for prosperity's contribution to the adequate target: prosperity ÷ this value. Larger means prosperity matters less.",
                        Min=1, Max=1000, Discrete=true, Step=1, Def=80, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="AdequateThreatWeight",
                        LabelZh="价值函数：充足威胁权重", LabelEn="Value function: adequate threat weight",
                        HintZh="周边威胁强度对充足目标的权重：威胁附加 = round(周边威胁强度 × 此权重)。",
                        HintEn="Weight of nearby threat intensity on the adequate target: threat add-on = round(nearby threat intensity × this weight).",
                        Min=0, Max=1000, Discrete=true, Step=1, Def=8, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="CoreTierCount",
                        LabelZh="价值函数：核心段子层数", LabelEn="Value function: core sub-tier count",
                        HintZh="核心段离散成多少个递减子层（K）。子层越多价值曲线越平滑，调度图越大。",
                        HintEn="How many diminishing sub-tiers (K) the core tier is discretized into. More sub-tiers smooth the value curve and enlarge the dispatch graph.",
                        Min=1, Max=20, Discrete=true, Step=1, Def=5, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="MaxGarrisonHardCap",
                        LabelZh="价值函数：驻军硬上限兜底", LabelEn="Value function: garrison hard-cap fallback",
                        HintZh="取不到 vanilla 驻军 PartySizeLimit 时使用的硬上限兜底。必须 ≥ 驻军保底头数。",
                        HintEn="Hard-cap fallback used when the vanilla garrison PartySizeLimit cannot be read. Must be >= the minimum garrison floor.",
                        Min=0, Max=2000, Discrete=true, Step=1, Def=400, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="TownAdequateVanillaAnchorRatio",
                        LabelZh="价值函数：城镇充足目标 vanilla 锚定比例", LabelEn="Value function: town adequate vanilla anchor ratio",
                        HintZh="城镇充足目标的下限锚定：充足目标不低于 vanilla 驻军容量（PartySizeLimit）× 此比例。公式基数对普通城镇偏低时由此兜底。0 = 关闭锚定。仅城镇生效，城堡不受影响。",
                        HintEn="Lower-bound anchor for a town's adequate target: it will not drop below the vanilla garrison capacity (PartySizeLimit) × this ratio, backstopping the formula base for ordinary towns. 0 disables the anchor. Towns only — castles are unaffected.",
                        Min=0.0, Max=1.0, Discrete=false, Step=0.05, Def=0.5, Advanced=true },
                },
            },

            // ── 9. merged_solver（合并 MCMF + 时间展开 MPC 调度器，P3）──────────────────
            new SpecGroup
            {
                Key = "merged_solver",
                LabelZh = "合并调度 (P3)", LabelEn = "Unified solver (P3)",
                HintZh  = "合并 MCMF + 时间展开 MPC 调度器。「后勤评估间隔」与「派发风险」即时影响游戏；价值标度 / 时域 / 预测 / 威胁曲线为开发者级旋钮。",
                HintEn  = "The unified MCMF + time-expanded MPC dispatcher. The logistics interval and dispatch-risk knobs affect gameplay immediately; value scale, horizon, forecast and threat-curve are developer-level knobs.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    // —— live 旋钮（即时改变游戏行为，Advanced=false）——
                    new SpecEntry { Root="FiscalAutonomy", Key="CapitalLogisticsTickHours",
                        LabelZh="后勤评估间隔（小时）", LabelEn="Logistics evaluation interval (hours)",
                        HintZh="首府后勤（招募 / 调拨 / 遣散）评估的间隔小时数，也是时间展开 solver 一个 tick 的时长。",
                        HintEn="Interval in hours between capital logistics evaluations (recruitment / transfer / disband); also the length of one tick in the time-expanded solver.",
                        Min=1, Max=24, Discrete=true, Step=1, Def=6 },

                    new SpecEntry { Root="FiscalAutonomy", Key="DispatchRiskEnabled",
                        LabelZh="启用派发风险否决", LabelEn="Enable dispatch-risk veto",
                        HintZh="开启后，征兵队 / 调拨队的路线沿途有敌军时本次评估暂不派出、下个 tick 重试；修复「征兵队被派进敌军送死」。",
                        HintEn="When on, a recruiter / transfer party is held back this tick (retried next tick) if hostiles sit along its route; fixes recruiter parties being sent into enemies.",
                        IsBool=true, Def=1.0, Advanced=false,
                        Min=0, Max=1, Step=1 },

                    new SpecEntry { Root="FiscalAutonomy", Key="DispatchRiskScanRadius",
                        LabelZh="派发风险：扫描半径", LabelEn="Dispatch risk: scan radius",
                        HintZh="检测派发路线沿途敌对兵力的地图半径。",
                        HintEn="Map radius within which hostile strength is scanned along a dispatch route.",
                        Min=0, Max=300, Discrete=false, Step=5, Def=30 },

                    new SpecEntry { Root="FiscalAutonomy", Key="DispatchRiskVetoThreshold",
                        LabelZh="派发风险：否决阈值", LabelEn="Dispatch risk: veto threshold",
                        HintZh="路线沿途敌对健康兵力 ≥ 此值时，本 tick 不派征兵 / 调拨队。",
                        HintEn="When hostile healthy strength along the route reaches this value, no recruiter / transfer party is dispatched this tick.",
                        Min=0, Max=500, Discrete=false, Step=5, Def=60 },

                    new SpecEntry { Root="FiscalAutonomy", Key="SspYieldEvery",
                        LabelZh="求解分帧粒度（每帧增广数）", LabelEn="Solve frame-split granularity (augmentations/frame)",
                        HintZh="调度器的 SSP 求解每隔多少次增广让出一帧。值越小每帧耗时越低、卡顿越轻，但整次求解跨更多帧。不影响求解结果，仅影响分帧观感。默认 8。",
                        HintEn="How many SSP augmentations the solver runs before yielding a frame. Lower = less time per frame and smoother, but the whole solve spans more frames. Does not affect the solve result. Default 8.",
                        Min=1, Max=64, Discrete=true, Step=1, Def=8 },

                    // —— 开发者级旋钮（Advanced=true，默认折叠）——
                    new SpecEntry { Root="FiscalAutonomy", Key="ForecastMode",
                        LabelZh="时域威胁预测模式", LabelEn="Horizon forecast mode",
                        HintZh="0 = 平展（所有 tick 用当前威胁，不前瞻）；1 = 威胁投影（tick>0 按逼近敌军的 ETA 上调威胁）。",
                        HintEn="0 = flat (every tick uses the current threat, no look-ahead); 1 = projected (tick>0 raises threat by approaching-enemy ETA).",
                        Min=0, Max=1, Discrete=true, Step=1, Def=0, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="HorizonTicks",
                        LabelZh="时间展开时域 T（tick 数）", LabelEn="Time-expansion horizon T (ticks)",
                        HintZh="时间展开 solver 一次求解覆盖的 tick 数。每 tick = 后勤评估间隔小时。须 ≥ 典型征兵队行程 tick 数，否则只能原地招募。",
                        HintEn="Number of ticks one solve of the time-expanded solver covers. Each tick = the logistics evaluation interval. Must be >= a typical recruiter trip in ticks, otherwise only in-place recruitment is possible.",
                        Min=1, Max=64, Discrete=true, Step=1, Def=16, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ThreatForecastScanRadius",
                        LabelZh="威胁预测：扫描半径", LabelEn="Threat forecast: scan radius",
                        HintZh="威胁预测器探测正逼近敌军的地图半径，须远大于「派发风险扫描半径」。仅预测模式为「威胁投影」时生效。",
                        HintEn="Map radius within which the threat forecaster detects approaching enemies; should be far larger than the dispatch-risk scan radius. Effective only in projected forecast mode.",
                        Min=0, Max=500, Discrete=false, Step=10, Def=150, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ValueFloorBase",
                        LabelZh="调度器：保底段价值基数", LabelEn="Solver: floor-tier value base",
                        HintZh="保底段单兵价值基数。须与路由成本（数百~千）同量级。",
                        HintEn="Floor-tier per-troop value base. Must be on the same order as routing cost (hundreds to thousands).",
                        Min=0, Max=20000, Discrete=true, Step=100, Def=3000, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ValueCoreBase",
                        LabelZh="调度器：核心段价值基数", LabelEn="Solver: core-tier value base",
                        HintZh="核心段单兵价值基数。核心段在城内逐子层递减。",
                        HintEn="Core-tier per-troop value base. The core tier diminishes across sub-tiers within a town.",
                        Min=0, Max=10000, Discrete=true, Step=50, Def=800, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="SurplusEdgeCost",
                        LabelZh="调度器：过剩段边费用", LabelEn="Solver: surplus-tier edge cost",
                        HintZh="过剩段单兵价值 = 此值的负数。严格为正，使「不养过剩兵」永远优于过度驻军。",
                        HintEn="Surplus-tier per-troop value = the negative of this. Strictly positive, so leaving surplus unfilled always beats over-garrisoning.",
                        Min=1, Max=1000, Discrete=true, Step=1, Def=1, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="PatrolValue",
                        LabelZh="调度器：巡逻回报值", LabelEn="Solver: patrol reward value",
                        HintZh="盈余兵去巡逻 vs 直接遣散的强度。值越大越优先把首府盈余兵送去巡逻。",
                        HintEn="Strength of \"surplus troops patrol\" vs \"surplus troops disbanded\". Higher prefers sending capital surplus to patrol.",
                        Min=0, Max=5000, Discrete=true, Step=50, Def=200, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="DisbandPerDayCap",
                        LabelZh="调度器：每日遣散上限", LabelEn="Solver: disband-per-day cap",
                        HintZh="每城每天经正常段遣散的头数上限。0 = 只在驻军物理塞不下硬上限时才遣散。",
                        HintEn="Per-town daily cap on troops disbanded through the normal channel. 0 = disband only when the garrison physically overflows the hard cap.",
                        Min=0, Max=200, Discrete=true, Step=1, Def=20, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="BypassOverflowPenalty",
                        LabelZh="调度器：溢出遣散罚分", LabelEn="Solver: overflow-disband penalty",
                        HintZh="超过「每日遣散上限」后走溢出段的附加费用。须大于「过剩段边费用」，否则两段限速失效。",
                        HintEn="Extra cost of the overflow channel used once the disband-per-day cap is exhausted. Must exceed the surplus-tier edge cost, or the two-stage rate limit fails.",
                        Min=0, Max=10000, Discrete=true, Step=100, Def=1000, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="DispatchRiskCostScale",
                        LabelZh="派发风险：成本标度", LabelEn="Dispatch risk: cost scale",
                        HintZh="调度器建图时「路线风险 → 成本」的乘子。",
                        HintEn="Multiplier mapping route risk to graph cost inside the solver.",
                        Min=0, Max=200, Discrete=true, Step=1, Def=10, Advanced=true },

                    // —— 价值函数曲线 tunables（开发者级，Advanced=true）——
                    new SpecEntry { Root="FiscalAutonomy", Key="ThreatWeightSafe",
                        LabelZh="威胁权重：安全", LabelEn="Threat weight: Safe",
                        HintZh="风险等级「安全」时的价值乘子。",
                        HintEn="Value multiplier when the risk level is Safe.",
                        Min=0, Max=8, Discrete=false, Step=0.1, Def=0.5, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ThreatWeightLow",
                        LabelZh="威胁权重：低", LabelEn="Threat weight: Low",
                        HintZh="风险等级「低」时的价值乘子。",
                        HintEn="Value multiplier when the risk level is Low.",
                        Min=0, Max=8, Discrete=false, Step=0.1, Def=1.0, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ThreatWeightMedium",
                        LabelZh="威胁权重：中", LabelEn="Threat weight: Medium",
                        HintZh="风险等级「中」时的价值乘子。",
                        HintEn="Value multiplier when the risk level is Medium.",
                        Min=0, Max=8, Discrete=false, Step=0.1, Def=1.5, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ThreatWeightHigh",
                        LabelZh="威胁权重：高", LabelEn="Threat weight: High",
                        HintZh="风险等级「高」时的价值乘子。",
                        HintEn="Value multiplier when the risk level is High.",
                        Min=0, Max=8, Discrete=false, Step=0.1, Def=2.0, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ThreatWeightCritical",
                        LabelZh="威胁权重：危急", LabelEn="Threat weight: Critical",
                        HintZh="风险等级「危急」时的价值乘子。",
                        HintEn="Value multiplier when the risk level is Critical.",
                        Min=0, Max=8, Discrete=false, Step=0.1, Def=3.0, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="CoreDimRange",
                        LabelZh="核心段递减幅度", LabelEn="Core diminishing range",
                        HintZh="核心段逐子层递减的总幅度：最低子层价值乘子 = 1 − 此值。",
                        HintEn="Total diminishing range of the core tier: the lowest sub-tier value multiplier = 1 − this value.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.8, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="CoreDimMidpoint",
                        LabelZh="核心段子层取样中点", LabelEn="Core sub-tier sampling midpoint",
                        HintZh="核心段第 k 子层用 (k + 此值) / K 作为归一化取样位置。",
                        HintEn="The core tier's k-th sub-tier samples at (k + this value) / K as its normalized position.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.5, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ProsperityNormalizer",
                        LabelZh="繁荣度归一化除数", LabelEn="Prosperity normalizer",
                        HintZh="strategic 乘子的繁荣度归一化除数：繁荣度 ÷ 此值 再 clamp 到 [0.5, 1.5]。",
                        HintEn="Prosperity normalizer for the strategic multiplier: prosperity ÷ this value, then clamped to [0.5, 1.5].",
                        Min=500, Max=20000, Discrete=true, Step=100, Def=4000, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="CapitalStrategicBonus",
                        LabelZh="首府战略加成", LabelEn="Capital strategic bonus",
                        HintZh="首府在 strategic 乘子中的加成系数（非首府为 1.0）。",
                        HintEn="Strategic-multiplier bonus coefficient for the capital (non-capital uses 1.0).",
                        Min=1, Max=3, Discrete=false, Step=0.05, Def=1.3, Advanced=true },

                    new SpecEntry { Root="FiscalAutonomy", Key="ReferenceSpeedPerDay",
                        LabelZh="参考队伍速度（地图单位/天）", LabelEn="Reference party speed (map units/day)",
                        HintZh="ETA 估算用的参考队伍速度。近似值，与 vanilla 单队速度无关。",
                        HintEn="Reference party speed used for ETA estimation. An approximation, unrelated to any vanilla party's actual speed.",
                        Min=1, Max=20, Discrete=false, Step=0.5, Def=5.0, Advanced=true },
                },
            },
        };
    }
}
