using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AITypes;

public class GarrisonRecruiter : BoundedParty
{
	public enum Mode
	{
		recruiting,
		trading,
		returning,
		holding,
		waiting
	}

	public Settlement tradeTarget = null;

	private Dictionary<Settlement, int> visitedSettlementsWithTimer = new Dictionary<Settlement, int>();

	private int triesWithoutRecruitmentSettlement = 0;

	public Mode currentMode { get; set; } = Mode.recruiting;


	private Mode previousMode { get; set; }

	public GarrisonRecruiter(MobileParty party, Settlement home)
		: base(party, home)
	{
		party.MemberRoster.UpdateVersion();
	}

	public void PartialHourlyThinkBehavior()
	{
		try
		{
			if (currentMode == Mode.recruiting && tradeTarget != null)
			{
				NavigationType val = default(NavigationType);
				float num = default(float);
				bool flag = default(bool);
				AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(mobileParty, tradeTarget, mobileParty.HasNavalNavigationCapability, ref val, ref num, ref flag);
				if (num < 1f)
				{
					EnterSettlementAction.ApplyForParty(mobileParty, tradeTarget);
					mobileParty.Ai.DisableAi();
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void HourlyThinkBehavior()
	{
		try
		{
			mobileParty.Ai.EnableAi();
			switch (currentMode)
			{
			case Mode.recruiting:
				if (tradeTarget != null && SettlementIsAValidTradePartner(tradeTarget, mobileParty.MapFaction, includeVillages: true))
				{
					if (mobileParty.CurrentSettlement != null && mobileParty.CurrentSettlement == tradeTarget)
					{
						if (base.homeGarrisonSettings.RecruitmentFollowsTemplate)
						{
							ExecuteRecruitTemplateUnitsFromSettlement(mobileParty.CurrentSettlement);
						}
						else
						{
							ExecuteBasicRecruitFromSettlement(mobileParty.CurrentSettlement);
						}
					}
					else
					{
						Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(tradeTarget, mobileParty);
					}
				}
				else
				{
					tradeTarget = GetBestSettlementToRecruitFrom();
					if (tradeTarget == null)
					{
						if (!base.homeGarrisonSettings.RecruitOnlyEliteUnits)
						{
							currentMode = Mode.waiting;
						}
						else
						{
							currentMode = Mode.waiting;
						}
					}
				}
				SetReturnModeIfNeeded();
				break;
			case Mode.returning:
				if (mobileParty.CurrentSettlement != null && mobileParty.CurrentSettlement == fromSettlement)
				{
					SellItems(mobileParty.CurrentSettlement, onlyNecessary: false);
					Main.PartyManagement.RecruitMobilePartyToGarrison(mobileParty, fromSettlement);
				}
				else
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(fromSettlement, mobileParty);
				}
				break;
			case Mode.trading:
			{
				bool flag = WantsToBuyHorses() > 0;
				if (flag)
				{
					if (mobileParty.CurrentSettlement != null && mobileParty.CurrentSettlement == settlementTradeTarget)
					{
						bool recruiterAllowHorseBuy = base.homeGarrisonSettings.RecruiterAllowHorseBuy;
						ExecuteTrade(sellPrisoners: true, sellItems: true, recruiterAllowHorseBuy);
					}
					if (settlementTradeTarget == null)
					{
						if (!flag)
						{
							settlementTradeTarget = fromSettlement;
						}
						else
						{
							settlementTradeTarget = BestNearbyTradeSettlement(flag);
						}
					}
					Settlement val = settlementTradeTarget;
					if (val != null)
					{
						Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(val, mobileParty);
					}
					else
					{
						currentMode = Mode.recruiting;
					}
				}
				else
				{
					currentMode = Mode.recruiting;
				}
				break;
			}
			case Mode.waiting:
				mobileParty.Ai.SetDoNotMakeNewDecisions(false);
				tradeTarget = GetBestSettlementToRecruitFrom();
				if (tradeTarget == null)
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(fromSettlement, mobileParty);
				}
				else
				{
					currentMode = Mode.recruiting;
				}
				SetReturnModeIfNeeded();
				break;
			case Mode.holding:
				mobileParty.Ai.SetDoNotMakeNewDecisions(false);
				if (Hero.MainHero.PartyBelongedTo != null)
				{
					MobileParty targetParty = Hero.MainHero.PartyBelongedTo.TargetParty;
					if (targetParty != null && targetParty == mobileParty)
					{
						mobileParty.SetMoveModeHold();
						break;
					}
					currentMode = previousMode;
					if (currentMode == Mode.holding)
					{
						currentMode = Mode.recruiting;
					}
				}
				else
				{
					currentMode = previousMode;
					if (currentMode == Mode.holding)
					{
						currentMode = Mode.recruiting;
					}
				}
				break;
			}
			StopIfPlayerTarget();
			IfStuckPortToHome();
			RemoveItemsIfOverburdened();
			GiveFoodIfNeeded();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetReturnMode()
	{
		currentMode = Mode.returning;
		if (!base.homeGarrisonSettings.RecruiterAutoSpawn)
		{
		}
	}

	private void SetReturnModeIfNeeded()
	{
		bool flag = mobileParty.Party.NumberOfAllMembers >= base.homeGarrisonSettings.RecruiterInitialSize + base.homeGarrisonSettings.RecruiterRecruitAmount;
		bool flag2 = GetAmountLeftToRecruit() <= 0;
		bool flag3 = false;
		if (((Fief)fromSettlement.Town).GarrisonParty != null)
		{
			int partySizeLimit = Main.PartyManagement.GetPartySizeLimit(((Fief)fromSettlement.Town).GarrisonParty.Party);
			flag3 = ((Fief)fromSettlement.Town).GarrisonParty.Party.NumberOfAllMembers + mobileParty.Party.NumberOfAllMembers >= partySizeLimit;
		}
		if (flag || flag2 || flag3)
		{
			currentMode = Mode.returning;
		}
	}

	private bool SetTradeModeIfNeeded()
	{
		if (currentMode == Mode.recruiting && base.homeGarrisonSettings.RecruiterAllowHorseBuy && WantsToBuyHorses() > 0)
		{
			settlementTradeTarget = BestNearbyTradeSettlement(toBuy: true);
			if (settlementTradeTarget != null)
			{
				currentMode = Mode.trading;
				return true;
			}
		}
		return false;
	}

	private Settlement GetBestSettlementToRecruitFrom()
	{
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (GetCurrentHourTimeOfCounter("GetBestSettlement") > 0)
			{
				return null;
			}
			List<Settlement> list = new List<Settlement>();
			CultureObject cultureObjectByID = GetCultureObjectByID(base.homeGarrisonSettings.RecruiterCultureToRecruit);
			float num = float.MaxValue;
			Settlement val = null;
			Dictionary<CharacterObject, int> allNeededTemplateUnitsLeftToRecruitWithRecruiter = Main.RecruitmentLogic.GetAllNeededTemplateUnitsLeftToRecruitWithRecruiter(fromSettlement, this);
			List<CharacterObject> list2 = new List<CharacterObject>();
			IEnumerable<CharacterObject> enumerable = CharacterObject.FindAll((Predicate<CharacterObject>)delegate(CharacterObject x)
			{
				bool flag2 = ((MBObjectBase)x).StringId.Contains("militia");
				return !((BasicCharacterObject)x).IsHero && ((BasicCharacterObject)x).IsSoldier && !flag2;
			});
			foreach (CharacterObject item in enumerable)
			{
				foreach (KeyValuePair<CharacterObject, int> item2 in allNeededTemplateUnitsLeftToRecruitWithRecruiter)
				{
					if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item, item2.Key))
					{
						list2.Add(item);
					}
				}
			}
			int amountLeftToRecruit = GetAmountLeftToRecruit();
			List<Settlement> list3 = new List<Settlement>();
			if (triesWithoutRecruitmentSettlement <= 2)
			{
				LocatableSearchData<Settlement> val2 = Settlement.StartFindingLocatablesAroundPosition(mobileParty.GetPosition2D, 30f);
				for (Settlement val3 = Settlement.FindNextLocatable(ref val2); val3 != null; val3 = Settlement.FindNextLocatable(ref val2))
				{
					if (!val3.IsHideout && !val3.IsRaided && !val3.IsUnderRaid)
					{
						list3.Add(val3);
					}
				}
			}
			else
			{
				list3 = (List<Settlement>)(object)Settlement.All;
			}
			NavigationType val4 = default(NavigationType);
			float num3 = default(float);
			bool flag = default(bool);
			foreach (Settlement item3 in list3)
			{
				if ((cultureObjectByID == item3.Culture || cultureObjectByID == null || base.homeGarrisonSettings.RecruitmentFollowsTemplate) && SettlementIsAValidTradePartner(item3, mobileParty.MapFaction, includeVillages: true) && !visitedSettlementsWithTimer.TryGetValue(item3, out var _) && (!item3.IsVillage || item3.Village.Bound != fromSettlement) && item3 != fromSettlement)
				{
					bool recruiterRecruitOnlyElites = base.homeGarrisonSettings.RecruiterRecruitOnlyElites;
					int num2 = 0;
					num2 = ((!base.homeGarrisonSettings.RecruitmentFollowsTemplate) ? Main.RecruitmentLogic.OverallAmountOfAvailableRecruits(item3, int.MaxValue, base.ownerHero, recruiterRecruitOnlyElites) : Main.RecruitmentLogic.AmountOfAvailableRecruitsByList(item3, amountLeftToRecruit, base.ownerHero, list2));
					AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(mobileParty, item3, mobileParty.HasNavalNavigationCapability, ref val4, ref num3, ref flag);
					if (num3 < num && (num2 >= 3 || ((recruiterRecruitOnlyElites || base.homeGarrisonSettings.RecruitmentFollowsTemplate) && num2 >= 1)))
					{
						val = item3;
						num = num3;
					}
				}
			}
			if (val != null)
			{
				triesWithoutRecruitmentSettlement = 0;
				return val;
			}
			AddHourCounter("GetBestSettlement", 6);
			triesWithoutRecruitmentSettlement++;
			return null;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	private CultureObject GetCultureObjectByID(string id)
	{
		try
		{
			if (id != null)
			{
				return MBObjectManager.Instance.GetObject<CultureObject>(id);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	private void ExecuteBasicRecruitFromSettlement(Settlement settlement)
	{
		if (mobileParty.CurrentSettlement == null || mobileParty.CurrentSettlement != settlement)
		{
			return;
		}
		bool recruiterRecruitOnlyElites = base.homeGarrisonSettings.RecruiterRecruitOnlyElites;
		List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = Main.RecruitmentLogic.GetAllRecruitableRecruitsFromSettlement(settlement, 99, base.ownerHero, recruiterRecruitOnlyElites);
		foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
		{
			if (item.Item2 != null)
			{
				int amountLeftToRecruit = GetAmountLeftToRecruit();
				if (amountLeftToRecruit > 0)
				{
					RecruitmentCampaignBehavior behavior = Campaign.Current.CampaignBehaviorManager.GetBehavior<RecruitmentCampaignBehavior>();
					MethodInfo method = ((object)behavior).GetType().GetMethod("GetRecruitVolunteerFromIndividual", BindingFlags.Instance | BindingFlags.NonPublic);
					method.Invoke(behavior, new object[4] { mobileParty, item.Item2, item.Item1, item.Item3 });
				}
				Main.ActivityLogManager.AddUnitRecruitmentCost(this, item.Item2, 1, settlement.OwnerClan.Leader);
			}
		}
		ResetTradeTarget();
		visitedSettlementsWithTimer.Add(settlement, 0);
	}

	private void ExecuteRecruitTemplateUnitsFromSettlement(Settlement settlement)
	{
		if (mobileParty.CurrentSettlement == null || mobileParty.CurrentSettlement != settlement)
		{
			return;
		}
		List<Tuple<Hero, CharacterObject, int>> allRecruitableRecruitsFromSettlement = Main.RecruitmentLogic.GetAllRecruitableRecruitsFromSettlement(settlement, 99, base.ownerHero, onlyEliteUnits: false);
		foreach (Tuple<Hero, CharacterObject, int> item in allRecruitableRecruitsFromSettlement)
		{
			Dictionary<CharacterObject, int> allNeededTemplateUnitsLeftToRecruitWithRecruiter = Main.RecruitmentLogic.GetAllNeededTemplateUnitsLeftToRecruitWithRecruiter(fromSettlement, this);
			if (item.Item2 == null || allNeededTemplateUnitsLeftToRecruitWithRecruiter.Count <= 0)
			{
				continue;
			}
			bool flag = false;
			foreach (KeyValuePair<CharacterObject, int> item2 in allNeededTemplateUnitsLeftToRecruitWithRecruiter)
			{
				if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item.Item2, item2.Key))
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				RecruitmentCampaignBehavior behavior = Campaign.Current.CampaignBehaviorManager.GetBehavior<RecruitmentCampaignBehavior>();
				MethodInfo method = ((object)behavior).GetType().GetMethod("GetRecruitVolunteerFromIndividual", BindingFlags.Instance | BindingFlags.NonPublic);
				method.Invoke(behavior, new object[4] { mobileParty, item.Item2, item.Item1, item.Item3 });
				Main.ActivityLogManager.AddUnitRecruitmentCost(this, item.Item2, 1, settlement.OwnerClan.Leader);
			}
		}
		ResetTradeTarget();
		visitedSettlementsWithTimer.Add(settlement, 0);
	}

	public int GetAmountLeftToRecruit()
	{
		int numberOfRecruited = GetNumberOfRecruited();
		int num = 300;
		int num2 = 0;
		if (((Fief)fromSettlement.Town).GarrisonParty != null)
		{
			num = Main.PartyManagement.GetPartySizeLimit(((Fief)fromSettlement.Town).GarrisonParty.Party);
			num2 = ((Fief)fromSettlement.Town).GarrisonParty.Party.NumberOfAllMembers;
		}
		int numberOfAllMembers = mobileParty.Party.NumberOfAllMembers;
		if (numberOfAllMembers + num2 >= num)
		{
			return 0;
		}
		int num3 = 0;
		if (base.homeGarrisonSettings.RecruitmentFollowsTemplate)
		{
			Dictionary<CharacterObject, int> allNeededTemplateUnitsLeftToRecruitWithRecruiter = Main.RecruitmentLogic.GetAllNeededTemplateUnitsLeftToRecruitWithRecruiter(fromSettlement, this);
			foreach (KeyValuePair<CharacterObject, int> item in allNeededTemplateUnitsLeftToRecruitWithRecruiter)
			{
				num3 += item.Value;
			}
			if (num3 <= 0)
			{
				return 0;
			}
		}
		int num4 = base.homeGarrisonSettings.RecruiterRecruitAmount - numberOfRecruited;
		return (num4 >= 0) ? num4 : 0;
	}

	public bool ResetTradeTarget()
	{
		tradeTarget = null;
		return true;
	}

	public MobileParty GetMobileParty()
	{
		return mobileParty;
	}

	private void StopIfPlayerTarget()
	{
		if (Hero.MainHero.PartyBelongedTo != null)
		{
			MobileParty targetParty = Hero.MainHero.PartyBelongedTo.TargetParty;
			if (targetParty != null && targetParty == mobileParty)
			{
				previousMode = currentMode;
				currentMode = Mode.holding;
			}
		}
	}

	public int GetNumberOfRecruited()
	{
		int num = mobileParty.Party.NumberOfAllMembers - base.homeGarrisonSettings.RecruiterInitialSize;
		return (num > 0) ? num : 0;
	}

	public void SetInitialSize()
	{
		base.homeGarrisonSettings.RecruiterInitialSize = mobileParty.Party.NumberOfAllMembers;
	}

	public override void NextHour()
	{
		base.NextHour();
		List<Settlement> list = new List<Settlement>();
		foreach (Settlement item in visitedSettlementsWithTimer.Keys.ToList())
		{
			if (visitedSettlementsWithTimer[item]++ >= 48)
			{
				visitedSettlementsWithTimer.Remove(item);
			}
		}
		foreach (Settlement item2 in list)
		{
			visitedSettlementsWithTimer.Remove(item2);
		}
		SetTradeModeIfNeeded();
	}

	public string GetStatusText()
	{
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Expected O, but got Unknown
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Expected O, but got Unknown
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Expected O, but got Unknown
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Expected O, but got Unknown
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Expected O, but got Unknown
		string result = null;
		bool flag = true;
		switch (currentMode)
		{
		case Mode.recruiting:
		{
			if (tradeTarget == null)
			{
				flag = false;
				break;
			}
			TextObject val = ((tradeTarget.EncyclopediaLinkWithName != (TextObject)null) ? tradeTarget.EncyclopediaLinkWithName : tradeTarget.Name);
			MBTextManager.SetTextVariable("CURRENTSETTLEMENT", val, false);
			result = ((object)new TextObject("{=menu_recruiter_status_goingto}The recruiter is going to" + ModuleStrings._space + "{CURRENTSETTLEMENT}", (Dictionary<string, object>)null)).ToString();
			break;
		}
		case Mode.returning:
			result = ((object)new TextObject("{=menu_recruiter_status_returningto}The recruiter is returning to" + ModuleStrings._space + (object)fromSettlement.Name, (Dictionary<string, object>)null)).ToString();
			break;
		case Mode.waiting:
			result = ((object)new TextObject("{=menu_recruiter_status_gatheringintel}The recruiter is gathering information about new recruits", (Dictionary<string, object>)null)).ToString();
			break;
		case Mode.trading:
			if (tradeTarget == null)
			{
				flag = false;
			}
			else
			{
				result = ((object)new TextObject("{=menu_recruiter_status_tradingwith}The recruiter is trading with" + ModuleStrings._space + (object)tradeTarget.Name, (Dictionary<string, object>)null)).ToString();
			}
			break;
		}
		if (!flag)
		{
			result = ((object)new TextObject("{=menu_recruiter_status_gatheringintel}The recruiter is gathering information about new recruits", (Dictionary<string, object>)null)).ToString();
		}
		return result;
	}
}
