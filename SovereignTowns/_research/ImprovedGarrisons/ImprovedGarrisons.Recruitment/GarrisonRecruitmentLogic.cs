using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.Recruitment;

public class GarrisonRecruitmentLogic
{
	private Dictionary<Town, Dictionary<CharacterObject, int>> prisonersPerTown = new Dictionary<Town, Dictionary<CharacterObject, int>>();

	public void CheatSpawnUnitInAllGarrisons(int amount)
	{
		try
		{
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if (item.IsCastle || item.IsTown)
				{
					CharacterObject character = (ConfigManager.Instance.Config.SpawnOnlyNobleTroops ? item.Town.Culture.EliteBasicTroop : item.Town.Culture.BasicTroop);
					CheatSpawnUnitInGarrison(character, amount, item);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public bool CheatSpawnUnitInGarrison(CharacterObject character, int amount, Settlement settlement)
	{
		bool flag = character != null && settlement != null;
		if ((settlement.IsCastle || settlement.IsTown) && flag)
		{
			if (((BasicCharacterObject)character).IsHero)
			{
				return false;
			}
			try
			{
				GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
				if (townSettings.EnableRecruitFromRegion)
				{
					int num = 1;
					int num2 = 0;
					if (((Fief)settlement.Town).GarrisonParty == null)
					{
						settlement.AddGarrisonParty();
					}
					else
					{
						num = Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
						num2 = ((Fief)settlement.Town).GarrisonParty.MemberRoster.TotalManCount;
						if (num2 + amount > num)
						{
							amount = num - num2;
						}
						if (num2 + amount > townSettings.MaxRecruitThreshold)
						{
							amount = townSettings.MaxRecruitThreshold - num2;
						}
					}
					if (IsGarrisonRecruitmentAllowed(settlement) && amount > 0)
					{
						((Fief)settlement.Town).GarrisonParty.MemberRoster.AddToCounts(character, amount, false, 0, 0, true, -1);
						Main.ActivityLogManager.AddUnitRecruitmentCost(settlement.Town, character, amount, settlement.Owner);
						Main.ActivityLogManager.AddNewRecruits(settlement.Town, amount);
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
				return false;
			}
		}
		return false;
	}

	public bool RecruitPrisonerToGarrison(CharacterObject character, int amount, Settlement settlement)
	{
		bool flag = character != null && settlement != null;
		if ((settlement.IsCastle || settlement.IsTown) && flag)
		{
			if (((BasicCharacterObject)character).IsHero)
			{
				return false;
			}
			try
			{
				GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
				if (townSettings.EnablePrisonerRecruitment)
				{
					int num = 1;
					int num2 = 0;
					if (((Fief)settlement.Town).GarrisonParty == null)
					{
						settlement.AddGarrisonParty();
					}
					else
					{
						num = Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
						num2 = Main.GarrisonBehavior.GetAutomatedGarrisonForSettlement(settlement).GetGarrisonSizeWithRecruiter();
						if (num2 + amount > num)
						{
							amount = num - num2;
						}
					}
					if (IsGarrisonRecruitmentAllowed(settlement) && amount > 0)
					{
						((Fief)settlement.Town).GarrisonParty.MemberRoster.AddToCounts(character, amount, false, 0, 0, true, -1);
						if (settlement.Owner != null && settlement.Owner == Hero.MainHero)
						{
							Main.ActivityLogManager.AddNewPrisonerTurnover(settlement.Town, character, amount);
						}
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
				return false;
			}
		}
		return false;
	}

	public void RecruitSurroundingForAllSettlements()
	{
		try
		{
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if ((item.IsCastle || item.IsTown) && IsGarrisonRecruitmentAllowed(item))
				{
					RecruitFromSurroundingVillages(item);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void RecruitFromSurroundingVillages(Settlement settlement)
	{
		try
		{
			if (settlement == null)
			{
				return;
			}
			GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
			if (!townSettings.EnableRecruitFromRegion)
			{
				return;
			}
			int num = ConfigManager.Instance.Config.MinRecruitmentAmountFromVillages;
			int num2 = townSettings.MaxRecruitThreshold;
			int num3 = 1;
			int num4 = 0;
			if (((Fief)settlement.Town).GarrisonParty == null)
			{
				settlement.AddGarrisonParty();
			}
			else
			{
				num3 = Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
				num4 = ((Fief)settlement.Town).GarrisonParty.MemberRoster.TotalManCount;
				if (num4 + num > num3)
				{
					num = num3 - num4;
				}
				if (num4 + num > num2)
				{
					num = num2 - num4;
				}
				num2 -= num4;
			}
			bool flag = num3 > num4;
			if (!townSettings.RecruitmentFollowsTemplate && flag && num > 0)
			{
				GetAndMoveSurroundRecruitsToMainSettlement(settlement, num, num2);
			}
			else if (townSettings.RecruitmentFollowsTemplate && flag)
			{
				GetAndMoveSurroundTemplateRecruitsToMainSettlement(settlement, 1);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void GetAndMoveSurroundRecruitsToMainSettlement(Settlement settlement, int minAmountPerVillage, int maxAmountToRecruit)
	{
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Expected O, but got Unknown
		if (!settlement.IsTown && !settlement.IsCastle)
		{
			return;
		}
		GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
		bool recruitOnlyEliteUnits = townSettings.RecruitOnlyEliteUnits;
		try
		{
			List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = GetAllRecruitableRecruitsFromSettlement(settlement, maxAmountToRecruit, settlement.Owner, recruitOnlyEliteUnits);
			if (allRecruitableRecruitsFromSettlement.Count >= minAmountPerVillage || recruitOnlyEliteUnits)
			{
				int num = 0;
				foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
				{
					bool flag = item.Item2 != null;
					bool flag2 = maxAmountToRecruit > 0;
					if (flag && flag2 && RecruitFromSettlement(((Fief)settlement.Town).GarrisonParty, settlement, item.Item1, item.Item3, settlement.Owner))
					{
						maxAmountToRecruit--;
						num++;
					}
				}
				if (num > 0)
				{
					Main.ActivityLogManager.AddNewRecruits(settlement.Town, num, settlement);
				}
			}
			if (maxAmountToRecruit <= minAmountPerVillage && (!recruitOnlyEliteUnits || maxAmountToRecruit <= 0))
			{
				return;
			}
			foreach (Village item2 in (List<Village>)(object)settlement.BoundVillages)
			{
				List<Tuple<Hero, CharacterObject, int>> list = new List<Tuple<Hero, CharacterObject, int>>();
				PartyBase val = Main.PartyManagement.villageRecruitPartyManagement.GetMobilePartyFromVillage(item2);
				if (val != null || ((SettlementComponent)item2).Settlement == null)
				{
					continue;
				}
				list = GetAllRecruitableRecruitsFromSettlement(((SettlementComponent)item2).Settlement, maxAmountToRecruit, settlement.Owner, recruitOnlyEliteUnits);
				if (!(list.Count >= minAmountPerVillage || recruitOnlyEliteUnits) || list.Count <= 0)
				{
					continue;
				}
				int num2 = 0;
				foreach (Tuple<Hero, CharacterObject, int> item3 in list)
				{
					if (item3.Item2 != null)
					{
						if (val == null)
						{
							val = Main.PartyManagement.villageRecruitPartyManagement.InitializeVillageRecruitParty(new TextObject("{=party_recruits_name}Garrison recruits", (Dictionary<string, object>)null), settlement, ((SettlementComponent)item2).Settlement);
						}
						bool flag3 = maxAmountToRecruit > 0;
						if (val != null && flag3 && RecruitFromSettlement(val.MobileParty, settlement, item3.Item1, item3.Item3, settlement.Owner))
						{
							maxAmountToRecruit--;
						}
					}
				}
				if (num2 > 0)
				{
					Main.ActivityLogManager.AddNewRecruits(settlement.Town, num2, ((SettlementComponent)item2).Settlement);
				}
				if (val != null)
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(settlement, val.MobileParty);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void GetAndMoveSurroundTemplateRecruitsToMainSettlement(Settlement settlement, int minAmountPerVillage)
	{
		//IL_038d: Unknown result type (might be due to invalid IL or missing references)
		//IL_039f: Expected O, but got Unknown
		if (!settlement.IsTown && !settlement.IsCastle)
		{
			return;
		}
		GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
		Dictionary<CharacterObject, int> allNeededTemplateUnitsLeftToRecruitWithRecruiter = GetAllNeededTemplateUnitsLeftToRecruitWithRecruiter(settlement);
		if (!townSettings.RecruitmentFollowsTemplate || allNeededTemplateUnitsLeftToRecruitWithRecruiter.Count <= 0)
		{
			return;
		}
		try
		{
			List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = GetAllRecruitableRecruitsFromSettlement(settlement, int.MaxValue, settlement.Owner, onlyEliteUnits: false);
			if (allRecruitableRecruitsFromSettlement.Count >= minAmountPerVillage)
			{
				int num = 0;
				foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
				{
					if (item.Item2 == null)
					{
						continue;
					}
					bool flag = false;
					CharacterObject key = null;
					foreach (KeyValuePair<CharacterObject, int> item2 in allNeededTemplateUnitsLeftToRecruitWithRecruiter)
					{
						if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item.Item2, item2.Key))
						{
							flag = true;
							key = item2.Key;
							break;
						}
					}
					if (flag && allNeededTemplateUnitsLeftToRecruitWithRecruiter.ContainsKey(key) && RecruitFromSettlement(((Fief)settlement.Town).GarrisonParty, settlement, item.Item1, item.Item3, settlement.Owner))
					{
						allNeededTemplateUnitsLeftToRecruitWithRecruiter[key]--;
						if (allNeededTemplateUnitsLeftToRecruitWithRecruiter[key] <= 0)
						{
							allNeededTemplateUnitsLeftToRecruitWithRecruiter.Remove(key);
						}
					}
				}
				if (num > 0)
				{
					Main.ActivityLogManager.AddNewRecruits(settlement.Town, num, settlement);
				}
			}
			if (allNeededTemplateUnitsLeftToRecruitWithRecruiter.Count <= 0)
			{
				return;
			}
			foreach (Village item3 in (List<Village>)(object)settlement.BoundVillages)
			{
				List<Tuple<Hero, CharacterObject, int>> list = new List<Tuple<Hero, CharacterObject, int>>();
				PartyBase val = Main.PartyManagement.villageRecruitPartyManagement.GetMobilePartyFromVillage(item3);
				if (val != null || ((SettlementComponent)item3).Settlement == null)
				{
					continue;
				}
				list = GetAllRecruitableRecruitsFromSettlement(((SettlementComponent)item3).Settlement, int.MaxValue, settlement.Owner, onlyEliteUnits: false);
				List<Tuple<Hero, CharacterObject, int, CharacterObject>> list2 = new List<Tuple<Hero, CharacterObject, int, CharacterObject>>();
				foreach (Tuple<Hero, CharacterObject, int> item4 in list.ToList())
				{
					foreach (KeyValuePair<CharacterObject, int> item5 in allNeededTemplateUnitsLeftToRecruitWithRecruiter)
					{
						if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item4.Item2, item5.Key))
						{
							list2.Add(new Tuple<Hero, CharacterObject, int, CharacterObject>(item4.Item1, item4.Item2, item4.Item3, item5.Key));
							break;
						}
					}
				}
				if (list2.Count <= 0 || list2.Count < minAmountPerVillage)
				{
					continue;
				}
				int num2 = 0;
				foreach (Tuple<Hero, CharacterObject, int, CharacterObject> item6 in list2)
				{
					if (item6.Item2 == null)
					{
						continue;
					}
					if (val == null)
					{
						val = Main.PartyManagement.villageRecruitPartyManagement.InitializeVillageRecruitParty(new TextObject("{=party_recruits_name}Garrison recruits", (Dictionary<string, object>)null), settlement, ((SettlementComponent)item3).Settlement);
					}
					if (val != null && allNeededTemplateUnitsLeftToRecruitWithRecruiter.ContainsKey(item6.Item4) && RecruitFromSettlement(val.MobileParty, settlement, item6.Item1, item6.Item3, settlement.Owner))
					{
						allNeededTemplateUnitsLeftToRecruitWithRecruiter[item6.Item4]--;
						if (allNeededTemplateUnitsLeftToRecruitWithRecruiter[item6.Item4] <= 0)
						{
							allNeededTemplateUnitsLeftToRecruitWithRecruiter.Remove(item6.Item4);
						}
					}
				}
				if (num2 > 0)
				{
					Main.ActivityLogManager.AddNewRecruits(settlement.Town, num2, ((SettlementComponent)item3).Settlement);
				}
				if (val != null)
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(settlement, val.MobileParty);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public List<Tuple<Hero, CharacterObject, int>> GetAllRecruitableRecruitsFromSettlement(Settlement settlement, int maxAmountToRecruit, Hero ownerHero, bool onlyEliteUnits)
	{
		List<Tuple<Hero, CharacterObject, int>> list = new List<Tuple<Hero, CharacterObject, int>>();
		if (settlement == null || settlement.Notables == null || ownerHero == null)
		{
			return null;
		}
		foreach (Hero item in (List<Hero>)(object)settlement.Notables)
		{
			if (!item.CanHaveRecruits || maxAmountToRecruit <= 0)
			{
				continue;
			}
			CharacterObject[] volunteerTypes = item.VolunteerTypes;
			if (volunteerTypes == null || volunteerTypes.Length == 0)
			{
				continue;
			}
			int num = Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, item, -101);
			for (int i = 0; i < volunteerTypes.Length; i++)
			{
				if (volunteerTypes[i] != null)
				{
					bool flag = i <= num;
					bool flag2 = true;
					if (onlyEliteUnits)
					{
						flag2 = Main.UpgradeLogic.CharacterCanUpgradeToTarget(volunteerTypes[i].Culture.EliteBasicTroop, volunteerTypes[i]);
					}
					if (flag && volunteerTypes != null && maxAmountToRecruit > 0 && flag2)
					{
						list.Add(new Tuple<Hero, CharacterObject, int>(item, volunteerTypes[i], i));
						maxAmountToRecruit--;
					}
				}
			}
		}
		return list;
	}

	public bool RecruitFromSettlement(MobileParty recruitingParty, Settlement settlementToRecruitFrom, Hero notable, int bitCode, Hero partyOwner)
	{
		if (recruitingParty == null || settlementToRecruitFrom == null || notable == null)
		{
			return false;
		}
		if (notable.VolunteerTypes[bitCode] != null)
		{
			recruitingParty.AddElementToMemberRoster(notable.VolunteerTypes[bitCode], 1, false);
			Main.ActivityLogManager.AddUnitRecruitmentCost(settlementToRecruitFrom.Town, notable.VolunteerTypes[bitCode], 1, partyOwner);
			notable.VolunteerTypes[bitCode] = null;
			return true;
		}
		return false;
	}

	public int OverallAmountOfAvailableRecruits(Settlement settlement, int maxAmountToRecruit, Hero ownerHero, bool onlyEliteUnits)
	{
		List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = GetAllRecruitableRecruitsFromSettlement(settlement, maxAmountToRecruit, ownerHero, onlyEliteUnits);
		int num = 0;
		foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
		{
			num++;
		}
		return num;
	}

	public int AmountOfAvailableRecruitsByList(Settlement settlementToRecruitFrom, int minAmountToRecruit, Hero ownerHero, List<CharacterObject> unitList)
	{
		if (unitList == null || unitList.Count <= 0)
		{
			return 0;
		}
		List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = GetAllRecruitableRecruitsFromSettlement(settlementToRecruitFrom, minAmountToRecruit, ownerHero, onlyEliteUnits: false);
		int num = 0;
		if (allRecruitableRecruitsFromSettlement == null)
		{
			return 0;
		}
		foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
		{
			if (unitList.Contains(item.Item2))
			{
				num++;
			}
		}
		return num;
	}

	public int AmountOfAvailableRecruitsThatCanBeUpgraded(Settlement settlementToRecruitFrom, int minAmountToRecruit, Hero ownerHero, List<CharacterObject> templateUnitPaths)
	{
		if (templateUnitPaths == null || templateUnitPaths.Count <= 0)
		{
			return 0;
		}
		List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = GetAllRecruitableRecruitsFromSettlement(settlementToRecruitFrom, minAmountToRecruit, ownerHero, onlyEliteUnits: false);
		int num = 0;
		if (allRecruitableRecruitsFromSettlement == null)
		{
			return 0;
		}
		foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
		{
			foreach (CharacterObject templateUnitPath in templateUnitPaths)
			{
				if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item.Item2, templateUnitPath))
				{
					num++;
					break;
				}
			}
		}
		return num;
	}

	public Dictionary<CharacterObject, int> GetAmountOfTemplateUnitsLeftToRecruit(Settlement forSettlement)
	{
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (forSettlement == null || forSettlement.Town == null)
			{
				return null;
			}
			GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(forSettlement.Town);
			Dictionary<CharacterObject, int> dictionary = townSettings.Template.GetTroopListAsCharacterObjects().ToDictionary((KeyValuePair<CharacterObject, int> entry) => entry.Key, (KeyValuePair<CharacterObject, int> entry) => entry.Value);
			if (((Fief)forSettlement.Town).GarrisonParty != null)
			{
				List<TroopRosterElement> troopRoster = (List<TroopRosterElement>)(object)((Fief)forSettlement.Town).GarrisonParty.MemberRoster.GetTroopRoster();
				troopRoster = troopRoster.OrderByDescending((TroopRosterElement x) => x.Character.Tier).ToList();
				foreach (TroopRosterElement item in troopRoster)
				{
					TroopRosterElement current = item;
					int num = ((TroopRosterElement)(ref current)).Number;
					foreach (KeyValuePair<CharacterObject, int> item2 in townSettings.Template.GetTroopListAsCharacterObjects().ToList())
					{
						CharacterObject key = item2.Key;
						if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(current.Character, key) && dictionary.ContainsKey(key))
						{
							int num2 = dictionary[key];
							int num3 = num2 - num;
							if (num3 <= 0)
							{
								dictionary.Remove(key);
							}
							else
							{
								dictionary[key] = num3;
							}
							num -= num2;
							if (num <= 0)
							{
								break;
							}
						}
					}
				}
			}
			return dictionary;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public Dictionary<CharacterObject, int> GetAllNeededTemplateUnitsLeftToRecruitWithRecruiter(Settlement forSettlement, GarrisonRecruiter recruiter = null)
	{
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Dictionary<CharacterObject, int> amountOfTemplateUnitsLeftToRecruit = GetAmountOfTemplateUnitsLeftToRecruit(forSettlement);
			if (recruiter == null)
			{
				recruiter = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(forSettlement);
				if (recruiter == null)
				{
					return amountOfTemplateUnitsLeftToRecruit;
				}
			}
			List<TroopRosterElement> troopRoster = (List<TroopRosterElement>)(object)recruiter.mobileParty.MemberRoster.GetTroopRoster();
			troopRoster = troopRoster.OrderByDescending((TroopRosterElement x) => x.Character.Tier).ToList();
			foreach (TroopRosterElement item in troopRoster)
			{
				TroopRosterElement current = item;
				int num = ((TroopRosterElement)(ref current)).Number;
				foreach (KeyValuePair<CharacterObject, int> item2 in amountOfTemplateUnitsLeftToRecruit.ToList())
				{
					if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(current.Character, item2.Key))
					{
						int num2 = num;
						num -= amountOfTemplateUnitsLeftToRecruit[item2.Key];
						amountOfTemplateUnitsLeftToRecruit[item2.Key] -= num2;
						if (amountOfTemplateUnitsLeftToRecruit[item2.Key] <= 0)
						{
							amountOfTemplateUnitsLeftToRecruit.Remove(item2.Key);
						}
						if (num <= 0)
						{
							break;
						}
					}
				}
			}
			return amountOfTemplateUnitsLeftToRecruit;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public int AmountOfNeededTemplateUnitsAlreadyInSettlement(Settlement templateSettlement, CharacterObject templateObject)
	{
		if (templateSettlement == null || templateSettlement.Town == null || ((Fief)templateSettlement.Town).GarrisonParty == null)
		{
			return 0;
		}
		return Main.UpgradeLogic.RecursivGetUnitsOnPathInGarrison(templateObject, templateSettlement);
	}

	public void TryRecruitAllPrisoners()
	{
		foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
		{
			if (item.IsCastle || item.IsTown)
			{
				RecruitPrisonersInSettlement(item);
			}
		}
	}

	public void RecruitPrisonersInSettlement(Settlement settlement)
	{
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			int dailyPrisonerConformityAmount = ConfigManager.Instance.Config.DailyPrisonerConformityAmount;
			if (settlement == null)
			{
				return;
			}
			GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
			if (!townSettings.EnablePrisonerRecruitment)
			{
				return;
			}
			List<CharacterObject> list = new List<CharacterObject>();
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)settlement.Party.PrisonRoster.GetTroopRoster())
			{
				list.Add(item.Character);
			}
			foreach (CharacterObject item2 in list)
			{
				PrisonerRecruitmentCalculationModel prisonerRecruitmentCalculationModel = Campaign.Current.Models.PrisonerRecruitmentCalculationModel;
				int num = prisonerRecruitmentCalculationModel.CalculateRecruitableNumber(settlement.Party, item2);
				if (((BasicCharacterObject)item2).IsHero || (!IsGarrisonRecruitmentAllowed(settlement) && !townSettings.AllowPrisonerRecruitAboveThreshold))
				{
					continue;
				}
				int troopCount = settlement.Party.PrisonRoster.GetTroopCount(item2);
				int num2 = dailyPrisonerConformityAmount * troopCount;
				if (num < troopCount && settlement.Party.PrisonRoster.Contains(item2))
				{
					settlement.Party.PrisonRoster.AddXpToTroop(item2, num2);
					num = prisonerRecruitmentCalculationModel.CalculateRecruitableNumber(settlement.Party, item2);
				}
				if (!townSettings.PrisonerRecruitmentIgnoresTemplate && townSettings.RecruitmentFollowsTemplate)
				{
					Dictionary<CharacterObject, int> allNeededTemplateUnitsLeftToRecruitWithRecruiter = Main.RecruitmentLogic.GetAllNeededTemplateUnitsLeftToRecruitWithRecruiter(settlement);
					if (allNeededTemplateUnitsLeftToRecruitWithRecruiter.Count <= 0)
					{
						break;
					}
					bool flag = false;
					foreach (KeyValuePair<CharacterObject, int> item3 in allNeededTemplateUnitsLeftToRecruitWithRecruiter)
					{
						if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item2, item3.Key))
						{
							flag = true;
							break;
						}
					}
					if (!flag)
					{
						continue;
					}
				}
				int conformityNeededToRecruitPrisoner = prisonerRecruitmentCalculationModel.GetConformityNeededToRecruitPrisoner(item2);
				if (((Fief)settlement.Town).GarrisonParty != null)
				{
					int partySizeLimit = Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
					int totalManCount = ((Fief)settlement.Town).GarrisonParty.MemberRoster.TotalManCount;
					if (totalManCount + num > partySizeLimit)
					{
						num = partySizeLimit - totalManCount;
					}
				}
				if (num > 0)
				{
					if (((Fief)settlement.Town).GarrisonParty == null)
					{
						settlement.AddGarrisonParty();
					}
					if (RecruitPrisonerToGarrison(item2, num, settlement))
					{
						settlement.Party.PrisonRoster.AddXpToTroop(item2, -1 * conformityNeededToRecruitPrisoner * num);
						settlement.Party.PrisonRoster.RemoveTroop(item2, num, default(UniqueTroopDescriptor), 0);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void MilitiaToGarrisonAllSettlements()
	{
		foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
		{
			MilitiaToGarrison(item);
		}
	}

	public void MilitiaToGarrison(Settlement settlement)
	{
		if (settlement.IsCastle || settlement.IsTown)
		{
			try
			{
			}
			catch (Exception ex)
			{
				LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			}
		}
	}

	private bool IsGarrisonRecruitmentAllowed(Settlement settlement)
	{
		try
		{
			GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(settlement.Town);
			bool flag = ((Fief)settlement.Town).GarrisonParty != null;
			bool flag2 = !settlement.Town.IsUnderSiege;
			bool flag3 = true;
			bool flag4 = false;
			bool recruitmentFollowsTemplate = townSettings.RecruitmentFollowsTemplate;
			if (flag)
			{
				int partySizeLimit = Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
				int garrisonSizeWithRecruiter = Main.GarrisonBehavior.GetAutomatedGarrisonForSettlement(settlement).GetGarrisonSizeWithRecruiter();
				flag3 = garrisonSizeWithRecruiter < partySizeLimit;
				flag4 = garrisonSizeWithRecruiter > townSettings.MaxRecruitThreshold;
			}
			return flag3 && (recruitmentFollowsTemplate || !flag4) && flag2;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	private bool DoesAmountExeedGarrisonLimit(Settlement settlement, int amount)
	{
		if (settlement != null && settlement.Town != null && ((Fief)settlement.Town).GarrisonParty != null)
		{
			int partySizeLimit = Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
			int totalManCount = ((Fief)settlement.Town).GarrisonParty.MemberRoster.TotalManCount;
			bool flag = totalManCount + amount > partySizeLimit;
			bool flag2 = totalManCount + amount > Main.PartyManagement.GetPartySizeLimit(((Fief)settlement.Town).GarrisonParty.Party);
			return flag || flag2;
		}
		return false;
	}
}
