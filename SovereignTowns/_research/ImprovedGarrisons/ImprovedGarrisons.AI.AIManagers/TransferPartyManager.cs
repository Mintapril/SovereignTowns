using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AIManagers;

public class TransferPartyManager
{
	public string GarrisonTransferPartyID { get; } = "garrisontransferparty_";


	internal Dictionary<MobileParty, Hero> TransferParties { get; } = new Dictionary<MobileParty, Hero>();


	public List<MobileParty> GetAllTransferParties()
	{
		List<MobileParty> list = new List<MobileParty>();
		foreach (MobileParty key in TransferParties.Keys)
		{
			list.Add(key);
		}
		return list;
	}

	public PartyBase CreateNewTransferParty(Settlement fromSettlement, Settlement transferTarget)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		try
		{
			string text = ((object)fromSettlement.Name).ToString();
			string id = GarrisonTransferPartyID + text;
			TextObject partyName = new TextObject(((object)new TextObject("{=party_transfer_name}Garrison transfer party of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + text, (Dictionary<string, object>)null);
			PartyBase val = Main.PartyManagement.InitializeNewParty(id, partyName, transferTarget, fromSettlement);
			val.MobileParty.Aggressiveness = 0f;
			TransferParties.Add(val.MobileParty, fromSettlement.Owner);
			return val;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public void ExecuteHourThinkBehavior()
	{
		foreach (MobileParty item in TransferParties.Keys.ToList())
		{
			if (item != null && item.HomeSettlement != null)
			{
				if (item.CurrentSettlement == item.HomeSettlement)
				{
					Main.PartyManagement.RecruitMobilePartyToGarrison(item, item.HomeSettlement);
					Main.PartyManagement.transferPartyManagement.TransferParties.Remove(item);
				}
				else
				{
					Settlement homeSettlement = item.HomeSettlement;
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(homeSettlement, item);
				}
				if (item.Food <= 100f)
				{
					Main.PartyManagement.GivePartyFood(item);
				}
			}
		}
	}

	public bool SettlementHasTransferParty(Settlement settlement)
	{
		foreach (MobileParty key in TransferParties.Keys)
		{
			Settlement transferPartyHome = GetTransferPartyHome(key);
			if (transferPartyHome != null && transferPartyHome == settlement)
			{
				return true;
			}
		}
		return false;
	}

	public Settlement GetTransferPartyHome(MobileParty party)
	{
		int num = ((MBObjectBase)party).StringId.IndexOf(GarrisonTransferPartyID);
		if (num < 0)
		{
			return null;
		}
		string text = ((MBObjectBase)party).StringId.Substring(num, ((MBObjectBase)party).StringId.Length - num).Replace(GarrisonTransferPartyID, "");
		int num2 = text.IndexOf('_');
		if (num2 > 0)
		{
			text = text.Substring(0, text.IndexOf('_'));
		}
		return Main.GarrisonBehavior.GetSettlementFromName(text);
	}

	public bool IsTransferParty(MobileParty party)
	{
		if (party != null && ((MBObjectBase)party).StringId != null)
		{
			return ((MBObjectBase)party).StringId.Contains(GarrisonTransferPartyID);
		}
		return false;
	}
}
