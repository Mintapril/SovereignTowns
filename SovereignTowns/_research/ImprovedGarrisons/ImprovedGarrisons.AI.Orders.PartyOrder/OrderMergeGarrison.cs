using System.Collections.Generic;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.AI.Orders.PartyOrder;

public class OrderMergeGarrison : ImprovedPartyOrder
{
	private Settlement settlement;

	public readonly bool isReturning;

	public OrderMergeGarrison(Settlement settlementToMergeWith, bool isReturning = false)
	{
		settlement = settlementToMergeWith;
		this.isReturning = isReturning;
	}

	public override void ExecuteOrder()
	{
		if (base.PartyToOrder.mobileParty.CurrentSettlement != null && base.PartyToOrder.mobileParty.CurrentSettlement == settlement)
		{
			Main.PartyManagement.RecruitMobilePartyToGarrison(base.PartyToOrder.mobileParty, settlement);
			return;
		}
		Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(settlement, base.PartyToOrder.mobileParty);
		base.PartyToOrder.mobileParty.Aggressiveness = 0f;
	}

	public override string GetStatusText()
	{
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Expected O, but got Unknown
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Expected O, but got Unknown
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected O, but got Unknown
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Expected O, but got Unknown
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Expected O, but got Unknown
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		string result = "";
		if (!isReturning)
		{
			result = ((settlement == null) ? ((object)new TextObject("{=menu_guard_status_isfortifyinThe guard party is reinforcingng", (Dictionary<string, object>)null)).ToString() : ((!(settlement.EncyclopediaLinkWithName != (TextObject)null)) ? ((object)new TextObject("{=menu_guard_status_isfortifyingThe guard party is reinforcingg" + ModuleStrings._space + (object)settlement.Name, (Dictionary<string, object>)null)).ToString() : ((object)new TextObject("{=menu_guard_status_isfortifying}The guard party is reinforcing" + ModuleStrings._space + (object)settlement.EncyclopediaLinkWithName, (Dictionary<string, object>)null)).ToString()));
		}
		else if (isReturning)
		{
			result = ((settlement == null) ? ((object)new TextObject("{=menu_guard_status_returning}The guard party is returning", (Dictionary<string, object>)null)).ToString() : ((!(settlement.EncyclopediaLinkWithName != (TextObject)null)) ? ((object)new TextObject("{=menu_guard_status_returningto}The guard party is returning to" + ModuleStrings._space + (object)settlement.Name, (Dictionary<string, object>)null)).ToString() : ((object)new TextObject("{=menu_guard_status_returningto}The guard party is returning to" + ModuleStrings._space + (object)settlement.EncyclopediaLinkWithName, (Dictionary<string, object>)null)).ToString()));
		}
		return result;
	}
}
