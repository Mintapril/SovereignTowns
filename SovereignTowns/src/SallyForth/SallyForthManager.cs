using System;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.SallyForth;

/// <summary>
/// MVP 5：监管玩家自有 town 的"主动出击队"（<see cref="SallyForthPartyComponent"/>）。
///
/// 设计原则（仿 GDS，但避开 Harmony，并改进 GDS 的回程 bug）：
///   - 仅触碰 OwnerClan == PlayerClan 且通过 PartyLifecycleManager 注册 kind="sallyforth" 的队伍；
///   - 所有方法 try-catch，绝不向调用方抛异常；
///   - 在 SettlementHourlyTick 评估是否应出击 + 选目标 + 创建出击队；
///   - 在 PartyHourlyTick 处理"在外 → 撤退 / 超时 → 回家 / 到家 → 转兵+销毁"状态机；
///   - 在 <c>MapEventEnded</c> 上精确捕捉战后 sally party，立即下达回程指令（vs GDS 靠 TickEvent 轮询，已知 bug 来源）。
///
/// 与 PatrolManager 互斥：仅在城内"无巡逻队"时考虑出击（防止两条流水同时抽兵）。
/// </summary>
public sealed class SallyForthManager
{
    // === 触发参数（合理默认；未来挂控制面板可改） ===
    // 2026-05-12 审查 B2 调整：15f → 50f 与 GDS 默认 AttackDistance 对齐，
    // 否则 FindBestEnemyTarget 在大多数场景下返回 null，sortie 不会触发。
    private const float DetectionRadius      = 50f;   // 检测半径（地图单位）
    private const int   MinGarrisonForSally  = 50;    // 出击门槛：驻军总人数
    public  const int   MaxSallyPartySize    = 100;   // 出击队上限（亦由 STPartySizeLimitModel 引用）
    private const float SallyRatio           = 0.6f;  // 抽 60% 驻军
    private const int   MinPartyBeforeRetreat = 10;   // 兵员低于此值 → 回家
    private const float MaxSallyHours        = 12f;   // 12 小时未归 → 强制回程
    private const int   InitialSallyGold     = 100;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalManager? _capitalManager;

    public SallyForthManager(PartyLifecycleManager lifecycle, CapitalManager? capitalManager = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalManager = capitalManager;
    }

    // ────────── Settlement 小时 tick：评估并创建 ──────────

    public void OnHourlyTickSettlement(Settlement settlement)
    {
        // 用户原话："无巡逻队模式时每个城镇都会出城攻击" — 仅 town，不含 castle。
        if (settlement == null) return;
        if (!settlement.IsTown) return;
        if (settlement.OwnerClan != Clan.PlayerClan) return;

        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.SallyForth) return;

            // 系统开关：未设首府 → 系统关闭
            if (_capitalManager?.GetCapital() == null) return;

            if (settlement.IsUnderSiege) return;

            // 关键：仅在"无巡逻队"时考虑出击
            // 1) vanilla patrol（settlement.PatrolParty 非空）
            if (settlement.PatrolParty != null) return;
            // 2) 我们自创的 patrol
            if (_lifecycle.CountActive(settlement, PartyLifecycleManager.KindPatrol) > 0) return;

            // 上限：每城最多 1 支 sally
            if (!_lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindSallyForth)) return;

            // 驻军门槛
            var garrison = settlement.Town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;
            if (garrisonCount < MinGarrisonForSally) return;

            // 找附近敌方 party（评分：选最弱者）
            var target = FindBestEnemyTarget(settlement);
            if (target == null) return;

            TryCreateSallyParty(settlement, garrison!, garrisonCount, target);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.OnHourlyTickSettlement failed for '{SafeName(settlement)}'", ex);
        }
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
                Logger.Warn($"SallyForthManager: '{SafeName(party)}' has null HomeSettlement, orphan cleanup");
                EmergencyCleanup(party);
                return;
            }
            // 2026-05-12 审查 D-I3 修复：失守时不再静默 return 留下孤儿；改为紧急清理
            if (home.OwnerClan != Clan.PlayerClan)
            {
                Logger.Warn($"SallyForthManager: '{SafeName(party)}' home '{home.Name}' lost (owner={home.OwnerClan?.Name?.ToString() ?? "none"}), emergency cleanup");
                EmergencyCleanup(party);
                return;
            }

            // 1) 已到家 → 转兵 + 销毁
            if (party.LastVisitedSettlement == home)
            {
                TransferAndDestroy(party, home);
                return;
            }

            // 2) 兵员过少 → 回家
            var memberCount = party.MemberRoster?.TotalManCount ?? 0;
            if (memberCount < MinPartyBeforeRetreat)
            {
                Logger.Info($"SallyForthManager: '{SafeName(party)}' members={memberCount} < {MinPartyBeforeRetreat}, return home '{home.Name}'");
                ReleaseAiAndReturnHome(party, home);
                return;
            }

            // 3) 超时 → 强制回家
            var hoursAway = (CampaignTime.Now - sp.DepartureTime).ToHours;
            if (hoursAway > MaxSallyHours)
            {
                Logger.Warn($"SallyForthManager: '{SafeName(party)}' away {hoursAway:F1}h > {MaxSallyHours}h, force return to '{home.Name}'");
                ReleaseAiAndReturnHome(party, home);
                return;
            }

            // 4) B5 F2: target 进入 settlement → 追击队卡在外面（vanilla 已知 bug），立即回家
            //    否则 target 已死亡/失效 → 释放 vanilla AI 接管
            //    否则继续 engage（创建时已 SetMoveEngageParty + SetDoNotMakeNewDecisions）
            var target = sp.TargetParty;
            if (target != null && target.IsActive && target.CurrentSettlement != null)
            {
                Logger.Info($"SallyForthManager: '{SafeName(party)}' target '{SafeName(target)}' entered '{target.CurrentSettlement.Name}', returning home '{home.Name}'");
                ReleaseAiAndReturnHome(party, home);
                return;
            }
            if (target == null || !target.IsActive)
            {
                Logger.Info($"SallyForthManager: '{SafeName(party)}' target lost, releasing AI for re-decision");
                try { party.Ai?.SetDoNotMakeNewDecisions(false); }
                catch (Exception aiEx) { Logger.Error("SetDoNotMakeNewDecisions(false) failed", aiEx); }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.OnHourlyTickParty failed for '{SafeName(party)}'", ex);
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
                    Logger.Warn($"SallyForthManager.MapEventEnded: '{SafeName(mp)}' has null HomeSettlement, emergency cleanup");
                    EmergencyCleanup(mp);
                    continue;
                }
                // 2026-05-12 审查 D-I3 修复：战后若 home 已失守，不要 SetMoveGoToSettlement 一个敌方城市
                if (home.OwnerClan != Clan.PlayerClan)
                {
                    Logger.Warn($"SallyForthManager.MapEventEnded: '{SafeName(mp)}' home '{home.Name}' fell during battle, emergency cleanup");
                    EmergencyCleanup(mp);
                    continue;
                }

                // 层 A：战后立即处置战利品（必须先于 ReleaseAiAndReturnHome — 否则 PrisonRoster 等仍在 party 上）
                try
                {
                    BattleLootHandler.ProcessPartyLoot(mp, _capitalManager);
                }
                catch (Exception lootEx)
                {
                    Logger.Error($"SallyForthManager: BattleLootHandler.ProcessPartyLoot threw for '{SafeName(mp)}'", lootEx);
                }

                Logger.Info($"SallyForthManager: '{SafeName(mp)}' completed battle, returning to '{home.Name}'");
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
            Logger.Error($"FindBestEnemyTarget failed for '{SafeName(settlement)}'", ex);
            return null;
        }
    }

    // ────────── 内部辅助：创建出击队 ──────────

    private void TryCreateSallyParty(Settlement settlement, MobileParty garrison, int garrisonCount, MobileParty target)
    {
        try
        {
            var sallySize = Math.Min(MaxSallyPartySize, (int)(garrisonCount * SallyRatio));
            if (sallySize < MinPartyBeforeRetreat)
            {
                Logger.Debug($"SallyForthManager: '{settlement.Name}' computed sallySize={sallySize} too small, skip");
                return;
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
            if (moved < MinPartyBeforeRetreat)
            {
                Logger.Warn($"SallyForthManager: '{settlement.Name}' transferred only {moved} troops < {MinPartyBeforeRetreat}, aborting sally");
                // 把已抽走的兵塞回去 + 销毁空 sally
                TransferTroopsBackToGarrison(sallyParty, garrison);
                try { DestroyPartyAction.Apply(null, sallyParty); }
                catch (Exception destroyEx) { Logger.Error("DestroyPartyAction.Apply (rollback) failed", destroyEx); }
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
                Logger.Error($"SallyForthManager: AI directive failed for '{SafeName(sallyParty)}'", aiEx);
            }

            // 注册 lifecycle
            _lifecycle.RegisterTrackedParty(sallyParty, settlement, PartyLifecycleManager.KindSallyForth);

            DecisionAuditLogger.LogRule(
                decisionType: "create_sally_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} moved={moved} target={target.StringId} targetMen={target.MemberRoster?.TotalManCount ?? 0}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{sallyParty.StringId}\",\"target\":\"{target.StringId}\",\"moved\":{moved},\"radius\":{DetectionRadius}}}",
                accepted: true);
            Logger.Info($"SallyForthManager: created sally '{SafeName(sallyParty)}' for '{settlement.Name}' (moved={moved} troops, target='{SafeName(target)}')");
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.TryCreateSallyParty failed for '{SafeName(settlement)}'", ex);
        }
    }

    private static int TransferTroopsFromGarrison(MobileParty garrison, MobileParty sally, int batchSize)
    {
        try
        {
            var gRoster = garrison?.MemberRoster;
            var sRoster = sally?.MemberRoster;
            if (gRoster == null || sRoster == null || batchSize <= 0) return 0;

            int remaining = batchSize;
            int moved = 0;

            var snapshot = new System.Collections.Generic.List<TroopRosterElement>(gRoster.GetTroopRoster());
            // 优先高 tier（仿 GDS：精锐才出击）
            snapshot.Sort((a, b) =>
            {
                var ta = a.Character?.Tier ?? 0;
                var tb = b.Character?.Tier ?? 0;
                return tb.CompareTo(ta);
            });

            foreach (var elem in snapshot)
            {
                if (remaining <= 0) break;
                if (elem.Character == null || elem.Character.IsHero) continue;
                if (elem.Number <= 0) continue;

                int take = Math.Min(elem.Number, remaining);
                try
                {
                    gRoster.AddToCounts(elem.Character, -take, false, 0, 0);
                    sRoster.AddToCounts(elem.Character, take, false, 0, 0);
                    moved += take;
                    remaining -= take;
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"TransferTroopsFromGarrison (sally): per-element transfer failed (char='{elem.Character?.StringId}', n={take})", oneEx);
                }
            }
            return moved;
        }
        catch (Exception ex)
        {
            Logger.Error("TransferTroopsFromGarrison (sally) failed", ex);
            return 0;
        }
    }

    private static void TransferTroopsBackToGarrison(MobileParty sally, MobileParty garrison)
    {
        try
        {
            var sRoster = sally?.MemberRoster;
            var gRoster = garrison?.MemberRoster;
            if (sRoster == null || gRoster == null) return;

            var snapshot = new System.Collections.Generic.List<TroopRosterElement>(sRoster.GetTroopRoster());
            foreach (var elem in snapshot)
            {
                if (elem.Character == null || elem.Character.IsHero || elem.Number <= 0) continue;
                try
                {
                    gRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                    sRoster.AddToCounts(elem.Character, -elem.Number, false, 0, 0);
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"TransferTroopsBackToGarrison: per-element rollback failed (char='{elem.Character?.StringId}')", oneEx);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("TransferTroopsBackToGarrison failed", ex);
        }
    }

    // ────────── 内部辅助：到家转兵 + 销毁 ──────────

    private void TransferAndDestroy(MobileParty sally, Settlement home)
    {
        try
        {
            // 兜底层 B：destroy 前先处置战利品（捕捉 MapEventEnded 路径漏网情况）
            try { BattleLootHandler.ProcessPartyLoot(sally, _capitalManager); }
            catch (Exception lootEx) { Logger.Error($"SallyForthManager.TransferAndDestroy: ProcessPartyLoot threw for '{SafeName(sally)}'", lootEx); }

            var town = home.Town;
            if (town == null)
            {
                Logger.Warn($"SallyForthManager: '{SafeName(sally)}' at non-town '{home.Name}', direct destroy");
                SafeDestroy(sally);
                return;
            }

            var garrison = town.GarrisonParty;
            var sRoster = sally.MemberRoster;
            int transferred = 0;

            if (garrison?.MemberRoster != null && sRoster != null)
            {
                var elements = sRoster.GetTroopRoster();
                foreach (var elem in elements)
                {
                    if (elem.Character == null || elem.Character.IsHero) continue;
                    garrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                    transferred += elem.Number;
                }
                sRoster.RemoveIf(e => e.Character != null && !e.Character.IsHero);
            }

            DecisionAuditLogger.LogRule(
                decisionType: "merge_sally_into_garrison",
                inputSummary: $"home={home.StringId} sally={sally.StringId} transferred={transferred}",
                decisionJson: $"{{\"home\":\"{home.StringId}\",\"sally\":\"{sally.StringId}\",\"transferred\":{transferred}}}",
                accepted: true);
            Logger.Info($"SallyForthManager: '{SafeName(sally)}' merged {transferred} troops into '{home.Name}' garrison, destroying");

            SafeDestroy(sally);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.TransferAndDestroy failed for '{SafeName(sally)}'", ex);
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
            // 优先迁兵到新首府（如系统未关闭）
            var newCapital = _capitalManager?.GetCapitalSettlement();
            if (newCapital != null && newCapital.Town?.GarrisonParty != null && sally.MemberRoster != null)
            {
                var gRoster = newCapital.Town.GarrisonParty.MemberRoster;
                var sRoster = sally.MemberRoster;
                int rescued = 0;
                try
                {
                    var snapshot = new System.Collections.Generic.List<TroopRosterElement>(sRoster.GetTroopRoster());
                    foreach (var elem in snapshot)
                    {
                        if (elem.Character == null || elem.Character.IsHero || elem.Number <= 0) continue;
                        gRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                        sRoster.AddToCounts(elem.Character, -elem.Number, false, 0, 0);
                        rescued += elem.Number;
                    }
                }
                catch (Exception rEx) { Logger.Error("EmergencyCleanup: roster transfer to new capital failed", rEx); }
                Logger.Info($"EmergencyCleanup: rescued {rescued} troops from '{SafeName(sally)}' to new capital '{newCapital.Name}'");
            }
            else
            {
                Logger.Info($"EmergencyCleanup: '{SafeName(sally)}' troops lost (no fallback settlement)");
            }
        }
        catch (Exception ex) { Logger.Error("EmergencyCleanup roster phase failed", ex); }

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
                Logger.Warn($"SafeDestroy: '{SafeName(party)}' is in MapEvent, deferring destroy to post-battle path");
                return;
            }
        }
        catch { /* MapEvent 属性访问失败也直接降级到 Apply */ }

        try
        {
            DestroyPartyAction.Apply(null, party);
            _lifecycle.UntrackParty(party);
        }
        catch (Exception ex)
        {
            Logger.Error($"SafeDestroy failed for '{SafeName(party)}', falling back to Disband", ex);
            try
            {
                DisbandPartyAction.StartDisband(party);
                _lifecycle.UntrackParty(party);
            }
            catch (Exception fbEx)
            {
                Logger.Error($"Fallback Disband also failed for '{SafeName(party)}'", fbEx);
            }
        }
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
        var sp = party.PartyComponent as SallyForthPartyComponent;
        if (sp == null) return;

        try
        {
            var home = sp.HomeSettlement;
            if (home == null || home.OwnerClan != Clan.PlayerClan)
            {
                Logger.Info($"OnMobilePartyDestroyed: '{SafeName(party)}' home unavailable, no rescue");
                return;
            }

            var garrison = home.Town?.GarrisonParty;
            var sRoster = party.MemberRoster;
            if (garrison?.MemberRoster == null || sRoster == null) return;

            int rescued = 0;
            try
            {
                var snapshot = new System.Collections.Generic.List<TroopRosterElement>(sRoster.GetTroopRoster());
                foreach (var elem in snapshot)
                {
                    if (elem.Character == null || elem.Character.IsHero || elem.Number <= 0) continue;
                    garrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                    rescued += elem.Number;
                }
            }
            catch (Exception rEx) { Logger.Error("OnMobilePartyDestroyed: roster rescue failed", rEx); }

            if (rescued > 0)
            {
                Logger.Info($"OnMobilePartyDestroyed: rescued {rescued} survivors from '{SafeName(party)}' to '{home.Name}' garrison");
                DecisionAuditLogger.LogRule(
                    decisionType: "sally_destroyed_rescue",
                    inputSummary: $"sally={party.StringId} home={home.StringId} rescued={rescued} destroyer={destroyerParty?.Name?.ToString() ?? "none"}",
                    decisionJson: $"{{\"home\":\"{home.StringId}\",\"rescued\":{rescued}}}",
                    accepted: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyForthManager.OnMobilePartyDestroyed failed for '{SafeName(party)}'", ex);
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
            Logger.Error($"SetDoNotMakeNewDecisions(false) failed for '{SafeName(party)}'", ex);
        }
        try
        {
            party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveGoToSettlement failed for '{SafeName(party)}' -> '{SafeName(home)}'", ex);
        }
    }

    private static string SafeName(MobileParty? party)
    {
        if (party == null) return "<null>";
        try { return party.Name?.ToString() ?? party.StringId ?? "<unnamed>"; }
        catch { return "<error>"; }
    }

    private static string SafeName(Settlement? settlement)
    {
        if (settlement == null) return "<null>";
        try { return settlement.Name?.ToString() ?? settlement.StringId ?? "<unnamed>"; }
        catch { return "<error>"; }
    }
}
