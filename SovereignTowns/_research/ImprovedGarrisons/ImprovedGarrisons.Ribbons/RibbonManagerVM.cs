using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;

namespace ImprovedGarrisons.Ribbons;

public class RibbonManagerVM : ViewModel
{
	private List<RibbonVM> allRibbons = new List<RibbonVM>();

	public RibbonVM FirstRibbon { get; set; } = new RibbonVM();


	public RibbonVM SecondRibbon { get; set; } = new RibbonVM();


	public RibbonVM ThirdRibbon { get; set; } = new RibbonVM();


	private int ribbonindex => allRibbons.Count();

	public void AddRibbon(string title, string text)
	{
		switch (ribbonindex)
		{
		case 0:
			FirstRibbon.Title = title;
			FirstRibbon.Text = text;
			allRibbons.Add(FirstRibbon);
			break;
		case 1:
			SecondRibbon.Title = title;
			SecondRibbon.Text = text;
			allRibbons.Add(SecondRibbon);
			break;
		case 2:
			ThirdRibbon.Title = title;
			ThirdRibbon.Text = text;
			allRibbons.Add(ThirdRibbon);
			break;
		}
	}

	public void RemoveAllRibbons()
	{
		allRibbons.Clear();
		FirstRibbon = new RibbonVM();
		SecondRibbon = new RibbonVM();
		ThirdRibbon = new RibbonVM();
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		foreach (RibbonVM allRibbon in allRibbons)
		{
			((ViewModel)allRibbon).RefreshValues();
		}
	}
}
