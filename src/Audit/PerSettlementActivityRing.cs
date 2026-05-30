using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SovereignTowns.Audit;

/// <summary>
/// B17.4 A2：按 settlement.StringId 分桶,每桶最近 N 条结构化活动 → 控制面板「近期动态」消费。
/// IG ActivityLog.cs:46-58 的 FIFO 容量 100；这里默认 50（玩家关心的窗口短）。
/// 内存常驻 ~20 town × 50 entry × ~300B ≈ 0.3MB,可忽略。纯 in-memory 不持久化。
/// </summary>
public static class PerSettlementActivityRing
{
    public const int Capacity = 50;

    public sealed class Entry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Kind { get; set; } = "";          // "Recruit" / "Transfer" / "Patrol" / "Sally" / "Prisoner" / ...
        public string Summary { get; set; } = "";        // 一句话摘要,如 "招募 12 兵 from Village_X"
        public string? DecisionJson { get; set; }        // 可选 — 复用 DecisionAuditLogger.DecisionJson
    }

    private static readonly ConcurrentDictionary<string, LinkedList<Entry>> _bySettlement = new();

    public static void Add(string? settlementStringId, string kind, string summary, string? decisionJson = null)
    {
        if (string.IsNullOrEmpty(settlementStringId)) return;
        var entry = new Entry
        {
            Timestamp = DateTime.UtcNow,
            Kind = kind ?? "",
            Summary = summary ?? "",
            DecisionJson = decisionJson,
        };
        var list = _bySettlement.GetOrAdd(settlementStringId!, _ => new LinkedList<Entry>());
        lock (list)
        {
            list.AddFirst(entry);
            while (list.Count > Capacity) list.RemoveLast();
        }
    }

    /// <summary>读取某 settlement 的最近 N 条活动(从新到旧)。返回 snapshot,后续修改不影响调用方。</summary>
    public static IReadOnlyList<Entry> Read(string settlementStringId, int maxCount = Capacity)
    {
        if (string.IsNullOrEmpty(settlementStringId)) return Array.Empty<Entry>();
        if (!_bySettlement.TryGetValue(settlementStringId, out var list)) return Array.Empty<Entry>();
        lock (list)
        {
            int count = Math.Min(Math.Max(0, maxCount), list.Count);
            var snap = new List<Entry>(count);
            var node = list.First;
            for (int i = 0; i < count && node != null; i++, node = node.Next)
                snap.Add(node.Value);
            return snap;
        }
    }

    public static void Clear() => _bySettlement.Clear();
}
