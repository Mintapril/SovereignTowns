using System;
using System.Collections.ObjectModel;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI;
using ImprovedGarrisons.ImprovedGarrisonsUI.CascadeMenuUI.CascadeMenuElements;
using ImprovedGarrisons.Tutorial;
using SandBox.View.Map;
using TaleWorlds.Library;

namespace ImprovedGarrisons.ImprovedGarrisonsUI;

public class UIManager
{
	public enum UiState
	{
		Expanded,
		Retracted,
		NotInitialized
	}

	public ImprovedGarrisonsUIGauntlet improvedGarrisonsUI;

	private int UiRefreshCounter = 0;

	private TutorialGauntlet tutorialGauntlet;

	public CascadeMenuGauntlet cascadeMenuGauntlet = new CascadeMenuGauntlet();

	private static UIManager _instance;

	public UiState CurrentUiState { get; set; } = UiState.NotInitialized;


	public static UIManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new UIManager();
			}
			return _instance;
		}
		set
		{
			_instance = value;
		}
	}

	public void ResetInstance()
	{
		try
		{
			_instance = null;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public bool TryInitializeImprovedGarrisonsUI()
	{
		try
		{
			if (Main.IsMapState && MapScreen.Instance != null && improvedGarrisonsUI == null)
			{
				improvedGarrisonsUI = new ImprovedGarrisonsUIGauntlet();
				CurrentUiState = UiState.Retracted;
				improvedGarrisonsUI.UpdateCurrentUiTab();
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	public void CloseImprovedGarrisonsUI()
	{
		try
		{
			if (improvedGarrisonsUI != null)
			{
				improvedGarrisonsUI.CloseUi();
				improvedGarrisonsUI = null;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void InvertUiState()
	{
		CurrentUiState = ((CurrentUiState == UiState.Expanded) ? UiState.Retracted : UiState.Expanded);
	}

	public void ForceFullRefresh()
	{
		if (improvedGarrisonsUI != null)
		{
			improvedGarrisonsUI.ForceFullRefresh();
		}
	}

	public void ForceOverviewUpdate()
	{
		if (improvedGarrisonsUI != null)
		{
			improvedGarrisonsUI.ForceOverviewUpdate();
		}
	}

	public void ForceRecruitmentUpdate()
	{
		if (improvedGarrisonsUI != null)
		{
			improvedGarrisonsUI.ForceOverviewUpdate();
		}
	}

	public void TryUpdateImprovedGarrisonsUI()
	{
		try
		{
			UiRefreshCounter++;
			if (Main.IsMapState && improvedGarrisonsUI != null && CurrentUiState == UiState.Expanded && UiRefreshCounter >= 32)
			{
				improvedGarrisonsUI.UpdateCurrentUiTab();
				UiRefreshCounter = 0;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void StartTutorial(bool onMapTutorial = false)
	{
		if (tutorialGauntlet == null)
		{
			tutorialGauntlet = new TutorialGauntlet(onMapTutorial);
		}
	}

	public void CloseTutorial()
	{
		if (tutorialGauntlet != null)
		{
			tutorialGauntlet.CloseTutorial();
			tutorialGauntlet = null;
		}
	}

	public void CloseCascadeMenu()
	{
		cascadeMenuGauntlet?.CloseCascadeMenu();
	}

	public void RefreshWholeUI()
	{
		CloseImprovedGarrisonsUI();
		TryInitializeImprovedGarrisonsUI();
	}

	public void CreateCascadeMenuOnMousePointer(string title, MBBindingList<CascadeMenuElementVM> elements, int suggestedWidth = -1)
	{
		if (((Collection<CascadeMenuElementVM>)(object)elements).Count > 0)
		{
			cascadeMenuGauntlet.InitializeAndPushCascadeMenu(title, elements, suggestedWidth);
		}
	}
}
