using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataTypes;

[Serializable]
public class GlobalSettings
{
	private static GlobalSettings _instance = null;

	private static string _latestVersion = "c1.0.0";

	public string Version { get; set; } = _latestVersion;


	public bool DisableErrorMessage { get; set; }

	public bool EnableImprovedGarrisonsUIOnMap { get; set; }

	public static GlobalSettings Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new GlobalSettings();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public Dictionary<string, TrainingTemplate> TrainingTemplates { get; set; } = new Dictionary<string, TrainingTemplate>();


	public void AddTrainingTemplate(TrainingTemplate template)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Expected O, but got Unknown
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Expected O, but got Unknown
		if (!TemplateNameIsAlreadyUsed(template.Name))
		{
			TrainingTemplates.Add(template.Name, template);
			SaveSystemManager.Instance.SaveGlobalSettings();
			return;
		}
		string text = ((object)new TextObject("{=menu_template_alreadyused}Name already used", (Dictionary<string, object>)null)).ToString();
		string text2 = ((object)new TextObject("{=menu_template_alreadyused_desc}This template name is already used, Do you want to overwrite this template?", (Dictionary<string, object>)null)).ToString();
		InformationManager.ShowInquiry(new InquiryData(text, text2, true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			TrainingTemplates.Remove(template.Name);
			TrainingTemplates.Add(template.Name, template);
		}, (Action)null, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	private bool TemplateNameIsAlreadyUsed(string name)
	{
		try
		{
			foreach (string key in TrainingTemplates.Keys)
			{
				if (key.Equals(name))
				{
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public bool VersionNumberIsUpToDate()
	{
		return Version.Equals(_latestVersion);
	}
}
