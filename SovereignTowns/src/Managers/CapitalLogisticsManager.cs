using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Parties;
using SovereignTowns.Recruitment;
using SovereignTowns.Transfer;
using SovereignTowns.Upgrades;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Managers;

/// <summary>
/// 首府级驻军后勤调度器。
/// 每日按 clan 汇总首府与所有自有城镇/城堡的缺口、富余和在途调拨，然后由首府统一决定：
/// 1) 首府本城 notable 招募；
/// 2) 是否从首府派出征兵队；
/// 3) 由哪些自有 settlement 向缺兵目标调拨兵力。
/// </summary>
public sealed class CapitalLogisticsManager
{
    // 阈值改读 PartyThresholds（玩家可在网页面板调）。人数阈值均按实际驻军比例派生。
    internal static float CriticalProjectedRatio
        => ConfigurationManager.Current?.Thresholds?.TransferCriticalProjectedRatio ?? 0.24f;
    private static float TransferRatio
        => ConfigurationManager.Current?.Thresholds?.TransferRatio ?? 0.30f;
    private static float MaxTroopsPerTaskRatio
        => ConfigurationManager.Current?.Thresholds?.TransferMaxTroopsPerTaskRatio ?? 0.67f;
    private static float MinTransferTroopRatio
        => ConfigurationManager.Current?.Thresholds?.TransferMinTroopRatio ?? 0.13f;
    private static float MinRecruitmentDemandRatio
        => ConfigurationManager.Current?.Thresholds?.RecruitmentMinDemandRatio ?? 0.07f;

    private const float MaxPairDistance = 100f;

    private readonly CapitalRegistry _capitalRegistry;
    private readonly RecruitmentManager _recruitmentManager;
    private readonly GarrisonTransferManager _transferManager;

    public CapitalLogisticsManager(
        CapitalRegistry capitalRegistry,
        RecruitmentManager recruitmentManager,
        GarrisonTransferManager transferManager)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
        _recruitmentManager = recruitmentManager ?? throw new ArgumentNullException(nameof(recruitmentManager));
        _transferManager = transferManager ?? throw new ArgumentNullException(nameof(transferManager));
    }

    public void EvaluateAll()
    {
        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.AutoGarrison)
            {
                Logger.Debug("CapitalLogisticsManager.EvaluateAll skipped: AutoGarrison disabled");
                return;
            }

            int evaluated = 0;
            foreach (var manager in _capitalRegistry.AllManagers)
            {
                try
                {
                    if (manager == null) continue;
                    EvaluateClan(manager);
                    evaluated++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"CapitalLogisticsManager: clan evaluation failed (clan={manager?.OwnerClan?.StringId})", ex);
                }
            }

            Logger.Info($"CapitalLogisticsManager.EvaluateAll: evaluated {evaluated} managed clan(s)");
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalLogisticsManager.EvaluateAll failed", ex);
        }
    }

    private void EvaluateClan(CapitalManager manager)
    {
        var capitalTown = manager.GetCapital();
        var capitalSettlement = capitalTown?.Settlement;
        if (capitalTown == null || capitalSettlement == null)
        {
            Logger.Debug($"CapitalLogisticsManager: clan={manager.OwnerClan?.StringId} has no valid capital town");
            return;
        }

        if (capitalTown.OwnerClan != manager.OwnerClan)
        {
            Logger.Warn($"CapitalLogisticsManager: capital '{capitalTown.Name}' owner drift; expected clan={manager.OwnerClan?.StringId}");
            return;
        }

        var nodes = BuildNodes(manager, capitalSettlement);
        if (nodes.Count == 0)
        {
            Logger.Debug($"CapitalLogisticsManager: clan={manager.OwnerClan?.StringId} has no owned town/castle nodes");
            return;
        }

        AccountInFlightParties(nodes, manager.OwnerClan);
        var capitalNode = nodes.FirstOrDefault(n => n.IsCapital);
        if (capitalNode == null)
        {
            Logger.Warn($"CapitalLogisticsManager: capital '{capitalSettlement.Name}' not found in owned nodes");
            return;
        }

        LogClanSnapshot(manager, nodes);
        TryUpgradeCapital(capitalNode);
        CoordinateRecruitment(capitalNode, nodes);

        // 原地招募/派征兵队可能改变首府驻军，调拨前重建一次快照。
        nodes = BuildNodes(manager, capitalSettlement);
        AccountInFlightParties(nodes, manager.OwnerClan);
        capitalNode = nodes.FirstOrDefault(n => n.IsCapital);
        if (capitalNode == null) return;

        DispatchTransfers(manager, capitalNode, nodes);
    }

    private static List<LogisticsNode> BuildNodes(CapitalManager manager, Settlement capitalSettlement)
    {
        var result = new List<LogisticsNode>();
        try
        {
            foreach (var town in Town.AllTowns)
            {
                if (town == null) continue;
                if (!(town.IsTown || town.IsCastle)) continue;
                // OwnerClan == null 时（刚被攻陷未交接的极短窗口）`!= manager.OwnerClan` 不能区分
                // "本氏族" 与 "无主"——必须显式排除无主，避免被当成本氏族城拉进 nodes。
                if (town.OwnerClan == null || town.OwnerClan != manager.OwnerClan) continue;

                var settlement = town.Settlement;
                if (settlement == null || !settlement.IsActive) continue;

                var rule = ConfigurationManager.GetRuleFor(town) ?? TownGarrisonRule.CreateDefault();
                var risk = RiskAssessmentService.Assess(settlement);
                int current = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                bool isCapital = settlement == capitalSettlement;
                int desired = ComputeDesiredTarget(rule, risk);

                result.Add(new LogisticsNode(town, settlement, rule, risk, current, desired, isCapital));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.BuildNodes failed (clan={manager.OwnerClan?.StringId})", ex);
        }
        return result;
    }

    private static int ComputeDesiredTarget(TownGarrisonRule rule, RiskAssessment risk)
    {
        float multiplier = risk.Level >= RiskLevel.High
            ? rule.WartimeMultiplier
            : rule.PeacetimeMultiplier;
        int scaled = (int)Math.Round(rule.TargetTotalCount * multiplier);
        return Math.Max(1, scaled);
    }

    private static void AccountInFlightParties(List<LogisticsNode> nodes, Clan? ownerClan)
    {
        // 直接收 manager.OwnerClan 作为权威氏族过滤源，避免从 nodes[0].Settlement.OwnerClan 反推
        // ——刚易主一瞬间该字段可能为 null 或已指向新主人，会让跨氏族 in-flight 错误计入。
        if (ownerClan == null) return;

        var bySettlement = new Dictionary<Settlement, LogisticsNode>();
        foreach (var node in nodes)
        {
            if (!bySettlement.ContainsKey(node.Settlement))
                bySettlement[node.Settlement] = node;
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
                int men = party.MemberRoster?.TotalManCount ?? 0;
                if (men <= 0) continue;

                if (party.PartyComponent is TransferPartyComponent transfer)
                {
                    var source = transfer.Source;
                    var destination = transfer.Destination;
                    var target = party.TargetSettlement;

                    if (source != null
                        && destination != null
                        && target == source
                        && bySettlement.TryGetValue(source, out var returningSource))
                    {
                        returningSource.Inbound += men;
                        continue;
                    }

                    if (source != null && bySettlement.TryGetValue(source, out var sourceNode))
                    {
                        sourceNode.Outbound += men;
                    }
                    if (destination != null && bySettlement.TryGetValue(destination, out var destNode))
                    {
                        destNode.Inbound += men;
                    }
                }
                else if (party.PartyComponent is RecruitingPartyComponent recruiter)
                {
                    var home = recruiter.HomeSettlement;
                    if (home != null && bySettlement.TryGetValue(home, out var homeNode))
                    {
                        homeNode.Inbound += men;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalLogisticsManager.AccountInFlightParties failed", ex);
        }
    }

    private static Clan? ResolvePartyClan(MobileParty party)
    {
        try
        {
            if (party.ActualClan != null) return party.ActualClan;
            if (party.PartyComponent is TransferPartyComponent transfer) return transfer.Source?.OwnerClan;
            if (party.PartyComponent is RecruitingPartyComponent recruiter) return recruiter.HomeSettlement?.OwnerClan;
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static void LogClanSnapshot(CapitalManager manager, List<LogisticsNode> nodes)
    {
        try
        {
            int demand = nodes.Sum(n => n.Demand);
            int criticalDemand = nodes.Sum(n => n.CriticalDemand);
            int capacity = nodes.Sum(n => n.TransferCapacity);
            int inbound = nodes.Sum(n => n.Inbound);
            int outbound = nodes.Sum(n => n.Outbound);

            Logger.Info(
                $"CapitalLogistics clan={manager.OwnerClan?.StringId}: nodes={nodes.Count} " +
                $"demand={demand} critical={criticalDemand} capacity={capacity} inbound={inbound} outbound={outbound}");

            foreach (var node in nodes.OrderByDescending(n => n.IsCapital).ThenByDescending(n => n.Priority))
            {
                Logger.Info(
                    $"  node '{node.Settlement.Name}' capital={node.IsCapital} risk={node.Risk.Level} " +
                    $"current={node.CurrentMen} inbound={node.Inbound} outbound={node.Outbound} projected={node.ProjectedMen} " +
                    $"desired={node.DesiredTarget} demand={node.Demand} capacity={node.TransferCapacity}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalLogisticsManager.LogClanSnapshot failed", ex);
        }
    }

    private static void TryUpgradeCapital(LogisticsNode capitalNode)
    {
        try
        {
            var rule = capitalNode.Rule;
            if (!rule.AllowAutoUpgrade) return;

            var snap = TroopCompositionEvaluator.Snapshot(capitalNode.Town.GarrisonParty?.MemberRoster);
            if (snap.Total <= 0) return;
            if (snap.Tier1To2 <= snap.Total * 0.3f) return;

            int budgetCap = Math.Max(rule.BudgetLimit / 4, 500);
            var report = TroopUpgradeService.TryUpgradeGarrison(capitalNode.Town, budgetCap, maxUpgradesPerCall: 20);
            if (report.DidWork)
            {
                Logger.Info($"CapitalLogistics: upgraded capital '{capitalNode.Settlement.Name}' units={report.UpgradedUnits}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.TryUpgradeCapital failed for '{capitalNode.Settlement?.Name}'", ex);
        }
    }

    private void CoordinateRecruitment(LogisticsNode capitalNode, List<LogisticsNode> nodes)
    {
        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment)
            {
                Logger.Debug("CapitalLogistics recruitment skipped: AutoRecruitment disabled");
                return;
            }

            int totalDemand = nodes.Sum(n => n.Demand);
            int branchDemand = nodes.Where(n => !n.IsCapital).Sum(n => n.Demand);
            int criticalDemand = nodes.Sum(n => n.CriticalDemand);
            if (totalDemand <= 0 && criticalDemand <= 0) return;

            int stockpileForBranches = Math.Min(branchDemand, Math.Max(0, capitalNode.Rule.TargetTotalCount));
            int desiredCapitalStock = Math.Max(
                capitalNode.DesiredTarget,
                capitalNode.DesiredTarget + stockpileForBranches);

            string reason =
                $"capital logistics totalDemand={totalDemand} branchDemand={branchDemand} criticalDemand={criticalDemand}";

            int inPlace = CapitalInPlaceRecruiter.RecruitFromCapitalNotables(
                capitalNode.Settlement,
                desiredCapitalStock,
                reason);

            if (inPlace > 0)
            {
                Logger.Info($"CapitalLogistics: capital in-place recruited {inPlace} troop(s) for '{capitalNode.Settlement.Name}'");
            }

            int remainingDemand = Math.Max(0, totalDemand - inPlace);
            int remainingCriticalDemand = Math.Max(0, criticalDemand - inPlace);
            int recruitmentDemandThreshold = Math.Max(
                1,
                GarrisonThresholdMath.CountFromRatio(capitalNode.CurrentMen, MinRecruitmentDemandRatio, minimumWhenPositive: 1));
            if (remainingDemand >= recruitmentDemandThreshold || remainingCriticalDemand > 0)
            {
                _recruitmentManager.TryDispatchRecruiter(capitalNode.Town, Math.Max(remainingDemand, remainingCriticalDemand), reason);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.CoordinateRecruitment failed for '{capitalNode.Settlement?.Name}'", ex);
        }
    }

    private void DispatchTransfers(CapitalManager manager, LogisticsNode capitalNode, List<LogisticsNode> nodes)
    {
        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.TroopTransfers)
            {
                Logger.Debug("CapitalLogistics transfers skipped: TroopTransfers disabled");
                return;
            }

            var remainingCapacity = new Dictionary<Settlement, int>();
            var remainingDemand = new Dictionary<Settlement, int>();
            foreach (var node in nodes)
            {
                remainingCapacity[node.Settlement] = node.TransferCapacity;
                remainingDemand[node.Settlement] = node.Demand;
            }

            var demands = nodes
                .Where(n => n.Demand > 0 && !n.Settlement.IsUnderSiege)
                .OrderByDescending(n => n.Priority)
                .ThenBy(n => Distance(capitalNode.Settlement, n.Settlement))
                .ToList();

            int dispatched = 0;
            foreach (var dest in demands)
            {
                int guard = 0;
                while (remainingDemand.TryGetValue(dest.Settlement, out var demand) && demand > 0 && guard++ < nodes.Count)
                {
                    var source = FindBestSource(dest, nodes, remainingCapacity, capitalNode);
                    if (source == null) break;

                    int capacity = remainingCapacity[source.Settlement];
                    int maxTroopsPerTask = GarrisonThresholdMath.CountFromRatio(source.CurrentMen, MaxTroopsPerTaskRatio, minimumWhenPositive: 1);
                    if (maxTroopsPerTask <= 0) break;
                    int byRatio = Math.Max(1, (int)Math.Round(source.CurrentMen * TransferRatio));
                    int amount = Math.Min(maxTroopsPerTask, Math.Min(demand, Math.Min(capacity, byRatio)));
                    if (amount <= 0) break;
                    int minTransferTroops = GarrisonThresholdMath.CountFromRatio(source.CurrentMen, MinTransferTroopRatio, minimumWhenPositive: 0);
                    if (amount < minTransferTroops && dest.CriticalDemand <= 0)
                    {
                        Logger.Debug(
                            $"CapitalLogistics: skip trivial transfer src='{source.Settlement.Name}' dest='{dest.Settlement.Name}' amount={amount} min={minTransferTroops}");
                        break;
                    }

                    string routeKind = source.IsCapital || dest.IsCapital ? "capital-coordinated" : "branch-to-branch";
                    string reason =
                        $"{routeKind} clan={manager.OwnerClan?.StringId} " +
                        $"srcCurrent={source.CurrentMen} srcCapacity={capacity} destDemand={demand} " +
                        $"destCritical={dest.CriticalDemand} destRisk={dest.Risk.Level}";

                    var task = new TransferTask(source.Settlement, dest.Settlement, amount, dest.Priority, reason);
                    bool ok = _transferManager.TryDispatchTransfer(task);
                    if (ok)
                    {
                        remainingCapacity[source.Settlement] = Math.Max(0, capacity - amount);
                        remainingDemand[dest.Settlement] = Math.Max(0, demand - amount);
                        dispatched++;

                        DecisionAuditLogger.LogRule(
                            decisionType: "CapitalLogisticsTransfer",
                            inputSummary: $"clan={manager.OwnerClan?.StringId} src={source.Settlement.StringId} dest={dest.Settlement.StringId} amount={amount}",
                            decisionJson: $"{{\"src\":\"{source.Settlement.StringId}\",\"dest\":\"{dest.Settlement.StringId}\",\"amount\":{amount},\"priority\":{dest.Priority},\"reason\":\"{AuditHelpers.EscapeJson(reason)}\"}}",
                            accepted: true);
                    }
                    else
                    {
                        remainingCapacity[source.Settlement] = 0;
                    }
                }
            }

            Logger.Info($"CapitalLogistics transfers: clan={manager.OwnerClan?.StringId} dispatched={dispatched}");
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.DispatchTransfers failed (clan={manager.OwnerClan?.StringId})", ex);
        }
    }

    private static LogisticsNode? FindBestSource(
        LogisticsNode destination,
        List<LogisticsNode> nodes,
        Dictionary<Settlement, int> remainingCapacity,
        LogisticsNode capitalNode)
    {
        LogisticsNode? best = null;
        float bestScore = float.MaxValue;

        foreach (var source in nodes)
        {
            if (source.Settlement == destination.Settlement) continue;
            if (!remainingCapacity.TryGetValue(source.Settlement, out var capacity) || capacity <= 0) continue;
            if (source.Settlement.OwnerClan != destination.Settlement.OwnerClan) continue;
            if (source.Settlement.MapFaction != destination.Settlement.MapFaction) continue;
            if (source.Settlement.IsUnderSiege) continue;
            if (source.Risk.Level >= RiskLevel.High) continue;

            if (destination.IsCapital && source.IsCapital) continue;

            float distance = Distance(source.Settlement, destination.Settlement);
            if (distance > MaxPairDistance) continue;

            int maxTroopsPerTask = GarrisonThresholdMath.CountFromRatio(source.CurrentMen, MaxTroopsPerTaskRatio, minimumWhenPositive: 1);
            float score = distance - Math.Min(capacity, maxTroopsPerTask) * 0.05f;

            if (!destination.IsCapital)
            {
                if (!source.IsCapital)
                {
                    score -= 25f;
                }
                else if (source.Settlement == capitalNode.Settlement)
                {
                    score += 10f;
                }
            }

            if (score < bestScore)
            {
                best = source;
                bestScore = score;
            }
        }

        return best;
    }

    private static float Distance(Settlement a, Settlement b)
    {
        try { return (a.GetPosition2D - b.GetPosition2D).Length; }
        catch { return float.MaxValue; }
    }

    private sealed class LogisticsNode
    {
        public LogisticsNode(
            Town town,
            Settlement settlement,
            TownGarrisonRule rule,
            RiskAssessment risk,
            int currentMen,
            int desiredTarget,
            bool isCapital)
        {
            Town = town;
            Settlement = settlement;
            Rule = rule;
            Risk = risk;
            CurrentMen = currentMen;
            DesiredTarget = desiredTarget;
            IsCapital = isCapital;
        }

        public Town Town { get; }
        public Settlement Settlement { get; }
        public TownGarrisonRule Rule { get; }
        public RiskAssessment Risk { get; }
        public int CurrentMen { get; }
        public int DesiredTarget { get; }
        public bool IsCapital { get; }
        public int Inbound { get; set; }
        public int Outbound { get; set; }

        public int ProjectedMen => Math.Max(0, CurrentMen + Inbound);

        public int CriticalThreshold =>
            CriticalProjectedRatio <= 0f
                ? 0
                : Math.Max(1, GarrisonThresholdMath.CountFromRatio(DesiredTarget, CriticalProjectedRatio, minimumWhenPositive: 1));

        public int CriticalDemand => Math.Max(0, CriticalThreshold - ProjectedMen);

        public int Demand => Math.Max(0, DesiredTarget - ProjectedMen);

        public int Reserve => DesiredTarget;

        public int TransferCapacity => Math.Max(0, CurrentMen - Reserve);

        public int Priority =>
            (CriticalDemand > 0 ? 1000 : 0)
            + ((int)Risk.Level * 100)
            + Demand
            + CriticalDemand * 2;
    }
}
