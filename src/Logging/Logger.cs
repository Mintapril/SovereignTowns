using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SovereignTowns.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

/// <summary>
/// 分级异步日志。所有 Tick 回调通过此 Logger 落盘；写入由独立后台 Task 完成，主线程零阻塞。
/// 文件路径：%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\SovereignTowns_yyyy-MM-dd_HH-mm-ss-fff.log
/// 单文件 5 MB 自动轮转。
/// </summary>
public static class Logger
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const int FlushIntervalMs = 200;

    private static readonly ConcurrentQueue<LogEntry> _queue = new ConcurrentQueue<LogEntry>();
    // R8：CTS 必须可替换。Shutdown 之后用户重新启用 mod / 重载 → Initialize 需要全新 token。
    private static CancellationTokenSource _cts = new CancellationTokenSource();
    private static readonly object _fileLock = new object();

    private static Task? _writerTask;
    private static string? _logDir;
    private static string? _logFilePath;
    private static long _currentFileSize;
    private static LogLevel _minLevel = LogLevel.Info;
    private static bool _initialized;

    public static void Initialize(string moduleId, LogLevel minLevel = LogLevel.Info)
    {
        if (_initialized) return;
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _logDir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModLogs", "SovereignTowns");
            if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
            RotateFile();
            _minLevel = minLevel;
            _writerTask = Task.Run(WriteLoop);
            _initialized = true;
            Info($"Logger initialized: path={_logFilePath}, minLevel={_minLevel}");
        }
        catch (Exception ex)
        {
            TaleWorlds.Library.Debug.Print($"[SovereignTowns][Logger] init failed: {ex.Message}");
        }
    }

    public static void SetMinLevel(LogLevel level) => _minLevel = level;

    public static void Debug(string message) => Enqueue(LogLevel.Debug, message);
    public static void Info(string message)  => Enqueue(LogLevel.Info, message);
    public static void Warn(string message)  => Enqueue(LogLevel.Warn, message);

    public static void Warn(string message, Exception? ex)
    {
        if (ex is null) Enqueue(LogLevel.Warn, message);
        else Enqueue(LogLevel.Warn, message + Environment.NewLine + ex);
    }

    public static void Error(string message, Exception? ex = null)
    {
        if (ex is null) Enqueue(LogLevel.Error, message);
        else Enqueue(LogLevel.Error, message + Environment.NewLine + ex);
    }

    public static void Shutdown()
    {
        try
        {
            _cts.Cancel();
            _writerTask?.Wait(TimeSpan.FromSeconds(2));
            // 写入残留
            while (_queue.TryDequeue(out var entry)) WriteOne(entry);
        }
        catch { /* swallow on shutdown */ }
        finally
        {
            // R8：复位状态，允许同进程内 re-Initialize（如 mod hot reload）。
            try { _cts.Dispose(); } catch { }
            _cts = new CancellationTokenSource();
            _writerTask = null;
            _initialized = false;
        }
    }

    private static void Enqueue(LogLevel level, string message)
    {
        if (!_initialized || level < _minLevel) return;
        _queue.Enqueue(new LogEntry(DateTime.Now, level, message));
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

    private static void WriteOne(LogEntry entry)
    {
        if (_logFilePath is null) return;
        var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level,-5}] {entry.Message}{Environment.NewLine}";
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
            // 退化输出
            TaleWorlds.Library.Debug.Print($"[SovereignTowns][Logger] write fail: {ex.Message}");
        }
    }

    private static void RotateFile()
    {
        if (_logDir is null) return;
        _logFilePath = Path.Combine(_logDir, $"SovereignTowns_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.log");
        _currentFileSize = 0;
    }

    private readonly struct LogEntry
    {
        public LogEntry(DateTime timestamp, LogLevel level, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Message { get; }
    }
}
