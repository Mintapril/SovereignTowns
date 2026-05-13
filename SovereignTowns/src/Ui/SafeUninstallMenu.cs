using System;
using System.Linq;
using SovereignTowns.Audit;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui;

/// <summary>
/// 在 vanilla "town" 菜单注册 "Sovereign Towns: 安全卸载向导" 选项。
///
/// <para>
/// 仅在玩家所在 settlement 为玩家自有 Town 时显示。点击后：
///   1) 销毁本 Mod 创建的所有 <see cref="RecruitingPartyComponent"/> /
///      <see cref="TransferPartyComponent"/> 队伍；
///   2) 销毁玩家自有 town 的 <see cref="PatrolPartyComponent"/> 队伍
///      (MVP 5 简化：不区分本 Mod 创建与 vanilla 创建)；
///   3) 还原玩家自有 Town 的 <see cref="Town.GarrisonAutoRecruitmentIsEnabled"/> = true；
///   4) 通过 <see cref="DecisionAuditLogger"/> 记录一条 SafeUninstall 审计；
///   5) 返回 "town" 菜单，避免把玩家踢回大地图。
/// </para>
///
/// <para>
/// 所有外部 API 调用均包裹 try/catch；一个 UI 钩子绝不能崩溃 campaign loop。
/// </para>
/// </summary>
public static class SafeUninstallMenu
{
    /// <summary>
    /// 在 vanilla "town" 菜单加 "Sovereign Towns: 安全卸载向导" 选项。
    /// TaleWorlds 按 optionId 去重，重复注册无副作用。
    /// </summary>
    /// <param name="starter">由 <c>OnGameStart</c> / <c>OnSessionLaunched</c> 提供的
    /// <see cref="CampaignGameStarter"/>。</param>
    public static void Register(CampaignGameStarter starter)
    {
        if (starter is null)
        {
            Logger.Warn("SafeUninstallMenu.Register: starter is null, skipping");
            return;
        }

        try
        {
            starter.AddGameMenuOption(
                menuId: "town",
                optionId: "sovereign_towns_uninstall",
                optionText: "{=ST_SU}Sovereign Towns: 安全卸载向导",
                condition: new GameMenuOption.OnConditionDelegate(IsAvailable),
                consequence: new GameMenuOption.OnConsequenceDelegate(OnSelected),
                isLeave: false,
                index: -1,
                isRepeatable: false,
                relatedObject: null);
            Logger.Info("SafeUninstallMenu: registered 'sovereign_towns_uninstall' on 'town' menu");
        }
        catch (Exception ex)
        {
            Logger.Error("SafeUninstallMenu.Register failed", ex);
        }
    }

    /// <summary>
    /// 条件委托：仅在玩家自有 Town 显示。尝试设置 optionLeaveType = Submenu，
    /// 若枚举值在当前游戏版本缺失则静默吞掉（选项仍能用）。
    /// </summary>
    private static bool IsAvailable(MenuCallbackArgs args)
    {
        try
        {
            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; }
            catch { /* 枚举值在当前 build 不存在 — 非致命 */ }

            var s = Settlement.CurrentSettlement;
            return s != null && s.IsTown && s.OwnerClan == Clan.PlayerClan;
        }
        catch (Exception ex)
        {
            Logger.Error("SafeUninstallMenu.IsAvailable failed", ex);
            return false;
        }
    }

    /// <summary>
    /// 结果委托：销毁本 Mod 所有 MobileParty，还原所有玩家自有 Town 的
    /// <see cref="Town.GarrisonAutoRecruitmentIsEnabled"/>，向玩家显示总结消息，
    /// 落一条审计，最后回到 "town" 菜单。
    /// </summary>
    private static void OnSelected(MenuCallbackArgs args)
    {
        int destroyedParties = 0;
        int restoredTowns = 0;
        try
        {
            // 1) 销毁本 Mod 自定义队伍（RecruitingPartyComponent / TransferPartyComponent）。
            //    AllCustomParties 在迭代中被 DisbandPartyAction 改写，先拷贝。
            var customCopy = MobileParty.AllCustomParties.ToList();
            foreach (var p in customCopy)
            {
                try
                {
                    if (p?.PartyComponent is SovereignTowns.Parties.RecruitingPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.TransferPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.SallyForthPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.DismissPartyComponent)
                    {
                        if (p!.IsActive)
                        {
                            DisbandPartyAction.StartDisband(p);
                            destroyedParties++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("uninstall: disband custom party failed: " + ex.Message);
                }
            }

            // 2) 销毁玩家 owner clan 拥有的 PatrolPartyComponent 队伍。
            //    MVP 5 简化：vanilla 与 Mod 创建无法区分，全部销毁；玩家可通过 vanilla 菜单重新生成。
            var patrolCopy = MobileParty.AllPatrolParties.ToList();
            foreach (var p in patrolCopy)
            {
                try
                {
                    if (p?.PartyComponent is PatrolPartyComponent pp
                        && pp.HomeSettlement?.OwnerClan == Clan.PlayerClan)
                    {
                        if (p.IsActive)
                        {
                            DisbandPartyAction.StartDisband(p);
                            destroyedParties++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("uninstall: disband patrol failed: " + ex.Message);
                }
            }

            // 3) 还原玩家自有 Town 的自动招募。
            foreach (var t in Town.AllTowns)
            {
                try
                {
                    if (t != null && t.OwnerClan == Clan.PlayerClan)
                    {
                        t.GarrisonAutoRecruitmentIsEnabled = true;
                        restoredTowns++;
                    }
                }
                catch { /* 单座 town 失败不影响其他 */ }
            }

            var msg = $"Sovereign Towns 安全卸载完成。销毁本 Mod 队伍 {destroyedParties} 支；还原 {restoredTowns} 座城镇的自动招募。请在启动器中禁用本 Mod 并重新读档。";
            SafeDisplay(msg, Colors.Yellow);

            try
            {
                DecisionAuditLogger.LogRule(
                    decisionType: "SafeUninstall",
                    inputSummary: "user-initiated",
                    decisionJson: $"{{\"destroyedParties\":{destroyedParties},\"restoredTowns\":{restoredTowns}}}",
                    accepted: true);
            }
            catch (Exception ex)
            {
                Logger.Warn("SafeUninstall audit log failed: " + ex.Message);
            }

            Logger.Warn($"SAFE UNINSTALL: destroyed={destroyedParties} restored={restoredTowns}");
        }
        catch (Exception ex)
        {
            Logger.Error("SafeUninstall failed", ex);
            SafeDisplay("Sovereign Towns 安全卸载出错，请查日志。", Colors.Red);
        }
        finally
        {
            try { GameMenu.SwitchToMenu("town"); }
            catch (Exception ex) { Logger.Error("SafeUninstallMenu: SwitchToMenu(town) failed", ex); }
        }
    }

    /// <summary>
    /// 安全包裹 <see cref="InformationManager.DisplayMessage"/>，UI 子系统抽风时不影响调用栈。
    /// </summary>
    private static void SafeDisplay(string text, Color color)
    {
        try
        {
            InformationManager.DisplayMessage(new InformationMessage(text, color));
        }
        catch (Exception ex)
        {
            Logger.Error("SafeUninstallMenu.SafeDisplay failed", ex);
        }
    }
}
