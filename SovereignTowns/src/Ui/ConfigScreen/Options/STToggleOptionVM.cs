using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// A boolean (checkbox / toggle) option row. The setter on
/// <see cref="OptionValueAsBoolean"/> writes immediately to
/// <see cref="ConfigurationManager"/> via the supplied <c>onCommit</c> delegate.
/// </summary>
public sealed class STToggleOptionVM : STOptionDataVM
{
    private readonly Action<bool> _onCommit;
    private bool _optionValue;

    /// <summary>Current toggle state. Bound from XML as <c>@OptionValueAsBoolean</c>
    /// (matches the property name <see cref="ButtonWidget.IsSelected"/> typically binds to).</summary>
    [DataSourceProperty]
    public bool OptionValueAsBoolean
    {
        get => _optionValue;
        set
        {
            if (value != _optionValue)
            {
                _optionValue = value;
                OnPropertyChanged(nameof(OptionValueAsBoolean));
                try { _onCommit?.Invoke(_optionValue); }
                catch { /* swallow: never crash UI thread from a setter */ }
            }
        }
    }

    public STToggleOptionVM(string name, string description, bool current, Action<bool> onCommit)
        : base(name, description)
    {
        _onCommit = onCommit;
        _optionValue = current;
    }
}
