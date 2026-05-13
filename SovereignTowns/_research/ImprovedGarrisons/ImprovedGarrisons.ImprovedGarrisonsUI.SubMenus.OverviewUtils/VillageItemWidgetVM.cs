using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.ElementLists;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.OverviewUtils;

public class VillageItemWidgetVM : ViewModel
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

	private Settlement Settlement { get; set; }

	public HintViewModel SettlementImageHoverHint { get; set; } = new HintViewModel(new TextObject("{=ui_villagetitemwidget_track}Track village", (Dictionary<string, object>)null), (string)null);


	public VillageItemWidgetVM(Settlement settlement)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		SettlementComponent settlementComponent = settlement.SettlementComponent;
		FileName = ((settlementComponent == null) ? "placeholder" : (settlementComponent.BackgroundMeshName + "_t"));
		Settlement = settlement;
		((ViewModel)this).RefreshValues();
	}

	public void OpenContextMenu()
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected O, but got Unknown
		MBBindingList<CascadeMenuElementVM> val = new MBBindingList<CascadeMenuElementVM>();
		MBBindingList<ImprovedGarrisonsPartyInformationVM> val2 = new MBBindingList<ImprovedGarrisonsPartyInformationVM>();
		if (Settlement.IsUnderRaid || Settlement.IsUnderSiege)
		{
			SettlementDefenceActions settlementDefenceActions = new SettlementDefenceActions(Settlement);
			((Collection<CascadeMenuElementVM>)(object)val).Add((CascadeMenuElementVM)new CascadeMenuExtendButtonVM(settlementDefenceActions.Title, settlementDefenceActions.Menu));
		}
		UIManager.Instance.CreateCascadeMenuOnMousePointer(((object)new TextObject("{=ui_improvedgarrisonsui_activity_village1}Village action", (Dictionary<string, object>)null)).ToString(), val);
	}

	public void ExecuteTrack()
	{
		if (!Campaign.Current.VisualTrackerManager.CheckTracked((ITrackableBase)(object)Settlement))
		{
			Campaign.Current.VisualTrackerManager.RegisterObject((ITrackableCampaignObject)(object)Settlement);
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		Settlement settlement = Settlement;
		NameText = ((settlement != null) ? ((object)settlement.Name).ToString() : null) ?? "";
	}
}
