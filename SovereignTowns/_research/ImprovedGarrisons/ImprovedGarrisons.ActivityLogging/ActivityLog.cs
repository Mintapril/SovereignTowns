using System;
using System.Collections.Generic;
using ImprovedGarrisons.ActivityLogging.Activities;

namespace ImprovedGarrisons.ActivityLogging;

[Serializable]
public class ActivityLog
{
	public Dictionary<string, float> RecruiterCosts = new Dictionary<string, float>();

	private List<GarrisonActivity> Activities = new List<GarrisonActivity>();

	public float WeeklyRecruitmentCosts;

	public float WeeklyTrainingCosts;

	public float RecruitmentCosts { get; private set; }

	public float UnitUpgradeCosts { get; private set; }

	public bool ActivitiesHaveBeenUpdated { get; set; } = true;


	public int WeeklyRecruits { get; private set; } = 0;


	public int DailyRecruits { get; private set; } = 0;


	public int WeeklyUpgrades { get; private set; } = 0;


	public int DailyUpgrades { get; private set; } = 0;


	public int WeeklyPrisonerTurnover { get; private set; } = 0;


	public int DailyPrisonerTurnover { get; private set; } = 0;


	public int DailyPrisonerTurnIn { get; private set; } = 0;


	public void AddActivity(GarrisonActivity activity)
	{
		if (Activities.Count >= 100)
		{
			Activities.RemoveAt(Activities.Count - 1);
			Activities.Insert(0, activity);
		}
		else
		{
			Activities.Insert(0, activity);
		}
		ActivitiesHaveBeenUpdated = true;
	}

	public List<GarrisonActivity> GetActivities()
	{
		return Activities;
	}

	public void AddNewRecruits(int amount)
	{
		DailyRecruits += amount;
		WeeklyRecruits += amount;
	}

	public void AddNewUpgrades(int amount)
	{
		DailyUpgrades += amount;
		WeeklyUpgrades += amount;
	}

	public void AddNewPrisonerTurnover(int amount)
	{
		DailyPrisonerTurnover += amount;
		WeeklyPrisonerTurnover += amount;
	}

	public void ResetDailies()
	{
		DailyUpgrades = 0;
		DailyRecruits = 0;
		DailyPrisonerTurnover = 0;
		DailyPrisonerTurnIn = 0;
	}

	public void ResetWeeklies()
	{
		WeeklyPrisonerTurnover = 0;
		WeeklyRecruitmentCosts = 0f;
		WeeklyRecruits = 0;
		WeeklyTrainingCosts = 0f;
		WeeklyUpgrades = 0;
	}

	public void AddRecruiterCosts(string name, float cost)
	{
		if (RecruiterCosts.TryGetValue(name, out var value))
		{
			RecruiterCosts[name] = value + cost;
		}
		else
		{
			RecruiterCosts.Add(name, cost);
		}
		WeeklyRecruitmentCosts += cost;
	}

	public void ResetRecruiterCosts(string name)
	{
		RecruiterCosts.Remove(name);
	}

	public void AddRecruitmentCosts(float cost)
	{
		RecruitmentCosts += cost;
		WeeklyRecruitmentCosts += cost;
	}

	public void ResetRecruitmentCosts()
	{
		RecruitmentCosts = 0f;
	}

	public void AddUpgradeCosts(float cost)
	{
		UnitUpgradeCosts += cost;
		WeeklyTrainingCosts += cost;
	}

	public void ResetUpgradeCosts()
	{
		UnitUpgradeCosts = 0f;
	}
}
