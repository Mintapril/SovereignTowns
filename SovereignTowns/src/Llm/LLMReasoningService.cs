using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SovereignTowns.Audit;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Llm;

/// <summary>
/// 一条由 LLM 给出的长程规划建议（结构化）。
/// 仅用于非即时路径（DailyTick / 用户主动咨询）。
/// </summary>
public sealed class LLMAdvice
{
    /// <summary>
    /// 建议动作；必须取自允许枚举：
    /// <c>send_recruiting_party</c> / <c>transfer_troops</c> /
    /// <c>adjust_rule</c> / <c>advise_user</c> / <c>do_nothing</c>。
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>目标聚落（settlement）字符串标识，可为空字符串。</summary>
    public string TargetSettlement { get; set; } = "";

    /// <summary>建议的数量级（兵力 / 调整步长等）。允许范围 [0, 199]。</summary>
    public int MagnitudeSuggested { get; set; }

    /// <summary>简短理由（短文本，审计与 UI 提示用）。</summary>
    public string Reason { get; set; } = "";

    /// <summary>LLM 原始响应内容（审计用，未经裁剪）。</summary>
    public string RawLlmContent { get; set; } = "";

    /// <summary>产生该建议的 provider 名称（来自 <see cref="LLMResponse.ProviderName"/>）。</summary>
    public string ProviderName { get; set; } = "";
}

/// <summary>
/// LLM 长程推理服务（非即时路径专用）。
/// <para>
/// 架构约束：本服务<b>禁止</b>在 HourlyTick / 存档加载 / UI 渲染等热路径中被调用；
/// 仅允许在 DailyTick 长程规划与用户主动触发的咨询入口中使用。调用方需自觉遵守。
/// </para>
/// <para>
/// 失败语义：任何异常 / 超时 / 解析失败 / 校验失败都会被吞掉并返回 <c>null</c>，
/// 调用方应回退到 <see cref="SovereignTowns.Decisions.RuleBasedFallbackDecisionMaker"/>
/// 等确定性启发式。
/// </para>
/// </summary>
public sealed class LLMReasoningService
{
    private readonly ILLMProvider _provider;
    private readonly LLMConfig _config;

    /// <summary>构造服务。注入 provider 与配置。</summary>
    /// <param name="provider">底层 LLM provider 实现（V/W/X 之一）。</param>
    /// <param name="config">运行期配置。</param>
    public LLMReasoningService(ILLMProvider provider, LLMConfig config)
    {
        _provider = provider;
        _config = config;
    }

    /// <summary>
    /// 非即时路径专用：基于"局势摘要"请求 LLM 给出长程规划建议。
    /// 异步、超时由 <see cref="LLMConfig.TimeoutSeconds"/> 控制（下限 3 秒）。
    /// 失败一律返回 <c>null</c>；调用方应回退到 <see cref="SovereignTowns.Decisions.RuleBasedFallbackDecisionMaker"/>。
    /// <b>不能被 HourlyTick 路径调用</b> —— 调用者自觉遵守。
    /// </summary>
    /// <param name="contextLabel">审计与日志用的上下文标签（例如 "garrison_planning"）。</param>
    /// <param name="situationSummary">局势摘要文本（短至中长度）。</param>
    /// <param name="ct">外部取消令牌。</param>
    /// <returns>解析并通过校验的 <see cref="LLMAdvice"/>；失败时为 <c>null</c>。</returns>
    public async Task<LLMAdvice?> SuggestForLongTermPlanningAsync(
        string contextLabel,
        string situationSummary,
        CancellationToken ct = default)
    {
        try
        {
            if (!_config.EnableForLongTermPlanning)
            {
                return null;
            }
            if (!_provider.IsConfigured)
            {
                Logger.Info("LLMReasoning: provider 未配置，跳过");
                return null;
            }

            var systemPrompt = @"You are an AI strategist for a Mount & Blade II garrison-management mod.
Output ONLY a JSON object with fields: action, targetSettlement, magnitudeSuggested (int), reason (short string).
Allowed actions: send_recruiting_party | transfer_troops | adjust_rule | advise_user | do_nothing.
Do not include any natural-language explanation outside the JSON.";

            var userPrompt = $"Context: {contextLabel}\nSituation:\n{situationSummary}\n\nRespond with a single JSON object.";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(3, _config.TimeoutSeconds)));

            var resp = await _provider.CompleteAsync(
                new LLMPrompt(systemPrompt, userPrompt, contextLabel),
                cts.Token).ConfigureAwait(false);

            if (!resp.Success)
            {
                Logger.Warn("LLMReasoning: provider 调用失败 — " + (resp.Error ?? "(no error message)"));
                return null;
            }

            var advice = ParseAdvice(resp.Content);
            if (advice == null)
            {
                Logger.Warn("LLMReasoning: 解析 JSON 失败");
                return null;
            }
            advice.RawLlmContent = resp.Content ?? "";
            advice.ProviderName = resp.ProviderName ?? "";

            var errors = LLMDecisionValidator.Validate(advice);
            var inputSummary = situationSummary == null
                ? ""
                : (situationSummary.Length > 200 ? situationSummary.Substring(0, 200) + "..." : situationSummary);

            if (errors.Count > 0)
            {
                var rejectReason = string.Join("; ", errors);
                Logger.Warn("LLMReasoning: 决策不通过校验 — " + rejectReason);
                DecisionAuditLogger.Log(new AuditEntry
                {
                    DecisionType = "LLMSuggestion",
                    Source = DecisionSource.Llm,
                    InputSummary = inputSummary,
                    DecisionJson = $"{{\"action\":\"{EscapeJson(advice.Action)}\",\"target\":\"{EscapeJson(advice.TargetSettlement)}\",\"mag\":{advice.MagnitudeSuggested}}}",
                    Accepted = false,
                    RejectionReason = rejectReason
                });
                return null;
            }

            DecisionAuditLogger.Log(new AuditEntry
            {
                DecisionType = "LLMSuggestion",
                Source = DecisionSource.Llm,
                InputSummary = inputSummary,
                DecisionJson = $"{{\"action\":\"{EscapeJson(advice.Action)}\",\"target\":\"{EscapeJson(advice.TargetSettlement)}\",\"mag\":{advice.MagnitudeSuggested},\"latencyMs\":{resp.LatencyMs}}}",
                Accepted = true
            });
            Logger.Info($"LLMReasoning: advice action={advice.Action} target={advice.TargetSettlement} mag={advice.MagnitudeSuggested} latency={resp.LatencyMs}ms");
            return advice;
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("LLMReasoning: 超时取消");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("LLMReasoning: 异常", ex);
            return null;
        }
    }

    /// <summary>
    /// 从 LLM 响应文本中提取首个 JSON 对象块（从首个 '{' 到末尾的 '}'），
    /// 然后用正则提取四个目标字段。容忍前后散文。
    /// </summary>
    private static LLMAdvice? ParseAdvice(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        var i = content!.IndexOf('{');
        var j = content.LastIndexOf('}');
        if (i < 0 || j <= i) return null;
        var jsonBlock = content.Substring(i, j - i + 1);

        string action = ExtractField(jsonBlock, "action") ?? "";
        string target = ExtractField(jsonBlock, "targetSettlement") ?? "";
        string magStr = ExtractField(jsonBlock, "magnitudeSuggested") ?? "0";
        string reason = ExtractField(jsonBlock, "reason") ?? "";

        if (!int.TryParse(magStr, out var mag)) mag = 0;
        return new LLMAdvice
        {
            Action = action,
            TargetSettlement = target,
            MagnitudeSuggested = mag,
            Reason = reason
        };
    }

    /// <summary>
    /// 在 JSON 子串中匹配 <c>"key" : "value"</c> 或 <c>"key" : number</c>。
    /// 字符串值支持反斜杠转义（<c>\"</c>、<c>\\</c>）。
    /// </summary>
    private static string? ExtractField(string json, string key)
    {
        var rx = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*(\"((?:[^\"\\\\]|\\\\.)*)\"|(-?\\d+))");
        var m = rx.Match(json);
        if (!m.Success) return null;
        if (m.Groups[2].Success)
        {
            return m.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        return m.Groups[3].Value;
    }

    /// <summary>最小 JSON 字符串转义（用于审计输出的 DecisionJson 字段）。</summary>
    private static string EscapeJson(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }
}

/// <summary>
/// 静态校验器：在 LLM 输出被采纳前，确保其满足本 Mod 的安全规则。
/// </summary>
public static class LLMDecisionValidator
{
    private static readonly HashSet<string> AllowedActions = new HashSet<string>
    {
        "send_recruiting_party",
        "transfer_troops",
        "adjust_rule",
        "advise_user",
        "do_nothing"
    };

    /// <summary>
    /// 校验 LLM 输出是否符合本 Mod 的安全规则：
    /// <list type="number">
    ///   <item>JSON 可解析（由调用方保证，<c>null</c> advice 直接被拒）。</item>
    ///   <item><see cref="LLMAdvice.Action"/> 在允许枚举内。</item>
    ///   <item><see cref="LLMAdvice.MagnitudeSuggested"/> ∈ [0, 200)。</item>
    ///   <item><see cref="LLMAdvice.TargetSettlement"/> 仅含字母/数字/<c>_</c>/<c>-</c>/<c>.</c>（可为空）。</item>
    /// </list>
    /// </summary>
    /// <param name="advice">待校验的建议。</param>
    /// <returns>错误列表；为空表示通过。</returns>
    public static IReadOnlyList<string> Validate(LLMAdvice advice)
    {
        var errors = new List<string>();
        if (advice == null)
        {
            errors.Add("null advice");
            return errors;
        }
        if (!AllowedActions.Contains(advice.Action))
        {
            errors.Add("action not in allowed set: " + advice.Action);
        }
        if (advice.MagnitudeSuggested < 0 || advice.MagnitudeSuggested >= 200)
        {
            errors.Add($"magnitude out of range: {advice.MagnitudeSuggested}");
        }
        if (!string.IsNullOrEmpty(advice.TargetSettlement))
        {
            foreach (var c in advice.TargetSettlement)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
                {
                    errors.Add("target settlement contains invalid chars");
                    break;
                }
            }
        }
        return errors;
    }
}
