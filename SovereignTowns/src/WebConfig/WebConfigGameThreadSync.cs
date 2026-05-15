using System.Threading;
using SovereignTowns.Capital;
using SovereignTowns.SettlementManagement;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// Queues campaign-object synchronization requested by the HTTP config server.
/// HttpListener handlers run on ThreadPool threads; Bannerlord campaign objects must be
/// touched from campaign event callbacks instead.
/// </summary>
internal static class WebConfigGameThreadSync
{
    private static int _pending;

    public static void Request(string reason)
    {
        Interlocked.Exchange(ref _pending, 1);
        Logger.Info($"WebConfigGameThreadSync: queued campaign sync ({reason})");
    }

    public static void Drain()
    {
        if (Interlocked.Exchange(ref _pending, 0) == 0) return;

        try { CapitalRegistry.Instance?.SyncFromConfig(); }
        catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: CapitalRegistry sync failed: {ex.Message}"); }

        try { VanillaSuppressionManager.Instance?.ApplyToAllSettlements(); }
        catch (System.Exception ex) { Logger.Warn($"WebConfigGameThreadSync: VanillaSuppression re-apply failed: {ex.Message}"); }
    }
}
