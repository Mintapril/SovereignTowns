using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ImprovedGarrisons.ImprovedGarrisonsUI;

public class ImprovedGarrisonsUIGauntlet : MapView
{
	public bool WantToChangeTab = false;

	private string _wantToChangeTabToID;

	private GauntletLayer _layer;

	private ImprovedGarrisonsUIVM _datasource;

	private OverviewUIVM _compactDatsource;

	private GauntletLayer _compactLayer;

	public string ActualCurrentTabId { get; set; }

	public string WantToChangeTabToID
	{
		get
		{
			return _wantToChangeTabToID;
		}
		set
		{
			_wantToChangeTabToID = value;
			WantToChangeTab = true;
		}
	}

	public ImprovedGarrisonsUIGauntlet()
	{
		((MapView)this).CreateLayout();
	}

	protected override void CreateLayout()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		((MapView)this).CreateLayout();
		_layer = new GauntletLayer("GauntletLayer", 550, false);
		_datasource = new ImprovedGarrisonsUIVM();
		_layer.LoadMovie("ImprovedGarrisonsMenu", (ViewModel)(object)_datasource);
		((ScreenLayer)_layer).InputRestrictions.SetInputRestrictions(false, (InputUsageMask)7);
		((ScreenBase)MapScreen.Instance).AddLayer((ScreenLayer)(object)_layer);
		ScreenManager.TrySetFocus((ScreenLayer)(object)_layer);
	}

	public void ChangeSelectorSelectionToCurrentSettlement()
	{
		if (_datasource != null)
		{
			_datasource.ChangeSelectorSelectionToCurrentSettlement();
		}
	}

	public void ChangeSelectorSelection(Settlement settlement)
	{
		if (_datasource != null)
		{
			_datasource.ChangeSelectorSelection(settlement);
		}
	}

	public void SwitchToCompactUI()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		CloseUi();
		_compactLayer = new GauntletLayer("GauntletLayer", 560, false);
		_compactDatsource = new OverviewUIVM();
		_compactLayer.LoadMovie("ImprovedGarrisonsCompactOverview", (ViewModel)(object)_datasource);
		((ScreenLayer)_compactLayer).InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
		((ScreenBase)MapScreen.Instance).AddLayer((ScreenLayer)(object)_compactLayer);
		ScreenManager.TrySetFocus((ScreenLayer)(object)_compactLayer);
	}

	public void SwitchToTab(string tabId)
	{
		WantToChangeTabToID = tabId;
	}

	public void SwitchBackToNormalUI()
	{
		CloseCompactUI();
		((MapView)this).CreateLayout();
	}

	public void CloseCompactUI()
	{
		((ScreenBase)MapScreen.Instance).RemoveLayer((ScreenLayer)(object)_compactLayer);
	}

	public void CloseUi()
	{
		((ScreenBase)MapScreen.Instance).RemoveLayer((ScreenLayer)(object)_layer);
	}

	public void UpdateUiContents()
	{
		_datasource.UpdateUiContents();
	}

	public void ForceFullRefresh()
	{
		_datasource.ForceFullRefresh();
	}

	public void ForceOverviewUpdate()
	{
		_datasource.ForceOverviewUpdate();
	}

	public void UpdateCurrentUiTab()
	{
		((ViewModel)_datasource).RefreshValues();
	}

	public void UpdateSettlementSelector()
	{
		_datasource.UpdateSettlementSelector();
	}
}
