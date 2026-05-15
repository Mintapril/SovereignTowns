using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
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
/// 巡逻队 Order 枚举（B7.26 简化版）。
/// </summary>
public enum PatrolOrder
{
    /// <summary>主动防守某座被围攻的同氏族城（attack-initiative 0.3，avoid 0.7）。</summary>
    Defense = 0,
    /// <summary>回城并入驻军（首府被围 / 队员过少时）。</summary>
    MergeGarrison = 2,
    /// <summary>默认 — scheduler 决定下一站，巡视整个氏族领土。</summary>
    Patrol = 3,
    /// <summary>兵员伤亡过多 — 回首府治疗。</summary>
    Heal = 5
}

/// <summary>
/// B7.26：监管受管 clan（玩家氏族 + 可选 AI 氏族，通过 CapitalRegistry）的巡逻队（vanilla
/// <see cref="PatrolPartyComponent"/>）。
///
/// 设计原则：
///   - 仅触碰 _capitalRegistry 中受管 clan 拥有的巡逻队；clan 过滤由 GetForSettlement 提供；
///   - 所有方法 try-catch，绝不向调用方抛异常；
///   - 派遣集中在该 clan 的首府（OnHourlyTickSettlement 只在 settlement == 首府时创建新队）；
///   - 路径选择委托给该 clan 的 <see cref="ClanPatrolScheduler"/>（全氏族范围、最久未访问 + 距离评分、多队预占互补）；
///   - 防御响应：scheduler.GetDefenseTarget 返回被围攻的同氏族城；首府被围 → MergeGarrison，否则 → Defense。
///
/// vanilla 注意：PatrolPartyComponent 本身有一套自治 AI；我们对 <see cref="MobilePartyAi.SetInitiative"/> 的调用
/// 仅在 hoursUntilReset 窗口内生效，期满后 vanilla AI 接管。
/// </summary>
public sealed class PatrolManager
{
    private const int MinPatrolGarrisonRequired = 40;
    private const int PatrolTroopBatchSize = 15;

    /// <summary>Defense / Patrol 模式应用 SetInitiative 时的有效时长（小时）。</summary>
    private const float InitiativeResetHours = 4f;

    /// <summary>兵员不足触发 MergeGarrison 的阈值。</summary>
    private const int MinPartyMembersBeforeMerge = 5;

    /// <summary>Heal 状态触发的兵员阈值（&lt; 此值且伤兵比例 &gt; 1-HealHealthyRatioThreshold 时回家治疗）。</summary>
    private const int MinPartyMembersForHeal = 8;

    /// <summary>Heal 触发的健康兵员比例阈值（低于此值视为需要治疗）。</summary>
    private const float HealHealthyRatioThreshold = 0.6f;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;
    private readonly SovereignTowns.SallyForth.SallyForthManager? _sallyForthManager;  // B7.27：用于支援判定

    public PatrolManager(
        PartyLifecycleManager lifecycle,
        CapitalRegistry? capitalRegistry = null,
        SovereignTowns.SallyForth.SallyForthManager? sallyForthManager = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
        _sallyForthManager = sallyForthManager;
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

            // 当前 AI 接管不包含巡逻；避免 ApplyToAiSettlementsToo + AutoPatrol 时给 AI 生成 ST patrol。
            if (settlement.OwnerClan != Clan.PlayerClan) return;

            // B7.26：派遣集中在首府 — 只在 settlement == 首府时评估
            var capitalMgr = _capitalRegistry?.GetForSettlement(settlement);
            if (capitalMgr == null) return;  // 该 settlement 不属任何受管 clan
            var capital = capitalMgr.GetCapitalSettlement();
            if (capital == null || settlement != capital) return;  // 不是该 clan 的首府 → 跳过

            TryCreatePatrolParty(settlement);
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolManager.OnHourlyTickSettlement failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    /// <summary>
    /// HourlyTickPartyEvent 转发（B7.26 重写）：
    ///   1. 兵员过少 merge：单独检查；
    ///   2. 防御优先：scheduler.GetDefenseTarget — 首府被围 → MergeGarrison；非首府被围 → Defense；
    ///   3. Heal 检查：兵员低且健康差 → 回首府治疗；
    ///   4. 抵达侦测：刚到一个新 settlement → RecordVisit + PickNextStop；
    ///   5. 卡死保护：超出 stuckTimeout 仍未抵达 → 强制重选。
    /// </summary>
    public void OnHourlyTickParty(MobileParty party)
    {
        if (party == null) return;
        var pp = party.PartyComponent as PatrolPartyComponent;
        if (pp == null) return;

        try
        {
            if (!party.IsActive) return;
            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol) return;

            var home = pp.HomeSettlement;
            if (home == null) return;
            if (home.OwnerClan != Clan.PlayerClan) return;

            var capitalMgr = _capitalRegistry?.GetForSettlement(home);
            if (capitalMgr == null) return;  // home 已易主或不再受管
            var scheduler = capitalMgr.PatrolScheduler;
            var capital = capitalMgr.GetCapitalSettlement();

            int members = PartyNameFormatter.SafeMemberCount(party);

            // 1) 自动 merge：兵员过少
            if (members < MinPartyMembersBeforeMerge)
            {
                Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' members={members} < {MinPartyMembersBeforeMerge} — auto MergeGarrison");
                HandleMergeGarrison(party, capital ?? home);
                return;
            }

            // 2) 防御响应（B7.26）
            var defenseTarget = scheduler.GetDefenseTarget(party);
            if (defenseTarget != null)
            {
                // 防御 mid-tick 易主：再校验一次目标仍属本氏族（罕见但 vanilla 在围攻进程中可能 fire ChangeOwner）
                if (defenseTarget.OwnerClan != capitalMgr.OwnerClan)
                {
                    Logger.Warn($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' defense target '{PartyNameFormatter.SafeName(defenseTarget)}' flipped owner mid-tick — skip");
                    // fall through to normal tick logic
                }
                else if (capital != null && defenseTarget == capital)
                {
                    Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' capital '{PartyNameFormatter.SafeName(capital)}' under siege — MergeGarrison");
                    HandleMergeGarrison(party, capital);
                    return;
                }
                else
                {
                    Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' defending '{PartyNameFormatter.SafeName(defenseTarget)}' (under siege)");
                    ApplyOrderToParty(party, defenseTarget, PatrolOrder.Defense);
                    return;
                }
            }

            // ★ 3) 支援出击战斗（B7.27 新增）
            if (_sallyForthManager != null)
            {
                var supportSally = FindSupportableSallyBattle(party, capitalMgr);
                if (supportSally != null)
                {
                    Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' supporting sally '{PartyNameFormatter.SafeName(supportSally)}' (ETA < {ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours:F1}h)");
                    SafeSetMoveEngageParty(party, supportSally);
                    return;
                }
            }

            // 4) Heal 检查
            if (ShouldHeal(party, members))
            {
                Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' members={members} healthy_ratio low — Heal");
                ApplyHealOrder(party, capital ?? home);
                return;
            }

            // 5) 抵达侦测 → RecordVisit + PickNextStop
            var visited = party.LastVisitedSettlement;
            if (visited != null && visited.OwnerClan == capitalMgr.OwnerClan
                && scheduler.TryMarkArrival(party, visited))
            {
                scheduler.RecordVisit(visited);
                var next = scheduler.PickNextStop(party);
                var dest = next ?? capital ?? home;
                SafeSetMoveGoToSettlement(party, dest);
                Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' arrived '{PartyNameFormatter.SafeName(visited)}', next='{PartyNameFormatter.SafeName(dest)}'");
                return;
            }

            // 6) 卡死保护
            var stuckTimeout = ConfigurationManager.Current.ClanPatrol.StuckTimeoutHours;
            if (scheduler.IsStuck(party, stuckTimeout))
            {
                var next = scheduler.PickNextStop(party);
                var dest = next ?? capital ?? home;
                SafeSetMoveGoToSettlement(party, dest);
                Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' stuck > {stuckTimeout}h — re-pick next='{PartyNameFormatter.SafeName(dest)}'");
                return;
            }

            // 否则：让 vanilla AI 继续按已设置的目标走，不打扰
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolManager.OnHourlyTickParty failed for '{PartyNameFormatter.SafeName(party)}'", ex);
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
                    BattleLootHandler.ProcessPartyLoot(mp, _capitalRegistry);
                }
                catch (Exception ex)
                {
                    Logger.Error($"PatrolManager: BattleLootHandler.ProcessPartyLoot threw for '{PartyNameFormatter.SafeName(mp)}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("PatrolManager.HandleSideLoot iteration failed", ex);
        }
    }

    // ────────── 内部辅助：根据 Order 在 party tick 中下达指令 ──────────

    private void ApplyOrderToParty(MobileParty party, Settlement target, PatrolOrder order)
    {
        switch (order)
        {
            case PatrolOrder.Defense:
                SafeSetMoveDefendSettlement(party, target);
                SafeSetInitiative(party, attack: 0.3f, avoid: 0.7f, hours: InitiativeResetHours);
                Logger.Debug($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' applied Defense (target='{target.Name}')");
                break;

            case PatrolOrder.Heal:
                ApplyHealOrder(party, target);
                break;

            case PatrolOrder.MergeGarrison:
                HandleMergeGarrison(party, target);
                break;

            case PatrolOrder.Patrol:
                // B7.26：默认 Patrol 走 scheduler，由 OnHourlyTickParty 直接调度；
                // 如果某调用方误传 Patrol 进来，记一条 Warn 便于诊断（不阻塞）。
                Logger.Warn($"PatrolManager.ApplyOrderToParty: unexpected PatrolOrder.Patrol for '{PartyNameFormatter.SafeName(party)}' — no-op");
                break;
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
                Logger.Debug($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' returning home '{home.Name}' for MergeGarrison");
                return;
            }

            // 已到家：转兵 + 解散
            TransferAndDisband(party, home);
        }
        catch (Exception ex)
        {
            Logger.Error($"HandleMergeGarrison failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    private void TransferAndDisband(MobileParty patrol, Settlement home)
    {
        try
        {
            // 兜底层 B：disband 前先处置战利品（捕捉 MapEventEnded 路径漏网情况）
            try { BattleLootHandler.ProcessPartyLoot(patrol, _capitalRegistry); }
            catch (Exception lootEx) { Logger.Error($"PatrolManager.TransferAndDisband: ProcessPartyLoot threw for '{PartyNameFormatter.SafeName(patrol)}'", lootEx); }

            var town = home.Town;
            if (town == null)
            {
                Logger.Warn($"PatrolManager: '{PartyNameFormatter.SafeName(patrol)}' at non-town '{home.Name}', direct disband");
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
            Logger.Info($"PatrolManager: '{PartyNameFormatter.SafeName(patrol)}' merged {transferred} troops into '{home.Name}' garrison, disbanding");

            SafeDisband(patrol);
        }
        catch (Exception ex)
        {
            Logger.Error($"TransferAndDisband failed for '{PartyNameFormatter.SafeName(patrol)}'", ex);
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
            Logger.Error($"SafeDisband failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    // ────────── 创建巡逻队 ──────────

    private void TryCreatePatrolParty(Settlement settlement)
    {
        try
        {
            // B7.16：cap 来自 town 的兵营建筑（settlement_garrison）等级 + 1。
            // 统计该 settlement 的 vanilla + ST 巡逻队总数；只有 < cap 才允许再创建。
            int cap = _lifecycle.GetCapFor(settlement, PartyLifecycleManager.KindPatrol);
            int existing = CountExistingPatrolsAtHome(settlement);
            if (existing >= cap)
            {
                Logger.Debug($"PatrolManager: '{settlement.Name}' patrols={existing}/{cap} (cap from barracks lvl) — skip");
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

            // 模板兜底链：先尝试若干常见 stringId
            // B7.21 Fix C：vanilla CreatePatrolParty 在 v1.3.15 对 null 模板会 NRE。
            // 之前传 null 试一次的兜底导致每秒一个 NRE 填日志洪水。改为：模板=null → 直接早退，
            // 警告级日志只在第一次 hourly tick 出现一次（后续 tick 重新评估）。
            PartyTemplateObject? template = TryFindPatrolTemplate(settlement, out var templateId);
            if (template == null)
            {
                Logger.Warn($"PatrolManager: '{settlement.Name}' 找不到 PatrolPartyTemplate（候选 stringId 全 miss），跳过本次创建");
                return;
            }

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
                    template: template);
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
                        SafeSetMoveGoToSettlement(created, nextStop);
                        Logger.Info($"PatrolManager: '{created.Name}' first hop -> '{nextStop.Name}'");
                    }
                }
            }
            catch (Exception schedEx)
            {
                Logger.Error("PatrolManager: scheduler first-hop assignment failed (party will idle until next tick)", schedEx);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"PatrolManager.TryCreatePatrolParty failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    /// <summary>
    /// B7.16：统计某 settlement 当前活跃的 PatrolPartyComponent 总数（vanilla + ST 创建的都算）。
    /// 用于按 barracks 等级缩放的 cap 判定。
    /// </summary>
    private static int CountExistingPatrolsAtHome(Settlement settlement)
    {
        try
        {
            int count = 0;
            var all = MobileParty.AllPatrolParties;
            if (all == null) return 0;
            foreach (var p in all)
            {
                if (p == null || !p.IsActive) continue;
                var pp = p.PartyComponent as PatrolPartyComponent;
                if (pp == null) continue;
                if (pp.HomeSettlement == settlement) count++;
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
    /// 尝试解析一个可用的 PatrolPartyComponent 模板。B7.21 修：vanilla v1.3.15 的实际 stringId 是
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

    // B2 重构（2026-05-14）：实现搬运至 SovereignTowns.Common.TroopTransferHelper；
    // 排序策略：LowestTierFirst（保留城内精锐镇守，巡逻队走低 Tier）。
    // 注：原实现无显式 sort（依 roster 自然顺序，通常已近低→高）；
    //     现固定为 LowestTierFirst，行为对玩家不可观测地等价。
    private static int TransferTroopsFromGarrison(MobileParty garrison, MobileParty patrol, int batchSize)
    {
        var gRoster = garrison?.MemberRoster;
        var pRoster = patrol?.MemberRoster;
        if (gRoster == null || pRoster == null) return 0;
        return TroopTransferHelper.TransferFromGarrison(
            gRoster, pRoster, batchSize, TroopTransferHelper.SortStrategy.LowestTierFirst);
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
            Logger.Error($"ShouldHeal failed for '{PartyNameFormatter.SafeName(party)}'", ex);
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
                Logger.Debug($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' already at home '{home.Name}' — Heal in place");
                return;
            }
            SafeSetMoveGoToSettlement(party, home);
            Logger.Debug($"PatrolManager: '{PartyNameFormatter.SafeName(party)}' applied Heal (returning to home='{home.Name}')");
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplyHealOrder failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(home)}'", ex);
        }
    }

    // ────────── B7.27：支援判定 ──────────

    /// <summary>
    /// B7.27：判定本 patrol 是否能在某 sally 战斗结束前抵达。返回最近的可支援目标，无则 null。
    /// 简单算法：ETA = 距离 / 速度，ETA &lt; SupportEtaThresholdHours 即可。
    /// </summary>
    private MobileParty? FindSupportableSallyBattle(MobileParty patrol, Capital.CapitalManager capitalMgr)
    {
        try
        {
            if (_sallyForthManager == null) return null;
            var threshold = ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours;
            var sallies = _sallyForthManager.GetActiveCombatSallyParties(capitalMgr.OwnerClan);
            if (sallies.Count == 0) return null;

            var partyPos = patrol.GetPosition2D;
            float partySpeed = Math.Max(patrol.Speed, 0.1f);

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

    // ────────── 包装：安全调用 vanilla API ──────────

    /// <summary>B7.27：安全包装 vanilla SetMoveEngageParty。</summary>
    private static void SafeSetMoveEngageParty(MobileParty party, MobileParty target)
    {
        try
        {
            party.SetMoveEngageParty(target, MobileParty.NavigationType.Default);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveEngageParty failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(target)}'", ex);
        }
    }

    private static void SafeSetMoveGoToSettlement(MobileParty party, Settlement home)
    {
        try
        {
            party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(home)}'", ex);
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
            Logger.Error($"SetMoveDefendSettlement failed for '{PartyNameFormatter.SafeName(party)}' -> '{PartyNameFormatter.SafeName(home)}'", ex);
            // 退而求其次：至少让它回 settlement，避免完全无指令
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
