using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// 每次 OnGameStart 把所有可招募兵种 dump 成 troops.json，写到
/// <c>%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\SovereignTowns\troops.json</c>。
/// 网页前端 fetch /api/troops 时由 WebConfigServer 直接读这份文件回给浏览器，作为 ExactTroopTemplate
/// picker 的数据源。
/// </summary>
/// <remarks>
/// 包含玩家加载的其他 mod 兵种（RBM 等），因为遍历的是 <c>MBObjectManager.GetObjectTypeList&lt;CharacterObject&gt;()</c>。
/// 角色分类委托给已有的 <see cref="GenericTroopMatcher.GetRole"/>（与战斗时的匹配逻辑保持一致）。
/// </remarks>
public static class TroopDumper
{
    private const int SchemaVersion = 1;

    /// <summary>
    /// dump 全部基础兵种到 troops.json。失败仅记录日志，不抛。
    /// </summary>
    public static void Dump()
    {
        try
        {
            var troops = CollectTroops();
            var payload = new
            {
                schemaVersion = SchemaVersion,
                dumpedAt = DateTime.UtcNow.ToString("O"),
                count = troops.Count,
                troops,
            };

            string path = GetTroopsJsonPath();
            EnsureDirectory(path);
            string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            File.WriteAllText(path, json);
            Logger.Info($"TroopDumper: wrote {troops.Count} troops to '{path}'");
        }
        catch (Exception ex)
        {
            Logger.Error("TroopDumper.Dump failed", ex);
        }
    }

    /// <summary>暴露给 WebConfigServer 的路径取值。</summary>
    public static string GetTroopsJsonPath()
    {
        string baseDir = GetBaseDirectory();
        return Path.Combine(baseDir, "troops.json");
    }

    /// <summary>SovereignTowns 在玩家文档目录下的根目录（含 Configs/SovereignTowns/）。</summary>
    public static string GetBaseDirectory()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "SovereignTowns");
    }

    private static void EnsureDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static List<TroopEntry> CollectTroops()
    {
        var list = new List<TroopEntry>();
        IEnumerable<CharacterObject>? all = null;
        try
        {
            all = MBObjectManager.Instance?.GetObjectTypeList<CharacterObject>();
        }
        catch (Exception ex)
        {
            Logger.Error("TroopDumper: failed to enumerate CharacterObject list", ex);
            return list;
        }
        if (all is null) return list;

        foreach (var co in all)
        {
            try
            {
                if (co is null) continue;
                if (co.IsHero) continue;
                if (!co.IsBasicTroop) continue;

                string id = co.StringId ?? "";
                if (string.IsNullOrEmpty(id)) continue;

                var role = GenericTroopMatcher.GetRole(co);
                if (role == GenericTroopRole.Unknown) continue; // 跳过怪物/无效条目

                list.Add(new TroopEntry
                {
                    id = id,
                    name = co.Name?.ToString() ?? id,
                    culture = co.Culture?.StringId ?? "",
                    tier = co.Tier,
                    type = role.ToString().ToLowerInvariant(),
                    isMounted = SafeBool(() => co.IsMounted),
                    isRanged = SafeBool(() => co.IsRanged),
                    isNoble = TroopClassifier.IsNoble(co),
                });
            }
            catch (Exception ex)
            {
                Logger.Error("TroopDumper: failed to inspect a character, skipping", ex);
            }
        }

        list.Sort((a, b) =>
        {
            int c = string.Compare(a.culture, b.culture, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = a.tier.CompareTo(b.tier);
            if (c != 0) return c;
            return string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }

    private sealed class TroopEntry
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string culture { get; set; } = "";
        public int tier { get; set; }
        public string type { get; set; } = "";
        public bool isMounted { get; set; }
        public bool isRanged { get; set; }
        public bool isNoble { get; set; }
    }
}
