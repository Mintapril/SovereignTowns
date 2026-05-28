using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace SovereignTowns.Evaluators;

/// <summary>
/// Culture-agnostic troop template matcher. It intentionally ignores faction/culture and
/// compares a unit only by broad battlefield role plus tier, matching the user-facing
/// "通用匹配" controls. Role classification follows Bannerlord's DefaultFormationClass:
/// horse archers are independent, bow/crossbow troops share Ranged, and throwing troops
/// are folded into their default formation or runtime fallback role.
/// </summary>
public enum GenericTroopRole
{
    Unknown = 0,
    Cavalry = 1,
    HorseArcher = 2,
    Infantry = 3,
    Ranged = 4
}

public readonly struct GenericCompositionSnapshot
{
    public GenericCompositionSnapshot(
        int total,
        int cavalry,
        int horseArcher,
        int infantry,
        int ranged,
        int tier1,
        int tier2,
        int tier3,
        int tier4,
        int tier5,
        int tier6)
    {
        Total = total;
        Cavalry = cavalry;
        HorseArcher = horseArcher;
        Infantry = infantry;
        Ranged = ranged;
        Tier1 = tier1;
        Tier2 = tier2;
        Tier3 = tier3;
        Tier4 = tier4;
        Tier5 = tier5;
        Tier6 = tier6;
    }

    public int Total { get; }
    public int Cavalry { get; }
    public int HorseArcher { get; }
    public int Infantry { get; }
    public int Ranged { get; }
    public int Tier1 { get; }
    public int Tier2 { get; }
    public int Tier3 { get; }
    public int Tier4 { get; }
    public int Tier5 { get; }
    public int Tier6 { get; }

    public int CountOf(GenericTroopRole role) => role switch
    {
        GenericTroopRole.Cavalry => Cavalry,
        GenericTroopRole.HorseArcher => HorseArcher,
        GenericTroopRole.Infantry => Infantry,
        GenericTroopRole.Ranged => Ranged,
        _ => 0
    };

    public int TierCount(int tier) => tier switch
    {
        1 => Tier1,
        2 => Tier2,
        3 => Tier3,
        4 => Tier4,
        5 => Tier5,
        _ => Tier6
    };
}

public static class GenericTroopMatcher
{
    public static GenericTroopRole GetRole(CharacterObject? troop)
    {
        if (troop == null) return GenericTroopRole.Unknown;
        if (EvaluatorCache.RoleCache.TryGetValue(troop, out var cached)) return cached;
        var result = GetRoleCore(troop);
        EvaluatorCache.RoleCache[troop] = result;
        return result;
    }

    private static GenericTroopRole GetRoleCore(CharacterObject troop)
    {
        try
        {
            if (troop.IsHero) return GenericTroopRole.Unknown;

            switch (troop.DefaultFormationClass)
            {
                case FormationClass.HorseArcher:
                    return GenericTroopRole.HorseArcher;

                case FormationClass.Cavalry:
                case FormationClass.LightCavalry:
                case FormationClass.HeavyCavalry:
                    return GenericTroopRole.Cavalry;

                case FormationClass.Ranged:
                    return GenericTroopRole.Ranged;

                case FormationClass.HeavyInfantry:
                case FormationClass.Infantry:
                    return GenericTroopRole.Infantry;

                case FormationClass.Skirmisher:
                case FormationClass.General:
                case FormationClass.Bodyguard:
                case FormationClass.Unset:
                    return ClassifyByRuntimeFlags(troop);
            }

            return ClassifyByRuntimeFlags(troop);
        }
        catch
        {
            return GenericTroopRole.Infantry;
        }
    }

    private static GenericTroopRole ClassifyByRuntimeFlags(CharacterObject troop)
    {
        if (SafeBool(() => troop.IsMounted))
        {
            return SafeBool(() => troop.IsRanged)
                ? GenericTroopRole.HorseArcher
                : GenericTroopRole.Cavalry;
        }

        if (SafeBool(() => troop.IsRanged))
        {
            return GenericTroopRole.Ranged;
        }

        return GenericTroopRole.Infantry;
    }

    public static int GetTierBucket(CharacterObject? troop)
    {
        if (troop == null) return 1;
        if (EvaluatorCache.TierCache.TryGetValue(troop, out var cached)) return cached;
        var result = GetTierBucketCore(troop);
        EvaluatorCache.TierCache[troop] = result;
        return result;
    }

    private static int GetTierBucketCore(CharacterObject troop)
    {
        try
        {
            if (troop.Tier <= 1) return 1;
            if (troop.Tier >= 6) return 6;
            return troop.Tier;
        }
        catch
        {
            return 1;
        }
    }

    public static bool MatchesRule(CharacterObject? troop, TownGarrisonRule? rule)
    {
        if (troop == null || rule == null) return false;

        try
        {
            if (troop.IsHero) return false;
            if (!troop.IsRegular) return false;
            if (!rule.AllowNobleTroops && TroopClassifier.IsNoble(troop)) return false;
            if (IsListed(troop, rule.BannedTroopIds)) return false;

            int tier = GetTierBucket(troop);
            // PR-5'(2026-05-24): MinTier/MaxTier removed from TownGarrisonRule; no tier-band filter here.

            // 显式 AllowedCultureIds 允许表（AI 氏族由 AiCulturePresets 写入；玩家默认空）。
            // 注意：玩家面板的「文化过滤」(TownGarrisonRule.GenericCultureFilter) 不在此处生效 ——
            // 它依赖玩家氏族 / 首府的运行时上下文（MatchesRule 拿不到），由各招募调用点通过
            // ResolveRequiredCultureId + CultureFilterAllows 单独应用。两套过滤互相独立、同时生效。
            if (rule.AllowedCultureIds != null && rule.AllowedCultureIds.Count > 0)
            {
                var cid = troop.Culture?.StringId;
                if (string.IsNullOrEmpty(cid)) return false;
                bool cultureOk = false;
                foreach (var id in rule.AllowedCultureIds)
                    if (string.Equals(id, cid, StringComparison.OrdinalIgnoreCase)) { cultureOk = true; break; }
                if (!cultureOk) return false;
            }

            var role = GetRole(troop);
            if (role == GenericTroopRole.Unknown) return false;
            // 2026-05-29: role-ratio gate 删除（rule 不再分 role 比例）。culture / banned / noble / hero 已过完即合格。
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 2026-05-29: 简化为"规则可接 → 优先列表加 100 分；否则 0 分"。
    /// role gap / tier 权重全部删除（B-pool solver 自己用 cost 通道处理 tier 偏好）。
    /// </summary>
    public static float ScoreCandidate(
        CharacterObject? troop,
        TownGarrisonRule? rule,
        TroopRoster? currentRoster,
        int targetTotal)
    {
        if (!MatchesRule(troop, rule) || troop == null || rule == null) return float.NegativeInfinity;
        return IsListed(troop, rule.PriorityTroopIds) ? 100f : 0f;
    }

    public static GenericCompositionSnapshot Snapshot(TroopRoster? roster)
    {
        if (roster == null) return default;

        int total = 0;
        int cav = 0, ha = 0, inf = 0, rng = 0;
        int t1 = 0, t2 = 0, t3 = 0, t4 = 0, t5 = 0, t6 = 0;

        foreach (var elem in roster.GetTroopRoster())
        {
            var ch = elem.Character;
            if (ch == null || ch.IsHero || elem.Number <= 0) continue;

            total += elem.Number;
            switch (GetRole(ch))
            {
                case GenericTroopRole.Cavalry: cav += elem.Number; break;
                case GenericTroopRole.HorseArcher: ha += elem.Number; break;
                case GenericTroopRole.Infantry: inf += elem.Number; break;
                case GenericTroopRole.Ranged: rng += elem.Number; break;
            }

            switch (GetTierBucket(ch))
            {
                case 1: t1 += elem.Number; break;
                case 2: t2 += elem.Number; break;
                case 3: t3 += elem.Number; break;
                case 4: t4 += elem.Number; break;
                case 5: t5 += elem.Number; break;
                default: t6 += elem.Number; break;
            }
        }

        return new GenericCompositionSnapshot(total, cav, ha, inf, rng, t1, t2, t3, t4, t5, t6);
    }

    // 2026-05-29: RoleRatio / TargetCount 已删。rule 不再带 role 比例，相关解算从 solver / 评分中砍出。

    /// <summary>
    /// 把 <see cref="TownGarrisonRule.GenericCultureFilter"/> 策略解析成「必须匹配的文化 stringId」。
    /// 返回 <c>null</c> 表示不按文化过滤。仅作用于玩家氏族首府的招募 —— 见字段注释。
    /// </summary>
    /// <param name="rule">首府规则（通常是 GlobalDefaults）。</param>
    /// <param name="capital">招募所服务的首府 Town；用于解析 OwnerClan 与 CapitalCulture。</param>
    public static string? ResolveRequiredCultureId(TownGarrisonRule? rule, Town? capital)
    {
        try
        {
            // PR-5'(2026-05-24): UseGenericMatching removed; generic matching is always on.
            // Culture filter always applies (when rule != null and capital is player clan).
            if (rule == null) return null;

            // GenericCultureFilter 是玩家网页面板的设置，只作用于玩家氏族。AI 城即便 rule 回退到
            // GlobalDefaults（ApplyToAiSettlementsToo=false 或 preset 缺失），也不应被玩家设置左右；
            // AI 自身文化过滤由 AiCulturePresets 写入的 AllowedCultureIds 负责。
            if (capital == null || capital.OwnerClan != Clan.PlayerClan) return null;

            switch (rule.GenericCultureFilter)
            {
                case "Any":
                    return null;
                case "CapitalCulture":
                    return capital.Culture?.StringId;
                case "PlayerCulture":
                default: // 未知 / 缺失值兜底为玩家文化
                    return Clan.PlayerClan?.Culture?.StringId;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 文化过滤判定：<paramref name="requiredCultureId"/> 为空表示不过滤（直接放行）；
    /// 否则要求兵种文化 stringId 与之相等（大小写无关）。配合 <see cref="ResolveRequiredCultureId"/> 使用。
    /// </summary>
    public static bool CultureFilterAllows(CharacterObject? troop, string? requiredCultureId)
    {
        if (string.IsNullOrEmpty(requiredCultureId)) return true;
        if (troop == null) return false;
        try
        {
            var cid = troop.Culture?.StringId;
            return !string.IsNullOrEmpty(cid)
                && string.Equals(cid, requiredCultureId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsListed(CharacterObject troop, IReadOnlyCollection<string>? ids)
    {
        if (ids == null || ids.Count == 0) return false;
        var stringId = troop.StringId;
        if (string.IsNullOrEmpty(stringId)) return false;
        foreach (var id in ids)
        {
            if (string.Equals(id, stringId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool SafeBool(Func<bool> getter)
    {
        try { return getter(); }
        catch { return false; }
    }

}
