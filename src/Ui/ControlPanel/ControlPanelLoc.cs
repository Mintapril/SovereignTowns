using TaleWorlds.Localization;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 控制面板的内联双语助手。镜像 WebUI 的 tr(zh,en)：不向 std_sovereigntowns_strings.xml
/// 灌 100+ 个 key，面板自带文案按当前游戏语言就地切换。
/// </summary>
internal static class ControlPanelLoc
{
    private static bool? _isZh;

    /// <summary>当前游戏语言是否为中文。首次调用时探测并缓存。</summary>
    public static bool IsChinese
    {
        get
        {
            if (_isZh == null) _isZh = DetectIsChinese();
            return _isZh.Value;
        }
    }

    /// <summary>按游戏语言返回 zh 或 en。</summary>
    public static string Tr(string zh, string en) => IsChinese ? zh : en;

    private static bool DetectIsChinese()
    {
        try
        {
            // 复用 WebConfigEndpoints.GetUiLang() 的同一判定：
            // 游戏语言为简体中文时 CNs 表把 ST_WebUiLang 解析为 "zh"，
            // 其他语言回退到键内默认值 "en"。不依赖任何 vanilla 语言 API。
            // 参见 SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:300-306
            string lang = new TextObject("{=ST_WebUiLang}en").ToString();
            return lang == "zh";
        }
        catch { return false; }
    }
}
