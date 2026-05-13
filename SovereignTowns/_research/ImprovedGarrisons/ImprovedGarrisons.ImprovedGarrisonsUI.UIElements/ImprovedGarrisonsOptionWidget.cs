using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;

public class ImprovedGarrisonsOptionWidget : Widget
{
	private bool _buttonClicked = false;

	public Widget BooleanWidget { get; set; }

	public Widget SliderWidget { get; set; }

	public Widget ButtonActionWidget { get; set; }

	public Widget TitleWidget { get; set; }

	public Widget DropdownWidget { get; set; }

	public int OptionTypeID { get; set; }

	public Action OnButtonpressAction { get; set; }

	public ImprovedGarrisonsOptionWidget(UIContext context)
		: base(context)
	{
	}

	protected override void OnLateUpdate(float dt)
	{
		_buttonClicked = false;
		((Widget)this).OnLateUpdate(dt);
		SetVisibility();
	}

	protected override bool OnPreviewMouseScroll()
	{
		return ((Widget)this).OnPreviewMouseScroll();
	}

	private void SetVisibility()
	{
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		switch (OptionTypeID)
		{
		case 0:
			TitleWidget.IsVisible = true;
			break;
		case 1:
			BooleanWidget.IsVisible = true;
			break;
		case 2:
			SliderWidget.IsVisible = true;
			break;
		case 3:
		{
			ButtonActionWidget.IsVisible = true;
			ButtonWidget val = (ButtonWidget)ButtonActionWidget.FindChild("Button");
			val.ClickEventHandlers.Add(OnButtonClick);
			break;
		}
		case 4:
			DropdownWidget.IsVisible = true;
			break;
		}
	}

	private void OnButtonClick(Widget widget)
	{
		if (OnButtonpressAction != null && !_buttonClicked)
		{
			OnButtonpressAction();
			_buttonClicked = true;
		}
	}
}
