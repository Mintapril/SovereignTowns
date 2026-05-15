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
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

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

            // 4) Token check (API + static both gated. ?t=xxx satisfies first-load; subsequent fetches use X-ST-Token).
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

            // 5) Route
            switch (method)
            {
                case "GET" when path == "/api/config":      WebConfigEndpoints.GetConfig(ctx); return;
                case "PUT" when path == "/api/config":      WebConfigEndpoints.PutConfig(ctx); return;
                case "GET" when path == "/api/finance":     WebConfigEndpoints.GetFinance(ctx); return;
                case "GET" when path == "/api/troops":      WebConfigEndpoints.GetTroops(ctx); return;
                case "GET" when path == "/api/settlements": WebConfigEndpoints.GetSettlements(ctx); return;
                case "GET" when path == "/api/status":      WebConfigEndpoints.GetStatus(ctx); return;
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
