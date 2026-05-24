using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 一个策略参数分组。同时充当「分组过滤芯片」的 VM —— 因此必须满足芯片绑定契约：
/// <c>Label</c> / <c>IsActive</c> / <c>IsDimmed</c> / 命令 <c>ExecuteClick</c>。
/// 持有本组拆分后的滑条行与开关行（<see cref="Sliders"/> / <see cref="Toggles"/>），
/// 由 <see cref="StrategyTabVM"/> 在切换激活分组时拷贝到其扁平列表。
/// </summary>
public sealed class SettingsGroupVM : ViewModel
{
    private readonly SpecGroup _group;
    private readonly GlobalConfig _config;
    private readonly Action _markDirty;
    private readonly Action<SettingsGroupVM> _onSelect;
    // 本组当前可见的 bool spec（过滤后）—— 用于「本组恢复默认」时还原开关。
    private readonly List<SpecEntry> _visibleBoolSpecs = new List<SpecEntry>();
    private bool _isActive;

    public string Key => _group.Key;

    // ── 芯片绑定契约 ──
    /// <summary>芯片标签 = 分组短标签。分组头也复用同一个标签。</summary>
    [DataSourceProperty] public string Label { get; }

    [DataSourceProperty]
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
    }

    /// <summary>SettingsGroupVM 充当芯片时永远不灰显（仅 Task 12 的 ChipVM 才需要 dim 逻辑）。</summary>
    [DataSourceProperty] public bool IsDimmed => false;

    // ── 分组自身标签 / 提示（与芯片 Label 相同的分组标签） ──
    [DataSourceProperty] public string GroupLabel { get; }
    [DataSourceProperty] public string GroupHint { get; }

    // ── 本组行（已按 showAdvanced 过滤） ──
    [DataSourceProperty] public MBBindingList<SliderRowVM> Sliders { get; } = new MBBindingList<SliderRowVM>();
    [DataSourceProperty] public MBBindingList<ToggleRowVM> Toggles { get; } = new MBBindingList<ToggleRowVM>();

    /// <param name="group">分组元数据。</param>
    /// <param name="config">控制面板工作配置副本。</param>
    /// <param name="markDirty">任意字段变更后回调。</param>
    /// <param name="showAdvanced">false 时过滤掉 <c>Advanced==true</c> 的 spec。</param>
    /// <param name="onSelect">芯片点击时回调（由 StrategyTabVM 提供）。</param>
    public SettingsGroupVM(SpecGroup group, GlobalConfig config, Action markDirty,
                           bool showAdvanced, Action<SettingsGroupVM> onSelect)
    {
        _group = group;
        _config = config;
        _markDirty = markDirty;
        _onSelect = onSelect;

        Label = ControlPanelLoc.Tr(group.LabelZh, group.LabelEn);
        GroupLabel = Label;
        GroupHint = ControlPanelLoc.Tr(group.HintZh, group.HintEn);

        // 2026-05-24:手动驻军目标整体下线,删除 RequiresManualMode 过滤。
        foreach (var spec in group.Specs)
        {
            if (!showAdvanced && spec.Advanced) continue;

            var s = spec; // 闭包捕获副本
            if (s.IsBool)
            {
                _visibleBoolSpecs.Add(s);
                Toggles.Add(new ToggleRowVM(
                    ControlPanelLoc.Tr(s.LabelZh, s.LabelEn),
                    ControlPanelLoc.Tr(s.HintZh, s.HintEn),
                    false,
                    () => StrategyTabVM.GetD(config, s) != 0.0,
                    b => StrategyTabVM.SetD(config, s, b ? 1.0 : 0.0),
                    markDirty));
            }
            else
            {
                Sliders.Add(new SliderRowVM(
                    s,
                    () => StrategyTabVM.GetD(config, s),
                    v => StrategyTabVM.SetD(config, s, v),
                    markDirty));
            }
        }
    }

    /// <summary>本组当前可见 spec 行数（过滤后）。StrategyTabVM 用它丢弃空组。</summary>
    public int RowCount => Sliders.Count + Toggles.Count;

    /// <summary>芯片点击命令。</summary>
    public void ExecuteClick() => _onSelect?.Invoke(this);

    /// <summary>把本组每个可见 spec 恢复到出厂默认值（未保存）。</summary>
    public void ExecuteResetGroup()
    {
        // 滑条：SliderRowVM 自带 reset（按 SpecEntry.Def）。
        foreach (var slider in Sliders) slider.ExecuteReset();

        // 开关：ToggleRowVM 不携带 SpecEntry，按本组的 bool spec 直接写默认值。
        for (int i = 0; i < _visibleBoolSpecs.Count && i < Toggles.Count; i++)
        {
            var s = _visibleBoolSpecs[i];
            if (!s.Def.HasValue) continue;
            // 通过 ToggleRowVM.IsChecked 的 setter 走 markDirty + 属性通知。
            Toggles[i].IsChecked = s.Def.Value != 0.0;
        }
    }
}
