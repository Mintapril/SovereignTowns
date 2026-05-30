using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 大地图左侧常驻「打开控制面板」按钮。模仿 IG 的 ImprovedGarrisonsUIGauntlet
/// （_research/ImprovedGarrisons/ImprovedGarrisons.ImprovedGarrisonsUI/ImprovedGarrisonsUIGauntlet.cs）：
/// 子类化 MapView，在 CreateLayout() 里建 GauntletLayer、LoadMovie、AddLayer 到 MapScreen.Instance。
/// 输入限制传 false（不抢占地图拖拽/缩放），只有按钮自身消费点击。
/// </summary>
internal sealed class ControlPanelMapButtonView : MapView
{
    private GauntletLayer? _layer;
    private MapButtonVM? _vm;
    private bool _disposed;

    public ControlPanelMapButtonView()
    {
        CreateLayout();
    }

    protected override void CreateLayout()
    {
        base.CreateLayout();
        try
        {
            _vm = new MapButtonVM();
            // IG ImprovedGarrisonsUIGauntlet.CreateLayout:49 — new GauntletLayer(name, localOrder, shouldClear=false)。
            _layer = new GauntletLayer("GauntletLayer", 200, false);
            _layer.LoadMovie("SovereignTownsMapButton", _vm);
            // IG:52 — false = 不阻挡地图输入；(InputUsageMask)7 = Mouse|Keyboard|Controller。
            _layer.InputRestrictions.SetInputRestrictions(false, (InputUsageMask)7);
            // IG:53 — MapScreen.Instance 转 ScreenBase 后 AddLayer。GauntletLayer 隐式可转 ScreenLayer。
            ((ScreenBase)MapScreen.Instance).AddLayer(_layer);
        }
        catch (System.Exception ex) { Logger.Error("ControlPanelMapButtonView.CreateLayout failed", ex); }
    }

    internal void DisposeView()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_layer != null && MapScreen.Instance != null)
            {
                ((ScreenBase)MapScreen.Instance).RemoveLayer(_layer);
            }
        }
        catch (System.Exception ex)
        {
            Logger.Error("ControlPanelMapButtonView.DisposeView remove layer failed", ex);
        }

        try { _vm?.OnFinalize(); }
        catch (System.Exception ex) { Logger.Error("ControlPanelMapButtonView.DisposeView VM finalize failed", ex); }

        _layer = null;
        _vm = null;
    }

    protected override void OnFinalize()
    {
        DisposeView();
        base.OnFinalize();
    }
}
