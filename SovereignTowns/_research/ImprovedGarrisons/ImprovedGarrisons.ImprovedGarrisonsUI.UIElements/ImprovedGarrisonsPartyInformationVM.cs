using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.UIElements;

public class ImprovedGarrisonsPartyInformationVM : ViewModel
{
	private Action<MobileParty> onPressAction;

	private string _name;

	private string _troopAmount;

	private ImageIdentifierVM _visual;

	private float _distanceInTimeFloat;

	public MobileParty Party { get; private set; }

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (value != _name)
			{
				_name = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Name");
			}
		}
	}

	public string TroopAmount
	{
		get
		{
			return _troopAmount;
		}
		set
		{
			if (value != _troopAmount)
			{
				_troopAmount = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "TroopAmount");
			}
		}
	}

	public ImageIdentifierVM Visual
	{
		get
		{
			return _visual;
		}
		set
		{
			if (value != _visual)
			{
				_visual = value;
				((ViewModel)this).OnPropertyChangedWithValue<ImageIdentifierVM>(value, "Visual");
			}
		}
	}

	public string DistanceInTime => DistanceInTimeFloat + " " + ((object)new TextObject("{=misc_hour}h", (Dictionary<string, object>)null)).ToString();

	public float DistanceInTimeFloat
	{
		get
		{
			return _distanceInTimeFloat;
		}
		set
		{
			if (value != _distanceInTimeFloat)
			{
				_distanceInTimeFloat = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "DistanceInTimeFloat");
				((ViewModel)this).OnPropertyChanged("DistanceInTime");
			}
		}
	}

	public ImprovedGarrisonsPartyInformationVM(MobileParty party, Settlement settlementForDistance, Action<MobileParty> onPressAction)
	{
		if (party != null)
		{
			Party = party;
			Name = ((party.Party.Name != (TextObject)null) ? ((object)party.Party.Name).ToString() : ((object)((MBObjectBase)party).GetName()).ToString());
			SetVisualsForParty(party);
			SetTroopAmountStringForParty(party);
			SetDistance(party, settlementForDistance);
			this.onPressAction = onPressAction;
		}
	}

	public void ExecuteClick()
	{
		if (onPressAction != null)
		{
			onPressAction(Party);
		}
	}

	private void SetVisualsForParty(MobileParty party)
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		if (party == null)
		{
			return;
		}
		Tuple<CharacterObject, int> tuple = new Tuple<CharacterObject, int>(null, 0);
		if (party.LeaderHero != null)
		{
			tuple = new Tuple<CharacterObject, int>(party.LeaderHero.CharacterObject, -1);
		}
		else
		{
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)party.MemberRoster.GetTroopRoster())
			{
				TroopRosterElement current = item;
				if (tuple.Item1 == null || ((TroopRosterElement)(ref current)).Number > tuple.Item2)
				{
					tuple = new Tuple<CharacterObject, int>(current.Character, ((TroopRosterElement)(ref current)).Number);
				}
			}
		}
		if (tuple.Item1 != null)
		{
			ImageIdentifierVM visual = null;
			try
			{
				visual = (ImageIdentifierVM)new CharacterImageIdentifierVM(CampaignUIHelper.GetCharacterCode(tuple.Item1, false));
			}
			catch (Exception)
			{
			}
			Visual = visual;
		}
	}

	private void SetTroopAmountStringForParty(MobileParty party)
	{
		if (party != null)
		{
			int totalManCount = party.MemberRoster.TotalManCount;
			int totalWounded = party.MemberRoster.TotalWounded;
			TroopAmount = (totalManCount - totalWounded).ToString();
			if (totalWounded > 0)
			{
				TroopAmount = TroopAmount + "+" + totalWounded + "w";
			}
		}
	}

	private void SetDistance(MobileParty party, Settlement settlement)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (party == null || settlement == null)
			{
				DistanceInTimeFloat = 0f;
				return;
			}
			Main.GarrisonPartyBehavior.DetermineNavigationForSettlement(party, settlement, out var navigationType, out var isTargetingThePort);
			float num = default(float);
			float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(party, settlement, isTargetingThePort, navigationType, ref num);
			if (distance <= 0f || float.IsNaN(distance) || float.IsInfinity(distance))
			{
				DistanceInTimeFloat = 0f;
				return;
			}
			float num2 = ((Party != null) ? Party.Speed : party.Speed);
			if (num2 <= 0f)
			{
				DistanceInTimeFloat = 0f;
				return;
			}
			float distanceInTimeFloat = MathF.Ceiling(distance / num2);
			DistanceInTimeFloat = distanceInTimeFloat;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
