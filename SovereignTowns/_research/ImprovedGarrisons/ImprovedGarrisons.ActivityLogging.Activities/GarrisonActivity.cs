using System;
using TaleWorlds.CampaignSystem;

namespace ImprovedGarrisons.ActivityLogging.Activities;

[Serializable]
public abstract class GarrisonActivity
{
	public string CampaignDayOfTheActivity { get; private set; }

	public GarrisonActivity()
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		_ = CampaignTime.Now;
		CampaignTime now = CampaignTime.Now;
		CampaignDayOfTheActivity = ((object)(CampaignTime)(ref now)).ToString();
	}

	public abstract string GetLogDescription();
}
