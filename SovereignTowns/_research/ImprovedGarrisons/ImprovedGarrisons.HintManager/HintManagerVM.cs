using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ImprovedGarrisons.HintManager;

public class HintManagerVM : ViewModel
{
	private DateTime lastUpdateTime = DateTime.Now;

	public MBBindingList<ImprovedGarrisonsHintVM> Hints;

	public ImprovedGarrisonsHintVM CurrentHint
	{
		get
		{
			if (Hints != null)
			{
				if (Index > ((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Count - 1)
				{
					Index = 0;
				}
				Index = ((Index <= ((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Count - 1) ? Index : 0);
				Index = ((Index < 0) ? (((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Count - 1) : Index);
				return ((Collection<ImprovedGarrisonsHintVM>)(object)Hints)[Index];
			}
			return null;
		}
	}

	private int Index { get; set; }

	public string TipsText { get; } = ((object)new TextObject("{=tipstext}Tips", (Dictionary<string, object>)null)).ToString();


	public HintManagerVM()
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		InitializeHints();
	}

	private void InitializeHints()
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected O, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected O, but got Unknown
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Expected O, but got Unknown
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected O, but got Unknown
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Expected O, but got Unknown
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Expected O, but got Unknown
		//IL_0120: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Expected O, but got Unknown
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Expected O, but got Unknown
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Expected O, but got Unknown
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Expected O, but got Unknown
		Hints = new MBBindingList<ImprovedGarrisonsHintVM>();
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip1}Any garrison can recruit and train template troops for you.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip2}Guard parties can join you during sieges.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip3}A training template can be used to specify the upgrade target path of any garrison.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip4}Clicking on the icon in the overview section tracks the village or settlement.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip5}You can copy the Improved Garrison settings from one garrison to another.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip6}The configuration manager is used to change many of this mod's values. It can be opened by pressing (ALT + G).", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip7}Recruiters can automatically collect troops depending on your current training template.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip8}The garrison wages can be disabled in the cheat section of the Improved Garrison settings.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip9}If set up, a garrison can automatically send a guard party to defend raided villages.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip10}A new guard party can be automatically created after the last one has been destroyed.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip11}Troops train faster if there are more units of one type.", (Dictionary<string, object>)null)).ToString()));
		((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Add(new ImprovedGarrisonsHintVM(((object)new TextObject("{=tips_tip12}Having trouble with prosperity and food shortage? Try out the food gathering cheat in the mods configuration!", (Dictionary<string, object>)null)).ToString()));
		ShuffleList((IList<ImprovedGarrisonsHintVM>)Hints);
		Index = new Random().Next(0, ((Collection<ImprovedGarrisonsHintVM>)(object)Hints).Count - 1);
	}

	public void ExecuteNextHint()
	{
		Index++;
		((ViewModel)this).OnPropertyChanged("CurrentHint");
	}

	public void ExecutePreviousHint()
	{
		Index--;
		((ViewModel)this).OnPropertyChanged("CurrentHint");
	}

	public void ShuffleList(IList<ImprovedGarrisonsHintVM> list)
	{
		Random random = new Random();
		int count = list.Count;
		for (int num = list.Count - 1; num > 1; num--)
		{
			int index = random.Next(num + 1);
			ImprovedGarrisonsHintVM value = list[index];
			list[index] = list[num];
			list[num] = value;
		}
	}

	public override void RefreshValues()
	{
		((ViewModel)this).RefreshValues();
		DateTime t = DateTime.Now.AddMilliseconds(-20000.0);
		if (DateTime.Compare(t, lastUpdateTime) > 0)
		{
			ExecuteNextHint();
			lastUpdateTime = DateTime.Now;
		}
	}
}
