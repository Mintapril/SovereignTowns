using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.Menu;

public class MainMenu : ImprovedGarrisonMenu
{
	public MainMenu(CampaignGameStarter gameStarter)
		: base(gameStarter)
	{
	}

	public override void AddGameMenus(CampaignGameStarter gameStarter)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		//IL_003e: Expected O, but got Unknown
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected O, but got Unknown
		//IL_007c: Expected O, but got Unknown
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Expected O, but got Unknown
		//IL_00b9: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Expected O, but got Unknown
		//IL_00f7: Expected O, but got Unknown
		try
		{
			gameStarter.AddGameMenuOption("town_keep", "town_garrison_options", ((object)new TextObject("{=menu_start}Improved Garrison", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(Garrison_menu_on_playerowner_condition), (OnConsequenceDelegate)delegate
			{
				garrisonBehavior.CurrentTownForSettings = Settlement.CurrentSettlement.Town;
				if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
				{
					UIManager.Instance.improvedGarrisonsUI.UpdateSettlementSelector();
					UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
				}
			}, false, 1, false, (object)null);
			gameStarter.AddGameMenuOption("castle", "town_garrison_options", ((object)new TextObject("{=menu_start}Improved Garrison", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(Garrison_menu_on_playerowner_condition), (OnConsequenceDelegate)delegate
			{
				garrisonBehavior.CurrentTownForSettings = Settlement.CurrentSettlement.Town;
				if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
				{
					UIManager.Instance.improvedGarrisonsUI.UpdateSettlementSelector();
					UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
				}
			}, false, 1, false, (object)null);
			gameStarter.AddGameMenuOption("town_keep", "town_garrison_options", ((object)new TextObject("{=menu_manage}Manage your Improved Garrisons", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(Garrison_menu_on_playerNotowner_condition), (OnConsequenceDelegate)delegate
			{
				garrisonBehavior.CurrentTownForSettings = Settlement.CurrentSettlement.Town;
				if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
				{
					UIManager.Instance.improvedGarrisonsUI.UpdateSettlementSelector();
					UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
				}
			}, false, 1, false, (object)null);
			gameStarter.AddGameMenuOption("castle", "town_garrison_options", ((object)new TextObject("{=menu_manage}Manage your Improved Garrisons", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(Garrison_menu_on_playerNotowner_condition), (OnConsequenceDelegate)delegate
			{
				garrisonBehavior.CurrentTownForSettings = Settlement.CurrentSettlement.Town;
				if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
				{
					UIManager.Instance.improvedGarrisonsUI.UpdateSettlementSelector();
					UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
				}
			}, false, 1, false, (object)null);
			gameStarter.AddGameMenu("garrison_options", "Menu no longer supported", (OnInitDelegate)null, (MenuOverlayType)2, (MenuFlags)0, (object)null);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private bool Garrison_menu_on_playerowner_condition(MenuCallbackArgs args)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		args.optionLeaveType = (LeaveType)2;
		Settlement currentSettlement = Settlement.CurrentSettlement;
		return currentSettlement.OwnerClan == Clan.PlayerClan && currentSettlement.MapFaction == Hero.MainHero.MapFaction && currentSettlement.IsFortification;
	}

	private bool Garrison_menu_on_playerNotowner_condition(MenuCallbackArgs args)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		args.optionLeaveType = (LeaveType)2;
		Settlement currentSettlement = Settlement.CurrentSettlement;
		bool flag = currentSettlement.OwnerClan != Clan.PlayerClan;
		bool flag2 = MobileParty.MainParty != null && !MobileParty.MainParty.Party.MapFaction.IsAtWarWith(currentSettlement.MapFaction);
		bool flag3 = Main.GarrisonBehavior.SettlementSettingsData != null && Main.GarrisonBehavior.SettlementSettingsData.Count > 1;
		return flag && flag2 && currentSettlement.IsFortification && flag3;
	}

	private void Inquirydata_GlobalGarrisonSettingsMenu(List<InquiryElement> list)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Expected O, but got Unknown
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Expected O, but got Unknown
		if (list.Count > 0)
		{
			Town val = (Town)Extensions.GetRandomElement<InquiryElement>((IReadOnlyList<InquiryElement>)list).Identifier;
			garrisonBehavior.CurrentTownForSettings = val;
			GameMenu.SwitchToMenu("garrison_options");
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_global_conf}You are now editing the settings of", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)val).Name, Color.FromUint(ModuleColors.green)));
		}
	}

	public void BackToMainMenu()
	{
		Back_on_consequence(null);
	}
}
