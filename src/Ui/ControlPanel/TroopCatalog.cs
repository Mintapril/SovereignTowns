using System;
using System.Collections.Generic;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 进程内枚举可招募兵种 — 给控制面板「兵员模板」/「卫队编制」消费。
/// 之前是 WebConfig/TroopDumper（带 dump 到 troops.json 的副作用，给已删除的 WebUI 前端用）；
/// WebUI 整体移除后只保留进程内 Collect()。
/// </summary>
public static class TroopCatalog
{
    /// <summary>进程内取可招募兵种列表，结果按 (culture, tier, id) 排序。失败仅记录日志返回空表。</summary>
    public static List<TroopEntry> Collect()
    {
        try { return CollectTroops(); }
        catch (Exception ex)
        {
            Logger.Error("TroopCatalog.Collect failed", ex);
            return new List<TroopEntry>();
        }
    }

    private static List<TroopEntry> CollectTroops()
    {
        var list = new List<TroopEntry>();

        HashSet<CharacterObject> recruitable;
        try
        {
            recruitable = CollectRecruitableTroops();
        }
        catch (Exception ex)
        {
            Logger.Error("TroopCatalog: failed to compute recruitable troop whitelist", ex);
            return list;
        }

        if (recruitable.Count == 0)
        {
            Logger.Warn("TroopCatalog: recruitable whitelist is empty — Cultures may not yet be registered.");
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
                if (role == GenericTroopRole.Unknown) continue;

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
                Logger.Error("TroopCatalog: failed to inspect a character, skipping", ex);
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
    /// BFS upgrade tree from each Culture's BasicTroop + EliteBasicTroop. 结果集合是 vanilla
    /// 村庄/城镇/城堡 notable VolunteerTypes 实际能给出的兵种全集（不含 tavern 雇佣兵）。
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
                Logger.Error($"TroopCatalog: culture '{culture.StringId}' seed enumeration failed", ex);
            }
        }

        while (queue.Count > 0)
        {
            var co = queue.Dequeue();
            if (co is null) continue;
            if (!result.Add(co)) continue;

            CharacterObject[]? targets = null;
            try { targets = co.UpgradeTargets; } catch { }
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

    public sealed class TroopEntry
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string culture { get; set; } = "";
        public string cultureName { get; set; } = "";
        public int tier { get; set; }
        public string type { get; set; } = "";
        public bool isMounted { get; set; }
        public bool isRanged { get; set; }
        public bool isNoble { get; set; }
    }
}
