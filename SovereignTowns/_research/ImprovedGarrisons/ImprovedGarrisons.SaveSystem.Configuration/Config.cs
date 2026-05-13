using System;

namespace ImprovedGarrisons.SaveSystem.Configuration;

[Serializable]
public class Config
{
	private static string _latestVersion = "c1.0.4";

	public string Version { get; set; } = _latestVersion;


	public bool ActivateDeleteAllModsPartiesMode { get; set; }

	public bool DeactivateTutorial { get; set; }

	public bool DisableDailyMessage { get; set; }

	public bool EnableMapBannerTracker { get; set; }

	public bool NPCSpawnGuards { get; set; }

	public bool EnableNPCWorldRecruitment { get; set; }

	public int MaximumNPCRecruitmentThreshold { get; set; }

	public bool EnableNPCGarrisonTraining { get; set; }

	public int NPCBonusFoodGathering { get; set; }

	public bool EnableNPCPrisonerRecruitment { get; set; }

	public bool EnabeNPCFoodbonus { get; set; }

	public int NPCMaxUpgradeTier { get; set; }

	public int NPCGuardSpawnThreshold { get; set; }

	public double NPCGuardCreationMultiplier { get; set; }

	public bool RecruitsAreRecruitedFromVillages { get; set; }

	public int AmountOfUnitsToSpawn { get; set; }

	public int MinRecruitmentAmountFromVillages { get; set; }

	public int DailyEXPAmount { get; set; }

	public int DailyPrisonerConformityAmount { get; set; }

	public int DailyFoodGatheringAmount { get; set; }

	public bool DisableGarrisonNeedsFood { get; set; }

	public float GarrisonWageMultiplier { get; set; }

	public float GarrisonTrainingMultiplier { get; set; }

	public float GarrisonGuardsWageMultiplier { get; set; }

	public float CustomGarrisonSizeMultiplier { get; set; }

	public float MainPartySizeMultiplier { get; set; }

	public float PlayerClanPartySizeMultiplier { get; set; }

	public float AIClanPartySizeMultiplier { get; set; }

	public float GuardPrisonerSellThreshold { get; set; }

	public float GuardReplenishPercentage { get; set; }

	public float GuardAvailableTroopsPercentage { get; set; }

	public float PatrolPartyHealPercentage { get; set; }

	public bool SpawnOnlyNobleTroops { get; set; }

	public bool DefaultPlayerEnableTraining { get; set; }

	public bool DefaultGuardsEnableReplenish { get; set; }

	public bool DefaultPlayerEnablePrisonerRecruitment { get; set; }

	public bool DefaultAllowPrisonerRecruitAboveThreshold { get; set; }

	public bool DefaultPlayerEnableRecruitment { get; set; }

	public int DefaultPlayerMaxRecruitThreshold { get; set; }

	public int DefaultPlayerMaxUpgradeTier { get; set; }

	public bool DefaultOnlyRecruitElites { get; set; }

	public bool DefaultEnableGuardPrisonerSell { get; set; }

	public bool DefaultEnableGuardHideoutClear { get; set; }

	public bool DefaultEnableGuardBuyHorses { get; set; }

	public bool DefaultEnableGuardUpgrade { get; set; }

	public bool DefaultEnableGuardPrisonerRecruitment { get; set; }

	public int CustomTransferAndGuardPartySize { get; set; }

	public float CustomGuardAndTransferPartySpeed { get; set; }

	public bool LoadCustomPartySpeedModel { get; set; }

	public bool LoadCustomPartySizeModel { get; set; }

	public bool LoadFoodGatheringModule { get; set; }

	public bool EnablePlayerFoodBonus { get; set; }

	public void ResetToDefault()
	{
		Version = _latestVersion;
		EnableMapBannerTracker = true;
		DisableDailyMessage = false;
		EnableNPCWorldRecruitment = false;
		NPCMaxUpgradeTier = 3;
		RecruitsAreRecruitedFromVillages = true;
		MaximumNPCRecruitmentThreshold = 50;
		EnableNPCGarrisonTraining = false;
		EnabeNPCFoodbonus = false;
		NPCBonusFoodGathering = 10;
		EnableNPCPrisonerRecruitment = false;
		MinRecruitmentAmountFromVillages = 4;
		DailyEXPAmount = 20;
		LoadFoodGatheringModule = false;
		LoadCustomPartySizeModel = true;
		LoadCustomPartySpeedModel = false;
		DailyFoodGatheringAmount = 10;
		DailyPrisonerConformityAmount = 25;
		CustomGarrisonSizeMultiplier = 1f;
		CustomTransferAndGuardPartySize = 200;
		CustomGuardAndTransferPartySpeed = 4.8f;
		GarrisonWageMultiplier = 1f;
		GarrisonTrainingMultiplier = 1f;
		GarrisonGuardsWageMultiplier = 1f;
		DefaultPlayerEnablePrisonerRecruitment = true;
		DefaultPlayerEnableRecruitment = false;
		DefaultPlayerEnableTraining = false;
		EnablePlayerFoodBonus = false;
		DefaultPlayerMaxRecruitThreshold = 100;
		DefaultPlayerMaxUpgradeTier = 3;
		DefaultOnlyRecruitElites = false;
		DefaultAllowPrisonerRecruitAboveThreshold = true;
		AmountOfUnitsToSpawn = 5;
		GuardPrisonerSellThreshold = 0.25f;
		DefaultEnableGuardPrisonerSell = true;
		DefaultGuardsEnableReplenish = true;
		GuardReplenishPercentage = 0.5f;
		GuardAvailableTroopsPercentage = 0.25f;
		PatrolPartyHealPercentage = 0.5f;
		DisableGarrisonNeedsFood = false;
		SpawnOnlyNobleTroops = false;
		DefaultEnableGuardBuyHorses = false;
		DefaultEnableGuardHideoutClear = true;
		DefaultEnableGuardPrisonerRecruitment = false;
		DefaultEnableGuardUpgrade = true;
		NPCGuardSpawnThreshold = 120;
		NPCGuardCreationMultiplier = 0.4;
		NPCSpawnGuards = false;
	}

	public void UpdateVersion()
	{
		Version = _latestVersion;
		ConfigManager.Instance.CreateAndUpdateConfigForCurrentGame();
	}

	public string GetLatestVersionNumber()
	{
		return _latestVersion;
	}

	public bool VersionNumberIsUpToDate()
	{
		return Version.Equals(_latestVersion);
	}
}
