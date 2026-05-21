using HarmonyLib;
using SovereignTowns.SettlementManagement;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Patches;

/// <summary>
/// Harmony 前缀:受管氏族定居点禁止 vanilla 生成新巡逻队。
/// 目标 = PatrolPartiesCampaignBehavior.CanSettlementSpawnNewPartyCurrently —— 所有 vanilla
/// 巡逻 spawn 路径(DailyTickSettlement / OnNewGameCreated / OnBuildingLevelChanged)的总闸。
/// 前缀返回 false 表示跳过原方法;此时必须给出 __result 与 out 参数 reason。
/// </summary>
[HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "CanSettlementSpawnNewPartyCurrently")]
internal static class PatrolSpawnSuppressionPatch
{
    private static bool Prefix(Settlement settlement, ref bool __result, ref TextObject reason)
    {
        try
        {
            if (VanillaPatrolSuppressor.ShouldSuppressPatrolFor(settlement))
            {
                __result = false;
                reason = new TextObject(string.Empty);
                return false; // 跳过 vanilla 原方法
            }
        }
        catch (System.Exception ex)
        {
            Logger.Error("PatrolSpawnSuppressionPatch.Prefix failed", ex);
        }
        return true; // 继续执行 vanilla 原方法
    }
}
