using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.Models;

public class GarrisonpartySizeLimitModel : DefaultPartySizeLimitModel
{
	public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions = false)
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0321: Unknown result type (might be due to invalid IL or missing references)
		//IL_0322: Unknown result type (might be due to invalid IL or missing references)
		//IL_0326: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Expected O, but got Unknown
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_0307: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0304: Expected O, but got Unknown
		//IL_02ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0304: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Expected O, but got Unknown
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Expected O, but got Unknown
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_023a: Expected O, but got Unknown
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Expected O, but got Unknown
		ExplainedNumber result = ((DefaultPartySizeLimitModel)this).GetPartyMemberSizeLimit(party, includeDescriptions);
		try
		{
			bool flag = Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(party.MobileParty);
			bool flag2 = Main.PartyManagement.transferPartyManagement.IsTransferParty(party.MobileParty);
			bool flag3 = Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(party.MobileParty);
			if (flag || flag2 || flag3)
			{
				return result = new ExplainedNumber((float)ConfigManager.Instance.Config.CustomTransferAndGuardPartySize, true, new TextObject("[IG-cheats] custom party size", (Dictionary<string, object>)null));
			}
			double num = ConfigManager.Instance.Config.CustomGarrisonSizeMultiplier;
			double num2 = ConfigManager.Instance.Config.MainPartySizeMultiplier;
			double num3 = ConfigManager.Instance.Config.PlayerClanPartySizeMultiplier;
			double num4 = ConfigManager.Instance.Config.AIClanPartySizeMultiplier;
			if (party != null && party.MobileParty != null)
			{
				if (party.MobileParty.IsGarrison && num > 0.0)
				{
					float num5 = (float)((double)((ExplainedNumber)(ref result)).ResultNumber * num - (double)((ExplainedNumber)(ref result)).ResultNumber);
					if (num5 > 1f)
					{
						((ExplainedNumber)(ref result)).Add(num5, new TextObject("[IG-cheats] custom garrison size", (Dictionary<string, object>)null), (TextObject)null);
					}
				}
				if (party.MobileParty == MobileParty.MainParty && num2 > 0.0)
				{
					float num6 = (float)((double)((ExplainedNumber)(ref result)).ResultNumber * num2 - (double)((ExplainedNumber)(ref result)).ResultNumber);
					if (num6 > 1f)
					{
						((ExplainedNumber)(ref result)).Add(num6, new TextObject("[IG-cheats] custom player party size", (Dictionary<string, object>)null), (TextObject)null);
					}
				}
				if (party.MobileParty != MobileParty.MainParty && party.MobileParty.ActualClan != null && MobileParty.MainParty.ActualClan != null && party.MobileParty.ActualClan == MobileParty.MainParty.ActualClan && num3 > 0.0)
				{
					float num7 = (float)((double)((ExplainedNumber)(ref result)).ResultNumber * num3 - (double)((ExplainedNumber)(ref result)).ResultNumber);
					if (num7 > 1f)
					{
						((ExplainedNumber)(ref result)).Add(num7, new TextObject("[IG-cheats] custom player clan party size", (Dictionary<string, object>)null), (TextObject)null);
					}
				}
				if (party.MobileParty.ActualClan != null && MobileParty.MainParty.ActualClan != null && party.MobileParty.ActualClan != MobileParty.MainParty.ActualClan && num3 > 0.0)
				{
					float num8 = (float)((double)((ExplainedNumber)(ref result)).ResultNumber * num3 - (double)((ExplainedNumber)(ref result)).ResultNumber);
					if (num8 > 1f)
					{
						((ExplainedNumber)(ref result)).Add(num8, new TextObject("[IG-cheats] custom ai clan party size", (Dictionary<string, object>)null), (TextObject)null);
					}
				}
			}
			if (party != null && party.MobileParty != null && ((MBObjectBase)party.MobileParty).StringId == "improvedgarrisons_template_party")
			{
				result = new ExplainedNumber(100000f, true, new TextObject("Improved Garrison template party size", (Dictionary<string, object>)null));
			}
			return result;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return result;
		}
	}
}
