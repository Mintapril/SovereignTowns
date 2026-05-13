using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.HintManager;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI;

public class ImprovedGarrisonsUIVM : ViewModel
{
	public const string OverviewTabString = "OverviewTab";

	public const string TrainingTabString = "TrainingTab";

	public const string RecruitmentTabString = "RecruitmentTab";

	public const string GuardsTabString = "GuardsTab";

	public const string ManagementTabString = "ManagementTab";

	public const string GarrisonTabString = "GarrisonTab";

	private bool _mapToggleValue = GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap;

	private bool overviewNeedsUpdate;

	private SelectorVM<SelectorItemVM> _playerSettlementsSelector;

	public OverviewUIVM OverviewDatasource { get; set; } = new OverviewUIVM();


	public GuardsUIVM GuardsDatasource { get; set; } = new GuardsUIVM();


	public RecruitmentUIVM RecruitmentDatasource { get; set; } = new RecruitmentUIVM();


	public TrainingUIVM TrainingDatasource { get; set; } = new TrainingUIVM();


	public ManagementUIVM ManagementDatasource { get; set; } = new ManagementUIVM();


	public GarrisonUIVM GarrisonDatasource { get; set; } = new GarrisonUIVM();


	public HintManagerVM HintManagerDatasource { get; set; } = new HintManagerVM();


	public string Title { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_title}Improved Garrisons", (Dictionary<string, object>)null)).ToString();


	public string OverviewTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_overview}Overview", (Dictionary<string, object>)null)).ToString();


	public string RecruitmentTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_recruitment}Recruitment", (Dictionary<string, object>)null)).ToString();


	public string GuardTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_guards}Guards", (Dictionary<string, object>)null)).ToString();


	public string TrainingTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_training}Training", (Dictionary<string, object>)null)).ToString();


	public string ManagementTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_management}Management", (Dictionary<string, object>)null)).ToString();


	public string FinanceTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_finance}Finance", (Dictionary<string, object>)null)).ToString();


	public string GarrisonTabText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_garrison}Garrison", (Dictionary<string, object>)null)).ToString();


	public string MapToggleText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_maptoggle}Display on map", (Dictionary<string, object>)null)).ToString();


	public string TutorialButtonText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_tutorialtext}Tutorial", (Dictionary<string, object>)null)).ToString();


	public string ConfigurationManagerButtonText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_configurationtext}Configuration", (Dictionary<string, object>)null)).ToString();


	public string ResetSettingsButtonText { get; } = ((object)new TextObject("{=ui_improvedgarrisonsui_defaulttext}Default settings", (Dictionary<string, object>)null)).ToString();


	public HintViewModel MapToggleHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonsui_maptogglehint}Display the Improved Garrisons menu on the overworld map.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel TutorialHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonsui_tutorialhint}Start the Improved Garrisons tutorial.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel ConfigmanagerHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonsui_configurationhint}Display the Improved Garrisons settings.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel ResetSettingsHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonsui_resethint}Reset the currently selected location to its default settings.", (Dictionary<string, object>)null), (string)null);


	public bool MapToggleValue
	{
		get
		{
			return _mapToggleValue;
		}
		set
		{
			if (value != _mapToggleValue)
			{
				_mapToggleValue = value;
				((ViewModel)this).OnPropertyChanged("MapToggleValue");
				StayOnMapToggle(value);
			}
		}
	}

	public SelectorVM<SelectorItemVM> PlayerSettlementsSelector
	{
		get
		{
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_0090: Expected O, but got Unknown
			if (_playerSettlementsSelector == null)
			{
				List<Settlement> allPlayerSettlements = Main.GarrisonBehavior.GetAllPlayerSettlements();
				Settlement currentSettlement = Settlement.CurrentSettlement;
				_playerSettlementsSelector = new SelectorVM<SelectorItemVM>(0, (Action<SelectorVM<SelectorItemVM>>)OnSelectorChange);
				bool flag = Clan.PlayerClan != null && Main.GarrisonBehavior.CurrentTownForSettings != null && Clan.PlayerClan == Main.GarrisonBehavior.CurrentTownForSettings.OwnerClan;
				foreach (Settlement item in allPlayerSettlements)
				{
					SelectorItemVM val = new SelectorItemVM(((object)item.Name).ToString());
					_playerSettlementsSelector.AddItem(val);
				}
			}
			if (_playerSettlementsSelector.SelectedIndex < 0 && ((Collection<SelectorItemVM>)(object)_playerSettlementsSelector.ItemList).Count > 0)
			{
				_playerSettlementsSelector.SelectedIndex = 0;
			}
			return _playerSettlementsSelector;
		}
	}

	public ImprovedGarrisonsUIVM()
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Expected O, but got Unknown
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Expected O, but got Unknown
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Expected O, but got Unknown
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Expected O, but got Unknown
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Expected O, but got Unknown
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Expected O, but got Unknown
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Expected O, but got Unknown
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Expected O, but got Unknown
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Expected O, but got Unknown
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Expected O, but got Unknown
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Expected O, but got Unknown
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Expected O, but got Unknown
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Expected O, but got Unknown
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Expected O, but got Unknown
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Expected O, but got Unknown
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Expected O, but got Unknown
		UpdateUiContents();
	}

	public void UpdateUiContents()
	{
		OverviewDatasource = new OverviewUIVM();
		((ViewModel)this).OnPropertyChanged("OverviewDatasource");
		GuardsDatasource = new GuardsUIVM();
		((ViewModel)this).OnPropertyChanged("GuardsDatasource");
		RecruitmentDatasource = new RecruitmentUIVM();
		((ViewModel)this).OnPropertyChanged("RecruitmentDatasource");
		TrainingDatasource = new TrainingUIVM();
		((ViewModel)this).OnPropertyChanged("TrainingDatasource");
		ManagementDatasource = new ManagementUIVM();
		((ViewModel)this).OnPropertyChanged("ManagementDatasource");
		GarrisonDatasource = new GarrisonUIVM();
		((ViewModel)this).OnPropertyChanged("GarrisonDatasource");
	}

	public void ForceOverviewUpdate()
	{
		overviewNeedsUpdate = true;
	}

	public void UpdateSettlementSelector()
	{
		_playerSettlementsSelector = null;
		((ViewModel)this).OnPropertyChanged("PlayerSettlementsSelector");
	}

	public void ChangeSelectorSelectionToCurrentSettlement()
	{
		UpdateSettlementSelector();
		if (Settlement.CurrentSettlement == null || _playerSettlementsSelector == null)
		{
			return;
		}
		Settlement currentSettlement = Settlement.CurrentSettlement;
		MBBindingList<SelectorItemVM> itemList = _playerSettlementsSelector.ItemList;
		for (int i = 0; i < ((Collection<SelectorItemVM>)(object)_playerSettlementsSelector.ItemList).Count; i++)
		{
			if (((Collection<SelectorItemVM>)(object)itemList)[i].StringItem.Equals(((object)currentSettlement.Name).ToString()))
			{
				_playerSettlementsSelector.SelectedIndex = i;
			}
		}
	}

	public void ChangeSelectorSelection(Settlement settlement)
	{
		UpdateSettlementSelector();
		if (settlement == null || _playerSettlementsSelector == null)
		{
			return;
		}
		MBBindingList<SelectorItemVM> itemList = _playerSettlementsSelector.ItemList;
		for (int i = 0; i < ((Collection<SelectorItemVM>)(object)_playerSettlementsSelector.ItemList).Count; i++)
		{
			if (((Collection<SelectorItemVM>)(object)itemList)[i].StringItem.Equals(((object)settlement.Name).ToString()))
			{
				_playerSettlementsSelector.SelectedIndex = i;
			}
		}
	}

	private void OnSelectorChange(SelectorVM<SelectorItemVM> selector)
	{
		try
		{
			if (selector == null)
			{
				return;
			}
			SelectorItemVM currentItem = selector.GetCurrentItem();
			if (currentItem != null)
			{
				Settlement settlementFromName = Main.GarrisonBehavior.GetSettlementFromName(currentItem.StringItem);
				if (settlementFromName != null)
				{
					Main.GarrisonBehavior.CurrentTownForSettings = settlementFromName.Town;
				}
				UpdateUiContents();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private Settlement GetCurrentlySelectedSettlement()
	{
		SelectorItemVM currentItem = PlayerSettlementsSelector.GetCurrentItem();
		if (currentItem == null)
		{
			return null;
		}
		return Main.GarrisonBehavior.GetSettlementFromName(currentItem.StringItem);
	}

	public void OnTutorialButtonClick()
	{
		UIManager.Instance.StartTutorial(onMapTutorial: true);
	}

	public void OnConfigurationButtonClick()
	{
		Main.OpenConfigurationScreen();
	}

	public void OnResetSettingsButtonClick()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Expected O, but got Unknown
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=ui_improvedgarrisonsui_resetdefault_title}Reset the location to its default settings", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=ui_improvedgarrisonsui_resetdefault_description}Are you sure you want to reset the currently selected location to its default settings?", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			Settlement currentlySelectedSettlement = GetCurrentlySelectedSettlement();
			if (currentlySelectedSettlement != null)
			{
				Main.GarrisonBehavior.ResetTownSettings(currentlySelectedSettlement.Town);
			}
			UpdateUiContents();
		}, (Action)delegate
		{
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void StayOnMapToggle(bool value)
	{
		GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap = value;
	}

	public void ForceFullRefresh()
	{
		switch (UIManager.Instance.improvedGarrisonsUI.ActualCurrentTabId)
		{
		case "OverviewTab":
			((ViewModel)OverviewDatasource).RefreshValues();
			break;
		case "TrainingTab":
			TrainingDatasource.InitializeAll();
			break;
		case "RecruitmentTab":
			RecruitmentDatasource.InitializeAll();
			break;
		case "GuardsTab":
			GuardsDatasource.InitializeAll();
			break;
		case "ManagementTab":
			ManagementDatasource.InitializeAll();
			break;
		case "GarrisonTab":
			GarrisonDatasource.InitializeAll();
			break;
		}
	}

	public override void RefreshValues()
	{
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Invalid comparison between Unknown and I4
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Invalid comparison between Unknown and I4
		((ViewModel)this).RefreshValues();
		((ViewModel)HintManagerDatasource).RefreshValues();
		switch (UIManager.Instance.improvedGarrisonsUI.ActualCurrentTabId)
		{
		case "OverviewTab":
			if (((int)Campaign.Current.TimeControlMode != 0 && (int)Campaign.Current.TimeControlMode != 3 && (int)Campaign.Current.TimeControlMode != 6) || overviewNeedsUpdate)
			{
				((ViewModel)OverviewDatasource).RefreshValues();
				overviewNeedsUpdate = false;
			}
			break;
		case "TrainingTab":
			((ViewModel)TrainingDatasource).RefreshValues();
			break;
		case "RecruitmentTab":
			((ViewModel)RecruitmentDatasource).RefreshValues();
			break;
		case "GuardsTab":
			((ViewModel)GuardsDatasource).RefreshValues();
			break;
		case "ManagementTab":
			((ViewModel)ManagementDatasource).RefreshValues();
			break;
		case "GarrisonTab":
			((ViewModel)GarrisonDatasource).RefreshValues();
			break;
		}
	}
}
