using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>通用 chip：文化过滤 / tier 选择 / 兵种类型过滤 复用。绑定契约见 STCPChip.xml。</summary>
public sealed class ChipVM : ViewModel
{
    private readonly Action _onClick;
    private bool _isActive, _isDimmed;

    [DataSourceProperty] public string Label { get; }

    [DataSourceProperty]
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
    }

    [DataSourceProperty]
    public bool IsDimmed
    {
        get => _isDimmed;
        set { if (_isDimmed != value) { _isDimmed = value; OnPropertyChanged(nameof(IsDimmed)); } }
    }

    public ChipVM(string label, Action onClick) { Label = label; _onClick = onClick; }
    public void ExecuteClick() { if (!_isDimmed) _onClick?.Invoke(); }
}
