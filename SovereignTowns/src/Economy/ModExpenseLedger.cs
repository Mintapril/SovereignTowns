using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// B7.27：mod 支出流水。内存保留最近 30 天 entries，超期累加到 _historicalRolledOver。
/// 持久化模型：当前阶段 mod 自定义存档已删除 —— 全部数据均瞬态，进程重启 / 读档后从零开始。
///
/// 线程模型：所有写入假定在 campaign tick（主线程）发起；网页 endpoint 在 ThreadPool 读取 BuildReport，
/// 用 _gate 锁保证一致性（rare contention，开销可忽略）。
/// </summary>
public static class ModExpenseLedger
{
    private static readonly object _gate = new();
    private static readonly List<ExpenseEntry> _entries = new();
    private static readonly Dictionary<ExpenseCategory, long> _historicalRolledOver = new();
    private const int MaxInMemoryDays = 30;

    /// <summary>追加一条 entry。同时触发轮转。</summary>
    public static void Record(ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return;
        try
        {
            lock (_gate)
            {
                long nowMs;
                try { nowMs = (long)CampaignTime.Now.ToMilliseconds; }
                catch { nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond; }

                _entries.Add(new ExpenseEntry
                {
                    TimestampMs = nowMs,
                    Category = category,
                    Amount = amount,
                    Note = note ?? ""
                });
                TrimAndRollOverIfNeeded();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.Record failed", ex);
        }
    }

    /// <summary>构建给 /api/finance 用的报告。</summary>
    public static FinanceReport BuildReport()
    {
        try
        {
            lock (_gate)
            {
                long nowMs;
                try { nowMs = (long)CampaignTime.Now.ToMilliseconds; }
                catch { nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond; }

                long todayStartMs = nowMs - (24L * 3600L * 1000L);
                long weekStartMs = nowMs - (7L * 24L * 3600L * 1000L);

                var today = AggregateBy(e => e.TimestampMs >= todayStartMs);
                var week  = AggregateBy(e => e.TimestampMs >= weekStartMs);
                var allTime = AggregateAllIncludingHistorical();

                // 最近 50 条（倒序）
                var recentEntries = _entries.Count > 50
                    ? _entries.GetRange(_entries.Count - 50, 50)
                    : new List<ExpenseEntry>(_entries);
                recentEntries.Reverse();

                return new FinanceReport
                {
                    Today = today.byCategory,
                    TodayTotal = today.total,
                    Week = week.byCategory,
                    WeekTotal = week.total,
                    AllTime = allTime.byCategory,
                    AllTimeTotal = allTime.total,
                    RecentEntries = recentEntries
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.BuildReport failed", ex);
            return new FinanceReport
            {
                Today = new(), Week = new(), AllTime = new(),
                RecentEntries = new()
            };
        }
    }

    /// <summary>清空全部瞬态支出状态。</summary>
    public static void Clear()
    {
        try
        {
            lock (_gate)
            {
                _entries.Clear();
                _historicalRolledOver.Clear();
                Logger.Info("ModExpenseLedger: cleared all state");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.Clear failed", ex);
        }
    }

    // ── 内部辅助 ──

    private static void TrimAndRollOverIfNeeded()
    {
        long nowMs;
        try { nowMs = (long)CampaignTime.Now.ToMilliseconds; }
        catch { return; }

        long cutoff = nowMs - (MaxInMemoryDays * 24L * 3600L * 1000L);
        while (_entries.Count > 0 && _entries[0].TimestampMs < cutoff)
        {
            var old = _entries[0];
            _historicalRolledOver.TryGetValue(old.Category, out var sum);
            _historicalRolledOver[old.Category] = sum + old.Amount;
            _entries.RemoveAt(0);
        }
    }

    private static (Dictionary<string, long> byCategory, long total) AggregateBy(Func<ExpenseEntry, bool> filter)
    {
        var dict = new Dictionary<string, long>();
        long total = 0;
        foreach (var e in _entries)
        {
            if (!filter(e)) continue;
            var key = e.Category.ToString();
            dict.TryGetValue(key, out var s);
            dict[key] = s + e.Amount;
            total += e.Amount;
        }
        return (dict, total);
    }

    private static (Dictionary<string, long> byCategory, long total) AggregateAllIncludingHistorical()
    {
        var dict = new Dictionary<string, long>();
        long total = 0;
        // historical 滚存
        foreach (var kv in _historicalRolledOver)
        {
            var key = kv.Key.ToString();
            dict[key] = kv.Value;
            total += kv.Value;
        }
        // 内存里 30 天 entries 累加
        foreach (var e in _entries)
        {
            var key = e.Category.ToString();
            dict.TryGetValue(key, out var s);
            dict[key] = s + e.Amount;
            total += e.Amount;
        }
        return (dict, total);
    }
}

/// <summary>单条支出 entry。public 属性供 Newtonsoft 序列化。</summary>
public sealed class ExpenseEntry
{
    public long TimestampMs { get; set; }
    public ExpenseCategory Category { get; set; }
    public int Amount { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>财务报告 DTO（响应 /api/finance）。</summary>
public sealed class FinanceReport
{
    public Dictionary<string, long> Today { get; set; } = new();
    public long TodayTotal { get; set; }
    public Dictionary<string, long> Week { get; set; } = new();
    public long WeekTotal { get; set; }
    public Dictionary<string, long> AllTime { get; set; } = new();
    public long AllTimeTotal { get; set; }
    public List<ExpenseEntry> RecentEntries { get; set; } = new();
}
