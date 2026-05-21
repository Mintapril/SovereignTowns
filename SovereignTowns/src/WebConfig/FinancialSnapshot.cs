using System;
using System.Collections.Generic;
using System.Threading;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// 财政自治运行期快照（受管氏族金库 + 各受管领地单城 P&amp;L）。
///
/// 与 <see cref="SettlementsSnapshot"/> / <c>CapitalLogisticsManager.LatestAssessments</c> 同一
/// 跨线程模式：写入发生在 Campaign 主线程（<c>CapitalLogisticsManager.EvaluateClan</c> 每日一次，
/// 此时已算出 Pass A 分配结果），HTTP /api/finance 与控制面板状态一览看板在各自线程只读读取。
/// 引用赋值在 CLR 上原子；<see cref="Volatile"/> 套一层 release-acquire 语义，无需锁。
///
/// DTO 只持 string / 数值，绝不持有 Settlement / Clan / Town 任何 vanilla 引用。
/// </summary>
internal static class FinancialSnapshot
{
    /// <summary>单座受管领地的盈亏行。income / wage 为重算值；recommended 来自 Pass A 分配。</summary>
    public sealed class SettlementPnl
    {
        public string SettlementId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsCastle { get; set; }
        public long Income { get; set; }            // 税+关税+村庄+项目
        public long GarrisonWage { get; set; }      // 当前驻军日工资
        public long Net { get; set; }               // Income - GarrisonWage
        public int CurrentGarrison { get; set; }    // 当前实际驻军头数
        public int RecommendedGarrison { get; set; }// Pass A 推荐目标头数
    }

    /// <summary>单个受管氏族的财政视图。</summary>
    public sealed class ClanFinance
    {
        public string ClanId { get; set; } = "";
        public string ClanName { get; set; } = "";
        public long TreasuryBalance { get; set; }
        public long BufferCap { get; set; }
        public long TrailingDailyExpense { get; set; }
        public long TotalIncome { get; set; }
        public long TotalGarrisonWage { get; set; }
        public long GarrisonWageBudget { get; set; }  // Pass A 算出的氏族驻军工资预算（金币/日）
        public List<SettlementPnl> Settlements { get; set; } = new List<SettlementPnl>();
    }

    // 引用赋值原子；Volatile.Read/Write 套 release-acquire。起始空 list，daily tick 来之前 UI 拿 []。
    private static IReadOnlyList<ClanFinance> _snapshot = Array.Empty<ClanFinance>();

    // 玩家氏族 StringId。HTTP 线程拿不到 vanilla Clan.PlayerClan，由主线程
    // (SettlementsSnapshot.Refresh 已读 Clan.PlayerClan) 发布到这里。string 赋值原子。
    private static string _playerClanId = "";

    /// <summary>主线程调用：发布当前玩家氏族 StringId。</summary>
    public static void SetPlayerClanId(string id)
        => Volatile.Write(ref _playerClanId, id ?? "");

    /// <summary>HTTP handler / 控制面板调用：当前玩家氏族 StringId（可能为空）。</summary>
    public static string PlayerClanId => Volatile.Read(ref _playerClanId) ?? "";

    /// <summary>HTTP handler / 控制面板调用：读最近一份快照（引用赋值原子，零拷贝）。</summary>
    public static IReadOnlyList<ClanFinance> Read()
        => Volatile.Read(ref _snapshot) ?? Array.Empty<ClanFinance>();

    /// <summary>玩家氏族的财政视图；无则返回 null。</summary>
    public static ClanFinance ReadPlayerClan(string playerClanId)
    {
        if (string.IsNullOrEmpty(playerClanId)) return null;
        foreach (var cf in Read())
            if (cf.ClanId == playerClanId) return cf;
        return null;
    }

    /// <summary>
    /// 在 Campaign 主线程调用：复制旧快照 → 替换 / 删除本 clan 条目 → 整体原子换上。
    /// <paramref name="entry"/> 为 null 时删除该 clan 条目（受管氏族失去全部领地等）。
    /// 任何失败保留旧快照不变（调用方应自带 try/catch，本方法亦兜底）。
    /// </summary>
    public static void ReplaceClan(string clanId, ClanFinance entry)
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
}
