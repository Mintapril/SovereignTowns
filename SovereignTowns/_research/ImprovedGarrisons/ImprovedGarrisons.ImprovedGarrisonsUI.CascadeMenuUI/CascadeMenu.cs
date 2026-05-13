using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI;

public class CascadeMenu
{
	public MBBindingList<CascadeMenuElementVM> cascadeMenuElements;

	private CascadeMenuVM _dataSource;

	private string cascadeGauntletID = "CascadeMenu";

	private GauntletLayer _gauntletLayer;

	public Widget cascadeMenuWidget { get; set; }

	public string TitleText { get; set; }

	public CascadeMenuVM Datasource => _dataSource;

	public GauntletLayer GauntletLayer
	{
		get
		{
			return _gauntletLayer;
		}
		set
		{
			_gauntletLayer = value;
		}
	}

	public CascadeMenu(string title, MBBindingList<CascadeMenuElementVM> elements, int suggestedWidth = -1)
	{
		TitleText = title;
		_dataSource = new CascadeMenuVM(title, elements, suggestedWidth);
		cascadeMenuElements = elements;
	}

	public void Initialize()
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		GauntletLayer = new GauntletLayer(cascadeGauntletID, 9000 + UIManager.Instance.cascadeMenuGauntlet.currentCascadeLevel + 1, false)
		{
			IsFocusLayer = true
		};
		((ScreenLayer)GauntletLayer).InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
		GauntletLayer.LoadMovie("ImprovedGarrisonsCascadeMenu", (ViewModel)(object)Datasource);
	}

	public void OnFinalize()
	{
		if (GauntletLayer != null)
		{
			((ScreenLayer)GauntletLayer).InputRestrictions.ResetInputRestrictions();
			((ScreenBase)MapScreen.Instance).RemoveLayer((ScreenLayer)(object)GauntletLayer);
			ScreenManager.TryLoseFocus((ScreenLayer)(object)GauntletLayer);
			_gauntletLayer = null;
		}
		_dataSource = null;
	}
}
