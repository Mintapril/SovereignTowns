using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI;

public class CascadeMenuWidget : BrushWidget
{
	private bool positionIsSet = false;

	public CascadeMenuWidget(UIContext context)
		: base(context)
	{
	}

	protected override void OnRender(TwoDimensionContext twoDimensionContext, TwoDimensionDrawContext drawContext)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		((BrushWidget)this).OnRender(twoDimensionContext, drawContext);
		bool flag = UIManager.Instance.cascadeMenuGauntlet != null && UIManager.Instance.cascadeMenuGauntlet.cascadeMenuIsOpen;
		if (!(!positionIsSet && flag))
		{
			return;
		}
		CascadeMenuGauntlet cascadeMenuGauntlet = UIManager.Instance.cascadeMenuGauntlet;
		if (!cascadeMenuGauntlet.cascadeLevelIsAboveTwo)
		{
			Vec2 mousePositionPixel = Input.MousePositionPixel;
			((Widget)this).ScaledPositionXOffset = mousePositionPixel.x - ((Widget)this).ScaledSuggestedWidth;
			((Widget)this).ScaledPositionYOffset = mousePositionPixel.y - ((Widget)this).Size.Y / 2f;
			positionIsSet = true;
		}
		else
		{
			CascadeMenu cascadeMenu = cascadeMenuGauntlet.TryGetPreviousCascadeMenu();
			if (cascadeMenu != null && cascadeMenu.cascadeMenuWidget != null)
			{
				Widget cascadeMenuWidget = cascadeMenu.cascadeMenuWidget;
				((Widget)this).ScaledPositionXOffset = cascadeMenuWidget.ScaledPositionXOffset - ((Widget)this).ScaledSuggestedWidth;
				((Widget)this).ScaledPositionYOffset = cascadeMenuWidget.ScaledPositionYOffset;
				positionIsSet = true;
			}
		}
		float num = ((Widget)this).ScaledPositionYOffset + ((Widget)this).Size.Y;
		Vec2 resolution = Input.Resolution;
		if (num >= ((Vec2)(ref resolution)).Y)
		{
			float num2 = ((Widget)this).ScaledPositionYOffset + ((Widget)this).Size.Y;
			resolution = Input.Resolution;
			float num3 = num2 - ((Vec2)(ref resolution)).Y;
			((Widget)this).ScaledPositionYOffset = ((Widget)this).ScaledPositionYOffset - num3 - 15f;
		}
		cascadeMenuGauntlet.GetLatestCascadeMenu().cascadeMenuWidget = (Widget)(object)this;
	}
}
