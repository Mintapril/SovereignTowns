using TaleWorlds.Library;

namespace ImprovedGarrisons.Ribbons;

public class RibbonVM : ViewModel
{
	public string Title { get; set; }

	public string Text { get; set; }

	public bool IsVisible => Title != null && Text != null;
}
