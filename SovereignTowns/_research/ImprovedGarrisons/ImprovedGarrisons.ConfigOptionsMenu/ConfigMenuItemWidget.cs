using System.Collections.Generic;
using System.Linq;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;

namespace ImprovedGarrisons.ConfigOptionsMenu;

public class ConfigMenuItemWidget : Widget
{
	private ConfigMenuWidget _screenWidget;

	private bool _eventsRegistered;

	private bool _initialized;

	private Widget _dropdownExtensionParentWidget;

	private AnimatedDropdownWidget _dropdownWidget;

	private ButtonWidget _booleanToggleButtonWidget;

	private List<Sprite> _graphicsSprites;

	private int _optionTypeID;

	private string _optionDescription;

	private string _optionTitle;

	private string[] _imageIDs;

	public Widget BooleanOption { get; set; }

	public Widget NumericOption { get; set; }

	public Widget StringOption { get; set; }

	public Widget GameKeyOption { get; set; }

	public Widget ActionOption { get; set; }

	public Widget InputOption { get; set; }

	public Widget TitleWidget { get; set; }

	public int OptionTypeID
	{
		get
		{
			return _optionTypeID;
		}
		set
		{
			if (_optionTypeID != value)
			{
				_optionTypeID = value;
				((PropertyOwnerObject)this).OnPropertyChanged(_optionTypeID, "OptionTypeID");
			}
		}
	}

	public string OptionTitle
	{
		get
		{
			return _optionTitle;
		}
		set
		{
			if (_optionTitle != value)
			{
				_optionTitle = value;
			}
		}
	}

	public string[] ImageIDs
	{
		get
		{
			return _imageIDs;
		}
		set
		{
			if (_imageIDs != value)
			{
				_imageIDs = value;
			}
		}
	}

	public string OptionDescription
	{
		get
		{
			return _optionDescription;
		}
		set
		{
			if (_optionDescription != value)
			{
				_optionDescription = value;
			}
		}
	}

	public ConfigMenuItemWidget(UIContext context)
		: base(context)
	{
		_optionTypeID = -1;
		_graphicsSprites = new List<Sprite>();
	}

	protected override void OnLateUpdate(float dt)
	{
		((Widget)this).OnLateUpdate(dt);
		if (!_initialized)
		{
			if (ImageIDs != null)
			{
				int i;
				for (i = 0; i < ImageIDs.Length; i++)
				{
					if (ImageIDs[i] != string.Empty)
					{
						Sprite item = ((Widget)this).Context.SpriteData.Sprites.Values.SingleOrDefault((Sprite s) => s.Name == ImageIDs[i]);
						_graphicsSprites.Add(item);
					}
				}
			}
			RefreshVisibilityOfSubItems();
			_initialized = true;
		}
		if (!_eventsRegistered)
		{
			RegisterHoverEvents();
			_eventsRegistered = true;
		}
	}

	protected override void OnHoverBegin()
	{
		((Widget)this).OnHoverBegin();
		SetCurrentOption(fromHoverOverDropdown: false, fromBooleanSelection: false);
	}

	protected override void OnHoverEnd()
	{
		((Widget)this).OnHoverEnd();
		ResetCurrentOption();
	}

	private void SetCurrentOption(bool fromHoverOverDropdown, bool fromBooleanSelection, int hoverDropdownItemIndex = -1)
	{
		if (_optionTypeID == 3)
		{
			if (fromHoverOverDropdown)
			{
				if (_graphicsSprites.Count > hoverDropdownItemIndex)
				{
					Sprite val = _graphicsSprites[hoverDropdownItemIndex];
				}
			}
			else if (_graphicsSprites.Count > _dropdownWidget.CurrentSelectedIndex && _dropdownWidget.CurrentSelectedIndex >= 0)
			{
				Sprite val2 = _graphicsSprites[_dropdownWidget.CurrentSelectedIndex];
			}
			_screenWidget?.SetCurrentOption((Widget)(object)this, null);
		}
		else if (_optionTypeID == 0)
		{
			int num = ((!_booleanToggleButtonWidget.IsSelected) ? 1 : 0);
			if (_graphicsSprites.Count > num)
			{
				Sprite val3 = _graphicsSprites[num];
			}
			_screenWidget?.SetCurrentOption((Widget)(object)this, null);
		}
		else
		{
			_screenWidget?.SetCurrentOption((Widget)(object)this, null);
		}
	}

	public void SetCurrentScreenWidget(ConfigMenuWidget screenWidget)
	{
		_screenWidget = screenWidget;
	}

	private void ResetCurrentOption()
	{
		_screenWidget?.SetCurrentOption(null, null);
	}

	private void RegisterHoverEvents()
	{
		foreach (Widget child3 in ((Widget)this).Children)
		{
			((PropertyOwnerObject)child3).PropertyChanged += Child_PropertyChanged;
		}
		if (OptionTypeID == 0)
		{
			ref ButtonWidget booleanToggleButtonWidget = ref _booleanToggleButtonWidget;
			Widget child = BooleanOption.GetChild(0);
			booleanToggleButtonWidget = (ButtonWidget)(object)((child is ButtonWidget) ? child : null);
			((PropertyOwnerObject)_booleanToggleButtonWidget).PropertyChanged += BooleanOption_PropertyChanged;
		}
		else
		{
			if (OptionTypeID != 3)
			{
				return;
			}
			ref AnimatedDropdownWidget dropdownWidget = ref _dropdownWidget;
			Widget child2 = StringOption.GetChild(1);
			dropdownWidget = (AnimatedDropdownWidget)(object)((child2 is AnimatedDropdownWidget) ? child2 : null);
			_dropdownExtensionParentWidget = _dropdownWidget.DropdownClipWidget;
			foreach (Widget child4 in _dropdownExtensionParentWidget.Children)
			{
				((PropertyOwnerObject)child4).PropertyChanged += DropdownItem_PropertyChanged1;
			}
		}
	}

	private void BooleanOption_PropertyChanged(PropertyOwnerObject childWidget, string propertyName, object propertyValue)
	{
		if (propertyName == "IsSelected")
		{
			SetCurrentOption(fromHoverOverDropdown: false, fromBooleanSelection: true);
		}
	}

	private void Child_PropertyChanged(PropertyOwnerObject childWidget, string propertyName, object propertyValue)
	{
		if (propertyName == "IsHovered")
		{
			if ((bool)propertyValue)
			{
				SetCurrentOption(fromHoverOverDropdown: false, fromBooleanSelection: false);
			}
			else
			{
				ResetCurrentOption();
			}
		}
	}

	private void DropdownItem_PropertyChanged1(PropertyOwnerObject childWidget, string propertyName, object propertyValue)
	{
		if (propertyName == "IsHovered")
		{
			if ((bool)propertyValue)
			{
				Widget val = (Widget)(object)((childWidget is Widget) ? childWidget : null);
				SetCurrentOption(fromHoverOverDropdown: true, fromBooleanSelection: false, val.ParentWidget.GetChildIndex(val));
			}
			else
			{
				ResetCurrentOption();
			}
		}
	}

	private void RefreshVisibilityOfSubItems()
	{
		BooleanOption.IsVisible = OptionTypeID == 0;
		NumericOption.IsVisible = OptionTypeID == 1;
		StringOption.IsVisible = OptionTypeID == 3;
		GameKeyOption.IsVisible = OptionTypeID == 2;
		InputOption.IsVisible = OptionTypeID == 4;
		if (ActionOption != null)
		{
			ActionOption.IsVisible = OptionTypeID == 5;
		}
		TitleWidget.IsVisible = OptionTypeID == 6;
	}
}
