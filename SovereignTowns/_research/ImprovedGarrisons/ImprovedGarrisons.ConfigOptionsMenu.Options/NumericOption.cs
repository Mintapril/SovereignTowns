using System;
using TaleWorlds.Engine.Options;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

[Serializable]
public class NumericOption : OptionUtils, INumericOptionData, IOptionData
{
	private readonly bool _noFloatingNumbers;

	private readonly float _minValue;

	private readonly float _maxValue;

	private Action<float> _onCommit;

	public NumericOption(string name, string description, string extraDescription, string requirements, bool isDiscrete, float minValue, float maxValue, float startValue, Action<float> onCommit)
		: base(name, description, startValue, extraDescription, requirements)
	{
		_maxValue = maxValue;
		_minValue = minValue;
		_onCommit = onCommit;
		_noFloatingNumbers = isDiscrete;
	}

	public bool IsAction()
	{
		return true;
	}

	public void Commit()
	{
		_onCommit(_value);
	}

	public bool GetIsDiscrete()
	{
		return _noFloatingNumbers;
	}

	public float GetMaxValue()
	{
		return _maxValue;
	}

	public float GetMinValue()
	{
		return _minValue;
	}

	public object GetOptionType()
	{
		return ConfigMenuVM.OptionsDataType.NumericOption;
	}

	public bool IsNative()
	{
		return false;
	}

	public bool GetShouldUpdateContinuously()
	{
		return false;
	}

	public (string, bool) GetIsDisabledAndReasonID()
	{
		return (string.Empty, false);
	}

	public int GetDiscreteIncrementInterval()
	{
		return 1;
	}
}
