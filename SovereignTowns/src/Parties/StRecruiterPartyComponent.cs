using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 征兵队伍组件（B16.3）。显式 RecruiterPhase 状态机（Dispatching / AtVillage / Travelling / Returning）。
///
/// 实例化字段 <see cref="_visitedThisTrip"/> 是 [SaveableField(23)] (B16.4a P1-2 修复)：
/// 由 [CachedData] 改回持久化以保证重启后候选评估不会推荐已访问村庄。
/// 改用 List&lt;Settlement&gt; 而非 HashSet 因为：(1) vanilla SaveSystem 不直接支持 HashSet 序列化，
/// (2) 单次招兵 trip 平均访问 &lt; 10 个村庄，O(n) Contains 完全够用。
/// 容器声明：见 SovereignTownsTypeDefiner.DefineContainerDefinitions。
///
/// SaveableField 槽位：基类占 [10, 20)；本类占 [20, 24)。
/// </summary>
public sealed class StRecruiterPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_recruit_";

    /// <summary>vanilla volunteer slot 上限放宽倍率（B7.20，硬编码 2.0）。</summary>
    private const float VolunteerMul = 2.0f;
    /// <summary>玩家氏族单兵金币折扣（B7.20，硬编码 0.5）。</summary>
    private const float CostDiscount = 0.5f;
    private const int DefaultGoldPerRecruit = 10;
    private const int CandidateBatchSizeDefault = 8;
    private static int CandidateBatchSize
        => ConfigurationManager.Current?.Thresholds?.RecruitmentCandidateBatchSize ?? CandidateBatchSizeDefault;

    public enum RecruiterPhase
    {
        Dispatching = 0,
        AtVillage = 1,
        Travelling = 2,
        Returning = 3,
    }

    [SaveableField(20)] private int _recruitedThisTrip;
    [SaveableField(21)] private Settlement? _assignedTarget;
    [SaveableField(22)] private RecruiterPhase _phase = RecruiterPhase.Dispatching;
    [SaveableField(23)] private List<Settlement> _visitedThisTrip = new List<Settlement>();
    [CachedData] private TextObject? _cachedName;

    private List<Settlement> VisitedThisTrip
    {
        get
        {
            // 读档兼容：旧存档中 _visitedThisTrip 为 null（字段不存在），lazy init 以维持非 null 不变式。
            if (_visitedThisTrip == null) _visitedThisTrip = new List<Settlement>();
            return _visitedThisTrip;
        }
    }

    private void MarkVisited(Settlement s)
    {
        if (s == null) return;
        var list = VisitedThisTrip;
        if (!list.Contains(s)) list.Add(s);
    }

    public int RecruitedThisTrip => _recruitedThisTrip;
    public Settlement? AssignedTarget => _assignedTarget;
    public RecruiterPhase Phase => _phase;

    private static int ReturnRecruitedCount
        => ConfigurationManager.Current?.Thresholds?.RecruiterReturnRecruitedCount ?? 50;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            // B16.4a P1-7：Name 必须容忍 _homeSettlement 为 null（损坏存档 / 序列化未完成时调用），
            // 用 HomeSettlementOrNull 而非 HomeSettlement 以避免抛 InvalidOperationException。
            var home = HomeSettlementOrNull;
            var n = new TextObject("{=ST_RecruiterPartyName}Recruiter — {SETTLEMENT}");
            n.SetTextVariable("SETTLEMENT",
                home?.Name ?? new TextObject("{=ST_Common_Unknown}unknown"));
            _cachedName = n;
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => true;

    protected override Economy.ExpenseCategory GetExpenseCategoryForKind() => Economy.ExpenseCategory.RecruiterSeed;

    public void RecordRecruited(int count) { if (count > 0) _recruitedThisTrip += count; }
    public void SetAssignedTarget(Settlement? target) => _assignedTarget = target;
    public void TransitionTo(RecruiterPhase phase) => _phase = phase;

    private StRecruiterPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
    }

    /// <summary>
    /// 工厂：创建征兵队伍。初始 escort 由 dispatcher 抽取后传入。
    /// SnapshotInitialMembers 在 MobileParty.CreateParty 之后立即调用。
    /// </summary>
    public static MobileParty? CreateForTown(Town homeTown, TroopRoster? initialEscort = null)
    {
        if (homeTown == null)
        {
            Logger.Error("StRecruiterPartyComponent.CreateForTown: homeTown is null");
            return null;
        }
        try
        {
            var settlement = homeTown.Settlement;
            if (settlement == null)
            {
                Logger.Error("StRecruiterPartyComponent.CreateForTown: homeTown.Settlement is null");
                return null;
            }
            var ownerClan = settlement.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"StRecruiterPartyComponent.CreateForTown: town '{settlement.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var startingTroops = initialEscort ?? TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(settlement.GatePosition, 1f, ownerClan, startingTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_RecruiterPartyName}Recruiter — {SETTLEMENT}");
            nameObj.SetTextVariable("SETTLEMENT", settlement.Name);

            var component = new StRecruiterPartyComponent(
                home: settlement, name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: true,
                args: args, leader: null);

            var stringId = StringIdPrefix + settlement.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"StRecruiterPartyComponent.CreateForTown: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }
            // B7.22：强制 0 攻击性 — 防自家征兵队主动招惹敌方
            try { mobileParty.Aggressiveness = 0f; } catch { /* swallow */ }
            // 2026-05-18 fix: 阻止 vanilla AI 在第一个 hourly tick 之前接管 ST recruiter（默认行为=回家）。
            try { mobileParty.Ai?.SetDoNotMakeNewDecisions(true); } catch { /* swallow */ }

            component.SnapshotInitialMembers(mobileParty);
            // T1.7：食物由 RecruitmentDispatcher 在创建后通过 BuyFoodAtSettlement(_teamFunds) 真实购买，
            // 不再凭空塞 3 天食物。
            Logger.Info($"StRecruiterPartyComponent: created '{stringId}' for '{settlement.StringId}'");
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StRecruiterPartyComponent.CreateForTown failed", ex);
            return null;
        }
    }

    // ── 状态机核心 ────────────────────────────────────────

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        Logger.Debug($"[DIAG] Recruiter.Core '{PartyNameFormatter.SafeName(self)}' phase={_phase} assignedTarget='{_assignedTarget?.Name?.ToString() ?? "null"}' recruited={_recruitedThisTrip} visited={VisitedThisTrip.Count}");
        switch (_phase)
        {
            case RecruiterPhase.Dispatching: HandleDispatching(self); break;
            case RecruiterPhase.AtVillage:   HandleAtVillage(self); break;
            case RecruiterPhase.Travelling:  HandleTravelling(self); break;
            case RecruiterPhase.Returning:   /* base.IsAtHome 接管 → OnArrivedHome → DefaultMergeAndDisband */ break;
        }
    }

    /// <summary>
    /// Dispatching：刚创建尚未首发，或异常情况下回到 Dispatching。
    /// ResolveDepartureTarget 取首站目标后切到 Travelling。
    /// </summary>
    private void HandleDispatching(MobileParty self)
    {
        // B16.4a P1-7：保留 null 防御 —— 用 OrNull 而非抛诊断异常版的 HomeSettlement。
        var home = HomeSettlementOrNull;
        if (home == null) return;
        var next = ResolveDepartureTarget(self, home);
        Logger.Debug($"[DIAG] Recruiter.HandleDispatching '{PartyNameFormatter.SafeName(self)}' home='{home.Name}' assignedTarget='{_assignedTarget?.Name?.ToString() ?? "null"}' resolved='{next?.Name?.ToString() ?? "null"}'");
        if (next == null || next == home)
        {
            // 无候选；下个 tick 再试
            return;
        }
        MoveTo(self, next, "Dispatching → first hop");
        _phase = RecruiterPhase.Travelling;
    }

    /// <summary>
    /// AtVillage：抵达非 home village → 招募 + 标记冷却 + 阈值检查 + 规划下一站。
    /// </summary>
    private void HandleAtVillage(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return;

        var currentSettlement = self.CurrentSettlement ?? self.LastVisitedSettlement;
        // 安全网：若实际不在 village（或目标已经变化），重新规划
        if (currentSettlement == null || !currentSettlement.IsVillage || currentSettlement == home)
        {
            // 可能 vanilla 已经把我们带离了村子（被推走 / 战斗等）；重新走 Travelling 逻辑
            _phase = RecruiterPhase.Travelling;
            HandleTravelling(self);
            return;
        }

        // 与 _assignedTarget 不一致 = 玩家或 vanilla 改路 / 强制回家。视为继续 travelling 处理。
        // 同时 — 若 vanilla TargetSettlement 已被基类 ReturnToHome 重定向到 home，
        // 不应执行招兵；走 Travelling 让基类的 IsAtHome 判定接管下次 tick。
        if (_assignedTarget != null && currentSettlement != _assignedTarget)
        {
            _phase = RecruiterPhase.Travelling;
            HandleTravelling(self);
            return;
        }
        if (self.TargetSettlement != null && self.TargetSettlement == home)
        {
            _phase = RecruiterPhase.Returning;
            return;
        }

        if (!IsRecruitmentTargetStillValid(currentSettlement, home))
        {
            Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 目标村庄 '{currentSettlement.Name}' 已不适合招募，重新规划");
            MarkVisited(currentSettlement);
            var replacement = PlanNextHop(self, home);
            MoveTo(self, replacement ?? home, "invalid arrived village");
            _phase = replacement != null && replacement != home ? RecruiterPhase.Travelling : RecruiterPhase.Returning;
            return;
        }

        int recruited = RecruitFromTargetVillage(self, currentSettlement, home);
        if (recruited > 0)
        {
            RecordRecruited(recruited);
            RecruitmentCooldown.MarkRecruited(currentSettlement);
        }
        MarkVisited(currentSettlement);

        // 2026-05-18 v3：在 just-arrived village 上下文中触发经济维护（卖战利品入资金 + 食物补给）。
        // 注意：currentSettlement 此处大概率是 village（HandleAtVillage 入口已校验 IsVillage），
        // 基类 TryEconomicMaintenance 内部仍要求 Town != null → 当前实现下 village 会被跳过，但日志
        // 行会清晰显示 isVillage=True hasTownComponent=False，下一步是否打开 village 经济由此 log 决定。
        try { TryEconomicMaintenance(self, currentSettlement); }
        catch (Exception econEx) { Logger.Warn($"Recruiter HandleAtVillage maintenance failed: {econEx.Message}"); }

        int returnThreshold = ReturnRecruitedCount;
        if (_recruitedThisTrip >= returnThreshold)
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 本趟招募 {_recruitedThisTrip} ≥ 阈值 {returnThreshold}，回 '{home.Name}'");
            MoveTo(self, home, "recruited threshold");
            _phase = RecruiterPhase.Returning;
            return;
        }

        var next = PlanNextHop(self, home);
        if (next != null && next != home)
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 巡回下一站：'{next.Name}' (此前 visited={VisitedThisTrip.Count})");
            MoveTo(self, next, "next village");
            _phase = RecruiterPhase.Travelling;
        }
        else
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 候选枯竭，回 '{home.Name}'");
            MoveTo(self, home, "no candidates");
            _phase = RecruiterPhase.Returning;
        }
    }

    /// <summary>
    /// Travelling：在路上或刚抵达村庄。
    /// 抵达分配的村庄 → 同 tick fall-through 到 HandleAtVillage（避免 1h 延迟）。
    /// 否则检查累计阈值 / 目标失效 / 风险高。
    /// </summary>
    private void HandleTravelling(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return;

        // 抵达 _assignedTarget（即 vanilla CurrentSettlement 或 LastVisitedSettlement 与 _assignedTarget 一致）
        // → 切到 AtVillage 并同 tick 处理（"到村即招"行为，避免 1h 延迟）。
        // 2026-05-18 修复 v2：原 `self.TargetSettlement == _assignedTarget` 太严格——日志铁证显示
        // vanilla 在 party 抵达 settlement 时会清空 TargetSettlement（"我到了"），导致征兵队永远卡在
        // Travelling，"没有当前目标，改去 'Jahasim'" 反复输出 24h 后被 idle force-return。
        // 防御 ReturnToHome 改成"只在 TargetSettlement 明确指向 home 时绕开"——见下方 Returning 短路。
        var currentSettlement = self.CurrentSettlement ?? self.LastVisitedSettlement;
        if (_assignedTarget != null
            && _assignedTarget != home
            && currentSettlement == _assignedTarget
            && currentSettlement.IsVillage
            && (self.TargetSettlement == null || self.TargetSettlement == _assignedTarget))
        {
            _phase = RecruiterPhase.AtVillage;
            HandleAtVillage(self);
            return;
        }

        var targetSettlement = self.TargetSettlement;

        // 累计阈值检查：即使没到下一个村也可能因之前累积已满
        int returnThreshold = ReturnRecruitedCount;
        if (_recruitedThisTrip >= returnThreshold && (targetSettlement == null || targetSettlement != home))
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 本趟招募达到阈值 {_recruitedThisTrip}/{returnThreshold}，回 '{home.Name}'");
            MoveTo(self, home, "road recruited threshold");
            _phase = RecruiterPhase.Returning;
            return;
        }

        if (targetSettlement == null)
        {
            var replacement = ResolveDepartureTarget(self, home);
            Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 没有当前目标，{(replacement != null && replacement != home ? $"改去 '{replacement.Name}'" : $"回 '{home.Name}'")}");
            MoveTo(self, replacement ?? home, "missing target");
            _phase = replacement != null && replacement != home ? RecruiterPhase.Travelling : RecruiterPhase.Returning;
            return;
        }

        // 目标失效 / 风险高 → 重新规划
        if (targetSettlement != home)
        {
            if (targetSettlement.IsVillage && !IsRecruitmentTargetStillValid(targetSettlement, home))
            {
                Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}': 目标 '{targetSettlement.Name}' 已失效，重新规划");
                MarkVisited(targetSettlement);
                var replacement = PlanNextHop(self, home);
                MoveTo(self, replacement ?? home, "invalid road target");
                _phase = replacement != null && replacement != home ? RecruiterPhase.Travelling : RecruiterPhase.Returning;
                return;
            }

            var risk = RiskAssessmentService.Assess(targetSettlement);
            if (risk.Level >= RiskLevel.High)
            {
                Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}': 目标 '{targetSettlement.Name}' risk={risk.Level}，重新规划");
                MarkVisited(targetSettlement);
                var replacement = PlanNextHop(self, home);
                MoveTo(self, replacement ?? home, "risky road target");
                _phase = replacement != null && replacement != home ? RecruiterPhase.Travelling : RecruiterPhase.Returning;
            }
        }
    }

    // ── helpers ────────────────────────────────────────────────

    private static bool MoveTo(MobileParty party, Settlement destination, string reason)
    {
        try
        {
            if (party?.PartyComponent is StRecruiterPartyComponent rp)
            {
                rp.SetAssignedTarget(destination);
            }
            // 2026-05-18 修复 v2：用 GoToWithLeave 以处理"已在 settlement 内 → 目标是别处"的情况。
            // SetDoNotMakeNewDecisions(true) 下 vanilla 不会自主 LeaveSettlement，必须显式触发。
            if (party != null) SovereignTowns.Common.SafeMoveHelper.GoToWithLeave(party, destination, $"recruiter MoveTo: {reason}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"  StRecruiterPartyComponent: MoveTo failed for '{PartyNameFormatter.SafeName(party)}' -> '{destination?.Name}' ({reason})", ex);
            return false;
        }
    }

    private static bool IsRecruitmentTargetStillValid(Settlement village, Settlement home)
    {
        try
        {
            if (village == null || home == null) return false;
            if (!village.IsVillage || !village.IsActive) return false;
            // 2026-05-18 fix：与 RecruitmentPlanner.RankCandidates 第3类对齐 —— 允许"同阵营友军 /
            // 中立第三方"村庄（非交战即可），不再要求 MapFaction 严格相等。
            // 旧实现 `village.MapFaction != home.MapFaction → 失效` 导致 PlanNextHop 选出来的友邦村
            // 立刻被本函数判失效 → MarkVisited → 又选另一个友邦村 → 一直循环到所有村被 visit 完，
            // 日志中表现为 100+ 行 "目标 X 已失效，重新规划" 噪声。
            var villageFaction = village.MapFaction;
            var homeFaction = home.MapFaction;
            if (villageFaction == null || homeFaction == null) return false;
            if (villageFaction != homeFaction && homeFaction.IsAtWarWith(villageFaction)) return false;
            var v = village.Village;
            if (v == null) return false;
            return v.VillageState != Village.VillageStates.BeingRaided
                && v.VillageState != Village.VillageStates.Looted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 优先用 <see cref="_assignedTarget"/>（若未访问 + 合法）；否则 PlanNextHop。
    /// </summary>
    private Settlement? ResolveDepartureTarget(MobileParty party, Settlement home)
    {
        try
        {
            var assigned = _assignedTarget;
            if (assigned != null && assigned != home
                && !VisitedThisTrip.Contains(assigned)
                && IsRecruitmentTargetStillValid(assigned, home))
            {
                return assigned;
            }
            return PlanNextHop(party, home);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ResolveDepartureTarget failed for '{PartyNameFormatter.SafeName(party)}'", ex);
            return PlanNextHop(party, home);
        }
    }

    /// <summary>
    /// 规划巡回的下一站。优先 ClanRecruiterScheduler（多队互补）；失败回退 RankCandidates。
    /// </summary>
    private Settlement? PlanNextHop(MobileParty party, Settlement home)
    {
        try
        {
            var registry = CapitalRegistry.Instance;
            var capitalMgr = registry?.GetForSettlement(home);
            if (capitalMgr != null)
            {
                var next = capitalMgr.RecruiterScheduler.PickNextVillage(party);
                if (next != null) return next;
            }

            var homeTown = home.Town;
            if (homeTown == null) return null;
            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();

            var exclude = new HashSet<Settlement>();
            foreach (var s in VisitedThisTrip) exclude.Add(s);

            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxResults: CandidateBatchSize,
                excludeSettlements: exclude,
                matchingRule: rule);

            if (candidates.Count == 0) return null;
            return candidates[0].VillageSettlement;
        }
        catch (Exception ex)
        {
            Logger.Error("PlanNextHop failed", ex);
            return null;
        }
    }

    /// <summary>
    /// 抵达 village 的实际招募动作。返回实际招到的人数（用于冷却登记判定）。
    /// 含 per-role 饱和检查 + vanilla VolunteerModel slot 扩展 + ModTreasury rollback。
    /// </summary>
    private int RecruitFromTargetVillage(MobileParty recruitingParty, Settlement village, Settlement home)
    {
        int recruited = 0;
        try
        {
            if (village?.Notables == null) return 0;
            var ownerHero = home.OwnerClan?.Leader;
            if (ownerHero == null) return 0;
            var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) return 0;

            var rule = ConfigurationManager.GetRuleFor(home.Town);
            int budgetRemaining = Math.Max(0, rule?.BudgetLimit ?? 5000);
            int leaderGold = ownerHero.Gold;

            // B7.20：硬编码 0.5 折扣（玩家 5 denar/兵）；AI 免费。
            bool shouldChargeRecruit = CapitalRegistry.ShouldChargeClan(home.OwnerClan);
            int costPerRecruit = shouldChargeRecruit ? Math.Max(1, (int)Math.Round(DefaultGoldPerRecruit * CostDiscount)) : 0;

            int spent = 0;
            int candidatesScanned = 0;
            var scoredCandidates = new List<(CharacterObject Troop, CharacterObject[] Slots, int SlotIndex, float Score)>();
            var garrisonRoster = home.Town?.GarrisonParty?.MemberRoster;
            int targetTotal = Math.Max(1, rule?.TargetTotalCount ?? 1);

            // B7.22 Fix per-role 饱和：预测「征兵队回家合并后」各 role 总人数。
            var snapGarrison = GenericTroopMatcher.Snapshot(garrisonRoster);
            var snapRecruiter = GenericTroopMatcher.Snapshot(recruitingParty.MemberRoster);
            int rTargetCav = (int)Math.Round((rule?.CavalryRatio  ?? 0f) * targetTotal);
            int rTargetHa  = (int)Math.Round((rule?.HorseArcherRatio ?? 0f) * targetTotal);
            int rTargetInf = (int)Math.Round((rule?.InfantryRatio ?? 0f) * targetTotal);
            int rTargetRng = (int)Math.Round((rule?.RangedRatio ?? 0f) * targetTotal);
            int rGainCav = 0, rGainHa = 0, rGainInf = 0, rGainRng = 0;

            // 通用匹配文化过滤：home 即征兵队所属首府，解析一次玩家面板的文化策略（null = 不过滤）。
            string? requiredCultureId = GenericTroopMatcher.ResolveRequiredCultureId(rule, home.Town);

            foreach (var notable in village.Notables)
            {
                if (notable == null) continue;
                if (!notable.CanHaveRecruits) continue;

                var volunteerTypes = notable.VolunteerTypes;
                if (volunteerTypes == null || volunteerTypes.Length == 0) continue;

                int maxIdx;
                try
                {
                    // ST 自身招兵：进入 StRecruitContext 让 STVolunteerModel 放行 — 否则被管 AI clan
                    // 派出的 RecruitingParty 调本方法时会被自己的 model 阻断 → 永远招不到。
                    using (StRecruitContext.Enter())
                    {
                        maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: MaximumIndexHeroCanRecruitFromHero threw for notable '{notable.Name}'", ex);
                    continue;
                }
                if (maxIdx < 0) continue;

                // B7.14: vanilla slot 上限按 VolunteerMul 扩大，但不超 volunteerTypes 实际长度
                int effectiveMaxIdx = Math.Min(
                    volunteerTypes.Length - 1,
                    Math.Max(maxIdx, (int)Math.Round((maxIdx + 1) * VolunteerMul) - 1));

                for (int i = 0; i < volunteerTypes.Length && i <= effectiveMaxIdx; i++)
                {
                    var troop = volunteerTypes[i];
                    if (troop == null) continue;

                    candidatesScanned++;
                    if (rule != null && !TroopTemplateMatcher.MatchesRule(troop, rule)) continue;
                    // 玩家面板的文化过滤策略（玩家文化 / 首府文化 / 不过滤）
                    if (!GenericTroopMatcher.CultureFilterAllows(troop, requiredCultureId)) continue;
                    float score = TroopTemplateMatcher.ScoreCandidate(troop, rule, garrisonRoster, targetTotal);
                    if (float.IsNegativeInfinity(score)) continue;
                    scoredCandidates.Add((troop, volunteerTypes, i, score));
                }
            }

            scoredCandidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));

            foreach (var candidate in scoredCandidates)
            {
                int cost = costPerRecruit;
                if (budgetRemaining < cost) break;
                if (candidate.Slots[candidate.SlotIndex] == null) continue;

                // B7.22 Fix per-role 饱和检查
                var roleOfCand = GenericTroopMatcher.GetRole(candidate.Troop);
                int projected, target;
                switch (roleOfCand)
                {
                    case GenericTroopRole.Cavalry:     projected = snapGarrison.Cavalry     + snapRecruiter.Cavalry     + rGainCav; target = rTargetCav; break;
                    case GenericTroopRole.HorseArcher: projected = snapGarrison.HorseArcher + snapRecruiter.HorseArcher + rGainHa;  target = rTargetHa;  break;
                    case GenericTroopRole.Infantry:    projected = snapGarrison.Infantry    + snapRecruiter.Infantry    + rGainInf; target = rTargetInf; break;
                    case GenericTroopRole.Ranged:      projected = snapGarrison.Ranged      + snapRecruiter.Ranged      + rGainRng; target = rTargetRng; break;
                    default: continue;
                }
                if (projected >= target) continue;

                if (shouldChargeRecruit && cost > 0 && !ModTreasury.CanAfford(cost))
                {
                    Logger.Info($"  RecruitFromTargetVillage: 玩家金币不足，停止招募（已招 {recruited} 人）");
                    break;
                }

                bool added = false;
                try
                {
                    recruitingParty.AddElementToMemberRoster(candidate.Troop, 1, false);
                    added = true;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: AddElementToMemberRoster threw for '{candidate.Troop.StringId}'", ex);
                    continue;
                }

                // 扣费失败 → rollback 刚加入的兵，避免免费招募
                if (shouldChargeRecruit && cost > 0)
                {
                    if (!ModTreasury.Charge(ExpenseCategory.RecruiterWage, cost, $"recruit village={village.StringId} troop={candidate.Troop.StringId}"))
                    {
                        try
                        {
                            if (added)
                            {
                                recruitingParty.MemberRoster?.RemoveTroop(candidate.Troop, 1, default(UniqueTroopDescriptor), 0);
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            Logger.Warn($"  RecruitFromTargetVillage: rollback failed after charge refusal for '{candidate.Troop.StringId}': {rollbackEx.Message}");
                        }
                        Logger.Info($"  RecruitFromTargetVillage: ModTreasury.Charge 拒绝，停止招募（已招 {recruited} 人）");
                        break;
                    }
                }

                candidate.Slots[candidate.SlotIndex] = null!;
                try
                {
                    switch (roleOfCand)
                    {
                        case GenericTroopRole.Cavalry:     rGainCav++; break;
                        case GenericTroopRole.HorseArcher: rGainHa++; break;
                        case GenericTroopRole.Infantry:    rGainInf++; break;
                        case GenericTroopRole.Ranged:      rGainRng++; break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: role counter update failed for '{candidate.Troop.StringId}'", ex);
                }

                budgetRemaining -= cost;
                leaderGold -= cost;
                spent += cost;
                recruited++;
            }

            DecisionAuditLogger.LogRule(
                decisionType: "RecruitFromVillage",
                inputSummary: $"home={home.StringId} village={village.StringId} notables={village.Notables.Count} candidates={candidatesScanned}",
                decisionJson: $"{{\"home\":\"{home.StringId}\",\"village\":\"{village.StringId}\",\"recruited\":{recruited},\"spent\":{spent},\"budgetRemaining\":{budgetRemaining}}}",
                accepted: recruited > 0);

            // B7.27：通知 scheduler 本次访问，更新 LastRecruitedAt
            if (recruited > 0)
            {
                try
                {
                    var visitCapitalMgr = CapitalRegistry.Instance?.GetForSettlement(home);
                    visitCapitalMgr?.RecruiterScheduler.RecordVisit(village);
                }
                catch (Exception ex) { Logger.Warn("RecruiterScheduler.RecordVisit (per-village) failed", ex); }
            }
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(recruitingParty)}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar）");
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitFromTargetVillage failed", ex);
        }
        return recruited;
    }
}
