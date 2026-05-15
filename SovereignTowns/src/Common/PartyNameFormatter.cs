using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Common;

/// <summary>
/// MobileParty / Settlement 的 null-safe 名称与计数读取。
/// 在 log 与 telemetry 中用于产出可读字符串，绝不抛异常。
/// </summary>
public static class PartyNameFormatter
{
    public static string SafeName(MobileParty? party)
    {
        if (party == null) return "(null party)";
        try { return party.Name?.ToString() ?? "(unnamed)"; }
        catch { return "(name error)"; }
    }

    public static string SafeName(Settlement? settlement)
    {
        if (settlement == null) return "(null settlement)";
        try { return settlement.Name?.ToString() ?? "(unnamed)"; }
        catch { return "(name error)"; }
    }

    public static int SafeMemberCount(MobileParty? party)
    {
        if (party == null) return 0;
        try { return party.MemberRoster?.TotalManCount ?? 0; }
        catch { return 0; }
    }
}
