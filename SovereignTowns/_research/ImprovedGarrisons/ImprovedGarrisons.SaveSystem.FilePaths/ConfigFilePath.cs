using System.IO;

namespace ImprovedGarrisons.SaveSystem.FilePaths;

public class ConfigFilePath : IGSaveFilePath
{
	public ConfigFilePath(string name)
	{
		if (base.SaveFilesPath != null)
		{
			base.CombinedPath = Path.Combine(new string[2]
			{
				base.SaveFilesPath,
				"IGConfiguration_" + name + ".xml"
			});
		}
		base.FileName = name;
	}
}
