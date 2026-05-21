using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SovereignTowns.Algorithm;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Recruitment;
using SovereignTowns.Transfer;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

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

        // Task 6: Pass A(分配求解器)在路由 MCMF 之前跑一次。auto 模式下 Pass A 的每城目标
        // 是路由的权威输入;manual 模式下 Pass A 只作推荐,玩家手动目标驱动路由,推荐值 stash 成
        // GarrisonAssessment 供控制面板消费。
        var passA = RunPassA(manager);
        bool manualMode = ConfigurationManager.Current?.FiscalAutonomy?.AllowManualGarrisonTargets ?? false;
        if (manualMode)
            StashAssessments(manager, passA);

        var result = RunMcmf(manager, capitalSettlement, passA);
        if (result.SettlementCount == 0)
        {
            Logger.Debug($"CapitalLogisticsManager: clan={manager.OwnerClan?.StringId} has no owned town/castle nodes");
            return;
        }

        ExecuteMcmfInstructions(manager, result);
    }

    private static GarrisonAllocationResult RunPassA(CapitalManager manager)
    {
        try
        {
            return GarrisonAllocationSolver.Solve(manager);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.RunPassA failed (clan={manager.OwnerClan?.StringId})", ex);
            return new GarrisonAllocationResult();
        }
    }

    private static SupplyDemandGraphResult RunMcmf(
        CapitalManager manager, Settlement capitalSettlement, GarrisonAllocationResult passA)
    {
        try
        {
            return SupplyDemandGraph.Run(manager, capitalSettlement, passA);
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
        if (settlement == null) return false;
        var capitalRegistry = SovereignTowns.Capital.CapitalRegistry.Instance;
        bool isCapital = capitalRegistry != null
            && settlement == capitalRegistry.GetCapitalForClan(settlement.OwnerClan);

        if (isCapital)
        {
            var garrison = settlement.Town?.GarrisonParty?.MemberRoster;
            int current = garrison?.TotalManCount ?? 0;
            string reason = $"mcmf in-place role={instruction.Role} count={instruction.Count}";
            int recruited = CapitalInPlaceRecruiter.RecruitFromCapitalNotables(
                settlement,
                current + instruction.Count,
                reason);
            if (recruited > 0)
            {
                Logger.Info($"CapitalLogistics MCMF: capital in-place recruited {recruited} troop(s) settlement='{settlement.Name}' requested={instruction.Count}");
                return true;
            }
            return false;
        }
        else
        {
            var branchRule = ConfigurationManager.GetBranchRuleFor(settlement.Town) ?? BranchRule.CreateDefault();
            int recruited = BranchInPlaceRecruiter.RecruitFromBranchNotables(
                settlement,
                branchRule.TargetPower,
                $"mcmf branch in-place flow={instruction.Count}");
            if (recruited > 0)
            {
                Logger.Info($"CapitalLogistics MCMF: branch in-place recruited {recruited} troop(s) settlement='{settlement.Name}' targetPower={branchRule.TargetPower}");
                return true;
            }
            return false;
        }
    }

    private bool ExecuteRecruiterDispatch(CapitalManager manager, RecruiterPartyInstruction instruction)
    {
        var capital = manager.GetCapital();
        if (capital == null || instruction.Town != capital || instruction.ReturnSettlement != capital.Settlement)
        {
            Logger.Warn(
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

    // ── Task 6: 手动模式评估 stash ──────────────────────────────────────────

    /// <summary>
    /// 手动驻军目标模式下,Pass A 的推荐值与玩家手动目标的对比评估。按 clan StringId 分组。
    /// 镜像 <see cref="WebConfig.SettlementsSnapshot"/> 的静态线程安全模式 —— HTTP / 控制面板
    /// handler 跑在 ThreadPool 线程,而写入发生在 Campaign 主线程(daily evaluate)。
    /// 引用赋值在 CLR 上原子;<see cref="Volatile"/> 套一层 release-acquire 语义,无需锁。
    /// DTO 只持 string / 数值,绝不持 Settlement / Clan vanilla 引用。
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<GarrisonAssessment>> _latestAssessments
        = new Dictionary<string, IReadOnlyList<GarrisonAssessment>>();

    /// <summary>控制面板 / WebUI handler 调用:读最近一份评估快照(引用赋值原子,零拷贝)。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<GarrisonAssessment>> LatestAssessments
        => Volatile.Read(ref _latestAssessments)
           ?? new Dictionary<string, IReadOnlyList<GarrisonAssessment>>();

    /// <summary>
    /// 在 Campaign 主线程调用:对该 clan 的每个受管城/堡构造一条 GarrisonAssessment
    /// (玩家手动目标 vs Pass A 推荐),整体替换该 clan 在 stash 中的条目。
    /// 仅手动模式调用。任何失败保留旧快照不变(本方法整体 try/catch)。
    /// </summary>
    private static void StashAssessments(CapitalManager manager, GarrisonAllocationResult passA)
    {
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null || passA == null) return;
            string clanId = clan.StringId ?? "";

            int wagePerTroop = WagePerTroopAtMaxTier(manager);
            var assessments = new List<GarrisonAssessment>(8);

            foreach (var town in clan.Fiefs)
            {
                if (town?.Settlement == null || !town.Settlement.IsActive) continue;
                if (!(town.IsTown || town.IsCastle)) continue;

                try
                {
                    var settlement = town.Settlement;
                    int playerTarget;
                    if (town.IsTown)
                    {
                        var rule = ConfigurationManager.GetRuleFor(town) ?? TownGarrisonRule.CreateDefault();
                        var risk = RiskAssessmentService.Assess(settlement);
                        float mul = risk.Level >= RiskLevel.High ? rule.WartimeMultiplier : rule.PeacetimeMultiplier;
                        playerTarget = Math.Max(0, (int)Math.Round(rule.TargetTotalCount * mul));
                    }
                    else
                    {
                        // 城堡:BranchRule.TargetPower 是 power 口径,这里直接当目标头数展示
                        // (评估值,非路由输入;路由侧自有 power↔头数换算)。
                        var branch = ConfigurationManager.GetBranchRuleFor(town) ?? BranchRule.CreateDefault();
                        playerTarget = Math.Max(0, branch.TargetPower);
                    }

                    int recommended = passA.Target.TryGetValue(settlement, out var rec) ? Math.Max(0, rec) : 0;

                    assessments.Add(new GarrisonAssessment
                    {
                        SettlementId = settlement.StringId ?? "",
                        PlayerTarget = playerTarget,
                        RecommendedTarget = recommended,
                        DailyWageDelta = (playerTarget - recommended) * wagePerTroop,
                        LoopClosesAtPlayerTarget = playerTarget <= recommended,
                    });
                }
                catch (Exception inner)
                {
                    Logger.Error($"CapitalLogisticsManager.StashAssessments: skipping '{town?.Settlement?.StringId}' on error", inner);
                }
            }

            // 复制旧字典 → 替换本 clan 条目 → 整体原子换上(读侧拿到的要么旧字典要么新字典)。
            // 注意:net472 的 Dictionary 复制构造只接受 IDictionary,IReadOnlyDictionary 须逐项拷贝。
            var snapshot = new Dictionary<string, IReadOnlyList<GarrisonAssessment>>();
            var previous = Volatile.Read(ref _latestAssessments);
            if (previous != null)
                foreach (var kv in previous)
                    snapshot[kv.Key] = kv.Value;
            snapshot[clanId] = assessments;
            Volatile.Write(ref _latestAssessments, snapshot);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.StashAssessments failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// 满级单兵工资 = PartyWageModel.GetCharacterWage(满级 tier 代表兵种)。
    /// 满级 tier 取自首府 TownGarrisonRule.MaxTier(取不到默认 5)。GarrisonAssessment 的
    /// DailyWageDelta 用。任何失败 → 返回 1(保守)。逻辑对应 GarrisonAllocationSolver
    /// 的私有 WagePerTroopAtMaxTier;因 Task 6 只允许改 3 个文件、不暴露 solver 内部,故本地重实现。
    /// </summary>
    private static int WagePerTroopAtMaxTier(CapitalManager manager)
    {
        try
        {
            int maxTier = 5;
            Town? capitalTown = null;
            try { capitalTown = manager?.GetCapital(); }
            catch (Exception ex)
            {
                Logger.Warn($"CapitalLogisticsManager.WagePerTroopAtMaxTier: GetCapital threw, falling back: {ex.Message}");
                capitalTown = null;
            }
            if (capitalTown == null)
                capitalTown = manager?.OwnerClan?.Fiefs?.FirstOrDefault(t => t != null && t.IsTown);
            if (capitalTown != null)
            {
                var rule = ConfigurationManager.GetRuleFor(capitalTown);
                if (rule != null && rule.MaxTier > 0) maxTier = rule.MaxTier;
            }

            var wageModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PartyWageModel;
            if (wageModel == null) return 1;

            var rep = GarrisonPowerEvaluator.MakeStubTroop(maxTier, mounted: false)
                      ?? GarrisonPowerEvaluator.MakeStubTroop(maxTier, mounted: true);
            if (rep == null) return 1;

            return Math.Max(1, wageModel.GetCharacterWage(rep));
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalLogisticsManager.WagePerTroopAtMaxTier failed", ex);
            return 1;
        }
    }
}
