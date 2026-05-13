using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Patrol;

/// <summary>
/// 巡逻队 Order 枚举。每个玩家自有 town 维护一个 Order，随风险态势切换。
/// </summary>
public enum PatrolOrder
{
    /// <summary>待在城内 + 风险高时主动出击附近敌军（attack-initiative 偏低，avoid 较高，仍倾向回家）。</summary>
    Defense = 0,
    /// <summary>跟随商队 / 村民队（粗实现：附近 1 个友方非战斗队伍）。MVP 简化为留城待命。</summary>
    Escort = 1,
    /// <summary>回城并入驻军；仿 RecruitmentManager.TransferAndDisband 模式。</summary>
    MergeGarrison = 2,
    /// <summary>默认 — 在 homeSettlement 周围转。</summary>
    Patrol = 3,
    /// <summary>当玩家成为目标时停止干预（DisableForHours）。</summary>
    StopIfPlayerTarget = 4,
    /// <summary>兵员伤亡过多 — 回家治疗整补（仍未触发自动 merge）。</summary>
    Heal = 5
}

/// <summary>
/// MVP 4：监管玩家自有 town 的巡逻队（vanilla <see cref="PatrolPartyComponent"/>）。
///
/// 与 RecruitmentManager / GarrisonTransferManager 共同的设计原则：
///   - 仅触碰 OwnerClan == PlayerClan 且通过本 Mod 创建（向 PartyLifecycleManager 注册 kind="patrol"）的队伍；
///   - 所有方法 try-catch，绝不向调用方抛异常；
///   - 在 SettlementHourlyTick 重评估风险并更新 _orders[settlement]；
///   - 在 HourlyTickParty 把当前 Order 翻译成 vanilla AI 调用（SetMoveGoToSettlement / SetInitiative / DisableForHours）。
///
/// vanilla 注意：PatrolPartyComponent 本身有一套自治 AI；我们对 <see cref="MobilePartyAi.SetInitiative"/> 的调用
/// 仅在 hoursUntilReset 窗口内生效，期满后 vanilla AI 接管。
/// </summary>
public sealed class PatrolManager
{
    private const int MinPatrolGarrisonRequired = 40;
    private const int PatrolTroopBatchSize = 15;

    /// <summary>"巡逻太远了"判定阈值（单位与 Vec2 距离一致，地图格）。</summary>
    private const float PatrolRadius = 25f;

    /// <summary>Defense / Patrol 模式应用 SetInitiative 时的有效时长（小时）。</summary>
    private const float InitiativeResetHours = 4f;

    /// <summary>StopIfPlayerTarget 模式禁用 AI 的小时数。</summary>
    private const int StopIfPlayerTargetDisableHours = 4;

    /// <summary>兵员不足触发 MergeGarrison 的阈值。</summary>
    private const int MinPartyMembersBeforeMerge = 5;

    /// <summary>Heal 状态触发的兵员阈值（&lt; 此值且伤兵比例 &gt; 1-HealHealthyRatioThreshold 时回家治疗）。</summary>
    private const int MinPartyMembersForHeal = 8;

    /// <summary>Heal 触发的健康兵员比例阈值（低于此值视为需要治疗）。</summary>
    private const float HealHealthyRatioThreshold = 0.6f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalManager? _capitalManager;

    private readonly Dictionary<Settlement, PatrolOrder> _orders = new Dictionary<Settlement, PatrolOrder>();

    public PatrolManager(PartyLifecycleManager lifecycle, CapitalManager? capitalManager = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalManager = capitalManager;
    }

    /// <summary>当前 settlement 的 Order（无记录默认 Patrol）。</summary>
    public PatrolOrder GetOrder(Settlement settlement)
    {
        if (settlement == null) return PatrolOrder.Patrol;
        return _orders.TryGetValue(settlement, out var o) ? o : PatrolOrder.Patrol;
    }

    /// <summary>
    /// HourlyTickSettlementEvent 转发：评估风险并切 Order；必要时创建新巡逻队。
    /// 首行做强过滤，绝不触碰非玩家 town。
    /// </summary>
    public void OnHourlyTickSettlement(Settlement settlement)
    {
        // 用户原话："每个城镇"都创建巡逻队 — 仅 town，不含 castle。
        if (settlement == null) return;
        if (!settlement.IsTown) return;
        if (settlement.OwnerClan != Clan.PlayerClan) return;

        try
        {
            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
            {
                return;
            }

            // 系统启用判定：未设首府（玩家无 town） → 系统关闭，全部 settlement 不创建巡逻队
            var capital = _capitalManager?.GetCapital();
            if (capital == null) return;                  // 系统关闭（玩家无 town），所有 town 都不创建
            // settlement 是玩家自有 Town，capital != null（系统启用）→ 进入巡逻评估
            // 不再过滤 settlement != capital — 现在每个玩家自有 town 都参与

            // 1) 评估风险并决定 newOrder
            var risk = RiskAssessmentService.Assess(settlement);
            var newOrder = DecideOrder(settlement, risk);

            // 2) Order 变化 → 写审计 + 应用到现有巡逻队
            var hadOrder = _orders.TryGetValue(settlement, out var prev);
            if (!hadOrder || prev != newOrder)
            {
                _orders[settlement] = newOrder;

                var prevName = hadOrder ? prev.ToString() : "<none>";
                DecisionAuditLogger.LogRule(
                    decisionType: "set_patrol_order",
                    inputSummary: $"home={settlement.StringId} risk={risk.Level} ratio={risk.Score:F2} reason={risk.Reason}",
                    decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"prev\":\"{prevName}\",\"next\":\"{newOrder}\",\"riskLevel\":\"{risk.Level}\"}}",
                    accepted: true);
                Logger.Info($"PatrolManager: '{settlement.Name}' order {prevName} -> {newOrder} (risk={risk.Level}, ratio={risk.Score:F2})");

                // 立即把新 Order 应用到（如果存在的）巡逻队
                ApplyOrderToExistingPatrol(settlement, newOrder);
            }

            // 3) 创建巡逻队：仅当尚无巡逻 + 驻军充足 + lifecycle 上限未满
            TryCreatePatrolParty(settlement);
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolManager.OnHourlyTickSettlement failed for '{SafeName(settlement)}'", ex);
        }
    }

    /// <summary>
    /// HourlyTickPartyEvent 转发：把 Order 翻译为 vanilla AI 行为。
    /// 仅处理 vanilla <see cref="PatrolPartyComponent"/> 队伍，且其 HomeSettlement 必须是玩家自有 town。
    /// </summary>
    public void OnHourlyTickParty(MobileParty party)
    {
        // 严格过滤
        if (party == null) return;
        var pp = party.PartyComponent as PatrolPartyComponent;
        if (pp == null) return;

        try
        {
            if (!party.IsActive) return;

            var home = pp.HomeSettlement;
            if (home == null) return;
            if (home.OwnerClan != Clan.PlayerClan) return;

            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol) return;

            // 自动 merge：兵员过少
            var members = SafeMemberCount(party);
            if (members < MinPartyMembersBeforeMerge)
            {
                Logger.Info($"PatrolManager: '{SafeName(party)}' members={members} < {MinPartyMembersBeforeMerge} — auto MergeGarrison");
                _orders[home] = PatrolOrder.MergeGarrison;
                HandleMergeGarrison(party, home);
                return;
            }

            // Heal：兵员低于阈值 + 健康比例过低 → 回家治疗（per-party 决策，不写入 settlement._orders）
            if (ShouldHeal(party, members))
            {
                Logger.Info($"PatrolManager: '{SafeName(party)}' members={members} healthy_ratio low — switching to Heal (home='{home.Name}')");
                ApplyHealOrder(party, home);
                return;
            }

            var order = GetOrder(home);
            ApplyOrderToParty(party, home, order);
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolManager.OnHourlyTickParty failed for '{SafeName(party)}'", ex);
        }
    }

    // ────────── MapEventEnded：战后立即处置战利品（层 A） ──────────

    /// <summary>
    /// 战斗结束回调：扫 attacker / defender 两侧，找到玩家自有的 <see cref="PatrolPartyComponent"/> party，
    /// 立即调 <see cref="BattleLootHandler.ProcessPartyLoot"/> 处置战利品。
    /// 与 SallyForthManager.OnMapEventEnded 结构对称；try-catch 包裹避免影响 vanilla 事件链。
    /// </summary>
    public void OnMapEventEnded(MapEvent mapEvent)
    {
        if (mapEvent == null) return;
        try
        {
            HandleSideLoot(mapEvent.AttackerSide);
            HandleSideLoot(mapEvent.DefenderSide);
        }
        catch (Exception ex)
        {
            Logger.Error("PatrolManager.OnMapEventEnded failed", ex);
        }
    }

    private void HandleSideLoot(MapEventSide? side)
    {
        if (side == null) return;
        try
        {
            var parties = side.Parties;
            if (parties == null) return;

            foreach (var uop in parties)
            {
                MobileParty? mp = null;
                try { mp = uop.Party?.MobileParty; }
                catch { continue; }
                if (mp == null) continue;
                if (!mp.IsActive) continue;
                if (mp.ActualClan != Clan.PlayerClan) continue;
                if (mp.PartyComponent is not PatrolPartyComponent) continue;

                try
                {
                    BattleLootHandler.ProcessPartyLoot(mp, _capitalManager);
                }
                catch (Exception ex)
                {
                    Logger.Error($"PatrolManager: BattleLootHandler.ProcessPartyLoot threw for '{SafeName(mp)}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("PatrolManager.HandleSideLoot iteration failed", ex);
        }
    }

    // ────────── 内部辅助：Order 决策 ──────────

    private static PatrolOrder DecideOrder(Settlement settlement, RiskAssessment risk)
    {
        if (settlement.IsUnderSiege) return PatrolOrder.MergeGarrison;
        if (risk.Level >= RiskLevel.High) return PatrolOrder.Defense;
        // Safe / Low / Medium → 默认巡逻
        return PatrolOrder.Patrol;
    }

    // ────────── 内部辅助：把 Order 应用到 settlement 的现有巡逻队 ──────────

    private void ApplyOrderToExistingPatrol(Settlement settlement, PatrolOrder order)
    {
        try
        {
            if (settlement == null) return;
            var pp = settlement.PatrolParty;
            if (pp == null) return;

            // PatrolPartyComponent.PartyOwner 等是属性，找到对应 MobileParty 需要通过 MobileParty.PatrolPartyComponent 反查；
            // 这里采用最稳妥的方式：扫一遍 MobileParty.AllPartiesWithoutPartiesInArmy / All 列表很贵，
            // 我们直接利用下一个 HourlyTickParty 应用 Order — 简单且足够。
            // 这里仅做日志，真正的应用在 OnHourlyTickParty。
            Logger.Debug($"PatrolManager: order {order} queued for '{SafeName(settlement)}' — will apply on next HourlyTickParty");
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplyOrderToExistingPatrol failed for '{SafeName(settlement)}'", ex);
        }
    }

    // ────────── 内部辅助：根据 Order 在 party tick 中下达指令 ──────────

    private void ApplyOrderToParty(MobileParty party, Settlement home, PatrolOrder order)
    {
        switch (order)
        {
            case PatrolOrder.Defense:
                // v1.3.15 真实 API: SetMoveDefendSettlement(Settlement, bool isAvoidingTrouble, NavigationType)
                // 该调用真正切换 vanilla AI 状态机为"防守此 settlement"——不再只是 SetInitiative 倾向。
                SafeSetMoveDefendSettlement(party, home);
                SafeSetInitiative(party, attack: 0.3f, avoid: 0.7f, hours: InitiativeResetHours);
                Logger.Debug($"PatrolManager: '{SafeName(party)}' applied Defense (home='{home.Name}')");
                break;

            case PatrolOrder.Heal:
                // 安全网：settlement._orders 不会直接写入 Heal（per-party 决策走 ApplyHealOrder），
                // 但若外部代码将 settlement order 设为 Heal，仍按 Heal 行为执行。
                ApplyHealOrder(party, home);
                break;

            case PatrolOrder.Patrol:
                // 默认：若没有目标或目标已离 home 太远，重新指回 home（让 vanilla AI 接管巡逻）
                if (NeedsRedirect(party, home))
                {
                    SafeSetMoveGoToSettlement(party, home);
                }
                Logger.Debug($"PatrolManager: '{SafeName(party)}' applied Patrol (home='{home.Name}')");
                break;

            case PatrolOrder.MergeGarrison:
                HandleMergeGarrison(party, home);
                break;

            case PatrolOrder.StopIfPlayerTarget:
                if (IsTargetingPlayer(party))
                {
                    try { party.Ai?.DisableForHours(StopIfPlayerTargetDisableHours); }
                    catch (Exception ex) { Logger.Error($"DisableForHours failed for '{SafeName(party)}'", ex); }
                    Logger.Info($"PatrolManager: '{SafeName(party)}' targeting MainParty — DisableForHours({StopIfPlayerTargetDisableHours})");
                }
                break;

            case PatrolOrder.Escort:
                // MVP 简化：留城待命
                SafeSetMoveGoToSettlement(party, home);
                Logger.Debug($"PatrolManager: '{SafeName(party)}' applied Escort (留城待命, home='{home.Name}')");
                break;
        }
    }

    private static bool NeedsRedirect(MobileParty party, Settlement home)
    {
        try
        {
            var target = party.TargetSettlement;
            if (target == null) return true;
            // 与 home 距离过大 → 拉回
            var homePos = home.GetPosition2D;
            var partyPos = party.GetPosition2D;
            var distance = (partyPos - homePos).Length;
            return distance > PatrolRadius;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTargetingPlayer(MobileParty party)
    {
        try
        {
            var behaviorPb = party.Ai?.AiBehaviorPartyBase;
            if (behaviorPb == null) return false;
            return behaviorPb.MobileParty == MobileParty.MainParty;
        }
        catch
        {
            return false;
        }
    }

    // ────────── MergeGarrison ──────────

    private void HandleMergeGarrison(MobileParty party, Settlement home)
    {
        try
        {
            // 还没回到 home → 让 vanilla 把它带回去
            if (party.LastVisitedSettlement != home)
            {
                SafeSetMoveGoToSettlement(party, home);
                Logger.Debug($"PatrolManager: '{SafeName(party)}' returning home '{home.Name}' for MergeGarrison");
                return;
            }

            // 已到家：转兵 + 解散
            TransferAndDisband(party, home);
        }
        catch (Exception ex)
        {
            Logger.Error($"HandleMergeGarrison failed for '{SafeName(party)}'", ex);
        }
    }

    private void TransferAndDisband(MobileParty patrol, Settlement home)
    {
        try
        {
            // 兜底层 B：disband 前先处置战利品（捕捉 MapEventEnded 路径漏网情况）
            try { BattleLootHandler.ProcessPartyLoot(patrol, _capitalManager); }
            catch (Exception lootEx) { Logger.Error($"PatrolManager.TransferAndDisband: ProcessPartyLoot threw for '{SafeName(patrol)}'", lootEx); }

            var town = home.Town;
            if (town == null)
            {
                Logger.Warn($"PatrolManager: '{SafeName(patrol)}' at non-town '{home.Name}', direct disband");
                SafeDisband(patrol);
                return;
            }

            var garrison = town.GarrisonParty;
            var patrolRoster = patrol.MemberRoster;
            var transferred = 0;

            if (garrison?.MemberRoster != null && patrolRoster != null)
            {
                var elements = patrolRoster.GetTroopRoster();
                foreach (var elem in elements)
                {
                    if (elem.Character == null || elem.Character.IsHero) continue;
                    garrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                    transferred += elem.Number;
                }
                patrolRoster.RemoveIf(e => e.Character != null && !e.Character.IsHero);
            }

            DecisionAuditLogger.LogRule(
                decisionType: "merge_patrol_into_garrison",
                inputSummary: $"home={home.StringId} patrol={patrol.StringId} transferred={transferred}",
                decisionJson: $"{{\"home\":\"{home.StringId}\",\"patrol\":\"{patrol.StringId}\",\"transferred\":{transferred}}}",
                accepted: true);
            Logger.Info($"PatrolManager: '{SafeName(patrol)}' merged {transferred} troops into '{home.Name}' garrison, disbanding");

            SafeDisband(patrol);
        }
        catch (Exception ex)
        {
            Logger.Error($"TransferAndDisband failed for '{SafeName(patrol)}'", ex);
        }
    }

    private void SafeDisband(MobileParty party)
    {
        try
        {
            DisbandPartyAction.StartDisband(party);
            _lifecycle.UntrackParty(party);
        }
        catch (Exception ex)
        {
            Logger.Error($"SafeDisband failed for '{SafeName(party)}'", ex);
        }
    }

    // ────────── 创建巡逻队 ──────────

    private void TryCreatePatrolParty(Settlement settlement)
    {
        try
        {
            // 已有巡逻：跳过
            if (settlement.PatrolParty != null) return;

            // lifecycle 上限
            if (!_lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindPatrol))
            {
                return;
            }

            var town = settlement.Town;
            var garrison = town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;
            if (garrisonCount < MinPatrolGarrisonRequired)
            {
                Logger.Debug($"PatrolManager: '{settlement.Name}' garrison={garrisonCount} < {MinPatrolGarrisonRequired}, defer patrol creation");
                return;
            }

            // 模板兜底链：先尝试若干常见 stringId，最后退到 null
            // （vanilla CreatePatrolParty 在 v1.3.15 接受 null 模板的兼容性未保证；
            //  我们 try-catch 包住整个调用，模板查找全失败时也允许传 null 试一次。）
            PartyTemplateObject? template = TryFindPatrolTemplate(out var templateId);

            // 唯一 stringId
            var stringId = "st_patrol_" + settlement.StringId + "_" + DateTime.UtcNow.Ticks.ToString();

            MobileParty? created = null;
            try
            {
                created = PatrolPartyComponent.CreatePatrolParty(
                    stringId,
                    settlement.GatePosition,
                    spawnRadius: 1f,
                    homeSettlement: settlement,
                    template: template!);
            }
            catch (Exception createEx)
            {
                Logger.Error($"PatrolManager: CreatePatrolParty threw (template='{templateId}') for '{settlement.Name}'", createEx);
                return;
            }

            if (created == null)
            {
                Logger.Warn($"PatrolManager: CreatePatrolParty returned null for '{settlement.Name}' (template='{templateId}')");
                return;
            }

            // 从 garrison 抽 PatrolTroopBatchSize 名兵员（skip heroes）
            var moved = TransferTroopsFromGarrison(garrison!, created, PatrolTroopBatchSize);

            _lifecycle.RegisterTrackedParty(created, settlement, PartyLifecycleManager.KindPatrol);

            DecisionAuditLogger.LogRule(
                decisionType: "create_patrol_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} template={templateId} moved={moved}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{stringId}\",\"template\":\"{templateId}\",\"moved\":{moved}}}",
                accepted: true);
            Logger.Info($"PatrolManager: created patrol '{stringId}' for '{settlement.Name}' (template='{templateId}', moved={moved} troops)");
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolManager.TryCreatePatrolParty failed for '{SafeName(settlement)}'", ex);
        }
    }

    /// <summary>
    /// 尝试解析一个可用的 PatrolPartyComponent 模板；按候选 stringId 优先级查找，全失败返回 null。
    /// </summary>
    private static PartyTemplateObject? TryFindPatrolTemplate(out string idUsed)
    {
        // 这些 stringId 来自 vanilla XML 的常见模板；我们不强依赖任一存在。
        // 命中即用，全 miss 时返回 null（调用方对 null 也已 try-catch）。
        string[] candidates =
        {
            "empire_patrol_party",
            "patrol_party_template",
            "caravan_party_template",
            "villager_party_template"
        };

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

    private static int TransferTroopsFromGarrison(MobileParty garrison, MobileParty patrol, int batchSize)
    {
        try
        {
            var gRoster = garrison?.MemberRoster;
            var pRoster = patrol?.MemberRoster;
            if (gRoster == null || pRoster == null || batchSize <= 0) return 0;

            int remaining = batchSize;
            int moved = 0;

            var elements = gRoster.GetTroopRoster();
            // 拷贝快照，避免迭代时修改
            var snapshot = new List<TroopRosterElement>(elements);
            foreach (var elem in snapshot)
            {
                if (remaining <= 0) break;
                if (elem.Character == null || elem.Character.IsHero) continue;
                if (elem.Number <= 0) continue;

                int take = Math.Min(elem.Number, remaining);
                try
                {
                    gRoster.AddToCounts(elem.Character, -take, false, 0, 0);
                    pRoster.AddToCounts(elem.Character, take, false, 0, 0);
                    moved += take;
                    remaining -= take;
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"TransferTroopsFromGarrison: per-element transfer failed (char='{elem.Character?.StringId}', n={take})", oneEx);
                }
            }
            return moved;
        }
        catch (Exception ex)
        {
            Logger.Error("TransferTroopsFromGarrison failed", ex);
            return 0;
        }
    }

    // ────────── Heal ──────────

    /// <summary>
    /// 判定是否应进入 Heal 状态：
    ///   - members &lt; MinPartyMembersForHeal（仍 &gt;= MinPartyMembersBeforeMerge，否则上一步已 auto-merge）
    ///   - 健康兵员比例 &lt; HealHealthyRatioThreshold（伤员太多）
    /// 两个条件同时成立才触发，避免和正常 Patrol/Defense 抢占。
    /// </summary>
    private static bool ShouldHeal(MobileParty party, int members)
    {
        try
        {
            if (members <= 0) return false;
            if (members >= MinPartyMembersForHeal) return false;

            var partyBase = party.Party;
            if (partyBase == null) return false;

            int healthy = partyBase.NumberOfHealthyMembers;
            int total = members;
            float ratio = total > 0 ? (float)healthy / total : 1f;
            return ratio < HealHealthyRatioThreshold;
        }
        catch (Exception ex)
        {
            Logger.Error($"ShouldHeal failed for '{SafeName(party)}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Heal 行为：回 homeSettlement 治疗。
    /// 到达 settlement 后由 vanilla 的 garrison/治疗机制接管伤员恢复；本方法只负责把巡逻队送回家。
    /// </summary>
    private static void ApplyHealOrder(MobileParty party, Settlement home)
    {
        try
        {
            // 已在 home：什么都不做（vanilla 治疗逻辑自然生效）
            if (party.CurrentSettlement == home || party.LastVisitedSettlement == home)
            {
                Logger.Debug($"PatrolManager: '{SafeName(party)}' already at home '{home.Name}' — Heal in place");
                return;
            }
            SafeSetMoveGoToSettlement(party, home);
            Logger.Debug($"PatrolManager: '{SafeName(party)}' applied Heal (returning to home='{home.Name}')");
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplyHealOrder failed for '{SafeName(party)}' -> '{SafeName(home)}'", ex);
        }
    }

    // ────────── 包装：安全调用 vanilla API ──────────

    private static void SafeSetMoveGoToSettlement(MobileParty party, Settlement home)
    {
        try
        {
            party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveGoToSettlement failed for '{SafeName(party)}' -> '{SafeName(home)}'", ex);
        }
    }

    /// <summary>
    /// v1.3.15 SetMoveDefendSettlement(Settlement, bool isAvoidingTrouble, NavigationType)。
    /// 真正切换 vanilla AI 到"防守此 settlement"——这是 Defense Order 生效的关键。
    /// </summary>
    private static void SafeSetMoveDefendSettlement(MobileParty party, Settlement home)
    {
        try
        {
            party.SetMoveDefendSettlement(home, false, MobileParty.NavigationType.Default);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveDefendSettlement failed for '{SafeName(party)}' -> '{SafeName(home)}'", ex);
            // 退而求其次：至少让它回 settlement，避免完全无指令
            try
            {
                party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
            }
            catch (Exception fallbackEx)
            {
                Logger.Error($"Defense fallback SetMoveGoToSettlement also failed for '{SafeName(party)}'", fallbackEx);
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
            Logger.Error($"SetInitiative failed for '{SafeName(party)}'", ex);
        }
    }

    private static int SafeMemberCount(MobileParty party)
    {
        try { return party?.MemberRoster?.TotalManCount ?? 0; }
        catch { return 0; }
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
