using TaleWorlds.Library;

namespace ImprovedGarrisons.Tutorial;

public class TutorialItemUIVM : ViewModel
{
	private string _description;

	private string _colorCode;

	private int _marginBottom;

	private int _marginTop;

	private int _fontSize;

	public string Description
	{
		get
		{
			return _description;
		}
		set
		{
			if (value != _description)
			{
				_description = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Description");
			}
		}
	}

	public string ColorCode
	{
		get
		{
			return _colorCode;
		}
		set
		{
			if (value != _colorCode)
			{
				_colorCode = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "ColorCode");
			}
		}
	}

	public int MarginBottom
	{
		get
		{
			return _marginBottom;
		}
		set
		{
			if (value != _marginBottom)
			{
				_marginBottom = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "MarginBottom");
			}
		}
	}

	public int MarginTop
	{
		get
		{
			return _marginTop;
		}
		set
		{
			if (value != _marginTop)
			{
				_marginTop = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "MarginTop");
			}
		}
	}

	public int FontSize
	{
		get
		{
			return _fontSize;
		}
		set
		{
			if (value != _fontSize)
			{
				_fontSize = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "FontSize");
			}
		}
	}

	public TutorialItemUIVM(string color, string description, int fontsize, int marginTop, int marginBottom)
	{
		ColorCode = color;
		Description = description;
		MarginBottom = marginBottom;
		MarginTop = marginTop;
		FontSize = fontsize;
	}
}
