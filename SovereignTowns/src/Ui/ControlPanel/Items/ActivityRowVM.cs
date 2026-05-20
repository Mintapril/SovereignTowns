using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>「运行动态」标签页:活动流的一条记录。Tone 拆成三个 bool 供 prefab 着色。</summary>
public sealed class ActivityRowVM : ViewModel
{
    [DataSourceProperty] public string When { get; }
    [DataSourceProperty] public string Text { get; }
    [DataSourceProperty] public bool IsInfo { get; }
    [DataSourceProperty] public bool IsOk { get; }
    [DataSourceProperty] public bool IsErr { get; }

    public ActivityRowVM(string when, string text, string tone)
    {
        When = when ?? "";
        Text = text ?? "";
        IsOk  = tone == "ok";
        IsErr = tone == "err";
        IsInfo = !IsOk && !IsErr;
    }
}

/// <summary>「运行动态」标签页:今日概况的一格统计(标题 + 数值)。</summary>
public sealed class ActivityStatVM : ViewModel
{
    [DataSourceProperty] public string Caption { get; }
    [DataSourceProperty] public string Value { get; }

    public ActivityStatVM(string caption, int value)
    {
        Caption = caption ?? "";
        Value = value.ToString();
    }
}
