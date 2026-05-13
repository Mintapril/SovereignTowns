using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Transfer;

/// <summary>
/// 一个跨 settlement 的兵员调拨任务（纯数据：不持有 MobileParty，不引用 GarrisonTransferManager）。
/// 由 <see cref="CastleSupportManager"/> 在每日评估时产出，下游消费者（如 GarrisonTransferManager）
/// 自行决定如何执行（创建临时押运队 / 直接走结算 API / 排队等等）。
/// </summary>
/// <remarks>
/// 不使用 C# 9 的 <c>init</c> 访问器：netstandard2.1 缺少 <c>System.Runtime.CompilerServices.IsExternalInit</c>。
/// 一律走构造器赋值 + 只读 <c>get;</c>。
/// </remarks>
public sealed class TransferTask
{
    /// <summary>构造一个不可变的 TransferTask。</summary>
    /// <param name="source">兵员来源 settlement（surplus）。</param>
    /// <param name="destination">兵员去向 settlement（deficit）。</param>
    /// <param name="requestedTroops">本次计划调拨的兵员人数（≥ 1）。</param>
    /// <param name="priority">优先级，越大越紧迫（通常正比于目的地缺口）。</param>
    /// <param name="reason">人类可读理由（写入审计 / 日志）。</param>
    public TransferTask(
        Settlement source,
        Settlement destination,
        int requestedTroops,
        int priority,
        string reason)
    {
        Source = source;
        Destination = destination;
        RequestedTroops = requestedTroops;
        Priority = priority;
        Reason = reason ?? string.Empty;
    }

    /// <summary>兵员来源 settlement。</summary>
    public Settlement Source { get; }

    /// <summary>兵员去向 settlement。</summary>
    public Settlement Destination { get; }

    /// <summary>计划调拨人数。</summary>
    public int RequestedTroops { get; }

    /// <summary>优先级（越大越紧迫）。</summary>
    public int Priority { get; }

    /// <summary>人类可读理由（审计字符串）。</summary>
    public string Reason { get; }
}

/// <summary>
/// 每日扫描玩家自有 Town/Castle 驻军差距，输出"调拨任务"列表（<see cref="TransferTask"/>）。
/// 仅负责"规划"——不创建 MobileParty、不直接调用 <c>GarrisonTransferManager</c>。
/// 该 Manager 是无状态的（除了 ctor 注入的 dep），可由 CampaignBehavior 每天调用一次。
/// </summary>
public sealed class CastleSupportManager
{
    // === 可选依赖：用于判定 surplus 候选是否为首府（首府享受优先调出 + 较低 surplus 阈值） ===
    private readonly CapitalManager? _capitalManager;

    /// <summary>
    /// 构造。<paramref name="capitalManager"/> 为 null 时退化为旧版"对等阈值 + 仅按距离匹配"逻辑，
    /// 用于系统关闭 / 单元测试场景。
    /// </summary>
    public CastleSupportManager(CapitalManager? capitalManager = null)
    {
        _capitalManager = capitalManager;
    }

    // === 任务规则文档中明确的硬编码常量 ===
    /// <summary>缺口阈值：garrisonCount &lt; MinimumDefenders × 1.2 视为 deficit。</summary>
    private const float DeficitMultiplier = 1.2f;

    /// <summary>非首府的盈余阈值：garrisonCount &gt; TargetTotalCount × 1.3 视为 surplus。</summary>
    private const float SurplusMultiplier = 1.3f;

    /// <summary>首府的盈余阈值：只要达到 TargetTotalCount × 1.0 即视为 surplus（积极外调）。</summary>
    private const float CapitalSurplusMultiplier = 1.0f;

    /// <summary>单次调拨硬上限（人）。</summary>
    private const int MaxTroopsPerTask = 30;

    /// <summary>调拨人数缩放系数（min(deficit, surplus, 30) × 0.5）。</summary>
    private const float TransferScale = 0.5f;

    /// <summary>地图二维距离上限。</summary>
    private const float MaxPairDistance = 100f;

    /// <summary>
    /// 扫描所有玩家自有 Town/Castle，配对 deficit ↔ surplus，产出 TransferTask 列表。
    /// 整体 try-catch；任何子流程异常都不会让本方法抛出。
    /// </summary>
    /// <returns>不可变视图：本次评估产生的调拨任务（可能为空）。</returns>
    public IReadOnlyList<TransferTask> EvaluateAll()
    {
        try
        {
            var owned = CollectPlayerOwnedTownsAndCastles();
            if (owned.Count == 0)
            {
                Logger.Debug("CastleSupportManager.EvaluateAll: no player-owned town/castle");
                return Array.Empty<TransferTask>();
            }

            var deficits = new List<EvaluatedSettlement>();
            var surpluses = new List<EvaluatedSettlement>();

            foreach (var t in owned)
            {
                try
                {
                    ClassifyOne(t, deficits, surpluses);
                }
                catch (Exception ex)
                {
                    Logger.Error($"CastleSupportManager.ClassifyOne failed for '{t?.Name}'", ex);
                }
            }

            Logger.Info(
                $"CastleSupportManager.EvaluateAll: owned={owned.Count} " +
                $"deficits={deficits.Count} surpluses={surpluses.Count}");

            if (deficits.Count == 0 || surpluses.Count == 0)
            {
                return Array.Empty<TransferTask>();
            }

            return PairAndBuildTasks(deficits, surpluses);
        }
        catch (Exception ex)
        {
            Logger.Error("CastleSupportManager.EvaluateAll outer failure", ex);
            return Array.Empty<TransferTask>();
        }
    }

    /// <summary>
    /// B1 #1.B: single-destination demand path. Runs the standard pair evaluation
    /// but restricts <paramref name="destination"/> to the supplied town, then
    /// dispatches the resulting tasks through <see cref="GarrisonTransferManager"/>
    /// in the same tick (no waiting for the next DailyTick). Idempotent: if a
    /// transfer is already in-flight to <paramref name="destination"/>, the underlying
    /// <c>MaxTransfersPerTown=1</c> limit makes additional dispatches a no-op.
    /// </summary>
    /// <param name="destination">The deficit town whose intent triggered this call.</param>
    /// <param name="requestedMagnitude">Suggested troop count; only used to cap the
    /// number of tasks dispatched (one task per <see cref="MaxTroopsPerTask"/>).</param>
    /// <param name="transferManager">Already-constructed GarrisonTransferManager; null
    /// returns 0 + warning log.</param>
    /// <returns>Number of TransferTasks that were actually dispatched (≥ 0).</returns>
    public int TryDispatchForDemand(Town destination, int requestedMagnitude, GarrisonTransferManager? transferManager)
    {
        if (destination == null || destination.Settlement == null)
        {
            Logger.Warn("CastleSupportManager.TryDispatchForDemand: destination is null");
            return 0;
        }
        if (transferManager == null)
        {
            Logger.Warn($"TryDispatchForDemand '{destination.Name}': transferManager is null — skipped");
            return 0;
        }
        if (requestedMagnitude <= 0) return 0;

        try
        {
            // Reuse the full evaluation; intent-triggered path stays consistent with
            // DailyTick logic. PairAndBuildTasks already honors all filters (faction /
            // siege / risk / distance) so we just filter the result by destination.
            var allTasks = EvaluateAll();
            if (allTasks.Count == 0) return 0;

            int maxTasks = (int)Math.Ceiling((double)requestedMagnitude / MaxTroopsPerTask);
            if (maxTasks < 1) maxTasks = 1;

            int dispatched = 0;
            foreach (var task in allTasks)
            {
                if (task.Destination != destination.Settlement) continue;
                if (dispatched >= maxTasks) break;

                bool ok;
                try
                {
                    ok = transferManager.TryDispatchTransfer(task);
                }
                catch (Exception ex)
                {
                    Logger.Error("TryDispatchForDemand: TryDispatchTransfer threw", ex);
                    ok = false;
                }

                if (ok)
                {
                    dispatched++;
                    DecisionAuditLogger.LogRule(
                        decisionType: "TransferRequestedByIntent",
                        inputSummary: $"dst={destination.Settlement.StringId} src={task.Source.StringId} requested={requestedMagnitude}",
                        decisionJson: $"{{\"requested\":{requestedMagnitude},\"task_troops\":{task.RequestedTroops},\"reason\":\"{EscapeAudit(task.Reason)}\"}}",
                        accepted: true);
                }
            }

            Logger.Info($"TryDispatchForDemand '{destination.Name}': dispatched={dispatched} / candidates={allTasks.Count} (requested={requestedMagnitude})");
            return dispatched;
        }
        catch (Exception ex)
        {
            Logger.Error($"CastleSupportManager.TryDispatchForDemand outer failure for '{destination.Name}'", ex);
            return 0;
        }
    }

    private static string EscapeAudit(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }

    // ---------------------------------------------------------------
    // 内部辅助：所有玩家自有 Town/Castle
    // ---------------------------------------------------------------

    private static List<Town> CollectPlayerOwnedTownsAndCastles()
    {
        var result = new List<Town>();
        try
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null) return result;

            // Town.AllTowns 同时枚举 Town 和 Castle（Castle 内部也持有一个 Town 实体）。
            foreach (var t in Town.AllTowns)
            {
                if (t == null) continue;
                if (!(t.IsTown || t.IsCastle)) continue;
                if (t.OwnerClan != playerClan) continue;
                result.Add(t);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CastleSupportManager.CollectPlayerOwnedTownsAndCastles failed", ex);
        }
        return result;
    }

    // ---------------------------------------------------------------
    // 内部辅助：单个 settlement 分类
    // ---------------------------------------------------------------

    private void ClassifyOne(
        Town town,
        List<EvaluatedSettlement> deficits,
        List<EvaluatedSettlement> surpluses)
    {
        var settlement = town.Settlement;
        if (settlement == null) return;
        if (!settlement.IsActive) return;

        var rule = ConfigurationManager.GetRuleFor(town) ?? TownGarrisonRule.CreateDefault();

        int garrisonCount = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;

        // 首府：surplus 阈值降为 1.0× 以积极外调；非首府保持 1.3×。
        // _capitalManager 为 null（系统关闭）时退化为旧逻辑（全部走 1.3×）。
        bool isCapital = false;
        try
        {
            var capitalSettlement = _capitalManager?.GetCapitalSettlement();
            isCapital = capitalSettlement != null && capitalSettlement == settlement;
        }
        catch (Exception ex)
        {
            Logger.Error($"CastleSupportManager.ClassifyOne: capital lookup failed for '{settlement.Name}'", ex);
            isCapital = false;
        }
        float effectiveSurplusMultiplier = isCapital ? CapitalSurplusMultiplier : SurplusMultiplier;

        float deficitThreshold = rule.MinimumDefenders * DeficitMultiplier;
        float surplusThreshold = rule.TargetTotalCount * effectiveSurplusMultiplier;

        // 防御：首府不能同时是 deficit 又是 surplus（两阈值有数学上可能重叠的 corner case）。
        // 此 if/else if 结构天然保证一个 settlement 至多归入一类；首府若 garrison 低于
        // deficitThreshold，仍会被列为 deficit（合理：首府自身正缺人也不能"自外调"）。
        if (garrisonCount < deficitThreshold)
        {
            // deficit_gap：相对于 MinimumDefenders 的硬下限缺口（向上取整以"宁多勿少"）。
            int gap = (int)Math.Ceiling(deficitThreshold - garrisonCount);
            if (gap < 1) gap = 1;

            deficits.Add(new EvaluatedSettlement(
                town: town,
                settlement: settlement,
                garrisonCount: garrisonCount,
                gap: gap,
                rule: rule,
                isCapital: isCapital));

            Logger.Debug(
                $"  deficit: '{settlement.Name}' garrison={garrisonCount} " +
                $"threshold={deficitThreshold:F1} gap={gap} isCapital={isCapital}");
        }
        else if (garrisonCount > surplusThreshold)
        {
            int excess = (int)Math.Floor(garrisonCount - surplusThreshold);
            if (excess < 1) return;

            surpluses.Add(new EvaluatedSettlement(
                town: town,
                settlement: settlement,
                garrisonCount: garrisonCount,
                gap: excess,
                rule: rule,
                isCapital: isCapital));

            Logger.Debug(
                $"  surplus: '{settlement.Name}' garrison={garrisonCount} " +
                $"threshold={surplusThreshold:F1} excess={excess} isCapital={isCapital}");
        }
    }

    // ---------------------------------------------------------------
    // 内部辅助：配对 deficit ↔ surplus
    // ---------------------------------------------------------------

    private List<TransferTask> PairAndBuildTasks(
        List<EvaluatedSettlement> deficits,
        List<EvaluatedSettlement> surpluses)
    {
        // 缺口越大优先级越高 → 先匹配最缺的目的地
        deficits.Sort(static (a, b) => b.Gap.CompareTo(a.Gap));

        var tasks = new List<TransferTask>();

        foreach (var dest in deficits)
        {
            try
            {
                var pair = FindBestSurplusFor(dest, surpluses);
                if (pair == null) continue;
                if (dest.Settlement == null || pair.Settlement == null) continue;

                // 防御性自配对检查（理论上 ClassifyOne 的 if/else 已经排除了这种情况；
                // 这里只是兜底，避免任何下游误传入相同 settlement 两次）。
                if (pair.Settlement == dest.Settlement)
                {
                    Logger.Warn(
                        $"  skip self-pair guard: '{dest.Settlement?.Name}' would transfer to itself — defensive skip");
                    continue;
                }

                int rawAmount = Math.Min(dest.Gap, Math.Min(pair.Gap, MaxTroopsPerTask));
                int planned = (int)Math.Floor(rawAmount * TransferScale);
                if (planned < 1)
                {
                    Logger.Debug(
                        $"  skip pair: dest='{dest.Settlement?.Name}' source='{pair.Settlement?.Name}' " +
                        $"planned<1 (raw={rawAmount})");
                    continue;
                }

                string capitalTag = pair.IsCapital ? " (capital->branch)" : string.Empty;
                string reason =
                    $"deficit dst='{dest.Settlement?.Name}' gap={dest.Gap} " +
                    $"surplus src='{pair.Settlement?.Name}' excess={pair.Gap} " +
                    $"planned={planned}{capitalTag}";

                tasks.Add(new TransferTask(
                    source: pair.Settlement!,
                    destination: dest.Settlement!,
                    requestedTroops: planned,
                    priority: dest.Gap,
                    reason: reason));

                Logger.Info(
                    $"CastleSupport task: '{pair.Settlement?.Name}' -> '{dest.Settlement?.Name}' " +
                    $"troops={planned} priority={dest.Gap}{capitalTag}");
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"CastleSupportManager.PairAndBuildTasks failed for dest='{dest.Settlement?.Name}'", ex);
            }
        }

        return tasks;
    }

    /// <summary>
    /// 匹配规则（优先级从高到低）：
    ///   1) 首府候选 vs 非首府候选：首府胜（不论距离）；
    ///   2) 同类（都是首府或都不是）：距离短者胜（< 100）。
    /// 过滤：必须同 MapFaction、不被围攻、风险非 Critical、距离 &lt; <see cref="MaxPairDistance"/>。
    /// </summary>
    private static EvaluatedSettlement? FindBestSurplusFor(
        EvaluatedSettlement dest,
        List<EvaluatedSettlement> surpluses)
    {
        var destSettlement = dest.Settlement;
        var destFaction = destSettlement.MapFaction;
        var destPos = destSettlement.GetPosition2D;

        EvaluatedSettlement? best = null;
        float bestDistance = float.MaxValue;
        bool bestIsCapital = false;

        for (int i = 0; i < surpluses.Count; i++)
        {
            var candidate = surpluses[i];
            var src = candidate.Settlement;
            if (src == null) continue;
            if (src == destSettlement) continue;

            // 同 MapFaction
            if (src.MapFaction != destFaction) continue;

            // 不被围攻
            if (src.IsUnderSiege) continue;

            // 风险 != Critical
            var risk = RiskAssessmentService.Assess(src);
            if (risk.Level == RiskLevel.Critical) continue;

            // 距离 < 100
            float distance = (src.GetPosition2D - destPos).Length;
            if (distance >= MaxPairDistance) continue;

            // 首府优先：若当前 best 不是首府而 candidate 是 → 取代；
            //          若当前 best 是首府而 candidate 不是 → 跳过；
            //          否则同类比距离。
            if (best == null)
            {
                best = candidate;
                bestDistance = distance;
                bestIsCapital = candidate.IsCapital;
                continue;
            }

            if (candidate.IsCapital && !bestIsCapital)
            {
                best = candidate;
                bestDistance = distance;
                bestIsCapital = true;
            }
            else if (!candidate.IsCapital && bestIsCapital)
            {
                // 已有首府 source，非首府无法抢占
                continue;
            }
            else if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
                bestIsCapital = candidate.IsCapital;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------
    // 内部数据载体
    // ---------------------------------------------------------------

    private sealed class EvaluatedSettlement
    {
        public EvaluatedSettlement(
            Town town,
            Settlement settlement,
            int garrisonCount,
            int gap,
            TownGarrisonRule rule,
            bool isCapital)
        {
            Town = town;
            Settlement = settlement;
            GarrisonCount = garrisonCount;
            Gap = gap;
            Rule = rule;
            IsCapital = isCapital;
        }

        public Town Town { get; }
        public Settlement Settlement { get; }
        public int GarrisonCount { get; }
        /// <summary>deficit: 距离阈值还差多少；surplus: 超过阈值多少。</summary>
        public int Gap { get; }
        public TownGarrisonRule Rule { get; }
        /// <summary>是否为玩家当前首府（影响 surplus 阈值与匹配优先级）。</summary>
        public bool IsCapital { get; }
    }
}
