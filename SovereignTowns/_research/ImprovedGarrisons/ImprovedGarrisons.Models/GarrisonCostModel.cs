using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.ActivityLogging;
using ImprovedGarrisons.Behaviours;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.Models;

public class GarrisonCostModel : DefaultClanFinanceModel
{
	public override ExplainedNumber CalculateClanExpenses(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		ExplainedNumber val = ((DefaultClanFinanceModel)this).CalculateClanExpenses(clan, true, applyWithdrawals, true);
		return ((DefaultClanFinanceModel)this).CalculateClanExpenses(clan, includeDescriptions, applyWithdrawals, includeDetails);
	}

	public override ExplainedNumber CalculateClanGoldChange(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		ExplainedNumber currentValue = ((DefaultClanFinanceModel)this).CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);
		return CalculateImprovedGarrisonCosts(clan, includeDescriptions, applyWithdrawals, currentValue);
	}

	private ExplainedNumber CalculateImprovedGarrisonCosts(Clan clan, bool includeDescriptions, bool applyWithdrawals, ExplainedNumber currentValue)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0515: Unknown result type (might be due to invalid IL or missing references)
		//IL_0516: Unknown result type (might be due to invalid IL or missing references)
		//IL_051a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected O, but got Unknown
		//IL_0309: Unknown result type (might be due to invalid IL or missing references)
		//IL_0313: Expected O, but got Unknown
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_0320: Expected O, but got Unknown
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Expected O, but got Unknown
		//IL_049a: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a1: Expected O, but got Unknown
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Expected O, but got Unknown
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Expected O, but got Unknown
		ExplainedNumber result = currentValue;
		try
		{
			if (clan == Hero.MainHero.Clan)
			{
			}
			List<MobileParty> allClanParties = GarrisonPartyBehavior.GetAllClanParties(clan);
			bool flag = allClanParties != null;
			double num = ConfigManager.Instance.Config.GarrisonTrainingMultiplier;
			if (num <= 1.0 && num > 0.0 && flag)
			{
				foreach (Town item in (List<Town>)(object)clan.Fiefs)
				{
					ActivityLog activityLog = Main.ActivityLogManager.GetActivityLog(item);
					if (activityLog == null || (!((SettlementComponent)item).IsCastle && !((SettlementComponent)item).IsTown))
					{
						continue;
					}
					TextObject val = new TextObject("{=misc_costmodel_trainingcosts}Improved garrison training of" + ModuleStrings._space + (object)((SettlementComponent)item).Name, (Dictionary<string, object>)null);
					if (activityLog.UnitUpgradeCosts > 0f)
					{
						((ExplainedNumber)(ref result)).Add(0f - activityLog.UnitUpgradeCosts, val, (TextObject)null);
						if (applyWithdrawals)
						{
							activityLog.ResetUpgradeCosts();
						}
					}
					TextObject val2 = new TextObject("{=misc_costmodel_recruitmentcosts}Improved garrison recruitment of" + ModuleStrings._space + (object)((SettlementComponent)item).Name, (Dictionary<string, object>)null);
					if (activityLog.RecruitmentCosts > 0f)
					{
						((ExplainedNumber)(ref result)).Add(0f - activityLog.RecruitmentCosts, val2, (TextObject)null);
						if (applyWithdrawals)
						{
							activityLog.ResetRecruitmentCosts();
						}
					}
					foreach (KeyValuePair<string, float> item2 in activityLog.RecruiterCosts.ToList())
					{
						TextObject val3 = new TextObject(item2.Key + ModuleStrings._space + ((object)new TextObject("{=misc_costs}costs", (Dictionary<string, object>)null)).ToString(), (Dictionary<string, object>)null);
						float value = item2.Value;
						((ExplainedNumber)(ref result)).Add(0f - value, val3, (TextObject)null);
						if (applyWithdrawals)
						{
							activityLog.RecruiterCosts.Remove(item2.Key);
						}
					}
				}
			}
			double num2 = ConfigManager.Instance.Config.GarrisonGuardsWageMultiplier;
			if (num2 <= 1.0 && num2 > 0.0 && flag)
			{
				foreach (MobileParty item3 in allClanParties)
				{
					if (item3 != null && item3.Party != null && item3.Party.IsMobile && Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(item3))
					{
						int totalWage = item3.TotalWage;
						Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(item3);
						if (mobileGarrisonHome != null)
						{
							TextObject val4 = new TextObject(((object)mobileGarrisonHome.Name)?.ToString() + ModuleStrings._space + ((object)new TextObject("{=misc_guardwages}Guard wages", (Dictionary<string, object>)null)).ToString(), (Dictionary<string, object>)null);
							((ExplainedNumber)(ref result)).Add((float)(num2 * (double)(0f - (float)totalWage)), val4, (TextObject)null);
						}
					}
				}
			}
			float garrisonWageMultiplier = ConfigManager.Instance.Config.GarrisonWageMultiplier;
			if (!(garrisonWageMultiplier >= 1f) && flag)
			{
				foreach (MobileParty item4 in allClanParties)
				{
					if (item4 == null || !item4.IsActive || !item4.IsGarrison)
					{
						continue;
					}
					int totalWage2 = item4.TotalWage;
					int num3 = ((item4.LeaderHero != null && item4.LeaderHero != clan.Leader && !item4.IsCaravan && item4.LeaderHero.Gold <= 10000) ? ((item4.LeaderHero.Gold < 5000) ? ((int)((float)(5000 - item4.LeaderHero.Gold) / 10f)) : 0) : 0);
					float num4 = (float)clan.Gold + ((ExplainedNumber)(ref result)).ResultNumber;
					if (num4 > (float)(totalWage2 + num3))
					{
						int num5 = totalWage2;
						if (num3 > 0)
						{
							GiveGoldAction.ApplyBetweenCharacters((Hero)null, item4.LeaderHero, num3, true);
						}
					}
					else
					{
						int num5 = (int)(((int)num4 > 0) ? num4 : 0f);
						if (num5 > totalWage2)
						{
							num5 = totalWage2;
						}
					}
					TextObject val5 = new TextObject("{=rhKxsdtz} {PARTY_NAME} finance help", (Dictionary<string, object>)null);
					val5.SetTextVariable("PARTY_NAME", item4.Name);
					double num6 = -1f * (garrisonWageMultiplier - 1f);
					((ExplainedNumber)(ref result)).Add((float)(num6 * (double)(float)totalWage2), val5, (TextObject)null);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return result;
	}
}
