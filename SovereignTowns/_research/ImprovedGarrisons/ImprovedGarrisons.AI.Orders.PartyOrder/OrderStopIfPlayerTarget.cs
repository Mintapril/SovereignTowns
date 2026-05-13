using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.AI.Orders.PartyOrder;

public class OrderStopIfPlayerTarget : ImprovedPartyOrder
{
	public override void ExecuteOrder()
	{
		base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(true);
		if (Hero.MainHero.PartyBelongedTo != null)
		{
			MobileParty targetParty = Hero.MainHero.PartyBelongedTo.TargetParty;
			if (targetParty != null && targetParty == base.PartyToOrder.mobileParty)
			{
				base.PartyToOrder.mobileParty.SetMoveModeHold();
				return;
			}
			base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
			SetOrderToFinished();
		}
		else
		{
			base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
			SetOrderToFinished();
		}
	}

	public override string GetStatusText()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		return ((object)new TextObject("{=menu_guard_status_iswaiting}The guard party is waiting", (Dictionary<string, object>)null)).ToString();
	}
}
