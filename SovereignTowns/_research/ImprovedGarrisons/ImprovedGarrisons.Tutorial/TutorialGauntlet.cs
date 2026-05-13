using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.Utils;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace ImprovedGarrisons.Tutorial;

public class TutorialGauntlet : MapView
{
	private GauntletLayer _layer;

	private ImprovedGarrisonsHighlightBorderVM _highlightDatasource;

	private TutorialUIVM _datasource;

	private bool onMapTutorial = false;

	public TutorialGauntlet(bool onMapTutorial = false)
	{
		((MapView)this).CreateLayout();
		this.onMapTutorial = onMapTutorial;
		StartTutorial();
	}

	protected override void CreateLayout()
	{
		((MapView)this).CreateLayout();
	}

	public void PushScreen(TutorialUIVM vm, ImprovedGarrisonsHighlightBorderVM highlightVM = null)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		if (_layer != null)
		{
			((ScreenBase)MapScreen.Instance).RemoveLayer((ScreenLayer)(object)_layer);
			_layer = null;
		}
		_layer = new GauntletLayer("GauntletLayer", 580, false);
		_datasource = vm;
		_layer.LoadMovie("ImprovedGarrisonsTutorial", (ViewModel)(object)_datasource);
		if (highlightVM != null)
		{
			_highlightDatasource = highlightVM;
			_layer.LoadMovie("ImprovedGarrisonsHighlightBorder", (ViewModel)(object)_highlightDatasource);
		}
		((ScreenLayer)_layer).InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
		((ScreenBase)MapScreen.Instance).AddLayer((ScreenLayer)(object)_layer);
		ScreenManager.TrySetFocus((ScreenLayer)(object)_layer);
	}

	public void StartTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_introduction}Welcome to the Improved Garrisons tutorial. \n \nThis tutorial will explain the basic features of Improved Garrisons.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM vm = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_cancel}Cancel tutorial", (Dictionary<string, object>)null)).ToString(), delegate
		{
			if (!onMapTutorial)
			{
				FindImprovedGarrisonMenuTutorial();
			}
			else
			{
				GarrisonSelectorTutorial();
			}
		}, delegate
		{
			OnEndTutorial();
		});
		PushScreen(vm);
	}

	private void FindImprovedGarrisonMenuTutorial()
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Expected O, but got Unknown
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected O, but got Unknown
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected O, but got Unknown
		if (Settlement.CurrentSettlement != null)
		{
			if (Settlement.CurrentSettlement.IsTown)
			{
				GameMenu.SwitchToMenu("town_keep");
			}
			else
			{
				GameMenu.SwitchToMenu("castle");
			}
			MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
			((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_findmenu1}The main Improved Garrisons features can be accessed by pressing the button", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
			((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("Improved Garrisons", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
			((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_findmenu2}in the settlement menu of your castles or towns. \n(on the left side of your screen)", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
			TutorialUIVM vm = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
			{
				UIGuide();
			}, delegate
			{
				StartTutorial();
			});
			ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(30, 200, 300, 500);
			PushScreen(vm, highlightVM);
		}
	}

	private void UIGuide()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_uiguide1}Improved Garrisons Menu", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_uiguide2}After pressing the Improved Garrisons button, the Improved Garrisons menu will open. This menu is used to access and control your garrisons. It consists of several different tabs with features like recruitment and training of your garrisoned troops.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GarrisonSelectorTutorial();
		}, delegate
		{
			FindImprovedGarrisonMenuTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(940, 150, 780, 960);
		PushScreen(tutorialUIVM, highlightVM);
	}

	private void GarrisonSelectorTutorial()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_garrisonselector1}Improved Garrisons settlement selection", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrisonselector2}At the top of the Improved Garrisons menu, you will find a drop-down button. All of your current castles and towns will be listed there.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrisonselector3}Use this drop-down menu to select the location where you want to make changes to.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			OverviewTutorial();
		}, delegate
		{
			if (!onMapTutorial)
			{
				UIGuide();
			}
			else
			{
				StartTutorial();
			}
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1270, 225, 50, 300);
		PushScreen(tutorialUIVM, highlightVM);
	}

	private void OverviewTutorial()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("OverviewTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_overview1}Overview tab", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_overview2}This tab provides an overview of all your current fiefs (castles or towns). Each of your fiefs will be listed in a succession of cards. From this list, you can see the current status of each of your fiefs from anywhere on the map.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_overview3}It will also dynamically show useful information, for example if a fief or one of its villages is currently under attack, or even destroyed.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			OverviewTutorial2();
		}, delegate
		{
			GarrisonSelectorTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	private void OverviewTutorial2()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Expected O, but got Unknown
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Expected O, but got Unknown
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Expected O, but got Unknown
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("OverviewTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_overview4}Overview tab features", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_overview5}Each fief card also comes with some functionalities.", (Dictionary<string, object>)null)).ToString(), 24, 0, 5));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, "> " + ((object)new TextObject("{=tutorial_overview6}By left-clicking a fief picture, it will be tracked on the map.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, "> " + ((object)new TextObject("{=tutorial_overview7}By left-clicking a fief card, it will be selected for configuration.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, "> " + ((object)new TextObject("{=tutorial_overview8}By right-clicking a fief card, a context menu with further options will be displayed.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_overview9}Note: if a location is under attack, the context menu also will provide an option to defend it with a guard party (see below for further details). This allows you to defend a castle, town or village from anywhere on the map.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			RecruitmentTutorial();
		}, delegate
		{
			OverviewTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	private void RecruitmentTutorial()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Expected O, but got Unknown
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("RecruitmentTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment1}Recruitment menu", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment2}This is the recruitment section. You can automatically recruit garrison troops from the fief's region, or establish a recruiter party to recruit troops from outside of the region.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment2_1}Note: a region consists of the castle or town and its bound villages", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			RecruitmentTutorial2();
		}, delegate
		{
			OverviewTutorial2();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void RecruitmentTutorial2()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("RecruitmentTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment3}Recruiter information", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment4}A recruiter party is used to recruit new troops for the garrison. The recruiter will look for new troops outside of the region of your current fief. You can select the culture and the number of troops you want the recruiter to recruit for your garrison.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			RecruitmentTutorial3();
		}, delegate
		{
			RecruitmentTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 340, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void RecruitmentTutorial3()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Expected O, but got Unknown
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Expected O, but got Unknown
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Expected O, but got Unknown
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Expected O, but got Unknown
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Expected O, but got Unknown
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Expected O, but got Unknown
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b7: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("RecruitmentTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment5}Recruitment settings", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment6}The recruitment section of Improved Garrisons enables your castle or town to automatically recruit new troops. In the highlighted section, you have a couple of different options to chose on how you want new troops to be recruited.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment6_1}Vanilla recruitment", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment6_2}The vanilla recruitment refers to the recruitment of the base game, which is not managed by this mod. It is highly recommended to keep this setting deactivated as the recruitment from the base game is not controlled by Improved Garrisons and can therefore interfere with your custom settings.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment6_3}Recruit from region", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment6_4}With this setting enabled, nearby villages will send a party of recruits to the garrison without the need of you collecting it.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment6_5}Recruit prisoners", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment6_6}With this setting enabled, prisoners from the dungeon will slowly be recruited into your garrison.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment7}Note: each prisoner has its own conformity value that needs to be reached in order to be recruitable. You can either run around with your prisoners to build up their conformity, or use this Improved Garrison option to slowly build up their conformity.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			RecruitmentTutorial4();
		}, delegate
		{
			RecruitmentTutorial2();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1560, 290, 340, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void RecruitmentTutorial4()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("RecruitmentTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_recruitment8}Recruitment settings", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment9}(You can scroll in this section)", (Dictionary<string, object>)null)).ToString(), 20, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_recruitment10}Use these settings to further customize this garrison's recruitment behavior, and the recruiter party behavior.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			TrainingTutorial();
		}, delegate
		{
			RecruitmentTutorial3();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 630, 250, 600);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void TrainingTutorial()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("TrainingTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training1}Training menu", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training2}This is the training section of Improved Garrisons. In this section, you can tell each garrison to train its troops.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			TrainingTutorial2();
		}, delegate
		{
			RecruitmentTutorial4();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void TrainingTutorial2()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected O, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Expected O, but got Unknown
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Expected O, but got Unknown
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Expected O, but got Unknown
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Expected O, but got Unknown
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Expected O, but got Unknown
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("TrainingTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training3}Training templates", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training4}When you set up a training template, you can specify exactly which troops you want your garrison to train its troops to. With this feature, you can define an entire army into a template, for example how many Battanian Fian Champions or how many Sturgian Line Breakers you want to be trained.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training5}This feature can be very convenient. If one of your garrisons trained a template army, the next time you lose a big fight, you will not be forced to collect and train new troops. Simply go to the garrison and collect the troops that have been trained for you.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training6}How does it work? Each time a garrisoned troop can be upgraded, Improved Garrison will check if the troop has a relevant upgrade target specified in your current training template. Improved Garrison will then proceed to upgrade the troop towards this target.", (Dictionary<string, object>)null)).ToString(), 24, 20, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training7}Note: Improved Garrison will not upgrade your troops above the target specified in the training template.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training7_1}Note: if no upgrade is specified in the template, the troop will be randomly upgraded in accordance to the 'max upgrade tier for troops' setting.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training7_2}Note: the troop list that is highlighted is interactive. Right-click to delete a troop from the list and use the '+' and '-' buttons to change the amount you want to be trained.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			TrainingTutorial3();
		}, delegate
		{
			TrainingTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1560, 290, 430, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void TrainingTutorial3()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Expected O, but got Unknown
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("TrainingTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training8}Add troops", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training9}Here you can add new troops to your current template. The troops will be added to the list above, so you can always have a quick look to what your garrison is training its troops towards.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training10}Template manager", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training11}The template manager is a convenient feature to save and manage training templates. Your saved templates will be synchronized across all your saves, so you will always be able to apply them later if you need them.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			TrainingTutorial4();
		}, delegate
		{
			TrainingTutorial2();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1560, 700, 180, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void TrainingTutorial4()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Expected O, but got Unknown
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected O, but got Unknown
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Expected O, but got Unknown
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Expected O, but got Unknown
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Expected O, but got Unknown
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Expected O, but got Unknown
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("TrainingTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training12}Training settings", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training13}For the template feature to work, Improved Garrison needs your permission to train troops from your garrison. This is where you give this permission.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training14}Note: keep in mind that training costs money. If you struggle keeping up with costs, it may be smart to check if you are able to maintain your current training settings.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training14_1}Vanilla training", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training14_2}As with the vanilla recruitment, there is also a vanilla training setting which refers to the base games logic of upgrading troops. It is highly recommended to leave this setting disabled, as the base game upgrade logic randomly upgrades and downgrades troops without looking at the training template.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training17}Max upgrade tier for troops (non template)", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training18}This feature is only relevant for all those troops in your garrison that are not affected by your current training template. Improved Garrison will only upgrade those troops to the tier you have selected here.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training19}Note: if no training template is found for a troop, its upgrade path will be randomly selected.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			TrainingTutorial5();
		}, delegate
		{
			TrainingTutorial3();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 310);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void TrainingTutorial5()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected O, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected O, but got Unknown
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Expected O, but got Unknown
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("RecruitmentTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training14_3}Let's go back to the recruitment section for a moment, and review the highlighted setting here.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_training15}Recruiting follows template", (Dictionary<string, object>)null)).ToString(), 24, 20, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training16}This feature is very convenient. To be able to train troops according to your current training template, Improved Garrison obviously needs units that can be trained towards these template targets!", (Dictionary<string, object>)null)).ToString(), 24, 10, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training16_1}Instead of having to collect these units yourself, the Improved Garrison recruitment can do it for you. Keep in mind that depending on your template, troops from different cultures that are from different parts of the world map can be needed. To automatically recruit troops from these cultures, enable the 'automatic recruiter creation' in the settings below (after activating the 'recruiting follows template' button) to have recruiter parties automatically be created for you.", (Dictionary<string, object>)null)).ToString(), 24, 10, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_training16_2}The recruiter is (kind of) intelligent. He knows what troops from which cultures you need for your training template and will gather these troops from around the world map for you and returns them to your garrison.", (Dictionary<string, object>)null)).ToString(), 24, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GuardTutorial();
		}, delegate
		{
			TrainingTutorial4();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1560, 550, 80, 310);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void GuardTutorial()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Expected O, but got Unknown
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Expected O, but got Unknown
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("GuardsTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_guards1}Garrison guard parties", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_guards2}This is the guard party section. Here, you can create a new guard party, display information about your current guard party, give orders to your guard party or customize its behavior.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_guards3}A guard is a party that is created out of your currently garrisoned troops. Each Improved Garrison may have one guard party. Guard parties have a advanced custom AI build on top of the base games AI. The guards AI thinks about stuff like \"I should defend XY village\", \"I shouldn't attack XY party it is to fast or to strong\", \"I should go back to a settlement and heal my units\", and more!", (Dictionary<string, object>)null)).ToString(), 24, 20, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_guards4}Each guard party can also be given orders. The most common usage is the defense of your region. If you tell the guard to patrol, it will walk around the region, engage looters and bandits and defend against sieges and raids. You may also tell the guards to escort other parties. The guards will then follow the party and defend it. This can be useful if you need help with your next attack or if you want one of your caravans to be protected.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GuardTutorial2();
		}, delegate
		{
			TrainingTutorial5();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void GuardTutorial2()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Expected O, but got Unknown
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("GuardsTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_guards5}Guard settings", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_guards6}(You can scroll in this section)", (Dictionary<string, object>)null)).ToString(), 20, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_guards7}This list consists of further behavior settings for your guards. You can for example automatically create guards when raided, disable the recruitment of captured prisoners and more.", (Dictionary<string, object>)null)).ToString(), 24, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GarrisonTutorial();
		}, delegate
		{
			GuardTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 630, 250, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void GarrisonTutorial()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("GarrisonTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_garrison1}Garrison information", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrison2}This is the garrison information section. Important information about your garrison and the acitivty of Improved Garrison is shown here.", (Dictionary<string, object>)null)).ToString(), 24, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GarrisonTutorial2();
		}, delegate
		{
			GuardTutorial2();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void GarrisonTutorial2()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Expected O, but got Unknown
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("GarrisonTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_garrison3}Current garrison", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrison4}This list displays the current garrison of the selected town or castle.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrison5}At the bottom, there is a button dedicated to transfer troops. This allows you to create a transfer party which will go to a selected location you own. Use this feature if you want to transfer units between garrisons.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrison6}Note: if you are at war, this could be useful to transfer troops to locations that need a bigger garrison.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GarrisonTutorial3();
		}, delegate
		{
			GarrisonTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1560, 290, 600, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void GarrisonTutorial3()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("GarrisonTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_garrison7}Weekly garrison information", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrison8}This list displays the various costs of recruitment and training this garrison undergoes. It also shows the cumulative amount of troops that have been recruited and upgraded this week.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			GarrisonTutorial4();
		}, delegate
		{
			GarrisonTutorial2();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1250, 290, 280, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void GarrisonTutorial4()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("GarrisonTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_garrison9}Garrison activity", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_garrison10}A chat-like list that displays the latest activities the garrison performed. This list shows activities like recruitments and upgrades, along with a timestamp.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			ManagementTutorial();
		}, delegate
		{
			GarrisonTutorial3();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1250, 590, 300, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void ManagementTutorial()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("ManagementTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_management1}Management", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management2}The management section displays current information about your fief, allows you to copy settings between them and manage their projects.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			ManagementTutorial2();
		}, delegate
		{
			GarrisonTutorial4();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 290, 600, 620);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void ManagementTutorial2()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Expected O, but got Unknown
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("ManagementTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_management3}Town Projects", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management2}The management section displays current information about your fief, allows you to copy settings between them and manage their projects.", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management2}The management section displays current information about your fief, allows you to copy settings between them and manage their projects.", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management2}The management section displays current information about your fief, allows you to copy settings between them and manage their projects.", (Dictionary<string, object>)null)).ToString(), 24, 0, 10));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			ManagementTutorial3();
		}, delegate
		{
			ManagementTutorial();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1560, 290, 600, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void ManagementTutorial3()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("ManagementTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_management7}Copy manager", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management8}The copy manager is used to copy the settings of the current garrison to another one.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			ManagementTutorial4();
		}, delegate
		{
			ManagementTutorial2();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 300, 380, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void ManagementTutorial4()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Expected O, but got Unknown
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		ChangeTab("ManagementTab");
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_management7}Copy manager", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management8}The copy manager is used to copy the settings of the current garrison to another one.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_management9}Note: for example, if you quickly want all your fiefs to collect 300 troops to prepare for your next war, you can do this here.", (Dictionary<string, object>)null)).ToString(), 18, 10, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			EnableOnMapTutorial();
		}, delegate
		{
			ManagementTutorial3();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1260, 670, 220, 320);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void EnableOnMapTutorial()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Expected O, but got Unknown
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_enableonmap1}Enable on map", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_enableonmap2}Quick tip: if you want to use the Improved Garrisons menu from anywhere on the map, check this button.", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			ConfigTutorial();
		}, delegate
		{
			ManagementTutorial4();
		});
		tutorialUIVM.PositionXOffset = -400;
		ImprovedGarrisonsHighlightBorderVM highlightVM = new ImprovedGarrisonsHighlightBorderVM(1700, 870, 50, 200);
		PushScreen(tutorialUIVM, highlightVM);
	}

	public void ConfigTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected O, but got Unknown
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiHighlightColor, ((object)new TextObject("{=tutorial_configmanager1}Configuration manager", (Dictionary<string, object>)null)).ToString(), 24, 0, 20));
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_configmanager2}The Improved Garrison configuration manager is accessible anytime while on the map by pressing [left ALT] + [G]. \n \nHere, you can adjust many values as you like. You could for example:\n> Change the daily training experience\n> Lower your guard parties and garrison troops costs\n> Increase the speed with which your prisoners are recruited\n> Enable the food gathering module\n> Allow NPC factions to use Improved Garrisons features\n> Change the default values that are set upon getting a new fief\n> Activate cheat settings\n> ... and much more!", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM tutorialUIVM = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			Main.CloseConfigurationScreen();
			EndTutorial();
		}, delegate
		{
			Main.CloseConfigurationScreen();
			EnableOnMapTutorial();
		});
		tutorialUIVM.PositionXOffset = 600;
		Main.OpenConfigurationScreen(tutorialUIVM);
	}

	public void EndTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		MBBindingList<TutorialItemUIVM> val = new MBBindingList<TutorialItemUIVM>();
		((Collection<TutorialItemUIVM>)(object)val).Add(new TutorialItemUIVM(ModuleColors.uiStandardTextColor, ((object)new TextObject("{=tutorial_thankyou}Aaand you made it through the tutorial!! :-)\n \nI personally want to thank you for using Improved Garrisons! I put countless hours of work in this project and seeing how many people enjoy the mod is truly amazing.\n \nIf you like my work, it would be awesome to hear back from you. Consider leaving a comment and endorsement on the Nexus Mod Page. And if you want to support my work, you can donate me a coffee for my late night development sessions :-)\n \nOn to a great journey of Mount and Blade \n ~ Sidies", (Dictionary<string, object>)null)).ToString(), 24, 0, 0));
		TutorialUIVM vm = new TutorialUIVM(val, ((object)new TextObject("{=tutorial_end}End tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_back}Back", (Dictionary<string, object>)null)).ToString(), delegate
		{
			OnEndTutorial();
		}, delegate
		{
			ConfigTutorial();
		});
		PushScreen(vm);
	}

	public void CloseTutorial()
	{
		((ScreenBase)MapScreen.Instance).RemoveLayer((ScreenLayer)(object)_layer);
	}

	private void OnEndTutorial()
	{
		ConfigManager.Instance.Config.DeactivateTutorial = true;
		ConfigManager.Instance.CreateAndUpdateConfigForCurrentGame();
		UIManager.Instance.CloseTutorial();
	}

	private void ChangeTab(string tabId)
	{
		if (UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.improvedGarrisonsUI.SwitchToTab(tabId);
		}
	}
}
