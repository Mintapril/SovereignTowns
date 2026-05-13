using System;
using SovereignTowns.Capital;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui;

/// <summary>
/// Registers the "设为首府" option on the vanilla "town" game-menu. This is the only town-menu
/// hook the mod ships — all other UI (诊断 / 控制面板) now lives behind the persistent campaign-map
/// ribbon (<see cref="SovereignTowns.Ui.MapRibbon.SovereignTownsRibbonInjector"/>), so the town
/// menu stays lean (one extra row max, gated by player-clan ownership).
///
/// <para>
/// Class name kept as <c>DiagnosticGameMenu</c> to minimise diff against existing call-sites
/// (e.g. <c>SovereignTownsCampaignBehavior.OnSessionLaunched</c>). All public surface is wrapped
/// in try/catch and degrades to a single <see cref="Logger.Error(string, Exception?)"/> entry on
/// failure — a misbehaving UI hook must never crash the campaign loop or block the player from
/// leaving the menu.
/// </para>
/// </summary>
public static class DiagnosticGameMenu
{
    private static CapitalManager? _capitalManager;

    /// <summary>
    /// Add the "设为首府" option to the vanilla <c>"town"</c> menu. Idempotent at the framework
    /// level — TaleWorlds keys options by id so re-registering with the same id is harmless.
    /// </summary>
    /// <param name="starter">The <see cref="CampaignGameStarter"/> handed to
    /// <c>OnGameStart</c> / <c>OnSessionLaunched</c>.</param>
    /// <param name="capitalManager">Capital subsystem; required for the "设为首府" option.
    /// Pass <c>null</c> to disable the menu entry entirely.</param>
    public static void Register(CampaignGameStarter starter, CapitalManager? capitalManager)
    {
        _capitalManager = capitalManager;

        if (starter is null)
        {
            Logger.Warn("DiagnosticGameMenu.Register: starter is null, skipping");
            return;
        }

        try
        {
            starter.AddGameMenuOption(
                menuId: "town",
                optionId: "sovereign_towns_set_capital",
                optionText: "Sovereign Towns: 设为首府",
                condition: new GameMenuOption.OnConditionDelegate(IsSetCapitalAvailable),
                consequence: new GameMenuOption.OnConsequenceDelegate(OnSetCapitalSelected),
                isLeave: false,
                index: -1,
                isRepeatable: false);
            Logger.Info("DiagnosticGameMenu: registered 'sovereign_towns_set_capital'");
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.Register failed", ex);
        }
    }

    /// <summary>
    /// Condition delegate for the "设为首府" option. Visible when the player stands in a
    /// player-clan-owned town that is NOT already the active capital. Returns <c>false</c> if
    /// the capital subsystem is unavailable.
    /// </summary>
    private static bool IsSetCapitalAvailable(MenuCallbackArgs args)
    {
        try
        {
            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; }
            catch { /* enum value or property absent on this build — non-fatal */ }

            var s = Settlement.CurrentSettlement;
            return s != null
                && s.IsTown
                && s.OwnerClan == Clan.PlayerClan
                && _capitalManager != null
                && s.Town != _capitalManager.GetCapital();
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.IsSetCapitalAvailable failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Consequence delegate for "设为首府". Delegates to
    /// <see cref="CapitalManager.ManuallySetCapital(Town)"/>; shows a colored toast for the
    /// outcome and then bounces back to the "town" menu.
    /// </summary>
    private static void OnSetCapitalSelected(MenuCallbackArgs args)
    {
        try
        {
            var s = Settlement.CurrentSettlement;
            if (s?.Town != null && _capitalManager != null)
            {
                bool ok = _capitalManager.ManuallySetCapital(s.Town);
                if (ok)
                {
                    SafeDisplay($"[Sovereign Towns] 首府已切换至 '{s.Name}'", Colors.Green);
                }
                else
                {
                    SafeDisplay($"[Sovereign Towns] 首府切换失败 ('{s.Name}' 已是首府或不合法)", Colors.Yellow);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.OnSetCapitalSelected failed", ex);
        }
        finally
        {
            TryReturnToTownMenu();
        }
    }

    /// <summary>
    /// Safely show a single <see cref="InformationMessage"/>. Wrapped so a UI subsystem
    /// hiccup (very rare, but possible on save-load races) cannot abort the loop above.
    /// </summary>
    private static void SafeDisplay(string text, Color color)
    {
        try
        {
            InformationManager.DisplayMessage(new InformationMessage(text, color));
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.SafeDisplay failed", ex);
        }
    }

    /// <summary>
    /// Try to switch the player back to the vanilla "town" menu. <see cref="GameMenu.SwitchToMenu"/>
    /// is the canonical API since v1.0; wrapped in try/catch as a paranoia measure in case a
    /// future build renames or restricts it.
    /// </summary>
    private static void TryReturnToTownMenu()
    {
        try
        {
            GameMenu.SwitchToMenu("town");
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.TryReturnToTownMenu failed", ex);
        }
    }
}
