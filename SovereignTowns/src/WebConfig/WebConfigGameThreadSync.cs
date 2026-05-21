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
/// </summary>
internal static class WebConfigGameThreadSync
{
    private static int _pending;
    private static readonly ConcurrentQueue<string?> _pendingChangedSettlementIds = new();

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

        // C1：HTTP 客户端短时间内大批 PATCH 时 _pendingChangedSettlementIds 可能积累上百条 →
        // 在单次 Drain 内全部回放会卡死 game-thread tick；每 tick 最多 256 条，其余保留下次 Drain。
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
    }
}
