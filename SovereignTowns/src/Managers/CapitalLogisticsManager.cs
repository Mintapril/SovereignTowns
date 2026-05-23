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
using SovereignTowns.Patrol;
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
    private readonly PatrolDispatcher _patrolDispatcher;

    /// <summary>每 clan 在飞的派发求解协程任务 —— 重入防护(自愈式:已完成 / 取消即可覆盖,绝不长期占位)。</summary>
    private readonly Dictionary<Clan, AsyncSimulator.SimulatedTask> _mergedDispatchTask = new();

    /// <summary>每 clan 最近一次调度器 solver 完成的每城驻军目标。控制面板「推荐驻军」读此缓存;
    /// 求解跨帧 → 首个求解完成前缓存为空(返回 0)。</summary>
    private readonly Dictionary<Clan, Dictionary<Settlement, int>> _lastMergedTargets = new();

    public CapitalLogisticsManager(
        CapitalRegistry capitalRegistry,
        RecruitmentDispatcher recruitmentDispatcher,
        TransferDispatcher transferDispatcher,
        PatrolDispatcher patrolDispatcher)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
        _recruitmentDispatcher = recruitmentDispatcher ?? throw new ArgumentNullException(nameof(recruitmentDispatcher));
        _transferDispatcher = transferDispatcher ?? throw new ArgumentNullException(nameof(transferDispatcher));
        _patrolDispatcher = patrolDispatcher ?? throw new ArgumentNullException(nameof(patrolDispatcher));
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

        // 财政自治财务视图快照（金库 + 单城 P&L）。在主线程产出纯数值 DTO，
        // 供 /api/finance 与控制面板状态一览看板跨线程只读消费。
        StashFinancialSnapshot(manager);

        // 时间展开调度器权威派发:经 AsyncSimulator 分帧求解（避免 ~300-600ms 单帧卡顿），
        // 在完成回调里派发其路由指令 + 执行遣散决策。
        RunUnifiedDispatch(manager, capitalSettlement);
    }

    /// <summary>
    /// 时间展开调度器派发:经 <see cref="AsyncSimulator"/> 分帧跑 <see cref="UnifiedGarrisonSolver"/>
    /// （避免 ~300-600ms 单帧卡顿），在完成回调里派发其路由指令 + 执行遣散决策。
    ///
    /// 求解跨帧 → 回调在未来某帧触发,故回调内重做首府快照校验。每 clan 同一时刻至多一个
    /// 在飞派发求解(<see cref="_mergedDispatchTask"/> 自愈式重入防护);高速游戏下求解可能
    /// 横跨多个 logistics tick,其间的 tick 被重入防护跳过 —— 招募 / 调拨节奏可容忍此粒度。
    /// solver 跑不成(无 fief / 首府不符)→ 回调内跳过(本就无可派发物)。
    /// </summary>
    private void RunUnifiedDispatch(CapitalManager manager, Settlement capitalSettlement)
    {
        try
        {
            var clan = manager.OwnerClan;
            if (clan == null) return;
            string clanId = clan.StringId ?? "?";

            // 重入防护:上一次派发求解仍在分帧途中 → 本 tick 跳过(自愈:完成 / 取消即可覆盖)。
            if (_mergedDispatchTask.TryGetValue(clan, out var prev)
                && prev != null && !prev.IsCompleted && !prev.IsCancelled)
            {
                Logger.Debug($"UNIFIED-DISPATCH skipped — previous dispatch solve still running clan={clanId}");
                return;
            }

            // solver 建图会按 EnabledFeatures 剪掉被禁用的招募 / 调拨通道;巡逻容量也在这里
            // 置零,避免计划出执行层必然跳过的动作。
            var features = ConfigurationManager.Current?.EnabledFeatures;

            var fa = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            IHorizonForecast forecast = fa.ForecastMode == ForecastMode.Threat
                ? (IHorizonForecast)new ThreatForecast(fa.ThreatForecastScanRadius, fa.CapitalLogisticsTickHours)
                : new FlatForecast();
            int patrolHeadroom = features?.AutoPatrol == true
                ? _patrolDispatcher.PatrolHeadroomHeads(capitalSettlement)
                : 0;

            // 分帧求解:StartCoroutine 入 _pendingTasks,下一帧 AsyncSimulator.Update 起建图。
            var task = AsyncSimulator.StartCoroutine(
                UnifiedGarrisonSolver.SolveCoroutine(
                    manager, capitalSettlement, forecast, patrolHeadroom,
                    unified =>
                    {
                        try
                        {
                            if (!unified.Ran)
                            {
                                Logger.Debug($"UNIFIED-DISPATCH did not run (no fiefs / capital mismatch) clan={clanId}");
                                return;
                            }
                            // 快照校验:求解跨帧,期间首府可能易主 / 失活 → 丢弃结果免误派发。
                            if (capitalSettlement == null || !capitalSettlement.IsActive
                                || capitalSettlement.OwnerClan != clan)
                            {
                                Logger.Debug($"UNIFIED-DISPATCH result discarded — capital changed during solve clan={clanId}");
                                return;
                            }
                            Logger.Info("UNIFIED-DISPATCH " + unified.DiffLine(clanId));
                            StashMergedTargets(clan, unified);
                            ExecuteMergedInstructions(manager, unified);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"CapitalLogisticsManager.RunUnifiedDispatch callback failed (clan={clanId})", ex);
                        }
                    }));
            _mergedDispatchTask[clan] = task;
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.RunUnifiedDispatch failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>缓存调度器 solver 的每城驻军目标,供控制面板「推荐驻军」读取
    /// (<see cref="ResolveRecommendedGarrison"/>)。</summary>
    private void StashMergedTargets(Clan clan, UnifiedSolverResult unified)
    {
        if (clan == null || unified == null) return;
        var map = new Dictionary<Settlement, int>();
        foreach (var kv in unified.Target)
            if (kv.Key != null) map[kv.Key] = Math.Max(0, kv.Value);
        _lastMergedTargets[clan] = map;
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
    /// 从某城驻军按 LowestTierFirst 抽走 <paramref name="count"/> 头并丢弃(遣散),返回实抽头数。
    /// 由 <see cref="ExecuteMergedDisband"/> 调用。围城 / 风险等门限校验由调用方负责。
    /// 丢弃用 dummy roster 作目标 —— 抽出的兵被废弃,与 transfer/patrol/recruiter 各路径既有
    /// <c>TroopRoster.CreateDummyTroopRoster()</c> 用法一致。
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

    /// <summary>
    /// 派发一组路由指令。InPlace 按定居点分组、Recruiter 按 (首府,返回点,role) 分组打包成多站
    /// 行程,Transfer 逐条派发。<paramref name="auditSource"/> 标记决策来源,写进 Transfer 审计
    /// 日志的 inputSummary。返回 (接受数, 跳过数)。
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

            // 按 (定居点, role) 分组:MCMF 每 role 出一条 in-place 指令,招募器逐 role 招募
            // (招募器只认 role/count、不再自算配额),所以同 role 的多条合并、不同 role 各走一遍。
            foreach (var group in inPlaceInstructions.GroupBy(x => new { x.Settlement, x.Role }))
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

                case PatrolInstruction x:
                    return ExecutePatrolDispatch(manager, x);

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
            int recruited = CapitalInPlaceRecruiter.RecruitFromCapitalNotables(
                settlement, instruction.Role, instruction.Count);
            if (recruited > 0)
            {
                Logger.Info($"CapitalLogistics MCMF: capital in-place recruited {recruited} troop(s) settlement='{settlement.Name}' role={instruction.Role} requested={instruction.Count}");
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

    /// <summary>
    /// 派发调度器 solver 的巡逻指令。巡逻容量已由 PatrolDispatcher.PatrolHeadroomHeads
    /// 在建图前约束。
    /// </summary>
    private bool ExecutePatrolDispatch(CapitalManager manager, PatrolInstruction instruction)
    {
        var capital = manager.GetCapital();
        if (capital == null)
        {
            Logger.Warn($"CapitalLogistics MERGED: patrol instruction skipped — clan={manager.OwnerClan?.StringId} has no capital");
            return false;
        }
        return _patrolDispatcher.TryDispatchPatrol(capital, instruction.Count);
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

    // 注:手动模式「玩家目标 vs 推荐」评估(StashAssessments / ClearAssessments)随 legacy
    // Pass A 一并删除。/api/assessment 现恒返回空列表 —— LatestAssessments 不再被填充。

    // ── 财政自治财务视图快照 ──────────────────────────────────────────────────

    /// <summary>一次性 warn 去重：registered ClanFinanceModel 不是 STClanFinanceModel 时只警告一次。</summary>
    private static bool _warnedFinanceModelMissing;

    /// <summary>
    /// 在 Campaign 主线程调用:对该受管氏族产出一份 <see cref="WebConfig.FinancialSnapshot.ClanFinance"/>
    /// (金库余额/缓冲上限/日均开销 + 各受管领地单城 P&amp;L),整体替换该 clan 在快照中的条目。
    /// 收入用 <see cref="Models.STClanFinanceModel.SafeTownIncome"/> 重算;推荐头数取调度器最近一次
    /// 求解的目标缓存。任何失败保留旧快照不变(本方法整体 try/catch)。
    /// </summary>
    private void StashFinancialSnapshot(CapitalManager manager)
    {
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return;
            string clanId = clan.StringId ?? "";
            if (string.IsNullOrEmpty(clanId)) return;

            // clan = manager?.OwnerClan 且 clan != null → manager 必非空。
            var treasury = manager!.Treasury;
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();

            // 驻军工资预算:复用调度器同一口径(GarrisonAllocationSolver.ClanWageBudget)——
            // 调度器求解是 async/跨帧,看板不等求解;此处同步重算与求解所用一致的预算值。
            long garrisonWageBudget = 0;
            try
            {
                var budgetFiefs = clan.Fiefs?.Where(t => t?.Settlement != null && t.Settlement.IsActive).ToList()
                                  ?? new List<Town>();
                if (budgetFiefs.Count > 0)
                {
                    int wagePerTroop = Math.Max(1, GarrisonAllocationSolver.WagePerTroopAtMaxTier(manager!, budgetFiefs));
                    garrisonWageBudget = GarrisonAllocationSolver.ClanWageBudget(manager!, budgetFiefs, cfg, wagePerTroop);
                }
            }
            catch (Exception budgetEx)
            {
                Logger.Error($"CapitalLogisticsManager.StashFinancialSnapshot: budget compute failed (clan={clanId})", budgetEx);
            }

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
                TrailingDailyExpense = treasury?.TrailingDailyExpense() ?? 0,
                GarrisonWageBudget = garrisonWageBudget,
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
                    // 推荐驻军取调度器 solver 最近一次完成的目标(玩家 value* 调参在此体现);
                    // 缓存未就绪(首个求解未完成)→ 返回 0。
                    int recommended = ResolveRecommendedGarrison(clan, s);

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

    /// <summary>控制面板「推荐驻军」取值:取调度器 solver 最近完成的每城目标
    /// (<see cref="_lastMergedTargets"/>);缓存未就绪(首个求解尚未完成)→ 返回 0。</summary>
    private int ResolveRecommendedGarrison(Clan clan, Settlement s)
    {
        // clan / s 形参非空(调用方 StashFinancialSnapshot 已过滤);不写 != null 检查 ——
        // 否则 nullable 流分析会把 s 标成可空,传给下方 TryGetValue 触发 CS8604。
        if (_lastMergedTargets.TryGetValue(clan, out var mt)
            && mt.TryGetValue(s, out var mrec))
            return Math.Max(0, mrec);
        return 0;
    }
}
