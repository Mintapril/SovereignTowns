using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Coordination;

/// <summary>
/// Scheduler 基类：管理"某氏族范围内多支 party 对定居点的差异化访问调度"。
/// 子类只需提供候选源 EnumerateCandidates、过滤 PassesCandidateFilter、
/// 以及 3 个配置参数（MinVisitGapHours / DistanceWeightHoursPerTile / EtaBufferHours）。
///
/// 评分公式：score = -hoursSinceVisit + DistanceWeight * distance，越小越优先（久未访问优先 + 距离近优先）。
/// 持久化：C 系列后无 mod 自定义存档；瞬态字典在游戏会话内有效。
/// </summary>
public abstract class BaseSettlementVisitScheduler
{
    protected readonly Clan _clan;
    protected readonly Dictionary<string, CampaignTime> _lastVisitedAt = new();   // key: Settlement.StringId
    protected readonly Dictionary<string, CampaignTime> _bookedUntil = new();     // 瞬态
    protected readonly Dictionary<MBGUID, CampaignTime> _lastStopChangedAt = new();  // 瞬态
    protected readonly Dictionary<MBGUID, string> _lastSeenLocation = new();      // 瞬态

    protected BaseSettlementVisitScheduler(Clan clan)
    {
        _clan = clan ?? throw new ArgumentNullException(nameof(clan));
    }

    public Clan OwnerClan => _clan;

    // ── 子类钩子 ──

    /// <summary>子类返回该氏族范围内此次调度的候选定居点。</summary>
    protected abstract IEnumerable<Settlement> EnumerateCandidates(MobileParty party);

    /// <summary>子类候选过滤：true=接受。如 patrol 加 IsUnderSiege / IsVillageRaided 过滤。</summary>
    protected abstract bool PassesCandidateFilter(Settlement s, MobileParty party);

    /// <summary>子类提供配置中此 scheduler 段的 MinVisitGapHours（短期回访保护小时数）。从 ConfigurationManager.Current 静态读，支持热重载。</summary>
    protected abstract float MinVisitGapHours { get; }

    /// <summary>子类提供配置中此 scheduler 段的 DistanceWeightHoursPerTile。</summary>
    protected abstract float DistanceWeightHoursPerTile { get; }

    /// <summary>子类提供配置中此 scheduler 段的 EtaBufferHours。</summary>
    protected abstract float EtaBufferHours { get; }

    /// <summary>子类 log 标签，如 "ClanPatrolScheduler" 或 "ClanRecruiterScheduler"。</summary>
    protected abstract string SchedulerLogTag { get; }

    // ── 公共 API（基类实现） ──

    /// <summary>
    /// 为 party 选下一站。按 "最久未访问 + 距离权重" 评分，排除被过滤/被他队预占/最小回访间隔内的。
    /// 选中后自动 PreemptiveBook。返回 null 表示当前无合适候选（调用方可让 party 回首府）。
    /// </summary>
    public Settlement? PickNext(MobileParty party)
    {
        if (party == null) return null;
        try
        {
            var now = CampaignTime.Now;
            var partyPos = party.GetPosition2D;
            Settlement? best = null;
            float bestScore = float.MaxValue;

            int total = 0, filteredOut = 0, bookedOut = 0, gapOut = 0, passed = 0;
            var diag = new System.Text.StringBuilder();
            float gap = MinVisitGapHours;
            float weight = DistanceWeightHoursPerTile;

            foreach (var s in EnumerateCandidates(party))
            {
                if (s == null) continue;
                total++;
                if (!PassesCandidateFilter(s, party)) { filteredOut++; diag.Append($" filtered:'{s.Name}'"); continue; }

                // 多队互补：被他队预占且未到期 → 跳过
                if (_bookedUntil.TryGetValue(s.StringId, out var booked) && booked > now)
                {
                    bookedOut++;
                    diag.Append($" booked:'{s.Name}'(until={(booked - now).ToHours:F1}h)");
                    continue;
                }

                // 最小回访间隔
                if (_lastVisitedAt.TryGetValue(s.StringId, out var lva))
                {
                    var sinceHrs = lva.ElapsedHoursUntilNow;
                    if (sinceHrs < gap)
                    {
                        gapOut++;
                        diag.Append($" gap:'{s.Name}'(visited={sinceHrs:F1}h<{gap}h)");
                        continue;
                    }
                }

                float hoursSinceVisit = _lastVisitedAt.TryGetValue(s.StringId, out var l)
                    ? (float)l.ElapsedHoursUntilNow
                    : 1e6f;
                float distance = (partyPos - s.GetPosition2D).Length;
                float score = -hoursSinceVisit + weight * distance;
                passed++;
                diag.Append($" cand:'{s.Name}'(sinceH={hoursSinceVisit:F1} dist={distance:F1} score={score:F2})");

                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }

            Logger.Info($"[DIAG] {SchedulerLogTag}.PickNext party='{party?.Name?.ToString() ?? "null"}' total={total} filtered={filteredOut} booked={bookedOut} gap={gapOut} passed={passed} → best='{best?.Name?.ToString() ?? "null"}'{diag}");

            if (best != null)
            {
                float etaHours = ComputeEtaHours(party, best);
                PreemptiveBook(best, party, etaHours);
            }
            return best;
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.PickNext failed for clan '{_clan?.StringId}'", ex);
            return null;
        }
    }

    public void RecordVisit(Settlement settlement)
    {
        if (settlement == null) return;
        try
        {
            _lastVisitedAt[settlement.StringId] = CampaignTime.Now;
            _bookedUntil.Remove(settlement.StringId);
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.RecordVisit failed for '{settlement?.StringId}'", ex);
        }
    }

    public void PreemptiveBook(Settlement settlement, MobileParty party, float etaHours)
    {
        if (settlement == null || party == null) return;
        try
        {
            float bookHours = Math.Max(0.5f, etaHours + EtaBufferHours);
            _bookedUntil[settlement.StringId] = CampaignTime.HoursFromNow(bookHours);
            _lastStopChangedAt[party.Id] = CampaignTime.Now;
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.PreemptiveBook failed", ex);
        }
    }

    public bool IsStuck(MobileParty party, float stuckTimeoutHours)
    {
        if (party == null) return false;
        try
        {
            if (!_lastStopChangedAt.TryGetValue(party.Id, out var last)) return false;
            return last.ElapsedHoursUntilNow >= stuckTimeoutHours;
        }
        catch { return false; }
    }

    public bool TryMarkArrival(MobileParty party, Settlement visited)
    {
        if (party == null || visited == null) return false;
        try
        {
            var sid = visited.StringId;
            if (_lastSeenLocation.TryGetValue(party.Id, out var prev) && prev == sid) return false;
            _lastSeenLocation[party.Id] = sid;
            return true;
        }
        catch { return false; }
    }

    public void NotifySettlementLost(Settlement settlement)
    {
        if (settlement == null) return;
        try
        {
            _lastVisitedAt.Remove(settlement.StringId);
            _bookedUntil.Remove(settlement.StringId);
            Logger.Info($"{SchedulerLogTag}({_clan.StringId}): cleared state for lost settlement '{settlement.StringId}'");
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.NotifySettlementLost failed for '{settlement?.StringId}'", ex);
        }
    }

    public void NotifyAllLost()
    {
        try
        {
            _lastVisitedAt.Clear();
            _bookedUntil.Clear();
            _lastStopChangedAt.Clear();
            _lastSeenLocation.Clear();
            Logger.Info($"{SchedulerLogTag}({_clan.StringId}): NotifyAllLost — all state cleared");
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.NotifyAllLost failed", ex);
        }
    }

    public void NotifyPartyDestroyed(MobileParty party)
    {
        if (party == null) return;
        try
        {
            _lastStopChangedAt.Remove(party.Id);
            _lastSeenLocation.Remove(party.Id);
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.NotifyPartyDestroyed failed", ex);
        }
    }

    protected static float ComputeEtaHours(MobileParty party, Settlement target)
    {
        try
        {
            float distance = (party.GetPosition2D - target.GetPosition2D).Length;
            float speed = Math.Max(party.Speed, 0.1f);
            return distance / speed;
        }
        catch
        {
            return 24f;
        }
    }
}
