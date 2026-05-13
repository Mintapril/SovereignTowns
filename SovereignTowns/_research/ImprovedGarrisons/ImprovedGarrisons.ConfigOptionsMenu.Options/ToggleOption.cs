using System;
using TaleWorlds.Engine.Options;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class ToggleOption : OptionUtils, IBooleanOptionData, IOptionData
{
	private Action<bool> _onCommit;

	public ToggleOption(string name, string description, string extraDescription, string requirements, float currentValue, Action<bool> onCommit)
		: base(name, description, currentValue, extraDescription, requirements)
	{
		_onCommit = onCommit;
	}

	public bool IsAction()
	{
		return true;
	}

	public void Commit()
	{
		_onCommit(_value == 1f);
	}

	public object GetOptionType()
	{
		return ConfigMenuVM.OptionsDataType.BooleanOption;
	}

	public bool IsNative()
	{
		return false;
	}

	public (string, bool) GetIsDisabledAndReasonID()
	{
		return (string.Empty, false);
	}
}
