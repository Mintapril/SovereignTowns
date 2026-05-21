using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Parties;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Algorithm;

public sealed class SupplyDemandGraphResult
{
    public SupplyDemandGraphResult(
        int settlementCount,
        int totalDemand,
        int totalFlow,
        int totalCost,
        int unmet,
        List<DispatchInstruction> instructions)
    {
        SettlementCount = settlementCount;
        TotalDemand = totalDemand;
        TotalFlow = totalFlow;
        TotalCost = totalCost;
        Unmet = unmet;
        Instructions = instructions ?? new List<DispatchInstruction>();
    }

    public int SettlementCount { get; }
    public int TotalDemand { get; }
    public int TotalFlow { get; }
    public int TotalCost { get; }
    public int Unmet { get; }
    public IReadOnlyList<DispatchInstruction> Instructions { get; }
}

public static class SupplyDemandGraph
{
    private enum SourceKind
    {
        InPlace,
        Village,
        Garrison
    }

    private sealed class SettlementState
    {
        public SettlementState(
            Town town, Settlement settlement,
            TownGarrisonRule? capitalRule, BranchRule? branchRule,
            int desiredTotal, int desiredPower, bool isCapital,
            bool isOtherOwnedBranch = false)
        {
            Town = town;
            Settlement = settlement;
            CapitalRule = capitalRule;
            BranchRule = branchRule;
            DesiredTotal = desiredTotal;
            DesiredPower = desiredPower;
            IsCapital = isCapital;
            IsOtherOwnedBranch = isOtherOwnedBranch;
            Buckets = MatchPolicy.Bucketize(town.GarrisonParty?.MemberRoster);
            Inbound = new Dictionary<GenericTroopRole, int>();
            InboundPower = 0f;
            var garrisonRoster = town.GarrisonParty?.MemberRoster;
            CurrentPower = SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeRosterPower(garrisonRoster);
            CurrentHeadCount = garrisonRoster?.TotalManCount ?? 0;
            CurrentLowTierHeadCount = ComputeLowTierHeadCount(garrisonRoster);
        }

        public Town Town { get; }
        public Settlement Settlement { get; }
        public TownGarrisonRule? CapitalRule { get; }
        public BranchRule? BranchRule { get; }
        public int DesiredTotal { get; }
        public int DesiredPower { get; }
        public bool IsCapital { get; }
        /// <summary>
        /// 同氏族但非"首府拥有者本人"持有的非首府 town/castle。
        /// 这两个 BranchRule 配置（TargetPower/LowTierMinFraction）不应用：
        ///   - 不作为 demand（不被 ST 补员；按 vanilla 行为补兵）
        ///   - 仍可作为 Garrison surplus 抽兵源（DesiredPower 用 vanilla baseline，避免抽空）
        ///   - 不作为 InPlace volunteer 招募源
        /// </summary>
        public bool IsOtherOwnedBranch { get; }
        public List<TroopBucket> Buckets { get; }
        public Dictionary<GenericTroopRole, int> Inbound { get; }
        public float InboundPower { get; private set; }

        public int Current(GenericTroopRole role)
            => Buckets.Where(b => b.Role == role).Sum(b => b.Count);

        public int Projected(GenericTroopRole role)
            => Math.Max(0, Current(role) + Count(Inbound, role));

        public int Available(GenericTroopRole role) => Current(role);

        public float CurrentPower { get; }
        public float ProjectedPower => CurrentPower + InboundPower;
        public int CurrentHeadCount { get; }
        public int CurrentLowTierHeadCount { get; }

        public void AddInbound(GenericTroopRole role, int count)
            => AddCount(Inbound, role, count);

        public void AddInboundPower(float power)
        {
            if (power > 0f) InboundPower += power;
        }

        private static int Count(IReadOnlyDictionary<GenericTroopRole, int> values, GenericTroopRole role)
            => values.TryGetValue(role, out var count) ? count : 0;

        private static void AddCount(Dictionary<GenericTroopRole, int> values, GenericTroopRole role, int count)
        {
            if (role == GenericTroopRole.Unknown || count <= 0) return;
            values[role] = Count(values, role) + count;
        }

        public TroopBucket? Bucket(GenericTroopRole role)
            => Buckets.FirstOrDefault(b => b.Role == role && b.Count > 0);

        private static int ComputeLowTierHeadCount(TaleWorlds.CampaignSystem.Roster.TroopRoster? roster)
        {
            if (roster == null) return 0;
            int sum = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var el = roster.GetElementCopyAtIndex(i);
                if (el.Character == null || el.Character.IsHero) continue;
                if (el.Character.Tier <= SovereignTowns.Evaluators.GarrisonPowerEvaluator.LowTierMaxInclusive)
                    sum += el.Number;
            }
            return sum;
        }
    }

    private sealed class SourceDef
    {
        public SourceDef(int nodeId, SourceKind kind, Settlement settlement, Town? town, TroopBucket bucket)
        {
            NodeId = nodeId;
            Kind = kind;
            Settlement = settlement;
            Town = town;
            Bucket = bucket;
        }

        public int NodeId { get; }
        public SourceKind Kind { get; }
        public Settlement Settlement { get; }
        public Town? Town { get; }
        public TroopBucket Bucket { get; }
    }

    private sealed class DemandDef
    {
        public DemandDef(
            int nodeId,
            SettlementState state,
            GenericTroopRole role,
            int desired,
            int current,
            int demand,
            bool isRecruitmentStockpile)
        {
            NodeId = nodeId;
            State = state;
            Role = role;
            Desired = desired;
            Current = current;
            Demand = demand;
            IsRecruitmentStockpile = isRecruitmentStockpile;
        }

        public int NodeId { get; }
        public SettlementState State { get; }
        public GenericTroopRole Role { get; }
        public int Desired { get; }
        public int Current { get; }
        public int Demand { get; }
        public bool IsRecruitmentStockpile { get; }
        public float DeficitRatio => Desired <= 0 ? 1f : Math.Min(1f, Math.Max(0f, Demand / (float)Desired));
    }

    private static bool _selfTestLogged;

    public static SupplyDemandGraphResult Run(
        CapitalManager manager, Settlement capitalSettlement, GarrisonAllocationResult? passA = null)
        => RunInternal(manager, capitalSettlement, "MCMF", passA);

    private static SupplyDemandGraphResult RunInternal(
        CapitalManager manager, Settlement capitalSettlement, string logTag, GarrisonAllocationResult? passA)
    {
        if (!_selfTestLogged)
        {
            _selfTestLogged = true;
            if (MinCostFlow.SelfTest(out var selfTestMessage))
                Logger.Info(logTag + ": " + selfTestMessage);
            else
                Logger.Error(logTag + " self-test failed: " + selfTestMessage);
        }

        var states = BuildSettlementStates(manager, capitalSettlement, passA);
        if (states.Count == 0)
            return new SupplyDemandGraphResult(0, 0, 0, 0, 0, new List<DispatchInstruction>());
        AccountInFlight(states, manager.OwnerClan);

        var graph = new MinCostFlow();
        int nextNodeId = 1;
        int superSource = nextNodeId++;
        int superSink = nextNodeId++;
        int unmetNode = nextNodeId++;

        var sources = new Dictionary<int, SourceDef>();
        var demands = new Dictionary<int, DemandDef>();
        var capitalState = states.FirstOrDefault(s => s.IsCapital);
        var features = ConfigurationManager.Current?.EnabledFeatures;
        bool autoRecruitmentEnabled = features?.AutoRecruitment ?? true;
        bool troopTransfersEnabled = features?.TroopTransfers ?? true;

        foreach (var state in states)
        {
            if (state.IsCapital)
            {
                foreach (var role in MatchPolicy.Roles)
                {
                    int desired = MatchPolicy.DesiredCount(state.CapitalRule!, role, state.DesiredTotal);
                    int current = state.Projected(role);
                    int demand = Math.Max(0, desired - current);
                    if (demand <= 0) continue;

                    int demandNode = nextNodeId++;
                    var def = new DemandDef(demandNode, state, role, desired, current, demand, isRecruitmentStockpile: false);
                    demands[demandNode] = def;
                    graph.AddEdge(demandNode, superSink, demand, 0);
                }
            }
            else
            {
                // "同氏族其他领主持有"的非首府：BranchRule 不应用 → 不生成 demand（不被 ST 补员，按 vanilla 行为补兵）。
                if (state.IsOtherOwnedBranch) continue;

                float projected = state.ProjectedPower;
                int projectedInt = (int)Math.Round(projected);
                int demand = Math.Max(0, state.DesiredPower - projectedInt);
                if (demand <= 0) continue;

                // 非首府用 GenericTroopRole.Infantry 作为"占位 role"，仅为复用图节点结构；
                // CanConnect 里所有 source role 都能连到 branch demand（详见 Step 5）。
                int demandNode = nextNodeId++;
                var def = new DemandDef(demandNode, state, GenericTroopRole.Infantry, state.DesiredPower, projectedInt, demand, isRecruitmentStockpile: false);
                demands[demandNode] = def;
                graph.AddEdge(demandNode, superSink, demand, 0);
            }
        }

        // 首府"招募囤兵"需求：把所有 branch 缺口的 role 总和注入 capital 招募 stockpile。
        // 非首府的 demand 已经记到 totalBranchDemand 里，这里只对 capital 仍然作为兵源囤兵。
        if (autoRecruitmentEnabled && capitalState != null && !capitalState.Settlement.IsUnderSiege)
        {
            int totalBranchDemand = states
                .Where(s => !s.IsCapital && !s.IsOtherOwnedBranch)
                .Sum(s => Math.Max(0, s.DesiredPower - (int)Math.Round(s.ProjectedPower)));
            if (totalBranchDemand > 0)
            {
                foreach (var role in MatchPolicy.Roles)
                {
                    if (!MatchPolicy.AllowsRole(capitalState.CapitalRule!, role)) continue;
                    // 按首府规则的兵种比例把分支总缺口拆到各 role。否则每个 role 各囤一份
                    // totalBranchDemand，4 个 role 合计会让首府囤兵到 4× 实际分支需求。
                    int roleStockpile = MatchPolicy.DesiredCount(capitalState.CapitalRule!, role, totalBranchDemand);
                    if (roleStockpile <= 0) continue;
                    int demandNode = nextNodeId++;
                    var def = new DemandDef(
                        demandNode,
                        capitalState,
                        role,
                        desired: roleStockpile,
                        current: 0,
                        demand: roleStockpile,
                        isRecruitmentStockpile: true);
                    demands[demandNode] = def;
                    graph.AddEdge(demandNode, superSink, roleStockpile, 0);
                }
            }
        }

        int totalDemand = demands.Values.Sum(d => d.Demand);
        if (totalDemand <= 0)
            return new SupplyDemandGraphResult(states.Count, 0, 0, 0, 0, new List<DispatchInstruction>());

        graph.AddEdge(superSource, unmetNode, totalDemand, 0);
        foreach (var demand in demands.Values)
            graph.AddEdge(unmetNode, demand.NodeId, demand.Demand, Thresholds.McmfUnmetCost);

        var inFlightVillages = CollectInFlightRecruiterVillages(manager.OwnerClan);
        foreach (var state in states)
        {
            if (state.Settlement.IsUnderSiege) continue;
            if (troopTransfersEnabled)
                AddRosterSurplusSources(graph, sources, ref nextNodeId, superSource, state);
            if (!autoRecruitmentEnabled) continue;

            // 同氏族其他领主持有：不让 ST 在其 notable 处招兵（按 vanilla 行为补兵）。
            // Garrison surplus 仍可被抽（已在 AddRosterSurplusSources 中按 vanilla baseline 处理）。
            if (state.IsOtherOwnedBranch) continue;

            AddCharacterSources(graph, sources, ref nextNodeId, superSource, SourceKind.InPlace, state.Settlement, state.Town, EnumerateVolunteerTroops(state.Settlement));

            // 首府：per-village 招募源。MCMF 直接在全图候选村里挑"兵种 + 距离"最优的村,
            // 取代旧的"首府直属村聚合成单一 Village 源"。Decode 出 RecruiterPartyInstruction.TargetVillage。
            if (state.IsCapital)
            {
                foreach (var village in EnumerateRecruitmentVillages(state.Town, manager.OwnerClan, inFlightVillages))
                    AddCharacterSources(graph, sources, ref nextNodeId, superSource, SourceKind.Village,
                        village, state.Town, EnumerateVolunteerTroops(village));
            }
        }

        foreach (var source in sources.Values)
        {
            foreach (var demand in demands.Values)
            {
                if (!CanConnect(source, demand)) continue;
                int cost = Cost(source, demand);
                graph.AddEdge(source.NodeId, demand.NodeId, Math.Min(source.Bucket.Count, demand.Demand), cost);
            }
        }

        var flow = graph.Solve(superSource, superSink);
        var instructions = Decode(flow, sources, demands, unmetNode, out var unmet);
        var result = new SupplyDemandGraphResult(states.Count, totalDemand, flow.TotalFlow, flow.TotalCost, unmet, instructions);
        LogResult(manager, result, logTag);
        return result;
    }

    private static PartyThresholds Thresholds
        => ConfigurationManager.Current?.Thresholds ?? new PartyThresholds();

    private static List<SettlementState> BuildSettlementStates(
        CapitalManager manager, Settlement capitalSettlement, GarrisonAllocationResult? passA)
    {
        // Task 6: Pass A → Pass B 集成。AllowManualGarrisonTargets==false → 路由直接采用
        // Pass A（GarrisonAllocationSolver）按预算解出的每城目标头数；==true → 玩家手动目标
        // 权威，Pass A 仅作推荐（assessment 已由 CapitalLogisticsManager 单独 stash）。
        // passA 中缺失的定居点回退到旧的 ComputeDesiredTarget / branch.TargetPower 行为。
        var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
        bool manualMode = cfg.AllowManualGarrisonTargets;
        int hardCap = Math.Max(1, cfg.MaxGarrisonHardCap);
        float perTroopPower = ReferencePerTroopPower();

        var result = new List<SettlementState>();
        foreach (var town in Town.AllTowns)
        {
            if (town == null) continue;
            if (!(town.IsTown || town.IsCastle)) continue;
            if (town.OwnerClan == null || town.OwnerClan != manager.OwnerClan) continue;

            var settlement = town.Settlement;
            if (settlement == null || !settlement.IsActive) continue;

            bool isCapital = settlement == capitalSettlement;
            if (isCapital)
            {
                var rule = ConfigurationManager.GetRuleFor(town) ?? TownGarrisonRule.CreateDefault();
                int desired;
                if (manualMode)
                {
                    // 手动模式:玩家配置目标(含风险乘数)再 clamp 到 hardCap。
                    desired = Math.Min(ComputeDesiredTarget(rule, RiskAssessmentService.Assess(settlement)), hardCap);
                }
                else if (passA != null && passA.Target.TryGetValue(settlement, out var passACapital))
                {
                    // 自动模式:Pass A 解出的目标头数权威。
                    desired = Math.Max(1, passACapital);
                }
                else
                {
                    // passA 缺该定居点 → 回退旧行为。
                    desired = ComputeDesiredTarget(rule, RiskAssessmentService.Assess(settlement));
                }
                result.Add(new SettlementState(town, settlement, capitalRule: rule, branchRule: null,
                    desiredTotal: desired, desiredPower: 0, isCapital: true));
            }
            else
            {
                // 判别"首府拥有者本人持有" vs "同氏族其他领主持有"。
                // 防御性回退：capitalSettlement.Owner / settlement.Owner 任一为 null 时按"首府主本人持有"处理（保留原行为）。
                var capitalOwner = capitalSettlement?.Owner;
                var branchOwner = settlement.Owner;
                bool isOtherOwned = capitalOwner != null && branchOwner != null && branchOwner != capitalOwner;

                if (isOtherOwned)
                {
                    // 他人持有：BranchRule 不应用 — 按 vanilla baseline 算 DesiredPower（仅供 Garrison surplus 计算用）。
                    // baseline 算不出（vanilla helper 返回 0）→ 退化为 currentPower，相当于不允许抽兵兜底。
                    int baseline = SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeAiVanillaTargetPower(town);
                    if (baseline <= 0)
                    {
                        var rosterNow = town.GarrisonParty?.MemberRoster;
                        baseline = (int)Math.Round(SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeRosterPower(rosterNow));
                    }
                    result.Add(new SettlementState(town, settlement, capitalRule: null, branchRule: null,
                        desiredTotal: 0, desiredPower: baseline, isCapital: false, isOtherOwnedBranch: true));
                }
                else
                {
                    var branch = ConfigurationManager.GetBranchRuleFor(town) ?? BranchRule.CreateDefault();
                    int branchPower;
                    if (manualMode)
                    {
                        // 手动模式:玩家配置的 TargetPower,但不得超过 hardCap 换算出的 power 上限。
                        branchPower = Math.Min(branch.TargetPower, HeadsToPower(hardCap, perTroopPower));
                    }
                    else if (passA != null && passA.Target.TryGetValue(settlement, out var passABranch))
                    {
                        // 自动模式:Pass A 解出的目标头数,换算成 branch 的 power 口径需求。
                        branchPower = HeadsToPower(passABranch, perTroopPower);
                    }
                    else
                    {
                        // passA 缺该定居点 → 回退旧行为(直接用 BranchRule.TargetPower)。
                        branchPower = branch.TargetPower;
                    }
                    result.Add(new SettlementState(town, settlement, capitalRule: null, branchRule: branch,
                        desiredTotal: 0, desiredPower: branchPower, isCapital: false));
                }
            }
        }
        return result;
    }

    private static void AccountInFlight(List<SettlementState> states, Clan? ownerClan)
    {
        if (ownerClan == null || states.Count == 0) return;

        var bySettlement = new Dictionary<Settlement, SettlementState>();
        foreach (var state in states)
        {
            if (state.Settlement != null && !bySettlement.ContainsKey(state.Settlement))
                bySettlement[state.Settlement] = state;
        }

        try
        {
            var parties = MobileParty.AllCustomParties;
            if (parties == null) return;

            foreach (var party in parties)
            {
                if (party == null || !party.IsActive) continue;
                var partyClan = ResolvePartyClan(party);
                if (partyClan == null || partyClan != ownerClan) continue;

                var roster = party.MemberRoster;
                if (roster == null || roster.TotalManCount <= 0) continue;

                if (party.PartyComponent is StTransferPartyComponent transfer)
                {
                    var source = transfer.Source;
                    var destination = transfer.Destination;
                    var target = party.TargetSettlement;

                    if (source != null
                        && destination != null
                        && target == source
                        && bySettlement.TryGetValue(source, out var returningSource))
                    {
                        AddInboundWithPower(returningSource, party);
                        continue;
                    }

                    if (destination != null && bySettlement.TryGetValue(destination, out var destinationState))
                        AddInboundWithPower(destinationState, party);
                }
                else if (party.PartyComponent is StRecruiterPartyComponent recruiter)
                {
                    var home = recruiter.HomeSettlementOrNull;
                    if (home != null && bySettlement.TryGetValue(home, out var homeState))
                        AddInboundWithPower(homeState, party);
                }
                else if (party.PartyComponent is StSallyPartyComponent sally)
                {
                    var home = sally.HomeSettlementOrNull;
                    if (home != null && bySettlement.TryGetValue(home, out var homeState))
                        AddInboundWithPower(homeState, party);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SupplyDemandGraph.AccountInFlight failed", ex);
        }
    }

    private static Clan? ResolvePartyClan(MobileParty party)
    {
        try
        {
            if (party.ActualClan != null) return party.ActualClan;
            if (party.PartyComponent is StTransferPartyComponent transfer) return transfer.Source?.OwnerClan;
            if (party.PartyComponent is StRecruiterPartyComponent recruiter) return recruiter.HomeSettlementOrNull?.OwnerClan;
            if (party.PartyComponent is StSallyPartyComponent sally) return sally.HomeSettlementOrNull?.OwnerClan;
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static void AddInboundWithPower(SettlementState state, MobileParty party)
    {
        var roster = party?.MemberRoster;
        if (roster == null) return;

        var buckets = MatchPolicy.Bucketize(roster);
        foreach (var bucket in buckets)
            state.AddInbound(bucket.Role, bucket.Count);

        float power = SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeRosterPower(roster);
        state.AddInboundPower(power);
    }

    private static int ComputeDesiredTarget(TownGarrisonRule rule, RiskAssessment risk)
    {
        float multiplier = risk.Level >= RiskLevel.High
            ? rule.WartimeMultiplier
            : rule.PeacetimeMultiplier;
        return Math.Max(1, (int)Math.Round(rule.TargetTotalCount * multiplier));
    }

    /// <summary>
    /// Task 6: Pass A 产出的是"头数"目标,但非首府 demand 走 vanilla power 口径。
    /// 把头数近似换算成 power = heads × 参考单兵 power。参考值取中 tier(T3)的
    /// vanilla MilitaryPowerModel power(自检基准 ≈ 1.30) —— 这是一个有意的近似:
    /// 真实 power 取决于实际驻军兵种构成,但路由 demand 只需一个合理量级即可。
    /// </summary>
    private const float ReferencePerTroopPowerFallback = 1.3f;

    private static float ReferencePerTroopPower()
    {
        try
        {
            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MilitaryPowerModel;
            var stub = SovereignTowns.Evaluators.GarrisonPowerEvaluator.MakeStubTroop(3, mounted: false);
            if (model == null || stub == null) return ReferencePerTroopPowerFallback;
            float power = model.GetDefaultTroopPower(stub);
            return power > 0f ? power : ReferencePerTroopPowerFallback;
        }
        catch (Exception ex)
        {
            Logger.Error("SupplyDemandGraph.ReferencePerTroopPower failed; using fallback", ex);
            return ReferencePerTroopPowerFallback;
        }
    }

    private static int HeadsToPower(int heads, float perTroopPower)
        => Math.Max(0, (int)Math.Round(Math.Max(0, heads) * perTroopPower));

    private static void AddRosterSurplusSources(
        MinCostFlow graph,
        Dictionary<int, SourceDef> sources,
        ref int nextNodeId,
        int superSource,
        SettlementState state)
    {
        if (state.IsCapital)
        {
            // 首府：原行为 — 对每个 role 算 desired vs available 的超额头数
            foreach (var bucket in state.Buckets)
            {
                int desired = MatchPolicy.DesiredCount(state.CapitalRule!, bucket.Role, state.DesiredTotal);
                int surplus = Math.Max(0, state.Available(bucket.Role) - desired);
                if (surplus <= 0) continue;

                var sourceBucket = new TroopBucket(bucket.Role, surplus, bucket.MinTier, bucket.Representative);
                AddSource(graph, sources, ref nextNodeId, superSource, SourceKind.Garrison, state.Settlement, state.Town, sourceBucket);
            }
            return;
        }

        // 非首府：把整城驻军挂为"可抽兵源"，但每桶上限是 TotalCount —
        // (TotalPower / TargetPower) - 1) × 该桶头数（按比例匀分超额）。
        // 简化做法：power 超过 TargetPower 时，每桶可抽 0..bucket.Count 头数，
        // 实际抽多少由 MinCostFlow 解出来。
        float currentPower = state.CurrentPower;
        if (currentPower <= state.DesiredPower) return;

        float overshootRatio = (currentPower - state.DesiredPower) / Math.Max(1f, currentPower);
        foreach (var bucket in state.Buckets)
        {
            int abstractable = Math.Max(0, (int)Math.Round(bucket.Count * overshootRatio));
            if (abstractable <= 0) continue;

            var sourceBucket = new TroopBucket(bucket.Role, abstractable, bucket.MinTier, bucket.Representative);
            AddSource(graph, sources, ref nextNodeId, superSource, SourceKind.Garrison, state.Settlement, state.Town, sourceBucket);
        }
    }

    private static void AddCharacterSources(
        MinCostFlow graph,
        Dictionary<int, SourceDef> sources,
        ref int nextNodeId,
        int superSource,
        SourceKind kind,
        Settlement settlement,
        Town town,
        IEnumerable<CharacterObject?> characters)
    {
        foreach (var bucket in BucketizeCharacters(characters))
            AddSource(graph, sources, ref nextNodeId, superSource, kind, settlement, town, bucket);
    }

    private static void AddSource(
        MinCostFlow graph,
        Dictionary<int, SourceDef> sources,
        ref int nextNodeId,
        int superSource,
        SourceKind kind,
        Settlement settlement,
        Town town,
        TroopBucket bucket)
    {
        if (bucket.Count <= 0 || bucket.Role == GenericTroopRole.Unknown) return;
        int nodeId = nextNodeId++;
        sources[nodeId] = new SourceDef(nodeId, kind, settlement, town, bucket);
        graph.AddEdge(superSource, nodeId, bucket.Count, 0);
    }

    private static List<TroopBucket> BucketizeCharacters(IEnumerable<CharacterObject?> characters)
    {
        var byRole = new Dictionary<GenericTroopRole, (int Count, int MinTier, CharacterObject? Representative)>();
        foreach (var character in characters)
        {
            if (character == null || character.IsHero) continue;
            var role = GenericTroopMatcher.GetRole(character);
            if (role == GenericTroopRole.Unknown) continue;
            int tier = GenericTroopMatcher.GetTierBucket(character);

            if (!byRole.TryGetValue(role, out var current))
            {
                byRole[role] = (1, tier, character);
                continue;
            }

            var representative = current.Representative;
            int minTier = current.MinTier;
            if (tier < minTier)
            {
                minTier = tier;
                representative = character;
            }
            byRole[role] = (current.Count + 1, minTier, representative);
        }

        return byRole.Select(kv => new TroopBucket(kv.Key, kv.Value.Count, kv.Value.MinTier, kv.Value.Representative)).ToList();
    }

    private static IEnumerable<CharacterObject?> EnumerateVolunteerTroops(Settlement settlement)
    {
        var notables = settlement.Notables;
        if (notables == null) yield break;

        foreach (var notable in notables)
        {
            if (notable == null || !notable.CanHaveRecruits) continue;
            var slots = notable.VolunteerTypes;
            if (slots == null) continue;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null) yield return slots[i];
        }
    }

    /// <summary>
    /// 招募候选村:全图 village,过滤(active / 非围城 / 非 Raided·Looted / 与 clan 非交战 /
    /// 非招募冷却 / 非在飞征兵队目标),按距首府距离升序取 Top-RecruiterVillageCandidateCap。
    /// 取代旧的"仅首府直属村"枚举 —— 让 MCMF 能把远处稀有兵种所在村纳入图。
    /// </summary>
    private static List<Settlement> EnumerateRecruitmentVillages(
        Town capitalTown, Clan? clan, HashSet<Settlement> excludeVillages)
    {
        var result = new List<Settlement>();
        try
        {
            var capitalSettlement = capitalTown?.Settlement;
            if (capitalSettlement == null) return result;
            var capitalFaction = capitalSettlement.MapFaction;
            var capitalPos = capitalSettlement.GetPosition2D;
            int cap = Math.Max(4, Thresholds.RecruiterVillageCandidateCap);

            var all = Settlement.All;
            if (all == null) return result;

            var scored = new List<(Settlement Village, float Dist)>();
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.IsVillage || !s.IsActive) continue;
                if (s.IsUnderSiege) continue;
                if (excludeVillages.Contains(s)) continue;
                if (RecruitmentCooldown.IsOnCooldown(s)) continue;

                var v = s.Village;
                if (v != null && (v.VillageState == Village.VillageStates.BeingRaided
                                  || v.VillageState == Village.VillageStates.Looted)) continue;

                var f = s.MapFaction;
                if (f == null) continue;
                if (f != capitalFaction)
                {
                    if (capitalFaction == null) continue;
                    if (capitalFaction.IsAtWarWith(f)) continue;
                }

                scored.Add((s, (s.GetPosition2D - capitalPos).Length));
            }

            scored.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));
            int take = Math.Min(cap, scored.Count);
            for (int i = 0; i < take; i++) result.Add(scored[i].Village);
        }
        catch (Exception ex)
        {
            Logger.Error("SupplyDemandGraph.EnumerateRecruitmentVillages failed", ex);
        }
        return result;
    }

    /// <summary>
    /// 本 clan 在飞征兵队当前及之后尚未访问的目标村集合。这些村已被服务,排除出本轮招募图,
    /// 防止下一 daily MCMF 重复派队去同一个村。
    /// </summary>
    private static HashSet<Settlement> CollectInFlightRecruiterVillages(Clan? clan)
    {
        var set = new HashSet<Settlement>();
        if (clan == null) return set;
        try
        {
            var parties = MobileParty.AllCustomParties;
            if (parties == null) return set;
            foreach (var party in parties)
            {
                if (party == null || !party.IsActive) continue;
                if (!(party.PartyComponent is StRecruiterPartyComponent recruiter)) continue;
                if (recruiter.HomeSettlementOrNull?.OwnerClan != clan) continue;
                foreach (var v in recruiter.PendingVillages)
                    if (v != null) set.Add(v);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SupplyDemandGraph.CollectInFlightRecruiterVillages failed", ex);
        }
        return set;
    }

    private static bool CanConnect(SourceDef source, DemandDef demand)
    {
        if (source.Settlement.IsUnderSiege || demand.State.Settlement.IsUnderSiege) return false;

        // 非首府 demand：Garrison 跨城调拨，InPlace 仅本城。
        // Village（征兵队）只把兵带回首府，从不直接补给非首府 → 不连。
        if (!demand.State.IsCapital)
        {
            switch (source.Kind)
            {
                case SourceKind.InPlace:
                    return source.Settlement == demand.State.Settlement;
                case SourceKind.Garrison:
                    return source.Settlement != demand.State.Settlement;
                case SourceKind.Village:
                default:
                    return false;
            }
        }

        // 首府 demand（含 capital 招募 stockpile）：先要求 role 匹配。
        if (source.Bucket.Role != demand.Role) return false;

        if (demand.IsRecruitmentStockpile)
        {
            // stockpile 只收"招募来源"：本城 InPlace + 该首府的任一候选村 Village。
            switch (source.Kind)
            {
                case SourceKind.InPlace:
                    return source.Settlement == demand.State.Settlement;
                case SourceKind.Village:
                    // per-village source：Town 字段为该村归属的首府 town；归属同一首府即可连。
                    return source.Town == demand.State.Town;
                default:
                    return false;
            }
        }

        switch (source.Kind)
        {
            case SourceKind.InPlace:
                return source.Settlement == demand.State.Settlement;
            case SourceKind.Village:
                // per-village source：村本身不必等于首府,归属同一首府即可连。
                return source.Town == demand.State.Town;
            case SourceKind.Garrison:
                return source.Settlement != demand.State.Settlement;
            default:
                return false;
        }
    }

    private static int Cost(SourceDef source, DemandDef demand)
    {
        int overhead = source.Kind switch
        {
            SourceKind.Village => Thresholds.McmfRecruiterOverhead,
            SourceKind.Garrison => Thresholds.McmfTransferOverhead,
            _ => 0
        };

        // 非首府 demand：不考虑 tier 不符的硬罚，因 branch 是黑箱（任意 role 都接受）。
        // 仅按距离 + overhead 计 cost。
        int penalty = demand.State.IsCapital
            ? MatchPolicy.MatchPenalty(source.Bucket, demand.State.CapitalRule!, Thresholds.McmfHardPenalty, Thresholds.McmfTierPenalty)
            : 0;
        // Village（征兵队远征该村再带回首府）与 Garrison（跨城调拨）都计真实地图距离;
        // InPlace 原地招募距离 0。Village 距离 = 候选村 → 首府,即征兵队单程跋涉。
        float distance = (source.Kind == SourceKind.Garrison || source.Kind == SourceKind.Village)
            ? Distance(source.Settlement, demand.State.Settlement)
            : 0f;
        return MatchPolicy.EdgeCost(distance, overhead, penalty, demand.DeficitRatio, Thresholds.McmfLeniency);
    }

    private static float Distance(Settlement a, Settlement b)
    {
        try { return (a.GetPosition2D - b.GetPosition2D).Length; }
        catch { return 1000f; }
    }

    private static List<DispatchInstruction> Decode(
        MinCostFlowResult flow,
        IReadOnlyDictionary<int, SourceDef> sources,
        IReadOnlyDictionary<int, DemandDef> demands,
        int unmetNode,
        out int unmet)
    {
        unmet = 0;
        var instructions = new List<DispatchInstruction>();
        foreach (var kv in flow.EdgeFlows)
        {
            int from = kv.Key.From;
            int to = kv.Key.To;
            int count = kv.Value;
            if (count <= 0) continue;

            if (from == unmetNode && demands.ContainsKey(to))
            {
                unmet += count;
                continue;
            }

            if (!sources.TryGetValue(from, out var source)) continue;
            if (!demands.TryGetValue(to, out var demand)) continue;

            // 非首府 demand 的 role 是占位（Infantry），不能传出来；用 source 的实际兵种 role。
            var role = demand.State.IsCapital ? demand.Role : source.Bucket.Role;
            switch (source.Kind)
            {
                case SourceKind.InPlace:
                    instructions.Add(new InPlaceRecruitInstruction(source.Settlement, role, count));
                    break;
                case SourceKind.Village:
                    // source.Town = 首府 town；source.Settlement = MCMF 选定的目标村。
                    if (source.Town?.Settlement != null)
                        instructions.Add(new RecruiterPartyInstruction(
                            source.Town, source.Town.Settlement, source.Settlement, role, count));
                    break;
                case SourceKind.Garrison:
                    instructions.Add(new TransferPartyInstruction(source.Settlement, demand.State.Settlement, role, count));
                    break;
            }
        }
        return instructions;
    }

    private static void LogResult(CapitalManager manager, SupplyDemandGraphResult result, string logTag)
    {
        Logger.Info(
            $"{logTag} clan={manager.OwnerClan?.StringId} settlements={result.SettlementCount} " +
            $"demand={result.TotalDemand} flow={result.TotalFlow} cost={result.TotalCost} " +
            $"instructions={result.Instructions.Count} unmet={result.Unmet}");

        int shown = 0;
        foreach (var instruction in result.Instructions.Take(20))
        {
            Logger.Info("  " + logTag + " " + Describe(instruction));
            shown++;
        }
        if (result.Instructions.Count > shown)
            Logger.Info($"  {logTag} ... {result.Instructions.Count - shown} more instruction(s)");
    }

    private static string Describe(DispatchInstruction instruction)
    {
        switch (instruction)
        {
            case InPlaceRecruitInstruction x:
                return $"InPlace settlement='{x.Settlement?.StringId}' role={x.Role} count={x.Count}";
            case RecruiterPartyInstruction x:
                return $"Recruiter town='{x.Town?.Settlement?.StringId}' village='{x.TargetVillage?.StringId}' return='{x.ReturnSettlement?.StringId}' role={x.Role} count={x.Count}";
            case PrisonerConvertInstruction x:
                return $"Prison settlement='{x.Settlement?.StringId}' role={x.Role} count={x.Count}";
            case TransferPartyInstruction x:
                return $"Transfer src='{x.Source?.StringId}' dst='{x.Destination?.StringId}' role={x.Role} count={x.Count}";
            default:
                return $"Unknown role={instruction.Role} count={instruction.Count}";
        }
    }
}
