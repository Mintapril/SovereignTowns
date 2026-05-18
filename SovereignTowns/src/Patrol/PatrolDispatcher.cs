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
            // B17.4 S1：围城下不派巡逻队（出门即冲撞围攻军）。
            if (settlement?.Town?.IsUnderSiege == true)
            {
                Logger.Debug($"PatrolDispatcher: '{settlement.Name}' is under siege — skip patrol creation");
                return;
            }

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
            Logger.Info($"[DIAG] PatrolDispatcher.TryCreate '{settlement.Name}' garrison={garrisonCount} ratio={PatrolTroopBatchRatio:F2} → batch={batchSize}");
            if (batchSize <= 0)
            {
                Logger.Info($"[DIAG] PatrolDispatcher: '{settlement.Name}' garrison={garrisonCount}, patrol batch computed 0, defer patrol creation");
                return;
            }

            int reserveAfterCreation = GarrisonThresholdMath.CountFromRatio(garrisonCount, PatrolReserveAfterCreationRatio, minimumWhenPositive: 0);
            Logger.Info($"[DIAG] PatrolDispatcher.TryCreate '{settlement.Name}' reserveAfterCreation={reserveAfterCreation} (ratio={PatrolReserveAfterCreationRatio:P0}) garrison-batch={garrisonCount - batchSize}");
            if (garrisonCount - batchSize < reserveAfterCreation)
            {
                Logger.Info($"[DIAG] PatrolDispatcher: '{settlement.Name}' garrison={garrisonCount}, batch={batchSize}, reserve={reserveAfterCreation} — defer (garrison-batch < reserve)");
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
            Logger.Info($"[DIAG] PatrolDispatcher '{settlement.Name}' transfer: requested={batchSize}, gRoster?={gRoster != null}, pRoster?={pRoster != null}, moved={moved}, garrison-after-transfer={gRoster?.TotalManCount ?? -1}");

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

                // 2026-05-18：巡逻队自负盈亏经济模型 — 出发时注入 2000d 队伍资金，立刻在首府市场买 3 天食物。
                //
                // 玩家氏族 vs AI 氏族走不同路径，与 RecruitmentDispatcher / SallyDispatcher 惯例对称：
                //   - 玩家氏族（ShouldChargeClan=true）：扣款走 ModTreasury.Charge，受 PauseSpendingWhenBroke
                //     门控、写 ledger + audit；扣款失败时把已抽兵员还回 garrison 并销毁实例（回滚）。
                //   - AI 氏族（ShouldChargeClan=false）：仍走 InitTeamFundsFromHomeOwner 从 AI 领主 hero.Gold
                //     扣（vanilla 路径，不经 ModTreasury）。
                const int patrolSeedGold = 2000;
                bool shouldChargePatrol = CapitalRegistry.ShouldChargeClan(settlement.OwnerClan);
                if (shouldChargePatrol)
                {
                    // 玩家路径：先预检，再扣款，成功后直接把资金写入队伍（不从 hero.Gold 扣第二次）。
                    if (!ModTreasury.CanAfford(patrolSeedGold))
                    {
                        Logger.Info($"PatrolDispatcher: '{settlement.Name}' 玩家金币不足 (need {patrolSeedGold})，回滚巡逻队");
                        TroopTransferHelper.TransferBackToGarrison(created.MemberRoster, garrison!.MemberRoster);
                        PartyMergeService.Instance.DestroyAndUntrack(created, "PatrolDispatcher insufficient funds rollback", deferIfInMapEvent: false);
                        return;
                    }
                    if (!ModTreasury.Charge(ExpenseCategory.PatrolSeed, patrolSeedGold, $"patrol_seed home={settlement.StringId}"))
                    {
                        Logger.Info($"PatrolDispatcher: '{settlement.Name}' ModTreasury.Charge 拒绝，回滚巡逻队");
                        TroopTransferHelper.TransferBackToGarrison(created.MemberRoster, garrison!.MemberRoster);
                        PartyMergeService.Instance.DestroyAndUntrack(created, "PatrolDispatcher Charge rejected rollback", deferIfInMapEvent: false);
                        return;
                    }
                    // 扣款成功 → 把 2000d 写入队伍资金（队伍在路上用它买粮买装）
                    stc.SetTeamFunds(patrolSeedGold);
                }
                else
                {
                    // AI 路径：从 AI 领主 hero.Gold 扣（vanilla 路径，不受 PauseSpendingWhenBroke 影响）。
                    stc.InitTeamFundsFromHomeOwner(created, patrolSeedGold);
                }

                stc.BuyFoodAtSettlement(created, settlement, 3f);
            }

            _lifecycle.RegisterTrackedParty(created, settlement, PartyLifecycleManager.KindPatrol);

            DecisionAuditLogger.LogRule(
                decisionType: "create_patrol_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} template={templateId} moved={moved}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{created.StringId}\",\"template\":\"{templateId}\",\"moved\":{moved}}}",
                accepted: true);
            Logger.Info($"PatrolDispatcher: created ST patrol '{created.StringId}' for '{settlement.Name}' (template='{templateId}', moved={moved} troops)");

            // B7.26：新巡逻队的首站走 scheduler。
            // 2026-05-18 产品语义：巡逻队不允许把 home 当巡逻站（ClanPatrolScheduler 已 filter 排除）。
            // 若 PickNextStop 返 null（clan 只有 home 一个 settlement，或所有非-home settlement 全被
            // 过滤）→ 巡逻队无处可去 → 把刚抽出的兵员还回 garrison 并销毁实例，避免在 home 旁傻站浪费。
            try
            {
                var schedulerCapitalMgr = _capitalRegistry?.GetForSettlement(settlement);
                if (schedulerCapitalMgr != null)
                {
                    schedulerCapitalMgr.PatrolScheduler.RecordVisit(settlement);  // 标记首府刚访问
                    var nextStop = schedulerCapitalMgr.PatrolScheduler.PickNextStop(created);
                    if (nextStop != null)
                    {
                        try { created.SetMoveGoToSettlement(nextStop, MobileParty.NavigationType.Default, false); }
                        catch (Exception navEx) { Logger.Error($"first-hop SetMoveGoToSettlement failed for '{created.Name}' -> '{nextStop.Name}'", navEx); }
                        Logger.Info($"PatrolDispatcher: '{created.Name}' first hop -> '{nextStop.Name}'");
                    }
                    else
                    {
                        Logger.Warn($"PatrolDispatcher: '{created.Name}' no non-home candidate at create-time — returning {moved} troops and destroying empty patrol");
                        PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(created, settlement, "PatrolDispatcher empty patrol rollback (no candidate)");
                        PartyMergeService.Instance.DisbandAndUntrack(created, "PatrolDispatcher first-hop no candidate");
                        return;
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
