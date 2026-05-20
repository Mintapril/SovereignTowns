using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 一行数值参数：滑条 + 数值文本 + 「↺ 恢复默认」链接。
/// 通过 Func/Action 委托读写控制面板的工作配置副本。镜像 WebUI 的 type!=='bool' 行。
/// </summary>
public sealed class SliderRowVM : ViewModel
{
    private readonly SpecEntry _spec;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly Action _markDirty;

    [DataSourceProperty] public string Label { get; }
    [DataSourceProperty] public string Hint { get; }
    [DataSourceProperty] public bool IsAdvanced { get; }
    [DataSourceProperty] public bool IsDiscrete { get; }
    [DataSourceProperty] public float Min { get; }
    [DataSourceProperty] public float Max { get; }

    [DataSourceProperty]
    public float Value
    {
        get => (float)_get();
        set
        {
            double v = Clamp(value);
            if (Math.Abs(_get() - v) < 1e-9) return;
            _set(v);
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(ShowReset));
            _markDirty?.Invoke();
        }
    }

    [DataSourceProperty]
    public string ValueText
    {
        get => _spec.Discrete ? ((int)Math.Round(_get())).ToString() : _get().ToString("0.00");
        set { if (double.TryParse(value, out double v)) Value = (float)v; }
    }

    [DataSourceProperty] public bool ShowReset =>
        _spec.Def.HasValue && Math.Abs(_get() - _spec.Def.Value) > 1e-6;

    [DataSourceProperty] public string ResetLabel =>
        ControlPanelLoc.Tr("↺ 恢复默认 ", "↺ Reset ") +
        (_spec.Def.HasValue ? (_spec.Discrete ? ((int)_spec.Def.Value).ToString()
                                              : _spec.Def.Value.ToString("0.##")) : "");

    public SliderRowVM(SpecEntry spec, Func<double> get, Action<double> set, Action markDirty)
    {
        _spec = spec; _get = get; _set = set; _markDirty = markDirty;
        Label = ControlPanelLoc.Tr(spec.LabelZh, spec.LabelEn);
        Hint = ControlPanelLoc.Tr(spec.HintZh, spec.HintEn);
        IsAdvanced = spec.Advanced;
        IsDiscrete = spec.Discrete;
        Min = (float)spec.Min; Max = (float)spec.Max;
    }

    public void ExecuteReset() { if (_spec.Def.HasValue) Value = (float)_spec.Def.Value; }

    /// <summary>外部改了底层值后强制刷新绑定（比例联动场景用）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(ShowReset));
    }

    private double Clamp(double v)
    {
        if (v < _spec.Min) v = _spec.Min;
        if (v > _spec.Max) v = _spec.Max;
        if (_spec.Discrete) v = Math.Round(v);
        return v;
    }
}
