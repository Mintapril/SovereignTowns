using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.Engine.Options;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public class ImprovedGarrisonCategoryVM : ViewModel
{
	private readonly TextObject _nameObj;

	private string _name;

	private MBBindingList<ConfigOptionsMenuItemVM> _options;

	[DataSourceProperty]
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
				((ViewModel)this).OnPropertyChanged("Name");
			}
		}
	}

	[DataSourceProperty]
	public MBBindingList<ConfigOptionsMenuItemVM> Options
	{
		get
		{
			return _options;
		}
		set
		{
			if (value != _options)
			{
				_options = value;
				((ViewModel)this).OnPropertyChanged("Options");
			}
		}
	}

	public ImprovedGarrisonCategoryVM(TextObject name, IEnumerable<IOptionData> targetList)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected O, but got Unknown
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Expected O, but got Unknown
		try
		{
			_options = new MBBindingList<ConfigOptionsMenuItemVM>();
			_nameObj = name;
			foreach (IOptionData target in targetList)
			{
				TextObject name2 = new TextObject((target as OptionUtils).getName(), (Dictionary<string, object>)null);
				TextObject description = new TextObject((target as OptionUtils).getDescription(), (Dictionary<string, object>)null);
				if (target is IBooleanOptionData)
				{
					((Collection<ConfigOptionsMenuItemVM>)(object)_options).Add((ConfigOptionsMenuItemVM)new ToggleOptionDataVM((IBooleanOptionData)(object)((target is IBooleanOptionData) ? target : null), name2, description, ConfigMenuVM.OptionsDataType.BooleanOption));
				}
				else if (target is INumericOptionData)
				{
					((Collection<ConfigOptionsMenuItemVM>)(object)_options).Add((ConfigOptionsMenuItemVM)new NumericOptionDataVM((INumericOptionData)(object)((target is INumericOptionData) ? target : null), name2, description, ConfigMenuVM.OptionsDataType.NumericOption));
				}
				else if (target is ISelectionOptionData)
				{
					((Collection<ConfigOptionsMenuItemVM>)(object)_options).Add((ConfigOptionsMenuItemVM)new SelectionOptionDataVM((ISelectionOptionData)(object)((target is ISelectionOptionData) ? target : null), name2, description, ConfigMenuVM.OptionsDataType.MultipleSelectionOption));
				}
				else if (target is TitleText)
				{
					((Collection<ConfigOptionsMenuItemVM>)(object)_options).Add((ConfigOptionsMenuItemVM)new TitleTextVM((IOptionData)(object)(target as TitleText), name2, description, ConfigMenuVM.OptionsDataType.Title));
				}
			}
			((ViewModel)this).RefreshValues();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		Name = ((object)_nameObj).ToString();
		Options.ApplyActionOnAllItems((Action<ConfigOptionsMenuItemVM>)delegate(ConfigOptionsMenuItemVM x)
		{
			((ViewModel)x).RefreshValues();
		});
	}
}
