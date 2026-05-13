using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ActivityLogging.Activities;

[Serializable]
public class PartyMergeWithGarrisonActivity : GarrisonActivity
{
	private string partyName;

	private int size = 0;

	public PartyMergeWithGarrisonActivity(MobileParty party)
	{
		partyName = ((object)party.ArmyName).ToString();
		size = party.MemberRoster.TotalManCount;
	}

	public override string GetLogDescription()
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Expected O, but got Unknown
		string result = "";
		if (partyName != null)
		{
			result = "<a style=\"Link.Hero\">" + partyName + " </b></a> " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_partymerge1}has returned with", (Dictionary<string, object>)null)).ToString() + " " + size + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_partymerge2}troops to the garrison.", (Dictionary<string, object>)null)).ToString();
		}
		return result;
	}
}
