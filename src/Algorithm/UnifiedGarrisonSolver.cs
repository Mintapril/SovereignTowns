using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Models;
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

    /// <summary>本次求解所用氏族驻军工资预算(金币/日,informational)。</summary>
    public long Budget { get; set; }

    /// <summary>decode:每城 tick-0 目标头数(= Σ_R holding 边 G[S,R,0]→G[S,R,1] 流)。每 fief 预播种 0。
    /// 注意:此值是 MCMF 实际 flow,受 supply / 初始 garrison 影响。"算法判断该城应有多少兵"请用
    /// <see cref="Capacity"/>。</summary>
    public Dictionary<Settlement, int> Target { get; } = new();

    /// <summary>每城 capacity = 预算+威胁+战略约束下算法判断的应有驻军(独立于实际 garrison)。
    /// 公式: min(PartySizeLimit_i, N × w_i / Σw) 其中 N = 全氏族可养兵数 = clanWageBudget / wagePerTroop,
    /// w_i = PartySizeLimit_i × threat_i × strategic_i。
    /// 这也是 MCMF holding cap 的实际值 —— UI "目标驻军" 显示此值。
    /// 预算充足时 → 每城 = PartySizeLimit。预算紧时按 (PartySizeLimit × threat × strategic) 加权分配。
    /// 不受当前实际驻军影响,玩家抽兵不会让此值变化。</summary>
    public Dictionary<Settlement, int> Capacity { get; } = new();

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
         + $"stay={StayFlow} recruit={RecruitFlow} transfer={TransferFlow} bypass={BypassFlow}";
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
    private enum EdgeCat { Internal, Stay, Transfer, Recruit, Bypass }

    /// <summary>decode 用:layer-0 出发 / 指令相关边的登记。</summary>
    private enum DecodeKind { Recruiter = 1, InPlace, Transfer, Disband, HoldingL0, HonorGuardRecruiter }

    /// <summary>
    /// 同步求解封装 —— 一次排干 <see cref="SolveCoroutine"/>。同步驱动下 SSP 的 yield 退化为空
    /// MoveNext,结果与分帧驱动逐字节一致。
    /// </summary>
    public static UnifiedSolverResult Solve(
        CapitalManager manager, Settlement capitalSettlement,
        IHorizonForecast forecast)
    {
        UnifiedSolverResult? captured = null;
        try
        {
            var it = SolveCoroutine(manager, capitalSettlement, forecast,
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
    /// 分帧求解协程。建图同步跑完 → SSP 按 cfg.SolvePerFrameBudgetMs CPU 预算 yield 分帧,
    /// 超 cfg.SolveBudgetWallMs 总预算切同步排干 → decode 同步跑完。
    /// 完成(含前置守卫提前结束 / 异常)时一律经 finally 调 <paramref name="onResult"/>。
    /// 异常不在此 catch —— 交由 AsyncSimulator.Update 捕获并打印协程栈。
    /// </summary>
    /// <param name="forecast">每 tick 威胁来源(ThreatForecast,按敌军 ETA 投影)。</param>
    /// <param name="onResult">求解结束回调(finally 保证触发;前置守卫提前结束时 result.Ran=false)。</param>
    public static System.Collections.IEnumerator SolveCoroutine(
        CapitalManager manager, Settlement capitalSettlement,
        IHorizonForecast forecast,
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

            // ── Per-city capacity 分配(2026-05-28 重构) ──
            // 把"全氏族预算"硬分配到每城,作为 holding cap。MCMF holding 流量天然受此约束 →
            // 预算真正发挥作用(此前 clanWageBudget 只入诊断字段、不约束求解)。
            //
            // 公式: N = clanWageBudget / wagePerTroop          // 全氏族可养兵数
            //      w_i = hardCap_i × threat_i × strategic_i    // 每城权重
            //      capacity_i = min(hardCap_i, round(N × w_i / Σw))
            //
            // 性质:
            //   - 预算充足(N ≥ Σ hardCap)→ 每城 = hardCap_i = PartySizeLimit
            //   - 预算紧 → 按 (PartySizeLimit × threat × strategic) 加权分(高威胁/首府/富城多分)
            //   - 不依赖当前 garrison 实际人数 → UI 显示稳定,玩家抽兵不会让目标变
            var perCityCapacity = new Dictionary<Settlement, int>(towns.Count);
            {
                long N = Math.Max(0L, clanWageBudget / Math.Max(1, wagePerTroop));
                double totalWeight = 0d;
                var weightOf = new Dictionary<Settlement, double>(towns.Count);
                var hardCapOf = new Dictionary<Settlement, int>(towns.Count);
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == null) continue;
                    int hc = GarrisonAllocationSolver.HardCapFor(t, cfg);
                    bool isCapital = s == capitalSettlement;
                    float threat = ThreatWeightOf(SafeThreatAt(forecast, s, 0), cfg);
                    float strat = StrategicWeight(s, isCapital, cfg);
                    double w = (double)hc * threat * strat;
                    weightOf[s] = w;
                    hardCapOf[s] = hc;
                    totalWeight += w;
                }
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == null) continue;
                    int hc = hardCapOf.TryGetValue(s, out var h) ? h : 0;
                    int cap;
                    if (totalWeight <= 0d || N <= 0)
                    {
                        cap = 0;
                    }
                    else
                    {
                        double share = (double)N * weightOf[s] / totalWeight;
                        cap = (int)Math.Round(Math.Max(0d, Math.Min(hc, share)));
                    }
                    perCityCapacity[s] = cap;
                    result.Capacity[s] = cap;
                }

                // [GARRISON-DIAG] 预算→容量 分配明细
                Logger.Info($"[GARRISON-DIAG] perCityCapacity clan='{clan.StringId}' N={N} wagePerTroop={wagePerTroop} clanWageBudget={clanWageBudget} totalWeight={totalWeight:F1} towns={towns.Count}");
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == null) continue;
                    int hc = hardCapOf.TryGetValue(s, out var h) ? h : 0;
                    double w = weightOf.TryGetValue(s, out var ww) ? ww : 0d;
                    int cap = perCityCapacity.TryGetValue(s, out var cc) ? cc : 0;
                    int actualHeads = t.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                    int delta = actualHeads - cap;
                    string deltaTag = delta > 0 ? $" → OVER by {delta}" : (delta < 0 ? $" → under by {-delta}" : " → exact");
                    Logger.Info($"[GARRISON-DIAG]   town='{s.StringId}' hardCap={hc} weight={w:F1} → cap={cap} actualHeads={actualHeads}{deltaTag}");
                }
            }

            // PR-5'(2026-05-24): starvation penalty branch (IsClanAtWar + clan.Gold) removed.

            var graph = new MinCostFlow();
            var edgeCat = new Dictionary<(int, int), EdgeCat>();
            // Tier 字段仅 HonorGuardRecruiter decode 用（per-bucket 兵种 tier）；其他 kind 一律 0。
            var decodeInfo = new Dictionary<(int, int),
                (DecodeKind Kind, Settlement A, Settlement B, GenericTroopRole Role, int Tier)>();
            int edgeCount = 0;
            int clampedPublicCostEdges = 0;
            long mostNegativePublicCost = 0;
            void AddE(int from, int to, int cap, EdgeCost ec, EdgeCat cat)
            {
                if (cap <= 0) return;
                long cost = EdgeCostCompose.ToPublicFlowCost(ec, cfg, out var rawCost);
                if (rawCost < 0)
                {
                    clampedPublicCostEdges++;
                    if (rawCost < mostNegativePublicCost) mostNegativePublicCost = rawCost;
                }
                graph.AddEdge(from, to, cap, cost);
                edgeCat[(from, to)] = cat;
                edgeCount++;
            }

            // ── 节点(懒分配)──
            // 2026-05-29 最简化重构：tier 也从 solver 全砍。每城单节点 G(s, τ)；hold 边直接 G→G cap=perCityCapacity。
            // 边数 / 单边 cost 都降到 SSP 易收敛的量级（边数从 ~22K → ~5K，solve 时间应 < 100ms）。
            // tier 身份只在 decode 描述符（di.Tier 用 0 sentinel），由执行端 LowestTierFirst 决定具体抽哪 tier。
            int next = 1;
            int superSource = next++, superSink = next++;
            var gNode = new Dictionary<(Settlement S, int Tau), int>();
            int G(Settlement s, int tau)
            {
                var key = (s, tau);
                if (!gNode.TryGetValue(key, out var n)) { n = next++; gNode[key] = n; }
                return n;
            }
            // Transit 也去 tier 维度；recruit 多 origin 多 tier 共用同一 Transit[τ]，cost 差异在 origin→Transit 边付。
            var transitNode = new Dictionary<int, int>();
            int Transit(int tau)
            {
                if (!transitNode.TryGetValue(tau, out var n)) { n = next++; transitNode[tau] = n; }
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
            // [GARRISON-DIAG] 同时缓存 bucketHeads 供下游 holding 边日志 (subCap vs heads) 对比。
            var bucketHeadsByTown = new Dictionary<Settlement, Dictionary<(GenericTroopRole Role, int Tier), int>>(towns.Count);
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
                bucketHeadsByTown[s] = bucketHeads;
                // 2026-05-29: 所有 tier 合并入单节点 G[s, 0]；tier 信息仅用于直方图日志和执行端 LowestTierFirst。
                int totalSupplyTau0 = bucketHeads.Values.Sum();
                if (totalSupplyTau0 > 0)
                {
                    AddE(superSource, G(s, 0), totalSupplyTau0, EdgeCost.Zero, EdgeCat.Internal);
                    originSupply += totalSupplyTau0;
                }
                // [GARRISON-DIAG] 直方图：驻军 (role × tier) 分布——观察是否聚集在某几桶。
                if (bucketHeads.Count > 0)
                {
                    int totalHeads = bucketHeads.Values.Sum();
                    var perRole = bucketHeads
                        .GroupBy(kv => kv.Key.Role)
                        .Select(g => $"{g.Key}={g.Sum(kv => kv.Value)}");
                    var perBucket = bucketHeads
                        .OrderBy(kv => kv.Key.Role).ThenBy(kv => kv.Key.Tier)
                        .Select(kv => $"{kv.Key.Role}T{kv.Key.Tier}={kv.Value}");
                    Logger.Info($"[GARRISON-DIAG] initial garrison '{s.StringId}' total={totalHeads} byRole=[{string.Join(",", perRole)}] byBucket=[{string.Join(",", perBucket)}]");
                }
            }

            // ── 在飞兵作为到达 supply:superSource → G[dest, arrivalτ] ──
            // 2026-05-29: role/tier 都从节点 key 删除；CollectInFlightArrivals 返 role/tier 仅作描述符，solver 忽略。
            // 2026-05-30 修 #17：在飞兵 arrivalTau **强制 ≥ 1**。在飞兵此刻并不在驻军里（还在行军队伍中，
            //   merge 是另一条事件路径），返航/在城边的队伍 eta 会算成 0，旧实现把它们注入 G[dest,0]，
            //   与当前真实驻军抢同一条 τ=0 hold 边（cap=settlementCapacity）→ 真实驻军被挤去遣散
            //   （日志可见 garrison 远低于目标却 disband-plan total=7/18）。改到 τ≥1 后：τ=0 的 hold 压力
            //   只剩当前真实驻军，只有真实驻军超 cap 才遣散；在飞兵在 τ≥1 占容量、自然抑制过量补员，
            //   不会再为"还没到的兵"先裁真兵。
            foreach (var (dest, role, tier, heads, arrivalTau) in CollectInFlightArrivals(clan, tickHours, T))
            {
                if (dest == null || !townSet.Contains(dest)) continue;
                int safeArrivalTau = Math.Min(T, Math.Max(1, arrivalTau));
                AddE(superSource, G(dest, safeArrivalTau), heads, new EdgeCost(timeUnits: safeArrivalTau), EdgeCat.Internal);
                originSupply += heads;
            }

            // ── holding 池化 + 时域出口 + 遣散边(每城) ──
            // 2026-05-29 B-pool 重构：
            //   1) 节点：G[s, tier, τ] 仅作 supply / disband 入口 / transfer 锚点；hold 流向 Pool[s, τ]。
            //   2) Per-tier fan-in：G[s, tier, τ] → Pool[s, τ+1]，一次性付清 (T-τ) ticks 的 wage + strategic（避免双重计费）。
            //   3) Pool 转移：Pool[s, τ] → Pool[s, τ+1]，cap=perCityCapacity[s] 即真正总量约束；cost=K (timeUnits=1)。
            //   4) Pool 终点：Pool[s, T] → superSink，cap=BigCap, cost=0（保留 horizon 终末 retain 语义）。
            //   5) 遣散：G[s, tier, 0] → disbandGate[s, 0]，cost=T*K (跳过 hold cost)。in-flight τ>0 不允许直接遣散。
            // 旧 (role × tier) cap/value 阶梯 + 1/6 锁死 + per-tier subCap binding 全部删除。
            int demandTierCapL0 = 0;
            foreach (var t in towns)
            {
                var s = t.Settlement;
                bool isCapital = s == capitalSettlement;
                bool besieged = s.IsUnderSiege;
                bool protectedCity = besieged || IsProtectedFromDisband(s, cfg);

                int settlementCapacity = perCityCapacity.TryGetValue(s, out var pcc) ? pcc : 0;

                // (a) 单节点 hold 边：G[s, τ] → G[s, τ+1]，cap=settlementCapacity，cost=K+WageOf(3)−valTau×PowerOf(3)
                //     tier=3 代表性常数，所有 tier 共享一条 hold 边；总量约束 = settlementCapacity。
                for (int tau = 0; tau < T; tau++)
                {
                    var defs = TierDefs(t, s, isCapital, tau, cfg, forecast, settlementCapacity);
                    if (defs.Count == 0) continue;
                    float valTau = defs[0].Value;
                    int holdGold = WageOf(3);
                    int holdStrategic = ClampValue((int)Math.Round(valTau * PowerOf(3)), K);
                    var holdEc = new EdgeCost(gold: holdGold, timeUnits: 1, strategic: holdStrategic);
                    int from = G(s, tau), to = G(s, tau + 1);
                    AddE(from, to, settlementCapacity, holdEc, EdgeCat.Stay);
                    if (tau == 0)
                    {
                        // τ=0 hold 边 = 本城留驻总头数。
                        decodeInfo[(from, to)] = (DecodeKind.HoldingL0, s, null!, GenericTroopRole.Unknown, 0);
                        demandTierCapL0 += settlementCapacity;
                    }
                }

                // (b) 时域出口：G[s, T] → superSink。
                AddE(G(s, T), superSink, BigCap, EdgeCost.Zero, EdgeCat.Internal);

                // (c) 遣散：只在 τ=0。单节点 → 单 G[s, 0]→gate 边。
                if (!disbandGate.TryGetValue((s, 0), out var gate0))
                {
                    gate0 = next++;
                    disbandGate[(s, 0)] = gate0;
                    AddE(gate0, superSink, protectedCity ? 0 : disbandPerTickCap, EdgeCost.Zero, EdgeCat.Bypass);
                    AddE(gate0, superSink, BigCap, new EdgeCost(gold: overflowPenalty), EdgeCat.Bypass);
                }
                // 2026-05-29: +1 gold tie-breaker。recruit bypass cost = T·K；disband cost = T·K + 1。
                // 让 in-place recruit 在 pool 满时严格选 bypass 而非 disband——避免 executor 把"流量"
                // 当成 garrison 砍掉（in-place recruit 与 garrison 共享 G[s, 0] 节点，无法在 decode 时区分）。
                AddE(G(s, 0), gate0, BigCap, new EdgeCost(gold: 1, timeUnits: T), EdgeCat.Internal);
                decodeInfo[(G(s, 0), gate0)] = (DecodeKind.Disband, s, null!, GenericTroopRole.Unknown, 0);

                // [GARRISON-DIAG] 池化容量 + 每 tick 代表性成本。
                int diagHoldGold = WageOf(3);
                int diagValTau0 = (int)Math.Round(TierDefs(t, s, isCapital, 0, cfg, forecast, settlementCapacity)[0].Value * PowerOf(3));
                Logger.Info($"[GARRISON-DIAG] poolCap '{s.StringId}' cap={settlementCapacity} T={T} perTickHoldCost=K({K})+wage({diagHoldGold})-strat({diagValTau0})={K + diagHoldGold - diagValTau0} disbandCostPerHead={T * K}");
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
                        // 2026-05-29: 单节点 → 单 transfer 边 per (s, s', τ)。tier 身份不保留（执行端 LowestTierFirst 决定具体抽哪 tier）。
                        for (int tau = 0; tau + d <= T - 1; tau++)
                        {
                            int from = G(s, tau), to = G(s2, tau + d);
                            AddE(from, to, BigCap, txEc, EdgeCat.Transfer);
                            if (tau == 0) decodeInfo[(from, to)] = (DecodeKind.Transfer, s, s2, GenericTroopRole.Unknown, 0);
                        }
                    }
                }
            }

            // ── 卫队（Honor Guard）需求预处理 ──
            // 2026-05-29 fix: bucket key 砍 tier，按 role 单维聚合。模板里的高 tier 兵 (T5/T6 noble)
            // 与村庄 T1 入门兵在 sink 上同 key 命中；CountVillageVolunteersInTemplate 已用升级链匹配
            // 数对了头，sink-key 不该再 strict tier 锁死。
            // 总额受 HonorGuardCap 约束（hgCapitalSink → superSink 容量）。
            // 注意：HG demand 只在 allowRecruitment 路径下被消费（只有 village recruit 边能流到 HG sink）；
            //       不抑制 garrison 招募 —— bucket.Count 是上限，HG 与 garrison 竞争同一供给。
            int hgCapitalSink = -1;
            var hgBucketSink = new Dictionary<GenericTroopRole, int>();
            var hgAllowedTroopIds = new Dictionary<GenericTroopRole, HashSet<string>>();
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
                    // 把模板按 role 聚合：bucketTarget = Σ template[troopId].count（同 role 不同 tier 合并）；
                    // bucketDeficit = max(0, bucketTarget − inPool − inFlight)（per-troopId 减再求和）。
                    var bucketTarget  = new Dictionary<GenericTroopRole, int>();
                    var bucketDeficit = new Dictionary<GenericTroopRole, int>();
                    foreach (var kv in cfg.HonorGuardTemplate)
                    {
                        if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                        var co = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<CharacterObject>(kv.Key);
                        if (co == null || co.IsHero) continue;
                        var role = GenericTroopMatcher.GetRole(co);
                        if (role == GenericTroopRole.Unknown) continue;

                        bucketTarget[role] = (bucketTarget.TryGetValue(role, out var bt) ? bt : 0) + kv.Value;

                        int inPoolThisTroop    = CountTroopInRoster(hgPool?.MemberRoster, kv.Key);
                        int inFlightThisTroop  = CountHonorGuardInFlight(capitalSettlement, troopIdFilter: kv.Key);
                        int thisDeficit        = Math.Max(0, kv.Value - inPoolThisTroop - inFlightThisTroop);
                        bucketDeficit[role] = (bucketDeficit.TryGetValue(role, out var bd) ? bd : 0) + thisDeficit;

                        if (!hgAllowedTroopIds.TryGetValue(role, out var idSet))
                        {
                            idSet = new HashSet<string>();
                            hgAllowedTroopIds[role] = idSet;
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
                        Logger.Info($"[GARRISON-DIAG] HG sinks built: capital='{capitalSettlement.StringId}' globalRoom={hgGlobalRoom} byRole=[{string.Join(",", bucketDeficit.Select(kv => $"{kv.Key}={kv.Value}"))}] allowedTroopIds=[{string.Join(",", hgAllowedTroopIds.SelectMany(kv => kv.Value))}]");
                    }
                    else
                    {
                        Logger.Info($"[GARRISON-DIAG] HG sinks skipped: capital='{capitalSettlement.StringId}' globalRoom={hgGlobalRoom} bucketDeficitSum=0 (already at target)");
                    }
                }
                else
                {
                    Logger.Info($"[GARRISON-DIAG] HG sinks skipped: capital='{capitalSettlement.StringId}' globalRoom=0 (cap reached: inPool={hgInPoolTotal} inFlight={hgInFlightTotal} cap={cfg.HonorGuardCap})");
                }
            }

            // ── 招募(围城中的首府不招募)──
            // 两池迭代：garrison 走原有近距 + faction + 文化过滤池；HG 走全图无过滤池（含敌国）。
            // 两路独立 bucketize 与独立 origin —— HG 不与 garrison 共享 supply cap。
            // HG 边突破 etaV 闸（timeUnits=T 扁平，不经时间展开）；gold 加 etaV×HgDistanceGoldPerTick
            // 让 MCMF 在近远村都能供给同模板时偏好近村。
            long recruitStart = sw.ElapsedMilliseconds;
            if (allowRecruitment && !capitalSettlement.IsUnderSiege)
            {
                var capitalRule = ConfigurationManager.GetRuleFor(capitalTown) ?? TownGarrisonRule.CreateDefault();
                string? requiredCultureId = GenericTroopMatcher.ResolveRequiredCultureId(capitalRule, capitalTown);

                // InPlace：首府 notable 志愿兵（与改造前完全等价）。
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
                    int inPlaceRecruitCost = RecruitCostByTier(recTier);
                    // 2026-05-29: in-place 招募 arrival 固定 τ=0；旧的 [0, T-1] 多备份分支物理上无意义（arrival 不能延迟），
                    // 且 SSP 永远只挑最早一条（cost 最低）—— 多余的 63 条/桶 是纯 dead weight。
                    {
                        int to = Transit(0);
                        AddE(origin, to, bucket.Count, new EdgeCost(gold: inPlaceRecruitCost, timeUnits: 0), EdgeCat.Recruit);
                        decodeInfo[(origin, to)] = (DecodeKind.InPlace, capitalSettlement, null!, bucket.Role, 0);
                    }
                    AddE(origin, superSink, bucket.Count, new EdgeCost(timeUnits: T), EdgeCat.Bypass);
                }

                // ── garrison village 边（与改造前逐字节等价）──
                void AddGarrisonVillageEdges(Settlement village, TroopBucket bucket, int etaV, int recOverhead, int recRisk)
                {
                    if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) return;
                    int origin = next++;
                    AddE(superSource, origin, bucket.Count, EdgeCost.Zero, EdgeCat.Internal);
                    originSupply += bucket.Count;
                    int recTier = Math.Max(1, Math.Min(6, bucket.MinTier));
                    int villageRecruitCost = RecruitCostByTier(recTier);
                    int recGold = recOverhead + villageRecruitCost;
                    // 2026-05-29: village 招募 arrival 固定 etaV；旧 [etaV, T-1] 多备份分支同 in-place，物理上无意义。
                    {
                        int to = Transit(etaV);
                        var recEc = new EdgeCost(gold: recGold, timeUnits: etaV, pathRisk: recRisk);
                        AddE(origin, to, bucket.Count, recEc, EdgeCat.Recruit);
                        decodeInfo[(origin, to)] = (DecodeKind.Recruiter, village, null!, bucket.Role, 0);
                    }
                    AddE(origin, superSink, bucket.Count, new EdgeCost(timeUnits: T), EdgeCat.Bypass);
                }

                // ── HG village 边 ──
                // 与 garrison 关键差异：独立 origin / 不受 etaV 闸 / timeUnits=T 扁平 /
                // gold 加 etaV × HgDistanceGoldPerTick。
                void AddHonorGuardVillageEdges(Settlement village, TroopBucket bucket, int etaV, int recOverhead, int recRisk)
                {
                    if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) return;
                    // 2026-05-29 fix: HG key 砍 tier，按 role 单维查找；村庄 T1 入门兵也能命中模板的 T5/T6 sink。
                    if (!hgBucketSink.TryGetValue(bucket.Role, out int hgSinkNode)) return;
                    if (!hgAllowedTroopIds.TryGetValue(bucket.Role, out var allowedIds)) return;

                    int hgVillageSupply = CountVillageVolunteersInTemplate(village, allowedIds);
                    int hgEdgeCap = Math.Min(bucket.Count, hgVillageSupply);
                    if (hgEdgeCap <= 0) return;

                    int hgOrigin = next++;
                    AddE(superSource, hgOrigin, bucket.Count, EdgeCost.Zero, EdgeCat.Internal);
                    originSupply += bucket.Count;

                    // 招募成本按 bucket.MinTier 计算（村庄供给的最低 tier 兵种）；strategic 用 bucket 平均。
                    int recTier = Math.Max(1, Math.Min(6, bucket.MinTier));
                    int villageRecruitCost = RecruitCostByTier(recTier);
                    int distanceGold = etaV * Math.Max(0, cfg.HgDistanceGoldPerTick);
                    int hgGold = recOverhead + villageRecruitCost + distanceGold;
                    // 2026-05-29 fix: hgStrategic 必须 > hgGold + risk*scale，否则 HG bypass (cost T·K) 比 HG path cheaper, solver 选 bypass。
                    // 给个保底：hgValueBase × power(tier) × 20 倍。默认 hgValueBase=2500、power(T6)=2.56 → 128000，远 > 任何 hgGold+risk 组合。
                    int hgStrategic = ClampValue(hgValueBase * PowerOf(recTier) * 20, K);

                    var hgEc = new EdgeCost(gold: hgGold, timeUnits: T, pathRisk: recRisk, strategic: hgStrategic);
                    AddE(hgOrigin, hgSinkNode, hgEdgeCap, hgEc, EdgeCat.Recruit);
                    decodeInfo[(hgOrigin, hgSinkNode)] =
                        (DecodeKind.HonorGuardRecruiter, village, capitalSettlement, bucket.Role, recTier);

                    AddE(hgOrigin, superSink, bucket.Count, new EdgeCost(timeUnits: T), EdgeCat.Bypass);
                    Logger.Info($"[GARRISON-DIAG] HG edge added village='{village.StringId}' role={bucket.Role} villageSupply={hgVillageSupply} bucketCount={bucket.Count} edgeCap={hgEdgeCap} etaV={etaV} gold={hgGold}");
                }

                var inFlightVillages = RecruitmentTopology.CollectInFlightRecruiterVillages(clan);
                var garrisonVillages = RecruitmentTopology.EnumerateRecruitmentVillages(capitalTown, clan, inFlightVillages);
                bool hgPathActive = hgCapitalSink >= 0;
                var hgVillages = hgPathActive
                    ? RecruitmentTopology.EnumerateRecruitmentVillagesForHG(capitalSettlement, clan, inFlightVillages)
                    : new List<Settlement>();
                var garrisonSet = new HashSet<Settlement>(garrisonVillages);
                var hgSet = hgPathActive ? new HashSet<Settlement>(hgVillages) : new HashSet<Settlement>();
                var processed = new HashSet<Settlement>();

                foreach (var village in garrisonVillages.Concat(hgVillages))
                {
                    if (village == null) continue;
                    if (!processed.Add(village)) continue;

                    int etaV = EtaTicks(2 * RoutingDistance(village, capitalSettlement), tickHours);
                    int recOverhead = Math.Max(0, thresholds.McmfRecruiterOverhead);
                    int recRisk = RouteRiskSurcharge(village, capitalSettlement, cfg);

                    if (garrisonSet.Contains(village) && etaV <= T - 1)
                    {
                        foreach (var bucket in RecruitmentTopology.BucketizeCharacters(
                                     RecruitmentTopology.EnumerateVolunteerTroops(village),
                                     capitalRule,
                                     requiredCultureId))
                        {
                            AddGarrisonVillageEdges(village, bucket, etaV, recOverhead, recRisk);
                        }
                    }

                    if (hgSet.Contains(village))
                    {
                        foreach (var bucket in RecruitmentTopology.BucketizeCharacters(
                                     RecruitmentTopology.EnumerateVolunteerTroops(village),
                                     rule: null,
                                     requiredCultureId: null))
                        {
                            AddHonorGuardVillageEdges(village, bucket, etaV, recOverhead, recRisk);
                        }
                    }
                }
            }
            recruitMs = sw.ElapsedMilliseconds - recruitStart;

            // ── transit 出边:招募兵留首府 / 转发分支 ──
            // 2026-05-29: transit key 去 tier 维。转发按 (s, s', τ) 单边；decode 的 Transfer 指令 role/tier=Unknown/0。
            foreach (var kv in transitNode.ToList())
            {
                int tau = kv.Key;
                int tn = kv.Value;
                // 留首府:Transit[τ] → G[capital, τ]。
                AddE(tn, G(capitalSettlement, tau), BigCap, EdgeCost.Zero, EdgeCat.Internal);
                // 转发分支:Transit[τ] → G[branch, τ+d]。
                if (!allowTransfers) continue;
                foreach (var t in towns)
                {
                    var s = t.Settlement;
                    if (s == capitalSettlement || s.IsUnderSiege) continue;
                    int d = EtaTicks(RoutingDistance(capitalSettlement, s), tickHours);
                    if (tau + d > T - 1) continue;
                    int fwdOverhead = Math.Max(0, thresholds.McmfTransferOverhead);
                    int fwdRisk = RouteRiskSurcharge(capitalSettlement, s, cfg);
                    int to = G(s, tau + d);
                    AddE(tn, to, BigCap, new EdgeCost(gold: fwdOverhead, timeUnits: d, pathRisk: fwdRisk), EdgeCat.Transfer);
                    if (tau == 0) decodeInfo[(tn, to)] = (DecodeKind.Transfer, capitalSettlement, s, GenericTroopRole.Unknown, 0);
                }
            }

            // ── Solve(分帧 SSP)── swSolve 只累计 MoveNext 内的 CPU,排除 yield 帧间隙。
            long buildMs = sw.ElapsedMilliseconds;
            if (clampedPublicCostEdges > 0)
            {
                Logger.Warn($"UnifiedGarrisonSolver: clamped {clampedPublicCostEdges} negative public edge cost(s) to 0; mostNegative={mostNegativePublicCost}. Consider lowering strategic rewards or CostWeightStrategic.");
            }

            // PR-5'(2026-05-24): MERGED-TIERS diagnostic removed (used AdequateFor + MinGarrisonFloor).
            // 2026-05-28: TierDefs cap = perCityCapacity (预算分配), 见 SolveCoroutine 头部 perCityCapacity 块。

            var swSolve = new System.Diagnostics.Stopwatch();
            int sspFrames = 0;
            // PR-6 (2026-05-28): time-budget driven SSP scheduling.
            // SolveStepwise(yieldEvery=1) → 每次 MoveNext 跑 1 次增广,本循环按累计 ms 决定 yield。
            int wallBudgetMs = Math.Max(50, Math.Min(5000, cfg.SolveBudgetWallMs));
            int frameBudgetMs = Math.Max(1, Math.Min(16, cfg.SolvePerFrameBudgetMs));
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var frameSw = System.Diagnostics.Stopwatch.StartNew();
            bool overWallBudget = false;
            var sspIt = graph.SolveStepwise(superSource, superSink, yieldEvery: 1);
            while (true)
            {
                swSolve.Start();
                bool more = sspIt.MoveNext();
                swSolve.Stop();
                if (!more) break;
                if (overWallBudget) continue;                                      // 已超总预算 → 同步排干
                if (totalSw.ElapsedMilliseconds >= wallBudgetMs)
                {
                    overWallBudget = true;
                    Logger.Warn(
                        $"SSP solve over wall budget {wallBudgetMs}ms — switching to sync drain " +
                        $"(clan={clan.StringId} elapsed={totalSw.ElapsedMilliseconds}ms)");
                    continue;
                }
                if (frameSw.ElapsedMilliseconds >= frameBudgetMs)
                {
                    sspFrames++;
                    yield return null;
                    frameSw.Restart();   // Reset on resume; next MoveNext starts a fresh window
                }
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
            // 2026-05-29 B-pool: transfers key 改 (src, dst, tier)，role 维度删。
            var transfers = new Dictionary<(Settlement Src, Settlement Dst, int Tier), int>();
            // [GARRISON-DIAG] 按 (settlement, role, tier) 累计 disband，便于看清 solver 是把哪几桶遣散光的。
            var disbandPerBucket = new Dictionary<(Settlement S, GenericTroopRole Role, int Tier), int>();

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
                        // [GARRISON-DIAG] B-pool: role 维度删，按 (settlement, tier) 桶累计。
                        {
                            var bk = (di.A, di.Role, di.Tier);  // di.Role = Unknown sentinel
                            disbandPerBucket[bk] = (disbandPerBucket.TryGetValue(bk, out var pb) ? pb : 0) + f;
                        }
                        break;
                    case DecodeKind.Recruiter:
                        result.Instructions.Add(new RecruiterPartyInstruction(
                            capitalTown, capitalSettlement, di.A, di.Role, f));
                        break;
                    case DecodeKind.HonorGuardRecruiter:
                        // 统一指令：HG 调度走 RecruiterPartyInstruction(Mode=HonorGuardPrecise) +
                        // 精确模板快照（troopId → desiredCount，整模板原样附带）。执行端 per-village
                        // 用模板算 deficit 并做 IG 升级链匹配（TroopTemplateMatcher.PickPreciseTemplateMatch）。
                        {
                            var hgTemplate = cfg.HonorGuardTemplate != null
                                ? new Dictionary<string, int>(cfg.HonorGuardTemplate, StringComparer.Ordinal)
                                : new Dictionary<string, int>(StringComparer.Ordinal);
                            result.Instructions.Add(new RecruiterPartyInstruction(
                                capitalTown, capitalSettlement, di.A, di.Role, f,
                                mode: RecruiterMode.HonorGuardPrecise,
                                preciseTemplate: hgTemplate));
                        }
                        break;
                    case DecodeKind.InPlace:
                        result.Instructions.Add(new InPlaceRecruitInstruction(capitalSettlement, di.Role, f));
                        break;
                    case DecodeKind.Transfer:
                        // 2026-05-29 B-pool: di.Role = Unknown sentinel；按 (src, dst, tier) 聚合，避免不同 tier 流量被同 key 吞掉。
                        var key = (di.A, di.B, di.Tier);
                        transfers[key] = (transfers.TryGetValue(key, out var c) ? c : 0) + f;
                        break;
                }
            }

            result.DemandFilled = result.Target.Values.Sum();
            foreach (var kv in transfers)
                result.Instructions.Add(new TransferPartyInstruction(
                    kv.Key.Src, kv.Key.Dst, GenericTroopRole.Unknown, kv.Value));

            long decodeMs = sw.ElapsedMilliseconds - beforeDecode;
            Logger.Info(
                $"MERGED-TIMING clan={clan.StringId} T={T} nodes={result.NodeCount} edges={result.EdgeCount} "
              + $"build={buildMs}ms (recruit={recruitMs}ms) solve={solveMs}ms (sspFrames={sspFrames}) "
              + $"decode={decodeMs}ms wall={sw.ElapsedMilliseconds}ms");

            // [GARRISON-DIAG] 2026-05-29: tier 维度从 solver 全删，disband 只输出 per-settlement total；
            // 具体 tier 由执行端 LowestTierFirst 自行决定。
            if (disbandPerBucket.Count > 0)
            {
                var bySettlement = disbandPerBucket
                    .GroupBy(kv => kv.Key.S)
                    .OrderByDescending(g => g.Sum(kv => kv.Value));
                foreach (var grp in bySettlement)
                {
                    int total = grp.Sum(kv => kv.Value);
                    Logger.Info($"[GARRISON-DIAG] disband-plan '{grp.Key.StringId}' total={total} (executor LowestTierFirst will pick actual tiers)");
                }
            }

            // PR-5'(2026-05-24): MERGED-GAP diagnostic removed (used AdequateFor + MinGarrisonFloor).
        }
        finally
        {
            onResult?.Invoke(result);
        }
    }

    /// <summary>
    /// 单段 cap = perCityCapacity (上层算的"预算+威胁+战略约束下应养的兵"); value = BaseValuePerTier × threat × strat。
    /// 2026-05-28: cap 由 SolveCoroutine 算的 perCityCapacity 传入,替代旧 hardCap×TargetFraction 公式。
    /// PR-5'(2026-05-24): 删除了 floor/core/surplus 三段逻辑、CoreTierCount、CoreDimRange、CoreDimMidpoint、
    /// ValueFloorBase、ValueCoreBase、SurplusEdgeCost、AdequateFor 依赖。
    /// </summary>
    private static List<(int Cap, float Value)> TierDefs(
        Town t, Settlement s, bool isCapital, int tau,
        FiscalAutonomyConfig cfg, IHorizonForecast forecast, int perCityCapacity)
    {
        float threat = ThreatWeightOf(SafeThreatAt(forecast, s, tau), cfg);
        float strat = StrategicWeight(s, isCapital, cfg);
        float value = cfg.BaseValuePerTier * threat * strat;
        return new List<(int, float)> { (perCityCapacity, value) };
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

    // EtaTicks 参考速度的运行时 sample 缓存。
    // 第一次 solve 时从 ST party 池采一个 MobileParty.Speed(单位 units/in-game hour),
    // 缓存 24 in-game hour 后重 sample。零配置、自校准。
    // 没有可用 ST party(冷启动)→ fallback 5.4 units/hour(vanilla 4.5 base × ST +20% 加成的最佳猜测)。
    private static float _sampledSpeedPerHour = 0f;
    private static CampaignTime _sampledAt;
    private const float ReferenceSpeedFallbackPerHour = 4.5f * (1f + STPartySpeedModel.SpeedBonusFactor);
    private const float ReferenceSpeedTtlHours = 24f;

    /// <summary>
    /// 寻路距离换算成 tick 数。单位口径与 mod 其他 ETA 计算
    /// (<see cref="SovereignTowns.Coordination.BaseSettlementVisitScheduler"/>.ComputeEtaHours、
    /// <see cref="SovereignTowns.Evaluators.HostilePartyScanner"/>)对齐:
    /// distance(units)÷ speed(units/in-game hour)÷ (24 / tickHours) = ticks。
    ///
    /// 参考速度由 <see cref="GetReferenceSpeedPerHour"/> 运行时 sample 真实 ST party 得出,
    /// 自动适应 vanilla 速度模型 / 季节 / mod 修改;无 ST party 时 fallback 5.4 units/hour。
    /// </summary>
    private static int EtaTicks(int distance, int tickHours)
    {
        float perHour = GetReferenceSpeedPerHour();
        float perTick = Math.Max(0.1f, perHour * tickHours);  // units/tick
        return Math.Max(1, (int)Math.Round(Math.Max(0, distance) / perTick));
    }

    /// <summary>
    /// 取 EtaTicks 参考速度(units/in-game hour)。
    /// 缓存 <see cref="ReferenceSpeedTtlHours"/> in-game hour,超时重 sample 一支活的 ST party。
    /// 无 ST party 时返回 fallback 常量。
    /// </summary>
    private static float GetReferenceSpeedPerHour()
    {
        try
        {
            bool needResample = _sampledSpeedPerHour <= 0f;
            if (!needResample)
            {
                float ageHours = (float)(CampaignTime.Now - _sampledAt).ToHours;
                if (ageHours < 0f || ageHours >= ReferenceSpeedTtlHours) needResample = true;
            }
            if (!needResample) return _sampledSpeedPerHour;

            // sample:任意一支活的 ST party 的当前 Speed
            var parties = MobileParty.AllCustomParties;
            if (parties != null)
            {
                foreach (var p in parties)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.PartyComponent is not StPartyComponent) continue;
                    float speed = p.Speed;
                    if (speed > 0.1f)
                    {
                        _sampledSpeedPerHour = speed;
                        _sampledAt = CampaignTime.Now;
                        Logger.Info($"EtaTicks reference speed re-sampled: {speed:F2} units/hour from '{p.StringId}'");
                        return _sampledSpeedPerHour;
                    }
                }
            }
            // 没找到可采样的 party → fallback;但不写入 _sampledSpeedPerHour
            // (保留 0f,下次还会尝试 sample,而不是缓存 fallback)
            return ReferenceSpeedFallbackPerHour;
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetReferenceSpeedPerHour failed: {ex.Message} — fallback {ReferenceSpeedFallbackPerHour:F2}/h");
            return ReferenceSpeedFallbackPerHour;
        }
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

    /// <summary>统计 <paramref name="capital"/> 的在飞卫队征兵队（StRecruiterPartyComponent + Mode=HonorGuardPrecise）
    /// 中可能补充进卫队池的兵数。
    /// troopIdFilter=null → 用 _tripCountTarget 估算总容量（用于 hgGlobalRoom 计算）。
    /// troopIdFilter 指定 → 扫该队 MemberRoster 中实际持有的该 troopId 兵数 +
    /// 该队 PreciseTemplate 对应 entry 中尚未招满的预期数（按已招兵种 / 模板 deficit 折算）。
    /// 实现上偏保守：按 template[troopId] 计入预期（避免重复派遣），与原 HG 单一 troopId 行为等价（多 troopId 模板时
    /// 略偏保守但不会超估）。</summary>
    private static int CountHonorGuardInFlight(Settlement capital, string? troopIdFilter)
    {
        if (capital == null) return 0;
        try
        {
            int total = 0;
            foreach (var p in MobileParty.All)
            {
                if (p?.PartyComponent is not SovereignTowns.Parties.StRecruiterPartyComponent rc) continue;
                if (rc.Mode != RecruiterMode.HonorGuardPrecise) continue;
                if (rc.HomeSettlementOrNull != capital) continue;
                if (troopIdFilter == null)
                {
                    // 总在飞 = 该队剩余招募人数目标（_tripCountTarget − recruited，clamp ≥0）。
                    int remaining = Math.Max(0, /*_tripCountTarget*/ EstimateHGRemaining(rc));
                    total += remaining;
                }
                else
                {
                    // 该 troopId 视角：现持有 + 模板中该 entry 的目标数（保守预期，避免重复派遣同一 entry）。
                    int held = CountTroopInRoster(p.MemberRoster, troopIdFilter);
                    int templated = rc.PreciseTemplate != null
                        && rc.PreciseTemplate.TryGetValue(troopIdFilter, out var v) ? Math.Max(0, v) : 0;
                    total += Math.Min(templated, held + EstimateHGRemaining(rc));
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

    /// <summary>估算 HG 征兵队剩余可招人数（_tripCountTarget − _recruitedThisTrip）。
    /// _tripCountTarget 在 SetItinerary 时设入；公开 accessor 上有 RecruitedThisTrip，trip 目标无现成 accessor，
    /// 这里用一个保守上限：当前 MemberRoster.TotalManCount 不再增长的假设下取 0。
    /// 准确值需扩接口；目前 callers 主要靠 troopIdFilter 路径，全总额仅用于 hgGlobalRoom 软上限。</summary>
    private static int EstimateHGRemaining(SovereignTowns.Parties.StRecruiterPartyComponent rc)
    {
        try { return Math.Max(0, rc.TripCountRemaining); }
        catch { return 0; }
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

    /// <summary>统计 village 中各 notable 的 VolunteerTypes 与 <paramref name="allowedTroopIds"/>
    /// 的升级链可达交集大小，作为该村对此卫队 bucket 的供给容量。
    /// 升级链匹配（CanUpgradeToTarget）与执行端 PickPreciseTemplateMatch 同口径——任一 notable 槽位
    /// 通过 UpgradeTargets 树能到达 allowed troopId 即计入（含 source == target 情形）。</summary>
    private static int CountVillageVolunteersInTemplate(Settlement village, HashSet<string> allowedTroopIds)
    {
        if (village?.Notables == null || allowedTroopIds.Count == 0) return 0;
        try
        {
            var targets = new List<CharacterObject>(allowedTroopIds.Count);
            foreach (var id in allowedTroopIds)
            {
                var co = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<CharacterObject>(id);
                if (co != null) targets.Add(co);
            }
            if (targets.Count == 0) return 0;

            int count = 0;
            foreach (var notable in village.Notables)
            {
                if (notable?.CanHaveRecruits != true) continue;
                var vt = notable.VolunteerTypes;
                if (vt == null) continue;
                foreach (var co in vt)
                {
                    if (co == null) continue;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (SovereignTowns.Evaluators.TroopTemplateMatcher.CanUpgradeToTarget(co, targets[i]))
                        {
                            count++;
                            break;
                        }
                    }
                }
            }
            return count;
        }
        catch { return 0; }
    }
}
