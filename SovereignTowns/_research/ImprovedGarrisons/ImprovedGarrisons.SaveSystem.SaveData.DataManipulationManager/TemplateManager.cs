using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataManipulationManager;

public class TemplateManager : ImprovedGarrisonSettings
{
	private enum ManagementType
	{
		Apply,
		Remove,
		Rename,
		Inspect
	}

	private TrainingTemplate _currentTrainingTemplate;

	private TrainingUIVM _trainingDataSource;

	private Town _currentTown;

	private static TemplateManager _instance;

	public static TemplateManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new TemplateManager();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public void PromptTemplateManager(Town town, TrainingUIVM trainingDataSource = null)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Expected O, but got Unknown
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected O, but got Unknown
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Expected O, but got Unknown
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Expected O, but got Unknown
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Expected O, but got Unknown
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Expected O, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Expected O, but got Unknown
		try
		{
			if (!CheckIfTownIsValid(town))
			{
				return;
			}
			List<InquiryElement> list = new List<InquiryElement>();
			List<TrainingTemplate> list2 = GlobalSettings.Instance.TrainingTemplates.Values.ToList();
			_currentTown = town;
			if (trainingDataSource != null)
			{
				_trainingDataSource = trainingDataSource;
			}
			list.Add(new InquiryElement((object)null, ((object)new TextObject("{=settings_templatemanager_savetemplate}Save current template", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(new Banner("11.123.97.1836.1836.768.788.1.0.-30.505.0.0.34.317.777.771.1.0.90.505.0.0.34.317.777.771.1.0.-1"), false)));
			foreach (TrainingTemplate item in list2)
			{
				list.Add(new InquiryElement((object)item, item.Name, item.GetImage()));
			}
			string text = ((object)new TextObject("{=settings_templatemanager_title}Training templates", (Dictionary<string, object>)null)).ToString();
			string text2 = ((object)new TextObject("{=settings_templatemanager_desc}Choose a training template.", (Dictionary<string, object>)null)).ToString();
			MultiSelectionInquiryData val = new MultiSelectionInquiryData(text, text2, list, true, 1, 1, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)Inquirydata_TemplateManager, (Action<List<InquiryElement>>)null, "", false);
			MBInformationManager.ShowMultiSelectionInquiry(val, false, false);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void AddNewTemplate(List<InquiryElement> list = null)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Expected O, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected O, but got Unknown
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Expected O, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Expected O, but got Unknown
		try
		{
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(_currentTown);
			if (townSettings.Template.AmountOfTroopsInTemplate <= 0)
			{
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_template_nosetup}Your setup currently doesn't have training units.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(13897216u)));
				return;
			}
			string text = ((object)new TextObject("{=settings_templatemanager_addnew1}Add a new template", (Dictionary<string, object>)null)).ToString();
			string text2 = ((object)new TextObject("{=settings_templatemanager_addnew2}This adds the current training setup to your template manager. The template will be accessible across all of your saves. \n \nPlease select a name for your template.", (Dictionary<string, object>)null)).ToString();
			InformationManager.ShowTextInquiry(new TextInquiryData(text, text2, true, true, ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<string>)InquiryData_NewTemplate, (Action)null, false, (Func<string, Tuple<bool, string>>)null, "", ""), false, false);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_TemplateManager(List<InquiryElement> list)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Expected O, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Expected O, but got Unknown
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Expected O, but got Unknown
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Expected O, but got Unknown
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Expected O, but got Unknown
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Expected O, but got Unknown
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Expected O, but got Unknown
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Expected O, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Expected O, but got Unknown
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Expected O, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Expected O, but got Unknown
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Expected O, but got Unknown
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Expected O, but got Unknown
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_015c: Expected O, but got Unknown
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Expected O, but got Unknown
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Expected O, but got Unknown
		try
		{
			if (list == null || list.Count <= 0)
			{
				return;
			}
			_currentTrainingTemplate = (TrainingTemplate)list.First().Identifier;
			if (_currentTrainingTemplate == null)
			{
				AddNewTemplate();
				return;
			}
			Banner val = new Banner("11.116.1.1836.1836.768.788.1.0.-30.510.122.122.304.296.685.749.1.0.270.527.122.122.197.183.759.680.1.0.180.510.122.122.162.262.811.827.1.0.303");
			Banner val2 = new Banner("11.116.1.1836.1836.768.788.1.0.-30.510.122.122.306.296.764.743.1.0.90");
			Banner val3 = new Banner("11.116.1.1836.1836.768.788.1.0.-30.510.122.122.306.296.831.730.1.0.108.510.122.122.132.296.764.757.1.0.-1.510.122.122.306.296.692.730.1.1.71.510.122.122.88.296.761.601.1.1.0");
			Banner val4 = new Banner("11.116.1.1836.1836.768.788.1.0.-30.510.122.122.359.296.764.732.1.0.45.510.122.122.359.296.764.732.1.1.315");
			List<InquiryElement> list2 = new List<InquiryElement>();
			list2.Add(new InquiryElement((object)ManagementType.Apply, ((object)new TextObject("{=settings_recruitmentsettings_apply}Apply template", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(val3, false)));
			list2.Add(new InquiryElement((object)ManagementType.Inspect, ((object)new TextObject("{=settings_recruitmentsettings_inspect}Inspect template", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(val2, false)));
			list2.Add(new InquiryElement((object)ManagementType.Rename, ((object)new TextObject("{=settings_recruitmentsettings_rename}Rename template", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(val, false)));
			list2.Add(new InquiryElement((object)ManagementType.Remove, ((object)new TextObject("{=settings_recruitmentsettings_remove}Delete template", (Dictionary<string, object>)null)).ToString(), (ImageIdentifier)new BannerImageIdentifier(val4, false)));
			string text = ((object)new TextObject("{=settings_recruitmentsettings_manage}Manage template", (Dictionary<string, object>)null)).ToString();
			MultiSelectionInquiryData data = new MultiSelectionInquiryData(text, (string)null, list2, true, 1, 1, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action<List<InquiryElement>>)Inquirydata_AddTemplate, (Action<List<InquiryElement>>)null, "", false);
			Main.ExecuteActionOnNextTick(delegate
			{
				MBInformationManager.ShowMultiSelectionInquiry(data, false, false);
			});
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void Inquirydata_AddTemplate(List<InquiryElement> list)
	{
		try
		{
			if (list == null || list.Count <= 0)
			{
				return;
			}
			switch ((ManagementType)list.First().Identifier)
			{
			case ManagementType.Apply:
				Main.ExecuteActionOnNextTick(delegate
				{
					ApplyTemplate(_currentTrainingTemplate);
				});
				break;
			case ManagementType.Rename:
				Main.ExecuteActionOnNextTick(delegate
				{
					//IL_0007: Unknown result type (might be due to invalid IL or missing references)
					//IL_0011: Expected O, but got Unknown
					//IL_0017: Unknown result type (might be due to invalid IL or missing references)
					//IL_0021: Expected O, but got Unknown
					//IL_0029: Unknown result type (might be due to invalid IL or missing references)
					//IL_0033: Expected O, but got Unknown
					//IL_0039: Unknown result type (might be due to invalid IL or missing references)
					//IL_0043: Expected O, but got Unknown
					//IL_0067: Unknown result type (might be due to invalid IL or missing references)
					//IL_0073: Expected O, but got Unknown
					InformationManager.ShowTextInquiry(new TextInquiryData(((object)new TextObject("{=settings_recruitmentsettings_rename}Rename template", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=settings_recruitmentsettings_renamedesc}Enter a new name for your template.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_no}No", (Dictionary<string, object>)null)).ToString(), (Action<string>)delegate(string x)
					{
						RenameCurrentTemplate(x);
						PromptTemplateManager(_currentTown);
					}, (Action)delegate
					{
						PromptTemplateManager(_currentTown);
					}, false, (Func<string, Tuple<bool, string>>)null, "", ""), false, false);
				});
				break;
			case ManagementType.Remove:
				RemoveTemplate(_currentTrainingTemplate);
				PromptTemplateManager(_currentTown);
				break;
			case ManagementType.Inspect:
				InspectTemplate(_currentTrainingTemplate);
				break;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void ApplyTemplate(TrainingTemplate template)
	{
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Expected O, but got Unknown
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected O, but got Unknown
		try
		{
			GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(base.garrisonBehavior.CurrentTownForSettings);
			Dictionary<string, int> troopList = template.GetTroopList();
			if (troopList != null)
			{
				townSettings.Template = template;
				if (base.garrisonBehavior.SettlementSettingsData.TryGetValue(((object)((SettlementComponent)_currentTown).Name).ToString(), out var _))
				{
					base.garrisonBehavior.SettlementSettingsData[((object)((SettlementComponent)_currentTown).Name).ToString()] = townSettings;
				}
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_recruitmentsettings_yourtemplate}Your template", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + _currentTrainingTemplate.Name + ModuleStrings._space + ((object)new TextObject("{=info_template_apply}has been applied.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
				if (_trainingDataSource != null)
				{
					_trainingDataSource.TroopListIsDirty = true;
					_trainingDataSource = null;
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void InspectTemplate(TrainingTemplate template)
	{
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Expected O, but got Unknown
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Expected O, but got Unknown
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Expected O, but got Unknown
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Expected O, but got Unknown
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Expected O, but got Unknown
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Expected O, but got Unknown
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Expected O, but got Unknown
		try
		{
			List<InquiryElement> list2 = new List<InquiryElement>();
			Dictionary<string, int> troopList = template.GetTroopList();
			if (troopList == null)
			{
				return;
			}
			foreach (KeyValuePair<string, int> item in troopList)
			{
				CharacterObject @object = MBObjectManager.Instance.GetObject<CharacterObject>(item.Key);
				if (@object != null)
				{
					string text = ((item.Value >= 0 && item.Value <= 999) ? ("[" + item.Value + "] ") : ("[" + ((object)new TextObject("{=menu_train_pathamount_restr}no restr.", (Dictionary<string, object>)null)).ToString() + "] "));
					ImageIdentifier val = null;
					try
					{
						val = (ImageIdentifier)new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(@object, false));
					}
					catch (Exception)
					{
					}
					if (val == null)
					{
						val = (ImageIdentifier)new EmptyImageIdentifier();
					}
					list2.Add(new InquiryElement((object)@object, text + (object)((BasicCharacterObject)@object).Name, val));
				}
			}
			string text2 = ((object)new TextObject("{=settings_recruitmentsettings_inspect2}Inspect", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + template.Name;
			string text3 = ((object)new TextObject("{=settings_recruitmentsettings_yourtemplate}Your template", (Dictionary<string, object>)null)).ToString();
			MultiSelectionInquiryData val2 = new MultiSelectionInquiryData(text2, "", list2, false, 0, 1, ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (string)null, (Action<List<InquiryElement>>)delegate
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					PromptTemplateManager(_currentTown);
				});
			}, (Action<List<InquiryElement>>)null, "", false);
			MBInformationManager.ShowMultiSelectionInquiry(val2, false, false);
		}
		catch (Exception ex2)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex2);
		}
	}

	private void RenameCurrentTemplate(string templateName)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		try
		{
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_recruitmentsettings_yourtemplate}Your template", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + _currentTrainingTemplate.Name + ModuleStrings._space + ((object)new TextObject("{=info_template_rename}has been renamed to", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + templateName, Color.FromUint(ModuleColors.green)));
			GlobalSettings.Instance.TrainingTemplates.Remove(_currentTrainingTemplate.Name);
			_currentTrainingTemplate.Name = templateName;
			GlobalSettings.Instance.TrainingTemplates.Add(templateName, _currentTrainingTemplate);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void RemoveTemplate(TrainingTemplate template)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected O, but got Unknown
		try
		{
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_recruitmentsettings_yourtemplate}Your template", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + _currentTrainingTemplate.Name + ModuleStrings._space + ((object)new TextObject("{=info_template_remove}has been removed.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
			GlobalSettings.Instance.TrainingTemplates.Remove(template.Name);
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void InquiryData_NewTemplate(string templateName)
	{
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected O, but got Unknown
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Expected O, but got Unknown
		try
		{
			if (templateName != null && templateName.Length > 0)
			{
				GarrisonSettings townSettings = base.garrisonBehavior.GetTownSettings(base.garrisonBehavior.CurrentTownForSettings);
				Dictionary<string, int> troopList = townSettings.Template.GetTroopList();
				TrainingTemplate trainingTemplate = new TrainingTemplate(templateName);
				trainingTemplate.SetTroops(troopList);
				InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=settings_recruitmentsettings_yourtemplate}Your template", (Dictionary<string, object>)null)).ToString() + ModuleStrings._space + trainingTemplate.Name + ModuleStrings._space + ((object)new TextObject("{=info_template_add}has been added.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
				GlobalSettings.Instance.AddTrainingTemplate(trainingTemplate);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
