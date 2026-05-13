using System;
using TaleWorlds.Library;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.GuardsUtils;

public class GuardOrdersVM : ViewModel
{
	private Action _onPress;

	public string ButtonText { get; set; }

	public GuardOrdersVM(string buttonText, Action onPress)
	{
		ButtonText = buttonText;
		_onPress = onPress;
	}

	public void OnPress()
	{
		if (_onPress != null)
		{
			_onPress();
		}
	}
}
