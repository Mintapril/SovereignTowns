using System.Linq;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// 兵种选择弹窗中按文化分组的容器：组头（文化名 + 该组兵种总数）+ 该组所有兵种行。
/// 当组内全部 <see cref="STTroopRowVM"/> 都被 filter 隐藏时，组整体也隐藏，避免出现"空 header"。
/// </summary>
public sealed class STCultureGroupVM : ViewModel
{
    private readonly string _cultureId;
    private string _headerText;
    private bool _isGroupVisible = true;
    private MBBindingList<STTroopRowVM> _rows;

    public string CultureId => _cultureId;

    [DataSourceProperty]
    public string HeaderText
    {
        get => _headerText;
        set { if (value != _headerText) { _headerText = value ?? ""; OnPropertyChanged(nameof(HeaderText)); } }
    }

    [DataSourceProperty]
    public bool IsGroupVisible
    {
        get => _isGroupVisible;
        set { if (value != _isGroupVisible) { _isGroupVisible = value; OnPropertyChanged(nameof(IsGroupVisible)); } }
    }

    [DataSourceProperty]
    public MBBindingList<STTroopRowVM> Rows
    {
        get => _rows;
        set { if (value != _rows) { _rows = value; OnPropertyChanged(nameof(Rows)); } }
    }

    public STCultureGroupVM(string cultureId, string displayName)
    {
        _cultureId = cultureId ?? "";
        _rows = new MBBindingList<STTroopRowVM>();
        _headerText = $"━━ {displayName} ━━";
    }

    /// <summary>父 VM 每次 ApplyFilters 后调用本方法，按当前各行 IsVisible 重算自身可见性。</summary>
    public void RecomputeGroupVisibility()
    {
        IsGroupVisible = _rows.Any(r => r.IsVisible);
    }
}
