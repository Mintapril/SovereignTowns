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
/// 方案2 合并 solver 的求解产出 —— flow 统计 + 按边语义类别汇总 + decode 出的对外契约
/// (每城 Target / 派发指令 / 每城遣散头数)。见 audits/mcmf-merge-handoff.md §3。
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

    /// <summary>本次求解所用氏族驻军工资预算(金币/日)。</summary>
    public long Budget { get; set; }

    /// <summary>M2 decode:每城目标头数(= Σ 流入该城所有 demand-tier)。每个 fief 预播种 0。</summary>
    public Dictionary<Settlement, int> Target { get; } = new();

    /// <summary>M2 decode:派发指令(Recruiter / InPlace / Transfer)。M2 仅供差异对账,不派发。</summary>
    public List<DispatchInstruction> Instructions { get; } = new();

    /// <summary>decode:每城遣散头数(现有驻军 origin → 本城 disbandGate 的流)。</summary>
    public Dictionary<Settlement, int> Disband { get; } = new();

    /// <summary>parallel-run 差异日志行(边语义类别汇总)。Target / 指令级对账见
    /// CapitalLogisticsManager.LogMergedDiff。</summary>
    public string DiffLine(string clanId)
        => $"clan={clanId} settlements={SettlementCount} nodes={NodeCount} edges={EdgeCount} "
         + $"originSupply={OriginSupply} budgetCap={BudgetTroopCap} demandCap={DemandTierCapacity} "
         + $"flow={TotalFlow} demandFilled={DemandFilled} unmet={Math.Max(0, DemandTierCapacity - DemandFilled)} "
         + $"stay={StayFlow} recruit={RecruitFlow} transfer={TransferFlow} bypass={BypassFlow}";
}

/// <summary>
/// 方案2 合并 solver —— 把 Pass A(分配)与 Pass B(路由)合并为单一 MinCostFlow 图、
/// 单次求解。建图 + Solve + decode 成 Target / DispatchInstruction / 每城遣散。
/// 精确规格见 audits/mcmf-merge-handoff.md §3。
///
/// 里程碑进度:
///   - M1:建图 + Solve + 按边语义类别统计。
///   - M2:decode 成 Target / DispatchInstruction / disband(填入结果对象)。
///   - M3:value 用合并 solver 专用 Merged* 基常数(与 Pass A 独立,routing-可比标度)。
///   - M4:两段 bypass —— 每个非保护城一个 disbandGate 节点,正常段(每 tick 遣散上限,
///     费用 0)+ 溢出段(罚分)。现有驻军经 disbandGate 出图;保护态(siege / risk≥High /
///     flag-off)origin 不连 disbandGate → 永不遣散。两段保证非保护城驻军恒有出路。
///   - M5:manual 模式 —— 玩家手动目标作 demand 容量(adequate = hardCap = 手动目标,
///     surplus 段归零);manual 模式全城保护、永不遣散。详见 handoff §3.8。
/// 派发由 CapitalLogisticsManager 接线(ShadowMerged 只记差异、MergedOnly 真派发)。
/// </summary>
public static class UnifiedGarrisonSolver
{
    /// <summary>费用偏移,使 K − value ≥ 0(MinCostFlow 拒负费用)。须 ≥ 任何 merged tier
    /// value 的最大值;20M 远高于此。全程整数运算 → routing-vs-value 比较精确,K 大小不影响正确性。</summary>
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

    /// <param name="passA">非 null 时复用 legacy Pass A 已算好的预算 / 头数上限,
    /// 避免 ShadowMerged 期双算;null 则自算(standalone)。</param>
    public static UnifiedSolverResult Solve(
        CapitalManager manager, Settlement capitalSettlement, GarrisonAllocationResult? passA = null)
    {
        var result = new UnifiedSolverResult();
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null || capitalSettlement == null) return result;

            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            var thresholds = ConfigurationManager.Current?.Thresholds ?? new PartyThresholds();
            // M5:manual 模式下 demand 容量改用玩家手动目标,且全城保护不遣散(见 §3.8)。
            bool manualMode = cfg.AllowManualGarrisonTargets;

            // #2:同氏族但首府主之外的领主持有的非首府(同氏族封臣的独立封地)—— 合并 solver
            // 整体排除(既不建 demand 也不建 origin)。
            // 注:legacy SupplyDemandGraph 对这类分支仍保留为 Garrison surplus 抽兵源("仍可被抽"),
            // 合并 solver 取全排除的简化口径 —— 代价是不再从他人持有分支抽超额兵。单领主氏族
            // (典型玩家氏族)永不触发此分支。改动此处前请勿"只建 origin 不建 demand":那会让
            // solver 把同氏族封臣的驻军纳入遣散候选(disbandGate),等于擅自遣散他人部队。
            var capitalOwner = capitalSettlement.Owner;
            var towns = clan.Fiefs
                .Where(t => t?.Settlement != null && t.Settlement.IsActive && (t.IsTown || t.IsCastle))
                .Where(t => t.Settlement == capitalSettlement
                            || capitalOwner == null
                            || t.Settlement.Owner == null
                            || t.Settlement.Owner == capitalOwner)
                .ToList();
            if (towns.Count == 0) return result;

            var capitalTown = towns.FirstOrDefault(t => t.Settlement == capitalSettlement);
            if (capitalTown == null)
            {
                Logger.Debug($"UnifiedGarrisonSolver: capital '{capitalSettlement.StringId}' not among clan fiefs");
                return result;
            }

            // passA 提供时复用 legacy 已算好的预算 / 头数上限(advisor perf 项);
            // standalone(passA == null)则自算。
            long clanWageBudget;
            int budgetTroopCap;
            if (passA != null)
            {
                clanWageBudget = passA.Budget;
                budgetTroopCap = Math.Max(0, passA.BudgetTroopCap);
            }
            else
            {
                int wagePerTroop = Math.Max(1, GarrisonAllocationSolver.WagePerTroopAtMaxTier(manager!, towns));
                clanWageBudget = GarrisonAllocationSolver.ClanWageBudget(manager!, towns, cfg, wagePerTroop);
                budgetTroopCap = (int)Math.Max(0L, Math.Min(int.MaxValue, clanWageBudget / wagePerTroop));
            }

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

            // #1:本 clan 在飞 ST 队按目标定居点汇总的在途头数 —— 据此 floor-first 削减
            // demand-tier 容量,避免重复补员已在途的缺口。
            var inFlightInbound = CollectInFlightInbound(clan);

            int next = 1;
            int superSource = next++, superSink = next++, budgetGate = next++,
                bypass = next++, bypassOverflow = next++;

            // capital-transit:每首府每 role 一个 —— role-blind 的单 transit 会让 Cav 招募兵
            // 经 transit 填进 Inf 缺口(见 handoff §3.1)。
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
                if (manualMode)
                {
                    // M5:manual 模式 —— 玩家手动目标作 demand 容量上限。adequate = hardCap =
                    // 手动目标 → surplus 段归零;floor 收窄到不超过手动目标。value 函数照常
                    // (手动目标只改容量,不改值域口径,见 handoff §3.8)。
                    int manualTarget = ComputeManualTarget(t, cfg);
                    hardCap = manualTarget;
                    adequate = manualTarget;
                    floor = Math.Min(floor, manualTarget);
                }
                float threat = ThreatWeight(s);
                float strat = StrategicWeight(s, isCapital);
                int coreSpan = Math.Max(0, adequate - floor);
                int coreCount = Math.Max(1, cfg.CoreTierCount);
                int surplusSpan = Math.Max(0, hardCap - adequate);

                // (tier 容量, tier value) 序列 —— tier 分层沿用 Pass A 的 floor/core/surplus 口径;
                // value 基常数用合并 solver 专用的 Merged* 常数(与 Pass A 独立,见 §2)。
                var tierDefs = new List<(int Cap, float Value)>();
                if (floor > 0)
                    tierDefs.Add((floor, cfg.MergedValueFloorBase * threat * strat));
                for (int k = 0; k < coreCount; k++)
                {
                    int cap = coreSpan * (k + 1) / coreCount - coreSpan * k / coreCount;
                    if (cap <= 0) continue;
                    float dim = 1.0f - CoreDimRange * ((k + CoreDimMidpoint) / coreCount);
                    tierDefs.Add((cap, cfg.MergedValueCoreBase * dim * threat * strat));
                }
                if (surplusSpan > 0)
                    tierDefs.Add((surplusSpan, -Math.Max(1, cfg.MergedSurplusEdgeCost)));

                // #1:在飞 inbound 兵将填满高优先 tier → 从 floor 起逐层削减 tier 容量,
                // solver 只需补其上方的缺口。role-blind 近似(不区分兵种,见 handoff §3.6)。
                int inflight = inFlightInbound.TryGetValue(s, out var inb) ? inb : 0;
                for (int ti = 0; ti < tierDefs.Count && inflight > 0; ti++)
                {
                    int take = Math.Min(inflight, tierDefs[ti].Cap);
                    tierDefs[ti] = (tierDefs[ti].Cap - take, tierDefs[ti].Value);
                    inflight -= take;
                }

                var rule = isCapital
                    ? (ConfigurationManager.GetRuleFor(t) ?? TownGarrisonRule.CreateDefault())
                    : null;

                foreach (var (cap, value) in tierDefs)
                {
                    if (cap <= 0) continue;   // 在飞 inbound 削减后可能整层归零
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

            // bypass / bypassOverflow → superSink:容量 ≥ 总 origin 供给,保证每个非保护
            // origin 都有出路(招募 origin 直连 bypass;现有驻军经 disbandGate 两段)。
            AddE(bypass, superSink, Math.Max(1, originSupply), 0, EdgeCat.Internal);
            AddE(bypassOverflow, superSink, Math.Max(1, originSupply), 0, EdgeCat.Internal);

            // ── M4 两段 bypass:每个非保护城一个 disbandGate 节点(懒创建)──
            // 正常段 disbandGate→bypass:容量 = 每 tick 遣散上限,费用 0(总遣散费 = K)。
            // 溢出段 disbandGate→bypassOverflow:容量足量,费用 = 溢出罚分(总费 = K + 罚分)。
            // 正常段费用 < surplus 留驻(K + surplusEdgeCost),罚分 > surplusEdgeCost →
            // 正常段耗尽后 solver 优先 surplus 留驻,超额遣散被限制在每 tick 上限。
            // MergedDisbandPerDayCap 是「每天」口径;按 tick 间隔折算成每 tick 上限。
            int tickHours = Math.Min(24, Math.Max(1, cfg.CapitalLogisticsTickHours));
            int disbandPerTickCap = Math.Max(0, (int)Math.Round(cfg.MergedDisbandPerDayCap * tickHours / 24.0));
            int overflowPenalty = Math.Max(1, cfg.MergedBypassOverflowPenalty);
            var disbandGate = new Dictionary<Settlement, int>();
            int DisbandGateFor(Settlement s)
            {
                if (!disbandGate.TryGetValue(s, out var gate))
                {
                    gate = next++;
                    disbandGate[s] = gate;
                    // cap 0 时 AddE 跳过正常段 → 该城只能经溢出段遣散(仅塞不下 hardCap 的兵)。
                    AddE(gate, bypass, disbandPerTickCap, 0, EdgeCat.Internal);
                    AddE(gate, bypassOverflow, Math.Max(1, originSupply), overflowPenalty, EdgeCat.Internal);
                }
                return gate;
            }

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
                                          + Math.Max(0, thresholds.McmfTransferOverhead)
                                          + RouteRiskSurcharge(o.Settlement, dt.Settlement, cfg);
                            AddE(o.Node, dt.Node, cap, routing + dt.CostInto, EdgeCat.Transfer);
                        }
                    }
                    // 保护态(siege / risk≥High / flag-off)不连 disbandGate → 永不被遣散。
                    // 非保护:经本城 disbandGate 两段 bypass(正常段速率限制 + 溢出段)。
                    if (!o.Protected)
                        AddE(o.Node, DisbandGateFor(o.Settlement), o.Count, K, EdgeCat.Bypass);
                }
                else
                {
                    // 招募 origin 经其首府的 transit 入图(role 专属 transit)。
                    int routing = o.Kind == OriginKind.Village
                        ? RoutingDistance(o.Settlement, capitalSettlement)
                          + Math.Max(0, thresholds.McmfRecruiterOverhead)
                          + RouteRiskSurcharge(o.Settlement, capitalSettlement, cfg)
                        : 0;   // InPlace 原地招募,routing 0、零暴露
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
                                      + Math.Max(0, thresholds.McmfTransferOverhead)
                                      + RouteRiskSurcharge(capitalSettlement, dt.Settlement, cfg);
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
            result.Budget = clanWageBudget;
            result.BudgetTroopCap = budgetTroopCap;
            result.DemandTierCapacity = demandTierCapacity;
            result.TotalFlow = flow.TotalFlow;
            result.TotalCost = flow.TotalCost;

            // ── decode:flow → 对外契约(Target / 指令 / disband)。每条边一一对应至多一个动作 ──
            var originByNode = origins.ToDictionary(o => o.Node);
            var tierByNode = tiers.ToDictionary(d => d.Node);
            var transitRoleByNode = transit.ToDictionary(kv => kv.Value, kv => kv.Key);
            foreach (var t in towns) result.Target[t.Settlement] = 0;

            // 跨城调拨按 (源, 目标, role) 聚合后再发指令 —— 现有驻军跨城 + transit→分支 共用。
            var transfers = new Dictionary<(Settlement Src, Settlement Dst, GenericTroopRole Role), int>();
            void AccumTransfer(Settlement src, Settlement dst, GenericTroopRole role, int count)
            {
                var key = (src, dst, role);
                transfers[key] = (transfers.TryGetValue(key, out var c) ? c : 0) + count;
            }

            foreach (var kv in flow.EdgeFlows)
            {
                int f = kv.Value;
                if (f <= 0) continue;
                int from = kv.Key.From, to = kv.Key.To;

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

                // Target:流入 demand-tier(dt → budgetGate 的流 = 该 tier 被填的量)。
                if (to == budgetGate && tierByNode.TryGetValue(from, out var dtIn))
                {
                    result.DemandFilled += f;
                    result.Target[dtIn.Settlement] += f;
                    continue;
                }

                // origin 出边。
                if (originByNode.TryGetValue(from, out var o))
                {
                    if (o.Kind == OriginKind.Garrison)
                    {
                        if (tierByNode.TryGetValue(to, out var dt))
                        {
                            // 跨城 = Transfer;同城 = 留(无指令)。
                            if (dt.Settlement != o.Settlement)
                                AccumTransfer(o.Settlement, dt.Settlement, o.Role, f);
                        }
                        else if (disbandGate.TryGetValue(o.Settlement, out var dg) && to == dg)
                        {
                            // 现有驻军 origin → 本城 disbandGate = 遣散(正常段/溢出段对 decode 无别)。
                            result.Disband[o.Settlement] =
                                (result.Disband.TryGetValue(o.Settlement, out var d) ? d : 0) + f;
                        }
                    }
                    else if (transitRoleByNode.ContainsKey(to))
                    {
                        // 招募 origin → transit:Village → Recruiter;InPlace → InPlace。
                        if (o.Kind == OriginKind.Village)
                            result.Instructions.Add(new RecruiterPartyInstruction(
                                capitalTown, capitalSettlement, o.Settlement, o.Role, f));
                        else
                            result.Instructions.Add(new InPlaceRecruitInstruction(
                                capitalSettlement, o.Role, f));
                    }
                    // recruit → bypass:志愿兵名额未被招,无指令(≠ disband)。
                    continue;
                }

                // transit → 分支 demand-tier = 招募兵经首府转发,decode 成 Transfer。
                // transit → 首府 demand-tier = 招募兵留首府(无指令,招募已由 origin→transit 产出)。
                if (transitRoleByNode.TryGetValue(from, out var trole)
                    && tierByNode.TryGetValue(to, out var tdt) && !tdt.IsCapital)
                {
                    AccumTransfer(capitalSettlement, tdt.Settlement, trole, f);
                }
            }

            foreach (var kv in transfers)
                result.Instructions.Add(new TransferPartyInstruction(
                    kv.Key.Src, kv.Key.Dst, kv.Key.Role, kv.Value));
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

    /// <summary>该城现有驻军是否禁止遣散:功能开关关 / manual 模式 / 围城 / 高危。
    /// 镜像 DisbandExcessGarrisons Gate 1/2/3/4。</summary>
    private static bool IsProtectedFromDisband(Settlement s, FiscalAutonomyConfig cfg)
    {
        if (!cfg.DisbandUnaffordableExcess) return true;
        // M5:manual 模式玩家自选目标(可能有意 over-garrison)—— 永不遣散,
        // 镜像 CapitalLogisticsManager.DisbandExcessGarrisons Gate 2。
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

    /// <summary>
    /// D2:路线风险加进 routing 成本。a↔b 端点 + 连线中点处的最大敌对健康兵力
    /// × <see cref="FiscalAutonomyConfig.DispatchRiskCostScale"/>。`DispatchRiskEnabled`
    /// 关时返回 0。软成本 —— 危险路线成本升高,solver 自行改选安全村 / 原地招募 / 不招。
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
    /// 城镇:<see cref="TownGarrisonRule.TargetTotalCount"/> × 风险乘子(risk≥High 用
    /// WartimeMultiplier,否则 PeacetimeMultiplier),round 后 ≥1;城堡:
    /// <see cref="BranchRule.TargetPower"/> 直接当头数(合并 solver 全程头数口径,
    /// 见 handoff §3.1)。两者均 clamp 到 <see cref="FiscalAutonomyConfig.MaxGarrisonHardCap"/>
    /// —— 与 legacy SupplyDemandGraph.BuildSettlementStates 的 manual 口径一致。
    /// 控制面板的 CapitalLogisticsManager.StashAssessments(展示"玩家目标")复用本方法,
    /// 单一口径 —— 否则面板展示值与 solver 实采容量乖离。
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

    /// <summary>#1:本 clan 所有在飞 ST 队(Transfer / Recruiter / Sally)按目标定居点
    /// 汇总的在途头数。Transfer 回程(目标 == 源)记到源城,否则记到目的城;Recruiter /
    /// Sally 记到各自 home。role-blind:返回总头数,不区分兵种(M-stage 近似,见 handoff §3.6)。
    /// 镜像 SupplyDemandGraph.AccountInFlight 的口径。</summary>
    private static Dictionary<Settlement, int> CollectInFlightInbound(Clan clan)
    {
        var inbound = new Dictionary<Settlement, int>();
        try
        {
            var parties = MobileParty.AllCustomParties;
            if (parties == null) return inbound;
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
                inbound[dest] = (inbound.TryGetValue(dest, out var c) ? c : 0) + heads;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UnifiedGarrisonSolver.CollectInFlightInbound failed", ex);
        }
        return inbound;
    }
}
