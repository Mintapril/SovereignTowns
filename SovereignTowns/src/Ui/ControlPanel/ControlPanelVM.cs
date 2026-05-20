using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
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

    // ── 工作副本 ──
    private GlobalConfig _config;

    // ── 表头状态 ──
    private bool _isDirty;
    private bool _isSaving;
    private string _capitalName = "";
    private string _warning = "";
    private string _success = "";

    // ── 公开工作副本引用（后续 Tab VM 用）──
    public GlobalConfig Config => _config;

    /// <summary>屏幕轮询此标记决定是否 PopScreen。</summary>
    public bool IsClosing => _isClosing;

    // ── DataSourceProperty：脏状态 / 保存状态 ──

    [DataSourceProperty]
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(SaveLabel));
                OnPropertyChanged(nameof(DirtyLabel));
            }
        }
    }

    [DataSourceProperty]
    public bool IsSaving
    {
        get => _isSaving;
        set
        {
            if (_isSaving != value)
            {
                _isSaving = value;
                OnPropertyChanged(nameof(IsSaving));
                OnPropertyChanged(nameof(SaveLabel));
            }
        }
    }

    [DataSourceProperty]
    public string CapitalName
    {
        get => _capitalName;
        set
        {
            if (_capitalName != value)
            {
                _capitalName = value;
                OnPropertyChanged(nameof(CapitalName));
            }
        }
    }

    [DataSourceProperty]
    public string Warning
    {
        get => _warning;
        set
        {
            if (_warning != value)
            {
                _warning = value;
                OnPropertyChanged(nameof(Warning));
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    [DataSourceProperty]
    public string Success
    {
        get => _success;
        set
        {
            if (_success != value)
            {
                _success = value;
                OnPropertyChanged(nameof(Success));
                OnPropertyChanged(nameof(HasSuccess));
            }
        }
    }

    [DataSourceProperty] public bool HasWarning => !string.IsNullOrEmpty(_warning);
    [DataSourceProperty] public bool HasSuccess => !string.IsNullOrEmpty(_success);

    [DataSourceProperty]
    public string SaveLabel =>
        _isSaving ? ControlPanelLoc.Tr("保存中…", "Saving…")
        : _isDirty ? ControlPanelLoc.Tr("● 保存改动", "● Save changes")
        : ControlPanelLoc.Tr("已保存", "Saved");

    [DataSourceProperty]
    public string DirtyLabel =>
        _isDirty ? ControlPanelLoc.Tr("● 有未保存改动", "● Unsaved changes")
                 : ControlPanelLoc.Tr("✓ 已保存", "✓ Saved");

    // ── 静态标题字符串 ──
    [DataSourceProperty] public string TitleText => "SOVEREIGN TOWNS";
    [DataSourceProperty] public string SubtitleText => ControlPanelLoc.Tr("控制面板", "Control Panel");
    [DataSourceProperty] public string ReloadLabel => ControlPanelLoc.Tr("↻ 重读", "↻ Reload");

    /// <summary>构造时立即暂停游戏（面板从大地图弹出，地图态时间在走）。镜像 IG：ctor 末尾调用 PauseGame。</summary>
    public ControlPanelVM()
    {
        PauseGame();

        // ── 工作副本 ──
        try
        {
            _config = ControlPanelData.CloneCurrentConfig();
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelVM: CloneCurrentConfig failed", ex);
            _warning = ControlPanelLoc.Tr("配置加载失败，请查看日志。", "Failed to load config — see logs.");
        }

        // ── 首府名称 ──
        try
        {
            var settlement = CapitalRegistry.Instance?.GetForPlayer()?.GetCapitalSettlement();
            _capitalName = settlement != null
                ? ControlPanelLoc.Tr("首府: ", "Capital: ") + settlement.Name?.ToString()
                : ControlPanelLoc.Tr("首府: 无", "Capital: none");
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelVM: capital name lookup failed", ex);
            _capitalName = ControlPanelLoc.Tr("首府: 无", "Capital: none");
        }
    }

    // ── 公共辅助 ──

    /// <summary>标记工作副本已被修改（由子 VM 在任何字段变更后调用）。</summary>
    public void MarkDirty() { IsDirty = true; }

    // ── 命令 ──

    /// <summary>关闭按钮 / ESC 调用。</summary>
    public void ExecuteClose()
    {
        _isClosing = true;
    }

    /// <summary>把工作副本写回配置系统并落盘。</summary>
    public void ExecuteSave()
    {
        if (_config == null || _isSaving) return;
        IsSaving = true;
        Warning = "";
        bool ok = ControlPanelData.Save(_config, out string reason);
        IsSaving = false;
        if (ok)
        {
            IsDirty = false;
            Success = ControlPanelLoc.Tr("已保存到游戏。", "Saved to the game.");
        }
        else
        {
            Warning = ControlPanelLoc.Tr("保存失败：", "Save failed: ") + reason;
        }
    }

    /// <summary>
    /// 从磁盘重读配置。有未保存改动时先弹二次确认对话框。
    /// InquiryData 构造器签名来自
    /// _research/ImprovedGarrisons/ImprovedGarrisons.ConfigOptionsMenu/ConfigMenuVM.cs:708:
    /// new InquiryData(title, text, isAff, isNeg, affText, negText, affAction, negAction,
    ///     soundEventPath, expireTime, expireAction, isAffValidAction, isNegValidAction)
    /// ShowInquiry(data, pauseGameActiveState, prioritize) — 见 ConfigMenuVM.cs:715
    /// </summary>
    public void ExecuteReload()
    {
        if (_isDirty)
        {
            InformationManager.ShowInquiry(
                new InquiryData(
                    ControlPanelLoc.Tr("重读磁盘", "Reload from disk"),
                    ControlPanelLoc.Tr(
                        "当前有未保存的改动。重读会丢弃这些改动，确定继续？",
                        "You have unsaved changes. Reloading from disk will discard them. Continue?"),
                    true,
                    true,
                    ControlPanelLoc.Tr("确定", "OK"),
                    ControlPanelLoc.Tr("取消", "Cancel"),
                    (Action)DoReload,
                    (Action)null,
                    "",
                    0f,
                    (Action)null,
                    (Func<ValueTuple<bool, string>>)null,
                    (Func<ValueTuple<bool, string>>)null),
                false,
                false);
        }
        else
        {
            DoReload();
        }
    }

    private void DoReload()
    {
        _config = ControlPanelData.Reload(out string reason);
        IsDirty = false;
        Warning = "";
        Success = ControlPanelLoc.Tr("已从磁盘重读配置。", "Configuration reloaded from disk.");
        // 注意：后续任务会在此处重建各 TabVM（本任务暂无 tab）。
    }

    /// <summary>关闭警告横幅。</summary>
    public void ExecuteDismissWarning() { Warning = ""; }

    /// <summary>关闭成功横幅。</summary>
    public void ExecuteDismissSuccess() { Success = ""; }

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
