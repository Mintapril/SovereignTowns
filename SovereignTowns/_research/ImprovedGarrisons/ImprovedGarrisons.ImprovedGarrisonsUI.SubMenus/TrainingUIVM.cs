using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;

public class TrainingUIVM : ViewModel
{
	private string _estimatedTemplateCosts;

	private GarrisonSettings CurrentGarrisonSettings => Main.GarrisonBehavior.GetCurrentTownSettings();

	public MBBindingList<ImprovedGarrisonsTroopItemWidgetVM> Troops { get; set; }

	public MBBindingList<ImprovedGarrisonsOptionVM> TrainingSettingsVM { get; set; }

	public bool TroopListIsDirty { get; set; } = true;


	public ImprovedGarrisonsOptionVM ToggleTraining { get; set; }

	public string CurrentTemplateAddTroopText { get; } = ((object)new TextObject("{=ui_trainingui_addtroops}Add troops", (Dictionary<string, object>)null)).ToString();


	public string CurrentTemplateTitleText { get; } = ((object)new TextObject("{=ui_trainingui_currenttemplate}Current template", (Dictionary<string, object>)null)).ToString();


	public string TemplateManagerText { get; } = ((object)new TextObject("{=ui_trainingui_templatemanager}Template manager", (Dictionary<string, object>)null)).ToString();


	public string TrainingSettingsText { get; } = ((object)new TextObject("{=ui_trainingui_trainingsettings}Training settings", (Dictionary<string, object>)null)).ToString();


	public HintViewModel AddTroopsButtonHint { get; } = new HintViewModel(new TextObject("{=ui_trainingui_addtroopshint}Add new troops to the training template.\n\nA training template is used to define the upgrade path this garrison will take when training troops. The troops you select here are not affected by the training tier restriction.\n\n Use a training template to compose your army as you want it. You can set the number of troops that should be trained for each upgrade target you define. You could, for example, define the number of infantry, ranged or cavalry troops your garrison should have.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel TemplateManagerButtonHint { get; } = new HintViewModel(new TextObject("{=ui_trainingui_templatemanagerhint}A training template is used to define the upgrade path this garrison will take when training troops. The troops you select here are not affected by the training tier restriction.\n \nUse a training template to compose your army as you want it. You can set the number of troops that should be trained for each upgrade target you define. You could, for example, define the number of infantry, ranged or cavalry troops your garrison should have.\n \nThe template manager is used to save, apply, inspect or remove your training templates. Your training templates are synchronized across your garrisons and game saves.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel EstimatedCostsHint { get; } = new HintViewModel(new TextObject("{=ui_trainingui_dailywagetemplatehint}The daily wage for each troop in your template.", (Dictionary<string, object>)null), (string)null);


	public string EstimatedTemplateCosts
	{
		get
		{
			return _estimatedTemplateCosts;
		}
		set
		{
			if (value != _estimatedTemplateCosts)
			{
				_estimatedTemplateCosts = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "EstimatedTemplateCosts");
			}
		}
	}

	public TrainingUIVM()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected O, but got Unknown
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Expected O, but got Unknown
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected O, but got Unknown
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Expected O, but got Unknown
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		InitializeAll();
		((ViewModel)this).RefreshValues();
	}

	public void InitializeAll()
	{
		InitializeTrainingSettings();
		InitializeTroops();
	}

	private void InitializeTrainingSettings()
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected O, but got Unknown
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Expected O, but got Unknown
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected O, but got Unknown
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Expected O, but got Unknown
		TrainingSettingsVM = new MBBindingList<ImprovedGarrisonsOptionVM>();
		if (CurrentGarrisonSettings != null)
		{
			((Collection<ImprovedGarrisonsOptionVM>)(object)TrainingSettingsVM).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_recruitmentui_vanillatraining}Vanilla training", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.VanillaTraining, delegate(bool x)
			{
				TrainingSettings.Instance.ToggleVanillaTraining(Main.GarrisonBehavior.CurrentTownForSettings, x);
			}, new TextObject("{=ui_recruitmentui_vanillatraining2}Enable the garrison training of the base game. \n \nThis setting sets the garrison wage limit to 0 when disabled, which stops the wage control of the base game. The vanilla training is NOT controlled by Improved Garrison and may conflict with your template settings. If you enable this setting, the base game will downgrade and upgrade units without the control of Improved Garrison. \n \nIt is highly recommended to keep this option disabled for a better control of your garrison.", (Dictionary<string, object>)null)));
			((Collection<ImprovedGarrisonsOptionVM>)(object)TrainingSettingsVM).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_trainingui_garrisontraining1}Improved garrison training", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.EnableTraining, delegate(bool x)
			{
				TrainingSettings.Instance.ToggleTraining(Main.GarrisonBehavior.CurrentTownForSettings, x);
			}, new TextObject("{=ui_trainingui_garrisontraining2}Allow this garrison to train its troops. \n \nYou may select the maximum tier the garrison should train to and/or set a training template to determine the upgrade paths.", (Dictionary<string, object>)null)));
			((Collection<ImprovedGarrisonsOptionVM>)(object)TrainingSettingsVM).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_trainingui_maxupgradetier1}Max upgrade tier for troops \n (Non template)", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.MaxUpgradeTier, 1f, 10f, discrete: true, delegate(float x)
			{
				TrainingSettings.Instance.SetTownMaxUpgradeTier(Main.GarrisonBehavior.CurrentTownForSettings, (int)x);
			}, new TextObject("{=ui_trainingui_maxupgradetier2}The maximum tier this garrison will train troops that are NOT specified in the current training template.", (Dictionary<string, object>)null)));
		}
	}

	private void InitializeTroops()
	{
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		Troops = new MBBindingList<ImprovedGarrisonsTroopItemWidgetVM>();
		if (CurrentGarrisonSettings == null)
		{
			return;
		}
		foreach (CharacterObject item in (List<CharacterObject>)(object)CharacterObject.All)
		{
			if (((MBObjectBase)item).StringId != null && CurrentGarrisonSettings.Template.Contains(item))
			{
				int num = CurrentGarrisonSettings.Template.GetAmountForTemplateTroop(item);
				if (num <= 0)
				{
					num = 9999;
				}
				TroopRosterElement val = new TroopRosterElement(item);
				((TroopRosterElement)(ref val)).Number = num;
				TroopRosterElement troop = val;
				((Collection<ImprovedGarrisonsTroopItemWidgetVM>)(object)Troops).Add(new ImprovedGarrisonsTroopItemWidgetVM(troop, this));
			}
		}
		((ViewModel)this).OnPropertyChanged("Troops");
	}

	public void OpenTemplateManager()
	{
		try
		{
			TemplateManager.Instance.PromptTemplateManager(Main.GarrisonBehavior.CurrentTownForSettings, this);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void AddNewCurrentTemplateTroop()
	{
		try
		{
			TrainingSettings.Instance.PromptFilterForNewTroopsToAdd(Main.GarrisonBehavior.CurrentTownForSettings, this);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void CalculateTemplateWage()
	{
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Expected O, but got Unknown
		try
		{
			int num = 0;
			Dictionary<CharacterObject, int> troopListAsCharacterObjects = CurrentGarrisonSettings.Template.GetTroopListAsCharacterObjects();
			if (troopListAsCharacterObjects != null)
			{
				foreach (KeyValuePair<CharacterObject, int> item in troopListAsCharacterObjects)
				{
					num += item.Key.TroopWage * item.Value;
				}
			}
			EstimatedTemplateCosts = ((object)new TextObject("{=ui_trainingui_dailywagetext}Daily wage:", (Dictionary<string, object>)null)).ToString() + " " + num;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		if (TroopListIsDirty)
		{
			InitializeTroops();
			TroopListIsDirty = false;
			CalculateTemplateWage();
		}
	}
}
