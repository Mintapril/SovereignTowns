using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.AI.Orders.PartyOrder;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AIManagers;

public class MobileGarrisonManager
{
	public readonly string _mobileGarrisonPartyID = "mobilegarrison_";

	internal Dictionary<string, MobileGarrison> MobileGarrisons { get; private set; } = new Dictionary<string, MobileGarrison>();


	public List<MobileParty> GetAllMobileGarrisons()
	{
		List<MobileParty> list = new List<MobileParty>();
		foreach (MobileGarrison value in MobileGarrisons.Values)
		{
			list.Add(value.mobileParty);
		}
		return list;
	}

	public void ExecutePartialHourThinkBehavior()
	{
		try
		{
			if (MobileGarrisons == null)
			{
				return;
			}
			foreach (MobileGarrison item in MobileGarrisons.Values.ToList())
			{
				item.PartialHourlyThinkBehavior();
			}
		}
		catch (Exception)
		{
		}
	}

	public void ExecuteHourThinkBehavior()
	{
		try
		{
			if (MobileGarrisons == null)
			{
				return;
			}
			foreach (MobileGarrison item in MobileGarrisons.Values.ToList())
			{
				item.NextHour();
				item.HourlyThinkBehavior();
				item.RethinkNextHour = true;
			}
		}
		catch (Exception)
		{
		}
	}

	public void RemoveNotValidMobileGarrisons()
	{
		foreach (KeyValuePair<string, MobileGarrison> item in MobileGarrisons.ToList())
		{
			if (!item.Value.IsValidAndActive())
			{
				MobileGarrisons.Remove(item.Key);
			}
		}
	}

	public Settlement GetMobileGarrisonHome(MobileParty party)
	{
		int num = ((MBObjectBase)party).StringId.IndexOf(_mobileGarrisonPartyID);
		if (num < 0)
		{
			return null;
		}
		if (((MBObjectBase)party).StringId.Contains(ModuleStrings.newOwnerString))
		{
			return null;
		}
		string text = ((MBObjectBase)party).StringId.Substring(num, ((MBObjectBase)party).StringId.Length - num).Replace(_mobileGarrisonPartyID, "");
		int num2 = text.IndexOf('_');
		if (num2 > 0)
		{
			text = text.Substring(0, text.IndexOf('_'));
		}
		text = Regex.Replace(text, "[\\d-]", string.Empty);
		return Main.GarrisonBehavior.GetSettlementFromName(text);
	}

	public MobileGarrison GetMobileGarrisonForParty(MobileParty party)
	{
		try
		{
			if (party == null)
			{
				return null;
			}
			foreach (KeyValuePair<string, MobileGarrison> mobileGarrison in MobileGarrisons)
			{
				if (((MBObjectBase)mobileGarrison.Value.getMobileParty()).StringId == ((MBObjectBase)party).StringId)
				{
					return mobileGarrison.Value;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	internal MobileGarrison GiveMobilePartyAMobileGarrison(MobileParty party)
	{
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Invalid comparison between Unknown and I4
		try
		{
			if (party != null && IsMobileGarrisonParty(party))
			{
				if (Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(party) != null)
				{
					Main.GarrisonPartyBehavior.RemovePartyHelper(party);
					return null;
				}
				Settlement mobileGarrisonHome = GetMobileGarrisonHome(party);
				if (mobileGarrisonHome == null)
				{
					Main.GarrisonPartyBehavior.RemovePartyHelper(party);
					return null;
				}
				MobileGarrison mobileGarrison = new MobileGarrison(party, mobileGarrisonHome);
				party.SetCustomHomeSettlement(mobileGarrison.fromSettlement);
				GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(mobileGarrison.fromSettlement.Town);
				MobileGarrison value;
				bool flag = MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrison.fromSettlement).StringId, out value);
				party.ShouldJoinPlayerBattles = true;
				if (value != null && mobileGarrison != value)
				{
					((MBObjectBase)party).StringId = "IGParty_should_be_removed";
					Main.GarrisonPartyBehavior.RemovePartyHelper(party);
					return null;
				}
				if (value != null)
				{
					mobileGarrison = value;
				}
				else if (value == null)
				{
					MobileGarrisons.Add(((MBObjectBase)mobileGarrison.fromSettlement).StringId, mobileGarrison);
				}
				if (mobileGarrison.fromSettlement.Owner != null && mobileGarrison.fromSettlement.Owner != Hero.MainHero)
				{
					mobileGarrison.isNPC = true;
				}
				if ((int)party.ShortTermBehavior == 14)
				{
					mobileGarrison.GiveAndExecuteOrder(new OrderEscort(party.Ai.AiBehaviorPartyBase.MobileParty));
				}
				return mobileGarrison;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public PartyBase CreateMobileGarrison(Settlement forSettlement, Settlement spawnSettlement, bool npc = false, bool shortlived = false)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		try
		{
			string text = ((object)forSettlement.Name).ToString();
			string id = _mobileGarrisonPartyID + text;
			TextObject partyName = new TextObject(((object)new TextObject("{=party_guards_name}Garrison guards of", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + text, (Dictionary<string, object>)null);
			PartyBase val = Main.PartyManagement.InitializeNewParty(id, partyName, forSettlement, spawnSettlement);
			if (val != null)
			{
				MobileGarrison mobileGarrison = new MobileGarrison(val.MobileParty, forSettlement, shortlived);
				MobileGarrisons.Add(((MBObjectBase)forSettlement).StringId, mobileGarrison);
				if (npc)
				{
					mobileGarrison.isNPC = true;
				}
			}
			return val;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public PartyBase TryCreateMobileGarrisonByStrength(Settlement forSettlement, float strength, bool npc = false, bool shortLived = false)
	{
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_031c: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (forSettlement == null || forSettlement.IsVillage || forSettlement.IsHideout || forSettlement.Town == null || ((Fief)forSettlement.Town).GarrisonParty == null)
			{
				return null;
			}
			List<Tuple<CharacterObject, int>> list = new List<Tuple<CharacterObject, int>>();
			List<TroopRosterElement> list2 = ((IEnumerable<TroopRosterElement>)((Fief)forSettlement.Town).GarrisonParty.MemberRoster.GetTroopRoster()).OrderByDescending((TroopRosterElement x) => x.Character.Tier).ToList();
			float num = 0f;
			foreach (TroopRosterElement item in list2)
			{
				TroopRosterElement current = item;
				num += Campaign.Current.Models.MilitaryPowerModel.GetDefaultTroopPower(current.Character) * (float)((TroopRosterElement)(ref current)).Number;
			}
			double num2 = 0.275;
			double num3 = 0.45;
			double num4 = 0.275;
			int numberOfAllMembers = ((Fief)forSettlement.Town).GarrisonParty.Party.NumberOfAllMembers;
			int num5 = (int)((double)numberOfAllMembers * num2);
			int num6 = (int)((double)numberOfAllMembers * num3) + 1;
			int num7 = (int)((double)numberOfAllMembers * num4);
			if (num > strength)
			{
				PartyBase val = CreateMobileGarrison(forSettlement, forSettlement, npc, shortLived);
				float num8 = strength;
				if (val != null)
				{
					foreach (TroopRosterElement item2 in list2)
					{
						TroopRosterElement current2 = item2;
						CharacterObject character = current2.Character;
						int number = ((TroopRosterElement)(ref current2)).Number;
						int num9 = 0;
						if (((BasicCharacterObject)character).IsHero)
						{
							continue;
						}
						if (num8 <= 0f)
						{
							break;
						}
						if (((BasicCharacterObject)character).IsRanged && !((BasicCharacterObject)character).IsMounted && num5 > 0)
						{
							num9 = ((num5 - number <= 0) ? num5 : number);
							num5 -= num9;
						}
						else if (((BasicCharacterObject)character).IsInfantry && !((BasicCharacterObject)character).IsMounted && num6 > 0)
						{
							num9 = ((num6 - number <= 0) ? num6 : number);
							num6 -= num9;
						}
						else
						{
							if (!((BasicCharacterObject)character).IsMounted || num7 <= 0)
							{
								continue;
							}
							num9 = ((num7 - number <= 0) ? num7 : number);
							num7 -= num9;
						}
						float num10 = Campaign.Current.Models.MilitaryPowerModel.GetDefaultTroopPower(current2.Character) * (float)num9;
						num8 -= num10;
						list.Add(new Tuple<CharacterObject, int>(current2.Character, num9));
					}
					foreach (TroopRosterElement item3 in (List<TroopRosterElement>)(object)((Fief)forSettlement.Town).GarrisonParty.MemberRoster.GetTroopRoster())
					{
						TroopRosterElement current3 = item3;
						if (num8 <= 0f)
						{
							break;
						}
						CharacterObject character2 = current3.Character;
						int number2 = ((TroopRosterElement)(ref current3)).Number;
						float num11 = Campaign.Current.Models.MilitaryPowerModel.GetDefaultTroopPower(current3.Character) * (float)((TroopRosterElement)(ref current3)).Number;
						num8 -= num11;
						list.Add(new Tuple<CharacterObject, int>(current3.Character, number2));
					}
					Main.GarrisonPartyBehavior.TransferTroopsFromPartyToParty(((Fief)forSettlement.Town).GarrisonParty, list, val);
				}
				return val;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public PartyBase CreateMobileGarrisonWithUnits(Settlement forSettlement, int amountOfUnits, bool npc = false)
	{
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0248: Unknown result type (might be due to invalid IL or missing references)
		//IL_024d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_028f: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (forSettlement == null || forSettlement.IsVillage || forSettlement.IsUnderRaid || forSettlement.IsUnderSiege || forSettlement.IsHideout || forSettlement.Town == null || ((Fief)forSettlement.Town).GarrisonParty == null || ((Fief)forSettlement.Town).GarrisonParty.Party.NumberOfAllMembers <= amountOfUnits)
			{
				return null;
			}
			PartyBase val = CreateMobileGarrison(forSettlement, forSettlement, npc);
			if (val != null)
			{
				List<Tuple<CharacterObject, int>> list = new List<Tuple<CharacterObject, int>>();
				double num = 0.275;
				double num2 = 0.45;
				double num3 = 0.275;
				int num4 = (int)((double)amountOfUnits * num);
				int num5 = (int)((double)amountOfUnits * num2) + 1;
				int num6 = (int)((double)amountOfUnits * num3);
				List<TroopRosterElement> list2 = ((IEnumerable<TroopRosterElement>)((Fief)forSettlement.Town).GarrisonParty.MemberRoster.GetTroopRoster()).OrderByDescending((TroopRosterElement x) => x.Character.Tier).ToList();
				foreach (TroopRosterElement item in list2)
				{
					TroopRosterElement current = item;
					CharacterObject character = current.Character;
					int number = ((TroopRosterElement)(ref current)).Number;
					int num7 = 0;
					if (((BasicCharacterObject)character).IsHero)
					{
						continue;
					}
					if (((BasicCharacterObject)character).IsRanged && !((BasicCharacterObject)character).IsMounted && num4 > 0)
					{
						num7 = ((num4 - number <= 0) ? num4 : number);
						num4 -= num7;
					}
					else if (((BasicCharacterObject)character).IsInfantry && !((BasicCharacterObject)character).IsMounted && num5 > 0)
					{
						num7 = ((num5 - number <= 0) ? num5 : number);
						num5 -= num7;
					}
					else
					{
						if (!((BasicCharacterObject)character).IsMounted || num6 <= 0)
						{
							continue;
						}
						num7 = ((num6 - number <= 0) ? num6 : number);
						num6 -= num7;
					}
					list.Add(new Tuple<CharacterObject, int>(current.Character, num7));
				}
				int num8 = num4 + num5 + num6;
				foreach (TroopRosterElement item2 in (List<TroopRosterElement>)(object)((Fief)forSettlement.Town).GarrisonParty.MemberRoster.GetTroopRoster())
				{
					TroopRosterElement current2 = item2;
					if (num8 <= 0)
					{
						break;
					}
					CharacterObject character2 = current2.Character;
					int number2 = ((TroopRosterElement)(ref current2)).Number;
					int num9 = 0;
					num9 = ((num8 - number2 <= 0) ? num8 : number2);
					num8 -= num9;
					list.Add(new Tuple<CharacterObject, int>(current2.Character, num9));
				}
				Main.GarrisonPartyBehavior.TransferTroopsFromPartyToParty(((Fief)forSettlement.Town).GarrisonParty, list, val);
			}
			return val;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public bool IsMobileGarrisonParty(MobileParty party)
	{
		if (party != null && ((MBObjectBase)party).StringId != null)
		{
			return ((MBObjectBase)party).StringId.Contains(_mobileGarrisonPartyID);
		}
		return false;
	}

	public MobileGarrison GetMobileGarrisonPartyOfSettlement(Settlement settlement)
	{
		if (MobileGarrisons.TryGetValue(((MBObjectBase)settlement).StringId, out var value))
		{
			return value;
		}
		return null;
	}
}
