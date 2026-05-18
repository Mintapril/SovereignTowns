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
        public SettlementState(Town town, Settlement settlement, TownGarrisonRule rule, int desiredTotal, bool isCapital)
        {
            Town = town;
            Settlement = settlement;
            Rule = rule;
            DesiredTotal = desiredTotal;
            IsCapital = isCapital;
            Buckets = MatchPolicy.Bucketize(town.GarrisonParty?.MemberRoster);
            Inbound = new Dictionary<GenericTroopRole, int>();
        }

        public Town Town { get; }
        public Settlement Settlement { get; }
        public TownGarrisonRule Rule { get; }
        public int DesiredTotal { get; }
        public bool IsCapital { get; }
        public List<TroopBucket> Buckets { get; }
        public Dictionary<GenericTroopRole, int> Inbound { get; }

        public int Current(GenericTroopRole role)
            => Buckets.Where(b => b.Role == role).Sum(b => b.Count);

        public int Projected(GenericTroopRole role)
            => Math.Max(0, Current(role) + Count(Inbound, role));

        public int Available(GenericTroopRole role)
            => Current(role);

        public TroopBucket? Bucket(GenericTroopRole role)
            => Buckets.FirstOrDefault(b => b.Role == role && b.Count > 0);

        public void AddInbound(GenericTroopRole role, int count)
            => AddCount(Inbound, role, count);

        private static int Count(IReadOnlyDictionary<GenericTroopRole, int> values, GenericTroopRole role)
            => values.TryGetValue(role, out var count) ? count : 0;

        private static void AddCount(Dictionary<GenericTroopRole, int> values, GenericTroopRole role, int count)
        {
            if (role == GenericTroopRole.Unknown || count <= 0) return;
            values[role] = Count(values, role) + count;
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

    public static SupplyDemandGraphResult Run(CapitalManager manager, Settlement capitalSettlement)
        => RunInternal(manager, capitalSettlement, "MCMF");

    private static SupplyDemandGraphResult RunInternal(CapitalManager manager, Settlement capitalSettlement, string logTag)
    {
        if (!_selfTestLogged)
        {
            _selfTestLogged = true;
            if (MinCostFlow.SelfTest(out var selfTestMessage))
                Logger.Info(logTag + ": " + selfTestMessage);
            else
                Logger.Error(logTag + " self-test failed: " + selfTestMessage);
        }

        var states = BuildSettlementStates(manager, capitalSettlement);
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
        var branchDemandByRole = new Dictionary<GenericTroopRole, int>();
        var capitalState = states.FirstOrDefault(s => s.IsCapital);
        var features = ConfigurationManager.Current?.EnabledFeatures;
        bool autoRecruitmentEnabled = features?.AutoRecruitment ?? true;
        bool troopTransfersEnabled = features?.TroopTransfers ?? true;

        foreach (var state in states)
        {
            foreach (var role in MatchPolicy.Roles)
            {
                int desired = MatchPolicy.DesiredCount(state.Rule, role, state.DesiredTotal);
                int current = state.Projected(role);
                int demand = Math.Max(0, desired - current);
                if (demand <= 0) continue;

                int demandNode = nextNodeId++;
                var def = new DemandDef(demandNode, state, role, desired, current, demand, isRecruitmentStockpile: false);
                demands[demandNode] = def;
                graph.AddEdge(demandNode, superSink, demand, 0);

                if (!state.IsCapital)
                    branchDemandByRole[role] = branchDemandByRole.TryGetValue(role, out var existing)
                        ? existing + demand
                        : demand;
            }
        }

        if (autoRecruitmentEnabled && capitalState != null && !capitalState.Settlement.IsUnderSiege)
        {
            foreach (var role in MatchPolicy.Roles)
            {
                int demand = branchDemandByRole.TryGetValue(role, out var value) ? value : 0;
                if (demand <= 0 || !MatchPolicy.AllowsRole(capitalState.Rule, role)) continue;

                int demandNode = nextNodeId++;
                var def = new DemandDef(
                    demandNode,
                    capitalState,
                    role,
                    desired: demand,
                    current: 0,
                    demand: demand,
                    isRecruitmentStockpile: true);
                demands[demandNode] = def;
                graph.AddEdge(demandNode, superSink, demand, 0);
            }
        }

        int totalDemand = demands.Values.Sum(d => d.Demand);
        if (totalDemand <= 0)
            return new SupplyDemandGraphResult(states.Count, 0, 0, 0, 0, new List<DispatchInstruction>());

        graph.AddEdge(superSource, unmetNode, totalDemand, 0);
        foreach (var demand in demands.Values)
            graph.AddEdge(unmetNode, demand.NodeId, demand.Demand, Thresholds.McmfUnmetCost);

        foreach (var state in states)
        {
            if (state.Settlement.IsUnderSiege) continue;
            if (troopTransfersEnabled)
                AddRosterSurplusSources(graph, sources, ref nextNodeId, superSource, state);
            if (!autoRecruitmentEnabled) continue;

            AddCharacterSources(graph, sources, ref nextNodeId, superSource, SourceKind.InPlace, state.Settlement, state.Town, EnumerateVolunteerTroops(state.Settlement));
            if (state.IsCapital)
                AddCharacterSources(graph, sources, ref nextNodeId, superSource, SourceKind.Village, state.Settlement, state.Town, EnumerateVillageVolunteerTroops(state.Town));
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

    private static List<SettlementState> BuildSettlementStates(CapitalManager manager, Settlement capitalSettlement)
    {
        var result = new List<SettlementState>();
        foreach (var town in Town.AllTowns)
        {
            if (town == null) continue;
            if (!(town.IsTown || town.IsCastle)) continue;
            if (town.OwnerClan == null || town.OwnerClan != manager.OwnerClan) continue;

            var settlement = town.Settlement;
            if (settlement == null || !settlement.IsActive) continue;

            var rule = ConfigurationManager.GetRuleFor(town) ?? TownGarrisonRule.CreateDefault();
            int desired = ComputeDesiredTarget(rule, RiskAssessmentService.Assess(settlement));
            result.Add(new SettlementState(town, settlement, rule, desired, settlement == capitalSettlement));
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

                var buckets = MatchPolicy.Bucketize(party.MemberRoster);
                if (buckets.Count == 0) continue;

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
                        AddInbound(returningSource, buckets);
                        continue;
                    }

                    if (destination != null && bySettlement.TryGetValue(destination, out var destinationState))
                        AddInbound(destinationState, buckets);
                }
                else if (party.PartyComponent is StRecruiterPartyComponent recruiter)
                {
                    var home = recruiter.HomeSettlementOrNull;
                    if (home != null && bySettlement.TryGetValue(home, out var homeState))
                        AddInbound(homeState, buckets);
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
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static void AddInbound(SettlementState state, IEnumerable<TroopBucket> buckets)
    {
        foreach (var bucket in buckets)
            state.AddInbound(bucket.Role, bucket.Count);
    }

    private static int ComputeDesiredTarget(TownGarrisonRule rule, RiskAssessment risk)
    {
        float multiplier = risk.Level >= RiskLevel.High
            ? rule.WartimeMultiplier
            : rule.PeacetimeMultiplier;
        return Math.Max(1, (int)Math.Round(rule.TargetTotalCount * multiplier));
    }

    private static void AddRosterSurplusSources(
        MinCostFlow graph,
        Dictionary<int, SourceDef> sources,
        ref int nextNodeId,
        int superSource,
        SettlementState state)
    {
        foreach (var bucket in state.Buckets)
        {
            int desired = MatchPolicy.DesiredCount(state.Rule, bucket.Role, state.DesiredTotal);
            int surplus = Math.Max(0, state.Available(bucket.Role) - desired);
            if (surplus <= 0) continue;

            var sourceBucket = new TroopBucket(bucket.Role, surplus, bucket.MinTier, bucket.Representative);
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

    private static IEnumerable<CharacterObject?> EnumerateVillageVolunteerTroops(Town town)
    {
        var villages = town.Villages;
        if (villages == null) yield break;

        foreach (var village in villages)
        {
            var settlement = village?.Settlement;
            if (settlement == null || !settlement.IsActive) continue;
            foreach (var character in EnumerateVolunteerTroops(settlement))
                yield return character;
        }
    }

    private static bool CanConnect(SourceDef source, DemandDef demand)
    {
        if (source.Bucket.Role != demand.Role) return false;
        if (source.Settlement.IsUnderSiege || demand.State.Settlement.IsUnderSiege) return false;

        if (demand.IsRecruitmentStockpile)
        {
            return demand.State.IsCapital
                && source.Settlement == demand.State.Settlement
                && (source.Kind == SourceKind.InPlace || source.Kind == SourceKind.Village);
        }

        switch (source.Kind)
        {
            case SourceKind.InPlace:
            case SourceKind.Village:
                return source.Settlement == demand.State.Settlement;
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
        int penalty = MatchPolicy.MatchPenalty(
            source.Bucket,
            demand.State.Rule,
            Thresholds.McmfHardPenalty,
            Thresholds.McmfTierPenalty);
        float distance = source.Kind == SourceKind.Garrison
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

            switch (source.Kind)
            {
                case SourceKind.InPlace:
                    instructions.Add(new InPlaceRecruitInstruction(source.Settlement, demand.Role, count));
                    break;
                case SourceKind.Village:
                    if (source.Town != null)
                        instructions.Add(new RecruiterPartyInstruction(source.Town, source.Settlement, demand.Role, count));
                    break;
                case SourceKind.Garrison:
                    instructions.Add(new TransferPartyInstruction(source.Settlement, demand.State.Settlement, demand.Role, count));
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
                return $"Recruiter town='{x.Town?.Settlement?.StringId}' return='{x.ReturnSettlement?.StringId}' role={x.Role} count={x.Count}";
            case PrisonerConvertInstruction x:
                return $"Prison settlement='{x.Settlement?.StringId}' role={x.Role} count={x.Count}";
            case TransferPartyInstruction x:
                return $"Transfer src='{x.Source?.StringId}' dst='{x.Destination?.StringId}' role={x.Role} count={x.Count}";
            default:
                return $"Unknown role={instruction.Role} count={instruction.Count}";
        }
    }
}
