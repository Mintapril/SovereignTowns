using TaleWorlds.Engine.Options;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class TitleTextVM : ConfigOptionsMenuItemVM
{
	public TitleTextVM(IOptionData option, TextObject name, TextObject description, ConfigMenuVM.OptionsDataType typeID)
		: base(option, name, description, typeID)
	{
	}

	public override void Cancel()
	{
	}

	public override bool IsChanged()
	{
		return false;
	}

	public override void RefreshValues()
	{
		base.RefreshValues();
	}

	public override void SetValue(float value)
	{
	}

	public override void UpdateValue()
	{
	}
}
