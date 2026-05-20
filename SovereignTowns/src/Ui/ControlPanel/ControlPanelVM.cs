using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 游戏内控制面板的根 ViewModel。
/// 暂停 / 恢复游戏在 VM 内完成（镜像 ImprovedGarrisons 的 ConfigMenuVM.PauseGame/UnpauseGame，
/// 见 _research/ImprovedGarrisons/ImprovedGarrisons.ConfigOptionsMenu/ConfigMenuVM.cs:2704-2733）：
/// RegisterActiveStateDisableRequest 以 VM 自身作为 request token，因此暂停/恢复必须由同一个 VM 实例完成。
/// </summary>
public sealed class ControlPanelVM : ViewModel
{
    private bool _isClosing;

    /// <summary>屏幕轮询此标记决定是否 PopScreen。</summary>
    public bool IsClosing => _isClosing;

    /// <summary>构造时立即暂停游戏（面板从大地图弹出，地图态时间在走）。镜像 IG：ctor 末尾调用 PauseGame。</summary>
    public ControlPanelVM()
    {
        PauseGame();
    }

    /// <summary>关闭按钮 / ESC 调用。</summary>
    public void ExecuteClose()
    {
        _isClosing = true;
    }

    /// <summary>暂停游戏。写法以 _research 的 ConfigMenuVM.PauseGame 为准。</summary>
    internal void PauseGame()
    {
        try
        {
            if (Game.Current != null)
            {
                Game.Current.GameStateManager.RegisterActiveStateDisableRequest((object)this);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelVM.PauseGame failed", ex);
        }
    }

    /// <summary>恢复游戏。写法以 _research 的 ConfigMenuVM.UnpauseGame 为准。</summary>
    internal void UnpauseGame()
    {
        try
        {
            if (Game.Current != null)
            {
                Game.Current.GameStateManager.UnregisterActiveStateDisableRequest((object)this);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelVM.UnpauseGame failed", ex);
        }
    }
}
