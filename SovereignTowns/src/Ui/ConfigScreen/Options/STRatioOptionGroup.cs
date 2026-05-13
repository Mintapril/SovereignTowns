using System;
using System.Collections.Generic;
using System.Linq;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// Coordinates a set of ratio sliders so they always sum to 1.0. When one slider
/// changes, its new value is kept and the remaining value is redistributed across
/// the other sliders proportionally to their previous values.
/// </summary>
public sealed class STRatioOptionGroup
{
    private readonly List<Entry> _entries = new();
    private readonly Action? _afterChange;
    private bool _updating;

    public STRatioOptionGroup(Action? afterChange = null)
    {
        _afterChange = afterChange;
    }

    public STNumericOptionVM Add(
        string name,
        string description,
        float current,
        Action<float> writeValue)
    {
        var entry = new Entry(writeValue, current);
        var option = new STNumericOptionVM(
            name,
            description,
            min: 0f,
            max: 1f,
            current: Clamp01(current),
            isDiscrete: false,
            onCommit: v => OnUserChanged(entry, v));
        entry.Option = option;
        _entries.Add(entry);
        return option;
    }

    public void NormalizeInitial()
    {
        if (_entries.Count == 0) return;

        float sum = _entries.Sum(e => Clamp01(e.Value));
        if (sum <= 0.0001f)
        {
            float equal = 1f / _entries.Count;
            foreach (var e in _entries)
                SetEntry(e, equal);
        }
        else
        {
            foreach (var e in _entries)
                SetEntry(e, Clamp01(e.Value) / sum);
        }
        _afterChange?.Invoke();
    }

    private void OnUserChanged(Entry changed, float rawValue)
    {
        if (_updating) return;

        _updating = true;
        try
        {
            float changedValue = Clamp01(rawValue);
            changed.Value = changedValue;

            float remaining = 1f - changedValue;
            var others = _entries.Where(e => !ReferenceEquals(e, changed)).ToArray();
            float otherSum = others.Sum(e => Clamp01(e.Value));

            SetEntry(changed, changedValue);
            if (others.Length > 0)
            {
                if (remaining <= 0f)
                {
                    foreach (var e in others)
                        SetEntry(e, 0f);
                }
                else if (otherSum <= 0.0001f)
                {
                    float equal = remaining / others.Length;
                    foreach (var e in others)
                        SetEntry(e, equal);
                }
                else
                {
                    foreach (var e in others)
                        SetEntry(e, remaining * Clamp01(e.Value) / otherSum);
                }
            }
        }
        finally
        {
            _updating = false;
        }

        _afterChange?.Invoke();
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    private static void SetEntry(Entry entry, float value)
    {
        value = Clamp01(value);
        entry.Value = value;
        entry.WriteValue(value);
        entry.Option?.SetValueSilently(value);
    }

    private sealed class Entry
    {
        public Entry(Action<float> writeValue, float value)
        {
            WriteValue = writeValue;
            Value = value;
        }

        public Action<float> WriteValue { get; }
        public float Value { get; set; }
        public STNumericOptionVM? Option { get; set; }
    }
}
