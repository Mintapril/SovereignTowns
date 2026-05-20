using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

public enum LogKind { Info, Ok, Err }

public sealed class LogEntryVM : ViewModel
{
    [DataSourceProperty] public string Timestamp { get; }
    [DataSourceProperty] public string Message { get; }
    /// <summary>prefab 按此着色：info / ok / err。</summary>
    [DataSourceProperty] public string ColorHint { get; }

    public LogEntryVM(string message, LogKind kind)
    {
        Timestamp = System.DateTime.Now.ToString("HH:mm:ss");
        Message = message;
        ColorHint = kind == LogKind.Err ? "err" : kind == LogKind.Ok ? "ok" : "info";
    }
}
