using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.SallyForth;

/// <summary>
/// MVP 5：监管受管氏族自有 town 的"主动出击队"（<see cref="SallyForthPartyComponent"/>）。
///
/// 设计原则（仿 GDS，但避开 Harmony，并改进 GDS 的回程 bug）：
///   - 仅触碰 CapitalRegistry 接管且通过 PartyLifecycleManager 注册 kind="sallyforth" 的队伍；
///   - 所有方法 try-catch，绝不向调用方抛异常；
///   - 在 SettlementHourlyTick 评估是否应出击 + 选目标 + 创建出击队；
///   - 在 PartyHourlyTick 处理"在外 → 撤退 / 超时 → 回家 / 到家 → 转兵+销毁"状态机；
///   - 在 <c>MapEventEnded</c> 上精确捕捉战后 sally party，立即下达回程指令（vs GDS 靠 TickEvent 轮询，已知 bug 来源）。
///
/// 与 PatrolManager 并行：B7.27 之后两者可以同时启用（巡逻队会评估"能否在战斗结束前抵达"赶去支援）。
/// </summary>
public sealed class SallyForthManager
{
    // === 触发参数（合理默认；未来挂控制面板可改） ===
    // 2026-05-12 审查 B2 调整：15f → 50f 与 GDS 默认 AttackDistance 对齐，
    // 否则 FindBestEnemyTarget 在大多数场景下返回 null，sortie 不会触发。
    private const float DetectionRadius      = 50f;   // 检测半径（地图单位）
    private const float MaxSallyHours        = 12f;   // 12 小时未归 → 强制回程
    private const int   InitialSallyGold     = 100;

    private static float SallyExtractionRatio
        => ConfigurationManager.Current?.Thresholds?.SallyExtractionRatio ?? 0.60f;
    private static float SallyTargetPartySizeMultiplier
        => ConfigurationManager.Current?.Thresholds?.SallyTargetPartySizeMultiplier ?? 2.0f;
    private static int SallyCreateMinPartyCount
        => ConfigurationManager.Current?.Thresholds?.SallyCreateMinPartyCount ?? 30;

    // B7.22：出击节奏控制（避免一进检测圈就冲、刚回又冲）
    private const float SallyCooldownHours    = 24f; // 上次出击结束后冷却小时数
    private const int   MinSustainedTicks     = 3;   // 敌人需在视野内连续 N 个 hourly tick 才触发出击

    /// <summary>每城上次出击结束（成功合并 / EmergencyCleanup / 销毁）的时间戳；冷却用。</summary>
    private readonly Dictionary<Settlement, CampaignTime> _lastSallyEndedAt = new();

    /// <summary>每城连续看到敌方威胁的 tick 计数；用于持续可见性判定。无敌人即清零。</summary>
    private readonly Dictionary<Settlement, int> _enemySustainedTicks = new();

    /// <summary>已 log 过 force-return 的 sally party 集合；避免每 tick 重复 Warn。party 销毁时移除。</summary>
    private readonly HashSet<MobileParty> _forceReturnLogged = new();

    /// <summary>已 log 过 target-lost 的 sally party 集合；避免每 tick 重复 Info。party 销毁/目标恢复时移除。</summary>
    private readonly HashSet<MobileParty> _targetLostLogged = new();

    private readonly PartyLifecycleManager _lifecycle;
    private readonly PartyMergeService _mergeService;
    private readonly CapitalRegistry? _capitalRegistry;
    private readonly BattleLootManager? _battleLootManager;

    public SallyForthManager(
        PartyLifecycleManager lifecycle,
        CapitalRegistry? capitalRegistry = null,
        BattleLootManager? battleLootManager = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _mergeService = new PartyMergeService(_lifecycle);
        _capitalRegistry = capitalRegistry;
        _battleLootManager = battleLootManager;
    }

    // ────────── Settlement 小时 tick：评估并创建 ──────────

    public void OnHourlyTickSettlement(Settlement settlement)
    {
        // 用户原话："无巡逻队模式时每个城镇都会出城攻击" — 仅 town，不含 castle。
        // B7.15 Phase C：拓宽到所有 registry 管理的 clan（player + 受管 AI）。
        if (settlement == null) return;
        if (!settlement.IsTown) return;

        try
        {
            // 所有可能抛异常的 registry / config 访问都纳入 try，避免逃逸到 vanilla
            // HourlyTickSettlementEvent 链（违反硬不变量 #5）。
            var owningMgr = _capitalRegistry?.GetForSettlement(settlement);
            if (owningMgr is null) return;
            var usableCapital = _capitalRegistry?.GetCapitalForClan(owningMgr.OwnerClan);
            if (usableCapital == null) return;

            if (!ConfigurationManager.Current.EnabledFeatures.SallyForth) return;

            // 系统开关：未设首府 → 系统关闭（该 clan 无 town）
            if (usableCapital.Town == null) return;

            if (settlement.IsUnderSiege) return;

            // 上限：每城最多 1 支 sally
            if (!_lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindSallyForth)) return;

            // 驻军门槛均按实际驻军比例派生，不含民兵。
            var garrison = settlement.Town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;

            // 找附近敌方 party（评分：选最弱者）
            var target = FindBestEnemyTarget(settlement);
            if (target == null)
            {
                // 视野空 → 清零持续计数
                _enemySustainedTicks.Remove(settlement);
                return;
            }

            // B7.22：出击冷却 — 上次出击结束后 24h 内不再出
            if (_lastSallyEndedAt.TryGetValue(settlement, out var lastEnd))
            {
                var hoursSinceLast = (CampaignTime.Now - lastEnd).ToHours;
                if (hoursSinceLast < SallyCooldownHours)
                {
                    Logger.Debug($"SallyForth '{PartyNameFormatter.SafeName(settlement)}': 冷却中 ({hoursSinceLast:F1}h < {SallyCooldownHours}h)，跳过");
                    return;
                }
            }

            // B7.22：持续可见性 — 敌人需在视野内连续 N 个 hourly tick 才触发，避免见即冲
            int prevTicks = _enemySustainedTicks.TryGetValue(settlement, out var p) ? p : 0;
            int newTicks = prevTicks + 1;
            _enemySustainedTicks[settlement] = newTicks;
            if (newTicks < MinSustainedTicks)
            {
                Logger.Debug($"SallyForth '{PartyNameFormatter.SafeName(settlement)}': 敌方 '{PartyNameFormatter.SafeName(target)}' 已见 {newTicks}/{MinSustainedTicks} 小时，等持续可见后才出击");
                return;
            }

            TryCreateSallyParty(settlement, garrison!, garrisonCount, target);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.OnHourlyTickSettlement failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    // ────────── 查询接口 ──────────

    /// <summary>
    /// B7.27：返回该氏族当前正在 MapEvent 中战斗的 sally 队列表。
    /// 供 PatrolManager 评估是否需要派 patrol 赶去支援。
    /// </summary>
    public List<MobileParty> GetActiveCombatSallyParties(Clan clan)
    {
        var result = new List<MobileParty>();
        if (clan == null) return result;
        try
        {
            foreach (var party in MobileParty.AllCustomParties)
            {
                if (party == null || !party.IsActive) continue;
                if (party.PartyComponent is not SallyForthPartyComponent sc) continue;
                if (sc.HomeSettlement?.OwnerClan != clan) continue;
                if (party.MapEvent == null) continue;  // 不在战斗中
                result.Add(party);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetActiveCombatSallyParties failed", ex);
        }
        return result;
    }

    // ────────── Party 小时 tick：状态机 ──────────

    public void OnHourlyTickParty(MobileParty party)
    {
        if (party == null) return;
        var sp = party.PartyComponent as SallyForthPartyComponent;
        if (sp == null) return;

        try
        {
            if (!party.IsActive) return;

            var home = sp.HomeSettlement;
            if (home == null)
            {
                // 异常 sally：无家可归 → 直接销毁（先尝试转兵到当前首府）
                Logger.Warn($"SallyForthManager: '{PartyNameFormatter.SafeName(party)}' has null HomeSettlement, orphan cleanup");
                EmergencyCleanup(party);
                return;
            }
            // 2026-05-12 审查 D-I3 修复：失守时不再静默 return 留下孤儿；改为紧急清理。
            // B7.28：归属必须看 party.ActualClan；home 易主后 home.OwnerClan 已是新主人。
            var partyClan = party.ActualClan ?? home.OwnerClan;
            if (_capitalRegistry == null
                || partyClan == null
                || !_capitalRegistry.IsManagedClanWithCapital(partyClan)
                || home.OwnerClan != partyClan)
            {
                Logger.Warn($"SallyForthManager: '{PartyNameFormatter.SafeName(party)}' home '{home.Name}' lost (owner={home.OwnerClan?.Name?.ToString() ?? "none"}), emergency cleanup");
                EmergencyCleanup(party);
                return;
            }

            // 1) 已到家 → 转兵 + 销毁。刚创建/刚出门时 LastVisitedSettlement 可能仍是 home，
            // 需确认当前确实在 home，或 AI 目标已经是 home。
            if (party.CurrentSettlement == home
                || (party.LastVisitedSettlement == home && party.TargetSettlement == home))
            {
                TransferAndDestroy(party, home);
                return;
            }

            // 2) 超时 → 强制回家
            var hoursAway = (CampaignTime.Now - sp.DepartureTime).ToHours;
            if (hoursAway > MaxSallyHours)
            {
                // B7.22 Fix B：状态首次切换才 Warn；后续 tick 用 Debug 避免每秒一行刷屏
                if (!_forceReturnLogged.Contains(party))
                {
                    Logger.Warn($"SallyForthManager: '{PartyNameFormatter.SafeName(party)}' away {hoursAway:F1}h > {MaxSallyHours}h, force return to '{home.Name}'");
                    _forceReturnLogged.Add(party);
                }
                ReleaseAiAndReturnHome(party, home);
                return;
            }

            // 3) B5 F2: target 进入 settlement → 追击队卡在外面（vanilla 已知 bug），立即回家
            //    否则 target 已死亡/失效 → 释放 vanilla AI 接管
            //    否则继续 engage（创建时已 SetMoveEngageParty + SetDoNotMakeNewDecisions）
            var target = sp.TargetParty;
            if (target != null && target.IsActive && target.CurrentSettlement != null)
            {
                Logger.Info($"SallyForthManager: '{PartyNameFormatter.SafeName(party)}' target '{PartyNameFormatter.SafeName(target)}' entered '{target.CurrentSettlement.Name}', returning home '{home.Name}'");
                ReleaseAiAndReturnHome(party, home);
                return;
            }
            if (target == null || !target.IsActive)
            {
                // B7.22 Fix B：状态首次切换才 Info；后续 tick 用 Debug
                if (!_targetLostLogged.Contains(party))
                {
                    Logger.Info($"SallyForthManager: '{PartyNameFormatter.SafeName(party)}' target lost, releasing AI for re-decision");
                    _targetLostLogged.Add(party);
                }
                try { party.Ai?.SetDoNotMakeNewDecisions(false); }
                catch (Exception aiEx) { Logger.Error("SetDoNotMakeNewDecisions(false) failed", aiEx); }
            }
            else
            {
                // 目标恢复 → 清状态，下次丢失时重新 log
                _targetLostLogged.Remove(party);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.OnHourlyTickParty failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    // ────────── MapEventEnded：战后回程（核心改进 vs GDS） ──────────

    /// <summary>
    /// 战斗结束回调（vanilla <c>CampaignEvents.MapEventEnded</c>，签名 <c>Action&lt;MapEvent&gt;</c>）。
    /// 找出参战的自家 sally party，立即下达"回家"指令。
    /// 注意：MapEventEnded 触发时，败方 party 可能已被销毁，需 IsActive 防御。
    /// </summary>
    public void OnMapEventEnded(MapEvent mapEvent)
    {
        if (mapEvent == null) return;

        try
        {
            HandleSideEndOfEvent(mapEvent.AttackerSide);
            HandleSideEndOfEvent(mapEvent.DefenderSide);
        }
        catch (Exception ex)
        {
            Logger.Error("SallyForthManager.OnMapEventEnded failed", ex);
        }
    }

    private void HandleSideEndOfEvent(MapEventSide? side)
    {
        if (side == null) return;
        try
        {
            var parties = side.Parties;
            if (parties == null) return;

            foreach (var uop in parties)
            {
                MobileParty? mp = null;
                try
                {
                    // MapEventSide.Parties 是 MapEventParty 包装；其 .Party.MobileParty 取 MobileParty。
                    // vanilla v1.3.15 暴露 .Party (PartyBase) → .MobileParty
                    mp = uop.Party?.MobileParty;
                }
                catch
                {
                    continue;
                }

                if (mp == null) continue;
                if (!mp.IsActive) continue;

                var sp = mp.PartyComponent as SallyForthPartyComponent;
                if (sp == null) continue;

                var home = sp.HomeSettlement;
                if (home == null)
                {
                    Logger.Warn($"SallyForthManager.MapEventEnded: '{PartyNameFormatter.SafeName(mp)}' has null HomeSettlement, emergency cleanup");
                    EmergencyCleanup(mp);
                    continue;
                }
                // 2026-05-12 审查 D-I3 修复：战后若 home 已失守，不要 SetMoveGoToSettlement 一个敌方城市。
                // B7.28：归属必须看 party.ActualClan；home 易主后 home.OwnerClan 已是新主人。
                var partyClan = mp.ActualClan ?? home.OwnerClan;
                if (_capitalRegistry == null
                    || partyClan == null
                    || !_capitalRegistry.IsManagedClanWithCapital(partyClan)
                    || home.OwnerClan != partyClan)
                {
                    Logger.Warn($"SallyForthManager.MapEventEnded: '{PartyNameFormatter.SafeName(mp)}' home '{home.Name}' fell during battle, emergency cleanup");
                    EmergencyCleanup(mp);
                    continue;
                }

                Logger.Info($"SallyForthManager: '{PartyNameFormatter.SafeName(mp)}' completed battle, returning to '{home.Name}'");
                ReleaseAiAndReturnHome(mp, home);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HandleSideEndOfEvent iteration failed", ex);
        }
    }

    // ────────── 内部辅助：找目标 ──────────

    private static MobileParty? FindBestEnemyTarget(Settlement settlement)
    {
        try
        {
            var ownFaction = settlement.MapFaction;
            if (ownFaction == null) return null;

            MobileParty? best = null;
            float bestStrength = float.MaxValue;

            var search = MobileParty.StartFindingLocatablesAroundPosition(settlement.GetPosition2D, DetectionRadius);
            for (var candidate = MobileParty.FindNextLocatable(ref search);
                 candidate != null;
                 candidate = MobileParty.FindNextLocatable(ref search))
            {
                if (!candidate.IsActive) continue;
                if (candidate == MobileParty.MainParty) continue;

                var faction = candidate.MapFaction;
                if (faction == null) continue;
                if (!faction.IsAtWarWith(ownFaction)) continue;

                // 评分：优先选力量小的（避免冒险打硬目标）
                // v1.3.15: PartyBase 无 TotalStrength；用 MemberRoster.TotalManCount 作代理
                var strength = 0f;
                try { strength = (float)(candidate.MemberRoster?.TotalManCount ?? 0); }
                catch { strength = 0f; }
                if (strength < bestStrength)
                {
                    bestStrength = strength;
                    best = candidate;
                }
            }

            return best;
        }
        catch (Exception ex)
        {
            Logger.Error($"FindBestEnemyTarget failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
            return null;
        }
    }

    // ────────── 内部辅助：创建出击队 ──────────

    private void TryCreateSallyParty(Settlement settlement, MobileParty garrison, int garrisonCount, MobileParty target)
    {
        try
        {
            // 目标兵力倍数抽兵，并受实际驻军抽取比例与 MinimumDefenderRatio 钳制。
            var ruleSally = settlement.Town != null ? ConfigurationManager.GetRuleFor(settlement.Town) : null;
            float minimumDefenderRatio = ruleSally?.MinimumDefenderRatio ?? TownGarrisonRule.CreateDefault().MinimumDefenderRatio;
            int minDef = GarrisonThresholdMath.CountFromRatio(garrisonCount, minimumDefenderRatio, minimumWhenPositive: 0);
            int extractable = Math.Max(0, garrisonCount - minDef);
            int byGarrisonRatio = GarrisonThresholdMath.CountFromRatio(garrisonCount, SallyExtractionRatio, minimumWhenPositive: 0);
            int targetMen = Math.Max(0, target.MemberRoster?.TotalManCount ?? 0);
            int byTarget = Math.Max(0, (int)Math.Ceiling(targetMen * SallyTargetPartySizeMultiplier));
            int sallySize = Math.Min(byTarget, Math.Min(extractable, byGarrisonRatio));
            int createMin = SallyCreateMinPartyCount;
            if (sallySize < createMin)
            {
                Logger.Debug($"SallyForthManager: '{settlement.Name}' sallySize={sallySize} < {createMin} (target={targetMen}×{SallyTargetPartySizeMultiplier:F2}->{byTarget}, garrison={garrisonCount}, cap={SallyExtractionRatio:P0}->{byGarrisonRatio}, minDef={minimumDefenderRatio:P0}->{minDef}), 抽兵过少，不出击");
                return;
            }

            if (settlement.Town == null) return; // 不可能，但 nullable warn 安抚

            // B7.27：派出 sally 前先扣本钱（仅玩家氏族）
            bool shouldChargeSally = CapitalRegistry.ShouldChargeClan(settlement.OwnerClan);
            if (shouldChargeSally)
            {
                if (!ModTreasury.CanAfford(InitialSallyGold))
                {
                    Logger.Info($"SallyForthManager: '{settlement.Name}' 玩家金币不足 (need {InitialSallyGold})，跳过出击");
                    return;
                }
                if (!ModTreasury.Charge(ExpenseCategory.SallySeed, InitialSallyGold, $"sally_seed home={settlement.StringId}"))
                {
                    Logger.Info($"SallyForthManager: '{settlement.Name}' ModTreasury.Charge 拒绝，跳过出击");
                    return;
                }
            }

            var sallyParty = SallyForthPartyComponent.CreateForTown(
                homeTown: settlement.Town,
                initialTarget: target,
                initialGold: InitialSallyGold);

            if (sallyParty == null)
            {
                Logger.Warn($"SallyForthManager: CreateForTown returned null for '{settlement.Name}'");
                return;
            }

            // 从 garrison 抽兵（仿 PatrolManager.TransferTroopsFromGarrison：skip heroes，逐兵种迁移）
            var moved = TransferTroopsFromGarrison(garrison, sallyParty, sallySize);
            if (moved < createMin)
            {
                Logger.Warn($"SallyForthManager: '{settlement.Name}' transferred only {moved} troops < createMin {createMin}, aborting sally");
                // 把已抽走的兵塞回去 + 销毁空 sally
                TransferTroopsBackToGarrison(sallyParty, garrison);
                _mergeService.DestroyAndUntrack(sallyParty, "SallyForthManager rollback", deferIfInMapEvent: false);
                return;
            }

            // AI 编排：交给 vanilla 战斗系统
            try
            {
                sallyParty.Ai?.SetDoNotMakeNewDecisions(true);
                sallyParty.SetMoveEngageParty(target, MobileParty.NavigationType.Default);
                // B5 F1: 防止 sally party 被 vanilla 拉去加入玩家战斗（GDS 同等处置）
                sallyParty.ShouldJoinPlayerBattles = false;
            }
            catch (Exception aiEx)
            {
                Logger.Error($"SallyForthManager: AI directive failed for '{PartyNameFormatter.SafeName(sallyParty)}'", aiEx);
            }

            // 注册 lifecycle
            _lifecycle.RegisterTrackedParty(sallyParty, settlement, PartyLifecycleManager.KindSallyForth);

            DecisionAuditLogger.LogRule(
                decisionType: "create_sally_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} moved={moved} target={target.StringId} targetMen={targetMen}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{sallyParty.StringId}\",\"target\":\"{target.StringId}\",\"moved\":{moved},\"targetMen\":{targetMen},\"targetMultiplier\":{SallyTargetPartySizeMultiplier:F2},\"garrisonCapRatio\":{SallyExtractionRatio:F2},\"radius\":{DetectionRadius}}}",
                accepted: true);
            Logger.Info($"SallyForthManager: created sally '{PartyNameFormatter.SafeName(sallyParty)}' for '{settlement.Name}' (moved={moved} troops, target='{PartyNameFormatter.SafeName(target)}')");
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.TryCreateSallyParty failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    // B2 重构（2026-05-14）：实现搬运至 SovereignTowns.Common.TroopTransferHelper；
    // 这里保留薄包装，保留 sally 调用方对 MobileParty 入参的语义。
    // 排序策略：HighestTierFirst（精锐先出击，仿 GDS）。
    private static int TransferTroopsFromGarrison(MobileParty garrison, MobileParty sally, int batchSize)
    {
        var gRoster = garrison?.MemberRoster;
        var sRoster = sally?.MemberRoster;
        if (gRoster == null || sRoster == null) return 0;
        return TroopTransferHelper.TransferFromGarrison(
            gRoster, sRoster, batchSize, TroopTransferHelper.SortStrategy.HighestTierFirst);
    }

    private static void TransferTroopsBackToGarrison(MobileParty sally, MobileParty garrison)
    {
        var sRoster = sally?.MemberRoster;
        var gRoster = garrison?.MemberRoster;
        if (sRoster == null || gRoster == null) return;
        TroopTransferHelper.TransferBackToGarrison(sRoster, gRoster);
    }

    // ────────── 内部辅助：到家转兵 + 销毁 ──────────

    private void TransferAndDestroy(MobileParty sally, Settlement home)
    {
        try
        {
            // 兜底层 B：destroy 前先处置战利品（捕捉 MapEventEnded 路径漏网情况）
            try { _battleLootManager?.ProcessPartyIfEligible(sally); }
            catch (Exception lootEx) { Logger.Error($"SallyForthManager.TransferAndDestroy: loot processing threw for '{PartyNameFormatter.SafeName(sally)}'", lootEx); }

            var town = home.Town;
            if (town == null)
            {
                Logger.Warn($"SallyForthManager: '{PartyNameFormatter.SafeName(sally)}' at non-town '{home.Name}', direct destroy");
                SafeDestroy(sally);
                return;
            }

            int transferred = _mergeService.MergeNonHeroTroopsIntoGarrison(
                sally,
                home,
                "SallyForthManager.TransferAndDestroy");

            DecisionAuditLogger.LogRule(
                decisionType: "merge_sally_into_garrison",
                inputSummary: $"home={home.StringId} sally={sally.StringId} transferred={transferred}",
                decisionJson: $"{{\"home\":\"{home.StringId}\",\"sally\":\"{sally.StringId}\",\"transferred\":{transferred}}}",
                accepted: true);
            Logger.Info($"SallyForthManager: '{PartyNameFormatter.SafeName(sally)}' merged {transferred} troops into '{home.Name}' garrison, destroying");

            // B7.22：记录 sally 结束时间戳，用于下次出击冷却 + 清持续可见计数 + 清 log 状态
            try
            {
                _lastSallyEndedAt[home] = CampaignTime.Now;
                _enemySustainedTicks.Remove(home);
                _forceReturnLogged.Remove(sally);
                _targetLostLogged.Remove(sally);
            }
            catch { /* swallow */ }

            SafeDestroy(sally);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.TransferAndDestroy failed for '{PartyNameFormatter.SafeName(sally)}'", ex);
        }
    }

    /// <summary>
    /// 紧急清理：home 失守 / null 时，把存活兵塞回当前首府（如有），然后销毁 party。
    /// 同时 untrack 防止 lifecycle 计数漂移。
    /// 2026-05-12 审查 D-I3 新增。
    /// </summary>
    private void EmergencyCleanup(MobileParty sally)
    {
        try
        {
            // B7.15：只迁到 sally party 自己 origClan 的当前首府。
            // 旧版本写过"否则退回玩家首府"是错的 — AI sally party 的兵会被白送给玩家驻军（free-troops bug）。
            // origMgr 为 null 或该 clan 已无首府 → newCapital 也为 null → 走下方的 "兵员蒸发" 路径。
            Settlement? newCapital = null;
            try
            {
                var spComp = sally.PartyComponent as SallyForthPartyComponent;
                var origHome = spComp?.HomeSettlement;
                var origClan = sally.ActualClan ?? origHome?.OwnerClan;
                newCapital = _capitalRegistry?.GetCapitalForClan(origClan);
            }
            catch { newCapital = null; }
            if (newCapital != null && sally.MemberRoster != null)
            {
                int rescued = _mergeService.MergeNonHeroTroopsIntoGarrison(
                    sally,
                    newCapital,
                    "SallyForthManager.EmergencyCleanup");
                Logger.Info($"EmergencyCleanup: rescued {rescued} troops from '{PartyNameFormatter.SafeName(sally)}' to new capital '{newCapital.Name}'");
            }
            else
            {
                Logger.Info($"EmergencyCleanup: '{PartyNameFormatter.SafeName(sally)}' troops lost (no fallback settlement)");
            }
        }
        catch (Exception ex) { Logger.Error("EmergencyCleanup roster phase failed", ex); }

        // B7.22：与正常合并路径一致，标记 sally 结束时间戳供冷却用 + 清 log 状态
        try
        {
            var spComp = sally.PartyComponent as SallyForthPartyComponent;
            var origHome = spComp?.HomeSettlement;
            if (origHome != null)
            {
                _lastSallyEndedAt[origHome] = CampaignTime.Now;
                _enemySustainedTicks.Remove(origHome);
            }
            _forceReturnLogged.Remove(sally);
            _targetLostLogged.Remove(sally);
        }
        catch { /* swallow */ }

        SafeDestroy(sally);
    }

    private void SafeDestroy(MobileParty party)
    {
        // 2026-05-12 审查 E-WARN-10 防护：DestroyPartyAction 在 MapEvent 中可能 NRE
        // (社区报告 BesiegerCamp.RemoveSiegePartyInternal 链路)。若 party 在战斗中，
        // 推迟销毁 — MapEventEnded 后状态机自然会经过到家 → TransferAndDestroy。
        try
        {
            if (party.MapEvent != null)
            {
                Logger.Warn($"SafeDestroy: '{PartyNameFormatter.SafeName(party)}' is in MapEvent, deferring destroy to post-battle path");
                return;
            }
        }
        catch { /* MapEvent 属性访问失败也直接降级到 Apply */ }

        _mergeService.DestroyAndUntrack(party, "SallyForthManager.SafeDestroy", deferIfInMapEvent: false);
    }

    /// <summary>
    /// 2026-05-12 审查 B-WarPartyComponent.OnFinalize 修复：sally party 战场被歼灭时
    /// 由 vanilla 直接 destroy（绕过我们的状态机）→ roster 残留兵员丢失。
    /// 订阅 MobilePartyDestroyed，尝试把还在 roster 中的兵搬回 home garrison。
    /// 大多数情况下战斗结束时 vanilla 已清空了 roster（战死/被俘），但少量残兵的回收对玩家体验关键。
    /// </summary>
    public void OnMobilePartyDestroyed(MobileParty party, PartyBase? destroyerParty)
    {
        if (party == null) return;

        try
        {
            // 把 PartyComponent 访问也纳入 try：party.PartyComponent 是 vanilla 属性，
            // 不应假设其访问无异常（vanilla 在 destroy 中途置 null 的场景可能存在）。
            var sp = party.PartyComponent as SallyForthPartyComponent;
            if (sp == null) return;
            var home = sp.HomeSettlement;
            // B7.22：销毁也算 sally 结束 — stamp 冷却时间，无论 home 状态如何 + 清 log 状态
            if (home != null)
            {
                try { _lastSallyEndedAt[home] = CampaignTime.Now; _enemySustainedTicks.Remove(home); }
                catch { /* swallow */ }
            }
            try { _forceReturnLogged.Remove(party); _targetLostLogged.Remove(party); } catch { }

            // B7.28：销毁救援也以 party.ActualClan 为原始归属；home 易主时改救到该 clan 当前首府。
            var partyClan = party.ActualClan ?? home?.OwnerClan;
            Settlement? rescueTarget = null;
            if (_capitalRegistry != null && partyClan != null)
            {
                if (home != null && home.OwnerClan == partyClan && _capitalRegistry.IsManagedClanWithCapital(partyClan))
                {
                    rescueTarget = home;
                }
                else
                {
                    rescueTarget = _capitalRegistry.GetCapitalForClan(partyClan);
                }
            }

            if (rescueTarget == null)
            {
                Logger.Info($"OnMobilePartyDestroyed: '{PartyNameFormatter.SafeName(party)}' home unavailable, no rescue");
                return;
            }

            int rescued = _mergeService.MergeNonHeroTroopsIntoGarrison(
                party,
                rescueTarget,
                "SallyForthManager.OnMobilePartyDestroyed");

            if (rescued > 0)
            {
                Logger.Info($"OnMobilePartyDestroyed: rescued {rescued} survivors from '{PartyNameFormatter.SafeName(party)}' to '{rescueTarget.Name}' garrison");
                DecisionAuditLogger.LogRule(
                    decisionType: "sally_destroyed_rescue",
                    inputSummary: $"sally={party.StringId} home={rescueTarget.StringId} rescued={rescued} destroyer={destroyerParty?.Name?.ToString() ?? "none"}",
                    decisionJson: $"{{\"home\":\"{rescueTarget.StringId}\",\"rescued\":{rescued}}}",
                    accepted: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.OnMobilePartyDestroyed failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    private static void ReleaseAiAndReturnHome(MobileParty party, Settlement home)
    {
        try
        {
            party.Ai?.SetDoNotMakeNewDecisions(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetDoNotMakeNewDecisions(false) failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
        try
        {
            party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(home)}'", ex);
        }
    }

}
