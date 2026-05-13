using System;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ImprovedGarrisons.SaveSystem;

public class UiBehavior : CampaignBehaviorBase
{
	public override void RegisterEvents()
	{
		CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener((object)this, (Action<MobileParty, Settlement>)OnSettlementLeftAction);
		CampaignEvents.AfterSettlementEntered.AddNonSerializedListener((object)this, (Action<MobileParty, Settlement, Hero>)OnSettlementEnteredAction);
	}

	private void OnSettlementEnteredAction(MobileParty party, Settlement settlement, Hero hero)
	{
		MobileParty mainParty = MobileParty.MainParty;
		bool flag = Main.GarrisonBehavior.PlayerHasAFief();
		if (mainParty != null && party == mainParty && flag)
		{
			UIManager.Instance.TryInitializeImprovedGarrisonsUI();
		}
	}

	private void OnSettlementLeftAction(MobileParty party, Settlement settlement)
	{
		MobileParty mainParty = MobileParty.MainParty;
		bool enableImprovedGarrisonsUIOnMap = GlobalSettings.Instance.EnableImprovedGarrisonsUIOnMap;
		if (mainParty != null && party == mainParty && !enableImprovedGarrisonsUIOnMap)
		{
			UIManager.Instance.CloseImprovedGarrisonsUI();
		}
	}

	public override void SyncData(IDataStore dataStore)
	{
	}
}
