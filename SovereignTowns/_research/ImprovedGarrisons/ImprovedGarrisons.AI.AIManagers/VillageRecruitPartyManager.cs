using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AIManagers;

public class VillageRecruitPartyManager
{
	public string VillageRecruitPartyID { get; } = "improvedgarrison_recruit_";


	internal HashSet<MobileParty> VillageRecruitParties { get; } = new HashSet<MobileParty>();


	public List<MobileParty> GetAllVillageRecruitParties()
	{
		return VillageRecruitParties.ToList();
	}

	public void ExecuteHourThinkBehavior()
	{
		foreach (MobileParty item in VillageRecruitParties.ToList())
		{
			if (item != null && item.HomeSettlement != null)
			{
				if (item.CurrentSettlement == item.HomeSettlement)
				{
					Main.PartyManagement.RecruitMobilePartyToGarrison(item, item.HomeSettlement);
					Main.PartyManagement.villageRecruitPartyManagement.VillageRecruitParties.Remove(item);
				}
				else
				{
					Settlement homeSettlement = item.HomeSettlement;
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(homeSettlement, item);
				}
			}
		}
	}

	public PartyBase GetMobilePartyFromVillage(Village village)
	{
		return Main.PartyManagement.GetPartyBaseById(VillageRecruitPartyID + ((MBObjectBase)((SettlementComponent)village).Settlement).StringId);
	}

	public Settlement GetVillageFromMobileParty(MobileParty party)
	{
		int num = ((MBObjectBase)party).StringId.IndexOf(VillageRecruitPartyID);
		if (num < 0)
		{
			return null;
		}
		string text = ((MBObjectBase)party).StringId.Substring(num, ((MBObjectBase)party).StringId.Length - num).Replace(VillageRecruitPartyID, "");
		int num2 = text.IndexOf('_');
		if (num2 > 0)
		{
			text = text.Substring(0, text.IndexOf('_'));
		}
		text = Regex.Replace(text, "[\\d-]", string.Empty);
		return Main.GarrisonBehavior.GetSettlementFromName(text);
	}

	public PartyBase InitializeVillageRecruitParty(TextObject partyName, Settlement homeTown, Settlement spawnOn)
	{
		try
		{
			if (homeTown != null && homeTown.Owner != null && homeTown.Owner != null)
			{
				Hero owner = homeTown.Owner;
				string id = VillageRecruitPartyID + (object)spawnOn.Name;
				PartyBase partyBaseById = Main.PartyManagement.GetPartyBaseById(id);
				if (partyBaseById == null)
				{
					MobileParty mobileParty = Main.PartyManagement.InitializeNewParty(id, partyName, homeTown, spawnOn).MobileParty;
					mobileParty.Aggressiveness = 0f;
					mobileParty.Ai.SetInitiative(0f, 0.5f, float.MaxValue);
					VillageRecruitParties.Add(mobileParty);
					return mobileParty.Party;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public bool IsImprovedGarrisonVillageRecruitParty(MobileParty party)
	{
		if (party != null && ((MBObjectBase)party).StringId != null)
		{
			return ((MBObjectBase)party).StringId.Contains(VillageRecruitPartyID);
		}
		return false;
	}
}
