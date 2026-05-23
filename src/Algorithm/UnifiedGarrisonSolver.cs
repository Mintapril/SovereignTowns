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
         + $"stay={StayFlow} recruit={RecruitFlow} transfer={TransferFlow} bypass={BypassFlow} patrol={PatrolFlow}";
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
    /// <summary>K 平衡偏移。每条 source→sink 路径的 K 分量之和恒 = T·K(§6.3),公共偏移抵消。
    /// 须 ≥ 任何 tier value(~47K);20M 远高于此。单条边费用 ≤ ~T·K,T≤64 → ≤ ~1.3G,在 int 内。
    /// 结构性常量,不暴露为配置。</summary>
    private const int K = 20_000_000;

    /// <summary>分支 G 节点单一占位 role(分支头数口径,见 handoff §3.1)。</summary>
    private static readonly GenericTroopRole[] SingleInf = { GenericTroopRole.Infantry };

    /// <summary>边的语义类别 —— Solve 后聚合 EdgeFlows 产差异日志。</summary>
    private enum EdgeCat { Internal, Stay, Transfer, Recruit, Bypass, Patrol }

    /// <summary>decode 用:layer-0 出发 / 指令相关边的登记。</summary>
    private enum DecodeKind { Recruiter = 1, InPlace, Transfer, Disband, HoldingL0, Patrol }

    /// <summary>
    /// 同步求解封装 —— 一次排干 <see cref="SolveCoroutine"/>。同步驱动下 SSP 的 yield 退化为空
    /// MoveNext,结果与分帧驱动逐字节一致。
    /// </summary>
    public static UnifiedSolverResult Solve(
        CapitalManager manager, Settlement capitalSettlement,
        IHorizonForecast forecast, int patrolHeadroom = 0)
    {
        UnifiedSolverResult? captured = null;
        try
        {
            var it = SolveCoroutine(manager, capitalSettlement, forecast, patrolHeadroom,
                r => captured = r);
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
    /// <param name="forecast">每 tick 威胁来源(FlatForecast / ThreatForecast)。</param>
    /// <param name="patrolHeadroom">巡逻 sink 容量(头数)= 哨所派生的巡逻队上限余量 ×
    /// PatrolTargetSize;0 = 不建巡逻 sink。由 CapitalLogisticsManager 算好传入(solver 不查建筑等级)。</param>
    /// <param name="onResult">求解结束回调(finally 保证触发;前置守卫提前结束时 result.Ran=false)。</param>
    public static System.Collections.IEnumerator SolveCoroutine(
        CapitalManager manager, Settlement capitalSettlement,
        IHorizonForecast forecast, int patrolHeadroom,
        Action<UnifiedSolverResult> onResult)
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
            bool manualMode = cfg.AllowManualGarrisonTargets;
            int T = Math.Min(64, Math.Max(1, cfg.HorizonTicks));
            int tickHours = Math.Min(24, Math.Max(1, cfg.CapitalLogisticsTickHours));

            // 2026-05-23 Plan B 起：管理范围 = 整个 clan.Fiefs，不再按 settlement.Owner（首府所有者 hero）
            // 做过滤。理由：方案 B 下所有 clan.Fiefs 的收入统一走 vanilla Clan.Gold，
            // 区分"首府所有者持有"vs"兄弟/子嗣持有"已无经济意义。
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

            var graph = new MinCostFlow();
            var edgeCat = new Dictionary<(int, int), EdgeCat>();
            var decodeInfo = new Dictionary<(int, int),
                (DecodeKind Kind, Settlement A, Settlement B, GenericTroopRole Role)>();
            int edgeCount = 0;
            void AddE(int from, int to, int cap, int cost, EdgeCat cat)
            {
                if (cap <= 0) return;
                graph.AddEdge(from, to, cap, cost);
                edgeCat[(from, to)] = cat;
                edgeCount++;
            }

            // ── 节点(懒分配)──
            int next = 1;
            int superSource = next++, superSink = next++;
            var gNode = new Dictionary<(Settlement, GenericTroopRole, int), int>();
            int G(Settlement s, GenericTroopRole r, int tau)
            {
                var key = (s, r, tau);
                if (!gNode.TryGetValue(key, out var n)) { n = next++; gNode[key] = n; }
                return n;
            }
            var transitNode = new Dictionary<(GenericTroopRole, int), int>();
            int Transit(GenericTroopRole r, int tau)
            {
                var key = (r, tau);
                if (!transitNode.TryGetValue(key, out var n)) { n = next++; transitNode[key] = n; }
                return n;
            }
            var disbandGate = new Dictionary<(Settlement, int), int>();

            const int BigCap = 1_000_000;
            int disbandPerTickCap = Math.Max(0, (int)Math.Round(cfg.DisbandPerDayCap * tickHours / 24.0));
            int overflowPenalty = Math.Max(1, cfg.BypassOverflowPenalty);

            GenericTroopRole[] RolesOf(Settlement s) => s == capitalSettlement ? MatchPolicy.Roles : SingleInf;
            int originSupply = 0;

            // ── 初始驻军:superSource → G[S,R,0] ──
            foreach (var t in towns)
            {
                var s = t.Settlement;
                foreach (var bucket in MatchPolicy.Bucketize(t.GarrisonParty?.MemberRoster))
                {
                    if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) continue;
                    var r = s == capitalSettlement ? bucket.Role : GenericTroopRole.Infantry;
                    AddE(superSource, G(s, r, 0), bucket.Count, 0, EdgeCat.Internal);
                    originSupply += bucket.Count;
                }
            }

            // ── 在飞兵作为到达 supply:superSource → G[dest,Inf,arrivalτ](§6.3 Task4c)──
            // role-blind:在飞兵一律记 Infantry track(handoff §3.6 近似)。
            foreach (var (dest, heads, arrivalTau) in CollectInFlightArrivals(clan, tickHours, T))
            {
                if (dest == null || !townSet.Contains(dest)) continue;
                AddE(superSource, G(dest, GenericTroopRole.Infantry, arrivalTau), heads, arrivalTau * K, EdgeCat.Internal);
                originSupply += heads;
            }

            // ── holding 边 + 时域出口 + 遣散边(每城)──
            int demandTierCapL0 = 0;
            foreach (var t in towns)
            {
                var s = t.Settlement;
                bool isCapital = s == capitalSettlement;
                bool besieged = s.IsUnderSiege;
                bool protectedCity = besieged || IsProtectedFromDisband(s, cfg);
                var rule = isCapital ? (ConfigurationManager.GetRuleFor(t) ?? TownGarrisonRule.CreateDefault()) : null;

                for (int tau = 0; tau < T; tau++)
                {
                    // holding 边:G[S,R,τ]→G[S,R,τ+1],按 tier 拆;费用 = 1·K + wage − value。
                    foreach (var (cap, value) in TierDefs(t, s, isCapital, tau, cfg, manualMode, forecast))
                    {
                        if (cap <= 0) continue;
                        int holdCost = K + wagePerTroop - ClampValue(value);
                        if (isCapital)
                        {
                            foreach (var role in MatchPolicy.Roles)
                            {
                                int roleCap = MatchPolicy.DesiredCount(rule!, role, cap);
                                if (roleCap <= 0) continue;
                                int from = G(s, role, tau), to = G(s, role, tau + 1);
                                AddE(from, to, roleCap, holdCost, EdgeCat.Stay);
                                if (tau == 0)
                                {
                                    decodeInfo[(from, to)] = (DecodeKind.HoldingL0, s, null!, role);
                                    demandTierCapL0 += roleCap;
                                }
                            }
                        }
                        else
                        {
                            int from = G(s, GenericTroopRole.Infantry, tau), to = G(s, GenericTroopRole.Infantry, tau + 1);
                            AddE(from, to, cap, holdCost, EdgeCat.Stay);
                            if (tau == 0)
                            {
                                decodeInfo[(from, to)] = (DecodeKind.HoldingL0, s, null!, GenericTroopRole.Infantry);
                                demandTierCapL0 += cap;
                            }
                        }
                    }

                    // 遣散边:G[S,R,τ]→disbandGate[S,τ],K 分量 (T−τ)·K。保护城正常段 cap=0。
                    if (!disbandGate.TryGetValue((s, tau), out var gate))
                    {
                        gate = next++;
                        disbandGate[(s, tau)] = gate;
                        AddE(gate, superSink, protectedCity ? 0 : disbandPerTickCap, 0, EdgeCat.Bypass);
                        AddE(gate, superSink, BigCap, overflowPenalty, EdgeCat.Bypass);
                    }
                    foreach (var role in RolesOf(s))
                    {
                        int from = G(s, role, tau);
                        // G→gate 标 Internal(非 Bypass):遣散是 G→gate→superSink 两跳,
                        // 若两段都记 Bypass,BypassFlow 会把同一批遣散兵计两次。只让
                        // gate→superSink 段计入 Bypass 统计,每个遣散兵恰好计一次。
                        AddE(from, gate, BigCap, (T - tau) * K, EdgeCat.Internal);
                        if (tau == 0) decodeInfo[(from, gate)] = (DecodeKind.Disband, s, null!, role);
                    }
                }

                // 时域出口:G[S,R,T]→superSink。
                foreach (var role in RolesOf(s))
                    AddE(G(s, role, T), superSink, BigCap, 0, EdgeCat.Internal);
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
                        int routing = d * K
                                      + Math.Max(0, thresholds.McmfTransferOverhead)
                                      + RouteRiskSurcharge(s, s2, cfg);
                        foreach (var role in RolesOf(s))
                        {
                            var destRole = s2 == capitalSettlement ? role : GenericTroopRole.Infantry;
                            for (int tau = 0; tau + d <= T - 1; tau++)
                            {
                                int from = G(s, role, tau), to = G(s2, destRole, tau + d);
                                AddE(from, to, BigCap, routing, EdgeCat.Transfer);
                                if (tau == 0) decodeInfo[(from, to)] = (DecodeKind.Transfer, s, s2, role);
                            }
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
                    AddE(superSource, origin, bucket.Count, 0, EdgeCat.Internal);
                    originSupply += bucket.Count;
                    for (int tau = 0; tau <= T - 1; tau++)
                    {
                        int to = Transit(bucket.Role, tau);
                        AddE(origin, to, bucket.Count, tau * K, EdgeCat.Recruit);
                        if (tau == 0) decodeInfo[(origin, to)] = (DecodeKind.InPlace, capitalSettlement, null!, bucket.Role);
                    }
                    AddE(origin, superSink, bucket.Count, T * K, EdgeCat.Bypass);  // 未招募出口
                }

                // 候选村:RecOrigin[V,R] 单池;在飞边落 Transit[R,τ_a],τ_a∈[ETA_V,T-1]。
                var inFlightVillages = RecruitmentTopology.CollectInFlightRecruiterVillages(clan);
                foreach (var village in RecruitmentTopology.EnumerateRecruitmentVillages(capitalTown, clan, inFlightVillages))
                {
                    int etaV = EtaTicks(2 * RoutingDistance(village, capitalSettlement), tickHours);  // 往返
                    if (etaV > T - 1) continue;
                    int recRouting = Math.Max(0, thresholds.McmfRecruiterOverhead)
                                     + RouteRiskSurcharge(village, capitalSettlement, cfg);
                    foreach (var bucket in RecruitmentTopology.BucketizeCharacters(
                                 RecruitmentTopology.EnumerateVolunteerTroops(village),
                                 capitalRule,
                                 requiredCultureId))
                    {
                        if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) continue;
                        int origin = next++;
                        AddE(superSource, origin, bucket.Count, 0, EdgeCat.Internal);
                        originSupply += bucket.Count;
                        for (int arrival = etaV; arrival <= T - 1; arrival++)
                        {
                            int to = Transit(bucket.Role, arrival);
                            AddE(origin, to, bucket.Count, arrival * K + recRouting, EdgeCat.Recruit);
                            if (arrival == etaV)  // dispatch tick = arrival − etaV == 0
                                decodeInfo[(origin, to)] = (DecodeKind.Recruiter, village, null!, bucket.Role);
                        }
                        AddE(origin, superSink, bucket.Count, T * K, EdgeCat.Bypass);  // 未招募出口
                    }
                }
            }
            recruitMs = sw.ElapsedMilliseconds - recruitStart;

            // ── transit 出边:招募兵留首府 / 转发分支 ──
            foreach (var kv in transitNode.ToList())
            {
                var (role, tau) = kv.Key;
                int tn = kv.Value;
                // 留首府:Transit[R,τ]→G[capital,R,τ]。
                AddE(tn, G(capitalSettlement, role, tau), BigCap, 0, EdgeCat.Internal);
                // 转发分支:Transit[R,τ]→G[branch,Inf,τ+d]。
                if (!allowTransfers) continue;
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == capitalSettlement || s.IsUnderSiege) continue;
                    int d = EtaTicks(RoutingDistance(capitalSettlement, s), tickHours);
                    if (tau + d > T - 1) continue;
                    int routing = d * K
                                  + Math.Max(0, thresholds.McmfTransferOverhead)
                                  + RouteRiskSurcharge(capitalSettlement, s, cfg);
                    int to = G(s, GenericTroopRole.Infantry, tau + d);
                    AddE(tn, to, BigCap, routing, EdgeCat.Transfer);
                    if (tau == 0) decodeInfo[(tn, to)] = (DecodeKind.Transfer, capitalSettlement, s, role);
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
                AddE(patrolSink, superSink, patrolHeadroom, 0, EdgeCat.Internal);
                for (int tau = 0; tau < T; tau++)
                {
                    foreach (var role in MatchPolicy.Roles)
                    {
                        int from = G(capitalSettlement, role, tau);
                        AddE(from, patrolSink, BigCap, (T - tau) * K - patrolValue, EdgeCat.Patrol);
                        if (tau == 0)
                            decodeInfo[(from, patrolSink)] = (DecodeKind.Patrol, capitalSettlement, null!, role);
                    }
                }
            }

            // ── Solve(分帧 SSP)── swSolve 只累计 MoveNext 内的 CPU,排除 yield 帧间隙。
            long buildMs = sw.ElapsedMilliseconds;

            // 诊断:首府 tick-0 tier 结构。守军止于「tier value ≤ wage」处 —— 对照 wage / adequate
            // 即知 merged 欠配(tick0Garrison < legacy)是 value/wage 还是 adequate 上限所致。
            try
            {
                int diagFloor = Math.Max(0, cfg.MinGarrisonFloor);
                int diagHardCap = GarrisonAllocationSolver.HardCapFor(capitalTown, cfg);
                int diagAdequate = GarrisonAllocationSolver.AdequateFor(capitalTown, cfg, diagFloor, diagHardCap);
                var diagTiers = TierDefs(capitalTown, capitalSettlement, true, 0, cfg, manualMode, forecast);
                var sb = new System.Text.StringBuilder();
                foreach (var (cap, value) in diagTiers) sb.Append($"({cap}@{value:F0}) ");
                Logger.Info(
                    $"MERGED-TIERS clan={clan.StringId} wage={wagePerTroop} budget={clanWageBudget} "
                  + $"floor={diagFloor} adequate={diagAdequate} hardCap={diagHardCap} tier(cap@value)=[ {sb}]");
            }
            catch (Exception ex) { Logger.Error("MERGED-TIERS diagnostic failed", ex); }

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
                }
            }

            result.DemandFilled = result.Target.Values.Sum();
            foreach (var kv in transfers)
                result.Instructions.Add(new TransferPartyInstruction(
                    kv.Key.Src, kv.Key.Dst, kv.Key.Role, kv.Value));
            if (patrolHeads > 0)
                result.Instructions.Add(new PatrolInstruction(capitalSettlement, patrolHeads));

            long decodeMs = sw.ElapsedMilliseconds - beforeDecode;
            Logger.Info(
                $"MERGED-TIMING clan={clan.StringId} T={T} nodes={result.NodeCount} edges={result.EdgeCount} "
              + $"build={buildMs}ms (recruit={recruitMs}ms) solve={solveMs}ms (sspFrames={sspFrames}) "
              + $"decode={decodeMs}ms wall={sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            onResult?.Invoke(result);
        }
    }

    /// <summary>
    /// 某城某 tick 的 (tier 容量, tier value) 序列 —— floor/core/surplus,沿用今天 Merged* 口径;
    /// threat 乘子取 <paramref name="forecast"/> 的该 tick 投影。manual 模式用玩家手动目标作容量(M5)。
    /// </summary>
    private static List<(int Cap, float Value)> TierDefs(
        Town t, Settlement s, bool isCapital, int tau,
        FiscalAutonomyConfig cfg, bool manualMode, IHorizonForecast forecast)
    {
        int floor = Math.Max(0, cfg.MinGarrisonFloor);
        int hardCap = GarrisonAllocationSolver.HardCapFor(t, cfg);
        int adequate = GarrisonAllocationSolver.AdequateFor(t, cfg, floor, hardCap);
        if (manualMode)
        {
            int manualTarget = ComputeManualTarget(t, cfg);
            hardCap = manualTarget;
            adequate = manualTarget;
            floor = Math.Min(floor, manualTarget);
        }
        float threat = ThreatWeightOf(SafeThreatAt(forecast, s, tau), cfg);
        float strat = StrategicWeight(s, isCapital, cfg);
        int coreSpan = Math.Max(0, adequate - floor);
        int coreCount = Math.Max(1, cfg.CoreTierCount);
        int surplusSpan = Math.Max(0, hardCap - adequate);

        var defs = new List<(int, float)>();
        if (floor > 0)
            defs.Add((floor, cfg.ValueFloorBase * threat * strat));
        for (int k = 0; k < coreCount; k++)
        {
            int cap = coreSpan * (k + 1) / coreCount - coreSpan * k / coreCount;
            if (cap <= 0) continue;
            float dim = 1.0f - cfg.CoreDimRange * ((k + cfg.CoreDimMidpoint) / coreCount);
            defs.Add((cap, cfg.ValueCoreBase * dim * threat * strat));
        }
        if (surplusSpan > 0)
            defs.Add((surplusSpan, -Math.Max(1, cfg.SurplusEdgeCost)));
        return defs;
    }

    private static RiskLevel SafeThreatAt(IHorizonForecast forecast, Settlement s, int tau)
    {
        try { return forecast.ThreatAt(s, tau); }
        catch (Exception ex)
        {
            Logger.Error($"UnifiedGarrisonSolver: forecast.ThreatAt failed for '{s?.StringId}' tick={tau}", ex);
            return RiskLevel.Low;
        }
    }

    /// <summary>把 tier value(可负)round + clamp 到 [−K, K]。</summary>
    private static int ClampValue(float value)
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

    /// <summary>strategic 乘子:(首府 ? cfg.CapitalStrategicBonus : 1.0)
    /// × clamp(Prosperity / cfg.ProsperityNormalizer, 0.5, 1.5)。</summary>
    private static float StrategicWeight(Settlement s, bool isCapital, FiscalAutonomyConfig cfg)
    {
        float prosperity = 0f;
        try { if (s != null && s.IsTown && s.Town != null) prosperity = s.Town.Prosperity; }
        catch { prosperity = 0f; }

        float normalizer = cfg.ProsperityNormalizer > 0f ? cfg.ProsperityNormalizer : 4000f;
        float pf = prosperity / normalizer;
        if (pf < 0.5f) pf = 0.5f;
        if (pf > 1.5f) pf = 1.5f;
        return (isCapital ? cfg.CapitalStrategicBonus : 1.0f) * pf;
    }

    /// <summary>该城现有驻军是否禁止主动遣散:功能开关关 / manual 模式 / 围城 / 高危。
    /// 保护城仍建 disbandGate,但正常段 cap=0(只 overflow 兜底,见 §6.3)。</summary>
    private static bool IsProtectedFromDisband(Settlement s, FiscalAutonomyConfig cfg)
    {
        if (!cfg.DisbandUnaffordableExcess) return true;
        if (cfg.AllowManualGarrisonTargets) return true;
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

    private static int RoutingDistance(Settlement a, Settlement b)
    {
        try { return (int)Math.Round((double)(a.GetPosition2D - b.GetPosition2D).Length); }
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

    /// <summary>
    /// M5:manual 模式下玩家配置的目标头数 —— 合并 solver 的 demand 容量上限。
    /// 城镇:TargetTotalCount × 风险乘子;城堡:BranchRule.TargetPower 当头数。均 clamp MaxGarrisonHardCap。
    /// 控制面板 StashAssessments 复用本方法 —— 单一口径。
    /// </summary>
    internal static int ComputeManualTarget(Town t, FiscalAutonomyConfig cfg)
    {
        try
        {
            var s = t?.Settlement;
            if (s == null) return Math.Max(1, cfg.MinGarrisonFloor);
            int hardCap = Math.Max(1, cfg.MaxGarrisonHardCap);
            if (t!.IsTown)
            {
                var rule = ConfigurationManager.GetRuleFor(t) ?? TownGarrisonRule.CreateDefault();
                var risk = RiskAssessmentService.Assess(s);
                float mul = risk.Level >= RiskLevel.High ? rule.WartimeMultiplier : rule.PeacetimeMultiplier;
                return Math.Min(Math.Max(1, (int)Math.Round(rule.TargetTotalCount * mul)), hardCap);
            }
            var branch = ConfigurationManager.GetBranchRuleFor(t) ?? BranchRule.CreateDefault();
            return Math.Min(Math.Max(1, branch.TargetPower), hardCap);
        }
        catch (Exception ex)
        {
            Logger.Error($"UnifiedGarrisonSolver.ComputeManualTarget failed for '{t?.Settlement?.StringId}'", ex);
            return Math.Max(1, cfg.MinGarrisonFloor);
        }
    }

    /// <summary>
    /// 本 clan 所有在飞 ST 队按目标定居点 + 估计到达 tick 汇总。Transfer 回程(目标==源)记到源城,
    /// 否则记目的城;Recruiter / Sally 记各自 home。role-blind(§6.3 / handoff §3.6)。
    /// </summary>
    private static List<(Settlement Dest, int Heads, int ArrivalTau)> CollectInFlightArrivals(
        Clan clan, int tickHours, int horizon)
    {
        var list = new List<(Settlement, int, int)>();
        try
        {
            var parties = MobileParty.AllCustomParties;
            if (parties == null) return list;
            foreach (var party in parties)
            {
                if (party == null || !party.IsActive) continue;
                int heads = party.MemberRoster?.TotalManCount ?? 0;
                if (heads <= 0) continue;

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
                list.Add((dest, heads, Math.Max(0, eta)));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UnifiedGarrisonSolver.CollectInFlightArrivals failed", ex);
        }
        return list;
    }
}
