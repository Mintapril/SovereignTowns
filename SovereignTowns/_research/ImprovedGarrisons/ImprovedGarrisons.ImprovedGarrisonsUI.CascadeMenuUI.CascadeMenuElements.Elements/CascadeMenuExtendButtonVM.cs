namespace ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements.Elements;

public class CascadeMenuExtendButtonVM : CascadeMenuElementVM
{
	private CascadeMenu cascadeMenu;

	private string text;

	public new int OptionTypeID { get; set; } = 2;


	public string Text
	{
		get
		{
			return text;
		}
		set
		{
			text = value;
		}
	}

	public CascadeMenuExtendButtonVM(string text, CascadeMenu cascadeMenu)
	{
		Text = text;
		this.cascadeMenu = cascadeMenu;
	}

	public void ExecuteClick()
	{
		UIManager.Instance.cascadeMenuGauntlet.InitializeAndPushCascadeMenu(cascadeMenu);
	}
}
