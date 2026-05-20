using System;
using TaleWorlds.Library;
using SovereignTowns.Audit;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 6「运行动态 / Activity」VM。
/// 今日概况(DailyActivityCounters)+ 近期动态流(ActivityFeed,已翻译成玩家可读文本)。
/// 纯展示;ControlPanelVM 在 ActiveTab 切到 6 时调 Refresh()。
/// 面板打开期间游戏已暂停 → 快照即当前态,无需轮询(WebUI 端才是真实时)。
/// </summary>
public sealed class ActivityTabVM : ViewModel
{
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }
    [DataSourceProperty] public string TodayLabel { get; }
    [DataSourceProperty] public string FeedTitle { get; }
    [DataSourceProperty] public string EmptyText { get; }

    private readonly MBBindingList<ActivityStatVM> _stats = new MBBindingList<ActivityStatVM>();
    [DataSourceProperty] public MBBindingList<ActivityStatVM> Stats => _stats;

    private readonly MBBindingList<ActivityRowVM> _feed = new MBBindingList<ActivityRowVM>();
    [DataSourceProperty] public MBBindingList<ActivityRowVM> Feed => _feed;

    private bool _isEmpty = true;

    [DataSourceProperty]
    public bool IsEmpty
    {
        get => _isEmpty;
        private set { if (_isEmpty != value) { _isEmpty = value; OnPropertyChanged(nameof(IsEmpty)); } }
    }

    public ActivityTabVM()
    {
        Title = ControlPanelLoc.Tr("运行动态", "Activity");
        Intro = ControlPanelLoc.Tr(
            "本 Mod 自动完成的招募、调运、巡逻、出击等行为,按时间倒序列出。",
            "Recruitment, transfers, patrols, sallies and other actions this mod performed — newest first.");
        TodayLabel = ControlPanelLoc.Tr("今日概况", "Today at a glance");
        FeedTitle  = ControlPanelLoc.Tr("近期动态", "Recent activity");
        EmptyText  = ControlPanelLoc.Tr(
            "暂无动态 —— Mod 还没有执行任何操作。",
            "No activity yet — the mod hasn't taken any action.");

        Refresh();
    }

    /// <summary>重新读取今日计数与活动流。ControlPanelVM 在切到本标签页时调用。</summary>
    public void Refresh()
    {
        try
        {
            var (recruited, transferred, patrols, sallies, prisoners) = DailyActivityCounters.Snapshot();
            _stats.Clear();
            _stats.Add(new ActivityStatVM(ControlPanelLoc.Tr("招募兵员", "Recruited"),  recruited));
            _stats.Add(new ActivityStatVM(ControlPanelLoc.Tr("调运兵员", "Transferred"), transferred));
            _stats.Add(new ActivityStatVM(ControlPanelLoc.Tr("派出巡逻", "Patrols"),     patrols));
            _stats.Add(new ActivityStatVM(ControlPanelLoc.Tr("出击迎敌", "Sallies"),     sallies));
            _stats.Add(new ActivityStatVM(ControlPanelLoc.Tr("策反俘虏", "Prisoners"),   prisoners));

            _feed.Clear();
            foreach (var e in ActivityFeed.Read())
                _feed.Add(new ActivityRowVM(e.When, e.Text, e.Tone));

            IsEmpty = _feed.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("ActivityTabVM.Refresh failed", ex);
        }
    }
}
