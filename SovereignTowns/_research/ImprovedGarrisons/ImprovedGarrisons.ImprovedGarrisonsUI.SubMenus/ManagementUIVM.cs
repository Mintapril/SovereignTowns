using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Helpers;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.ManagementUtils;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.OverviewUtils;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;

public class ManagementUIVM : ViewModel
{
	private bool _hasCurrentBuilding;

	private string _currentBuildingVisual;

	private HintViewModel _currentProjectHint;

	private string _currentBuildingProgress;

	private bool _hasQueue = false;

	private bool _hasNoQueue = true;

	private int _construction;

	private int _reserve;

	private BuildingVM _currentProjectVM;

	private Building lastKnownCurrentBuilding;

	private GarrisonSettings CurrentGarrisonSettings => Main.GarrisonBehavior.GetCurrentTownSettings();

	public bool HasCurrentBuilding
	{
		get
		{
			return _hasCurrentBuilding;
		}
		set
		{
			if (value != _hasCurrentBuilding)
			{
				_hasCurrentBuilding = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HasCurrentBuilding");
			}
		}
	}

	public string CurrentBuildingVisual
	{
		get
		{
			return _currentBuildingVisual;
		}
		set
		{
			if (value != _currentBuildingVisual)
			{
				_currentBuildingVisual = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "CurrentBuildingVisual");
			}
		}
	}

	public HintViewModel CurrentProjectHint
	{
		get
		{
			return _currentProjectHint;
		}
		set
		{
			if (value != _currentProjectHint)
			{
				_currentProjectHint = value;
				((ViewModel)this).OnPropertyChangedWithValue<HintViewModel>(value, "CurrentProjectHint");
			}
		}
	}

	public string CurrentBuildingProgress
	{
		get
		{
			return _currentBuildingProgress;
		}
		set
		{
			if (value != _currentBuildingProgress)
			{
				_currentBuildingProgress = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "CurrentBuildingProgress");
			}
		}
	}

	public bool HasQueue
	{
		get
		{
			return _hasQueue;
		}
		set
		{
			if (value != _hasQueue)
			{
				_hasQueue = value;
				HasNoQueue = !value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HasQueue");
			}
		}
	}

	public bool HasNoQueue
	{
		get
		{
			return _hasNoQueue;
		}
		set
		{
			if (value != _hasNoQueue)
			{
				_hasNoQueue = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HasNoQueue");
			}
		}
	}

	public int Construction
	{
		get
		{
			return _construction;
		}
		set
		{
			if (value != _construction)
			{
				_construction = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "Construction");
			}
		}
	}

	public int Reserve
	{
		get
		{
			return _reserve;
		}
		set
		{
			if (value != _reserve)
			{
				_reserve = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "Reserve");
			}
		}
	}

	public BuildingVM CurrentProjectVM
	{
		get
		{
			return _currentProjectVM;
		}
		set
		{
			if (value != _currentProjectVM)
			{
				_currentProjectVM = value;
				((ViewModel)this).OnPropertyChangedWithValue<BuildingVM>(value, "CurrentProjectVM");
			}
		}
	}

	public string ProgressText { get; set; } = ((object)new TextObject("{=ui_managementui_progresstext}Progress", (Dictionary<string, object>)null)).ToString();


	public string QueueText { get; set; } = ((object)new TextObject("{=ui_managementui_queuetext}Queue", (Dictionary<string, object>)null)).ToString();


	public string NoProjectsText { get; set; } = ((object)new TextObject("{=ui_managementui_noqueuetext}No project in queue", (Dictionary<string, object>)null)).ToString();


	public string DailyDefaultText { get; set; } = ((object)new TextObject("{=ui_managementui_dailydefaulttext}Default project", (Dictionary<string, object>)null)).ToString();


	public MBBindingList<ImprovedGarrisonsOptionVM> ManagementSettingsVM { get; set; }

	public MBBindingList<BuildingVM> Buildings { get; set; } = new MBBindingList<BuildingVM>();


	public MBBindingList<BuildingVM> DailyDefaults { get; set; } = new MBBindingList<BuildingVM>();


	public MBBindingList<BuildingVM> CurrentQueue { get; set; } = new MBBindingList<BuildingVM>();


	public string CopyManagementTitle { get; } = ((object)new TextObject("{=ui_managementui_copymanager}Copy manager", (Dictionary<string, object>)null)).ToString();


	public string BuildingsTitle { get; } = ((object)new TextObject("{=ui_managementui_townprojectstext}Town projects", (Dictionary<string, object>)null)).ToString();


	public string GarrisonInformationTitle { get; } = ((object)new TextObject("{=ui_managementui_information}Location information", (Dictionary<string, object>)null)).ToString();


	public MBBindingList<SettlementInformationVM> SettlementInformation { get; set; }

	public MBBindingList<SettlementInformationVM> SettlementBuildingInformation { get; set; }

	public ManagementUIVM()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected O, but got Unknown
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected O, but got Unknown
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Expected O, but got Unknown
		InitializeAll();
		((ViewModel)this).RefreshValues();
	}

	public void InitializeAll()
	{
		InitializeManagementSettings();
		InitializeSettlementInformation();
		InitializeBuildings();
		InitializeTownBuildingStats();
		InitializeDailyDefaults();
	}

	private void InitializeTownBuildingStats()
	{
		SettlementBuildingInformation = new MBBindingList<SettlementInformationVM>();
		if (Main.GarrisonBehavior.CurrentTownForSettings != null)
		{
			((Collection<SettlementInformationVM>)(object)SettlementBuildingInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsConstructionInformation());
			((Collection<SettlementInformationVM>)(object)SettlementBuildingInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsReserveInformation());
		}
	}

	private void InitializeSettlementInformation()
	{
		SettlementInformation = new MBBindingList<SettlementInformationVM>();
		if (Main.GarrisonBehavior.CurrentTownForSettings != null)
		{
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsConstructionInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsReserveInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsGarrisonInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsMilitiaInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsFoodChangeInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsGoldChangeInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsProsperityInformation());
			((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement).SetAsLoyalityInformation());
		}
	}

	private void InitializeManagementSettings()
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Expected O, but got Unknown
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected O, but got Unknown
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Expected O, but got Unknown
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Expected O, but got Unknown
		ManagementSettingsVM = new MBBindingList<ImprovedGarrisonsOptionVM>();
		if (CurrentGarrisonSettings != null)
		{
			((Collection<ImprovedGarrisonsOptionVM>)(object)ManagementSettingsVM).Add(new ImprovedGarrisonsOptionVM().SetAsButtonOption(((object)new TextObject("{=ui_managementui_copycastles1}Copy to all castles", (Dictionary<string, object>)null)).ToString(), delegate
			{
				ManagementSettings.Instance.PromptCopyToAllCastles(Main.GarrisonBehavior.CurrentTownForSettings);
			}, new TextObject("{=ui_managementui_copycastles2}Copy the current garrison settings to all of your castles.", (Dictionary<string, object>)null)));
			((Collection<ImprovedGarrisonsOptionVM>)(object)ManagementSettingsVM).Add(new ImprovedGarrisonsOptionVM().SetAsButtonOption(((object)new TextObject("{=ui_managementui_copytown1}Copy to all towns", (Dictionary<string, object>)null)).ToString(), delegate
			{
				ManagementSettings.Instance.PromptCopyToAllTowns(Main.GarrisonBehavior.CurrentTownForSettings);
			}, new TextObject("{=ui_managementui_copytown2}Copy the current garrison settings to all of your towns.", (Dictionary<string, object>)null)));
			((Collection<ImprovedGarrisonsOptionVM>)(object)ManagementSettingsVM).Add(new ImprovedGarrisonsOptionVM().SetAsButtonOption(((object)new TextObject("{=ui_managementui_copyspecific1}Specific copy", (Dictionary<string, object>)null)).ToString(), delegate
			{
				ManagementSettings.Instance.PromptCopyToSpecificTowns(Main.GarrisonBehavior.CurrentTownForSettings);
			}, new TextObject("{=ui_managementui_copyspecific2}Copy the current garrison settings to a specific town or castle that you own.", (Dictionary<string, object>)null)));
		}
	}

	private void InitializeDailyDefaults()
	{
		DailyDefaults = new MBBindingList<BuildingVM>();
		if (Main.GarrisonBehavior.CurrentTownForSettings == null)
		{
			return;
		}
		Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
		List<Building> buildings = (List<Building>)(object)currentTownForSettings.Buildings;
		foreach (Building item in buildings)
		{
			if (item.BuildingType.IsDailyProject)
			{
				((Collection<BuildingVM>)(object)DailyDefaults).Add(new BuildingVM(item));
			}
		}
	}

	private void InitializeBuildings()
	{
		if (Main.GarrisonBehavior.CurrentTownForSettings == null)
		{
			return;
		}
		Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
		List<Building> buildings = (List<Building>)(object)currentTownForSettings.Buildings;
		Building currentBuilding = currentTownForSettings.CurrentBuilding;
		List<Building> list = currentTownForSettings.BuildingsInProgress.ToList();
		CurrentQueue = new MBBindingList<BuildingVM>();
		if (list != null && list.Count > 1)
		{
			list.RemoveAt(0);
			foreach (Building item in list)
			{
				if (((Collection<BuildingVM>)(object)CurrentQueue).Count > 4)
				{
					break;
				}
				((Collection<BuildingVM>)(object)CurrentQueue).Add(new BuildingVM(item));
			}
			HasQueue = true;
			((ViewModel)this).OnPropertyChanged("CurrentQueue");
		}
		else if (HasQueue)
		{
			((ViewModel)this).OnPropertyChanged("CurrentQueue");
			HasQueue = false;
		}
		if (lastKnownCurrentBuilding != null)
		{
			string text = ((object)currentBuilding.Name).ToString();
			Building obj = lastKnownCurrentBuilding;
			if (text.Equals((obj != null) ? ((object)obj.Name).ToString() : null))
			{
				if (lastKnownCurrentBuilding == null)
				{
					return;
				}
				string text2 = ((object)currentBuilding.Name).ToString();
				Building obj2 = lastKnownCurrentBuilding;
				if (text2.Equals((obj2 != null) ? ((object)obj2.Name).ToString() : null))
				{
					list = currentTownForSettings.BuildingsInProgress.ToList();
					if (list.Count > 0)
					{
						CurrentBuildingProgress = (int)(BuildingHelper.GetProgressOfBuilding(currentBuilding, currentTownForSettings) * 100f) + " %";
					}
					else
					{
						CurrentBuildingProgress = "";
					}
				}
				return;
			}
		}
		list = currentTownForSettings.BuildingsInProgress.ToList();
		if (list.Count > 0)
		{
			CurrentProjectVM = new BuildingVM(currentBuilding);
			CurrentBuildingVisual = CurrentProjectVM.VisualCode;
			CurrentBuildingProgress = (int)(BuildingHelper.GetProgressOfBuilding(currentBuilding, currentTownForSettings) * 100f) + " %";
			HasCurrentBuilding = true;
			CurrentProjectHint = CurrentProjectVM.Hint;
		}
		else
		{
			CurrentProjectVM = null;
			CurrentBuildingProgress = "";
			HasCurrentBuilding = false;
			CurrentBuildingVisual = "";
			CurrentProjectHint = null;
		}
		Buildings = new MBBindingList<BuildingVM>();
		foreach (Building item2 in buildings)
		{
			bool isDailyProject = item2.BuildingType.IsDailyProject;
			bool flag = ((object)currentBuilding.Name).ToString().Equals(((object)item2.Name).ToString());
			if (!isDailyProject && !flag)
			{
				((Collection<BuildingVM>)(object)Buildings).Add(new BuildingVM(item2));
			}
		}
		lastKnownCurrentBuilding = currentBuilding;
		((ViewModel)this).OnPropertyChanged("Buildings");
		((ViewModel)this).OnPropertyChanged("CurrentQueue");
	}

	public void ExecuteCopyToAllTowns()
	{
		try
		{
			ManagementSettings.Instance.PromptCopyToAllTowns(Main.GarrisonBehavior.CurrentTownForSettings);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ExecuteCopyToAllCastles()
	{
		try
		{
			ManagementSettings.Instance.PromptCopyToAllCastles(Main.GarrisonBehavior.CurrentTownForSettings);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ExecuteCopyToSpecific()
	{
		try
		{
			ManagementSettings.Instance.PromptCopyToSpecificTowns(Main.GarrisonBehavior.CurrentTownForSettings);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		BuildingVM.EnsureSpritesLoaded();
		foreach (SettlementInformationVM item in (Collection<SettlementInformationVM>)(object)SettlementInformation)
		{
			((ViewModel)item).RefreshValues();
		}
		foreach (SettlementInformationVM item2 in (Collection<SettlementInformationVM>)(object)SettlementBuildingInformation)
		{
			((ViewModel)item2).RefreshValues();
		}
		foreach (BuildingVM item3 in (Collection<BuildingVM>)(object)DailyDefaults)
		{
			((ViewModel)item3).RefreshValues();
		}
		InitializeBuildings();
	}
}
