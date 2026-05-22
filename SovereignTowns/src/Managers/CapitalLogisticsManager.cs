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

        // 把调度器的预算决策叙述进运行动态(看板「近期动态」段消费)。
        NarrateDispatcherDecisions(manager, passA, manualMode);
        // 把每座领地的价值函数诊断串写进决策审计日志(诊断 only,不进玩家动态流)。
        LogGarrisonPlan(manager, passA);

        if (manualMode)
            StashAssessments(manager, passA);
        else
            // M-1: 自动模式下 Pass A 直接驱动路由,不存在"玩家目标 vs 推荐"差异 —
            // 清掉该 clan 上一次手动模式残留的 assessment,避免控制面板展示过期数据。
            ClearAssessments(manager);

        // 财政自治财务视图快照（金库 + 单城 P&L）。在主线程产出纯数值 DTO，
        // 供 /api/finance 与控制面板状态一览看板跨线程只读消费。
        StashFinancialSnapshot(manager, passA);

        var result = RunMcmf(manager, capitalSettlement, passA);
        if (result.SettlementCount == 0)
        {
            Logger.Debug($"CapitalLogisticsManager: clan={manager.OwnerClan?.StringId} has no owned town/castle nodes");
            return;
        }

        // 方案2 派发路由:MergedOnly + 自动模式 → 合并 solver 权威派发,跳过 legacy 路由 + 遣散。
        // 合并 solver 跑不成(无 fief / 首府不符)则回退 legacy,确保该 tick 仍有调度。
        // 其余模式(LegacyOnly / ShadowMerged / MergedOnly+manual)走 legacy 派发 + 影子运行。
        var mergedMode = ConfigurationManager.Current?.FiscalAutonomy?.MergedSolverMode ?? MergedSolverMode.LegacyOnly;
        if (mergedMode == MergedSolverMode.MergedOnly && !manualMode)
        {
            if (RunMergedDispatch(manager, capitalSettlement, passA, result))
                return;
            Logger.Warn(
                $"MERGED-DISPATCH: unified solver did not run — falling back to legacy dispatch this tick " +
                $"clan={manager.OwnerClan?.StringId}");
        }

        ExecuteMcmfInstructions(manager, result);

        // 和平期遣散超额驻军：必须在 MCMF 之后跑,此时 passA 的每城目标已算出并用于路由。
        DisbandExcessGarrisons(manager, passA);

        // 方案2 parallel-run:合并 solver 影子运行(ShadowMerged)。必须在 legacy 全套
        // (narrate / stash / dispatch / disband)跑完之后单独一段 —— 仅求解 + 记差异日志,
        // 绝不触碰任何派发或 stash 状态(side-effect 隔离)。LegacyOnly / manual 模式下为 no-op。
        RunMergedShadow(manager, capitalSettlement, manualMode, passA, result);
    }

    /// <summary>
    /// 方案2(双层 MCMF 合并)parallel-run 影子运行。详见 audits/mcmf-merge-handoff.md §4。
    /// <list type="bullet">
    ///   <item><c>LegacyOnly</c>:no-op(= 合并前行为)。</item>
    ///   <item><c>ShadowMerged</c>:跑 <see cref="UnifiedGarrisonSolver"/>,记差异日志,**不派发**。</item>
    ///   <item>manual 模式(M5):合并 solver 用玩家手动目标作 demand 容量、照常求解,
    ///     记差异日志、**不派发** —— manual 下 legacy 仍权威派发,merged 仅影子。</item>
    /// </list>
    /// MergedOnly + 自动模式不经此方法 —— 走 <see cref="RunMergedDispatch"/> 真派发(EvaluateClan
    /// 已分流);ShadowMerged(auto / manual)与 MergedOnly + manual 模式均经此方法。
    /// 严格只读:仅求解 + Logger,绝不调 Narrate/Stash/dispatcher —— legacy 路径已在本方法
    /// 调用前全套跑完,此处复用任何 stash 写入都会导致 double-stash。
    /// </summary>
    private static void RunMergedShadow(
        CapitalManager manager, Settlement capitalSettlement, bool manualMode,
        GarrisonAllocationResult passA, SupplyDemandGraphResult legacyResult)
    {
        try
        {
            var mode = ConfigurationManager.Current?.FiscalAutonomy?.MergedSolverMode ?? MergedSolverMode.LegacyOnly;
            if (mode == MergedSolverMode.LegacyOnly) return;

            string clanId = manager.OwnerClan?.StringId ?? "?";

            // 合并 solver 不复刻 EnabledFeatures 开关(保持 solver 纯只读);开关关闭时
            // merged shadow 仍建模招募/调拨 → 与 legacy 必然分叉,先打一条 warn 免事后误判 bug。
            var features = ConfigurationManager.Current?.EnabledFeatures;
            if (features != null && (!features.AutoRecruitment || !features.TroopTransfers))
                Logger.Warn(
                    $"MERGED-SHADOW: EnabledFeatures off (AutoRecruitment={features.AutoRecruitment} " +
                    $"TroopTransfers={features.TroopTransfers}) — shadow flow will diverge from legacy clan={clanId}");

            // passA 复用:合并 solver 跳过预算 / wage 重算(passA 只读,side-effect 隔离不受影响)。
            var unified = UnifiedGarrisonSolver.Solve(manager, capitalSettlement, passA);
            if (!unified.Ran)
            {
                Logger.Debug($"MERGED-SHADOW did not run (no fiefs / capital mismatch) clan={clanId}");
                return;
            }

            // 边语义类别汇总 + Target / 指令级对账。stockpile divergence 是预期的
            // —— 合并图经 transit 转发分支,legacy 走首府囤兵。
            Logger.Info("MERGED-SHADOW " + unified.DiffLine(clanId));
            LogMergedDiff(clanId, unified, passA, legacyResult, manualMode);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.RunMergedShadow failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// 方案2 MergedOnly 真派发(M6):跑合并 solver,派发其路由指令 + 执行其遣散决策,
    /// 跳过 legacy 路由 / 遣散。详见 audits/mcmf-merge-handoff.md §4。仅自动模式调用
    /// (manual 模式留待 M5)。
    /// 返回 true = 合并 solver 跑成并已派发;false = 没跑成 → 调用方回退 legacy。
    /// </summary>
    private bool RunMergedDispatch(
        CapitalManager manager, Settlement capitalSettlement,
        GarrisonAllocationResult passA, SupplyDemandGraphResult legacyResult)
    {
        try
        {
            string clanId = manager.OwnerClan?.StringId ?? "?";

            // 合并 solver 不复刻 EnabledFeatures 开关 —— 派发侧由各 dispatcher 自校验,
            // 但开关关闭时合并图仍建模招募/调拨,先打一条 warn 免事后误判。
            var features = ConfigurationManager.Current?.EnabledFeatures;
            if (features != null && (!features.AutoRecruitment || !features.TroopTransfers))
                Logger.Warn(
                    $"MERGED-DISPATCH: EnabledFeatures off (AutoRecruitment={features.AutoRecruitment} " +
                    $"TroopTransfers={features.TroopTransfers}) — merged solver still models them clan={clanId}");

            // passA 复用:合并 solver 跳过预算 / wage 重算(passA 只读)。
            var unified = UnifiedGarrisonSolver.Solve(manager, capitalSettlement, passA);
            if (!unified.Ran)
            {
                Logger.Debug($"MERGED-DISPATCH did not run (no fiefs / capital mismatch) clan={clanId}");
                return false;
            }

            Logger.Info("MERGED-DISPATCH " + unified.DiffLine(clanId));
            // RunMergedDispatch 仅自动模式调用(EvaluateClan 分流 MergedOnly && !manualMode)。
            LogMergedDiff(clanId, unified, passA, legacyResult, manualMode: false);
            ExecuteMergedInstructions(manager, unified);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.RunMergedDispatch failed (clan={manager?.OwnerClan?.StringId})", ex);
            return false;
        }
    }

    /// <summary>
    /// 方案2 MergedOnly:派发合并 solver 的路由指令 + 执行其每城遣散决策。
    /// 路由指令派发复用 <see cref="ExecuteInstructionList"/>(与 legacy 同一执行层);
    /// 遣散经 <see cref="ExecuteMergedDisband"/>。
    /// </summary>
    private void ExecuteMergedInstructions(CapitalManager manager, UnifiedSolverResult unified)
    {
        try
        {
            var (accepted, skipped) = ExecuteInstructionList(manager, unified.Instructions, "merged");
            int unmet = Math.Max(0, unified.DemandTierCapacity - unified.DemandFilled);
            Logger.Info(
                $"CapitalLogistics MERGED execution: clan={manager.OwnerClan?.StringId} " +
                $"accepted={accepted} skipped={skipped} unmet={unmet}");

            ExecuteMergedDisband(manager, unified);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.ExecuteMergedInstructions failed (clan={manager.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// 执行合并 solver 的每城遣散决策。solver 求解时已排除保护态城(围城 / 高危),
    /// 但 solve→dispatch 之间风险可能变化 —— 派发前重查 Gate 3/4(镜像
    /// <see cref="DisbandExcessGarrisons"/>),被门限拦下则记一条 deferred 日志免差异日志误导。
    /// </summary>
    private static void ExecuteMergedDisband(CapitalManager manager, UnifiedSolverResult unified)
    {
        var clan = manager?.OwnerClan;
        foreach (var kv in unified.Disband)
        {
            var settlement = kv.Key;
            int count = kv.Value;
            if (settlement == null || count <= 0) continue;
            try
            {
                // Gate 3:围城 —— 永不遣散。
                if (settlement.IsUnderSiege)
                {
                    Logger.Info($"MERGED-DISPATCH disband deferred (under siege) settlement='{settlement.StringId}' planned={count}");
                    continue;
                }
                // Gate 4:高/危风险 —— 仅和平期遣散。
                if (RiskAssessmentService.Assess(settlement).Level >= RiskLevel.High)
                {
                    Logger.Info($"MERGED-DISPATCH disband deferred (risk≥High) settlement='{settlement.StringId}' planned={count}");
                    continue;
                }

                int disbanded = DisbandFromGarrison(settlement, count);
                if (disbanded > 0)
                {
                    Logger.Info(
                        $"MERGED-DISPATCH disband settlement='{settlement.StringId}' planned={count} disbanded={disbanded}");
                    DecisionAuditLogger.LogRule(
                        decisionType: "DisbandExcessGarrison",
                        inputSummary: $"settlement={settlement.StringId} clan={clan?.StringId} planned={count} disbanded={disbanded} source=merged",
                        decisionJson: $"{{\"settlement\":\"{settlement.StringId}\",\"clan\":\"{clan?.StringId}\",\"planned\":{count},\"disbanded\":{disbanded},\"source\":\"merged\"}}",
                        accepted: true);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CapitalLogisticsManager.ExecuteMergedDisband per-settlement failed (settlement='{settlement?.StringId}')", ex);
            }
        }
    }

    /// <summary>
    /// merged(含路由 + 预算约束)对 legacy 的差异对账,写入运行日志(诊断 only,不进玩家动态流)。
    /// Target:merged vs Pass A —— 自动模式下差异即合并 ROI(Pass A 对路由可行性瞎);
    /// manual 模式下 Pass A 是 auto 推荐、merged 是 manual 上限下的结果,差异≈玩家目标偏离推荐。
    /// 指令:类型摘要 + 按 (类型,源,目标,role) 签名的逐条差异(首 10 条)。
    /// disband:legacy 的 DisbandExcessGarrisons 为独立 pass、不输出可比 list,故仅记 merged 侧。
    /// </summary>
    private static void LogMergedDiff(
        string clanId, UnifiedSolverResult merged,
        GarrisonAllocationResult passA, SupplyDemandGraphResult legacyResult, bool manualMode)
    {
        try
        {
            int diffCount = 0, sumAbs = 0;
            var targetDiffs = new List<string>();
            foreach (var kv in merged.Target)
            {
                int legacyTarget = passA.Target.TryGetValue(kv.Key!, out var lt) ? lt : 0;
                int d = kv.Value - legacyTarget;
                if (d == 0) continue;
                diffCount++;
                sumAbs += Math.Abs(d);
                if (targetDiffs.Count < 10)
                    targetDiffs.Add($"s={kv.Key?.StringId} legacy={legacyTarget} merged={kv.Value} diff={d:+0;-0;0}");
            }
            string baselineNote = manualMode
                ? "(manual 模式:passA=auto 推荐,merged=manual 上限下的结果 — 差异≈玩家目标 vs 推荐)"
                : "(merged 含路由+预算约束,passA 未含 — 差异 = 合并 ROI)";
            Logger.Info(
                $"MERGED-DIFF clan={clanId} mode={(manualMode ? "manual" : "auto")} " +
                $"Δtarget: {diffCount} settlement(s) differ ΣabsDiff={sumAbs} {baselineNote}");
            foreach (var line in targetDiffs)
                Logger.Info("  MERGED-DIFF target " + line);

            Logger.Info($"  MERGED-DIFF instr legacy: {SummarizeInstructions(legacyResult.Instructions)}");
            Logger.Info($"  MERGED-DIFF instr merged: {SummarizeInstructions(merged.Instructions)}");
            Logger.Info(
                "  MERGED-DIFF signature diffs include expected stockpile divergence " +
                "(I:<capital>:* inflated, T:<capital>>* present only in merged — see mcmf-merge-handoff.md §3.3)");

            var legSig = SignatureCounts(legacyResult.Instructions);
            var mrgSig = SignatureCounts(merged.Instructions);
            var allKeys = new HashSet<string>(legSig.Keys);
            allKeys.UnionWith(mrgSig.Keys);
            int sigDiffs = 0, shown = 0;
            foreach (var key in allKeys)
            {
                int l = legSig.TryGetValue(key, out var lv) ? lv : 0;
                int m = mrgSig.TryGetValue(key, out var mv) ? mv : 0;
                if (l == m) continue;
                sigDiffs++;
                if (shown < 10)
                {
                    shown++;
                    Logger.Info($"  MERGED-DIFF instr {key} legacy={l} merged={m}");
                }
            }
            if (sigDiffs > shown)
                Logger.Info($"  MERGED-DIFF instr ... {sigDiffs - shown} more signature diff(s)");

            if (merged.Disband.Count > 0)
            {
                int totalDisband = merged.Disband.Values.Sum();
                Logger.Info(
                    $"  MERGED-DIFF merged disband total={totalDisband} across {merged.Disband.Count} " +
                    $"settlement(s) (legacy disband 为独立 pass,不可直接对比)");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.LogMergedDiff failed (clan={clanId})", ex);
        }
    }

    /// <summary>指令列表按类型 + 头数汇总成一行。</summary>
    private static string SummarizeInstructions(IReadOnlyList<DispatchInstruction> list)
    {
        int rec = 0, recT = 0, inp = 0, inpT = 0, xfer = 0, xferT = 0, other = 0;
        foreach (var i in list)
        {
            if (i == null) continue;
            switch (i)
            {
                case RecruiterPartyInstruction: rec++; recT += i.Count; break;
                case InPlaceRecruitInstruction: inp++; inpT += i.Count; break;
                case TransferPartyInstruction: xfer++; xferT += i.Count; break;
                default: other++; break;
            }
        }
        string s = $"recruiter={rec}(t={recT}) inplace={inp}(t={inpT}) transfer={xfer}(t={xferT})";
        return other > 0 ? s + $" other={other}" : s;
    }

    /// <summary>指令列表按 (类型,源,目标,role) 签名聚合头数,供逐条差异对账。</summary>
    private static Dictionary<string, int> SignatureCounts(IEnumerable<DispatchInstruction> list)
    {
        var d = new Dictionary<string, int>();
        foreach (var i in list)
        {
            if (i == null) continue;
            string sig = i switch
            {
                RecruiterPartyInstruction r => $"R:{r.TargetVillage?.StringId}>{r.ReturnSettlement?.StringId}:{r.Role}",
                InPlaceRecruitInstruction p => $"I:{p.Settlement?.StringId}:{p.Role}",
                TransferPartyInstruction t => $"T:{t.Source?.StringId}>{t.Destination?.StringId}:{t.Role}",
                _ => $"?:{i.GetType().Name}:{i.Role}",
            };
            d[sig] = (d.TryGetValue(sig, out var c) ? c : 0) + i.Count;
        }
        return d;
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

                    int disbanded = DisbandFromGarrison(settlement, excess);

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

    /// <summary>
    /// 从某城驻军按 LowestTierFirst 抽走 <paramref name="count"/> 头并丢弃(遣散),返回实抽头数。
    /// legacy <see cref="DisbandExcessGarrisons"/> 与方案2 <see cref="ExecuteMergedDisband"/> 共用。
    /// 围城 / 风险等门限校验由调用方负责。丢弃用 dummy roster 作目标 —— 抽出的兵被废弃,与
    /// transfer/patrol/recruiter 各路径既有 <c>TroopRoster.CreateDummyTroopRoster()</c> 用法一致。
    /// </summary>
    private static int DisbandFromGarrison(Settlement settlement, int count)
    {
        if (count <= 0) return 0;
        var garrisonRoster = settlement?.Town?.GarrisonParty?.MemberRoster;
        if (garrisonRoster == null) return 0;

        var discardRoster = TroopRoster.CreateDummyTroopRoster();
        return TroopTransferHelper.TransferFromGarrison(
            garrisonRoster,
            discardRoster,
            count,
            TroopTransferHelper.SortStrategy.LowestTierFirst);
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

    // ── 调度器决策叙述（看板「近期动态」段）──────────────────────────────────

    /// <summary>预算相对上次变动达到此比例才发一条 feed，避免每日税收抖动刷屏。</summary>
    private const double DispatcherBudgetDeltaFraction = 0.10;

    /// <summary>
    /// 上次发布过的 clan 驻军工资预算，按 clan StringId。仅 EvaluateClan（Campaign 主线程，
    /// 顺序遍历）读写 —— 单线程，无需锁。非持久化：重载后首次评估按「首次」发一条。
    /// </summary>
    private static readonly Dictionary<string, long> _prevDispatcherBudget = new Dictionary<string, long>();

    /// <summary>
    /// 把调度器 Pass A 的预算决策叙述进 ActivityFeed。仅当预算相对上次变动
    /// ≥ <see cref="DispatcherBudgetDeltaFraction"/> 或首次评估该 clan 时发一条。
    /// 经 DecisionAuditLogger.LogRule → ActivityNarrator，与其余决策共用同一管线。
    /// </summary>
    private static void NarrateDispatcherDecisions(CapitalManager manager, GarrisonAllocationResult passA, bool manualMode)
    {
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null || passA == null) return;
            string clanId = clan.StringId ?? "";
            if (string.IsNullOrEmpty(clanId)) return;

            long budget = passA.Budget;
            bool first = !_prevDispatcherBudget.TryGetValue(clanId, out long prev);
            bool changed = first
                ? budget > 0
                : Math.Abs(budget - prev) >= Math.Max(1L, (long)(Math.Max(prev, 1L) * DispatcherBudgetDeltaFraction));
            _prevDispatcherBudget[clanId] = budget;
            if (!changed) return;

            int totalTarget = 0;
            foreach (var kv in passA.Target) totalTarget += Math.Max(0, kv.Value);
            string mode = manualMode ? "manual" : "auto";

            DecisionAuditLogger.LogRule(
                decisionType: "DispatcherBudget",
                inputSummary: $"clan={clanId} budget={budget} troopCap={passA.BudgetTroopCap} totalTarget={totalTarget} holdings={passA.Target.Count} mode={mode}",
                decisionJson: $"{{\"clan\":\"{clanId}\",\"budget\":{budget},\"troopCap\":{passA.BudgetTroopCap},\"totalTarget\":{totalTarget},\"holdings\":{passA.Target.Count},\"mode\":\"{mode}\"}}",
                accepted: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.NarrateDispatcherDecisions failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// 把 Pass A 每座领地的价值函数诊断串（threat / strat / floor / adequate / hardCap）
    /// 加解出的目标头数,逐条写进决策审计日志。诊断 only —— decisionType "DispatcherGarrisonPlan"
    /// 在 ActivityNarrator 无对应 case,不进玩家动态流;inputSummary 带 home= 故同时落 per-settlement ring。
    /// </summary>
    private static void LogGarrisonPlan(CapitalManager manager, GarrisonAllocationResult passA)
    {
        try
        {
            if (passA == null) return;
            string clanId = manager?.OwnerClan?.StringId ?? "";
            foreach (var kv in passA.Target)
            {
                var s = kv.Key;
                if (s == null) continue;
                string breakdown = passA.Breakdown.TryGetValue(s, out var b) ? b : "(no breakdown)";
                DecisionAuditLogger.LogRule(
                    decisionType: "DispatcherGarrisonPlan",
                    inputSummary: $"home={s.StringId} clan={clanId} target={kv.Value} {breakdown}",
                    decisionJson: $"{{\"settlement\":\"{s.StringId}\",\"target\":{kv.Value},\"budget\":{passA.Budget}}}",
                    accepted: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.LogGarrisonPlan failed (clan={manager?.OwnerClan?.StringId})", ex);
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
        var (accepted, skipped) = ExecuteInstructionList(manager, result.Instructions, "mcmf");
        Logger.Info(
            $"CapitalLogistics MCMF execution: clan={manager.OwnerClan?.StringId} " +
            $"accepted={accepted} skipped={skipped} unmet={result.Unmet}");
    }

    /// <summary>
    /// 派发一组路由指令。InPlace 按定居点分组、Recruiter 按 (首府,返回点,role) 分组打包成多站
    /// 行程,Transfer 逐条派发。legacy MCMF(Pass B)与方案2 合并 solver 共用此执行层。
    /// <paramref name="auditSource"/> 标记决策来源("mcmf" / "merged"),写进 Transfer 审计日志
    /// 的 inputSummary 以区分两条路径。返回 (接受数, 跳过数)。
    /// </summary>
    private (int Accepted, int Skipped) ExecuteInstructionList(
        CapitalManager manager, IReadOnlyList<DispatchInstruction> instructions, string auditSource)
    {
        int accepted = 0;
        int skipped = 0;

        try
        {
            var inPlaceInstructions = new List<InPlaceRecruitInstruction>();
            var recruiterInstructions = new List<RecruiterPartyInstruction>();

            foreach (var instruction in instructions)
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

                bool ok = ExecuteMcmfInstruction(manager, instruction, auditSource);
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

            // 按 role 分组：MCMF 为每个 (村, role) 出一条指令；同 role 的多个目标村打包成一支
            // 征兵队的多站行程（按距首府最近邻排序）。每 role 一支队，count 按该 role 精确求和。
            // 征兵队上限由 PartyLifecycleManager 控制，多 role 时未派出的留待下个 daily tick。
            foreach (var group in recruiterInstructions.GroupBy(x => new { x.Town, x.ReturnSettlement, x.Role }))
            {
                var first = group.First();
                int count = group.Sum(x => x.Count);
                var itinerary = OrderItineraryNearestNeighbor(
                    first.ReturnSettlement, group.Select(x => x.TargetVillage));
                bool ok = ExecuteRecruiterDispatch(
                    manager, first.Town, first.ReturnSettlement, first.Role, itinerary, count);
                if (ok) accepted++;
                else skipped++;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.ExecuteInstructionList failed (clan={manager.OwnerClan?.StringId} source={auditSource})", ex);
        }

        return (accepted, skipped);
    }

    private bool ExecuteMcmfInstruction(CapitalManager manager, DispatchInstruction instruction, string auditSource)
    {
        try
        {
            switch (instruction)
            {
                case InPlaceRecruitInstruction x:
                    return ExecuteInPlaceRecruitment(x);

                case RecruiterPartyInstruction x:
                    return ExecuteRecruiterDispatch(
                        manager, x.Town, x.ReturnSettlement, x.Role, new[] { x.TargetVillage }, x.Count);

                case TransferPartyInstruction x:
                    return ExecuteTransferDispatch(manager, x, auditSource);

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

    /// <summary>
    /// 路线风险分 = home 与各 waypoint 的端点 + 连线中点处、由
    /// <see cref="HostilePartyScanner"/> 评出的最大敌对健康兵力。0 = 安全。
    /// Part D1(派发否决)用它比阈值。评估失败返回 0(保守:宁可派也不无故卡死)。
    /// </summary>
    private static float RouteRiskScore(Settlement home, IEnumerable<Settlement> waypoints)
    {
        try
        {
            if (home == null) return 0f;
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            var friendly = home.MapFaction;
            if (friendly == null) return 0f;
            float radius = Math.Max(1f, cfg.DispatchRiskScanRadius);
            float worst = HostilePartyScanner.HostileStrengthNear(home.GetPosition2D, radius, friendly);
            foreach (var wp in waypoints)
            {
                if (wp == null) continue;
                worst = Math.Max(worst,
                    HostilePartyScanner.HostileStrengthNear(wp.GetPosition2D, radius, friendly));
                var mid = (home.GetPosition2D + wp.GetPosition2D) * 0.5f;
                worst = Math.Max(worst,
                    HostilePartyScanner.HostileStrengthNear(mid, radius, friendly));
            }
            return worst;
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.RouteRiskScore failed (home='{home?.StringId}')", ex);
            return 0f;
        }
    }

    private bool ExecuteRecruiterDispatch(
        CapitalManager manager, Town town, Settlement returnSettlement,
        GenericTroopRole role, IReadOnlyList<Settlement> itinerary, int tripTarget)
    {
        var capital = manager.GetCapital();
        if (capital == null || town != capital || returnSettlement != capital.Settlement)
        {
            Logger.Warn(
                $"CapitalLogistics MCMF: recruiter skipped because current dispatcher only supports capital dispatch " +
                $"town={town?.Settlement?.StringId} return={returnSettlement?.StringId}");
            return false;
        }
        if (itinerary == null || itinerary.Count == 0) return false;

        // D1:路途有敌军 → 本 tick 不派(soft skip,下 tick 重评)。出击队不经此路径。
        var riskCfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
        if (riskCfg.DispatchRiskEnabled)
        {
            float risk = RouteRiskScore(returnSettlement, itinerary);
            if (risk >= riskCfg.DispatchRiskVetoThreshold)
            {
                Logger.Info(
                    $"DISPATCH-RISK recruiter skipped: route risk {risk:F0} ≥ threshold " +
                    $"{riskCfg.DispatchRiskVetoThreshold:F0} home='{returnSettlement?.StringId}' stops={itinerary.Count}");
                return false;
            }
        }

        string reason =
            $"mcmf recruiter clan={manager.OwnerClan?.StringId} role={role} stops={itinerary.Count} count={tripTarget}";
        return _recruitmentDispatcher.TryDispatchRecruiter(town, itinerary, role, tripTarget, reason);
    }

    /// <summary>
    /// 把 MCMF 选定的一组目标村按"从首府出发的最近邻"排成征兵队多站行程。去重；
    /// 这只是把 MCMF 已决定的村集合排序成可走的路线，不做任何"选不选这个村"的二次决策。
    /// </summary>
    private static List<Settlement> OrderItineraryNearestNeighbor(
        Settlement start, IEnumerable<Settlement> villages)
    {
        var remaining = new List<Settlement>();
        foreach (var v in villages)
            if (v != null && !remaining.Contains(v)) remaining.Add(v);

        var ordered = new List<Settlement>();
        var cursor = start;
        while (remaining.Count > 0 && cursor != null)
        {
            var cursorPos = cursor.GetPosition2D;
            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                float d = (remaining[i].GetPosition2D - cursorPos).Length;
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            cursor = remaining[bestIdx];
            remaining.RemoveAt(bestIdx);
            ordered.Add(cursor);
        }
        // cursor 为 null 的极端兜底：剩余村原样追加。
        ordered.AddRange(remaining);
        return ordered;
    }

    private bool ExecuteTransferDispatch(
        CapitalManager manager, TransferPartyInstruction instruction, string auditSource)
    {
        // D1:路途有敌军 → 本 tick 不派(soft skip,下 tick 重评)。
        var riskCfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
        if (riskCfg.DispatchRiskEnabled)
        {
            float risk = RouteRiskScore(instruction.Source, new[] { instruction.Destination });
            if (risk >= riskCfg.DispatchRiskVetoThreshold)
            {
                Logger.Info(
                    $"DISPATCH-RISK transfer skipped: route risk {risk:F0} ≥ threshold " +
                    $"{riskCfg.DispatchRiskVetoThreshold:F0} src='{instruction.Source?.StringId}' " +
                    $"dst='{instruction.Destination?.StringId}'");
                return false;
            }
        }

        string reason =
            $"{auditSource} transfer clan={manager.OwnerClan?.StringId} role={instruction.Role} count={instruction.Count}";
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
                inputSummary: $"clan={manager.OwnerClan?.StringId} src={instruction.Source.StringId} dest={instruction.Destination.StringId} role={instruction.Role} amount={instruction.Count} source={auditSource}",
                decisionJson: $"{{\"src\":\"{instruction.Source.StringId}\",\"dest\":\"{instruction.Destination.StringId}\",\"role\":\"{instruction.Role}\",\"amount\":{instruction.Count},\"source\":\"{auditSource}\"}}",
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
                    // 玩家手动目标:与合并 solver 的 manual demand 容量共用同一口径
                    // (UnifiedGarrisonSolver.ComputeManualTarget)—— 单一来源,否则面板展示的
                    // "玩家目标"与 solver 实采容量乖离,DailyWageDelta 也会是虚构数。
                    int playerTarget = UnifiedGarrisonSolver.ComputeManualTarget(town, cfg);

                    int recommended = passA.Target.TryGetValue(settlement, out var rec) ? Math.Max(0, rec) : 0;

                    // DailyWageDelta for castles: BranchRule.TargetPower is in military-power units
                    // (~3-5× headcount) while passA.Target (recommended) is in headcount. Subtracting
                    // the two different units yields a meaningless inflated figure, so we emit 0 for
                    // castles. Town settlements use headcount for both sides and compute correctly.
                    int dailyWageDelta = town.IsTown ? (playerTarget - recommended) * wagePerTroop : 0;

                    assessments.Add(new GarrisonAssessment
                    {
                        SettlementId = settlement.StringId ?? "",
                        PlayerTarget = playerTarget,
                        RecommendedTarget = recommended,
                        DailyWageDelta = dailyWageDelta,
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

    // ── 财政自治财务视图快照 ──────────────────────────────────────────────────

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
                GarrisonWageBudget = passA?.Budget ?? 0,
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
                    // 防御性上界 10000，与 SettlementsSnapshot.Refresh 的驻军头数钳制口径一致。
                    int current = Math.Min(gp?.MemberRoster?.TotalManCount ?? 0, 10000);
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
