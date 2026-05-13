using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Helpers;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.AI.Orders.PartyOrder;
using ImprovedGarrisons.AI.PartyComponent;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.Utils;
using SandBox.GauntletUI.Map;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.Tracker;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.AI.AIManagers;

public class PartyManager
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static Func<FieldInfo, bool> _003C_003E9__10_0;

		public static PartyPresentationDoneButtonConditionDelegate _003C_003E9__22_1;

		public static PartyPresentationDoneButtonConditionDelegate _003C_003E9__23_0;

		internal bool _003CTryGetMapTrackerProvider_003Eb__10_0(FieldInfo f)
		{
			return typeof(MapTrackerProvider).IsAssignableFrom(f.FieldType);
		}

		internal Tuple<bool, TextObject> _003CPromptPartyManagementMenu_003Eb__22_1(TroopRoster leftMemberRoster, TroopRoster leftPrisonRoster, TroopRoster rightMemberRoster, TroopRoster rightPrisonRoster, int leftLimitNum, int rightLimitNum)
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Expected O, but got Unknown
			return new Tuple<bool, TextObject>(item1: true, new TextObject("", (Dictionary<string, object>)null));
		}

		internal Tuple<bool, TextObject> _003CPromptManagementScreenWithActions_003Eb__23_0(TroopRoster leftMembers, TroopRoster leftPrisoners, TroopRoster rightMembers, TroopRoster rightPrisoners, int leftLimit, int rightLimit)
		{
			return new Tuple<bool, TextObject>(item1: true, TextObject.GetEmpty());
		}
	}

	public readonly MobileGarrisonManager mobileGarrisonManagement;

	public readonly VillageRecruitPartyManager villageRecruitPartyManagement;

	public readonly TransferPartyManager transferPartyManagement;

	public readonly GarrisonRecruiterPartyManager garrisonRecruiterPartyManagement;

	public PartyManager()
	{
		mobileGarrisonManagement = new MobileGarrisonManager();
		villageRecruitPartyManagement = new VillageRecruitPartyManager();
		transferPartyManagement = new TransferPartyManager();
		garrisonRecruiterPartyManagement = new GarrisonRecruiterPartyManager();
	}

	public List<MobileParty> GetAllImprovedGarrisonParties()
	{
		List<MobileParty> list = new List<MobileParty>();
		list.AddRange(mobileGarrisonManagement.GetAllMobileGarrisons());
		list.AddRange(villageRecruitPartyManagement.GetAllVillageRecruitParties());
		list.AddRange(transferPartyManagement.GetAllTransferParties());
		list.AddRange(garrisonRecruiterPartyManagement.GetAllRecruiters());
		return list;
	}

	public PartyBase InitializeNewParty(string id, TextObject partyName, Settlement homeSettlement, Settlement spawnOn)
	{
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (homeSettlement != null && homeSettlement.Owner != null)
			{
				Hero owner = homeSettlement.Owner;
				if (owner == null)
				{
					return null;
				}
				PartyBase partyBaseById = GetPartyBaseById(id);
				if (partyBaseById == null)
				{
					ImprovedGarrisonPartyComponent improvedGarrisonPartyComponent = new ImprovedGarrisonPartyComponent(homeSettlement, owner, partyName);
					MobileParty val = MobileParty.CreateParty(id, (PartyComponent)null);
					if (val != null)
					{
						val.Party.SetCustomName(partyName);
						val.SetCustomHomeSettlement(homeSettlement);
						val.Party.SetCustomOwner(owner);
						val.ActualClan = owner.Clan;
						val.Ai.SetInitiative(0.8f, 0.5f, float.MaxValue);
						val.ShouldJoinPlayerBattles = true;
						GivePartyFood(val);
						val.InitializeMobilePartyAroundPosition(TroopRoster.CreateDummyTroopRoster(), TroopRoster.CreateDummyTroopRoster(), spawnOn.GatePosition, 0f, 0f, !spawnOn.GatePosition.IsOnLand);
						val.Party.SetVisualAsDirty();
						val.SetPartyUsedByQuest(true);
					}
					bool flag = villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(val);
					if (ConfigManager.Instance.Config.EnableMapBannerTracker && owner == Hero.MainHero && !flag)
					{
						TrackPartyWithBanner(val);
					}
					else
					{
						UntrackPartyWithBanner(val);
					}
					return val.Party;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public bool TrackAllImprovedGarrisonparties()
	{
		try
		{
			List<MobileParty> allImprovedGarrisonParties = Main.PartyManagement.GetAllImprovedGarrisonParties();
			foreach (MobileParty item in allImprovedGarrisonParties)
			{
				Main.PartyManagement.TrackPartyWithBanner(item);
			}
			return true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public bool UntrackAllImprovedGarrisonparties()
	{
		try
		{
			List<MobileParty> allImprovedGarrisonParties = Main.PartyManagement.GetAllImprovedGarrisonParties();
			foreach (MobileParty item in allImprovedGarrisonParties)
			{
				Main.PartyManagement.UntrackPartyWithBanner(item);
			}
			return true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private bool TryGetMapTrackerCollectionVM(out MapTrackerCollectionVM vm)
	{
		vm = null;
		if (MapScreen.Instance == null)
		{
			return false;
		}
		GauntletMapTrackersView mapView = MapScreen.Instance.GetMapView<GauntletMapTrackersView>();
		if (mapView == null)
		{
			return false;
		}
		FieldInfo field = ((object)mapView).GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
		if (field == null)
		{
			return false;
		}
		object value = field.GetValue(mapView);
		vm = (MapTrackerCollectionVM)((value is MapTrackerCollectionVM) ? value : null);
		return vm != null;
	}

	private bool TryGetMapTrackerProvider(out MapTrackerProvider provider)
	{
		provider = null;
		if (!TryGetMapTrackerCollectionVM(out var vm))
		{
			return false;
		}
		FieldInfo[] fields = ((object)vm).GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo fieldInfo = fields.FirstOrDefault((FieldInfo f) => typeof(MapTrackerProvider).IsAssignableFrom(f.FieldType));
		if (fieldInfo == null)
		{
			return false;
		}
		object value = fieldInfo.GetValue(vm);
		provider = (MapTrackerProvider)((value is MapTrackerProvider) ? value : null);
		return provider != null;
	}

	private bool TryInvokeProviderMethod(MapTrackerProvider provider, string methodName, MobileParty party)
	{
		Type type = ((object)provider).GetType();
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.NonPublic;
		MethodInfo methodInfo = null;
		object obj = null;
		(Type, Func<object>)[] array = new(Type, Func<object>)[3]
		{
			(typeof(MobileParty), () => party),
			(typeof(ITrackableCampaignObject), () => party),
			(typeof(PartyBase), () => party.Party)
		};
		(Type, Func<object>)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(Type, Func<object>) tuple = array2[i];
			methodInfo = type.GetMethod(methodName, bindingAttr, null, new Type[1] { tuple.Item1 }, null);
			if (methodInfo != null)
			{
				obj = tuple.Item2();
				break;
			}
		}
		if (methodInfo == null)
		{
			return false;
		}
		methodInfo.Invoke(provider, new object[1] { obj });
		return true;
	}

	public bool TrackPartyWithBanner(MobileParty party)
	{
		try
		{
			if (party == null)
			{
				return false;
			}
			if (MapScreen.Instance == null || party.ActualClan != Clan.PlayerClan || villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(party))
			{
				return false;
			}
			if (!TryGetMapTrackerProvider(out var provider))
			{
				return false;
			}
			return TryInvokeProviderMethod(provider, "AddIfEligible", party);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	public bool UntrackPartyWithBanner(MobileParty party)
	{
		try
		{
			if (party == null || MapScreen.Instance == null)
			{
				return false;
			}
			if (!TryGetMapTrackerProvider(out var provider))
			{
				return false;
			}
			return TryInvokeProviderMethod(provider, "RemoveIfExists", party);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	public bool IsTrackedWithBanner(MobileParty party)
	{
		try
		{
			if (party == null || MapScreen.Instance == null)
			{
				return false;
			}
			if (!TryGetMapTrackerCollectionVM(out var vm))
			{
				return false;
			}
			MBBindingList<MapTrackerItemVM> trackers = vm.Trackers;
			if (trackers == null)
			{
				return false;
			}
			foreach (MapTrackerItemVM item in (Collection<MapTrackerItemVM>)(object)trackers)
			{
				if (item?.TrackedObject == party)
				{
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public void GivePartyFood(MobileParty party)
	{
		float num = Math.Abs(party.FoodChange * 2f) + 100f;
		int inventoryCapacity = party.InventoryCapacity;
		int num2 = (int)party.TotalWeightCarried;
		int num3 = inventoryCapacity - num2;
		if (num3 < 0)
		{
			return;
		}
		num = ((num > (float)num3) ? ((float)num3) : num);
		foreach (ItemObject item in (List<ItemObject>)(object)Items.All)
		{
			if (item != null && item.IsFood)
			{
				float num4 = num * item.Weight;
				if (num4 > (float)inventoryCapacity)
				{
					num = (float)inventoryCapacity / item.Weight - 1f;
				}
				party.ItemRoster.AddToCounts(item, (int)num);
				if (party.Food >= num)
				{
					party.ItemRoster.UpdateVersion();
					break;
				}
			}
		}
	}

	public void ExecutePartialHourlyAi(MobileParty party)
	{
		if (((MBObjectBase)party).StringId.Contains("mobile") || ((MBObjectBase)party).StringId.Contains("recruiter"))
		{
		}
		garrisonRecruiterPartyManagement.GetRecruiterForParty(party)?.PartialHourlyThinkBehavior();
	}

	public void ExecutePartialHourlyAi()
	{
	}

	public void ExecuteHourlyAi()
	{
		mobileGarrisonManagement.ExecutePartialHourThinkBehavior();
		transferPartyManagement.ExecuteHourThinkBehavior();
		villageRecruitPartyManagement.ExecuteHourThinkBehavior();
		mobileGarrisonManagement.ExecuteHourThinkBehavior();
		garrisonRecruiterPartyManagement.ExecuteHourThinkBehaviorForAll();
	}

	public PartyBase GetPartyBaseById(string id)
	{
		try
		{
			MBReadOnlyList<MobileParty> mobileParties = Campaign.Current.MobileParties;
			PartyBase result = null;
			if (mobileParties != null)
			{
				foreach (MobileParty item in (List<MobileParty>)(object)mobileParties)
				{
					if (string.Equals(((MBObjectBase)item).StringId, id))
					{
						result = item.Party;
						break;
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

	public List<MobileParty> GetAllNearNearbyParties(CampaignVec2 position, float radius, List<MobileParty> blacklist = null)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		LocatableSearchData<MobileParty> val = MobileParty.StartFindingLocatablesAroundPosition(((CampaignVec2)(ref position)).ToVec2(), radius);
		List<MobileParty> list = new List<MobileParty>();
		for (MobileParty val2 = MobileParty.FindNextLocatable(ref val); val2 != null; val2 = MobileParty.FindNextLocatable(ref val))
		{
			if (blacklist != null && !blacklist.Contains(val2))
			{
				list.Add(val2);
			}
		}
		return list;
	}

	public void RecruitMobilePartyToGarrison(MobileParty party, Settlement settlement, TroopRoster blackList = null)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_0379: Unknown result type (might be due to invalid IL or missing references)
		//IL_0383: Expected O, but got Unknown
		//IL_03aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b4: Expected O, but got Unknown
		//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d7: Expected O, but got Unknown
		//IL_0417: Unknown result type (might be due to invalid IL or missing references)
		//IL_0421: Expected O, but got Unknown
		//IL_0448: Unknown result type (might be due to invalid IL or missing references)
		//IL_0452: Expected O, but got Unknown
		//IL_0475: Unknown result type (might be due to invalid IL or missing references)
		//IL_047f: Expected O, but got Unknown
		//IL_04b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c1: Expected O, but got Unknown
		//IL_023a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0244: Expected O, but got Unknown
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0275: Expected O, but got Unknown
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0298: Expected O, but got Unknown
		//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Expected O, but got Unknown
		//IL_0305: Unknown result type (might be due to invalid IL or missing references)
		//IL_030f: Expected O, but got Unknown
		//IL_0323: Unknown result type (might be due to invalid IL or missing references)
		//IL_0328: Unknown result type (might be due to invalid IL or missing references)
		//IL_0332: Expected O, but got Unknown
		try
		{
			int numberOfAllMembers = party.Party.NumberOfAllMembers;
			int num = 0;
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)party.MemberRoster.GetTroopRoster())
			{
				TroopRosterElement current = item;
				int num2 = ((TroopRosterElement)(ref current)).Number;
				if (blackList != null)
				{
					int num3 = blackList.FindIndexOfTroop(current.Character);
					if (num3 >= 0)
					{
						TroopRosterElement elementCopyAtIndex = blackList.GetElementCopyAtIndex(num3);
						int number = ((TroopRosterElement)(ref elementCopyAtIndex)).Number;
						num2 -= number;
						if (num2 <= 0)
						{
							continue;
						}
					}
				}
				int num4 = ((TroopRosterElement)(ref current)).Number - ((TroopRosterElement)(ref current)).WoundedNumber;
				if (num4 < 0)
				{
					num4 = 0;
				}
				num4 = ((num4 - num2 < 0) ? num4 : num2);
				int num5 = ((TroopRosterElement)(ref current)).WoundedNumber;
				if (num4 - num2 < 0)
				{
					num5 = ((num5 + (num4 - num2) < 0) ? num5 : (-(num4 - num2)));
				}
				if (((Fief)settlement.Town).GarrisonParty == null)
				{
					settlement.AddGarrisonParty();
				}
				((Fief)settlement.Town).GarrisonParty.MemberRoster.AddToCounts(current.Character, num4, false, num5, 0, true, -1);
				num += num2;
			}
			if (num > 0 && villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(party) && settlement.Owner != null && settlement.Owner == Hero.MainHero)
			{
				Settlement villageFromMobileParty = villageRecruitPartyManagement.GetVillageFromMobileParty(party);
				Main.ActivityLogManager.AddNewRecruits(settlement.Town, num, villageFromMobileParty);
			}
			bool flag = settlement.Owner != null && settlement.Owner != null && settlement.Owner == Hero.MainHero;
			if (mobileGarrisonManagement.IsMobileGarrisonParty(party) && flag)
			{
				MobileGarrison mobileGarrisonForParty = mobileGarrisonManagement.GetMobileGarrisonForParty(party);
				if (mobileGarrisonForParty != null)
				{
					OrderMergeGarrison orderMergeGarrison = mobileGarrisonForParty.CurrentOrder as OrderMergeGarrison;
					if (orderMergeGarrison != null && orderMergeGarrison.isReturning)
					{
						string text = ((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_return2}have returned to your garrison.", (Dictionary<string, object>)null)).ToString();
						InformationManager.DisplayMessage(new InformationMessage(text.ToString(), Color.FromUint(ModuleColors.yellow)));
						Main.ActivityLogManager.AddPartyMergedWithGarrisonActivity(settlement.Town, mobileGarrisonForParty.mobileParty);
					}
					else if (orderMergeGarrison != null)
					{
						string text2 = ((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_transfer_done}have reached their transfer location.", (Dictionary<string, object>)null)).ToString();
						InformationManager.DisplayMessage(new InformationMessage(text2.ToString(), Color.FromUint(ModuleColors.yellow)));
						Main.ActivityLogManager.AddPartyMergedWithGarrisonActivity(settlement.Town, mobileGarrisonForParty.mobileParty);
					}
				}
			}
			else if (transferPartyManagement.IsTransferParty(party) && flag)
			{
				string text3 = ((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_transfer_donehave reached their transfer location..", (Dictionary<string, object>)null)).ToString();
				InformationManager.DisplayMessage(new InformationMessage(text3.ToString(), Color.FromUint(ModuleColors.yellow)));
				Main.ActivityLogManager.AddPartyMergedWithGarrisonActivity(settlement.Town, party);
			}
			else if (garrisonRecruiterPartyManagement.IsRecruiterParty(party) && flag)
			{
				string text4 = ((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_recruiter_returned1}has returned with", (Dictionary<string, object>)null)).ToString() + " " + numberOfAllMembers + " " + ((object)new TextObject("{=info_recruiter_returned2}troops to", (Dictionary<string, object>)null)).ToString() + " " + (object)settlement.Name;
				InformationManager.DisplayMessage(new InformationMessage(text4.ToString(), Color.FromUint(ModuleColors.yellow)));
				Main.ActivityLogManager.AddNewRecruits(settlement.Town, numberOfAllMembers, null, addActivity: false);
				Main.ActivityLogManager.AddPartyMergedWithGarrisonActivity(settlement.Town, party);
			}
			LeaveSettlementAction.ApplyForParty(party);
			Main.GarrisonPartyBehavior.OnPartyRemoved(party.Party);
			Main.GarrisonPartyBehavior.RemovePartyHelper(party);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptPartyManagementMenu(PartyBase leftParty, MobileParty rightParty)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected O, but got Unknown
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Expected O, but got Unknown
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_019f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Expected O, but got Unknown
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Expected O, but got Unknown
		try
		{
			if (leftParty == null || leftParty.MobileParty == null || rightParty == null || rightParty.Party == null || leftParty.Name == (TextObject)null)
			{
				return;
			}
			PartyScreenLogic val = new PartyScreenLogic();
			PartyScreenLogicInitializationData val2 = default(PartyScreenLogicInitializationData);
			val2.LeftOwnerParty = leftParty;
			val2.RightOwnerParty = rightParty.Party;
			val2.LeftMemberRoster = leftParty.MobileParty.MemberRoster;
			val2.LeftPrisonerRoster = leftParty.PrisonRoster;
			val2.RightMemberRoster = rightParty.MemberRoster;
			val2.RightPrisonerRoster = rightParty.Party.PrisonRoster;
			val2.LeftLeaderHero = leftParty.LeaderHero;
			val2.RightLeaderHero = rightParty.LeaderHero;
			val2.LeftPartyMembersSizeLimit = leftParty.PartySizeLimit;
			val2.LeftPartyPrisonersSizeLimit = leftParty.PrisonerSizeLimit;
			val2.RightPartyMembersSizeLimit = rightParty.Party.PartySizeLimit;
			val2.RightPartyPrisonersSizeLimit = rightParty.Party.PrisonerSizeLimit;
			val2.LeftPartyName = leftParty.Name;
			val2.RightPartyName = rightParty.Name;
			val2.TroopTransferableDelegate = new IsTroopTransferableDelegate(PartyScreenHelper.TroopTransferableDelegate);
			val2.IsDismissMode = false;
			val2.IsTroopUpgradesDisabled = true;
			val2.Header = null;
			val2.TransferHealthiesGetWoundedsFirst = true;
			val2.ShowProgressBar = false;
			val2.MemberTransferState = (TransferState)1;
			val2.PrisonerTransferState = (TransferState)1;
			val2.AccompanyingTransferState = (TransferState)1;
			PartyScreenLogicInitializationData val3 = val2;
			val3.PartyPresentationDoneButtonDelegate = (PartyPresentationDoneButtonDelegate)delegate
			{
				if (mobileGarrisonManagement.IsMobileGarrisonParty(leftParty.MobileParty))
				{
					MobileGarrison mobileGarrisonForParty = mobileGarrisonManagement.GetMobileGarrisonForParty(leftParty.MobileParty);
					if (mobileGarrisonForParty != null)
					{
						mobileGarrisonForParty.InitializeInitialTroopRoster(withReset: true);
						Main.ActivityLogManager.AddPartyCreationActivity(mobileGarrisonForParty.fromSettlement.Town, mobileGarrisonForParty.mobileParty);
						UIManager.Instance.ForceOverviewUpdate();
					}
				}
				else if (garrisonRecruiterPartyManagement.IsRecruiterParty(leftParty.MobileParty))
				{
					GarrisonRecruiter recruiterForParty = garrisonRecruiterPartyManagement.GetRecruiterForParty(leftParty.MobileParty);
					if (recruiterForParty != null)
					{
						UIManager.Instance.ForceFullRefresh();
						recruiterForParty.SetInitialSize();
						Main.ActivityLogManager.AddPartyCreationActivity(recruiterForParty.fromSettlement.Town, recruiterForParty.mobileParty);
						UIManager.Instance.ForceOverviewUpdate();
					}
				}
				if (leftParty.NumberOfAllMembers <= 0)
				{
					Main.GarrisonPartyBehavior.RemovePartyHelper(leftParty.MobileParty);
				}
				return true;
			};
			object obj = _003C_003Ec._003C_003E9__22_1;
			if (obj == null)
			{
				PartyPresentationDoneButtonConditionDelegate val4 = (TroopRoster leftMemberRoster, TroopRoster leftPrisonRoster, TroopRoster rightMemberRoster, TroopRoster rightPrisonRoster, int leftLimitNum, int rightLimitNum) => new Tuple<bool, TextObject>(item1: true, new TextObject("", (Dictionary<string, object>)null));
				_003C_003Ec._003C_003E9__22_1 = val4;
				obj = (object)val4;
			}
			val3.PartyPresentationDoneButtonConditionDelegate = (PartyPresentationDoneButtonConditionDelegate)obj;
			val.Initialize(val3);
			PartyState val5 = Game.Current.GameStateManager.CreateState<PartyState>();
			val5.PartyScreenLogic = val;
			val5.IsDonating = false;
			val5.PartyScreenMode = (PartyScreenMode)5;
			Game.Current.GameStateManager.PushState((GameState)(object)val5, 0);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptManagementScreenWithActions(PartyBase leftParty, MobileParty rightParty, Action<TroopRoster, TroopRoster> doneAction, Action cancelAction)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Expected O, but got Unknown
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Expected O, but got Unknown
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Expected O, but got Unknown
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Unknown result type (might be due to invalid IL or missing references)
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Expected O, but got Unknown
		try
		{
			if (leftParty == null || leftParty.MobileParty == null || rightParty == null || rightParty.Party == null || leftParty.Name == (TextObject)null)
			{
				return;
			}
			PartyScreenLogic val = new PartyScreenLogic();
			PartyScreenLogicInitializationData val2 = default(PartyScreenLogicInitializationData);
			val2.LeftLeaderHero = leftParty.LeaderHero;
			val2.RightLeaderHero = rightParty.LeaderHero;
			val2.LeftMemberRoster = leftParty.MobileParty.MemberRoster;
			val2.RightMemberRoster = rightParty.MemberRoster;
			val2.LeftPrisonerRoster = leftParty.PrisonRoster;
			val2.RightPrisonerRoster = rightParty.Party.PrisonRoster;
			val2.LeftOwnerParty = leftParty;
			val2.RightOwnerParty = rightParty.Party;
			val2.LeftPartyMembersSizeLimit = leftParty.PartySizeLimit;
			val2.RightPartyMembersSizeLimit = rightParty.Party.PartySizeLimit;
			val2.LeftPartyPrisonersSizeLimit = leftParty.PrisonerSizeLimit;
			val2.RightPartyPrisonersSizeLimit = rightParty.Party.PrisonerSizeLimit;
			val2.LeftPartyName = leftParty.Name;
			val2.RightPartyName = rightParty.Name;
			val2.MemberTransferState = (TransferState)1;
			val2.PrisonerTransferState = (TransferState)1;
			val2.AccompanyingTransferState = (TransferState)0;
			val2.TroopTransferableDelegate = new IsTroopTransferableDelegate(PartyScreenHelper.TroopTransferableDelegate);
			val2.PartyScreenMode = (PartyScreenMode)5;
			object obj = _003C_003Ec._003C_003E9__23_0;
			if (obj == null)
			{
				PartyPresentationDoneButtonConditionDelegate val3 = (TroopRoster leftMembers, TroopRoster leftPrisoners, TroopRoster rightMembers, TroopRoster rightPrisoners, int leftLimit, int rightLimit) => new Tuple<bool, TextObject>(item1: true, TextObject.GetEmpty());
				_003C_003Ec._003C_003E9__23_0 = val3;
				obj = (object)val3;
			}
			val2.PartyPresentationDoneButtonConditionDelegate = (PartyPresentationDoneButtonConditionDelegate)obj;
			val2.PartyPresentationDoneButtonDelegate = (PartyPresentationDoneButtonDelegate)delegate(TroopRoster leftMembers, TroopRoster leftPrisoners, TroopRoster rightMembers, TroopRoster rightPrisoners, FlattenedTroopRoster takenPrisoners, FlattenedTroopRoster releasedPrisoners, bool isForced, PartyBase leftOwner, PartyBase rightOwner)
			{
				doneAction?.Invoke(leftMembers, rightMembers);
				return true;
			};
			val2.PartyPresentationCancelButtonActivateDelegate = (PartyPresentationCancelButtonActivateDelegate)delegate
			{
				cancelAction?.Invoke();
				return true;
			};
			PartyScreenLogicInitializationData val4 = val2;
			val.Initialize(val4);
			GameStateManager gameStateManager = Game.Current.GameStateManager;
			PartyState val5 = gameStateManager.CreateState<PartyState>();
			val5.PartyScreenLogic = val;
			val5.IsDonating = false;
			val5.PartyScreenMode = (PartyScreenMode)5;
			gameStateManager.PushState((GameState)(object)val5, 0);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public Dictionary<CharacterObject, int> CompareTwoRosters(TroopRoster initial, TroopRoster after)
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (initial == null || after == null)
			{
				return null;
			}
			Dictionary<CharacterObject, int> dictionary = new Dictionary<CharacterObject, int>();
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)after.GetTroopRoster())
			{
				TroopRosterElement current = item;
				if (initial.Contains(current.Character))
				{
					int num = initial.FindIndexOfTroop(current.Character);
					int elementNumber = initial.GetElementNumber(num);
					int num2 = ((TroopRosterElement)(ref current)).Number - elementNumber;
					if (num2 != 0)
					{
						dictionary.Add(current.Character, num2);
					}
				}
				else
				{
					dictionary.Add(current.Character, ((TroopRosterElement)(ref current)).Number);
				}
			}
			return dictionary;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
	}

	public int GetPartySizeLimit(PartyBase party)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			ExplainedNumber partyMemberSizeLimit = Campaign.Current.Models.PartySizeLimitModel.GetPartyMemberSizeLimit(party, false);
			float resultNumber = ((ExplainedNumber)(ref partyMemberSizeLimit)).ResultNumber;
			return (int)resultNumber;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return 0;
		}
	}

	public TroopRoster CopyTroopRoster(TroopRoster toCopy, PartyBase ownerParty)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (toCopy == null)
			{
				return null;
			}
			TroopRoster val = new TroopRoster(ownerParty);
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)toCopy.GetTroopRoster())
			{
				TroopRosterElement current = item;
				val.AddToCounts(current.Character, ((TroopRosterElement)(ref current)).Number, false, 0, 0, false, -1);
			}
			return val;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
	}
}
