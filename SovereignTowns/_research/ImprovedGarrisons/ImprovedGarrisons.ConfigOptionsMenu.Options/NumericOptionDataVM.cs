using TaleWorlds.Engine.Options;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class NumericOptionDataVM : ConfigOptionsMenuItemVM
{
	private readonly float _initialValue;

	private INumericOptionData _numericOptionData;

	private float _min;

	private float _max;

	private float _optionValue;

	private bool _isDiscrete;

	[DataSourceProperty]
	public float Min
	{
		get
		{
			return _min;
		}
		set
		{
			if (value != _min)
			{
				_min = value;
				((ViewModel)this).OnPropertyChanged("Min");
			}
		}
	}

	[DataSourceProperty]
	public float Max
	{
		get
		{
			return _max;
		}
		set
		{
			if (value != _max)
			{
				_max = value;
				((ViewModel)this).OnPropertyChanged("Max");
			}
		}
	}

	[DataSourceProperty]
	public float OptionValue
	{
		get
		{
			return _optionValue;
		}
		set
		{
			if (value != _optionValue)
			{
				_optionValue = value;
				((ViewModel)this).OnPropertyChanged("OptionValue");
				((ViewModel)this).OnPropertyChanged("OptionValueAsString");
				UpdateValue();
			}
		}
	}

	[DataSourceProperty]
	public bool IsDiscrete
	{
		get
		{
			return _isDiscrete;
		}
		set
		{
			if (value != _isDiscrete)
			{
				_isDiscrete = value;
				((ViewModel)this).OnPropertyChanged("IsDiscrete");
			}
		}
	}

	[DataSourceProperty]
	public string OptionValueAsString
	{
		get
		{
			if (!IsDiscrete)
			{
				return _optionValue.ToString("F");
			}
			return ((int)_optionValue).ToString();
		}
	}

	public NumericOptionDataVM(INumericOptionData option, TextObject name, TextObject description, ConfigMenuVM.OptionsDataType typeID)
		: base((IOptionData)(object)option, name, description, typeID)
	{
		_numericOptionData = option;
		_initialValue = ((IOptionData)_numericOptionData).GetValue(false);
		Min = _numericOptionData.GetMinValue();
		Max = _numericOptionData.GetMaxValue();
		OptionValue = _initialValue;
		IsDiscrete = _numericOptionData.GetIsDiscrete();
	}

	public override void Cancel()
	{
		OptionValue = _initialValue;
		UpdateValue();
	}

	public override bool IsChanged()
	{
		return _initialValue != OptionValue;
	}

	public override void SetValue(float value)
	{
		OptionValue = value;
	}

	public override void UpdateValue()
	{
		Option.SetValue(OptionValue);
		Option.Commit();
	}
}
