using System.Collections.Generic;
using ImprovedGarrisons.SaveSystem.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.Models;

public class GarrisonFoodModel : DefaultSettlementFoodModel
{
	public override ExplainedNumber CalculateTownFoodStocksChange(Town town, bool includeMarketStocks = true, bool includeDescriptions = false)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Expected O, but got Unknown
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		ExplainedNumber result = ((DefaultSettlementFoodModel)this).CalculateTownFoodStocksChange(town, includeMarketStocks, includeDescriptions);
		bool enablePlayerFoodBonus = ConfigManager.Instance.Config.EnablePlayerFoodBonus;
		bool enabeNPCFoodbonus = ConfigManager.Instance.Config.EnabeNPCFoodbonus;
		bool loadFoodGatheringModule = ConfigManager.Instance.Config.LoadFoodGatheringModule;
		bool flag = false;
		if (Hero.MainHero != null)
		{
			flag = town.OwnerClan != Hero.MainHero.Clan;
		}
		if (((flag && enabeNPCFoodbonus) || (!flag && enablePlayerFoodBonus)) && loadFoodGatheringModule)
		{
			ExplainedNumber val = default(ExplainedNumber);
			((ExplainedNumber)(ref val))._002Ector(0f, includeDescriptions, (TextObject)null);
			TextObject val2 = new TextObject("[IG-Cheats] Garrison Food Bonus", (Dictionary<string, object>)null);
			((ExplainedNumber)(ref val)).Add((float)ConfigManager.Instance.Config.DailyFoodGatheringAmount, val2, (TextObject)null);
			((ExplainedNumber)(ref result)).Add(((ExplainedNumber)(ref val)).ResultNumber, val2, (TextObject)null);
		}
		if (ConfigManager.Instance.Config.DisableGarrisonNeedsFood)
		{
			MobileParty garrisonParty = ((Fief)town).GarrisonParty;
			int num = ((garrisonParty != null) ? garrisonParty.Party.NumberOfAllMembers : 0);
			num /= 20;
			ExplainedNumber val3 = default(ExplainedNumber);
			((ExplainedNumber)(ref val3))._002Ector(0f, includeDescriptions, (TextObject)null);
			TextObject val4 = new TextObject("[IG-Cheats] Garrison needs no food", (Dictionary<string, object>)null);
			((ExplainedNumber)(ref val3)).Add((float)num, val4, (TextObject)null);
			((ExplainedNumber)(ref result)).Add(((ExplainedNumber)(ref val3)).ResultNumber, val4, (TextObject)null);
		}
		return result;
	}
}
