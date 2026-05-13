using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using ImprovedGarrisons.SaveSystem.Configuration;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataTypes;

[Serializable]
public class GarrisonSettings
{
	public int MaxRecruitThreshold { get; set; }

	public bool EnableRecruitFromRegion { get; set; }

	public bool EnableTraining { get; set; }

	public bool EnablePrisonerRecruitment { get; set; }

	public bool EnablePrisonerSell { get; set; }

	public bool EnableHorseBuy { get; set; }

	public bool EnableHideoutClear { get; set; }

	public bool EnableReplenish { get; set; }

	public int MaxUpgradeTier { get; set; }

	public bool GuardEnablePrisonerRecruitment { get; set; }

	public bool GuardEnableUpgradeTroops { get; set; }

	public bool[] TroopsToUpgradeTo { get; set; }

	public bool AllowPrisonerRecruitAboveThreshold { get; set; }

	public bool PrisonerRecruitmentIgnoresTemplate { get; set; }

	public bool RecruitOnlyEliteUnits { get; set; }

	public bool RecruitmentFollowsTemplate { get; set; }

	public TrainingTemplate Template { get; set; }

	public List<Tuple<string, int>> InitialTroopRoster { get; set; }

	public string RecruiterCultureToRecruit { get; set; }

	public bool RecruiterAutoSpawn { get; set; }

	public bool RecruiterRecruitOnlyElites { get; set; }

	public int RecruiterRecruitAmount { get; set; }

	public bool RecruiterAllowHorseBuy { get; set; }

	public int RecruiterInitialSize { get; set; }

	public bool AutoRemoveNonTemplateTroops { get; set; }

	public int WageLimit { get; set; }

	public bool VanillaRecruitment { get; set; }

	public bool VanillaTraining { get; set; }

	public bool GuardsAutoSpawn { get; set; }

	public bool GuardsAutoSpawnToDefend { get; set; }

	public int GuardsAutoSpawnThreshold { get; set; }

	public int GuardsAutoSpawnSize { get; set; }

	public float GuardReturnPercentage { get; set; }

	public GarrisonSettings()
	{
		Config config = ConfigManager.Instance.Config;
		EnableTraining = config.DefaultPlayerEnableTraining;
		EnablePrisonerRecruitment = config.DefaultPlayerEnablePrisonerRecruitment;
		EnableRecruitFromRegion = config.DefaultPlayerEnableRecruitment;
		EnablePrisonerSell = config.DefaultEnableGuardPrisonerSell;
		EnableReplenish = config.DefaultGuardsEnableReplenish;
		EnableHideoutClear = config.DefaultEnableGuardHideoutClear;
		EnableHorseBuy = config.DefaultEnableGuardBuyHorses;
		GuardEnablePrisonerRecruitment = config.DefaultEnableGuardPrisonerRecruitment;
		GuardEnableUpgradeTroops = config.DefaultEnableGuardUpgrade;
		MaxRecruitThreshold = config.DefaultPlayerMaxRecruitThreshold;
		MaxUpgradeTier = config.DefaultPlayerMaxUpgradeTier;
		AllowPrisonerRecruitAboveThreshold = config.DefaultAllowPrisonerRecruitAboveThreshold;
		RecruitOnlyEliteUnits = config.DefaultOnlyRecruitElites;
		Template = new TrainingTemplate("Default");
		RecruitmentFollowsTemplate = false;
		PrisonerRecruitmentIgnoresTemplate = false;
		AutoRemoveNonTemplateTroops = false;
		RecruiterCultureToRecruit = null;
		RecruiterAutoSpawn = false;
		RecruiterRecruitAmount = 50;
		RecruiterAllowHorseBuy = false;
		RecruiterRecruitOnlyElites = false;
		RecruiterInitialSize = 0;
		GuardsAutoSpawn = false;
		GuardsAutoSpawnToDefend = false;
		GuardReturnPercentage = 0.35f;
		GuardsAutoSpawnThreshold = 100;
		GuardsAutoSpawnSize = 50;
		WageLimit = -1;
		VanillaRecruitment = false;
		VanillaTraining = false;
	}

	[OnDeserialized]
	private void OnDeserialized(StreamingContext context)
	{
		if (Template == null)
		{
			Template = new TrainingTemplate("Default");
		}
	}

	public int UpgradeTargetsAmount()
	{
		int num = 0;
		return num + Template.AmountOfTroopsInTemplate;
	}

	public GarrisonSettings clone()
	{
		GarrisonSettings garrisonSettings = (GarrisonSettings)MemberwiseClone();
		garrisonSettings.Template = new TrainingTemplate(Template.Name);
		garrisonSettings.Template.SetTroops(Template.GetTroopList());
		return garrisonSettings;
	}
}
