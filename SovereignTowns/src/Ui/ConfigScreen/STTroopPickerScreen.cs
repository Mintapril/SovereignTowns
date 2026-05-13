using System;
using SovereignTowns.Configuration;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using GameKey = TaleWorlds.InputSystem.GameKey;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ConfigScreen;

/// <summary>
/// 兵种选择弹窗的 Screen 宿主。叠加在 <see cref="SovereignTownsConfigScreen"/> 之上，
/// 玩家保存或取消后弹出本 Screen 返回 ConfigScreen。
/// </summary>
/// <remarks>
/// 与 <see cref="SovereignTownsConfigScreen"/> 相同的 Gauntlet 生命周期；VM 的
/// <see cref="STTroopPickerVM.IsFinished"/> 由 Done/Cancel 按钮设置后，OnFrameTick
/// 检测到即弹出 Screen。
/// </remarks>
public sealed class STTroopPickerScreen : ScreenBase
{
    private readonly TownGarrisonRule _rule;
    private readonly string _ownerName;
    private readonly Action? _onSaved;
    private GauntletLayer? _layer;
    private STTroopPickerVM? _vm;

    public STTroopPickerScreen(TownGarrisonRule rule, string ownerName, Action? onSaved)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _ownerName = ownerName ?? "";
        _onSaved = onSaved;
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            _vm = new STTroopPickerVM(_rule, _ownerName);
            _layer = new GauntletLayer("GauntletLayer", 4100, false);
            _layer.LoadMovie("STTroopPickerPopup", _vm);
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            _layer.InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
            _layer.IsFocusLayer = true;

            AddLayer(_layer);
            ScreenManager.TrySetFocus(_layer);
            Logger.Info($"STTroopPickerScreen: initialized for '{_ownerName}'");
        }
        catch (Exception ex)
        {
            Logger.Error("STTroopPickerScreen.OnInitialize failed", ex);
            try { ScreenManager.PopScreen(); } catch { }
        }
    }

    protected override void OnFrameTick(float dt)
    {
        base.OnFrameTick(dt);
        try
        {
            if (_vm is null || _layer is null) return;

            bool exitHotKey = _layer.Input.IsHotKeyReleased("Exit");
            // Enter / NumpadEnter = ExecuteDone（保存）。屏蔽搜索框聚焦时的回车以免误触发，
            // 但 EditableTextWidget 的 Enter 默认只 commit 文本不发 Key 事件，这里直接读 raw
            // key 释放即可。先 try Confirm hotkey（若 GenericPanelGameKeyCategory 注册了），
            // 失败 / 未注册则回落到 InputKey.Enter / NumpadEnter。
            bool confirmHotKey = false;
            try { confirmHotKey = _layer.Input.IsHotKeyReleased("Confirm"); }
            catch { /* category 未注册 Confirm */ }
            bool enterKey = _layer.Input.IsKeyReleased(InputKey.Enter)
                            || _layer.Input.IsKeyReleased(InputKey.NumpadEnter);

            if (!_vm.IsFinished && (confirmHotKey || enterKey))
            {
                _vm.ExecuteDone();
            }

            if (_vm.IsFinished || exitHotKey)
            {
                bool wasSaved = _vm.SaveOnClose;
                ScreenManager.PopScreen();
                if (wasSaved) _onSaved?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("STTroopPickerScreen.OnFrameTick failed", ex);
        }
    }

    protected override void OnFinalize()
    {
        try
        {
            if (_layer != null)
            {
                RemoveLayer(_layer);
                _layer = null;
            }
            _vm = null;
        }
        catch (Exception ex)
        {
            Logger.Error("STTroopPickerScreen.OnFinalize failed", ex);
        }
        finally
        {
            base.OnFinalize();
        }
    }
}
