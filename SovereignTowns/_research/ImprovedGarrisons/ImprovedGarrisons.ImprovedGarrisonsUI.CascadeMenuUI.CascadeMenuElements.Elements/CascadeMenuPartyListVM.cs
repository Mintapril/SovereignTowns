using System.Collections.Generic;
using System.Collections.ObjectModel;
using ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;

public class CascadeMenuPartyListVM : CascadeMenuElementVM
{
	private bool _isEmpty;

	public new int OptionTypeID { get; set; } = 3;


	public bool IsEmpty
	{
		get
		{
			return _isEmpty;
		}
		set
		{
			_isEmpty = value;
			IsNotEmpty = !value;
		}
	}

	public bool IsNotEmpty { get; private set; }

	public string EmptyText { get; set; } = ((object)new TextObject("{=ui_improvedgarrisonsui_activity_noguards}There are no active guard parties", (Dictionary<string, object>)null)).ToString();


	public MBBindingList<ImprovedGarrisonsPartyInformationVM> Parties { get; set; }

	public CascadeMenuPartyListVM(MBBindingList<ImprovedGarrisonsPartyInformationVM> partyList)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected O, but got Unknown
		Parties = partyList;
		if (Parties != null && ((Collection<ImprovedGarrisonsPartyInformationVM>)(object)Parties).Count > 0)
		{
			IsEmpty = false;
		}
		else
		{
			IsEmpty = true;
		}
	}
}
