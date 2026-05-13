using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Upgrade;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;

public class TrainingSettings : ImprovedGarrisonSettings
{
	private List<TroopTypes.Type> _unitTypeForSpecificUpgrade = new List<TroopTypes.Type>();

	private static TrainingSettings _instance;

	private TrainingUIVM _trainingDataSource;

	private Town _currentTown;

	public static TrainingSettings Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new TrainingSettings();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public void PromptCurrentTemplateManagement(Town town, TrainingUIVM trainingDataSource = null)
	{
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected O, but got Unknown
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Expected O, but got Unknown
		//IL_009a: Expected O, but got Unknown
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Expected O, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Expected O, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		//IL_00d7: Expected O, but got Unknown
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Expected O, but got Unknown
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Expected O, but got Unknown
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Expected O, but got Unknown
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Expected O, but got Unknown
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Expected O, but got Unknown
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				List<InquiryElement> list = new List<InquiryElement>();
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				_currentTown = town;
				if (trainingDataSource != null)
				{
					_trainingDataSource = trainingDataSource;
				}
				Banner val = new Banner("11.116.1.1836.1836.768.788.1.0.-30.527.0.0.304.450.763.786.1.0.1");
				Banner val2 = new Banner("11.116.1.1836.1836.768.788.1.0.-30.510.122.122.304.296.665.772.1.0.-91.510.122.122.304.296.859.766.1.0.-91.510.122.122.328.296.764.772.1.0.-56");
				bool flag;
				list.Add(new InquiryElement((object)(flag = false), ((object)new TextObject("{=settings_trainingsettings_currenttemplate1}Current training template", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(val, false), true, ((object)new TextObject("{=settings_trainingsettings_currenttemplate2}Your list of specified training targets", (Dictionary<string, object>)null)).ToString()));
				list.Add(new InquiryElement((object)(flag = true), ((object)new TextObject("{=settings_trainingsettings_specifytroops1}Specify troops to train", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(val2, false), true, ((object)new TextObject("{=settings_trainingsettings_specifytroops2}List of every troops", (Dictionary<string, object>)null)).ToString()));
				MultiSelectionInquiryData data = new MultiSelectionInquiryData(((object)new TextObject("{=settings_trainingsettings_selecttargets}Select training troops", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_trainingsettings_selecttargetsdesc}Normally, if a unit can be upgraded to its next tier, Improved Garrison randomly chooses which path to take. Here, you can select wether you want to remove or add troops to specify their upgrade path. This upgrade is NOT affected by your tier restriction. If a unit's upgrade path hasn't been specified by the player, it will still upgrade, but randomly, and only up to the tier restriction. \n> The first list is an overview of the current upgrade paths Improved Garrison is upgrading to. \n> The second list contains all possible upgrade paths for you to select.", (Dictionary<string, object>)null)).ToString(), list, true, 1, 1, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)Inquirydata_SelectPathList, (Action<List<InquiryElement>>)null, "", false);
				Main.ExecuteActionOnNextTick(delegate
				{
					MBInformationManager.ShowMultiSelectionInquiry(data, false, false);
				});
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptFilterForNewTroopsToAdd(Town town, TrainingUIVM trainingDataSource = null)
	{
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Expected O, but got Unknown
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Expected O, but got Unknown
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Expected O, but got Unknown
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Expected O, but got Unknown
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Expected O, but got Unknown
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Expected O, but got Unknown
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Expected O, but got Unknown
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Expected O, but got Unknown
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Expected O, but got Unknown
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Expected O, but got Unknown
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Expected O, but got Unknown
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Expected O, but got Unknown
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Expected O, but got Unknown
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a5: Expected O, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				_currentTown = town;
				if (trainingDataSource != null)
				{
					_trainingDataSource = trainingDataSource;
				}
				List<InquiryElement> list = new List<InquiryElement>();
				TroopTypes.Type type = TroopTypes.Type.Archer;
				TroopTypes.Type type2 = TroopTypes.Type.Infantry;
				TroopTypes.Type type3 = TroopTypes.Type.Cavalary;
				CharacterCode characterCode = CampaignUIHelper.GetCharacterCode(town.Culture.RangedMilitiaTroop, false);
				CharacterCode characterCode2 = CampaignUIHelper.GetCharacterCode(town.Culture.MeleeEliteMilitiaTroop, false);
				CharacterCode characterCode3 = CampaignUIHelper.GetCharacterCode(MBObjectManager.Instance.GetObject<CharacterObject>("aserai_vanguard_faris"), false);
				ImageIdentifier val = (ImageIdentifier)new CharacterImageIdentifier(characterCode);
				if (val == null)
				{
					val = (ImageIdentifier)new EmptyImageIdentifier();
				}
				ImageIdentifier val2 = (ImageIdentifier)new CharacterImageIdentifier(characterCode2);
				if (val2 == null)
				{
					val2 = (ImageIdentifier)new EmptyImageIdentifier();
				}
				ImageIdentifier val3 = (ImageIdentifier)new CharacterImageIdentifier(characterCode3);
				if (val3 == null)
				{
					val3 = (ImageIdentifier)new EmptyImageIdentifier();
				}
				list.Add(new InquiryElement((object)type, ((object)new TextObject("{=settings_trainingsettings_archer}Ranged", (Dictionary<string, object>)null)).ToString(), val));
				list.Add(new InquiryElement((object)type2, ((object)new TextObject("{=settings_trainingsettings_infantry}Infantry", (Dictionary<string, object>)null)).ToString(), val2));
				list.Add(new InquiryElement((object)type3, ((object)new TextObject("{=settings_trainingsettings_cavalary}Cavalry", (Dictionary<string, object>)null)).ToString(), val3));
				MultiSelectionInquiryData val4 = new MultiSelectionInquiryData(((object)new TextObject("{=settings_trainingsettings_filtertype}Filter by type", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_trainingsettings_filtertypedesc}What is the troop type you want to add to the template?", (Dictionary<string, object>)null)).ToString(), list, true, 1, list.Count, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)Inquirydata_SetSpecificUpgradePath, (Action<List<InquiryElement>>)null, "", false);
				MBInformationManager.ShowMultiSelectionInquiry(val4, false, false);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_SetSpecificUpgradePath(List<InquiryElement> list)
	{
		try
		{
			_unitTypeForSpecificUpgrade.Clear();
			if (list != null && list.Count > 0)
			{
				foreach (InquiryElement item in list)
				{
					_unitTypeForSpecificUpgrade.Add((TroopTypes.Type)item.Identifier);
				}
			}
			else
			{
				_unitTypeForSpecificUpgrade.Add(TroopTypes.Type.Infantry);
				_unitTypeForSpecificUpgrade.Add(TroopTypes.Type.Cavalary);
				_unitTypeForSpecificUpgrade.Add(TroopTypes.Type.Archer);
			}
			SpecifyUpgradePath(addNewUnits: true);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_SelectPathList(List<InquiryElement> list)
	{
		try
		{
			if (list == null || list.Count <= 0)
			{
				return;
			}
			InquiryElement randomElement = Extensions.GetRandomElement<InquiryElement>((IReadOnlyList<InquiryElement>)list);
			if (randomElement.Identifier != null && (bool)randomElement.Identifier)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					PromptFilterForNewTroopsToAdd(_currentTown);
				});
			}
			else
			{
				SpecifyUpgradePath(addNewUnits: false);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void SpecifyUpgradePath(bool addNewUnits)
	{
		//IL_01fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Expected O, but got Unknown
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Expected O, but got Unknown
		//IL_04b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04bb: Expected O, but got Unknown
		//IL_04c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d2: Expected O, but got Unknown
		//IL_04e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e7: Expected O, but got Unknown
		//IL_04f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fe: Expected O, but got Unknown
		//IL_0523: Unknown result type (might be due to invalid IL or missing references)
		//IL_0490: Unknown result type (might be due to invalid IL or missing references)
		//IL_049a: Expected O, but got Unknown
		//IL_049f: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ae: Expected O, but got Unknown
		//IL_0279: Unknown result type (might be due to invalid IL or missing references)
		//IL_0283: Expected O, but got Unknown
		//IL_0283: Unknown result type (might be due to invalid IL or missing references)
		//IL_028f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Expected O, but got Unknown
		//IL_029e: Expected O, but got Unknown
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Expected O, but got Unknown
		//IL_0600: Unknown result type (might be due to invalid IL or missing references)
		//IL_060a: Expected O, but got Unknown
		//IL_060f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0614: Unknown result type (might be due to invalid IL or missing references)
		//IL_061e: Expected O, but got Unknown
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Expected O, but got Unknown
		//IL_02d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dc: Expected O, but got Unknown
		//IL_02dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f2: Expected O, but got Unknown
		//IL_02f7: Expected O, but got Unknown
		//IL_02f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fc: Expected O, but got Unknown
		//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0304: Expected O, but got Unknown
		//IL_03a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ae: Expected O, but got Unknown
		//IL_03d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03de: Expected O, but got Unknown
		//IL_03e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ee: Expected O, but got Unknown
		//IL_03fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0407: Expected O, but got Unknown
		//IL_040d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0417: Expected O, but got Unknown
		//IL_042a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0434: Expected O, but got Unknown
		//IL_0351: Unknown result type (might be due to invalid IL or missing references)
		//IL_0358: Expected O, but got Unknown
		//IL_0364: Unknown result type (might be due to invalid IL or missing references)
		//IL_036b: Expected O, but got Unknown
		try
		{
			List<InquiryElement> list = new List<InquiryElement>();
			if (addNewUnits)
			{
				HashSet<CharacterObject> hashSet = new HashSet<CharacterObject>();
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_currentTown);
				foreach (TroopTypes.Type item2 in _unitTypeForSpecificUpgrade)
				{
					IEnumerable<CharacterObject> enumerable = new List<CharacterObject>();
					switch (item2)
					{
					case TroopTypes.Type.Archer:
						enumerable = CharacterObject.FindAll((Predicate<CharacterObject>)delegate(CharacterObject x)
						{
							bool flag3 = ((MBObjectBase)x).StringId.Contains("militia");
							return ((BasicCharacterObject)x).IsRanged && !((BasicCharacterObject)x).IsHero && ((BasicCharacterObject)x).IsSoldier && !flag3;
						});
						break;
					case TroopTypes.Type.Infantry:
						enumerable = CharacterObject.FindAll((Predicate<CharacterObject>)delegate(CharacterObject x)
						{
							bool flag2 = ((MBObjectBase)x).StringId.Contains("militia");
							return ((BasicCharacterObject)x).IsInfantry && !((BasicCharacterObject)x).IsHero && ((BasicCharacterObject)x).IsSoldier && !flag2;
						});
						break;
					case TroopTypes.Type.Cavalary:
						enumerable = CharacterObject.FindAll((Predicate<CharacterObject>)delegate(CharacterObject x)
						{
							bool flag4 = ((MBObjectBase)x).StringId.Contains("militia");
							return ((BasicCharacterObject)x).IsMounted && !((BasicCharacterObject)x).IsHero && ((BasicCharacterObject)x).IsSoldier && !flag4;
						});
						break;
					}
					if (enumerable == null)
					{
						continue;
					}
					foreach (CharacterObject item3 in enumerable)
					{
						hashSet.Add(item3);
					}
				}
				List<InquiryElement> list2 = new List<InquiryElement>();
				Dictionary<CultureObject, List<InquiryElement>> dictionary = new Dictionary<CultureObject, List<InquiryElement>>();
				foreach (CharacterObject item4 in hashSet)
				{
					if (((MBObjectBase)item4).IsInitialized && ((BasicCharacterObject)item4).Name != (TextObject)null && item4.Culture != null)
					{
						CultureObject culture = item4.Culture;
						List<InquiryElement> value;
						bool flag = dictionary.TryGetValue(culture, out value);
						ImageIdentifier val = null;
						try
						{
							val = (ImageIdentifier)new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(item4, false));
						}
						catch (Exception)
						{
						}
						if (val == null)
						{
							val = (ImageIdentifier)new EmptyImageIdentifier();
						}
						InquiryElement item = new InquiryElement((object)item4, ((object)((BasicCharacterObject)item4).Name).ToString(), val);
						if (!flag)
						{
							value = new List<InquiryElement>();
						}
						value.Add(item);
						list2.Add(item);
						if (!flag)
						{
							dictionary.Add(culture, value);
						}
					}
				}
				List<Kingdom> list3 = ((IEnumerable<Kingdom>)Kingdom.All).ToList();
				list.Add(new InquiryElement((object)list2, ((object)new TextObject("{=menu_selectall}Select all", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new EmptyImageIdentifier(), true, ((object)new TextObject("{=hint_select_all_clans}Select all clans in the list", (Dictionary<string, object>)null)).ToString()));
				foreach (KeyValuePair<CultureObject, List<InquiryElement>> item5 in dictionary)
				{
					item5.Value.Insert(0, new InquiryElement((object)item5.Value, ((object)new TextObject("{=menu_selectall}Select all", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new EmptyImageIdentifier(), true, ((object)new TextObject("{=hint_select_all_units}Select all units in this list", (Dictionary<string, object>)null)).ToString()));
					ImageIdentifier val2 = (ImageIdentifier)new EmptyImageIdentifier();
					if (list3 != null)
					{
						foreach (Kingdom item6 in list3)
						{
							if (item6.Culture != null && item6.Culture == item5.Key)
							{
								val2 = (ImageIdentifier)new BannerImageIdentifier(item6.Banner, false);
								if (val2 == null)
								{
									val2 = (ImageIdentifier)new EmptyImageIdentifier();
								}
							}
						}
					}
					list.Add(new InquiryElement((object)item5.Value, ((object)((BasicCultureObject)item5.Key).Name).ToString(), val2));
				}
				MultiSelectionInquiryData data = new MultiSelectionInquiryData(((object)new TextObject("{=settings_trainingsettings_filterculture1}Filter by culture", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_trainingsettings_filterculture2}Please select the culture of your desired troops.", (Dictionary<string, object>)null)).ToString(), list, true, 1, list.Count, ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)PromptClanSpecificUnitsWithPartyManager, (Action<List<InquiryElement>>)null, "", false);
				Main.ExecuteActionOnNextTick(delegate
				{
					MBInformationManager.ShowMultiSelectionInquiry(data, false, false);
				});
				return;
			}
			GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(base.garrisonBehavior.CurrentTownForSettings);
			if (townSettings2.Template == null || townSettings2.Template.AmountOfTroopsInTemplate <= 0)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_template_notargets}There are currently no Improved Garrisons upgrade targets defined.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.red)));
				return;
			}
			MobileParty val3 = new MobileParty();
			val3.Party.SetCustomName(new TextObject("{=settings_trainingsettings_currenttroops}Current troops to train", (Dictionary<string, object>)null));
			((MBObjectBase)val3).StringId = "improvedgarrisons_template_party";
			MobileParty val4 = new MobileParty();
			val4.Party.SetCustomName(new TextObject("{=settings_trainingsettings_troopstoselect}Troops to select", (Dictionary<string, object>)null));
			((MBObjectBase)val4).StringId = "improvedgarrisons_template_party";
			Campaign.Current.Models.PartySizeLimitModel.GetPartyMemberSizeLimit(val4.Party, false);
			foreach (CharacterObject item7 in (List<CharacterObject>)(object)CharacterObject.All)
			{
				if (townSettings2.Template.Contains(item7))
				{
					int num = townSettings2.Template.GetAmountForTemplateTroop(item7);
					if (num <= 0)
					{
						num = 9999;
					}
					val3.AddElementToMemberRoster(item7, num, false);
					val4.AddElementToMemberRoster(item7, 900, false);
				}
			}
			Main.PartyManagement.PromptManagementScreenWithActions(val3.Party, val4, delegate(TroopRoster leftMemberRoster, TroopRoster rightMemberRoster)
			{
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				//IL_0026: Unknown result type (might be due to invalid IL or missing references)
				//IL_0029: Unknown result type (might be due to invalid IL or missing references)
				if (leftMemberRoster != null)
				{
					List<TroopRosterElement> list4 = new List<TroopRosterElement>();
					foreach (TroopRosterElement item8 in (List<TroopRosterElement>)(object)leftMemberRoster.GetTroopRoster())
					{
						list4.Add(item8);
					}
					SetSpecifiedUpgradeTargets(list4);
				}
			}, delegate
			{
			});
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_template_managetargets}In this screen, you can adjust the number of units this garrison should train. You may also remove troops from the list. \n \nOn the left side are the troops this garrison is training towards. On the right side is a copy of the same units that can be added to the left side.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.modMainColor)));
		}
		catch (Exception ex2)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex2);
		}
	}

	private void SetSpecifiedUpgradeTargets(List<TroopRosterElement> list)
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Expected O, but got Unknown
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Expected O, but got Unknown
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01af: Expected O, but got Unknown
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Expected O, but got Unknown
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Expected O, but got Unknown
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0169: Expected O, but got Unknown
		try
		{
			if (list == null)
			{
				return;
			}
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_currentTown);
			if (townSettings == null)
			{
				return;
			}
			townSettings.Template.Clear();
			foreach (TroopRosterElement item in list)
			{
				TroopRosterElement current = item;
				CharacterObject character = current.Character;
				if (((TroopRosterElement)(ref current)).Number > 0)
				{
					townSettings.Template.AddOrUpdateCharacter(current.Character, ((TroopRosterElement)(ref current)).Number);
					GarrisonSettings currentTownSettings = Main.GarrisonBehavior.GetCurrentTownSettings();
				}
			}
			if (base.garrisonBehavior.SettlementSettingsData.TryGetValue(((object)((SettlementComponent)_currentTown).Name).ToString(), out var _))
			{
				base.garrisonBehavior.SettlementSettingsData[((object)((SettlementComponent)_currentTown).Name).ToString()] = townSettings;
			}
			if (townSettings.Template.AmountOfTroopsInTemplate > 0)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_template_newtargets1}The training template for", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Name)?.ToString() + ((object)new TextObject("{=info_template_newtargets2}has been set.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_template_removedtargets1}The garrison of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Name)?.ToString() + ((object)new TextObject("{=info_template_removedtargets1}The garrison of", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.yellow)));
			}
			if (_trainingDataSource != null)
			{
				_trainingDataSource.TroopListIsDirty = true;
				_trainingDataSource = null;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PromptClanSpecificUnitsWithPartyManager(List<InquiryElement> list)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Expected O, but got Unknown
		//IL_021b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0225: Expected O, but got Unknown
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0239: Expected O, but got Unknown
		try
		{
			if (list == null || list.Count <= 0)
			{
				return;
			}
			MobileParty val = new MobileParty();
			val.Party.SetCustomName(new TextObject("{=settings_trainingsettings_currenttroops}Current troops to train", (Dictionary<string, object>)null));
			((MBObjectBase)val).StringId = "improvedgarrisons_template_party";
			MobileParty val2 = new MobileParty();
			val2.Party.SetCustomName(new TextObject("{=settings_trainingsettings_troopstoselect}Troops to select", (Dictionary<string, object>)null));
			((MBObjectBase)val2).StringId = "improvedgarrisons_template_party";
			Campaign.Current.Models.PartySizeLimitModel.GetPartyMemberSizeLimit(val2.Party, false);
			foreach (InquiryElement item in list)
			{
				List<InquiryElement> list2 = (List<InquiryElement>)item.Identifier;
				foreach (InquiryElement item2 in list2)
				{
					if (item2.Title != ((object)new TextObject("{=menu_selectall}Select all", (Dictionary<string, object>)null)).ToString())
					{
						CharacterObject val3 = (CharacterObject)item2.Identifier;
						val2.AddElementToMemberRoster(val3, 900, false);
					}
				}
			}
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(base.garrisonBehavior.CurrentTownForSettings);
			foreach (CharacterObject item3 in (List<CharacterObject>)(object)CharacterObject.All)
			{
				if (townSettings.Template.Contains(item3))
				{
					int num = townSettings.Template.GetAmountForTemplateTroop(item3);
					if (num <= 0)
					{
						num = 9999;
					}
					val.AddElementToMemberRoster(item3, num, false);
				}
			}
			Main.PartyManagement.PromptManagementScreenWithActions(val.Party, val2, delegate(TroopRoster leftMemberRoster, TroopRoster rightMemberRoster)
			{
				//IL_002c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0031: Unknown result type (might be due to invalid IL or missing references)
				//IL_0034: Unknown result type (might be due to invalid IL or missing references)
				if (leftMemberRoster != null && leftMemberRoster.Count > 0)
				{
					List<TroopRosterElement> list3 = new List<TroopRosterElement>();
					foreach (TroopRosterElement item4 in (List<TroopRosterElement>)(object)leftMemberRoster.GetTroopRoster())
					{
						list3.Add(item4);
					}
					SetSpecifiedUpgradeTargets(list3);
				}
			}, delegate
			{
			});
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_template_newtargets_desc}Move the troops that are to be trained by this garrison to the left side of the screen. \nMake sure to select the number you want to have trained. \n \nNote: Improved Garrison will always try to have this number of units in the garrison and will automatically train new units when this number is no longer reached.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.modMainColor)));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_SetUpgradePath(List<InquiryElement> list)
	{
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Expected O, but got Unknown
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Expected O, but got Unknown
		//IL_0218: Unknown result type (might be due to invalid IL or missing references)
		//IL_0222: Expected O, but got Unknown
		//IL_0248: Unknown result type (might be due to invalid IL or missing references)
		//IL_0252: Expected O, but got Unknown
		//IL_0262: Unknown result type (might be due to invalid IL or missing references)
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Expected O, but got Unknown
		if (list == null || list.Count <= 0)
		{
			return;
		}
		try
		{
			Settlement settlement = ((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Settlement;
			if (settlement == null || settlement.Town == null || list == null || list.Count <= 0)
			{
				return;
			}
			bool[] array = new bool[3];
			string text = "";
			foreach (InquiryElement item in list)
			{
				text = text + ((TroopTypes.Type)item.Identifier).ToString() + str_space + ((object)new TextObject("{=menu_and}and", (Dictionary<string, object>)null)).ToString() + str_space;
				switch ((TroopTypes.Type)item.Identifier)
				{
				case TroopTypes.Type.Archer:
					array[0] = true;
					break;
				case TroopTypes.Type.Infantry:
					array[1] = true;
					break;
				case TroopTypes.Type.Cavalary:
					array[2] = true;
					break;
				}
			}
			if (text.Length < 1)
			{
				text = ((object)new TextObject("{=menu_none}none", (Dictionary<string, object>)null)).ToString();
			}
			else
			{
				int length = text.LastIndexOf(str_space + ((object)new TextObject("{=menu_and}and", (Dictionary<string, object>)null)).ToString() + str_space);
				text = text.Substring(0, length);
			}
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(settlement.Town);
			townSettings.TroopsToUpgradeTo = array;
			if (base.garrisonBehavior.SettlementSettingsData.TryGetValue(((object)((SettlementComponent)_currentTown).Name).ToString(), out var _))
			{
				base.garrisonBehavior.SettlementSettingsData[((object)((SettlementComponent)_currentTown).Name).ToString()] = townSettings;
			}
			if (_trainingDataSource != null)
			{
				_trainingDataSource.TroopListIsDirty = true;
				_trainingDataSource = null;
			}
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_path_set}The upgrade paths for", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)settlement.Name)?.ToString() + ((object)new TextObject("{=info_path_set2}has been set to", (Dictionary<string, object>)null)).ToString() + text, Color.FromUint(ModuleColors.green)));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public bool RemoveUpgradeTarget(Town town, CharacterObject character, TrainingUIVM dataSource = null)
	{
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Expected O, but got Unknown
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return false;
			}
			if (dataSource != null)
			{
				_trainingDataSource = dataSource;
			}
			GarrisonSettings settings = Main.GarrisonBehavior.GetTownSettings(town);
			if (settings != null)
			{
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=settings_trainingsettings_removetroop1}Remove template troop", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_trainingsettings_removetroop2}Are you sure you want to remove this troop from the current template?", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_no}No", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					settings.Template.RemoveCharacter(character);
					if (_trainingDataSource != null)
					{
						_trainingDataSource.TroopListIsDirty = true;
						_trainingDataSource = null;
					}
					InformationManager.HideInquiry();
				}, (Action)delegate
				{
					InformationManager.HideInquiry();
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public void SetTownMaxUpgradeTier(Town town, int tier)
	{
		try
		{
			if (CheckIfTownIsValid(town))
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				townSettings.MaxUpgradeTier = tier;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleVanillaTraining(Town town, bool enable)
	{
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				if (enable)
				{
					GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
					townSettings.VanillaTraining = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_vanillatrain_enable}Enabled vanilla training for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.VanillaTraining = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_vanillatrain_disable}Disabled vanilla training for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleTraining(Town town, bool enable)
	{
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				if (enable)
				{
					GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
					townSettings.EnableTraining = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_train_enable}Enabled training for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnableTraining = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_train_disable}Disabled training for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleAutoSpawn(Town town, bool enable)
	{
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Expected O, but got Unknown
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				if (enable)
				{
					GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
					townSettings.RecruiterAutoSpawn = true;
					Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(((SettlementComponent)town).Settlement)?.SetReturnMode();
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_autocreate_enabled}This garrison will now automatically gather the necessary troops for the current template. Make sure to have at least 1 unit for the recruiter to spawn in the garrison of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.RecruiterAutoSpawn = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_autocreate_disabled}Disabled automatic troop gathering for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleFollowTemplate(Town town, bool enable)
	{
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				if (enable)
				{
					GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
					townSettings.RecruitmentFollowsTemplate = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_followtemplate_enable}The Improved Garrison recruitment is now only recruiting troops that are either part of the template or needed for upgrades towards a template troop.", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.RecruitmentFollowsTemplate = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_followtemplate_disable}The Improved Garrison recruitment is no longer only recruiting template-related troops.", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleRemoveNonTemplateTroops(Town town, bool enable)
	{
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				if (enable)
				{
					GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
					townSettings.AutoRemoveNonTemplateTroops = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_removenontemplate_enable}Enabled automatic removal of non template troops for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.AutoRemoveNonTemplateTroops = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_removenontemplate_disable}Disabled automatic removal of non template troops for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
