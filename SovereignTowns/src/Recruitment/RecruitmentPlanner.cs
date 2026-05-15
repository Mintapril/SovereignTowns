using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 单个候选村庄的招募评估结果（不可变）。由 <see cref="RecruitmentPlanner"/> 输出。
/// </summary>
public readonly struct VillageRecruitOption
{
    /// <summary>
    /// 构造一个 <see cref="VillageRecruitOption"/>。
    /// </summary>
    /// <param name="villageSettlement">候选村庄的 <see cref="Settlement"/>（IsVillage=true）。</param>
    /// <param name="estimatedAvailableTroops">该村当前可招募的兵员粗估值（来自 Notables 的 VolunteerTypes 非空槽位求和）。</param>
    /// <param name="distanceFromHome">到 home town 的二维地图距离。</param>
    /// <param name="riskScore">基于 <see cref="Settlement.NearbyLandThreatIntensity"/> 的威胁分数（越高越危险）。</param>
    /// <param name="priorityScore">综合优先级（越高越应优先派遣）。</param>
    public VillageRecruitOption(
        Settlement villageSettlement,
        int estimatedAvailableTroops,
        float distanceFromHome,
        float riskScore,
        float priorityScore)
    {
        VillageSettlement = villageSettlement;
        EstimatedAvailableTroops = estimatedAvailableTroops;
        DistanceFromHome = distanceFromHome;
        RiskScore = riskScore;
        PriorityScore = priorityScore;
    }

    /// <summary>候选村庄的 vanilla <see cref="Settlement"/>。</summary>
    public Settlement VillageSettlement { get; }

    /// <summary>预估当前可招募的兵员数（Notables 的非空 VolunteerTypes 槽位求和）。</summary>
    public int EstimatedAvailableTroops { get; }

    /// <summary>到 home town 的二维地图距离。</summary>
    public float DistanceFromHome { get; }

    /// <summary>该村威胁分数（<see cref="Settlement.NearbyLandThreatIntensity"/>，越高越危险）。</summary>
    public float RiskScore { get; }

    /// <summary>综合优先级（越高越优先）。</summary>
    public float PriorityScore { get; }
}

/// <summary>
/// 无状态招募规划器：给定 home <see cref="Town"/>，挑选一批"该派征兵队过去"的候选村庄。
/// 不创建任何 MobileParty，不修改世界状态；纯计算 + 日志。线程注意：vanilla settlement 属性访问需主线程，
/// 调用方需在 campaign tick 上下文中触发。
/// </summary>
public static class RecruitmentPlanner
{
    private const int VolunteerSlotsPerNotable = 6;
    private const int MaxVolunteerContributionPerVillage = 6;
    private const float TroopScoreWeight = 10f;
    private const float DistancePenaltyWeight = 0.5f;
    private const float RiskPenaltyWeight = 5f;

    /// <summary>
    /// 给定 <paramref name="homeTown"/>，返回按 <see cref="VillageRecruitOption.PriorityScore"/> 倒序的候选村庄。
    /// 候选范围：homeTown 直接隶属村庄 + 其它同氏族自有 town 的村庄（仅当与 home town 距离 ≤ <paramref name="maxDistance"/>）。
    /// 不扫描全图全部 settlement —— 仅遍历本氏族自有 town 的 villages 集合。
    /// </summary>
    /// <param name="homeTown">发起招募的受管城镇。null 时返回空数组。</param>
    /// <param name="maxDistance">最大允许的二维地图距离（默认 100f）。</param>
    /// <param name="maxResults">最多返回的候选数（默认 10）。</param>
    public static IReadOnlyList<VillageRecruitOption> RankCandidates(
        Town homeTown,
        float maxDistance = 100f,
        int maxResults = 10,
        IReadOnlyCollection<Settlement>? excludeSettlements = null,
        TownGarrisonRule? matchingRule = null)
    {
        if (homeTown == null || homeTown.Settlement == null)
        {
            return Array.Empty<VillageRecruitOption>();
        }

        if (maxResults <= 0)
        {
            return Array.Empty<VillageRecruitOption>();
        }

        try
        {
            var homeSettlement = homeTown.Settlement;
            var homeFaction = homeSettlement.MapFaction;
            var homePos = homeSettlement.GetPosition2D;

            // 用一个 HashSet 避免重复（同一 village 可能属于直接列表 + 范围扩展两个来源）。
            var seen = new HashSet<Settlement>();
            var options = new List<VillageRecruitOption>();
            var excludeSet = (excludeSettlements != null && excludeSettlements.Count > 0)
                ? new HashSet<Settlement>(excludeSettlements)
                : null;

            // 1) home town 直接隶属的村庄
            var directVillages = homeTown.Villages;
            if (directVillages != null)
            {
                for (int i = 0; i < directVillages.Count; i++)
                {
                    var v = directVillages[i];
                    var s = v?.Settlement;
                    if (s == null) continue;
                    if (!seen.Add(s)) continue;
                    TryAdd(options, s, homeSettlement, homeFaction, homePos, maxDistance,
                        includeDistanceFilter: false, excludeSet: excludeSet, matchingRule: matchingRule);
                }
            }

            // 2) 其它 *同氏族* 自有 town 的村庄（仅当 ≤ maxDistance）。
            // B7.15: 多 clan 化 — 用 homeTown.OwnerClan 而非硬编码 PlayerClan，让 AI clan
            // 的征兵队也能跨自家其他城的辐射范围招兵（玩家走同样路径仍正确）。
            var homeClan = homeTown.OwnerClan;
            if (homeClan != null)
            {
                foreach (var t in Town.AllTowns)
                {
                    if (t == null) continue;
                    if (t == homeTown) continue;
                    if (!t.IsTown) continue;
                    if (t.OwnerClan != homeClan) continue;

                    var villages = t.Villages;
                    if (villages == null) continue;
                    for (int i = 0; i < villages.Count; i++)
                    {
                        var v = villages[i];
                        var s = v?.Settlement;
                        if (s == null) continue;
                        if (!seen.Add(s)) continue;
                        TryAdd(options, s, homeSettlement, homeFaction, homePos, maxDistance,
                            includeDistanceFilter: true, excludeSet: excludeSet, matchingRule: matchingRule);
                    }
                }
            }

            // 按 PriorityScore 倒序，再截断到 maxResults
            options.Sort(static (a, b) => b.PriorityScore.CompareTo(a.PriorityScore));
            if (options.Count > maxResults)
            {
                options.RemoveRange(maxResults, options.Count - maxResults);
            }

            Logger.Debug(
                $"RecruitmentPlanner.RankCandidates home='{homeSettlement.Name}' " +
                $"scanned={seen.Count} kept={options.Count} maxDistance={maxDistance:F1} " +
                $"exclude={excludeSet?.Count ?? 0} ruleMatch={(matchingRule != null)}");

            return options;
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitmentPlanner.RankCandidates failed", ex);
            return Array.Empty<VillageRecruitOption>();
        }
    }

    private static void TryAdd(
        List<VillageRecruitOption> sink,
        Settlement villageSettlement,
        Settlement homeSettlement,
        IFaction homeFaction,
        Vec2 homePos,
        float maxDistance,
        bool includeDistanceFilter,
        HashSet<Settlement>? excludeSet,
        TownGarrisonRule? matchingRule)
    {
        if (!villageSettlement.IsVillage) return;
        if (!villageSettlement.IsActive) return;

        // 显式排除（调用方传入，例如刚刚招过的村庄）
        if (excludeSet != null && excludeSet.Contains(villageSettlement)) return;

        // 冷却中（vanilla 还未刷新足够 volunteer 槽位）
        if (RecruitmentCooldown.IsOnCooldown(villageSettlement)) return;

        // 不去敌方阵营
        if (villageSettlement.MapFaction != homeFaction) return;

        // 村庄状态屏蔽：被洗劫 / 正在被劫
        var village = villageSettlement.Village;
        if (village != null)
        {
            var state = village.VillageState;
            if (state == Village.VillageStates.BeingRaided || state == Village.VillageStates.Looted)
            {
                return;
            }
        }

        // 距离
        var pos = villageSettlement.GetPosition2D;
        var distance = (pos - homePos).Length;
        if (includeDistanceFilter && distance > maxDistance) return;

        // 兵员粗估：遍历 Notables 的 VolunteerTypes 非 null 槽位
        var available = CountAvailableVolunteers(villageSettlement);
        if (available <= 0) return;

        // 兵种匹配：若提供 rule，必须 village 至少一个 volunteer 命中 rule 非零 ratio 的某个兵种桶
        if (matchingRule != null && !VillageHasMatchingTroops(villageSettlement, matchingRule))
        {
            return;
        }

        var risk = villageSettlement.NearbyLandThreatIntensity;

        var priority =
            (TroopScoreWeight * Math.Min(available, MaxVolunteerContributionPerVillage))
            - (DistancePenaltyWeight * distance)
            - (RiskPenaltyWeight * risk);

        sink.Add(new VillageRecruitOption(
            villageSettlement: villageSettlement,
            estimatedAvailableTroops: available,
            distanceFromHome: distance,
            riskScore: risk,
            priorityScore: priority));
    }

    /// <summary>
    /// 该 village 的 Notables.VolunteerTypes 中是否存在至少一个兵种命中当前招募规则。
    /// 用于"目标兵种在该村没有则不去"的剪枝。
    /// </summary>
    private static bool VillageHasMatchingTroops(Settlement villageSettlement, TownGarrisonRule rule)
    {
        try
        {
            var notables = villageSettlement.Notables;
            if (notables == null || notables.Count == 0) return false;

            for (int i = 0; i < notables.Count; i++)
            {
                var notable = notables[i];
                if (notable == null) continue;
                var slots = notable.VolunteerTypes;
                if (slots == null) continue;

                int len = slots.Length;
                if (len > VolunteerSlotsPerNotable) len = VolunteerSlotsPerNotable;
                for (int j = 0; j < len; j++)
                {
                    var troop = slots[j];
                    if (troop == null) continue;
                    if (TroopTemplateMatcher.MatchesRule(troop, rule)) return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"VillageHasMatchingTroops threw for '{villageSettlement?.StringId}': {ex.Message}");
            // 异常路径放行而不剔除，避免计算 bug 让规划完全瘫痪
            return true;
        }
    }

    private static int CountAvailableVolunteers(Settlement villageSettlement)
    {
        var notables = villageSettlement.Notables;
        if (notables == null || notables.Count == 0) return 0;

        int total = 0;
        for (int i = 0; i < notables.Count; i++)
        {
            var notable = notables[i];
            if (notable == null) continue;
            var slots = notable.VolunteerTypes;
            if (slots == null) continue;

            // VolunteerTypes 长度固定为 6，但仍按实际长度遍历以兼容未来变更。
            int len = slots.Length;
            if (len > VolunteerSlotsPerNotable) len = VolunteerSlotsPerNotable;
            for (int j = 0; j < len; j++)
            {
                if (slots[j] != null) total++;
            }
        }
        return total;
    }
}
