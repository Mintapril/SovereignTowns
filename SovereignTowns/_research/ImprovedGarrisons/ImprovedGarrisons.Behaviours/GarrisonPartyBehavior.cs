using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Helpers;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.AI.Orders.PartyOrder;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;
using ImprovedGarrisons.Utils;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.Behaviours;

public class GarrisonPartyBehavior : CampaignBehaviorBase
{
	private bool trackersInitialized = false;

	private MobileGarrison _currentMobileGarrisonForFortification;

	private static MethodInfo _removePartyMethodInfo;

	public override void RegisterEvents()
	{
		CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener((object)this, (Action<CampaignGameStarter>)OnGameOpen);
		CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener((object)this, (Action<CampaignGameStarter>)OnAfterGameOpened);
		CampaignEvents.AfterSettlementEntered.AddNonSerializedListener((object)this, (Action<MobileParty, Settlement, Hero>)OnPartyEnteredSettlement);
		CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener((object)this, (Action<MobileParty>)PartyPartialHourlyAi);
		CampaignEvents.HourlyTickEvent.AddNonSerializedListener((object)this, (Action)PartyHourlyAi);
		CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener((object)this, (Action<MobileParty>)PartyDailyAi);
		CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener((object)this, (Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementDetail>)OnSettlementOwnerChanged);
		CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener((object)this, (Action<PartyBase>)OnPartyRemoved);
		CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener((object)this, (Action<MobileParty, PartyBase>)OnPartyDestroyed);
		CampaignEvents.MapEventStarted.AddNonSerializedListener((object)this, (Action<MapEvent, PartyBase, PartyBase>)OnMapEventStarted);
		CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener((object)this, (Action<MobileParty, PartyThinkParams>)AiHourlyTickEvent);
	}

	public override void SyncData(IDataStore dataStore)
	{
	}

	private void AiHourlyTickEvent(MobileParty party, PartyThinkParams para)
	{
	}

	private void OnGameOpen(CampaignGameStarter campaignGameStarter)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		try
		{
			Main.GarrisonBehavior.OnGameOpen(campaignGameStarter);
			AddDialog(campaignGameStarter);
			if (ConfigManager.Instance.Config.ActivateDeleteAllModsPartiesMode)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=misc_deletepartiesmode}Delete all Improved Garrisons parties mode has been activated and will now be executed.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.modMainColor)));
				OnGameStartDeleteAllIGParties();
			}
			else
			{
				OnGameStartSetAllIGParties();
			}
			if (!ConfigManager.Instance.Config.EnableMapBannerTracker)
			{
				return;
			}
			Action action = delegate
			{
				if (MapScreen.Instance != null)
				{
					Main.PartyManagement.TrackAllImprovedGarrisonparties();
					trackersInitialized = true;
					Main.RemoveActionToExecuteEachTick("tryTrackAllParties");
				}
			};
			Main.AddActionToExecuteEachTick("tryTrackAllParties", action);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnGameTick(float tick)
	{
	}

	private void OnAfterGameOpened(CampaignGameStarter campaignGameStarter)
	{
		try
		{
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PartyPartialHourlyAi(MobileParty party)
	{
		try
		{
			Main.PartyManagement.ExecutePartialHourlyAi(party);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PartyPartialHourlyAi()
	{
		try
		{
			Main.PartyManagement.ExecutePartialHourlyAi();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PartyHourlyAi()
	{
		try
		{
			Main.PartyManagement.ExecuteHourlyAi();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PartyDailyAi(MobileParty party)
	{
	}

	public void OnPartyDestroyed(MobileParty party, PartyBase partyBase)
	{
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Expected O, but got Unknown
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Expected O, but got Unknown
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Expected O, but got Unknown
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Expected O, but got Unknown
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_0207: Expected O, but got Unknown
		//IL_0223: Unknown result type (might be due to invalid IL or missing references)
		//IL_0228: Unknown result type (might be due to invalid IL or missing references)
		//IL_0232: Expected O, but got Unknown
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Expected O, but got Unknown
		try
		{
			if (party == null || !(party.Name != (TextObject)null) || Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(party))
			{
				return;
			}
			if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(party))
			{
				if (Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(party) == null || Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(party).OwnerClan == Hero.MainHero.Clan)
				{
					TextObject val = new TextObject("{=party_destroyed_new}Your" + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=party_destroyed_new2}have been destroyed.", (Dictionary<string, object>)null)).ToString(), (Dictionary<string, object>)null);
					MBInformationManager.AddQuickInformation(val, 0, (BasicCharacterObject)null, (Equipment)null, "");
					InformationManager.DisplayMessage(new InformationMessage(((object)val).ToString(), Color.FromUint(ModuleColors.yellow)));
				}
			}
			else if (Main.PartyManagement.transferPartyManagement.IsTransferParty(party))
			{
				TextObject val2 = new TextObject("{=party_destroyed_new}Your" + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=party_destroyed_new2}have been destroyed.", (Dictionary<string, object>)null)).ToString(), (Dictionary<string, object>)null);
				MBInformationManager.AddQuickInformation(val2, 0, (BasicCharacterObject)null, (Equipment)null, "");
				InformationManager.DisplayMessage(new InformationMessage(((object)val2).ToString(), Color.FromUint(ModuleColors.yellow)));
			}
			else if (Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(party))
			{
				TextObject val3 = new TextObject("{=party_destroyed_new}Your" + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=party_destroyed_new2}have been destroyed.", (Dictionary<string, object>)null)).ToString(), (Dictionary<string, object>)null);
				MBInformationManager.AddQuickInformation(val3, 0, (BasicCharacterObject)null, (Equipment)null, "");
				InformationManager.DisplayMessage(new InformationMessage(((object)val3).ToString(), Color.FromUint(ModuleColors.yellow)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void OnPartyRemoved(PartyBase party)
	{
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f8: Expected O, but got Unknown
		try
		{
			if (party == null || party.MobileParty == null)
			{
				return;
			}
			if (Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(party.MobileParty))
			{
				Main.PartyManagement.villageRecruitPartyManagement.VillageRecruitParties.Remove(party.MobileParty);
			}
			else if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(party.MobileParty))
			{
				List<MobileGarrison> list = new List<MobileGarrison>();
				foreach (KeyValuePair<string, MobileGarrison> mobileGarrison in Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons)
				{
					if (mobileGarrison.Value.getMobileParty() == party.MobileParty)
					{
						list.Add(mobileGarrison.Value);
					}
				}
				foreach (MobileGarrison item in list)
				{
					item.RemoveMobileGarrison(forceRemove: false);
				}
				Main.PartyManagement.UntrackPartyWithBanner(party.MobileParty);
			}
			else if (Main.PartyManagement.transferPartyManagement.IsTransferParty(party.MobileParty) && Main.PartyManagement.transferPartyManagement.TransferParties.ContainsKey(party.MobileParty))
			{
				Main.PartyManagement.transferPartyManagement.TransferParties.Remove(party.MobileParty);
				Main.PartyManagement.UntrackPartyWithBanner(party.MobileParty);
			}
			else if (Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(party.MobileParty))
			{
				Main.PartyManagement.garrisonRecruiterPartyManagement.GarrisonRecruiterParties.Remove(party.MobileParty);
				Main.PartyManagement.UntrackPartyWithBanner(party.MobileParty);
			}
		}
		catch (Exception ex)
		{
			InformationManager.DisplayMessage(new InformationMessage("Improved Garrisons: Could not remove party please restart the game. See moduledata/errorlog.xml for more."));
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnPartyEnteredSettlement(MobileParty party, Settlement settlement, Hero hero)
	{
		try
		{
			bool flag = party != null;
			bool flag2 = settlement != null;
			if (!(flag && flag2))
			{
				return;
			}
			bool flag3 = ((MBObjectBase)party).StringId != null;
			bool flag4 = settlement.IsTown || settlement.IsCastle;
			bool flag5 = party.HomeSettlement == settlement;
			bool flag6 = party == MobileParty.MainParty;
			if (flag4 && flag6 && settlement.Town.OwnerClan == Hero.MainHero.Clan)
			{
				UIManager.Instance.improvedGarrisonsUI.ChangeSelectorSelectionToCurrentSettlement();
				if (!ConfigManager.Instance.Config.DeactivateTutorial)
				{
					UIManager.Instance.StartTutorial();
				}
			}
			bool flag7 = Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(party);
			if (!(flag3 && flag4 && flag7))
			{
				return;
			}
			Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(party);
			if (mobileGarrisonHome != null)
			{
				if (Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrisonHome).StringId, out var value))
				{
					if (value.CurrentOrder is OrderMergeGarrison)
					{
						value.SellItems(settlement, onlyNecessary: false);
						Main.PartyManagement.RecruitMobilePartyToGarrison(party, settlement);
						SellPrisoners(party, settlement.Town);
					}
					OrderPatrol orderPatrol = value.CurrentOrder as OrderPatrol;
					if (orderPatrol != null && orderPatrol.CurrentMode == OrderPatrol.Mode.PrisonerTurnIn)
					{
						PutPrisonersIntoDungeon(party, settlement.Town);
					}
					if (orderPatrol != null && orderPatrol.CurrentMode == OrderPatrol.Mode.Trade)
					{
						bool enablePrisonerSell = value.homeGarrisonSettings.EnablePrisonerSell;
						bool enableHorseBuy = value.homeGarrisonSettings.EnableHorseBuy;
						value.ExecuteTrade(enablePrisonerSell, sellItems: true, enableHorseBuy);
					}
				}
			}
			else
			{
				Main.PartyManagement.RecruitMobilePartyToGarrison(party, settlement);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnSettlementOwnerChanged(Settlement settlement, bool b, Hero x, Hero y, Hero z, ChangeOwnerOfSettlementDetail details)
	{
		try
		{
			if (settlement == null || x == null || x.Clan == null || y == null || y.Clan == null || x.Clan == y.Clan)
			{
				return;
			}
			List<MobileParty> list = new List<MobileParty>();
			foreach (KeyValuePair<MobileParty, Hero> transferParty in Main.PartyManagement.transferPartyManagement.TransferParties)
			{
				if (transferParty.Key.HomeSettlement == null || transferParty.Key.HomeSettlement != settlement)
				{
					continue;
				}
				if (transferParty.Value != null)
				{
					Settlement bestGarrisonToReturnTo = GetBestGarrisonToReturnTo(transferParty.Key);
					transferParty.Key.SetCustomHomeSettlement(bestGarrisonToReturnTo);
					if (bestGarrisonToReturnTo != null)
					{
						if (!TrySetPartyMoveToSettlement(transferParty.Key, bestGarrisonToReturnTo))
						{
							list.Add(transferParty.Key);
						}
					}
					else
					{
						list.Add(transferParty.Key);
					}
				}
				else
				{
					list.Add(transferParty.Key);
				}
			}
			foreach (KeyValuePair<string, MobileGarrison> item in Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.ToList())
			{
				if (item.Value.fromSettlement != null && item.Value.fromSettlement == settlement)
				{
					Settlement bestGarrisonToReturnTo2 = GetBestGarrisonToReturnTo(item.Value.mobileParty);
					if (bestGarrisonToReturnTo2 != null)
					{
						item.Value.fromSettlement = bestGarrisonToReturnTo2;
						MobileParty mobileParty = item.Value.mobileParty;
						((MBObjectBase)mobileParty).StringId = ((MBObjectBase)mobileParty).StringId + "_returning";
						item.Value.SetReturnMode();
					}
					else
					{
						list.Add(item.Value.mobileParty);
					}
				}
			}
			foreach (KeyValuePair<MobileParty, GarrisonRecruiter> garrisonRecruiterParty in Main.PartyManagement.garrisonRecruiterPartyManagement.GarrisonRecruiterParties)
			{
				if (garrisonRecruiterParty.Key.HomeSettlement != null && garrisonRecruiterParty.Key.HomeSettlement == settlement)
				{
					Settlement bestGarrisonToReturnTo3 = GetBestGarrisonToReturnTo(garrisonRecruiterParty.Value.mobileParty);
					if (bestGarrisonToReturnTo3 != null)
					{
						garrisonRecruiterParty.Value.fromSettlement = bestGarrisonToReturnTo3;
						MobileParty mobileParty2 = garrisonRecruiterParty.Value.mobileParty;
						((MBObjectBase)mobileParty2).StringId = ((MBObjectBase)mobileParty2).StringId + "_returning";
						garrisonRecruiterParty.Value.SetReturnMode();
					}
					else
					{
						list.Add(garrisonRecruiterParty.Value.mobileParty);
					}
				}
			}
			foreach (MobileParty item2 in list)
			{
				Main.GarrisonPartyBehavior.RemovePartyHelper(item2);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnMapEventStarted(MapEvent mapEvent, PartyBase leftParty, PartyBase rightParty)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Invalid comparison between Unknown and I4
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (mapEvent == null || leftParty == null || rightParty == null || PartyBase.MainParty == null)
			{
				return;
			}
			if (mapEvent.IsPlayerMapEvent)
			{
				PartyBase mainParty = PartyBase.MainParty;
				Clan clan = mainParty.Owner.Clan;
				BattleSideEnum playerSide = mapEvent.PlayerSide;
				MapEventSide val = (((int)playerSide != 1) ? mapEvent.DefenderSide : mapEvent.AttackerSide);
				List<MobileParty> allNearNearbyParties = Main.PartyManagement.GetAllNearNearbyParties(mapEvent.Position, 5f);
				{
					foreach (MobileParty item in allNearNearbyParties)
					{
						if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(item) && item.Party.Owner != null && item.Party.Owner.Clan == clan)
						{
							MethodInfo method = ((object)val).GetType().GetMethod("AddNearbyPartyToPlayerMapEvent", BindingFlags.Instance | BindingFlags.NonPublic);
							method.Invoke(val, new object[1] { item });
						}
					}
					return;
				}
			}
			MobileParty val2 = null;
			if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(leftParty.MobileParty))
			{
				val2 = leftParty.MobileParty;
			}
			else if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(rightParty.MobileParty))
			{
				val2 = rightParty.MobileParty;
			}
			if (val2 != null)
			{
				Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(val2)?.SaveRosterBeforeFight();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SellPrisoners(MobileParty party, Town town)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		try
		{
			if (party.PrisonRoster != null && party.PrisonRoster.Count > 0)
			{
				SellPrisonersAction.ApplyForAllPrisoners(party.Party, ((SettlementComponent)town).Settlement.Party);
				MobileGarrison mobileGarrisonForParty = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(party);
				if (mobileGarrisonForParty != null && !mobileGarrisonForParty.isNPC)
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_sell}sold its prisoners.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.grey)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PutPrisonersIntoDungeon(MobileParty party, Town town)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0175: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Expected O, but got Unknown
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Expected O, but got Unknown
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Expected O, but got Unknown
		//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f7: Expected O, but got Unknown
		try
		{
			int num = 0;
			List<TroopRosterElement> list = new List<TroopRosterElement>();
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)party.PrisonRoster.GetTroopRoster())
			{
				TroopRosterElement current = item;
				if (!((BasicCharacterObject)current.Character).IsHero)
				{
					int num2 = ((TroopRosterElement)(ref current)).Number - ((TroopRosterElement)(ref current)).WoundedNumber;
					if (num2 < 0)
					{
						num2 = 0;
					}
					((SettlementComponent)town).Settlement.Party.AddPrisoner(current.Character, num2);
					list.Add(current);
					num += num2 + ((TroopRosterElement)(ref current)).WoundedNumber;
				}
			}
			foreach (TroopRosterElement item2 in list)
			{
				TroopRosterElement current2 = item2;
				if (((BasicCharacterObject)current2.Character).IsHero)
				{
					TransferPrisonerAction.Apply(current2.Character, party.Party, ((SettlementComponent)town).Settlement.Party);
					num++;
				}
				else
				{
					party.PrisonRoster.RemoveTroop(current2.Character, ((TroopRosterElement)(ref current2)).Number, default(UniqueTroopDescriptor), 0);
				}
			}
			if (((SettlementComponent)town).Owner != null && ((SettlementComponent)town).Owner.Owner != null && ((SettlementComponent)town).Owner.Owner == Hero.MainHero)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=menu_your}Your", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + ((object)party.Name).ToString() + ModuleStrings._space + ((object)new TextObject("{=info_guards_prisoners_put}put", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + num + ModuleStrings._space + ((object)new TextObject("{=info_guards_prisoners_intodungeon}prisoners into the dungeon.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public bool TransferTroopsFromPartyToParty(MobileParty party, List<Tuple<CharacterObject, int>> troops, PartyBase partyToTransferTo)
	{
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (party != null && partyToTransferTo != null)
			{
				foreach (Tuple<CharacterObject, int> troop in troops)
				{
					int num = troop.Item2;
					CharacterObject item = troop.Item1;
					int troopCount = party.MemberRoster.GetTroopCount(item);
					if (troopCount == 0)
					{
						return false;
					}
					if (troopCount - num < 0)
					{
						num = troopCount;
					}
					partyToTransferTo.MobileParty.MemberRoster.AddToCounts(item, num, false, 0, 0, true, -1);
					party.MemberRoster.RemoveTroop(item, num, default(UniqueTroopDescriptor), 0);
				}
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	public void OnGameStartDeleteAllIGParties()
	{
		try
		{
			if (Campaign.Current != null)
			{
				foreach (MobileParty item in ((IEnumerable<MobileParty>)Campaign.Current.MobileParties).ToList())
				{
					if (Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(item))
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(item);
					}
					else if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(item))
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(item);
					}
					else if (Main.PartyManagement.transferPartyManagement.IsTransferParty(item))
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(item);
					}
					else if (Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(item))
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(item);
					}
				}
			}
			ConfigManager.Instance.Config.ActivateDeleteAllModsPartiesMode = false;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ReturnAllIGParties()
	{
		try
		{
			if (Campaign.Current == null)
			{
				return;
			}
			foreach (MobileParty item in ((IEnumerable<MobileParty>)Campaign.Current.MobileParties).ToList())
			{
				if (Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(item))
				{
					continue;
				}
				if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(item))
				{
					MobileGarrison mobileGarrisonForParty = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(item);
					if (mobileGarrisonForParty != null)
					{
						mobileGarrisonForParty.SetReturnMode();
					}
					else
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(item);
					}
				}
				else if (!Main.PartyManagement.transferPartyManagement.IsTransferParty(item) && Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(item))
				{
					GarrisonRecruiter recruiterForParty = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterForParty(item);
					if (recruiterForParty != null)
					{
						recruiterForParty.SetReturnMode();
					}
					else
					{
						Main.GarrisonPartyBehavior.RemovePartyHelper(item);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnGameStartSetAllIGParties()
	{
		try
		{
			List<MobileParty> list = new List<MobileParty>();
			List<MobileParty> list2 = new List<MobileParty>();
			if (Campaign.Current != null)
			{
				foreach (MobileParty item in (List<MobileParty>)(object)Campaign.Current.MobileParties)
				{
					if (Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(item))
					{
						Main.PartyManagement.villageRecruitPartyManagement.VillageRecruitParties.Add(item);
						SetPartyOwner(item);
					}
					else if (Main.PartyManagement.mobileGarrisonManagement.IsMobileGarrisonParty(item))
					{
						list.Add(item);
						SetPartyOwner(item);
					}
					else if (Main.PartyManagement.transferPartyManagement.IsTransferParty(item))
					{
						Main.PartyManagement.transferPartyManagement.TransferParties.Add(item, item.Party.Owner);
						SetPartyOwner(item);
					}
					else if (Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(item))
					{
						list2.Add(item);
						SetPartyOwner(item);
					}
				}
			}
			foreach (MobileParty item2 in list)
			{
				Main.PartyManagement.mobileGarrisonManagement.GiveMobilePartyAMobileGarrison(item2);
				item2.SetPartyUsedByQuest(true);
			}
			foreach (MobileParty item3 in list2)
			{
				Main.PartyManagement.garrisonRecruiterPartyManagement.GiveMobilePartyARecruiter(item3);
				item3.SetPartyUsedByQuest(true);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void SetPartyOwner(MobileParty party)
	{
		if (party.Party.Owner == null)
		{
			if (party.HomeSettlement != null && party.HomeSettlement.Owner != null)
			{
				party.Party.SetCustomOwner(party.HomeSettlement.Owner);
			}
			else
			{
				Main.GarrisonPartyBehavior.RemovePartyHelper(party);
			}
		}
	}

	private void AddDialog(CampaignGameStarter starter)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected O, but got Unknown
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Expected O, but got Unknown
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Expected O, but got Unknown
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Expected O, but got Unknown
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Expected O, but got Unknown
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Expected O, but got Unknown
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Expected O, but got Unknown
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Expected O, but got Unknown
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Expected O, but got Unknown
		//IL_01c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Expected O, but got Unknown
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f5: Expected O, but got Unknown
		//IL_01fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_020b: Expected O, but got Unknown
		//IL_0222: Unknown result type (might be due to invalid IL or missing references)
		//IL_022c: Expected O, but got Unknown
		//IL_024d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0257: Expected O, but got Unknown
		//IL_025f: Unknown result type (might be due to invalid IL or missing references)
		//IL_026d: Expected O, but got Unknown
		//IL_0284: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Expected O, but got Unknown
		//IL_02af: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b9: Expected O, but got Unknown
		//IL_02c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cf: Expected O, but got Unknown
		//IL_02e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f0: Expected O, but got Unknown
		//IL_0311: Unknown result type (might be due to invalid IL or missing references)
		//IL_031b: Expected O, but got Unknown
		//IL_0323: Unknown result type (might be due to invalid IL or missing references)
		//IL_0331: Expected O, but got Unknown
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_0352: Expected O, but got Unknown
		//IL_035a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0368: Expected O, but got Unknown
		//IL_037f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0389: Expected O, but got Unknown
		//IL_0390: Unknown result type (might be due to invalid IL or missing references)
		//IL_039e: Expected O, but got Unknown
		//IL_03b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bf: Expected O, but got Unknown
		//IL_03c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e0: Expected O, but got Unknown
		//IL_03e0: Expected O, but got Unknown
		//IL_03f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0401: Expected O, but got Unknown
		//IL_0408: Unknown result type (might be due to invalid IL or missing references)
		//IL_0414: Unknown result type (might be due to invalid IL or missing references)
		//IL_0422: Expected O, but got Unknown
		//IL_0422: Expected O, but got Unknown
		//IL_0439: Unknown result type (might be due to invalid IL or missing references)
		//IL_0443: Expected O, but got Unknown
		//IL_044a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0458: Expected O, but got Unknown
		//IL_046f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0479: Expected O, but got Unknown
		//IL_0480: Unknown result type (might be due to invalid IL or missing references)
		//IL_048c: Unknown result type (might be due to invalid IL or missing references)
		//IL_049a: Expected O, but got Unknown
		//IL_049a: Expected O, but got Unknown
		//IL_04b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04bb: Expected O, but got Unknown
		//IL_04c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d0: Expected O, but got Unknown
		//IL_04e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f1: Expected O, but got Unknown
		//IL_0512: Unknown result type (might be due to invalid IL or missing references)
		//IL_051c: Expected O, but got Unknown
		//IL_0524: Unknown result type (might be due to invalid IL or missing references)
		//IL_0532: Expected O, but got Unknown
		//IL_0549: Unknown result type (might be due to invalid IL or missing references)
		//IL_0553: Expected O, but got Unknown
		//IL_0574: Unknown result type (might be due to invalid IL or missing references)
		//IL_057e: Expected O, but got Unknown
		//IL_0586: Unknown result type (might be due to invalid IL or missing references)
		//IL_0594: Expected O, but got Unknown
		//IL_05ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b5: Expected O, but got Unknown
		//IL_05d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e0: Expected O, but got Unknown
		//IL_05e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_05f6: Expected O, but got Unknown
		//IL_060d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0617: Expected O, but got Unknown
		//IL_061f: Unknown result type (might be due to invalid IL or missing references)
		//IL_062d: Expected O, but got Unknown
		try
		{
			starter.AddDialogLine("improvedgarrison_recruit_talk_start", "start", "improvedgarrison_recruit_talk", ((object)new TextObject("{=dialog_recruit_start}Greetings, my Lord, we are the new garrison recruits heading to your settlement!", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(ImprovedGarrison_recruit_talk_start_on_condition), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddDialogLine("improvedgarrison_recruit_talk_start", "start", "improvedgarrison_recruit_talk", ((object)new TextObject("{=dialog_recruit_neutral_start}Greetings, we are the new recruits, we will help keeping our home safe!", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(ImprovedGarrison_recruit_talk_start_on_neutral_condition), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_recruit_talk_leave", "improvedgarrison_recruit_talk", "close_window", ((object)new TextObject("{=dialog_end_nice}Carry on, then. Farewell.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_recruit_leave_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_transferparty_talk_start", "start", "improvedgarrison_transferparty_talk", ((object)new TextObject("{=dialog_transfer_start}Greetings, my Lord, we are the garrison transfer party!", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(ImprovedGarrison_transferparty_talk_start_on_condition), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_transferparty_talk_leave", "improvedgarrison_transferparty_talk", "close_window", ((object)new TextObject("{=dialog_end_nice}Carry on, then. Farewell.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_transferparty_leave_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_mobilegarrison_talk_start", "start", "improvedgarrison_mobilegarrison_talk", ((object)new TextObject("{=dialog_guard_start}Greetings, my Lord! How can we be of service?", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(ImprovedGarrison_mobilegarrison_talk_start_on_condition), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddDialogLine("improvedgarrison_mobilegarrison_pretalk_start", "improvedgarrison_mobilegarrison_inspect_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_pretalk}It's a pleasure serving you, my Lord.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_mobilegarrison_talk_inspect", "improvedgarrison_mobilegarrison_talk", "improvedgarrison_mobilegarrison_inspect_pretalk", ((object)new TextObject("{=dialog_guard_inspect}Let me inspect your troops.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_inspect_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_mobilegarrison_pretalk_start", "improvedgarrison_mobilegarrison_transfer_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_fortify_answer}Very well, we will reinforce this location.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_mobilegarrison_talk_transfer", "improvedgarrison_mobilegarrison_talk", "improvedgarrison_mobilegarrison_transfer_pretalk", ((object)new TextObject("{=dialog_guard_fortify}Reinforce a garrison.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_fortify_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_mobilegarrison_pretalk_start", "improvedgarrison_mobilegarrison_return_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_return_answer}Very well, we will return home at once, my Lord!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_mobilegarrison_talk_return", "improvedgarrison_mobilegarrison_talk", "improvedgarrison_mobilegarrison_return_pretalk", ((object)new TextObject("{=dialog_guard_return}Return to your garrison.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_return_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_mobilegarrison_pretalk_start", "improvedgarrison_mobilegarrison_escort_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_escort_answer}Very well, we will protect you with our life, my Lord!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_mobilegarrison_talk_escort", "improvedgarrison_mobilegarrison_talk", "improvedgarrison_mobilegarrison_escort_pretalk", ((object)new TextObject("{=dialog_guard_escort}Fight by my side!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_escort_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_mobilegarrison_pretalk_start", "improvedgarrison_mobilegarrison_patrol_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_patrol_answer}Very well, we will protect your lands, my Lord!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_mobilegarrison_talk_patrol", "improvedgarrison_mobilegarrison_talk", "improvedgarrison_mobilegarrison_patrol_pretalk", ((object)new TextObject("{=dialog_guard_patrol}Patrol this region!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_patrol_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_mobilegarrison_talk_leave", "improvedgarrison_mobilegarrison_talk", "close_window", ((object)new TextObject("{=dialog_end_nice}Carry on, then. Farewell.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_leave_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_npcmobilegarrison_talk_start_nice", "start", "improvedgarrison_npcmobilegarrison_talk", ((object)new TextObject("{=dialog_npcguard_neutral_start}Greetings! We are the guards patrolling this region.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)(() => IsNPCMobileGarrison() && !IsHostileParty()), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_npcmobilegarrison_talk_leave", "improvedgarrison_npcmobilegarrison_talk", "close_window", ((object)new TextObject("{=dialog_npcguard_hostile_fight}Surrender or die!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)(() => IsNPCMobileGarrison() && !IsHostileParty()), new OnConsequenceDelegate(conversation_fight_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_npcmobilegarrison_talk_leave", "improvedgarrison_npcmobilegarrison_talk", "close_window", ((object)new TextObject("{=dialog_end_niceCarry on, then. Farewell..", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)(() => IsNPCMobileGarrison() && !IsHostileParty()), new OnConsequenceDelegate(Conversation_improvedgarrison_mobilegarrison_leave_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_npcmobilegarrison_talk_start_hostile", "start", "improvedgarrison_npcmobilegarrison_talk", ((object)new TextObject("{=dialog_npcguard_hostile_startplayer}We are the guards patrolling this region.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)(() => IsNPCMobileGarrison() && IsHostileParty() && PlayerHasEngaged()), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_npcmobilegarrison_talk_start_hostile_notplayer", "start", "close_window", ((object)new TextObject("{=dialog_npcguard_hostile_startnpc}We are the guards patrolling this region. You know we are at war! Trespassing is not allowed here.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)(() => IsNPCMobileGarrison() && IsHostileParty() && !PlayerHasEngaged()), new OnConsequenceDelegate(conversation_fight_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_recruiter_talk_start", "start", "improvedgarrison_recruiter_talk", ((object)new TextObject("{=dialog_recruiter_start}Greetings, my Lord, we are your garrison recruiters!", (Dictionary<string, object>)null)).ToString(), new OnConditionDelegate(ImprovedGarrison_recruiter_talk_start_on_condition), (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddDialogLine("improvedgarrison_recruiter_pretalk_start", "improvedgarrison_recruiter_continue_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_pretalkIt's a pleasure serving you, my Lord..", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_recruiter_talk_inspect", "improvedgarrison_recruiter_talk", "improvedgarrison_recruiter_continue_pretalk", ((object)new TextObject("{=dialog_recruiter_inspect}Let me inspect your troops.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_recruiter_inspect_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_recruiter_pretalk_start", "improvedgarrison_recruiter_changeculture_pretalk", "improvedgarrison_recruiter_talk", ((object)new TextObject("{=dialog_recruiter_culturechange_answer}Very well, we will change our recruitment to another culture!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_recruiter_talk_changeculture", "improvedgarrison_recruiter_talk", "improvedgarrison_recruiter_changeculture_pretalk", ((object)new TextObject("{=dialog_recruiter_culturechange}Change your recruitment culture.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_recruiter_changeCulture_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddDialogLine("improvedgarrison_recruiter_pretalk_start", "improvedgarrison_recruiter_return_pretalk", "close_window", ((object)new TextObject("{=dialog_guard_return_anVery well, we will return home at once, my Lord!lord!", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, (OnConsequenceDelegate)null, 100, (OnClickableConditionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_recruiter_talk_return", "improvedgarrison_recruiter_talk", "improvedgarrison_recruiter_return_pretalk", ((object)new TextObject("{=dialog_guard_return}Return to your garrison.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_recruiter_return_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
			starter.AddPlayerLine("improvedgarrison_recruiter_talk_leave", "improvedgarrison_recruiter_talk", "close_window", ((object)new TextObject("{=dialog_end_nice}Carry on, then. Farewell.", (Dictionary<string, object>)null)).ToString(), (OnConditionDelegate)null, new OnConsequenceDelegate(Conversation_improvedgarrison_recruiter_leave_on_consequence), 100, (OnClickableConditionDelegate)null, (OnPersuasionOptionDelegate)null);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void conversation_fight_on_consequence()
	{
		BeHostileAction.ApplyEncounterHostileAction(PartyBase.MainParty, MobileParty.ConversationParty.Party);
	}

	private bool TryToLeaveCondition()
	{
		try
		{
			return PlayerEncounter.EncounteredParty != null && PlayerEncounter.EncounteredParty.CalculateCurrentStrength() <= PartyBase.MainParty.CalculateCurrentStrength();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private bool PlayerHasEngaged()
	{
		try
		{
			return PlayerEncounter.PlayerIsAttacker;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private bool IsHostileParty()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null)
			{
				bool flag = ((MBObjectBase)encounteredParty.MobileParty).IsInitialized && encounteredParty.MobileParty.IsMoving;
				bool flag2 = encounteredParty.MapFaction.IsAtWarWith(MobileParty.MainParty.MapFaction);
				return flag2 && flag;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private bool IsStuckInMainParty()
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			MobileParty mainParty = MobileParty.MainParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null && mainParty != null && encounteredParty.MobileParty.GetPosition2D == mainParty.GetPosition2D)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private bool IsNPCMobileGarrison()
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Invalid comparison between Unknown and I4
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null)
			{
				MobileGarrison mobileGarrisonForParty = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(encounteredParty.MobileParty);
				if (mobileGarrisonForParty != null && PlayerEncounter.Current != null && (int)Campaign.Current.CurrentConversationContext == 3 && encounteredParty.IsMobile && mobileGarrisonForParty.isNPC)
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

	private void Conversation_improvedgarrison_recruiter_changeCulture_on_consequence()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null)
			{
				GarrisonRecruiter recruiterForParty = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterForParty(encounteredParty.MobileParty);
				if (recruiterForParty != null)
				{
					RecruitmentSettings.Instance.PromptChangeRecruitmentCulture(recruiterForParty.fromSettlement.Town);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private bool ImprovedGarrison_recruit_talk_start_on_condition()
	{
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Invalid comparison between Unknown and I4
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null)
			{
				bool flag = Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(encounteredParty.MobileParty);
				bool flag2 = encounteredParty.MobileParty.ActualClan == Hero.MainHero.Clan;
				if (flag && PlayerEncounter.Current != null && (int)Campaign.Current.CurrentConversationContext == 3 && encounteredParty.IsMobile && flag2)
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

	private bool ImprovedGarrison_recruit_talk_start_on_neutral_condition()
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Invalid comparison between Unknown and I4
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null)
			{
				bool flag = Main.PartyManagement.villageRecruitPartyManagement.IsImprovedGarrisonVillageRecruitParty(encounteredParty.MobileParty);
				bool flag2 = encounteredParty.MobileParty.ActualClan != Hero.MainHero.Clan;
				if (flag && PlayerEncounter.Current != null && (int)Campaign.Current.CurrentConversationContext == 3 && encounteredParty.IsMobile && flag2)
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

	private void Conversation_improvedgarrison_recruit_leave_on_consequence()
	{
		PlayerEncounter.LeaveEncounter = true;
	}

	private bool ImprovedGarrison_transferparty_talk_start_on_condition()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Invalid comparison between Unknown and I4
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null && Main.PartyManagement.transferPartyManagement.IsTransferParty(encounteredParty.MobileParty) && PlayerEncounter.Current != null && (int)Campaign.Current.CurrentConversationContext == 3 && encounteredParty.IsMobile)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private void Conversation_improvedgarrison_transferparty_leave_on_consequence()
	{
		PlayerEncounter.LeaveEncounter = true;
	}

	private bool ImprovedGarrison_recruiter_talk_start_on_condition()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Invalid comparison between Unknown and I4
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null && Main.PartyManagement.garrisonRecruiterPartyManagement.IsRecruiterParty(encounteredParty.MobileParty) && PlayerEncounter.Current != null && (int)Campaign.Current.CurrentConversationContext == 3 && encounteredParty.IsMobile)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private void Conversation_improvedgarrison_recruiter_leave_on_consequence()
	{
		PlayerEncounter.LeaveEncounter = true;
	}

	private bool ImprovedGarrison_mobilegarrison_talk_start_on_condition()
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Invalid comparison between Unknown and I4
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty != null && encounteredParty.MobileParty != null)
			{
				MobileGarrison mobileGarrisonForParty = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonForParty(encounteredParty.MobileParty);
				bool flag = mobileGarrisonForParty != null;
				bool flag2 = IsStuckInMainParty();
				if (flag && !flag2 && PlayerEncounter.Current != null && (int)Campaign.Current.CurrentConversationContext == 3 && encounteredParty.IsMobile && !mobileGarrisonForParty.isNPC)
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

	private void Conversation_improvedgarrison_mobilegarrison_escort_on_consequence()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty.MobileParty != null && encounteredParty.MobileParty.HomeSettlement != null)
			{
				Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(encounteredParty.MobileParty);
				if (mobileGarrisonHome != null && Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrisonHome).StringId, out var value))
				{
					value.GiveAndExecuteOrder(new OrderEscort(Hero.MainHero.PartyBelongedTo));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_mobilegarrison_showloot_on_consequence()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty.MobileParty == null)
			{
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_mobilegarrison_patrol_on_consequence()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty.MobileParty != null)
			{
				Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(encounteredParty.MobileParty);
				if (mobileGarrisonHome != null && Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrisonHome).StringId, out var value))
				{
					value.GiveAndExecuteOrder(new OrderPatrol(mobileGarrisonHome));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_mobilegarrison_inspect_on_consequence()
	{
		PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
		if (encounteredParty.MobileParty != null && encounteredParty.MobileParty.HomeSettlement != null)
		{
			Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(encounteredParty.MobileParty);
			if (Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrisonHome).StringId, out var _))
			{
				Main.PartyManagement.PromptPartyManagementMenu(encounteredParty, MobileParty.MainParty);
			}
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_recruiter_inspect_on_consequence()
	{
		PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
		if (encounteredParty.MobileParty != null && encounteredParty.MobileParty.HomeSettlement != null)
		{
			GarrisonRecruiter recruiterForParty = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterForParty(encounteredParty.MobileParty);
			if (recruiterForParty != null)
			{
				Main.PartyManagement.PromptPartyManagementMenu(encounteredParty, MobileParty.MainParty);
			}
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_mobilegarrison_fortify_on_consequence()
	{
		PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
		if (encounteredParty.MobileParty != null && encounteredParty.MobileParty.HomeSettlement != null)
		{
			Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(encounteredParty.MobileParty);
			if (Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrisonHome).StringId, out var value))
			{
				PromptForitfyGarrison(value);
			}
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_mobilegarrison_return_on_consequence()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty.MobileParty != null && encounteredParty.MobileParty.HomeSettlement != null)
			{
				Settlement mobileGarrisonHome = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonHome(encounteredParty.MobileParty);
				if (mobileGarrisonHome != null && Main.PartyManagement.mobileGarrisonManagement.MobileGarrisons.TryGetValue(((MBObjectBase)mobileGarrisonHome).StringId, out var value))
				{
					value.SetReturnMode();
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_recruiter_return_on_consequence()
	{
		try
		{
			PartyBase encounteredParty = PlayerEncounter.EncounteredParty;
			if (encounteredParty.MobileParty != null && encounteredParty.MobileParty.HomeSettlement != null)
			{
				Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterForParty(encounteredParty.MobileParty)?.SetReturnMode();
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		PlayerEncounter.LeaveEncounter = true;
	}

	private void Conversation_improvedgarrison_mobilegarrison_leave_on_consequence()
	{
		PlayerEncounter.LeaveEncounter = true;
	}

	private Settlement GetBestGarrisonToReturnTo(MobileParty party)
	{
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		Settlement best;
		float bestScore;
		Settlement bestCapacityOk;
		float bestCapacityOkScore;
		try
		{
			if (party == null || party.MapFaction == null)
			{
				return null;
			}
			best = null;
			bestScore = 0f;
			bestCapacityOk = null;
			bestCapacityOkScore = 0f;
			foreach (Settlement item in (List<Settlement>)(object)party.MapFaction.Settlements)
			{
				if (item.IsFortification)
				{
					if (item == party.CurrentSettlement)
					{
						return item;
					}
					considerCandidate(item);
				}
			}
			if (best == null)
			{
				float num = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType((NavigationType)((!party.IsCurrentlyAtSea) ? 1 : 2)) * 2f;
				int num2 = -1;
				Func<Settlement, bool> func = (Settlement s) => s.OwnerClan != null && !s.OwnerClan.IsAtWarWith(party.MapFaction) && s.IsFortification;
				int num3;
				do
				{
					num3 = SettlementHelper.FindNextSettlementAroundMobileParty(party, party.NavigationCapability, num, num2, func);
					if (num3 >= 0)
					{
						num2 = num3;
						Settlement s2 = ((List<Settlement>)(object)Settlement.All)[num3];
						considerCandidate(s2);
					}
				}
				while (num3 >= 0);
			}
			if (best == null)
			{
				Settlement val = SettlementHelper.FindNearestFortificationToMobileParty(party, party.NavigationCapability, (Func<Settlement, bool>)((Settlement x) => x.OwnerClan != null && !x.OwnerClan.IsAtWarWith(party.MapFaction)));
				if (val != null)
				{
					considerCandidate(val);
				}
			}
			return (bestCapacityOk != null) ? bestCapacityOk : best;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return null;
		}
		void considerCandidate(Settlement s)
		{
			if (s != null && s.IsFortification && s.OwnerClan != null && !s.OwnerClan.IsAtWarWith(party.MapFaction))
			{
				CalculateTargetSettlementScore(party, s, out var _, out var bestScore2, out var _);
				if (bestScore2 > bestScore)
				{
					best = s;
					bestScore = bestScore2;
				}
				bool flag = true;
				if (s.Town != null && ((Fief)s.Town).GarrisonParty != null)
				{
					try
					{
						int partySizeLimit = Main.PartyManagement.GetPartySizeLimit(((Fief)s.Town).GarrisonParty.Party);
						int num4 = ((Fief)s.Town).GarrisonParty.Party.NumberOfAllMembers + party.Party.NumberOfAllMembers;
						flag = num4 < partySizeLimit;
					}
					catch
					{
					}
				}
				if (flag && bestScore2 > bestCapacityOkScore)
				{
					bestCapacityOk = s;
					bestCapacityOkScore = bestScore2;
				}
			}
		}
	}

	public unsafe void DetermineNavigationForSettlement(MobileParty party, Settlement targetSettlement, out NavigationType navigationType, out bool isTargetingThePort)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Expected I4, but got Unknown
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e->IL000e: Incompatible stack types: Ref vs I4
		//IL_0008->IL000e: Incompatible stack types: I4 vs Ref
		//IL_0008->IL000e: Incompatible stack types: Ref vs I4
		ref NavigationType reference = ref navigationType;
		int num;
		if (party != null)
		{
			reference = ref *(NavigationType*)party.NavigationCapability;
			num = (int)(ref reference);
		}
		else
		{
			num = 0;
			reference = ref *(NavigationType*)num;
			num = (int)(ref reference);
		}
		*(int*)num = (int)System.Runtime.CompilerServices.Unsafe.AsPointer(ref reference);
		isTargetingThePort = false;
		try
		{
			if (party == null || targetSettlement == null || targetSettlement.Town == null)
			{
				return;
			}
			float num2 = default(float);
			bool flag = default(bool);
			AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(party, targetSettlement, false, ref navigationType, ref num2, ref flag);
			float num3 = num2;
			NavigationType val = navigationType;
			bool flag2 = false;
			if (targetSettlement.HasPort && party.HasNavalNavigationCapability)
			{
				NavigationType val2 = default(NavigationType);
				float num4 = default(float);
				AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(party, targetSettlement, true, ref val2, ref num4, ref flag);
				if (num4 < num3)
				{
					num3 = num4;
					val = val2;
					flag2 = true;
				}
			}
			navigationType = (NavigationType)(int)val;
			isTargetingThePort = flag2;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetMoveGoToSettlementHelper(Settlement settlement, MobileParty party)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		Main.GarrisonPartyBehavior.DetermineNavigationForSettlement(party, settlement, out var navigationType, out var isTargetingThePort);
		party.SetMoveGoToSettlement(settlement, navigationType, isTargetingThePort);
	}

	private bool TrySetPartyMoveToSettlement(MobileParty party, Settlement targetSettlement)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (party == null || targetSettlement == null)
			{
				return false;
			}
			DetermineNavigationForSettlement(party, targetSettlement, out var navigationType, out var isTargetingThePort);
			party.SetMoveGoToSettlement(targetSettlement, navigationType, isTargetingThePort);
			return true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
	}

	private unsafe void CalculateTargetSettlementScore(MobileParty disbandParty, Settlement settlement, out NavigationType bestNavigationType, out float bestScore, out bool isTargetingPort)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Invalid comparison between Unknown and I4
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Expected I4, but got Unknown
		//IL_001a->IL001a: Incompatible stack types: Ref vs I4
		//IL_0014->IL001a: Incompatible stack types: I4 vs Ref
		//IL_0014->IL001a: Incompatible stack types: Ref vs I4
		isTargetingPort = false;
		bestScore = 0f;
		ref NavigationType reference = ref bestNavigationType;
		int num;
		if (disbandParty != null)
		{
			reference = ref *(NavigationType*)disbandParty.NavigationCapability;
			num = (int)(ref reference);
		}
		else
		{
			num = 0;
			reference = ref *(NavigationType*)num;
			num = (int)(ref reference);
		}
		*(int*)num = (int)System.Runtime.CompilerServices.Unsafe.AsPointer(ref reference);
		if (disbandParty == null || settlement == null)
		{
			return;
		}
		float num2 = default(float);
		bool flag = default(bool);
		AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(disbandParty, settlement, false, ref bestNavigationType, ref num2, ref flag);
		float num3 = num2;
		NavigationType val = bestNavigationType;
		if (settlement.HasPort && disbandParty.HasNavalNavigationCapability)
		{
			NavigationType val2 = default(NavigationType);
			float num4 = default(float);
			AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(disbandParty, settlement, true, ref val2, ref num4, ref flag);
			if (num4 < num3)
			{
				num3 = num4;
				val = val2;
				isTargetingPort = true;
			}
		}
		float num5 = MathF.Pow(1f - 0.95f * (MathF.Min(Campaign.MapDiagonal, num3) / Campaign.MapDiagonal), 3f);
		Hero owner = disbandParty.Party.Owner;
		float num6;
		if (((owner != null) ? owner.Clan : null) != settlement.OwnerClan)
		{
			Hero owner2 = disbandParty.Party.Owner;
			num6 = ((((owner2 != null) ? owner2.MapFaction : null) == settlement.MapFaction) ? 0.1f : 0.01f);
		}
		else
		{
			num6 = 1f;
		}
		float num7 = (((int)disbandParty.DefaultBehavior == 2 && disbandParty.TargetSettlement == settlement) ? 1f : 0.3f);
		bestScore = num5 * num6 * num7;
		bestNavigationType = (NavigationType)(int)val;
	}

	private void PromptForitfyGarrisonFilter(MobileGarrison mobileGarrison)
	{
		//IL_02e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ee: Expected O, but got Unknown
		//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f8: Expected O, but got Unknown
		//IL_02f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fd: Expected O, but got Unknown
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_0310: Expected O, but got Unknown
		//IL_0310: Unknown result type (might be due to invalid IL or missing references)
		//IL_031a: Expected O, but got Unknown
		//IL_0315: Unknown result type (might be due to invalid IL or missing references)
		//IL_031f: Expected O, but got Unknown
		//IL_0326: Unknown result type (might be due to invalid IL or missing references)
		//IL_0330: Expected O, but got Unknown
		//IL_0336: Unknown result type (might be due to invalid IL or missing references)
		//IL_0340: Expected O, but got Unknown
		//IL_034a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0354: Expected O, but got Unknown
		//IL_035a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0364: Expected O, but got Unknown
		//IL_036c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0378: Expected O, but got Unknown
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Expected O, but got Unknown
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0169: Expected O, but got Unknown
		//IL_02b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bc: Expected O, but got Unknown
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c1: Expected O, but got Unknown
		try
		{
			_currentMobileGarrisonForFortification = mobileGarrison;
			MobileParty mobileParty = mobileGarrison.getMobileParty();
			List<InquiryElement> list = new List<InquiryElement>();
			List<InquiryElement> list2 = new List<InquiryElement>();
			List<InquiryElement> list3 = new List<InquiryElement>();
			Settlement[] array = ((IEnumerable<Settlement>)Settlement.All).ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				Kingdom val = null;
				if (mobileParty.Party.Owner != null && mobileParty.Party.Owner.Clan != null && mobileParty.Party.Owner.Clan.Kingdom != null)
				{
					val = mobileParty.Party.Owner.Clan.Kingdom;
				}
				if (array[i] != null && array[i].Town != null && (((SettlementComponent)array[i].Town).IsCastle || ((SettlementComponent)array[i].Town).IsTown) && (Main.GarrisonBehavior.SettlementSettingsData.TryGetValue(((object)array[i].Name).ToString(), out var _) || (array[i].OwnerClan != null && mobileParty.Party.Owner != null && array[i].OwnerClan == mobileParty.Party.Owner.Clan)))
				{
					list3.Add(new InquiryElement((object)array[i], ((object)array[i].Name).ToString(), (ImageIdentifier)new BannerImageIdentifier(array[i].Banner, false)));
				}
			}
			for (int j = 0; j < array.Length; j++)
			{
				Kingdom val2 = null;
				if (mobileParty.Party.Owner != null && mobileParty.Party.Owner.Clan != null && mobileParty.Party.Owner.Clan.Kingdom != null)
				{
					val2 = mobileParty.Party.Owner.Clan.Kingdom;
				}
				if (array[j] != null && array[j].Town != null && (((SettlementComponent)array[j].Town).IsCastle || ((SettlementComponent)array[j].Town).IsTown) && array[j].OwnerClan != null && array[j].OwnerClan.Kingdom != null && array[j].OwnerClan.Kingdom == val2 && mobileParty.Party.Owner != null && array[j].OwnerClan != mobileParty.Party.Owner.Clan)
				{
					list2.Add(new InquiryElement((object)array[j], ((object)array[j].Name).ToString(), (ImageIdentifier)new BannerImageIdentifier(array[j].Banner, false)));
				}
			}
			list.Add(new InquiryElement((object)list3, ((object)new TextObject("Show your clans garrisons", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new EmptyImageIdentifier()));
			list.Add(new InquiryElement((object)list2, ((object)new TextObject("Show all alliance garrisons", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new EmptyImageIdentifier()));
			MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(((object)new TextObject("Select Garrison", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("", (Dictionary<string, object>)null)).ToString(), list, true, 1, 1, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)null, (Action<List<InquiryElement>>)null, "", false), false, false);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void PromptForitfyGarrison(MobileGarrison mobileGarrison)
	{
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Expected O, but got Unknown
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Expected O, but got Unknown
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Expected O, but got Unknown
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Expected O, but got Unknown
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e1: Expected O, but got Unknown
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Expected O, but got Unknown
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Expected O, but got Unknown
		try
		{
			_currentMobileGarrisonForFortification = mobileGarrison;
			MobileParty mobileParty = mobileGarrison.getMobileParty();
			List<InquiryElement> list = new List<InquiryElement>();
			Settlement[] array = ((IEnumerable<Settlement>)Settlement.All).ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				Kingdom val = null;
				if (mobileParty.Party.Owner != null && mobileParty.Party.Owner.Clan != null && mobileParty.Party.Owner.Clan.Kingdom != null)
				{
					val = mobileParty.Party.Owner.Clan.Kingdom;
				}
				if (array[i] != null && array[i].Town != null && (((SettlementComponent)array[i].Town).IsCastle || ((SettlementComponent)array[i].Town).IsTown) && (Main.GarrisonBehavior.SettlementSettingsData.TryGetValue(((object)array[i].Name).ToString(), out var _) || (array[i].OwnerClan != null && mobileParty.Party.Owner != null && array[i].OwnerClan == mobileParty.Party.Owner.Clan)))
				{
					ImageIdentifier val2 = (ImageIdentifier)new BannerImageIdentifier(array[i].Banner, false);
					list.Add(new InquiryElement((object)array[i], ((object)array[i].Name).ToString(), val2));
				}
			}
			string text = ((object)new TextObject("{=menu_transfer_select}Select Garrison", (Dictionary<string, object>)null)).ToString();
			string text2 = ((object)new TextObject("{=menu_fortify_desc}Select the garrison you want your guards to fortify.", (Dictionary<string, object>)null)).ToString();
			string text3 = ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString();
			string text4 = ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString();
			MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(text, text2, list, true, 1, 1, text3, text4, (Action<List<InquiryElement>>)Inquirydata_FortifyGarrison, (Action<List<InquiryElement>>)null, "", false), false, false);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_FortifyGarrison(List<InquiryElement> list)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		try
		{
			if (_currentMobileGarrisonForFortification != null)
			{
				Settlement val = (Settlement)list.First().Identifier;
				_currentMobileGarrisonForFortification.getMobileParty().SetCustomHomeSettlement(val);
				_currentMobileGarrisonForFortification.SetFortifyMode(val);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public static List<MobileParty> GetAllClanParties(Clan clan)
	{
		try
		{
			if (clan == null)
			{
				return null;
			}
			List<MobileParty> list = new List<MobileParty>();
			foreach (MobileParty item in (List<MobileParty>)(object)Campaign.Current.MobileParties)
			{
				if (item != null && item.Party != null && (item.ActualClan == clan || (item.Owner != null && item.Owner.Clan == clan)))
				{
					list.Add(item);
				}
			}
			return list;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public void RemovePartyHelper(MobileParty party)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		try
		{
			if (party == null)
			{
				return;
			}
			if (_removePartyMethodInfo == null)
			{
				_removePartyMethodInfo = typeof(MobileParty).GetMethod("RemoveParty", BindingFlags.Instance | BindingFlags.NonPublic);
				if (_removePartyMethodInfo == null)
				{
					InformationManager.DisplayMessage(new InformationMessage("Improved Garrisons: Could not access MobileParty.RemoveParty via reflection. Falling back to DestroyParty2Action.", Color.FromUint(ModuleColors.yellow)));
					Main.GarrisonPartyBehavior.RemovePartyHelper(party);
					return;
				}
			}
			_removePartyMethodInfo.Invoke(party, null);
		}
		catch (TargetInvocationException ex)
		{
			Exception ex2 = ex.InnerException ?? ex;
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex2);
			Main.GarrisonPartyBehavior.RemovePartyHelper(party);
		}
		catch (Exception ex3)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex3);
			Main.GarrisonPartyBehavior.RemovePartyHelper(party);
		}
	}
}
