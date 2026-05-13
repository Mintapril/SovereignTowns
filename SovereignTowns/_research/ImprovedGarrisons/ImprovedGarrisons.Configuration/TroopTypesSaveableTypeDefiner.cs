using ImprovedGarrisons.Upgrade;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Resolvers;

namespace ImprovedGarrisons.Configuration;

internal class TroopTypesSaveableTypeDefiner : SaveableTypeDefiner
{
	public TroopTypesSaveableTypeDefiner()
		: base(62589786)
	{
	}

	protected override void DefineClassTypes()
	{
		((SaveableTypeDefiner)this).AddClassDefinition(typeof(TroopTypes.Type), 1, (IObjectResolver)null);
	}

	protected override void DefineContainerDefinitions()
	{
	}
}
