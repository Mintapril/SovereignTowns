using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.Menu;
using ImprovedGarrisons.SaveSystem.SaveData;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.SaveSystem;

public class GarrisonBehavior : CampaignBehaviorBase
{
	private MainMenu mainMenu;

	public readonly Dictionary<Settlement, ImprovedSettlement> ImprovedSettlements = new Dictionary<Settlement, ImprovedSettlement>();

	public MainMenu MainMenu => mainMenu;

	public Dictionary<string, GarrisonSettings> SettlementSettingsData => IGSaveData.Instance.SettlementSettingsData;

	public Town CurrentTownForSettings { get; set; }

	public override void RegisterEvents()
	{
		CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener((object)this, (Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementDetail>)onSettlementOwnerChanged);
		CampaignEvents.HourlyTickEvent.AddNonSerializedListener((object)this, (Action)HourlyEvent);
		CampaignEvents.DailyTickEvent.AddNonSerializedListener((object)this, (Action)DailyEvent);
	}

	public void OnGameOpen(CampaignGameStarter campaignGameStarter)
	{
		try
		{
			mainMenu = new MainMenu(campaignGameStarter);
			SetAllAutomatedGarrisons();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void HourlyEvent()
	{
		try
		{
			foreach (ImprovedSettlement value in ImprovedSettlements.Values)
			{
				value.HourlyThinkBehavior();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void DailyEvent()
	{
		try
		{
			foreach (ImprovedSettlement value in ImprovedSettlements.Values)
			{
				value.DailyThinkBehavior();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void onSettlementOwnerChanged(Settlement settlement, bool x, Hero y, Hero z, Hero a, ChangeOwnerOfSettlementDetail action)
	{
		try
		{
			if (settlement != null && (settlement.IsTown || settlement.IsCastle) && SettlementSettingsData.ContainsKey(((object)((SettlementComponent)settlement.Town).Name).ToString()) && settlement.Owner != Hero.MainHero)
			{
				SettlementSettingsData.Remove(((object)((SettlementComponent)settlement.Town).Name).ToString());
				if (UIManager.Instance.improvedGarrisonsUI != null)
				{
					UIManager.Instance.improvedGarrisonsUI.UpdateSettlementSelector();
				}
			}
			else if (settlement != null && (settlement.IsTown || settlement.IsCastle) && !SettlementSettingsData.ContainsKey(((object)((SettlementComponent)settlement.Town).Name).ToString()) && settlement.Owner == Hero.MainHero)
			{
				GetTownSettings(settlement.Town);
				if (UIManager.Instance.improvedGarrisonsUI != null)
				{
					UIManager.Instance.improvedGarrisonsUI.UpdateSettlementSelector();
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public GarrisonSettings GetCurrentTownSettings()
	{
		return GetTownSettings(CurrentTownForSettings);
	}

	public void ResetTownSettings(Town town)
	{
		if (SettlementSettingsData.ContainsKey(((object)((SettlementComponent)town).Name).ToString()))
		{
			SettlementSettingsData[((object)((SettlementComponent)town).Name).ToString()] = new GarrisonSettings();
		}
	}

	public GarrisonSettings GetTownSettings(Town town)
	{
		try
		{
			bool flag = SettlementSettingsData != null;
			if (town != null && ((SettlementComponent)town).Owner != null)
			{
				if (((object)((SettlementComponent)town).Settlement.OwnerClan).Equals((object)Hero.MainHero.Clan) && flag)
				{
					if (SettlementSettingsData.TryGetValue(((object)((SettlementComponent)town).Name).ToString(), out var value))
					{
						return value;
					}
					value = new GarrisonSettings();
					SettlementSettingsData.Add(((object)((SettlementComponent)town).Name).ToString(), value);
					return value;
				}
				return new NPCGarrisonSettings();
			}
			return new NPCGarrisonSettings();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
	}

	public bool PlayerHasAFief()
	{
		try
		{
			return SettlementSettingsData != null && SettlementSettingsData.Count > 0;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	public ImprovedSettlement GetAutomatedGarrisonForSettlement(Settlement settlement)
	{
		if (!ImprovedSettlements.TryGetValue(settlement, out var value))
		{
			return new ImprovedSettlement(settlement);
		}
		return value;
	}

	public Settlement GetSettlementFromName(string name)
	{
		if (name != null && name.Length > 0)
		{
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if (item.Name != (TextObject)null && ((object)item.Name).ToString() == name)
				{
					return item;
				}
			}
		}
		return null;
	}

	internal void InitializeSettlements()
	{
		try
		{
			List<string> list = new List<string>();
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if (item != null && item.Town != null && (item.IsTown || item.IsCastle) && item.Town.OwnerClan == Hero.MainHero.Clan)
				{
					GetTownSettings(item.Town);
				}
				else
				{
					if (item == null || (!item.IsTown && !item.IsCastle))
					{
						continue;
					}
					foreach (KeyValuePair<string, GarrisonSettings> settlementSettingsDatum in SettlementSettingsData)
					{
						if (((object)((SettlementComponent)item.Town).Name).ToString() == settlementSettingsDatum.Key)
						{
							list.Add(settlementSettingsDatum.Key);
						}
					}
				}
			}
			foreach (string item2 in list)
			{
				SettlementSettingsData.Remove(item2);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void SetAllAutomatedGarrisons()
	{
		try
		{
			foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
			{
				if (item != null && (item.IsTown || item.IsCastle))
				{
					if (SettlementSettingsData.TryGetValue(((object)item.Name).ToString(), out var _))
					{
						ImprovedSettlements.Add(item, new ImprovedSettlement(item));
						continue;
					}
					bool flag = true;
					ImprovedSettlement improvedSettlement = new ImprovedSettlement(item);
					ImprovedSettlements.Add(item, improvedSettlement);
					improvedSettlement.Activate();
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void UpdateNPCGarrisonSettings()
	{
		try
		{
			foreach (KeyValuePair<Settlement, ImprovedSettlement> item in ImprovedSettlements.ToList())
			{
				if (!SettlementSettingsData.TryGetValue(((object)item.Key.Name).ToString(), out var _))
				{
					ImprovedSettlements[item.Key] = new ImprovedSettlement(item.Key);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public List<Settlement> GetAllPlayerSettlements()
	{
		Settlement[] array = ((IEnumerable<Settlement>)Settlement.All).ToArray();
		List<Settlement> list = new List<Settlement>();
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] != null && array[i].Town != null && (((SettlementComponent)array[i].Town).IsCastle || ((SettlementComponent)array[i].Town).IsTown) && SettlementSettingsData.TryGetValue(((object)array[i].Name).ToString(), out var _))
			{
				list.Add(array[i]);
			}
		}
		return list;
	}

	public List<Tuple<CharacterObject, int>> GetLowestTierUnitsByAmount(int amount, Town town)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		if (town != null && amount > 0 && ((Fief)town).GarrisonParty != null)
		{
			MobileParty garrisonParty = ((Fief)town).GarrisonParty;
			List<TroopRosterElement> troopRoster = (List<TroopRosterElement>)(object)garrisonParty.MemberRoster.GetTroopRoster();
			List<TroopRosterElement> list = troopRoster.OrderBy((TroopRosterElement x) => x.Character.Tier).ToList();
			List<Tuple<CharacterObject, int>> list2 = new List<Tuple<CharacterObject, int>>();
			troopRoster = new List<TroopRosterElement>();
			while (amount > 0 && list.Count > 0)
			{
				TroopRosterElement val = list.First();
				int num = garrisonParty.MemberRoster.GetTroopCount(val.Character);
				if (num > amount)
				{
					num = amount;
				}
				list2.Add(new Tuple<CharacterObject, int>(val.Character, num));
				list.Remove(val);
				amount -= num;
			}
			return list2;
		}
		return null;
	}

	public override void SyncData(IDataStore dataStore)
	{
	}
}
