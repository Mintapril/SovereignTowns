using System.Collections.Generic;
using TaleWorlds.Engine.Options;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.ConfigOptionsMenu.Options;

public abstract class ConfigOptionsMenuItemVM : ViewModel
{
	private TextObject _nameObj;

	protected IOptionData Option;

	private TextObject _descriptionObj;

	private int _optionTypeId = -1;

	private string[] _imageIDs;

	[DataSourceProperty]
	public string Name
	{
		get
		{
			return ((object)_nameObj).ToString();
		}
		set
		{
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Expected O, but got Unknown
			_nameObj = new TextObject(value, (Dictionary<string, object>)null);
			((ViewModel)this).OnPropertyChanged("Name");
		}
	}

	[DataSourceProperty]
	public string Description
	{
		get
		{
			return ((object)_descriptionObj).ToString();
		}
		set
		{
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Expected O, but got Unknown
			_descriptionObj = new TextObject(value, (Dictionary<string, object>)null);
			((ViewModel)this).OnPropertyChanged("Description");
		}
	}

	[DataSourceProperty]
	public string[] ImageIDs
	{
		get
		{
			return _imageIDs;
		}
		set
		{
			if (value != _imageIDs)
			{
				_imageIDs = value;
				((ViewModel)this).OnPropertyChanged("ImageIDs");
			}
		}
	}

	[DataSourceProperty]
	public int OptionTypeID
	{
		get
		{
			return _optionTypeId;
		}
		set
		{
			if (value != _optionTypeId)
			{
				_optionTypeId = value;
				((ViewModel)this).OnPropertyChanged("OptionTypeID");
			}
		}
	}

	public ConfigOptionsMenuItemVM(IOptionData option, TextObject name, TextObject description, ConfigMenuVM.OptionsDataType typeID)
	{
		_nameObj = name;
		Option = option;
		OptionTypeID = (int)typeID;
		_descriptionObj = description;
		((ViewModel)this).RefreshValues();
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
	}

	public void ExecuteAction()
	{
	}

	public abstract void UpdateValue();

	public abstract void Cancel();

	public abstract bool IsChanged();

	public abstract void SetValue(float value);
}
