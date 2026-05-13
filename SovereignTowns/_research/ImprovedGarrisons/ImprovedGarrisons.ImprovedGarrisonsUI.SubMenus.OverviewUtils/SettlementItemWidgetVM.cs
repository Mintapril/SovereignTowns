using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.ElementLists;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.OverviewUtils;

public class SettlementItemWidgetVM : ViewModel
{
	private string _fileName;

	private string _nameText;

	private string _color;

	private string _status;

	public string FileName
	{
		get
		{
			return _fileName;
		}
		set
		{
			if (value != _fileName)
			{
				_fileName = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "FileName");
			}
		}
	}

	public string NameText
	{
		get
		{
			return _nameText;
		}
		set
		{
			if (value != _nameText)
			{
				_nameText = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "NameText");
			}
		}
	}

	public string Color
	{
		get
		{
			return _color;
		}
		set
		{
			if (value != _color)
			{
				_color = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Color");
			}
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			if (value != _status)
			{
				_status = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Status");
			}
		}
	}

	public HintViewModel SettlementImageHoverHint { get; set; } = new HintViewModel(new TextObject("{=ui_settlementitemwidget_track}Track location", (Dictionary<string, object>)null), (string)null);


	public bool IsWarningState { get; set; }

	private Settlement Settlement { get; set; }

	public ImprovedGarrisonsUIWidget Widget { get; set; }

	public MBBindingList<SettlementInformationVM> SettlementInformation { get; set; }

	public MBBindingList<VillageItemWidgetVM> Villages { get; set; }

	public SettlementItemWidgetVM(Settlement settlement)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		SettlementComponent settlementComponent = settlement.SettlementComponent;
		FileName = ((settlementComponent == null) ? "placeholder" : (settlementComponent.BackgroundMeshName + "_t"));
		Settlement = settlement;
		SettlementInformation = new MBBindingList<SettlementInformationVM>();
		((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(Settlement).SetAsGarrisonInformation());
		((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(Settlement).SetAsFoodChangeInformation());
		((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(Settlement).SetAsGoldChangeInformation());
		((Collection<SettlementInformationVM>)(object)SettlementInformation).Add(new SettlementInformationVM(Settlement).SetAsMobileGarrisonInformation());
		((ViewModel)this).RefreshValues();
	}

	private void InitializeVillages()
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Expected O, but got Unknown
		Villages = new MBBindingList<VillageItemWidgetVM>();
		foreach (Village item in (List<Village>)(object)Settlement.BoundVillages)
		{
			if (((SettlementComponent)item).Settlement != null)
			{
				if (((SettlementComponent)item).Settlement.IsUnderRaid)
				{
					VillageItemWidgetVM villageItemWidgetVM = new VillageItemWidgetVM(((SettlementComponent)item).Settlement);
					villageItemWidgetVM.Status = ((object)new TextObject("{=ui_settlementitemwidget_raid}Under raid", (Dictionary<string, object>)null)).ToString();
					villageItemWidgetVM.Color = ModuleColors.uiColorAttacked;
					((Collection<VillageItemWidgetVM>)(object)Villages).Add(villageItemWidgetVM);
					IsWarningState = true;
					Color = ModuleColors.uiColorWarning;
					Status = ((object)new TextObject("{=ui_settlementitemwidget_hostileraid}Hostile raid", (Dictionary<string, object>)null)).ToString();
				}
				else if (((SettlementComponent)item).Settlement.IsRaided)
				{
					VillageItemWidgetVM villageItemWidgetVM2 = new VillageItemWidgetVM(((SettlementComponent)item).Settlement);
					villageItemWidgetVM2.Status = ((object)new TextObject("{=ui_settlementitemwidget_raided}Raided", (Dictionary<string, object>)null)).ToString();
					villageItemWidgetVM2.Color = ModuleColors.uiColorDestroyed;
					((Collection<VillageItemWidgetVM>)(object)Villages).Add(villageItemWidgetVM2);
				}
			}
		}
	}

	public void ExecuteTrack()
	{
		if (!Campaign.Current.VisualTrackerManager.CheckTracked((ITrackableBase)(object)Settlement))
		{
			Campaign.Current.VisualTrackerManager.RegisterObject((ITrackableCampaignObject)(object)Settlement);
		}
	}

	public void ExecuteChangeSelection()
	{
		UIManager.Instance.improvedGarrisonsUI.ChangeSelectorSelection(Settlement);
	}

	public void OpenContextMenu()
	{
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Expected O, but got Unknown
		MBBindingList<CascadeMenuElementVM> val = new MBBindingList<CascadeMenuElementVM>();
		CreateOrReturnGuardAction createOrReturnGuardAction = new CreateOrReturnGuardAction(Settlement);
		((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)new CascadeMenuBaseButtonVM(createOrReturnGuardAction.Title, createOrReturnGuardAction.Action));
		CreateOrReturnRecruiterAction createOrReturnRecruiterAction = new CreateOrReturnRecruiterAction(Settlement);
		((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)new CascadeMenuBaseButtonVM(createOrReturnRecruiterAction.Title, createOrReturnRecruiterAction.Action));
		if (Settlement.IsUnderRaid || Settlement.IsUnderSiege)
		{
			SettlementDefenceActions settlementDefenceActions = new SettlementDefenceActions(Settlement);
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)new CascadeMenuExtendButtonVM(settlementDefenceActions.Title, settlementDefenceActions.Menu));
		}
		UIManager.Instance.CreateCascadeMenuOnMousePointer(((object)new TextObject("{=ui_improvedgarrisonsui_activity}Settlement action", (Dictionary<string, object>)null)).ToString(), val);
	}

	public override void RefreshValues()
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		((ViewModel)this).RefreshValues();
		Settlement settlement = Settlement;
		NameText = ((settlement != null) ? ((object)settlement.Name).ToString() : null) ?? "";
		InitializeVillages();
		if (settlement != null && settlement.Town != null)
		{
			if (settlement.IsUnderSiege)
			{
				Color = ModuleColors.uiColorAttacked;
				Status = ((object)new TextObject("{=ui_settlementitemwidget_siege}Under siege", (Dictionary<string, object>)null)).ToString();
				IsWarningState = true;
			}
			else if (!IsWarningState)
			{
				Color = ModuleColors.uiColorPeace;
				Status = ((object)new TextObject("{=ui_settlementitemwidget_peace}Peaceful", (Dictionary<string, object>)null)).ToString();
			}
		}
	}
}
