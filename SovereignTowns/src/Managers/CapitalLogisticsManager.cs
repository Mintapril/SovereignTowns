using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SovereignTowns.Algorithm;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Recruitment;
using SovereignTowns.Transfer;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
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
        else
            // M-1: 自动模式下 Pass A 直接驱动路由,不存在"玩家目标 vs 推荐"差异 —
            // 清掉该 clan 上一次手动模式残留的 assessment,避免控制面板展示过期数据。
            ClearAssessments(manager);

        // Task 9: 财政自治财务视图快照（金库 + 单城 P&L）。在主线程产出纯数值 DTO，
        // 供 /api/finance 与控制面板 FinanceTabVM 跨线程只读消费。
        StashFinancialSnapshot(manager, passA);

        var result = RunMcmf(manager, capitalSettlement, passA);
        if (result.SettlementCount == 0)
        {
            Logger.Debug($"CapitalLogisticsManager: clan={manager.OwnerClan?.StringId} has no owned town/castle nodes");
            return;
        }

        ExecuteMcmfInstructions(manager, result);

        // 和平期遣散超额驻军：必须在 MCMF 之后跑,此时 passA 的每城目标已算出并用于路由。
        DisbandExcessGarrisons(manager, passA);
    }

    /// <summary>
    /// 和平期遣散超额驻军。遍历该氏族的所有 fief(clan.Fiefs),逐城/堡施加跳过门限
    /// (功能开关 / 手动模式 / 围攻 / 高风险 / 未被 Pass A 分配),对实际头数超过
    /// 可承担目标 × DisbandExcessThreshold 的城/堡,通过
    /// TroopTransferHelper.TransferFromGarrison(LowestTierFirst)抽走超额兵员并丢弃(废除)。
    /// </summary>
    private static void DisbandExcessGarrisons(CapitalManager manager, GarrisonAllocationResult passA)
    {
        try
        {
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();

            // Gate 1: feature disabled
            if (!cfg.DisbandUnaffordableExcess) return;

            // Gate 2: manual mode — player chose to over-garrison; never disband
            if (cfg.AllowManualGarrisonTargets) return;

            var clan = manager?.OwnerClan;
            if (clan == null) return;

            foreach (var town in clan.Fiefs)
            {
                if (town == null) continue;
                var settlement = town.Settlement;
                if (settlement == null || !settlement.IsActive) continue;

                try
                {
                    // Gate 3: under siege — never disband
                    if (settlement.IsUnderSiege) continue;

                    // Gate 4: high/critical risk — peacetime-only, skip when threatened
                    var risk = RiskAssessmentService.Assess(settlement);
                    if (risk.Level >= RiskLevel.High) continue;

                    // Gate 5: skip if the solver did not allocate this settlement at all.
                    // The solver pre-seeds Target=0 for every fief, so present-with-0 is NOT skipped
                    // here — only a settlement genuinely missing from the result is skipped.
                    // Present-with-0 falls through to the MinGarrisonFloor clamp below.
                    if (!passA.Target.TryGetValue(settlement, out int affordable)) continue;

                    // MinGarrisonFloor is the design's guaranteed minimum garrison (fiscal-autonomy §3.5):
                    // a budget-starved clan whose affordable target came out below the floor keeps the
                    // floor as an accepted subsidy — disband-excess must never breach it. The Math.Max
                    // clamp uniformly handles affordable==0 and any affordable < MinGarrisonFloor.
                    int floor = Math.Max(0, cfg.MinGarrisonFloor);
                    int effectiveTarget = Math.Max(affordable, floor);
                    // MinGarrisonFloor=0 + affordable=0 → skip rather than disband to 0
                    if (effectiveTarget <= 0) continue;

                    int current = GarrisonThresholdMath.ActualGarrisonCount(settlement);

                    // Gate 6: not over threshold
                    if (current <= (int)(effectiveTarget * cfg.DisbandExcessThreshold)) continue;

                    int excess = current - effectiveTarget;
                    if (excess <= 0) continue;

                    var garrison = town.GarrisonParty;
                    if (garrison == null) continue;
                    var garrisonRoster = garrison.MemberRoster;
                    if (garrisonRoster == null) continue;

                    // Use a throwaway roster as target — troops extracted here are abandoned (disbanded).
                    // This matches the established TroopRoster.CreateDummyTroopRoster() pattern used
                    // across transfer/patrol/recruiter paths (no PartyBase needed).
                    var discardRoster = TroopRoster.CreateDummyTroopRoster();
                    int disbanded = TroopTransferHelper.TransferFromGarrison(
                        garrisonRoster,
                        discardRoster,
                        excess,
                        TroopTransferHelper.SortStrategy.LowestTierFirst);

                    if (disbanded > 0)
                    {
                        Logger.Info(
                            $"DisbandExcessGarrisons: settlement='{settlement.StringId}' " +
                            $"current={current} affordable={affordable} effectiveTarget={effectiveTarget} " +
                            $"threshold={cfg.DisbandExcessThreshold:F2} disbanded={disbanded}");
                        DecisionAuditLogger.LogRule(
                            decisionType: "DisbandExcessGarrison",
                            inputSummary: $"settlement={settlement.StringId} clan={clan.StringId} current={current} affordable={affordable} effectiveTarget={effectiveTarget} disbanded={disbanded}",
                            decisionJson: $"{{\"settlement\":\"{settlement.StringId}\",\"clan\":\"{clan.StringId}\",\"current\":{current},\"affordable\":{affordable},\"effectiveTarget\":{effectiveTarget},\"disbanded\":{disbanded}}}",
                            accepted: true);
                    }
                }
                catch (Exception perEx)
                {
                    Logger.Error(
                        $"DisbandExcessGarrisons: per-settlement failed (settlement='{town?.Settlement?.StringId}' clan='{clan?.StringId}')",
                        perEx);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"DisbandExcessGarrisons failed (clan='{manager?.OwnerClan?.StringId}')", ex);
        }
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

            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            // I-1: 路由 manual 模式把目标 clamp 到 cfg.MaxGarrisonHardCap(见
            // SupplyDemandGraph.BuildSettlementStates)。评估面板必须显示路由真实采用的值,
            // 否则 DailyWageDelta 是虚构数 —— 这里用同一 hardCap 钳制 playerTarget。
            int hardCap = Math.Max(1, cfg.MaxGarrisonHardCap);

            // I-2: 复用 GarrisonAllocationSolver 的满级工资口径,不再本地重实现。
            // solver 的 WagePerTroopAtMaxTier(manager, towns) 中 towns 仅作 GetCapital 失败兜底。
            // manager 在 clan != null 守卫之后必非 null(clan = manager?.OwnerClan)。
            var fiefList = clan.Fiefs?.Where(t => t != null).ToList() ?? new List<Town>();
            int wagePerTroop = GarrisonAllocationSolver.WagePerTroopAtMaxTier(manager!, fiefList);
            var assessments = new List<GarrisonAssessment>(8);

            foreach (var town in fiefList)
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
                        // ComputeDesiredTarget 同口径:Math.Max(1, round(...)) 再 clamp 到 hardCap。
                        playerTarget = Math.Min(Math.Max(1, (int)Math.Round(rule.TargetTotalCount * mul)), hardCap);
                    }
                    else
                    {
                        // 城堡:BranchRule.TargetPower 是 power 口径,这里直接当目标头数展示
                        // (评估值,非路由输入;路由侧自有 power↔头数换算)。同样 clamp 到 hardCap
                        // 避免面板显示超出路由允许的上限。
                        var branch = ConfigurationManager.GetBranchRuleFor(town) ?? BranchRule.CreateDefault();
                        playerTarget = Math.Min(Math.Max(1, branch.TargetPower), hardCap);
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

            ReplaceClanAssessments(clanId, assessments);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.StashAssessments failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// M-1:自动模式下清掉该 clan 残留的手动模式评估,避免控制面板展示过期数据。
    /// 该 clan 不在 stash 中时是 no-op。
    /// </summary>
    private static void ClearAssessments(CapitalManager manager)
    {
        try
        {
            string clanId = manager?.OwnerClan?.StringId ?? "";
            ReplaceClanAssessments(clanId, removeOnly: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.ClearAssessments failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// 复制旧字典 → 替换 / 删除本 clan 条目 → 整体原子换上(读侧拿到的要么旧字典要么新字典)。
    /// 注意:net472 的 Dictionary 复制构造只接受 IDictionary,IReadOnlyDictionary 须逐项拷贝。
    /// <paramref name="removeOnly"/> 为 true 时删除该 clan 条目,否则写入 <paramref name="entries"/>。
    /// </summary>
    private static void ReplaceClanAssessments(
        string clanId, List<GarrisonAssessment>? entries = null, bool removeOnly = false)
    {
        var snapshot = new Dictionary<string, IReadOnlyList<GarrisonAssessment>>();
        var previous = Volatile.Read(ref _latestAssessments);
        if (previous != null)
            foreach (var kv in previous)
                snapshot[kv.Key] = kv.Value;

        if (removeOnly)
        {
            if (!snapshot.Remove(clanId)) return;  // 无残留 → 无需替换引用
        }
        else
        {
            snapshot[clanId] = entries ?? new List<GarrisonAssessment>();
        }
        Volatile.Write(ref _latestAssessments, snapshot);
    }

    // ── Task 9: 财政自治财务视图快照 ──────────────────────────────────────────

    /// <summary>一次性 warn 去重：registered ClanFinanceModel 不是 STClanFinanceModel 时只警告一次。</summary>
    private static bool _warnedFinanceModelMissing;

    /// <summary>
    /// 在 Campaign 主线程调用:对该受管氏族产出一份 <see cref="WebConfig.FinancialSnapshot.ClanFinance"/>
    /// (金库余额/缓冲上限/日均开销 + 各受管领地单城 P&amp;L),整体替换该 clan 在快照中的条目。
    /// 收入用 <see cref="Models.STClanFinanceModel.SafeTownIncome"/> 重算;推荐头数取 Pass A 输出。
    /// 任何失败保留旧快照不变(本方法整体 try/catch)。
    /// </summary>
    private static void StashFinancialSnapshot(CapitalManager manager, GarrisonAllocationResult passA)
    {
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return;
            string clanId = clan.StringId ?? "";
            if (string.IsNullOrEmpty(clanId)) return;

            var treasury = manager.Treasury;
            int bufferDays = ConfigurationManager.Current?.FiscalAutonomy?.TreasuryBufferDays ?? 30;

            // STClanFinanceModel 的 SafeTownIncome 是只读重算 helper(绝不抛、绝不改金库)。
            // 优先复用已注册的 model 实例;取不到则构造一个(无状态,构造廉价)。
            // 取不到说明别的 mod 覆盖了 ClanFinanceModel —— 一次性 warn 提示兼容性意外
            // (SafeTownIncome 是纯读 helper,fallback 实例本身安全)。
            var financeModel =
                TaleWorlds.CampaignSystem.Campaign.Current?.Models?.ClanFinanceModel as Models.STClanFinanceModel;
            if (financeModel == null)
            {
                if (!_warnedFinanceModelMissing)
                {
                    _warnedFinanceModelMissing = true;
                    Logger.Warn("CapitalLogisticsManager.StashFinancialSnapshot: registered ClanFinanceModel is not STClanFinanceModel (another mod overrode it); using a standalone instance for the read-only SafeTownIncome helper.");
                }
                financeModel = new Models.STClanFinanceModel();
            }

            var cf = new WebConfig.FinancialSnapshot.ClanFinance
            {
                ClanId = clanId,
                ClanName = clan.Name?.ToString() ?? clanId,
                TreasuryBalance = treasury?.Balance ?? 0,
                BufferCap = treasury?.BufferCap(bufferDays) ?? 0,
                TrailingDailyExpense = treasury?.TrailingDailyExpense() ?? 0,
            };

            var fiefs = clan.Fiefs?.Where(t => t?.Settlement != null && t.Settlement.IsActive).ToList()
                        ?? new List<Town>();
            foreach (var town in fiefs)
            {
                try
                {
                    var s = town.Settlement;
                    long income = financeModel.SafeTownIncome(clan, town);
                    long wage = 0;
                    var gp = town.GarrisonParty;
                    if (gp != null && gp.IsActive) wage = Math.Max(0, gp.TotalWage);
                    int current = Math.Min(gp?.MemberRoster?.TotalManCount ?? 0, 100000);
                    int recommended = passA != null && passA.Target.TryGetValue(s, out var rec)
                        ? Math.Max(0, rec) : 0;

                    cf.Settlements.Add(new WebConfig.FinancialSnapshot.SettlementPnl
                    {
                        SettlementId = s.StringId ?? "",
                        Name = s.Name?.ToString() ?? s.StringId ?? "",
                        IsCastle = s.IsCastle,
                        Income = income,
                        GarrisonWage = wage,
                        Net = income - wage,
                        CurrentGarrison = current,
                        RecommendedGarrison = recommended,
                    });
                    cf.TotalIncome += income;
                    cf.TotalGarrisonWage += wage;
                }
                catch (Exception inner)
                {
                    Logger.Error($"CapitalLogisticsManager.StashFinancialSnapshot: skipping '{town?.Settlement?.StringId}'", inner);
                }
            }

            WebConfig.FinancialSnapshot.ReplaceClan(clanId, cf);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.StashFinancialSnapshot failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }
}
