using System.IO;

namespace ImprovedGarrisons.SaveSystem.FilePaths;

public class SettlementSaveFilePath : IGSaveFilePath
{
	public SettlementSaveFilePath(string name)
	{
		if (base.SaveFilesPath != null)
		{
			base.CombinedPath = Path.Combine(new string[2]
			{
				base.SaveFilesPath,
				"IGSave_" + name + ".bin"
			});
		}
	}
}
