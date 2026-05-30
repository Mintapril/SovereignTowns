using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Audit;

/// <summary>
/// 决策来源：规则引擎或玩家。
/// </summary>
public enum DecisionSource
{
    Rule = 0,
    User = 1
}

/// <summary>
/// 一条决策审计记录。
/// </summary>
public sealed class AuditEntry
{
    /// <summary>UTC 时间戳。</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>决策类型标识，如 "send_recruiting_party", "transfer_troops", "create_patrol_party"。</summary>
    public string DecisionType { get; set; } = "";

    /// <summary>决策来源。</summary>
    public DecisionSource Source { get; set; }

    /// <summary>局势摘要（短文本）。</summary>
    public string InputSummary { get; set; } = "";

    /// <summary>决策内容（JSON 字符串或 key=val 形式）。</summary>
    public string DecisionJson { get; set; } = "";

    /// <summary>该决策是否被接受执行。</summary>
    public bool Accepted { get; set; }

    /// <summary>拒绝原因（被拒时填写，接受时可为 null）。</summary>
    public string? RejectionReason { get; set; }
}

/// <summary>
/// 决策审计日志器。与一般 Logger 共存但独立写文件，专记决策流水。
/// 异步写入：主线程入队后立即返回，由后台 Task 负责落盘。
/// 文件路径：%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\Audit\Audit_yyyy-MM-dd_HH-mm-ss.log
/// 单文件 5 MB 自动轮转。
/// </summary>
public static class DecisionAuditLogger
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const int FlushIntervalMs = 200;
    private const string ModuleId = "SovereignTowns";

    private static readonly ConcurrentQueue<AuditEntry> _queue = new ConcurrentQueue<AuditEntry>();
    // R8：CTS 必须可替换以支持 Shutdown → re-Initialize 流。
    private static CancellationTokenSource _cts = new CancellationTokenSource();
    private static readonly object _fileLock = new object();

    private static Task? _writerTask;
    private static string? _logDir;
    private static string? _logFilePath;
    private static long _currentFileSize;
    private static bool _initialized;

    /// <summary>启动期一次性初始化。重复调用安全（仅首次生效）。</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _logDir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModLogs", "SovereignTowns", "Audit");
            if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
            RotateFile();
            _writerTask = Task.Run(WriteLoop);
            _initialized = true;
            Logger.Info($"DecisionAuditLogger initialized: path={_logFilePath}");
        }
        catch (Exception ex)
        {
            try
            {
                Logger.Error("DecisionAuditLogger init failed", ex);
            }
            catch
            {
                TaleWorlds.Library.Debug.Print($"[SovereignTowns][Audit] init failed: {ex.Message}");
            }
        }
    }

    /// <summary>记录一条决策。线程安全。主线程零阻塞（入队后异步写盘）。</summary>
    public static void Log(AuditEntry entry)
    {
        if (!_initialized || entry is null) return;
        _queue.Enqueue(entry);
    }

    /// <summary>便利方法：直接构造规则来源的审计条目并入队。</summary>
    public static void LogRule(string decisionType, string inputSummary, string decisionJson, bool accepted, string? rejectionReason = null)
    {
        if (!_initialized) return;
        _queue.Enqueue(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            DecisionType = decisionType ?? "",
            Source = DecisionSource.Rule,
            InputSummary = inputSummary ?? "",
            DecisionJson = decisionJson ?? "",
            Accepted = accepted,
            RejectionReason = rejectionReason
        });

        // B17.4 A2：路由到 per-settlement ring + daily counters,不影响磁盘审计流。
        if (!accepted) return;  // 仅成功决策记录到 ring/counter
        TryUpdateActivityRingAndCounters(decisionType ?? "", inputSummary ?? "", decisionJson ?? "");
    }

    /// <summary>
    /// B17.4 A2：从 inputSummary 中粗略解析 settlement key(home= / source= / village= / town= / dest=),
    /// 路由到 PerSettlementActivityRing,并按 decisionType 累加 DailyActivityCounters。
    /// 失败仅吞 — A2 是辅助功能,绝不影响审计主路径。
    /// decisionType 字符串对照实际代码(plan 里 PrisonerRecruit / DispatchSally 与实际不符,以代码为准):
    ///   "RecruitFromVillage"     → +recruited
    ///   "DispatchRecruiter"      → no-op(派出 ≠ 招到)
    ///   "DispatchTransfer"       → +transferred
    ///   "create_patrol_party"    → +1 patrol
    ///   "create_sally_party"     → +1 sally(SallyDispatcher.cs:286)
    ///   "RecruitPrisoner"        → +prisoners(PrisonerRecruitmentManager.cs:223)
    /// </summary>
    private static void TryUpdateActivityRingAndCounters(string decisionType, string inputSummary, string decisionJson)
    {
        try
        {
            // 优先 home=,然后 source=,然后 village=,最后 town=(prisoner recruit 使用 town=...)
            string? key = ExtractKey(inputSummary, "home=")
                ?? ExtractKey(inputSummary, "source=")
                ?? ExtractKey(inputSummary, "village=")
                ?? ExtractKey(inputSummary, "town=");
            if (key != null)
            {
                PerSettlementActivityRing.Add(key, decisionType, inputSummary, decisionJson);
            }
            string? destKey = ExtractKey(inputSummary, "dest=");
            if (destKey != null && destKey != key)
            {
                PerSettlementActivityRing.Add(destKey, decisionType + "(inbound)", inputSummary, decisionJson);
            }

            switch (decisionType)
            {
                case "RecruitFromVillage":
                    DailyActivityCounters.AddRecruited(ExtractInt(decisionJson, "recruited"));
                    break;
                case "DispatchTransfer":
                    DailyActivityCounters.AddTransferred(ExtractInt(decisionJson, "extracted"));
                    break;
                case "create_patrol_party":
                    DailyActivityCounters.AddPatrolDispatched(1);
                    break;
                case "create_sally_party":
                    DailyActivityCounters.AddSallyDispatched(1);
                    break;
                case "RecruitPrisoner":
                    DailyActivityCounters.AddPrisonerRecruited(ExtractInt(decisionJson, "recruited"));
                    break;
            }

            // B17.5：翻译为玩家可读动态 → 全局 ActivityFeed（游戏内控制面板展示）。
            var narrated = ActivityNarrator.Narrate(decisionType, decisionJson);
            if (narrated != null)
                ActivityFeed.Add(narrated.Value.tone, narrated.Value.text, CurrentGameTimeLabel());
        }
        catch { /* swallow */ }
    }

    private static string CurrentGameTimeLabel()
    {
        try { return CampaignTime.Now.ToString(); }
        catch { return ""; }
    }

    /// <summary>从 "key1=val1 key2=val2" 串里取 prefix 后的 token。失败返 null。</summary>
    private static string? ExtractKey(string text, string prefix)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int idx = text.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        int start = idx + prefix.Length;
        int end = text.IndexOf(' ', start);
        if (end < 0) end = text.Length;
        if (end <= start) return null;
        return text.Substring(start, end - start);
    }

    /// <summary>从 decisionJson 取整数字段。解析失败 / 字段缺失返 0(不抛)。</summary>
    private static int ExtractInt(string json, string key)
    {
        try
        {
            if (string.IsNullOrEmpty(json)) return 0;
            return JObject.Parse(json).Value<int?>(key) ?? 0;
        }
        catch { return 0; }
    }

    /// <summary>关闭审计：取消后台任务并冲刷队列残留。</summary>
    public static void Shutdown()
    {
        try
        {
            _cts.Cancel();
            _writerTask?.Wait(TimeSpan.FromSeconds(2));
            while (_queue.TryDequeue(out var entry)) WriteOne(entry);
        }
        catch { /* swallow on shutdown */ }
        finally
        {
            // R8：复位状态，允许同进程内 re-Initialize。
            try { _cts.Dispose(); } catch { }
            _cts = new CancellationTokenSource();
            _writerTask = null;
            _initialized = false;
        }
    }

    private static async Task WriteLoop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            while (_queue.TryDequeue(out var entry))
            {
                WriteOne(entry);
            }
            try { await Task.Delay(FlushIntervalMs, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
        }
    }

    private static void WriteOne(AuditEntry entry)
    {
        if (_logFilePath is null || entry is null) return;
        var line = FormatLine(entry);
        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, line);
                _currentFileSize += Encoding.UTF8.GetByteCount(line);
                if (_currentFileSize >= MaxFileSizeBytes) RotateFile();
            }
        }
        catch (Exception ex)
        {
            try
            {
                Logger.Error("DecisionAuditLogger write failed", ex);
            }
            catch
            {
                TaleWorlds.Library.Debug.Print($"[SovereignTowns][Audit] write fail: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 行格式：[ISO8601 UTC] [Source] DecisionType | accepted=&lt;bool&gt; | reject=&lt;reason or "-"&gt; | input=&lt;summary&gt; | decision=&lt;json&gt;
    /// </summary>
    private static string FormatLine(AuditEntry entry)
    {
        var sb = new StringBuilder(256);
        sb.Append('[').Append(entry.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")).Append(']');
        sb.Append(" [").Append(entry.Source.ToString()).Append("] ");
        sb.Append(Sanitize(entry.DecisionType));
        sb.Append(" | accepted=").Append(entry.Accepted ? "true" : "false");
        sb.Append(" | reject=").Append(string.IsNullOrEmpty(entry.RejectionReason) ? "-" : Sanitize(entry.RejectionReason!));
        sb.Append(" | input=").Append(Sanitize(entry.InputSummary));
        sb.Append(" | decision=").Append(Sanitize(entry.DecisionJson));
        sb.Append(Environment.NewLine);
        return sb.ToString();
    }

    /// <summary>剥除换行符以保证单条记录占单行。</summary>
    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOf('\r') < 0 && s.IndexOf('\n') < 0) return s;
        return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
    }

    private static void RotateFile()
    {
        if (_logDir is null) return;
        _logFilePath = Path.Combine(_logDir, $"Audit_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.log");
        _currentFileSize = 0;
    }
}
