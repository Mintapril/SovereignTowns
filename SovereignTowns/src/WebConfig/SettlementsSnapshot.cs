using System;
using System.Collections.Generic;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// P0-6 修复：HTTP listener handler 跑在 ThreadPool 线程；但 vanilla 的 Town / Settlement / Clan
/// 对象只能在 Campaign 主线程访问（vanilla 内部很多字段不是 lock-free 的，并发触发会撕状态）。
///
/// 解法：CampaignBehavior 在 DailyTick 拉一份只读 DTO 快照写入此处；HTTP /api/settlements 端点
/// 改为读取快照。引用赋值在 .NET 上原子（CLI Memory Model §I.12.6.6），用 <c>Volatile.Read</c> /
/// <c>Volatile.Write</c> 包一层确保 release-acquire 语义即可，不需要锁。
///
/// DTO 中只放 string / 数值等不可变值，**绝不持有 Settlement / Clan / Town 任何 vanilla 引用**，
/// 否则就把跨线程访问问题搬到了消费方。
/// </summary>
internal static class SettlementsSnapshot
{
    /// <summary>给前端用的纯数据条目。字段命名与原 GetSettlements JSON 兼容。</summary>
    internal sealed class SettlementInfo
    {
        public string stringId { get; set; } = "";
        public string name { get; set; } = "";
        public string ownerClanStringId { get; set; } = "";
        public string ownerClanName { get; set; } = "";
        public bool isTown { get; set; }
        public bool isCastle { get; set; }
        public int prosperity { get; set; }
        public int currentGarrisonCount { get; set; }
    }

    // 引用类型字段赋值在 CLR 上原子；Volatile.Read/Write 套一层 release-acquire 语义。
    // 起始值为空 list,在 daily tick 来之前 UI 拿到 []。
    private static IReadOnlyList<SettlementInfo> _snapshot = Array.Empty<SettlementInfo>();

    /// <summary>HTTP handler 调用：读最近一份快照（引用赋值原子，零拷贝）。</summary>
    public static IReadOnlyList<SettlementInfo> Read()
        => Volatile.Read(ref _snapshot) ?? Array.Empty<SettlementInfo>();

    /// <summary>
    /// 在 Campaign 主线程调用：扫 vanilla 全城列表，过滤 player 所属，产出 DTO，整体替换。
    /// 任一条目 throw 时跳过当条，继续下一条；整方法异常时保留旧快照不变。
    /// 调用方应自带 try/catch。
    /// </summary>
    public static void Refresh()
    {
        try
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                Volatile.Write(ref _snapshot, Array.Empty<SettlementInfo>());
                return;
            }

            var fresh = new List<SettlementInfo>(8);

            // Town.AllTowns 同时包含 IsTown=true 与 IsCastle=true 的 Town wrapper（vanilla 设计：
            // 城镇与城堡共享 Town 类，IsCastle 区分）— 与原 GetSettlements 行为一致。
            foreach (var t in Town.AllTowns)
            {
                try
                {
                    if (t?.Settlement == null) continue;
                    if (t.OwnerClan != playerClan) continue;

                    var s = t.Settlement;
                    var owner = t.OwnerClan;

                    fresh.Add(new SettlementInfo
                    {
                        stringId = s.StringId ?? "",
                        name = s.Name?.ToString() ?? s.StringId ?? "",
                        ownerClanStringId = owner?.StringId ?? "",
                        ownerClanName = owner?.Name?.ToString() ?? "",
                        isTown = s.IsTown,
                        isCastle = s.IsCastle,
                        prosperity = (int)(t.Prosperity),
                        currentGarrisonCount = t.GarrisonParty?.MemberRoster?.TotalManCount ?? 0,
                    });
                }
                catch (Exception inner)
                {
                    Logger.Error("SettlementsSnapshot.Refresh: skipping one entry on error", inner);
                }
            }

            // 引用赋值原子；HTTP 读侧拿到的要么旧 list 要么新 list,绝不会撕开。
            Volatile.Write(ref _snapshot, fresh);
        }
        catch (Exception ex)
        {
            Logger.Error("SettlementsSnapshot.Refresh failed (keeping previous snapshot)", ex);
        }
    }
}
