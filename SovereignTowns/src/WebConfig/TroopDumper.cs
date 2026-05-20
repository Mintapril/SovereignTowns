using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
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

        // Build the "settlement-recruitable" whitelist by BFS from each culture's
        // BasicTroop + EliteBasicTroop along UpgradeTargets. This is exactly what vanilla
        // village/town notables draw from; tavern mercenaries / story / caravan-guard troops
        // are NOT in these trees so they get excluded automatically.
        // BasicTroop / EliteBasicTroop are the canonical vanilla entry points used by
        // RecruitmentCampaignBehavior to seed each notable's VolunteerTypes.
        HashSet<CharacterObject> recruitable;
        try
        {
            recruitable = CollectRecruitableTroops();
        }
        catch (Exception ex)
        {
            Logger.Error("TroopDumper: failed to compute recruitable troop whitelist", ex);
            return list;
        }

        if (recruitable.Count == 0)
        {
            Logger.Warn("TroopDumper: recruitable whitelist is empty — Cultures may not yet be registered.");
            return list;
        }

        foreach (var co in recruitable)
        {
            try
            {
                if (co is null) continue;
                if (co.IsHero) continue;

                string id = co.StringId ?? "";
                if (string.IsNullOrEmpty(id)) continue;

                var role = GenericTroopMatcher.GetRole(co);
                if (role == GenericTroopRole.Unknown) continue; // 跳过怪物/无效条目

                // co.Name and Culture.Name return the vanilla localization for the active
                // game language (i.e. official Chinese when the player is running 简体中文
                // Bannerlord). Mods that add new troops / cultures supply their own L10N too,
                // so this automatically covers RBM, Calradia Expanded, etc.
                string cultureStringId = co.Culture?.StringId ?? "";
                string cultureName = co.Culture?.Name?.ToString() ?? cultureStringId;

                list.Add(new TroopEntry
                {
                    id = id,
                    name = co.Name?.ToString() ?? id,
                    culture = cultureStringId,
                    cultureName = cultureName,
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

    /// <summary>
    /// BFS the upgrade tree from each Culture's BasicTroop + EliteBasicTroop entry points.
    /// The resulting HashSet is the set of CharacterObjects that can ultimately appear as a
    /// village/town/castle recruit. Tavern mercenaries are absent from any Culture's tree and
    /// thus excluded.
    /// </summary>
    private static HashSet<CharacterObject> CollectRecruitableTroops()
    {
        var result = new HashSet<CharacterObject>();
        var cultures = MBObjectManager.Instance?.GetObjectTypeList<CultureObject>();
        if (cultures is null) return result;

        var queue = new Queue<CharacterObject>();
        foreach (var culture in cultures)
        {
            if (culture is null) continue;
            try
            {
                if (culture.BasicTroop is { } basic) queue.Enqueue(basic);
                if (culture.EliteBasicTroop is { } elite) queue.Enqueue(elite);
            }
            catch (Exception ex)
            {
                Logger.Error($"TroopDumper: culture '{culture.StringId}' seed enumeration failed", ex);
            }
        }

        while (queue.Count > 0)
        {
            var co = queue.Dequeue();
            if (co is null) continue;
            if (!result.Add(co)) continue; // already visited

            CharacterObject[]? targets = null;
            try { targets = co.UpgradeTargets; } catch { /* swallow */ }
            if (targets is null) continue;

            foreach (var next in targets)
            {
                if (next is not null && !result.Contains(next)) queue.Enqueue(next);
            }
        }
        return result;
    }

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }

    /// <summary>进程内取可招募兵种列表（游戏内控制面板用，不落盘）。</summary>
    public static System.Collections.Generic.List<TroopEntry> Collect() => CollectTroops();

    public sealed class TroopEntry
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";          // vanilla-localized character name
        public string culture { get; set; } = "";       // stringId (key for filter/serialization)
        public string cultureName { get; set; } = "";   // vanilla-localized culture display name
        public int tier { get; set; }
        public string type { get; set; } = "";          // role enum lowercase: cavalry/horsearcher/infantry/ranged
        public bool isMounted { get; set; }
        public bool isRanged { get; set; }
        public bool isNoble { get; set; }
    }
}
