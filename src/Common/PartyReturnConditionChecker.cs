using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem.Party;

namespace SovereignTowns.Common;

/// <summary>
/// 统一回城解散判定：本 mod 创建的非调拨部队（巡逻 / 征兵 / 主动出击）共用两个条件。
///   1. 当前实际兵员 / 出发时兵员 &lt; <see cref="PartyThresholds.PartyReturnSizeRatio"/>（默认 0.5）
///   2. 当前受伤兵员 / 当前全部兵员 &gt; <see cref="PartyThresholds.PartyReturnWoundedRatio"/>（默认 0.3）
/// 调拨队不应用本条件（兵员是货物，到目的地交付前不应中途解散）。
/// </summary>
public static class PartyReturnConditionChecker
{
    public enum ReturnReason
    {
        None = 0,
        SizeBelowRatio = 1,
        WoundedAboveRatio = 2,
    }

    /// <summary>
    /// 判定是否满足回城解散条件。
    /// </summary>
    /// <param name="party">本 mod 创建的部队。</param>
    /// <param name="initialMembers">出发时的兵员数。&lt;= 0 时跳过 size 检查（无基准）。</param>
    /// <param name="reason">命中的具体条件（None 表示未命中）。</param>
    /// <param name="detail">命中详情（用于日志，例如 "current=3/initial=15 ratio=0.20"）。</param>
    public static bool ShouldReturnAndDisband(MobileParty? party, int initialMembers, out ReturnReason reason, out string detail)
    {
        reason = ReturnReason.None;
        detail = "";
        if (party is null) return false;

        var roster = party.MemberRoster;
        int current = roster?.TotalManCount ?? 0;
        if (current <= 0)
        {
            reason = ReturnReason.SizeBelowRatio;
            detail = "current=0";
            return true;
        }

        float sizeRatio = ConfigurationManager.Current?.Thresholds?.PartyReturnSizeRatio ?? 0.5f;
        float woundedRatioThreshold = ConfigurationManager.Current?.Thresholds?.PartyReturnWoundedRatio ?? 0.3f;

        if (initialMembers > 0 && sizeRatio > 0f)
        {
            float ratio = (float)current / initialMembers;
            if (ratio < sizeRatio)
            {
                reason = ReturnReason.SizeBelowRatio;
                detail = $"current={current}/initial={initialMembers} ratio={ratio:F2} < {sizeRatio:F2}";
                return true;
            }
        }

        if (woundedRatioThreshold > 0f)
        {
            int wounded = SafeWoundedCount(roster);
            if (wounded > 0)
            {
                float wr = (float)wounded / current;
                if (wr > woundedRatioThreshold)
                {
                    reason = ReturnReason.WoundedAboveRatio;
                    detail = $"wounded={wounded}/current={current} ratio={wr:F2} > {woundedRatioThreshold:F2}";
                    return true;
                }
            }
        }

        return false;
    }

    private static int SafeWoundedCount(TaleWorlds.CampaignSystem.Roster.TroopRoster? roster)
    {
        if (roster is null) return 0;
        try { return roster.TotalWounded; }
        catch { return 0; }
    }
}
