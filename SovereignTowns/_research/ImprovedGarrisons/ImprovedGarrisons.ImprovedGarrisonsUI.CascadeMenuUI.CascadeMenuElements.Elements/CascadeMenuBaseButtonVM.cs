using System;
using TaleWorlds.Library;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;

public class CascadeMenuBaseButtonVM : CascadeMenuElementVM
{
	private MBBindingList<CascadeMenuElementVM> cascadeMenuElements;

	private Action onPressAction;

	private string text;

	public new int OptionTypeID { get; set; } = 1;


	public string Text
	{
		get
		{
			return text;
		}
		set
		{
			text = value;
		}
	}

	public CascadeMenuBaseButtonVM(string text, Action onPress)
	{
		Text = text;
		onPressAction = onPress;
	}

	public void ExecuteClick()
	{
		if (onPressAction != null)
		{
			onPressAction();
		}
	}
}
