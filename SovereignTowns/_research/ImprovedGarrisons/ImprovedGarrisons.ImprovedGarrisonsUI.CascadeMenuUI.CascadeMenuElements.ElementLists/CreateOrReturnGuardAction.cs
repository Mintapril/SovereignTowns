using System;
using System.Collections.Generic;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.ElementLists;

public class CreateOrReturnGuardAction
{
	public Action Action;

	private Settlement settlementForAction;

	private MobileGarrison mobileGarrison;

	public string Title
	{
		get
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Expected O, but got Unknown
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Expected O, but got Unknown
			if (mobileGarrison == null)
			{
				return ((object)new TextObject("{=ui_improvedgarrisonsui_activity_guard1}Create guard party", (Dictionary<string, object>)null)).ToString();
			}
			return ((object)new TextObject("{=ui_improvedgarrisonsui_activity_guard2}Return guard party", (Dictionary<string, object>)null)).ToString();
		}
	}

	public CreateOrReturnGuardAction(Settlement settlement)
	{
		settlementForAction = settlement;
		mobileGarrison = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(settlementForAction);
		InitializeAction();
	}

	private void InitializeAction()
	{
		Action = delegate
		{
			UIManager.Instance.CloseCascadeMenu();
			if (mobileGarrison == null)
			{
				MobileGarrisonSettings.Instance.PromptCreateMobileGarrison(settlementForAction.Town);
			}
			else
			{
				MobileGarrisonSettings.Instance.OrderMobileGarrisonReturn(settlementForAction.Town);
			}
		};
	}
}
