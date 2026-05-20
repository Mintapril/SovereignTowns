using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Templates;
using TaleWorlds.Localization;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// 把请求路由到具体业务处理。每个处理函数自带 try/catch，
/// 异常一律转 500 + JSON body，绝不向 HttpListener 主循环传播。
/// </summary>
internal static class WebConfigEndpoints
{
    private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Culture = System.Globalization.CultureInfo.InvariantCulture,
    };

    /// <summary>GET /api/config → 当前 GlobalConfig。</summary>
    public static void GetConfig(HttpListenerContext ctx)
    {
        try
        {
            var cfg = ConfigurationManager.Current;
            WebConfigServer.WriteJson(ctx, 200, cfg);
        }
        catch (Exception ex)
        {
            Logger.Error("GetConfig threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>PUT /api/config body 大小上限。超过则 413（防 DoS / 误传大文件灌爆 server 进程内存）。</summary>
    private const long MaxConfigPayloadBytes = 1L * 1024 * 1024; // 1 MiB

    /// <summary>
    /// PUT /api/config body=完整 GlobalConfig JSON。
    /// 反序列化 → 替换 in-memory → ValidateCurrent → Save 写盘。
    /// 验证失败回 422，不污染磁盘 last-known-good。
    /// P1-5：先做 Content-Type 与 payload 大小校验，避免误传大文件或非 JSON 内容时
    /// 一路读到 StreamReader.ReadToEnd 才报错（更友好且节省内存）。
    /// </summary>
    public static void PutConfig(HttpListenerContext ctx)
    {
        try
        {
            // P1-5 修复 B：Content-Type 必须是 application/json（允许带 charset 等参数，因此用 StartsWith）。
            // 浏览器 fetch 默认 form-encoded → 早期拒绝避免 Newtonsoft 拿到完全不像 JSON 的 body 才 throw。
            string contentType = ctx.Request.ContentType ?? "";
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"PUT /api/config rejected: Content-Type='{contentType}' (expected application/json)");
                WebConfigServer.WriteError(ctx, 415, "unsupported_media_type",
                    $"Content-Type must be application/json (got '{contentType}')");
                return;
            }

            // P1-5 修复 A：payload 上限。
            // ContentLength64 == -1 表示 chunked encoding（无声明长度）—— 拒绝，要求客户端提供 Content-Length。
            // WebUI 始终发送 Content-Length，此处 411 只影响脚本/curl 等非标准调用。
            long declared = ctx.Request.ContentLength64;
            if (declared < 0)
            {
                Logger.Warn("PUT /api/config: chunked encoding without Content-Length rejected");
                WebConfigServer.WriteError(ctx, 411, "length_required", "Content-Length header required");
                return;
            }
            if (declared > MaxConfigPayloadBytes)
            {
                Logger.Warn($"PUT /api/config rejected: declared Content-Length={declared} 超过 {MaxConfigPayloadBytes}");
                WebConfigServer.WriteError(ctx, 413, "payload_too_large",
                    $"Request body {declared} bytes exceeds limit {MaxConfigPayloadBytes} bytes");
                return;
            }

            string body = ReadBody(ctx.Request);
            if (string.IsNullOrWhiteSpace(body))
            {
                WebConfigServer.WriteError(ctx, 400, "empty_body", "PUT /api/config requires a JSON body");
                return;
            }

            GlobalConfig? parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<GlobalConfig>(body);
            }
            catch (JsonException jex)
            {
                WebConfigServer.WriteError(ctx, 400, "bad_json", jex.Message);
                return;
            }

            if (parsed is null)
            {
                WebConfigServer.WriteError(ctx, 400, "null_config", "Body deserialized to null");
                return;
            }

            // 兜底空字段，与 ConfigurationManager.TryLoadFromDisk 一致。
            parsed.GlobalDefaults ??= TownGarrisonRule.CreateDefault();
            parsed.BranchDefaults ??= BranchRule.CreateDefault();
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.ClanPatrol ??= new ClanPatrolConfig();
            parsed.ClanRecruiter ??= new ClanRecruiterConfig();
            parsed.Thresholds ??= new PartyThresholds();
            parsed.LastModified ??= "";

            // 原子 validate + replace + write。失败时 in-memory 不动。
            if (!ConfigurationManager.ReplaceAndSave(parsed, out var reason, out bool configChanged))
            {
                int status = reason.StartsWith("ReplaceAndSave threw", StringComparison.Ordinal) ? 500 : 422;
                WebConfigServer.WriteError(ctx, status, "validation_failed", reason);
                return;
            }

            // P0-1 修复：仅在内容真正变化时广播 OnConfigChanged。
            // UI-only 字段（ShowDailySummary 等）不会触发 in-flight recruiter 重置。
            // HttpListener handlers run off the campaign thread. Queue any campaign-object
            // mutations + config-changed event invocation; both replay on the next Drain
            // from a campaign tick handler.
            if (configChanged)
            {
                WebConfigGameThreadSync.RequestConfigChanged(null, "PUT /api/config");
                Logger.Info("PUT /api/config accepted and persisted (content changed → broadcasting OnConfigChanged)");
            }
            else
            {
                Logger.Info("PUT /api/config accepted and persisted (content identical → no OnConfigChanged broadcast)");
            }

            WebConfigServer.WriteJson(ctx, 200, new { ok = true });
        }
        catch (Exception ex)
        {
            Logger.Error("PutConfig threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>POST /api/reload → ConfigurationManager.TryReload() 手动重读磁盘。
    /// 失败时回 422 + reason，避免 UI 误以为 reload 成功而 OnConfigChanged 也不再触发。
    /// P0-1 修复：仅在磁盘内容与 in-memory 实际不同时才广播 OnConfigChanged，
    /// 避免打开/刷新 WebUI 无修改 reload 就重置所有 in-flight recruiter。</summary>
    public static void PostReload(HttpListenerContext ctx)
    {
        try
        {
            if (!ConfigurationManager.TryReload(out var reloadReason, out bool configChanged))
            {
                WebConfigServer.WriteError(ctx, 422, "reload_failed", reloadReason);
                return;
            }

            // P0-1 修复：只有磁盘内容真正与 in-memory 不同时才广播，让 in-flight party 按新规则重算。
            if (configChanged)
            {
                WebConfigGameThreadSync.RequestConfigChanged(null, "POST /api/reload");
                Logger.Info("POST /api/reload accepted (content changed → broadcasting OnConfigChanged)");
            }
            else
            {
                Logger.Info("POST /api/reload accepted (content identical → no OnConfigChanged broadcast)");
            }

            WebConfigServer.WriteJson(ctx, 200, new { ok = true });
        }
        catch (Exception ex)
        {
            Logger.Error("PostReload threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>GET /api/troops → troops.json 文件原样返回（前端 picker 数据源）。</summary>
    public static void GetTroops(HttpListenerContext ctx)
    {
        try
        {
            string path = TroopDumper.GetTroopsJsonPath();
            if (!File.Exists(path))
            {
                WebConfigServer.WriteError(ctx, 404, "troops_not_dumped",
                    "troops.json not yet generated; load a campaign save first");
                return;
            }

            string text = File.ReadAllText(path);
            WebConfigServer.WriteRawJson(ctx, 200, text);
        }
        catch (Exception ex)
        {
            Logger.Error("GetTroops threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>
    /// GET /api/settlements → 玩家拥有的城/堡列表。
    /// P0-6：HTTP handler 跑在 ThreadPool 线程，不能直接读 vanilla Town/Settlement/Clan。
    /// 改读 <see cref="SettlementsSnapshot"/> 缓存（由 CampaignBehavior 在主线程刷新）。
    /// </summary>
    public static void GetSettlements(HttpListenerContext ctx)
    {
        try
        {
            var snapshot = SettlementsSnapshot.Read();
            WebConfigServer.WriteJson(ctx, 200, new { settlements = snapshot });
        }
        catch (Exception ex)
        {
            Logger.Error("GetSettlements threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>
    /// GET /api/settlements/{stringId}/activities → 该 settlement 最近 N 条结构化活动（B17.4 A2）。
    /// 数据源:<see cref="SovereignTowns.Audit.PerSettlementActivityRing"/>。
    /// HTTP 线程安全 — ring 内部 lock；不读取 vanilla 对象。
    /// </summary>
    public static void GetSettlementActivities(HttpListenerContext ctx, string settlementStringId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settlementStringId))
            {
                WebConfigServer.WriteError(ctx, 400, "missing_settlement_id", "URL must include /api/settlements/{stringId}/activities");
                return;
            }
            var entries = SovereignTowns.Audit.PerSettlementActivityRing.Read(settlementStringId);
            WebConfigServer.WriteJson(ctx, 200, new { settlement = settlementStringId, count = entries.Count, activities = entries });
        }
        catch (Exception ex)
        {
            Logger.Error("GetSettlementActivities threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>GET /api/status → 简单运行时统计。</summary>
    public static void GetStatus(HttpListenerContext ctx)
    {
        try
        {
            var cfg = ConfigurationManager.Current;
            var status = new
            {
                ok = true,
                configVersion = cfg.ConfigVersion,
                lastModified = cfg.LastModified,
                features = cfg.EnabledFeatures,
                branchTargetPower = cfg.BranchDefaults?.TargetPower ?? 0,
                exactTroopTemplateCount = cfg.GlobalDefaults?.ExactTroopTemplate?.Count ?? 0,
                uiLang = GetUiLang(),
            };
            WebConfigServer.WriteJson(ctx, 200, status);
        }
        catch (Exception ex)
        {
            Logger.Error("GetStatus threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>GET /api/finance → mod 支出报告（今日/本周/全部 + 近期流水）。</summary>
    public static void GetFinance(HttpListenerContext ctx)
    {
        try
        {
            var report = ModExpenseLedger.BuildReport();
            WebConfigServer.WriteJson(ctx, 200, report);
        }
        catch (Exception ex)
        {
            Logger.Error("GetFinance threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>
    /// GET /api/activity → 玩家可读的运行动态(今日概况 + 近期动态流)。
    /// 数据源:<see cref="SovereignTowns.Audit.DailyActivityCounters"/> +
    /// <see cref="SovereignTowns.Audit.ActivityFeed"/> —— 均为线程安全 in-memory,HTTP 线程可直读。
    /// </summary>
    public static void GetActivity(HttpListenerContext ctx)
    {
        try
        {
            var (recruited, transferred, patrols, sallies, prisoners) =
                SovereignTowns.Audit.DailyActivityCounters.Snapshot();

            var feed = new List<object>();
            foreach (var e in SovereignTowns.Audit.ActivityFeed.Read())
                feed.Add(new { when = e.When, text = e.Text, tone = e.Tone });

            var payload = new
            {
                today = new { recruited, transferred, patrols, sallies, prisoners },
                feed,
            };
            WebConfigServer.WriteJson(ctx, 200, payload);
        }
        catch (Exception ex)
        {
            Logger.Error("GetActivity threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    // ---------------- helpers ----------------

    private static string? _uiLang;

    /// <summary>
    /// 当前游戏 UI 语言探针,供 WebUI 决定渲染语言。借 mod 自己的 {=key} 本地化系统判定:
    /// 游戏语言为简体中文时 CNs 表把 ST_WebUiLang 解析为 "zh",其他语言回退到键内默认值 "en"。
    /// 不依赖任何 vanilla 语言 API。语言一局内不变,解析一次后缓存。
    /// </summary>
    private static string GetUiLang()
    {
        if (_uiLang != null) return _uiLang;
        try { _uiLang = new TextObject("{=ST_WebUiLang}en").ToString(); }
        catch { _uiLang = "en"; }
        return _uiLang;
    }

    private static string ReadBody(HttpListenerRequest req)
    {
        if (!req.HasEntityBody) return "";
        using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return sr.ReadToEnd();
    }

    internal static string JsonSerialize(object obj)
        => JsonConvert.SerializeObject(obj, _json);
}
