using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.ElementLists;

public class TownManagementActions
{
	public MBBindingList<CascadeMenuElementVM> actions = new MBBindingList<CascadeMenuElementVM>();

	public string Title = ((object)new TextObject("{=ui_improvedgarrisonsui_activity_townmanagement}Town management", (Dictionary<string, object>)null)).ToString();

	public CascadeMenu Menu;

	private Settlement settlementForAction;

	public TownManagementActions(Settlement settlement)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		settlementForAction = settlement;
		InitializeProjectsActions();
		Menu = new CascadeMenu(((object)new TextObject("{=ui_improvedgarrisonsui_activity_townmanagement}Town management", (Dictionary<string, object>)null)).ToString(), actions);
	}

	private void InitializeProjectsActions()
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected O, but got Unknown
		List<Building> buildings = (List<Building>)(object)settlementForAction.Town.Buildings;
		foreach (Building item in buildings)
		{
		}
		MBBindingList<CascadeMenuElementVM> val = new MBBindingList<CascadeMenuElementVM>();
		((Collection<CascadeMenuElementVM>)(object)actions).Add((CascadeMenuElementVM)new CascadeMenuExtendButtonVM(((object)new TextObject("{=ui_improvedgarrisonsui_activity_projects}Projects", (Dictionary<string, object>)null)).ToString(), null));
	}
}
