using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Algorithm;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Recruitment;
using SovereignTowns.Transfer;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Managers;

/// <summary>
/// 首府级驻军后勤调度器。
/// 每日按 clan 构造 MCMF 供需图，然后把 flow 解码为执行层 instruction。
/// 该类只负责求解和派发；siege、food、party cap、扣款、rollback 仍由各 dispatcher 自己校验。
/// </summary>
public sealed class CapitalLogisticsManager
{
    private readonly CapitalRegistry _capitalRegistry;
    private readonly RecruitmentDispatcher _recruitmentDispatcher;
    private readonly TransferDispatcher _transferDispatcher;

    public CapitalLogisticsManager(
        CapitalRegistry capitalRegistry,
        RecruitmentDispatcher recruitmentDispatcher,
        TransferDispatcher transferDispatcher)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
        _recruitmentDispatcher = recruitmentDispatcher ?? throw new ArgumentNullException(nameof(recruitmentDispatcher));
        _transferDispatcher = transferDispatcher ?? throw new ArgumentNullException(nameof(transferDispatcher));
    }

    public void EvaluateAll()
    {
        try
        {
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

        var result = RunMcmf(manager, capitalSettlement);
        if (result.SettlementCount == 0)
        {
            Logger.Debug($"CapitalLogisticsManager: clan={manager.OwnerClan?.StringId} has no owned town/castle nodes");
            return;
        }

        ExecuteMcmfInstructions(manager, result);
    }

    private static SupplyDemandGraphResult RunMcmf(CapitalManager manager, Settlement capitalSettlement)
    {
        try
        {
            return SupplyDemandGraph.Run(manager, capitalSettlement);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.MCMF failed (clan={manager.OwnerClan?.StringId})", ex);
            return new SupplyDemandGraphResult(0, 0, 0, 0, 0, new List<DispatchInstruction>());
        }
    }

    private void ExecuteMcmfInstructions(CapitalManager manager, SupplyDemandGraphResult result)
    {
        int accepted = 0;
        int skipped = 0;

        try
        {
            var inPlaceInstructions = new List<InPlaceRecruitInstruction>();
            var recruiterInstructions = new List<RecruiterPartyInstruction>();

            foreach (var instruction in result.Instructions)
            {
                if (instruction == null || instruction.Count <= 0)
                {
                    skipped++;
                    continue;
                }

                if (instruction is InPlaceRecruitInstruction inPlace)
                {
                    inPlaceInstructions.Add(inPlace);
                    continue;
                }
                if (instruction is RecruiterPartyInstruction recruiter)
                {
                    recruiterInstructions.Add(recruiter);
                    continue;
                }

                bool ok = ExecuteMcmfInstruction(manager, instruction);
                if (ok) accepted++;
                else skipped++;
            }

            foreach (var group in inPlaceInstructions.GroupBy(x => x.Settlement))
            {
                var first = group.First();
                int count = group.Sum(x => x.Count);
                bool ok = ExecuteInPlaceRecruitment(new InPlaceRecruitInstruction(first.Settlement, first.Role, count));
                if (ok) accepted++;
                else skipped++;
            }

            foreach (var group in recruiterInstructions.GroupBy(x => new { x.Town, x.ReturnSettlement }))
            {
                var first = group.First();
                int count = group.Sum(x => x.Count);
                bool ok = ExecuteRecruiterDispatch(manager, new RecruiterPartyInstruction(first.Town, first.ReturnSettlement, first.Role, count));
                if (ok) accepted++;
                else skipped++;
            }

            Logger.Info(
                $"CapitalLogistics MCMF execution: clan={manager.OwnerClan?.StringId} " +
                $"accepted={accepted} skipped={skipped} unmet={result.Unmet}");
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.ExecuteMcmfInstructions failed (clan={manager.OwnerClan?.StringId})", ex);
        }
    }

    private bool ExecuteMcmfInstruction(CapitalManager manager, DispatchInstruction instruction)
    {
        try
        {
            switch (instruction)
            {
                case InPlaceRecruitInstruction x:
                    return ExecuteInPlaceRecruitment(x);

                case RecruiterPartyInstruction x:
                    return ExecuteRecruiterDispatch(manager, x);

                case TransferPartyInstruction x:
                    return ExecuteTransferDispatch(manager, x);

                case PrisonerConvertInstruction x:
                    Logger.Debug(
                        $"CapitalLogistics MCMF: prisoner instruction skipped pending instruction-scoped prisoner conversion " +
                        $"settlement={x.Settlement?.StringId} role={x.Role} count={x.Count}");
                    return false;

                default:
                    Logger.Warn($"CapitalLogistics MCMF: unknown instruction '{instruction.GetType().Name}'");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.ExecuteMcmfInstruction failed ({instruction?.GetType().Name})", ex);
            return false;
        }
    }

    private static bool ExecuteInPlaceRecruitment(InPlaceRecruitInstruction instruction)
    {
        var settlement = instruction.Settlement;
        var garrison = settlement?.Town?.GarrisonParty?.MemberRoster;
        int current = garrison?.TotalManCount ?? 0;
        string reason = $"mcmf in-place role={instruction.Role} count={instruction.Count}";
        int recruited = CapitalInPlaceRecruiter.RecruitFromCapitalNotables(
            settlement,
            current + instruction.Count,
            reason);
        if (recruited > 0)
        {
            Logger.Info(
                $"CapitalLogistics MCMF: in-place recruited {recruited} troop(s) " +
                $"settlement='{settlement?.Name}' role={instruction.Role} requested={instruction.Count}");
            return true;
        }
        return false;
    }

    private bool ExecuteRecruiterDispatch(CapitalManager manager, RecruiterPartyInstruction instruction)
    {
        var capital = manager.GetCapital();
        if (capital == null || instruction.Town != capital || instruction.ReturnSettlement != capital.Settlement)
        {
            Logger.Debug(
                $"CapitalLogistics MCMF: recruiter skipped because current dispatcher only supports capital dispatch " +
                $"town={instruction.Town?.Settlement?.StringId} return={instruction.ReturnSettlement?.StringId}");
            return false;
        }

        string reason =
            $"mcmf recruiter clan={manager.OwnerClan?.StringId} role={instruction.Role} count={instruction.Count}";
        return _recruitmentDispatcher.TryDispatchRecruiter(instruction.Town, instruction.Count, reason);
    }

    private bool ExecuteTransferDispatch(CapitalManager manager, TransferPartyInstruction instruction)
    {
        string reason =
            $"mcmf transfer clan={manager.OwnerClan?.StringId} role={instruction.Role} count={instruction.Count}";
        var task = new TransferTask(
            instruction.Source,
            instruction.Destination,
            instruction.Count,
            instruction.Count,
            reason,
            instruction.Role);
        bool ok = _transferDispatcher.TryDispatchTransfer(task);
        if (ok)
        {
            DecisionAuditLogger.LogRule(
                decisionType: "CapitalLogisticsMcmfTransfer",
                inputSummary: $"clan={manager.OwnerClan?.StringId} src={instruction.Source.StringId} dest={instruction.Destination.StringId} role={instruction.Role} amount={instruction.Count}",
                decisionJson: $"{{\"src\":\"{instruction.Source.StringId}\",\"dest\":\"{instruction.Destination.StringId}\",\"role\":\"{instruction.Role}\",\"amount\":{instruction.Count}}}",
                accepted: true);
        }
        return ok;
    }

}
