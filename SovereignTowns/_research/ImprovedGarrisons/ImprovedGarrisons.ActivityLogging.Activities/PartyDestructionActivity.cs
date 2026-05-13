using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.ActivityLogging.Activities;

[Serializable]
public class PartyDestructionActivity : GarrisonActivity
{
	private string partyName;

	public PartyDestructionActivity(MobileParty party)
	{
		partyName = ((object)((MBObjectBase)party).GetName()).ToString();
	}

	public override string GetLogDescription()
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		string result = "";
		if (partyName != null)
		{
			result = partyName + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_destroyed1}has been destroyed.", (Dictionary<string, object>)null)).ToString();
		}
		return result;
	}
}
