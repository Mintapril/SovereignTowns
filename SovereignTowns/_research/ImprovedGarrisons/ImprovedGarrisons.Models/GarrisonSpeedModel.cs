using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.Models;

internal class GarrisonSpeedModel : DefaultPartySpeedCalculatingModel
{
	public override ExplainedNumber CalculateFinalSpeed(MobileParty mobileParty, ExplainedNumber finalSpeed)
	{
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Expected O, but got Unknown
		try
		{
			ExplainedNumber result = ((DefaultPartySpeedCalculatingModel)this).CalculateFinalSpeed(mobileParty, finalSpeed);
			double num = ConfigManager.Instance.Config.CustomGuardAndTransferPartySpeed;
			if (mobileParty != null && num > 0.0 && (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(mobileParty) || Main.PartyManagement.transferPartyManagement.IsTransferParty(mobileParty) || Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(mobileParty)))
			{
				((ExplainedNumber)(ref result))._002Ector((float)num, true, new TextObject("Improved Garrisons speed cheat", (Dictionary<string, object>)null));
			}
			return result;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return default(ExplainedNumber);
		}
	}
}
