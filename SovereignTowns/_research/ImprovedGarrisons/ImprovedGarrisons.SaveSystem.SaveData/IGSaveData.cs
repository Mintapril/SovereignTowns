using System;
using System.Collections.Generic;
using ImprovedGarrisons.ActivityLogging;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;

namespace ImprovedGarrisons.SaveSystem.SaveData;

[Serializable]
public class IGSaveData
{
	private static IGSaveData _instance;

	private Dictionary<string, GarrisonSettings> _settlementSettingsData;

	private Dictionary<string, ActivityLog> _activityLogs;

	public static IGSaveData Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new IGSaveData();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public Dictionary<string, GarrisonSettings> SettlementSettingsData
	{
		get
		{
			if (_settlementSettingsData == null)
			{
				_settlementSettingsData = new Dictionary<string, GarrisonSettings>();
			}
			return _settlementSettingsData;
		}
		set
		{
			_settlementSettingsData = value;
		}
	}

	public Dictionary<string, ActivityLog> ActivityLogs
	{
		get
		{
			if (_activityLogs == null)
			{
				_activityLogs = new Dictionary<string, ActivityLog>();
			}
			return _activityLogs;
		}
		set
		{
			_activityLogs = value;
		}
	}
}
