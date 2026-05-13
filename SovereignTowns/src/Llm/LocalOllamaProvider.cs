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
/// LLM provider that talks to a local Ollama daemon over HTTP.
/// <para>
/// Calls <c>POST &lt;endpoint&gt;/api/chat</c> with <c>stream=false</c> and
/// extracts <c>message.content</c> from the response. All failure modes
/// (network errors, timeouts, malformed responses) are surfaced as a
/// <see cref="LLMResponse"/> with <c>Success = false</c> - this method
/// never throws across the <see cref="ILLMProvider"/> boundary.
/// </para>
/// <para>
/// Per-request timeout is enforced via a linked <see cref="CancellationTokenSource"/>
/// so the caller's <see cref="CancellationToken"/> is honoured in addition
/// to <see cref="LLMConfig.TimeoutSeconds"/>. The <see cref="HttpClient"/>
/// instance is static and shared to avoid socket exhaustion.
/// </para>
/// </summary>
public sealed class LocalOllamaProvider : ILLMProvider
{
    private readonly LLMConfig _config;

    // Shared HttpClient (avoid socket exhaustion). InfiniteTimeSpan because
    // we manage timeout per-request via CancellationTokenSource.
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    // Regex matches the first "content":"..." JSON string value, with escape support.
    // (?:[^"\\]|\\.)*  -> any non-quote/non-backslash char, OR a backslash + any char.
    private static readonly Regex _contentRx = new Regex(
        "\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public LocalOllamaProvider(LLMConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public string Name => "ollama";

    /// <inheritdoc/>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.Endpoint) &&
        !string.IsNullOrWhiteSpace(_config.Model);

    /// <inheritdoc/>
    public async Task<LLMResponse> CompleteAsync(LLMPrompt prompt, CancellationToken ct)
    {
        if (prompt is null)
        {
            return LLMResponse.Fail("prompt is null", Name);
        }
        if (!IsConfigured)
        {
            return LLMResponse.Fail("ollama provider not configured (endpoint/model missing)", Name);
        }

        var sw = Stopwatch.StartNew();

        // Build URL: <endpoint>/api/chat (trim trailing slash to avoid //api/chat)
        var endpoint = _config.Endpoint.TrimEnd('/');
        var url = endpoint + "/api/chat";

        // Hand-rolled JSON body (System.Text.Json not available on netstandard2.1 baseline here).
        var bodyJson = BuildRequestJson(prompt);

        // Link caller ct with our per-request timeout.
        var timeoutSeconds = _config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 10;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, linkedCts.Token)
                .ConfigureAwait(false);

            var respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                sw.Stop();
                var snippet = respBody is null ? "" : (respBody.Length > 256 ? respBody.Substring(0, 256) : respBody);
                Logger.Warn($"[LLM/ollama] http {(int)resp.StatusCode} from {url}: {snippet}");
                return LLMResponse.Fail($"ollama http {(int)resp.StatusCode}: {snippet}", Name);
            }

            var content = ExtractMessageContent(respBody);
            sw.Stop();

            if (content is null)
            {
                var snippet = respBody is null ? "" : (respBody.Length > 256 ? respBody.Substring(0, 256) : respBody);
                Logger.Warn($"[LLM/ollama] failed to parse message.content from response: {snippet}");
                return LLMResponse.Fail("ollama: failed to parse message.content", Name);
            }

            Logger.Debug($"[LLM/ollama] ok ctx={prompt.Context} model={_config.Model} latency={sw.ElapsedMilliseconds}ms chars={content.Length}");
            return LLMResponse.Ok(content, (int)sw.ElapsedMilliseconds, Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            return LLMResponse.Fail("ollama: cancelled by caller", Name);
        }
        catch (OperationCanceledException)
        {
            // Linked CTS fired due to timeout (also covers TaskCanceledException,
            // which derives from OperationCanceledException).
            sw.Stop();
            return LLMResponse.Fail($"ollama: timeout after {timeoutSeconds}s", Name);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            Logger.Warn($"[LLM/ollama] http error: {ex.Message}");
            return LLMResponse.Fail($"ollama http error: {ex.Message}", Name);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Error($"[LLM/ollama] unexpected error", ex);
            return LLMResponse.Fail($"ollama unexpected: {ex.GetType().Name}: {ex.Message}", Name);
        }
    }

    /// <summary>
    /// Builds the JSON request body for <c>POST /api/chat</c>:
    /// <code>
    /// { "model": "...", "messages": [...], "stream": false,
    ///   "options": { "temperature": ..., "num_predict": ... } }
    /// </code>
    /// Hand-rolled to avoid a System.Text.Json dependency.
    /// </summary>
    private string BuildRequestJson(LLMPrompt prompt)
    {
        var sb = new StringBuilder(256 + prompt.SystemPrompt.Length + prompt.UserPrompt.Length);
        sb.Append('{');
        sb.Append("\"model\":\"").Append(Escape(_config.Model)).Append("\",");
        sb.Append("\"messages\":[");
        sb.Append("{\"role\":\"system\",\"content\":\"").Append(Escape(prompt.SystemPrompt)).Append("\"},");
        sb.Append("{\"role\":\"user\",\"content\":\"").Append(Escape(prompt.UserPrompt)).Append("\"}");
        sb.Append("],");
        sb.Append("\"stream\":false,");
        sb.Append("\"options\":{");
        sb.Append("\"temperature\":").Append(FormatFloat(_config.Temperature)).Append(',');
        sb.Append("\"num_predict\":").Append(_config.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Escapes a string for embedding inside a JSON string literal.</summary>
    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s!.Length + 8);
        foreach (var c in s)
        {
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

    /// <summary>Culture-invariant float formatting (avoids "0,2" on de-DE).</summary>
    private static string FormatFloat(float f) =>
        f.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Extracts the assistant's content from an Ollama <c>/api/chat</c> response.
    /// Strategy: locate the <c>"message"</c> object then take the first
    /// <c>"content":"..."</c> inside it, with full JSON-string escape handling.
    /// Falls back to the first <c>"content":"..."</c> in the document if the
    /// <c>"message"</c> anchor isn't found.
    /// </summary>
    private static string? ExtractMessageContent(string? responseJson)
    {
        if (string.IsNullOrEmpty(responseJson)) return null;

        // Prefer the content inside the "message" object so we don't accidentally
        // pick up "content" from elsewhere in the payload.
        var msgIdx = responseJson!.IndexOf("\"message\"", StringComparison.Ordinal);
        var searchStart = msgIdx >= 0 ? msgIdx : 0;

        var m = _contentRx.Match(responseJson, searchStart);
        if (!m.Success && msgIdx >= 0)
        {
            // Anchor present but no match after it — try from start as fallback.
            m = _contentRx.Match(responseJson);
        }
        if (!m.Success) return null;

        return UnescapeJsonString(m.Groups[1].Value);
    }

    /// <summary>
    /// Unescapes a captured JSON string body (the text between the quotes).
    /// Handles <c>\" \\ \/ \b \f \n \r \t</c> and <c>\uXXXX</c>.
    /// </summary>
    private static string UnescapeJsonString(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }
            var n = s[++i];
            switch (n)
            {
                case '"':  sb.Append('"');  break;
                case '\\': sb.Append('\\'); break;
                case '/':  sb.Append('/');  break;
                case 'b':  sb.Append('\b'); break;
                case 'f':  sb.Append('\f'); break;
                case 'n':  sb.Append('\n'); break;
                case 'r':  sb.Append('\r'); break;
                case 't':  sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < s.Length)
                    {
                        var hex = s.Substring(i + 1, 4);
                        if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append('\\').Append(n);
                        }
                    }
                    else
                    {
                        sb.Append('\\').Append(n);
                    }
                    break;
                default:
                    sb.Append('\\').Append(n);
                    break;
            }
        }
        return sb.ToString();
    }
}
