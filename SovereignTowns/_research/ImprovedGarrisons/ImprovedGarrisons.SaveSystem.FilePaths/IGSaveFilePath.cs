using System;
using System.IO;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace ImprovedGarrisons.SaveSystem.FilePaths;

public class IGSaveFilePath
{
	private string _saveFilesPath;

	public string CombinedPath { get; protected set; }

	public string FileName { get; protected set; }

	public string SaveFilesPath
	{
		get
		{
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d7: Expected O, but got Unknown
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Invalid comparison between I4 and Unknown
			//IL_0076: Unknown result type (might be due to invalid IL or missing references)
			if (_saveFilesPath != null)
			{
				return _saveFilesPath;
			}
			try
			{
				string text = null;
				if (8 == (int)ApplicationPlatform.CurrentPlatform)
				{
					text = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
				}
				else
				{
					PropertyInfo property = ((object)Common.PlatformFileHelper).GetType().GetProperty("DocumentsPath", BindingFlags.Instance | BindingFlags.NonPublic);
					text = (string)property.GetValue(Common.PlatformFileHelper);
				}
				text = Path.Combine(text, Utilities.GetApplicationName(), EngineFilePaths.ConfigsPath.Path, "ImprovedGarrisons", "Saves");
				if (text == null || text == "")
				{
					throw new Exception();
				}
				_saveFilesPath = text;
				return _saveFilesPath;
			}
			catch (Exception ex)
			{
				InformationManager.DisplayMessage(new InformationMessage("The file path for the Improved Garrisons config could not be initialized. The mods settings will not be saved!"));
				LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex, withoutMessage: true);
				return null;
			}
		}
	}

	public bool PathIsValid()
	{
		return SaveFilesPath != null;
	}
}
