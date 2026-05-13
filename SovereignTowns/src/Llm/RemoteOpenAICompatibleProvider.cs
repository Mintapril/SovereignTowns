using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Llm;

/// <summary>
/// LLM provider targeting any remote endpoint that implements the OpenAI
/// Chat Completions REST contract (e.g. OpenAI, Anthropic OpenAI-compatible
/// gateway, OpenRouter, vLLM, llama.cpp server with --api).
/// <para>
/// The API key is read at call-time from the environment variable whose
/// name is given by <see cref="LLMConfig.ApiKeyEnvVar"/>. The key is never
/// logged, never persisted, and never echoed in error messages.
/// </para>
/// <para>
/// All failure modes (missing key, HTTP error, timeout, cancellation,
/// malformed JSON) are returned as <see cref="LLMResponse"/> with
/// <c>Success = false</c>; this method intentionally never throws across
/// the <see cref="ILLMProvider"/> boundary.
/// </para>
/// </summary>
public sealed class RemoteOpenAICompatibleProvider : ILLMProvider
{
    private readonly LLMConfig _config;

    // Process-wide singleton: HttpClient is designed to be reused. The
    // per-request timeout is enforced via a linked CTS rather than the
    // (process-global) HttpClient.Timeout property.
    private static readonly HttpClient _http = new HttpClient();

    // Matches the first JSON "content":"..." string value, honouring
    // backslash escape sequences inside the quoted body. The OpenAI
    // response shape places choices[0].message.content as the first
    // "content" key, so a first-match scan is sufficient.
    private static readonly Regex _contentRegex =
        new Regex("\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Constructs a provider bound to the given configuration. The
    /// configuration object is captured by reference; callers must not
    /// mutate it for the lifetime of this provider.
    /// </summary>
    public RemoteOpenAICompatibleProvider(LLMConfig config)
    {
        _config = config;
    }

    /// <inheritdoc/>
    public string Name => "openai_compatible";

    /// <inheritdoc/>
    /// <remarks>
    /// Requires endpoint, model, API key env var name, AND the env var
    /// itself to be populated. The first three are static config; the
    /// fourth is queried from the process environment at each call to
    /// this getter so the user can set the key after game launch.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.Endpoint)
        && !string.IsNullOrWhiteSpace(_config.Model)
        && !string.IsNullOrWhiteSpace(_config.ApiKeyEnvVar)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(_config.ApiKeyEnvVar));

    /// <inheritdoc/>
    public async Task<LLMResponse> CompleteAsync(LLMPrompt prompt, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // 1. Pre-flight: env-var resolution. No network I/O if key is absent.
        var envVar = _config.ApiKeyEnvVar ?? "";
        if (string.IsNullOrWhiteSpace(envVar))
        {
            return LLMResponse.Fail("API key env var name not configured", Name);
        }
        var apiKey = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Mention env var name only - never the key value.
            return LLMResponse.Fail("API key env var not set: " + envVar, Name);
        }

        if (string.IsNullOrWhiteSpace(_config.Endpoint))
        {
            return LLMResponse.Fail("Endpoint not configured", Name);
        }
        if (string.IsNullOrWhiteSpace(_config.Model))
        {
            return LLMResponse.Fail("Model not configured", Name);
        }

        // 2. Build URL: normalise endpoint to avoid '//v1/...'.
        var baseEndpoint = _config.Endpoint.TrimEnd('/');
        var url = baseEndpoint + "/v1/chat/completions";

        // 3. Build request body (hand-rolled JSON to keep parity with the
        // rest of this codebase, which has no System.Text.Json dependency
        // available under netstandard2.1 with the current references).
        string body = BuildRequestBody(
            _config.Model,
            prompt.SystemPrompt ?? "",
            prompt.UserPrompt ?? "",
            _config.Temperature,
            _config.MaxTokens);

        // 4. Per-request timeout via linked CTS. We must not touch
        // _http.Timeout because it is process-global on the shared client.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeoutSeconds = _config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 10;
        try
        {
            linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (Exception ex)
        {
            Logger.Error("[LLM][openai_compatible] failed to arm timeout", ex);
            return LLMResponse.Fail("timeout configuration failed", Name);
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            // Authorization header carries the secret. Attach it here
            // and never read it back for logging.
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            using var resp = await _http.SendAsync(
                req,
                HttpCompletionOption.ResponseContentRead,
                linked.Token).ConfigureAwait(false);

            string respBody;
            try
            {
                respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("[LLM][openai_compatible] failed reading response body", ex);
                return LLMResponse.Fail("response read failed", Name);
            }

            if (!resp.IsSuccessStatusCode)
            {
                // Truncate body in error message to avoid log spam.
                // Note: we are echoing only the *response* body. We never
                // log request headers, so the bearer token does not leak.
                var snippet = respBody.Length > 256 ? respBody.Substring(0, 256) : respBody;
                var msg = $"HTTP {(int)resp.StatusCode} {resp.StatusCode}: {snippet}";
                Logger.Warn($"[LLM][openai_compatible] non-success status={(int)resp.StatusCode} bodyLen={respBody.Length}");
                return LLMResponse.Fail(msg, Name);
            }

            var content = ExtractFirstChoiceContent(respBody);
            if (content is null)
            {
                Logger.Warn($"[LLM][openai_compatible] response missing message.content (bodyLen={respBody.Length})");
                return LLMResponse.Fail("response missing message.content", Name);
            }

            sw.Stop();
            return LLMResponse.Ok(content, (int)sw.ElapsedMilliseconds, Name);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            // Distinguish caller cancel (token already requested) from timeout.
            if (ct.IsCancellationRequested)
            {
                return LLMResponse.Fail("cancelled by caller", Name);
            }
            return LLMResponse.Fail($"timeout after {timeoutSeconds}s", Name);
        }
        catch (HttpRequestException ex)
        {
            // ex.Message includes URI but never the Authorization header value.
            Logger.Error("[LLM][openai_compatible] HTTP request failed", ex);
            return LLMResponse.Fail("HTTP request failed: " + ex.Message, Name);
        }
        catch (Exception ex)
        {
            Logger.Error("[LLM][openai_compatible] unexpected error", ex);
            return LLMResponse.Fail("unexpected error: " + ex.GetType().Name, Name);
        }
    }

    /// <summary>
    /// Builds an OpenAI Chat Completions request body. JSON is constructed
    /// by string concatenation with strict escaping of all string fields.
    /// </summary>
    private static string BuildRequestBody(string model, string system, string user, float temperature, int maxTokens)
    {
        var sb = new StringBuilder(256 + system.Length + user.Length);
        sb.Append('{');
        sb.Append("\"model\":\"").Append(EscapeJsonString(model)).Append("\",");
        sb.Append("\"messages\":[");
        sb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJsonString(system)).Append("\"},");
        sb.Append("{\"role\":\"user\",\"content\":\"").Append(EscapeJsonString(user)).Append("\"}");
        sb.Append("],");
        // Use invariant culture so e.g. de-DE locales don't emit '0,2'.
        sb.Append("\"temperature\":").Append(temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"max_tokens\":").Append(maxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a string for safe embedding inside a JSON string literal.
    /// Preserves newlines as <c>\n</c> (the existing codebase's
    /// <c>EscapeJson</c> collapses them to spaces which would corrupt
    /// multi-line prompts).
    /// </summary>
    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the first <c>"content":"..."</c> string value from a JSON
    /// blob. For OpenAI Chat Completion responses the first such match is
    /// <c>choices[0].message.content</c>. Returns <c>null</c> if no match
    /// is found. Unescapes the common JSON escape sequences encountered
    /// in model output.
    /// </summary>
    private static string? ExtractFirstChoiceContent(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var m = _contentRegex.Match(json);
        if (!m.Success) return null;
        var raw = m.Groups[1].Value;
        return UnescapeJsonString(raw);
    }

    /// <summary>
    /// Reverses the standard JSON string escapes. Handles
    /// <c>\\</c>, <c>\"</c>, <c>\/</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>,
    /// <c>\b</c>, <c>\f</c>, and <c>\uXXXX</c>.
    /// </summary>
    private static string UnescapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }
            char next = s[++i];
            switch (next)
            {
                case '\\': sb.Append('\\'); break;
                case '"':  sb.Append('"');  break;
                case '/':  sb.Append('/');  break;
                case 'n':  sb.Append('\n'); break;
                case 'r':  sb.Append('\r'); break;
                case 't':  sb.Append('\t'); break;
                case 'b':  sb.Append('\b'); break;
                case 'f':  sb.Append('\f'); break;
                case 'u':
                    if (i + 4 < s.Length)
                    {
                        var hex = s.Substring(i + 1, 4);
                        if (int.TryParse(hex,
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var codePoint))
                        {
                            sb.Append((char)codePoint);
                            i += 4;
                        }
                        else
                        {
                            sb.Append('\\').Append(next);
                        }
                    }
                    else
                    {
                        sb.Append('\\').Append(next);
                    }
                    break;
                default:
                    sb.Append('\\').Append(next);
                    break;
            }
        }
        return sb.ToString();
    }
}
