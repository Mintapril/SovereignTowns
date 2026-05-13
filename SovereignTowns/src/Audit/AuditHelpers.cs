namespace SovereignTowns.Audit;

/// <summary>
/// 共享的审计输出辅助。Minimal JSON 字符串转义（用于审计的 decisionJson / inputSummary 字段）。
/// </summary>
public static class AuditHelpers
{
    /// <summary>最小 JSON 字符串转义。null/空 输入返回空字符串。</summary>
    public static string EscapeJson(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Replace("\\", "\\\\")
                 .Replace("\"", "\\\"")
                 .Replace("\r", " ")
                 .Replace("\n", " ");
    }
}
