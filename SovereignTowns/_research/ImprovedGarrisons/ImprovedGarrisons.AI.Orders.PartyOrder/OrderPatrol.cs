using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.AI.Orders.PartyOrder;

public class OrderPatrol : ImprovedPartyOrder
{
	public enum Mode
	{
		Patrol,
		Trade,
		ReturnToRegion,
		PrisonerTurnIn,
		Heal,
		ClearHideout
	}

	private Settlement settlementTarget;

	private readonly List<Settlement> BoundSettlements = new List<Settlement>();

	private Tuple<List<Settlement>, List<Settlement>> SettlementsToPatrol;

	private float patrolRadius = 60f;

	private MobileParty currentTarget;

	public Settlement hideoutTarget;

	private bool isMobileGarrison;

	private MobileGarrison mobileGarrison;

	private bool justHealedItself = false;

	public Mode CurrentMode { get; set; } = Mode.Patrol;


	protected Settlement MainPatrolSettlement { get; private set; }

	public ImprovedSettlement ImprovedSettlement { get; private set; }

	public OrderPatrol(Settlement patrolSettlement)
	{
		MainPatrolSettlement = patrolSettlement;
		ImprovedSettlement = Main.GarrisonBehavior.GetAutomatedGarrisonForSettlement(patrolSettlement);
		InitializePatrolSettlements();
		CalculatePatrolRadius();
	}

	public override void InitializeOrder(ImprovedPartyAi partyToOrder)
	{
		base.InitializeOrder(partyToOrder);
		isMobileGarrison = Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(partyToOrder.mobileParty);
		if (isMobileGarrison)
		{
			mobileGarrison = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(partyToOrder.mobileParty);
		}
	}

	public override void ExecuteOrder()
	{
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0736: Unknown result type (might be due to invalid IL or missing references)
		//IL_0740: Expected O, but got Unknown
		//IL_074a: Unknown result type (might be due to invalid IL or missing references)
		//IL_074f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0759: Expected O, but got Unknown
		//IL_066f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0679: Expected O, but got Unknown
		//IL_0683: Unknown result type (might be due to invalid IL or missing references)
		//IL_0688: Unknown result type (might be due to invalid IL or missing references)
		//IL_0692: Expected O, but got Unknown
		//IL_0ace: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ad8: Expected O, but got Unknown
		//IL_0b09: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b13: Expected O, but got Unknown
		//IL_0b1e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b23: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b2d: Expected O, but got Unknown
		//IL_0343: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
			base.PartyToOrder.ReturnToBaseAggressiveness();
			DontRunToFar();
			SetTradeModeIfNeeded();
			switch (CurrentMode)
			{
			case Mode.Patrol:
			{
				bool flag = false;
				if (base.PartyToOrder.GetCurrentHourTimeOfCounter("PatrolCounter") <= 0)
				{
					base.PartyToOrder.AddHourCounter("PatrolCounter", 16);
					flag = true;
				}
				if ((flag || settlementTarget == null) && !base.PartyToOrder.RethinkNextHour)
				{
					if (BoundSettlements != null)
					{
						base.PartyToOrder.RethinkNextHour = true;
						if (SettlementsToPatrol.Item1.Count > 0)
						{
							Settlement val = SettlementsToPatrol.Item1.First();
							Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(val, base.PartyToOrder.mobileParty);
							settlementTarget = val;
							SettlementsToPatrol.Item1.Remove(val);
							if (!SettlementsToPatrol.Item2.Contains(val))
							{
								SettlementsToPatrol.Item2.Add(val);
							}
						}
						else
						{
							List<Settlement> item = SettlementsToPatrol.Item2;
							SettlementsToPatrol = new Tuple<List<Settlement>, List<Settlement>>(item, new List<Settlement>());
						}
					}
				}
				else if (settlementTarget != null)
				{
					base.PartyToOrder.mobileParty.SetMovePatrolAroundSettlement(settlementTarget, base.PartyToOrder.mobileParty.NavigationCapability, false);
				}
				if (GiveDefenseOrderIfAttacked())
				{
					break;
				}
				if (base.PartyToOrder.ReplenishEnabled && !SetHealModeIfNeeded() && isMobileGarrison)
				{
					mobileGarrison.CheckForReturn();
				}
				MobileParty bestNearestHostileParty = base.PartyToOrder.GetBestNearestHostileParty(base.PartyToOrder.partySightRadius, base.PartyToOrder.mobileParty);
				if (bestNearestHostileParty != null && base.PartyToOrder.mobileParty.Aggressiveness > 0f && !base.PartyToOrder.targetsWithTimer.TryGetValue(bestNearestHostileParty, out var _))
				{
					if (bestNearestHostileParty != currentTarget)
					{
						base.PartyToOrder.ResetTarget();
						base.PartyToOrder.HourCounter = 0;
						currentTarget = bestNearestHostileParty;
						base.PartyToOrder.targetsWithTimer.Add(currentTarget, 0);
					}
					base.PartyToOrder.mobileParty.SetMoveEngageParty(bestNearestHostileParty, base.PartyToOrder.mobileParty.NavigationCapability);
				}
				if (currentTarget != null && currentTarget.IsVisible && base.PartyToOrder.MobilePartyEngageIsAllowed(currentTarget, base.PartyToOrder.mobileParty))
				{
					base.PartyToOrder.mobileParty.SetMoveEngageParty(currentTarget, base.PartyToOrder.mobileParty.NavigationCapability);
				}
				if (currentTarget != null && ((base.PartyToOrder.HourCounter > 0 && base.PartyToOrder.HourCounter % 6 == 0) || !base.PartyToOrder.MobilePartyEngageIsAllowed(currentTarget, base.PartyToOrder.mobileParty)))
				{
					base.PartyToOrder.ResetTarget();
					currentTarget = null;
					base.PartyToOrder.HourCounter = 0;
				}
				if (isMobileGarrison && mobileGarrison.ShortLife && CurrentMode == Mode.Patrol)
				{
					mobileGarrison.SetReturnMode();
				}
				break;
			}
			case Mode.PrisonerTurnIn:
				if (base.PartyToOrder.mobileParty.PrisonRoster != null && base.PartyToOrder.mobileParty.PrisonRoster.TotalManCount > 0)
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(MainPatrolSettlement, base.PartyToOrder.mobileParty);
				}
				else
				{
					SetPatrolMode();
				}
				GiveDefenseOrderIfAttacked();
				break;
			case Mode.ReturnToRegion:
				base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
				base.PartyToOrder.mobileParty.Aggressiveness = 0f;
				GiveDefenseOrderIfAttacked();
				if (GetDistanceToPatrolSettlement() > patrolRadius / 2f)
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(MainPatrolSettlement, base.PartyToOrder.mobileParty);
				}
				else
				{
					SetPatrolMode();
				}
				break;
			case Mode.Heal:
				if (!base.PartyToOrder.ReplenishEnabled)
				{
					SetPatrolMode();
					break;
				}
				base.PartyToOrder.mobileParty.Aggressiveness = 0f;
				if (base.PartyToOrder.mobileParty.CurrentSettlement != null && base.PartyToOrder.mobileParty.CurrentSettlement == MainPatrolSettlement)
				{
					if (base.PartyToOrder.NeedsHeal())
					{
						if (!GiveDefenseOrderIfAttacked())
						{
							Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(MainPatrolSettlement, base.PartyToOrder.mobileParty);
							justHealedItself = true;
							base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(true);
						}
						break;
					}
					if (isMobileGarrison && ((Fief)MainPatrolSettlement.Town).GarrisonParty != null)
					{
						List<Tuple<CharacterObject, int>> allReplenishTroops = mobileGarrison.GetAllReplenishTroops();
						if (allReplenishTroops != null && allReplenishTroops.Count > 0)
						{
							Main.GarrisonPartyBehavior.TransferTroopsFromPartyToParty(((Fief)MainPatrolSettlement.Town).GarrisonParty, allReplenishTroops, base.PartyToOrder.mobileParty.Party);
							if (!base.PartyToOrder.isNPC)
							{
								InformationManager.DisplayMessage(new InformationMessage(((object)base.PartyToOrder.mobileParty.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=party_replenished}replenished its troops.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
							}
						}
					}
					if (isMobileGarrison)
					{
						bool enableHorseBuy = mobileGarrison.homeGarrisonSettings.EnableHorseBuy;
						bool enablePrisonerSell = mobileGarrison.homeGarrisonSettings.EnablePrisonerSell;
						mobileGarrison.ExecuteTrade(enablePrisonerSell, sellItems: true, enableHorseBuy);
					}
					SetPatrolMode();
					base.PartyToOrder.mobileParty.Aggressiveness = 0.9f;
					if (justHealedItself && !base.PartyToOrder.isNPC)
					{
						InformationManager.DisplayMessage(new InformationMessage(((object)base.PartyToOrder.mobileParty.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=party_healed}healed its troops.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
						justHealedItself = false;
						base.PartyToOrder.mobileParty.Ai.SetDoNotMakeNewDecisions(false);
					}
				}
				else
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(MainPatrolSettlement, base.PartyToOrder.mobileParty);
				}
				break;
			case Mode.Trade:
			{
				if (!isMobileGarrison)
				{
					SetPatrolMode();
					break;
				}
				bool flag2 = WantsToSellPrisoners();
				bool enableHorseBuy2 = mobileGarrison.homeGarrisonSettings.EnableHorseBuy;
				bool flag3 = false;
				if (enableHorseBuy2)
				{
					flag3 = base.PartyToOrder.WantsToBuyHorses() > 0;
				}
				if (flag2 || flag3)
				{
					if (base.PartyToOrder.mobileParty.CurrentSettlement != null && base.PartyToOrder.mobileParty.CurrentSettlement == base.PartyToOrder.settlementTradeTarget)
					{
						bool enablePrisonerSell2 = mobileGarrison.homeGarrisonSettings.EnablePrisonerSell;
						base.PartyToOrder.ExecuteTrade(enablePrisonerSell2, sellItems: true, enableHorseBuy2);
					}
					if (base.PartyToOrder.settlementTradeTarget == null)
					{
						if (!flag3)
						{
							base.PartyToOrder.settlementTradeTarget = MainPatrolSettlement;
						}
						else
						{
							base.PartyToOrder.settlementTradeTarget = base.PartyToOrder.BestNearbyTradeSettlement();
						}
					}
					Settlement settlementTradeTarget = base.PartyToOrder.settlementTradeTarget;
					if (settlementTradeTarget != null)
					{
						Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(settlementTradeTarget, base.PartyToOrder.mobileParty);
					}
					else
					{
						SetPatrolMode();
					}
				}
				else
				{
					SetPatrolMode();
				}
				GiveDefenseOrderIfAttacked();
				break;
			}
			case Mode.ClearHideout:
				if (hideoutTarget == null || !hideoutTarget.IsHideout)
				{
					CurrentMode = Mode.Patrol;
					LogFileManager.WriteErrorLogEntry("Unepected AI behavior. Settlement was not a hideout");
				}
				else if (base.PartyToOrder.mobileParty.CurrentSettlement != null && hideoutTarget != null && base.PartyToOrder.mobileParty.CurrentSettlement == hideoutTarget)
				{
					List<MobileParty> list = new List<MobileParty>();
					foreach (MobileParty item2 in (List<MobileParty>)(object)hideoutTarget.Parties)
					{
						if (item2 != base.PartyToOrder.mobileParty)
						{
							list.Add(item2);
						}
					}
					if (list.Count > 0)
					{
						FieldBattleEventComponent.CreateFieldBattleEvent(base.PartyToOrder.mobileParty.Party, list.First().Party);
						list.RemoveAt(0);
						for (int num = list.Count - 1; num >= 0; num--)
						{
							MapEvent mapEvent = base.PartyToOrder.mobileParty.MapEvent;
							MethodInfo method = ((object)mapEvent).GetType().GetMethod("AddInvolvedPartyInternal", BindingFlags.Instance | BindingFlags.NonPublic);
							method.Invoke(mapEvent, new object[2]
							{
								list[num].Party,
								(object)(BattleSideEnum)0
							});
						}
					}
					else
					{
						hideoutTarget = null;
						CurrentMode = Mode.Patrol;
						if (!base.PartyToOrder.isNPC)
						{
							InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)base.PartyToOrder.mobileParty.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_hideoutcleared}cleared a hideout.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.grey)));
						}
					}
				}
				else
				{
					Main.GarrisonPartyBehavior.SetMoveGoToSettlementHelper(hideoutTarget, base.PartyToOrder.mobileParty);
				}
				break;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private bool SetTradeModeIfNeeded()
	{
		if (CurrentMode == Mode.Patrol && isMobileGarrison)
		{
			if (WantsToSellPrisoners())
			{
				CurrentMode = Mode.Trade;
				base.PartyToOrder.settlementTradeTarget = null;
				return true;
			}
			if (mobileGarrison.homeGarrisonSettings.EnableHorseBuy && mobileGarrison.WantsToBuyHorses() > 0)
			{
				base.PartyToOrder.settlementTradeTarget = base.PartyToOrder.BestNearbyTradeSettlement();
				if (base.PartyToOrder.settlementTradeTarget != null)
				{
					CurrentMode = Mode.Trade;
					return true;
				}
			}
		}
		return false;
	}

	private bool SetMoveEngageNearestHideout()
	{
		try
		{
			Settlement nearestHideoutSettlement = base.PartyToOrder.GetNearestHideoutSettlement(base.PartyToOrder.settlementSightRadius, base.PartyToOrder.mobileParty);
			if (nearestHideoutSettlement != null)
			{
				float num = 0f;
				foreach (MobileParty item in (List<MobileParty>)(object)nearestHideoutSettlement.Parties)
				{
					if (item != base.PartyToOrder.mobileParty)
					{
						if (item.Party != null && item.Party.Owner != null && item.Party.Owner.Clan != null && item.Party.Owner.Clan == base.PartyToOrder.mobileParty.Party.Owner.Clan)
						{
							return false;
						}
						num += item.Party.EstimatedStrength;
					}
				}
				if (num > 0f && base.PartyToOrder.CanDeafeat(num, base.PartyToOrder.mobileParty))
				{
					CurrentMode = Mode.ClearHideout;
					hideoutTarget = nearestHideoutSettlement;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private bool WantsToSellPrisoners()
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		if (isMobileGarrison)
		{
			bool enablePrisonerSell = mobileGarrison.homeGarrisonSettings.EnablePrisonerSell;
			if (mobileGarrison.mobileParty.PrisonRoster != null && enablePrisonerSell)
			{
				int totalManCount = mobileGarrison.mobileParty.PrisonRoster.TotalManCount;
				ExplainedNumber partyPrisonerSizeLimit = Campaign.Current.Models.PartySizeLimitModel.GetPartyPrisonerSizeLimit(mobileGarrison.mobileParty.Party, false);
				int num = (int)((ExplainedNumber)(ref partyPrisonerSizeLimit)).ResultNumber;
				float num2 = Math.Abs(ConfigManager.Instance.Config.GuardPrisonerSellThreshold - 1f);
				if ((float)totalManCount > (float)num * num2)
				{
					return true;
				}
			}
		}
		return false;
	}

	private float GetDistanceToPatrolSettlement()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		return DistanceHelper.FindClosestDistanceFromMobilePartyToSettlement(base.PartyToOrder.mobileParty, MainPatrolSettlement, base.PartyToOrder.mobileParty.NavigationCapability);
	}

	private void DontRunToFar()
	{
		try
		{
			float distanceToPatrolSettlement = GetDistanceToPatrolSettlement();
			if (distanceToPatrolSettlement > 0f && distanceToPatrolSettlement > patrolRadius)
			{
				SetReturnToRegionMode();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void SetReturnToRegionMode()
	{
		CurrentMode = Mode.ReturnToRegion;
	}

	private void SetPatrolMode()
	{
		CurrentMode = Mode.Patrol;
		currentTarget = null;
	}

	private bool SetHealModeIfNeeded()
	{
		try
		{
			if (isMobileGarrison)
			{
				List<Tuple<CharacterObject, int>> allReplenishTroops = mobileGarrison.GetAllReplenishTroops();
				if (allReplenishTroops != null)
				{
					int num = 0;
					int totalManCount = base.PartyToOrder.mobileParty.MemberRoster.TotalManCount;
					foreach (Tuple<CharacterObject, int> item in allReplenishTroops)
					{
						num += item.Item2;
					}
					float guardReplenishPercentage = ConfigManager.Instance.Config.GuardReplenishPercentage;
					float guardAvailableTroopsPercentage = ConfigManager.Instance.Config.GuardAvailableTroopsPercentage;
					if ((float)totalManCount < (float)base.PartyToOrder.InitialSize * guardReplenishPercentage && (float)num > (float)base.PartyToOrder.InitialSize * guardAvailableTroopsPercentage)
					{
						CurrentMode = Mode.Heal;
						return false;
					}
				}
			}
			if (base.PartyToOrder.NeedsHeal())
			{
				CurrentMode = Mode.Heal;
				return false;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	public bool GiveDefenseOrderIfAttacked()
	{
		Settlement val = CheckForBoundedAttack();
		if (val != null)
		{
			base.PartyToOrder.ResetTarget();
			base.PartyToOrder.GiveAndExecuteOrder(new OrderDefense(val));
			base.PartyToOrder.QueueNextOrder(this);
			return true;
		}
		return false;
	}

	protected Settlement CheckForBoundedAttack()
	{
		try
		{
			int num = ((List<Village>)(object)MainPatrolSettlement.BoundVillages).Count + 1;
			if (BoundSettlements.Count == num)
			{
				foreach (Settlement boundSettlement in BoundSettlements)
				{
					if (boundSettlement.IsUnderRaid || boundSettlement.IsUnderSiege)
					{
						return boundSettlement;
					}
				}
				if (ImprovedSettlement != null)
				{
					foreach (Settlement neighbourSettlementsAndVillage in ImprovedSettlement.NeighbourSettlementsAndVillages)
					{
						if (neighbourSettlementsAndVillage.OwnerClan != ImprovedSettlement.Settlement.OwnerClan || (!neighbourSettlementsAndVillage.IsUnderRaid && !neighbourSettlementsAndVillage.IsUnderSiege))
						{
							continue;
						}
						return neighbourSettlementsAndVillage;
					}
				}
			}
			else
			{
				InitializePatrolSettlements();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	private void InitializePatrolSettlements()
	{
		List<Settlement> list = new List<Settlement>();
		List<Settlement> item = new List<Settlement>();
		foreach (Village item2 in (List<Village>)(object)MainPatrolSettlement.BoundVillages)
		{
			list.Add(((SettlementComponent)item2).Settlement);
			if (!BoundSettlements.Contains(((SettlementComponent)item2).Settlement))
			{
				BoundSettlements.Add(((SettlementComponent)item2).Settlement);
			}
		}
		list.Add(MainPatrolSettlement);
		if (!BoundSettlements.Contains(MainPatrolSettlement))
		{
			BoundSettlements.Add(MainPatrolSettlement);
		}
		SettlementsToPatrol = new Tuple<List<Settlement>, List<Settlement>>(list, item);
	}

	private void CalculatePatrolRadius()
	{
		float num = 0f;
		foreach (Settlement boundSettlement in BoundSettlements)
		{
			float num2 = DistanceHelper.FindClosestDistanceFromSettlementToSettlement(MainPatrolSettlement, boundSettlement, (NavigationType)3);
			num = ((num < num2) ? num2 : num);
		}
		num += 30f;
		patrolRadius = num;
	}

	public override string GetStatusText()
	{
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Expected O, but got Unknown
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Expected O, but got Unknown
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Expected O, but got Unknown
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected O, but got Unknown
		//IL_0237: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Expected O, but got Unknown
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Expected O, but got Unknown
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Expected O, but got Unknown
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Expected O, but got Unknown
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Expected O, but got Unknown
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f5: Expected O, but got Unknown
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected O, but got Unknown
		string result = "";
		switch (CurrentMode)
		{
		case Mode.ClearHideout:
		{
			Settlement obj = hideoutTarget;
			result = ((!(((obj != null) ? obj.Name : null) != (TextObject)null)) ? ((object)new TextObject("{=menu_guard_status_isattackinghideout}The guard party is attacking a hideout", (Dictionary<string, object>)null)).ToString() : ((object)new TextObject("{=menu_guard_status_isattacking}The guard party is attacking" + ModuleStrings._space + (object)hideoutTarget.Name, (Dictionary<string, object>)null)).ToString());
			break;
		}
		case Mode.Patrol:
			result = ((settlementTarget == null) ? ((object)new TextObject("{=menu_guard_status_ispatroling}The guard party is patrolling", (Dictionary<string, object>)null)).ToString() : ((!(settlementTarget.EncyclopediaLinkWithName != (TextObject)null)) ? ((object)new TextObject("{=menu_guard_status_ispatrolingaroundThe guard party is patrolling aroundd" + ModuleStrings._space + (object)settlementTarget.Name, (Dictionary<string, object>)null)).ToString() : ((object)new TextObject("{=menu_guard_status_ispatrolingaround}The guard party is patrolling around" + ModuleStrings._space + (object)settlementTarget.EncyclopediaLinkWithName, (Dictionary<string, object>)null)).ToString()));
			break;
		case Mode.PrisonerTurnIn:
			result = ((object)new TextObject("{=menu_guard_status_turningprisoners}The guard party is turning in prisoners", (Dictionary<string, object>)null)).ToString();
			break;
		case Mode.Heal:
			result = ((object)new TextObject("{=menu_guard_status_isreplenishing}The guard party is replenishing", (Dictionary<string, object>)null)).ToString();
			break;
		case Mode.ReturnToRegion:
			result = ((object)new TextObject("{=menu_guard_status_returninghome}The guard party is returning to its home region", (Dictionary<string, object>)null)).ToString();
			break;
		case Mode.Trade:
			result = ((settlementTarget == null) ? ((object)new TextObject("{=menu_guard_status_istrading}The guard party is trading", (Dictionary<string, object>)null)).ToString() : ((!(settlementTarget.EncyclopediaLinkWithName != (TextObject)null)) ? ((object)new TextObject("{=menu_guard_status_istradingwith}The guard party is trading with" + ModuleStrings._space + (object)settlementTarget.Name, (Dictionary<string, object>)null)).ToString() : ((object)new TextObject("{=menu_guard_status_istradingwith}The guard party is trading with" + ModuleStrings._space + (object)settlementTarget.EncyclopediaLinkWithName, (Dictionary<string, object>)null)).ToString()));
			break;
		}
		return result;
	}
}
