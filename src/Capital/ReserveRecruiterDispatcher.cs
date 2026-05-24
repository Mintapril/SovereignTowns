using System;
using System.Collections.Generic;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Capital;

/// <summary>
/// PR-7: B 池专用征兵队调度器。
///
/// 每日 tick（OnDailyTickSettlement，首府节点）检查：
///   1. ReservePoolCap &gt; 0 且 ReserveTemplate 非空；
///   2. 计算全局 headroom = cap − bPool 当前 − 所有在途 ReserveRecruiterPartyComponent.TargetCount 之和；
///   3. 对每个 troopId：deficit = desiredCount − bPool 中该 troop 数量；
///   4. 若全局 headroom &gt; 0 且存在 deficit &gt; 0 的 troop，找有该 troop 志愿兵的村庄，派出一支
///      <see cref="ReserveRecruiterPartyComponent"/>（每次最多派 1 支，受 lifecycle cap=1 守护）。
/// </summary>
public sealed class ReserveRecruiterDispatcher
{
    private static readonly string PartyKind = PartyLifecycleManager.KindBPoolRecruiter;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry _capitalRegistry;
    private readonly ReservePoolManager _poolManager;

    public ReserveRecruiterDispatcher(
        PartyLifecycleManager lifecycle,
        CapitalRegistry capitalRegistry,
        ReservePoolManager poolManager)
    {
        _lifecycle       = lifecycle       ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
        _poolManager     = poolManager     ?? throw new ArgumentNullException(nameof(poolManager));
    }

    /// <summary>
    /// 每日 tick 入口（由 SovereignTownsCampaignBehavior.OnDailyTickSettlement 调用，
    /// 仅在 settlement == capital 时进入）。
    /// </summary>
    public void OnDailyTick(Settlement capital)
    {
        try
        {
            if (capital == null) return;

            var fa = ConfigurationManager.Current?.FiscalAutonomy;
            int poolCap = fa?.ReservePoolCap ?? 0;
            if (poolCap <= 0) return; // B 池关闭

            var template = fa?.ReserveTemplate;
            if (template == null || template.Count == 0) return; // 模板为空

            var bPool = _poolManager.GetPool(capital);
            if (bPool == null || !bPool.IsActive) return; // 无活跃 B 池

            // 全局 headroom = cap − 当前 B 池 − 所有在途 recruiter.TargetCount
            int inPool  = bPool.MemberRoster?.TotalManCount ?? 0;
            int inFlight = CountInFlightTarget(capital);
            int headroom = Math.Max(0, poolCap - inPool - inFlight);
            if (headroom <= 0) return;

            // 已达并发上限（cap=1 per home）
            if (!_lifecycle.CanCreateAnotherParty(capital, PartyKind))
            {
                Logger.Debug($"ReserveRecruiterDispatcher: '{capital.Name}' 已有在途 B 池征兵队，跳过");
                return;
            }

            // 找第一个有 deficit 的 troopId
            foreach (var kv in template)
            {
                string troopId      = kv.Key;
                int desiredCount    = kv.Value;

                if (string.IsNullOrEmpty(troopId) || desiredCount <= 0) continue;

                int alreadyInPool   = CountTroopInBPool(bPool, troopId);
                int deficit         = desiredCount - alreadyInPool;
                if (deficit <= 0) continue;

                // 实际派遣数量 = min(deficit, headroom)
                int sendCount = Math.Min(deficit, headroom);

                // 找一个有该兵种的村庄
                var village = FindVillageWithTroop(capital, troopId);
                if (village == null)
                {
                    Logger.Debug($"ReserveRecruiterDispatcher: '{capital.Name}' 找不到提供 '{troopId}' 的村庄，跳过");
                    continue;
                }

                TryDispatch(capital, village, troopId, sendCount);
                break; // 每 daily tick 至多派一支（lifecycle cap=1 进一步保护）
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ReserveRecruiterDispatcher.OnDailyTick failed for '{capital?.Name}'", ex);
        }
    }

    // ── 内部 ──

    private bool TryDispatch(Settlement capital, Settlement village, string troopId, int targetCount)
    {
        try
        {
            if (capital?.Town?.IsUnderSiege == true)
            {
                Logger.Info($"ReserveRecruiterDispatcher: '{capital.Name}' 被围城，跳过 B 池征兵");
                return false;
            }

            var party = ReserveRecruiterPartyComponent.Create(capital, village, troopId, targetCount);
            if (party == null)
            {
                Logger.Warn($"ReserveRecruiterDispatcher: Create returned null for '{capital.StringId}'");
                return false;
            }

            // 扣款 + 注资 + 买粮（与 StRecruiterPartyComponent 同路径）
            if (party.PartyComponent is ReserveRecruiterPartyComponent rrpc)
            {
                if (!SovereignTowns.Parties.StPartyComponent.TrySeedAndBuyInitialFood(
                    rrpc, party, capital,
                    SovereignTowns.Economy.ExpenseCategory.RecruiterSeed,
                    capital.OwnerClan,
                    $"bpool_recruiter home={capital.StringId} troop={troopId}"))
                {
                    SovereignTowns.Lifecycle.PartyMergeService.Instance?.DestroyAndUntrack(party, "ReserveRecruiterDispatcher seed failed", deferIfInMapEvent: false);
                    return false;
                }
            }

            _lifecycle.RegisterTrackedParty(party, capital, PartyKind);

            // 发出 SetMoveGoToSettlement
            try
            {
                party.SetMoveGoToSettlement(village, MobileParty.NavigationType.Default, false);
            }
            catch (Exception ex)
            {
                Logger.Error($"ReserveRecruiterDispatcher: SetMoveGoToSettlement failed for '{party.StringId}'", ex);
            }

            Logger.Info($"ReserveRecruiterDispatcher: dispatched '{party.StringId}' to '{village.Name}' for troop='{troopId}' target={targetCount}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ReserveRecruiterDispatcher.TryDispatch failed for '{capital?.Name}' troop='{troopId}'", ex);
            return false;
        }
    }

    /// <summary>统计所有在途（lifecycle 追踪）的 B 池征兵队的 TargetCount 之和。</summary>
    private int CountInFlightTarget(Settlement capital)
    {
        try
        {
            int total = 0;
            foreach (var party in MobileParty.All)
            {
                if (party?.PartyComponent is ReserveRecruiterPartyComponent rrpc
                    && rrpc.HomeSettlementOrNull == capital)
                {
                    total += rrpc.TargetCount;
                }
            }
            return total;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ReserveRecruiterDispatcher.CountInFlightTarget failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>统计 B 池 roster 中指定 troopId 的健康兵员数。</summary>
    private static int CountTroopInBPool(MobileParty bPool, string troopId)
    {
        try
        {
            var roster = bPool.MemberRoster;
            if (roster == null) return 0;
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character?.StringId == troopId) count += e.Number;
            }
            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 从首府关联村庄（ownerClan 友善 / 非交战）中找到有 troopId 志愿兵的一座村庄。
    /// 仅需找到有该 troopId 存在于 notable.VolunteerTypes 的村庄，不校验 maxIdx 可募性
    /// （那是 ReserveRecruiterPartyComponent 到村后执行的逻辑）。
    /// </summary>
    private static Settlement? FindVillageWithTroop(Settlement capital, string troopId)
    {
        try
        {
            var homeFaction = capital.MapFaction;
            if (homeFaction == null) return null;

            foreach (var village in Settlement.All)
            {
                if (village == null || !village.IsVillage || !village.IsActive) continue;

                var vf = village.MapFaction;
                if (vf == null) continue;
                if (vf != homeFaction && homeFaction.IsAtWarWith(vf)) continue;

                var v = village.Village;
                if (v == null) continue;
                if (v.VillageState == Village.VillageStates.BeingRaided
                    || v.VillageState == Village.VillageStates.Looted) continue;

                if (village.Notables == null) continue;
                foreach (var notable in village.Notables)
                {
                    if (notable?.CanHaveRecruits != true) continue;
                    var vt = notable.VolunteerTypes;
                    if (vt == null) continue;
                    foreach (var co in vt)
                    {
                        if (co != null && co.StringId == troopId)
                            return village;
                    }
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ReserveRecruiterDispatcher.FindVillageWithTroop failed: {ex.Message}");
            return null;
        }
    }
}
