using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;

public class ManagementSettings : ImprovedGarrisonSettings
{
	private Town _transferTarget;

	private Town _currentTown;

	private static ManagementSettings _instance;

	public static ManagementSettings Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new ManagementSettings();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public void PromptTransfer(Town fromTown)
	{
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Expected O, but got Unknown
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Expected O, but got Unknown
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Expected O, but got Unknown
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Expected O, but got Unknown
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(fromTown))
			{
				return;
			}
			if (!fromTown.IsUnderSiege)
			{
				if (((Fief)fromTown).GarrisonParty != null && ((Fief)fromTown).GarrisonParty.MemberRoster.TotalManCount > 0)
				{
					if (Main.PartyManagement.transferPartyManagement.SettlementHasTransferParty(((SettlementComponent)fromTown).Settlement))
					{
						InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_transfer_dupe}This garrison already has a transfer party. Please wait for it to reach its destination target.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
						return;
					}
					string title = ((object)new TextObject("{=settings_managementsettings_select}Select a garrison", (Dictionary<string, object>)null)).ToString();
					string desc = ((object)new TextObject("{=settings_managementsettings_selectdesc}Select the garrison you want to transfer these units to", (Dictionary<string, object>)null)).ToString();
					PromptGarrisonSelector(title, desc, 1, fromTown, Inquirydata_TranferGarrison);
				}
				else
				{
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_emptygarrison}Your garrison is empty.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
				}
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_transfer_undersiege}This location is currently under siege. The transfer party can't get out!", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptGarrisonSelector(string title, string desc, int selectableAmount, Town currentTown, Action<List<InquiryElement>> positiveAction)
	{
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Expected O, but got Unknown
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Expected O, but got Unknown
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Expected O, but got Unknown
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Expected O, but got Unknown
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(currentTown))
			{
				return;
			}
			_currentTown = currentTown;
			List<InquiryElement> list = new List<InquiryElement>();
			Settlement[] array = ((IEnumerable<Settlement>)Settlement.All).ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] != null && array[i].Town != null && (((SettlementComponent)array[i].Town).IsCastle || ((SettlementComponent)array[i].Town).IsTown))
				{
					bool flag = array[i].Town != _currentTown;
					if (base.garrisonBehavior.SettlementSettingsData.TryGetValue(((object)array[i].Name).ToString(), out var _) && flag)
					{
						list.Add(new InquiryElement((object)array[i].Town, ((object)array[i].Name).ToString(), (ImageIdentifier)new EmptyImageIdentifier()));
					}
				}
			}
			string text = ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString();
			string text2 = ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString();
			MultiSelectionInquiryData val = new MultiSelectionInquiryData(title, desc, list, true, 0, selectableAmount, text, text2, (Action<List<InquiryElement>>)positiveAction.Invoke, (Action<List<InquiryElement>>)null, "", false);
			MBInformationManager.ShowMultiSelectionInquiry(val, false, false);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptCopyToSpecificTowns(Town town)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				_currentTown = town;
				string title = ((object)new TextObject("{=settings_managementsettings_copyselction}Copy to garrison", (Dictionary<string, object>)null)).ToString();
				string desc = ((object)new TextObject("{=settings_managementsettings_copydesc}Select the garrisons you want to copy the settings of the current garrison to.", (Dictionary<string, object>)null)).ToString();
				PromptGarrisonSelector(title, desc, -1, town, Inquirydata_CopySpecific);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptCopyToAllTowns(Town town)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				_currentTown = town;
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=settings_managementsettings_copytownselection}Copy these settings to all my towns", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_managementsettings_copytownselectiondesc}If you continue, all of your towns will have the same settings as this one.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_no}No", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					CopyToAllTowns(town);
					InformationManager.HideInquiry();
				}, (Action)delegate
				{
					InformationManager.HideInquiry();
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void PromptCopyToAllCastles(Town town)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Expected O, but got Unknown
		try
		{
			if (CheckIfTownIsValid(town))
			{
				_currentTown = town;
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=settings_managementsettings_copycastleselection}Copy these settings to all my castles", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_managementsettings_copycastleselectiondesc}If you continue, all of your castles will have the same settings as this one.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_no}No", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					CopyToAllCastles(town);
					InformationManager.HideInquiry();
				}, (Action)delegate
				{
					InformationManager.HideInquiry();
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_TranferGarrison(List<InquiryElement> list)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		try
		{
			if (list == null || list.Count <= 0)
			{
				return;
			}
			_transferTarget = (Town)Extensions.GetRandomElement<InquiryElement>((IReadOnlyList<InquiryElement>)list).Identifier;
			Main.ExecuteActionOnNextTick(delegate
			{
				Settlement settlement = ((SettlementComponent)base.garrisonBehavior.CurrentTownForSettings).Settlement;
				PartyBase val = Main.PartyManagement.transferPartyManagement.CreateNewTransferParty(settlement, ((SettlementComponent)_transferTarget).Settlement);
				if (val != null)
				{
					Main.PartyManagement.PromptPartyManagementMenu(val, ((Fief)base.garrisonBehavior.CurrentTownForSettings).GarrisonParty);
				}
			});
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_CopySpecific(List<InquiryElement> list)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		try
		{
			if (list == null || list.Count <= 0)
			{
				return;
			}
			foreach (InquiryElement item in list)
			{
				CopyGarrisonSettings(base.garrisonBehavior.CurrentTownForSettings, (Town)item.Identifier);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void CopyToAllCastles(Town town)
	{
		try
		{
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
			List<KeyValuePair<string, GarrisonSettings>> list = base.garrisonBehavior.SettlementSettingsData.ToList();
			foreach (KeyValuePair<string, GarrisonSettings> item in list)
			{
				Settlement settlementFromName = base.garrisonBehavior.GetSettlementFromName(item.Key);
				if (settlementFromName != null && settlementFromName.IsCastle && settlementFromName.Town != town)
				{
					CopyGarrisonSettings(town, settlementFromName.Town);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void CopyToAllTowns(Town town)
	{
		try
		{
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(town);
			if (townSettings == null)
			{
				return;
			}
			List<KeyValuePair<string, GarrisonSettings>> list = base.garrisonBehavior.SettlementSettingsData.ToList();
			foreach (KeyValuePair<string, GarrisonSettings> item in list)
			{
				Settlement settlementFromName = base.garrisonBehavior.GetSettlementFromName(item.Key);
				if (settlementFromName != null && settlementFromName.IsTown && settlementFromName.Town != town)
				{
					CopyGarrisonSettings(town, settlementFromName.Town);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void CopyGarrisonSettings(Town from, Town to)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Expected O, but got Unknown
		try
		{
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(from);
			GarrisonSettings value = townSettings.clone();
			base.garrisonBehavior.SettlementSettingsData[((object)((SettlementComponent)to).Name).ToString()] = value;
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_manage_copy}Copied settings to", (Dictionary<string, object>)null)).ToString() + str_space + (object)((SettlementComponent)to).Name, Color.FromUint(ModuleColors.green)));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
