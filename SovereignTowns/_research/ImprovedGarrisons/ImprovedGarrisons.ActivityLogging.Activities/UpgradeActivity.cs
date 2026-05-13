using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ActivityLogging.Activities;

[Serializable]
public class UpgradeActivity : GarrisonActivity
{
	private string previousCharacterNameWithLink;

	private string newCharacterNameWithLink;

	private int amount;

	public UpgradeActivity(CharacterObject previousCharacter, CharacterObject newCharacter, int amount)
	{
		previousCharacterNameWithLink = ((previousCharacter.EncyclopediaLinkWithName != (TextObject)null) ? ((object)previousCharacter.EncyclopediaLinkWithName).ToString() : ((object)((BasicCharacterObject)previousCharacter).Name).ToString());
		newCharacterNameWithLink = ((newCharacter.EncyclopediaLinkWithName != (TextObject)null) ? ((object)newCharacter.EncyclopediaLinkWithName).ToString() : ((object)((BasicCharacterObject)newCharacter).Name).ToString());
		this.amount = amount;
	}

	public override string GetLogDescription()
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		if (previousCharacterNameWithLink != null && newCharacterNameWithLink != null && amount > 0)
		{
			string result = previousCharacterNameWithLink + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_upgrade1}has been trained to", (Dictionary<string, object>)null)).ToString() + " " + newCharacterNameWithLink;
			if (amount > 1)
			{
				result = amount + " " + previousCharacterNameWithLink + " " + ((object)new TextObject("{=ui_improvedgarrisonsui_activity_upgrade2}have been trained to", (Dictionary<string, object>)null)).ToString() + " " + newCharacterNameWithLink;
			}
			return result;
		}
		return "";
	}
}
