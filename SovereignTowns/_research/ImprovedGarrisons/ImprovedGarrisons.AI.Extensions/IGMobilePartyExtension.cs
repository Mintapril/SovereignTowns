using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.CampaignSystem.Party;

namespace ImprovedGarrisons.AI.Extensions;

public static class IGMobilePartyExtension
{
	public static MobileGarrison GetMobileGarrisonForParty(this MobileParty party)
	{
		try
		{
			if (party == null)
			{
				return null;
			}
			foreach (KeyValuePair<string, MobileGarrison> mobileGarrison in Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons)
			{
				if (mobileGarrison.Value.getMobileParty() == party)
				{
					return mobileGarrison.Value;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}
}
