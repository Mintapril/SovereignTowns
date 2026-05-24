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
    // K 平衡偏移：旧 const K = 20_000_000 已迁到 FiscalAutonomyConfig.TickHoldingValueK
    // （PR-1, 2026-05-24）。本类内部仍用 K 作变量名以减少 diff，但读取自 cfg.TickHoldingValueK。
    // 不变性约束（PR-3 将进 Validate()）：K × HorizonTicks ≤ int.MaxValue 且 K > max(per-edge strategic)。

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
    /// <param name="forecast">每 tick 威胁来源(ThreatForecast,按敌军 ETA 投影)。</param>
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

            // Phase 6(2026-05-24):战时 + Clan.Gold ≤ 0 = 缓冲耗尽。holding cost 加 starvation penalty,
            // 让 MCMF 主动减兵 / 巡逻消化盈余,而不是默认贴 hardCap 留兵继续亏金。
            // K/4 是软推动:小到不会盖过 floor/core 守军 value(几千~ K 级),大到能让 surplus / 模板外
            // tier 被强制 disband/patrol。
            bool starving = false;
            try
            {
                starving = clan.Gold <= 0 && GarrisonAllocationSolver.IsClanAtWar(clan);
            }
            catch (Exception starveEx)
            {
                Logger.Warn($"UnifiedGarrisonSolver: starvation check failed for clan={clan.StringId}: {starveEx.Message}");
            }
            int starvationPenalty = starving ? K / 4 : 0;
            if (starving)
                Logger.Info($"UNIFIED-STARVATION clan={clan.StringId} Gold={clan.Gold} → holding penalty=K/4");

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
            void AddEcost(int from, int to, int cap, EdgeCost ec, EdgeCat cat)
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
                    AddE(superSource, G(s, kv.Key.Role, 0, kv.Key.Tier), kv.Value, 0, EdgeCat.Internal);
                    originSupply += kv.Value;
                }
            }

            // ── 在飞兵作为到达 supply:superSource → G[dest, role, tier, arrivalτ] ──
            // Phase 1 修复(2026-05-24):按真实 (role, tier) 入图,不再 hardcode Infantry。
            // 改造 6 已删 SingleInf/RolesOf,所有 town 平等承接全 role 维度。
            foreach (var (dest, role, tier, heads, arrivalTau) in CollectInFlightArrivals(clan, tickHours, T))
            {
                if (dest == null || !townSet.Contains(dest)) continue;
                AddE(superSource, G(dest, role, arrivalTau, tier), heads, arrivalTau * K, EdgeCat.Internal);
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
                                float tierShare = TierShareWithinRole(rule, role, tier);
                                if (tierShare <= 0f) continue;
                                int subCap = (int)Math.Round(roleCap * tierShare);
                                if (subCap <= 0) continue;
                                // Phase 3(2026-05-24):wage 按 vanilla tier 表,value 乘 power(tier) 让高 tier 兵 cost 更负。
                                // Phase 6:战时缓冲耗尽时加 starvationPenalty,主动减兵。
                                int holdCost = K + WageOf(tier) - ClampValue(value * PowerOf(tier), K) + starvationPenalty;
                                int from = G(s, role, tau, tier), to = G(s, role, tau + 1, tier);
                                AddE(from, to, subCap, holdCost, EdgeCat.Stay);
                                if (tau == 0)
                                {
                                    decodeInfo[(from, to)] = (DecodeKind.HoldingL0, s, null!, role);
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
                        AddE(gate, superSink, protectedCity ? 0 : disbandPerTickCap, 0, EdgeCat.Bypass);
                        AddE(gate, superSink, BigCap, overflowPenalty, EdgeCat.Bypass);
                    }
                    foreach (var role in MatchPolicy.Roles)
                    {
                        for (int tier = 1; tier <= 6; tier++)
                        {
                            int from = G(s, role, tau, tier);
                            // G→gate 标 Internal(非 Bypass):遣散是 G→gate→superSink 两跳,
                            // 若两段都记 Bypass,BypassFlow 会把同一批遣散兵计两次。只让
                            // gate→superSink 段计入 Bypass 统计,每个遣散兵恰好计一次。
                            AddE(from, gate, BigCap, (T - tau) * K, EdgeCat.Internal);
                            if (tau == 0) decodeInfo[(from, gate)] = (DecodeKind.Disband, s, null!, role);
                        }
                    }
                }

                // 时域出口:G[S,role,tier,T]→superSink(每 (role × tier) 各一条)。
                foreach (var role in MatchPolicy.Roles)
                    for (int tier = 1; tier <= 6; tier++)
                        AddE(G(s, role, T, tier), superSink, BigCap, 0, EdgeCat.Internal);
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
                        // Phase 1 改造 6:删 RolesOf,所有 (src, dst) 在 4 role × 6 tier 维度独立守恒。
                        foreach (var role in MatchPolicy.Roles)
                        {
                            for (int tier = 1; tier <= 6; tier++)
                            {
                                for (int tau = 0; tau + d <= T - 1; tau++)
                                {
                                    int from = G(s, role, tau, tier), to = G(s2, role, tau + d, tier);
                                    AddE(from, to, BigCap, routing, EdgeCat.Transfer);
                                    if (tau == 0) decodeInfo[(from, to)] = (DecodeKind.Transfer, s, s2, role);
                                }
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
                    int recTier = Math.Max(1, Math.Min(6, bucket.MinTier));
                    for (int tau = 0; tau <= T - 1; tau++)
                    {
                        int to = Transit(bucket.Role, recTier, tau);
                        AddE(origin, to, bucket.Count, tau * K, EdgeCat.Recruit);
                        if (tau == 0) decodeInfo[(origin, to)] = (DecodeKind.InPlace, capitalSettlement, null!, bucket.Role);
                    }
                    AddE(origin, superSink, bucket.Count, T * K, EdgeCat.Bypass);  // 未招募出口
                }

                // 候选村:RecOrigin[V,R] 单池;在飞边落 Transit[R, tier, τ_a],τ_a∈[ETA_V,T-1]。
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
                        int recTier = Math.Max(1, Math.Min(6, bucket.MinTier));
                        for (int arrival = etaV; arrival <= T - 1; arrival++)
                        {
                            int to = Transit(bucket.Role, recTier, arrival);
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
            // Phase 1 改造 4(2026-05-24):transit 节点带 tier 维度,转发也按 tier 守恒,无 Infantry hardcode。
            foreach (var kv in transitNode.ToList())
            {
                var (role, tier, tau) = kv.Key;
                int tn = kv.Value;
                // 留首府:Transit[R, tier, τ] → G[capital, R, tier, τ]。
                AddE(tn, G(capitalSettlement, role, tau, tier), BigCap, 0, EdgeCat.Internal);
                // 转发分支:Transit[R, tier, τ] → G[branch, R, tier, τ+d]。
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
                    int to = G(s, role, tau + d, tier);
                    AddE(tn, to, BigCap, routing, EdgeCat.Transfer);
                    if (tau == 0) decodeInfo[(tn, to)] = (DecodeKind.Transfer, capitalSettlement, s, role);
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
            // MCMF 这里只为 SSP 提供"低 tier 兵可升 → 高 tier holding cap"的拓扑路径,让流量穿过。
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
                            AddE(from, to, BigCap, goldCost, EdgeCat.Internal);
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
                AddE(patrolSink, superSink, patrolHeadroom, 0, EdgeCat.Internal);
                for (int tau = 0; tau < T; tau++)
                {
                    foreach (var role in MatchPolicy.Roles)
                    {
                        for (int tier = 1; tier <= 6; tier++)
                        {
                            int from = G(capitalSettlement, role, tau, tier);
                            // Phase 3:patrol 收益按 tier power 加权(高 tier 巡逻战力更强)。
                            int tierPatrolValue = (int)Math.Round(patrolValue * PowerOf(tier));
                            AddE(from, patrolSink, BigCap, (T - tau) * K - tierPatrolValue, EdgeCat.Patrol);
                            if (tau == 0)
                                decodeInfo[(from, patrolSink)] = (DecodeKind.Patrol, capitalSettlement, null!, role);
                        }
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
                var diagTiers = TierDefs(capitalTown, capitalSettlement, true, 0, cfg, forecast);
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

            // Phase 8(2026-05-24):per-settlement adequate(愿望) vs solved(实际) 对比诊断。
            // 愿望 = AdequateFor 公式驱动;实际 = MCMF 解出的 tick-0 持有头数。差距 > 0 表明
            // 供给/路径/预算/cap 之一是当前瓶颈。
            try
            {
                var diagSb = new System.Text.StringBuilder();
                foreach (var tDiag in towns)
                {
                    var sDiag = tDiag.Settlement;
                    if (sDiag == null) continue;
                    int diagFloor2 = Math.Max(0, cfg.MinGarrisonFloor);
                    int diagHardCap2 = GarrisonAllocationSolver.HardCapFor(tDiag, cfg);
                    int adequate = GarrisonAllocationSolver.AdequateFor(tDiag, cfg, diagFloor2, diagHardCap2);
                    int solved = result.Target.TryGetValue(sDiag, out var sv) ? sv : 0;
                    diagSb.Append($"{sDiag.StringId}:want={adequate}/got={solved} ");
                }
                Logger.Info($"MERGED-GAP clan={clan.StringId} {diagSb}");
            }
            catch (Exception ex) { Logger.Error("MERGED-GAP diagnostic failed", ex); }
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
        FiscalAutonomyConfig cfg, IHorizonForecast forecast)
    {
        int floor = Math.Max(0, cfg.MinGarrisonFloor);
        int hardCap = GarrisonAllocationSolver.HardCapFor(t, cfg);
        int adequate = GarrisonAllocationSolver.AdequateFor(t, cfg, floor, hardCap);
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

    /// <summary>
    /// 该 (role, tier) 在 rule 模板中相对该 role 内部的占比 ∈ [0, 1]。
    /// - ExactTemplate 模式:扫 rule.ExactTroopTemplate,按 (role, tier) 聚合 weight,归一化到 role 内
    /// - Generic 模式:tier == clamp(rule.MaxTier, 1, 6) ? 1 : 0(目标顶 tier,中间 tier 是过渡)
    /// 若 role 没有任何 share(模板未覆盖该 role),返回 0 — 该 (role, tier) 节点 holding cap = 0,
    /// 进入的兵需走 upgrade edge / transfer / disband / patrol 才能消化。
    /// </summary>
    private static float TierShareWithinRole(TownGarrisonRule rule, GenericTroopRole role, int tier)
    {
        if (rule == null) return tier == 1 ? 1f : 0f;
        int maxTier = Math.Max(1, Math.Min(6, rule.MaxTier));
        if (rule.UseGenericMatching)
            return tier == maxTier ? 1f : 0f;

        if (rule.ExactTroopTemplate == null || rule.ExactTroopTemplate.Count == 0)
            return tier == maxTier ? 1f : 0f;

        var mbom = TaleWorlds.ObjectSystem.MBObjectManager.Instance;
        if (mbom == null) return tier == maxTier ? 1f : 0f;

        float roleSum = 0f, roleTierSum = 0f;
        foreach (var kv in rule.ExactTroopTemplate)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0f) continue;
            CharacterObject? ch;
            try { ch = mbom.GetObject<CharacterObject>(kv.Key); }
            catch { continue; }
            if (ch == null || ch.IsHero) continue;
            if (GenericTroopMatcher.GetRole(ch) != role) continue;
            roleSum += kv.Value;
            if (GenericTroopMatcher.GetTierBucket(ch) == tier) roleTierSum += kv.Value;
        }
        if (roleSum <= 0f) return 0f;
        return roleTierSum / roleSum;
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
}
