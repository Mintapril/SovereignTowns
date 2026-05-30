using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Patrol;

/// <summary>
/// B16.4：监管受管 clan（玩家氏族 + 可选 AI 氏族，通过 CapitalRegistry）的巡逻队创建端。
///
/// 设计原则：
///   - 仅创建端在此；状态机（防御响应 / 支援 / 抵达 / 卡死）已搬到 <see cref="StPatrolPartyComponent"/>；
///   - 所有方法 try-catch，绝不向调用方抛异常；
///   - 巡逻队由 <see cref="CapitalLogisticsManager"/> 经时间展开调度器(MCMF)派发(<see cref="TryDispatchPatrol"/>);
///     不再有每小时启发式创建路径；
///   - 路径选择委托给该 clan 的 <see cref="ClanPatrolScheduler"/>（全氏族范围、最久未访问 + 距离评分、多队预占互补）。
///
/// 与 vanilla 共存：ST 创建的 <see cref="StPatrolPartyComponent"/> 不进 MobileParty.AllPatrolParties；
/// vanilla 自动 spawn 的巡逻队继续以 PatrolPartyComponent 形式存在（共存策略，不互相干涉）。
/// </summary>
public sealed class PatrolDispatcher
{
    private static int PatrolMinDispatchSize
        => ConfigurationManager.Current?.Thresholds?.PatrolMinDispatchSize ?? 50;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public PatrolDispatcher(
        PartyLifecycleManager lifecycle,
        CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }

    // ────────── 创建巡逻队 ──────────

    /// <summary>
    /// 创建一支巡逻队:抽 <paramref name="troopCount"/> 兵、注资、注册、走 scheduler 首站。
    /// 抽兵 / 注资 / scheduler 任一步失败均回滚并返回 false。
    /// 调用方负责前置门控(围城 / 队伍上限 / 兵力来源决策)。
    /// </summary>
    private bool CreateOnePatrol(Settlement settlement, int troopCount)
    {
        if (settlement?.Town == null || troopCount <= 0) return false;
        var town = settlement.Town;
        var garrison = town.GarrisonParty;
        int garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;

        // 模板：仅作语义参考（按文化挑兵种），B16.4 起本 dispatcher 不再依赖 vanilla CreatePatrolParty 自动注兵。
        // 找不到模板仍按 null 创建，兵员从 garrison 直接抽取。
        PartyTemplateObject? template = TryFindPatrolTemplate(settlement, out var templateId);
        if (template == null)
            Logger.Info($"PatrolDispatcher: '{settlement.Name}' PatrolPartyTemplate 未找到（候选 stringId 全 miss）— 沿用 null 模板继续创建 ST 巡逻队");

        MobileParty? created;
        try
        {
            created = StPatrolPartyComponent.CreateForTown(settlement, template);
        }
        catch (Exception createEx)
        {
            Logger.Error($"PatrolDispatcher: StPatrolPartyComponent.CreateForTown threw (template='{templateId}') for '{settlement.Name}'", createEx);
            return false;
        }

        if (created == null)
        {
            Logger.Warn($"PatrolDispatcher: StPatrolPartyComponent.CreateForTown returned null for '{settlement.Name}' (template='{templateId}')");
            return false;
        }

        // 从 garrison 抽兵（skip heroes）
        var gRoster = garrison?.MemberRoster;
        var pRoster = created.MemberRoster;
        int moved = 0;
        if (gRoster != null && pRoster != null)
        {
            moved = TroopTransferHelper.TransferFromGarrison(
                gRoster, pRoster, troopCount, TroopTransferHelper.SortStrategy.LowestTierFirst);
        }
        Logger.Debug($"[DIAG] PatrolDispatcher '{settlement.Name}' transfer: requested={troopCount}, gRoster?={gRoster != null}, pRoster?={pRoster != null}, moved={moved}, garrison-after-transfer={gRoster?.TotalManCount ?? -1}");

        if (moved <= 0)
        {
            Logger.Warn($"PatrolDispatcher: '{settlement.Name}' created patrol but moved 0 troops; destroying empty patrol");
            PartyMergeService.Instance.DestroyAndUntrack(created, "PatrolDispatcher empty patrol rollback", deferIfInMapEvent: false);
            return false;
        }

        // 兵员注入完成 → 快照出发兵员数（基类的 ShouldReturnAndDisband 判定要用）
        if (created.PartyComponent is StPatrolPartyComponent stc)
        {
            stc.SnapshotInitialMembers(created);

            // T1 重整 (doc §20 #1)：统一走基类 helper 处理"扣款 + 注资 + 买粮"。
            // 玩家路径扣款失败 → 把兵还 garrison 并销毁 party；AI 路径不会失败。
            if (!StPartyComponent.TrySeedAndBuyInitialFood(
                stc, created, settlement,
                ExpenseCategory.PatrolSeed,
                settlement.OwnerClan,
                $"patrol_seed home={settlement.StringId}"))
            {
                TroopTransferHelper.TransferBackToGarrison(created.MemberRoster, garrison!.MemberRoster);
                PartyMergeService.Instance.DestroyAndUntrack(created, "PatrolDispatcher seed failed rollback", deferIfInMapEvent: false);
                return false;
            }
        }

        _lifecycle.RegisterTrackedParty(created, settlement, PartyLifecycleManager.KindPatrol);

        // B7.26：新巡逻队的首站走 scheduler。
        // 2026-05-18 产品语义：巡逻队不允许把 home 当巡逻站（ClanPatrolScheduler 已 filter 排除）。
        // 若 PickNextStop 返 null（clan 只有 home 一个 settlement，或所有非-home settlement 全被
        // 过滤）→ 巡逻队无处可去 → 把刚抽出的兵员还回 garrison 并销毁实例，避免在 home 旁傻站浪费。
        bool firstHopAssigned = false;
        try
        {
            var schedulerCapitalMgr = _capitalRegistry?.GetForSettlement(settlement);
            if (schedulerCapitalMgr != null)
            {
                schedulerCapitalMgr.PatrolScheduler.RecordVisit(settlement);  // 标记首府刚访问
                var nextStop = schedulerCapitalMgr.PatrolScheduler.PickNextStop(created);
                if (nextStop != null)
                {
                    bool navOk = false;
                    try
                    {
                        created.SetMoveGoToSettlement(nextStop, MobileParty.NavigationType.Default, false);
                        navOk = true;
                    }
                    catch (Exception navEx) { Logger.Error($"first-hop SetMoveGoToSettlement failed for '{created.Name}' -> '{nextStop.Name}'", navEx); }
                    if (navOk)
                    {
                        Logger.Info($"PatrolDispatcher: '{created.Name}' first hop -> '{nextStop.Name}'");
                        firstHopAssigned = true;
                    }
                }
                else
                {
                    Logger.Warn($"PatrolDispatcher: '{created.Name}' no non-home candidate at create-time — returning {moved} troops and destroying empty patrol");
                    PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(created, settlement, "PatrolDispatcher empty patrol rollback (no candidate)");
                    PartyMergeService.Instance.DisbandAndUntrack(created, "PatrolDispatcher first-hop no candidate");
                    return false;
                }
            }
            else
            {
                Logger.Warn($"PatrolDispatcher: no capital manager for '{settlement.Name}' — rolling back patrol");
            }
        }
        catch (Exception schedEx)
        {
            Logger.Error("PatrolDispatcher: scheduler first-hop assignment failed", schedEx);
        }
        if (!firstHopAssigned)
        {
            Logger.Warn($"PatrolDispatcher: '{created.Name}' has no first hop — returning {moved} troops and destroying patrol");
            PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(created, settlement, "PatrolDispatcher first-hop failed rollback");
            PartyMergeService.Instance.DisbandAndUntrack(created, "PatrolDispatcher first-hop failed");
            return false;
        }

        DecisionAuditLogger.LogRule(
            decisionType: "create_patrol_party",
            inputSummary: $"home={settlement.StringId} garrison={garrisonCount} template={templateId} moved={moved}",
            decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{created.StringId}\",\"template\":\"{templateId}\",\"moved\":{moved}}}",
            accepted: true);
        Logger.Info($"PatrolDispatcher: created ST patrol '{created.StringId}' for '{settlement.Name}' (template='{templateId}', moved={moved} troops)");
        return true;
    }

    /// <summary>
    /// MergedOnly 口径下首府的巡逻 sink 容量(头数)= (哨所派生的巡逻队上限 − 当前活跃巡逻队数)
    /// × PatrolTargetSize。<see cref="CapitalLogisticsManager"/> 取此值传入 <see cref="UnifiedGarrisonSolver"/>。
    /// 非城镇 / 围城 / 已满 / 出错 → 0(不建巡逻 sink)。
    /// </summary>
    public int PatrolHeadroomHeads(Settlement capital)
    {
        try
        {
            if (capital?.Town == null || !capital.IsTown) return 0;
            if (capital.Town.IsUnderSiege) return 0;
            int cap = _lifecycle.GetCapFor(capital, PartyLifecycleManager.KindPatrol);
            int free = cap - CountExistingPatrolsAtHome(capital);
            if (free <= 0) return 0;
            int size = ConfigurationManager.Current?.Thresholds?.PatrolTargetSize ?? 50;
            return size <= 0 ? 0 : free * size;
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolDispatcher.PatrolHeadroomHeads failed for '{PartyNameFormatter.SafeName(capital)}'", ex);
            return 0;
        }
    }

    /// <summary>
    /// 由 CapitalLogisticsManager 在 MergedOnly 派发路径调用:消费 MCMF 决定的巡逻总头数,
    /// 在首府创建一支巡逻队。headcount 已是 MCMF 解出的真·盈余兵 —— 不再叠加比例 / 留守启发式;
    /// 仍受 PatrolMinDispatchSize 地板(避免微型巡逻队)与 KindPatrol 队伍上限约束。
    /// </summary>
    public bool TryDispatchPatrol(Town capitalTown, int headcount)
    {
        try
        {
            if (capitalTown?.Settlement == null || headcount <= 0) return false;
            var settlement = capitalTown.Settlement;

            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
            {
                Logger.Debug($"  PatrolDispatcher: TryDispatchPatrol skipped '{settlement.Name}' — AutoPatrol disabled");
                return false;
            }
            if (capitalTown.IsUnderSiege)
            {
                Logger.Info($"  PatrolDispatcher: '{settlement.Name}' is under siege — skip MCMF patrol dispatch");
                return false;
            }
            int minDispatch = PatrolMinDispatchSize;
            if (minDispatch > 0 && headcount < minDispatch)
            {
                Logger.Debug($"  PatrolDispatcher: '{settlement.Name}' MCMF headcount={headcount} < PatrolMinDispatchSize={minDispatch} — defer");
                return false;
            }

            int targetSize = ConfigurationManager.Current?.Thresholds?.PatrolTargetSize ?? 50;
            if (targetSize <= 0) targetSize = 50;

            // headcount = MCMF 解出的巡逻总头数;按 PatrolTargetSize 切成多支队伍,受 KindPatrol
            // 队伍上限约束。末尾不足 PatrolMinDispatchSize 的余量留到下个 tick 再解。
            int remaining = headcount;
            int dispatched = 0;
            while (remaining > 0 && _lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindPatrol))
            {
                int chunk = Math.Min(remaining, targetSize);
                if (minDispatch > 0 && chunk < minDispatch) break;
                if (!CreateOnePatrol(settlement, chunk)) break;
                remaining -= chunk;
                dispatched++;
            }
            if (dispatched == 0)
                Logger.Info($"  PatrolDispatcher: '{settlement.Name}' MCMF headcount={headcount} — 0 patrol(s) dispatched (cap reached / create failed)");
            return dispatched > 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolDispatcher.TryDispatchPatrol failed for '{PartyNameFormatter.SafeName(capitalTown?.Settlement)}'", ex);
            return false;
        }
    }

    /// <summary>
    /// B16.4：统计某 settlement 当前活跃的 ST 巡逻队总数（不再纳入 vanilla auto-spawn 的 PatrolPartyComponent）。
    /// 改为遍历 <see cref="MobileParty.AllCustomParties"/> + <c>is StPatrolPartyComponent</c> 过滤。
    /// </summary>
    private static int CountExistingPatrolsAtHome(Settlement settlement)
    {
        try
        {
            int count = 0;
            var all = MobileParty.AllCustomParties;
            if (all == null) return 0;
            var ownerClan = settlement.OwnerClan;
            foreach (var p in all)
            {
                if (p == null || !p.IsActive) continue;
                if (ownerClan != null && p.ActualClan != null && p.ActualClan != ownerClan) continue;
                // R6/R5: 用 HomeSettlementOrNull 防止损坏存档下 HomeSettlement getter 抛出 → 整段
                // 早年版本被 catch 后返回 int.MaxValue 永久阻塞巡逻队创建（fail-deadly）。
                if (p.PartyComponent is StPatrolPartyComponent stc && stc.HomeSettlementOrNull == settlement) count++;
            }
            return count;
        }
        catch (Exception ex)
        {
            // R5 (DeepSeek audit 2026-05-18)：原实现注释自称 fail-safe 但返回 int.MaxValue =
            // existing >= cap 永远 true → 该城**永久**无法再创建巡逻队，无恢复路径。改为 0
            // (允许下次再试)，宁可多创建一支也不要永久关闭通道。
            Logger.Error($"CountExistingPatrolsAtHome failed for '{PartyNameFormatter.SafeName(settlement)}' — returning 0 to keep creation channel open", ex);
            return 0;
        }
    }

    /// <summary>
    /// 尝试解析一个可用的 PartyTemplateObject。B7.21 修：vanilla v1.3.15 的实际 stringId 是
    /// <c>settlement_patrol_template_level_{1,2,3}</c> 和 <c>settlement_patrol_template_coastal</c>，
    /// 之前用的旧名 (empire_patrol_party 等) 全 miss → 每秒 NRE。
    ///
    /// 按 settlement 的兵营建筑等级选模板：lvl 0/1 → level_1，lvl 2 → level_2，lvl 3 → level_3。
    /// 找不到逐级降级，最终全 miss 返回 null。
    /// </summary>
    private static PartyTemplateObject? TryFindPatrolTemplate(Settlement? settlement, out string idUsed)
    {
        // 巡逻模板等级 ← 哨所(Guard House)等级。读不到建筑按 0 级,下面 clamp 到 [1,3]。
        int guardHouseLevel = BuildingLevelReader.GetLevel(settlement, StBuilding.GuardHouse);
        if (guardHouseLevel < 1) guardHouseLevel = 1;
        if (guardHouseLevel > 3) guardHouseLevel = 3;

        // 优先匹配同等级，找不到逐级降级，最后试 coastal 兜底
        var candidates = new System.Collections.Generic.List<string>();
        for (int lvl = guardHouseLevel; lvl >= 1; lvl--)
        {
            candidates.Add($"settlement_patrol_template_level_{lvl}");
        }
        candidates.Add("settlement_patrol_template_coastal");

        foreach (var id in candidates)
        {
            try
            {
                var t = MBObjectManager.Instance?.GetObject<PartyTemplateObject>(id);
                if (t != null)
                {
                    idUsed = id;
                    return t;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"TryFindPatrolTemplate: GetObject('{id}') threw: {ex.Message}");
            }
        }

        idUsed = "<null>";
        return null;
    }
}
