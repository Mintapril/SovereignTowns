using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ActivityLogging.Activities;

[Serializable]
public class RecruitmentActivity : GarrisonActivity
{
	private string settlementNameWithLink;

	private string prisonerNameWithLink;

	private int amount;

	public RecruitmentActivity(int amount, Settlement recruitedFrom = null)
	{
		this.amount = amount;
		if (recruitedFrom != null)
		{
			settlementNameWithLink = ((recruitedFrom.EncyclopediaLinkWithName != (TextObject)null) ? ((object)recruitedFrom.EncyclopediaLinkWithName).ToString() : ((object)recruitedFrom.Name).ToString());
		}
	}

	public RecruitmentActivity(int amount, CharacterObject prisoner)
	{
		if (prisoner != null)
		{
			this.amount = amount;
			prisonerNameWithLink = ((prisoner.EncyclopediaLinkWithName != (TextObject)null) ? ((object)prisoner.EncyclopediaLinkWithName).ToString() : ((object)((BasicCharacterObject)prisoner).Name).ToString());
		}
	}

	public override string GetLogDescription()
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Expected O, but got Unknown
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Expected O, but got Unknown
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Expected O, but got Unknown
		string text = "";
		if (amount > 0 && settlementNameWithLink != null)
		{
			return amount + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_recruitment1}troops have been recruited from", (Dictionary<string, object>)null)).ToString() + " " + settlementNameWithLink + ".";
		}
		if (prisonerNameWithLink != null && amount == 1)
		{
			return prisonerNameWithLink + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_recruitment2}has been recruited from the dungeon.", (Dictionary<string, object>)null)).ToString();
		}
		if (prisonerNameWithLink != null && amount > 0)
		{
			return amount + " " + prisonerNameWithLink + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_recruitment3}have been recruited from the dungeon.", (Dictionary<string, object>)null)).ToString();
		}
		return amount + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_recruitment4}troops have been recruited.", (Dictionary<string, object>)null)).ToString();
	}
}
