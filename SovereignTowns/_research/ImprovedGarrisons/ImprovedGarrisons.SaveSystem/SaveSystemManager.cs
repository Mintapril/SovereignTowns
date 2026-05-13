using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.FilePaths;
using ImprovedGarrisons.SaveSystem.SaveWriter;
using ImprovedGarrisons.Utils;
using StoryMode;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace ImprovedGarrisons.SaveSystem;

public class SaveSystemManager
{
	private static SaveSystemManager _instance;

	private string UniqueGameId = null;

	private SaveWriterManager saveWriterManager;

	private string _currentSaveSlotNameWithID;

	public static SaveSystemManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new SaveSystemManager();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	private SaveGameFileInfo[] CurrentMBSaveFiles => MBSaveLoad.GetSaveFiles((Func<SaveGameFileInfo, bool>)null);

	public string CurrentSaveSlotNameWithID
	{
		get
		{
			try
			{
				string activeSaveSlotName = MBSaveLoad.ActiveSaveSlotName;
				if (string.IsNullOrEmpty(activeSaveSlotName) && _currentSaveSlotNameWithID != null)
				{
					return _currentSaveSlotNameWithID;
				}
				if (string.IsNullOrEmpty(activeSaveSlotName))
				{
					string nameOfLatestSaveByID = GetNameOfLatestSaveByID(Campaign.Current.UniqueGameId);
					if (string.IsNullOrEmpty(nameOfLatestSaveByID))
					{
						SaveGameFileInfo val = MBSaveLoad.GetSaveFiles((Func<SaveGameFileInfo, bool>)((SaveGameFileInfo info) => info.Name.StartsWith("saveauto", StringComparison.OrdinalIgnoreCase))).FirstOrDefault();
						if (val != null)
						{
							return val.Name + "_";
						}
						return null;
					}
					_currentSaveSlotNameWithID = AddUniqueGameIDToString(nameOfLatestSaveByID);
					return _currentSaveSlotNameWithID;
				}
				_currentSaveSlotNameWithID = AddUniqueGameIDToString(activeSaveSlotName);
				return _currentSaveSlotNameWithID;
			}
			catch (Exception)
			{
				LogFileManager.WriteErrorLogEntry("Could not find the current save slot name. Loading/Saving failed.");
				return null;
			}
		}
	}

	public SaveSystemManager()
	{
		saveWriterManager = new SaveWriterManager();
	}

	public string AddUniqueGameIDToString(string name)
	{
		string text = "";
		if (Campaign.Current != null)
		{
			text = Campaign.Current.UniqueGameId;
			if (text != null)
			{
				UniqueGameId = text;
			}
		}
		else
		{
			text = UniqueGameId;
		}
		if (text == null)
		{
			text = "";
		}
		return name + "_" + text;
	}

	public bool SaveSettlementSaveData(string saveGameName)
	{
		try
		{
			SettlementSaveFilePath name = new SettlementSaveFilePath(AddUniqueGameIDToString(saveGameName));
			saveWriterManager.CreateAndUpdateSaveFile(name);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public bool SaveGlobalSettings()
	{
		try
		{
			saveWriterManager.CreateAndUpdateGlobalSettings();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public void LoadSettlementSaveDataAndGlobalSettings(CampaignGameStarter starter)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Invalid comparison between Unknown and I4
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected O, but got Unknown
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		try
		{
			SettlementSaveFilePath filePath = new SettlementSaveFilePath(CurrentSaveSlotNameWithID);
			saveWriterManager.LoadSaveFile(filePath);
			saveWriterManager.LoadGlobalSettings();
			GameType gameType = Game.Current.GameType;
			CampaignStoryMode val = (CampaignStoryMode)(object)((gameType is CampaignStoryMode) ? gameType : null);
			if (Game.Current.GameType is Campaign && val != null && (int)((Campaign)val).CampaignGameLoadingType != 1 && !ConfigManager.Instance.Config.VersionNumberIsUpToDate())
			{
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=misc_update_title}Improved Garrison update", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=misc_update_desc}~ Thanks for using Improved Garrisons \n \nThere are new configuration options in the latest update!\n \nIt is recommended to reset the configuration to its default values. These new configurations are otherwise set to false or 0. \n \nYou can always check this mod's settings by pressing ALT + G. \n \nHave fun playing! \n~ Sidies", (Dictionary<string, object>)null)).ToString(), true, false, ((object)new TextObject("{=menu_okay}Okay", (Dictionary<string, object>)null)).ToString(), (string)null, (Action)delegate
				{
					ConfigManager.Instance.Config.UpdateVersion();
					Main.GarrisonPartyBehavior.ReturnAllIGParties();
				}, (Action)null, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void DeleteUnnecessarySaveAndConfigFiles()
	{
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Expected O, but got Unknown
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Expected O, but got Unknown
		try
		{
			SaveGameFileInfo[] saveFiles = MBSaveLoad.GetSaveFiles((Func<SaveGameFileInfo, bool>)null);
			string saveFilesPath = new IGSaveFilePath().SaveFilesPath;
			string[] files = Directory.GetFiles(saveFilesPath);
			foreach (string item in files.ToList())
			{
				string iGSaveNameFromPath = GetIGSaveNameFromPath(item);
				string iGSaveIDFromPath = getIGSaveIDFromPath(item);
				bool flag = false;
				bool flag2 = false;
				SaveGameFileInfo[] array = saveFiles;
				foreach (SaveGameFileInfo val in array)
				{
					flag = iGSaveIDFromPath == MetaDataExtensions.GetUniqueGameId(val.MetaData);
					flag2 = iGSaveNameFromPath == val.Name;
					if (flag && flag2)
					{
						break;
					}
				}
				if (!flag && !flag2)
				{
					saveWriterManager.DeleteSaveFile(new SettlementSaveFilePath(iGSaveNameFromPath + "_" + iGSaveIDFromPath));
					ConfigManager.Instance.DeleteConfig(new ConfigFilePath(iGSaveNameFromPath + "_" + iGSaveIDFromPath));
					InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_save_delete}The Improved Garrison data has been deleted from this save:", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + iGSaveNameFromPath, Color.FromUint(ModuleColors.modMainColor)));
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private string GetNameOfLatestSaveByID(string id)
	{
		try
		{
			if (id != null)
			{
				string saveFilesPath = new IGSaveFilePath().SaveFilesPath;
				List<string> list = (from file in Directory.GetFiles(saveFilesPath)
					where file.Contains(id)
					select file).ToList();
				DateTime dateTime = DateTime.MinValue;
				string text = "";
				foreach (string item in list)
				{
					string text2 = Path.Combine(saveFilesPath, item);
					DateTime lastWriteTime = File.GetLastWriteTime(text2);
					if (dateTime < lastWriteTime)
					{
						dateTime = lastWriteTime;
						text = text2;
					}
				}
				if (text.Count() > 0)
				{
					return GetIGSaveNameFromPath(text);
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private string GetIGSaveNameFromPath(string path)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		int num = fileNameWithoutExtension.IndexOf('_') + 1;
		int num2 = fileNameWithoutExtension.LastIndexOf('_');
		int length = fileNameWithoutExtension.Length;
		int num3 = fileNameWithoutExtension.Count((char f) => f == '_');
		if (!fileNameWithoutExtension.StartsWith("IG") || num <= 0 || num2 < 0 || num3 < 2)
		{
			return null;
		}
		return fileNameWithoutExtension.Substring(num, num2 - num);
	}

	private string getIGSaveIDFromPath(string path)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		int num = fileNameWithoutExtension.IndexOf('_') + 1;
		int num2 = fileNameWithoutExtension.LastIndexOf('_');
		int length = fileNameWithoutExtension.Length;
		int num3 = fileNameWithoutExtension.Count((char f) => f == '_');
		if (!fileNameWithoutExtension.StartsWith("IG") || num <= 0 || num2 < 0 || num3 < 2)
		{
			return null;
		}
		return fileNameWithoutExtension.Substring(num2 + 1, length - num2 - 1);
	}
}
