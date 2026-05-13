using System;
using System.IO;
using System.Text;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.Library;

namespace ImprovedGarrisons.Debugging.LogFileSystem;

public static class LogFileManager
{
	private static readonly LogFileWriter logFileWriter = new LogFileWriter();

	private static string errorLogPath = Path.Combine(BasePath.Name, "Modules", "ImprovedGarrisons", "ModuleData", "ErrorLog.xml");

	public static InformationMessage errorMessage = new InformationMessage("It seems like Improved Garrisons encountered an error. You find the log under /Moduledata/ErrorLog.xml. Please consider reporting it on the official NexusMod Page. Thank you for helping. - Sidies");

	public static void WriteErrorLogEntry(string fromMethod, Exception ex, bool withoutMessage = false)
	{
		if (!GlobalSettings.Instance.DisableErrorMessage)
		{
			if (!withoutMessage)
			{
				InformationManager.DisplayMessage(errorMessage);
			}
			StringBuilder stringBuilder = new StringBuilder();
			while (ex != null)
			{
				stringBuilder.AppendLine(ex.Message);
				stringBuilder.AppendLine(ex.StackTrace);
				ex = ex.InnerException;
			}
			string text = stringBuilder.ToString();
			string error = "There was an error in method " + fromMethod + " Exception: " + text;
			logFileWriter.WriteErrorLog(error, errorLogPath);
		}
	}

	public static void WriteErrorLogEntry(string customErrorLogMessage)
	{
		if (!GlobalSettings.Instance.DisableErrorMessage)
		{
			InformationManager.DisplayMessage(errorMessage);
			logFileWriter.WriteErrorLog(customErrorLogMessage, errorLogPath);
		}
	}
}
