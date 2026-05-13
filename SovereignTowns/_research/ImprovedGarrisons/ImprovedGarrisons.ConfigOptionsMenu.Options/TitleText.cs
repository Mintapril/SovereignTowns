using TaleWorlds.Engine.Options;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class TitleText : OptionUtils, IOptionData
{
	public TitleText(string name)
		: base(name)
	{
	}

	public void Commit()
	{
	}

	public object GetOptionType()
	{
		return GetType();
	}

	public bool IsAction()
	{
		return false;
	}

	public bool IsNative()
	{
		return false;
	}

	public (string, bool) GetIsDisabledAndReasonID()
	{
		return (string.Empty, false);
	}
}
