using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class ToggleRowVM : ViewModel
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    private readonly Action _onChanged;

    [DataSourceProperty] public string Label { get; }
    [DataSourceProperty] public string Hint { get; }
    [DataSourceProperty] public bool IsDanger { get; }

    [DataSourceProperty]
    public bool IsChecked
    {
        get => _get();
        set
        {
            if (_get() == value) return;
            _set(value);
            OnPropertyChanged(nameof(IsChecked));
            _onChanged?.Invoke();
        }
    }

    public ToggleRowVM(string label, string hint, bool isDanger,
                       Func<bool> get, Action<bool> set, Action onChanged)
    {
        Label = label; Hint = hint; IsDanger = isDanger;
        _get = get; _set = set; _onChanged = onChanged;
    }
}
