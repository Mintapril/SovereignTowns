using System;
using System.IO;
using TaleWorlds.Library;

namespace ImprovedGarrisons.Debugging.LogFileSystem;

public class LogFileWriter
{
	public void WriteErrorLog(string error, string filePath)
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Expected O, but got Unknown
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath));
			using StreamWriter w = File.AppendText(filePath);
			LogEntry(error, w);
		}
		catch (Exception ex)
		{
			InformationManager.DisplayMessage(new InformationMessage("Improved Garrisons: Could not print error log." + ex.ToString()));
		}
	}

	private void LogEntry(string logMessage, TextWriter w)
	{
		w.Write("\r\nNew Log Entry: ");
		w.WriteLine(DateTime.Now.ToLongTimeString() + " " + DateTime.Now.ToLongDateString());
		w.WriteLine(" >> " + logMessage);
		w.WriteLine("-------------------------------");
	}
}
