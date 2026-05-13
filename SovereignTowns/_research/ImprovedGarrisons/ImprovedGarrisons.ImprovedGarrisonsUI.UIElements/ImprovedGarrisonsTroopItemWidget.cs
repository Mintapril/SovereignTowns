using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;

public class ImprovedGarrisonsTroopItemWidget : ButtonWidget
{
	private bool _isTroopHero;

	private bool _isInitialized = false;

	private int _currentAmount;

	public ButtonWidget AddButtonWidget { get; set; }

	public ButtonWidget RemoveButtonWidget { get; set; }

	public bool IsTroopHero
	{
		get
		{
			return _isTroopHero;
		}
		set
		{
			if (_isTroopHero != value)
			{
				_isTroopHero = value;
				((PropertyOwnerObject)this).OnPropertyChanged(value, "IsTroopHero");
			}
		}
	}

	public int CurrentAmount
	{
		get
		{
			return _currentAmount;
		}
		set
		{
			if (_currentAmount != value)
			{
				_currentAmount = value;
				((PropertyOwnerObject)this).OnPropertyChanged(value, "CurrentAmount");
			}
		}
	}

	private void OnRemove(Widget obj)
	{
		((Widget)this).EventFired("Remove", Array.Empty<object>());
	}

	private void OnAdd(Widget obj)
	{
		((Widget)this).EventFired("Add", Array.Empty<object>());
	}

	public ImprovedGarrisonsTroopItemWidget(UIContext context)
		: base(context)
	{
	}

	protected override void OnLateUpdate(float dt)
	{
		if (AddButtonWidget != null && RemoveButtonWidget != null && !_isInitialized)
		{
			AddButtonWidget.ClickEventHandlers.Add(OnAdd);
			RemoveButtonWidget.ClickEventHandlers.Add(OnRemove);
			_isInitialized = true;
		}
	}
}
