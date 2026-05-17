using System;
using System.Diagnostics;
using SovereignTowns.Capital;
using SovereignTowns.WebConfig;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
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
    private static CapitalRegistry? _capitalRegistry;

    /// <summary>玩家氏族的 CapitalManager；菜单只服务玩家视角。</summary>
    private static CapitalManager? PlayerCapital => _capitalRegistry?.GetForPlayer();

    /// <summary>
    /// Add the "设为首府" option to the vanilla <c>"town"</c> menu. Idempotent at the framework
    /// level — TaleWorlds keys options by id so re-registering with the same id is harmless.
    /// </summary>
    /// <param name="starter">The <see cref="CampaignGameStarter"/> handed to
    /// <c>OnGameStart</c> / <c>OnSessionLaunched</c>.</param>
    /// <param name="capitalRegistry">Capital registry; the menu queries the player's manager.
    /// Pass <c>null</c> to disable the menu entry entirely.</param>
    public static void Register(CampaignGameStarter starter, CapitalRegistry? capitalRegistry)
    {
        _capitalRegistry = capitalRegistry;

        if (starter is null)
        {
            Logger.Warn("DiagnosticGameMenu.Register: starter is null, skipping");
            return;
        }

        try
        {
            // 注册到 town + castle 两套菜单 — castle-only 玩家（早期氏族、攻陷后仅持城堡）也要能开网页面板。
            // 「设为首府」的 condition 自身仍仅 s.IsTown == true 才显示（首府语义只允许 Town），
            // 所以注册到 castle 菜单后该选项不会渲染，但 capital_status 和 open_web_config 在 castle 内会出现。
            foreach (var menu in new[] { "town", "castle" })
            {
                // 常驻状态行：在玩家自有城/堡显示「当前首府: <名>」（IsCapitalStatusVisible 内做 town/castle 联合判定）
                starter.AddGameMenuOption(
                    menuId: menu,
                    optionId: "sovereign_towns_capital_status",
                    optionText: "{=!}{ST_CAPITAL_STATUS}",
                    condition: new GameMenuOption.OnConditionDelegate(IsCapitalStatusVisible),
                    consequence: new GameMenuOption.OnConsequenceDelegate(OnCapitalStatusSelected),
                    isLeave: false,
                    index: -1,
                    isRepeatable: true);

                starter.AddGameMenuOption(
                    menuId: menu,
                    optionId: "sovereign_towns_set_capital",
                    optionText: "主权城镇：设为首府",
                    condition: new GameMenuOption.OnConditionDelegate(IsSetCapitalAvailable),
                    consequence: new GameMenuOption.OnConsequenceDelegate(OnSetCapitalSelected),
                    isLeave: false,
                    index: -1,
                    isRepeatable: false);

                // B7.5: web config entry. Available in any town/castle menu — once we pivot to web-only
                // configuration in Phase 2 this is the only player-facing config touchpoint.
                starter.AddGameMenuOption(
                    menuId: menu,
                    optionId: "sovereign_towns_open_web_config",
                    optionText: "主权城镇：打开网页控制面板",
                    condition: new GameMenuOption.OnConditionDelegate(IsOpenWebConfigAvailable),
                    consequence: new GameMenuOption.OnConsequenceDelegate(OnOpenWebConfigSelected),
                    isLeave: false,
                    index: -1,
                    isRepeatable: true);
            }
            Logger.Info("DiagnosticGameMenu: registered capital_status / set_capital / open_web_config on town + castle");
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
                SafeDisplay("[主权城镇] 网页服务未启动；查看日志了解原因。", Colors.Yellow);
                TryReturnToSettlementMenu();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
                // 不把含 token 的 URL 写进 chat / log，避免玩家分享截图 / ModLogs 时 token 外泄。
                SafeDisplay("[主权城镇] 已尝试启动浏览器打开网页控制面板。", Colors.Green);
            }
            catch (Exception procEx)
            {
                Logger.Error("Process.Start for web config URL failed", procEx);
                // 启动失败时玩家需要手动打开浏览器；但绝不能把含 token 的完整 URL 写进 chat
                // —— 玩家若截图分享 mod 配置/驻军面板会泄漏 token 给本机其他进程。
                // 只给 host:port 提示，token 由玩家自己从 Documents 下 auth.txt 读出。
                int port = WebConfigServer.Port;
                SafeDisplay(
                    $"[主权城镇] 浏览器启动失败。请手动访问 http://127.0.0.1:{port}/ ，" +
                    $"并从 Documents\\Mount and Blade II Bannerlord\\Configs\\SovereignTowns\\auth.txt 读取 token。",
                    Colors.Yellow);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnOpenWebConfigSelected failed", ex);
        }
        finally
        {
            TryReturnToSettlementMenu();
        }
    }

    /// <summary>
    /// Condition delegate for the "设为首府" option. Visible when the player stands in a
    /// player-clan-owned town that is NOT already the active capital. Returns <c>false</c> if
    /// the capital subsystem is unavailable.
    /// </summary>
    /// <summary>
    /// 「当前首府」常驻状态行 — 在玩家自有 town/castle 显示。
    /// 通过 MBTextManager.SetTextVariable 把动态首府名注入 optionText 模板「{ST_CAPITAL_STATUS}」。
    /// </summary>
    private static bool IsCapitalStatusVisible(MenuCallbackArgs args)
    {
        try
        {
            var s = Settlement.CurrentSettlement;
            if (s == null || (!s.IsTown && !s.IsCastle) || s.OwnerClan != Clan.PlayerClan) return false;

            var playerMgr = PlayerCapital;
            var capital = playerMgr?.GetCapital();
            string capitalName = capital?.Settlement?.Name?.ToString() ?? "<未设>";
            bool isThisCapital = capital != null && capital == s.Town;

            string text = isThisCapital
                ? $"主权城镇：当前首府 ★ ({capitalName})"
                : $"主权城镇：当前首府 = {capitalName}";

            try { MBTextManager.SetTextVariable("ST_CAPITAL_STATUS", text, false); }
            catch { /* SetTextVariable 偶尔抛 — 不致命 */ }

            // 让选项不可点击 — 视觉上是只读状态行
            args.IsEnabled = false;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.IsCapitalStatusVisible failed", ex);
            return false;
        }
    }

    /// <summary>状态行点击 consequence — 不应触发，但作为防御回弹回原菜单。</summary>
    private static void OnCapitalStatusSelected(MenuCallbackArgs args)
    {
        TryReturnToSettlementMenu();
    }

    private static bool IsSetCapitalAvailable(MenuCallbackArgs args)
    {
        try
        {
            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; }
            catch { /* enum value or property absent on this build — non-fatal */ }

            var s = Settlement.CurrentSettlement;
            var playerMgr = PlayerCapital;
            return s != null
                && s.IsTown
                && s.OwnerClan == Clan.PlayerClan
                && playerMgr != null
                && s.Town != playerMgr.GetCapital();
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
            var playerMgr = PlayerCapital;
            if (s?.Town != null && playerMgr != null)
            {
                bool ok = playerMgr.ManuallySetCapital(s.Town);
                if (ok)
                {
                    SafeDisplay($"[主权城镇] 首府已切换至 '{s.Name}'", Colors.Green);
                }
                else
                {
                    SafeDisplay($"[主权城镇] 首府切换失败 ('{s.Name}' 已是首府或不合法)", Colors.Yellow);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.OnSetCapitalSelected failed", ex);
        }
        finally
        {
            TryReturnToSettlementMenu();
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
    /// Try to switch the player back to the vanilla settlement menu the option was triggered from
    /// ("town" or "castle"). <see cref="GameMenu.SwitchToMenu"/> is the canonical API since v1.0;
    /// wrapped in try/catch as a paranoia measure. Falls back to "town" if the current settlement
    /// is somehow null (defensive — shouldn't happen inside a settlement menu consequence).
    /// </summary>
    private static void TryReturnToSettlementMenu()
    {
        try
        {
            var s = Settlement.CurrentSettlement;
            string menuId = (s != null && s.IsCastle) ? "castle" : "town";
            GameMenu.SwitchToMenu(menuId);
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.TryReturnToSettlementMenu failed", ex);
        }
    }
}
