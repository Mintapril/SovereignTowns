using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;

public class RecruitmentUIVM : ViewModel
{
	private MBBindingList<ImprovedGarrisonsOptionVM> _recruitmentSettings;

	private MBBindingList<ImprovedGarrisonsInformationListVM> _recruiterInformation;

	private bool _hasNoActiveRecruiter;

	private bool _hasActiveRecruiter;

	private string _recruiterStatus;

	private GarrisonSettings CurrentGarrisonSettings => Main.GarrisonBehavior.GetCurrentTownSettings();

	private GarrisonRecruiter CurrentRecruiter
	{
		get
		{
			Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
			if (currentTownForSettings != null)
			{
				return Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(((SettlementComponent)currentTownForSettings).Settlement);
			}
			return null;
		}
	}

	public string RecruiterInfoText { get; } = ((object)new TextObject("{=ui_recruitmentui_recruiterinfo}Recruiter information", (Dictionary<string, object>)null)).ToString();


	public string CreateRecruiterText { get; } = ((object)new TextObject("{=ui_recruitmentui_createrecruiter}Create a new recruiter", (Dictionary<string, object>)null)).ToString();


	public string RegionRecruitmentInfoText { get; } = ((object)new TextObject("{=ui_recruitmentui_regionrecruitment}Recruitment settings", (Dictionary<string, object>)null)).ToString();


	public string ReturnRecruiterText { get; } = ((object)new TextObject("{=ui_recruitmentui_returnrecruiter}Return recruiter", (Dictionary<string, object>)null)).ToString();


	public HintViewModel RecruiterHint { get; } = new HintViewModel(new TextObject("{=ui_recruitmentui_recruiterdesc}A recruiter is used to recruit new troops from outside this fief's region.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel RegionRecruitmentHint { get; } = new HintViewModel(new TextObject("{=ui_recruitmentui_regionrecruitmentdesc}The garrison will recruit troops from nearby villages and the current castle or town. \nTo recruit outside of this region, use a recruiter party.", (Dictionary<string, object>)null), (string)null);


	public MBBindingList<ImprovedGarrisonsOptionVM> RecruitmentSettings
	{
		get
		{
			return _recruitmentSettings;
		}
		set
		{
			if (value != _recruitmentSettings)
			{
				_recruitmentSettings = value;
				((ViewModel)this).OnPropertyChangedWithValue<MBBindingList<ImprovedGarrisonsOptionVM>>(value, "RecruitmentSettings");
			}
		}
	}

	private MBBindingList<ImprovedGarrisonsOptionVM> RecruitmentSettingsWithTemplate { get; set; }

	private MBBindingList<ImprovedGarrisonsOptionVM> RecruitmentSettingsNonTemplate { get; set; }

	public MBBindingList<ImprovedGarrisonsInformationListVM> RecruiterInformation
	{
		get
		{
			return _recruiterInformation;
		}
		set
		{
			if (value != _recruiterInformation)
			{
				_recruiterInformation = value;
				((ViewModel)this).OnPropertyChangedWithValue<MBBindingList<ImprovedGarrisonsInformationListVM>>(value, "RecruiterInformation");
			}
		}
	}

	public ImprovedGarrisonsOptionVM ToggleRegionRecruitment { get; set; }

	public ImprovedGarrisonsOptionVM TogglePrisonerRecruitment { get; set; }

	public ImprovedGarrisonsOptionVM ToggleVanillaRecruitment { get; set; }

	public ImprovedGarrisonsOptionVM ToggleFollowTemplate { get; set; }

	public bool HasNoActiveRecruiter
	{
		get
		{
			return _hasNoActiveRecruiter;
		}
		set
		{
			if (value != _hasNoActiveRecruiter)
			{
				_hasNoActiveRecruiter = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HasNoActiveRecruiter");
				HasActiveRecruiter = !_hasNoActiveRecruiter;
			}
			if (_hasActiveRecruiter == _hasNoActiveRecruiter)
			{
				HasActiveRecruiter = !_hasNoActiveRecruiter;
			}
		}
	}

	public bool HasActiveRecruiter
	{
		get
		{
			return _hasActiveRecruiter;
		}
		set
		{
			if (value != _hasActiveRecruiter)
			{
				_hasActiveRecruiter = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HasActiveRecruiter");
			}
		}
	}

	public string RecruiterStatus
	{
		get
		{
			return _recruiterStatus;
		}
		set
		{
			if (value != _recruiterStatus)
			{
				_recruiterStatus = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "RecruiterStatus");
			}
		}
	}

	public RecruitmentUIVM()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected O, but got Unknown
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected O, but got Unknown
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Expected O, but got Unknown
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Expected O, but got Unknown
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected O, but got Unknown
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Expected O, but got Unknown
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Expected O, but got Unknown
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Expected O, but got Unknown
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Expected O, but got Unknown
		//IL_01bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c5: Expected O, but got Unknown
		//IL_01de: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Expected O, but got Unknown
		//IL_0223: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Expected O, but got Unknown
		ToggleRegionRecruitment = new ImprovedGarrisonsOptionVM();
		ToggleRegionRecruitment.SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_regionrecruitmentenable1}Recruit from region", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.EnableRecruitFromRegion, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ToggleRegionRecruitment(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_regionrecruitmentenable2}Allow this garrison to recruit troops from the villages and the town or castle.", (Dictionary<string, object>)null));
		TogglePrisonerRecruitment = new ImprovedGarrisonsOptionVM();
		TogglePrisonerRecruitment.SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_recruitprisoners1}Recruit prisoners", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.EnablePrisonerRecruitment, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.TogglePrisonerRecruitment(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_recruitprisoners2}Allow this garrison to recruit prisoners that are in this location's dungeon over time.", (Dictionary<string, object>)null));
		ToggleFollowTemplate = new ImprovedGarrisonsOptionVM();
		ToggleFollowTemplate.SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_recruitmentfollowstemplate}Recruitment \n follows template", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.RecruitmentFollowsTemplate, delegate(bool x)
		{
			TrainingSettings.Instance.ToggleFollowTemplate(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_recruitmentfollowstemplate2}This garrison will only recruit troops that are either part of the current training template or are needed for an upgrade towards a template troop.", (Dictionary<string, object>)null));
		ToggleVanillaRecruitment = new ImprovedGarrisonsOptionVM();
		ToggleVanillaRecruitment.SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_vanillarecruitment}Vanilla recruitment", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.VanillaRecruitment, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ToggleVanillaRecruitment(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_vanillarecruitment2}Enable the garrison recruitment of the base game. \n \nThis setting sets the automatic recruitment option of this garrison to false when disabled, which stops the daily recruitment of soldiers to the garrison. The vanilla recruitment is NOT controlled by Improved Garrison and may conflict with your template settings. \n \nIt is highly recommended to keep this option disabled for a better control of your garrison.", (Dictionary<string, object>)null));
		InitializeAll();
		((ViewModel)this).RefreshValues();
	}

	public void InitializeAll()
	{
		InitializeRecruitmentSettings();
		InitializeRecruiterInformation();
	}

	private void InitializeRecruiterInformation()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Expected O, but got Unknown
		RecruiterInformation = new MBBindingList<ImprovedGarrisonsInformationListVM>();
		((Collection<ImprovedGarrisonsInformationListVM>)(object)RecruiterInformation).Add(new ImprovedGarrisonsInformationListVM(((object)new TextObject("{=ui_recruitmentui_partysize}Party size", (Dictionary<string, object>)null)).ToString(), "0", () => (CurrentRecruiter != null) ? CurrentRecruiter.mobileParty.Party.NumberOfAllMembers.ToString() : "0", "General\\Icons\\Party@2x"));
		((Collection<ImprovedGarrisonsInformationListVM>)(object)RecruiterInformation).Add(new ImprovedGarrisonsInformationListVM(((object)new TextObject("{=ui_recruitmentui_recruitedtroops}Recruited troops", (Dictionary<string, object>)null)).ToString(), "0", () => (CurrentRecruiter != null) ? CurrentRecruiter.GetNumberOfRecruited().ToString() : "0", "General\\Icons\\PartyCost@2x"));
		((Collection<ImprovedGarrisonsInformationListVM>)(object)RecruiterInformation).Add(new ImprovedGarrisonsInformationListVM(((object)new TextObject("{=ui_recruitmentui_culture}Culture", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=misc_any}any", (Dictionary<string, object>)null)).ToString(), () => (CurrentGarrisonSettings != null) ? (CurrentGarrisonSettings.RecruitmentFollowsTemplate ? ((object)new TextObject("{=misc_template}template", (Dictionary<string, object>)null)).ToString() : ((CurrentGarrisonSettings.RecruiterCultureToRecruit != null) ? CurrentGarrisonSettings.RecruiterCultureToRecruit : ((object)new TextObject("{=misc_any}any", (Dictionary<string, object>)null)).ToString())) : ((object)new TextObject("{=misc_any}any", (Dictionary<string, object>)null)).ToString(), "General\\Icons\\Walls"));
	}

	private void InitializeRecruitmentSettings()
	{
		InitializeRecruitmentSettingsWithoutTemplate();
		InitializeRecruitmentSettingsWithTemplate();
		if (CurrentGarrisonSettings.RecruitmentFollowsTemplate)
		{
			RecruitmentSettings = RecruitmentSettingsWithTemplate;
		}
		else
		{
			RecruitmentSettings = RecruitmentSettingsNonTemplate;
		}
	}

	private void InitializeRecruitmentSettingsWithoutTemplate()
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Expected O, but got Unknown
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Expected O, but got Unknown
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Expected O, but got Unknown
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Expected O, but got Unknown
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Expected O, but got Unknown
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Expected O, but got Unknown
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Expected O, but got Unknown
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Expected O, but got Unknown
		//IL_0219: Unknown result type (might be due to invalid IL or missing references)
		//IL_0223: Expected O, but got Unknown
		//IL_023a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0244: Expected O, but got Unknown
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_028a: Expected O, but got Unknown
		//IL_02a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ab: Expected O, but got Unknown
		//IL_02db: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Expected O, but got Unknown
		//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0306: Expected O, but got Unknown
		//IL_032b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0335: Expected O, but got Unknown
		RecruitmentSettingsNonTemplate = new MBBindingList<ImprovedGarrisonsOptionVM>();
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsTitle(((object)new TextObject("{=ui_recruitmentui_recruitmentwithouttemplate_regiontitle}Region and prisoner recruitment settings", (Dictionary<string, object>)null)).ToString()));
		int num = 350;
		if (Main.GarrisonBehavior.CurrentTownForSettings != null && ((Fief)Main.GarrisonBehavior.CurrentTownForSettings).GarrisonParty != null)
		{
			num = Main.PartyManagement.GetPartySizeLimit(((Fief)Main.GarrisonBehavior.CurrentTownForSettings).GarrisonParty.Party);
			num = ((num < 0) ? 350 : num);
		}
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_recruitmentui_recruitthreshold1}Maximum number of troops to recruit", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.MaxRecruitThreshold, 0f, num, discrete: true, delegate(float x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.SetRecruitmentThreshold(Main.GarrisonBehavior.CurrentTownForSettings, (int)x);
		}, new TextObject("{=ui_recruitmentui_recruitthreshold2}The maximum number of units this garrison will recruit from the dungeon and its region.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_recruitelite1}Recruit only elite troops from this region", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.RecruitOnlyEliteUnits, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ToggleRecruitOnlyElite(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_recruitelite2}Force this garrison's region recruitment to only recruit elite units.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_prisonerthreshold1}Recruit prisoners above the threshold", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.AllowPrisonerRecruitAboveThreshold, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.TogglePrisonerRecruitmentAboveThreshold(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_prisonerthreshold2}Allow this garrison to recruit prisoners above the region recruitment threshold.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsTitle(((object)new TextObject("{=ui_recruitmentui_recruitmentwithouttemplate_recruitertitle}Region and prisoner recruitment settings", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_recruiteronlyelite1}Recruiter only recruits elite troops", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.RecruiterRecruitOnlyElites, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ToggleRecruiterOnlyElites(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_recruiteronlyelite2}If enabled, this garrison's recruiter will only gather elite units.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_recruitmentui_recruiterrecruitamount1}Recruitment headcount", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.RecruiterRecruitAmount, 1f, 150f, discrete: true, delegate(float x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.SetRecruiterAmountToRecruit(Main.GarrisonBehavior.CurrentTownForSettings, (int)x);
		}, new TextObject("{=ui_recruitmentui_recruiterrecruitamount2}Set the number of troops the recruiter should gather before returning", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_recruiterhorses1}Allow to buy horses", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.RecruiterAllowHorseBuy, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ToggleRecruiterBuyHorses(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_recruiterhorses2}Allow this garrison's recruiter party to buy horses to increase movement speed. \n \n(It can be expensive!)", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsNonTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsButtonOption(((object)new TextObject("{=ui_recruitmentui_recruiterculture1}Change the culture the recruiter recruits from", (Dictionary<string, object>)null)).ToString(), delegate
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.PromptChangeRecruitmentCulture(Main.GarrisonBehavior.CurrentTownForSettings);
		}, new TextObject("{=ui_recruitmentui_recruiterculture2}Change the culture this garrison's recruiter should recruit from.", (Dictionary<string, object>)null)));
	}

	private void InitializeRecruitmentSettingsWithTemplate()
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected O, but got Unknown
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Expected O, but got Unknown
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Expected O, but got Unknown
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Expected O, but got Unknown
		//IL_016d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Expected O, but got Unknown
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Expected O, but got Unknown
		RecruitmentSettingsWithTemplate = new MBBindingList<ImprovedGarrisonsOptionVM>();
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsWithTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsTitle(((object)new TextObject("{=ui_recruitmentui_recruitmentwithtemplate}Recruitment settings with template", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsWithTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_trainingui_autogather1}Automatic recruiter creation", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.RecruiterAutoSpawn, delegate(bool x)
		{
			TrainingSettings.Instance.ToggleAutoSpawn(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_trainingui_autogather2}Allow this garrison to automatically create recruiters to gather all the necessary troops needed for your current training template. \n \nThis feature makes it incredibly convenient to train your armies. Just enable this setting, set up your training template and wait until the garrison gathers troops from all the necessary cultures and trains the units you desire.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsWithTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_recruitmentui_recruiterrecruitamount1}Recruitment headcount", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.RecruiterRecruitAmount, 1f, 150f, discrete: true, delegate(float x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.SetRecruiterAmountToRecruit(Main.GarrisonBehavior.CurrentTownForSettings, (int)x);
		}, new TextObject("{=ui_recruitmentui_recruiterrecruitamount2}Set the number of troops the recruiter should gather before returning", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsWithTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_prisonertemplate1}Prisoner recruitment ignores template", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.PrisonerRecruitmentIgnoresTemplate, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.TogglePrisonerRecruitmentIgnoresTemplate(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_prisonertemplate2}The garrison will recruit your prisoners even if they are not part of your template.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)RecruitmentSettingsWithTemplate).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_recruiterhorses1}Allow to buy horses", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.RecruiterAllowHorseBuy, delegate(bool x)
		{
			ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ToggleRecruiterBuyHorses(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_recruitmentui_recruiterhorses2}Allow this garrison's recruiter party to buy horses to increase movement speed. \n \n(It can be expensive!)", (Dictionary<string, object>)null)));
	}

	public void OnCreateRecruitButtonPress()
	{
		ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.PromptCreateRecruiter(Main.GarrisonBehavior.CurrentTownForSettings);
	}

	public void OnReturnRecruiterButtonPress()
	{
		ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager.RecruitmentSettings.Instance.ReturnRecruiter(Main.GarrisonBehavior.CurrentTownForSettings);
	}

	public void ExecuteLink(string link)
	{
		Campaign.Current.EncyclopediaManager.GoToLink(link);
	}

	public void ForceFullRefresh()
	{
		InitializeRecruitmentSettings();
		InitializeRecruiterInformation();
	}

	public override void RefreshValues()
	{
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected O, but got Unknown
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Expected O, but got Unknown
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Expected O, but got Unknown
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		((ViewModel)this).RefreshValues();
		HasNoActiveRecruiter = CurrentRecruiter == null;
		if (CurrentRecruiter != null)
		{
			RecruiterStatus = CurrentRecruiter.GetStatusText();
		}
		else
		{
			bool recruiterAutoSpawn = CurrentGarrisonSettings.RecruiterAutoSpawn;
			bool enableTraining = CurrentGarrisonSettings.EnableTraining;
			bool recruitmentFollowsTemplate = CurrentGarrisonSettings.RecruitmentFollowsTemplate;
			Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
			bool flag = currentTownForSettings != null && ((Fief)currentTownForSettings).GarrisonParty != null && ((Fief)currentTownForSettings).GarrisonParty.Party.NumberOfAllMembers > 0;
			TrainingTemplate template = CurrentGarrisonSettings.Template;
			bool flag2 = template != null && template.AmountOfTroopsInTemplate > 0;
			if (!flag)
			{
				RecruiterStatus = ((object)new TextObject("{=ui_recruitmentui_notenoughtroops}Not enough troops to automatically spawn a recruiter party!", (Dictionary<string, object>)null)).ToString();
			}
			else if (recruitmentFollowsTemplate && recruiterAutoSpawn && !enableTraining)
			{
				RecruiterStatus = ((object)new TextObject("{=ui_recruitmentui_trainingdisabled}Training has to be enabled to automatically spawn a recruiter party!", (Dictionary<string, object>)null)).ToString();
			}
			else if (recruitmentFollowsTemplate && recruiterAutoSpawn && !flag2)
			{
				RecruiterStatus = ((object)new TextObject("{=ui_recruitmentui_notemplate}A training template is needed to automatically spawn a recruiter!", (Dictionary<string, object>)null)).ToString();
			}
			else
			{
				RecruiterStatus = ((object)new TextObject("{=ui_recruitmentui_norecruiter}There is no active recruiter party", (Dictionary<string, object>)null)).ToString();
			}
		}
		foreach (ImprovedGarrisonsInformationListVM item in (Collection<ImprovedGarrisonsInformationListVM>)(object)RecruiterInformation)
		{
			((ViewModel)item).RefreshValues();
		}
		if (CurrentGarrisonSettings.RecruitmentFollowsTemplate)
		{
			RecruitmentSettings = RecruitmentSettingsWithTemplate;
		}
		else
		{
			RecruitmentSettings = RecruitmentSettingsNonTemplate;
		}
	}
}
