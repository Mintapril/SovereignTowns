using System.Collections.Generic;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>一条数值 / bool 参数的元数据。对应 WebUI 的 *Specs 条目。</summary>
public sealed class SpecEntry
{
    public string Root;        // "GlobalDefaults" / "Thresholds" / "ClanPatrol" / "ClanRecruiter" / "" (=GlobalConfig 根)
    public string Key;         // 属性名
    public string LabelZh, LabelEn, HintZh, HintEn;
    public bool IsBool;        // true=开关行
    public double Min, Max, Step;
    public bool Discrete;      // 整数
    public double? Def;        // 出厂默认值（用于「恢复默认」）；null=无
    public bool Advanced;      // 开发者级旋钮，默认折叠
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
                        HintZh="驻军应维持的兵员数", HintEn="The number of troops a garrison should maintain.",
                        Min=50, Max=500, Discrete=true, Step=1, Def=150 },

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

                    new SpecEntry { Root="Thresholds", Key="RecruitmentCandidateBatchSize",
                        LabelZh="征兵：每轮候选村庄数", LabelEn="Recruitment: candidate villages per round",
                        HintZh="征兵规划每轮评估的候选村庄数",
                        HintEn="Number of candidate villages evaluated per recruitment-planning round.",
                        Min=1, Max=50, Discrete=true, Step=1, Def=8, Advanced=true },

                    // ClanRecruiter scheduler — adv=true
                    new SpecEntry { Root="ClanRecruiter", Key="EtaBufferHours",
                        LabelZh="征兵调度：ETA 缓冲", LabelEn="Recruiter scheduling: ETA buffer",
                        HintZh="站点预占时长 = 预计到达时间 + 此值",
                        HintEn="Stop reservation duration = estimated time of arrival + this value.",
                        Min=0, Max=168, Discrete=false, Step=0.5, Def=1.0, Advanced=true },

                    new SpecEntry { Root="ClanRecruiter", Key="MinVisitGapHours",
                        LabelZh="征兵调度：回访间隔", LabelEn="Recruiter scheduling: revisit gap",
                        HintZh="同一村庄的最小回访间隔",
                        HintEn="Minimum gap before revisiting the same village.",
                        Min=0, Max=720, Discrete=false, Step=1, Def=4, Advanced=true },

                    new SpecEntry { Root="ClanRecruiter", Key="DistanceWeightHoursPerTile",
                        LabelZh="征兵调度：距离评分权重", LabelEn="Recruiter scheduling: distance scoring weight",
                        HintZh="站点评分中距离项的权重，值越大越偏好近处",
                        HintEn="Weight of the distance term in stop scoring; higher favours nearer stops.",
                        Min=0, Max=100, Discrete=false, Step=0.1, Def=0.5, Advanced=true },
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
                    new SpecEntry { Root="Thresholds", Key="PatrolReserveAfterCreationRatio",
                        LabelZh="巡逻队：创建后保留比例", LabelEn="Patrol: garrison reserve after creation",
                        HintZh="创建巡逻队后首府至少保留实际驻军 × 此比例",
                        HintEn="After creating a patrol party, the capital keeps at least actual garrison × this fraction.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.8 },

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
                    new SpecEntry { Root="Thresholds", Key="TransferCriticalProjectedRatio",
                        LabelZh="调拨：危急驻军比例", LabelEn="Transfer: critical garrison ratio",
                        HintZh="预计驻军低于目标驻军 × 此比例时视为危急",
                        HintEn="A settlement is treated as critical when its projected garrison falls below target garrison × this fraction.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.24 },

                    new SpecEntry { Root="Thresholds", Key="TransferRatio",
                        LabelZh="调拨：源城抽取比例", LabelEn="Transfer: source extraction ratio",
                        HintZh="从源城驻军中按此比例抽取",
                        HintEn="Fraction drawn from the source settlement garrison.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.30 },

                    new SpecEntry { Root="Thresholds", Key="TransferMaxTroopsPerTaskRatio",
                        LabelZh="调拨：单次上限比例", LabelEn="Transfer: per-task cap ratio",
                        HintZh="单次调拨最多搬运源城实际驻军 × 此比例",
                        HintEn="A single transfer moves at most source actual garrison × this fraction.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.67 },

                    new SpecEntry { Root="Thresholds", Key="TransferMinTroopRatio",
                        LabelZh="调拨：单次下限比例", LabelEn="Transfer: per-task floor ratio",
                        HintZh="算出的调拨人数低于源城实际驻军 × 此比例则放弃",
                        HintEn="The transfer is abandoned if the computed headcount is below source actual garrison × this fraction.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.13 },

                    new SpecEntry { Root="Thresholds", Key="TransferCapacityWeight",
                        LabelZh="调拨：容量评分权重", LabelEn="Transfer: capacity scoring weight",
                        HintZh="调拨评分中按源城可用容量加权，调大利好高容量源",
                        HintEn="Weight given to the source settlement available capacity in transfer scoring; higher favours high-capacity sources.",
                        Min=0, Max=1, Discrete=false, Step=0.01, Def=0.05, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="TransferBranchToBranchPenalty",
                        LabelZh="调拨：非首府互调惩罚", LabelEn="Transfer: branch-to-branch penalty",
                        HintZh="两座非首府之间的调拨在评分中减去此值（值越大越避免）",
                        HintEn="This value is subtracted in scoring for a transfer between two branch settlements (higher = more strongly avoided).",
                        Min=0, Max=100, Discrete=false, Step=1, Def=25, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="TransferCapitalSourcePenalty",
                        LabelZh="调拨：首府出兵惩罚", LabelEn="Transfer: capital-as-source penalty",
                        HintZh="从首府向非首府调兵时评分加上此值（值越大越保留首府兵力）",
                        HintEn="This value is added in scoring when transferring from the capital to a branch (higher = more strongly preserves capital strength).",
                        Min=0, Max=100, Discrete=false, Step=1, Def=10, Advanced=true },
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
                    new SpecEntry { Root="Thresholds", Key="McmfHardPenalty",
                        LabelZh="MCMF：硬不匹配罚分", LabelEn="MCMF: hard-mismatch penalty",
                        HintZh="兵种 role 不符或精确模板不在升级树上的硬罚分",
                        HintEn="Hard penalty when a troop role mismatches or an exact-template troop is not on the upgrade tree.",
                        Min=0, Max=10000, Discrete=true, Step=1, Def=1000, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="McmfTierPenalty",
                        LabelZh="MCMF：Tier 差距罚分", LabelEn="MCMF: tier-gap penalty",
                        HintZh="每差 1 个 Tier 的匹配罚分",
                        HintEn="Matching penalty per tier of difference.",
                        Min=0, Max=10000, Discrete=true, Step=1, Def=50, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="McmfLeniency",
                        LabelZh="MCMF：缺口宽容度", LabelEn="MCMF: shortfall leniency",
                        HintZh="缺口越大越降低匹配罚分；0 = 严格，1 = 最大宽容",
                        HintEn="A larger shortfall lowers the matching penalty; 0 = strict, 1 = maximum leniency.",
                        Min=0, Max=1, Discrete=false, Step=0.05, Def=0.8, Advanced=true },

                    new SpecEntry { Root="Thresholds", Key="McmfUnmetCost",
                        LabelZh="MCMF：未满足成本", LabelEn="MCMF: unmet-demand cost",
                        HintZh="需求未满足的成本；低于极差路线时会选择暂不派遣",
                        HintEn="Cost of leaving demand unmet; when this is below a very poor route, the solver chooses not to dispatch for now.",
                        Min=0, Max=10000, Discrete=true, Step=1, Def=2000, Advanced=true },

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
        };
    }
}
