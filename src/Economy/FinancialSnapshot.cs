using System;
using System.Collections.Generic;
using System.Threading;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// 财政自治运行期快照（受管氏族金库 + 各受管领地单城 P&amp;L），供 ActivityTabVM「状态一览」消费。
///
/// 模式：写入发生在 Campaign 主线程（<c>CapitalLogisticsManager.EvaluateClan</c> 每 logistics
/// tick 一次），控制面板 Tab「状态一览」打开时只读读取。引用赋值在 CLR 上原子；
/// <see cref="Volatile"/> 套一层 release-acquire 语义。
///
/// DTO 只持 string / 数值，绝不持有 Settlement / Clan / Town 任何 vanilla 引用。
/// </summary>
internal static class FinancialSnapshot
{
    /// <summary>单座受管领地的盈亏行。</summary>
    public sealed class SettlementPnl
    {
        public string SettlementId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsCastle { get; set; }
        public long Income { get; set; }
        public long GarrisonWage { get; set; }
        public long Net { get; set; }
        public int CurrentGarrison { get; set; }
        public int TargetGarrison { get; set; }
    }

    /// <summary>单个受管氏族的财政视图。</summary>
    public sealed class ClanFinance
    {
        public string ClanId { get; set; } = "";
        public string ClanName { get; set; } = "";
        public long TreasuryBalance { get; set; }
        public long TrailingDailyExpense { get; set; }
        public long TotalIncome { get; set; }
        public long TotalGarrisonWage { get; set; }
        public long GarrisonWageBudget { get; set; }
        public List<SettlementPnl> Settlements { get; set; } = new List<SettlementPnl>();
    }

    private static IReadOnlyList<ClanFinance> _snapshot = Array.Empty<ClanFinance>();
    private static string _playerClanId = "";

    public static void SetPlayerClanId(string id)
        => Volatile.Write(ref _playerClanId, id ?? "");

    public static string PlayerClanId => Volatile.Read(ref _playerClanId) ?? "";

    public static IReadOnlyList<ClanFinance> Read()
        => Volatile.Read(ref _snapshot) ?? Array.Empty<ClanFinance>();

    public static ClanFinance? ReadPlayerClan(string playerClanId)
    {
        if (string.IsNullOrEmpty(playerClanId)) return null;
        foreach (var cf in Read())
            if (cf.ClanId == playerClanId) return cf;
        return null;
    }

    /// <summary>主线程调用：复制旧快照 → 替换 / 删除本 clan 条目 → 整体原子换上。</summary>
    public static void ReplaceClan(string clanId, ClanFinance? entry)
    {
        try
        {
            if (string.IsNullOrEmpty(clanId)) return;
            var fresh = new List<ClanFinance>();
            var previous = Volatile.Read(ref _snapshot);
            if (previous != null)
                foreach (var cf in previous)
                    if (cf.ClanId != clanId) fresh.Add(cf);
            if (entry != null) fresh.Add(entry);
            Volatile.Write(ref _snapshot, fresh);
        }
        catch (Exception ex)
        {
            Logger.Error($"FinancialSnapshot.ReplaceClan failed (clan={clanId})", ex);
        }
    }

    /// <summary>把当前 snapshot 中该 clan 的 TreasuryBalance 替换为 <paramref name="newBalance"/>。</summary>
    public static void PatchTreasuryBalance(string clanId, long newBalance)
    {
        try
        {
            if (string.IsNullOrEmpty(clanId)) return;
            var previous = Volatile.Read(ref _snapshot);
            if (previous == null) return;
            var fresh = new List<ClanFinance>(previous.Count);
            bool found = false;
            foreach (var cf in previous)
            {
                if (cf.ClanId == clanId)
                {
                    fresh.Add(new ClanFinance
                    {
                        ClanId = cf.ClanId,
                        ClanName = cf.ClanName,
                        TreasuryBalance = newBalance,
                        TrailingDailyExpense = cf.TrailingDailyExpense,
                        TotalIncome = cf.TotalIncome,
                        TotalGarrisonWage = cf.TotalGarrisonWage,
                        GarrisonWageBudget = cf.GarrisonWageBudget,
                        Settlements = cf.Settlements,
                    });
                    found = true;
                }
                else
                {
                    fresh.Add(cf);
                }
            }
            if (found) Volatile.Write(ref _snapshot, fresh);
        }
        catch (Exception ex)
        {
            Logger.Error($"FinancialSnapshot.PatchTreasuryBalance failed (clan={clanId})", ex);
        }
    }
}
