using System;
using TaleWorlds.CampaignSystem;
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

    private static MobileParty.NavigationType DecideNav(MobileParty party, Settlement target)
    {
        // v1.3.15: 无 IsCoastal / NavigationType.Coastal,统一 Default。
        return MobileParty.NavigationType.Default;
    }
}
