using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Transfer;

/// <summary>
/// 调拨队 Dispatcher（B16.1）：从原 GarrisonTransferManager 瘦身而来。
/// 只负责"消费 TransferTask → 抽兵 → 创建 StTransferPartyComponent → 注册到 Lifecycle"。
/// 所有"在飞中"的状态机搬到 StTransferPartyComponent.OnHourlyTickCore。
/// </summary>
public sealed class TransferDispatcher
{
    private const string PartyKind = PartyLifecycleManager.KindTransfer;

    private readonly PartyLifecycleManager _lifecycle;

    public TransferDispatcher(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// 主入口：由 CapitalLogisticsManager 调用，把一个 TransferTask 转换为真实运输队伍。
    public bool TryDispatchTransfer(TransferTask task)
    {
        try
        {
            if (task == null) { Logger.Warn("TryDispatchTransfer: task is null"); return false; }
            var source = task.Source;
            var destination = task.Destination;
            var requested = task.RequestedTroops;

            if (source == null || destination == null)
            {
                Logger.Warn("TryDispatchTransfer: task.Source/Destination is null");
                return false;
            }
            if (requested <= 0) return false;
            if (source == destination) return false;
            if (source.OwnerClan == null || source.OwnerClan != destination.OwnerClan)
            {
                Logger.Warn($"  TransferDispatcher: cross-clan transfer rejected ({source.Name} -> {destination.Name})");
                return false;
            }
            if (!ConfigurationManager.Current.EnabledFeatures.TroopTransfers)
            {
                Logger.Debug($"  TransferDispatcher: skipped '{source.Name}' -> '{destination.Name}' — TroopTransfers disabled");
                return false;
            }
            if (!_lifecycle.CanCreateAnotherParty(source, PartyKind))
            {
                Logger.Info($"  TransferDispatcher: '{source.Name}' 已达调拨队上限，跳过");
                return false;
            }

            var sourceTown = source.Town;
            var sourceGarrison = sourceTown?.GarrisonParty;
            var sourceRoster = sourceGarrison?.MemberRoster;
            if (sourceTown == null || sourceGarrison == null || sourceRoster == null)
            {
                Logger.Warn($"  TransferDispatcher: source '{source.Name}' has no Town/GarrisonParty/MemberRoster");
                return false;
            }

            int totalAvailable = sourceRoster.TotalManCount;
            if (totalAvailable < requested)
            {
                Logger.Info($"  TransferDispatcher: '{source.Name}' total={totalAvailable} < req({requested}), 跳过");
                return false;
            }

            var transferRoster = TroopRoster.CreateDummyTroopRoster();
            int extracted = TroopTransferHelper.TransferFromGarrison(
                sourceRoster, transferRoster, requested, TroopTransferHelper.SortStrategy.LowestTierFirst);

            if (extracted <= 0)
            {
                Logger.Warn($"  TransferDispatcher: '{source.Name}' extracted 0 troops (req={requested}, available={totalAvailable})");
                return false;
            }

            var party = StTransferPartyComponent.CreateForRoute(source, destination, transferRoster);
            if (party == null)
            {
                Logger.Warn($"  TransferDispatcher: CreateForRoute 返回 null ({source.Name} -> {destination.Name})");
                TroopTransferHelper.TransferBackToGarrison(transferRoster, sourceRoster);
                return false;
            }

            _lifecycle.RegisterTrackedParty(party, source, PartyKind);
            try { party.SetMoveGoToSettlement(destination, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error("SetMoveGoToSettlement initial failed", ex); }

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchTransfer",
                inputSummary: $"source={source.StringId} dest={destination.StringId} requested={requested} extracted={extracted} priority={task.Priority:F2} reason={task.Reason}",
                decisionJson: $"{{\"source\":\"{source.StringId}\",\"dest\":\"{destination.StringId}\",\"requested\":{requested},\"extracted\":{extracted},\"priority\":{task.Priority:F2}}}",
                accepted: true);
            Logger.Info($"  TransferDispatcher: 派出调拨队 '{source.Name}' -> '{destination.Name}' (兵员={extracted}, priority={task.Priority:F1})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchTransfer failed", ex);
            return false;
        }
    }
}
