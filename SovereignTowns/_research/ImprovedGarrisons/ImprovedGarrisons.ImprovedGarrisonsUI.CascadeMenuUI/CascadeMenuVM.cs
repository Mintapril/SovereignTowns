using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;
using TaleWorlds.Library;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI;

public class CascadeMenuVM : ViewModel
{
	public string TitleText { get; set; }

	public int SuggestedWidth { get; set; } = 240;


	public float MaxHeight { get; set; } = 500f;


	public MBBindingList<CascadeMenuElementVM> CascadeMenuElements { get; set; }

	public CascadeMenuVM(string title, MBBindingList<CascadeMenuElementVM> elements, int suggestedWidth = -1, float maxHeight = -1f)
	{
		TitleText = title;
		CascadeMenuElements = elements;
		if (suggestedWidth > 0)
		{
			SuggestedWidth = suggestedWidth;
		}
		if (maxHeight > 0f)
		{
			MaxHeight = maxHeight;
		}
	}

	public void CloseMenu()
	{
		if (UIManager.Instance.cascadeMenuGauntlet != null)
		{
			UIManager.Instance.cascadeMenuGauntlet.CloseCascadeMenu();
		}
	}
}
