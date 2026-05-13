using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;

public class ImprovedGarrisonsTroopItemWidgetVM : ViewModel
{
	private int _currentAmount;

	private int _heroHealthPercent;

	private CharacterImageIdentifierVM _visual;

	private bool _isTroopHero;

	private string _name;

	private string _amountText;

	private StringItemWithHintVM _tierIconData;

	private StringItemWithHintVM _typeIconData;

	private TrainingUIVM _trainingDatasource;

	private ManagementUIVM _managementDatasource;

	public HintViewModel RemoveHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonwidget_removetroop}Right-click to remove a troop type from a template.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel TemplateAmountHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonwidget_currentamount}The current amount of this troop type in the template.", (Dictionary<string, object>)null), (string)null);


	public HintViewModel GarrisonAmountHint { get; } = new HintViewModel(new TextObject("{=ui_improvedgarrisonwidget_currentgarrisonamount}The current amount of this troop type in the garrison.", (Dictionary<string, object>)null), (string)null);


	public TroopRosterElement CurrentTroop { get; set; }

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (value != _name)
			{
				_name = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Name");
			}
		}
	}

	public int CurrentAmount
	{
		get
		{
			return _currentAmount;
		}
		set
		{
			if (value != _currentAmount)
			{
				_currentAmount = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "CurrentAmount");
			}
		}
	}

	public bool IsTroopHero
	{
		get
		{
			return _isTroopHero;
		}
		set
		{
			if (value != _isTroopHero)
			{
				_isTroopHero = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "IsTroopHero");
			}
		}
	}

	public int HeroHealthPercent
	{
		get
		{
			return _heroHealthPercent;
		}
		set
		{
			if (value != _heroHealthPercent)
			{
				_heroHealthPercent = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HeroHealthPercent");
			}
		}
	}

	public CharacterImageIdentifierVM Visual
	{
		get
		{
			return _visual;
		}
		set
		{
			if (value != _visual)
			{
				_visual = value;
				((ViewModel)this).OnPropertyChangedWithValue<CharacterImageIdentifierVM>(value, "Visual");
			}
		}
	}

	public StringItemWithHintVM TierIconData
	{
		get
		{
			return _tierIconData;
		}
		set
		{
			if (value != _tierIconData)
			{
				_tierIconData = value;
				((ViewModel)this).OnPropertyChangedWithValue<StringItemWithHintVM>(value, "TierIconData");
			}
		}
	}

	public StringItemWithHintVM TypeIconData
	{
		get
		{
			return _typeIconData;
		}
		set
		{
			if (value != _typeIconData)
			{
				_typeIconData = value;
				((ViewModel)this).OnPropertyChangedWithValue<StringItemWithHintVM>(value, "TypeIconData");
			}
		}
	}

	public ImprovedGarrisonsTroopItemWidgetVM(TroopRosterElement troop, TrainingUIVM trainingDatasource = null, ManagementUIVM managementDatasource = null)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Expected O, but got Unknown
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Expected O, but got Unknown
		CurrentTroop = troop;
		_trainingDatasource = trainingDatasource;
		_managementDatasource = managementDatasource;
		TroopRosterElement currentTroop = CurrentTroop;
		CurrentAmount = ((TroopRosterElement)(ref currentTroop)).Number;
		Name = ((object)((BasicCharacterObject)CurrentTroop.Character).Name).ToString();
		IsTroopHero = ((BasicCharacterObject)CurrentTroop.Character).IsHero;
		HeroHealthPercent = (((BasicCharacterObject)CurrentTroop.Character).IsHero ? ((int)Math.Ceiling((double)((float)CurrentTroop.Character.HeroObject.HitPoints / (float)((BasicCharacterObject)CurrentTroop.Character).MaxHitPoints()) * 100.0)) : 0);
		CharacterImageIdentifierVM visual = null;
		try
		{
			visual = new CharacterImageIdentifierVM(CampaignUIHelper.GetCharacterCode(CurrentTroop.Character, false));
		}
		catch (Exception)
		{
		}
		Visual = visual;
		TierIconData = CampaignUIHelper.GetCharacterTierData(CurrentTroop.Character, false);
		TypeIconData = CampaignUIHelper.GetCharacterTypeData(CurrentTroop.Character, false);
		((ViewModel)this).RefreshValues();
	}

	private void ExecuteAdd()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			GarrisonSettings currentTownSettings = Main.GarrisonBehavior.GetCurrentTownSettings();
			if (currentTownSettings != null)
			{
				currentTownSettings.Template.AddOrUpdateCharacter(CurrentTroop.Character, currentTownSettings.Template.GetAmountForTemplateTroop(CurrentTroop.Character) + 1);
				_trainingDatasource.TroopListIsDirty = true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void ExecuteRemove()
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			GarrisonSettings currentTownSettings = Main.GarrisonBehavior.GetCurrentTownSettings();
			if (currentTownSettings == null)
			{
				return;
			}
			if (!currentTownSettings.Template.Contains(CurrentTroop.Character))
			{
				_trainingDatasource.TroopListIsDirty = true;
				return;
			}
			currentTownSettings.Template.AddOrUpdateCharacter(CurrentTroop.Character, currentTownSettings.Template.GetAmountForTemplateTroop(CurrentTroop.Character) - 1);
			if (currentTownSettings.Template.GetAmountForTemplateTroop(CurrentTroop.Character) <= 0)
			{
				ExecuteDelete();
			}
			_trainingDatasource.TroopListIsDirty = true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void ExecuteDelete()
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			bool flag = TrainingSettings.Instance.RemoveUpgradeTarget(Main.GarrisonBehavior.CurrentTownForSettings, CurrentTroop.Character, _trainingDatasource);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
	}
}
