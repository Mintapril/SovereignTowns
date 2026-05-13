using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImprovedGarrisons.ConfigOptionsMenu.Options;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem;
using ImprovedGarrisons.SaveSystem.Configuration;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using ImprovedGarrisons.Utils;
using TaleWorlds.Core;
using TaleWorlds.Engine.Options;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu;

public class ConfigMenuVM : ViewModel
{
	public enum OptionsDataType
	{
		None = -1,
		BooleanOption = 0,
		NumericOption = 1,
		MultipleSelectionOption = 3,
		InputOption = 4,
		ActionOption = 5,
		Title = 6
	}

	[CompilerGenerated]
	private sealed class _003Cget_BaseOptionsList_003Ed__50 : IEnumerable<IOptionData>, IEnumerable, IEnumerator<IOptionData>, IDisposable, IEnumerator
	{
		private int _003C_003E1__state;

		private IOptionData _003C_003E2__current;

		private int _003C_003El__initialThreadId;

		public ConfigMenuVM _003C_003E4__this;

		private string _003Cname_003E5__1;

		private string _003Cdescription_003E5__2;

		private float _003Cvalue_003E5__3;

		private Action<float> _003Caction_003E5__4;

		private string _003Cname_003E5__5;

		private string _003Cdescription_003E5__6;

		private string _003CextraDescription_003E5__7;

		private float _003Cvalue_003E5__8;

		private Action<float> _003Caction_003E5__9;

		private string _003Cname_003E5__10;

		private string _003Cdescription_003E5__11;

		private string _003CextraDescription_003E5__12;

		private float _003Cvalue_003E5__13;

		private Action<float> _003Caction_003E5__14;

		private string _003Cname_003E5__15;

		private string _003Cdescription_003E5__16;

		private string _003Crequirements_003E5__17;

		private float _003Cvalue_003E5__18;

		private Action<bool> _003Caction_003E5__19;

		private string _003Cname_003E5__20;

		private string _003Cdescription_003E5__21;

		private string _003CextraDescription_003E5__22;

		private string _003Crequirements_003E5__23;

		private float _003Cvalue_003E5__24;

		private Action<float> _003Caction_003E5__25;

		private string _003Cname_003E5__26;

		private string _003Cdescription_003E5__27;

		private string _003Crequirements_003E5__28;

		private float _003Cvalue_003E5__29;

		private Action<bool> _003Caction_003E5__30;

		private string _003Cname_003E5__31;

		private string _003Cdescription_003E5__32;

		private string _003CextraDescription_003E5__33;

		private string _003Crequirements_003E5__34;

		private float _003Cvalue_003E5__35;

		private Action<float> _003Caction_003E5__36;

		private string _003Cname_003E5__37;

		private string _003Cdescription_003E5__38;

		private string _003CextraDescription_003E5__39;

		private string _003Crequirements_003E5__40;

		private float _003Cvalue_003E5__41;

		private Action<float> _003Caction_003E5__42;

		private string _003Cname_003E5__43;

		private string _003Cdescription_003E5__44;

		private string _003CextraDescription_003E5__45;

		private float _003Cvalue_003E5__46;

		private Action<float> _003Caction_003E5__47;

		private string _003Cname_003E5__48;

		private string _003Cdescription_003E5__49;

		private string _003CextraDescription_003E5__50;

		private float _003Cvalue_003E5__51;

		private Action<float> _003Caction_003E5__52;

		private string _003Cname_003E5__53;

		private string _003Cdescription_003E5__54;

		private float _003Cvalue_003E5__55;

		private Action<bool> _003Caction_003E5__56;

		private string _003Cname_003E5__57;

		private string _003Cdescription_003E5__58;

		private string _003Crequirements_003E5__59;

		private float _003Cvalue_003E5__60;

		private Action<bool> _003Caction_003E5__61;

		private string _003Cname_003E5__62;

		private string _003Cdescription_003E5__63;

		private string _003Crequirements_003E5__64;

		private float _003Cvalue_003E5__65;

		private Action<bool> _003Caction_003E5__66;

		private string _003Cname_003E5__67;

		private string _003Cdescription_003E5__68;

		private string _003Crequirements_003E5__69;

		private string _003CextraDescription_003E5__70;

		private float _003Cvalue_003E5__71;

		private Action<bool> _003Caction_003E5__72;

		private string _003Cname_003E5__73;

		private string _003Cdescription_003E5__74;

		private string _003Crequirements_003E5__75;

		private float _003Cvalue_003E5__76;

		private Action<bool> _003Caction_003E5__77;

		private string _003Cname_003E5__78;

		private string _003Cdescription_003E5__79;

		private string _003Crequirements_003E5__80;

		private float _003Cvalue_003E5__81;

		private Action<bool> _003Caction_003E5__82;

		private string _003Cname_003E5__83;

		private string _003Cdescription_003E5__84;

		private string _003CextraDescription_003E5__85;

		private float _003Cvalue_003E5__86;

		private Action<bool> _003Caction_003E5__87;

		IOptionData IEnumerator<IOptionData>.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		[DebuggerHidden]
		public _003Cget_BaseOptionsList_003Ed__50(int _003C_003E1__state)
		{
			this._003C_003E1__state = _003C_003E1__state;
			_003C_003El__initialThreadId = Environment.CurrentManagedThreadId;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			_003Cname_003E5__1 = null;
			_003Cdescription_003E5__2 = null;
			_003Caction_003E5__4 = null;
			_003Cname_003E5__5 = null;
			_003Cdescription_003E5__6 = null;
			_003CextraDescription_003E5__7 = null;
			_003Caction_003E5__9 = null;
			_003Cname_003E5__10 = null;
			_003Cdescription_003E5__11 = null;
			_003CextraDescription_003E5__12 = null;
			_003Caction_003E5__14 = null;
			_003Cname_003E5__15 = null;
			_003Cdescription_003E5__16 = null;
			_003Crequirements_003E5__17 = null;
			_003Caction_003E5__19 = null;
			_003Cname_003E5__20 = null;
			_003Cdescription_003E5__21 = null;
			_003CextraDescription_003E5__22 = null;
			_003Crequirements_003E5__23 = null;
			_003Caction_003E5__25 = null;
			_003Cname_003E5__26 = null;
			_003Cdescription_003E5__27 = null;
			_003Crequirements_003E5__28 = null;
			_003Caction_003E5__30 = null;
			_003Cname_003E5__31 = null;
			_003Cdescription_003E5__32 = null;
			_003CextraDescription_003E5__33 = null;
			_003Crequirements_003E5__34 = null;
			_003Caction_003E5__36 = null;
			_003Cname_003E5__37 = null;
			_003Cdescription_003E5__38 = null;
			_003CextraDescription_003E5__39 = null;
			_003Crequirements_003E5__40 = null;
			_003Caction_003E5__42 = null;
			_003Cname_003E5__43 = null;
			_003Cdescription_003E5__44 = null;
			_003CextraDescription_003E5__45 = null;
			_003Caction_003E5__47 = null;
			_003Cname_003E5__48 = null;
			_003Cdescription_003E5__49 = null;
			_003CextraDescription_003E5__50 = null;
			_003Caction_003E5__52 = null;
			_003Cname_003E5__53 = null;
			_003Cdescription_003E5__54 = null;
			_003Caction_003E5__56 = null;
			_003Cname_003E5__57 = null;
			_003Cdescription_003E5__58 = null;
			_003Crequirements_003E5__59 = null;
			_003Caction_003E5__61 = null;
			_003Cname_003E5__62 = null;
			_003Cdescription_003E5__63 = null;
			_003Crequirements_003E5__64 = null;
			_003Caction_003E5__66 = null;
			_003Cname_003E5__67 = null;
			_003Cdescription_003E5__68 = null;
			_003Crequirements_003E5__69 = null;
			_003CextraDescription_003E5__70 = null;
			_003Caction_003E5__72 = null;
			_003Cname_003E5__73 = null;
			_003Cdescription_003E5__74 = null;
			_003Crequirements_003E5__75 = null;
			_003Caction_003E5__77 = null;
			_003Cname_003E5__78 = null;
			_003Cdescription_003E5__79 = null;
			_003Crequirements_003E5__80 = null;
			_003Caction_003E5__82 = null;
			_003Cname_003E5__83 = null;
			_003Cdescription_003E5__84 = null;
			_003CextraDescription_003E5__85 = null;
			_003Caction_003E5__87 = null;
			_003C_003E1__state = -2;
		}

		private bool MoveNext()
		{
			//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ca: Expected O, but got Unknown
			//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Expected O, but got Unknown
			//IL_0190: Unknown result type (might be due to invalid IL or missing references)
			//IL_019a: Expected O, but got Unknown
			//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c2: Expected O, but got Unknown
			//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d8: Expected O, but got Unknown
			//IL_0282: Unknown result type (might be due to invalid IL or missing references)
			//IL_028c: Expected O, but got Unknown
			//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b4: Expected O, but got Unknown
			//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_02ca: Expected O, but got Unknown
			//IL_0374: Unknown result type (might be due to invalid IL or missing references)
			//IL_037e: Expected O, but got Unknown
			//IL_039c: Unknown result type (might be due to invalid IL or missing references)
			//IL_03a6: Expected O, but got Unknown
			//IL_03b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_03bc: Expected O, but got Unknown
			//IL_046d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0477: Expected O, but got Unknown
			//IL_0495: Unknown result type (might be due to invalid IL or missing references)
			//IL_049f: Expected O, but got Unknown
			//IL_04ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_04b5: Expected O, but got Unknown
			//IL_04c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_04cb: Expected O, but got Unknown
			//IL_0581: Unknown result type (might be due to invalid IL or missing references)
			//IL_058b: Expected O, but got Unknown
			//IL_05a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_05b3: Expected O, but got Unknown
			//IL_05bf: Unknown result type (might be due to invalid IL or missing references)
			//IL_05c9: Expected O, but got Unknown
			//IL_067a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0684: Expected O, but got Unknown
			//IL_06a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_06ac: Expected O, but got Unknown
			//IL_06b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_06c2: Expected O, but got Unknown
			//IL_06ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_06d8: Expected O, but got Unknown
			//IL_078d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0797: Expected O, but got Unknown
			//IL_07b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_07bf: Expected O, but got Unknown
			//IL_07cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_07d5: Expected O, but got Unknown
			//IL_07e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_07eb: Expected O, but got Unknown
			//IL_089b: Unknown result type (might be due to invalid IL or missing references)
			//IL_08a5: Expected O, but got Unknown
			//IL_08c3: Unknown result type (might be due to invalid IL or missing references)
			//IL_08cd: Expected O, but got Unknown
			//IL_08d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_08e3: Expected O, but got Unknown
			//IL_098d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0997: Expected O, but got Unknown
			//IL_09b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_09bf: Expected O, but got Unknown
			//IL_09cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_09d5: Expected O, but got Unknown
			//IL_0a7f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a89: Expected O, but got Unknown
			//IL_0a95: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a9f: Expected O, but got Unknown
			//IL_0b40: Unknown result type (might be due to invalid IL or missing references)
			//IL_0b4a: Expected O, but got Unknown
			//IL_0b68: Unknown result type (might be due to invalid IL or missing references)
			//IL_0b72: Expected O, but got Unknown
			//IL_0b7e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0b88: Expected O, but got Unknown
			//IL_0c35: Unknown result type (might be due to invalid IL or missing references)
			//IL_0c3f: Expected O, but got Unknown
			//IL_0c5d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0c67: Expected O, but got Unknown
			//IL_0c73: Unknown result type (might be due to invalid IL or missing references)
			//IL_0c7d: Expected O, but got Unknown
			//IL_0d2a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0d34: Expected O, but got Unknown
			//IL_0d40: Unknown result type (might be due to invalid IL or missing references)
			//IL_0d4a: Expected O, but got Unknown
			//IL_0d56: Unknown result type (might be due to invalid IL or missing references)
			//IL_0d60: Expected O, but got Unknown
			//IL_0d6c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0d76: Expected O, but got Unknown
			//IL_0e25: Unknown result type (might be due to invalid IL or missing references)
			//IL_0e2f: Expected O, but got Unknown
			//IL_0e4d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0e57: Expected O, but got Unknown
			//IL_0e63: Unknown result type (might be due to invalid IL or missing references)
			//IL_0e6d: Expected O, but got Unknown
			//IL_0ef4: Unknown result type (might be due to invalid IL or missing references)
			//IL_0efe: Expected O, but got Unknown
			//IL_0f0a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0f14: Expected O, but got Unknown
			//IL_0f20: Unknown result type (might be due to invalid IL or missing references)
			//IL_0f2a: Expected O, but got Unknown
			//IL_0fb1: Unknown result type (might be due to invalid IL or missing references)
			//IL_0fbb: Expected O, but got Unknown
			//IL_0fc7: Unknown result type (might be due to invalid IL or missing references)
			//IL_0fd1: Expected O, but got Unknown
			//IL_0fdd: Unknown result type (might be due to invalid IL or missing references)
			//IL_0fe7: Expected O, but got Unknown
			switch (_003C_003E1__state)
			{
			default:
				return false;
			case 0:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = ((object)new TextObject("{=config_category_base_minrecruits_name}Village recruits party size", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("MinRecruitmentAmountFromVillages", _003Cname_003E5__1, false);
				_003Cdescription_003E5__2 = ((object)new TextObject("{=config_category_base_minrecruits_description}Set the minimum number of recruits that need to be gathered from villages before a recruit party is established and sent to the garrison..", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__3 = ConfigManager.Instance.Config.MinRecruitmentAmountFromVillages;
				_003Caction_003E5__4 = delegate(float x)
				{
					ConfigManager.Instance.Config.MinRecruitmentAmountFromVillages = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__1, _003Cdescription_003E5__2, null, null, isDiscrete: true, 1f, 20f, _003Cvalue_003E5__3, _003Caction_003E5__4);
				_003C_003E1__state = 1;
				return true;
			case 1:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = null;
				_003Cdescription_003E5__2 = null;
				_003Caction_003E5__4 = null;
				_003Cname_003E5__5 = ((object)new TextObject("{=config_category_base_dailyexp_name}Daily experience amount", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DailyEXPAmount", _003Cname_003E5__5, false);
				_003Cdescription_003E5__6 = ((object)new TextObject("{=config_category_base_dailyexp_description}Set the amount of daily experience your garrisoned troops gain while training.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__7 = ((object)new TextObject("{=config_category_base_dailyexp_extradescription}The mod gives each troop roster a set amount of experience multiplied by the number of units this roster has. For example, if you have 20 recruits and they each need 250 experience to be upgraded, and the daily experience gain is set to 50, they will get 50 * 20 = 1000 experience. Consequently, 4 units will be upgraded (1000 / 250 = 4).", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__8 = ConfigManager.Instance.Config.DailyEXPAmount;
				_003Caction_003E5__9 = delegate(float x)
				{
					ConfigManager.Instance.Config.DailyEXPAmount = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__5, _003Cdescription_003E5__6, _003CextraDescription_003E5__7, null, isDiscrete: true, 0f, 500f, _003Cvalue_003E5__8, _003Caction_003E5__9);
				_003C_003E1__state = 2;
				return true;
			case 2:
				_003C_003E1__state = -1;
				_003Cname_003E5__5 = null;
				_003Cdescription_003E5__6 = null;
				_003CextraDescription_003E5__7 = null;
				_003Caction_003E5__9 = null;
				_003Cname_003E5__10 = ((object)new TextObject("{=config_category_base_prisonerconformity_name}Daily prisoner conformity amount", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DailyPrisonerConformityAmount", _003Cname_003E5__10, false);
				_003Cdescription_003E5__11 = ((object)new TextObject("{=config_category_base_prisonerconformity_description}Set the amount of daily conformity each prisoner get.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__12 = ((object)new TextObject("{=config_category_base_prisonerconformity_extradescription}Each prisoner has its own conformity value that needs to be reached in order to be recruitable. With this setting, you can set the daily amount of conformity they get.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__13 = ConfigManager.Instance.Config.DailyPrisonerConformityAmount;
				_003Caction_003E5__14 = delegate(float x)
				{
					ConfigManager.Instance.Config.DailyPrisonerConformityAmount = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__10, _003Cdescription_003E5__11, _003CextraDescription_003E5__12, null, isDiscrete: true, 0f, 100f, _003Cvalue_003E5__13, _003Caction_003E5__14);
				_003C_003E1__state = 3;
				return true;
			case 3:
				_003C_003E1__state = -1;
				_003Cname_003E5__10 = null;
				_003Cdescription_003E5__11 = null;
				_003CextraDescription_003E5__12 = null;
				_003Caction_003E5__14 = null;
				_003Cname_003E5__15 = ((object)new TextObject("{=config_category_base_loadsize_name}Load custom size module", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("LoadCustomPartySizeModel", _003Cname_003E5__15, false);
				_003Cdescription_003E5__16 = ((object)new TextObject("{=config_category_base_loadsize_description}Loads the custom size module of Improved Garrisons. This enables you to set the maximum size of your garrison and guard parties.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__17 = ((object)new TextObject("{=config_category_base_loadsize_requirements}The game has to be restarted.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__18 = (ConfigManager.Instance.Config.LoadCustomPartySizeModel ? 1f : 0f);
				_003Caction_003E5__19 = delegate(bool x)
				{
					ConfigManager.Instance.Config.LoadCustomPartySizeModel = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__15, _003Cdescription_003E5__16, _003Cdescription_003E5__16, _003Crequirements_003E5__17, _003Cvalue_003E5__18, _003Caction_003E5__19);
				_003C_003E1__state = 4;
				return true;
			case 4:
				_003C_003E1__state = -1;
				_003Cname_003E5__15 = null;
				_003Cdescription_003E5__16 = null;
				_003Crequirements_003E5__17 = null;
				_003Caction_003E5__19 = null;
				_003Cname_003E5__20 = ((object)new TextObject("{=config_category_base_partysize_name}Garrison guard and transfer party size", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("CustomTransferAndGuardPartySize", _003Cname_003E5__20, false);
				_003Cdescription_003E5__21 = ((object)new TextObject("{=config_category_base_partysize_description}Set a custom size for guard and transfer parties.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__22 = ((object)new TextObject("{=config_category_base_partysize_extradescription}This is used for the base speed and morale calculations. If the size of your transfer party, recruiter party or guard party goes beyond this value, it will get slower.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__23 = ((object)new TextObject("{=config_category_base_partysize_requirements}[{LoadCustomPartySizeModel}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__24 = ConfigManager.Instance.Config.CustomTransferAndGuardPartySize;
				_003Caction_003E5__25 = delegate(float x)
				{
					ConfigManager.Instance.Config.CustomTransferAndGuardPartySize = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__20, _003Cdescription_003E5__21, _003CextraDescription_003E5__22, _003Crequirements_003E5__23, isDiscrete: true, 1f, 650f, _003Cvalue_003E5__24, _003Caction_003E5__25);
				_003C_003E1__state = 5;
				return true;
			case 5:
				_003C_003E1__state = -1;
				_003Cname_003E5__20 = null;
				_003Cdescription_003E5__21 = null;
				_003CextraDescription_003E5__22 = null;
				_003Crequirements_003E5__23 = null;
				_003Caction_003E5__25 = null;
				_003Cname_003E5__26 = ((object)new TextObject("{=config_category_base_loadspeed_name}Load custom speed module", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("LoadCustomPartySpeedModel", _003Cname_003E5__26, false);
				_003Cdescription_003E5__27 = ((object)new TextObject("{=config_category_base_loadspeed_description}Loads the custom speed module of Improved Garrisons. This enables you to manually set the speed for your guard party, recruiter party and transfer party. Use this setting if you experience compatibility issues with the custom size module.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__28 = ((object)new TextObject("{=config_category_base_loadspeed_requirements}The game has to be restarted.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__29 = (ConfigManager.Instance.Config.LoadCustomPartySpeedModel ? 1f : 0f);
				_003Caction_003E5__30 = delegate(bool x)
				{
					ConfigManager.Instance.Config.LoadCustomPartySpeedModel = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__26, _003Cdescription_003E5__27, _003Cdescription_003E5__27, _003Crequirements_003E5__28, _003Cvalue_003E5__29, _003Caction_003E5__30);
				_003C_003E1__state = 6;
				return true;
			case 6:
				_003C_003E1__state = -1;
				_003Cname_003E5__26 = null;
				_003Cdescription_003E5__27 = null;
				_003Crequirements_003E5__28 = null;
				_003Caction_003E5__30 = null;
				_003Cname_003E5__31 = ((object)new TextObject("{=config_Category_base_guardreplenishperc_name}Guards replenishment percentage", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("GuardReplenishPercentage", _003Cname_003E5__31, false);
				_003Cdescription_003E5__32 = ((object)new TextObject("{=config_Category_base_guardreplenishperc_description}The percentage the current party size has to drop to in order for the guards to go back to their garrison to replenish their troops. The guards will only replenish if there are valid troops in your garrison! They only pick up units which were in their initial party, and only if these units party amount is not above the initial amount.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__33 = ((object)new TextObject("{=config_Category_base_guardreplenishperc_extradescription}> The guards will try to replenish to mirror their initial composition.\n> With 0.0, the guards will never return to the garrison to replenish.\n> With 0.5, the guards will replenish at half their initial size.\n> With 1.0, the guards will replenish as soon as they lose troops, and the amount of available troops to be picked up are above the [{GuardAvailableTroopsPercentage}]", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__34 = ((object)new TextObject("{=config_Category_base_guardreplenishperc_requirements}[{GuardAvailableTroopsPercentage}] has to be reached", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__35 = ConfigManager.Instance.Config.GuardReplenishPercentage;
				_003Caction_003E5__36 = delegate(float x)
				{
					ConfigManager.Instance.Config.GuardReplenishPercentage = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__31, _003Cdescription_003E5__32, _003CextraDescription_003E5__33, _003Crequirements_003E5__34, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__35, _003Caction_003E5__36);
				_003C_003E1__state = 7;
				return true;
			case 7:
				_003C_003E1__state = -1;
				_003Cname_003E5__31 = null;
				_003Cdescription_003E5__32 = null;
				_003CextraDescription_003E5__33 = null;
				_003Crequirements_003E5__34 = null;
				_003Caction_003E5__36 = null;
				_003Cname_003E5__37 = ((object)new TextObject("{=config_Category_base_guardreplenishavail_name}Guards replenishment available troops percentage", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("GuardAvailableTroopsPercentage", _003Cname_003E5__37, false);
				_003Cdescription_003E5__38 = ((object)new TextObject("{=config_Category_base_guardreplenishavail_description}The number of troops in relation to the initial guard party size that needs to be available in the garrison in order for the guards to return and refill.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__39 = ((object)new TextObject("{=config_Category_base_guardreplenishavail_extradescription}> With 1.0, the entire initial guard party size needs to be available in the garrison\n> With 0.5, the guards will replenish if at least half their size is available in the garrison.\n> With 0.0, the guards will replenish as soon as there are available troops to pick up, and if their party size is below the [{GuardReplenishPercentage}]", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__40 = ((object)new TextObject("{=config_Category_base_guardreplenishavail_requirements}[{GuardReplenishPercentage}] has to be reached", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__41 = ConfigManager.Instance.Config.GuardAvailableTroopsPercentage;
				_003Caction_003E5__42 = delegate(float x)
				{
					ConfigManager.Instance.Config.GuardAvailableTroopsPercentage = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__37, _003Cdescription_003E5__38, _003CextraDescription_003E5__39, null, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__41, _003Caction_003E5__42);
				_003C_003E1__state = 8;
				return true;
			case 8:
				_003C_003E1__state = -1;
				_003Cname_003E5__37 = null;
				_003Cdescription_003E5__38 = null;
				_003CextraDescription_003E5__39 = null;
				_003Crequirements_003E5__40 = null;
				_003Caction_003E5__42 = null;
				_003Cname_003E5__43 = ((object)new TextObject("{=config_category_base_guardheal_name}Guards heal percentage", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("GuardHealPercentage", _003Cname_003E5__43, false);
				_003Cdescription_003E5__44 = ((object)new TextObject("{=config_category_base_guardheal_description}The percentage of killed or wounded troops in relation to the initial party size that needs to be reached in order for the patrolling party to return to a town or castle to heal.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__45 = ((object)new TextObject("{=config_category_base_guardheal_extradescription}> With 0.0, the party will never heal because they would need to be dead in order to be healed\n> With 0.5, the party will heal when its current size reaches half its initial size\n> With 1.0, the party will heal as soon as its size gets lower than its initial party size", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__46 = ConfigManager.Instance.Config.PatrolPartyHealPercentage;
				_003Caction_003E5__47 = delegate(float x)
				{
					ConfigManager.Instance.Config.PatrolPartyHealPercentage = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__43, _003Cdescription_003E5__44, _003CextraDescription_003E5__45, null, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__46, _003Caction_003E5__47);
				_003C_003E1__state = 9;
				return true;
			case 9:
				_003C_003E1__state = -1;
				_003Cname_003E5__43 = null;
				_003Cdescription_003E5__44 = null;
				_003CextraDescription_003E5__45 = null;
				_003Caction_003E5__47 = null;
				_003Cname_003E5__48 = ((object)new TextObject("{=config_category_base_guardsell_name}Guards prisoner ransom threshold", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("GuardPrisonerSellThreshold", _003Cname_003E5__48, false);
				_003Cdescription_003E5__49 = ((object)new TextObject("{=config_category_base_guardsell_description}This lowers the threshold that has to be reached for prisoners to be ransomed", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__50 = ((object)new TextObject("{=config_category_base_guardsell_prompt}> With 0.0, the maximum amount of prisoners has to be reached before prisoners are ransomed.\n> With 0.5, the guards will sell their prisoners at half their prisoners capability.\n> With 1.0 the guards will turn in their prisoners the moment they are captured.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__51 = ConfigManager.Instance.Config.GuardPrisonerSellThreshold;
				_003Caction_003E5__52 = delegate(float x)
				{
					ConfigManager.Instance.Config.GuardPrisonerSellThreshold = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__48, _003Cdescription_003E5__49, _003CextraDescription_003E5__50, null, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__51, _003Caction_003E5__52);
				_003C_003E1__state = 10;
				return true;
			case 10:
				_003C_003E1__state = -1;
				_003Cname_003E5__48 = null;
				_003Cdescription_003E5__49 = null;
				_003CextraDescription_003E5__50 = null;
				_003Caction_003E5__52 = null;
				_003Cname_003E5__53 = ((object)new TextObject("{=}Enable track all Improved Garrison Parties", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__54 = ((object)new TextObject("{=}Enable a banner frame that is used to track parties across the map for all Improved Garrison Parties", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__55 = (ConfigManager.Instance.Config.EnableMapBannerTracker ? 1f : 0f);
				_003Caction_003E5__56 = delegate(bool x)
				{
					ConfigManager.Instance.Config.EnableMapBannerTracker = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__53, _003Cdescription_003E5__54, null, null, _003Cvalue_003E5__55, _003Caction_003E5__56);
				_003C_003E1__state = 11;
				return true;
			case 11:
				_003C_003E1__state = -1;
				_003Cname_003E5__53 = null;
				_003Cdescription_003E5__54 = null;
				_003Caction_003E5__56 = null;
				_003Cname_003E5__57 = ((object)new TextObject("{=config_category_default_dailymessage_name}Disable the daily message", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DisableDailyMessage", _003Cname_003E5__57, false);
				_003Cdescription_003E5__58 = ((object)new TextObject("{=config_category_default_dailymessage_description}Disables the daily chat message.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__59 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__60 = (ConfigManager.Instance.Config.DisableDailyMessage ? 1f : 0f);
				_003Caction_003E5__61 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DisableDailyMessage = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__57, _003Cdescription_003E5__58, null, _003Crequirements_003E5__59, _003Cvalue_003E5__60, _003Caction_003E5__61);
				_003C_003E1__state = 12;
				return true;
			case 12:
				_003C_003E1__state = -1;
				_003Cname_003E5__57 = null;
				_003Cdescription_003E5__58 = null;
				_003Crequirements_003E5__59 = null;
				_003Caction_003E5__61 = null;
				_003Cname_003E5__62 = ((object)new TextObject("{=config_category_default_tutorial_name}Deactivate tutorial", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DeactivateTutorial", _003Cname_003E5__62, false);
				_003Cdescription_003E5__63 = ((object)new TextObject("{=config_category_default_tutorial_description}Disables the tutorial that starts upon entering an owned fief.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__64 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__65 = (ConfigManager.Instance.Config.DeactivateTutorial ? 1f : 0f);
				_003Caction_003E5__66 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DeactivateTutorial = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__62, _003Cdescription_003E5__63, null, _003Crequirements_003E5__64, _003Cvalue_003E5__65, _003Caction_003E5__66);
				_003C_003E1__state = 13;
				return true;
			case 13:
				_003C_003E1__state = -1;
				_003Cname_003E5__62 = null;
				_003Cdescription_003E5__63 = null;
				_003Crequirements_003E5__64 = null;
				_003Caction_003E5__66 = null;
				_003Cname_003E5__67 = ((object)new TextObject("{=config_category_base_errormessage_name}Disable error messages", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__68 = ((object)new TextObject("{=config_category_base_errormessage_description}Disables the error messages that are shown whenever the mod encounters an error.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__69 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__70 = ((object)new TextObject("{=config_category_base_errormessage_extradescription}WARNING: if you disable error messages, you will no longer be notified if this mod encounters an error!", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__71 = (GlobalSettings.Instance.DisableErrorMessage ? 1f : 0f);
				_003Caction_003E5__72 = delegate(bool x)
				{
					GlobalSettings.Instance.DisableErrorMessage = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__67, _003Cdescription_003E5__68, _003CextraDescription_003E5__70, null, _003Cvalue_003E5__71, _003Caction_003E5__72);
				_003C_003E1__state = 14;
				return true;
			case 14:
				_003C_003E1__state = -1;
				_003Cname_003E5__67 = null;
				_003Cdescription_003E5__68 = null;
				_003Crequirements_003E5__69 = null;
				_003CextraDescription_003E5__70 = null;
				_003Caction_003E5__72 = null;
				_003Cname_003E5__73 = ((object)new TextObject("{=config_category_base_reset_name}Reset to default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("ResetToDefault", _003Cname_003E5__73, false);
				_003Cdescription_003E5__74 = ((object)new TextObject("{=config_category_base_reset_description}This resets the configuration of Improved Garrisons to its default values.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__75 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__76 = 0f;
				_003Caction_003E5__77 = delegate(bool x)
				{
					//IL_0015: Unknown result type (might be due to invalid IL or missing references)
					//IL_001f: Expected O, but got Unknown
					//IL_0025: Unknown result type (might be due to invalid IL or missing references)
					//IL_002f: Expected O, but got Unknown
					//IL_0037: Unknown result type (might be due to invalid IL or missing references)
					//IL_0041: Expected O, but got Unknown
					//IL_0047: Unknown result type (might be due to invalid IL or missing references)
					//IL_0051: Expected O, but got Unknown
					//IL_0076: Unknown result type (might be due to invalid IL or missing references)
					//IL_0082: Expected O, but got Unknown
					if (x)
					{
						_003C_003E4__this.PromptIsOpen = true;
						InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=config_category_base_reset_name}Reset to default", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=config_category_base_reset_prompt}Do you want to reset the Improved Garrison configuration to its default values?", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action)delegate
						{
							_003C_003E4__this.ResetMode = true;
							_003C_003E4__this.PromptIsOpen = false;
						}, (Action)delegate
						{
							_003C_003E4__this.PromptIsOpen = false;
						}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
					}
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__73, _003Cdescription_003E5__74, null, _003Crequirements_003E5__75, _003Cvalue_003E5__76, _003Caction_003E5__77);
				_003C_003E1__state = 15;
				return true;
			case 15:
				_003C_003E1__state = -1;
				_003Cname_003E5__73 = null;
				_003Cdescription_003E5__74 = null;
				_003Crequirements_003E5__75 = null;
				_003Caction_003E5__77 = null;
				_003Cname_003E5__78 = ((object)new TextObject("{=config_category_base_deleteparties_name}Delete all Improved Garrison parties", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__79 = ((object)new TextObject("{=config_category_base_deleteparties_description}Enable this to delete all Improved Garrison parties in this campaign.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__80 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__81 = 0f;
				_003Caction_003E5__82 = delegate(bool x)
				{
					//IL_0015: Unknown result type (might be due to invalid IL or missing references)
					//IL_001f: Expected O, but got Unknown
					//IL_0025: Unknown result type (might be due to invalid IL or missing references)
					//IL_002f: Expected O, but got Unknown
					//IL_0037: Unknown result type (might be due to invalid IL or missing references)
					//IL_0041: Expected O, but got Unknown
					//IL_0047: Unknown result type (might be due to invalid IL or missing references)
					//IL_0051: Expected O, but got Unknown
					//IL_0076: Unknown result type (might be due to invalid IL or missing references)
					//IL_0082: Expected O, but got Unknown
					if (x)
					{
						_003C_003E4__this.PromptIsOpen = true;
						InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=info_deletemode1}Delete all Improved Garrison Parties?", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_deletemode2}Do you really want to delete all Improved Garrison Parties?", (Dictionary<string, object>)null)).ToString(), true, true, ((object)new TextObject("{=menu_yes}Yes", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=menu_cancel}Cancel", (Dictionary<string, object>)null)).ToString(), (Action)delegate
						{
							Main.GarrisonPartyBehavior.OnGameStartDeleteAllIGParties();
							_003C_003E4__this.PromptIsOpen = false;
						}, (Action)delegate
						{
							_003C_003E4__this.PromptIsOpen = false;
						}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
					}
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__78, _003Cdescription_003E5__79, null, _003Crequirements_003E5__80, _003Cvalue_003E5__81, _003Caction_003E5__82);
				_003C_003E1__state = 16;
				return true;
			case 16:
				_003C_003E1__state = -1;
				_003Cname_003E5__78 = null;
				_003Cdescription_003E5__79 = null;
				_003Crequirements_003E5__80 = null;
				_003Caction_003E5__82 = null;
				_003Cname_003E5__83 = ((object)new TextObject("{=config_category_base_deletesavedata_name}Delete unnecessary save data", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__84 = ((object)new TextObject("{=config_category_base_deletesavedata_description}If you delete a save file, the saved data of Improved Garrisons for this specific save file might still remain. By enabling this, the mod will search for data that is mapped to a save file that no longer exists and will then proceed to delete those data, as they are no longer used in any way.\n \nNote: this process might take a moment", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__85 = ((object)new TextObject("{=config_category_base_deletesavedata_extradescription}You can also manually delete those data by finding the Mount and Blade II Bannerlord folder in your documents folder, and look into the configuration files of Improved Garrison.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__86 = 0f;
				_003Caction_003E5__87 = delegate(bool x)
				{
					if (x)
					{
						SaveSystemManager.Instance.DeleteUnnecessarySaveAndConfigFiles();
					}
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__83, _003Cdescription_003E5__84, _003CextraDescription_003E5__85, null, _003Cvalue_003E5__86, _003Caction_003E5__87);
				_003C_003E1__state = 17;
				return true;
			case 17:
				_003C_003E1__state = -1;
				_003Cname_003E5__83 = null;
				_003Cdescription_003E5__84 = null;
				_003CextraDescription_003E5__85 = null;
				_003Caction_003E5__87 = null;
				return false;
			}
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}

		[DebuggerHidden]
		IEnumerator<IOptionData> IEnumerable<IOptionData>.GetEnumerator()
		{
			_003Cget_BaseOptionsList_003Ed__50 result;
			if (_003C_003E1__state == -2 && _003C_003El__initialThreadId == Environment.CurrentManagedThreadId)
			{
				_003C_003E1__state = 0;
				result = this;
			}
			else
			{
				result = new _003Cget_BaseOptionsList_003Ed__50(0)
				{
					_003C_003E4__this = _003C_003E4__this
				};
			}
			return result;
		}

		[DebuggerHidden]
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable<IOptionData>)this).GetEnumerator();
		}
	}

	[CompilerGenerated]
	private sealed class _003Cget_CheatOptionsList_003Ed__46 : IEnumerable<IOptionData>, IEnumerable, IEnumerator<IOptionData>, IDisposable, IEnumerator
	{
		private int _003C_003E1__state;

		private IOptionData _003C_003E2__current;

		private int _003C_003El__initialThreadId;

		public ConfigMenuVM _003C_003E4__this;

		private string _003Cname_003E5__1;

		private string _003Cname_003E5__2;

		private string _003Cdescription_003E5__3;

		private string _003CextraDescription_003E5__4;

		private float _003Cvalue_003E5__5;

		private Action<float> _003Caction_003E5__6;

		private string _003Cname_003E5__7;

		private string _003Cdescription_003E5__8;

		private string _003CextraDescription_003E5__9;

		private float _003Cvalue_003E5__10;

		private Action<float> _003Caction_003E5__11;

		private string _003Cname_003E5__12;

		private string _003Cdescription_003E5__13;

		private string _003CextraDescription_003E5__14;

		private float _003Cvalue_003E5__15;

		private Action<float> _003Caction_003E5__16;

		private string _003Cname_003E5__17;

		private string _003Cdescription_003E5__18;

		private string _003CextraDescription_003E5__19;

		private string _003Crequirements_003E5__20;

		private float _003Cvalue_003E5__21;

		private Action<float> _003Caction_003E5__22;

		private string _003Cname_003E5__23;

		private string _003Cdescription_003E5__24;

		private string _003Crequirements_003E5__25;

		private float _003Cvalue_003E5__26;

		private Action<float> _003Caction_003E5__27;

		private string _003Cname_003E5__28;

		private string _003Cdescription_003E5__29;

		private string _003Crequirements_003E5__30;

		private float _003Cvalue_003E5__31;

		private Action<float> _003Caction_003E5__32;

		private string _003Cname_003E5__33;

		private string _003Cdescription_003E5__34;

		private string _003Crequirements_003E5__35;

		private float _003Cvalue_003E5__36;

		private Action<float> _003Caction_003E5__37;

		private string _003Cname_003E5__38;

		private string _003Cdescription_003E5__39;

		private string _003Crequirements_003E5__40;

		private float _003Cvalue_003E5__41;

		private Action<float> _003Caction_003E5__42;

		private string _003Cname_003E5__43;

		private string _003Cdescription_003E5__44;

		private string _003CextraDescription_003E5__45;

		private float _003Cvalue_003E5__46;

		private Action<bool> _003Caction_003E5__47;

		private string _003Cname_003E5__48;

		private string _003Cdescription_003E5__49;

		private string _003Crequirements_003E5__50;

		private float _003Cvalue_003E5__51;

		private Action<float> _003Caction_003E5__52;

		private string _003Cname_003E5__53;

		private string _003Cdescription_003E5__54;

		private string _003Crequirements_003E5__55;

		private float _003Cvalue_003E5__56;

		private Action<bool> _003Caction_003E5__57;

		private string _003Cname_003E5__58;

		private string _003Cdescription_003E5__59;

		private string _003Crequirements_003E5__60;

		private float _003Cvalue_003E5__61;

		private Action<bool> _003Caction_003E5__62;

		private string _003Cname_003E5__63;

		private string _003Cdescription_003E5__64;

		private string _003Crequirements_003E5__65;

		private float _003Cvalue_003E5__66;

		private Action<bool> _003Caction_003E5__67;

		private string _003Cname_003E5__68;

		private string _003Cdescription_003E5__69;

		private string _003Crequirements_003E5__70;

		private float _003Cvalue_003E5__71;

		private Action<bool> _003Caction_003E5__72;

		private string _003Cname_003E5__73;

		private string _003Cdescription_003E5__74;

		private string _003CextraDescription_003E5__75;

		private string _003Crequirements_003E5__76;

		private float _003Cvalue_003E5__77;

		private Action<float> _003Caction_003E5__78;

		IOptionData IEnumerator<IOptionData>.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		[DebuggerHidden]
		public _003Cget_CheatOptionsList_003Ed__46(int _003C_003E1__state)
		{
			this._003C_003E1__state = _003C_003E1__state;
			_003C_003El__initialThreadId = Environment.CurrentManagedThreadId;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			_003Cname_003E5__1 = null;
			_003Cname_003E5__2 = null;
			_003Cdescription_003E5__3 = null;
			_003CextraDescription_003E5__4 = null;
			_003Caction_003E5__6 = null;
			_003Cname_003E5__7 = null;
			_003Cdescription_003E5__8 = null;
			_003CextraDescription_003E5__9 = null;
			_003Caction_003E5__11 = null;
			_003Cname_003E5__12 = null;
			_003Cdescription_003E5__13 = null;
			_003CextraDescription_003E5__14 = null;
			_003Caction_003E5__16 = null;
			_003Cname_003E5__17 = null;
			_003Cdescription_003E5__18 = null;
			_003CextraDescription_003E5__19 = null;
			_003Crequirements_003E5__20 = null;
			_003Caction_003E5__22 = null;
			_003Cname_003E5__23 = null;
			_003Cdescription_003E5__24 = null;
			_003Crequirements_003E5__25 = null;
			_003Caction_003E5__27 = null;
			_003Cname_003E5__28 = null;
			_003Cdescription_003E5__29 = null;
			_003Crequirements_003E5__30 = null;
			_003Caction_003E5__32 = null;
			_003Cname_003E5__33 = null;
			_003Cdescription_003E5__34 = null;
			_003Crequirements_003E5__35 = null;
			_003Caction_003E5__37 = null;
			_003Cname_003E5__38 = null;
			_003Cdescription_003E5__39 = null;
			_003Crequirements_003E5__40 = null;
			_003Caction_003E5__42 = null;
			_003Cname_003E5__43 = null;
			_003Cdescription_003E5__44 = null;
			_003CextraDescription_003E5__45 = null;
			_003Caction_003E5__47 = null;
			_003Cname_003E5__48 = null;
			_003Cdescription_003E5__49 = null;
			_003Crequirements_003E5__50 = null;
			_003Caction_003E5__52 = null;
			_003Cname_003E5__53 = null;
			_003Cdescription_003E5__54 = null;
			_003Crequirements_003E5__55 = null;
			_003Caction_003E5__57 = null;
			_003Cname_003E5__58 = null;
			_003Cdescription_003E5__59 = null;
			_003Crequirements_003E5__60 = null;
			_003Caction_003E5__62 = null;
			_003Cname_003E5__63 = null;
			_003Cdescription_003E5__64 = null;
			_003Crequirements_003E5__65 = null;
			_003Caction_003E5__67 = null;
			_003Cname_003E5__68 = null;
			_003Cdescription_003E5__69 = null;
			_003Crequirements_003E5__70 = null;
			_003Caction_003E5__72 = null;
			_003Cname_003E5__73 = null;
			_003Cdescription_003E5__74 = null;
			_003CextraDescription_003E5__75 = null;
			_003Crequirements_003E5__76 = null;
			_003Caction_003E5__78 = null;
			_003C_003E1__state = -2;
		}

		private bool MoveNext()
		{
			//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c1: Expected O, but got Unknown
			//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_0101: Expected O, but got Unknown
			//IL_010d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0117: Expected O, but got Unknown
			//IL_0123: Unknown result type (might be due to invalid IL or missing references)
			//IL_012d: Expected O, but got Unknown
			//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e0: Expected O, but got Unknown
			//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f6: Expected O, but got Unknown
			//IL_0202: Unknown result type (might be due to invalid IL or missing references)
			//IL_020c: Expected O, but got Unknown
			//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_02bf: Expected O, but got Unknown
			//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d5: Expected O, but got Unknown
			//IL_02e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_02eb: Expected O, but got Unknown
			//IL_0394: Unknown result type (might be due to invalid IL or missing references)
			//IL_039e: Expected O, but got Unknown
			//IL_03bc: Unknown result type (might be due to invalid IL or missing references)
			//IL_03c6: Expected O, but got Unknown
			//IL_03d2: Unknown result type (might be due to invalid IL or missing references)
			//IL_03dc: Expected O, but got Unknown
			//IL_03e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_03f2: Expected O, but got Unknown
			//IL_04a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_04b1: Expected O, but got Unknown
			//IL_04cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_04d9: Expected O, but got Unknown
			//IL_04e5: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ef: Expected O, but got Unknown
			//IL_0598: Unknown result type (might be due to invalid IL or missing references)
			//IL_05a2: Expected O, but got Unknown
			//IL_05ae: Unknown result type (might be due to invalid IL or missing references)
			//IL_05b8: Expected O, but got Unknown
			//IL_05c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_05ce: Expected O, but got Unknown
			//IL_0677: Unknown result type (might be due to invalid IL or missing references)
			//IL_0681: Expected O, but got Unknown
			//IL_068d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0697: Expected O, but got Unknown
			//IL_06a3: Unknown result type (might be due to invalid IL or missing references)
			//IL_06ad: Expected O, but got Unknown
			//IL_0756: Unknown result type (might be due to invalid IL or missing references)
			//IL_0760: Expected O, but got Unknown
			//IL_076c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0776: Expected O, but got Unknown
			//IL_0782: Unknown result type (might be due to invalid IL or missing references)
			//IL_078c: Expected O, but got Unknown
			//IL_0836: Unknown result type (might be due to invalid IL or missing references)
			//IL_0840: Expected O, but got Unknown
			//IL_085e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0868: Expected O, but got Unknown
			//IL_0874: Unknown result type (might be due to invalid IL or missing references)
			//IL_087e: Expected O, but got Unknown
			//IL_0926: Unknown result type (might be due to invalid IL or missing references)
			//IL_0930: Expected O, but got Unknown
			//IL_094e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0958: Expected O, but got Unknown
			//IL_0964: Unknown result type (might be due to invalid IL or missing references)
			//IL_096e: Expected O, but got Unknown
			//IL_0a19: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a23: Expected O, but got Unknown
			//IL_0a41: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a4b: Expected O, but got Unknown
			//IL_0a57: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a61: Expected O, but got Unknown
			//IL_0b0e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0b18: Expected O, but got Unknown
			//IL_0b36: Unknown result type (might be due to invalid IL or missing references)
			//IL_0b40: Expected O, but got Unknown
			//IL_0b4c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0b56: Expected O, but got Unknown
			//IL_0c03: Unknown result type (might be due to invalid IL or missing references)
			//IL_0c0d: Expected O, but got Unknown
			//IL_0c19: Unknown result type (might be due to invalid IL or missing references)
			//IL_0c23: Expected O, but got Unknown
			//IL_0c2f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0c39: Expected O, but got Unknown
			//IL_0ce6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0cf0: Expected O, but got Unknown
			//IL_0d0e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0d18: Expected O, but got Unknown
			//IL_0d24: Unknown result type (might be due to invalid IL or missing references)
			//IL_0d2e: Expected O, but got Unknown
			//IL_0ddb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0de5: Expected O, but got Unknown
			//IL_0e03: Unknown result type (might be due to invalid IL or missing references)
			//IL_0e0d: Expected O, but got Unknown
			//IL_0e19: Unknown result type (might be due to invalid IL or missing references)
			//IL_0e23: Expected O, but got Unknown
			//IL_0e2f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0e39: Expected O, but got Unknown
			switch (_003C_003E1__state)
			{
			default:
				return false;
			case 0:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = ((object)new TextObject("{=config_category_dummyname}Category title", (Dictionary<string, object>)null)).ToString();
				_003C_003E2__current = (IOptionData)(object)new TitleText(_003Cname_003E5__1);
				_003C_003E1__state = 1;
				return true;
			case 1:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = null;
				_003Cname_003E5__2 = ((object)new TextObject("{=config_category_cheats_garrisonwage_name}Garrison wage multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__3 = ((object)new TextObject("{=config_category_cheats_garrisonwage_description}Lower the garrison wage. Set the value to 1 for normal costs and to 0 for no costs.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__4 = ((object)new TextObject("{=config_category_cheats_garrisonwage_extradescription}By selecting a number below 1, a bonus equal to your garrison costs will be added to your daily finances.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__5 = ConfigManager.Instance.Config.GarrisonWageMultiplier;
				_003Caction_003E5__6 = delegate(float x)
				{
					ConfigManager.Instance.Config.GarrisonWageMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__2, _003Cdescription_003E5__3, _003CextraDescription_003E5__4, null, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__5, _003Caction_003E5__6);
				_003C_003E1__state = 2;
				return true;
			case 2:
				_003C_003E1__state = -1;
				_003Cname_003E5__2 = null;
				_003Cdescription_003E5__3 = null;
				_003CextraDescription_003E5__4 = null;
				_003Caction_003E5__6 = null;
				_003Cname_003E5__7 = ((object)new TextObject("{=config_category_cheats_trainingcost_name}Improved Garrison training multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__8 = ((object)new TextObject("{=config_category_cheats_trainingcost_description}Lower the costs of the Improved Garrison training of garrisoned troops. Set this to 1 for normal costs and to 0 for no costs.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__9 = ((object)new TextObject("{=config_category_cheats_trainingcost_extradescription}This option affects the upgrade costs of your garrisoned troops. Use this if you want to disable the upgrade costs.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__10 = ConfigManager.Instance.Config.GarrisonTrainingMultiplier;
				_003Caction_003E5__11 = delegate(float x)
				{
					ConfigManager.Instance.Config.GarrisonTrainingMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__7, _003Cdescription_003E5__8, _003CextraDescription_003E5__9, null, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__10, _003Caction_003E5__11);
				_003C_003E1__state = 3;
				return true;
			case 3:
				_003C_003E1__state = -1;
				_003Cname_003E5__7 = null;
				_003Cdescription_003E5__8 = null;
				_003CextraDescription_003E5__9 = null;
				_003Caction_003E5__11 = null;
				_003Cname_003E5__12 = ((object)new TextObject("{=config_category_cheats_garrisonguardwage_name}Garrison guard parties wage multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__13 = ((object)new TextObject("{=config_category_cheats_garrisoguardnwage_description}Lower the wage of guard parties. Set this to 1 for normal costs and to 0 for no costs.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__14 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__15 = ConfigManager.Instance.Config.GarrisonGuardsWageMultiplier;
				_003Caction_003E5__16 = delegate(float x)
				{
					ConfigManager.Instance.Config.GarrisonGuardsWageMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__12, _003Cdescription_003E5__13, _003CextraDescription_003E5__14, null, isDiscrete: false, 0f, 1f, _003Cvalue_003E5__15, _003Caction_003E5__16);
				_003C_003E1__state = 4;
				return true;
			case 4:
				_003C_003E1__state = -1;
				_003Cname_003E5__12 = null;
				_003Cdescription_003E5__13 = null;
				_003CextraDescription_003E5__14 = null;
				_003Caction_003E5__16 = null;
				_003Cname_003E5__17 = ((object)new TextObject("{=config_category_cheats_partyspeed_name}custom speed of guard parties, recruiting parties and transfer parties", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("CustomGuardAndTransferPartySpeed", _003Cname_003E5__17, false);
				_003Cdescription_003E5__18 = ((object)new TextObject("{=config_category_cheats_partyspeed_description}Set a custom movement speed for guard, recruiting and transfer parties.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__19 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__20 = ((object)new TextObject("{=config_category_cheats_partyspeed_requirements}[{LoadCustomPartySpeedModel}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__21 = ConfigManager.Instance.Config.CustomGuardAndTransferPartySpeed;
				_003Caction_003E5__22 = delegate(float x)
				{
					ConfigManager.Instance.Config.CustomGuardAndTransferPartySpeed = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__17, _003Cdescription_003E5__18, _003CextraDescription_003E5__19, _003Crequirements_003E5__20, isDiscrete: false, 0f, 15f, _003Cvalue_003E5__21, _003Caction_003E5__22);
				_003C_003E1__state = 5;
				return true;
			case 5:
				_003C_003E1__state = -1;
				_003Cname_003E5__17 = null;
				_003Cdescription_003E5__18 = null;
				_003CextraDescription_003E5__19 = null;
				_003Crequirements_003E5__20 = null;
				_003Caction_003E5__22 = null;
				_003Cname_003E5__23 = ((object)new TextObject("{=config_category_cheats_garrisonsize_name}custom garrison size multiplier", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("CustomGarrisonSizeMultiplier", _003Cname_003E5__23, false);
				_003Cdescription_003E5__24 = ((object)new TextObject("{=config_category_cheats_garrisonsize_description}Set a garrison size multiplier to increase the size of your garrisons. The automatic recruitment will not exceed the overall garrison size!", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__25 = ((object)new TextObject("{=config_category_cheats_garrisonsize_requirements}[{LoadCustomPartySizeModel}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__26 = ConfigManager.Instance.Config.CustomGarrisonSizeMultiplier;
				_003Caction_003E5__27 = delegate(float x)
				{
					ConfigManager.Instance.Config.CustomGarrisonSizeMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__23, _003Cdescription_003E5__24, null, _003Crequirements_003E5__25, isDiscrete: false, 1f, 10f, _003Cvalue_003E5__26, _003Caction_003E5__27);
				_003C_003E1__state = 6;
				return true;
			case 6:
				_003C_003E1__state = -1;
				_003Cname_003E5__23 = null;
				_003Cdescription_003E5__24 = null;
				_003Crequirements_003E5__25 = null;
				_003Caction_003E5__27 = null;
				_003Cname_003E5__28 = ((object)new TextObject("{=config_category_cheats_partysize_name}custom player party size multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__29 = ((object)new TextObject("{=config_category_cheats_partysize_description}Set a party size multiplier to increase the maximum size of your own party.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__30 = ((object)new TextObject("{=config_category_cheats_partysize_requirements}[{LoadCustomPartySizeModel}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__31 = ConfigManager.Instance.Config.MainPartySizeMultiplier;
				_003Caction_003E5__32 = delegate(float x)
				{
					ConfigManager.Instance.Config.MainPartySizeMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__28, _003Cdescription_003E5__29, null, _003Crequirements_003E5__30, isDiscrete: false, 1f, 10f, _003Cvalue_003E5__31, _003Caction_003E5__32);
				_003C_003E1__state = 7;
				return true;
			case 7:
				_003C_003E1__state = -1;
				_003Cname_003E5__28 = null;
				_003Cdescription_003E5__29 = null;
				_003Crequirements_003E5__30 = null;
				_003Caction_003E5__32 = null;
				_003Cname_003E5__33 = ((object)new TextObject("{=config_category_cheats_clanpartysize_name}custom player clan party size multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__34 = ((object)new TextObject("{=config_category_cheats_clanpartysize_description}Set a party size multiplier to increase the maximum size of every party that belongs to your clan.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__35 = ((object)new TextObject("{=config_category_cheats_clanpartysize_requirements}[{LoadCustomPartySizeModel}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__36 = ConfigManager.Instance.Config.PlayerClanPartySizeMultiplier;
				_003Caction_003E5__37 = delegate(float x)
				{
					ConfigManager.Instance.Config.PlayerClanPartySizeMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__33, _003Cdescription_003E5__34, null, _003Crequirements_003E5__35, isDiscrete: false, 1f, 10f, _003Cvalue_003E5__36, _003Caction_003E5__37);
				_003C_003E1__state = 8;
				return true;
			case 8:
				_003C_003E1__state = -1;
				_003Cname_003E5__33 = null;
				_003Cdescription_003E5__34 = null;
				_003Crequirements_003E5__35 = null;
				_003Caction_003E5__37 = null;
				_003Cname_003E5__38 = ((object)new TextObject("{=config_category_cheats_aiclanpartysize_name}custom AI clan party size multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__39 = ((object)new TextObject("{=config_category_cheats_aiclanpartysize_description}Set a party size multiplier to increase the maximum size of every AI parties.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__40 = ((object)new TextObject("{=config_category_cheats_aiclanpartysize_requirements}[{LoadCustomPartySizeModel}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__41 = ConfigManager.Instance.Config.AIClanPartySizeMultiplier;
				_003Caction_003E5__42 = delegate(float x)
				{
					ConfigManager.Instance.Config.AIClanPartySizeMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__38, _003Cdescription_003E5__39, null, _003Crequirements_003E5__40, isDiscrete: false, 1f, 10f, _003Cvalue_003E5__41, _003Caction_003E5__42);
				_003C_003E1__state = 9;
				return true;
			case 9:
				_003C_003E1__state = -1;
				_003Cname_003E5__38 = null;
				_003Cdescription_003E5__39 = null;
				_003Crequirements_003E5__40 = null;
				_003Caction_003E5__42 = null;
				_003Cname_003E5__43 = ((object)new TextObject("{=config_category_base_fromvillages_name}Spawn new troops directly into the garrison", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("RecruitsAreRecruitedFromVillages", _003Cname_003E5__43, false);
				_003Cdescription_003E5__44 = ((object)new TextObject("{=config_category_base_fromvillages_description}With this option enabled the Improved Garrison will no longer have to wait for the troops to be generated by the surrounding villages. New troops will be spawned in the garrison \"out of thin air\" every day.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__45 = ((object)new TextObject("{=config_category_base_fromvillages_extradescription}If automatic recruitment and recruitment from villages are both enabled, this will look for recruitable units in the settlement and in its bound villages. Be aware that the number of available troops is affected by the relation you have with notables. Once the minimum amount of recruits is reached (4 by default), a party will be created and sent on its way to the garrison. The recruited troops will be removed from the village pool, so it will take some time for the village to generate additional troops.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__46 = (ConfigManager.Instance.Config.RecruitsAreRecruitedFromVillages ? 0f : 1f);
				_003Caction_003E5__47 = delegate(bool x)
				{
					ConfigManager.Instance.Config.RecruitsAreRecruitedFromVillages = !x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__43, _003Cdescription_003E5__44, null, null, _003Cvalue_003E5__46, _003Caction_003E5__47);
				_003C_003E1__state = 10;
				return true;
			case 10:
				_003C_003E1__state = -1;
				_003Cname_003E5__43 = null;
				_003Cdescription_003E5__44 = null;
				_003CextraDescription_003E5__45 = null;
				_003Caction_003E5__47 = null;
				_003Cname_003E5__48 = ((object)new TextObject("{=config_category_base_spawnamount_name}Amount of troops to spawn", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("AmountOfUnitsToSpawn", _003Cname_003E5__48, false);
				_003Cdescription_003E5__49 = ((object)new TextObject("{=config_category_base_spawnamount_description}Set the number of troops that are spawned in garrisons if they are not recruited from nearby villages and settlements.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__50 = ((object)new TextObject("{=config_category_base_spawnamount_requirements}[{RecruitsAreRecruitedFromVillages}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__51 = ConfigManager.Instance.Config.AmountOfUnitsToSpawn;
				_003Caction_003E5__52 = delegate(float x)
				{
					ConfigManager.Instance.Config.AmountOfUnitsToSpawn = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__48, _003Cdescription_003E5__49, null, _003Crequirements_003E5__50, isDiscrete: true, 0f, 300f, _003Cvalue_003E5__51, _003Caction_003E5__52);
				_003C_003E1__state = 11;
				return true;
			case 11:
				_003C_003E1__state = -1;
				_003Cname_003E5__48 = null;
				_003Cdescription_003E5__49 = null;
				_003Crequirements_003E5__50 = null;
				_003Caction_003E5__52 = null;
				_003Cname_003E5__53 = ((object)new TextObject("{=config_category_cheats_onlynoble_name}Spawn noble troops in garrison", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("SpawnOnlyNobleTroops", _003Cname_003E5__53, false);
				_003Cdescription_003E5__54 = ((object)new TextObject("{=config_category_cheats_onlynoble_description}With this option enabled noble troops are spawned in the garrisons \"out of thin air\". The amount can be set with [{AmountOfUnitsToSpawn}].", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__55 = ((object)new TextObject("{=config_category_cheats_onlynoble_requirements}[{RecruitsAreRecruitedFromVillages}] has to be enabled!", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__56 = (ConfigManager.Instance.Config.SpawnOnlyNobleTroops ? 1f : 0f);
				_003Caction_003E5__57 = delegate(bool x)
				{
					ConfigManager.Instance.Config.SpawnOnlyNobleTroops = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__53, _003Cdescription_003E5__54, _003Crequirements_003E5__55, null, _003Cvalue_003E5__56, _003Caction_003E5__57);
				_003C_003E1__state = 12;
				return true;
			case 12:
				_003C_003E1__state = -1;
				_003Cname_003E5__53 = null;
				_003Cdescription_003E5__54 = null;
				_003Crequirements_003E5__55 = null;
				_003Caction_003E5__57 = null;
				_003Cname_003E5__58 = ((object)new TextObject("{=config_category_base_loadfood_name}Load food gathering module", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("LoadFoodGatheringModule", _003Cname_003E5__58, false);
				_003Cdescription_003E5__59 = ((object)new TextObject("{=config_category_base_loadfood_description}Loads the food gathering module of Improved Garrisons. This enables the option to add a food bonus to castles and settlements.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__60 = ((object)new TextObject("{=config_category_base_loadfood_requirements}The game has to be restarted.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__61 = (ConfigManager.Instance.Config.LoadFoodGatheringModule ? 1f : 0f);
				_003Caction_003E5__62 = delegate(bool x)
				{
					ConfigManager.Instance.Config.LoadFoodGatheringModule = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__58, _003Cdescription_003E5__59, null, _003Crequirements_003E5__60, _003Cvalue_003E5__61, _003Caction_003E5__62);
				_003C_003E1__state = 13;
				return true;
			case 13:
				_003C_003E1__state = -1;
				_003Cname_003E5__58 = null;
				_003Cdescription_003E5__59 = null;
				_003Crequirements_003E5__60 = null;
				_003Caction_003E5__62 = null;
				_003Cname_003E5__63 = ((object)new TextObject("{=config_category_cheats_foodbonusenable_name}Enable food bonus", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__64 = ((object)new TextObject("{=config_category_cheats_foodbonusenable_description}Activate the food bonus for your castles and settlements.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__65 = ((object)new TextObject("{=config_category_cheats_foodbonusenable_requirements}[{LoadFoodGatheringModule}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__66 = (ConfigManager.Instance.Config.LoadFoodGatheringModule ? 1f : 0f);
				_003Caction_003E5__67 = delegate(bool x)
				{
					ConfigManager.Instance.Config.EnablePlayerFoodBonus = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__63, _003Cdescription_003E5__64, null, _003Crequirements_003E5__65, _003Cvalue_003E5__66, _003Caction_003E5__67);
				_003C_003E1__state = 14;
				return true;
			case 14:
				_003C_003E1__state = -1;
				_003Cname_003E5__63 = null;
				_003Cdescription_003E5__64 = null;
				_003Crequirements_003E5__65 = null;
				_003Caction_003E5__67 = null;
				_003Cname_003E5__68 = ((object)new TextObject("{=config_category_cheats_garrisondonteat_name}Enable the garrisons to not eat food", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DisableGarrisonNeedsFood", _003Cname_003E5__68, false);
				_003Cdescription_003E5__69 = ((object)new TextObject("{=config_category_cheats_garrisondonteat_description}Add a food bonus to settlements equalling the amount of food consumed by the garrison. This is a workaround aimed at cancelling food consumption for garrisons.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__70 = ((object)new TextObject("{=config_category_cheats_garrisondonteat_requirements}[{LoadFoodGatheringModule}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__71 = (ConfigManager.Instance.Config.DisableGarrisonNeedsFood ? 1f : 0f);
				_003Caction_003E5__72 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DisableGarrisonNeedsFood = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__68, _003Cdescription_003E5__69, null, _003Crequirements_003E5__70, _003Cvalue_003E5__71, _003Caction_003E5__72);
				_003C_003E1__state = 15;
				return true;
			case 15:
				_003C_003E1__state = -1;
				_003Cname_003E5__68 = null;
				_003Cdescription_003E5__69 = null;
				_003Crequirements_003E5__70 = null;
				_003Caction_003E5__72 = null;
				_003Cname_003E5__73 = ((object)new TextObject("{=config_category_base_foodamount_name}Daily food bonus amount", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DailyFoodGatheringAmount", _003Cname_003E5__73, false);
				_003Cdescription_003E5__74 = ((object)new TextObject("{=config_category_base_foodamount_description}Set the amount of daily food bonus each of the player's castles and settlements get each day.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__75 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__76 = ((object)new TextObject("{=config_category_base_foodamount_requirements}[{LoadFoodGatheringModule}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__77 = ConfigManager.Instance.Config.DailyFoodGatheringAmount;
				_003Caction_003E5__78 = delegate(float x)
				{
					ConfigManager.Instance.Config.DailyFoodGatheringAmount = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__73, _003Cdescription_003E5__74, _003CextraDescription_003E5__75, _003Crequirements_003E5__76, isDiscrete: true, 0f, 100f, _003Cvalue_003E5__77, _003Caction_003E5__78);
				_003C_003E1__state = 16;
				return true;
			case 16:
				_003C_003E1__state = -1;
				_003Cname_003E5__73 = null;
				_003Cdescription_003E5__74 = null;
				_003CextraDescription_003E5__75 = null;
				_003Crequirements_003E5__76 = null;
				_003Caction_003E5__78 = null;
				return false;
			}
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}

		[DebuggerHidden]
		IEnumerator<IOptionData> IEnumerable<IOptionData>.GetEnumerator()
		{
			_003Cget_CheatOptionsList_003Ed__46 result;
			if (_003C_003E1__state == -2 && _003C_003El__initialThreadId == Environment.CurrentManagedThreadId)
			{
				_003C_003E1__state = 0;
				result = this;
			}
			else
			{
				result = new _003Cget_CheatOptionsList_003Ed__46(0)
				{
					_003C_003E4__this = _003C_003E4__this
				};
			}
			return result;
		}

		[DebuggerHidden]
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable<IOptionData>)this).GetEnumerator();
		}
	}

	[CompilerGenerated]
	private sealed class _003Cget_DefaultOptionsList_003Ed__48 : IEnumerable<IOptionData>, IEnumerable, IEnumerator<IOptionData>, IDisposable, IEnumerator
	{
		private int _003C_003E1__state;

		private IOptionData _003C_003E2__current;

		private int _003C_003El__initialThreadId;

		public ConfigMenuVM _003C_003E4__this;

		private string _003Cname_003E5__1;

		private string _003Cdescription_003E5__2;

		private float _003Cvalue_003E5__3;

		private Action<bool> _003Caction_003E5__4;

		private string _003Cname_003E5__5;

		private string _003Cdescription_003E5__6;

		private string _003Crequirements_003E5__7;

		private float _003Cvalue_003E5__8;

		private Action<float> _003Caction_003E5__9;

		private string _003Cname_003E5__10;

		private string _003Cdescription_003E5__11;

		private string _003Crequirements_003E5__12;

		private float _003Cvalue_003E5__13;

		private Action<bool> _003Caction_003E5__14;

		private string _003Cname_003E5__15;

		private string _003Cdescription_003E5__16;

		private float _003Cvalue_003E5__17;

		private Action<bool> _003Caction_003E5__18;

		private string _003Cname_003E5__19;

		private string _003Cdescription_003E5__20;

		private float _003Cvalue_003E5__21;

		private Action<bool> _003Caction_003E5__22;

		private string _003Cname_003E5__23;

		private string _003Cdescription_003E5__24;

		private string _003CextraDescription_003E5__25;

		private float _003Cvalue_003E5__26;

		private Action<bool> _003Caction_003E5__27;

		private string _003Cname_003E5__28;

		private string _003Cdescription_003E5__29;

		private string _003Crequirements_003E5__30;

		private float _003Cvalue_003E5__31;

		private Action<float> _003Caction_003E5__32;

		private string _003Cname_003E5__33;

		private string _003Cdescription_003E5__34;

		private float _003Cvalue_003E5__35;

		private Action<bool> _003Caction_003E5__36;

		private string _003Cname_003E5__37;

		private string _003Cdescription_003E5__38;

		private float _003Cvalue_003E5__39;

		private Action<bool> _003Caction_003E5__40;

		private string _003Cname_003E5__41;

		private string _003Cdescription_003E5__42;

		private float _003Cvalue_003E5__43;

		private Action<bool> _003Caction_003E5__44;

		private string _003Cname_003E5__45;

		private string _003Cdescription_003E5__46;

		private float _003Cvalue_003E5__47;

		private Action<bool> _003Caction_003E5__48;

		private string _003Cname_003E5__49;

		private string _003Cdescription_003E5__50;

		private float _003Cvalue_003E5__51;

		private Action<bool> _003Caction_003E5__52;

		private string _003Cname_003E5__53;

		private string _003Cdescription_003E5__54;

		private float _003Cvalue_003E5__55;

		private Action<bool> _003Caction_003E5__56;

		IOptionData IEnumerator<IOptionData>.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		[DebuggerHidden]
		public _003Cget_DefaultOptionsList_003Ed__48(int _003C_003E1__state)
		{
			this._003C_003E1__state = _003C_003E1__state;
			_003C_003El__initialThreadId = Environment.CurrentManagedThreadId;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			_003Cname_003E5__1 = null;
			_003Cdescription_003E5__2 = null;
			_003Caction_003E5__4 = null;
			_003Cname_003E5__5 = null;
			_003Cdescription_003E5__6 = null;
			_003Crequirements_003E5__7 = null;
			_003Caction_003E5__9 = null;
			_003Cname_003E5__10 = null;
			_003Cdescription_003E5__11 = null;
			_003Crequirements_003E5__12 = null;
			_003Caction_003E5__14 = null;
			_003Cname_003E5__15 = null;
			_003Cdescription_003E5__16 = null;
			_003Caction_003E5__18 = null;
			_003Cname_003E5__19 = null;
			_003Cdescription_003E5__20 = null;
			_003Caction_003E5__22 = null;
			_003Cname_003E5__23 = null;
			_003Cdescription_003E5__24 = null;
			_003CextraDescription_003E5__25 = null;
			_003Caction_003E5__27 = null;
			_003Cname_003E5__28 = null;
			_003Cdescription_003E5__29 = null;
			_003Crequirements_003E5__30 = null;
			_003Caction_003E5__32 = null;
			_003Cname_003E5__33 = null;
			_003Cdescription_003E5__34 = null;
			_003Caction_003E5__36 = null;
			_003Cname_003E5__37 = null;
			_003Cdescription_003E5__38 = null;
			_003Caction_003E5__40 = null;
			_003Cname_003E5__41 = null;
			_003Cdescription_003E5__42 = null;
			_003Caction_003E5__44 = null;
			_003Cname_003E5__45 = null;
			_003Cdescription_003E5__46 = null;
			_003Caction_003E5__48 = null;
			_003Cname_003E5__49 = null;
			_003Cdescription_003E5__50 = null;
			_003Caction_003E5__52 = null;
			_003Cname_003E5__53 = null;
			_003Cdescription_003E5__54 = null;
			_003Caction_003E5__56 = null;
			_003C_003E1__state = -2;
		}

		private bool MoveNext()
		{
			//IL_009c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a6: Expected O, but got Unknown
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Expected O, but got Unknown
			//IL_016e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0178: Expected O, but got Unknown
			//IL_0196: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a0: Expected O, but got Unknown
			//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b6: Expected O, but got Unknown
			//IL_0260: Unknown result type (might be due to invalid IL or missing references)
			//IL_026a: Expected O, but got Unknown
			//IL_0288: Unknown result type (might be due to invalid IL or missing references)
			//IL_0292: Expected O, but got Unknown
			//IL_029e: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a8: Expected O, but got Unknown
			//IL_0354: Unknown result type (might be due to invalid IL or missing references)
			//IL_035e: Expected O, but got Unknown
			//IL_037c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0386: Expected O, but got Unknown
			//IL_0426: Unknown result type (might be due to invalid IL or missing references)
			//IL_0430: Expected O, but got Unknown
			//IL_044e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0458: Expected O, but got Unknown
			//IL_04f8: Unknown result type (might be due to invalid IL or missing references)
			//IL_0502: Expected O, but got Unknown
			//IL_0520: Unknown result type (might be due to invalid IL or missing references)
			//IL_052a: Expected O, but got Unknown
			//IL_0536: Unknown result type (might be due to invalid IL or missing references)
			//IL_0540: Expected O, but got Unknown
			//IL_05ec: Unknown result type (might be due to invalid IL or missing references)
			//IL_05f6: Expected O, but got Unknown
			//IL_0602: Unknown result type (might be due to invalid IL or missing references)
			//IL_060c: Expected O, but got Unknown
			//IL_0618: Unknown result type (might be due to invalid IL or missing references)
			//IL_0622: Expected O, but got Unknown
			//IL_06cc: Unknown result type (might be due to invalid IL or missing references)
			//IL_06d6: Expected O, but got Unknown
			//IL_06e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_06ec: Expected O, but got Unknown
			//IL_078c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0796: Expected O, but got Unknown
			//IL_07b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_07be: Expected O, but got Unknown
			//IL_085f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0869: Expected O, but got Unknown
			//IL_0887: Unknown result type (might be due to invalid IL or missing references)
			//IL_0891: Expected O, but got Unknown
			//IL_0932: Unknown result type (might be due to invalid IL or missing references)
			//IL_093c: Expected O, but got Unknown
			//IL_095a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0964: Expected O, but got Unknown
			//IL_0a05: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a0f: Expected O, but got Unknown
			//IL_0a2d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0a37: Expected O, but got Unknown
			//IL_0ad8: Unknown result type (might be due to invalid IL or missing references)
			//IL_0ae2: Expected O, but got Unknown
			//IL_0aee: Unknown result type (might be due to invalid IL or missing references)
			//IL_0af8: Expected O, but got Unknown
			switch (_003C_003E1__state)
			{
			default:
				return false;
			case 0:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = ((object)new TextObject("{=config_category_default_recruitment_name}World recruitment enabled by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultPlayerEnableRecruitment", _003Cname_003E5__1, false);
				_003Cdescription_003E5__2 = ((object)new TextObject("{=config_category_default_recruitment_description}Enables world recruitment for player garrisons by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__3 = (ConfigManager.Instance.Config.DefaultPlayerEnableRecruitment ? 1f : 0f);
				_003Caction_003E5__4 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultPlayerEnableRecruitment = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__1, _003Cdescription_003E5__2, null, null, _003Cvalue_003E5__3, _003Caction_003E5__4);
				_003C_003E1__state = 1;
				return true;
			case 1:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = null;
				_003Cdescription_003E5__2 = null;
				_003Caction_003E5__4 = null;
				_003Cname_003E5__5 = ((object)new TextObject("{=config_category_default_recruitthreshold_name}Maximum recruitment threshold default amount", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultPlayerMaxRecruitThreshold", _003Cname_003E5__5, false);
				_003Cdescription_003E5__6 = ((object)new TextObject("{=config_category_default_recruitthreshold_description}Set the default maximum number of garrison units until recruitment stops.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__7 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__8 = ConfigManager.Instance.Config.DefaultPlayerMaxRecruitThreshold;
				_003Caction_003E5__9 = delegate(float x)
				{
					ConfigManager.Instance.Config.DefaultPlayerMaxRecruitThreshold = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__5, _003Cdescription_003E5__6, null, _003Crequirements_003E5__7, isDiscrete: true, 0f, 2500f, _003Cvalue_003E5__8, _003Caction_003E5__9);
				_003C_003E1__state = 2;
				return true;
			case 2:
				_003C_003E1__state = -1;
				_003Cname_003E5__5 = null;
				_003Cdescription_003E5__6 = null;
				_003Crequirements_003E5__7 = null;
				_003Caction_003E5__9 = null;
				_003Cname_003E5__10 = ((object)new TextObject("{=config_category_default_onlyelite_name}Only recruit elites enabled by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("", _003Cname_003E5__10, false);
				_003Cdescription_003E5__11 = ((object)new TextObject("{=config_category_default_onlyelite_description}Enables the only recruit elite units option by default.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__12 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__13 = (ConfigManager.Instance.Config.DefaultOnlyRecruitElites ? 1f : 0f);
				_003Caction_003E5__14 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultOnlyRecruitElites = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__10, _003Cdescription_003E5__11, null, _003Crequirements_003E5__12, _003Cvalue_003E5__13, _003Caction_003E5__14);
				_003C_003E1__state = 3;
				return true;
			case 3:
				_003C_003E1__state = -1;
				_003Cname_003E5__10 = null;
				_003Cdescription_003E5__11 = null;
				_003Crequirements_003E5__12 = null;
				_003Caction_003E5__14 = null;
				_003Cname_003E5__15 = ((object)new TextObject("{=config_category_default_prisonerrecruit_name}Prisoner recruitment enabled by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultPlayerEnablePrisonerRecruitment", _003Cname_003E5__15, false);
				_003Cdescription_003E5__16 = ((object)new TextObject("{=config_category_default_prisonerrecruit_description}Enables prisoner recruitment for player garrisons by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__17 = (ConfigManager.Instance.Config.DefaultPlayerEnablePrisonerRecruitment ? 1f : 0f);
				_003Caction_003E5__18 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultPlayerEnablePrisonerRecruitment = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__15, _003Cdescription_003E5__16, null, null, _003Cvalue_003E5__17, _003Caction_003E5__18);
				_003C_003E1__state = 4;
				return true;
			case 4:
				_003C_003E1__state = -1;
				_003Cname_003E5__15 = null;
				_003Cdescription_003E5__16 = null;
				_003Caction_003E5__18 = null;
				_003Cname_003E5__19 = ((object)new TextObject("{=config_category_default_prisonerthreshold_name}Prisoner recruitment above threshold enabled by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultAllowPrisonerRecruitAboveThreshold", _003Cname_003E5__19, false);
				_003Cdescription_003E5__20 = ((object)new TextObject("{=config_category_default_prisonerthreshold_description}Enables prisoner recruitment above threshold for player garrisons by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__21 = (ConfigManager.Instance.Config.DefaultAllowPrisonerRecruitAboveThreshold ? 1f : 0f);
				_003Caction_003E5__22 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultAllowPrisonerRecruitAboveThreshold = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__19, _003Cdescription_003E5__20, null, null, _003Cvalue_003E5__21, _003Caction_003E5__22);
				_003C_003E1__state = 5;
				return true;
			case 5:
				_003C_003E1__state = -1;
				_003Cname_003E5__19 = null;
				_003Cdescription_003E5__20 = null;
				_003Caction_003E5__22 = null;
				_003Cname_003E5__23 = ((object)new TextObject("{=config_category_default_training_name}Training enabled by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultPlayerEnableTraining", _003Cname_003E5__23, false);
				_003Cdescription_003E5__24 = ((object)new TextObject("{=config_category_default_training_description}Enables training for player garrisons by default.", (Dictionary<string, object>)null)).ToString();
				_003CextraDescription_003E5__25 = ((object)new TextObject("{=config_category_default_training_requirements}Each day, your garrisoned units get a set (but customizable) amount of experience. Once they are able to upgrade, two things can happen. If you did choose their upgrade path by selecting a specific upgrade target, Improved Garrison will upgrade them towards this target. If no target has been set, the path will be choosen randomly. If you want to limit your costs, you can restrict their maximum upgrade tier (note that this is overwritten by your custom upgrade targets).", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__26 = (ConfigManager.Instance.Config.DefaultPlayerEnableTraining ? 1f : 0f);
				_003Caction_003E5__27 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultPlayerEnableTraining = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__23, _003Cdescription_003E5__24, _003CextraDescription_003E5__25, null, _003Cvalue_003E5__26, _003Caction_003E5__27);
				_003C_003E1__state = 6;
				return true;
			case 6:
				_003C_003E1__state = -1;
				_003Cname_003E5__23 = null;
				_003Cdescription_003E5__24 = null;
				_003CextraDescription_003E5__25 = null;
				_003Caction_003E5__27 = null;
				_003Cname_003E5__28 = ((object)new TextObject("{=config_category_default_maximumtier_name}Maximum training tier default setting", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__29 = ((object)new TextObject("{=config_category_default_maximumtier_description}The default maximum tier garrisoned troops are trained to.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__30 = ((object)new TextObject("", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__31 = ConfigManager.Instance.Config.DefaultPlayerMaxUpgradeTier;
				_003Caction_003E5__32 = delegate(float x)
				{
					ConfigManager.Instance.Config.DefaultPlayerMaxUpgradeTier = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__28, _003Cdescription_003E5__29, null, _003Crequirements_003E5__30, isDiscrete: true, 1f, 10f, _003Cvalue_003E5__31, _003Caction_003E5__32);
				_003C_003E1__state = 7;
				return true;
			case 7:
				_003C_003E1__state = -1;
				_003Cname_003E5__28 = null;
				_003Cdescription_003E5__29 = null;
				_003Crequirements_003E5__30 = null;
				_003Caction_003E5__32 = null;
				_003Cname_003E5__33 = ((object)new TextObject("{=config_category_default_guardupgrade_name}Guards can upgrade troops by default", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__34 = ((object)new TextObject("{=config_category_default_guardupgrade_description}Allow player guard parties to upgrade their troops by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__35 = (ConfigManager.Instance.Config.DefaultEnableGuardUpgrade ? 1f : 0f);
				_003Caction_003E5__36 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultEnableGuardUpgrade = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__33, _003Cdescription_003E5__34, null, null, _003Cvalue_003E5__35, _003Caction_003E5__36);
				_003C_003E1__state = 8;
				return true;
			case 8:
				_003C_003E1__state = -1;
				_003Cname_003E5__33 = null;
				_003Cdescription_003E5__34 = null;
				_003Caction_003E5__36 = null;
				_003Cname_003E5__37 = ((object)new TextObject("{=config_category_default_replenish_name}Guard party replenishing enabled by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultGuardsEnableReplenish", _003Cname_003E5__37, false);
				_003Cdescription_003E5__38 = ((object)new TextObject("{=config_category_default_replenish_description}Enables replenishing and healing for guard parties by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__39 = (ConfigManager.Instance.Config.DefaultGuardsEnableReplenish ? 1f : 0f);
				_003Caction_003E5__40 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultGuardsEnableReplenish = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__37, _003Cdescription_003E5__38, null, null, _003Cvalue_003E5__39, _003Caction_003E5__40);
				_003C_003E1__state = 9;
				return true;
			case 9:
				_003C_003E1__state = -1;
				_003Cname_003E5__37 = null;
				_003Cdescription_003E5__38 = null;
				_003Caction_003E5__40 = null;
				_003Cname_003E5__41 = ((object)new TextObject("{=config_category_default_guardssell_name}Prisoner trading enabled by default for guards", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultEnableGuardPrisonerSell", _003Cname_003E5__41, false);
				_003Cdescription_003E5__42 = ((object)new TextObject("{=config_category_default_guardssell_description}Enables prisoner trading by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__43 = (ConfigManager.Instance.Config.DefaultEnableGuardPrisonerSell ? 1f : 0f);
				_003Caction_003E5__44 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultEnableGuardPrisonerSell = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__41, _003Cdescription_003E5__42, null, null, _003Cvalue_003E5__43, _003Caction_003E5__44);
				_003C_003E1__state = 10;
				return true;
			case 10:
				_003C_003E1__state = -1;
				_003Cname_003E5__41 = null;
				_003Cdescription_003E5__42 = null;
				_003Caction_003E5__44 = null;
				_003Cname_003E5__45 = ((object)new TextObject("{=config_category_default_guardprisonerrecruit_name}Guards can recruit prisoners by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultEnableGuardPrisonerSell", _003Cname_003E5__45, false);
				_003Cdescription_003E5__46 = ((object)new TextObject("{=config_category_default_guardprisonerrecruit_description}Allow the recruitment of prisoners by player guard parties by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__47 = (ConfigManager.Instance.Config.DefaultEnableGuardPrisonerRecruitment ? 1f : 0f);
				_003Caction_003E5__48 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultEnableGuardPrisonerRecruitment = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__45, _003Cdescription_003E5__46, null, null, _003Cvalue_003E5__47, _003Caction_003E5__48);
				_003C_003E1__state = 11;
				return true;
			case 11:
				_003C_003E1__state = -1;
				_003Cname_003E5__45 = null;
				_003Cdescription_003E5__46 = null;
				_003Caction_003E5__48 = null;
				_003Cname_003E5__49 = ((object)new TextObject("{=config_category_default_hideoutclear_name}Guards can clear hideouts by default", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("DefaultEnableGuardHideoutClear", _003Cname_003E5__49, false);
				_003Cdescription_003E5__50 = ((object)new TextObject("{=config_category_default_hideoutclear_description}Allow guards to clear hideouts by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__51 = (ConfigManager.Instance.Config.DefaultEnableGuardHideoutClear ? 1f : 0f);
				_003Caction_003E5__52 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultEnableGuardHideoutClear = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__49, _003Cdescription_003E5__50, null, null, _003Cvalue_003E5__51, _003Caction_003E5__52);
				_003C_003E1__state = 12;
				return true;
			case 12:
				_003C_003E1__state = -1;
				_003Cname_003E5__49 = null;
				_003Cdescription_003E5__50 = null;
				_003Caction_003E5__52 = null;
				_003Cname_003E5__53 = ((object)new TextObject("{=config_category_default_buyhorses_name}Guards can buy horses by default", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__54 = ((object)new TextObject("{=config_category_default_buyhorses_description}Allow guards to buy horses to gain additional movement speed by default.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__55 = (ConfigManager.Instance.Config.DefaultEnableGuardBuyHorses ? 1f : 0f);
				_003Caction_003E5__56 = delegate(bool x)
				{
					ConfigManager.Instance.Config.DefaultEnableGuardBuyHorses = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__53, _003Cdescription_003E5__54, null, null, _003Cvalue_003E5__55, _003Caction_003E5__56);
				_003C_003E1__state = 13;
				return true;
			case 13:
				_003C_003E1__state = -1;
				_003Cname_003E5__53 = null;
				_003Cdescription_003E5__54 = null;
				_003Caction_003E5__56 = null;
				return false;
			}
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}

		[DebuggerHidden]
		IEnumerator<IOptionData> IEnumerable<IOptionData>.GetEnumerator()
		{
			_003Cget_DefaultOptionsList_003Ed__48 result;
			if (_003C_003E1__state == -2 && _003C_003El__initialThreadId == Environment.CurrentManagedThreadId)
			{
				_003C_003E1__state = 0;
				result = this;
			}
			else
			{
				result = new _003Cget_DefaultOptionsList_003Ed__48(0)
				{
					_003C_003E4__this = _003C_003E4__this
				};
			}
			return result;
		}

		[DebuggerHidden]
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable<IOptionData>)this).GetEnumerator();
		}
	}

	[CompilerGenerated]
	private sealed class _003Cget_NpcOptionsList_003Ed__52 : IEnumerable<IOptionData>, IEnumerable, IEnumerator<IOptionData>, IDisposable, IEnumerator
	{
		private int _003C_003E1__state;

		private IOptionData _003C_003E2__current;

		private int _003C_003El__initialThreadId;

		public ConfigMenuVM _003C_003E4__this;

		private string _003Cname_003E5__1;

		private string _003Cdescription_003E5__2;

		private float _003Cvalue_003E5__3;

		private Action<bool> _003Caction_003E5__4;

		private string _003Cname_003E5__5;

		private string _003Cdescription_003E5__6;

		private string _003Crequirements_003E5__7;

		private float _003Cvalue_003E5__8;

		private Action<float> _003Caction_003E5__9;

		private string _003Cname_003E5__10;

		private string _003Cdescription_003E5__11;

		private string _003Crequirements_003E5__12;

		private float _003Cvalue_003E5__13;

		private Action<float> _003Caction_003E5__14;

		private string _003Cname_003E5__15;

		private string _003Cdescription_003E5__16;

		private float _003Cvalue_003E5__17;

		private Action<bool> _003Caction_003E5__18;

		private string _003Cname_003E5__19;

		private string _003Cdescription_003E5__20;

		private float _003Cvalue_003E5__21;

		private Action<bool> _003Caction_003E5__22;

		private string _003Cname_003E5__23;

		private string _003Cdescription_003E5__24;

		private string _003Crequirements_003E5__25;

		private float _003Cvalue_003E5__26;

		private Action<float> _003Caction_003E5__27;

		private string _003Cname_003E5__28;

		private string _003Cdescription_003E5__29;

		private float _003Cvalue_003E5__30;

		private Action<bool> _003Caction_003E5__31;

		private string _003Cname_003E5__32;

		private string _003Cdescription_003E5__33;

		private string _003Crequirements_003E5__34;

		private float _003Cvalue_003E5__35;

		private Action<float> _003Caction_003E5__36;

		private string _003Cname_003E5__37;

		private string _003Cdescription_003E5__38;

		private string _003Crequirements_003E5__39;

		private float _003Cvalue_003E5__40;

		private Action<bool> _003Caction_003E5__41;

		private string _003Cname_003E5__42;

		private string _003Cdescription_003E5__43;

		private string _003Crequirements_003E5__44;

		private float _003Cvalue_003E5__45;

		private Action<float> _003Caction_003E5__46;

		IOptionData IEnumerator<IOptionData>.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		[DebuggerHidden]
		public _003Cget_NpcOptionsList_003Ed__52(int _003C_003E1__state)
		{
			this._003C_003E1__state = _003C_003E1__state;
			_003C_003El__initialThreadId = Environment.CurrentManagedThreadId;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			_003Cname_003E5__1 = null;
			_003Cdescription_003E5__2 = null;
			_003Caction_003E5__4 = null;
			_003Cname_003E5__5 = null;
			_003Cdescription_003E5__6 = null;
			_003Crequirements_003E5__7 = null;
			_003Caction_003E5__9 = null;
			_003Cname_003E5__10 = null;
			_003Cdescription_003E5__11 = null;
			_003Crequirements_003E5__12 = null;
			_003Caction_003E5__14 = null;
			_003Cname_003E5__15 = null;
			_003Cdescription_003E5__16 = null;
			_003Caction_003E5__18 = null;
			_003Cname_003E5__19 = null;
			_003Cdescription_003E5__20 = null;
			_003Caction_003E5__22 = null;
			_003Cname_003E5__23 = null;
			_003Cdescription_003E5__24 = null;
			_003Crequirements_003E5__25 = null;
			_003Caction_003E5__27 = null;
			_003Cname_003E5__28 = null;
			_003Cdescription_003E5__29 = null;
			_003Caction_003E5__31 = null;
			_003Cname_003E5__32 = null;
			_003Cdescription_003E5__33 = null;
			_003Crequirements_003E5__34 = null;
			_003Caction_003E5__36 = null;
			_003Cname_003E5__37 = null;
			_003Cdescription_003E5__38 = null;
			_003Crequirements_003E5__39 = null;
			_003Caction_003E5__41 = null;
			_003Cname_003E5__42 = null;
			_003Cdescription_003E5__43 = null;
			_003Crequirements_003E5__44 = null;
			_003Caction_003E5__46 = null;
			_003C_003E1__state = -2;
		}

		private bool MoveNext()
		{
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_008b: Expected O, but got Unknown
			//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b3: Expected O, but got Unknown
			//IL_0153: Unknown result type (might be due to invalid IL or missing references)
			//IL_015d: Expected O, but got Unknown
			//IL_0169: Unknown result type (might be due to invalid IL or missing references)
			//IL_0173: Expected O, but got Unknown
			//IL_017f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0189: Expected O, but got Unknown
			//IL_0233: Unknown result type (might be due to invalid IL or missing references)
			//IL_023d: Expected O, but got Unknown
			//IL_0249: Unknown result type (might be due to invalid IL or missing references)
			//IL_0253: Expected O, but got Unknown
			//IL_025f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0269: Expected O, but got Unknown
			//IL_0313: Unknown result type (might be due to invalid IL or missing references)
			//IL_031d: Expected O, but got Unknown
			//IL_033b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0345: Expected O, but got Unknown
			//IL_03e5: Unknown result type (might be due to invalid IL or missing references)
			//IL_03ef: Expected O, but got Unknown
			//IL_03fb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0405: Expected O, but got Unknown
			//IL_04a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_04af: Expected O, but got Unknown
			//IL_04cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_04d7: Expected O, but got Unknown
			//IL_04e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ed: Expected O, but got Unknown
			//IL_0597: Unknown result type (might be due to invalid IL or missing references)
			//IL_05a1: Expected O, but got Unknown
			//IL_05bf: Unknown result type (might be due to invalid IL or missing references)
			//IL_05c9: Expected O, but got Unknown
			//IL_0669: Unknown result type (might be due to invalid IL or missing references)
			//IL_0673: Expected O, but got Unknown
			//IL_067f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0689: Expected O, but got Unknown
			//IL_0695: Unknown result type (might be due to invalid IL or missing references)
			//IL_069f: Expected O, but got Unknown
			//IL_0749: Unknown result type (might be due to invalid IL or missing references)
			//IL_0753: Expected O, but got Unknown
			//IL_075f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0769: Expected O, but got Unknown
			//IL_0775: Unknown result type (might be due to invalid IL or missing references)
			//IL_077f: Expected O, but got Unknown
			//IL_082c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0836: Expected O, but got Unknown
			//IL_0842: Unknown result type (might be due to invalid IL or missing references)
			//IL_084c: Expected O, but got Unknown
			//IL_0858: Unknown result type (might be due to invalid IL or missing references)
			//IL_0862: Expected O, but got Unknown
			switch (_003C_003E1__state)
			{
			default:
				return false;
			case 0:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = ((object)new TextObject("{=config_category_npc_guardparties_name}Allow NPCs to create guard parties", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("EnableNPCMode", _003Cname_003E5__1, false);
				_003Cdescription_003E5__2 = ((object)new TextObject("{=config_category_npc_guardparties_description}Allows other factions to create guard parties to defend their lands.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__3 = (ConfigManager.Instance.Config.NPCSpawnGuards ? 1f : 0f);
				_003Caction_003E5__4 = delegate(bool x)
				{
					ConfigManager.Instance.Config.NPCSpawnGuards = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__1, _003Cdescription_003E5__2, null, null, _003Cvalue_003E5__3, _003Caction_003E5__4);
				_003C_003E1__state = 1;
				return true;
			case 1:
				_003C_003E1__state = -1;
				_003Cname_003E5__1 = null;
				_003Cdescription_003E5__2 = null;
				_003Caction_003E5__4 = null;
				_003Cname_003E5__5 = ((object)new TextObject("{=config_category_npc_npcguardspawnthreshold_name}NPC guard spawn threshold", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__6 = ((object)new TextObject("{=config_category_npc_npcguardspawnthreshold_description}The size an NPC garrison has to reach before a new guard party can be created", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__7 = ((object)new TextObject("{=config_category_npc_npcguardspawnthreshold_requirements}[{EnableNPCMode}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__8 = ConfigManager.Instance.Config.NPCGuardSpawnThreshold;
				_003Caction_003E5__9 = delegate(float x)
				{
					ConfigManager.Instance.Config.NPCGuardSpawnThreshold = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__5, _003Cdescription_003E5__6, null, _003Crequirements_003E5__7, isDiscrete: true, 1f, 650f, _003Cvalue_003E5__8, _003Caction_003E5__9);
				_003C_003E1__state = 2;
				return true;
			case 2:
				_003C_003E1__state = -1;
				_003Cname_003E5__5 = null;
				_003Cdescription_003E5__6 = null;
				_003Crequirements_003E5__7 = null;
				_003Caction_003E5__9 = null;
				_003Cname_003E5__10 = ((object)new TextObject("{=config_category_npc_npcguardsizemultiplier_name}NPC guard party size multiplier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__11 = ((object)new TextObject("{=config_category_npc_npcguardsizemultiplier_description}Customize the size of the NPC guard parties. \n \nSet to 0.2 for small guard parties\nSet to 0.8 for large guard parties.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__12 = ((object)new TextObject("{=config_category_npc_npcguardsizemultiplier_requirements}[{EnableNPCMode}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__13 = (float)ConfigManager.Instance.Config.NPCGuardCreationMultiplier;
				_003Caction_003E5__14 = delegate(float x)
				{
					ConfigManager.Instance.Config.NPCGuardCreationMultiplier = x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__10, _003Cdescription_003E5__11, null, _003Crequirements_003E5__12, isDiscrete: false, 0.2f, 0.8f, _003Cvalue_003E5__13, _003Caction_003E5__14);
				_003C_003E1__state = 3;
				return true;
			case 3:
				_003C_003E1__state = -1;
				_003Cname_003E5__10 = null;
				_003Cdescription_003E5__11 = null;
				_003Crequirements_003E5__12 = null;
				_003Caction_003E5__14 = null;
				_003Cname_003E5__15 = ((object)new TextObject("{=config_category_npc_worldrecruit_name}NPC world recruitment", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("EnableNPCWorldRecruitment", _003Cname_003E5__15, false);
				_003Cdescription_003E5__16 = ((object)new TextObject("{=config_category_npc_worldrecruit_description}Enables automatic recruitment for all NPC garrisons.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__17 = (ConfigManager.Instance.Config.EnableNPCWorldRecruitment ? 1f : 0f);
				_003Caction_003E5__18 = delegate(bool x)
				{
					ConfigManager.Instance.Config.EnableNPCWorldRecruitment = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__15, _003Cdescription_003E5__16, null, null, _003Cvalue_003E5__17, _003Caction_003E5__18);
				_003C_003E1__state = 4;
				return true;
			case 4:
				_003C_003E1__state = -1;
				_003Cname_003E5__15 = null;
				_003Cdescription_003E5__16 = null;
				_003Caction_003E5__18 = null;
				_003Cname_003E5__19 = ((object)new TextObject("{=config_category_npc_prisonerrecruit_name}NPC prisoner recruitment", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__20 = ((object)new TextObject("{=config_category_npc_prisonerrecruit_description}Enables the recruitment of prisoners for all NPC garrisons.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__21 = (ConfigManager.Instance.Config.EnableNPCPrisonerRecruitment ? 1f : 0f);
				_003Caction_003E5__22 = delegate(bool x)
				{
					ConfigManager.Instance.Config.EnableNPCPrisonerRecruitment = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__19, _003Cdescription_003E5__20, null, null, _003Cvalue_003E5__21, _003Caction_003E5__22);
				_003C_003E1__state = 5;
				return true;
			case 5:
				_003C_003E1__state = -1;
				_003Cname_003E5__19 = null;
				_003Cdescription_003E5__20 = null;
				_003Caction_003E5__22 = null;
				_003Cname_003E5__23 = ((object)new TextObject("{=config_category_npc_recruitthreshold_name}Maximum NPC recruitment threshold", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("MaximumNPCRecruitmentThreshold", _003Cname_003E5__23, false);
				_003Cdescription_003E5__24 = ((object)new TextObject("{=config_category_npc_recruitthreshold_description}Maximum number of garrison units until recruitment stops.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__25 = ((object)new TextObject("{=config_category_npc_recruitthreshold_requirements}[{EnableNPCWorldRecruitment}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__26 = ConfigManager.Instance.Config.MaximumNPCRecruitmentThreshold;
				_003Caction_003E5__27 = delegate(float x)
				{
					ConfigManager.Instance.Config.MaximumNPCRecruitmentThreshold = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__23, _003Cdescription_003E5__24, null, _003Crequirements_003E5__25, isDiscrete: true, 0f, 1000f, _003Cvalue_003E5__26, _003Caction_003E5__27);
				_003C_003E1__state = 6;
				return true;
			case 6:
				_003C_003E1__state = -1;
				_003Cname_003E5__23 = null;
				_003Cdescription_003E5__24 = null;
				_003Crequirements_003E5__25 = null;
				_003Caction_003E5__27 = null;
				_003Cname_003E5__28 = ((object)new TextObject("{=config_category_npc_training_name}NPC garrison training", (Dictionary<string, object>)null)).ToString();
				MBTextManager.SetTextVariable("EnableNPCGarrisonTraining", _003Cname_003E5__28, false);
				_003Cdescription_003E5__29 = ((object)new TextObject("{=config_category_npc_training_description}Enables the training of garrisoned troops for all NPC garrisons.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__30 = (ConfigManager.Instance.Config.EnableNPCGarrisonTraining ? 1f : 0f);
				_003Caction_003E5__31 = delegate(bool x)
				{
					ConfigManager.Instance.Config.EnableNPCGarrisonTraining = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__28, _003Cdescription_003E5__29, null, null, _003Cvalue_003E5__30, _003Caction_003E5__31);
				_003C_003E1__state = 7;
				return true;
			case 7:
				_003C_003E1__state = -1;
				_003Cname_003E5__28 = null;
				_003Cdescription_003E5__29 = null;
				_003Caction_003E5__31 = null;
				_003Cname_003E5__32 = ((object)new TextObject("{=config_category_npc_trainingtier_name}NPC maximum training tier", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__33 = ((object)new TextObject("{=config_category_npc_trainingtier_description}The maximum tier NPC garrisoned troops are trained to.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__34 = ((object)new TextObject("{=config_category_npc_trainingtier_requirements}[{EnableNPCGarrisonTraining}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__35 = ConfigManager.Instance.Config.NPCMaxUpgradeTier;
				_003Caction_003E5__36 = delegate(float x)
				{
					ConfigManager.Instance.Config.NPCMaxUpgradeTier = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__32, _003Cdescription_003E5__33, null, _003Crequirements_003E5__34, isDiscrete: true, 0f, 10f, _003Cvalue_003E5__35, _003Caction_003E5__36);
				_003C_003E1__state = 8;
				return true;
			case 8:
				_003C_003E1__state = -1;
				_003Cname_003E5__32 = null;
				_003Cdescription_003E5__33 = null;
				_003Crequirements_003E5__34 = null;
				_003Caction_003E5__36 = null;
				_003Cname_003E5__37 = ((object)new TextObject("{=config_category_npc_foodgathering_name}NPC bonus food gathering", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__38 = ((object)new TextObject("{=config_category_npc_foodgathering_description}Gives a bonus food amount to all NPC garrisons.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__39 = ((object)new TextObject("{=config_category_npc_foodgathering_requirements}[{LoadFoodGatheringModule}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__40 = (ConfigManager.Instance.Config.EnabeNPCFoodbonus ? 1f : 0f);
				_003Caction_003E5__41 = delegate(bool x)
				{
					ConfigManager.Instance.Config.EnabeNPCFoodbonus = x;
				};
				_003C_003E2__current = (IOptionData)(object)new ToggleOption(_003Cname_003E5__37, _003Cdescription_003E5__38, null, _003Crequirements_003E5__39, _003Cvalue_003E5__40, _003Caction_003E5__41);
				_003C_003E1__state = 9;
				return true;
			case 9:
				_003C_003E1__state = -1;
				_003Cname_003E5__37 = null;
				_003Cdescription_003E5__38 = null;
				_003Crequirements_003E5__39 = null;
				_003Caction_003E5__41 = null;
				_003Cname_003E5__42 = ((object)new TextObject("{=config_category_npc_foodamount_name}NPC food bonus amount", (Dictionary<string, object>)null)).ToString();
				_003Cdescription_003E5__43 = ((object)new TextObject("{=config_category_npc_foodamount_description}The food bonus amount all NPC garrisons get if NPC bonus food gathering is enabled.", (Dictionary<string, object>)null)).ToString();
				_003Crequirements_003E5__44 = ((object)new TextObject("{=config_category_npc_foodamount_requirements}[{LoadFoodGatheringModule}] has to be enabled.", (Dictionary<string, object>)null)).ToString();
				_003Cvalue_003E5__45 = ConfigManager.Instance.Config.NPCBonusFoodGathering;
				_003Caction_003E5__46 = delegate(float x)
				{
					ConfigManager.Instance.Config.NPCBonusFoodGathering = (int)x;
				};
				_003C_003E2__current = (IOptionData)(object)new NumericOption(_003Cname_003E5__42, _003Cdescription_003E5__43, null, _003Crequirements_003E5__44, isDiscrete: true, 0f, 150f, _003Cvalue_003E5__45, _003Caction_003E5__46);
				_003C_003E1__state = 10;
				return true;
			case 10:
				_003C_003E1__state = -1;
				_003Cname_003E5__42 = null;
				_003Cdescription_003E5__43 = null;
				_003Crequirements_003E5__44 = null;
				_003Caction_003E5__46 = null;
				return false;
			}
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}

		[DebuggerHidden]
		IEnumerator<IOptionData> IEnumerable<IOptionData>.GetEnumerator()
		{
			_003Cget_NpcOptionsList_003Ed__52 result;
			if (_003C_003E1__state == -2 && _003C_003El__initialThreadId == Environment.CurrentManagedThreadId)
			{
				_003C_003E1__state = 0;
				result = this;
			}
			else
			{
				result = new _003Cget_NpcOptionsList_003Ed__52(0)
				{
					_003C_003E4__this = _003C_003E4__this
				};
			}
			return result;
		}

		[DebuggerHidden]
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable<IOptionData>)this).GetEnumerator();
		}
	}

	private List<ImprovedGarrisonCategoryVM> _allCategories = new List<ImprovedGarrisonCategoryVM>();

	private readonly ImprovedGarrisonCategoryVM _cheatOptionCategory;

	private readonly ImprovedGarrisonCategoryVM _baseSettingsCategory;

	private readonly ImprovedGarrisonCategoryVM _npcOptionCategory;

	private readonly ImprovedGarrisonCategoryVM _defaultOptionCategory;

	public string Title { get; } = "Improved Garrisons Config";


	public string CloseText { get; } = ((object)new TextObject("{=menu_close}Close", (Dictionary<string, object>)null)).ToString();


	public string SaveText { get; } = ((object)new TextObject("{=menu_save}Save", (Dictionary<string, object>)null)).ToString();


	public bool OldGameStateManagerDisabledStatus { get; private set; }

	internal bool ResetMode { get; set; }

	internal bool PromptIsOpen { get; set; }

	internal bool IsFinished { get; set; } = false;


	public ImprovedGarrisonCategoryVM CheatOptions => _cheatOptionCategory;

	public ImprovedGarrisonCategoryVM BaseOptions => _baseSettingsCategory;

	public ImprovedGarrisonCategoryVM NpcOptions => _npcOptionCategory;

	public ImprovedGarrisonCategoryVM DefaultOptions => _defaultOptionCategory;

	private IEnumerable<IOptionData> CheatOptionsList
	{
		[IteratorStateMachine(typeof(_003Cget_CheatOptionsList_003Ed__46))]
		get
		{
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			_003Cget_CheatOptionsList_003Ed__46 _003Cget_CheatOptionsList_003Ed__ = new _003Cget_CheatOptionsList_003Ed__46(-2);
			_003Cget_CheatOptionsList_003Ed__._003C_003E4__this = this;
			return _003Cget_CheatOptionsList_003Ed__;
		}
	}

	private IEnumerable<IOptionData> DefaultOptionsList
	{
		[IteratorStateMachine(typeof(_003Cget_DefaultOptionsList_003Ed__48))]
		get
		{
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			_003Cget_DefaultOptionsList_003Ed__48 _003Cget_DefaultOptionsList_003Ed__ = new _003Cget_DefaultOptionsList_003Ed__48(-2);
			_003Cget_DefaultOptionsList_003Ed__._003C_003E4__this = this;
			return _003Cget_DefaultOptionsList_003Ed__;
		}
	}

	private IEnumerable<IOptionData> BaseOptionsList
	{
		[IteratorStateMachine(typeof(_003Cget_BaseOptionsList_003Ed__50))]
		get
		{
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			_003Cget_BaseOptionsList_003Ed__50 _003Cget_BaseOptionsList_003Ed__ = new _003Cget_BaseOptionsList_003Ed__50(-2);
			_003Cget_BaseOptionsList_003Ed__._003C_003E4__this = this;
			return _003Cget_BaseOptionsList_003Ed__;
		}
	}

	private IEnumerable<IOptionData> NpcOptionsList
	{
		[IteratorStateMachine(typeof(_003Cget_NpcOptionsList_003Ed__52))]
		get
		{
			//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
			_003Cget_NpcOptionsList_003Ed__52 _003Cget_NpcOptionsList_003Ed__ = new _003Cget_NpcOptionsList_003Ed__52(-2);
			_003Cget_NpcOptionsList_003Ed__._003C_003E4__this = this;
			return _003Cget_NpcOptionsList_003Ed__;
		}
	}

	public ConfigMenuVM()
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Expected O, but got Unknown
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Expected O, but got Unknown
		_baseSettingsCategory = new ImprovedGarrisonCategoryVM(new TextObject("{=config_category_base_title}Mod Settings", (Dictionary<string, object>)null), BaseOptionsList);
		_npcOptionCategory = new ImprovedGarrisonCategoryVM(new TextObject("{=config_category_npc_title}NPC Settings", (Dictionary<string, object>)null), NpcOptionsList);
		_defaultOptionCategory = new ImprovedGarrisonCategoryVM(new TextObject("{=config_category_default_title}Default Settings", (Dictionary<string, object>)null), DefaultOptionsList);
		_cheatOptionCategory = new ImprovedGarrisonCategoryVM(new TextObject("{=config_category_cheats_title}Cheats", (Dictionary<string, object>)null), CheatOptionsList);
		_allCategories.Add(_baseSettingsCategory);
		_allCategories.Add(_cheatOptionCategory);
		_allCategories.Add(_npcOptionCategory);
		_allCategories.Add(_defaultOptionCategory);
		PauseGame();
		((ViewModel)this).RefreshValues();
	}

	public override void RefreshValues()
	{
		try
		{
			((ViewModel)this).RefreshValues();
			((ViewModel)BaseOptions).RefreshValues();
			((ViewModel)CheatOptions).RefreshValues();
			((ViewModel)DefaultOptions).RefreshValues();
			((ViewModel)NpcOptions).RefreshValues();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ExecuteCancel()
	{
		try
		{
			foreach (ImprovedGarrisonCategoryVM allCategory in _allCategories)
			{
				foreach (ConfigOptionsMenuItemVM item in (Collection<ConfigOptionsMenuItemVM>)(object)allCategory.Options)
				{
					item.Cancel();
				}
			}
			OnFinished();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void ExecuteDone()
	{
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Expected O, but got Unknown
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Expected O, but got Unknown
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Expected O, but got Unknown
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Expected O, but got Unknown
		try
		{
			bool loadFoodGatheringModule = ConfigManager.Instance.Config.LoadFoodGatheringModule;
			bool loadCustomPartySpeedModel = ConfigManager.Instance.Config.LoadCustomPartySpeedModel;
			bool loadCustomPartySizeModel = ConfigManager.Instance.Config.LoadCustomPartySizeModel;
			foreach (ImprovedGarrisonCategoryVM allCategory in _allCategories)
			{
				foreach (ConfigOptionsMenuItemVM item in (Collection<ConfigOptionsMenuItemVM>)(object)allCategory.Options)
				{
					item.UpdateValue();
				}
			}
			if (ConfigManager.Instance.Config.LoadFoodGatheringModule != loadFoodGatheringModule || ConfigManager.Instance.Config.LoadCustomPartySpeedModel != loadCustomPartySpeedModel || ConfigManager.Instance.Config.LoadCustomPartySizeModel != loadCustomPartySizeModel)
			{
				PromptIsOpen = true;
				InformationManager.ShowInquiry(new InquiryData(((object)new TextObject("{=config_reload_title}Reload required", (Dictionary<string, object>)null)).ToString(), ((object)new TextObject("{=config_reload_desc}Reloading your save is required for all Improved Garrison settings to apply.", (Dictionary<string, object>)null)).ToString(), true, false, ((object)new TextObject("{=menu_ok}Okay", (Dictionary<string, object>)null)).ToString(), string.Empty, (Action)delegate
				{
					PromptIsOpen = false;
				}, (Action)delegate
				{
					PromptIsOpen = false;
				}, "", 0f, (Action)null, (Func<ValueTuple<bool, string>>)null, (Func<ValueTuple<bool, string>>)null), false, false);
			}
			if (!ConfigManager.Instance.Config.EnableMapBannerTracker)
			{
				Main.PartyManagement.UntrackAllImprovedGarrisonparties();
			}
			else
			{
				Main.PartyManagement.TrackAllImprovedGarrisonparties();
			}
			OnFinished();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void OnFinished()
	{
		try
		{
			if (Main._configurationSettingsIsOpen)
			{
				IsFinished = true;
			}
			Main.GarrisonBehavior.UpdateNPCGarrisonSettings();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	private void OnResetToDefaults()
	{
		ConfigManager.Instance.Config.ResetToDefault();
	}

	public void SaveToConfig()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected O, but got Unknown
		try
		{
			ConfigManager.Instance.CreateAndUpdateConfigForCurrentGame();
			InformationManager.DisplayMessage(new InformationMessage(((object)new TextObject("{=info_config_save_done}Improved Garrison configuration saved successfully.", (Dictionary<string, object>)null)).ToString(), Color.FromUint(ModuleColors.green)));
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void ReadConfig()
	{
		ConfigManager.Instance.ReadConfigForCurrentGame();
	}

	internal void PauseGame()
	{
		try
		{
			if (Game.Current != null)
			{
				OldGameStateManagerDisabledStatus = Game.Current.GameStateManager.ActiveStateDisabledByUser;
				Game.Current.GameStateManager.RegisterActiveStateDisableRequest((object)this);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	internal void UnpauseGame()
	{
		try
		{
			if (Game.Current != null)
			{
				Game.Current.GameStateManager.UnregisterActiveStateDisableRequest((object)this);
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}
}
