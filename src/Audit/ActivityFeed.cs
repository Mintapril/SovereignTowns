using System;
using System.Collections.Generic;

namespace SovereignTowns.Audit;

/// <summary>
/// 全局"运行动态"环形缓冲 — 玩家可读的 mod 行为流水。
/// 与 PerSettlementActivityRing(按城分桶、技术格式)不同:这里是单一全局序列,
/// 存的是 ActivityNarrator 已翻译成人话的成品文本,游戏内控制面板与 WebUI 直接展示。
/// 纯 in-memory,不持久化 — 重载存档后清空、随 mod 行为重新累积。
/// </summary>
public static class ActivityFeed
{
    /// <summary>保留最近 N 条。控制面板/WebUI 一屏展示约 30~40 条,80 足够回看。</summary>
    public const int Capacity = 80;

    public sealed class Entry
    {
        /// <summary>玩家可读的时间(游戏内日期字符串,如 "Spring 3, 1084")。</summary>
        public string When { get; set; } = "";
        /// <summary>一句话描述,已本地化为当前游戏语言。</summary>
        public string Text { get; set; } = "";
        /// <summary>色调:info / ok / warn / err — 两端 UI 据此着色。</summary>
        public string Tone { get; set; } = "info";
    }

    private static readonly LinkedList<Entry> _entries = new LinkedList<Entry>();
    private static readonly object _lock = new object();

    /// <summary>追加一条动态(最新在前)。线程安全。text 为空则忽略。</summary>
    public static void Add(string tone, string text, string when)
    {
        if (string.IsNullOrEmpty(text)) return;
        var entry = new Entry
        {
            When = when ?? "",
            Text = text,
            Tone = string.IsNullOrEmpty(tone) ? "info" : tone,
        };
        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity) _entries.RemoveLast();
        }
    }

    /// <summary>读取最近 N 条(从新到旧)。返回 snapshot,后续修改不影响调用方。</summary>
    public static IReadOnlyList<Entry> Read(int maxCount = Capacity)
    {
        lock (_lock)
        {
            int count = Math.Min(maxCount, _entries.Count);
            var snap = new List<Entry>(count);
            var node = _entries.First;
            for (int i = 0; i < count && node != null; i++, node = node.Next)
                snap.Add(node.Value);
            return snap;
        }
    }
}
