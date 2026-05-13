using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.GuardsUtils;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;

public class GuardsUIVM : ViewModel
{
	private bool _hasNoActiveGuard;

	private string _guardStatus;

	private GarrisonSettings CurrentGarrisonSettings => Main.GarrisonBehavior.GetCurrentTownSettings();

	private MobileGarrison CurrentMobileGarrison
	{
		get
		{
			Town currentTownForSettings = Main.GarrisonBehavior.CurrentTownForSettings;
			if (currentTownForSettings != null)
			{
				return Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)currentTownForSettings).Settlement);
			}
			return null;
		}
	}

	public MBBindingList<ImprovedGarrisonsOptionVM> GuardSettings { get; set; }

	public MBBindingList<ImprovedGarrisonsTroopItemWidgetVM> GuardTroops { get; set; }

	public MBBindingList<GuardOrdersVM> GuardOrders { get; set; }

	public MBBindingList<ImprovedGarrisonsInformationListVM> GuardInformation { get; set; }

	public ImprovedGarrisonsOptionVM ToggleAutoGuardCreation { get; set; }

	public string GuardInfoText { get; } = ((object)new TextObject("{=ui_guardsui_infotitle}Guard party information", (Dictionary<string, object>)null)).ToString();


	public string CreateGuardText { get; } = ((object)new TextObject("{=ui_guardsui_createguard}Create a new guard party", (Dictionary<string, object>)null)).ToString();


	public string GuardOrdersTitleText { get; } = ((object)new TextObject("{=ui_guardsui_guardorders}Guard party orders", (Dictionary<string, object>)null)).ToString();


	public bool HasNoActiveGuard
	{
		get
		{
			return _hasNoActiveGuard;
		}
		set
		{
			if (value != _hasNoActiveGuard)
			{
				_hasNoActiveGuard = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HasNoActiveGuard");
			}
		}
	}

	public string GuardStatus
	{
		get
		{
			return _guardStatus;
		}
		set
		{
			if (value != _guardStatus)
			{
				_guardStatus = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "GuardStatus");
			}
		}
	}

	public GuardsUIVM()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected O, but got Unknown
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected O, but got Unknown
		InitializeAll();
		ToggleAutoGuardCreation = new ImprovedGarrisonsOptionVM();
		ToggleAutoGuardCreation.SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_autocreateguards1}Automatic guard party creation", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.GuardsAutoSpawn, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.ToggleAutoGuards(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_autocreateguards2}Allow this garrison to automatically create a guard party.", (Dictionary<string, object>)null));
		((ViewModel)this).RefreshValues();
	}

	public void InitializeAll()
	{
		InitializeGuardSettings();
		InitializeGuardOrders();
		InitializeGuardInformation();
	}

	private void InitializeGuardInformation()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		GuardInformation = new MBBindingList<ImprovedGarrisonsInformationListVM>();
		((Collection<ImprovedGarrisonsInformationListVM>)(object)GuardInformation).Add(new ImprovedGarrisonsInformationListVM(((object)new TextObject("{=ui_guardsui_healthytroops}Healthy troops", (Dictionary<string, object>)null)).ToString(), "0", () => (CurrentMobileGarrison != null) ? CurrentMobileGarrison.mobileParty.Party.NumberOfAllMembers.ToString() : "0", "General\\Icons\\Party@2x"));
		((Collection<ImprovedGarrisonsInformationListVM>)(object)GuardInformation).Add(new ImprovedGarrisonsInformationListVM(((object)new TextObject("{=ui_guardsui_woundedtroops}Wounded troops", (Dictionary<string, object>)null)).ToString(), "0", () => (CurrentMobileGarrison != null) ? CurrentMobileGarrison.mobileParty.Party.NumberOfWoundedTotalMembers.ToString() : "0", "General\\Icons\\Health@2x"));
		((Collection<ImprovedGarrisonsInformationListVM>)(object)GuardInformation).Add(new ImprovedGarrisonsInformationListVM(((object)new TextObject("{=ui_guardsui_prisoners}Prisoners", (Dictionary<string, object>)null)).ToString(), "0", () => (CurrentMobileGarrison != null) ? CurrentMobileGarrison.mobileParty.Party.NumberOfPrisoners.ToString() : "0", "General\\Icons\\TroopCost@2x"));
	}

	private void InitializeGuardOrders()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		GuardOrders = new MBBindingList<GuardOrdersVM>();
		((Collection<GuardOrdersVM>)(object)GuardOrders).Add(new GuardOrdersVM(((object)new TextObject("{=ui_guardsui_orderpatrol}Order to patrol", (Dictionary<string, object>)null)).ToString(), delegate
		{
			MobileGarrisonSettings.Instance.OrderMobileGarrisonToPatrol(Main.GarrisonBehavior.CurrentTownForSettings);
		}));
		((Collection<GuardOrdersVM>)(object)GuardOrders).Add(new GuardOrdersVM(((object)new TextObject("{=ui_guardsui_orderescort}Order to escort", (Dictionary<string, object>)null)).ToString(), delegate
		{
			MobileGarrisonSettings.Instance.PromptMobileGarrisonEscort(Main.GarrisonBehavior.CurrentTownForSettings);
		}));
		((Collection<GuardOrdersVM>)(object)GuardOrders).Add(new GuardOrdersVM(((object)new TextObject("{=ui_guardsui_orderreturn}Order to return", (Dictionary<string, object>)null)).ToString(), delegate
		{
			MobileGarrisonSettings.Instance.OrderMobileGarrisonReturn(Main.GarrisonBehavior.CurrentTownForSettings);
		}));
	}

	private void InitializeGuardSettings()
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected O, but got Unknown
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Expected O, but got Unknown
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected O, but got Unknown
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected O, but got Unknown
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Expected O, but got Unknown
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Expected O, but got Unknown
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Expected O, but got Unknown
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Expected O, but got Unknown
		//IL_0204: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Expected O, but got Unknown
		//IL_028a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0294: Expected O, but got Unknown
		//IL_02ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Expected O, but got Unknown
		//IL_02d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dc: Expected O, but got Unknown
		//IL_030c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0316: Expected O, but got Unknown
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0337: Expected O, but got Unknown
		//IL_0378: Unknown result type (might be due to invalid IL or missing references)
		//IL_0382: Expected O, but got Unknown
		//IL_0399: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a3: Expected O, but got Unknown
		//IL_03d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03dd: Expected O, but got Unknown
		//IL_03f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03fe: Expected O, but got Unknown
		//IL_042e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0438: Expected O, but got Unknown
		//IL_044f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0459: Expected O, but got Unknown
		//IL_0489: Unknown result type (might be due to invalid IL or missing references)
		//IL_0493: Expected O, but got Unknown
		//IL_04aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b4: Expected O, but got Unknown
		//IL_04e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ee: Expected O, but got Unknown
		GuardSettings = new MBBindingList<ImprovedGarrisonsOptionVM>();
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsTitle(((object)new TextObject("{=ui_guardsui_creationtitle}Smart guard creation settings", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_autodefend1}Automatically create a guard party to defend villages", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.GuardsAutoSpawnToDefend, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.ToggleAutoGuardDefend(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_autodefend2}This allows the current garrison to automatically create a guard party to defend raided villages.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_autoroam1}Automatically create a guard party to roam the region", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings != null && CurrentGarrisonSettings.GuardsAutoSpawn, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.ToggleAutoGuards(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_autoroam2}Allow this garrison to automatically create a guard party to fight bandits and enemy parties.", (Dictionary<string, object>)null)));
		List<string> list = new List<string>();
		list.Add(((object)new TextObject("{=ui_guardsui_partycreationsize_small}Small", (Dictionary<string, object>)null)).ToString());
		list.Add(((object)new TextObject("{=ui_guardsui_partycreationsize_medium}Medium", (Dictionary<string, object>)null)).ToString());
		list.Add(((object)new TextObject("{=ui_guardsui_partycreationsize_large}Large", (Dictionary<string, object>)null)).ToString());
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_guardsui_partycreationAutomatic guard creation party sizetysize", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.GuardsAutoSpawnSize, 10f, (Main.GarrisonBehavior.CurrentTownForSettings == null) ? 450 : ((((Fief)Main.GarrisonBehavior.CurrentTownForSettings).GarrisonParty != null) ? ((Fief)Main.GarrisonBehavior.CurrentTownForSettings).GarrisonParty.Party.PartySizeLimit : 450), discrete: true, delegate(float x)
		{
			MobileGarrisonSettings.Instance.SetAutoGarrisonSize(Main.GarrisonBehavior.CurrentTownForSettings, (int)x);
		}, new TextObject("{=ui_guardsui_partycreationsize2}Set the size of your guard party when automatically created by your current garrison.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_guardsui_autocreationthreshold1}Automatic guard creation threshold", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.GuardsAutoSpawnThreshold, 1f, (Main.GarrisonBehavior.CurrentTownForSettings == null) ? 450 : ((((Fief)Main.GarrisonBehavior.CurrentTownForSettings).GarrisonParty != null) ? ((Fief)Main.GarrisonBehavior.CurrentTownForSettings).GarrisonParty.Party.PartySizeLimit : 450), discrete: true, delegate(float x)
		{
			MobileGarrisonSettings.Instance.SetAutoGarrisonThreshold(Main.GarrisonBehavior.CurrentTownForSettings, (int)x);
		}, new TextObject("{=ui_guardsui_autocreationthreshold2}Set the garrison size that has to be reached for a guard to be automatically created. \n \nNote: the automatic guard party creation must be enabled.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsTitle(((object)new TextObject("{=ui_guardsui_settings}Guard behavior settings", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_allowupgrade1}Allow to upgrade troops", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.GuardEnableUpgradeTroops, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.ToggleUpgrade(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_allowupgrade2}Allow this guard party to upgrade its troops.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsSliderOption(((object)new TextObject("{=ui_guardsui_returnthreshold1}Return threshold (%)", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.GuardReturnPercentage * 100f, 1f, 90f, discrete: true, delegate(float x)
		{
			MobileGarrisonSettings.Instance.SetReturnPercentage(Main.GarrisonBehavior.CurrentTownForSettings, x / 100f);
		}, new TextObject("{=ui_guardsui_returnthreshold2}Set the threshold in relation to the initial party size for the guard party to return. \n \n Set this to 0.9 and the guard party will return after losing 10% of its troops \n \n Set this to 0.1 and 90% of the party's initial size has to be lost before returning.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_allowreplenish1}Allow to replenish", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.EnableReplenish, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.ToggleReplenish(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_allowreplenish2}Allow this guard party to go back to their town/castle to replenish their lost troops and to heal if they are wounded. \n \nGuard parties will only replenish troop types that were in their initial setup! Therefore, make sure to have these units recruited to the garrison if you want the guard party to pick them up when they replenish.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_allowsell1}Allow to ransom prisoners", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.EnablePrisonerSell, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.TogglePrisonerSell(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_allowsell2}Allow this guard party to ransom captured prisoners.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_allowrecruit1}Allow to recruit prisoners", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.GuardEnablePrisonerRecruitment, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.TogglePrisonerRecruit(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_allowrecruit2}Allow this guard party to recruit captured prisoners.", (Dictionary<string, object>)null)));
		((Collection<ImprovedGarrisonsOptionVM>)(object)GuardSettings).Add(new ImprovedGarrisonsOptionVM().SetAsBooleanOption(((object)new TextObject("{=ui_guardsui_allowhorses1}Allow to buy horses", (Dictionary<string, object>)null)).ToString(), CurrentGarrisonSettings.EnableHorseBuy, delegate(bool x)
		{
			MobileGarrisonSettings.Instance.ToggleHorseBuy(Main.GarrisonBehavior.CurrentTownForSettings, x);
		}, new TextObject("{=ui_guardsui_allowhorses2}Allow this guard party to buy horses to increase its movement speed.", (Dictionary<string, object>)null)));
	}

	public void OnCreateGuardButtonPress()
	{
		MobileGarrisonSettings.Instance.PromptCreateMobileGarrison(Main.GarrisonBehavior.CurrentTownForSettings);
	}

	public void ExecuteLink(string link)
	{
		Campaign.Current.EncyclopediaManager.GoToLink(link);
	}

	public override void RefreshValues()
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected O, but got Unknown
		((ViewModel)this).RefreshValues();
		HasNoActiveGuard = CurrentMobileGarrison == null;
		GuardStatus = ((CurrentMobileGarrison != null) ? CurrentMobileGarrison.GetStatusText() : ((object)new TextObject("{=ui_guardsui_noguards}There is no active guard party", (Dictionary<string, object>)null)).ToString());
		foreach (ImprovedGarrisonsInformationListVM item in (Collection<ImprovedGarrisonsInformationListVM>)(object)GuardInformation)
		{
			((ViewModel)item).RefreshValues();
		}
	}
}
