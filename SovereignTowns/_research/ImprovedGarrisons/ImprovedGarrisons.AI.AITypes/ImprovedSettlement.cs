using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using ImprovedGarrisons.AI.Orders.PartyOrder;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ImprovedGarrisons.AI.AITypes;

public class ImprovedSettlement : ImprovedAi
{
	protected enum Mode
	{
		NotActive,
		Saving,
		Defending,
		Supporting,
		Training,
		Normal
	}

	private readonly List<Village> boundVillages;

	private MobileParty garrisonParty;

	protected Mode currentMode = Mode.Normal;

	public int savingCounter = 0;

	public Settlement Settlement { get; private set; }

	public List<Settlement> NeighbourSettlementsAndVillages { get; private set; }

	public GarrisonSettings homeGarrisonSettings
	{
		get
		{
			if (Settlement != null)
			{
				return Main.GarrisonBehavior.GetTownSettings(Settlement.Town);
			}
			return new GarrisonSettings();
		}
	}

	public MobileParty GarrisonParty
	{
		get
		{
			if (garrisonParty == null)
			{
				Settlement.AddGarrisonParty();
			}
			return ((Fief)Settlement.Town).GarrisonParty;
		}
	}

	public ImprovedSettlement(Settlement settlement)
	{
		Settlement = settlement;
		boundVillages = ((IEnumerable<Village>)settlement.BoundVillages).ToList();
		NeighbourSettlementsAndVillages = GetNeighbours(50f);
	}

	public override void HourlyThinkBehavior()
	{
		try
		{
			switch (currentMode)
			{
			case Mode.Normal:
				GetRegionTaxes();
				break;
			case Mode.Saving:
				if (IsEconomyPositive())
				{
					currentMode = Mode.Normal;
				}
				else if (savingCounter % 3 != 0)
				{
				}
				break;
			}
			DefendVillageIfNeeded();
			AutoSpawnGuardsIfNeeded();
			ReturnGuardsIfNeeded();
			AutoSpawnRecruiterIfNeeded();
			DisableVanillaRecruitmentIfNeeded();
			DisableVanillaTrainingIfNeeded();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void DailyThinkBehavior()
	{
		try
		{
			switch (currentMode)
			{
			case Mode.Normal:
				GetRegionTaxes();
				break;
			case Mode.Saving:
				if (IsEconomyPositive())
				{
					currentMode = Mode.Normal;
				}
				else if (savingCounter % 3 != 0)
				{
				}
				break;
			}
			RemoveNonTemplateUnitsFromGarrison();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private List<Settlement> GetNeighbours(float radius)
	{
		try
		{
			HashSet<Settlement> hashSet = new HashSet<Settlement>();
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if (item == null || item.IsHideout || Settlement == item)
				{
					continue;
				}
				float num = DistanceHelper.FindClosestDistanceFromSettlementToSettlement(Settlement, item, (NavigationType)3);
				if (num <= radius)
				{
					hashSet.Add(item);
					if (item.IsVillage)
					{
						hashSet.Add(item.Village.Bound);
					}
				}
			}
			return hashSet.ToList();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
	}

	private void DisableVanillaRecruitmentIfNeeded()
	{
		try
		{
			if (!homeGarrisonSettings.VanillaRecruitment && Settlement != null && Settlement.Town != null)
			{
				Settlement.Town.GarrisonAutoRecruitmentIsEnabled = false;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void DisableVanillaTrainingIfNeeded()
	{
		try
		{
			if (!homeGarrisonSettings.VanillaTraining && Settlement != null && ((Fief)Settlement.Town).GarrisonParty != null)
			{
				((Fief)Settlement.Town).GarrisonParty.SetWagePaymentLimit(Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit);
				Settlement.Town.GarrisonAutoRecruitmentIsEnabled = false;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void ReturnGuardsIfNeeded()
	{
		try
		{
			MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(Settlement);
			bool flag = Settlement != null;
			bool guardsAutoSpawn = homeGarrisonSettings.GuardsAutoSpawn;
			if (mobileGarrisonPartyOfSettlement != null && flag && mobileGarrisonPartyOfSettlement.CurrentOrder is OrderPatrol && mobileGarrisonPartyOfSettlement.isNPC && (((Fief)Settlement.Town).GarrisonParty == null || !guardsAutoSpawn))
			{
				mobileGarrisonPartyOfSettlement.SetReturnMode();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void AutoSpawnGuardsIfNeeded()
	{
		try
		{
			MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(Settlement);
			Settlement settlement = Settlement;
			bool flag = ((settlement != null) ? ((Fief)settlement.Town).GarrisonParty : null) != null;
			bool guardsAutoSpawn = homeGarrisonSettings.GuardsAutoSpawn;
			IFaction mapFaction = Settlement.MapFaction;
			bool flag2 = ((mapFaction != null) ? mapFaction.Leader : null) != null;
			if (!(guardsAutoSpawn && mobileGarrisonPartyOfSettlement == null && flag))
			{
				return;
			}
			int numberOfAllMembers = ((Fief)Settlement.Town).GarrisonParty.Party.NumberOfAllMembers;
			if ((CheckIfNPCGarrison() && numberOfAllMembers > ConfigManager.Instance.Config.NPCGuardSpawnThreshold) || (!CheckIfNPCGarrison() && numberOfAllMembers > homeGarrisonSettings.GuardsAutoSpawnThreshold))
			{
				int num = 0;
				num = ((!CheckIfNPCGarrison()) ? homeGarrisonSettings.GuardsAutoSpawnSize : ((int)((double)numberOfAllMembers * ConfigManager.Instance.Config.NPCGuardCreationMultiplier)));
				if (num > 0 && numberOfAllMembers > num)
				{
					Main.PartyManagement.mobileGarrisonManagement.CreateMobileGarrisonWithUnits(Settlement, num, CheckIfNPCGarrison());
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void AutoSpawnRecruiterIfNeeded()
	{
		try
		{
			bool flag = !Main.PartyManagement.garrisonRecruiterPartyManagement.SettlementHasARecruiter(Settlement);
			bool flag2 = homeGarrisonSettings.RecruiterAutoSpawn && homeGarrisonSettings.RecruitmentFollowsTemplate && homeGarrisonSettings.EnableTraining;
			bool flag3 = !IsUnderAttack();
			if (flag2 && flag3 && flag && ((Fief)Settlement.Town).GarrisonParty != null)
			{
				int partySizeLimit = Main.PartyManagement.GetPartySizeLimit(((Fief)Settlement.Town).GarrisonParty.Party);
				bool flag4 = ((Fief)Settlement.Town).GarrisonParty.Party.NumberOfAllMembers >= partySizeLimit;
				Dictionary<CharacterObject, int> amountOfTemplateUnitsLeftToRecruit = Main.RecruitmentLogic.GetAmountOfTemplateUnitsLeftToRecruit(Settlement);
				if (amountOfTemplateUnitsLeftToRecruit != null && amountOfTemplateUnitsLeftToRecruit.Count > 0 && !flag4)
				{
					Main.PartyManagement.garrisonRecruiterPartyManagement.CreateGarrisonRecruiterParty(Settlement, Settlement, autoAddTroop: true);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void AutoSpawnGuardsToDefend(MobileParty attackerParty)
	{
		try
		{
			if (attackerParty == null)
			{
				return;
			}
			MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(Settlement);
			bool flag = Settlement != null && ((Fief)Settlement.Town).GarrisonParty != null;
			bool guardsAutoSpawnToDefend = homeGarrisonSettings.GuardsAutoSpawnToDefend;
			bool guardsAutoSpawn = homeGarrisonSettings.GuardsAutoSpawn;
			float num = attackerParty.GetTotalLandStrengthWithFollowers(true) + 50f;
			if (guardsAutoSpawnToDefend && mobileGarrisonPartyOfSettlement == null && flag)
			{
				Main.PartyManagement.mobileGarrisonManagement.TryCreateMobileGarrisonByStrength(Settlement, num, CheckIfNPCGarrison(), shortLived: true);
			}
			if (!(guardsAutoSpawnToDefend && guardsAutoSpawn && mobileGarrisonPartyOfSettlement != null && flag))
			{
				return;
			}
			float totalLandStrengthWithFollowers = mobileGarrisonPartyOfSettlement.mobileParty.GetTotalLandStrengthWithFollowers(true);
			if (totalLandStrengthWithFollowers < num)
			{
				float totalLandStrengthWithFollowers2 = ((Fief)Settlement.Town).GarrisonParty.GetTotalLandStrengthWithFollowers(true);
				if (totalLandStrengthWithFollowers + totalLandStrengthWithFollowers2 > num)
				{
					mobileGarrisonPartyOfSettlement.SetReturnMode();
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void DefendVillageIfNeeded()
	{
		try
		{
			foreach (Village boundVillage in boundVillages)
			{
				if (((SettlementComponent)boundVillage).Settlement.IsUnderRaid)
				{
					MobileParty lastAttackerParty = ((SettlementComponent)boundVillage).Settlement.LastAttackerParty;
					AutoSpawnGuardsToDefend(lastAttackerParty);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void RemoveNonTemplateUnitsFromGarrison()
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_031c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0322: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (Settlement.Name.Contains("Jacul"))
			{
			}
			if (((Fief)Settlement.Town).GarrisonParty == null || !homeGarrisonSettings.AutoRemoveNonTemplateTroops || homeGarrisonSettings.Template.AmountOfTroopsInTemplate <= 0)
			{
				return;
			}
			List<TroopRosterElement> troopRoster = (List<TroopRosterElement>)(object)((Fief)Settlement.Town).GarrisonParty.MemberRoster.GetTroopRoster();
			troopRoster = troopRoster.OrderByDescending((TroopRosterElement x) => x.Character.Tier).ToList();
			Dictionary<CharacterObject, int> dictionary = new Dictionary<CharacterObject, int>();
			foreach (TroopRosterElement item in troopRoster)
			{
				TroopRosterElement current = item;
				dictionary.Add(current.Character, ((TroopRosterElement)(ref current)).Number);
			}
			Dictionary<CharacterObject, int> troopListAsCharacterObjects = homeGarrisonSettings.Template.GetTroopListAsCharacterObjects();
			List<Tuple<CharacterObject, int>> list = new List<Tuple<CharacterObject, int>>();
			foreach (KeyValuePair<CharacterObject, int> item2 in troopListAsCharacterObjects)
			{
				list.Add(new Tuple<CharacterObject, int>(item2.Key, item2.Value));
			}
			list = list.OrderByDescending((Tuple<CharacterObject, int> x) => x.Item1.Tier).ToList();
			foreach (KeyValuePair<CharacterObject, int> item3 in dictionary.ToList())
			{
				foreach (Tuple<CharacterObject, int> item4 in list)
				{
					int num = 0;
					if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item3.Key, item4.Item1))
					{
						int num2 = troopListAsCharacterObjects[item4.Item1];
						num = num2 - item3.Value;
						num = ((num < 0) ? Math.Abs(num) : 0);
						troopListAsCharacterObjects[item4.Item1] -= item3.Value;
						troopListAsCharacterObjects[item4.Item1] = ((troopListAsCharacterObjects[item4.Item1] >= 0) ? troopListAsCharacterObjects[item4.Item1] : 0);
					}
					if (num >= 0)
					{
						dictionary[item4.Item1] = num;
					}
				}
			}
			foreach (KeyValuePair<CharacterObject, int> item5 in dictionary)
			{
				if (!((BasicCharacterObject)item5.Key).IsHero && item5.Value > 0)
				{
					((Fief)Settlement.Town).GarrisonParty.MemberRoster.RemoveTroop(item5.Key, item5.Value, default(UniqueTroopDescriptor), 0);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetModeSaving()
	{
		currentMode = Mode.Saving;
		savingCounter = 0;
	}

	private bool IsEconomyPositive()
	{
		return ((SettlementComponent)Settlement.Town).Gold > 0;
	}

	private void RemoveTroopsToBePositive(int minThreshold)
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			int totalManCount = GarrisonParty.MemberRoster.TotalManCount;
			if (totalManCount > minThreshold)
			{
				ExplainedNumber val = Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(Settlement.Town, false);
				int num = (int)((ExplainedNumber)(ref val)).ResultNumber;
				int gold = ((SettlementComponent)Settlement.Town).Gold;
				if (gold <= 0)
				{
					List<Tuple<CharacterObject, int>> lowestUnitsByCost = GetLowestUnitsByCost(0, Settlement.Town);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private List<Tuple<CharacterObject, int>> GetLowestUnitsByCost(int costs, Town town)
	{
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		if (town != null && costs > 0)
		{
			MobileParty val = ((Fief)town).GarrisonParty;
			List<TroopRosterElement> troopRoster = (List<TroopRosterElement>)(object)val.MemberRoster.GetTroopRoster();
			List<TroopRosterElement> list = troopRoster.OrderBy((TroopRosterElement x) => x.Character.Tier).ToList();
			List<Tuple<CharacterObject, int>> list2 = new List<Tuple<CharacterObject, int>>();
			troopRoster = new List<TroopRosterElement>();
			while (costs > 0)
			{
				TroopRosterElement val2 = list.First();
				int num = val.MemberRoster.GetTroopCount(val2.Character);
				int troopWage = val2.Character.TroopWage;
				while (num > 0 && costs > 0)
				{
					num--;
					costs -= troopWage;
				}
				list2.Add(new Tuple<CharacterObject, int>(val2.Character, num));
				list.Remove(val2);
			}
			return list2;
		}
		return null;
	}

	private void GetRegionTaxes()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			int num = 0;
			Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(Settlement.Town, false);
			num = Settlement.Town.TradeTaxAccumulated;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public bool HasMobileGarrison()
	{
		try
		{
			MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(Settlement);
			return mobileGarrisonPartyOfSettlement != null;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public int GetGarrisonSizeWithRecruiter()
	{
		try
		{
			int num = 0;
			if (Settlement.Town != null && ((Fief)Settlement.Town).GarrisonParty != null)
			{
				num += ((Fief)Settlement.Town).GarrisonParty.Party.NumberOfAllMembers;
			}
			GarrisonRecruiter recruiterOfSettlement = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(Settlement);
			if (recruiterOfSettlement != null)
			{
				num += recruiterOfSettlement.mobileParty.Party.NumberOfAllMembers;
				num += recruiterOfSettlement.GetAmountLeftToRecruit();
			}
			return num;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return -1;
	}

	public void Activate()
	{
		currentMode = Mode.Normal;
	}

	public new void NextHour()
	{
		base.HourCounter++;
		savingCounter++;
	}

	private bool IsUnderAttack()
	{
		return Settlement.IsUnderRaid || Settlement.IsUnderSiege;
	}

	private bool IsRebelling()
	{
		return Settlement.InRebelliousState;
	}

	private bool IsStarving()
	{
		return Settlement.IsStarving;
	}

	private bool CheckIfNPCGarrison()
	{
		return (Settlement.OwnerClan != MobileParty.MainParty.ActualClan) ? true : false;
	}
}
