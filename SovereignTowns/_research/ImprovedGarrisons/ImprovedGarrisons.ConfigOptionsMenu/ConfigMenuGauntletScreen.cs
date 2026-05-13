using System.Collections.Generic;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.Tutorial;
using ImprovedGarrisons.Utils;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace ImprovedGarrisons.ConfigOptionsMenu;

internal class ConfigMenuGauntletScreen : ScreenBase
{
	private TutorialUIVM tutorialVM;

	private GauntletLayer _gauntletLayer;

	private ConfigMenuVM _dataSource;

	private SpriteCategory _spriteCategory;

	public object TutorialUiVm { get; private set; }

	public ConfigMenuGauntletScreen(TutorialUIVM tutorial = null)
	{
		tutorialVM = tutorial;
	}

	protected override void OnInitialize()
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Expected O, but got Unknown
		((ScreenBase)this).OnInitialize();
		SpriteData spriteData = UIResourceManager.SpriteData;
		TwoDimensionEngineResourceContext resourceContext = UIResourceManager.ResourceContext;
		ResourceDepot resourceDepot = UIResourceManager.ResourceDepot;
		_spriteCategory = spriteData.SpriteCategories["ui_options"];
		_spriteCategory.Load((ITwoDimensionResourceContext)(object)resourceContext, resourceDepot);
		_dataSource = new ConfigMenuVM();
		_gauntletLayer = new GauntletLayer("GauntletLayer", 4000, false);
		_gauntletLayer.LoadMovie("ImprovedGarrisonsConfigScreen", (ViewModel)(object)_dataSource);
		if (tutorialVM != null)
		{
			_gauntletLayer.LoadMovie("ImprovedGarrisonsTutorial", (ViewModel)(object)tutorialVM);
		}
		((ScreenLayer)_gauntletLayer).Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
		((ScreenLayer)_gauntletLayer).InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
		((ScreenLayer)_gauntletLayer).IsFocusLayer = true;
		((ScreenBase)this).AddLayer((ScreenLayer)(object)_gauntletLayer);
		ScreenManager.TrySetFocus((ScreenLayer)(object)_gauntletLayer);
		Utilities.SetForceVsync(true);
	}

	public void CloseConfigurationMenu()
	{
		_dataSource.ExecuteCancel();
	}

	protected override void OnFinalize()
	{
		((ScreenBase)this).OnFinalize();
		_spriteCategory.Unload();
		Utilities.SetForceVsync(false);
	}

	protected override void OnDeactivate()
	{
		LoadingWindow.EnableGlobalLoadingWindow();
	}

	protected override void OnFrameTick(float dt)
	{
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Expected O, but got Unknown
		((ScreenBase)this).OnFrameTick(dt);
		if ((Main._configurationSettingsIsOpen && _dataSource.IsFinished && !_dataSource.PromptIsOpen) || (((ScreenLayer)_gauntletLayer).Input.IsHotKeyReleased("Exit") && !_dataSource.PromptIsOpen))
		{
			_dataSource.UnpauseGame();
			ScreenManager.PopScreen();
			Main._configurationSettingsIsOpen = false;
		}
		if (_dataSource.ResetMode)
		{
			ConfigManager.Instance.Config.ResetToDefault();
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_config_reset_done}The Improved Garrisons configuration has been reset to its default values.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.yellow)));
			_dataSource.ResetMode = false;
		}
	}
}
