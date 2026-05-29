namespace SovereignTowns.Configuration;

/// <summary>财政自治 + 中央驻军调度器配置。详见两份设计文档。</summary>
public sealed class FiscalAutonomyConfig
{
    // ── 工资预算 ──
    // 调度器从 Clan.Gold（= clan.Leader.Gold）当前余额按比例计算每 tick 可用的驻军工资预算。
    // 注:vanilla 没有"氏族金库"独立账本 —— Clan.Gold 即 Leader.Gold,所以"预算"不是抽出
    // 一块封闭账户,只是约束求解器每 tick 不要让推荐驻军超过本比例对应的工资。
    public float GarrisonWageBudgetRatio { get; set; } = 0.75f;

    // ── 后勤评估节奏 ──
    /// <summary>首府后勤评估间隔(小时)。默认 6h;读取时 clamp [1,24]。
    /// 改变「一个 tick = 多久」—— P3 时域 T、遣散速率均以此为单位。</summary>
    public int CapitalLogisticsTickHours { get; set; } = 6;

    // ── 遣散超额 ──
    public float DisbandExcessThreshold  { get; set; } = 1.2f;

    // PR-5'(2026-05-24): 以下字段已被 perCityCapacity + BaseValuePerTier 单段公式取代，全部删除：
    //   MinGarrisonFloor / AdequateBase / AdequateProsperityDivisor / AdequateThreatWeight
    //   / CoreTierCount / MaxGarrisonHardCap / ValueFloorBase / ValueCoreBase / SurplusEdgeCost
    //   / CoreDimRange / CoreDimMidpoint / TownAdequateVanillaAnchorRatio / DisbandUnaffordableExcess
    // AllowManualGarrisonTargets 也一并删除（驻军目标完全由调度器决定）。

    /// <summary>
    /// 巡逻并入调度器:patrol sink 边的回报值 —— patrol 边真实费用 = −此值。
    /// 本质是「用盈余兵巡逻 vs 直接遣散」的强度旋钮:&gt; 0 即让 patrol 胜过 disband(费用 0),
    /// MCMF 把首府盈余兵优先送去巡逻而非销毁;值越大越优先填满 patrol 容量。
    /// patrol **不会**抽走 core 守军 —— patrolHeadroom 是硬容量,逐 slot 经济上盈余兵恒比 core
    /// 兵先占该 slot(盈余省 disband 成本、core 省 hold 成本,前者更大),与本值大小无关。
    /// 首轮取保守值 200,须 in-game 调;clamp [0, K−1]。
    /// </summary>
    public int PatrolValue { get; set; } = 200;

    /// <summary>调度器两段 bypass:每城每【天】经「正常段」遣散的头数上限。
    /// solver 按 <see cref="CapitalLogisticsTickHours"/> 折算成每 tick 上限。
    /// 正常段费用 0(总费 K),溢出段费用 <see cref="BypassOverflowPenalty"/>。
    /// 设 0 = 关闭正常遣散段,只保留溢出段(仅遣散物理塞不下 hardCap 的兵)。</summary>
    public int DisbandPerDayCap { get; set; } = 20;

    /// <summary>调度器两段 bypass:溢出段附加费用。须远大于单段 holding edge value
    /// 以保证正常段耗尽后 solver 优先"留驻盈余"而非"溢出遣散" —— 从而把超额遣散
    /// 限制在每 tick <see cref="DisbandPerDayCap"/> 折算出的上限。</summary>
    public int BypassOverflowPenalty { get; set; } = 1000;

    // ── P3 时间展开 solver ──
    /// <summary>P3 时域 T(tick 数)。须 ≥ 典型征兵队 ETA,否则只能原地招募
    /// (见 audits/2026-05-22-p3-lookahead-design.md §6.5)。读取时 clamp [1,64]。</summary>
    public int HorizonTicks { get; set; } = 16;

    /// <summary>
    /// 一次 SSP 求解的墙钟总预算(毫秒)。超过此预算后切换为同步排干模式 ——
    /// 当前帧把剩下的所有增广跑完,不再 yield。保证求解必出结果(不丢弃),
    /// 代价是该帧 CPU 飙升。会打 Warn 日志 "SSP solve over wall budget"。
    /// 默认 500ms。读取时 clamp [50, 5000]。
    /// </summary>
    public int SolveBudgetWallMs { get; set; } = 500;

    /// <summary>
    /// 单帧花在 SSP 上的 CPU 上限(毫秒)。每次 SSP MoveNext 后检查累计时间,
    /// 超过此值就 yield 一帧。决定分帧卡顿观感 —— 值越小卡顿越轻、求解墙钟越长。
    /// 默认 2ms(60fps 下占帧时间 ~12%)。读取时 clamp [1, 16]。
    /// </summary>
    public int SolvePerFrameBudgetMs { get; set; } = 2;

    // ── 时间展开调度器 value 函数曲线 ──
    /// <summary>威胁等级 → value 乘子。Safe/Low 锚定和平期,Med/High/Crit 决定战时涨兵幅度。</summary>
    public float ThreatWeightSafe     { get; set; } = 0.5f;
    public float ThreatWeightLow      { get; set; } = 1.0f;
    public float ThreatWeightMedium   { get; set; } = 1.5f;
    public float ThreatWeightHigh     { get; set; } = 2.0f;
    public float ThreatWeightCritical { get; set; } = 3.0f;

    /// <summary>strategic 乘子的繁荣度归一化除数:Prosperity / 此值 再 clamp 到 [0.5, 1.5]。</summary>
    public float ProsperityNormalizer { get; set; } = 4000f;
    /// <summary>首府在 strategic 乘子中的加成系数(非首府为 1.0)。</summary>
    public float CapitalStrategicBonus { get; set; } = 1.3f;

    /// <summary>威胁预测器扫描半径(地图单位)—— 探测数天行程外正逼近的敌军,
    /// 须远大于 DispatchRiskScanRadius。初值 150,须 in-game 调。</summary>
    public float ThreatForecastScanRadius { get; set; } = 150f;

    // ── 派发风险(Part D)──
    /// <summary>派发风险否决总开关(D1)。true = 路途有敌军时本 tick 不派征兵 / 调拨队。
    /// 直接影响 live 行为,可一键回退。</summary>
    public bool DispatchRiskEnabled { get; set; } = true;

    /// <summary>HostilePartyScanner 扫描半径(地图单位)。初值 30,须 in-game 调。</summary>
    public float DispatchRiskScanRadius { get; set; } = 30f;

    /// <summary>D1 否决阈值:路线风险分(沿途最大敌对健康兵力)≥ 此值 → 本 tick 不派。
    /// 初值 60,须 in-game 调。</summary>
    public float DispatchRiskVetoThreshold { get; set; } = 60f;

    /// <summary>D2 路线风险→成本 的标度乘子:成本加项 = 风险分 × 此值。
    /// 须与 routing(数百~千)可比。初值 10,须 in-game 调。</summary>
    public int DispatchRiskCostScale { get; set; } = 10;

    // ── PR-1 (2026-05-24) EdgeCost 4 通道权重 ──
    // 默认值保 PR-1 行为不变性（与旧硬编码 K = 20_000_000、单一 int cost 加成同效）。
    // 暴露到控制面板 Gauntlet 滑块；改任何字段均会改变求解行为。

    /// <summary>EdgeCost 合成权重：gold 通道倍数。默认 1（行为不变）。</summary>
    public int CostWeightGold { get; set; } = 1;

    /// <summary>EdgeCost 合成权重：path-risk 通道倍数。默认 1（行为不变）。</summary>
    public int CostWeightRisk { get; set; } = 1;

    /// <summary>EdgeCost 合成权重：strategic 通道倍数。默认 1（行为不变）。</summary>
    public int CostWeightStrategic { get; set; } = 1;

    /// <summary>每 tick 占用一个 driver slot 的"时间机会成本"。替代旧硬编码 K。
    /// 必须满足 TickHoldingValueK × HorizonTicks ≤ int.MaxValue（solver 把 T clamp 到 64，
    /// 故上限 ≈ 33M；默认 T=16 时余量到 134M）且 TickHoldingValueK > 任意 per-edge
    /// strategic value（防符号反转）。默认 20_000_000 与旧 K 同值。</summary>
    public int TickHoldingValueK { get; set; } = 20_000_000;

    // ── PR-4 (2026-05-24) S6 出击 (Sally) ──
    // sally edge 与 patrol edge 同构（holding → sink → superSink），但 sink 是 per-settlement
    // 而非 capital 唯一。允许多座城/堡同时派 sally。

    /// <summary>Sally edge 战略价值基常数。与 PatrolValue 同量级（默认 5000）。
    /// 求解时 cost = (T-τ)·K − SallyValueBase × threat × strat × power(tier)，越大越愿意派 sally。</summary>
    public int SallyValueBase { get; set; } = 5_000;

    /// <summary>首府 sally 额外加成（非首府 = 1.0）。与 CapitalStrategicBonus 平行。默认 1.5。</summary>
    public float CapitalSallyBonus { get; set; } = 1.5f;

    /// <summary>每个 sally 队的固定头数。决定 sallyHeadroom 量化粒度。默认 30。</summary>
    public int SallyTeamSize { get; set; } = 30;

    /// <summary>单 settlement 最多同时存在的 sally 队数（cap）。默认 1，避免 sally 风暴。</summary>
    public int MaxSallyPartiesPerCity { get; set; } = 1;

    // ── PR-5' (2026-05-24) 大简化新字段 ──

    // 2026-05-28: TargetFraction 已删除。每城驻军目标(holding cap)改由 UnifiedGarrisonSolver
    // 内部的 perCityCapacity 分配公式决定: min(PartySizeLimit_i, N × w_i / Σw),
    //   N = (GarrisonWageBudgetRatio × Σ收入) / WagePerTroopAtMaxTier  // 预算可养兵数
    //   w_i = PartySizeLimit_i × threat_i × strategic_i                 // 每城权重
    // 此公式让"预算"真正约束 holding 容量(此前 GarrisonWageBudgetRatio 只入诊断日志)。
    // 老存档 JSON 中残留的 TargetFraction key 会被 Newtonsoft 自动忽略。

    /// <summary>城镇 (Town, 非城堡) strategic 乘子额外加成。城堡 = 1.0。默认 1.3。
    /// 与 CapitalStrategicBonus 叠乘（首府且为城镇时 = 1.3 × CapitalStrategicBonus）。</summary>
    public float TownStrategicBonus { get; set; } = 1.3f;

    /// <summary>holding edge value 基常数（PR-5' 替代旧 ValueCoreBase / ValueFloorBase 多段）。
    /// 单段 value = 此值 × threat × strat × power(tier)。默认 800。</summary>
    public int BaseValuePerTier { get; set; } = 800;

    // ── 卫队（Honor Guard）容量上限 ──
    /// <summary>卫队驻军容量上限（头数）。2026-05-29：卫队改为**恒开启、容量固定 300**，
    /// 不再是控制面板可调项（用户决定）。getter 恒返回 300，setter 忽略写入 —— 因此磁盘 /
    /// 旧存档 global.json 中残留的任何 HonorGuardCap 值（含 0）都不会再关闭卫队。
    /// 卫队 party 本就由 <see cref="SovereignTowns.Capital.HonorGuardManager"/> 无条件创建；
    /// 此值仅作为 MCMF 向卫队注入兵员时的硬上限。</summary>
    public int HonorGuardCap { get => 300; set { /* 固定 300，忽略任何写入 */ } }

    /// <summary>卫队招募 edge 的战略价值基常数。与 PatrolValue（200）/ SallyValueBase（5000）同量级。
    /// MCMF 在 origin → hg_bucket_sink 边上记入 strategic 通道 = 此值 × power(tier)；越大越愿意优先填卫队。
    /// 默认 2500 — 偏强意愿但仍弱于 holding edge 的累积 value，避免抽空首府常规驻军。</summary>
    public int HonorGuardValueBase { get; set; } = 2_500;

    /// <summary>HG 边距离权重：HG edge gold = recruitCost + recOverhead + HgDistanceGoldPerTick × etaV。
    /// 让 MCMF 在近村与远村都能供给同一模板 troopId 时偏好近村；
    /// 设 0 = 完全无视距离；默认 100。范围 [0, 10000]。</summary>
    public int HgDistanceGoldPerTick { get; set; } = 100;

    // ── 卫队招募模板 ──
    /// <summary>
    /// 卫队招募兵种模板：troopId → desiredCount（每个兵种在卫队中希望维持的头数）。
    /// null 或空 dict = MCMF 不派卫队征兵队。
    /// 校验：≤ 50 条目，每条 ≤ 100，count ≥ 0，key 非空。
    /// troopId 有效性在派发层懒校验（MBObjectManager 在 OnSessionLaunched 后才可用）。
    ///
    /// 示例：{"imperial_veteran_infantry": 30, "imperial_palatine_guard": 20}
    /// </summary>
    public System.Collections.Generic.Dictionary<string, int>? HonorGuardTemplate { get; set; }
}
