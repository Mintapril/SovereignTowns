using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>
/// B17.4 B3：调拨 / 巡逻 / 招募 等场景的统一 SetMoveGoToSettlement wrapper。
/// 集中 try/catch + Logger,避免每个 call site 重复 boilerplate。
///
/// 说明：原计划尝试用 source/dest IsCoastal 判定 + NavigationType.Coastal 来缓解跨海岛卡死问题,
/// 但 v1.3.15 vanilla 中 Settlement.IsCoastal 与 MobileParty.NavigationType.Coastal 均不存在,
/// 因此目前仅作 NavigationType.Default。后续若改用 Harmony 反射或读取 NavigationCapability,
/// 替换 <see cref="DecideNav"/> 即可,call sites 不必变。
/// </summary>
public static class SafeMoveHelper
{
    public static void GoTo(MobileParty party, Settlement target, string context)
    {
        if (party == null || target == null) return;
        var navType = DecideNav(party, target);
        try
        {
            party.SetMoveGoToSettlement(target, navType, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SafeMoveHelper.GoTo failed for '{party.Name}' -> '{target.Name}' [{context}, nav={navType}]; fallback Default", ex);
            try { party.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false); }
            catch (Exception fallbackEx) { Logger.Error($"SafeMoveHelper.GoTo fallback also failed for '{party.Name}'", fallbackEx); }
        }
    }

    /// <summary>
    /// 2026-05-18 修复 v2：若 party 当前在某 settlement 内 (CurrentSettlement != null)，先 LeaveSettlementAction
    /// 再 SetMoveGoToSettlement。
    ///
    /// 背景：今天加的 `SetDoNotMakeNewDecisions(true)` 使 vanilla AI 在 party 进入 settlement 后不会自主
    /// 执行 LeaveSettlement。结果：巡逻队抵达第一站后被困在 settlement 内，每 tick 重发 SetMoveGoToSettlement
    /// 都是 0 位移（日志铁证：stuck 25h 期间 SetMoveGoToSettlement 反复调用，直到二段瞬移到 home 才解锁）。
    ///
    /// IG 在 PartyManager.cs:540 与 PartyMergeService.TryLeaveSettlementBeforeRemoval 都用此固定顺序。
    /// 本 helper 把它扩展到 nav 路径。LeaveSettlementAction 失败不阻断后续 SetMoveGoToSettlement。
    /// </summary>
    public static void GoToWithLeave(MobileParty party, Settlement target, string context)
    {
        if (party == null || target == null) return;
        try
        {
            if (party.CurrentSettlement != null && party.CurrentSettlement != target)
            {
                try
                {
                    var from = party.CurrentSettlement;
                    LeaveSettlementAction.ApplyForParty(party);
                    Logger.Info($"SafeMoveHelper.GoToWithLeave: '{party.Name}' left '{from?.Name?.ToString() ?? "<unknown>"}' before heading to '{target.Name}' [{context}]");
                }
                catch (Exception leaveEx)
                {
                    Logger.Warn($"SafeMoveHelper.GoToWithLeave: LeaveSettlementAction failed for '{party.Name}' [{context}] (continuing): {leaveEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"SafeMoveHelper.GoToWithLeave: pre-leave check threw for '{party.Name}' [{context}] (continuing): {ex.Message}");
        }
        GoTo(party, target, context);
    }

    private static MobileParty.NavigationType DecideNav(MobileParty party, Settlement target)
    {
        // v1.3.15: 无 IsCoastal / NavigationType.Coastal,统一 Default。
        return MobileParty.NavigationType.Default;
    }
}
