using System;
using System.Collections.Generic;
using ImprovedGarrisons.ImprovedGarrisonsUI;
using ImprovedGarrisons.Menu;
using ImprovedGarrisons.SaveSystem.Configuration;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.Tutorial;

public class GarrisonMenuTutorial
{
	private MainMenu mainMenu = Main.GarrisonBehavior.MainMenu;

	private bool _isFullTutorial;

	private static GarrisonMenuTutorial _instance;

	private TutorialGauntlet tutorialGauntlet;

	public static GarrisonMenuTutorial Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new GarrisonMenuTutorial();
			}
			return _instance;
		}
	}

	public void StartTutorial()
	{
		_isFullTutorial = true;
		tutorialGauntlet = new TutorialGauntlet();
		Main.GarrisonBehavior.CurrentTownForSettings = Settlement.CurrentSettlement.Town;
	}

	private void ChangeTab(string tabId)
	{
		if (UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.improvedGarrisonsUI.SwitchToTab(tabId);
		}
	}

	private void Introduction()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Expected O, but got Unknown
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_introduction}Welcome to the Improved Garrisons tutorial. \n \nThis tutorial will explain the basic features of Improved Garrisons.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_cancel}Cancel tutorial", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					FindImprovedGarrisonMenuTutorial();
				});
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				OnEndTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	private void FindImprovedGarrisonMenuTutorial()
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected O, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Expected O, but got Unknown
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected O, but got Unknown
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected O, but got Unknown
		if (Settlement.CurrentSettlement.IsTown)
		{
			GameMenu.SwitchToMenu("town_keep");
		}
		else
		{
			GameMenu.SwitchToMenu("castle");
		}
		Main.GarrisonBehavior.CurrentTownForSettings = Settlement.CurrentSettlement.Town;
		if (UIManager.Instance.TryInitializeImprovedGarrisonsUI() || UIManager.Instance.improvedGarrisonsUI != null)
		{
			UIManager.Instance.CurrentUiState = UIManager.UiState.Expanded;
		}
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_findmenu}The Improved Garrisons option menu can be opened by pressing \n \n >> Improved Garrison \n \n in the Game Menu of your Castle or Town. \n(seen on the left side of your screen)", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					OverviewMenuTutorial();
				});
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				Introduction();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void OverviewMenuTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("OverviewTab");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_mainmenu}This is the [Improved Garrison Options Menu] \n \n Options:\n> Garrison Recruitment \n Automatically recruit new troops and prisoners for your Garrison. With this option you never have to manually put prisoners into your Garrison again.\n \n> Garrison Training \nSet your Improved Garrison to train garrisoned troops. You can select which and how many troops you want to train. With this option you can train an army like you want it.\n \n> Garrison Management\nCreate a new guard party for your Improved Garrison or transfer troops to another Garrison. You are also able to copy settings to other Improved Garrisons.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
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
				//IL_0068: Unknown result type (might be due to invalid IL or missing references)
				//IL_0074: Expected O, but got Unknown
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_mainmenu2}Options:\n> Garrison Guard Orders\nEach Improved Garrison may have one Guard party. Your Guards will take orders from you and keep your regions save.\n \n> Change the Options of another Garrison\nWith this option you may select another Improved Garrison you own to change its settings without having to travel to its location. There are many ways you can use this feature. If for example a kingdom declared war on you on the other side of the map, you could establish some Guards from another Improved Garrison to keep your borders save.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					if (_isFullTutorial)
					{
						Main.ExecuteActionOnNextTick(delegate
						{
							RecruitmentTutorial();
						});
					}
				}, (Action)delegate
				{
					if (_isFullTutorial)
					{
						OverviewMenuTutorial();
					}
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			});
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				FindImprovedGarrisonMenuTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void RecruitmentTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("RecruitmentTab");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_recruitment}This is the [Improved Garrison Recruitment Menu] \n \nOptions:\n> Recruit from region \nSet this if you want your Improved Garrison to automatically recruit new recruits from surrounding villages. You still have to have enough relation to the villages nobles. With this option you never have to manually fill your Garrison again.\n \n> Recruit Prisoners overtime \nSet this if you want your Improved Garrison to recruit prisoners from the castle or towns dungeons. Each prisoner in mount and blade has his own conformity value that needs to be reached in order to be recruitable. You could run around with your prisoner and fight to build up his conformity or use this option to gain conformity on a daily basis.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
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
				//IL_0068: Unknown result type (might be due to invalid IL or missing references)
				//IL_0074: Expected O, but got Unknown
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_recruitment2}This is the recruitment section. You can automatically recruit garrison troops from the fief's region, or establish a recruiter party to recruit troops from outside of the region.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					if (_isFullTutorial)
					{
						Main.ExecuteActionOnNextTick(delegate
						{
							GarrisonRecruitmentBehaviorTutorial();
						});
					}
				}, (Action)delegate
				{
					if (_isFullTutorial)
					{
						RecruitmentTutorial();
					}
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			});
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				OverviewMenuTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void GarrisonRecruitmentBehaviorTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_recruitment_garrisonrecruitmentbehavior");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_garrisonbehavior}This is the [Improved Garrison Recruitment Behavior Menu] \n \nOptions:\n> Set the maximum amount of troops to recruit.\nYour Improved Garrison will not recruit above the garrisons limits. With this option you can set a custom limit to which the recruitment will stop.\n \n> Recruit Prisoners above threshold\nThis allows the recruitment of prisoners even if the custom recruitment limit has been reached. If you want to recruit prisoners and also recruit nearby recruits your limit will be reached very fast. This would stop both recruitments. Therefore it is adviced to enable recruit prisoners above threshold to still be able to recruit prisoners if you want to.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					RecruiterBehaviorTutorial();
				});
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				OverviewMenuTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void RecruiterBehaviorTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_recruitment_recruiterbehavior");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_recruiterbehavior1}This is the [Improved Garrison Recruiter Behavior Menu] \n \nOptions: \n> Set amount of recruits\nThis is the maximum amount of recruits a Recruiter party will recruit before returning.\n \n> Only recruit Elite Troops\nThis will tell the Recruiter to only recruit Elite units. \n \n> Allow to buy Horses\nThis setting allows the Recruiter to buy horses from towns to gain more movement speed\n \n> Change the culture to recruit from\nSets the culture the Recruiter should look for when recruiting new troops\n \n> Order the Recruiter to return \nThe Recruiter will return to the garrison and put in all currently recruited units.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			Main.ExecuteActionOnNextTick(delegate
			{
				if (_isFullTutorial)
				{
					TrainingTutorial();
				}
			});
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				GarrisonRecruitmentBehaviorTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void TrainingTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_training");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_training}This is the [Improved Garrison Training Menu] \n \nOptions:\n> Train Troops in Garrison\nSet this if you want your Improved Garrison to train garrisoned troops.\n \n> Set the maximum Upgrade Tier\nThis will set the maximum tier your trained troops will be upgraded to.\n \n> Manage current training template\nHere you are able to exactly specify which troops you want your Improved Garrison to train your units towards. The units you select here are not affected by the tier restriction. Use this to compose your army as you want it. With this option you are able to set the amount of units that should be trained for each upgrade target you defined. You could for example, define the amount of infantry, archery or cavalary troops your army should have.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
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
				//IL_0068: Unknown result type (might be due to invalid IL or missing references)
				//IL_0074: Expected O, but got Unknown
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_training2}This is the training section of Improved Garrisons. In this section, you can tell each garrison to train its troops.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					if (_isFullTutorial)
					{
						Main.ExecuteActionOnNextTick(delegate
						{
							TemplateTutorial();
						});
					}
				}, (Action)delegate
				{
					TrainingTutorial();
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			});
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				RecruitmentTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void TemplateTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_template");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_template}This is the [Improved Garrison Training Template Menu] \n \nOptions:\n> Template Manager\nThe template manager is used to apply, inspect or remove your training templates. This templates save upgrade paths that the Improved Garrison should take while training its troops.\n \n> Add new Training Template\n This saves your current setup of upgrade paths and amount as a template. This templates are shared for each of your game saves and garrisons.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					ManagementTutorial();
				});
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				TrainingTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void ManagementTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_manage");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_management}This is the [Improved Garrison Management Menu] \n \nOptions:\n> Establish a new Garrisonguard party\nHere you are able to setup a new Guard party for your Garrison. The guards have a new smart AI and many features like defending your fiefs, helping your clans parties, selling prisoners and much more.\n \n> Transfer troops to another garrison\n With this option you are able to transfer troops of your current garrison to another.\n \n> Copy settings\nThis option copies all of your current garrisons settings to another.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					OrderTutorial();
				});
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				TemplateTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void OrderTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_order");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_order}This is the [Improved Garrison Order Menu] \n \nOptions:\n> Order to Patrol\nOrder the Garrison Guard of this garrison to patrol. On patrol your Guards will walk around your region and defend it against enemy intruders, looters and bandits. If a village, castle or town of the current patrol region is attacked they will try to defend it. They will only attack a party if they are capable of defeating it.\n \n> Order to Support\nChoose a party of your clan your guards should support. You could use this option if you want your guards to follow and help you on your next war. They will then support you on sieges and field battles. You could also use this option to tell your guards to defend a fellow clan party like a caravan or companion. You are even able to select other guards to build large guard patrols which could be useful to defend your kingdoms borders.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
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
				//IL_0068: Unknown result type (might be due to invalid IL or missing references)
				//IL_0074: Expected O, but got Unknown
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_order2}Options:\n> Order to return to garrison\n This will order your Guards to return. The party will be disbanded upon arriving at their home town/castle and the troops will go back into the garrison. They will also sell all there captured prisoners and loot.\n \n> Set the guards behaviours\nThis opens a submenu which allows you to set different rules or behaviors the guards AI should follow.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
				{
					if (_isFullTutorial)
					{
						Main.ExecuteActionOnNextTick(delegate
						{
							GuardBehaviorTutorial();
						});
					}
				}, (Action)delegate
				{
					GuardBehaviorTutorial();
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			});
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				ManagementTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void GuardBehaviorTutorial()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		ChangeTab("garrison_options_guardbehavior");
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_guardbehavior}This is the [Improved Garrison Guard Behavior Menu] \n \nOptions:\n> Allow to replenish\nThis allows your Guards to go back to their home town/castle to refill their lost troops and to heal if they are wounded. With this option your Guards are pretty much self sufficient. But they will only replenish troop types that were in the initial guard party setup. Therefore make sure to have these produced by the Improved Garrison for the guards to pick up later. \n \n> Allow to sell prisoners \nAllow your guards to sell captured prisoners. \n \n> Allow to clear hideouts\nThis allows your guards to clear hideouts they find while patroling.\n \n> Allow to buy horses\nThis allows your guards to buy horses from towns to get more movement speed by getting the footman on horses perk.", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.ExecuteActionOnNextTick(delegate
				{
					ConfigTutorial();
				});
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				OrderTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	public void ConfigTutorial()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected O, but got Unknown
		Main.OpenConfigurationScreen();
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_config}This is the [Improved Garrison Configuration Menu] \n \nIt is accessible anytime while on the map by pressing [left ALT] + [G]. \n \nHere you are able to adjust many mods values as you'd like. You could for example:\n> Change the daily training exp\n> lower your Guards and Garrisons costs\n> Increase the speed your prisoners are recruited\n> Load the food gathering module\n> Activate the mod for the npc factions\n> Change the default values that are set upon getting a new fief\n> Activate cheat settings\n> ... and more!", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				EndTutorial();
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				Main.CloseConfigurationScreen();
				ChangeTab("garrison_options_manage");
				OrderTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	private void EndTutorial()
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Expected O, but got Unknown
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Expected O, but got Unknown
		Main.CloseConfigurationScreen();
		InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=tutorial_title}Improved Garrisons tutorial", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=tutorial_thankyou}Aaand you made it through the tutorial!! :-)\n \nI personally want to thank you for using Improved Garrisons! I put countless hours of work in this project and seeing how many people enjoy the mod is truly amazing.\n \nIf you like my work, it would be awesome to hear back from you. Consider leaving a comment and endorsement on the Nexus Mod Page. And if you want to support my work, you can donate me a coffee for my late night development sessions :-)\n \nOn to a great journey of Mount and Blade \n ~ Sidies", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_continue}Continue", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_back}Back", (Dictionary<string, object>)null)).ToString(), (Action)delegate
		{
			if (_isFullTutorial)
			{
				OnEndTutorial();
			}
		}, (Action)delegate
		{
			if (_isFullTutorial)
			{
				ConfigTutorial();
			}
		}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
	}

	private void OnEndTutorial()
	{
		_isFullTutorial = false;
		ConfigManager.Instance.Config.DeactivateTutorial = true;
		ConfigManager.Instance.CreateAndUpdateConfigForCurrentGame();
		if (Settlement.CurrentSettlement.IsTown)
		{
			GameMenu.SwitchToMenu("town_keep");
		}
		else
		{
			GameMenu.SwitchToMenu("castle");
		}
	}
}
