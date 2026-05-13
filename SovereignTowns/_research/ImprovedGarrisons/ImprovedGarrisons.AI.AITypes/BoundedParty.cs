using System;
using Helpers;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ImprovedGarrisons.AI.AITypes;

public abstract class BoundedParty : ImprovedPartyAi
{
	public Settlement fromSettlement;

	private GarrisonSettings _homeGarrisonSettings;

	private int hourCounterStuck = 0;

	public GarrisonSettings homeGarrisonSettings
	{
		get
		{
			if (_homeGarrisonSettings == null)
			{
				_homeGarrisonSettings = Main.GarrisonBehavior.GetTownSettings(fromSettlement.Town);
			}
			return _homeGarrisonSettings;
		}
	}

	private float stuckDistance { get; set; }

	public override bool ReplenishEnabled => homeGarrisonSettings.EnableReplenish;

	protected BoundedParty(MobileParty party, Settlement home)
		: base(party)
	{
		fromSettlement = home;
	}

	public override void HourlyThinkBehavior()
	{
		base.HourlyThinkBehavior();
		IfStuckPortToHome();
	}

	protected void IfStuckPortToHome()
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		float distanceToHome = GetDistanceToHome();
		if ((double)Math.Abs(distanceToHome - stuckDistance) < 0.1 && mobileParty.MapEvent == null)
		{
			if (hourCounterStuck > 0 && hourCounterStuck % 48 == 0)
			{
				mobileParty.Position = fromSettlement.GatePosition;
			}
		}
		else
		{
			hourCounterStuck = 0;
		}
		stuckDistance = distanceToHome;
	}

	public float GetDistanceToHome()
	{
		NavigationType val = default(NavigationType);
		float result = default(float);
		bool flag = default(bool);
		AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(mobileParty, fromSettlement, mobileParty.HasNavalNavigationCapability, ref val, ref result, ref flag);
		return result;
	}

	public override void NextHour()
	{
		base.NextHour();
		hourCounterStuck++;
	}
}
