using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;

public class RecruitmentSettings : ImprovedGarrisonSettings
{
	private int _amountToRecruitForNewRecruiter = 50;

	private CultureObject _cultureToRecruitFromForNewRecruiter = null;

	private Town _recruiterTown;

	private static RecruitmentSettings _instance;

	public static RecruitmentSettings Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new RecruitmentSettings();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public void PromptCreateRecruiter(Town town)
	{
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Expected O, but got Unknown
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Expected O, but got Unknown
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected O, but got Unknown
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Expected O, but got Unknown
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Expected O, but got Unknown
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return;
			}
			if (!base.garrisonBehavior.CurrentTownForSettings.IsUnderSiege)
			{
				if (((Fief)base.garrisonBehavior.CurrentTownForSettings).GarrisonParty != null && ((Fief)base.garrisonBehavior.CurrentTownForSettings).GarrisonParty.MemberRoster.TotalManCount > 0)
				{
					if (!Main.PartyManagement.garrisonRecruiterPartyManagement.SettlementHasARecruiter(((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Settlement))
					{
						PromptAmountSelectorForRecruiter(town);
					}
					else
					{
						InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_dupe}This garrison already has a recruiter party.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
					}
				}
				else
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_emptygarrison}Your garrison is empty. You need at least one unit to create a recruiter.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
				}
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_siege}This garrison is currently under siege. The recruiter can't get out!", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PromptAmountSelectorForRecruiter(Town town)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Expected O, but got Unknown
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected O, but got Unknown
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Expected O, but got Unknown
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		InformationManager.ShowTextInquiry(new TextInquiryData(((object)new TextObject("{=settings_recruitmentsettings_recruiteramount1}Recruitment headcount", (Dictionary<string, object>)null)).ToString(), string.Format(((object)new TextObject("{=settings_recruitmentsettings_recruiteramount2}Set the number of troops the recruiter must recruit before returning", (Dictionary<string, object>)null)).ToString()), true, true, ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<string>)delegate(string amount)
		{
			GarrisonSettings settings = base.garrisonBehavior.GetTownSettings(town);
			_recruiterTown = town;
			if (int.TryParse(amount, out var result2))
			{
				_amountToRecruitForNewRecruiter = result2;
				Main.ExecuteActionOnNextTick(delegate
				{
					if (!settings.RecruitmentFollowsTemplate)
					{
						Main.PartyManagement.garrisonRecruiterPartyManagement.PromptCultureSelection(Inquirydata_SetRecruiterCulture);
					}
					else
					{
						_cultureToRecruitFromForNewRecruiter = null;
						PromptSelectorForRecruiter();
					}
				});
			}
		}, (Action)delegate
		{
			InformationManager.HideInquiry();
		}, false, (Func<string, Tuple<bool, string>>)((string x) => (int.TryParse(x, out var result) && result <= 150 && result > 0) ? new Tuple<bool, string>(item1: true, "") : new Tuple<bool, string>(item1: false, ((object)new TextObject("{=info_recruitment_recruitervalue}Value has to be between 1 and 150", (Dictionary<string, object>)null)).ToString())), "", ""), false, false);
	}

	public void SetRecruiterAmountToRecruit(Town town, int amount)
	{
		try
		{
			if (CheckIfTownIsValid(town))
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				townSettings.RecruiterRecruitAmount = amount;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptChangeRecruitmentCulture(Town town)
	{
		try
		{
			if (CheckIfTownIsValid(town))
			{
				_recruiterTown = town;
				Main.PartyManagement.garrisonRecruiterPartyManagement.PromptCultureSelection(InquiryData_CultureToRecruitFrom);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ReturnRecruiter(Town town)
	{
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Expected O, but got Unknown
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0159: Expected O, but got Unknown
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Expected O, but got Unknown
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Expected O, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected O, but got Unknown
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return;
			}
			if (Main.PartyManagement.garrisonRecruiterPartyManagement.SettlementHasARecruiter(((SettlementComponent)town).Settlement))
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_recruiterTown);
				bool recruiterAutoSpawn = townSettings.RecruiterAutoSpawn;
				townSettings.RecruiterAutoSpawn = false;
				if (recruiterAutoSpawn)
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_disabledautocreation}Disabled automatic recruiter creation for" + ModuleStrings._space + (object)((SettlementComponent)town).Name, (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.yellow)));
					UIManager.Instance.RefreshWholeUI();
				}
				Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(((SettlementComponent)town).Settlement).SetReturnMode();
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_of}The recruiter of", (Dictionary<string, object>)null)).ToString() + str_space + ((object)((SettlementComponent)town).Name)?.ToString() + str_space + ((object)new TextObject("{=info_recruiter_return}is now returning to the garrison.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_norecruiter}The garrison currently has no active recruiter", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.red)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_SetRecruiterCulture(List<InquiryElement> list)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		if (list == null || list.Count <= 0)
		{
			return;
		}
		try
		{
			CultureObject val = (CultureObject)list.First().Identifier;
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_recruiterTown);
			if (val != null)
			{
				_cultureToRecruitFromForNewRecruiter = val;
			}
			else
			{
				_cultureToRecruitFromForNewRecruiter = null;
			}
			townSettings.RecruiterAutoSpawn = false;
			PromptSelectorForRecruiter();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PromptSelectorForRecruiter()
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected O, but got Unknown
		PartyBase val = Main.PartyManagement.garrisonRecruiterPartyManagement.CreateGarrisonRecruiterParty(((SettlementComponent)_recruiterTown).Settlement, ((SettlementComponent)_recruiterTown).Settlement);
		if (val != null)
		{
			Main.PartyManagement.PromptPartyManagementMenu(val, ((Fief)_recruiterTown).GarrisonParty);
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_newrecruiter_desc}Select units for your recruiter party. You need at least one unit to establish the party!", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.modMainColor)));
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_recruiterTown);
			townSettings.RecruiterCultureToRecruit = ((_cultureToRecruitFromForNewRecruiter == null) ? null : ((MBObjectBase)_cultureToRecruitFromForNewRecruiter).StringId);
			townSettings.RecruiterRecruitAmount = _amountToRecruitForNewRecruiter;
			if (base.garrisonBehavior.SettlementSettingsData.TryGetValue(((object)((SettlementComponent)_recruiterTown).Name).ToString(), out var _))
			{
				base.garrisonBehavior.SettlementSettingsData[((object)((SettlementComponent)_recruiterTown).Name).ToString()] = townSettings;
			}
		}
	}

	private void InquiryData_CultureToRecruitFrom(List<InquiryElement> list)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Expected O, but got Unknown
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Expected O, but got Unknown
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Expected O, but got Unknown
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Expected O, but got Unknown
		if (list == null || list.Count <= 0)
		{
			return;
		}
		try
		{
			CultureObject val = (CultureObject)list.First().Identifier;
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_recruiterTown);
			if (val != null)
			{
				townSettings.RecruiterCultureToRecruit = ((MBObjectBase)val).StringId;
			}
			else
			{
				townSettings.RecruiterCultureToRecruit = null;
			}
			if (base.garrisonBehavior.SettlementSettingsData.TryGetValue(((object)((SettlementComponent)_recruiterTown).Name).ToString(), out var _))
			{
				base.garrisonBehavior.SettlementSettingsData[((object)((SettlementComponent)_recruiterTown).Name).ToString()] = townSettings;
			}
			Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(((SettlementComponent)_recruiterTown).Settlement)?.ResetTradeTarget();
			string text = ((object)new TextObject("{=misc_any}any", (Dictionary<string, object>)null)).ToString();
			if (val != null)
			{
				text = ((object)((BasicCultureObject)val).Name).ToString();
			}
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_changeculture1}Changed the recruitment culture to", (Dictionary<string, object>)null)).ToString() + " " + text + " " + ((object)new TextObject("{=info_recruiter_changeculture2}for the recruiter of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)_recruiterTown).Name, Color.FromUint(ModuleColors.green)));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetRecruitmentThreshold(Town town, int threshold)
	{
		try
		{
			if (CheckIfTownIsValid(town) && threshold >= 0)
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
				townSettings.MaxRecruitThreshold = threshold;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleRecruitOnlyElite(Town town, bool enable)
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
					townSettings.RecruitOnlyEliteUnits = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_onlyelites_enable}Enabled only elite recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.RecruitOnlyEliteUnits = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_onlyelites_disable}Disabled only elite recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void TogglePrisonerRecruitmentAboveThreshold(Town town, bool enable)
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
					townSettings.AllowPrisonerRecruitAboveThreshold = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_prison_above_enable}Enabled prisoner recruitment above threshold for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.AllowPrisonerRecruitAboveThreshold = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_prison_above_disable}Disabled prisoner recruitment above threshold for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void TogglePrisonerRecruitment(Town town, bool enable)
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
					townSettings.EnablePrisonerRecruitment = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_prison_enable}Enabled prisoner recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnablePrisonerRecruitment = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_prison_disable}Disabled prisoner recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleVanillaRecruitment(Town town, bool enable)
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
					townSettings.VanillaRecruitment = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_vanillarecruitment_enable}Enabled vanilla recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.VanillaRecruitment = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_vanillarecruitment_disable}Disabled vanilla recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleRegionRecruitment(Town town, bool enable)
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
					townSettings.EnableRecruitFromRegion = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_enable}Enabled region recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.EnableRecruitFromRegion = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_disable}Disabled region recruitment for", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleRecruiterOnlyElites(Town town, bool enable)
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
					townSettings.RecruiterRecruitOnlyElites = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_onlyelite_enabled}Enabled only elite recruitment for the recruiter of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.RecruiterRecruitOnlyElites = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_onlyelite_disabled}Disabled only elite recruitment for the recruiter of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleRecruiterBuyHorses(Town town, bool enable)
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
					townSettings.RecruiterAllowHorseBuy = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_buyhorses_enabled}Enabled horse trading for the recruiter of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.RecruiterAllowHorseBuy = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_buyhorses_disabled}Disabled horse trading for the recruiter of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void TogglePrisonerRecruitmentIgnoresTemplate(Town town, bool enable)
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
					townSettings.PrisonerRecruitmentIgnoresTemplate = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_prisonertemplate_enable}Prisoner recruitment now ignores the training template of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					townSettings2.PrisonerRecruitmentIgnoresTemplate = false;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruit_prisonertemplate_disable}Prisoner recruitment no longer ignores the training template of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.yellow)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ToggleRecruiterAutoSpawn(Town town, bool enable)
	{
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected O, but got Unknown
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				if (enable)
				{
					GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
					bool recruiterAutoSpawn = townSettings.RecruiterAutoSpawn;
					townSettings.RecruiterAutoSpawn = true;
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_recruiter_autocreate_enabled}This garrison will now automatically gather the necessary troops for the current template. Make sure to have at least 1 unit for the recruiter to spawn in the garrison of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)town).Name, Color.FromUint(ModuleColors.green)));
				}
				else
				{
					GarrisonSettings townSettings2 = base.garrisonBehavior.GetTownSettings(town);
					bool recruiterAutoSpawn2 = townSettings2.RecruiterAutoSpawn;
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
}
