using TaleWorlds.Library;

namespace ImprovedGarrisons.Tutorial;

public class ImprovedGarrisonsHighlightBorderVM : ViewModel
{
	private int _positionYOffset = 0;

	private int _positionXOffset = 0;

	private int _height = 0;

	private int _width = 0;

	public int PositionYOffset
	{
		get
		{
			return _positionYOffset;
		}
		set
		{
			if (_positionYOffset != value)
			{
				_positionYOffset = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "PositionYOffset");
			}
		}
	}

	public int PositionXOffset
	{
		get
		{
			return _positionXOffset;
		}
		set
		{
			if (_positionXOffset != value)
			{
				_positionXOffset = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "PositionXOffset");
			}
		}
	}

	public int Height
	{
		get
		{
			return _height;
		}
		set
		{
			if (_height != value)
			{
				_height = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "Height");
			}
		}
	}

	public int Width
	{
		get
		{
			return _width;
		}
		set
		{
			if (_width != value)
			{
				_width = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "Width");
			}
		}
	}

	public ImprovedGarrisonsHighlightBorderVM(int positionx, int positiony, int height, int width)
	{
		PositionXOffset = positionx;
		PositionYOffset = positiony;
		Height = height;
		Width = width;
	}
}
