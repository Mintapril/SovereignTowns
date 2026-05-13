using ImprovedGarrisons.SaveSystem.Configuration;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataTypes;

public class NPCGarrisonSettings : GarrisonSettings
{
	public NPCGarrisonSettings()
	{
		Config config = ConfigManager.Instance.Config;
		base.EnableTraining = config.EnableNPCGarrisonTraining;
		base.EnablePrisonerRecruitment = config.EnableNPCPrisonerRecruitment;
		base.EnableRecruitFromRegion = config.EnableNPCWorldRecruitment;
		base.MaxRecruitThreshold = config.MaximumNPCRecruitmentThreshold;
		base.MaxUpgradeTier = config.NPCMaxUpgradeTier;
		base.GuardsAutoSpawn = config.NPCSpawnGuards;
		base.GuardsAutoSpawnToDefend = config.NPCSpawnGuards;
	}
}
