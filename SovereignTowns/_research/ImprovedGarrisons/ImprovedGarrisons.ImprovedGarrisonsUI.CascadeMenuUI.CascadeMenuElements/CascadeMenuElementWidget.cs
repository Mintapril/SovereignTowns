using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;

public class CascadeMenuElementWidget : Widget
{
	public Widget ExtendButtonWidget { get; set; }

	public Widget BaseButtonWidget { get; set; }

	public Widget PartyListWidget { get; set; }

	public int OptionTypeID { get; set; }

	public CascadeMenuElementWidget(UIContext context)
		: base(context)
	{
	}

	protected override void OnLateUpdate(float dt)
	{
		((Widget)this).OnLateUpdate(dt);
		SetVisibility();
	}

	private void SetVisibility()
	{
		switch (OptionTypeID)
		{
		case 1:
			BaseButtonWidget.IsVisible = true;
			break;
		case 2:
			ExtendButtonWidget.IsVisible = true;
			break;
		case 3:
			PartyListWidget.IsVisible = true;
			break;
		}
	}
}
