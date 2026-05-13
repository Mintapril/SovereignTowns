using TaleWorlds.Engine.Options;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class ToggleOptionDataVM : ConfigOptionsMenuItemVM
{
	private readonly bool _initialValue;

	private bool _optionValue;

	private IBooleanOptionData _booleanOptionData;

	[DataSourceProperty]
	public bool OptionValueAsBoolean
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
				((ViewModel)this).OnPropertyChanged("OptionValueAsBoolean");
			}
		}
	}

	public ToggleOptionDataVM(IBooleanOptionData option, TextObject name, TextObject description, ConfigMenuVM.OptionsDataType typeID)
		: base((IOptionData)(object)option, name, description, typeID)
	{
		_booleanOptionData = option;
		_initialValue = ((IOptionData)_booleanOptionData).GetValue(false) == 1f;
		_optionValue = _initialValue;
	}

	public override void UpdateValue()
	{
		Option.SetValue((float)(OptionValueAsBoolean ? 1 : 0));
		Option.Commit();
	}

	public override void Cancel()
	{
	}

	public override bool IsChanged()
	{
		return _initialValue != OptionValueAsBoolean;
	}

	public override void SetValue(float value)
	{
		OptionValueAsBoolean = (int)value == 1;
	}
}
