using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.Ribbons;
using ImprovedGarrisons.SaveSystem;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;

namespace ImprovedGarrisons.Menu;

public abstract class ImprovedGarrisonMenu
{
	public RibbonManagerGauntlet ribbonManager;

	public GarrisonBehavior garrisonBehavior;

	public readonly string str_space = " ";

	public ImprovedGarrisonMenu(CampaignGameStarter gameStarter)
	{
		garrisonBehavior = getGarrisonBehavior();
		ribbonManager = new RibbonManagerGauntlet();
		AddGameMenus(gameStarter);
	}

	public abstract void AddGameMenus(CampaignGameStarter gameStarter);

	public GarrisonBehavior getGarrisonBehavior()
	{
		return Main.GarrisonBehavior;
	}

	public void UpdateRibbons()
	{
		ribbonManager.UpdateRibbons();
	}

	public bool Back_on_condition(MenuCallbackArgs args)
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		args.optionLeaveType = (LeaveType)16;
		return true;
	}

	public virtual void Back_on_consequence(MenuCallbackArgs args)
	{
		Settlement currentSettlement = Settlement.CurrentSettlement;
		if (currentSettlement.IsTown)
		{
			GameMenu.SwitchToMenu("town_keep");
		}
		else if (currentSettlement.IsCastle)
		{
			GameMenu.SwitchToMenu("castle");
		}
		ribbonManager.CloseAllRibbons();
		if (!GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap)
		{
			UIManager.Instance.CloseImprovedGarrisonsUI();
		}
	}
}
