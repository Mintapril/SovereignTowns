using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace ImprovedGarrisons.ImprovedGarrisonsUI;

public class ImprovedGarrisonsUIWidget : Widget
{
	private Widget _overlay;

	private BrushWidget _extendButtonArrowWidget;

	private ButtonWidget _extendButtonWidget;

	private ButtonWidget _createGuardsButtonWidget;

	public TabControl TabcontrolWidget { get; set; }

	public ButtonWidget CreateGuardsButton
	{
		get
		{
			return _createGuardsButtonWidget;
		}
		set
		{
			if (value != _createGuardsButtonWidget)
			{
				_createGuardsButtonWidget = value;
				((PropertyOwnerObject)this).OnPropertyChanged<ButtonWidget>(value, "CreateGuardsButton");
				_createGuardsButtonWidget.ClickEventHandlers.Add(OnCreateGuardButtonClick);
			}
		}
	}

	public Widget ContentWidget { get; set; }

	public Widget Overlay
	{
		get
		{
			return _overlay;
		}
		set
		{
			if (value != _overlay)
			{
				_overlay = value;
				((PropertyOwnerObject)this).OnPropertyChanged<Widget>(value, "Overlay");
				RetractUI();
			}
		}
	}

	public ButtonWidget ExtendButtonWidget
	{
		get
		{
			return _extendButtonWidget;
		}
		set
		{
			if (_extendButtonWidget != value)
			{
				_extendButtonWidget = value;
				((PropertyOwnerObject)this).OnPropertyChanged<ButtonWidget>(value, "ExtendButtonWidget");
				if (_extendButtonWidget != null)
				{
					_extendButtonWidget.ClickEventHandlers.Add(OnExtendButtonClick);
				}
				FlipEachBrushStyle(((BrushWidget)ExtendButtonWidget).Brush);
			}
		}
	}

	public BrushWidget ExtendButtonArrowWidget
	{
		get
		{
			return _extendButtonArrowWidget;
		}
		set
		{
			if (value != _extendButtonArrowWidget)
			{
				_extendButtonArrowWidget = value;
				((PropertyOwnerObject)this).OnPropertyChanged<BrushWidget>(value, "ExtendButtonArrowWidget");
			}
		}
	}

	public bool IsOverlayExtended
	{
		get
		{
			if (UIManager.Instance.improvedGarrisonsUI != null)
			{
				return (UIManager.Instance.CurrentUiState != 0) ? true : false;
			}
			return false;
		}
	}

	public ImprovedGarrisonsUIWidget(UIContext context)
		: base(context)
	{
	}

	public void UpdateOverlayState()
	{
		if (UIManager.Instance.CurrentUiState == UIManager.UiState.Expanded)
		{
			ExtendUI();
		}
		else
		{
			RetractUI();
		}
	}

	private void OnCreateGuardButtonClick(Widget button)
	{
		MobileGarrisonSettings.Instance.PromptCreateMobileGarrison(Main.GarrisonBehavior.CurrentTownForSettings);
	}

	private void OnExtendButtonClick(Widget button)
	{
		UIManager.Instance.InvertUiState();
		UpdateOverlayState();
	}

	protected override void OnLateUpdate(float dt)
	{
		((Widget)this).OnLateUpdate(dt);
		string id = TabcontrolWidget.ActiveTab.Id;
		if (UIManager.Instance.improvedGarrisonsUI.WantToChangeTab)
		{
			id = UIManager.Instance.improvedGarrisonsUI.WantToChangeTabToID;
			TabcontrolWidget.SetActiveTab(id);
			UIManager.Instance.improvedGarrisonsUI.WantToChangeTab = false;
		}
		UIManager.Instance.improvedGarrisonsUI.ActualCurrentTabId = TabcontrolWidget.ActiveTab.Id;
		if (UIManager.Instance.CurrentUiState.ToString() != Overlay.CurrentState)
		{
			UpdateOverlayState();
		}
	}

	public void ExtendUI()
	{
		Overlay.SetState("Expanded");
	}

	public void RetractUI()
	{
		Overlay.SetState("Retracted");
	}

	private void FlipEachBrushStyle(Brush brush)
	{
		foreach (Style style in brush.Styles)
		{
			StyleLayer[] layers = style.GetLayers();
			foreach (StyleLayer val in layers)
			{
				val.HorizontalFlip = !IsOverlayExtended;
			}
		}
	}
}
