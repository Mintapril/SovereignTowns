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
using SovereignTowns.SallyForth;
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
    private readonly SallyDispatcher _sallyDispatcher;

    /// <summary>每 clan 在飞的派发求解协程任务 —— 重入防护(自愈式:已完成 / 取消即可覆盖,绝不长期占位)。</summary>
    private readonly Dictionary<Clan, AsyncSimulator.SimulatedTask> _mergedDispatchTask = new();

    /// <summary>每 clan 最近一次调度器 solver 完成的每城驻军实际 flow(= MCMF Target,
    /// 受当前 supply / 初始 garrison 影响)。仅在指令派发 / 诊断日志使用,不显示给玩家。</summary>
    private readonly Dictionary<Clan, Dictionary<Settlement, int>> _lastMergedTargets = new();

    /// <summary>每 clan 最近一次调度器算出的每城 capacity(= 预算+威胁+战略约束下算法判断应养的兵数,
    /// 独立于实际 garrison)。控制面板「目标驻军」读此缓存; 求解跨帧 → 首个求解完成前 fallback hardCap。</summary>
    private readonly Dictionary<Clan, Dictionary<Settlement, int>> _lastMergedCapacity = new();

    public CapitalLogisticsManager(
        CapitalRegistry capitalRegistry,
        RecruitmentDispatcher recruitmentDispatcher,
        TransferDispatcher transferDispatcher,
        PatrolDispatcher patrolDispatcher,
        SallyDispatcher sallyDispatcher)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
        _recruitmentDispatcher = recruitmentDispatcher ?? throw new ArgumentNullException(nameof(recruitmentDispatcher));
        _transferDispatcher = transferDispatcher ?? throw new ArgumentNullException(nameof(transferDispatcher));
        _patrolDispatcher = patrolDispatcher ?? throw new ArgumentNullException(nameof(patrolDispatcher));
        _sallyDispatcher = sallyDispatcher ?? throw new ArgumentNullException(nameof(sallyDispatcher));
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
            IHorizonForecast forecast = new ThreatForecast(fa.ThreatForecastScanRadius, fa.CapitalLogisticsTickHours);

            // 分帧求解:StartCoroutine 入 _pendingTasks,下一帧 AsyncSimulator.Update 起建图。
            var solveStartedAt = TaleWorlds.CampaignSystem.CampaignTime.Now;
            var task = AsyncSimulator.StartCoroutine(
                UnifiedGarrisonSolver.SolveCoroutine(
                    manager, capitalSettlement, forecast,
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
                            // PR-6 (2026-05-28): 跨 tick 警告。求解开始到完成之间游戏内 >1 tick → 旧快照决策。
                            // 仅警告,不丢弃 —— 下游 dispatcher 的 D1/siege/cap 二次校验兜底。
                            try
                            {
                                var elapsedHours = (TaleWorlds.CampaignSystem.CampaignTime.Now - solveStartedAt).ToHours;
                                int tickHours = ConfigurationManager.Current?.FiscalAutonomy?.CapitalLogisticsTickHours ?? 6;
                                if (elapsedHours > tickHours)
                                {
                                    Logger.Warn(
                                        $"UNIFIED-DISPATCH solve crossed tick boundary " +
                                        $"(elapsed={elapsedHours:F1}h > tick={tickHours}h) — snapshot stale, applying anyway " +
                                        $"clan={clanId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"UNIFIED-DISPATCH stale check failed (clan={clanId})", ex);
                            }
                            Logger.Info("UNIFIED-DISPATCH " + unified.DiffLine(clanId));
                            StashMergedTargets(clan, unified);
                            ExecuteMergedInstructions(manager, unified);
                            // Post-decode patrol(2026-05-28 重构):
                            // MCMF 不再管 patrol。这里按"current garrison - target"算 surplus,capped by 容量。
                            if (features?.AutoPatrol == true)
                            {
                                try
                                {
                                    int patrolHeadroom = _patrolDispatcher.PatrolHeadroomHeads(capitalSettlement);
                                    if (patrolHeadroom > 0)
                                    {
                                        int currentGarrison = capitalSettlement.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                                        int target = unified.Target.TryGetValue(capitalSettlement, out var tg) ? tg : 0;
                                        int surplus = Math.Max(0, currentGarrison - target);
                                        int patrolHeads = Math.Min(surplus, patrolHeadroom);
                                        if (patrolHeads > 0)
                                        {
                                            var capitalTownForPatrol = manager.GetCapital();
                                            if (capitalTownForPatrol != null)
                                                _patrolDispatcher.TryDispatchPatrol(capitalTownForPatrol, patrolHeads);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"CapitalLogisticsManager post-decode patrol failed (clan={clanId})", ex);
                                }
                            }
                            if (features?.SallyForth == true)
                                _sallyDispatcher.EvaluateAllFiefs(clan);
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

    /// <summary>缓存 MCMF flow (_lastMergedTargets) + 算法判断 capacity (_lastMergedCapacity)。
    /// 后者供控制面板「目标驻军」读取(<see cref="ResolveTargetGarrison"/>)。</summary>
    private void StashMergedTargets(Clan clan, UnifiedSolverResult unified)
    {
        if (clan == null || unified == null) return;
        var capMap = new Dictionary<Settlement, int>(unified.Capacity.Count);
        foreach (var kv in unified.Capacity)
            if (kv.Key != null) capMap[kv.Key] = Math.Max(0, kv.Value);
        _lastMergedCapacity[clan] = capMap;
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

                // [GARRISON-DIAG] before/after head count，与 solver 的 disband-plan 对照。
                int headsBefore = settlement.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? -1;
                int disbanded = DisbandFromGarrison(settlement, count);
                int headsAfter = settlement.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? -1;
                if (disbanded > 0)
                {
                    Logger.Info(
                        $"MERGED-DISPATCH disband settlement='{settlement.StringId}' planned={count} disbanded={disbanded} heads={headsBefore}→{headsAfter}");
                    DecisionAuditLogger.LogRule(
                        decisionType: "DisbandExcessGarrison",
                        inputSummary: $"settlement={settlement.StringId} clan={clan?.StringId} planned={count} disbanded={disbanded} headsBefore={headsBefore} headsAfter={headsAfter} source=merged",
                        decisionJson: $"{{\"settlement\":\"{settlement.StringId}\",\"clan\":\"{clan?.StringId}\",\"planned\":{count},\"disbanded\":{disbanded},\"headsBefore\":{headsBefore},\"headsAfter\":{headsAfter},\"source\":\"merged\"}}",
                        accepted: true);
                }
                else if (count > 0)
                {
                    // [GARRISON-DIAG] planned > 0 但实抽 0 —— roster 为空或 LowestTierFirst 跳过英雄。罕见但值得记。
                    Logger.Info(
                        $"[GARRISON-DIAG] disband no-op settlement='{settlement.StringId}' planned={count} disbanded=0 heads={headsBefore} (roster empty or hero-only?)");
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

            // 按 (Town, Return, Role, Mode) 分组：同组的多个目标村打包成一支征兵队的多站行程。
            // GarrisonRole 与 HonorGuardPrecise 走同一调度路径——执行端按 Mode 分支匹配 / 回家。
            // 行程按距首府最近邻排序；count 按该组精确求和（多 role 时每 role 一支队）。
            // 队伍 cap 由 PartyLifecycleManager 按 (KindRecruiter / KindHonorGuardRecruiter) 分桶约束。
            foreach (var group in recruiterInstructions.GroupBy(x => new { x.Town, x.ReturnSettlement, x.Role, x.Mode }))
            {
                var first = group.First();
                int count = group.Sum(x => x.Count);
                var itinerary = OrderItineraryNearestNeighbor(
                    first.ReturnSettlement, group.Select(x => x.TargetVillage));
                // HG 模板从首条取（同 group 内所有指令的 PreciseTemplate 相同——皆出自同一 cfg 模板快照）。
                bool ok = ExecuteRecruiterDispatch(
                    manager, first.Town, first.ReturnSettlement, first.Role, itinerary, count,
                    first.Mode, first.PreciseTemplate);
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
                        manager, x.Town, x.ReturnSettlement, x.Role, new[] { x.TargetVillage }, x.Count,
                        x.Mode, x.PreciseTemplate);

                case TransferPartyInstruction x:
                    return ExecuteTransferDispatch(manager, x, auditSource);

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
            // 2026-05-28: targetPower 是给 BranchInPlaceRecruiter 的粗略 power 信号(用于"还需多少兵"
            // 估算),不需要精确到 capacity 级别。用 hardCap 作 upper bound 即可 —— recruiter 内部
            // 会按实际驻军/candidates 自动钳制,不会过招。
            var _cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            int _hardCap = GarrisonAllocationSolver.HardCapFor(settlement.Town, _cfg);
            int _targetPower = _hardCap * 2; // rough power proxy: avg tier-3 troop ≈ 2 power units
            int recruited = BranchInPlaceRecruiter.RecruitFromBranchNotables(
                settlement,
                _targetPower,
                $"mcmf branch in-place flow={instruction.Count}");
            if (recruited > 0)
            {
                Logger.Info($"CapitalLogistics MCMF: branch in-place recruited {recruited} troop(s) settlement='{settlement.Name}' targetPower={_targetPower}");
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
        GenericTroopRole role, IReadOnlyList<Settlement> itinerary, int tripTarget,
        RecruiterMode mode, IReadOnlyDictionary<string, int>? preciseTemplate)
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
        // 2026-05-29: HonorGuardPrecise 模式绕过 risk veto——HG 长程招募的设计意图就是跨敌区招兵
        // （solver 端 EnumerateRecruitmentVillagesForHG 已经放开 faction filter）。执行端不应再 risk-veto 卡掉。
        var riskCfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
        if (riskCfg.DispatchRiskEnabled && mode != RecruiterMode.HonorGuardPrecise)
        {
            float risk = RouteRiskScore(returnSettlement, itinerary);
            if (risk >= riskCfg.DispatchRiskVetoThreshold)
            {
                Logger.Info(
                    $"DISPATCH-RISK recruiter skipped: route risk {risk:F0} ≥ threshold " +
                    $"{riskCfg.DispatchRiskVetoThreshold:F0} home='{returnSettlement?.StringId}' stops={itinerary.Count} mode={mode}");
                return false;
            }
        }

        string reason =
            $"mcmf recruiter clan={manager.OwnerClan?.StringId} role={role} mode={mode} stops={itinerary.Count} count={tripTarget}";
        return _recruitmentDispatcher.TryDispatchRecruiter(town, itinerary, role, tripTarget, mode, preciseTemplate, reason);
    }

    /// <summary>
    /// 把 MCMF 选定的一组目标村按"从首府出发的最近邻"排成征兵队多站行程。去重；
    /// 这只是把 MCMF 已决定的村集合排序成可走的路线，不做任何"选不选这个村"的二次决策。
    /// </summary>
    /// <summary>
    /// Phase 7(2026-05-24):征兵队 itinerary 优化:
    /// - ≤8 村:暴力 TSP 枚举(N! 排列,8!=40320 < 5ms),含 start→...→last→start 回程。
    /// - >8 村:NN 贪心 fallback(实测 MCMF 单 group 一般 ≤4 村,不应触发)。
    /// 距离用 MapDistanceModel(寻路 cache 查表),与 UnifiedGarrisonSolver.RoutingDistance 同口径。
    /// </summary>
    private static List<Settlement> OrderItineraryNearestNeighbor(
        Settlement start, IEnumerable<Settlement> villages)
    {
        var nodes = new List<Settlement>();
        foreach (var v in villages)
            if (v != null && !nodes.Contains(v)) nodes.Add(v);
        if (nodes.Count <= 1 || start == null) return nodes;

        if (nodes.Count <= 8)
        {
            var bestOrder = new List<Settlement>(nodes);
            double bestTotal = double.MaxValue;
            TspPermute(nodes, 0, start, ref bestOrder, ref bestTotal);
            return bestOrder;
        }

        // NN fallback for N > 8(在飞征兵队组队 cap 决定 N 一般 ≤ 4-5,此分支保守兜底)
        var remaining = new List<Settlement>(nodes);
        var ordered = new List<Settlement>();
        var cursor = start;
        while (remaining.Count > 0 && cursor != null)
        {
            int bestIdx = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                double d = SettlementDistance(cursor, remaining[i]);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            cursor = remaining[bestIdx];
            remaining.RemoveAt(bestIdx);
            ordered.Add(cursor);
        }
        ordered.AddRange(remaining);
        return ordered;
    }

    private static void TspPermute(
        List<Settlement> arr, int from, Settlement start,
        ref List<Settlement> bestOrder, ref double bestTotal)
    {
        if (from >= arr.Count)
        {
            double total = 0;
            var cur = start;
            for (int i = 0; i < arr.Count; i++)
            {
                total += SettlementDistance(cur, arr[i]);
                cur = arr[i];
            }
            total += SettlementDistance(cur, start);  // 回 home
            if (total < bestTotal)
            {
                bestTotal = total;
                bestOrder = new List<Settlement>(arr);
            }
            return;
        }
        for (int i = from; i < arr.Count; i++)
        {
            (arr[from], arr[i]) = (arr[i], arr[from]);
            TspPermute(arr, from + 1, start, ref bestOrder, ref bestTotal);
            (arr[from], arr[i]) = (arr[i], arr[from]);
        }
    }

    private static double SettlementDistance(Settlement a, Settlement b)
    {
        try
        {
            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MapDistanceModel;
            if (model != null && a != null && b != null)
            {
                float d = model.GetDistance(a, b, false, false,
                    TaleWorlds.CampaignSystem.Party.MobileParty.NavigationType.Default, out _);
                if (d > 0f && d < TaleWorlds.CampaignSystem.ComponentInterfaces.MapDistanceModel.PossibleMaximumMapBoundary)
                    return d;
            }
            return (double)(a!.GetPosition2D - b!.GetPosition2D).Length;
        }
        catch { return 1000.0; }
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
    /// 控制面板 handler 在主线程读取;写入也在主线程(daily evaluate)。引用赋值在 CLR 上原子;
    /// <see cref="Volatile"/> 套一层 release-acquire 语义,无需锁。DTO 只持 string / 数值。
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<GarrisonAssessment>> _latestAssessments
        = new Dictionary<string, IReadOnlyList<GarrisonAssessment>>();

    /// <summary>控制面板调用:读最近一份评估快照(引用赋值原子,零拷贝)。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<GarrisonAssessment>> LatestAssessments
        => Volatile.Read(ref _latestAssessments)
           ?? new Dictionary<string, IReadOnlyList<GarrisonAssessment>>();

    // 注:手动模式「玩家目标 vs 推荐」评估(StashAssessments / ClearAssessments)随 legacy
    // Pass A 一并删除。/api/assessment 现恒返回空列表 —— LatestAssessments 不再被填充。

    // ── 财政自治财务视图快照 ──────────────────────────────────────────────────

    /// <summary>
    /// 在 Campaign 主线程调用:对该受管氏族产出一份 FinancialSnapshot.ClanFinance
    /// (金库余额/缓冲上限/日均开销 + 各受管领地单城 P&amp;L),整体替换该 clan 在快照中的条目。
    /// 收入用 <see cref="Models.STClanFinanceModel.SafeTownIncome"/> 重算;推荐头数取调度器最近一次
    /// 求解的目标缓存。任何失败保留旧快照不变(本方法整体 try/catch)。
    ///
    /// 2026-05-23 Plan B：金库余额 ≡ <c>Clan.Gold</c>。TrailingDailyExpense 概念已废弃
    /// （旧的 ClanTreasury 7-日开销环不再存在），快照中固定为 0。
    /// </summary>
    private void StashFinancialSnapshot(CapitalManager manager)
    {
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return;
            string clanId = clan.StringId ?? "";
            if (string.IsNullOrEmpty(clanId)) return;

            // 发布 player clan id 给 FinancialSnapshot 消费者（ActivityTabVM 用此筛选玩家氏族条目）。
            if (clan == TaleWorlds.CampaignSystem.Clan.PlayerClan)
            {
                Economy.FinancialSnapshot.SetPlayerClanId(clanId);
            }

            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();

            // 一次性物化 clan 的活跃 fiefs；预算计算和单城 P&L 共用同一份列表，避免重复 LINQ 分配。
            var fiefs = clan.Fiefs?.Where(t => t?.Settlement != null && t.Settlement.IsActive).ToList()
                        ?? new List<Town>();

            // 驻军工资预算:复用调度器同一口径(GarrisonAllocationSolver.ClanWageBudget)——
            // 调度器求解是 async/跨帧,看板不等求解;此处同步重算与求解所用一致的预算值。
            long garrisonWageBudget = 0;
            try
            {
                if (fiefs.Count > 0)
                {
                    int wagePerTroop = Math.Max(1, GarrisonAllocationSolver.WagePerTroopAtMaxTier(manager!, fiefs));
                    garrisonWageBudget = GarrisonAllocationSolver.ClanWageBudget(manager!, fiefs, cfg, wagePerTroop);
                }
            }
            catch (Exception budgetEx)
            {
                Logger.Error($"CapitalLogisticsManager.StashFinancialSnapshot: budget compute failed (clan={clanId})", budgetEx);
            }

            // STClanFinanceModel 仅作为继承自 DefaultClanFinanceModel 的只读 helper（Plan B 起不再注册）。
            // 构造廉价、无状态，直接 new 一份用于 SafeTownIncome 调用。
            var financeModel = new Models.STClanFinanceModel();

            var cf = new Economy.FinancialSnapshot.ClanFinance
            {
                ClanId = clanId,
                ClanName = clan.Name?.ToString() ?? clanId,
                TreasuryBalance = clan.Gold,
                TrailingDailyExpense = 0,  // Plan B：开销环概念已废弃
                GarrisonWageBudget = garrisonWageBudget,
            };

            foreach (var town in fiefs)
            {
                try
                {
                    var s = town.Settlement;
                    long income = financeModel.SafeTownIncome(clan, town);
                    long wage = 0;
                    var gp = town.GarrisonParty;
                    if (gp != null && gp.IsActive) wage = Math.Max(0, gp.TotalWage);
                    // 防御性上界 10000，应付存档里偶发异常 roster。
                    int current = Math.Min(gp?.MemberRoster?.TotalManCount ?? 0, 10000);
                    // 目标驻军 = PartySizeLimit × TargetFraction(由 cfg.TargetFraction 调激进度);
                    // 实际驻军受预算 / 供给 / 路径约束逼近此目标。缓存未就绪 → 返回 0。
                    int target = ResolveTargetGarrison(clan, s);

                    cf.Settlements.Add(new Economy.FinancialSnapshot.SettlementPnl
                    {
                        SettlementId = s.StringId ?? "",
                        Name = s.Name?.ToString() ?? s.StringId ?? "",
                        IsCastle = s.IsCastle,
                        Income = income,
                        GarrisonWage = wage,
                        Net = income - wage,
                        CurrentGarrison = current,
                        TargetGarrison = target,
                    });
                    cf.TotalIncome += income;
                    cf.TotalGarrisonWage += wage;
                }
                catch (Exception inner)
                {
                    Logger.Error($"CapitalLogisticsManager.StashFinancialSnapshot: skipping '{town?.Settlement?.StringId}'", inner);
                }
            }

            Economy.FinancialSnapshot.ReplaceClan(clanId, cf);
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalLogisticsManager.StashFinancialSnapshot failed (clan={manager?.OwnerClan?.StringId})", ex);
        }
    }

    /// <summary>
    /// 控制面板「目标驻军」取值 = 调度器算法在【预算+威胁+战略】约束下判断该城应有的驻军。
    /// 公式见 <see cref="UnifiedGarrisonSolver"/> SolveCoroutine 的 perCityCapacity 块。
    ///
    /// 性质:
    ///   - 预算充足 → = PartySizeLimit(vanilla 上限)
    ///   - 预算紧 → 按 (PartySizeLimit × threat × strategic) 加权分(高威胁/首府/富城多)
    ///   - 不依赖当前 garrison 实际人数(玩家抽兵不会让此值变)
    ///
    /// MCMF 实际派兵结果(flow,可能瞬时低于 capacity)由 <see cref="UnifiedSolverResult.Target"/>
    /// 在指令派发 / 诊断日志使用,不显示给玩家。
    ///
    /// 缓存未就绪(首个求解未完成)→ fallback 为 hardCap = PartySizeLimit。
    /// </summary>
    private int ResolveTargetGarrison(Clan clan, Settlement s)
    {
        try
        {
            var t = s?.Town;
            if (t == null) return 0;
            if (clan != null && _lastMergedCapacity.TryGetValue(clan, out var map)
                && s != null && map.TryGetValue(s, out var cap))
            {
                return cap;
            }
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();
            return GarrisonAllocationSolver.HardCapFor(t, cfg);
        }
        catch (Exception ex)
        {
            Logger.Error($"ResolveTargetGarrison failed for s={s?.StringId}", ex);
            return 0;
        }
    }
}
