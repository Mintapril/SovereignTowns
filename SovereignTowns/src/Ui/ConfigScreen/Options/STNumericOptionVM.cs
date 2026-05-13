using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// A numeric (slider) option row. The setter on <see cref="OptionValue"/> writes immediately to
/// <see cref="ConfigurationManager"/> via the supplied <c>onCommit</c> delegate — there is no
/// staged-then-saved pattern. Close still triggers a single
/// <see cref="Configuration.ConfigurationManager.Save"/> to flush to <c>global.json</c>.
/// </summary>
public sealed class STNumericOptionVM : STOptionDataVM
{
    private readonly Action<float> _onCommit;
    private float _min;
    private float _max;
    private float _optionValue;
    private bool _isDiscrete;
    private bool _suppressCommit;

    /// <summary>Slider minimum. Bound from XML as <c>@Min</c>.</summary>
    [DataSourceProperty]
    public float Min
    {
        get => _min;
        set
        {
            if (value != _min)
            {
                _min = value;
                OnPropertyChanged(nameof(Min));
            }
        }
    }

    /// <summary>Slider maximum. Bound from XML as <c>@Max</c>.</summary>
    [DataSourceProperty]
    public float Max
    {
        get => _max;
        set
        {
            if (value != _max)
            {
                _max = value;
                OnPropertyChanged(nameof(Max));
            }
        }
    }

    /// <summary>Current slider value. Setter calls <c>onCommit</c> so the underlying config
    /// updates instantly. Bound from XML as <c>@OptionValue</c>.</summary>
    [DataSourceProperty]
    public float OptionValue
    {
        get => _optionValue;
        set
        {
            if (value != _optionValue)
            {
                _optionValue = value;
                OnPropertyChanged(nameof(OptionValue));
                OnPropertyChanged(nameof(OptionValueAsString));
                if (_suppressCommit) return;
                try { _onCommit?.Invoke(_optionValue); }
                catch { /* swallow: never crash UI thread from a setter */ }
            }
        }
    }

    /// <summary>True when the option represents integers (slider snaps to int steps).
    /// Bound from XML as <c>@IsDiscrete</c>.</summary>
    [DataSourceProperty]
    public bool IsDiscrete
    {
        get => _isDiscrete;
        set
        {
            if (value != _isDiscrete)
            {
                _isDiscrete = value;
                OnPropertyChanged(nameof(IsDiscrete));
            }
        }
    }

    /// <summary>Formatted current value for the inline label next to the slider.
    /// Bound from XML as <c>@OptionValueAsString</c>.</summary>
    [DataSourceProperty]
    public string OptionValueAsString
    {
        get
        {
            if (_isDiscrete) return ((int)_optionValue).ToString();
            return _optionValue.ToString("F2");
        }
    }

    public STNumericOptionVM(
        string name,
        string description,
        float min,
        float max,
        float current,
        bool isDiscrete,
        Action<float> onCommit)
        : base(name, description)
    {
        _onCommit = onCommit;
        _min = min;
        _max = max;
        _isDiscrete = isDiscrete;
        _optionValue = current;
    }

    /// <summary>
    /// Updates the displayed slider value after another ratio in the same group adjusted it.
    /// The underlying config is already written by the group coordinator, so this avoids
    /// recursively firing the row's commit callback.
    /// </summary>
    public void SetValueSilently(float value)
    {
        _suppressCommit = true;
        try
        {
            OptionValue = value;
        }
        finally
        {
            _suppressCommit = false;
        }
    }
}
