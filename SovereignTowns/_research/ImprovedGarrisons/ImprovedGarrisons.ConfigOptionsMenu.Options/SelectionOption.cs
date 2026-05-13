using System.Collections.Generic;
using TaleWorlds.Engine.Options;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class SelectionOption : OptionUtils, ISelectionOptionData, IOptionData
{
	private int _selectableOptionsLimit;

	private IEnumerable<SelectionData> _selectionData;

	private new float _value;

	public SelectionOption(string name, string description, int selectableOptionsLimit, List<SelectionData> selectionData)
		: base(name, description)
	{
		_selectionData = selectionData;
		_selectableOptionsLimit = selectableOptionsLimit;
	}

	public bool IsAction()
	{
		return true;
	}

	public void Commit()
	{
	}

	public new float GetDefaultValue()
	{
		return 0f;
	}

	public object GetOptionType()
	{
		return ConfigMenuVM.OptionsDataType.MultipleSelectionOption;
	}

	public IEnumerable<SelectionData> GetSelectableOptionNames()
	{
		return _selectionData;
	}

	public int GetSelectableOptionsLimit()
	{
		return _selectableOptionsLimit;
	}

	public new float GetValue()
	{
		return _value;
	}

	public bool IsNative()
	{
		return false;
	}

	public new void SetValue(float value)
	{
		_value = value;
	}

	public (string, bool) GetIsDisabledAndReasonID()
	{
		return (string.Empty, false);
	}
}
