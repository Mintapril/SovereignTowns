using SovereignTowns.Common;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 控制面板双语助手。语言探测已合并到 <see cref="LanguageProbe"/>（commit 2026-05-23 审计去重）。
/// </summary>
internal static class ControlPanelLoc
{
    public static bool IsChinese => LanguageProbe.IsChinese;
    public static string Tr(string zh, string en) => LanguageProbe.Tr(zh, en);
}
