using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.MapRibbon;

/// <summary>
/// IG-style sliding drawer controller for the campaign-map floating panel.
/// The XML wires the hidden overlay, the exposed extend button, and its arrow
/// into this widget; this class only toggles the overlay visual state.
/// </summary>
public sealed class SovereignTownsFloatingPanelWidget : Widget
{
    private Widget? _overlay;
    private BrushWidget? _extendButtonArrowWidget;
    private ButtonWidget? _extendButtonWidget;

    public SovereignTownsFloatingPanelWidget(UIContext context)
        : base(context)
    {
    }

    public Widget? Overlay
    {
        get => _overlay;
        set
        {
            if (value == _overlay) return;
            _overlay = value;
            OnPropertyChanged(value, nameof(Overlay));
            UpdateOverlayState();
        }
    }

    public ButtonWidget? ExtendButtonWidget
    {
        get => _extendButtonWidget;
        set
        {
            if (value == _extendButtonWidget) return;
            if (_extendButtonWidget != null)
                _extendButtonWidget.ClickEventHandlers.Remove(OnExtendButtonClick);

            _extendButtonWidget = value;
            OnPropertyChanged(value, nameof(ExtendButtonWidget));

            if (_extendButtonWidget != null)
                _extendButtonWidget.ClickEventHandlers.Add(OnExtendButtonClick);
        }
    }

    public BrushWidget? ExtendButtonArrowWidget
    {
        get => _extendButtonArrowWidget;
        set
        {
            if (value == _extendButtonArrowWidget) return;
            _extendButtonArrowWidget = value;
            OnPropertyChanged(value, nameof(ExtendButtonArrowWidget));
            UpdateArrowDirection();
        }
    }

    protected override void OnLateUpdate(float dt)
    {
        base.OnLateUpdate(dt);

        try
        {
            if (Overlay != null && Overlay.CurrentState != SovereignTownsRibbonInjector.CurrentUiState.ToString())
                UpdateOverlayState();
        }
        catch (Exception ex)
        {
            Logger.Error("FloatingPanelWidget.OnLateUpdate failed", ex);
        }
    }

    private void OnExtendButtonClick(Widget button)
    {
        try
        {
            SovereignTownsRibbonInjector.InvertPanelState();
            UpdateOverlayState();
        }
        catch (Exception ex)
        {
            Logger.Error("FloatingPanelWidget.OnExtendButtonClick failed", ex);
        }
    }

    private void UpdateOverlayState()
    {
        if (Overlay == null) return;
        Overlay.SetState(SovereignTownsRibbonInjector.CurrentUiState.ToString());
        UpdateArrowDirection();
    }

    private void UpdateArrowDirection()
    {
        var arrowBrush = ExtendButtonArrowWidget?.Brush;
        if (arrowBrush == null) return;

        bool expanded = SovereignTownsRibbonInjector.CurrentUiState == SovereignTownsRibbonInjector.FloatingPanelState.Expanded;
        foreach (var style in arrowBrush.Styles)
        {
            foreach (var layer in style.GetLayers())
                layer.HorizontalFlip = expanded;
        }
    }
}
