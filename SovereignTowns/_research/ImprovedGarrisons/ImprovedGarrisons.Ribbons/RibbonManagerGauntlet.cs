using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.AI.AITypes;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using SandBox.View;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.ScreenSystem;

namespace ImprovedGarrisons.Ribbons;

[OverrideView(typeof(Ribbon))]
public class RibbonManagerGauntlet : MapView
{
	protected RibbonManagerVM ribbonManagerDataSource;

	private string RibbonGauntletID = "RibbonGauntlet";

	protected GauntletLayer _ribbonLayer;

	public RibbonManagerGauntlet()
	{
		ribbonManagerDataSource = new RibbonManagerVM();
	}

	protected override void CreateLayout()
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		((MapView)this).CreateLayout();
		if (_ribbonLayer != null)
		{
			CloseAllRibbons();
		}
		_ribbonLayer = new GauntletLayer(RibbonGauntletID, 105, false);
		_ribbonLayer.LoadMovie("ImprovedGarrisonsRibbonManager", (ViewModel)(object)ribbonManagerDataSource);
		((ScreenLayer)_ribbonLayer).InputRestrictions.SetInputRestrictions(false, (InputUsageMask)5);
		((ScreenBase)MapScreen.Instance).AddLayer((ScreenLayer)(object)_ribbonLayer);
	}

	public void OpenAllRibbonsForGarrison(Town town)
	{
		SetTownRibbons(town);
		((MapView)this).CreateLayout();
	}

	public void CloseAllRibbons()
	{
		GetCurrentRibbonFromMapScreen();
		if (_ribbonLayer != null && ribbonManagerDataSource != null)
		{
			((ScreenBase)MapScreen.Instance).RemoveLayer((ScreenLayer)(object)_ribbonLayer);
			_ribbonLayer = null;
			ribbonManagerDataSource.RemoveAllRibbons();
		}
	}

	public void UpdateRibbons()
	{
		CloseAllRibbons();
		OpenAllRibbonsForGarrison(Main.GarrisonBehavior.CurrentTownForSettings);
		if (_ribbonLayer != null)
		{
			((ScreenBase)MapScreen.Instance).UpdateLayout();
		}
	}

	public void GetCurrentRibbonFromMapScreen()
	{
		try
		{
			bool flag = false;
			foreach (ScreenLayer item in ((IEnumerable<ScreenLayer>)((ScreenBase)MapScreen.Instance).Layers).ToList())
			{
				GauntletLayer val = (GauntletLayer)(object)((item is GauntletLayer) ? item : null);
				if (val != null && item.Name == RibbonGauntletID)
				{
					if (!flag)
					{
						_ribbonLayer = val;
						flag = true;
					}
					else
					{
						((ScreenBase)MapScreen.Instance).RemoveLayer(item);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void AddNewRibbon(string title, string text)
	{
		ribbonManagerDataSource.AddRibbon(title, text);
	}

	private void SetTownRibbons(Town town)
	{
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected O, but got Unknown
		try
		{
			if (town != null)
			{
				GarrisonSettings townSettings = Main.GarrisonBehavior.GetTownSettings(town);
				MobileGarrison mobileGarrisonPartyOfSettlement = Main.PartyManagement.mobileGarrisonManagement.GetMobileGarrisonPartyOfSettlement(((SettlementComponent)town).Settlement);
				GarrisonRecruiter recruiterOfSettlement = Main.PartyManagement.garrisonRecruiterPartyManagement.GetRecruiterOfSettlement(((SettlementComponent)town).Settlement);
				if (mobileGarrisonPartyOfSettlement != null)
				{
					string title = ((object)new TextObject("{=party_guards}Garrison guard", (Dictionary<string, object>)null)).ToString();
					string statusText = mobileGarrisonPartyOfSettlement.GetStatusText();
					statusText = statusText.First().ToString().ToUpper() + statusText.Substring(1);
					AddNewRibbon(title, statusText);
				}
				if (recruiterOfSettlement != null)
				{
					string title2 = ((object)new TextObject("{=party_recruiter}Garrison recruiter", (Dictionary<string, object>)null)).ToString();
					string statusText2 = recruiterOfSettlement.GetStatusText();
					AddNewRibbon(title2, statusText2);
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	protected override void OnFinalize()
	{
		((SandboxView)this).OnFinalize();
		CloseAllRibbons();
	}
}
