using System;

namespace SovereignTowns.Economy;

/// <summary>
/// 单个受管氏族的金库。瞬态;余额 + 近 7 日实际开销环经 SyncData 的 st_treasuries_json 持久化。
/// 余额钳制 ≥ 0 —— 赤字由调用方按设计 §3.5 兜底。
/// </summary>
public sealed class ClanTreasury
{
    private long _balance;
    private readonly long[] _expenseByDay = new long[7];
    private int _dayCursor;

    public long Balance => _balance;

    public void Credit(long amount) { if (amount > 0) _balance += amount; }

    /// <summary>扣款。开销全额计入 7 日环;余额不足只扣到 0,返回欠款。</summary>
    public long Debit(long amount)
    {
        if (amount <= 0) return 0;
        _expenseByDay[_dayCursor] += amount;
        long shortfall = amount > _balance ? amount - _balance : 0;
        _balance -= (amount - shortfall);
        return shortfall;
    }

    public bool CanAfford(long amount) => amount <= 0 || _balance >= amount;

    public void RollDay() { _dayCursor = (_dayCursor + 1) % 7; _expenseByDay[_dayCursor] = 0; }

    public long TrailingDailyExpense()
    {
        long sum = 0; foreach (var d in _expenseByDay) sum += d; return sum / 7;
    }

    public long BufferCap(int bufferDays)
        => Math.Max(0, bufferDays) * Math.Max(0L, TrailingDailyExpense());

    public long SkimAboveBufferCap(int bufferDays)
    {
        long cap = BufferCap(bufferDays);
        if (_balance <= cap) return 0;
        long overflow = _balance - cap; _balance = cap; return overflow;
    }

    // 持久化形式: "balance;d0,..,d6;cursor"
    public string Serialize() => $"{_balance};{string.Join(",", _expenseByDay)};{_dayCursor}";

    public static ClanTreasury Deserialize(string? s)
    {
        var t = new ClanTreasury();
        try
        {
            if (string.IsNullOrEmpty(s)) return t;
            var p = s!.Split(';');
            if (p.Length >= 1) long.TryParse(p[0], out t._balance);
            if (p.Length >= 2)
            {
                var d = p[1].Split(',');
                for (int i = 0; i < 7 && i < d.Length; i++) long.TryParse(d[i], out t._expenseByDay[i]);
            }
            if (p.Length >= 3) int.TryParse(p[2], out t._dayCursor);
            if (t._dayCursor < 0 || t._dayCursor > 6) t._dayCursor = 0;
        }
        catch { /* → 空金库 */ }
        return t;
    }
}
