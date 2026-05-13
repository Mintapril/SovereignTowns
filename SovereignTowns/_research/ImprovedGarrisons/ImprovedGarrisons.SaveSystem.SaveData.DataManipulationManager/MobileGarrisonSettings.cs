using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.AI.Orders.PartyOrder;
using ImprovedGarrisons.Behaviours;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
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

namespace ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;

public class MobileGarrisonSettings : ImprovedGarrisonSettings
{
	private static MobileGarrisonSettings _instance;

	public static MobileGarrisonSettings Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new MobileGarrisonSettings();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public void PromptCreateMobileGarrison(Town town)
	{
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_0170: Expected O, but got Unknown
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Expected O, but got Unknown
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Expected O, but got Unknown
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Expected O, but got Unknown
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return;
			}
			if (!town.IsUnderSiege)
			{
				if (((Fief)town).GarrisonParty != null && ((Fief)town).GarrisonParty.MemberRoster.TotalManCount > 0)
				{
					MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)town).Settlement);
					if (mobileGarrisonPartyOfSettlement != null)
					{
						if (mobileGarrisonPartyOfSettlement.mobileParty.CurrentSettlement == null || mobileGarrisonPartyOfSettlement.mobileParty.CurrentSettlement.Town != town)
						{
							InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_dupe}This garrison already has a guard party. Please order it to return first.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
							return;
						}
						mobileGarrisonPartyOfSettlement.SetReturnMode();
					}
					PartyBase val = Main.PartyManagement.mobileGarrisonManagement.CreateMobileGarrison(((SettlementComponent)town).Settlement, ((SettlementComponent)town).Settlement);
					if (val != null)
					{
						Main.PartyManagement.PromptPartyManagementMenu(val, ((Fief)town).GarrisonParty);
						InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_new}Please select the troops for your new guard party. \nThe more troops you choose, the slower your guard party will be!\nIf you want your guards to have the upper hand on looters and bandits, the party size should be around 30. By default, the guard party will patrol your region. You can give them different orders in the Improved Garrisons menu.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.modMainColor)));
					}
				}
				else
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_emptygarrison}Your garrison is empty.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
				}
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_undersiege}This location is currently under siege. The guard party can't get out!", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptMobileGarrisonEscort(Town town)
	{
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e1: Expected O, but got Unknown
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c5: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Expected O, but got Unknown
		//IL_0217: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Expected O, but got Unknown
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Expected O, but got Unknown
		//IL_023b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0245: Expected O, but got Unknown
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_025f: Expected O, but got Unknown
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return;
			}
			if (Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Settlement) == null)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_noguards}This garrison has no guard party.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
			}
			else
			{
				if (town == null || ((SettlementComponent)town).Owner == null || ((SettlementComponent)town).Owner.Owner == null || ((SettlementComponent)town).Owner.Owner.Clan == null)
				{
					return;
				}
				List<InquiryElement> list = new List<InquiryElement>();
				List<MobileParty> allClanParties = GarrisonPartyBehavior.GetAllClanParties(((SettlementComponent)town).Owner.Owner.Clan);
				if (allClanParties == null)
				{
					return;
				}
				foreach (MobileParty item in allClanParties)
				{
					if (item != null && !item.IsGarrison && (!item.IsMilitia || (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(item) && !Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(item))) && !item.IsVillager && item != Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)town).Settlement).getMobileParty())
					{
						CharacterObject val = ((item.LeaderHero == null) ? Extensions.GetRandomElement<TroopRosterElement>(item.MemberRoster.GetTroopRoster()).Character : item.LeaderHero.CharacterObject);
						ImageIdentifier val2 = null;
						try
						{
							val2 = (ImageIdentifier)new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(val, false));
						}
						catch (Exception)
						{
						}
						if (val2 == null)
						{
							val2 = (ImageIdentifier)new EmptyImageIdentifier();
						}
						list.Add(new InquiryElement((object)item, ((object)item.Name).ToString(), val2));
					}
				}
				MultiSelectionInquiryData val3 = new MultiSelectionInquiryData(((object)new TextObject("{=settings_managementsettings_selectionescort1}Escort selection", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_managementsettings_selectionescort2}Select the party that should be supported", (Dictionary<string, object>)null)).ToString(), list, true, 1, 1, ((object)new TextObject("{=menu_ok}Ok", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)Inquirydata_MobileGarrisonEscort, (Action<List<InquiryElement>>)null, "", false);
				MBInformationManager.ShowMultiSelectionInquiry(val3, false, false);
			}
		}
		catch (Exception ex2)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex2);
		}
	}

	public void OrderMobileGarrisonToPatrol(Town town)
	{
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Expected O, but got Unknown
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected O, but got Unknown
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)town).Settlement);
				if (mobileGarrisonPartyOfSettlement == null)
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_noguards}This garrison has no guard party.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
					return;
				}
				mobileGarrisonPartyOfSettlement.GiveAndExecuteOrder(new OrderPatrol(((SettlementComponent)town).Settlement));
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_patrol_new}The guard party of", (Dictionary<string, object>)null)).ToString() + str_space + ((object)((SettlementComponent)town).Name)?.ToString() + str_space + ((object)new TextObject("{=info_patrol_new2}is now patrolling the region.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void OrderMobileGarrisonReturn(Town town)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Expected O, but got Unknown
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Expected O, but got Unknown
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return;
			}
			MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)town).Settlement);
			if (mobileGarrisonPartyOfSettlement == null)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_noguards}This garrison has no guard party.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
				return;
			}
			if (mobileGarrisonPartyOfSettlement.homeGarrisonSettings.GuardsAutoSpawn)
			{
				mobileGarrisonPartyOfSettlement.homeGarrisonSettings.GuardsAutoSpawn = false;
				UIManager.Instance.improvedGarrisonsUI.UpdateUiContents();
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_autoguardsstopped}The automatic creation of guard parties has been disabled.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.yellow)));
			}
			mobileGarrisonPartyOfSettlement.SetReturnMode();
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_return_new}The guard party of", (Dictionary<string, object>)null)).ToString() + str_space + ((object)((SettlementComponent)town).Name)?.ToString() + str_space + ((object)new TextObject("{=info_return_new2}is now returning to the garrison.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void OrderMobileGarrisonAttackOrDefend(Town town)
	{
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Expected O, but got Unknown
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)town).Settlement);
				if (mobileGarrisonPartyOfSettlement == null)
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_noguards}This garrison has no guard party.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
					return;
				}
				mobileGarrisonPartyOfSettlement.SetReturnMode();
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_return_new}The guard party of", (Dictionary<string, object>)null)).ToString() + str_space + ((object)((SettlementComponent)town).Name)?.ToString() + str_space + ((object)new TextObject("{=info_return_new2}is now returning to the garrison.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetReturnPercentage(Town town, float x)
	{
		try
		{
			if (CheckIfTownIsValid(town))
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				townSettings.GuardReturnPercentage = x;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetAutoGarrisonThreshold(Town town, int x)
	{
		try
		{
			if (CheckIfTownIsValid(town))
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				townSettings.GuardsAutoSpawnThreshold = x;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetAutoGarrisonSize(Town town, int x)
	{
		try
		{
			if (CheckIfTownIsValid(town))
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				townSettings.GuardsAutoSpawnSize = x;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_MobileGarrisonEscort(List<InquiryElement> list)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Expected O, but got Unknown
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Expected O, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected O, but got Unknown
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Expected O, but got Unknown
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		try
		{
			if (list != null && list.Count > 0)
			{
				MobileParty val = (MobileParty)Extensions.GetRandomElement<InquiryElement>((IReadOnlyList<InquiryElement>)list).Identifier;
				MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Settlement);
				if (mobileGarrisonPartyOfSettlement != null)
				{
					mobileGarrisonPartyOfSettlement.GiveAndExecuteOrder(new OrderEscort(val));
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_escort_new}The guard party of", (Dictionary<string, object>)null)).ToString() + str_space + ((object)((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Name)?.ToString() + str_space + ((object)new TextObject("{=info_escort_new2}is now escorting", (Dictionary<string, object>)null)).ToString() + str_space + ((object)val.Name).ToString(), Color.FromUint(ModuleColors.green)));
				}
				else
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_noguards}This garrison has no guard party.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void TogglePrisonerSell(Town town, bool enable)
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
					townSettings.EnablePrisonerSell = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_sell_enable}Enabled prisoner trade for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnablePrisonerSell = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_sell_disable}Disabled prisoner trade for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleAutoGuards(Town town, bool enable)
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
					townSettings.GuardsAutoSpawn = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_mobilegarrisonsettings_autoguardcreation1}Enabled automatic guard creation for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.GuardsAutoSpawn = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_mobilegarrisonsettings_autoguardcreation2}Disabled automatic guard creation for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleAutoGuardDefend(Town town, bool enable)
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
					townSettings.GuardsAutoSpawnToDefend = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_mobilegarrisonsettings_autodefend1}Enable automatic guard creation to defend villages for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.GuardsAutoSpawnToDefend = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_mobilegarrisonsettings_autodefend2}Disable automatic guard creation to defend villages for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void TogglePrisonerRecruit(Town town, bool enable)
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
					townSettings.GuardEnablePrisonerRecruitment = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_prisonerrecruit_enable}Enabled prisoner recruitment for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.GuardEnablePrisonerRecruitment = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_prisonerrecruit_disable}Disabled prisoner recruitment for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleUpgrade(Town town, bool enable)
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
					townSettings.GuardEnableUpgradeTroops = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_upgrade_enable}Enabled troops upgrading for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.GuardEnableUpgradeTroops = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_upgrade_disable}Disabled troops upgrading for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleReplenish(Town town, bool enable)
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
					townSettings.EnableReplenish = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_replenish_enable}Enabled replenish and heal for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnableReplenish = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_replenish_disable}Disabled replenish and heal for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleDestroyHideout(Town town, bool enable)
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
					townSettings.EnableHideoutClear = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_hideoutclear1}Enabled hideout clearing for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnableHideoutClear = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_hideoutclear2}Disabled hideout clearing for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleHorseBuy(Town town, bool enable)
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
					townSettings.EnableHorseBuy = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_horsebuy1}Enabled horse trading for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnableHorseBuy = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_guards_horsebuy2}Disabled horse trading for the guards of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
