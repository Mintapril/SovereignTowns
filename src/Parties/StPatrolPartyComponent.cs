using System;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Economy;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using SovereignTowns.SallyForth;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 巡逻队组件（B16.4）。完全替代 vanilla PatrolPartyComponent 使用路径。
/// 不出现在 MobileParty.AllPatrolParties，不绑定 Settlement.PatrolParty 槽位，不触发 vanilla 巡逻 AI。
/// vanilla 自动 spawn 的巡逻队仍以 PatrolPartyComponent 形式存在（共存策略，不互相干涉）。
///
/// 隐式状态机（无 enum）：行为由 scheduler 调度 + 防御响应 + 支援战斗 + 卡死保护四条规则驱动。
///
/// SaveableField 槽位：基类占 [10, 20)（含 12=_teamFunds）；本类占 [20, +∞)（当前 20=_createdAt）。
/// </summary>
public sealed class StPatrolPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_patrol_";
    private const float InitiativeResetHours = 4f;

    // 2026-05-18 v4：巡逻队创建时间，用于 PatrolMaxLifetimeHours 兜底（沿路 village 完全无食物时不至于无限饥饿减员）。
    [SaveableField(20)] private CampaignTime _createdAt;

    [CachedData] private TextObject? _cachedName;
    // B17.4 B6：防御日志去抖 — 只在 target 切换时 Info，否则 Debug。
    [CachedData] private Settlement? _lastLoggedDefenseTarget;
    // B17.4 A5 / R4：卡死期"首次检测时间"。每次 reissue 都会让 scheduler 内部 _lastStopChangedAt 复位，
    // 单纯计数 cycle 会把 24h UI 阈值变成"~24 个 stuckTimeout 周期 = ~288 真实小时"。
    // 真实小时按 (Now - _firstStuckAt) 衡量；到访新 settlement 或位置发生明显进展时重置 _stuckActive。
    [CachedData] private CampaignTime _firstStuckAt;
    [CachedData] private bool _stuckActive;
    // R5：首次卡死时的位置快照。若 reissue 后队伍有真实移动（距离阈值 > 此值）→ 视为恢复，重置卡死计时。
    [CachedData] private TaleWorlds.Library.Vec2 _stuckStartPosition;
    private const float StuckProgressDistanceThreshold = 1.0f; // 地图单位

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var home = HomeSettlementOrNull;
            var n = new TextObject("{=ST_PatrolPartyName}Patrol — {SETTLEMENT}");
            n.SetTextVariable("SETTLEMENT",
                home?.Name ?? new TextObject("{=ST_Common_Unknown}unknown"));
            _cachedName = n;
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => false;

    protected override Economy.ExpenseCategory GetExpenseCategoryForKind() => Economy.ExpenseCategory.PatrolSeed;

    private StPatrolPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        // 2026-05-18 v4：记录创建时间用于 PatrolMaxLifetimeHours 兜底。反序列化路径走 [SaveableField(20)]，
        // 旧存档没此字段反序列化后是 default (NumTicks=0)，OnHourlyTickCore 兜底检查里会自动 lazy-init。
        _createdAt = CampaignTime.Now;
    }

    /// <summary>
    /// 工厂：创建 ST 巡逻队（替代 vanilla PatrolPartyComponent.CreatePatrolParty）。
    /// 兵员注入 + SnapshotInitialMembers + 首站规划全部由 PatrolDispatcher 完成。
    /// template 参数仅作语义占位（按 settlement 文化挑兵种），实际不传给 vanilla CreatePatrolParty。
    /// </summary>
    public static MobileParty? CreateForTown(Settlement home, PartyTemplateObject? template)
    {
        if (home == null) return null;
        try
        {
            var ownerClan = home.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"StPatrolPartyComponent.CreateForTown: home '{home.StringId}' has no OwnerClan/Leader");
                return null;
            }

            // 不再用 vanilla PatrolPartyComponent.CreatePatrolParty — 自己构造
            var startingTroops = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(home.GatePosition, 1f, ownerClan, startingTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_PatrolPartyName}Patrol — {SETTLEMENT}");
            nameObj.SetTextVariable("SETTLEMENT", home.Name);

            var component = new StPatrolPartyComponent(
                home: home, name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: false,
                args: args, leader: null);

            var stringId = StringIdPrefix + home.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"StPatrolPartyComponent.CreateForTown: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }
            // 2026-05-18 fix: 阻止 vanilla AI 在第一个 hourly tick 之前接管 ST patrol。
            try { mobileParty.Ai?.SetDoNotMakeNewDecisions(true); } catch { /* swallow */ }
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StPatrolPartyComponent.CreateForTown failed", ex);
            return null;
        }
    }

    // ── 状态机核心 ────────────────────────────────────────

    /// <summary>
    /// 产品语义：巡逻队应当终身在户外巡逻（home 之外的 settlement 之间循环），回家 = 销毁。
    /// 因此 OnArrivedHome 等同 DefaultMergeAndDisband — 归还兵员到 garrison + 解散实例。
    /// 触发回家的"意外"路径包括：
    ///   - 兵员低于 PartyReturnSizeRatio（基类 OnMapEventEnded 检测 → ReturnToHome → 走到 home 触发本方法）
    ///   - 受伤比例高于 PartyReturnWoundedRatio（同上）
    ///   - PartyLifecycleManager 空闲超时 force-return（idleHours ≥ IdleHoursBeforeForceReturn）
    ///   - stuck 路径的 fallback：PickNext null 时 setMove(capital) → 走到 home 触发本方法
    /// 首府被围由 OnHourlyTickCore 的 "1) 防御响应" 处理（capital==defenseTarget → DefaultMergeAndDisband）。
    ///
    /// scheduler 的 PassesCandidateFilter 已把 patrol.HomeSettlementOrNull 从候选集排除，
    /// 正常巡逻路径不会主动回家。
    /// </summary>
    protected override void OnArrivedHome(MobileParty self)
    {
        Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived home — disbanding (回家=销毁 产品语义)");
        // 退款在 OnDestroyed 里做（覆盖所有销毁路径，包括 idle force-disband / 战败 destroy）。
        DefaultMergeAndDisband(self);
    }

    // 战后战利品保留在 party.ItemRoster；下次到达 settlement 时 base.TryEconomicMaintenance 会用 vanilla SellItemsAction
    // 真实卖出（与 vanilla 商队同模式）。不再 OnMapEventEndedCore 凭空卖。
    // 销毁退款逻辑统一在基类 TryRefundOnDestroy（doc §20 #1, T1）— 覆盖所有销毁路径（解散、战败、idle、stuck teleport 等）。

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
        {
            Logger.Debug($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' AutoPatrol=false → no-op");
            return;
        }

        // 2026-05-18 v4：兜底检查 — PatrolMaxLifetimeHours 到点强制回家解散。
        // 防御沿路 village 完全无食物（wool/clay/iron 等非粮村连片）、Village.Bound 异常等 A 路径
        // fallback 不到的极端饥饿场景。0 表示关闭兜底（接受"终身巡逻"风险）。
        // 旧存档反序列化后 _createdAt = default (epoch) → elapsed 会异常大；用 >2 倍 max validation 上限作哨兵 lazy-init 为 Now。
        float maxLifetime = ConfigurationManager.Current?.Thresholds?.PatrolMaxLifetimeHours ?? 168f;
        if (maxLifetime > 0f)
        {
            float elapsed = 0f;
            try { elapsed = (float)(CampaignTime.Now - _createdAt).ToHours; } catch { /* swallow */ }
            if (elapsed > 1500f)
            {
                // 1500h > 验证上限 720h 的两倍 — 唯一合理来源是旧存档 _createdAt=epoch（vanilla 年代差 ~1000+ 年）。
                Logger.Info($"StPatrolParty '{PartyNameFormatter.SafeName(self)}' _createdAt 反序列化后异常大 elapsed={elapsed:F0}h（旧存档?）— lazy-init 为 now");
                _createdAt = CampaignTime.Now;
                elapsed = 0f;
            }
            if (elapsed >= maxLifetime)
            {
                Logger.Info($"StPatrolParty '{PartyNameFormatter.SafeName(self)}' lifetime {elapsed:F1}h ≥ {maxLifetime}h 兜底 — 回首府解散");
                ReturnToHome(self);
                return;
            }
        }

        var registry = CapitalRegistry.Instance;
        var partyClan = self.ActualClan;
        if (partyClan == null || registry == null)
        {
            Logger.Debug($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' partyClan={partyClan?.StringId ?? "null"} registry={(registry == null ? "null" : "ok")} → early return");
            return;
        }
        var capitalMgr = registry.GetForClan(partyClan);
        if (capitalMgr == null)
        {
            Logger.Debug($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' capitalMgr=null for clan='{partyClan.StringId}' → early return");
            return;
        }
        var scheduler = capitalMgr.PatrolScheduler;

        // 1) 防御响应（B7.26）
        var defenseTarget = scheduler.GetDefenseTarget(self);
        if (defenseTarget != null)
        {
            if (defenseTarget.OwnerClan != capitalMgr.OwnerClan)
            {
                Logger.Warn($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defense target '{PartyNameFormatter.SafeName(defenseTarget)}' flipped owner mid-tick — skip");
                // fall through to normal tick logic
            }
            else if (defenseTarget == capital)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' capital '{PartyNameFormatter.SafeName(capital)}' under siege — MergeGarrison");
                DefaultMergeAndDisband(self);  // 基类提供
                return;
            }
            else
            {
                // B17.4 B6：日志去抖 — 只在 defense target 切换时 Info，否则 Debug。
                if (_lastLoggedDefenseTarget != defenseTarget)
                {
                    Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defending '{PartyNameFormatter.SafeName(defenseTarget)}' (under siege)");
                    _lastLoggedDefenseTarget = defenseTarget;
                }
                else
                {
                    Logger.Debug($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' still defending '{PartyNameFormatter.SafeName(defenseTarget)}'");
                }
                SafeSetMoveDefendSettlement(self, defenseTarget);
                SafeSetInitiative(self, attack: 0.3f, avoid: 0.7f, hours: InitiativeResetHours);

                // B17.4 B8：若 defense 目标 IsUnderSiege/IsUnderRaid 已转 false，立刻让 scheduler PickNextStop（不等下一 hour）。
                try
                {
                    bool stillThreat = defenseTarget.IsUnderSiege || (defenseTarget.IsVillage && defenseTarget.Village?.VillageState == Village.VillageStates.BeingRaided);
                    if (!stillThreat)
                    {
                        var nextEarly = scheduler.PickNextStop(self);
                        if (nextEarly != null && nextEarly != defenseTarget)
                        {
                            SafeMoveHelper.GoToWithLeave(self, nextEarly, "patrol defense early-return");
                            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defense target safe — early-return to '{nextEarly.Name}'");
                            _lastLoggedDefenseTarget = null;  // 重置去抖状态
                        }
                    }
                }
                catch (Exception threatEx) { Logger.Warn($"early-return threat check failed: {threatEx.Message}"); }
                return;
            }
        }

        // 离开 defense 路径 → 重置日志去抖
        _lastLoggedDefenseTarget = null;

        // 2) 支援出击战斗（B7.27）
        var sallyDispatcher = SovereignTowns.Campaign.SovereignTownsCampaignBehavior.SallyDispatcher;
        if (sallyDispatcher != null)
        {
            var supportSally = FindSupportableSallyBattle(self, capitalMgr, sallyDispatcher);
            if (supportSally != null)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' supporting sally '{PartyNameFormatter.SafeName(supportSally)}' (ETA < {ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours:F1}h)");
                try { self.SetMoveEngageParty(supportSally, MobileParty.NavigationType.Default); }
                catch (Exception ex) { Logger.Error($"SetMoveEngageParty failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(supportSally)}'", ex); }
                return;
            }
        }

        // 3) 抵达侦测 → RecordVisit + PickNextStop
        // 2026-05-18 修复 v2：原 `actuallyAtVisited = CurrentSettlement == visited` 严格检查太苛刻。
        // 日志铁证：巡逻队抵达 Jahasim 后 _lastVisitedAt['Jahasim'] 永远没设（sinceH=1e6），
        // 同时 PickNext 显示 dist=0（party 物理上就在 Jahasim 位置）。最可能原因：vanilla 在 party 抵达
        // 后短暂进出 settlement，hourly tick 时 CurrentSettlement=null 而 LastVisitedSettlement=Jahasim，
        // arrival 永不触发。改为：visited != home 即视为到访候选，TryMarkArrival 防重，home guard 避免
        // 出发首 tick（LastVisitedSettlement=home, CurrentSettlement=null）误判。
        var visited = self.CurrentSettlement ?? self.LastVisitedSettlement;
        var arrivalHome = HomeSettlementOrNull;
        Logger.Debug($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' arrival-check: cur='{self.CurrentSettlement?.Name?.ToString() ?? "null"}' lastVisited='{self.LastVisitedSettlement?.Name?.ToString() ?? "null"}' visited='{visited?.Name?.ToString() ?? "null"}' visitedOwner='{visited?.OwnerClan?.StringId ?? "null"}' vs mgrOwner='{capitalMgr.OwnerClan?.StringId ?? "null"}'");
        if (visited != null
            && visited != arrivalHome
            && visited.OwnerClan == capitalMgr.OwnerClan
            && scheduler.TryMarkArrival(self, visited))
        {
            scheduler.RecordVisit(visited);
            // R4：到访新 settlement = 真进展 → 卡死累计清零
            _stuckActive = false;
            // 2026-05-18 v3：在已知抵达 settlement 的上下文中触发经济维护（卖战利品 + 食物 <1 天补 3 天）。
            // 这是 base.TryEconomicMaintenance 真正能跑起来的唯一时机——hourly tick 入口时 CurrentSettlement
            // 几乎永远是 null（GoToWithLeave 已把 party 弹出），靠 LastVisitedSettlement fallback 有 silent
            // 副作用风险（在路上的 party 在已离开的 settlement 里 SellItemsAction）。
            try { TryEconomicMaintenance(self, visited); }
            catch (Exception econEx) { Logger.Warn($"StPatrolParty arrival-maintenance failed: {econEx.Message}"); }
            var next = scheduler.PickNextStop(self);
            if (next != null)
            {
                SafeMoveHelper.GoToWithLeave(self, next, "patrol arrival -> next stop");
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived '{PartyNameFormatter.SafeName(visited)}', next='{PartyNameFormatter.SafeName(next)}'");
            }
            else
            {
                // 2026-05-18 修复：原先 next==null 时 fallback 到 capital，导致氏族小（候选 < 2）
                // 时巡逻队"到 village 立刻回 home"。改为停留 — 下个 tick MinVisitGap 过期后 PickNext 会重试。
                Logger.Debug($"[DIAG] StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived '{PartyNameFormatter.SafeName(visited)}', no next candidate (MinVisitGap 内) — staying, next tick retry");
            }
            return;
        }
        Logger.Debug($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' arrival-check did not fire (visited='{visited?.Name?.ToString() ?? "null"}' home-skip={visited == arrivalHome} already-marked-or-other) — falling through to stuck check");

        // 4) 卡死保护 — R4/R5：用 CampaignTime 计算真实 elapsed hours；R5 用位置进展检测真正恢复。
        var stuckTimeout = ConfigurationManager.Current.ClanPatrol.StuckTimeoutHours;
        bool stuckNow = scheduler.IsStuck(self, stuckTimeout);
        if (stuckNow && !_stuckActive)
        {
            _stuckActive = true;
            _firstStuckAt = CampaignTime.Now;
            _stuckStartPosition = self.GetPosition2D;
        }
        // R5：每 tick 检测位置进展 — 若队伍已移动 > 阈值距离则视为恢复，清空卡死状态。
        // 解决 R4 修复后副作用："reissue 后实际在动但未到达 destination → 24h 后被误瞬移"。
        if (_stuckActive)
        {
            float moved = 0f;
            try { moved = (self.GetPosition2D - _stuckStartPosition).Length; } catch { /* swallow */ }
            if (moved > StuckProgressDistanceThreshold)
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' resumed motion ({moved:F2} > {StuckProgressDistanceThreshold:F2}) — clearing stuck state");
                _stuckActive = false;
            }
        }
        if (_stuckActive)
        {
            float teleportHours = ConfigurationManager.Current?.Thresholds?.StuckTeleportHours ?? 0f;
            float elapsedHours = 0f;
            try { elapsedHours = (float)(CampaignTime.Now - _firstStuckAt).ToHours; } catch { /* swallow */ }

            if (teleportHours > 0 && elapsedHours >= teleportHours)
            {
                try
                {
                    var home = HomeSettlementOrNull;
                    if (home != null)
                    {
                        // IG BoundedParty.cs:53 用的是 mobileParty.Position（不是 Position2D）。
                        // IG 是发布的 mod，证明 v1.3.15 该 setter 公开可写。
                        self.Position = home.GatePosition;
                        Logger.Warn($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck {elapsedHours:F1}h ≥ {teleportHours}h — teleport to '{home.Name}' GatePosition (二段救济)");
                        _stuckActive = false;
                        ReturnToHome(self);
                        return;
                    }
                }
                catch (Exception tpEx) { Logger.Error($"二段瞬移失败 for '{PartyNameFormatter.SafeName(self)}'", tpEx); }
            }

            if (stuckNow)
            {
                var next = scheduler.PickNextStop(self);
                var dest = next ?? capital;
                SafeMoveHelper.GoToWithLeave(self, dest, "patrol stuck re-pick");
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {stuckTimeout}h — re-pick next='{PartyNameFormatter.SafeName(dest)}' (elapsed since first stuck={elapsedHours:F1}h)");
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// B7.27：判定本 patrol 是否能在某 sally 战斗结束前抵达。返回最近的可支援目标，无则 null。
    /// 简单算法：ETA = 距离 / 速度，ETA &lt; SupportEtaThresholdHours 即可。
    /// </summary>
    private static MobileParty? FindSupportableSallyBattle(MobileParty self, CapitalManager capitalMgr, SallyDispatcher sallyDispatcher)
    {
        try
        {
            var threshold = ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours;
            var sallies = sallyDispatcher.GetActiveCombatSallyParties(capitalMgr.OwnerClan);
            if (sallies.Count == 0) return null;

            var partyPos = self.GetPosition2D;
            float partySpeed = Math.Max(self.Speed, 0.1f);

            MobileParty? best = null;
            float bestEta = float.MaxValue;
            foreach (var sally in sallies)
            {
                try
                {
                    if (sally.MapEvent == null) continue;  // 双重保险
                    float distance = (partyPos - sally.GetPosition2D).Length;
                    float eta = distance / partySpeed;
                    if (eta < threshold && eta < bestEta)
                    {
                        bestEta = eta;
                        best = sally;
                    }
                }
                catch { /* 单 sally 失败不影响其他 */ }
            }
            return best;
        }
        catch (Exception ex)
        {
            Logger.Error("FindSupportableSallyBattle failed", ex);
            return null;
        }
    }

    /// <summary>
    /// v1.3.15 SetMoveDefendSettlement(Settlement, bool isAvoidingTrouble, NavigationType)。
    /// 真正切换 vanilla AI 到"防守此 settlement"——这是 Defense 模式生效的关键。
    /// 失败时退而求其次 SetMoveGoToSettlement，避免完全无指令。
    /// </summary>
    private static void SafeSetMoveDefendSettlement(MobileParty party, Settlement home)
    {
        try
        {
            party.SetMoveDefendSettlement(home, false, MobileParty.NavigationType.Default);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveDefendSettlement failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(home)}'", ex);
            try
            {
                party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
            }
            catch (Exception fallbackEx)
            {
                Logger.Error($"Defense fallback SetMoveGoToSettlement also failed for '{PartyNameFormatter.SafeName(party)}'", fallbackEx);
            }
        }
    }

    private static void SafeSetInitiative(MobileParty party, float attack, float avoid, float hours)
    {
        try
        {
            party.Ai?.SetInitiative(attack, avoid, hours);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetInitiative failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }
}
