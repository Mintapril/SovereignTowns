using System;
using TaleWorlds.Library;
using SovereignTowns.Economy;
using SovereignTowns.WebConfig;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
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

    [DataSourceProperty]
    public bool HasNoRecent => _recentEntries.Count == 0;

    // ── 空条目提示 ──
    [DataSourceProperty]
    public string EmptyRecentText => ControlPanelLoc.Tr("尚无支出记录", "No spending records yet");

    // ── Task 9: 财政自治视图（金库 + 单城 P&L + 手动模式评估）──

    [DataSourceProperty] public string FiscalTitle { get; }
    [DataSourceProperty] public string TreasuryTitle { get; }
    [DataSourceProperty] public string PnlTitle { get; }
    [DataSourceProperty] public string AssessmentTitle { get; }
    [DataSourceProperty] public string AssessmentIntro { get; }

    private readonly MBBindingList<FinanceRowVM> _treasuryRows = new MBBindingList<FinanceRowVM>();
    private readonly MBBindingList<FinanceRowVM> _pnlRows = new MBBindingList<FinanceRowVM>();
    private readonly MBBindingList<FinanceRowVM> _assessmentRows = new MBBindingList<FinanceRowVM>();
    [DataSourceProperty] public MBBindingList<FinanceRowVM> TreasuryRows => _treasuryRows;
    [DataSourceProperty] public MBBindingList<FinanceRowVM> PnlRows => _pnlRows;
    [DataSourceProperty] public MBBindingList<FinanceRowVM> AssessmentRows => _assessmentRows;

    private bool _hasFiscal;
    [DataSourceProperty]
    public bool HasFiscal
    {
        get => _hasFiscal;
        private set { if (_hasFiscal != value) { _hasFiscal = value; OnPropertyChanged(nameof(HasFiscal)); OnPropertyChanged(nameof(HasNoFiscal)); } }
    }
    [DataSourceProperty] public bool HasNoFiscal => !_hasFiscal;

    private bool _showAssessment;
    [DataSourceProperty]
    public bool ShowAssessment
    {
        get => _showAssessment;
        private set { if (_showAssessment != value) { _showAssessment = value; OnPropertyChanged(nameof(ShowAssessment)); } }
    }

    [DataSourceProperty]
    public string EmptyFiscalText => ControlPanelLoc.Tr(
        "暂无财政数据 —— 调度器尚未运行（载入存档后等一个游戏日）。",
        "No fiscal data yet — the dispatcher has not run (wait one in-game day after loading a save).");

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

        FiscalTitle     = ControlPanelLoc.Tr("财政自治", "Fiscal autonomy");
        TreasuryTitle   = ControlPanelLoc.Tr("氏族金库", "Clan treasury");
        PnlTitle        = ControlPanelLoc.Tr("各领地盈亏（收入 / 驻军工资 / 净额；推荐 vs 当前驻军）",
                                             "Per-holding P&L (income / garrison wage / net; recommended vs current garrison)");
        AssessmentTitle = ControlPanelLoc.Tr("手动驻军目标评估", "Manual garrison target assessment");
        AssessmentIntro = ControlPanelLoc.Tr(
            "已开启手动驻军目标。下表对比你设定的目标与调度器推荐值；每日工资差额为正表示你比调度器推荐多付的工资。",
            "Manual garrison targets are on. The table below compares your set target with the dispatcher's recommendation; a positive daily wage delta is the extra wage you pay over the recommendation.");

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
            OnPropertyChanged(nameof(HasNoRecent));
            HasError  = false;
            ErrorText = "";
        }
        catch (Exception ex)
        {
            Logger.Error("FinanceTabVM.Refresh failed", ex);
            HasError  = true;
            ErrorText = ControlPanelLoc.Tr("财务数据加载失败，请查看日志。", "Failed to load finance data — see logs.");
        }

        RefreshFiscal();
    }

    /// <summary>
    /// Task 9: 重建财政自治视图（金库摘要 + 单城 P&L + 手动模式评估）。
    /// 数据源为主线程产出的纯数值快照（FinancialSnapshot / LatestAssessments）—— 控制面板
    /// 打开期间游戏已暂停，快照不变；与 WebUI 的 /api/finance + /api/assessment 同源。
    /// </summary>
    private void RefreshFiscal()
    {
        try
        {
            _treasuryRows.Clear();
            _pnlRows.Clear();
            _assessmentRows.Clear();

            string playerClanId = FinancialSnapshot.PlayerClanId;
            var cf = FinancialSnapshot.ReadPlayerClan(playerClanId);
            HasFiscal = cf != null;

            if (cf != null)
            {
                // 金库摘要（2 列：项目 / 金额）。
                _treasuryRows.Add(new FinanceRowVM(
                    ControlPanelLoc.Tr("金库余额", "Treasury balance"), cf.TreasuryBalance + "d"));
                _treasuryRows.Add(new FinanceRowVM(
                    ControlPanelLoc.Tr("缓冲金上限", "Buffer cap"), cf.BufferCap + "d"));
                _treasuryRows.Add(new FinanceRowVM(
                    ControlPanelLoc.Tr("近 7 日日均开销", "Trailing daily expense"), cf.TrailingDailyExpense + "d"));

                // 单城 P&L 表头 + 每城行（4 列：领地 / 收入 / 驻军工资 / 净额；推荐驻军附在领地名）。
                _pnlRows.Add(new FinanceRowVM(
                    ControlPanelLoc.Tr("领地（推荐/当前驻军）", "Holding (rec/cur garrison)"),
                    ControlPanelLoc.Tr("收入", "Income"),
                    ControlPanelLoc.Tr("驻军工资", "Garrison wage"),
                    ControlPanelLoc.Tr("净额", "Net")));
                foreach (var s in cf.Settlements)
                {
                    string label = s.Name + " (" + s.RecommendedGarrison + "/" + s.CurrentGarrison + ")";
                    _pnlRows.Add(new FinanceRowVM(
                        label,
                        s.Income + "d",
                        s.GarrisonWage + "d",
                        (s.Net >= 0 ? "+" : "") + s.Net + "d"));
                }
                _pnlRows.Add(new FinanceRowVM(
                    ControlPanelLoc.Tr("合计", "Total"),
                    cf.TotalIncome + "d",
                    cf.TotalGarrisonWage + "d",
                    ((cf.TotalIncome - cf.TotalGarrisonWage) >= 0 ? "+" : "")
                        + (cf.TotalIncome - cf.TotalGarrisonWage) + "d",
                    isTotal: true));
            }

            // 手动模式评估（仅 AllowManualGarrisonTargets 为 true 时展示）。
            bool manualMode = ConfigurationManager.Current?.FiscalAutonomy?.AllowManualGarrisonTargets ?? false;
            ShowAssessment = manualMode;
            if (manualMode && !string.IsNullOrEmpty(playerClanId))
            {
                var all = SovereignTowns.Managers.CapitalLogisticsManager.LatestAssessments;
                if (all.TryGetValue(playerClanId, out var entries) && entries != null && entries.Count > 0)
                {
                    _assessmentRows.Add(new FinanceRowVM(
                        ControlPanelLoc.Tr("领地", "Holding"),
                        ControlPanelLoc.Tr("你的目标", "Your target"),
                        ControlPanelLoc.Tr("推荐目标", "Recommended"),
                        ControlPanelLoc.Tr("每日工资差额", "Daily wage delta")));
                    foreach (var a in entries)
                    {
                        if (a == null) continue;
                        string verdict = a.LoopClosesAtPlayerTarget
                            ? ControlPanelLoc.Tr(" ✓闭环", " ✓sustainable")
                            : ControlPanelLoc.Tr(" ⚠超支", " ⚠over budget");
                        _assessmentRows.Add(new FinanceRowVM(
                            a.SettlementId,
                            a.PlayerTarget.ToString(),
                            a.RecommendedTarget.ToString(),
                            (a.DailyWageDelta >= 0 ? "+" : "") + a.DailyWageDelta + "d" + verdict));
                    }
                }
            }

            OnPropertyChanged(nameof(HasNoFiscal));
        }
        catch (Exception ex)
        {
            Logger.Error("FinanceTabVM.RefreshFiscal failed", ex);
        }
    }

    // ── 辅助 ──

    private static FinanceTableVM MakeEmpty(string title) =>
        new FinanceTableVM(title, new System.Collections.Generic.Dictionary<string, long>(), 0L);
}
