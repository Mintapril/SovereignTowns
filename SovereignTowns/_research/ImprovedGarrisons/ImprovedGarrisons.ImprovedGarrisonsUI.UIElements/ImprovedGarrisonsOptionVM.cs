using System;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;

public class ImprovedGarrisonsOptionVM : ViewModel
{
	private HintViewModel _hint;

	private bool _optionBooleanValue = false;

	private Action<bool> _onBooleanChangeAction;

	private float _optionFloatValue = 0f;

	private Action<float> _onFloatChangeAction;

	private float mousePositionX = 0f;

	private float mousePositionY = 0f;

	public int OptionTypeID { get; set; }

	public string Description { get; set; }

	public bool IsDiscrete { get; set; }

	public float Max { get; set; }

	public float Min { get; set; }

	public string ButtonName { get; set; }

	public Action OnPressAction { get; set; }

	public SelectorVM<SelectorItemVM> SelectorDatasource { get; set; }

	public bool OptionValueAsBoolean
	{
		get
		{
			return _optionBooleanValue;
		}
		set
		{
			if (value != _optionBooleanValue)
			{
				_optionBooleanValue = value;
				((ViewModel)this).OnPropertyChanged("OptionValueAsBoolean");
				_onBooleanChangeAction(value);
			}
		}
	}

	public float OptionValueAsFloat
	{
		get
		{
			return _optionFloatValue;
		}
		set
		{
			if (value != _optionFloatValue)
			{
				mousePositionX = Input.MouseMoveX;
				mousePositionY = Input.MouseMoveY;
				_optionFloatValue = value;
				((ViewModel)this).OnPropertyChanged("OptionValueAsFloat");
				((ViewModel)this).OnPropertyChanged("OptionFloatValueAsString");
				_onFloatChangeAction(value);
			}
		}
	}

	public string OptionFloatValueAsString => ((int)_optionFloatValue).ToString();

	public HintViewModel Hint
	{
		get
		{
			return _hint;
		}
		set
		{
			if (value != _hint)
			{
				_hint = value;
				((ViewModel)this).OnPropertyChangedWithValue<HintViewModel>(value, "Hint");
			}
		}
	}

	public ImprovedGarrisonsOptionVM SetAsBooleanOption(string desc, bool initialValue, Action<bool> onChange, TextObject hintText = null)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		try
		{
			OptionTypeID = 1;
			Description = desc;
			_optionBooleanValue = initialValue;
			_onBooleanChangeAction = onChange;
			if (hintText != (TextObject)null)
			{
				Hint = new HintViewModel(hintText, (string)null);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return this;
	}

	public ImprovedGarrisonsOptionVM SetAsSliderOption(string text, float initialValue, float min, float max, bool discrete, Action<float> onChange, TextObject hintText = null)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		try
		{
			Description = text;
			OptionTypeID = 2;
			_optionFloatValue = initialValue;
			_onFloatChangeAction = onChange;
			Max = max;
			Min = min;
			IsDiscrete = discrete;
			if (hintText != (TextObject)null)
			{
				Hint = new HintViewModel(hintText, (string)null);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return this;
	}

	public ImprovedGarrisonsOptionVM SetAsButtonOption(string buttonName, Action onPress, TextObject hintText = null)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		try
		{
			OptionTypeID = 3;
			ButtonName = buttonName;
			OnPressAction = onPress;
			if (hintText != (TextObject)null)
			{
				Hint = new HintViewModel(hintText, (string)null);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return this;
	}

	public ImprovedGarrisonsOptionVM SetAsDropdownOption(string selecorName, SelectorVM<SelectorItemVM> selector, TextObject hintText = null)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		try
		{
			OptionTypeID = 4;
			Description = selecorName;
			SelectorDatasource = selector;
			if (hintText != (TextObject)null)
			{
				Hint = new HintViewModel(hintText, (string)null);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return this;
	}

	public void OnPress()
	{
		if (OnPressAction != null)
		{
			OnPressAction();
		}
	}

	public ImprovedGarrisonsOptionVM SetAsTitle(string title, TextObject hintText = null)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		OptionTypeID = 0;
		Description = title;
		if (hintText != (TextObject)null)
		{
			Hint = new HintViewModel(hintText, (string)null);
		}
		return this;
	}
}
