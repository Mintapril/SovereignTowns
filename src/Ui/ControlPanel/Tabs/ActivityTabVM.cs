using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using SovereignTowns.Audit;
using SovereignTowns.Economy;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 6「状态一览 / Overview」VM —— 控制面板默认页（打开即显示）。
/// 汇总中央调度器的关键决策：氏族金库 / 驻军工资预算概览、各城镇城堡的驻军（人数）
/// 与收支、今日概况计数、近期动态流（招募 / 调运 / 巡逻 / 出击 / 预算调整 / 超额遣散）。
/// 纯展示；ControlPanelVM 在 ActiveTab 切到 6 时调 Refresh()。
/// 面板打开期间游戏已暂停 → 快照即当前态，无需轮询。
///
/// 数据源均为主线程产出的纯数值快照（FinancialSnapshot / DailyActivityCounters / ActivityFeed）。
/// </summary>
public sealed class ActivityTabVM : ViewModel
{
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }
    [DataSourceProperty] public string ClanTitle { get; }
    [DataSourceProperty] public string HoldingsTitle { get; }
    [DataSourceProperty] public string TodayLabel { get; }
    [DataSourceProperty] public string FeedTitle { get; }
    [DataSourceProperty] public string EmptyText { get; }
    [DataSourceProperty] public string EmptyFiscalText { get; }

    // 氏族调度概览（2 列：项目 / 值）
    private readonly MBBindingList<FinanceRowVM> _clanRows = new MBBindingList<FinanceRowVM>();
    [DataSourceProperty] public MBBindingList<FinanceRowVM> ClanRows => _clanRows;

    // 各领地状态表（4 列：领地 / 推荐·当前驻军 / 收入 / 净额）
    private readonly MBBindingList<FinanceRowVM> _holdingRows = new MBBindingList<FinanceRowVM>();
    [DataSourceProperty] public MBBindingList<FinanceRowVM> HoldingRows => _holdingRows;

    // 今日概况（5 格计数）
    private readonly MBBindingList<ActivityStatVM> _stats = new MBBindingList<ActivityStatVM>();
    [DataSourceProperty] public MBBindingList<ActivityStatVM> Stats => _stats;

    // 近期动态流
    private readonly MBBindingList<ActivityRowVM> _feed = new MBBindingList<ActivityRowVM>();
    [DataSourceProperty] public MBBindingList<ActivityRowVM> Feed => _feed;

    private bool _isEmpty = true;

    [DataSourceProperty]
    public bool IsEmpty
    {
        get => _isEmpty;
        private set { if (_isEmpty != value) { _isEmpty = value; OnPropertyChanged(nameof(IsEmpty)); } }
    }

    private bool _hasFiscal;

    [DataSourceProperty]
    public bool HasFiscal
    {
        get => _hasFiscal;
        private set
        {
            if (_hasFiscal != value)
            {
                _hasFiscal = value;
                OnPropertyChanged(nameof(HasFiscal));
                OnPropertyChanged(nameof(HasNoFiscal));
            }
        }
    }

    [DataSourceProperty] public bool HasNoFiscal => !_hasFiscal;


    public ActivityTabVM()
    {
        Title = ControlPanelLoc.Tr("状态一览", "Overview");
        Intro = ControlPanelLoc.Tr(
            "中央调度器的关键决策一览：氏族金库与驻军工资预算、各城镇城堡的驻军（人数）与收支，以及近期运行动态。",
            "Key decisions from the central dispatcher: clan treasury and garrison wage budget, each town/castle's garrison (headcount) and P&L, plus recent activity.");
        ClanTitle     = ControlPanelLoc.Tr("氏族调度概览", "Clan dispatch summary");
        HoldingsTitle = ControlPanelLoc.Tr("各领地状态", "Holdings status");
        TodayLabel    = ControlPanelLoc.Tr("今日概况", "Today at a glance");
        FeedTitle     = ControlPanelLoc.Tr("近期动态", "Recent activity");
        EmptyText     = ControlPanelLoc.Tr(
            "暂无动态 —— Mod 还没有执行任何操作。",
            "No activity yet — the mod hasn't taken any action.");
        EmptyFiscalText = ControlPanelLoc.Tr(
            "暂无调度数据 —— 调度器尚未运行（载入存档后等一个游戏日）。",
            "No dispatch data yet — the dispatcher has not run (wait one in-game day after loading a save).");

        Refresh();
    }

    /// <summary>重新读取调度快照、今日计数与动态流。ControlPanelVM 在切到本标签页时调用。</summary>
    public void Refresh()
    {
        RefreshFiscal();
        RefreshActivity();
    }

    /// <summary>重建氏族调度概览 + 各领地状态表（数据源 FinancialSnapshot）。</summary>
    private void RefreshFiscal()
    {
        try
        {
            _clanRows.Clear();
            _holdingRows.Clear();

            string playerClanId = FinancialSnapshot.PlayerClanId;
            var cf = FinancialSnapshot.ReadPlayerClan(playerClanId);
            HasFiscal = cf != null;
            if (cf == null) return;

            // 氏族概览（2 列）。Plan B 起金库 ≡ Clan.Gold，TrailingDailyExpense 行已移除。
            _clanRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("金库余额（= 氏族金币）", "Treasury balance (= clan gold)"), cf.TreasuryBalance + "d"));
            _clanRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("驻军工资预算", "Garrison wage budget"),
                cf.GarrisonWageBudget + "d/" + ControlPanelLoc.Tr("日", "day")));

            // 各领地状态表头 + 每城/堡行 + 合计行（4 列）。
            _holdingRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("领地", "Holding"),
                ControlPanelLoc.Tr("推荐/当前驻军", "Rec/cur garrison"),
                ControlPanelLoc.Tr("收入", "Income"),
                ControlPanelLoc.Tr("净额", "Net")));

            int totalRec = 0, totalCur = 0;
            foreach (var s in cf.Settlements)
            {
                if (s == null) continue;
                string castleTag = s.IsCastle ? ControlPanelLoc.Tr(" 〔堡〕", " (castle)") : "";
                _holdingRows.Add(new FinanceRowVM(
                    s.Name + castleTag,
                    s.RecommendedGarrison + "/" + s.CurrentGarrison,
                    s.Income + "d",
                    (s.Net >= 0 ? "+" : "") + s.Net + "d"));
                totalRec += s.RecommendedGarrison;
                totalCur += s.CurrentGarrison;
            }

            long netTotal = cf.TotalIncome - cf.TotalGarrisonWage;
            _holdingRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("合计", "Total"),
                totalRec + "/" + totalCur,
                cf.TotalIncome + "d",
                (netTotal >= 0 ? "+" : "") + netTotal + "d",
                isTotal: true));
        }
        catch (Exception ex)
        {
            Logger.Error("ActivityTabVM.RefreshFiscal failed", ex);
            HasFiscal = false;
        }
    }

    /// <summary>重建今日概况计数 + 近期动态流（数据源 DailyActivityCounters / ActivityFeed）。</summary>
    private void RefreshActivity()
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
            Logger.Error("ActivityTabVM.RefreshActivity failed", ex);
        }
    }
}
