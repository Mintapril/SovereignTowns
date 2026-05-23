using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using SovereignTowns.Audit;
using SovereignTowns.Economy;
using SovereignTowns.WebConfig;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 6「状态一览 / Overview」VM —— 控制面板默认页（打开即显示）。
/// 汇总中央调度器的关键决策：氏族金库 / 驻军工资预算概览、各城镇城堡的驻军（人数）
/// 与收支、今日概况计数、近期动态流（招募 / 调运 / 巡逻 / 出击 / 预算调整 / 超额遣散）。
/// 纯展示；ControlPanelVM 在 ActiveTab 切到 6 时调 Refresh()。
/// 面板打开期间游戏已暂停 → 快照即当前态，无需轮询（WebUI 端才是真实时）。
///
/// 数据源均为主线程产出的纯数值快照（FinancialSnapshot / DailyActivityCounters /
/// ActivityFeed）—— 与 WebUI 的 /api/finance + /api/activity 同源。
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

    // ── 金库主动存取 ──
    [DataSourceProperty] public string TreasuryActionTitle { get; }
    [DataSourceProperty] public string TreasuryActionHint { get; }
    [DataSourceProperty] public string TreasurySourceNote { get; }
    [DataSourceProperty] public string DepositSmallLabel { get; }
    [DataSourceProperty] public string DepositMediumLabel { get; }
    [DataSourceProperty] public string DepositLargeLabel { get; }
    [DataSourceProperty] public string DepositAllLabel { get; }
    [DataSourceProperty] public string WithdrawSmallLabel { get; }
    [DataSourceProperty] public string WithdrawMediumLabel { get; }
    [DataSourceProperty] public string WithdrawLargeLabel { get; }
    [DataSourceProperty] public string WithdrawAllLabel { get; }

    private string _treasuryActionStatus = "";
    [DataSourceProperty]
    public string TreasuryActionStatus
    {
        get => _treasuryActionStatus;
        private set { if (_treasuryActionStatus != value) { _treasuryActionStatus = value; OnPropertyChanged(nameof(TreasuryActionStatus)); OnPropertyChanged(nameof(HasTreasuryActionStatus)); } }
    }
    [DataSourceProperty] public bool HasTreasuryActionStatus => !string.IsNullOrEmpty(_treasuryActionStatus);

    // 大额取款时,首次点击不直接执行 —— 设 _pendingLargeWithdraw=amount 并显示警告;
    // 再次点击同金额的取款按钮才放行。任何其他操作清零。
    private long _pendingLargeWithdraw = 0;

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

        TreasuryActionTitle = ControlPanelLoc.Tr("金库存取", "Treasury deposit / withdraw");
        TreasuryActionHint  = ControlPanelLoc.Tr(
            "金库与你的个人金币之间不再自动结算 —— 收入只单向流入金库,工资也只从金库支付。要在两者之间转账请用以下按钮。",
            "The treasury no longer auto-settles with your personal gold — income flows in only, garrison wages flow out only. Use the buttons below to move funds between the two.");
        TreasurySourceNote = ControlPanelLoc.Tr(
            "收入来源：受管城镇/城堡的每日税收、商业收入与所属村庄收入(原本进 vanilla 氏族金币,由 Mod 拦截转入本金库)。支出:驻军工资、派出队伍工资、装备升级费。\n注:作坊和商队收益不在内 —— 它们仍按 vanilla 走主角个人金币,不进本金库。",
            "Income sources: daily taxes, trade income, and village income from your managed towns and castles (intercepted from your vanilla clan gold and routed here). Outflows: garrison wages, dispatched-party wages, and equipment upgrades.\nNote: workshop and caravan profits are not included — those still flow into your personal hero gold via vanilla.");
        DepositSmallLabel    = ControlPanelLoc.Tr("存入 100",   "Deposit 100");
        DepositMediumLabel   = ControlPanelLoc.Tr("存入 1000",  "Deposit 1000");
        DepositLargeLabel    = ControlPanelLoc.Tr("存入 10000", "Deposit 10000");
        DepositAllLabel      = ControlPanelLoc.Tr("存入全部",   "Deposit all");
        WithdrawSmallLabel   = ControlPanelLoc.Tr("取出 100",   "Withdraw 100");
        WithdrawMediumLabel  = ControlPanelLoc.Tr("取出 1000",  "Withdraw 1000");
        WithdrawLargeLabel   = ControlPanelLoc.Tr("取出 10000", "Withdraw 10000");
        WithdrawAllLabel     = ControlPanelLoc.Tr("取出全部",   "Withdraw all");

        Refresh();
    }

    // ── 存款命令(预设金额)──
    public void ExecuteDepositSmall()  => DoDeposit(100);
    public void ExecuteDepositMedium() => DoDeposit(1000);
    public void ExecuteDepositLarge()  => DoDeposit(10000);
    public void ExecuteDepositAll()
    {
        try { DoDeposit(Hero.MainHero?.Gold ?? 0); }
        catch (Exception ex) { Logger.Error("ExecuteDepositAll failed", ex); }
    }

    // ── 取款命令(预设金额 + 大额二次确认)──
    public void ExecuteWithdrawSmall()  => DoWithdraw(100);
    public void ExecuteWithdrawMedium() => DoWithdraw(1000);
    public void ExecuteWithdrawLarge()  => DoWithdraw(10000);
    public void ExecuteWithdrawAll()
    {
        try
        {
            long bal = ResolveTreasuryBalance();
            if (bal > 0) DoWithdraw(bal);
        }
        catch (Exception ex) { Logger.Error("ExecuteWithdrawAll failed", ex); }
    }

    private long ResolveTreasuryBalance()
    {
        try
        {
            var clan = Clan.PlayerClan;
            if (clan == null) return 0;
            return Capital.CapitalRegistry.Instance?.GetForClan(clan)?.Treasury?.Balance ?? 0;
        }
        catch { return 0; }
    }

    private void DoDeposit(long amount)
    {
        // 存款不涉及大额警告 —— 直接执行。
        _pendingLargeWithdraw = 0;
        if (amount <= 0)
        {
            TreasuryActionStatus = ControlPanelLoc.Tr("无金币可存。", "No gold to deposit.");
            Refresh();
            return;
        }
        bool ok = TreasuryUserActions.TryDeposit(amount, out var reason, out long bal, out int gold);
        if (ok)
        {
            TreasuryActionStatus = string.Format(
                ControlPanelLoc.Tr("存入 {0}d → 金库 {1}d,金币 {2}d", "Deposited {0}d → treasury {1}d, gold {2}d"),
                amount, bal, gold);
        }
        else
        {
            TreasuryActionStatus = ControlPanelLoc.Tr("存款失败:", "Deposit failed: ") + reason;
        }
        Refresh();
    }

    private void DoWithdraw(long amount)
    {
        if (amount <= 0)
        {
            TreasuryActionStatus = ControlPanelLoc.Tr("金库无金币可取。", "Nothing to withdraw.");
            _pendingLargeWithdraw = 0;
            Refresh();
            return;
        }

        // 大额警告:amount > min(balance × 0.5, TrailingDailyExpense × 7) → 首次提示,需二次点击同金额。
        long bal = ResolveTreasuryBalance();
        long avg = 0;
        try
        {
            var clan = Clan.PlayerClan;
            if (clan != null)
                avg = Capital.CapitalRegistry.Instance?.GetForClan(clan)?.Treasury?.TrailingDailyExpense() ?? 0;
        }
        catch { avg = 0; }
        long warnThreshold = Math.Min(bal / 2, avg * 7);
        if (warnThreshold > 0 && amount > warnThreshold && _pendingLargeWithdraw != amount)
        {
            _pendingLargeWithdraw = amount;
            TreasuryActionStatus = string.Format(
                ControlPanelLoc.Tr(
                    "⚠ 取出 {0}d 可能让驻军欠饷(逃兵)。再次点击同一按钮以确认。",
                    "⚠ Withdrawing {0}d may leave the garrison unpaid (desertion). Click the same button again to confirm."),
                amount);
            Refresh();
            return;
        }
        _pendingLargeWithdraw = 0;

        bool ok = TreasuryUserActions.TryWithdraw(amount, out var reason, out long balAfter, out int goldAfter);
        if (ok)
        {
            TreasuryActionStatus = string.Format(
                ControlPanelLoc.Tr("取出 {0}d → 金库 {1}d,金币 {2}d", "Withdrew {0}d → treasury {1}d, gold {2}d"),
                amount, balAfter, goldAfter);
        }
        else
        {
            TreasuryActionStatus = ControlPanelLoc.Tr("取款失败:", "Withdraw failed: ") + reason;
        }
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

            // 氏族概览（2 列）。
            _clanRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("金库余额", "Treasury balance"), cf.TreasuryBalance + "d"));
            _clanRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("驻军工资预算", "Garrison wage budget"),
                cf.GarrisonWageBudget + "d/" + ControlPanelLoc.Tr("日", "day")));
            _clanRows.Add(new FinanceRowVM(
                ControlPanelLoc.Tr("近 7 日日均开销", "Trailing daily expense"), cf.TrailingDailyExpense + "d"));

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
