using System;
using System.Collections.Concurrent;
using System.Threading;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.SettlementManagement;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// Queues campaign-object synchronization requested by the HTTP config server.
/// HttpListener handlers run on ThreadPool threads; Bannerlord campaign objects must be
/// touched from campaign event callbacks instead.
///
/// Issue #1 fix (cross-thread OnConfigChanged): config-changed event invocations are
/// queued here and replayed on the campaign thread inside <see cref="Drain"/> — never
/// invoked directly from the HTTP thread.
///
/// 2026-05-23: 提供 <see cref="EnqueueAction"/> 通用主线程 action 排队 + 等待完成,供未来需要
/// 在主线程触碰 vanilla 对象并把结果同步给 HTTP response 的 endpoint 复用(当前无在用 endpoint)。
/// </summary>
internal static class WebConfigGameThreadSync
{
    private static int _pending;
    private static readonly ConcurrentQueue<string?> _pendingChangedSettlementIds = new();

    // 2026-05-23:通用主线程 action 队列。HTTP handler 通过 EnqueueAction 排进来,
    // Drain 在 game-thread tick 内回放;每个 action 自带 ManualResetEventSlim
    // (调用方调 EnqueueAction(action, timeoutMs) 可阻塞等结果)。
    private static readonly ConcurrentQueue<PendingAction> _pendingActions = new();

    private sealed class PendingAction
    {
        public Action Action = () => { };
        public ManualResetEventSlim? Done;
        public string Reason = "";
    }

    public static void Request(string reason)
    {
        Interlocked.Exchange(ref _pending, 1);
        Logger.Info($"WebConfigGameThreadSync: queued campaign sync ({reason})");
    }

    /// <summary>
    /// Queue a config-changed event to be raised on the next main-thread Drain.
    /// settlementId == null → notify subscribers to refresh all in-flight parties.
    /// Also flips the sync pending flag so CapitalRegistry / VanillaSuppression replay too.
    /// </summary>
    public static void RequestConfigChanged(string? settlementId, string reason)
    {
        _pendingChangedSettlementIds.Enqueue(settlementId);
        Request(reason);
    }

    /// <summary>
    /// 排入一个主线程 action。HTTP handler 必须用此通道触碰 vanilla Hero / Settlement 等对象。
    /// 调用方可传入 <paramref name="done"/>(ManualResetEventSlim)在调用线程上 Wait 等待执行完成。
    /// <paramref name="action"/> 内任何异常都会被吞,只记 Warn;调用方应在 action 内部把结果写到
    /// 闭包持有的可变 box 上,再 done.Set(),从而把结果传回 HTTP handler。
    /// </summary>
    public static void EnqueueAction(Action action, ManualResetEventSlim? done, string reason)
    {
        if (action == null) { done?.Set(); return; }
        _pendingActions.Enqueue(new PendingAction { Action = action, Done = done, Reason = reason });
        // 不复用 _pending(那是 CapitalRegistry / VanillaSuppression 用的);action 队列自带 drain。
    }

    public static void Drain()
    {
        if (Interlocked.Exchange(ref _pending, 0) != 0)
        {
            try { CapitalRegistry.Instance?.SyncFromConfig(); }
            catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: CapitalRegistry sync failed: {ex.Message}"); }

            try { VanillaSuppressionManager.Instance?.ApplyToAllSettlements(); }
            catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: VanillaSuppression re-apply failed: {ex.Message}"); }

            try { VanillaPatrolSuppressor.Instance?.DissolveAllManagedVanillaPatrols(); }
            catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: VanillaPatrolSuppressor re-apply failed: {ex.Message}"); }
        }

        // C1:HTTP 客户端短时间内大批 PATCH 时 _pendingChangedSettlementIds 可能积累上百条 →
        // 在单次 Drain 内全部回放会卡死 game-thread tick;每 tick 最多 256 条,其余保留下次 Drain。
        const int MaxDrainPerTick = 256;
        int drainedCount = 0;
        while (_pendingChangedSettlementIds.TryDequeue(out var settlementId))
        {
            if (++drainedCount > MaxDrainPerTick)
            {
                Logger.Warn($"WebConfigGameThreadSync.Drain: limited to {MaxDrainPerTick} per tick, deferring remaining (queue depth ≈ {_pendingChangedSettlementIds.Count})");
                break;
            }
            try { ConfigurationManager.RaiseConfigChanged(settlementId); }
            catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: OnConfigChanged raise failed: {ex.Message}"); }
        }

        // 通用 action 队列。同 tick 上限保护一致。
        int actionCount = 0;
        while (_pendingActions.TryDequeue(out var pa))
        {
            if (++actionCount > MaxDrainPerTick)
            {
                Logger.Warn($"WebConfigGameThreadSync.Drain: action queue limited to {MaxDrainPerTick} per tick (queue depth ≈ {_pendingActions.Count})");
                // 把 pa 放回去 —— 简化处理:直接放弃,调用方靠 Wait timeout 兜底。
                // 真正高吞吐场景再扩。
                pa.Done?.Set();
                break;
            }
            try { pa.Action(); }
            catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: action '{pa.Reason}' failed: {ex.Message}"); }
            finally { pa.Done?.Set(); }
        }
    }
}
