using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ImprovedGarrisons.ImprovedGarrisonsUI.SubMenus.GarrisonUtils;

public class LogEntryVM : ViewModel
{
	private string _time;

	private string _description;

	public string Time
	{
		get
		{
			return _time;
		}
		set
		{
			if (value != _time)
			{
				_time = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Time");
			}
		}
	}

	public string Description
	{
		get
		{
			return _description;
		}
		set
		{
			if (value != _description)
			{
				_description = value;
				((ViewModel)this).OnPropertyChangedWithValue<string>(value, "Description");
			}
		}
	}

	public LogEntryVM(string time, string desc)
	{
		Time = time;
		Description = desc;
	}

	public void ExecuteLink(string link)
	{
		try
		{
			if (link != null && !(link == ""))
			{
				Campaign.Current.EncyclopediaManager.GoToLink(link);
			}
		}
		catch (Exception)
		{
		}
	}
}
