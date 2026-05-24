using System;
using Helpers;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui;

/// <summary>
/// Registers the "设为首府" / 「管理卫队」/ 「当前首府状态」 options on the vanilla town / castle game-menu.
/// 详细配置走大地图常驻的「Sovereign Towns 控制面板」按钮（Gauntlet 弹窗，见 ControlPanelMapButtonView）。
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

                // PR-9：「管理 卫队」可点击菜单选项 — 在玩家站在自己的首府 town 且 卫队存在时显示。
                // 点击后调 vanilla PartyScreenHelper.OpenScreenAsManageTroops 开原版兵员管理界面。
                starter.AddGameMenuOption(
                    menuId: menu,
                    optionId: "sovereign_towns_manage_reserve_pool",
                    optionText: "{=ST_Menu_ManageHonorGuard}Sovereign Towns: manage honor guard",
                    condition: new GameMenuOption.OnConditionDelegate(IsManageHonorGuardAvailable),
                    consequence: new GameMenuOption.OnConsequenceDelegate(OnManageHonorGuardSelected),
                    isLeave: false,
                    index: -1,
                    isRepeatable: true);

                starter.AddGameMenuOption(
                    menuId: menu,
                    optionId: "sovereign_towns_set_capital",
                    optionText: "{=ST_Menu_SetCapital}Sovereign Towns: set as capital",
                    condition: new GameMenuOption.OnConditionDelegate(IsSetCapitalAvailable),
                    consequence: new GameMenuOption.OnConsequenceDelegate(OnSetCapitalSelected),
                    isLeave: false,
                    index: -1,
                    isRepeatable: false);
            }
            Logger.Info("DiagnosticGameMenu: registered capital_status / manage_reserve_pool / set_capital on town + castle");
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
            var capitalNameObj = (TextObject?)capital?.Settlement?.Name
                ?? new TextObject("{=ST_Common_Unset}(none)");
            bool isThisCapital = capital != null && capital == s.Town;

            var statusTemplate = isThisCapital
                ? new TextObject("{=ST_Menu_CapitalStatus_Active}Sovereign Towns: current capital ★ ({CAPITAL})")
                : new TextObject("{=ST_Menu_CapitalStatus_Other}Sovereign Towns: current capital = {CAPITAL}");
            statusTemplate.SetTextVariable("CAPITAL", capitalNameObj);

            try { MBTextManager.SetTextVariable("ST_CAPITAL_STATUS", statusTemplate, false); }
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

    // ── PR-9: 「管理 卫队」可点击选项 ──
    // PR-9 (2026-05-24): 原 PR-8 只读状态行替换为可点击操作项，
    // 点击后调 vanilla PartyScreenHelper.OpenScreenAsManageTroops 打开原版兵员管理界面。

    /// <summary>
    /// PR-9：「管理 卫队」条件。
    /// 三重门控：
    ///   1. 当前 settlement 是玩家氏族拥有的 Town（非城堡）且是当前首府
    ///   2. HonorGuardCap &gt; 0（功能已启用）
    ///   3. 卫队 party 实际存在（HonorGuardManager 已为此首府创建了 卫队）
    /// </summary>
    private static bool IsManageHonorGuardAvailable(MenuCallbackArgs args)
    {
        try
        {
            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; }
            catch { /* enum value or property absent on this build — non-fatal */ }

            var s = Settlement.CurrentSettlement;
            if (s == null || !s.IsTown || s.OwnerClan != Clan.PlayerClan) return false;

            var playerMgr = PlayerCapital;
            if (playerMgr == null) return false;

            var capital = playerMgr.GetCapital();
            if (capital == null || capital != s.Town) return false;

            int cap = ConfigurationManager.Current?.FiscalAutonomy?.HonorGuardCap ?? 0;
            if (cap == 0) return false;

            // 卫队 party 必须已存在才显示选项。
            var pool = HonorGuardManager.GetPoolStatic(s);
            return pool != null;
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.IsManageHonorGuardAvailable failed", ex);
            return false;
        }
    }

    /// <summary>
    /// PR-9：「管理 卫队」consequence — 调 vanilla PartyScreenHelper.OpenScreenAsManageTroops
    /// 打开原版兵员管理界面（左侧 卫队 troops，右侧 main party，支持 transfer/split/recruit）。
    /// PartyScreen 自管理屏幕栈；关闭后玩家回到大地图，不调 TryReturnToSettlementMenu。
    /// 反编译证据（2026-05-24，TaleWorlds.CampaignSystem.dll）：
    ///   OpenScreenAsManageTroops(MobileParty leftParty) 单参数 → PushState(partyState)。
    /// </summary>
    private static void OnManageHonorGuardSelected(MenuCallbackArgs args)
    {
        try
        {
            var s = Settlement.CurrentSettlement;
            if (s == null) return;

            var pool = HonorGuardManager.GetPoolStatic(s);
            if (pool == null)
            {
                Logger.Warn("DiagnosticGameMenu.OnManageHonorGuardSelected: honor guard not found for current settlement");
                TryReturnToSettlementMenu();
                return;
            }

            PartyScreenHelper.OpenScreenAsManageTroops(pool);
            // PartyScreen 自推屏幕栈；不需要 TryReturnToSettlementMenu。
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticGameMenu.OnManageHonorGuardSelected failed", ex);
            TryReturnToSettlementMenu();
        }
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
                    var msg = new TextObject("{=ST_Msg_Capital_Changed}[Sovereign Towns] Capital switched to '{SETTLEMENT}'.");
                    msg.SetTextVariable("SETTLEMENT", s.Name);
                    SafeDisplay(msg.ToString(), Colors.Green);
                }
                else
                {
                    var msg = new TextObject("{=ST_Msg_Capital_ChangeFailed}[Sovereign Towns] Capital switch failed ('{SETTLEMENT}' is already the capital or is invalid).");
                    msg.SetTextVariable("SETTLEMENT", s.Name);
                    SafeDisplay(msg.ToString(), Colors.Yellow);
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
