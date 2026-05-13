using System;
using System.Collections.Generic;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

[Serializable]
public abstract class OptionUtils
{
	private string _name;

	private string _description;

	protected float _value;

	private string name;

	private string description;

	public OptionUtils(string name, string description, float currentValue, string extraDescription = null, string requirements = null)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		_name = name;
		_description = description;
		_value = currentValue;
		if (extraDescription != null && extraDescription.Length > 0)
		{
			_description = _description + "\n \n \n" + ((object)new TextObject("{=menu_description}Description:", (Dictionary<string, object>)null)).ToString() + "\n" + extraDescription;
		}
		if (requirements != null && requirements.Length > 0)
		{
			_description = _description + "\n \n \n" + ((object)new TextObject("{=menu_requirements}Requirements:", (Dictionary<string, object>)null)).ToString() + "\n" + requirements;
		}
	}

	protected OptionUtils(string name, string description)
	{
		this.name = name;
		this.description = description;
	}

	internal OptionUtils(string name)
	{
	}

	public string getName()
	{
		return _name;
	}

	public string getDescription()
	{
		return _description;
	}

	public float GetDefaultValue()
	{
		return 0f;
	}

	public float GetValue()
	{
		return _value;
	}

	public float GetValue(bool forceRefresh)
	{
		return _value;
	}

	public void SetValue(float value)
	{
		_value = value;
	}
}
