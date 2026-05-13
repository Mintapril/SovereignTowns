using System;
using TaleWorlds.Library;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;

public class ImprovedGarrisonsInformationListVM : ViewModel
{
	private Func<string> _refreshAction;

	private string _value;

	private string _iconPath;

	public string Value
	{
		get
		{
			return _value;
		}
		set
		{
			if (value != _value)
			{
				_value = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Value");
			}
		}
	}

	public string Iconpath
	{
		get
		{
			return _iconPath;
		}
		set
		{
			if (value != _iconPath)
			{
				_iconPath = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Iconpath");
			}
		}
	}

	public string Text { get; set; }

	public ImprovedGarrisonsInformationListVM(string text, string initialValue, Func<string> refreshAction, string iconPath = null)
	{
		Text = text;
		Value = initialValue;
		_refreshAction = refreshAction;
		Iconpath = iconPath;
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		Value = _refreshAction();
	}
}
