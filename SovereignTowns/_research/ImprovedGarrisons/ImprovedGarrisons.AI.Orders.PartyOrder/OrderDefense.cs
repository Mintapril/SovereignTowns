using System;
using System.Collections.Generic;
using System.Reflection;
using Helpers;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.AI.Orders.PartyOrder;

public class OrderDefense : ImprovedPartyOrder
{
	private MobileParty currentTarget;

	private bool defendMessageSent = false;

	public Settlement SettlementToDefend { get; private set; }

	public OrderDefense(Settlement settlementToDefend)
	{
		SettlementToDefend = settlementToDefend;
	}

	public override void ExecuteOrder()
	{
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0420: Unknown result type (might be due to invalid IL or missing references)
		//IL_0228: Unknown result type (might be due to invalid IL or missing references)
		//IL_0232: Expected O, but got Unknown
		//IL_025e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0268: Expected O, but got Unknown
		//IL_0292: Unknown result type (might be due to invalid IL or missing references)
		//IL_029c: Expected O, but got Unknown
		//IL_02bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ca: Expected O, but got Unknown
		//IL_0318: Unknown result type (might be due to invalid IL or missing references)
		//IL_0322: Expected O, but got Unknown
		//IL_034e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0358: Expected O, but got Unknown
		//IL_0382: Unknown result type (might be due to invalid IL or missing references)
		//IL_038c: Expected O, but got Unknown
		//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ba: Expected O, but got Unknown
		try
		{
			base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
			if (SettlementToDefend.IsUnderRaid || SettlementToDefend.IsUnderSiege)
			{
				bool flag = default(bool);
				float num = default(float);
				DistanceHelper.FindClosestDistanceFromMobilePartyToSettlement(base.PartyToOrder.mobileParty, SettlementToDefend, base.PartyToOrder.mobileParty.NavigationCapability, ref flag, ref num);
				base.PartyToOrder.mobileParty.SetMoveDefendSettlement(SettlementToDefend, flag, base.PartyToOrder.mobileParty.NavigationCapability);
				base.PartyToOrder.mobileParty.Aggressiveness = 1f;
				base.PartyToOrder.HourCounter = 0;
				if (defendMessageSent)
				{
					return;
				}
				bool isVillage = SettlementToDefend.IsVillage;
				MobileParty val = null;
				if (isVillage)
				{
					if (SettlementToDefend.LastAttackerParty != null)
					{
						val = SettlementToDefend.LastAttackerParty;
					}
				}
				else
				{
					Settlement settlementToDefend = SettlementToDefend;
					object obj;
					if (settlementToDefend == null)
					{
						obj = null;
					}
					else
					{
						SiegeEvent siegeEvent = settlementToDefend.SiegeEvent;
						if (siegeEvent == null)
						{
							obj = null;
						}
						else
						{
							BesiegerCamp besiegerCamp = siegeEvent.BesiegerCamp;
							obj = ((besiegerCamp != null) ? besiegerCamp.LeaderParty : null);
						}
					}
					if (obj != null)
					{
						val = SettlementToDefend.SiegeEvent.BesiegerCamp.LeaderParty;
					}
				}
				if (val != null && !base.PartyToOrder.isNPC)
				{
					float partyStrength = base.PartyToOrder.GetPartyStrength(val);
					float num2 = val.MemberRoster.TotalManCount;
					if (val.Army != null)
					{
						partyStrength = val.Army.EstimatedStrength;
						num2 = val.Army.TotalManCount;
					}
					if (base.PartyToOrder.CanDeafeat(partyStrength, base.PartyToOrder.mobileParty) && !base.PartyToOrder.isNPC)
					{
						InformationManager.DisplayMessage(new InformationMessage(((object)base.PartyToOrder.mobileParty.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_defending1}are defending", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)SettlementToDefend.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_defending2}against", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)val.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_defending3}of size", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + num2, Color.FromUint(ModuleColors.modMainColor)));
					}
					else if (!base.PartyToOrder.isNPC)
					{
						InformationManager.DisplayMessage(new InformationMessage(((object)base.PartyToOrder.mobileParty.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_needshelpdefending1}needs help defending", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)SettlementToDefend.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_needshelpdefending2}against", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)val.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_needshelpdefending3}of size", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + num2, Color.FromUint(ModuleColors.yellow)));
					}
					defendMessageSent = true;
				}
			}
			else if (currentTarget != null && base.PartyToOrder.HourCounter % 6 != 0)
			{
				base.PartyToOrder.mobileParty.Aggressiveness = 0.9f;
				base.PartyToOrder.mobileParty.SetMoveEngageParty(currentTarget, base.PartyToOrder.mobileParty.NavigationCapability);
			}
			else
			{
				SetOrderToFinished();
				base.PartyToOrder.mobileParty.Aggressiveness = 0.9f;
				defendMessageSent = false;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override string GetStatusText()
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Expected O, but got Unknown
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Expected O, but got Unknown
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Expected O, but got Unknown
		string text = "";
		if (SettlementToDefend != null)
		{
			if (SettlementToDefend.EncyclopediaLinkWithName != (TextObject)null)
			{
				return ((object)new TextObject("{=menu_guard_status_isdefending}The guard party is defending" + ModuleStrings._space + (object)SettlementToDefend.EncyclopediaLinkWithName, (Dictionary<string, object>)null)).ToString();
			}
			return ((object)new TextObject("{=menu_guard_status_isdefending}The guard party is defending" + ModuleStrings._space + (object)SettlementToDefend.Name, (Dictionary<string, object>)null)).ToString();
		}
		return ((object)new TextObject("{=menu_guard_status_isdefending}The guard party is defending", (Dictionary<string, object>)null)).ToString();
	}
}
