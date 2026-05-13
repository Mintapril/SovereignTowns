using System.IO;

namespace ImprovedGarrisons.SaveSystem.FilePaths;

public class GlobalSettingsFilePath : IGSaveFilePath
{
	public GlobalSettingsFilePath()
	{
		if (base.SaveFilesPath != null)
		{
			base.CombinedPath = Path.Combine(new string[2] { base.SaveFilesPath, "GlobalSettings.bin" });
		}
	}
}
