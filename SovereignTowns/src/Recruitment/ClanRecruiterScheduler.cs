using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Coordination;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Recruitment;

/// <summary>
/// B7.27：全氏族征兵调度器。每个 Clan 一份，由该 Clan 的 CapitalManager 持有。
///
/// 现已继承 <see cref="BaseSettlementVisitScheduler"/>：
///   - PickNextVillage / RecordVisit / PreemptiveBook / IsStuck / TryMarkArrival /
///     NotifySettlementLost / NotifyAllLost / NotifyPartyDestroyed 均由基类提供；
///   - 子类只补：候选源（RecruitmentPlanner.RankCandidates 取首府的 Top-N 村庄）、
///     无附加过滤（RankCandidates 已处理兵种匹配/距离/风险/村庄状态/72h 冷却）、
///     以及配置 getter（ClanRecruiter 段）。
///
/// 与 VillageCooldownHours=72h 协作：72h 是硬性冷却（RankCandidates 内 RecruitmentCooldown 表保护），
/// 本调度器额外提供 MinVisitGapHours 短期回访保护 + 多队互补 PreemptiveBook。
/// 持久化模型：mod 自定义存档已删除 —— 全部字段均瞬态，读档后由活动重建。
/// </summary>
public sealed class ClanRecruiterScheduler : BaseSettlementVisitScheduler
{
    public ClanRecruiterScheduler(Clan clan) : base(clan)
    {
    }

    // ── 基类钩子实现 ──

    protected override IEnumerable<Settlement> EnumerateCandidates(MobileParty party)
    {
        // 取首府 Town 用于 RankCandidates 输入
        var capitalMgr = CapitalRegistry.Instance?.GetForClan(_clan);
        var capitalTown = capitalMgr?.GetCapital();
        if (capitalTown == null) return System.Array.Empty<Settlement>();

        var rule = ConfigurationManager.GetRuleFor(capitalTown);
        if (rule == null) return System.Array.Empty<Settlement>();

        // 用现有 RecruitmentPlanner 取候选（已处理：兵种匹配、距离、风险、村庄状态、72h 冷却）
        var candidates = RecruitmentPlanner.RankCandidates(
            capitalTown,
            maxDistance: 100f,
            maxResults: 8,
            excludeSettlements: null,
            matchingRule: rule);
        if (candidates == null || candidates.Count == 0) return System.Array.Empty<Settlement>();

        return candidates
            .Select(c => c.VillageSettlement)
            .Where(s => s != null);
    }

    /// <summary>
    /// RankCandidates 已处理兵种匹配/距离/风险/村庄状态/72h 冷却等所有过滤，
    /// 此处无需再加任何附加过滤。
    /// </summary>
    protected override bool PassesCandidateFilter(Settlement s, MobileParty party) => true;

    protected override float MinVisitGapHours
        => ConfigurationManager.Current.ClanRecruiter.MinVisitGapHours;

    protected override float DistanceWeightHoursPerTile
        => ConfigurationManager.Current.ClanRecruiter.DistanceWeightHoursPerTile;

    protected override float EtaBufferHours
        => ConfigurationManager.Current.ClanRecruiter.EtaBufferHours;

    protected override string SchedulerLogTag => "ClanRecruiterScheduler";

    /// <summary>保留旧 API 名给 RecruitmentManager 调用。</summary>
    public Settlement? PickNextVillage(MobileParty recruiterParty) => PickNext(recruiterParty);
}
