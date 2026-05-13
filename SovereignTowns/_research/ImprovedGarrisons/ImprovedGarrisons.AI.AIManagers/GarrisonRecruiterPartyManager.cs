using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AIManagers;

public class GarrisonRecruiterPartyManager
{
	public string GarrisonRecruiterPartyID { get; } = "improvedgarrison_recruiter_";


	internal Dictionary<MobileParty, GarrisonRecruiter> GarrisonRecruiterParties { get; } = new Dictionary<MobileParty, GarrisonRecruiter>();


	public List<MobileParty> GetAllRecruiters()
	{
		List<MobileParty> list = new List<MobileParty>();
		foreach (MobileParty key in GarrisonRecruiterParties.Keys)
		{
			list.Add(key);
		}
		return list;
	}

	public PartyBase CreateGarrisonRecruiterParty(Settlement forSettlement, Settlement spawnSettlement, bool autoAddTroop = false)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		try
		{
			string text = ((object)forSettlement.Name).ToString();
			string id = GenerateRecruiterId(forSettlement.Town);
			TextObject partyName = new TextObject(((object)new TextObject("{=party_recruiter_name}Garrison recruiter of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + text, (Dictionary<string, object>)null);
			PartyBase val = Main.PartyManagement.InitializeNewParty(id, partyName, forSettlement, spawnSettlement);
			if (val != null)
			{
				GarrisonRecruiter garrisonRecruiter = new GarrisonRecruiter(val.MobileParty, forSettlement);
				val.MobileParty.Aggressiveness = 0f;
				if (autoAddTroop)
				{
					List<Tuple<CharacterObject, int>> bestRecruiterUnits = GetBestRecruiterUnits(forSettlement, 15);
					if (bestRecruiterUnits == null || bestRecruiterUnits.Count <= 0)
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(val.MobileParty);
						return null;
					}
					Main.GarrisonPartyBehavior.TransferTroopsFromPartyToParty(((Fief)forSettlement.Town).GarrisonParty, bestRecruiterUnits, val);
					garrisonRecruiter.SetInitialSize();
				}
				GarrisonRecruiterParties.Add(val.MobileParty, garrisonRecruiter);
			}
			return val;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public void GiveMobilePartyARecruiter(MobileParty party)
	{
		try
		{
			if (party != null && IsRecruiterParty(party))
			{
				Settlement recruiterPartyHome = GetRecruiterPartyHome(party);
				GarrisonRecruiterParties.Add(party, new GarrisonRecruiter(party, recruiterPartyHome));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ExecutePartialHourlyBehavior()
	{
		try
		{
			foreach (GarrisonRecruiter value in GarrisonRecruiterParties.Values)
			{
				value.PartialHourlyThinkBehavior();
			}
		}
		catch (Exception)
		{
		}
	}

	public void ExecuteHourThinkBehaviorForAll()
	{
		try
		{
			foreach (GarrisonRecruiter value in GarrisonRecruiterParties.Values)
			{
				value.NextHour();
				value.HourlyThinkBehavior();
				value.RethinkNextHour = true;
			}
		}
		catch (Exception)
		{
		}
	}

	private string GenerateRecruiterId(Town town)
	{
		if (town == null)
		{
			return null;
		}
		int num = 0;
		foreach (GarrisonRecruiter value in GarrisonRecruiterParties.Values)
		{
			Settlement fromSettlement = value.fromSettlement;
			if (fromSettlement != null && fromSettlement.Town == town)
			{
				num++;
			}
		}
		return Main.PartyManagement.garrisonRecruiterPartyManagement.GarrisonRecruiterPartyID + ((object)((SettlementComponent)town).Name)?.ToString() + "_" + num;
	}

	private List<Tuple<CharacterObject, int>> GetBestRecruiterUnits(Settlement settlement, int amount)
	{
		if (settlement == null || settlement.Town == null || ((Fief)settlement.Town).GarrisonParty == null)
		{
			return null;
		}
		return Main.GarrisonBehavior.GetLowestTierUnitsByAmount(amount, settlement.Town);
	}

	public bool SettlementHasARecruiter(Settlement settlement)
	{
		GarrisonRecruiter recruiterOfSettlement = GetRecruiterOfSettlement(settlement);
		return recruiterOfSettlement != null;
	}

	public bool IsRecruiterParty(MobileParty party)
	{
		if (party != null && ((MBObjectBase)party).StringId != null)
		{
			return ((MBObjectBase)party).StringId.Contains(GarrisonRecruiterPartyID);
		}
		return false;
	}

	public Settlement GetRecruiterPartyHome(MobileParty party)
	{
		int num = ((MBObjectBase)party).StringId.IndexOf(GarrisonRecruiterPartyID);
		if (num < 0)
		{
			return null;
		}
		string text = ((MBObjectBase)party).StringId.Substring(num, ((MBObjectBase)party).StringId.Length - num).Replace(GarrisonRecruiterPartyID, "");
		int num2 = text.IndexOf('_');
		if (num2 > 0)
		{
			text = text.Substring(0, text.IndexOf('_'));
		}
		return Main.GarrisonBehavior.GetSettlementFromName(text);
	}

	public GarrisonRecruiter GetRecruiterForParty(MobileParty party)
	{
		try
		{
			foreach (KeyValuePair<MobileParty, GarrisonRecruiter> garrisonRecruiterParty in GarrisonRecruiterParties)
			{
				if (((MBObjectBase)garrisonRecruiterParty.Key).StringId == ((MBObjectBase)party).StringId)
				{
					return garrisonRecruiterParty.Value;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public GarrisonRecruiter GetRecruiterOfSettlement(Settlement settlement)
	{
		List<GarrisonRecruiter> list = new List<GarrisonRecruiter>();
		foreach (GarrisonRecruiter value in GarrisonRecruiterParties.Values)
		{
			if (value.fromSettlement == settlement)
			{
				list.Add(value);
			}
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.First();
	}

	public void PromptCultureSelection(Action<List<InquiryElement>> positiveAction)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Expected O, but got Unknown
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Expected O, but got Unknown
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Expected O, but got Unknown
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Expected O, but got Unknown
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Expected O, but got Unknown
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected O, but got Unknown
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Expected O, but got Unknown
		try
		{
			List<InquiryElement> list = new List<InquiryElement>();
			List<CultureObject> allCultures = GetAllCultures();
			if (allCultures == null)
			{
				return;
			}
			list.Add(new InquiryElement((object)null, "Any", (ImageIdentifier)new EmptyImageIdentifier()));
			foreach (CultureObject item in allCultures)
			{
				ImageIdentifier val = null;
				foreach (Kingdom item2 in (List<Kingdom>)(object)Kingdom.All)
				{
					if (item2.Culture != null && item2.Culture == item)
					{
						val = (ImageIdentifier)new BannerImageIdentifier(item2.Banner, false);
						break;
					}
				}
				if (val == null)
				{
					val = (ImageIdentifier)new EmptyImageIdentifier();
				}
				list.Add(new InquiryElement((object)item, ((MBObjectBase)item).StringId, val));
			}
			MultiSelectionInquiryData val2 = new MultiSelectionInquiryData(((object)new TextObject("{=settings_recruitmentsettings_recruiterculture1}Recruiter recruitment culture", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_recruitmentsettings_recruiterculture2}Which culture should the recruiter party recruit from?", (Dictionary<string, object>)null)).ToString(), list, true, 1, 1, ((object)new TextObject("{=menu_ok}Ok", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), positiveAction, (Action<List<InquiryElement>>)null, "", false);
			MBInformationManager.ShowMultiSelectionInquiry(val2, false, false);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public List<CultureObject> GetAllCultures()
	{
		try
		{
			HashSet<CultureObject> hashSet = new HashSet<CultureObject>();
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if (item.Culture != null && !((BasicCultureObject)item.Culture).IsBandit)
				{
					hashSet.Add(item.Culture);
				}
			}
			return hashSet.ToList();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}
}
