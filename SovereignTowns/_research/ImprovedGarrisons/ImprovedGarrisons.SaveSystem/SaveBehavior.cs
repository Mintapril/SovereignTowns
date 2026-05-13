using System;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using TaleWorlds.CampaignSystem;

namespace ImprovedGarrisons.SaveSystem;

public class SaveBehavior : CampaignBehaviorBase
{
	public override void RegisterEvents()
	{
		CampaignEvents.OnSaveOverEvent.AddNonSerializedListener((object)this, (Action<bool, string>)OnSaveEvent);
		CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener((object)this, (Action)OnBeforeSaveEvent);
		CampaignEvents.OnSaveOverEvent.AddNonSerializedListener((object)this, (Action<bool, string>)OnAfterSaveEvent);
		CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener((object)this, (Action<CampaignGameStarter>)OnLoadEvent);
	}

	public override void SyncData(IDataStore dataStore)
	{
		try
		{
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnBeforeSaveEvent()
	{
		try
		{
			if (ConfigManager.Instance.Config.EnableMapBannerTracker)
			{
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnAfterSaveEvent(bool sucessful, string name)
	{
		try
		{
			if (ConfigManager.Instance.Config.EnableMapBannerTracker)
			{
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnSaveEvent(bool sucessful, string name)
	{
		if (sucessful && name != null)
		{
			SaveSystemManager.Instance.SaveSettlementSaveData(name);
			SaveSystemManager.Instance.SaveGlobalSettings();
			ConfigManager.Instance.CreateAndUpdateConfig(name);
		}
	}

	private void OnLoadEvent(CampaignGameStarter starter)
	{
		SaveSystemManager.Instance.LoadSettlementSaveDataAndGlobalSettings(starter);
	}
}
