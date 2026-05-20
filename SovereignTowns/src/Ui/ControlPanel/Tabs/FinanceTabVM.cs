using System;
using TaleWorlds.Library;
using SovereignTowns.Economy;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 5「财务 / Finance」VM。
/// 三张汇总表 (今日 / 本周 / 全部) + 近期流水列表（最多 50 条）。
/// 调用方在 ActiveTab 切换到 5 时调 Refresh() — 面板打开期间游戏已暂停，无需轮询。
/// </summary>
public sealed class FinanceTabVM : ViewModel
{
    // ── 标题 / 说明 ──
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }

    // ── 三张汇总表 ──
    private FinanceTableVM _todayTable;
    private FinanceTableVM _weekTable;
    private FinanceTableVM _allTimeTable;

    [DataSourceProperty]
    public FinanceTableVM TodayTable
    {
        get => _todayTable;
        private set { _todayTable = value; OnPropertyChanged(nameof(TodayTable)); }
    }

    [DataSourceProperty]
    public FinanceTableVM WeekTable
    {
        get => _weekTable;
        private set { _weekTable = value; OnPropertyChanged(nameof(WeekTable)); }
    }

    [DataSourceProperty]
    public FinanceTableVM AllTimeTable
    {
        get => _allTimeTable;
        private set { _allTimeTable = value; OnPropertyChanged(nameof(AllTimeTable)); }
    }

    // ── 近期流水 ──
    [DataSourceProperty] public string RecentTitle { get; }
    [DataSourceProperty] public string RecentColTime     { get; }
    [DataSourceProperty] public string RecentColCategory { get; }
    [DataSourceProperty] public string RecentColAmount   { get; }
    [DataSourceProperty] public string RecentColNote     { get; }

    private readonly MBBindingList<FinanceRowVM> _recentEntries = new MBBindingList<FinanceRowVM>();
    [DataSourceProperty] public MBBindingList<FinanceRowVM> RecentEntries => _recentEntries;

    // ── 错误状态 ──
    private bool _hasError;
    private string _errorText = "";

    [DataSourceProperty]
    public bool HasError
    {
        get => _hasError;
        private set { if (_hasError != value) { _hasError = value; OnPropertyChanged(nameof(HasError)); } }
    }

    [DataSourceProperty]
    public string ErrorText
    {
        get => _errorText;
        private set { if (_errorText != value) { _errorText = value; OnPropertyChanged(nameof(ErrorText)); } }
    }

    // ── 空状态 ──
    [DataSourceProperty]
    public bool HasRecent => _recentEntries.Count > 0;

    // ── 空条目提示 ──
    [DataSourceProperty]
    public string EmptyRecentText => ControlPanelLoc.Tr("尚无支出记录", "No spending records yet");

    public FinanceTabVM()
    {
        Title = ControlPanelLoc.Tr("财务报告", "Finance report");
        Intro = ControlPanelLoc.Tr(
            "本 Mod 引发的金币开销（招兵、升级、出击本钱等）。所有支出从玩家个人金币扣。",
            "Gold spending caused by this mod (recruitment, upgrades, sally seed funds, etc.). All spending is deducted from your personal gold.");

        RecentTitle       = ControlPanelLoc.Tr("近期流水（最近 50 条）", "Recent transactions (last 50)");
        RecentColTime     = ControlPanelLoc.Tr("时间", "Time");
        RecentColCategory = ControlPanelLoc.Tr("类别", "Category");
        RecentColAmount   = ControlPanelLoc.Tr("金额", "Amount");
        RecentColNote     = ControlPanelLoc.Tr("备注", "Note");

        // Initialise with empty tables so bindings are never null
        _todayTable   = MakeEmpty(ControlPanelLoc.Tr("今日", "Today"));
        _weekTable    = MakeEmpty(ControlPanelLoc.Tr("本周", "This week"));
        _allTimeTable = MakeEmpty(ControlPanelLoc.Tr("全部", "All time"));

        Refresh();
    }

    /// <summary>
    /// 重新从 ModExpenseLedger 读取财务报告并重建全部绑定列表。
    /// ControlPanelVM 在 ActiveTab 切换到 5 时调用。
    /// </summary>
    public void Refresh()
    {
        try
        {
            FinanceReport report = ControlPanelData.BuildFinanceReport();

            TodayTable   = new FinanceTableVM(ControlPanelLoc.Tr("今日", "Today"),   report.Today,   report.TodayTotal);
            WeekTable    = new FinanceTableVM(ControlPanelLoc.Tr("本周", "This week"), report.Week,    report.WeekTotal);
            AllTimeTable = new FinanceTableVM(ControlPanelLoc.Tr("全部", "All time"), report.AllTime, report.AllTimeTotal);

            _recentEntries.Clear();
            var entries = report.RecentEntries;
            int cap = Math.Min(entries.Count, 50);
            for (int i = 0; i < cap; i++)
            {
                var e = entries[i];
                string time;
                try
                {
                    time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddMilliseconds(e.TimestampMs)
                        .ToLocalTime()
                        .ToString("MM-dd HH:mm");
                }
                catch
                {
                    time = "-";
                }
                _recentEntries.Add(new FinanceRowVM(
                    time,
                    e.Category.ToString(),
                    "-" + e.Amount + "d",
                    e.Note ?? ""));
            }

            OnPropertyChanged(nameof(HasRecent));
            HasError  = false;
            ErrorText = "";
        }
        catch (Exception ex)
        {
            Logger.Error("FinanceTabVM.Refresh failed", ex);
            HasError  = true;
            ErrorText = ControlPanelLoc.Tr("财务数据加载失败，请查看日志。", "Failed to load finance data — see logs.");
        }
    }

    // ── 辅助 ──

    private static FinanceTableVM MakeEmpty(string title) =>
        new FinanceTableVM(title, new System.Collections.Generic.Dictionary<string, long>(), 0L);
}
