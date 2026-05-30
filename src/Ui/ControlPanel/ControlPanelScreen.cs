using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 游戏内控制面板的屏幕容器。由大地图按钮 PushScreen 弹出；PopScreen 关闭。
/// 打开期间暂停游戏（地图态时间在走，必须显式暂停）——暂停/恢复在 ControlPanelVM 内完成。
/// 结构镜像 _research/ImprovedGarrisons/ImprovedGarrisons.ConfigOptionsMenu/ConfigMenuGauntletScreen.cs。
/// </summary>
internal sealed class ControlPanelScreen : ScreenBase
{
    private GauntletLayer _layer;
    private ControlPanelVM _vm;
    // ui_options sprite category：SPOptions.* 字体 Brush 来源。LoadMovie 前必须 Load，
    // 否则 prefab 里的 Brush="SPOptions.*" 解析不到 → 文字以引擎默认大字号渲染。
    // 镜像 IG ConfigMenuGauntletScreen.OnInitialize。
    private SpriteCategory _spriteCategory;
    // OnInitialize 失败标记。若 LoadMovie 等步骤抛异常，屏幕已被 PushScreen 压栈、
    // VM 构造已 PauseGame()，但 AddLayer / 输入层尚未建立 —— 此时屏幕既无 UI 又无法
    // 关闭、且游戏处于暂停，表现为「卡死」。置位后由 OnFrameTick 兜底 PopScreen。
    private bool _initFailed;
    private bool _closeRequested;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            // 加载 ui_options sprite category，使 SPOptions.* 字体 Brush 可用（镜像 IG）。
            _spriteCategory = UIResourceManager.SpriteData.SpriteCategories["ui_options"];
            _spriteCategory.Load(UIResourceManager.ResourceContext, UIResourceManager.ResourceDepot);
            // VM 构造时即 PauseGame()（镜像 IG ConfigMenuVM ctor）。
            _vm = new ControlPanelVM();
            _layer = new GauntletLayer("GauntletLayer", 4000, false);
            _layer.LoadMovie("SovereignTownsControlPanel", _vm);
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            // IG 全程使用 (InputUsageMask)7 = Mouse|Keyboard|Controller。
            _layer.InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
            _layer.IsFocusLayer = true;
            AddLayer(_layer);
            ScreenManager.TrySetFocus(_layer);
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelScreen.OnInitialize failed", ex);
            // 优雅降级：恢复游戏并标记，让 OnFrameTick 把这个半成品屏幕弹掉，
            // 绝不把玩家留在「暂停 + 无 UI + 无出口」的卡死态。
            _initFailed = true;
            try { _vm?.UnpauseGame(); }
            catch (Exception unpauseEx) { Logger.Error("ControlPanelScreen init-fail unpause failed", unpauseEx); }
        }
    }

    protected override void OnFrameTick(float dt)
    {
        base.OnFrameTick(dt);
        try
        {
            if (_initFailed)
            {
                if (_closeRequested) return;
                _closeRequested = true;
                ScreenManager.PopScreen();
                return;
            }
            if (_vm != null && _layer != null
                && (_vm.IsClosing || _layer.Input.IsHotKeyReleased("Exit")))
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelScreen.OnFrameTick failed", ex);
        }
    }

    private void Close()
    {
        if (_closeRequested) return;
        _closeRequested = true;
        try
        {
            // 恢复游戏必须在 PopScreen 之前，由暂停时的同一个 VM 实例完成（镜像 IG OnFrameTick）。
            _vm?.UnpauseGame();
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelScreen unpause failed", ex);
        }
        ScreenManager.PopScreen();
    }

    protected override void OnFinalize()
    {
        base.OnFinalize();
        try
        {
            _vm?.OnFinalize();
            _spriteCategory?.Unload();
            _vm = null;
            _layer = null;
            _spriteCategory = null;
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelScreen.OnFinalize failed", ex);
        }
    }
}
