using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
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
///   - 派遣集中在该 clan 的首府（OnHourlyTickSettlement 只在 settlement == 首府时创建新队）；
///   - 路径选择委托给该 clan 的 <see cref="ClanPatrolScheduler"/>（全氏族范围、最久未访问 + 距离评分、多队预占互补）。
///
/// 与 vanilla 共存：ST 创建的 <see cref="StPatrolPartyComponent"/> 不进 MobileParty.AllPatrolParties；
/// vanilla 自动 spawn 的巡逻队继续以 PatrolPartyComponent 形式存在（共存策略，不互相干涉）。
/// </summary>
public sealed class PatrolDispatcher
{
    // 阈值改读 ConfigurationManager.Current.Thresholds；人数阈值由实际驻军比例派生。
    // 通过封装属性 + 兜底默认值访问，避免 config 加载失败时 NRE。
    private static float PatrolReserveAfterCreationRatio
        => ConfigurationManager.Current?.Thresholds?.PatrolReserveAfterCreationRatio ?? 0.8f;
    private static float PatrolTroopBatchRatio
        => ConfigurationManager.Current?.Thresholds?.PatrolTroopBatchRatio ?? 0.10f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public PatrolDispatcher(
        PartyLifecycleManager lifecycle,
        CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }

    /// <summary>
    /// HourlyTickSettlementEvent 转发：仅在首府上派遣新巡逻队，巡逻范围为全氏族（B7.26）。
    /// </summary>
    public void OnHourlyTickSettlement(Settlement settlement)
    {
        if (settlement == null) return;
        if (!settlement.IsTown) return;

        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
                return;

            // B7.26：派遣集中在首府 — 只在 settlement == 首府时评估
            var capitalMgr = _capitalRegistry?.GetForSettlement(settlement);
            if (capitalMgr == null) return;  // 该 settlement 不属任何受管 clan
            var capital = _capitalRegistry?.GetCapitalForClan(capitalMgr.OwnerClan);
            if (capital == null || settlement != capital) return;  // 不是该 clan 的首府 → 跳过

            TryCreatePatrolParty(settlement);
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolDispatcher.OnHourlyTickSettlement failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    // ────────── 创建巡逻队 ──────────

    private void TryCreatePatrolParty(Settlement settlement)
    {
        try
        {
            // B7.16：cap 来自 town 的兵营建筑（settlement_garrison）等级 + 1。
            // 统计该 settlement 的 ST 巡逻队总数；只有 < cap 才允许再创建。
            // B16.4：vanilla auto-spawn 的 PatrolPartyComponent 不再纳入计数 — 与我们独立共存。
            int cap = _lifecycle.GetCapFor(settlement, PartyLifecycleManager.KindPatrol);
            int existing = CountExistingPatrolsAtHome(settlement);
            if (existing >= cap)
            {
                Logger.Debug($"PatrolDispatcher: '{settlement.Name}' st-patrols={existing}/{cap} (cap from barracks lvl) — skip");
                return;
            }

            var town = settlement.Town;
            var garrison = town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;
            int batchSize = GarrisonThresholdMath.CountFromRatio(garrisonCount, PatrolTroopBatchRatio, minimumWhenPositive: 1);
            if (batchSize <= 0)
            {
                Logger.Debug($"PatrolDispatcher: '{settlement.Name}' garrison={garrisonCount}, patrol batch computed 0, defer patrol creation");
                return;
            }

            int reserveAfterCreation = GarrisonThresholdMath.CountFromRatio(garrisonCount, PatrolReserveAfterCreationRatio, minimumWhenPositive: 0);
            if (garrisonCount - batchSize < reserveAfterCreation)
            {
                Logger.Debug($"PatrolDispatcher: '{settlement.Name}' garrison={garrisonCount}, batch={batchSize}, reserve={reserveAfterCreation} (ratio {PatrolReserveAfterCreationRatio:P0}), defer patrol creation");
                return;
            }

            // 模板：仅作语义参考（按文化挑兵种），B16.4 起本 dispatcher 不再依赖 vanilla CreatePatrolParty 自动注兵。
            // 找不到模板仍按 null 创建，兵员从 garrison 直接抽取。
            PartyTemplateObject? template = TryFindPatrolTemplate(settlement, out var templateId);
            if (template == null)
            {
                Logger.Info($"PatrolDispatcher: '{settlement.Name}' PatrolPartyTemplate 未找到（候选 stringId 全 miss）— 沿用 null 模板继续创建 ST 巡逻队");
            }

            MobileParty? created = null;
            try
            {
                created = StPatrolPartyComponent.CreateForTown(settlement, template);
            }
            catch (Exception createEx)
            {
                Logger.Error($"PatrolDispatcher: StPatrolPartyComponent.CreateForTown threw (template='{templateId}') for '{settlement.Name}'", createEx);
                return;
            }

            if (created == null)
            {
                Logger.Warn($"PatrolDispatcher: StPatrolPartyComponent.CreateForTown returned null for '{settlement.Name}' (template='{templateId}')");
                return;
            }

            // 从 garrison 按比例抽兵（skip heroes）
            var gRoster = garrison?.MemberRoster;
            var pRoster = created.MemberRoster;
            int moved = 0;
            if (gRoster != null && pRoster != null)
            {
                moved = TroopTransferHelper.TransferFromGarrison(
                    gRoster, pRoster, batchSize, TroopTransferHelper.SortStrategy.LowestTierFirst);
            }

            if (moved <= 0)
            {
                Logger.Warn($"PatrolDispatcher: '{settlement.Name}' created patrol but moved 0 troops; destroying empty patrol");
                PartyMergeService.Instance.DestroyAndUntrack(created, "PatrolDispatcher empty patrol rollback", deferIfInMapEvent: false);
                return;
            }

            // 兵员注入完成 → 快照出发兵员数（基类的 ShouldReturnAndDisband 判定要用）
            if (created.PartyComponent is StPatrolPartyComponent stc)
            {
                stc.SnapshotInitialMembers(created);
            }

            _lifecycle.RegisterTrackedParty(created, settlement, PartyLifecycleManager.KindPatrol);

            DecisionAuditLogger.LogRule(
                decisionType: "create_patrol_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} template={templateId} moved={moved}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{created.StringId}\",\"template\":\"{templateId}\",\"moved\":{moved}}}",
                accepted: true);
            Logger.Info($"PatrolDispatcher: created ST patrol '{created.StringId}' for '{settlement.Name}' (template='{templateId}', moved={moved} troops)");

            // B7.26：新巡逻队的首站走 scheduler
            try
            {
                var schedulerCapitalMgr = _capitalRegistry?.GetForSettlement(settlement);
                if (schedulerCapitalMgr != null)
                {
                    schedulerCapitalMgr.PatrolScheduler.RecordVisit(settlement);  // 标记首府刚访问，避免立刻回家
                    var nextStop = schedulerCapitalMgr.PatrolScheduler.PickNextStop(created);
                    if (nextStop != null)
                    {
                        try { created.SetMoveGoToSettlement(nextStop, MobileParty.NavigationType.Default, false); }
                        catch (Exception navEx) { Logger.Error($"first-hop SetMoveGoToSettlement failed for '{created.Name}' -> '{nextStop.Name}'", navEx); }
                        Logger.Info($"PatrolDispatcher: '{created.Name}' first hop -> '{nextStop.Name}'");
                    }
                }
            }
            catch (Exception schedEx)
            {
                Logger.Error("PatrolDispatcher: scheduler first-hop assignment failed (party will idle until next tick)", schedEx);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolDispatcher.TryCreatePatrolParty failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
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
                if (p.PartyComponent is StPatrolPartyComponent stc && stc.HomeSettlement == settlement) count++;
            }
            return count;
        }
        catch (Exception ex)
        {
            Logger.Error($"CountExistingPatrolsAtHome failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
            return int.MaxValue; // 失败时报最大值，等同于"已超 cap"，停止创建（fail-safe）
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
        int barracksLevel = 0;
        try
        {
            var town = settlement?.Town;
            if (town?.Buildings != null)
            {
                foreach (var b in town.Buildings)
                {
                    if (b?.BuildingType == null) continue;
                    string id;
                    try { id = b.BuildingType.StringId ?? ""; } catch { continue; }
                    if (string.Equals(id, "settlement_garrison", StringComparison.Ordinal))
                    {
                        try { barracksLevel = b.CurrentLevel; } catch { barracksLevel = 0; }
                        break;
                    }
                }
            }
        }
        catch { /* 任何异常按 lvl 0 处理 */ }

        if (barracksLevel < 1) barracksLevel = 1;
        if (barracksLevel > 3) barracksLevel = 3;

        // 优先匹配同等级，找不到逐级降级，最后试 coastal 兜底
        var candidates = new System.Collections.Generic.List<string>();
        for (int lvl = barracksLevel; lvl >= 1; lvl--)
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
            catch
            {
                // 单个候选失败：忽略，继续下一个
            }
        }

        idUsed = "<null>";
        return null;
    }
}
