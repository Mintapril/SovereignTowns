using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using SovereignTowns.Configuration;
using SovereignTowns.Templates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
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

    /// <summary>
    /// PUT /api/config body=完整 GlobalConfig JSON。
    /// 反序列化 → 替换 in-memory → ValidateCurrent → Save 写盘。
    /// 验证失败回 422，不污染磁盘 last-known-good。
    /// </summary>
    public static void PutConfig(HttpListenerContext ctx)
    {
        try
        {
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
            parsed.PerSettlementOverrides ??= new Dictionary<string, TownGarrisonRule>();
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.LastModified ??= "";

            // 原子 validate + replace + write。失败时 in-memory 不动。
            if (!ConfigurationManager.ReplaceAndSave(parsed, out var reason))
            {
                int status = reason.StartsWith("ReplaceAndSave threw", StringComparison.Ordinal) ? 500 : 422;
                WebConfigServer.WriteError(ctx, status, "validation_failed", reason);
                return;
            }

            WebConfigServer.WriteJson(ctx, 200, new { ok = true });
            Logger.Info("PUT /api/config accepted and persisted");
        }
        catch (Exception ex)
        {
            Logger.Error("PutConfig threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>POST /api/reload → ConfigurationManager.Reload() 手动重读磁盘。</summary>
    public static void PostReload(HttpListenerContext ctx)
    {
        try
        {
            ConfigurationManager.Reload();
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

    /// <summary>GET /api/settlements → 玩家拥有的城/堡列表。</summary>
    public static void GetSettlements(HttpListenerContext ctx)
    {
        try
        {
            var list = new List<object>();
            var playerClan = Clan.PlayerClan;
            if (playerClan != null)
            {
                foreach (var t in Town.AllTowns)
                {
                    try
                    {
                        if (t?.Settlement is null) continue;
                        if (t.OwnerClan != playerClan) continue;
                        var s = t.Settlement;
                        list.Add(new
                        {
                            stringId = s.StringId ?? "",
                            name = s.Name?.ToString() ?? s.StringId,
                            isCastle = s.IsCastle,
                        });
                    }
                    catch (Exception inner)
                    {
                        Logger.Error("GetSettlements: skipping one entry on error", inner);
                    }
                }
            }
            WebConfigServer.WriteJson(ctx, 200, new { settlements = list });
        }
        catch (Exception ex)
        {
            Logger.Error("GetSettlements threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    /// <summary>GET /api/training-templates → 内置预设规则列表（前端「应用预设」用）。</summary>
    public static void GetTrainingTemplates(HttpListenerContext ctx)
    {
        try
        {
            var templates = TemplateManager.GetAllTemplates();
            WebConfigServer.WriteJson(ctx, 200, new { templates });
        }
        catch (Exception ex)
        {
            Logger.Error("GetTrainingTemplates threw", ex);
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
                perSettlementOverrideCount = cfg.PerSettlementOverrides.Count,
                exactTroopTemplateCount = cfg.GlobalDefaults?.ExactTroopTemplate?.Count ?? 0,
            };
            WebConfigServer.WriteJson(ctx, 200, status);
        }
        catch (Exception ex)
        {
            Logger.Error("GetStatus threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }

    // ---------------- helpers ----------------

    private static string ReadBody(HttpListenerRequest req)
    {
        if (!req.HasEntityBody) return "";
        using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return sr.ReadToEnd();
    }

    internal static string JsonSerialize(object obj)
        => JsonConvert.SerializeObject(obj, _json);
}
