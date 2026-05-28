using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Parties;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Algorithm;

/// <summary>
/// 招募拓扑 helper —— 由 <see cref="UnifiedGarrisonSolver"/> 建图时复用:
/// notable 志愿兵枚举 / character→bucket 分桶 / 招募候选村枚举 / 在飞征兵队目标村排除。
/// 旧 SupplyDemandGraph 删除后从中析出,无状态、纯只读。
/// </summary>
public static class RecruitmentTopology
{
    private static PartyThresholds Thresholds
        => ConfigurationManager.Current?.Thresholds ?? new PartyThresholds();

    /// <summary>
    /// 把一组 character 按可服务 role 分桶（取每桶最低 tier 兵作代表）。传入 rule 时会用
    /// 与执行层一致的模板 / 文化过滤,精确模板模式按可升级目标 role 分桶。
    /// </summary>
    public static List<TroopBucket> BucketizeCharacters(
        IEnumerable<CharacterObject?> characters,
        TownGarrisonRule? rule = null,
        string? requiredCultureId = null)
    {
        var byRole = new Dictionary<GenericTroopRole, (int Count, int MinTier, CharacterObject? Representative)>();
        foreach (var character in characters)
        {
            if (character == null || character.IsHero) continue;
            if (rule != null)
            {
                if (!TroopTemplateMatcher.MatchesRule(character, rule)) continue;
                // PR-5'(2026-05-24): UseGenericMatching removed; culture filter always applies.
                if (!GenericTroopMatcher.CultureFilterAllows(character, requiredCultureId))
                    continue;
            }

            var role = rule != null
                ? TroopTemplateMatcher.GetServiceRole(character, rule)
                : GenericTroopMatcher.GetRole(character);
            if (role == GenericTroopRole.Unknown) continue;
            int tier = GenericTroopMatcher.GetTierBucket(character);

            if (!byRole.TryGetValue(role, out var current))
            {
                byRole[role] = (1, tier, character);
                continue;
            }

            var representative = current.Representative;
            int minTier = current.MinTier;
            if (tier < minTier)
            {
                minTier = tier;
                representative = character;
            }
            byRole[role] = (current.Count + 1, minTier, representative);
        }

        return byRole.Select(kv => new TroopBucket(kv.Key, kv.Value.Count, kv.Value.MinTier, kv.Value.Representative)).ToList();
    }

    /// <summary>枚举某定居点所有 notable 的志愿兵 slot 兵种（CanHaveRecruits 的 notable）。</summary>
    public static IEnumerable<CharacterObject?> EnumerateVolunteerTroops(Settlement settlement)
    {
        var notables = settlement.Notables;
        if (notables == null) yield break;

        foreach (var notable in notables)
        {
            if (notable == null || !notable.CanHaveRecruits) continue;
            var slots = notable.VolunteerTypes;
            if (slots == null) continue;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null) yield return slots[i];
        }
    }

    // daily 缓存：稳定字段（IsVillage / IsActive / faction / 距离 top-N）的候选村。
    // 动态字段（cooldown / raided / excludeVillages）每次调用时再过滤。
    // 失效条件：capitalTown 变 / capitalFaction 变 / 缓存超过 22h。
    private static readonly object _cacheLock = new();
    private static Town? _cachedTown;
    private static IFaction? _cachedFaction;
    private static int _cachedCap;
    private static CampaignTime _cachedAt;
    private static List<Settlement>? _cachedBaseCandidates;

    /// <summary>
    /// 招募候选村：全图 village，过滤（active / 非围城 / 非 Raided·Looted / 与 clan 非交战 /
    /// 非招募冷却 / 非在飞征兵队目标），按距首府距离升序取 Top-RecruiterVillageCandidateCap。
    /// 稳定字段（IsVillage / IsActive / faction / 距离）每 22h 缓存一次；动态字段每次实时过滤。
    /// </summary>
    public static List<Settlement> EnumerateRecruitmentVillages(
        Town capitalTown, Clan? clan, HashSet<Settlement> excludeVillages)
    {
        var result = new List<Settlement>();
        try
        {
            var capitalSettlement = capitalTown?.Settlement;
            if (capitalSettlement == null) return result;
            var capitalFaction = capitalSettlement.MapFaction;
            int cap = Math.Max(4, Thresholds.RecruiterVillageCandidateCap);

            var baseCandidates = GetOrBuildBaseCandidates(capitalTown!, capitalFaction, cap);
            if (baseCandidates == null) return result;

            // 动态过滤（每次）：IsUnderSiege / cooldown / raided / exclude
            foreach (var s in baseCandidates)
            {
                if (s == null) continue;
                if (s.IsUnderSiege) continue;
                if (excludeVillages.Contains(s)) continue;
                if (RecruitmentCooldown.IsOnCooldown(s)) continue;
                var v = s.Village;
                if (v != null && (v.VillageState == Village.VillageStates.BeingRaided
                                  || v.VillageState == Village.VillageStates.Looted)) continue;
                result.Add(s);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitmentTopology.EnumerateRecruitmentVillages failed", ex);
        }
        return result;
    }

    /// <summary>
    /// HG 专用候选村枚举：全图 village，仅剔除围城 / Raided / Looted / cooldown / 在飞重复。
    /// **不**剔交战 faction，**不**按距离 top-N 裁，**不**走缓存——HG 长程招募需要全图可见性。
    /// 性能：全图扫 ~250 村 × 简单字段判断，每 tick &lt; 1ms，可忽略。
    /// </summary>
    public static List<Settlement> EnumerateRecruitmentVillagesForHG(
        Settlement capitalSettlement, Clan? clan, HashSet<Settlement> excludeVillages)
    {
        var result = new List<Settlement>();
        try
        {
            if (capitalSettlement == null) return result;
            var all = Settlement.All;
            if (all == null) return result;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.IsVillage || !s.IsActive) continue;
                if (s.IsUnderSiege) continue;
                if (excludeVillages.Contains(s)) continue;
                if (RecruitmentCooldown.IsOnCooldown(s)) continue;
                var v = s.Village;
                if (v != null && (v.VillageState == Village.VillageStates.BeingRaided
                                  || v.VillageState == Village.VillageStates.Looted)) continue;
                result.Add(s);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitmentTopology.EnumerateRecruitmentVillagesForHG failed", ex);
        }
        return result;
    }

    private static List<Settlement>? GetOrBuildBaseCandidates(Town capitalTown, IFaction? capitalFaction, int cap)
    {
        lock (_cacheLock)
        {
            bool valid = _cachedBaseCandidates != null
                      && _cachedTown == capitalTown
                      && _cachedFaction == capitalFaction
                      && _cachedCap == cap;
            if (valid)
            {
                try
                {
                    var ageHours = (CampaignTime.Now - _cachedAt).ToHours;
                    if (ageHours < 22.0) return _cachedBaseCandidates;
                }
                catch { /* stale time, rebuild */ }
            }

            var fresh = BuildBaseCandidates(capitalTown, capitalFaction, cap);
            _cachedTown = capitalTown;
            _cachedFaction = capitalFaction;
            _cachedCap = cap;
            _cachedAt = CampaignTime.Now;
            _cachedBaseCandidates = fresh;
            return fresh;
        }
    }

    private static List<Settlement> BuildBaseCandidates(Town capitalTown, IFaction? capitalFaction, int cap)
    {
        var result = new List<Settlement>();
        var capitalPos = capitalTown.Settlement.GetPosition2D;
        var all = Settlement.All;
        if (all == null) return result;

        var scored = new List<(Settlement Village, float Dist)>();
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null || !s.IsVillage || !s.IsActive) continue;
            var f = s.MapFaction;
            if (f == null) continue;
            if (f != capitalFaction)
            {
                if (capitalFaction == null) continue;
                if (capitalFaction.IsAtWarWith(f)) continue;
            }
            scored.Add((s, (s.GetPosition2D - capitalPos).Length));
        }
        scored.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));
        int take = Math.Min(cap, scored.Count);
        for (int i = 0; i < take; i++) result.Add(scored[i].Village);
        return result;
    }

    /// <summary>
    /// 本 clan 在飞征兵队当前及之后尚未访问的目标村集合。这些村已被服务,排除出本轮招募图,
    /// 防止下一 tick MCMF 重复派队去同一个村。
    /// </summary>
    public static HashSet<Settlement> CollectInFlightRecruiterVillages(Clan? clan)
    {
        var set = new HashSet<Settlement>();
        if (clan == null) return set;
        try
        {
            var parties = MobileParty.AllCustomParties;
            if (parties == null) return set;
            foreach (var party in parties)
            {
                if (party == null || !party.IsActive) continue;
                if (!(party.PartyComponent is StRecruiterPartyComponent recruiter)) continue;
                if (recruiter.HomeSettlementOrNull?.OwnerClan != clan) continue;
                foreach (var v in recruiter.PendingVillages)
                    if (v != null) set.Add(v);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitmentTopology.CollectInFlightRecruiterVillages failed", ex);
        }
        return set;
    }
}
