using System;
using System.Collections.Generic;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Coordination;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Patrol;

/// <summary>
/// B7.26：全氏族巡逻调度器。每个 Clan 一份，由该 Clan 的 CapitalManager 持有。
///
/// 现已继承 <see cref="BaseSettlementVisitScheduler"/>：
///   - PickNextStop / RecordVisit / PreemptiveBook / IsStuck / TryMarkArrival /
///     NotifySettlementLost / NotifyAllLost / NotifyPartyDestroyed 均由基类提供；
///   - 子类只补：候选源（_clan.Settlements）、过滤（OwnerClan/IsUnderSiege/raided village）、
///     配置 getter（ClanPatrol 段），以及 patrol 独有的 GetDefenseTarget。
///
/// 持久化模型：mod 自定义存档已删除 —— 全部字段均瞬态，读档后由活动重建。
/// 不注册到 <see cref="SovereignTowns.SaveSystem.SovereignTownsTypeDefiner"/>。
///
/// 调用线程模型：所有方法假定在 vanilla campaign tick（主线程）调用；同 hour 内不会并发。
/// 网页配置线程不直接调本类，配置变更通过 ConfigurationManager.Current 静态读取。
/// </summary>
public sealed class ClanPatrolScheduler : BaseSettlementVisitScheduler
{
    public ClanPatrolScheduler(Clan clan) : base(clan)
    {
    }

    // ── 基类钩子实现 ──

    protected override IEnumerable<Settlement> EnumerateCandidates(MobileParty party)
    {
        // Clan.Settlements 在 v1.3.15 包含 clan 直接拥有的 town/castle/village。
        // 若实际不返回 village（运行验证），改用 Settlement.All 全扫 + OwnerClan 过滤（O(全图)，仍在 hourly 预算内）。
        return _clan.Settlements;
    }

    protected override bool PassesCandidateFilter(Settlement s, MobileParty party)
    {
        if (s.OwnerClan != _clan) return false;
        if (s.IsUnderSiege) return false;
        var config = ConfigurationManager.Current.ClanPatrol;
        if (s.IsVillage && config.AvoidRaidedVillages && IsVillageRaided(s)) return false;
        return true;
    }

    protected override float MinVisitGapHours
        => ConfigurationManager.Current.ClanPatrol.MinVisitGapHours;

    protected override float DistanceWeightHoursPerTile
        => ConfigurationManager.Current.ClanPatrol.DistanceWeightHoursPerTile;

    protected override float EtaBufferHours
        => ConfigurationManager.Current.ClanPatrol.EtaBufferHours;

    protected override string SchedulerLogTag => "ClanPatrolScheduler";

    // ── 旧 API 名薄包装（保留调用点兼容） ──

    /// <summary>
    /// 为巡逻队选下一站（旧 API 名）。等价于基类 <see cref="BaseSettlementVisitScheduler.PickNext"/>。
    /// </summary>
    public Settlement? PickNextStop(MobileParty patrolParty) => PickNext(patrolParty);

    // ── 防御与生命周期（patrol 独有） ──

    /// <summary>
    /// 该巡逻队当前应去防守的同氏族城（若有正在被围攻的）。返回 null 表示无需防御响应。
    /// 调用方据返回值切到 Defense / MergeGarrison（首府被围 → MergeGarrison）。
    /// </summary>
    /// <remarks>
    /// 多巡逻队各自独立调用本方法，可能同时选中同一非首府目标（无跨队分配，MVP 接受）。
    /// </remarks>
    public Settlement? GetDefenseTarget(MobileParty patrolParty)
    {
        if (patrolParty == null) return null;
        try
        {
            // 收集本氏族正被围攻的 settlement
            var besieged = new List<Settlement>();
            foreach (var s in _clan.Settlements)
            {
                if (s != null && s.OwnerClan == _clan && s.IsUnderSiege)
                    besieged.Add(s);
            }
            if (besieged.Count == 0) return null;

            // 找首府：首府被围 → 所有 patrol 直接 MergeGarrison（调用方据返回值判断）
            // 这里只负责"返回目标 settlement"；调用方据 settlement == 首府 来决定 Order
            var capital = TryGetCapitalManager()?.GetCapitalSettlement();
            if (capital != null)
            {
                foreach (var s in besieged)
                {
                    if (s == capital) return s;
                }
            }

            // 非首府：选距离本 party 最近的围攻点
            Settlement? closest = null;
            float closestDist = float.MaxValue;
            var partyPos = patrolParty.GetPosition2D;
            foreach (var s in besieged)
            {
                float d = (partyPos - s.GetPosition2D).Length;
                if (d < closestDist)
                {
                    closestDist = d;
                    closest = s;
                }
            }
            return closest;
        }
        catch (Exception ex)
        {
            Logger.Error("ClanPatrolScheduler.GetDefenseTarget failed", ex);
            return null;
        }
    }

    private CapitalManager? TryGetCapitalManager()
    {
        try
        {
            return CapitalRegistry.Instance?.GetForClan(_clan);
        }
        catch
        {
            return null;
        }
    }

    // ── 私有辅助 ──

    private static bool IsVillageRaided(Settlement village)
    {
        try
        {
            return village.Village?.VillageState == Village.VillageStates.Looted
                || village.Village?.VillageState == Village.VillageStates.BeingRaided;
        }
        catch
        {
            return false;
        }
    }
}
