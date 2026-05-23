using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Configuration;

/// <summary>
/// Converts ratio-based settings into runtime troop counts using the real garrison roster.
/// Town.GarrisonParty does not include militia, so these helpers intentionally ignore militia.
/// </summary>
public static class GarrisonThresholdMath
{
    public static int ActualGarrisonCount(Town? town)
        => town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;

    public static int ActualGarrisonCount(Settlement? settlement)
        => ActualGarrisonCount(settlement?.Town);

    public static int CountFromRatio(Town? town, float ratio, int minimumWhenPositive = 1)
        => CountFromRatio(ActualGarrisonCount(town), ratio, minimumWhenPositive);

    public static int CountFromRatio(Settlement? settlement, float ratio, int minimumWhenPositive = 1)
        => CountFromRatio(ActualGarrisonCount(settlement), ratio, minimumWhenPositive);

    public static int CountFromRatio(int baseCount, float ratio, int minimumWhenPositive = 1)
    {
        if (baseCount <= 0 || ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
            return 0;

        int count = (int)Math.Round(baseCount * ratio);
        if (minimumWhenPositive > 0)
            count = Math.Max(minimumWhenPositive, count);

        return Math.Max(0, count);
    }
}
