using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.AI.AIManagers;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.ActivityLogging;
using ImprovedGarrisons.Behaviours;
using ImprovedGarrisons.ConfigOptionsMenu;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.Models;
using ImprovedGarrisons.Recruitment;
using ImprovedGarrisons.SaveSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.SaveData;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Tutorial;
using ImprovedGarrisons.Upgrade;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace ImprovedGarrisons;

public class Main : MBSubModuleBase
{
	private static readonly List<Action> ActionsToExecuteNextTick = new List<Action>();

	public static bool _configurationSettingsIsOpen = false;

	private static ConfigMenuGauntletScreen _configMenu;

	private readonly string modGameVersion = "v1.3.15";

	private static Dictionary<string, Action> ActionsToExecuteEveryTick = new Dictionary<string, Action>();

	public static GarrisonPartyBehavior GarrisonPartyBehavior { get; set; }

	public static GarrisonBehavior GarrisonBehavior { get; set; }

	public static SaveBehavior SaveBehavior { get; set; }

	public static ActivityLogManager ActivityLogManager { get; set; }

	public static GarrisonRecruitmentLogic RecruitmentLogic { get; set; }

	public static GarrisonUpgradeLogic UpgradeLogic { get; set; }

	public static GarrisonCostModel GarrisonCostModel { get; set; }

	public static GarrisonFoodModel FoodModel { get; set; }

	public static PartyManager PartyManagement { get; set; }

	public static bool IsMapState
	{
		get
		{
			Game current = Game.Current;
			object obj;
			if (current == null)
			{
				obj = null;
			}
			else
			{
				GameStateManager gameStateManager = current.GameStateManager;
				obj = ((gameStateManager == null) ? null : ((object)gameStateManager.ActiveState)?.GetType());
			}
			return (Type)obj == typeof(MapState) && !Game.Current.GameStateManager.ActiveState.IsMenuState;
		}
	}

	protected override void OnBeforeInitialModuleScreenSetAsRoot()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Expected O, but got Unknown
		InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=misc_ig_onmapload}Loaded Improved Garrisons.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
		ThrowWarningIfGameErrorDoesntMatchModVersion();
	}

	public override void OnGameLoaded(Game game, object initializerObject)
	{
		((MBSubModuleBase)this).OnGameLoaded(game, initializerObject);
	}

	public override void OnGameInitializationFinished(Game game)
	{
	}

	protected override void OnSubModuleLoad()
	{
		((MBSubModuleBase)this).OnSubModuleLoad();
	}

	protected override void OnApplicationTick(float dt)
	{
		try
		{
			((MBSubModuleBase)this).OnApplicationTick(dt);
			foreach (Action item in ActionsToExecuteNextTick)
			{
				item();
			}
			ActionsToExecuteNextTick.Clear();
			foreach (Action item2 in ActionsToExecuteEveryTick.Values.ToList())
			{
				item2();
			}
			OnKeyPress();
			if (GarrisonPartyBehavior != null && PartyManagement.mobileGarrisonManagement.MobileGarrisons != null)
			{
				List<MobileGarrison> list = new List<MobileGarrison>();
				foreach (MobileGarrison value in PartyManagement.mobileGarrisonManagement.MobileGarrisons.Values)
				{
					if (Game.Current != null && Game.Current.GameStateManager.ActiveState != null && !Game.Current.GameStateManager.ActiveState.IsMenuState && value.getMobileParty().MemberRoster.TotalManCount <= 0)
					{
						list.Add(value);
					}
				}
				foreach (MobileGarrison item3 in list)
				{
					GarrisonPartyBehavior.RemovePartyHelper(item3.getMobileParty());
				}
			}
			if (GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap)
			{
				UIManager.Instance.TryInitializeImprovedGarrisonsUI();
			}
			else
			{
				bool flag = UIManager.Instance.CurrentUiState == UIManager.UiState.Retracted;
				Campaign current5 = Campaign.Current;
				bool flag2 = ((current5 != null) ? current5.MainParty : null) != null && Campaign.Current.MainParty.CurrentSettlement != null;
				if (!GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap && UIManager.Instance.improvedGarrisonsUI != null && flag && !flag2 && IsMapState)
				{
					UIManager.Instance.CloseImprovedGarrisonsUI();
				}
			}
			if (UIManager.Instance.cascadeMenuGauntlet != null && UIManager.Instance.cascadeMenuGauntlet.cascadeMenuIsOpen)
			{
				UIManager.Instance.cascadeMenuGauntlet.Tick();
			}
			UIManager.Instance.TryUpdateImprovedGarrisonsUI();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void OnCampaignStart(Game game, object starterObject)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		((MBSubModuleBase)this).OnCampaignStart(game, starterObject);
		if (game.GameType is Campaign)
		{
			try
			{
				InitializeGame(game, (IGameStarter)starterObject);
			}
			catch (Exception ex)
			{
				LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			}
		}
	}

	protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Invalid comparison between Unknown and I4
		try
		{
			((MBSubModuleBase)this).OnGameStart(game, gameStarterObject);
			GameType gameType = game.GameType;
			Campaign val = (Campaign)(object)((gameType is Campaign) ? gameType : null);
			if (val != null && (int)val.CampaignGameLoadingType != 1)
			{
				InitializeGame(game, gameStarterObject);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public static void LoadImprovedGarrisonsIntoGame(Game game, IGameStarter gameStarter)
	{
	}

	public void InitializeGame(Game game, IGameStarter gameStarter)
	{
		try
		{
			UIManager.Instance.ResetInstance();
			IGSaveData.Instance = null;
			GarrisonPartyBehavior = new GarrisonPartyBehavior();
			GarrisonBehavior = new GarrisonBehavior();
			SaveBehavior = new SaveBehavior();
			SaveSystemManager.Instance = new SaveSystemManager();
			ActivityLogManager = new ActivityLogManager();
			GarrisonCostModel = new GarrisonCostModel();
			RecruitmentLogic = new GarrisonRecruitmentLogic();
			UpgradeLogic = new GarrisonUpgradeLogic();
			FoodModel = new GarrisonFoodModel();
			PartyManagement = new PartyManager();
			ConfigManager.Instance.ReadConfigForCurrentGame();
			AddBehaviours((CampaignGameStarter)(object)((gameStarter is CampaignGameStarter) ? gameStarter : null));
			AddModels((CampaignGameStarter)(object)((gameStarter is CampaignGameStarter) ? gameStarter : null));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void AddBehaviours(CampaignGameStarter starter)
	{
		starter.AddBehavior((CampaignBehaviorBase)(object)new GarrisonDailyBehavior());
		starter.AddBehavior((CampaignBehaviorBase)(object)GarrisonBehavior);
		starter.AddBehavior((CampaignBehaviorBase)(object)GarrisonPartyBehavior);
		starter.AddBehavior((CampaignBehaviorBase)(object)new UiBehavior());
		starter.AddBehavior((CampaignBehaviorBase)(object)SaveBehavior);
		starter.AddBehavior((CampaignBehaviorBase)(object)ActivityLogManager);
	}

	private void AddModels(CampaignGameStarter gameStarter)
	{
		if (gameStarter != null)
		{
			gameStarter.AddModel<ClanFinanceModel>((MBGameModel<ClanFinanceModel>)(object)GarrisonCostModel);
			if (ConfigManager.Instance.Config.LoadCustomPartySizeModel)
			{
				gameStarter.AddModel<PartySizeLimitModel>((MBGameModel<PartySizeLimitModel>)(object)new GarrisonpartySizeLimitModel());
			}
			if (ConfigManager.Instance.Config.LoadCustomPartySpeedModel)
			{
				gameStarter.AddModel<PartySpeedModel>((MBGameModel<PartySpeedModel>)(object)new GarrisonSpeedModel());
			}
			if (ConfigManager.Instance.Config.LoadFoodGatheringModule)
			{
				gameStarter.AddModel<SettlementFoodModel>((MBGameModel<SettlementFoodModel>)(object)FoodModel);
			}
		}
	}

	public static void ExecuteActionOnNextTick(Action action)
	{
		if (action != null)
		{
			ActionsToExecuteNextTick.Add(action);
		}
	}

	public static void AddActionToExecuteEachTick(string id, Action action)
	{
		if (!ActionsToExecuteEveryTick.ContainsKey(id))
		{
			ActionsToExecuteEveryTick.Add(id, action);
		}
	}

	public static void RemoveActionToExecuteEachTick(string id)
	{
		if (ActionsToExecuteEveryTick.ContainsKey(id))
		{
			ActionsToExecuteEveryTick.Remove(id);
		}
	}

	private void OnKeyPress()
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			InputKey val = (InputKey)34;
			bool flag = Input.IsKeyDown(val) && Input.IsKeyDown((InputKey)56);
			bool flag2 = Game.Current != null && Game.Current.GameStateManager != null && Game.Current.GameStateManager.ActiveState != null && ((object)Game.Current.GameStateManager.ActiveState).GetType() == typeof(MapState) && !Game.Current.GameStateManager.ActiveState.IsMenuState;
			if (flag && flag2)
			{
				OpenConfigurationScreen();
			}
			bool flag3 = Input.IsKeyDown((InputKey)32) && Input.IsKeyDown((InputKey)56) && Input.IsKeyDown((InputKey)29);
			if (flag3 && flag2)
			{
				UIManager.Instance.CloseImprovedGarrisonsUI();
				UIManager.Instance.TryInitializeImprovedGarrisonsUI();
				GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap = true;
			}
			bool flag4 = Input.IsKeyDown((InputKey)33) && Input.IsKeyDown((InputKey)56) && Input.IsKeyDown((InputKey)29);
			if (flag4 && flag2)
			{
				MobileParty targetParty = MobileParty.MainParty.TargetParty;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public static void OpenConfigurationScreen(TutorialUIVM tutorialVM = null)
	{
		if (!_configurationSettingsIsOpen)
		{
			ConfigMenuGauntletScreen configMenu = new ConfigMenuGauntletScreen(tutorialVM);
			_configMenu = configMenu;
			ScreenManager.PushScreen((ScreenBase)(object)_configMenu);
			_configurationSettingsIsOpen = true;
		}
	}

	public static void CloseConfigurationScreen()
	{
		_configMenu.CloseConfigurationMenu();
	}

	public void ThrowWarningIfGameErrorDoesntMatchModVersion()
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Expected O, but got Unknown
		try
		{
			ApplicationVersion val = ApplicationVersion.FromParametersFile((string)null);
			string text = ((object)(ApplicationVersion)(ref val)).ToString().Substring(0, 7);
			if (!text.Equals(modGameVersion))
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("The game version you are running is " + text + ". The Improved Garrison version you are using is made for " + modGameVersion + ". There might be compatibility issues! Make sure to update to the latest mod version.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.red)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
