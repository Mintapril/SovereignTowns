using System;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.FilePaths;

namespace ImprovedGarrisons.SaveSystem.Configuration;

internal class ConfigManager
{
	private static ConfigManager _instance;

	private Config _config;

	public Config Config
	{
		get
		{
			if (_config == null)
			{
				return new Config();
			}
			return _config;
		}
		set
		{
			_config = value;
		}
	}

	public static ConfigManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new ConfigManager();
			}
			return _instance;
		}
	}

	public void ReadConfigForCurrentGame()
	{
		try
		{
			ConfigFilePath filePath = new ConfigFilePath(SaveSystemManager.Instance.CurrentSaveSlotNameWithID);
			ReadConfig(filePath);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ReadConfig(ConfigFilePath filePath)
	{
		try
		{
			if (filePath != null && filePath.FileName != "")
			{
				Directory.CreateDirectory(Path.GetDirectoryName(filePath.CombinedPath));
				if (!File.Exists(filePath.CombinedPath))
				{
					WriteConfigWithDefaultValues(filePath);
				}
				XmlSerializer xmlSerializer = new XmlSerializer(typeof(Config));
				using StreamReader textReader = new StreamReader(filePath.CombinedPath);
				Config = (Config)xmlSerializer.Deserialize(textReader);
				return;
			}
			Config = new Config();
			Config.ResetToDefault();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			Config = new Config();
			Config.ResetToDefault();
		}
	}

	public void CreateAndUpdateConfigForCurrentGame()
	{
		try
		{
			ConfigFilePath filePath = new ConfigFilePath(SaveSystemManager.Instance.CurrentSaveSlotNameWithID);
			CreateAndUpdateConfig(filePath);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void CreateAndUpdateConfig(string name)
	{
		try
		{
			string name2 = SaveSystemManager.Instance.AddUniqueGameIDToString(name);
			ConfigFilePath configFilePath = new ConfigFilePath(name2);
			Directory.CreateDirectory(Path.GetDirectoryName(configFilePath.CombinedPath));
			Config.SerializeObject(configFilePath.CombinedPath);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void CreateAndUpdateConfig(ConfigFilePath filePath)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath.CombinedPath));
			Config.SerializeObject(filePath.CombinedPath);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void DeleteConfig(ConfigFilePath filePath)
	{
		try
		{
			File.Delete(filePath.CombinedPath);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void WriteConfigWithDefaultValues(ConfigFilePath filePath)
	{
		Config = new Config();
		Config.ResetToDefault();
		CreateAndUpdateConfig(filePath);
	}
}
