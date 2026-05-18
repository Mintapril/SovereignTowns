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
/// SaveableField 槽位：基类占 [10, 20)；本类用 [22] 持久化 _teamFunds（资金随存档保留）。
/// </summary>
public sealed class StPatrolPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_patrol_";
    private const float InitiativeResetHours = 4f;

    [CachedData] private TextObject? _cachedName;
    // 2026-05-18：巡逻队自负盈亏队伍资金（出发时从首府所有者扣 2000d；战利品卖出加入；销毁前还首府所有者）。
    [SaveableField(22)] private int _teamFunds;
    private const int InitialTeamFundsDefault = 2000;
    private const float BuyFoodDays = 3f;
    private const float BuyFoodWhenBelowDays = 1f;
    public int TeamFunds => _teamFunds;
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
            var s = HomeSettlementOrNull?.Name?.ToString() ?? "未知";
            _cachedName = new TextObject("{=ST_PatrolPartyName}巡逻队 - " + s);
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => false;

    // ── 队伍资金 API ────────────────────────────────────────────────
    /// <summary>从首府所有者扣 amount 第纳尔作为初始资金（玩家 hero 不够时按可负担扣）。</summary>
    public void InitTeamFundsFromHomeOwner(MobileParty self, int amount)
    {
        var owner = HomeSettlementOrNull?.OwnerClan?.Leader;
        int charged = PartyEconomyHelper.ChargeHero(owner, amount);
        _teamFunds = charged;
        Logger.Info($"StPatrolParty '{PartyNameFormatter.SafeName(self)}': team funds initialized = {charged}d (charged from '{owner?.StringId ?? "null"}')");
    }
    /// <summary>在 settlement 内用队伍资金购买 days 天食物（vanilla SellItemsAction 真实交易）。返回花费的第纳尔。</summary>
    public int BuyFoodAtSettlement(MobileParty self, Settlement settlement, float days)
        => PartyEconomyHelper.BuyFoodFromSettlement(self, settlement, days, ref _teamFunds);
    /// <summary>把战利品（非食物 item）卖给 settlement，金额加入队伍资金（vanilla SellItemsAction 真实交易）。返回收益。</summary>
    public int SellLootAtSettlement(MobileParty self, Settlement settlement)
        => PartyEconomyHelper.SellLootToSettlement(self, settlement, ref _teamFunds);
    /// <summary>把剩余资金还给首府所有者；清零 _teamFunds。返回还回去的数额。</summary>
    public int RefundTeamFundsToOwner()
    {
        if (_teamFunds <= 0) return 0;
        var owner = HomeSettlementOrNull?.OwnerClan?.Leader;
        int refunded = PartyEconomyHelper.RefundHero(owner, _teamFunds);
        if (refunded > 0)
            Logger.Info($"StPatrolParty: refunded {refunded}d team funds to '{owner?.StringId ?? "null"}'");
        _teamFunds = 0;
        return refunded;
    }

    /// <summary>
    /// 由 PatrolDispatcher 在玩家氏族 ModTreasury.Charge 成功后调用，直接把队伍资金设为 amount
    /// （不再从 owner.Gold 扣第二次）。
    /// </summary>
    public void SetTeamFunds(int amount)
    {
        _teamFunds = amount < 0 ? 0 : amount;
    }

    private StPatrolPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader) { }

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

            var nameObj = new TextObject("{=ST_PatrolPartyName}巡逻队 - " + home.Name);

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

    // 战后战利品保留在 party.ItemRoster；下次到达 settlement 时 OnHourlyTickCore 会用 vanilla SellItemsAction
    // 真实卖出（与 vanilla 商队同模式）。不再 OnMapEventEndedCore 凭空卖。

    /// <summary>
    /// 销毁前把队伍剩余资金还给首府所有者（覆盖所有销毁路径：解散、战败、idle、stuck teleport 等）。
    /// 退款路径与扣款路径对称：
    ///   - 玩家氏族（ShouldChargeClan=true）：走 ModTreasury.Refund，写 ledger + audit。
    ///   - AI 氏族（ShouldChargeClan=false）：走原 RefundTeamFundsToOwner → PartyEconomyHelper.RefundHero（vanilla 路径）。
    /// _teamFunds=0 时两路均 no-op（ModTreasury.Refund(_, 0, _) 内部直接 return true）。
    /// </summary>
    public override void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        try
        {
            var homeClan = HomeSettlementOrNull?.OwnerClan;
            if (CapitalRegistry.ShouldChargeClan(homeClan))
            {
                // 玩家氏族：退款走 ModTreasury 保持账目对称
                int toRefund = _teamFunds;
                _teamFunds = 0;
                ModTreasury.Refund(ExpenseCategory.PatrolSeed, toRefund,
                    $"patrol_destroyed home={HomeSettlementOrNull?.StringId ?? "null"}");
                if (toRefund > 0)
                    Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {toRefund}d via ModTreasury (player clan)");
            }
            else
            {
                // AI 氏族：退款走 vanilla hero.Gold 路径
                int refunded = RefundTeamFundsToOwner();
                if (refunded > 0)
                    Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {refunded}d to AI owner");
            }
        }
        catch (Exception ex) { Logger.Warn($"OnDestroyed refund threw: {ex.Message}"); }
        base.OnDestroyed(self, destroyer);
    }

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
        {
            Logger.Info($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' AutoPatrol=false → no-op");
            return;
        }

        var registry = CapitalRegistry.Instance;
        var partyClan = self.ActualClan;
        if (partyClan == null || registry == null)
        {
            Logger.Info($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' partyClan={partyClan?.StringId ?? "null"} registry={(registry == null ? "null" : "ok")} → early return");
            return;
        }
        var capitalMgr = registry.GetForClan(partyClan);
        if (capitalMgr == null)
        {
            Logger.Info($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' capitalMgr=null for clan='{partyClan.StringId}' → early return");
            return;
        }
        var scheduler = capitalMgr.PatrolScheduler;

        // 2026-05-18 经济：在 settlement 内时（CurrentSettlement != null）— 卖战利品 + 食物不足补食物，
        // 全部走 vanilla SellItemsAction 真实交易（物品在 settlement 库存和 party 库存间真实流转）。
        // 在荒郊路上 不交易，走完到下个 settlement 才行。
        var atSettlement = self.CurrentSettlement;
        if (atSettlement != null && atSettlement.Town != null)
        {
            // 1) 先卖战利品（无论食物状态，能卖就卖换钱）
            try
            {
                int gained = SellLootAtSettlement(self, atSettlement);
                if (gained > 0)
                    Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' sold loot at '{atSettlement.Name}' +{gained}d (funds={_teamFunds})");
            }
            catch (Exception ex) { Logger.Warn($"sell-loot tick threw: {ex.Message}"); }

            // 2) 食物 < 1 天 → 买 3 天食物（受资金限制）
            try
            {
                float daysLeft = PartyEconomyHelper.FoodDaysRemaining(self);
                if (daysLeft < BuyFoodWhenBelowDays && _teamFunds > 0)
                {
                    int spent = BuyFoodAtSettlement(self, atSettlement, BuyFoodDays);
                    if (spent > 0)
                        Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' food low ({daysLeft:F1}d) → bought at '{atSettlement.Name}' for {spent}d (funds={_teamFunds})");
                }
            }
            catch (Exception ex) { Logger.Warn($"food top-up tick threw: {ex.Message}"); }
        }
        else
        {
            float daysLeft = PartyEconomyHelper.FoodDaysRemaining(self);
            if (daysLeft < BuyFoodWhenBelowDays)
                Logger.Info($"[DIAG] StPatrolParty: '{PartyNameFormatter.SafeName(self)}' food low ({daysLeft:F1}d) but not at settlement — waiting until next stop");
        }

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
                            try { self.SetMoveGoToSettlement(nextEarly, MobileParty.NavigationType.Default, false); }
                            catch (Exception ex) { Logger.Error($"early-return SetMoveGoToSettlement failed", ex); }
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
        // 2026-05-18 修复：要求 CurrentSettlement==visited，即 party 真停在 settlement 里才算"到达"。
        // 此前 visited=LastVisitedSettlement 在出发后仍指向 home，导致刚出发的巡逻队第一个 tick 误判
        // 为"到达 home"，把 home 当首站 RecordVisit + PickNext。
        var visited = self.CurrentSettlement ?? self.LastVisitedSettlement;
        bool actuallyAtVisited = visited != null && self.CurrentSettlement == visited;
        Logger.Info($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' arrival-check: visited='{visited?.Name?.ToString() ?? "null"}' actuallyAt={actuallyAtVisited} visitedOwner='{visited?.OwnerClan?.StringId ?? "null"}' vs mgrOwner='{capitalMgr.OwnerClan?.StringId ?? "null"}'");
        if (actuallyAtVisited && visited!.OwnerClan == capitalMgr.OwnerClan
            && scheduler.TryMarkArrival(self, visited))
        {
            scheduler.RecordVisit(visited);
            // R4：到访新 settlement = 真进展 → 卡死累计清零
            _stuckActive = false;
            var next = scheduler.PickNextStop(self);
            if (next != null)
            {
                try { self.SetMoveGoToSettlement(next, MobileParty.NavigationType.Default, false); }
                catch (Exception ex) { Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(next)}'", ex); }
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived '{PartyNameFormatter.SafeName(visited)}', next='{PartyNameFormatter.SafeName(next)}'");
            }
            else
            {
                // 2026-05-18 修复：原先 next==null 时 fallback 到 capital，导致氏族小（候选 < 2）
                // 时巡逻队"到 village 立刻回 home"。改为停留 — 下个 tick MinVisitGap 过期后 PickNext 会重试。
                Logger.Info($"[DIAG] StPatrolParty: '{PartyNameFormatter.SafeName(self)}' arrived '{PartyNameFormatter.SafeName(visited)}', no next candidate (MinVisitGap 内) — staying, next tick retry");
            }
            return;
        }
        Logger.Info($"[DIAG] StPatrolParty.Core '{PartyNameFormatter.SafeName(self)}' arrival-check failed (not at any settlement or already-marked) — falling through to stuck check");

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
                        return;
                    }
                }
                catch (Exception tpEx) { Logger.Error($"二段瞬移失败 for '{PartyNameFormatter.SafeName(self)}'", tpEx); }
            }

            if (stuckNow)
            {
                var next = scheduler.PickNextStop(self);
                var dest = next ?? capital;
                try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
                catch (Exception ex) { Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(dest)}'", ex); }
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
