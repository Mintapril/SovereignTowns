using System;
using System.Diagnostics;
using SovereignTowns.Capital;
using SovereignTowns.WebConfig;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui;

/// <summary>
/// Registers the "设为首府" and "打开网页控制面板" options on the vanilla "town" game-menu.
/// These are the only town-menu hooks the mod ships — all detailed configuration now lives in
/// the browser-side web config served by <see cref="SovereignTowns.WebConfig.WebConfigServer"/>,
/// so the town menu stays lean.
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

            // B7.5: web config entry. Available in any town menu — once we pivot to web-only
            // configuration in Phase 2 this is the only player-facing config touchpoint.
            starter.AddGameMenuOption(
                menuId: "town",
                optionId: "sovereign_towns_open_web_config",
                optionText: "Sovereign Towns: 打开网页控制面板",
                condition: new GameMenuOption.OnConditionDelegate(IsOpenWebConfigAvailable),
                consequence: new GameMenuOption.OnConsequenceDelegate(OnOpenWebConfigSelected),
                isLeave: false,
                index: -1,
                isRepeatable: true);
            Logger.Info("DiagnosticGameMenu: registered 'sovereign_towns_open_web_config'");
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.Register failed", ex);
        }
    }

    /// <summary>「打开网页控制面板」条件 —— server 在运行才显示。</summary>
    private static bool IsOpenWebConfigAvailable(MenuCallbackArgs args)
    {
        try
        {
            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; }
            catch { /* enum value or property absent on this build — non-fatal */ }
            return WebConfigServer.IsRunning;
        }
        catch (Exception ex)
        {
            Logger.Error("IsOpenWebConfigAvailable failed", ex);
            return false;
        }
    }

    /// <summary>「打开网页控制面板」consequence —— Process.Start 启动系统默认浏览器到含 token 的 URL。</summary>
    private static void OnOpenWebConfigSelected(MenuCallbackArgs args)
    {
        try
        {
            string url = WebConfigServer.GetBrowserUrl();
            if (string.IsNullOrEmpty(url))
            {
                SafeDisplay("[Sovereign Towns] 网页服务未启动；查看日志了解原因。", Colors.Yellow);
                TryReturnToTownMenu();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
                SafeDisplay($"[Sovereign Towns] 已尝试启动浏览器：{url}", Colors.Green);
            }
            catch (Exception procEx)
            {
                Logger.Error("Process.Start for web config URL failed", procEx);
                SafeDisplay($"[Sovereign Towns] 浏览器启动失败。请手动访问：{url}", Colors.Yellow);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnOpenWebConfigSelected failed", ex);
        }
        finally
        {
            TryReturnToTownMenu();
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
