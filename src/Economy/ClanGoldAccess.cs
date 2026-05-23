using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// Reflection helper to write <see cref="Clan.Gold"/>. The vanilla setter is internal (only
/// vanilla's own daily handler applies <c>CalculateClanGoldChange</c> results), so external
/// code can't assign <c>clan.Gold = X</c> directly. Plan B (首府金库 ≡ Clan.Gold) needs that
/// write path, so we cache the setter via reflection at first use.
///
/// 2026-05-23：所有 mod 直接修改 <see cref="Clan.Gold"/> 的位置（<see cref="ModTreasury"/>
/// 和 <see cref="TreasuryUserActions"/>）都通过本类的 <see cref="Change"/> / <see cref="Set"/>。
/// </summary>
internal static class ClanGoldAccess
{
    private static readonly object _lock = new object();
    private static MethodInfo? _setter;
    private static bool _resolved;

    private static MethodInfo? ResolveSetter()
    {
        lock (_lock)
        {
            if (_resolved) return _setter;
            try
            {
                var prop = typeof(Clan).GetProperty(
                    "Gold",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _setter = prop?.GetSetMethod(nonPublic: true);
                if (_setter == null)
                    Logger.Error("ClanGoldAccess: could not resolve Clan.Gold setter via reflection — mod gold writes will be no-ops");
            }
            catch (Exception ex)
            {
                Logger.Error("ClanGoldAccess.ResolveSetter failed", ex);
                _setter = null;
            }
            _resolved = true;
            return _setter;
        }
    }

    /// <summary>Assign <paramref name="clan"/>.Gold = <paramref name="newGold"/>. Returns true on success.</summary>
    public static bool Set(Clan? clan, int newGold)
    {
        if (clan == null) return false;
        var setter = ResolveSetter();
        if (setter == null) return false;
        try
        {
            setter.Invoke(clan, new object[] { newGold });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ClanGoldAccess.Set failed (clan={clan.StringId} newGold={newGold})", ex);
            return false;
        }
    }

    /// <summary>
    /// Apply <paramref name="delta"/> to <paramref name="clan"/>.Gold（may be negative）。
    /// Saturates at <see cref="int.MaxValue"/> / <see cref="int.MinValue"/>. Returns true on success.
    /// </summary>
    public static bool Change(Clan? clan, int delta)
    {
        if (clan == null) return false;
        if (delta == 0) return true;
        long newGold = (long)clan.Gold + delta;
        if (newGold > int.MaxValue) newGold = int.MaxValue;
        if (newGold < int.MinValue) newGold = int.MinValue;
        return Set(clan, (int)newGold);
    }
}
