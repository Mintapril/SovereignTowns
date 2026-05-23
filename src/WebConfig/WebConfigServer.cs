using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaleWorlds.ModuleManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// Mod 内嵌本地 HTTP server，仅绑定 <c>127.0.0.1:port</c>。
/// 玩家从 MCM 或诊断菜单触发 <c>Process.Start(GetBrowserUrl())</c> 后用默认浏览器编辑配置。
///
/// <para><b>Hard invariants（B7 spec §9）</b>：</para>
/// <list type="number">
///   <item>必须绑定 127.0.0.1（不能 +/*，否则触发 UAC / 暴露公网）</item>
///   <item>除 OPTIONS 之外，任何 API 必须校验 X-ST-Token header（除非已通过 ?t= 在 query 里）</item>
///   <item>所有 handler 必须 try/catch，异常仅返回 500 JSON，不影响游戏主线程</item>
/// </list>
/// </summary>
public static class WebConfigServer
{
    private const int DefaultPort = 41763;
    private const int PortFallbackMax = 50;
    private const string LoopbackAddress = "127.0.0.1";

    private static HttpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static int _port = -1;
    private static string _token = "";
    private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Culture = System.Globalization.CultureInfo.InvariantCulture,
    };

    public static bool IsRunning => _listener?.IsListening == true;
    public static int Port => _port;
    public static string Token => _token;

    /// <summary>玩家点「打开网页配置」按钮时调用的目标 URL（含 token）。</summary>
    public static string GetBrowserUrl()
        => IsRunning ? $"http://{LoopbackAddress}:{_port}/?t={_token}" : "";

    /// <summary>
    /// 启动 server。如端口被占自动 +1 重试。失败仅记录日志，不抛。
    /// 安全调用多次（已运行则 no-op）。
    /// </summary>
    public static void Start()
    {
        try
        {
            if (IsRunning)
            {
                Logger.Info("WebConfigServer.Start: already running on port " + _port);
                return;
            }

            _token = WebConfigAuth.GenerateAndPersist();
            if (string.IsNullOrEmpty(_token))
            {
                Logger.Warn("WebConfigServer.Start: token generation failed, aborting startup");
                return;
            }

            for (int offset = 0; offset < PortFallbackMax; offset++)
            {
                int candidate = DefaultPort + offset;
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://{LoopbackAddress}:{candidate}/");
                    listener.Start();
                    _listener = listener;
                    _port = candidate;
                    break;
                }
                catch (HttpListenerException hex)
                {
                    Logger.Info($"WebConfigServer: port {candidate} unavailable ({hex.ErrorCode}), trying next");
                    continue;
                }
                catch (Exception ex)
                {
                    Logger.Error($"WebConfigServer: unexpected error binding port {candidate}", ex);
                    continue;
                }
            }

            if (_listener is null)
            {
                Logger.Error($"WebConfigServer: failed to bind any port in [{DefaultPort}, {DefaultPort + PortFallbackMax})");
                return;
            }

            _cts = new CancellationTokenSource();
            // R2: AcceptLoopAsync 内部已 try/catch，但仍可能因极端环境（端口被回收 / listener 被强杀）抛出 unobserved；
            // 用 ContinueWith.OnlyOnFaulted 显式记录，避免 silently fade。
            var acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            acceptTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Logger.Error("WebConfigServer.AcceptLoopAsync faulted (HTTP server stopped accepting connections)", t.Exception?.GetBaseException());
            }, TaskContinuationOptions.OnlyOnFaulted);

            // 写一份 port.txt 仅用于诊断（前端走 URL 直接拿到 port，不读这文件）。
            try
            {
                string portPath = Path.Combine(TroopDumper.GetBaseDirectory(), "port.txt");
                File.WriteAllText(portPath, _port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Logger.Error("WebConfigServer: failed to write port.txt", ex); }

            Logger.Info($"WebConfigServer: listening on http://{LoopbackAddress}:{_port}/  token=({_token.Length}-char base64-url)");
        }
        catch (Exception ex)
        {
            Logger.Error("WebConfigServer.Start failed (mod functions normally, browser config disabled)", ex);
            SafeStop();
        }
    }

    /// <summary>停止 server。OnSubModuleUnloaded 调用。安全多次调用。</summary>
    public static void Stop()
    {
        try
        {
            SafeStop();
            Logger.Info("WebConfigServer: stopped");
        }
        catch (Exception ex)
        {
            Logger.Error("WebConfigServer.Stop failed (swallowed)", ex);
        }
    }

    private static void SafeStop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts = null;
        _port = -1;
        _token = "";
    }

    private static async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; } // listener stopped
                catch (Exception ex)
                {
                    Logger.Error("WebConfigServer.AcceptLoop GetContext failed", ex);
                    continue;
                }

                // Fire-and-forget per request. Each Handle wraps its own try/catch.
                _ = Task.Run(() => Handle(ctx));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("WebConfigServer.AcceptLoop crashed", ex);
        }
    }

    private static void Handle(HttpListenerContext ctx)
    {
        try
        {
            // 1) Host header check (DNS rebinding mitigation)
            string host = ctx.Request.Headers["Host"] ?? "";
            string expectedHost = $"{LoopbackAddress}:{_port}";
            if (!host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
            {
                WriteError(ctx, 403, "host_mismatch", $"Host must be {expectedHost}");
                return;
            }

            // 2) Origin allow-list (defends against attacker-controlled localhost pages)
            string? origin = ctx.Request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin)
                && !origin!.Equals($"http://{LoopbackAddress}:{_port}", StringComparison.OrdinalIgnoreCase))
            {
                WriteError(ctx, 403, "origin_forbidden", $"Origin '{origin}' not allowed");
                return;
            }

            string method = ctx.Request.HttpMethod ?? "GET";
            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            // 3) CORS preflight — no auth required.
            if (method == "OPTIONS")
            {
                SetCorsHeaders(ctx);
                ctx.Response.StatusCode = 204;
                ctx.Response.OutputStream.Close();
                return;
            }

            SetCorsHeaders(ctx);

            // 4) Token check — 仅 /api/* 端点强制校验 token。
            // 静态 UI 资源（index.html、vendor/*.js、CSS 等）不再 token-gate：浏览器在解析
            // index.html 时发出的子资源请求（<script src> / <link href>）既无法携带 X-ST-Token
            // header，也不会继承文档 URL 的 ?t= query —— 若 gate 静态文件，本地化的 vendor 资源
            // 会全部 401，面板白屏。静态文件不含任何机密（token 由游戏注入启动 URL，从不写入文件）；
            // 真正读写配置的 /api/* 仍然 token-gate。127.0.0.1 绑定 + Host 校验 + Origin 白名单
            // 对所有请求依旧生效。
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                string? token = ctx.Request.Headers["X-ST-Token"];
                if (string.IsNullOrEmpty(token))
                {
                    // query string fallback for the initial document load.
                    token = ctx.Request.QueryString["t"];
                }
                if (!WebConfigAuth.IsAuthorized(token))
                {
                    WriteError(ctx, 401, "unauthorized", "Missing or invalid X-ST-Token");
                    return;
                }
            }

            // 5) Route
            // B17.4 A2: parameterized route `/api/settlements/{id}/activities` — pattern 不适合
            // 全局 switch（switch case 必须是常量），单独前置匹配。仅 GET。
            const string settlementsPrefix = "/api/settlements/";
            const string activitiesSuffix = "/activities";
            if (method == "GET"
                && path.StartsWith(settlementsPrefix, StringComparison.Ordinal)
                && path.EndsWith(activitiesSuffix, StringComparison.Ordinal)
                && path.Length > settlementsPrefix.Length + activitiesSuffix.Length)
            {
                int idStart = settlementsPrefix.Length;
                int idLength = path.Length - settlementsPrefix.Length - activitiesSuffix.Length;
                string settlementId = Uri.UnescapeDataString(path.Substring(idStart, idLength));
                WebConfigEndpoints.GetSettlementActivities(ctx, settlementId);
                return;
            }

            switch (method)
            {
                case "GET" when path == "/api/config":      WebConfigEndpoints.GetConfig(ctx); return;
                case "PUT" when path == "/api/config":      WebConfigEndpoints.PutConfig(ctx); return;
                case "GET" when path == "/api/finance":     WebConfigEndpoints.GetFinance(ctx); return;
                case "GET" when path == "/api/assessment":  WebConfigEndpoints.GetAssessment(ctx); return;
                case "GET" when path == "/api/activity":    WebConfigEndpoints.GetActivity(ctx); return;
                case "GET" when path == "/api/troops":      WebConfigEndpoints.GetTroops(ctx); return;
                case "GET" when path == "/api/settlements": WebConfigEndpoints.GetSettlements(ctx); return;
                case "GET" when path == "/api/status":      WebConfigEndpoints.GetStatus(ctx); return;
                case "GET" when path == "/api/specs":       WebConfigEndpoints.GetSpecs(ctx); return;
                case "POST" when path == "/api/reload":     WebConfigEndpoints.PostReload(ctx); return;
            }

            // 6) Static file fallback (only GET) — served from Modules/SovereignTowns/WebUI/.
            if (method == "GET")
            {
                ServeStatic(ctx, path);
                return;
            }

            WriteError(ctx, 404, "not_found", $"No route for {method} {path}");
        }
        catch (Exception ex)
        {
            Logger.Error("WebConfigServer.Handle threw", ex);
            try { WriteError(ctx, 500, "internal_error", ex.Message); } catch { }
        }
    }

    private static void SetCorsHeaders(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = $"http://{LoopbackAddress}:{_port}";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, PUT, POST, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-ST-Token";
        }
        catch { }
    }

    /// <summary>
    /// 强制浏览器不缓存 mod 控制面板（HTML + JSON API）。理由：
    ///   - 玩家修改 mod 代码 / 重启游戏后磁盘 index.html 已变，但浏览器启发式缓存可能仍发旧版
    ///   - /api/config 等数据响应永远是 in-memory 实时状态，缓存会撒谎
    /// 必须三件套同时设置：Cache-Control no-store + Pragma + Expires=0，覆盖 HTTP/1.0 + 1.1 + 历史代理。
    /// </summary>
    private static void SetNoCacheHeaders(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            ctx.Response.Headers["Pragma"] = "no-cache";
            ctx.Response.Headers["Expires"] = "0";
        }
        catch { }
    }

    private static void ServeStatic(HttpListenerContext ctx, string path)
    {
        try
        {
            if (path == "/" || string.IsNullOrEmpty(path)) path = "/index.html";

            // path traversal mitigation: reject any segment containing ".." and require / prefix
            if (path.Contains("..") || !path.StartsWith("/"))
            {
                WriteError(ctx, 400, "bad_path", "Path traversal not allowed");
                return;
            }

            string moduleDir;
            try { moduleDir = ModuleHelper.GetModuleFullPath("SovereignTowns"); }
            catch { moduleDir = ""; }
            if (string.IsNullOrEmpty(moduleDir))
            {
                WriteError(ctx, 500, "module_path_unresolved", "ModuleHelper returned empty path");
                return;
            }

            string webRoot = Path.GetFullPath(Path.Combine(moduleDir, "WebUI"));
            // Build candidate path with normalized separator and ensure it stays under webRoot.
            string rel = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(webRoot, rel));
            if (!fullPath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
            {
                WriteError(ctx, 403, "outside_webroot", "Static path escaped WebUI root");
                return;
            }

            if (!File.Exists(fullPath))
            {
                WriteError(ctx, 404, "not_found", $"Static file not found: {path}");
                return;
            }

            byte[] data = File.ReadAllBytes(fullPath);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = GuessContentType(fullPath);
            ctx.Response.ContentLength64 = data.Length;
            // 静态文件（index.html / index.css / 其他 WebUI 资源）禁缓存。
            SetNoCacheHeaders(ctx);
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Logger.Error("ServeStatic threw", ex);
            try { WriteError(ctx, 500, "internal_error", ex.Message); } catch { }
        }
    }

    private static string GuessContentType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }

    // ---------------- write helpers (also called by WebConfigEndpoints) ----------------

    internal static void WriteJson(HttpListenerContext ctx, int status, object body)
    {
        try
        {
            string text = JsonConvert.SerializeObject(body, _json);
            WriteRawJson(ctx, status, text);
        }
        catch (Exception ex)
        {
            Logger.Error("WriteJson threw", ex);
        }
    }

    internal static void WriteRawJson(HttpListenerContext ctx, int status, string jsonText)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(jsonText);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            // /api/* 数据响应永远是 in-memory 实时状态，禁缓存避免回传旧值。
            SetNoCacheHeaders(ctx);
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Logger.Error("WriteRawJson threw", ex);
        }
    }

    internal static void WriteError(HttpListenerContext ctx, int status, string code, string message)
    {
        WriteJson(ctx, status, new { ok = false, code, message });
    }
}
