using System;

namespace SovereignTowns.Economy;

/// <summary>
/// 单个受管氏族的金库。瞬态;余额 + 近 7 日实际开销环经 SyncData 的 st_treasuries_json 持久化。
/// 余额钳制 ≥ 0。
///
/// 玩家氏族(2026-05-23 起):金库与 Hero.Gold 之间不再有自动通道 —— 收入由
/// <see cref="SovereignTowns.Models.STClanFinanceModel"/> 单向流入,工资由 daily 结算单向流出;
/// 玩家通过 <see cref="TreasuryUserActions"/> 主动存款/取款来调度个人金币与金库的资金流。
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

    /// <summary>
    /// 玩家主动取款专用:从金库扣余额,**不进开销环**(取款不是开销,不应抬高 TrailingDailyExpense)。
    /// 仅供 <see cref="TreasuryUserActions.TryWithdraw"/> 调用;扣到 0 截止,返回实际扣到的额。
    /// </summary>
    public long WithdrawForUser(long amount)
    {
        if (amount <= 0) return 0;
        long actual = Math.Min(amount, _balance);
        _balance -= actual;
        return actual;
    }

    /// <summary>玩家主动存款专用 —— 与 <see cref="Credit"/> 同语义,起独立名以便审计上区分入账来源。</summary>
    public void DepositFromUser(long amount) => Credit(amount);

    /// <summary>
    /// 退款路径：归还余额 AND 从当日开销环中扣除对应金额（夹紧 ≥ 0 防止跨日退款出现负值）。
    /// 与 Credit 的区别：Credit 仅增加余额（收入路径），Refund 同时回滚开销记录。
    /// </summary>
    public void Refund(long amount)
    {
        if (amount <= 0) return;
        _balance += amount;
        long slot = _expenseByDay[_dayCursor];
        _expenseByDay[_dayCursor] = slot > amount ? slot - amount : 0;
    }

    public void RollDay() { _dayCursor = (_dayCursor + 1) % 7; _expenseByDay[_dayCursor] = 0; }

    public long TrailingDailyExpense()
    {
        long sum = 0; foreach (var d in _expenseByDay) sum += d; return sum / 7;
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
