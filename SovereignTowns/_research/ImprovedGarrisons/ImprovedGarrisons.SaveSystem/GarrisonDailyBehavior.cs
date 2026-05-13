using System;
using System.Collections.Generic;
using ImprovedGarrisons.ActivityLogging;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.SaveSystem;

public class GarrisonDailyBehavior : CampaignBehaviorBase
{
	public override void RegisterEvents()
	{
		CampaignEvents.DailyTickEvent.AddNonSerializedListener((object)this, (Action)DailyBehavior);
	}

	public override void SyncData(IDataStore dataStore)
	{
	}

	private void DailyBehavior()
	{
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Expected O, but got Unknown
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Expected O, but got Unknown
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Expected O, but got Unknown
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Expected O, but got Unknown
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cb: Expected O, but got Unknown
		if (ConfigManager.Instance.Config.RecruitsAreRecruitedFromVillages)
		{
			Main.RecruitmentLogic.RecruitSurroundingForAllSettlements();
		}
		else
		{
			Main.RecruitmentLogic.CheatSpawnUnitInAllGarrisons(ConfigManager.Instance.Config.AmountOfUnitsToSpawn);
		}
		Main.RecruitmentLogic.TryRecruitAllPrisoners();
		Main.UpgradeLogic.GiveExpToAllGarrisons();
		if (ConfigManager.Instance.Config.DisableDailyMessage)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (ActivityLog value in Main.ActivityLogManager.ActivityLogs.Values)
		{
			num += value.DailyRecruits;
			num2 += value.DailyUpgrades;
			num3 += value.DailyPrisonerTurnover;
			value.ResetDailies();
		}
		bool flag = num > 0;
		bool flag2 = num2 > 0;
		bool flag3 = num3 > 0;
		if (flag || flag2 || flag3)
		{
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_daily_info1}\"My lord your Improved Garrisons recruited", (Dictionary<string, object>)null)).ToString() + " " + num + " " + ((object)new TextObject("{=info_daily_info2}soldiers, upgraded", (Dictionary<string, object>)null)).ToString() + " " + num2 + " " + ((object)new TextObject("{=info_daily_info3}troops to the next tier and persuaded", (Dictionary<string, object>)null)).ToString() + " " + num3 + " " + ((object)new TextObject("{=info_daily_info4}prisoners today.\"", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.modMainColor)));
		}
	}
}
