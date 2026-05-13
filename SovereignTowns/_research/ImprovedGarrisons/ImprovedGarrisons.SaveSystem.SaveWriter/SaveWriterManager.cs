using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.FilePaths;
using ImprovedGarrisons.SaveSystem.SaveData;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.SaveSystem.SaveWriter;

public class SaveWriterManager
{
	private GlobalSettingsFilePath globalSettingsFilePath = new GlobalSettingsFilePath();

	public bool CreateAndUpdateSaveFile(SettlementSaveFilePath name)
	{
		try
		{
			FileWriter.SerializeToBin(IGSaveData.Instance, name.CombinedPath);
			return true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex, withoutMessage: true);
			return false;
		}
	}

	public bool CreateAndUpdateGlobalSettings()
	{
		try
		{
			FileWriter.SerializeToBin(GlobalSettings.Instance, globalSettingsFilePath.CombinedPath);
			return true;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex, withoutMessage: true);
			return false;
		}
	}

	public bool DeleteSaveFile(IGSaveFilePath filePath)
	{
		try
		{
			return FileWriter.DeleteSaveFileByPath(filePath.CombinedPath);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex, withoutMessage: true);
			return false;
		}
	}

	public bool LoadSaveFile(IGSaveFilePath filePath)
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		try
		{
			if (filePath.PathIsValid())
			{
				if (File.Exists(filePath.CombinedPath))
				{
					IGSaveData.Instance = FileWriter.DeserializeFromBin<IGSaveData>(filePath.CombinedPath);
				}
				Main.GarrisonBehavior.InitializeSettlements();
				return true;
			}
			if (Campaign.Current != null && Campaign.Current.GameStarted)
			{
				InformationManager.DisplayMessage(new InformationMessage("Warning. The configuration path for Improved Garrisons could not be set. The mod will not be able to save its settings.", Color.FromUint(ModuleColors.red)));
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		Main.GarrisonBehavior.InitializeSettlements();
		return false;
	}

	public bool LoadGlobalSettings()
	{
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Expected O, but got Unknown
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Expected O, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Expected O, but got Unknown
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected O, but got Unknown
		try
		{
			if (globalSettingsFilePath.PathIsValid())
			{
				if (File.Exists(globalSettingsFilePath.CombinedPath))
				{
					GlobalSettings.Instance = FileWriter.DeserializeFromBin<GlobalSettings>(globalSettingsFilePath.CombinedPath);
					return true;
				}
			}
			else if (Campaign.Current != null && Campaign.Current.GameStarted)
			{
				InformationManager.DisplayMessage(new InformationMessage("Warning. The configuration path for Improved Garrisons could not be set. The mod will not be able to save its settings.", Color.FromUint(ModuleColors.red)));
			}
		}
		catch (SerializationException)
		{
			InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=misc_update_title2}Improved Garrison Update", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=misc_update_desc2}Thank you for using Improved Garrisons! \n \nThere has been a major update for the mod which includes a new save system. Unfortunately this new save system is not compatible with the old way the mods settings have been saved. Therefore your mods data has been reset which includes your configuration and settlement specific settings like templates. \n \nI would recommend setting up the mods data before you continue playing. \n \nIf you want to read more about this update, head to Improved Garrisons on Nexusmods.\n \n Have fun playing! \n ~ Sidies", (Dictionary<string, object>)null)).ToString(), true, false, ((object)new TextObject("{=menu_okay}Okay", (Dictionary<string, object>)null)).ToString(), (string)null, (Action)delegate
			{
				CreateAndUpdateGlobalSettings();
			}, (Action)null, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
		}
		catch (Exception ex2)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex2);
		}
		return false;
	}
}
