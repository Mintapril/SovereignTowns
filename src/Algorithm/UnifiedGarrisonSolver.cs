using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Algorithm;

/// <summary>
/// P3 时间展开合并 solver 的求解产出 —— flow 统计 + 按边语义类别汇总 + decode 出的对外契约
/// (每城 tick-0 目标头数 / 派发指令 / 每城 tick-0 遣散头数)。见 audits/2026-05-22-p3-lookahead-design.md §6。
/// </summary>
public sealed class UnifiedSolverResult
{
    /// <summary>true = 求解完整跑完(未被前置守卫提前返回)。</summary>
    public bool Ran { get; set; }

    public int SettlementCount { get; set; }
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }

    /// <summary>Σ superSource 出边容量(初始驻军 + 志愿兵 + 在飞兵)。</summary>
    public int OriginSupply { get; set; }
    /// <summary>预算可供养头数(informational —— 软预算下不再是硬上限)。</summary>
    public int BudgetTroopCap { get; set; }
    /// <summary>Σ layer-0 holding tier 容量。</summary>
    public int DemandTierCapacity { get; set; }

    public int TotalFlow { get; set; }
    public long TotalCost { get; set; }
    /// <summary>tick 0 守军总头数(= Σ Target)。</summary>
    public int DemandFilled { get; set; }

    public int StayFlow { get; set; }
    public int TransferFlow { get; set; }
    public int RecruitFlow { get; set; }
    public int BypassFlow { get; set; }

    /// <summary>Σ patrol sink 边流量(跨所有 tick)。⚠️ 仅诊断:实际派发的巡逻头数 = τ=0 decode
    /// 出的 PatrolInstruction.Count,通常 &lt; 本值(τ&gt;0 的巡逻流只是规划塑形,不产指令)。</summary>
    public int PatrolFlow { get; set; }

    /// <summary>Σ sally sink 边流量(跨所有 tick、所有 settlement)。⚠️ 仅诊断:实际派发的出击
    /// 头数 = τ=0 decode 出的 Σ SallyInstruction.Count,通常 &lt; 本值。</summary>
    public int SallyFlow { get; set; }

    /// <summary>本次求解所用氏族驻军工资预算(金币/日,informational)。</summary>
    public long Budget { get; set; }

    /// <summary>decode:每城 tick-0 目标头数(= Σ_R holding 边 G[S,R,0]→G[S,R,1] 流)。每 fief 预播种 0。</summary>
    public Dictionary<Settlement, int> Target { get; } = new();

    /// <summary>decode:tick-0 派发指令(Recruiter / InPlace / Transfer)。</summary>
    public List<DispatchInstruction> Instructions { get; } = new();

    /// <summary>decode:每城 tick-0 遣散头数(G[S,R,0]→disbandGate[S,0] 流)。</summary>
    public Dictionary<Settlement, int> Disband { get; } = new();

    /// <summary>parallel-run 差异日志行(边语义类别汇总)。Target / 指令级对账见
    /// CapitalLogisticsManager.LogMergedDiff。</summary>
    public string DiffLine(string clanId)
        => $"clan={clanId} settlements={SettlementCount} nodes={NodeCount} edges={EdgeCount} "
         + $"originSupply={OriginSupply} budgetCap={BudgetTroopCap} holdL0Cap={DemandTierCapacity} "
         + $"flow={TotalFlow} tick0Garrison={DemandFilled} "
         + $"stay={StayFlow} recruit={RecruitFlow} transfer={TransferFlow} bypass={BypassFlow} patrol={PatrolFlow} sally={SallyFlow}";
}

/// <summary>
/// P3 时间展开合并 solver(option A:真时间展开 + 软预算)。把驻军分配 + 路由沿时间轴展开
/// 成 T 层单一 MinCostFlow 图、单次求解,decode 只产 tick-0 指令(receding-horizon MPC)。
/// 逐节点逐边规格见 audits/2026-05-22-p3-lookahead-design.md §6;K 平衡规则见 §6.3。
///
/// 历史:M1-M5 单 tick 合并 solver;P3 起重写为时间展开图。
/// 派发由 CapitalLogisticsManager 接线。
/// </summary>
public static class UnifiedGarrisonSolver
{
    // K 平衡偏移：旧 const K = 20_000_000 已迁到 FiscalAutonomyConfig.TickHoldingValueK
    // （PR-1, 2026-05-24）。本类内部仍用 K 作变量名以减少 diff，但读取自 cfg.TickHoldingValueK。
    // 不变性约束（PR-3 将进 Validate()）：K × HorizonTicks ≤ int.MaxValue 且 K > max(per-edge strategic)。

    /// <summary>边的语义类别 —— Solve 后聚合 EdgeFlows 产差异日志。</summary>
    private enum EdgeCat { Internal, Stay, Transfer, Recruit, Bypass, Patrol, Sally }

    /// <summary>decode 用:layer-0 出发 / 指令相关边的登记。</summary>
    private enum DecodeKind { Recruiter = 1, InPlace, Transfer, Disband, HoldingL0, Patrol, Sally, HonorGuardRecruiter }

    /// <summary>
    /// 同步求解封装 —— 一次排干 <see cref="SolveCoroutine"/>。同步驱动下 SSP 的 yield 退化为空
    /// MoveNext,结果与分帧驱动逐字节一致。
    /// </summary>
    public static UnifiedSolverResult Solve(
        CapitalManager manager, Settlement capitalSettlement,
        IHorizonForecast forecast, int patrolHeadroom = 0,
        Dictionary<Settlement, int>? sallyHeadrooms = null)
    {
        UnifiedSolverResult? captured = null;
        try
        {
            var it = SolveCoroutine(manager, capitalSettlement, forecast, patrolHeadroom,
                r => captured = r, sallyHeadrooms);
            while (it.MoveNext()) { }
        }
        catch (Exception ex)
        {
            Logger.Error("UnifiedGarrisonSolver.Solve failed", ex);
        }
        return captured ?? new UnifiedSolverResult();
    }

    /// <summary>
    /// 分帧求解协程。建图同步跑完 → SSP 每 cfg.SspYieldEvery 次增广 yield 一帧 → decode
    /// 同步跑完。完成(含前置守卫提前结束 / 异常)时一律经 finally 调 <paramref name="onResult"/>。
    /// 异常不在此 catch —— 交由 AsyncSimulator.Update 捕获并打印协程栈。
    /// </summary>
    /// <param name="forecast">每 tick 威胁来源(ThreatForecast,按敌军 ETA 投影)。</param>
    /// <param name="patrolHeadroom">巡逻 sink 容量(头数)= 哨所派生的巡逻队上限余量 ×
    /// PatrolTargetSize;0 = 不建巡逻 sink。由 CapitalLogisticsManager 算好传入(solver 不查建筑等级)。</param>
    /// <param name="onResult">求解结束回调(finally 保证触发;前置守卫提前结束时 result.Ran=false)。</param>
    public static System.Collections.IEnumerator SolveCoroutine(
        CapitalManager manager, Settlement capitalSettlement,
        IHorizonForecast forecast, int patrolHeadroom,
        Action<UnifiedSolverResult> onResult,
        Dictionary<Settlement, int>? sallyHeadrooms = null)
    {
        var result = new UnifiedSolverResult();
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null || capitalSettlement == null || forecast == null) yield break;

            // P3 性能埋点:build / solve / decode 三段计时;recruitMs 单列招募块
            // (候选村枚举 + 志愿兵分桶 + 路线风险空间扫描)—— 建图里最可疑的热点。
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long recruitMs = 0;

            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            var thresholds = ConfigurationManager.Current?.Thresholds ?? new PartyThresholds();
            var features = ConfigurationManager.Current?.EnabledFeatures ?? new EnabledFeatures();
            bool allowRecruitment = features.AutoRecruitment;
            bool allowTransfers = features.TroopTransfers;
            int K = cfg.TickHoldingValueK;  // 局部别名，保旧公式可读性；PR-1 替代原 const
            int T = Math.Min(64, Math.Max(1, cfg.HorizonTicks));
            int tickHours = Math.Min(24, Math.Max(1, cfg.CapitalLogisticsTickHours));

            // 管理范围 = 整个 clan.Fiefs。
            // Vanilla 没有 per-fief Hero 持有人（CLAUDE.md #6）：Town.OwnerClan 是唯一的所有权字段,
            // 全 clan.Fiefs 收入由 DailyTickClan 统一汇入 clan.Leader.Gold。所以这里既不应也无法
            // 按 "首府主人 vs 子嗣/配偶持有" 做二级过滤——这种区分在 vanilla 里根本不存在。
            var towns = clan.Fiefs
                .Where(t => t?.Settlement != null && t.Settlement.IsActive && (t.IsTown || t.IsCastle))
                .ToList();
            if (towns.Count == 0) yield break;

            var capitalTown = towns.FirstOrDefault(t => t.Settlement == capitalSettlement);
            if (capitalTown == null)
            {
                Logger.Debug($"UnifiedGarrisonSolver: capital '{capitalSettlement.StringId}' not among clan fiefs");
                yield break;
            }
            var townSet = new HashSet<Settlement>(towns.Select(t => t.Settlement));

            // 软预算:wage 进 holding 边费用,作守军的(唯一且很弱的)预算信号(§6.0)。
            // ⚠️ 单位约定:WagePerTroopAtMaxTier 返回 vanilla【日】工资,而 holding 边代表一个
            // tick(CapitalLogisticsTickHours,默认 6h)。value 常数同样按 per-tick
            // 标度调参 —— 故决定去留的 value > wage 比较两侧同标度、当前 tick 尺寸下自洽。
            // re-tune value 或 wage 时须保持两者同为 per-tick 口径,否则软预算比例失衡。
            // budget / BudgetTroopCap 走 clanWageBudget/wagePerTroop,两侧均日单位,不受影响。
            int wagePerTroop = Math.Max(1, GarrisonAllocationSolver.WagePerTroopAtMaxTier(manager!, towns));
            long clanWageBudget = GarrisonAllocationSolver.ClanWageBudget(manager!, towns, cfg, wagePerTroop);

            // PR-5'(2026-05-24): starvation penalty branch (IsClanAtWar + clan.Gold) removed.
            // Single-segment TierDefs with TargetFraction replaces floor/core/surplus; surplus is gone.

            var graph = new MinCostFlow();
            var edgeCat = new Dictionary<(int, int), EdgeCat>();
            // Tier 字段仅 HonorGuardRecruiter decode 用（per-bucket 兵种 tier）；其他 kind 一律 0。
            var decodeInfo = new Dictionary<(int, int),
                (DecodeKind Kind, Settlement A, Settlement B, GenericTroopRole Role, int Tier)>();
            int edgeCount = 0;
            void AddE(int from, int to, int cap, EdgeCost ec, EdgeCat cat)
            {
                if (cap <= 0) return;
                int cost = EdgeCostCompose.ToFlowCost(ec, cfg);
                graph.AddEdge(from, to, cap, cost);
                edgeCat[(from, to)] = cat;
                edgeCount++;
            }

            // ── 节点(懒分配)──
            // Phase 1 schema 扩展:节点 key 加 vanilla tier 维度(GenericTroopMatcher.GetTierBucket 口径,∈ [1,6])。
            // 为 Phase 2 cap 重设计 + upgrade edge 铺路。所有现有 G() 调用方暂未传 tier → 默认 tier=1。
            // 真正按 tier 分流在下一轮:TierShareWithinRole 拆 cap + 各 edge 显式传 tier(audits/2026-05-24-phase1-node-encoding.md)。
            int next = 1;
            int superSource = next++, superSink = next++;
            var gNode = new Dictionary<(Settlement, GenericTroopRole, int Tier, int Tau), int>();
            int G(Settlement s, GenericTroopRole r, int tau, int tier = 1)
            {
                var key = (s, r, tier, tau);
                if (!gNode.TryGetValue(key, out var n)) { n = next++; gNode[key] = n; }
                return n;
            }
            var transitNode = new Dictionary<(GenericTroopRole Role, int Tier, int Tau), int>();
            int Transit(GenericTroopRole r, int tier, int tau)
            {
                var key = (r, tier, tau);
                if (!transitNode.TryGetValue(key, out var n)) { n = next++; transitNode[key] = n; }
                return n;
            }
            var disbandGate = new Dictionary<(Settlement, int), int>();

            const int BigCap = 1_000_000;
            int disbandPerTickCap = Math.Max(0, (int)Math.Round(cfg.DisbandPerDayCap * tickHours / 24.0));
            int overflowPenalty = Math.Max(1, cfg.BypassOverflowPenalty);

            int originSupply = 0;

            // ── 初始驻军:superSource → G[S,role,tier,0] ──
            // Phase 1 改造 6(2026-05-24):删 RolesOf/SingleInf,所有 town 一律按真实 (role, tier) 入图。
            // 原 hardcode "非首府 Infantry" 与改造 3 修过的 in-flight bug 同语义,一并修。
            // 入图按 (role, tier) 桶扫 roster,与 CollectInFlightArrivals 同一口径。
            foreach (var t in towns)
            {
                var s = t.Settlement;
                var roster = t.GarrisonParty?.MemberRoster;
                if (roster == null) continue;
                var bucketHeads = new Dictionary<(GenericTroopRole Role, int Tier), int>();
                foreach (var elem in roster.GetTroopRoster())
                {
                    var ch = elem.Character;
                    if (ch == null || ch.IsHero) continue;
                    var role = GenericTroopMatcher.GetRole(ch);
                    if (role == GenericTroopRole.Unknown) continue;
                    int tier = GenericTroopMatcher.GetTierBucket(ch);
                    var key = (role, tier);
                    bucketHeads[key] = (bucketHeads.TryGetValue(key, out var c) ? c : 0) + elem.Number;
                }
                foreach (var kv in bucketHeads)
                {
                    if (kv.Value <= 0) continue;
                    AddE(superSource, G(s, kv.Key.Role, 0, kv.Key.Tier), kv.Value, EdgeCost.Zero, EdgeCat.Internal);
                    originSupply += kv.Value;
                }
            }

            // ── 在飞兵作为到达 supply:superSource → G[dest, role, tier, arrivalτ] ──
            // Phase 1 修复(2026-05-24):按真实 (role, tier) 入图,不再 hardcode Infantry。
            // 改造 6 已删 SingleInf/RolesOf,所有 town 平等承接全 role 维度。
            foreach (var (dest, role, tier, heads, arrivalTau) in CollectInFlightArrivals(clan, tickHours, T))
            {
                if (dest == null || !townSet.Contains(dest)) continue;
                AddE(superSource, G(dest, role, arrivalTau, tier), heads, new EdgeCost(timeUnits: arrivalTau), EdgeCat.Internal);
                originSupply += heads;
            }

            // ── holding 边 + 时域出口 + 遣散边(每城)──
            // Phase 1 改造 6(2026-05-24):所有 town 一律按 MatchPolicy.Roles 全 role 维度建图。
            // 非首府 hardcode Infantry 折叠分支已删,与改造 3 in-flight 修复保持一致。
            // 改造 2(下一轮)将引入 TierShareWithinRole 按 (role, tier) 真正拆 cap;
            // 当前 tier 维度仍走 default tier=1(G() 默认参数),tier 节点为单层。
            int demandTierCapL0 = 0;
            foreach (var t in towns)
            {
                var s = t.Settlement;
                bool isCapital = s == capitalSettlement;
                bool besieged = s.IsUnderSiege;
                bool protectedCity = besieged || IsProtectedFromDisband(s, cfg);
                var rule = ConfigurationManager.GetRuleFor(t);

                for (int tau = 0; tau < T; tau++)
                {
                    // holding 边:G[S,role,tier,τ]→G[S,role,tier,τ+1],按 (role × tier) 拆 cap × value 阶梯。
                    // 改造 2(2026-05-24):TierShareWithinRole 把 role 内的总 cap 进一步分到具体 tier。
                    foreach (var (cap, value) in TierDefs(t, s, isCapital, tau, cfg, forecast))
                    {
                        if (cap <= 0) continue;
                        foreach (var role in MatchPolicy.Roles)
                        {
                            int roleCap = MatchPolicy.DesiredCount(rule, role, cap);
                            if (roleCap <= 0) continue;
                            for (int tier = 1; tier <= 6; tier++)
                            {
                                // PR-5'(2026-05-24): TierShareWithinRole deleted. Uniform tier split = 1/6.
                                float tierShare = 1f / 6f;
                                int subCap = (int)Math.Round(roleCap * tierShare);
                                if (subCap <= 0) continue;
                                // Phase 3(2026-05-24):wage 按 vanilla tier 表,value 乘 power(tier) 让高 tier 兵 cost 更负。
                                // PR-1 (2026-05-24): wage 进 gold 通道；K 进 time 通道；value 进 strategic 通道。
                                // ClampValue(value × power, K) 保留原语义（防 strategic 溢出 ±K 区间触发符号反转）。
                                int holdGold = WageOf(tier);
                                int holdStrategic = ClampValue(value * PowerOf(tier), K);
                                var holdEc = new EdgeCost(gold: holdGold, timeUnits: 1, strategic: holdStrategic);
                                int from = G(s, role, tau, tier), to = G(s, role, tau + 1, tier);
                                AddE(from, to, subCap, holdEc, EdgeCat.Stay);
                                if (tau == 0)
                                {
                                    decodeInfo[(from, to)] = (DecodeKind.HoldingL0, s, null!, role, 0);
                                    demandTierCapL0 += subCap;
                                }
                            }
                        }
                    }

                    // 遣散边:G[S,role,tier,τ]→disbandGate[S,τ],K 分量 (T−τ)·K。保护城正常段 cap=0。
                    if (!disbandGate.TryGetValue((s, tau), out var gate))
                    {
                        gate = next++;
                        disbandGate[(s, tau)] = gate;
                        AddE(gate, superSink, protectedCity ? 0 : disbandPerTickCap, EdgeCost.Zero, EdgeCat.Bypass);
                        AddE(gate, superSink, BigCap, new EdgeCost(gold: overflowPenalty), EdgeCat.Bypass);
                    }
                    foreach (var role in MatchPolicy.Roles)
                    {
                        for (int tier = 1; tier <= 6; tier++)
                        {
                            int from = G(s, role, tau, tier);
                            // G→gate 标 Internal(非 Bypass):遣散是 G→gate→superSink 两跳,
                            // 若两段都记 Bypass,BypassFlow 会把同一批遣散兵计两次。只让
                            // gate→superSink 段计入 Bypass 统计,每个遣散兵恰好计一次。
                            AddE(from, gate, BigCap, new EdgeCost(timeUnits: T - tau), EdgeCat.Internal);
                            if (tau == 0) decodeInfo[(from, gate)] = (DecodeKind.Disband, s, null!, role, 0);
                        }
                    }
                }

                // 时域出口:G[S,role,tier,T]→superSink(每 (role × tier) 各一条)。
                foreach (var role in MatchPolicy.Roles)
                    for (int tier = 1; tier <= 6; tier++)
                        AddE(G(s, role, T, tier), superSink, BigCap, EdgeCost.Zero, EdgeCat.Internal);
            }

            // ── 调拨边:现有驻军跨城 G[S,R,τ]→G[S',R',τ+d] ──
            if (allowTransfers)
            {
                foreach (var ts in towns)
                {
                    var s = ts.Settlement;
                    if (s.IsUnderSiege) continue;            // 围城中的城不外调
                    foreach (var td in towns)
                    {
                        var s2 = td.Settlement;
                        if (s2 == s || s2.IsUnderSiege) continue;   // 围城中的城不接收
                        int d = EtaTicks(RoutingDistance(s, s2), tickHours);
                        // PR-1 (2026-05-24): 拆 3 通道——time(d) + gold(overhead) + pathRisk(scanner)。
                        // 默认 cfg 合成 = d·K + overhead + risk（与旧 routing 公式逐字节相等）。
                        int txOverhead = Math.Max(0, thresholds.McmfTransferOverhead);
                        int txRisk = RouteRiskSurcharge(s, s2, cfg);
                        var txEc = new EdgeCost(gold: txOverhead, timeUnits: d, pathRisk: txRisk);
                        // Phase 1 改造 6:删 RolesOf,所有 (src, dst) 在 4 role × 6 tier 维度独立守恒。
                        foreach (var role in MatchPolicy.Roles)
                        {
                            for (int tier = 1; tier <= 6; tier++)
                            {
                                for (int tau = 0; tau + d <= T - 1; tau++)
                                {
                                    int from = G(s, role, tau, tier), to = G(s2, role, tau + d, tier);
                                    AddE(from, to, BigCap, txEc, EdgeCat.Transfer);
                                    if (tau == 0) decodeInfo[(from, to)] = (DecodeKind.Transfer, s, s2, role, 0);
                                }
                            }
                        }
                    }
                }
            }

            // ── 卫队（Honor Guard）需求预处理 ──
            // 模板按 troopId 聚合到 (role, tier) 桶。bucketDemand 减去已在卫队池中 / 在飞征兵队的同 troopId 数。
            // 总额受 HonorGuardCap 约束（hgCapitalSink → superSink 容量）。
            // 注意：HG demand 只在 allowRecruitment 路径下被消费（只有 village recruit 边能流到 HG sink）；
            //       不抑制 garrison 招募 —— bucket.Count 是上限，HG 与 garrison 竞争同一供给。
            int hgCapitalSink = -1;
            var hgBucketSink = new Dictionary<(GenericTroopRole Role, int Tier), int>();
            var hgAllowedTroopIds = new Dictionary<(GenericTroopRole Role, int Tier), HashSet<string>>();
            int hgValueBase = Math.Max(0, cfg.HonorGuardValueBase);
            int hgGlobalRoom = 0;

            if (cfg.HonorGuardCap > 0 && cfg.HonorGuardTemplate != null && cfg.HonorGuardTemplate.Count > 0
                && allowRecruitment && !capitalSettlement.IsUnderSiege)
            {
                var hgPool = SovereignTowns.Capital.HonorGuardManager.GetPoolStatic(capitalSettlement);

                int hgInPoolTotal  = hgPool?.MemberRoster?.TotalManCount ?? 0;
                int hgInFlightTotal = CountHonorGuardInFlight(capitalSettlement, troopIdFilter: null);
                hgGlobalRoom = Math.Max(0, cfg.HonorGuardCap - hgInPoolTotal - hgInFlightTotal);

                if (hgGlobalRoom > 0)
                {
                    // 把模板按 (role, tier) 聚合：bucketTarget = Σ template[troopId].count；
                    // bucketDeficit = max(0, bucketTarget − inPool − inFlight)（per-troopId 减再求和）。
                    var bucketTarget  = new Dictionary<(GenericTroopRole, int), int>();
                    var bucketDeficit = new Dictionary<(GenericTroopRole, int), int>();
                    foreach (var kv in cfg.HonorGuardTemplate)
                    {
                        if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                        var co = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<CharacterObject>(kv.Key);
                        if (co == null || co.IsHero) continue;
                        var role = GenericTroopMatcher.GetRole(co);
                        if (role == GenericTroopRole.Unknown) continue;
                        int tier = Math.Max(1, Math.Min(6, GenericTroopMatcher.GetTierBucket(co)));
                        var bk = (role, tier);

                        bucketTarget[bk] = (bucketTarget.TryGetValue(bk, out var bt) ? bt : 0) + kv.Value;

                        int inPoolThisTroop    = CountTroopInRoster(hgPool?.MemberRoster, kv.Key);
                        int inFlightThisTroop  = CountHonorGuardInFlight(capitalSettlement, troopIdFilter: kv.Key);
                        int thisDeficit        = Math.Max(0, kv.Value - inPoolThisTroop - inFlightThisTroop);
                        bucketDeficit[bk] = (bucketDeficit.TryGetValue(bk, out var bd) ? bd : 0) + thisDeficit;

                        if (!hgAllowedTroopIds.TryGetValue(bk, out var idSet))
                        {
                            idSet = new HashSet<string>();
                            hgAllowedTroopIds[bk] = idSet;
                        }
                        idSet.Add(kv.Key);
                    }

                    if (bucketDeficit.Values.Sum() > 0)
                    {
                        // 建首府 HG sink：限总额 = hgGlobalRoom。
                        hgCapitalSink = next++;
                        AddE(hgCapitalSink, superSink, hgGlobalRoom, EdgeCost.Zero, EdgeCat.Internal);

                        // 每 bucket 一个 sink：限 bucket 总额 = bucketDeficit。
                        foreach (var kv in bucketDeficit)
                        {
                            int cap = kv.Value;
                            if (cap <= 0) continue;
                            int bucketSink = next++;
                            AddE(bucketSink, hgCapitalSink, cap, EdgeCost.Zero, EdgeCat.Internal);
                            hgBucketSink[kv.Key] = bucketSink;
                        }
                    }
                }
            }

            // ── 招募(围城中的首府不招募)──
            long recruitStart = sw.ElapsedMilliseconds;
            if (allowRecruitment && !capitalSettlement.IsUnderSiege)
            {
                var capitalRule = ConfigurationManager.GetRuleFor(capitalTown) ?? TownGarrisonRule.CreateDefault();
                string? requiredCultureId = GenericTroopMatcher.ResolveRequiredCultureId(capitalRule, capitalTown);

                // InPlace:首府 notable 志愿兵。RecOrigin[InPlace,R] 单池。
                foreach (var bucket in RecruitmentTopology.BucketizeCharacters(
                             RecruitmentTopology.EnumerateVolunteerTroops(capitalSettlement),
                             capitalRule,
                             requiredCultureId))
                {
                    if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) continue;
                    int origin = next++;
                    AddE(superSource, origin, bucket.Count, EdgeCost.Zero, EdgeCat.Internal);
                    originSupply += bucket.Count;
                    int recTier = Math.Max(1, Math.Min(6, bucket.MinTier));
                    // PR-2 G4 (2026-05-24): 招新兵的真金币 cost 入 gold 通道。
                    // vanilla DefaultPartyWageModel.GetTroopRecruitmentCost 公式：T1=20/T2=50/T3=100/T4=200/T5=400/T6=600。
                    int inPlaceRecruitCost = RecruitCostByTier(recTier);
                    for (int tau = 0; tau <= T - 1; tau++)
                    {
                        int to = Transit(bucket.Role, recTier, tau);
                        AddE(origin, to, bucket.Count, new EdgeCost(gold: inPlaceRecruitCost, timeUnits: tau), EdgeCat.Recruit);
                        if (tau == 0) decodeInfo[(origin, to)] = (DecodeKind.InPlace, capitalSettlement, null!, bucket.Role, 0);
                    }
                    AddE(origin, superSink, bucket.Count, new EdgeCost(timeUnits: T), EdgeCat.Bypass);  // 未招募出口
                }

                // 候选村:RecOrigin[V,R] 单池;在飞边落 Transit[R, tier, τ_a],τ_a∈[ETA_V,T-1]。
                var inFlightVillages = RecruitmentTopology.CollectInFlightRecruiterVillages(clan);
                foreach (var village in RecruitmentTopology.EnumerateRecruitmentVillages(capitalTown, clan, inFlightVillages))
                {
                    int etaV = EtaTicks(2 * RoutingDistance(village, capitalSettlement), tickHours);  // 往返
                    if (etaV > T - 1) continue;
                    int recOverhead = Math.Max(0, thresholds.McmfRecruiterOverhead);
                    int recRisk = RouteRiskSurcharge(village, capitalSettlement, cfg);
                    foreach (var bucket in RecruitmentTopology.BucketizeCharacters(
                                 RecruitmentTopology.EnumerateVolunteerTroops(village),
                                 capitalRule,
                                 requiredCultureId))
                    {
                        if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) continue;
                        int origin = next++;
                        AddE(superSource, origin, bucket.Count, EdgeCost.Zero, EdgeCat.Internal);
                        originSupply += bucket.Count;
                        int recTier = Math.Max(1, Math.Min(6, bucket.MinTier));
                        // PR-2 G4 (2026-05-24): 招新兵的真金币 cost 入 gold 通道（叠加 routing overhead）。
                        // vanilla DefaultPartyWageModel.GetTroopRecruitmentCost。
                        int villageRecruitCost = RecruitCostByTier(recTier);
                        int recGold = recOverhead + villageRecruitCost;
                        for (int arrival = etaV; arrival <= T - 1; arrival++)
                        {
                            int to = Transit(bucket.Role, recTier, arrival);
                            // 旧公式 cost = arrival·K + recOverhead + recRisk
                            // PR-2 G4: gold 通道现含 recruitCost + routing overhead
                            var recEc = new EdgeCost(gold: recGold, timeUnits: arrival, pathRisk: recRisk);
                            AddE(origin, to, bucket.Count, recEc, EdgeCat.Recruit);
                            if (arrival == etaV)  // dispatch tick = arrival − etaV == 0
                                decodeInfo[(origin, to)] = (DecodeKind.Recruiter, village, null!, bucket.Role, 0);
                        }

                        // 卫队 edge：仅当此 bucket 在卫队 demand 中、且该村供给含模板许可的 troopId。
                        // 容量 = 该村该 bucket 中匹配模板 troopId 的志愿兵数（≤ bucket.Count）。
                        // 与 garrison 招募同源（origin 总流出 ≤ bucket.Count），MCMF 自然不会双计。
                        //
                        // cost 平衡（与 patrol/sally edge 同口径）：
                        //   timeUnits=T → K 项 = T·K，与 garrison 全程累计同；MCMF 比较时落到 strategic 差异
                        //   strategic = hgValue × power(tier)，与 garrison 累计 holdStrategic·(T−etaV) 比较
                        //   默认配置 (HonorGuardValueBase=2500) 比 garrison 累计 strat (~3k/tick · 12 tick = 34k) 小
                        //   → MCMF 优先填 garrison，仅在 garrison cap 耗尽 / surplus 时才走 HG。
                        //   提高 HonorGuardValueBase 即可让 HG 优先（in-game 调）。
                        var hgKey = (bucket.Role, recTier);
                        if (hgBucketSink.TryGetValue(hgKey, out int hgSinkNode)
                            && hgAllowedTroopIds.TryGetValue(hgKey, out var allowedIds))
                        {
                            int hgVillageSupply = CountVillageVolunteersInTemplate(village, allowedIds);
                            int hgEdgeCap = Math.Min(bucket.Count, hgVillageSupply);
                            if (hgEdgeCap > 0)
                            {
                                int hgStrategic = ClampValue(hgValueBase * PowerOf(recTier), K);
                                var hgEc = new EdgeCost(gold: recGold, timeUnits: T, pathRisk: recRisk, strategic: hgStrategic);
                                AddE(origin, hgSinkNode, hgEdgeCap, hgEc, EdgeCat.Recruit);
                                decodeInfo[(origin, hgSinkNode)] =
                                    (DecodeKind.HonorGuardRecruiter, village, capitalSettlement, bucket.Role, recTier);
                            }
                        }

                        AddE(origin, superSink, bucket.Count, new EdgeCost(timeUnits: T), EdgeCat.Bypass);  // 未招募出口
                    }
                }
            }
            recruitMs = sw.ElapsedMilliseconds - recruitStart;

            // ── transit 出边:招募兵留首府 / 转发分支 ──
            // Phase 1 改造 4(2026-05-24):transit 节点带 tier 维度,转发也按 tier 守恒,无 Infantry hardcode。
            foreach (var kv in transitNode.ToList())
            {
                var (role, tier, tau) = kv.Key;
                int tn = kv.Value;
                // 留首府:Transit[R, tier, τ] → G[capital, R, tier, τ]。
                AddE(tn, G(capitalSettlement, role, tau, tier), BigCap, EdgeCost.Zero, EdgeCat.Internal);
                // 转发分支:Transit[R, tier, τ] → G[branch, R, tier, τ+d]。
                if (!allowTransfers) continue;
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == capitalSettlement || s.IsUnderSiege) continue;
                    int d = EtaTicks(RoutingDistance(capitalSettlement, s), tickHours);
                    if (tau + d > T - 1) continue;
                    int fwdOverhead = Math.Max(0, thresholds.McmfTransferOverhead);
                    int fwdRisk = RouteRiskSurcharge(capitalSettlement, s, cfg);
                    int to = G(s, role, tau + d, tier);
                    AddE(tn, to, BigCap, new EdgeCost(gold: fwdOverhead, timeUnits: d, pathRisk: fwdRisk), EdgeCat.Transfer);
                    if (tau == 0) decodeInfo[(tn, to)] = (DecodeKind.Transfer, capitalSettlement, s, role, 0);
                }
            }

            // ── upgrade edge:G[s,role,tier,τ] → G[s,role,tier+1,τ+xpTicks] ──
            // 改造 5(2026-05-24):MCMF 自主决定按模板升级。低 tier 节点 holding cap=0(用户模板没列)
            // 时,流量被迫走 upgrade edge 流向高 tier;高 tier 节点 holding cap 大 + value 高时,
            // upgrade 的 gold cost 被未来 holding 收益抵消。
            //
            // Phase 5 完整接入(2026-05-24):cost 用 vanilla tier-aware 公式,反编译核实自
            // DefaultPartyTroopUpgradeModel.GetGoldCostForUpgrade / GetXpCostForUpgrade。
            //   - goldCost(t→t+1) = (recruitCost(t+1) − recruitCost(t)) / 2
            //   - xpCost(t→t+1) = vanilla 阶梯 {100,300,550,900,1300,1700}
            //   - xpTicks = ceil(xpCost / xpPerTick),xpPerTick 按建筑等级估算
            // 真实升级由 vanilla PartyUpgraderCampaignBehavior 接管(MapEventEnded/DailyTick auto-upgrade),
            // MCMF 这里负责把升级显式计价 —— PR-2 T5 起 edge 同时携带 gold 与 timeUnits=xpTicks,
            // 让升级与"招高 tier / 留同 tier 等 xpTicks tick"在同尺度上比较。
            // 注：默认 cfg (T=16, upgradeXpPerTick=12) 下 xpTicks ≥ T 的 tier 跳跃不会被 loop 守卫
            // (`tau + xpTicks < T`) 放进图 —— 当前仅 T1→T2 (xpTicks=9) 真正建边;T≥2 升级要靠
            // 调大 HorizonTicks 或 upgradeXpPerTick 才能进图,与 PR-2 cost 改动无关。
            int upgradeXpPerTick = Math.Max(1, 50 * tickHours / 24);  // 估算:basic injection + barracks + daily bonus
            foreach (var tUpg in towns)
            {
                var sUpg = tUpg.Settlement;
                foreach (var role in MatchPolicy.Roles)
                {
                    for (int tier = 1; tier < 6; tier++)
                    {
                        int goldCost = UpgradeGoldByTier(tier, tier + 1);
                        int xpCost = UpgradeXpByTier(tier, tier + 1);
                        int xpTicks = Math.Max(1, (xpCost + upgradeXpPerTick - 1) / upgradeXpPerTick);
                        for (int tau = 0; tau + xpTicks < T; tau++)
                        {
                            int from = G(sUpg, role, tau, tier);
                            int to = G(sUpg, role, tau + xpTicks, tier + 1);
                            // PR-2 T5 (2026-05-24): timeUnits = xpTicks 显式化升级的时间机会成本。
                            // 节点跨层延迟仅保 SSP 拓扑正确性；cost 必须显式承担 K · xpTicks
                            // 才能让 MCMF 把"升级 vs 招高 tier vs 调拨"放在同尺度比较。
                            // 升级总 cost = goldCost (vanilla 公式) + K · xpTicks（时间机会成本）。
                            AddE(from, to, BigCap, new EdgeCost(gold: goldCost, timeUnits: xpTicks), EdgeCat.Internal);
                        }
                    }
                }
            }

            // ── 巡逻 sink:首府盈余兵去巡逻(receding-horizon,decode 只取 τ=0)──
            // patrol 是 disband 的同构兄弟:从 garrison 流提前退出、代理退出后的 (T−τ) 个 tick。
            // 边费用 = (T−τ)·K − patrolValue;patrolValue>0 → 胜过 disband(真实费用 0)与 surplus
            // 留守(真实费用为正),但远小于 core 留守的累计 value → core 守军不被抽走。
            // patrolHeadroom = (maxPatrols 余量 × PatrolTargetSize),由 CapitalLogisticsManager 传入。
            if (patrolHeadroom > 0 && !capitalSettlement.IsUnderSiege)
            {
                int patrolValue = Math.Max(0, Math.Min(K - 1, cfg.PatrolValue));
                int patrolSink = next++;
                AddE(patrolSink, superSink, patrolHeadroom, EdgeCost.Zero, EdgeCat.Internal);
                for (int tau = 0; tau < T; tau++)
                {
                    foreach (var role in MatchPolicy.Roles)
                    {
                        for (int tier = 1; tier <= 6; tier++)
                        {
                            int from = G(capitalSettlement, role, tau, tier);
                            // Phase 3:patrol 收益按 tier power 加权(高 tier 巡逻战力更强)。
                            // PR-1: (T-τ)·K → timeUnits 通道，patrolValue·power → strategic 通道（取负后等效）。
                            int tierPatrolValue = (int)Math.Round(patrolValue * PowerOf(tier));
                            var patrolEc = new EdgeCost(timeUnits: T - tau, strategic: tierPatrolValue);
                            AddE(from, patrolSink, BigCap, patrolEc, EdgeCat.Patrol);
                            if (tau == 0)
                                decodeInfo[(from, patrolSink)] = (DecodeKind.Patrol, capitalSettlement, null!, role, 0);
                        }
                    }
                }
            }

            // ── Sally sink:per-settlement 盈余兵出击(receding-horizon,decode 只取 τ=0)──
            // sally edge 与 patrol edge 同构（holding → sallySink[s] → superSink），但 sink 是
            // per-settlement 而非 capital 唯一，允许多座城/堡同时派 sally。
            // sallyHeadrooms[s] = SallyTeamSize × MaxSallyPartiesPerCity（由 CapitalLogisticsManager 传入）。
            if (sallyHeadrooms != null)
            {
                int sallyValue = Math.Max(0, cfg.SallyValueBase);
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == null) continue;
                    if (!sallyHeadrooms.TryGetValue(s, out int sallyHeadroom) || sallyHeadroom <= 0) continue;
                    if (s.IsUnderSiege) continue;
                    bool isCap = s == capitalSettlement;
                    float sallyBonus = isCap ? Math.Max(1f, cfg.CapitalSallyBonus) : 1.0f;
                    int sallySink = next++;
                    AddE(sallySink, superSink, sallyHeadroom, EdgeCost.Zero, EdgeCat.Internal);
                    for (int tau = 0; tau < T; tau++)
                    {
                        float threat = ThreatWeightOf(SafeThreatAt(forecast, s, tau), cfg);
                        float strat = StrategicWeight(s, isCap, cfg);
                        foreach (var role in MatchPolicy.Roles)
                        {
                            for (int tier = 1; tier <= 6; tier++)
                            {
                                int from = G(s, role, tau, tier);
                                int tierSallyValue = (int)Math.Round(sallyValue * sallyBonus * threat * strat * PowerOf(tier));
                                var sallyEc = new EdgeCost(timeUnits: T - tau, strategic: tierSallyValue);
                                AddE(from, sallySink, BigCap, sallyEc, EdgeCat.Sally);
                                if (tau == 0)
                                    decodeInfo[(from, sallySink)] = (DecodeKind.Sally, s, null!, role, 0);
                            }
                        }
                    }
                }
            }

            // ── Solve(分帧 SSP)── swSolve 只累计 MoveNext 内的 CPU,排除 yield 帧间隙。
            long buildMs = sw.ElapsedMilliseconds;

            // PR-5'(2026-05-24): MERGED-TIERS diagnostic removed (used AdequateFor + MinGarrisonFloor).
            // Replacement: single-segment TierDefs cap = hardCap × TargetFraction, logged below.

            var swSolve = new System.Diagnostics.Stopwatch();
            int sspFrames = 0;
            int sspYieldEvery = Math.Max(1, cfg.SspYieldEvery);
            var sspIt = graph.SolveStepwise(superSource, superSink, sspYieldEvery);
            while (true)
            {
                swSolve.Start();
                bool more = sspIt.MoveNext();
                swSolve.Stop();
                if (!more) break;
                sspFrames++;
                yield return null;
            }
            var flow = graph.LastResult;
            long solveMs = swSolve.ElapsedMilliseconds;
            long beforeDecode = sw.ElapsedMilliseconds;

            result.Ran = true;
            result.SettlementCount = towns.Count;
            result.NodeCount = next - 1;
            result.EdgeCount = edgeCount;
            result.OriginSupply = originSupply;
            result.Budget = clanWageBudget;
            result.BudgetTroopCap = (int)Math.Max(0L, Math.Min(int.MaxValue, clanWageBudget / Math.Max(1, wagePerTroop)));
            result.DemandTierCapacity = demandTierCapL0;
            result.TotalFlow = flow.TotalFlow;
            result.TotalCost = flow.TotalCost;

#if DEBUG
            if (graph.ReducedCostClampHits > 0)
                Logger.Warn($"MERGED-SOLVER reduced-cost clamp fired {graph.ReducedCostClampHits} times — K-balance bug");
#endif

            // ── decode:只取 layer-0 边 → Target / 指令 / disband ──
            // ⚠️ 依赖:同一 (S,R,τ)→(S,R,τ+1) 的 holding 边按 tier(floor/core_k/surplus)拆成
            // 多条并行边。edgeCat / decodeInfo 以 (from,to) 为键 → 多 tier 同键(同类别、同语义,
            // last-write 无害);flow.EdgeFlows 也按 (from,to) 聚合并行边流量 → HoldingL0 decode
            // 读到的是该 tick 该 (S,R) 守军总数。若 MinCostFlow.Solve 改为按边身份记流量,此处会
            // 静默漏计 —— 改 MinCostFlow 前先看这里。
            foreach (var t in towns) result.Target[t.Settlement] = 0;
            var transfers = new Dictionary<(Settlement Src, Settlement Dst, GenericTroopRole Role), int>();
            int patrolHeads = 0;
            var sallyHeadByCity = new Dictionary<Settlement, int>();

            foreach (var kv in flow.EdgeFlows)
            {
                int f = kv.Value;
                if (f <= 0) continue;

                if (edgeCat.TryGetValue(kv.Key, out var cat))
                {
                    switch (cat)
                    {
                        case EdgeCat.Stay: result.StayFlow += f; break;
                        case EdgeCat.Transfer: result.TransferFlow += f; break;
                        case EdgeCat.Recruit: result.RecruitFlow += f; break;
                        case EdgeCat.Bypass: result.BypassFlow += f; break;
                        case EdgeCat.Patrol: result.PatrolFlow += f; break;
                        case EdgeCat.Sally: result.SallyFlow += f; break;
                    }
                }

                if (!decodeInfo.TryGetValue(kv.Key, out var di)) continue;
                switch (di.Kind)
                {
                    case DecodeKind.HoldingL0:
                        result.Target[di.A] = (result.Target.TryGetValue(di.A, out var tg) ? tg : 0) + f;
                        break;
                    case DecodeKind.Disband:
                        result.Disband[di.A] = (result.Disband.TryGetValue(di.A, out var dd) ? dd : 0) + f;
                        break;
                    case DecodeKind.Recruiter:
                        result.Instructions.Add(new RecruiterPartyInstruction(
                            capitalTown, capitalSettlement, di.A, di.Role, f));
                        break;
                    case DecodeKind.HonorGuardRecruiter:
                        // 仅在 cap=1 守护下接第一条（执行层逻辑），但 decode 阶段全部产出。
                        // candidateTroopIds 取自该 bucket 的模板允许集，执行层据此挑实际 troopId。
                        if (hgAllowedTroopIds.TryGetValue((di.Role, di.Tier), out var hgIds))
                        {
                            result.Instructions.Add(new HonorGuardRecruiterInstruction(
                                capital: di.B, targetVillage: di.A,
                                role: di.Role, tier: di.Tier, count: f,
                                candidateTroopIds: new List<string>(hgIds)));
                        }
                        break;
                    case DecodeKind.InPlace:
                        result.Instructions.Add(new InPlaceRecruitInstruction(capitalSettlement, di.Role, f));
                        break;
                    case DecodeKind.Transfer:
                        var key = (di.A, di.B, di.Role);
                        transfers[key] = (transfers.TryGetValue(key, out var c) ? c : 0) + f;
                        break;
                    case DecodeKind.Patrol:
                        patrolHeads += f;
                        break;
                    case DecodeKind.Sally:
                        sallyHeadByCity[di.A] = (sallyHeadByCity.TryGetValue(di.A, out var sh) ? sh : 0) + f;
                        break;
                }
            }

            result.DemandFilled = result.Target.Values.Sum();
            foreach (var kv in transfers)
                result.Instructions.Add(new TransferPartyInstruction(
                    kv.Key.Src, kv.Key.Dst, kv.Key.Role, kv.Value));
            if (patrolHeads > 0)
                result.Instructions.Add(new PatrolInstruction(capitalSettlement, patrolHeads));
            foreach (var kv in sallyHeadByCity)
                if (kv.Value > 0)
                    result.Instructions.Add(new SallyInstruction(kv.Key, kv.Value));

            long decodeMs = sw.ElapsedMilliseconds - beforeDecode;
            Logger.Info(
                $"MERGED-TIMING clan={clan.StringId} T={T} nodes={result.NodeCount} edges={result.EdgeCount} "
              + $"build={buildMs}ms (recruit={recruitMs}ms) solve={solveMs}ms (sspFrames={sspFrames}) "
              + $"decode={decodeMs}ms wall={sw.ElapsedMilliseconds}ms");

            // PR-5'(2026-05-24): MERGED-GAP diagnostic removed (used AdequateFor + MinGarrisonFloor).
        }
        finally
        {
            onResult?.Invoke(result);
        }
    }

    /// <summary>
    /// PR-5'(2026-05-24): 单段 cap = hardCap × TargetFraction，value = BaseValuePerTier × threat × strat。
    /// 删除了 floor/core/surplus 三段逻辑、CoreTierCount、CoreDimRange、CoreDimMidpoint、
    /// ValueFloorBase、ValueCoreBase、SurplusEdgeCost、AdequateFor 依赖。
    /// </summary>
    private static List<(int Cap, float Value)> TierDefs(
        Town t, Settlement s, bool isCapital, int tau,
        FiscalAutonomyConfig cfg, IHorizonForecast forecast)
    {
        int hardCap = GarrisonAllocationSolver.HardCapFor(t, cfg);
        int totalCap = (int)Math.Round(hardCap * Math.Max(0f, Math.Min(1f, cfg.TargetFraction)));
        float threat = ThreatWeightOf(SafeThreatAt(forecast, s, tau), cfg);
        float strat = StrategicWeight(s, isCapital, cfg);
        float value = cfg.BaseValuePerTier * threat * strat;
        return new List<(int, float)> { (totalCap, value) };
    }

    // PR-5'(2026-05-24): TierShareWithinRole deleted. Uniform tier split = 1/6 used directly at call site.

    private static RiskLevel SafeThreatAt(IHorizonForecast forecast, Settlement s, int tau)
    {
        try { return forecast.ThreatAt(s, tau); }
        catch (Exception ex)
        {
            Logger.Error($"UnifiedGarrisonSolver: forecast.ThreatAt failed for '{s?.StringId}' tick={tau}", ex);
            return RiskLevel.Low;
        }
    }

    /// <summary>
    /// Vanilla wage 表(denars/day per troop),反编译核实自 DefaultPartyWageModel.GetCharacterWage(2026-05-24)。
    /// 直接走公式,等价于 PartyWageModel.GetCharacterWage 对该 tier soldier(非 merc),避免反查 stub troop。
    /// </summary>
    private static int WageOf(int tier) => tier switch
    {
        <= 0 => 1,
        1 => 2,
        2 => 3,
        3 => 5,
        4 => 8,
        5 => 12,
        6 => 17,
        _ => 23,
    };

    /// <summary>
    /// Vanilla recruit cost 阶梯(反编译核实自 DefaultPartyWageModel.GetTroopRecruitmentCost,
    /// withoutItemCost=true)。tier→cost: T0=10/T1=20/T2=50/T3=100/T4=200/T5=400/T6=600/T7+=1000。
    /// </summary>
    private static int RecruitCostByTier(int tier) => tier switch
    {
        <= 0 => 10,
        1 => 20,
        2 => 50,
        3 => 100,
        4 => 200,
        5 => 400,
        6 => 600,
        _ => 1000,
    };

    /// <summary>
    /// Vanilla upgrade gold cost 公式:`(recruitCost(tgt) − recruitCost(src)) / 2`(非 merc)。
    /// 反编译核实自 DefaultPartyTroopUpgradeModel.GetGoldCostForUpgrade(2026-05-24)。
    /// </summary>
    private static int UpgradeGoldByTier(int fromTier, int toTier)
    {
        int diff = RecruitCostByTier(toTier) - RecruitCostByTier(fromTier);
        return Math.Max(1, diff / 2);
    }

    /// <summary>
    /// Vanilla upgrade XP cost(累加 target.Tier 段固定值):T1=100/T2=300/T3=550/T4=900/T5=1300/T6=1700/T7=2100。
    /// 反编译核实自 DefaultPartyTroopUpgradeModel.GetXpCostForUpgrade(2026-05-24)。
    /// </summary>
    private static int UpgradeXpByTier(int fromTier, int toTier)
    {
        int xp = 0;
        for (int i = fromTier + 1; i <= toTier; i++)
        {
            xp += i switch
            {
                <= 1 => 100,
                2 => 300,
                3 => 550,
                4 => 900,
                5 => 1300,
                6 => 1700,
                _ => 2100,
            };
        }
        return Math.Max(1, xp);
    }

    /// <summary>
    /// Vanilla power 公式 `(2+n)(10+n) × 0.02`,反编译核实自 DefaultMilitaryPowerModel.GetDefaultTroopPower。
    /// 不含 mounted×1.2 / hero×1.5 修饰(MCMF 节点是聚合 tier 桶,不区分具体 troop)。
    /// 返回 dimensionless 标量。T1=0.66 / T3=1.30 / T5=2.10 / T6=2.56。
    /// </summary>
    private static float PowerOf(int tier)
    {
        int n = Math.Max(0, Math.Min(7, tier));
        return (2f + n) * (10f + n) * 0.02f;
    }

    /// <summary>把 tier value(可负)round + clamp 到 [−K, K]。K 由调用方从 cfg 读取。</summary>
    private static int ClampValue(float value, int K)
    {
        int v = (int)Math.Round(value);
        if (v > K) v = K;
        if (v < -K) v = -K;
        return v;
    }

    /// <summary>threat 乘子:风险等级映射各 cfg.ThreatWeight* 值。
    /// 默认 Safe .5 / Low 1 / Med 1.5 / High 2 / Crit 3 —— 战时仍有明显涨兵但不爆炸;
    /// 和平期驻军规模靠 cfg.ValueCoreBase 定。</summary>
    private static float ThreatWeightOf(RiskLevel level, FiscalAutonomyConfig cfg)
    {
        switch (level)
        {
            case RiskLevel.Safe: return cfg.ThreatWeightSafe;
            case RiskLevel.Low: return cfg.ThreatWeightLow;
            case RiskLevel.Medium: return cfg.ThreatWeightMedium;
            case RiskLevel.High: return cfg.ThreatWeightHigh;
            case RiskLevel.Critical: return cfg.ThreatWeightCritical;
            default: return cfg.ThreatWeightLow;
        }
    }

    /// <summary>
    /// PR-5'(2026-05-24): strategic 乘子 = typeBonus × prosperity_factor。
    /// typeBonus: Town × TownStrategicBonus, isCapital additionally × CapitalStrategicBonus, Castle 1.0.
    /// prosperity_factor = clamp(Prosperity / ProsperityNormalizer, 0.5, 1.5).
    /// </summary>
    private static float StrategicWeight(Settlement s, bool isCapital, FiscalAutonomyConfig cfg)
    {
        float prosperity = 0f;
        try { if (s != null && s.IsTown && s.Town != null) prosperity = s.Town.Prosperity; }
        catch { prosperity = 0f; }

        float normalizer = cfg.ProsperityNormalizer > 0f ? cfg.ProsperityNormalizer : 4000f;
        float pf = Math.Max(0.5f, Math.Min(1.5f, prosperity / normalizer));

        float typeBonus = 1f;
        if (s != null && s.IsTown) typeBonus *= cfg.TownStrategicBonus;
        if (isCapital) typeBonus *= cfg.CapitalStrategicBonus;
        return typeBonus * pf;
    }

    /// <summary>
    /// PR-5'(2026-05-24): DisbandUnaffordableExcess 已删除。保护仅在围城 / 高危时触发。
    /// 正常遣散始终允许（由 solver 的 bypass 费用模型控制量）。
    /// </summary>
    private static bool IsProtectedFromDisband(Settlement s, FiscalAutonomyConfig cfg)
    {
        try
        {
            if (s.IsUnderSiege) return true;
            if (RiskAssessmentService.Assess(s).Level >= RiskLevel.High) return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"UnifiedGarrisonSolver.IsProtectedFromDisband failed for '{s?.StringId}'", ex);
        }
        return false;
    }

    /// <summary>
    /// Phase 3(2026-05-24):用 vanilla MapDistanceModel 寻路距离 cache 替换直线距离。
    /// O(1) 查表(反编译核实自 _navigationCache.GetSettlementToSettlementDistanceWithLandRatio)。
    /// Calradia 单一大陆下不可达哨兵 >= PossibleMaximumMapBoundary(1e8)实际不触发,留兜底。
    /// `_navigationCache` 在 OnGameStart 阶段为 null;SolveCoroutine 在 daily tick 跑,
    /// 必在 OnSessionLaunched 之后,无 NRE 风险。
    /// </summary>
    private static int RoutingDistance(Settlement a, Settlement b)
    {
        try
        {
            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MapDistanceModel;
            if (model != null && a != null && b != null)
            {
                float d = model.GetDistance(a, b, false, false,
                    TaleWorlds.CampaignSystem.Party.MobileParty.NavigationType.Default,
                    out _);
                if (d > 0f && d < TaleWorlds.CampaignSystem.ComponentInterfaces.MapDistanceModel.PossibleMaximumMapBoundary)
                    return (int)Math.Round(d);
            }
            return (int)Math.Round((double)(a!.GetPosition2D - b!.GetPosition2D).Length);
        }
        catch { return 1000; }
    }

    /// <summary>地图直线距离换算成 ETA(tick 数)。参考速度取 cfg.ReferenceSpeedPerDay,
    /// role-blind(§6.4)。最小 1。</summary>
    private static int EtaTicks(int distance, int tickHours)
    {
        float speedPerDay = ConfigurationManager.Current?.FiscalAutonomy?.ReferenceSpeedPerDay ?? 5.0f;
        if (speedPerDay <= 0f) speedPerDay = 5.0f;
        float perTick = Math.Max(0.1f, speedPerDay * tickHours / 24f);
        return Math.Max(1, (int)Math.Round(Math.Max(0, distance) / perTick));
    }

    /// <summary>
    /// D2:路线风险加进 routing 成本。a↔b 端点 + 连线中点处的最大敌对健康兵力
    /// × <see cref="FiscalAutonomyConfig.DispatchRiskCostScale"/>。`DispatchRiskEnabled` 关时返回 0。
    /// </summary>
    private static int RouteRiskSurcharge(Settlement a, Settlement b, FiscalAutonomyConfig cfg)
    {
        try
        {
            if (!cfg.DispatchRiskEnabled || a == null || b == null) return 0;
            var friendly = a.MapFaction ?? b.MapFaction;
            if (friendly == null) return 0;
            float radius = Math.Max(1f, cfg.DispatchRiskScanRadius);
            float worst = HostilePartyScanner.HostileStrengthNear(a.GetPosition2D, radius, friendly);
            worst = Math.Max(worst,
                HostilePartyScanner.HostileStrengthNear(b.GetPosition2D, radius, friendly));
            var mid = (a.GetPosition2D + b.GetPosition2D) * 0.5f;
            worst = Math.Max(worst,
                HostilePartyScanner.HostileStrengthNear(mid, radius, friendly));
            return (int)Math.Round(worst * Math.Max(0, cfg.DispatchRiskCostScale));
        }
        catch (Exception ex)
        {
            Logger.Error("UnifiedGarrisonSolver.RouteRiskSurcharge failed", ex);
            return 0;
        }
    }

    // 2026-05-24:ComputeManualTarget 已删除。手动模式整体下线,驻军目标完全由 AdequateFor
    // (公式)+ MCMF instruction(求解)决定,不再有用户手动入口。

    /// <summary>
    /// 本 clan 所有在飞 ST 队按目标定居点 + (role, tier) + ETA 汇总。Transfer 回程(目标==源)记到源城,
    /// 否则记目的城;Recruiter / Sally 记各自 home。
    ///
    /// Phase 1 修复(2026-05-24):原 role-blind 实现把所有在飞兵 hardcode 为 Infantry 入图,
    /// 在用户配 ExactTemplate=khuzait_kheshig+heavy_horse_archer(纯 HorseArcher role)时
    /// → 流量卡死在 G[首府,Infantry,*] 节点(holding 出边为 0)→ 推荐驻军=0。
    /// 现按 roster 扫描真实 (role, tier) 分桶,每个桶产生一条到达 supply 边。
    /// </summary>
    private static List<(Settlement Dest, GenericTroopRole Role, int Tier, int Heads, int ArrivalTau)>
        CollectInFlightArrivals(Clan clan, int tickHours, int horizon)
    {
        var list = new List<(Settlement, GenericTroopRole, int, int, int)>();
        try
        {
            var parties = MobileParty.AllCustomParties;
            if (parties == null) return list;
            foreach (var party in parties)
            {
                if (party == null || !party.IsActive) continue;
                if ((party.MemberRoster?.TotalManCount ?? 0) <= 0) continue;

                Settlement? dest = null;
                switch (party.PartyComponent)
                {
                    case StTransferPartyComponent transfer:
                        if (transfer.Source?.OwnerClan != clan && transfer.Destination?.OwnerClan != clan)
                            continue;
                        dest = (transfer.Source != null && party.TargetSettlement == transfer.Source)
                            ? transfer.Source : transfer.Destination;
                        break;
                    case StRecruiterPartyComponent recruiter:
                        if (recruiter.HomeSettlementOrNull?.OwnerClan != clan) continue;
                        dest = recruiter.HomeSettlementOrNull;
                        break;
                    case StSallyPartyComponent sally:
                        if (sally.HomeSettlementOrNull?.OwnerClan != clan) continue;
                        dest = sally.HomeSettlementOrNull;
                        break;
                }
                if (dest == null) continue;

                int dist;
                try { dist = (int)Math.Round((double)(party.GetPosition2D - dest.GetPosition2D).Length); }
                catch { dist = 1000; }
                int eta = EtaTicks(dist, tickHours);
                // 抵达晚于 horizon → 对 tick-0 决策零影响,不建到达边(避免 clamp 高估在飞量)。
                if (eta > horizon - 1) continue;

                // 按 (role, tier) 桶分拆 roster;hero / unknown role 跳过。
                var bucketHeads = new Dictionary<(GenericTroopRole Role, int Tier), int>();
                foreach (var elem in party.MemberRoster!.GetTroopRoster())
                {
                    var ch = elem.Character;
                    if (ch == null || ch.IsHero) continue;
                    var role = GenericTroopMatcher.GetRole(ch);
                    if (role == GenericTroopRole.Unknown) continue;
                    int tier = GenericTroopMatcher.GetTierBucket(ch);
                    var key = (role, tier);
                    bucketHeads[key] = (bucketHeads.TryGetValue(key, out var c) ? c : 0) + elem.Number;
                }
                foreach (var kv in bucketHeads)
                {
                    if (kv.Value <= 0) continue;
                    list.Add((dest, kv.Key.Role, kv.Key.Tier, kv.Value, Math.Max(0, eta)));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UnifiedGarrisonSolver.CollectInFlightArrivals failed", ex);
        }
        return list;
    }

    // ── 卫队（HonorGuard）helpers ──

    /// <summary>统计 <paramref name="capital"/> 的卫队池 / 在飞卫队征兵队中匹配 <paramref name="troopIdFilter"/> 的兵数。
    /// troopIdFilter=null → 全部 troopId 汇总（用于 hgGlobalRoom 计算）。
    /// CollectInFlightArrivals 的 switch 不命中 HonorGuardRecruiterPartyComponent，故那里不重复计数。</summary>
    private static int CountHonorGuardInFlight(Settlement capital, string? troopIdFilter)
    {
        if (capital == null) return 0;
        try
        {
            int total = 0;
            foreach (var p in MobileParty.All)
            {
                if (p?.PartyComponent is not SovereignTowns.Parties.HonorGuardRecruiterPartyComponent hgr) continue;
                if (hgr.HomeSettlementOrNull != capital) continue;
                if (troopIdFilter == null)
                {
                    total += hgr.TargetCount;
                }
                else if (string.Equals(hgr.TroopId, troopIdFilter, StringComparison.Ordinal))
                {
                    total += hgr.TargetCount;
                }
            }
            return total;
        }
        catch (Exception ex)
        {
            Logger.Warn($"UnifiedGarrisonSolver.CountHonorGuardInFlight failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>统计 roster 中指定 troopId 的健康+受伤总头数（与 HonorGuard 池容量上限同口径）。</summary>
    private static int CountTroopInRoster(TaleWorlds.CampaignSystem.Roster.TroopRoster? roster, string troopId)
    {
        if (roster == null || string.IsNullOrEmpty(troopId)) return 0;
        try
        {
            int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character?.StringId == troopId) n += e.Number;
            }
            return n;
        }
        catch { return 0; }
    }

    /// <summary>统计 village 中各 notable 的 VolunteerTypes 与 <paramref name="allowedTroopIds"/> 的交集大小，
    /// 作为该村对此卫队 bucket 的供给容量。allowedTroopIds 已按 (role,tier) 约束过。</summary>
    private static int CountVillageVolunteersInTemplate(Settlement village, HashSet<string> allowedTroopIds)
    {
        if (village?.Notables == null || allowedTroopIds.Count == 0) return 0;
        try
        {
            int count = 0;
            foreach (var notable in village.Notables)
            {
                if (notable?.CanHaveRecruits != true) continue;
                var vt = notable.VolunteerTypes;
                if (vt == null) continue;
                foreach (var co in vt)
                {
                    if (co?.StringId != null && allowedTroopIds.Contains(co.StringId)) count++;
                }
            }
            return count;
        }
        catch { return 0; }
    }
}
