using System;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;

public abstract class ImprovedGarrisonSettings
{
	public readonly string str_space = " ";

	protected bool inquiryIsOpen = false;

	protected GarrisonBehavior garrisonBehavior => Main.GarrisonBehavior;

	protected bool CheckIfTownIsValid(Town town)
	{
		try
		{
			if (town == null || ((SettlementComponent)town).Settlement == null)
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return true;
	}
}
