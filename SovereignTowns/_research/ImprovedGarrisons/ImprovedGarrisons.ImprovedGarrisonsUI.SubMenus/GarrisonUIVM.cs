using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.ActivityLogging;
using ImprovedGarrisons.ActivityLogging.Activities;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.GarrisonUtils;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.OverviewUtils;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;

public class GarrisonUIVM : ViewModel
{
	private int versionNumber = -1;

	private string _dailyWageOfGarrison;

	private GarrisonSettings CurrentGarrisonSettings => Main.GarrisonBehavior.GetCurrentTownSettings();

	public MBBindingList<ImprovedGarrisonsTroopItemWidgetVM> GarrisonTroops { get; set; }

	private MobileParty CurrentGarrisonParty
	{
		get
		{
			Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
			if (currentTownForSettings != null && ((Fief)currentTownForSettings).GarrisonParty != null)
			{
				return ((Fief)currentTownForSettings).GarrisonParty;
			}
			return null;
		}
	}

	public string DailyWageOfGarrison
	{
		get
		{
			return _dailyWageOfGarrison;
		}
		set
		{
			if (value != _dailyWageOfGarrison)
			{
				_dailyWageOfGarrison = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "DailyWageOfGarrison");
			}
		}
	}

	public MBBindingList<SettlementInformationVM> GarrisonInformation { get; set; }

	public MBBindingList<LogEntryVM> LogEntries { get; set; } = new MBBindingList<LogEntryVM>();


	public string CurrentGarrisonTitle { get; } = ((object)new TextObject("{=ui_garrisonui_currentgarrison}Current garrison", (Dictionary<string, object>)null)).ToString();


	public string TransferTroopsText { get; } = ((object)new TextObject("{=ui_garrisonui_transfer}Transfer troops", (Dictionary<string, object>)null)).ToString();


	public string GarrisonActivityLogText { get; } = ((object)new TextObject("{=ui_garrisonui_garrisonactivity}Garrison activity", (Dictionary<string, object>)null)).ToString();


	public string WeeklyInformationText { get; } = ((object)new TextObject("{=ui_garrisonui_weeklyinformation}Weekly garrison information", (Dictionary<string, object>)null)).ToString();


	public GarrisonUIVM()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected O, but got Unknown
		InitializeAll();
		((ViewModel)this).RefreshValues();
	}

	public void InitializeAll()
	{
		InitializeGarrisonTroops();
		InitializeWeeklyCosts();
		InitializeLogEntries();
	}

	private void InitializeLogEntries()
	{
		bool flag = LogEntries == null || ((Collection<LogEntryVM>)(object)LogEntries).Count <= 0;
		Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
		if (currentTownForSettings == null)
		{
			return;
		}
		ActivityLog activityLog = Main.ActivityLogManager.GetActivityLog(currentTownForSettings);
		if (activityLog == null || !(activityLog.ActivitiesHaveBeenUpdated || flag))
		{
			return;
		}
		LogEntries = new MBBindingList<LogEntryVM>();
		activityLog.ActivitiesHaveBeenUpdated = false;
		foreach (GarrisonActivity activity in activityLog.GetActivities())
		{
			if (!(activity.CampaignDayOfTheActivity == ""))
			{
				((Collection<LogEntryVM>)(object)LogEntries).Add(new LogEntryVM(activity.CampaignDayOfTheActivity, activity.GetLogDescription()));
			}
		}
		((ViewModel)this).OnPropertyChanged("LogEntries");
	}

	private void InitializeWeeklyCosts()
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_0082: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected O, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Expected O, but got Unknown
		//IL_00d5: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Expected O, but got Unknown
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected O, but got Unknown
		//IL_0128: Expected O, but got Unknown
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Expected O, but got Unknown
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Expected O, but got Unknown
		//IL_017b: Expected O, but got Unknown
		try
		{
			GarrisonInformation = new MBBindingList<SettlementInformationVM>();
			Town currentTown = Main.GarrisonBehavior.CurrentTownForSettings;
			if (currentTown == null)
			{
				return;
			}
			((Collection<SettlementInformationVM>)(object)GarrisonInformation).Add(new SettlementInformationVM(((SettlementComponent)currentTown).Settlement).SetAsCustomInformation("General\\Icons\\Party@2x", new HintViewModel(new TextObject("{=ui_garrisonui_weeklycosts1}This week's number of recruited troops.", (Dictionary<string, object>)null), (string)null), ((object)new TextObject("{=ui_garrisonui_weeklycosts2}Recruited troops", (Dictionary<string, object>)null)).ToString(), delegate
			{
				ActivityLog activityLog4 = Main.ActivityLogManager.GetActivityLog(currentTown);
				return (activityLog4 != null) ? activityLog4.WeeklyRecruits.ToString() : "0";
			}));
			((Collection<SettlementInformationVM>)(object)GarrisonInformation).Add(new SettlementInformationVM(((SettlementComponent)currentTown).Settlement).SetAsCustomInformation("General\\Icons\\Coin@2x", new HintViewModel(new TextObject("{=ui_garrisonui_weeklycosts3}This week's recruitment costs", (Dictionary<string, object>)null), (string)null), ((object)new TextObject("{=ui_garrisonui_weeklycosts4}Recruitment costs", (Dictionary<string, object>)null)).ToString(), delegate
			{
				float num = 0f;
				ActivityLog activityLog3 = Main.ActivityLogManager.GetActivityLog(currentTown);
				if (activityLog3 != null)
				{
					foreach (KeyValuePair<string, float> recruiterCost in activityLog3.RecruiterCosts)
					{
						num += recruiterCost.Value;
					}
					num += activityLog3.WeeklyRecruitmentCosts;
				}
				return (activityLog3 != null) ? num.ToString() : "0";
			}));
			((Collection<SettlementInformationVM>)(object)GarrisonInformation).Add(new SettlementInformationVM(((SettlementComponent)currentTown).Settlement).SetAsCustomInformation("General\\Icons\\Party@2x", new HintViewModel(new TextObject("{=ui_garrisonui_weeklycosts5}This week's number of upgraded troops.", (Dictionary<string, object>)null), (string)null), ((object)new TextObject("{=ui_garrisonui_weeklycosts6}Upgraded troops", (Dictionary<string, object>)null)).ToString(), delegate
			{
				ActivityLog activityLog2 = Main.ActivityLogManager.GetActivityLog(currentTown);
				return (activityLog2 != null) ? activityLog2.WeeklyUpgrades.ToString() : "0";
			}));
			((Collection<SettlementInformationVM>)(object)GarrisonInformation).Add(new SettlementInformationVM(((SettlementComponent)currentTown).Settlement).SetAsCustomInformation("General\\Icons\\Coin@2x", new HintViewModel(new TextObject("{=ui_garrisonui_weeklycosts7}This week's training costs", (Dictionary<string, object>)null), (string)null), ((object)new TextObject("{=ui_garrisonui_weeklycosts8}Training costs", (Dictionary<string, object>)null)).ToString(), delegate
			{
				ActivityLog activityLog = Main.ActivityLogManager.GetActivityLog(currentTown);
				return (activityLog != null) ? activityLog.WeeklyTrainingCosts.ToString() : "0";
			}));
			((ViewModel)this).OnPropertyChanged("GarrisonInformation");
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void InitializeGarrisonTroops()
	{
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		if (CurrentGarrisonParty != null && CurrentGarrisonParty.VersionNo == versionNumber)
		{
			return;
		}
		if (CurrentGarrisonParty == null)
		{
			GarrisonTroops = new MBBindingList<ImprovedGarrisonsTroopItemWidgetVM>();
			((ViewModel)this).OnPropertyChanged("GarrisonTroops");
			return;
		}
		GarrisonTroops = new MBBindingList<ImprovedGarrisonsTroopItemWidgetVM>();
		List<TroopRosterElement> list = ((IEnumerable<TroopRosterElement>)CurrentGarrisonParty.MemberRoster.GetTroopRoster()).ToList();
		foreach (TroopRosterElement item in list)
		{
			((Collection<ImprovedGarrisonsTroopItemWidgetVM>)(object)GarrisonTroops).Add(new ImprovedGarrisonsTroopItemWidgetVM(item));
		}
		((ViewModel)this).OnPropertyChanged("GarrisonTroops");
		versionNumber = CurrentGarrisonParty.VersionNo;
	}

	public void ExecuteTransfer()
	{
		try
		{
			ManagementSettings.Instance.PromptTransfer(Main.GarrisonBehavior.CurrentTownForSettings);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void CalculateGarrisonWage()
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected O, but got Unknown
		try
		{
			int num = 0;
			MobileParty currentGarrisonParty = CurrentGarrisonParty;
			if (currentGarrisonParty != null)
			{
				num += currentGarrisonParty.TotalWage;
			}
			DailyWageOfGarrison = ((object)new TextObject("{=ui_trainingui_dailywagetext}Daily wage:", (Dictionary<string, object>)null)).ToString() + " " + num;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		InitializeGarrisonTroops();
		CalculateGarrisonWage();
		InitializeLogEntries();
		foreach (SettlementInformationVM item in (Collection<SettlementInformationVM>)(object)GarrisonInformation)
		{
			((ViewModel)item).RefreshValues();
		}
	}
}
