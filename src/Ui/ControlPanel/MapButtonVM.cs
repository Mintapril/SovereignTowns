using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 大地图左侧常驻「打开控制面板」按钮的 ViewModel。
/// 仅持一个本地化标签 + 一个点击命令；点击 → PushScreen(ControlPanelScreen)。
/// </summary>
public sealed class MapButtonVM : ViewModel
{
    private string _label;

    [DataSourceProperty]
    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); } }
    }

    public MapButtonVM()
    {
        _label = ControlPanelLoc.Tr("控制面板", "Control Panel");
    }

    /// <summary>按钮点击 → 弹出控制面板。</summary>
    public void ExecuteOpen()
    {
        try
        {
            ScreenManager.PushScreen(new ControlPanelScreen());
        }
        catch (System.Exception ex) { Logger.Error("MapButtonVM.ExecuteOpen failed", ex); }
    }
}
