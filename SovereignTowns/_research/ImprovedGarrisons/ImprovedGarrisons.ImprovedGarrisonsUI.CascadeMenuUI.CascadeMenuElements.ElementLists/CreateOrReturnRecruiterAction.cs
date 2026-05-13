using System;
using System.Collections.Generic;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.ElementLists;

public class CreateOrReturnRecruiterAction
{
	public Action Action;

	private Settlement settlementForAction;

	private GarrisonRecruiter recruiter;

	public string Title
	{
		get
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Expected O, but got Unknown
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Expected O, but got Unknown
			if (recruiter == null)
			{
				return ((object)new TextObject("{=ui_improvedgarrisonsui_activity_recruiter1}Create recruiter", (Dictionary<string, object>)null)).ToString();
			}
			return ((object)new TextObject("{=ui_improvedgarrisonsui_activity_recruiter2}Return recruiter", (Dictionary<string, object>)null)).ToString();
		}
	}

	public CreateOrReturnRecruiterAction(Settlement settlement)
	{
		settlementForAction = settlement;
		recruiter = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(settlementForAction);
		InitializeAction();
	}

	private void InitializeAction()
	{
		Action = delegate
		{
			UIManager.Instance.CloseCascadeMenu();
			if (recruiter == null)
			{
				RecruitmentSettings.Instance.PromptCreateRecruiter(settlementForAction.Town);
			}
			else
			{
				RecruitmentSettings.Instance.ReturnRecruiter(settlementForAction.Town);
			}
		};
	}
}
