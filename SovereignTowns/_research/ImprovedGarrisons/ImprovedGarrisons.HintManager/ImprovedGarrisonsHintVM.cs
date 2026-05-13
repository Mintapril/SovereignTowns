using TaleWorlds.Library;

namespace ImprovedGarrisons.HintManager;

public class ImprovedGarrisonsHintVM : ViewModel
{
	public string Text { get; }

	public ImprovedGarrisonsHintVM(string text)
	{
		Text = text;
	}
}
