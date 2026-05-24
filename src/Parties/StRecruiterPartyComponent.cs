using System;
using System.Collections.Generic;
using SovereignTowns.Algorithm;
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
/// 招募行程由 MCMF（UnifiedGarrisonSolver）决定：<see cref="_itinerary"/> 是派遣时静态确定的多站村庄
/// 序列，<see cref="_itineraryIndex"/> 是当前进度。征兵队不再运行时打分选村，只按行程逐站访问。
/// <see cref="_itinerary"/> 用 List&lt;Settlement&gt;（vanilla SaveSystem 不直接支持 HashSet；容器声明
/// 见 SovereignTownsTypeDefiner.DefineContainerDefinitions）。
///
/// SaveableField 槽位：基类占 [10, 20)；本类占 [20, 28)，槽位 23 空置（原 _visitedThisTrip，
/// 行程驱动改造后由 _itineraryIndex 取代）。
/// </summary>
public sealed class StRecruiterPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_recruit_";

    /// <summary>vanilla volunteer slot 上限放宽倍率（B7.20，硬编码 2.0）。</summary>
    private const float VolunteerMul = 2.0f;
    /// <summary>玩家氏族单兵金币折扣（B7.20，硬编码 0.5）。</summary>
    private const float CostDiscount = 0.5f;
    private const int DefaultGoldPerRecruit = 10;

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
    // 槽位 23 空置（原 _visitedThisTrip）。
    // MCMF 指定的定向招募兵种。Unknown = 无偏好。RecruitFromTargetVillage 据此只招该 role。
    [SaveableField(24)] private GenericTroopRole _assignedRole = GenericTroopRole.Unknown;
    // MCMF 派遣时静态确定的多站村庄行程；_itineraryIndex 为当前进度。
    [SaveableField(25)] private List<Settlement> _itinerary = new List<Settlement>();
    [SaveableField(26)] private int _itineraryIndex;
    // 本趟招募人数目标（MCMF count 求和）。达到即返航；<=0 时仅靠行程耗尽返航。
    [SaveableField(27)] private int _tripCountTarget;
    [CachedData] private TextObject? _cachedName;

    private List<Settlement> Itin
    {
        get
        {
            // 读档兼容：旧存档无此字段时为 null，lazy init 以维持非 null 不变式。
            if (_itinerary == null) _itinerary = new List<Settlement>();
            return _itinerary;
        }
    }

    /// <summary>当前及之后尚未访问的行程村庄（供 MCMF 在飞排除已被服务的村）。</summary>
    public IEnumerable<Settlement> PendingVillages
    {
        get
        {
            var list = Itin;
            for (int i = Math.Max(0, _itineraryIndex); i < list.Count; i++)
                if (list[i] != null) yield return list[i];
        }
    }

    public int RecruitedThisTrip => _recruitedThisTrip;
    public Settlement? AssignedTarget => _assignedTarget;
    public RecruiterPhase Phase => _phase;
    public GenericTroopRole AssignedRole => _assignedRole;

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
    public void SetAssignedRole(GenericTroopRole role) => _assignedRole = role;
    public void TransitionTo(RecruiterPhase phase) => _phase = phase;

    /// <summary>派遣时设定 MCMF 决定的多站行程与本趟招募人数目标。</summary>
    public void SetItinerary(IReadOnlyList<Settlement>? villages, int tripCountTarget)
    {
        _itinerary = new List<Settlement>();
        if (villages != null)
        {
            foreach (var v in villages)
                if (v != null) _itinerary.Add(v);
        }
        _itineraryIndex = 0;
        _tripCountTarget = tripCountTarget > 0 ? tripCountTarget : 0;
    }

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
        Logger.Debug($"[DIAG] Recruiter.Core '{PartyNameFormatter.SafeName(self)}' phase={_phase} assignedTarget='{_assignedTarget?.Name?.ToString() ?? "null"}' recruited={_recruitedThisTrip}/{_tripCountTarget} itin={_itineraryIndex}/{Itin.Count}");
        switch (_phase)
        {
            case RecruiterPhase.Dispatching: HandleDispatching(self); break;
            case RecruiterPhase.AtVillage:   HandleAtVillage(self); break;
            case RecruiterPhase.Travelling:  HandleTravelling(self); break;
            case RecruiterPhase.Returning:   /* base.IsAtHome 接管 → OnArrivedHome → DefaultMergeAndDisband */ break;
        }
    }

    /// <summary>
    /// Dispatching：刚创建尚未首发，或读档后回到 Dispatching。
    /// 取行程当前站后切到 Travelling；行程为空则直接 Returning。
    /// </summary>
    private void HandleDispatching(MobileParty self)
    {
        // B16.4a P1-7：保留 null 防御 —— 用 OrNull 而非抛诊断异常版的 HomeSettlement。
        var home = HomeSettlementOrNull;
        if (home == null) return;
        var next = CurrentItineraryStop(home);
        Logger.Debug($"[DIAG] Recruiter.HandleDispatching '{PartyNameFormatter.SafeName(self)}' home='{home.Name}' itin={_itineraryIndex}/{Itin.Count} resolved='{next?.Name?.ToString() ?? "null"}'");
        if (next == null)
        {
            MoveTo(self, home, "empty itinerary");
            _phase = RecruiterPhase.Returning;
            return;
        }
        MoveTo(self, next, "Dispatching → first hop");
        _phase = RecruiterPhase.Travelling;
    }

    /// <summary>
    /// AtVillage：抵达行程村庄 → 招募 + 标记冷却 + 达标检查 + 推进行程。
    /// </summary>
    private void HandleAtVillage(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return;

        var currentSettlement = self.CurrentSettlement ?? self.LastVisitedSettlement;
        // 安全网：若实际不在 village（被推走 / 战斗等），回 Travelling 逻辑。
        if (currentSettlement == null || !currentSettlement.IsVillage || currentSettlement == home)
        {
            _phase = RecruiterPhase.Travelling;
            HandleTravelling(self);
            return;
        }

        // 与 _assignedTarget 不一致 = vanilla 改路 / 强制回家。
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

        if (IsRecruitmentTargetStillValid(currentSettlement, home))
        {
            int recruited = RecruitFromTargetVillage(self, currentSettlement, home);
            if (recruited > 0)
            {
                RecordRecruited(recruited);
                RecruitmentCooldown.MarkRecruited(currentSettlement);
            }
            // 2026-05-18 v3：在 just-arrived village 上下文触发经济维护（卖战利品 + 食物补给）。
            try { TryEconomicMaintenance(self, currentSettlement); }
            catch (Exception econEx) { Logger.Warn($"Recruiter HandleAtVillage maintenance failed: {econEx.Message}"); }
        }
        else
        {
            Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 目标村庄 '{currentSettlement.Name}' 已不适合招募，跳过");
        }

        // 招够目标 → 返航
        if (TripCountReached())
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 本趟招募 {_recruitedThisTrip}/{_tripCountTarget} 达标，回 '{home.Name}'");
            MoveTo(self, home, "trip count reached");
            _phase = RecruiterPhase.Returning;
            return;
        }

        // 推进行程
        var next = AdvanceItinerary(home);
        if (next != null)
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 行程下一站：'{next.Name}' ({_itineraryIndex + 1}/{Itin.Count})");
            MoveTo(self, next, "next village");
            _phase = RecruiterPhase.Travelling;
        }
        else
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 行程结束，回 '{home.Name}'");
            MoveTo(self, home, "itinerary exhausted");
            _phase = RecruiterPhase.Returning;
        }
    }

    /// <summary>
    /// Travelling：在路上或刚抵达村庄。
    /// 抵达行程村庄 → 同 tick fall-through 到 HandleAtVillage（避免 1h 延迟）。
    /// 否则检查招募达标 / 目标失效 / 风险高。
    /// </summary>
    private void HandleTravelling(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return;

        // 抵达 _assignedTarget → 切 AtVillage 并同 tick 处理（"到村即招"，避免 1h 延迟）。
        // vanilla 抵达 settlement 时会清空 TargetSettlement，故 TargetSettlement==null 也视作抵达。
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

        // 招够目标 → 返航（即使没到下一站）
        if (TripCountReached() && (targetSettlement == null || targetSettlement != home))
        {
            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 本趟招募 {_recruitedThisTrip}/{_tripCountTarget} 达标，回 '{home.Name}'");
            MoveTo(self, home, "road trip count reached");
            _phase = RecruiterPhase.Returning;
            return;
        }

        if (targetSettlement == null)
        {
            var next = CurrentItineraryStop(home);
            Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}' 没有当前目标，{(next != null ? $"改去 '{next.Name}'" : $"回 '{home.Name}'")}");
            MoveTo(self, next ?? home, "missing target");
            _phase = next != null ? RecruiterPhase.Travelling : RecruiterPhase.Returning;
            return;
        }

        // 目标失效 / 风险高 → 跳到行程下一站
        if (targetSettlement != home && targetSettlement.IsVillage)
        {
            bool invalid = !IsRecruitmentTargetStillValid(targetSettlement, home);
            bool risky = !invalid && RiskAssessmentService.Assess(targetSettlement).Level >= RiskLevel.High;
            if (invalid || risky)
            {
                Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(self)}': 目标 '{targetSettlement.Name}' {(invalid ? "已失效" : "风险高")}，跳到下一站");
                var next = AdvanceItinerary(home);
                MoveTo(self, next ?? home, invalid ? "invalid road target" : "risky road target");
                _phase = next != null ? RecruiterPhase.Travelling : RecruiterPhase.Returning;
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
            // 与 RecruitmentTopology.EnumerateRecruitmentVillages 的入图过滤口径一致 —— 允许"同阵营
            // 友军 / 中立第三方"村庄（非交战即可），不要求 MapFaction 严格相等。MCMF 已按此口径选村，
            // 本函数仅复检"行程途中村庄状态是否变化"（沦陷 / 开战 / 被劫）。
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
    /// 返回行程当前站：<see cref="_itinerary"/>[<see cref="_itineraryIndex"/>]。
    /// 若当前条目失效（村庄沦陷 / 围城 / 交战 / 被劫），自动跳过；行程耗尽返回 null。
    /// </summary>
    private Settlement? CurrentItineraryStop(Settlement home)
    {
        var list = Itin;
        while (_itineraryIndex < list.Count)
        {
            var v = list[_itineraryIndex];
            if (v != null && v != home && IsRecruitmentTargetStillValid(v, home))
                return v;
            _itineraryIndex++;
        }
        return null;
    }

    /// <summary>推进到行程下一站（先 index++，再取当前站）。行程耗尽返回 null。</summary>
    private Settlement? AdvanceItinerary(Settlement home)
    {
        _itineraryIndex++;
        return CurrentItineraryStop(home);
    }

    /// <summary>本趟招募是否已达 MCMF 设定的人数目标。_tripCountTarget &lt;= 0 时恒 false（仅靠行程耗尽返航）。</summary>
    private bool TripCountReached() => _tripCountTarget > 0 && _recruitedThisTrip >= _tripCountTarget;

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
            // 角色配额与 MCMF 图(MatchPolicy.DesiredCount)同口径:精确模板模式用模板角色分布、
            // 通用模式用比例滑条。否则图按模板派本队来招某 role、本队却按通用比例判「已饱和」→ 0 招募。
            int rTargetCav = rule != null ? MatchPolicy.DesiredCount(rule, GenericTroopRole.Cavalry, targetTotal) : 0;
            int rTargetHa  = rule != null ? MatchPolicy.DesiredCount(rule, GenericTroopRole.HorseArcher, targetTotal) : 0;
            int rTargetInf = rule != null ? MatchPolicy.DesiredCount(rule, GenericTroopRole.Infantry, targetTotal) : 0;
            int rTargetRng = rule != null ? MatchPolicy.DesiredCount(rule, GenericTroopRole.Ranged, targetTotal) : 0;
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
                    // MCMF 定向招募：只招可服务指定 role 的兵。精确模板模式下服务 role
                    // 取可升级模板目标,与 solver 建图分桶保持一致。
                    if (_assignedRole != GenericTroopRole.Unknown
                        && !TroopTemplateMatcher.CanServeRole(troop, rule, _assignedRole)) continue;
                    // 玩家面板的文化过滤策略（玩家文化 / 首府文化 / 不过滤）。
                    // PR-5'(2026-05-24): UseGenericMatching removed; culture filter always applies.
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
                var roleOfCand = TroopTemplateMatcher.GetServiceRole(candidate.Troop, rule);
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

                if (shouldChargeRecruit && cost > 0 && !ModTreasury.CanAfford(home.OwnerClan, cost))
                {
                    Logger.Info($"  RecruitFromTargetVillage: 资金不足，停止招募（已招 {recruited} 人）");
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
                    if (!ModTreasury.Charge(home.OwnerClan, ExpenseCategory.RecruiterWage, cost, $"recruit village={village.StringId} troop={candidate.Troop.StringId}"))
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

            Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(recruitingParty)}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar）");
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitFromTargetVillage failed", ex);
        }
        return recruited;
    }
}
