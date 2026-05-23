using TaleWorlds.Localization;

namespace SovereignTowns.Common;

/// <summary>
/// 进程级语言探针：以 {=ST_WebUiLang} 的解析结果判定当前游戏语言是否为中文，
/// 首次访问缓存一次。
///
/// 之前 ControlPanelLoc / ConfigurationManager / (已删除的 TreasuryUserActions) 各有一份完全等价的
/// IsChinese + Tr(zh,en) 副本（commit 注释明确"刻意 YAGNI 不抽公共助手"）。审计 2026-05-23
/// 合并为单一来源，消费方改为转发到这里。
///
/// 语义不变：复用 WebConfigEndpoints.GetUiLang() 的同一判定（CNs 表把 ST_WebUiLang 解析为
/// "zh"，其他语言回退到键内默认值 "en"）。不依赖任何 vanilla 语言 API。
/// </summary>
internal static class LanguageProbe
{
    private static bool? _isZh;

    /// <summary>当前游戏语言是否为中文。首次访问探测并缓存。</summary>
    public static bool IsChinese
    {
        get
        {
            if (_isZh.HasValue) return _isZh.Value;
            try { _isZh = new TextObject("{=ST_WebUiLang}en").ToString() == "zh"; }
            catch { _isZh = false; }
            return _isZh.Value;
        }
    }

    /// <summary>按游戏语言返回 zh 或 en。</summary>
    public static string Tr(string zh, string en) => IsChinese ? zh : en;
}
