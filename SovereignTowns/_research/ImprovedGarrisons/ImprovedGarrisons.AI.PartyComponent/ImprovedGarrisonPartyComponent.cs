using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.AI.PartyComponent;

public class ImprovedGarrisonPartyComponent : WarPartyComponent
{
	private Settlement _settlement;

	private Hero _partyOwner;

	private TextObject _name;

	public override Hero PartyOwner => _partyOwner;

	public override TextObject Name => _name;

	public override Settlement HomeSettlement => _settlement;

	public ImprovedGarrisonPartyComponent(Settlement settlement, Hero partyOwner, TextObject name)
	{
		_settlement = settlement;
		_partyOwner = partyOwner;
		_name = name;
	}
}
