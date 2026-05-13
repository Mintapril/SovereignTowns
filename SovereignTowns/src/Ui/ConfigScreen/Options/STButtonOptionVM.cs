using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>Simple command row used for actions that open another native Bannerlord screen.</summary>
public sealed class STButtonOptionVM : STOptionDataVM
{
    private readonly Action _onExecute;
    private string _buttonText;
    private bool _isVisible;

    [DataSourceProperty]
    public string ButtonText
    {
        get => _buttonText;
        set
        {
            if (value != _buttonText)
            {
                _buttonText = value ?? "";
                OnPropertyChanged(nameof(ButtonText));
            }
        }
    }

    [DataSourceProperty]
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (value != _isVisible)
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    public STButtonOptionVM(
        string name,
        string description,
        string buttonText,
        bool isVisible,
        Action onExecute)
        : base(name, description)
    {
        _buttonText = buttonText ?? "";
        _isVisible = isVisible;
        _onExecute = onExecute ?? (() => { });
    }

    public void ExecuteAction()
    {
        try { _onExecute(); }
        catch { /* never crash the UI thread from a button row */ }
    }
}
