using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.AI.Orders.PartyOrder;

public class OrderEscort : ImprovedPartyOrder
{
	private MobileParty escortParty;

	public OrderEscort(MobileParty partyToEscort)
	{
		if (partyToEscort != null)
		{
			escortParty = partyToEscort;
		}
		else
		{
			SetOrderToFinished();
		}
	}

	public override void ExecuteOrder()
	{
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(true);
			if (escortParty != null && escortParty.IsActive)
			{
				base.PartyToOrder.mobileParty.SetMoveEscortParty(escortParty, base.PartyToOrder.mobileParty.NavigationCapability, escortParty.IsTargetingPort);
			}
			else if (escortParty != null && escortParty.Party.Owner != null)
			{
				base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
				Hero owner = escortParty.Party.Owner;
				PartyBase partyBelongedToAsPrisoner = owner.PartyBelongedToAsPrisoner;
				if (partyBelongedToAsPrisoner != null)
				{
					base.PartyToOrder.mobileParty.SetMoveEngageParty(partyBelongedToAsPrisoner.MobileParty, base.PartyToOrder.mobileParty.NavigationCapability);
				}
			}
			else
			{
				SetOrderToFinished();
			}
			base.PartyToOrder.mobileParty.Aggressiveness = 0.4f;
			if (escortParty != null && !escortParty.IsActive && escortParty.IsLordParty)
			{
				if (escortParty != null && escortParty.Ai.AiBehaviorPartyBase != null)
				{
					MobileParty mobileParty = escortParty.Ai.AiBehaviorPartyBase.MobileParty;
					base.PartyToOrder.mobileParty.SetMoveEngageParty(mobileParty, base.PartyToOrder.mobileParty.NavigationCapability);
					base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
				}
			}
			else if (escortParty != null && !escortParty.IsActive)
			{
				SetOrderToFinished();
				base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override string GetStatusText()
	{
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Expected O, but got Unknown
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		string text = "";
		if (escortParty != null && escortParty.Name != (TextObject)null)
		{
			return ((object)new TextObject("{=menu_guard_status_issupporting}The guard party is escorting" + ModuleStrings._space + (object)escortParty.Name, (Dictionary<string, object>)null)).ToString();
		}
		return ((object)new TextObject("{=menu_guard_status_issupporting}The guard party is escorting", (Dictionary<string, object>)null)).ToString();
	}
}
