using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.AI.Orders.PartyOrder;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AITypes;

public class MobileGarrison : BoundedParty
{
	private TroopRoster InitialTroopRoster;

	private TroopRoster _tempRosterBeforeFight;

	public readonly bool ShortLife = false;

	public Settlement settlementTarget;

	public MobileGarrison(MobileParty party, Settlement home, bool shortlife = false)
		: base(party, home)
	{
		ShortLife = shortlife;
	}

	public void PartialHourlyThinkBehavior()
	{
		try
		{
			if (!base.homeGarrisonSettings.GuardEnableUpgradeTroops)
			{
				DontUpgradeTroops();
			}
			CheckIfPlayerIsPrisonerInParty();
			StopIfPlayerTarget();
			CheckIfPrisonersIsAboveThreshold();
			RemoveItemsIfOverburdened();
			GiveFoodIfNeeded();
			if (!base.homeGarrisonSettings.GuardEnablePrisonerRecruitment)
			{
				ResetToInitialRosterAfterFight();
			}
			if (mobileParty.Party.LeaderHero != null)
			{
				int num = 0;
			}
			bool flag = false;
			if (mobileParty.Ai.AiBehaviorPartyBase != null && mobileParty.Ai.AiBehaviorPartyBase.MobileParty != null && (mobileParty.Ai.AiBehaviorPartyBase.MobileParty.IsCaravan || mobileParty.Ai.AiBehaviorPartyBase.MobileParty.IsVillager || mobileParty.Ai.AiBehaviorPartyBase.MobileParty.IsCurrentlyUsedByAQuest))
			{
				ResetTarget();
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
			if (base.CurrentOrder == null)
			{
				GiveAndExecuteOrder(new OrderPatrol(fromSettlement));
			}
			base.HourlyThinkBehavior();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void RemoveMobileGarrison(bool forceRemove)
	{
		try
		{
			GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(fromSettlement.Town);
			townSettings.InitialTroopRoster = null;
			Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(mobileParty);
			string text = null;
			if (mobileGarrisonHome != null)
			{
				text = ((MBObjectBase)mobileGarrisonHome).StringId;
			}
			else
			{
				foreach (KeyValuePair<string, MobileGarrison> mobileGarrison in Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons)
				{
					if (mobileGarrison.Value == this)
					{
						text = mobileGarrison.Key;
						break;
					}
				}
				if (text == null)
				{
					return;
				}
			}
			Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.Remove(text);
			if (forceRemove)
			{
				Main.GarrisonPartyBehavior.RemovePartyHelper(mobileParty);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void InitializeInitialTroopRoster(bool withReset = false)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (base.ownerHero == null || base.ownerHero.PartyBelongedTo == null)
			{
				return;
			}
			GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(fromSettlement.Town);
			if (townSettings.InitialTroopRoster != null && !withReset)
			{
				InitialTroopRoster = new TroopRoster(base.ownerHero.PartyBelongedTo.Party);
				foreach (Tuple<string, int> item in townSettings.InitialTroopRoster)
				{
					CharacterObject @object = MBObjectManager.Instance.GetObject<CharacterObject>(item.Item1);
					if (@object != null)
					{
						InitialTroopRoster.AddToCounts(@object, item.Item2, false, 0, 0, true, -1);
					}
				}
			}
			else
			{
				InitialTroopRoster = Main.PartyManagement.CopyTroopRoster(mobileParty.MemberRoster, base.ownerHero.PartyBelongedTo.Party);
				if (InitialTroopRoster == null)
				{
					return;
				}
				townSettings.InitialTroopRoster = new List<Tuple<string, int>>();
				foreach (TroopRosterElement item2 in (List<TroopRosterElement>)(object)InitialTroopRoster.GetTroopRoster())
				{
					TroopRosterElement current2 = item2;
					townSettings.InitialTroopRoster.Add(new Tuple<string, int>(((MBObjectBase)current2.Character).StringId, ((TroopRosterElement)(ref current2)).Number));
				}
			}
			if (InitialTroopRoster == null)
			{
				base.InitialSize = InitialTroopRoster.TotalManCount;
			}
			else
			{
				base.InitialSize = mobileParty.MemberRoster.TotalManCount;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void DontRecruitPrisoners()
	{
		if (mobileParty.PrisonRoster == null)
		{
		}
	}

	private void CheckIfPrisonersIsAboveThreshold()
	{
		if (mobileParty.PrisonRoster != null)
		{
			int prisonerSizeLimit = mobileParty.Party.PrisonerSizeLimit;
			int totalManCount = mobileParty.PrisonRoster.TotalManCount;
			int num = totalManCount - prisonerSizeLimit;
			if (num > 0)
			{
				mobileParty.PrisonRoster.RemoveNumberOfNonHeroTroopsRandomly(num);
			}
		}
	}

	private void DontUpgradeTroops()
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)mobileParty.MemberRoster.GetTroopRoster())
		{
			int num = mobileParty.MemberRoster.FindIndexOfTroop(item.Character);
			mobileParty.MemberRoster.SetElementXp(num, 0);
		}
		foreach (TroopRosterElement item2 in (List<TroopRosterElement>)(object)mobileParty.PrisonRoster.GetTroopRoster())
		{
			int num2 = mobileParty.PrisonRoster.FindIndexOfTroop(item2.Character);
			mobileParty.PrisonRoster.SetElementXp(num2, 0);
		}
	}

	public void SaveRosterBeforeFight()
	{
		if (mobileParty.MapEvent != null && _tempRosterBeforeFight == null)
		{
			_tempRosterBeforeFight = Main.PartyManagement.CopyTroopRoster(mobileParty.MemberRoster, mobileParty.Party);
		}
	}

	private void ResetToInitialRosterAfterFight()
	{
		//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		if (mobileParty.MapEvent != null || _tempRosterBeforeFight == null)
		{
			return;
		}
		Dictionary<CharacterObject, int> dictionary = Main.PartyManagement.CompareTwoRosters(_tempRosterBeforeFight, mobileParty.MemberRoster);
		List<KeyValuePair<CharacterObject, int>> list = dictionary.ToList();
		List<KeyValuePair<CharacterObject, int>> list2 = dictionary.ToList();
		list.RemoveAll((KeyValuePair<CharacterObject, int> pair) => pair.Value > 0);
		list2.RemoveAll((KeyValuePair<CharacterObject, int> pair) => pair.Value < 0);
		if (dictionary != null)
		{
			foreach (KeyValuePair<CharacterObject, int> item in list.ToList())
			{
				foreach (KeyValuePair<CharacterObject, int> item2 in list2.ToList())
				{
					if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item.Key, item2.Key))
					{
						if (!base.homeGarrisonSettings.GuardEnableUpgradeTroops)
						{
							mobileParty.MemberRoster.RemoveTroop(item2.Key, item2.Value, default(UniqueTroopDescriptor), 0);
							mobileParty.MemberRoster.AddToCounts(item.Key, item2.Value, false, 0, 0, true, -1);
						}
						list2.Remove(item2);
					}
					list.Remove(item);
				}
			}
			foreach (KeyValuePair<CharacterObject, int> item3 in list2)
			{
				mobileParty.MemberRoster.RemoveTroop(item3.Key, item3.Value, default(UniqueTroopDescriptor), 0);
			}
		}
		_tempRosterBeforeFight = null;
	}

	private void CheckIfPlayerIsPrisonerInParty()
	{
		if (Hero.MainHero.IsPrisoner && Hero.MainHero.PartyBelongedToAsPrisoner == mobileParty.Party && MobileParty.MainParty != null)
		{
			if (targetsWithTimer.ContainsKey(MobileParty.MainParty))
			{
				targetsWithTimer[MobileParty.MainParty] = 0;
			}
			else
			{
				targetsWithTimer.Add(MobileParty.MainParty, 0);
			}
		}
	}

	private void StopIfPlayerTarget()
	{
		if (Hero.MainHero.PartyBelongedTo != null && !isNPC)
		{
			MobileParty targetParty = Hero.MainHero.PartyBelongedTo.TargetParty;
			if (targetParty != null && targetParty == mobileParty)
			{
				QueueNextOrder(base.CurrentOrder);
				GiveAndExecuteOrder(new OrderStopIfPlayerTarget());
			}
		}
	}

	public void SetReturnMode()
	{
		GiveAndExecuteOrder(new OrderMergeGarrison(fromSettlement, isReturning: true));
		if (mobileParty.CurrentSettlement != null && mobileParty.CurrentSettlement == fromSettlement)
		{
			Main.PartyManagement.RecruitMobilePartyToGarrison(mobileParty, fromSettlement);
		}
	}

	public void SetFortifyMode(Settlement fortify)
	{
		GiveAndExecuteOrder(new OrderMergeGarrison(fortify));
	}

	public void removeIfNoUnits()
	{
		try
		{
			if (mobileParty.MemberRoster.TotalManCount <= 0)
			{
				Main.GarrisonPartyBehavior.RemovePartyHelper(mobileParty);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public MobileParty getMobileParty()
	{
		return mobileParty;
	}

	private void WarnAboutIntruderArmy()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Expected O, but got Unknown
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Expected O, but got Unknown
		List<MobileParty> allNearNearbyParties = Main.PartyManagement.GetAllNearNearbyParties(mobileParty.Position, base.partySightRadius);
		foreach (MobileParty item in allNearNearbyParties)
		{
			float estimatedStrength = mobileParty.Party.EstimatedStrength;
			int totalManCount = item.MemberRoster.TotalManCount;
			float estimatedStrength2 = item.Party.EstimatedStrength;
			if (item.Army != null)
			{
				estimatedStrength2 = item.Army.EstimatedStrength;
			}
			if (estimatedStrength < estimatedStrength2)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)mobileParty.Name).ToString() + ModuleStrings._space + "reports a large party of size" + ModuleStrings._space + totalManCount + ModuleStrings._space + "in your region of" + ModuleStrings._space + ((object)fromSettlement.Name).ToString()));
			}
		}
	}

	public void CheckForReturn()
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected O, but got Unknown
		try
		{
			int totalManCount = mobileParty.MemberRoster.TotalManCount;
			if ((float)base.InitialSize * base.homeGarrisonSettings.GuardReturnPercentage + 1f >= (float)totalManCount)
			{
				SetReturnMode();
				if (!isNPC)
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)mobileParty.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_returnafterthreshold}size has fallen below the threshold. The guard party is now returning.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.grey)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public List<Tuple<CharacterObject, int>> GetAllReplenishTroops()
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (InitialTroopRoster == null)
			{
				InitializeInitialTroopRoster();
				return null;
			}
			List<Tuple<CharacterObject, int>> list = new List<Tuple<CharacterObject, int>>();
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)InitialTroopRoster.GetTroopRoster())
			{
				if (((Fief)fromSettlement.Town).GarrisonParty == null || ((Fief)fromSettlement.Town).GarrisonParty.MemberRoster == null)
				{
					return null;
				}
				int num = ((Fief)fromSettlement.Town).GarrisonParty.MemberRoster.FindIndexOfTroop(item.Character);
				int troopCount = InitialTroopRoster.GetTroopCount(item.Character);
				int troopCount2 = mobileParty.MemberRoster.GetTroopCount(item.Character);
				if (num >= 0)
				{
					int troopCount3 = ((Fief)fromSettlement.Town).GarrisonParty.MemberRoster.GetTroopCount(item.Character);
					int num2 = troopCount - troopCount2;
					if (num2 > 0 && num2 > troopCount3)
					{
						num2 = troopCount3;
					}
					if (num2 > 0)
					{
						list.Add(new Tuple<CharacterObject, int>(item.Character, num2));
					}
				}
			}
			return list;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
	}

	public bool IsValidAndActive()
	{
		try
		{
			if (mobileParty == null || mobileParty.Party == null || mobileParty.MemberRoster == null || mobileParty.Party.NumberOfAllMembers <= 0 || !mobileParty.IsActive)
			{
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public string GetStatusText()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		if (base.CurrentOrder == null)
		{
			return ((object)new TextObject("{=menu_guard_status_iswaiting}The guard party is waiting", (Dictionary<string, object>)null)).ToString();
		}
		return base.CurrentOrder.GetStatusText();
	}
}
