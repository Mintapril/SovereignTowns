using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.OverviewUtils;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;

public class OverviewUIVM : ViewModel
{
	private GarrisonSettings CurrentGarrisonSettings => Main.GarrisonBehavior.GetCurrentTownSettings();

	public MBBindingList<SettlementItemWidgetVM> Settlements { get; set; }

	public string OverviewTitleText { get; } = ((object)new TextObject("{=ui_overviewui_fiefs}Your fiefs", (Dictionary<string, object>)null)).ToString();


	public OverviewUIVM()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		((ViewModel)this).RefreshValues();
	}

	private void RefreshSettlements()
	{
		Settlements = new MBBindingList<SettlementItemWidgetVM>();
		if (Clan.PlayerClan == null || Clan.PlayerClan.Fiefs == null)
		{
			return;
		}
		foreach (Town item in (List<Town>)(object)Clan.PlayerClan.Fiefs)
		{
			if (((SettlementComponent)item).Settlement != null)
			{
				SettlementItemWidgetVM settlementItemWidgetVM = new SettlementItemWidgetVM(((SettlementComponent)item).Settlement);
				if (settlementItemWidgetVM.IsWarningState)
				{
					((Collection<SettlementItemWidgetVM>)(object)Settlements).Insert(0, settlementItemWidgetVM);
				}
				else
				{
					((Collection<SettlementItemWidgetVM>)(object)Settlements).Add(new SettlementItemWidgetVM(((SettlementComponent)item).Settlement));
				}
			}
		}
		((ViewModel)this).OnPropertyChanged("Settlements");
	}

	public void OnCompactModePress()
	{
		UIManager.Instance.improvedGarrisonsUI.SwitchToCompactUI();
	}

	public void OnCompactModeReturnPress()
	{
		UIManager.Instance.improvedGarrisonsUI.SwitchBackToNormalUI();
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		RefreshSettlements();
	}
}
