using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ImprovedGarrisons.AI.AITypes;

public abstract class ImprovedAi
{
	private Dictionary<string, Tuple<int, int>> hourCounters = new Dictionary<string, Tuple<int, int>>();

	public bool RethinkNextHour { get; set; }

	public int HourCounter { get; set; } = 0;


	protected List<Settlement> GetAllNearbySettlements(float radius, MobileParty mobileParty)
	{
		List<Settlement> source = new List<Settlement>();
		NavigationType val = default(NavigationType);
		float num = default(float);
		bool flag = default(bool);
		foreach (Settlement item in (List<Settlement>)(object)Settlement.All)
		{
			AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(mobileParty, item, mobileParty.HasNavalNavigationCapability, ref val, ref num, ref flag);
			if (num <= radius)
			{
				source.Append(item);
			}
		}
		return source.ToList();
	}

	protected bool SettlementIsAValidTradePartner(Settlement settlement, IFaction faction, bool includeVillages = false)
	{
		if (settlement == null)
		{
			return false;
		}
		bool flag = includeVillages && settlement.IsVillage;
		return (settlement.IsTown || flag) && !settlement.IsUnderSiege && !settlement.IsRaided && !settlement.IsUnderRaid && !FactionManager.IsAtWarAgainstFaction(faction, settlement.MapFaction);
	}

	public Settlement GetNearestHideoutSettlement(float radius, MobileParty mobileParty)
	{
		try
		{
			List<Settlement> allNearbySettlements = GetAllNearbySettlements(radius, mobileParty);
			float num = float.MaxValue;
			Settlement result = null;
			NavigationType val = default(NavigationType);
			float num2 = default(float);
			bool flag = default(bool);
			foreach (Settlement item in allNearbySettlements)
			{
				if (item.IsHideout && item.Hideout.IsInfested)
				{
					AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(mobileParty, item, mobileParty.HasNavalNavigationCapability, ref val, ref num2, ref flag);
					if (num2 < num)
					{
						num = num2;
						result = item;
					}
				}
			}
			return result;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
	}

	public MobileParty GetBestNearestHostileParty(float partySightRadius, MobileParty mobileParty)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		List<MobileParty> list = new List<MobileParty>();
		list.Add(mobileParty);
		List<MobileParty> allNearNearbyParties = Main.PartyManagement.GetAllNearNearbyParties(mobileParty.Position, partySightRadius, list);
		Tuple<MobileParty, float, float, int, float> tuple = new Tuple<MobileParty, float, float, int, float>(null, 0f, 0f, 0, 0f);
		NavigationType val = default(NavigationType);
		float item = default(float);
		foreach (MobileParty item2 in allNearNearbyParties)
		{
			if (MobilePartyEngageIsAllowed(item2, mobileParty))
			{
				bool flag = false;
				float partyStrength = GetPartyStrength(item2);
				AiHelper.GetBestNavigationTypeAndDistanceOfMobilePartyForMobileParty(mobileParty, item2, ref val, ref item);
				int numberOfAllMembers = item2.Party.NumberOfAllMembers;
				float speed = item2.Speed;
				bool flag2 = CanDeafeat(partyStrength, mobileParty);
				bool flag3 = (double)(speed - mobileParty.Speed) < 0.3;
				if (flag2)
				{
					bool flag4 = tuple.Item2 < partyStrength;
					bool flag5 = tuple.Item4 < numberOfAllMembers;
					bool flag6 = tuple.Item5 < speed;
					flag = flag5 || flag4;
				}
				if (flag)
				{
					tuple = new Tuple<MobileParty, float, float, int, float>(item2, partyStrength, item, numberOfAllMembers, speed);
				}
			}
		}
		return tuple.Item1;
	}

	public bool CanDeafeat(float partyStrength, MobileParty mobileParty)
	{
		return partyStrength - mobileParty.Party.EstimatedStrength < -1f;
	}

	public float GetPartyStrength(MobileParty party)
	{
		float result = 0f;
		if (party != null && party.Party != null)
		{
			float estimatedStrength = party.Party.EstimatedStrength;
			if (party.Army != null)
			{
				estimatedStrength = party.Army.EstimatedStrength;
			}
			result = estimatedStrength;
		}
		return result;
	}

	public bool MobilePartyEngageIsAllowed(MobileParty party, MobileParty ownerParty)
	{
		if (party != null && party != ownerParty)
		{
			bool flag = party.IsActive && party.CurrentSettlement == null;
			bool flag2 = party.IsVillager || party.IsCaravan;
			bool flag3 = FactionManager.IsAtWarAgainstFaction(party.MapFaction, ownerParty.MapFaction);
			return !flag2 && flag3 && flag;
		}
		return false;
	}

	public void AddHourCounter(string id, int hoursLasting)
	{
		if (hourCounters.ContainsKey(id))
		{
			ResetCounter(id);
		}
		else
		{
			hourCounters.Add(id, new Tuple<int, int>(hoursLasting, hoursLasting));
		}
	}

	public void RemoveHourCounter(string id)
	{
		hourCounters.Remove(id);
	}

	public void ResetCounter(string id)
	{
		if (hourCounters.TryGetValue(id, out var value))
		{
			hourCounters[id] = new Tuple<int, int>(value.Item1, value.Item1);
		}
	}

	public int GetCurrentHourTimeOfCounter(string id)
	{
		if (hourCounters.TryGetValue(id, out var value))
		{
			return value.Item2;
		}
		return 0;
	}

	public virtual void NextHour()
	{
		RethinkNextHour = false;
		foreach (KeyValuePair<string, Tuple<int, int>> item in hourCounters.ToList())
		{
			if (item.Value.Item2 <= 0)
			{
				hourCounters.Remove(item.Key);
			}
			else
			{
				hourCounters[item.Key] = new Tuple<int, int>(item.Value.Item1, item.Value.Item2 - 1);
			}
		}
	}

	public abstract void HourlyThinkBehavior();
}
