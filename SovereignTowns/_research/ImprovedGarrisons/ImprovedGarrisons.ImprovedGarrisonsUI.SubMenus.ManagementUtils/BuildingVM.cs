using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Helpers;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.ManagementUtils;

public class BuildingVM : ViewModel
{
	private HintViewModel _hint;

	private string _iconPath;

	private string _tier;

	private bool _tierIsVisible;

	private bool _isDefault;

	private bool _isDaily;

	private string _name;

	private string _visualCode;

	private List<string> romanNumerals = new List<string>
	{
		"M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX",
		"V", "IV", "I"
	};

	private List<int> numerals = new List<int>
	{
		1000, 900, 500, 400, 100, 90, 50, 40, 10, 9,
		5, 4, 1
	};

	private Building currentBuilding;

	private readonly TextObject L1BonusText = new TextObject("{=PJZ8QYgA}L-I : {BONUS}", (Dictionary<string, object>)null);

	private readonly TextObject L2BonusText = new TextObject("{=9i0wnjJK}L-II : {BONUS}", (Dictionary<string, object>)null);

	private readonly TextObject L3BonusText = new TextObject("{=pRP2sOWP}L-III : {BONUS}", (Dictionary<string, object>)null);

	private static SpriteCategory _spriteCategory;

	public HintViewModel Hint
	{
		get
		{
			return _hint;
		}
		set
		{
			if (value != _hint)
			{
				_hint = value;
				((ViewModel)this).OnPropertyChangedWithValue<HintViewModel>(value, "Hint");
			}
		}
	}

	public string Iconpath
	{
		get
		{
			return _iconPath;
		}
		set
		{
			if (value != _iconPath)
			{
				_iconPath = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Iconpath");
			}
		}
	}

	public string Tier
	{
		get
		{
			return _tier;
		}
		set
		{
			if (value != _tier)
			{
				_tier = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Tier");
			}
		}
	}

	public bool TierIsVisible
	{
		get
		{
			return _tierIsVisible;
		}
		set
		{
			if (value != _tierIsVisible)
			{
				_tierIsVisible = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "TierIsVisible");
			}
		}
	}

	public bool IsDefault
	{
		get
		{
			return _isDefault;
		}
		set
		{
			if (value != _isDefault)
			{
				_isDefault = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "IsDefault");
			}
		}
	}

	public bool IsDaily
	{
		get
		{
			return _isDaily;
		}
		set
		{
			if (value != _isDaily)
			{
				_isDaily = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "IsDaily");
			}
		}
	}

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

	public string VisualCode
	{
		get
		{
			return _visualCode;
		}
		set
		{
			if (value != _visualCode)
			{
				_visualCode = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "VisualCode");
			}
		}
	}

	public BuildingVM(Building building)
	{
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Expected O, but got Unknown
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Expected O, but got Unknown
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Expected O, but got Unknown
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0217: Expected O, but got Unknown
		//IL_024d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0257: Expected O, but got Unknown
		//IL_0346: Unknown result type (might be due to invalid IL or missing references)
		//IL_0351: Expected O, but got Unknown
		//IL_034c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0356: Expected O, but got Unknown
		//IL_0330: Unknown result type (might be due to invalid IL or missing references)
		//IL_033a: Expected O, but got Unknown
		if (building == null)
		{
			return;
		}
		currentBuilding = building;
		VisualCode = ((MBObjectBase)building.BuildingType).StringId.ToLower();
		Name = ((object)building.Name).ToString();
		Tier = ToRomanNumeral(building.CurrentLevel);
		if (building.CurrentLevel > 0)
		{
			TierIsVisible = true;
		}
		bool isDailyProject = building.BuildingType.IsDailyProject;
		if (isDailyProject)
		{
			UpdateDefault();
		}
		string text = "";
		if (IsCurrentProject())
		{
			text += ((object)new TextObject("{=ui_buildingui_currentproject}>> Current location project <<", (Dictionary<string, object>)null)).ToString();
		}
		text = text + ((object)building.Explanation)?.ToString() + "\n \n" + ((object)new TextObject("{=ui_buildingui_effects}Effects:", (Dictionary<string, object>)null)).ToString() + "\n";
		if (!isDailyProject)
		{
			for (int i = 1; i <= 3; i++)
			{
				string bonusText = GetBonusText(currentBuilding, i);
				text += ((bonusText != "") ? ("> " + GetBonusText(currentBuilding, i) + "\n") : "");
			}
		}
		else if (Main.GarrisonBehavior.CurrentTownForSettings != null)
		{
			string text2 = ((object)currentBuilding.BuildingType.GetExplanationAtLevel(building.CurrentLevel)).ToString();
			text = text + "> " + text2 + "\n \n" + ((object)new TextObject("{=ui_buildingui_effectshint}Note: this only works if there is no current project.", (Dictionary<string, object>)null)).ToString();
		}
		Hint = new HintViewModel(new TextObject(text, (Dictionary<string, object>)null), (string)null);
	}

	public static void EnsureSpritesLoaded()
	{
		LoadBuildingSprites();
	}

	private static void LoadBuildingSprites()
	{
		try
		{
			_spriteCategory = UIResourceManager.LoadSpriteCategory("ui_town_management");
		}
		catch (Exception)
		{
		}
	}

	private void UpdateDefault()
	{
		if (Main.GarrisonBehavior.CurrentTownForSettings != null && Main.GarrisonBehavior.CurrentTownForSettings.CurrentBuilding != null)
		{
			bool isDefault = ((object)Main.GarrisonBehavior.CurrentTownForSettings.CurrentBuilding.Name).ToString().Equals(((object)currentBuilding.Name).ToString());
			IsDefault = isDefault;
		}
	}

	private string ToRomanNumeral(int number)
	{
		return number switch
		{
			0 => "", 
			1 => "SPGeneral\\TownManagement\\level_1", 
			2 => "SPGeneral\\TownManagement\\level_2", 
			3 => "SPGeneral\\TownManagement\\level_3", 
			_ => "", 
		};
	}

	public void ChangeToDefault()
	{
		if (Main.GarrisonBehavior.CurrentTownForSettings != null)
		{
			Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
			BuildingHelper.ChangeDefaultBuilding(currentBuilding, currentTownForSettings);
			IsDefault = true;
		}
	}

	public void OpenContextMenu()
	{
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected O, but got Unknown
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Expected O, but got Unknown
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Expected O, but got Unknown
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Expected O, but got Unknown
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Expected O, but got Unknown
		bool flag = currentBuilding.CurrentLevel >= 3;
		if (Main.GarrisonBehavior.CurrentTownForSettings == null || flag)
		{
			return;
		}
		Town currentTown = Main.GarrisonBehavior.CurrentTownForSettings;
		MBBindingList<CascadeMenuElementVM> val = new MBBindingList<CascadeMenuElementVM>();
		bool flag2 = IsCurrentProject();
		if (!flag2)
		{
			CascadeMenuBaseButtonVM item = new CascadeMenuBaseButtonVM(((object)new TextObject("{=ui_buildingui_setascurrent}Set as current project", (Dictionary<string, object>)null)).ToString(), delegate
			{
				//IL_005a: Unknown result type (might be due to invalid IL or missing references)
				//IL_0064: Expected O, but got Unknown
				//IL_0099: Unknown result type (might be due to invalid IL or missing references)
				//IL_00a3: Expected O, but got Unknown
				//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
				//IL_00da: Unknown result type (might be due to invalid IL or missing references)
				//IL_00e4: Expected O, but got Unknown
				Queue<Building> buildingsInProgress2 = currentTown.BuildingsInProgress;
				if (buildingsInProgress2.Contains(currentBuilding))
				{
					RemoveFromBuildingQueue();
				}
				ChangeCurrentBuilding(currentBuilding.BuildingType, currentTown);
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=ui_buildingui_currenthint1}The current location project in", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)((SettlementComponent)Main.GarrisonBehavior.CurrentTownForSettings).Settlement.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=ui_buildingui_currenthint2}has been set to", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + (object)currentBuilding.Name, Color.FromUint(ModuleColors.green)));
				UIManager.Instance.CloseCascadeMenu();
			});
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)item);
		}
		else
		{
			CascadeMenuBaseButtonVM item2 = new CascadeMenuBaseButtonVM(((object)new TextObject("{=ui_buildingui_boostproject}Boost current project", (Dictionary<string, object>)null)).ToString(), delegate
			{
				PromptReserveWindow();
				UIManager.Instance.CloseCascadeMenu();
			});
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)item2);
			CascadeMenuBaseButtonVM item3 = new CascadeMenuBaseButtonVM(((object)new TextObject("{=ui_buildingui_deselectproject}Deselect current project", (Dictionary<string, object>)null)).ToString(), delegate
			{
				//IL_004a: Unknown result type (might be due to invalid IL or missing references)
				//IL_0054: Expected O, but got Unknown
				//IL_008c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0096: Expected O, but got Unknown
				//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
				//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
				//IL_00d0: Expected O, but got Unknown
				List<Building> list = currentTown.BuildingsInProgress.ToList();
				if (list.Count > 0)
				{
					list.RemoveAt(0);
					currentTown.BuildingsInProgress = new Queue<Building>(list);
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=ui_buildingui_removequeue1}The project", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)currentBuilding.Name)?.ToString() + ModuleStrings._space + ((object)new TextObject("{=ui_buildingui_removequeue2}is no longer the current project of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)((SettlementComponent)currentTown).Settlement.Name).ToString(), Color.FromUint(ModuleColors.green)));
					UIManager.Instance.CloseCascadeMenu();
				}
			});
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)item3);
		}
		if ((from building in currentTown.BuildingsInProgress.ToList()
			where ((object)building.Name).ToString().Equals(((object)currentBuilding.Name).ToString())
			select building).Count() <= 0)
		{
			CascadeMenuBaseButtonVM item4 = new CascadeMenuBaseButtonVM(((object)new TextObject("{=ui_buildingui_addqueue1}Add to project queue", (Dictionary<string, object>)null)).ToString(), delegate
			{
				//IL_0049: Unknown result type (might be due to invalid IL or missing references)
				//IL_0053: Expected O, but got Unknown
				//IL_008b: Unknown result type (might be due to invalid IL or missing references)
				//IL_0095: Expected O, but got Unknown
				//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
				//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
				//IL_00cf: Expected O, but got Unknown
				Queue<Building> buildingsInProgress = currentTown.BuildingsInProgress;
				if (!buildingsInProgress.Contains(currentBuilding))
				{
					buildingsInProgress.Enqueue(currentBuilding);
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=ui_buildingui_addqueue2}The project", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)currentBuilding.Name)?.ToString() + ModuleStrings._space + ((object)new TextObject("{=ui_buildingui_addqueue3}has been added to the project queue of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)((SettlementComponent)currentTown).Settlement.Name).ToString(), Color.FromUint(ModuleColors.green)));
					UIManager.Instance.CloseCascadeMenu();
				}
				currentTown.BuildingsInProgress = buildingsInProgress;
			});
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)item4);
		}
		else if (!flag2)
		{
			CascadeMenuBaseButtonVM item5 = new CascadeMenuBaseButtonVM(((object)new TextObject("{=ui_buildingui_removequeue3}Remove from queue", (Dictionary<string, object>)null)).ToString(), delegate
			{
				RemoveFromBuildingQueue();
			});
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)item5);
		}
		UIManager.Instance.CreateCascadeMenuOnMousePointer(((object)new TextObject("{=ui_buildingui_action}Project action", (Dictionary<string, object>)null)).ToString(), val);
	}

	public static void ChangeCurrentBuilding(BuildingType buildingType, Town town)
	{
		Building val = null;
		foreach (Building item in (List<Building>)(object)town.Buildings)
		{
			if (item.BuildingType == buildingType)
			{
				val = item;
				break;
			}
		}
		if (val == null)
		{
			return;
		}
		List<Building> list = new List<Building>();
		list.Add(val);
		foreach (Building item2 in town.BuildingsInProgress)
		{
			if (item2 != val && !item2.BuildingType.IsDailyProject)
			{
				list.Add(item2);
			}
		}
		BuildingHelper.ChangeCurrentBuildingQueue(list, town);
	}

	private void RemoveFromBuildingQueue()
	{
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected O, but got Unknown
		if (Main.GarrisonBehavior.CurrentTownForSettings != null)
		{
			Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
			List<Building> list = currentTownForSettings.BuildingsInProgress.ToList();
			int num = list.FindIndex((Building building) => ((object)building.Name).ToString().Equals(((object)currentBuilding.Name).ToString()));
			if (num >= 0)
			{
				list.RemoveAt(num);
				currentTownForSettings.BuildingsInProgress = new Queue<Building>(list);
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=ui_buildingui_addqueue2}The project", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)currentBuilding.Name)?.ToString() + ModuleStrings._space + ((object)new TextObject("{=ui_buildingui_removequeue4}has been removed from the project queue of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)((SettlementComponent)currentTownForSettings).Settlement.Name).ToString(), Color.FromUint(ModuleColors.green)));
				UIManager.Instance.CloseCascadeMenu();
			}
		}
	}

	private void PromptReserveWindow()
	{
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Expected O, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected O, but got Unknown
		TextObject val = GameTexts.FindText("str_town_management_reserve_explanation", (string)null);
		val.SetTextVariable("BOOST", Campaign.Current.Models.BuildingConstructionModel.GetBoostAmount(Main.GarrisonBehavior.CurrentTownForSettings));
		val.SetTextVariable("COST", Campaign.Current.Models.BuildingConstructionModel.GetBoostCost(Main.GarrisonBehavior.CurrentTownForSettings));
		InformationManager.ShowTextInquiry(new TextInquiryData(((object)new TextObject("{=ui_buildingui_addreserve}Add reserve to boost project", (Dictionary<string, object>)null)).ToString(), string.Format(((object)val).ToString()), true, true, ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<string>)delegate(string amount)
		{
			if (int.TryParse(amount, out var input))
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					int boostBuildingProcess = Main.GarrisonBehavior.CurrentTownForSettings.BoostBuildingProcess;
					BuildingHelper.BoostBuildingProcessWithGold(boostBuildingProcess + input, Main.GarrisonBehavior.CurrentTownForSettings);
				});
			}
		}, (Action)delegate
		{
			InformationManager.HideInquiry();
		}, false, (Func<string, Tuple<bool, string>>)delegate(string x)
		{
			//IL_0059: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Expected O, but got Unknown
			Hero mainHero = Hero.MainHero;
			int num = ((((mainHero != null) ? mainHero.Clan : null) != null) ? Hero.MainHero.Clan.Gold : int.MaxValue);
			int result;
			return (int.TryParse(x, out result) && result <= num && result > 0) ? new Tuple<bool, string>(item1: true, "") : new Tuple<bool, string>(item1: false, ((object)new TextObject("{=ui_buildingui_addreserve2}You don't have enough denars.", (Dictionary<string, object>)null)).ToString());
		}, "", ""), false, false);
	}

	private bool IsCurrentProject()
	{
		Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
		if (currentTownForSettings == null)
		{
			return false;
		}
		List<Building> list = currentTownForSettings.BuildingsInProgress.ToList();
		if (list.Count > 0)
		{
			Building val = list.First();
			return ((object)val.Name).ToString().Equals(((object)currentBuilding.Name).ToString());
		}
		return false;
	}

	private TextObject GetBonusExplanationOfLevel(int level)
	{
		if (level >= 0 && level <= 3)
		{
			return currentBuilding.BuildingType.GetExplanationAtLevel(level);
		}
		return TextObject.GetEmpty();
	}

	private string GetBonusText(Building building, int level)
	{
		if (level == 0 || level == 4)
		{
			return "";
		}
		string text = level switch
		{
			2 => ((object)L2BonusText).ToString(), 
			1 => ((object)L1BonusText).ToString(), 
			_ => ((object)L3BonusText).ToString(), 
		};
		TextObject bonusExplanationOfLevel = GetBonusExplanationOfLevel(level);
		return text + ((object)bonusExplanationOfLevel).ToString();
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		UpdateDefault();
	}
}
