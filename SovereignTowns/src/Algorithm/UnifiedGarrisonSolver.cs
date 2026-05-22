using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Algorithm;

/// <summary>
/// 方案2 合并 solver 的 M1 产出 —— flow 统计 + 按边语义类别的流量汇总。
/// decode(Target / 指令)留待 M2;value 重定标留待 M3;两段 bypass 留待 M4。
/// 见 audits/mcmf-merge-design.md。
/// </summary>
public sealed class UnifiedSolverResult
{
    /// <summary>true = 求解完整跑完(未被前置守卫提前返回)。</summary>
    public bool Ran { get; set; }

    public int SettlementCount { get; set; }
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }

    /// <summary>Σ 所有 origin 桶大小(superSource 出边容量之和)。</summary>
    public int OriginSupply { get; set; }
    public int BudgetTroopCap { get; set; }
    /// <summary>Σ 所有 demand-tier 节点容量。</summary>
    public int DemandTierCapacity { get; set; }

    public int TotalFlow { get; set; }
    public long TotalCost { get; set; }
    /// <summary>Σ 流入所有 demand-tier 的流量(= Σ demand-tier→budgetGate 流)。</summary>
    public int DemandFilled { get; set; }

    public int StayFlow { get; set; }
    public int TransferFlow { get; set; }
    public int RecruitFlow { get; set; }
    public int BypassFlow { get; set; }

    /// <summary>parallel-run 差异日志行。M2 decode 落地后会再加 Target / 指令级对账。</summary>
    public string DiffLine(string clanId)
        => $"clan={clanId} settlements={SettlementCount} nodes={NodeCount} edges={EdgeCount} "
         + $"originSupply={OriginSupply} budgetCap={BudgetTroopCap} demandCap={DemandTierCapacity} "
         + $"flow={TotalFlow} demandFilled={DemandFilled} unmet={Math.Max(0, DemandTierCapacity - DemandFilled)} "
         + $"stay={StayFlow} recruit={RecruitFlow} transfer={TransferFlow} bypass={BypassFlow}";
}

/// <summary>
/// 方案2 合并 solver —— 把 Pass A(分配)与 Pass B(路由)合并为单一 MinCostFlow 图、
/// 单次求解。M1 范围:建图 + Solve + 按边语义类别的流量统计。**不 decode、不派发。**
/// 精确规格见 audits/mcmf-merge-design.md §1。
///
/// M1 刻意不做(留待后续里程碑):
///   - decode 成 Target / DispatchInstruction —— M2。
///   - value 标度重定标(M1 沿用 Pass A 的 20M 量级,routing 沦为噪声是预期的)—— M3。
///   - 两段 bypass(每 tick 遣散速率限制)—— M4。当前保护态 origin 仅"不连 bypass";
///     若某城现有兵超 hardCap + 预算闸,该 origin 流量无法全数路由 → flow 非最大,
///     不崩溃,M4 的溢出段修复。
/// </summary>
public static class UnifiedGarrisonSolver
{
    /// <summary>费用偏移。M1 沿用 Pass A 的 CostOffset 量级;M3 value 重定标时再调小。
    /// 须 ≥ 任何 tier value 可能取到的最大值,使 K − value ≥ 0(MinCostFlow 硬约束)。</summary>
    private const int K = 20_000_000;

    private const float CoreDimRange = 0.8f;
    private const float CoreDimMidpoint = 0.5f;
    private const float ProsperityNormalizer = 4000f;
    private const float CapitalStrategicBonus = 1.3f;

    private enum OriginKind { Garrison, InPlace, Village }

    /// <summary>边的语义类别 —— Solve 后按此聚合 EdgeFlows,产出差异日志(无需 decode)。</summary>
    private enum EdgeCat { Internal, Stay, Transfer, Recruit, Bypass }

    private sealed class DemandTier
    {
        public int Node;
        public Settlement Settlement = null!;
        public bool IsCapital;
        public GenericTroopRole Role;   // 首府:真实 role;分支:Infantry 占位
        public int Capacity;
        public int CostInto;            // K − clamp(round(value))
    }

    private sealed class Origin
    {
        public int Node;
        public OriginKind Kind;
        public Settlement Settlement = null!;   // Garrison/InPlace:该城;Village:该村
        public GenericTroopRole Role;
        public int Count;
        public bool Protected;                  // 仅 Garrison:siege/risk/flag-off → 不可遣散
    }

    public static UnifiedSolverResult Solve(CapitalManager manager, Settlement capitalSettlement)
    {
        var result = new UnifiedSolverResult();
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null || capitalSettlement == null) return result;

            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            var thresholds = ConfigurationManager.Current?.Thresholds ?? new PartyThresholds();

            var towns = clan.Fiefs
                .Where(t => t?.Settlement != null && t.Settlement.IsActive && (t.IsTown || t.IsCastle))
                .ToList();
            if (towns.Count == 0) return result;

            var capitalTown = towns.FirstOrDefault(t => t.Settlement == capitalSettlement);
            if (capitalTown == null)
            {
                Logger.Debug($"UnifiedGarrisonSolver: capital '{capitalSettlement.StringId}' not among clan fiefs");
                return result;
            }

            int wagePerTroop = Math.Max(1, GarrisonAllocationSolver.WagePerTroopAtMaxTier(manager!, towns));
            long clanWageBudget = GarrisonAllocationSolver.ClanWageBudget(manager!, towns, cfg, wagePerTroop);
            int budgetTroopCap = (int)Math.Max(0L, Math.Min(int.MaxValue, clanWageBudget / wagePerTroop));

            var graph = new MinCostFlow();
            var edgeCat = new Dictionary<(int From, int To), EdgeCat>();
            int edgeCount = 0;
            void AddE(int from, int to, int cap, int cost, EdgeCat cat)
            {
                if (cap <= 0) return;
                graph.AddEdge(from, to, cap, cost);
                edgeCat[(from, to)] = cat;
                edgeCount++;
            }

            int next = 1;
            int superSource = next++, superSink = next++, budgetGate = next++, bypass = next++;

            // capital-transit:每首府每 role 一个 —— role-blind 的单 transit 会让 Cav 招募兵
            // 经 transit 填进 Inf 缺口(见 design §1.1)。
            var transit = new Dictionary<GenericTroopRole, int>();
            foreach (var role in MatchPolicy.Roles)
                transit[role] = next++;

            // 总驻军预算闸:demand-tier 全部经此汇入 superSink,预算口径在头数上限。
            AddE(budgetGate, superSink, budgetTroopCap, 0, EdgeCat.Internal);

            // ── demand-tier 节点(每 settlement;首府按 role 拆,分支单一占位 role)──
            var tiers = new List<DemandTier>();
            int demandTierCapacity = 0;
            foreach (var t in towns)
            {
                var s = t.Settlement;
                bool isCapital = s == capitalSettlement;
                int floor = Math.Max(0, cfg.MinGarrisonFloor);
                int hardCap = GarrisonAllocationSolver.HardCapFor(t, cfg);
                int adequate = GarrisonAllocationSolver.AdequateFor(t, cfg, floor, hardCap);
                float threat = ThreatWeight(s);
                float strat = StrategicWeight(s, isCapital);
                int coreSpan = Math.Max(0, adequate - floor);
                int coreCount = Math.Max(1, cfg.CoreTierCount);
                int surplusSpan = Math.Max(0, hardCap - adequate);

                // (tier 容量, tier value) 序列 —— 沿用 Pass A 的 floor/core/surplus 分层口径。
                var tierDefs = new List<(int Cap, float Value)>();
                if (floor > 0)
                    tierDefs.Add((floor, cfg.ValueFloorBase * threat * strat));
                for (int k = 0; k < coreCount; k++)
                {
                    int cap = coreSpan * (k + 1) / coreCount - coreSpan * k / coreCount;
                    if (cap <= 0) continue;
                    float dim = 1.0f - CoreDimRange * ((k + CoreDimMidpoint) / coreCount);
                    tierDefs.Add((cap, cfg.ValueCoreBase * dim * threat * strat));
                }
                if (surplusSpan > 0)
                    tierDefs.Add((surplusSpan, -Math.Max(1, cfg.SurplusEdgeCost)));

                var rule = isCapital
                    ? (ConfigurationManager.GetRuleFor(t) ?? TownGarrisonRule.CreateDefault())
                    : null;

                foreach (var (cap, value) in tierDefs)
                {
                    int costInto = K - ClampValue(value);
                    if (isCapital)
                    {
                        // 首府每 tier 的总容量经 MatchPolicy.DesiredCount 按规则比例拆到各 role。
                        foreach (var role in MatchPolicy.Roles)
                        {
                            int roleCap = MatchPolicy.DesiredCount(rule!, role, cap);
                            if (roleCap <= 0) continue;
                            int node = next++;
                            tiers.Add(new DemandTier
                            {
                                Node = node, Settlement = s, IsCapital = true,
                                Role = role, Capacity = roleCap, CostInto = costInto,
                            });
                            AddE(node, budgetGate, roleCap, 0, EdgeCat.Internal);
                            demandTierCapacity += roleCap;
                        }
                    }
                    else
                    {
                        int node = next++;
                        tiers.Add(new DemandTier
                        {
                            Node = node, Settlement = s, IsCapital = false,
                            Role = GenericTroopRole.Infantry, Capacity = cap, CostInto = costInto,
                        });
                        AddE(node, budgetGate, cap, 0, EdgeCat.Internal);
                        demandTierCapacity += cap;
                    }
                }
            }

            // ── origin 节点 ──
            var origins = new List<Origin>();
            int originSupply = 0;
            void AddOrigin(OriginKind kind, Settlement s, GenericTroopRole role, int count, bool prot)
            {
                if (count <= 0 || role == GenericTroopRole.Unknown) return;
                int node = next++;
                origins.Add(new Origin
                {
                    Node = node, Kind = kind, Settlement = s,
                    Role = role, Count = count, Protected = prot,
                });
                AddE(superSource, node, count, 0, EdgeCat.Internal);
                originSupply += count;
            }

            // 现有驻军 origin:每 (settlement, role) 一桶。围城中的城整体跳过 —— 驻军固守,
            // 既不外调也不接收(复刻 Pass B 的 siege 隔离;接收侧见 origin 出边)。
            foreach (var t in towns)
            {
                var s = t.Settlement;
                if (s.IsUnderSiege) continue;
                bool prot = IsProtectedFromDisband(s, cfg);
                foreach (var bucket in MatchPolicy.Bucketize(t.GarrisonParty?.MemberRoster))
                    AddOrigin(OriginKind.Garrison, s, bucket.Role, bucket.Count, prot);
            }

            // 招募 origin:围城中的首府不招募(复刻 Pass B)。
            if (!capitalSettlement.IsUnderSiege)
            {
                // 首府 InPlace(notable 志愿兵)。
                foreach (var bucket in SupplyDemandGraph.BucketizeCharacters(
                             SupplyDemandGraph.EnumerateVolunteerTroops(capitalSettlement)))
                    AddOrigin(OriginKind.InPlace, capitalSettlement, bucket.Role, bucket.Count, false);

                // per-village 候选村(沿用 Phase 1 枚举,在飞征兵队目标村排除)。
                var inFlight = SupplyDemandGraph.CollectInFlightRecruiterVillages(clan);
                foreach (var village in SupplyDemandGraph.EnumerateRecruitmentVillages(capitalTown, clan, inFlight))
                    foreach (var bucket in SupplyDemandGraph.BucketizeCharacters(
                                 SupplyDemandGraph.EnumerateVolunteerTroops(village)))
                        AddOrigin(OriginKind.Village, village, bucket.Role, bucket.Count, false);
            }

            // bypass → superSink:容量 ≥ 总 origin 供给,保证每个非保护 origin 都有出路。
            AddE(bypass, superSink, Math.Max(1, originSupply), 0, EdgeCat.Internal);

            // ── origin 出边 ──
            foreach (var o in origins)
            {
                if (o.Kind == OriginKind.Garrison)
                {
                    foreach (var dt in tiers)
                    {
                        // 首府 demand-tier 须 role 匹配;分支 demand-tier 占位 role,接受任意 role。
                        if (dt.IsCapital && dt.Role != o.Role) continue;
                        // 不向围城中的城调兵(复刻 Pass B 的 siege 隔离)。
                        if (dt.Settlement.IsUnderSiege) continue;
                        int cap = Math.Min(o.Count, dt.Capacity);
                        if (dt.Settlement == o.Settlement)
                        {
                            AddE(o.Node, dt.Node, cap, dt.CostInto, EdgeCat.Stay);
                        }
                        else
                        {
                            int routing = RoutingDistance(o.Settlement, dt.Settlement)
                                          + Math.Max(0, thresholds.McmfTransferOverhead);
                            AddE(o.Node, dt.Node, cap, routing + dt.CostInto, EdgeCat.Transfer);
                        }
                    }
                    // 保护态(siege / risk≥High / flag-off)不连 bypass → 永不被遣散。
                    // M4 在此基础上加两段 bypass(遣散速率限制)。
                    if (!o.Protected)
                        AddE(o.Node, bypass, o.Count, K, EdgeCat.Bypass);
                }
                else
                {
                    // 招募 origin 经其首府的 transit 入图(role 专属 transit)。
                    int routing = o.Kind == OriginKind.Village
                        ? RoutingDistance(o.Settlement, capitalSettlement)
                          + Math.Max(0, thresholds.McmfRecruiterOverhead)
                        : 0;   // InPlace 原地招募,routing 0
                    AddE(o.Node, transit[o.Role], o.Count, routing, EdgeCat.Recruit);
                    // 未被招募的志愿兵名额走 bypass(无指令)。
                    AddE(o.Node, bypass, o.Count, K, EdgeCat.Bypass);
                }
            }

            // ── transit 出边 ──
            foreach (var role in MatchPolicy.Roles)
            {
                int tn = transit[role];
                foreach (var dt in tiers)
                {
                    if (dt.IsCapital)
                    {
                        if (dt.Role != role) continue;
                        // 招募兵留首府:招募指令已由 origin→transit 边产出,此边无指令(Internal)。
                        AddE(tn, dt.Node, dt.Capacity, dt.CostInto, EdgeCat.Internal);
                    }
                    else
                    {
                        // 不向围城中的分支转发(复刻 Pass B 的 siege 隔离)。
                        if (dt.Settlement.IsUnderSiege) continue;
                        // 招募兵经首府转发分支 = transit→分支 demand,decode 成 Transfer。
                        int routing = RoutingDistance(capitalSettlement, dt.Settlement)
                                      + Math.Max(0, thresholds.McmfTransferOverhead);
                        AddE(tn, dt.Node, dt.Capacity, routing + dt.CostInto, EdgeCat.Transfer);
                    }
                }
            }

            // ── Solve ──
            var flow = graph.Solve(superSource, superSink);

            result.Ran = true;
            result.SettlementCount = towns.Count;
            result.NodeCount = next - 1;
            result.EdgeCount = edgeCount;
            result.OriginSupply = originSupply;
            result.BudgetTroopCap = budgetTroopCap;
            result.DemandTierCapacity = demandTierCapacity;
            result.TotalFlow = flow.TotalFlow;
            result.TotalCost = flow.TotalCost;

            foreach (var kv in flow.EdgeFlows)
            {
                int f = kv.Value;
                if (f <= 0) continue;
                if (kv.Key.To == budgetGate) result.DemandFilled += f;
                if (!edgeCat.TryGetValue(kv.Key, out var cat)) continue;
                switch (cat)
                {
                    case EdgeCat.Stay: result.StayFlow += f; break;
                    case EdgeCat.Transfer: result.TransferFlow += f; break;
                    case EdgeCat.Recruit: result.RecruitFlow += f; break;
                    case EdgeCat.Bypass: result.BypassFlow += f; break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UnifiedGarrisonSolver.Solve failed", ex);
        }
        return result;
    }

    /// <summary>把 tier value(可负)round + clamp 到 [−K, K],保证 K − value ∈ [0, 2K]。</summary>
    private static int ClampValue(float value)
    {
        int v = (int)Math.Round(value);
        if (v > K) v = K;
        if (v < -K) v = -K;
        return v;
    }

    /// <summary>threat 乘子:RiskAssessmentService 等级映射 Safe .5 / Low 1 / Med 2 / High 4 / Crit 8。
    /// 与 Pass A 同口径(M3 重定标不动 threat/strat 这类无量纲乘子)。</summary>
    private static float ThreatWeight(Settlement s)
    {
        try
        {
            switch (RiskAssessmentService.Assess(s).Level)
            {
                case RiskLevel.Safe: return 0.5f;
                case RiskLevel.Low: return 1.0f;
                case RiskLevel.Medium: return 2.0f;
                case RiskLevel.High: return 4.0f;
                case RiskLevel.Critical: return 8.0f;
                default: return 1.0f;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"UnifiedGarrisonSolver.ThreatWeight failed for '{s?.StringId}'", ex);
            return 1.0f;
        }
    }

    /// <summary>strategic 乘子:(首府 ? 1.3 : 1.0) × clamp(Prosperity / 4000, 0.5, 1.5)。</summary>
    private static float StrategicWeight(Settlement s, bool isCapital)
    {
        float prosperity = 0f;
        try { if (s != null && s.IsTown && s.Town != null) prosperity = s.Town.Prosperity; }
        catch { prosperity = 0f; }

        float pf = prosperity / ProsperityNormalizer;
        if (pf < 0.5f) pf = 0.5f;
        if (pf > 1.5f) pf = 1.5f;
        return (isCapital ? CapitalStrategicBonus : 1.0f) * pf;
    }

    /// <summary>该城现有驻军是否禁止遣散:功能开关关 / 围城 / 高危。镜像 DisbandExcessGarrisons Gate 1/3/4。</summary>
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

    private static int RoutingDistance(Settlement a, Settlement b)
    {
        try { return (int)Math.Round((double)(a.GetPosition2D - b.GetPosition2D).Length); }
        catch { return 1000; }
    }
}
