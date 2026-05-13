using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ActivityLogging.Activities;

[Serializable]
public class PartyCreationActivity : GarrisonActivity
{
	private string partyName;

	private int size = 0;

	public PartyCreationActivity(MobileParty party)
	{
		partyName = ((object)party.ArmyName).ToString();
		size = party.MemberRoster.TotalManCount;
	}

	public override string GetLogDescription()
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Expected O, but got Unknown
		string result = "";
		if (partyName != null)
		{
			result = ((object)new TextObject("{=ui_improvedgarrisonsui_activity_partycreation1}A new party", (Dictionary<string, object>)null)).ToString() + " <a style=\"Link.Hero\">" + partyName + " </b></a> " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_partycreation2}has been created with", (Dictionary<string, object>)null)).ToString() + " " + size + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_partycreation3}troops.", (Dictionary<string, object>)null)).ToString();
		}
		return result;
	}
}
