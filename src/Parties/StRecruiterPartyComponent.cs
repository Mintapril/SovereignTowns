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
using TaleWorlds.ObjectSystem;
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
/// 两种模式（<see cref="RecruiterMode"/>）共用本类，分支只在两处：
///   - 志愿者匹配：调 <see cref="TroopTemplateMatcher.IsAcceptableVolunteer"/>，按 _mode 选 GarrisonRole / HonorGuardPrecise；
///   - 回家路径：HonorGuardPrecise 模式 override OnArrivedHome 转入卫队池，GarrisonRole 走默认 DefaultMergeAndDisband。
///
/// SaveableField 槽位：基类占 [10, 20)；本类占 [20, 30)。
/// 槽位 23 空置（原 _visitedThisTrip）。28/29 = 卫队精确模板支持。
/// </summary>
public sealed class StRecruiterPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_recruit_";

    /// <summary>vanilla volunteer slot 上限放宽倍率（B7.20，硬编码 2.0）。</summary>
    private const float VolunteerMul = 2.0f;
    /// <summary>招募人头费：玩家氏族每兵收 5 denar；AI 免费。两模式（GarrisonRole / HonorGuardPrecise）共用此常量。</summary>
    public const int RecruitChargePerHead = 5;

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
    // 招募模式：GarrisonRole（默认，缺省值 0，老存档无字段时为该值）/ HonorGuardPrecise。
    [SaveableField(28)] private RecruiterMode _mode = RecruiterMode.GarrisonRole;
    // 卫队精确模板的 JSON 序列化（仅 HonorGuardPrecise 模式持有）。
    // 用 JSON 而非 Dictionary<string,int>：vanilla SaveSystem 对字典容器声明繁琐，JSON 字符串零声明。
    [SaveableField(29)] private string? _preciseTemplateJson;
    [CachedData] private TextObject? _cachedName;
    [CachedData] private Dictionary<string, int>? _preciseTemplateCache;

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
    public RecruiterMode Mode => _mode;
    /// <summary>剩余可招人数 = _tripCountTarget − _recruitedThisTrip（钳到 ≥ 0）。_tripCountTarget ≤ 0 时恒 0。</summary>
    public int TripCountRemaining => _tripCountTarget > 0 ? Math.Max(0, _tripCountTarget - _recruitedThisTrip) : 0;

    /// <summary>HonorGuardPrecise 模式下的精确模板快照（troopId → desiredCount）。GarrisonRole 模式恒 null。
    /// 模板由派发时一次性载入（CapitalLogisticsManager 派发 → SetMode），运行时只读不变。</summary>
    public IReadOnlyDictionary<string, int>? PreciseTemplate
    {
        get
        {
            if (_mode != RecruiterMode.HonorGuardPrecise) return null;
            if (_preciseTemplateCache != null) return _preciseTemplateCache;
            _preciseTemplateCache = ParsePreciseTemplateJson(_preciseTemplateJson);
            return _preciseTemplateCache;
        }
    }

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

    /// <summary>派发时设定本队招募模式与（可选）卫队精确模板。
    /// preciseTemplate 仅在 HonorGuardPrecise 模式生效；GarrisonRole 时忽略。</summary>
    public void SetMode(RecruiterMode mode, IReadOnlyDictionary<string, int>? preciseTemplate)
    {
        _mode = mode;
        if (mode == RecruiterMode.HonorGuardPrecise && preciseTemplate != null && preciseTemplate.Count > 0)
        {
            _preciseTemplateJson = SerializePreciseTemplate(preciseTemplate);
            // 手动拷贝：net472 Dictionary ctor 不接受 IReadOnlyDictionary。
            var copy = new Dictionary<string, int>(preciseTemplate.Count, StringComparer.Ordinal);
            foreach (var kv in preciseTemplate)
            {
                if (!string.IsNullOrEmpty(kv.Key)) copy[kv.Key] = kv.Value;
            }
            _preciseTemplateCache = copy;
        }
        else
        {
            _preciseTemplateJson = null;
            _preciseTemplateCache = null;
        }
    }

    private static string SerializePreciseTemplate(IReadOnlyDictionary<string, int> template)
    {
        // 简单 JSON 序列化，避开 Newtonsoft 在私有字段上的反射要求。entries 顺序保留（IDictionary 遍历顺序）。
        var sb = new System.Text.StringBuilder(128);
        sb.Append('{');
        bool first = true;
        foreach (var kv in template)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(EscapeJsonString(kv.Key)).Append("\":").Append(kv.Value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, int>? ParsePreciseTemplateJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            // 用 Newtonsoft（CLAUDE.md #8：vanilla bundled），保持与项目其他 JSON 序列化一致。
            var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json!);
            if (dict == null || dict.Count == 0) return null;
            return new Dictionary<string, int>(dict, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Logger.Warn($"StRecruiterPartyComponent.ParsePreciseTemplateJson failed: {ex.Message}");
            return null;
        }
    }

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
            // 食物由 RecruitmentDispatcher 出发时经 TrySeedAndBuyInitialFood 按"到第一站行程"备粮（真实市场购买），不凭空塞。
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
        Logger.Debug($"[DIAG] Recruiter.Core '{PartyNameFormatter.SafeName(self)}' mode={_mode} phase={_phase} assignedTarget='{_assignedTarget?.Name?.ToString() ?? "null"}' recruited={_recruitedThisTrip}/{_tripCountTarget} itin={_itineraryIndex}/{Itin.Count}");
        switch (_phase)
        {
            case RecruiterPhase.Dispatching: HandleDispatching(self); break;
            case RecruiterPhase.AtVillage:   HandleAtVillage(self); break;
            case RecruiterPhase.Travelling:  HandleTravelling(self); break;
            case RecruiterPhase.Returning:   /* base.IsAtHome 接管 → OnArrivedHome → DefaultMergeAndDisband / HonorGuard 转池 */ break;
        }
    }

    /// <summary>
    /// 抵达首府时的处理：
    /// - GarrisonRole → 走 base.DefaultMergeAndDisband（兵员并入 Town.GarrisonParty）。
    /// - HonorGuardPrecise → 把非英雄兵员转入 HonorGuard 池（受 HonorGuardCap 钳制），然后 disband。
    ///   转不进池的兵（池满 / 池不存在）由基类 OnDestroyed 兜底尝试塞回 garrison。
    /// </summary>
    protected override void OnArrivedHome(MobileParty self)
    {
        if (_mode != RecruiterMode.HonorGuardPrecise)
        {
            base.OnArrivedHome(self);
            return;
        }

        try
        {
            var capital = HomeSettlementOrNull;
            if (capital == null)
            {
                Logger.Warn("StRecruiter(HonorGuard).OnArrivedHome: capital is null, falling back to DefaultMergeAndDisband");
                DefaultMergeAndDisband(self);
                return;
            }

            TransferHonorGuardRecruitsToPool(self, capital);
        }
        catch (Exception ex)
        {
            Logger.Error("StRecruiter(HonorGuard).OnArrivedHome failed", ex);
        }
        finally
        {
            // 任何路径下都走 DefaultMergeAndDisband 收尾：未转入池的剩余兵会进 garrison（兜底救人），
            // 同时统一走 TryRefundOnDestroy 退款 + DisbandAndUntrack。
            try { DefaultMergeAndDisband(self); }
            catch (Exception ex) { Logger.Error("StRecruiter(HonorGuard).OnArrivedHome DefaultMergeAndDisband failed", ex); }
        }
    }

    /// <summary>
    /// 把本队的非英雄兵员转入 <paramref name="capital"/> 的 HonorGuard 池。
    /// 受 HonorGuardCap 钳制；池不存在 / 已满 → 兵员留在 self.MemberRoster，由 DefaultMergeAndDisband 兜底进 garrison。
    /// </summary>
    private void TransferHonorGuardRecruitsToPool(MobileParty self, Settlement capital)
    {
        var bPool = HonorGuardManager.GetPoolStatic(capital);
        if (bPool == null || !bPool.IsActive)
        {
            Logger.Warn($"StRecruiter(HonorGuard) '{PartyNameFormatter.SafeName(self)}': no active honor-guard pool for '{capital.StringId}', troops will fall through to garrison");
            return;
        }
        var roster = self.MemberRoster;
        if (roster == null) return;

        int cap = ConfigurationManager.Current?.FiscalAutonomy?.HonorGuardCap ?? 0;
        int currentPool = bPool.MemberRoster?.TotalManCount ?? 0;
        int headroom = Math.Max(0, cap - currentPool);
        if (headroom <= 0)
        {
            Logger.Warn($"StRecruiter(HonorGuard) '{PartyNameFormatter.SafeName(self)}': pool full ({currentPool}/{cap}), troops fall through to garrison");
            return;
        }

        // 2026-05-29 fix: 只把"模板兵"(troopId 命中 / 可升级到模板某 troopId)转入卫队池。
        // 派遣时从 garrison 抽的低 tier 护卫(escort)与任何非模板兵都不进池 —— 留在 roster，
        // 由 OnArrivedHome 的 finally → DefaultMergeAndDisband 并回 garrison。
        // 此前无过滤 → 护卫(异文化低 tier 杂兵)被一并倒进卫队池 =「卫队大量非模板兵」，
        // 且每次派遣把护卫永久搬进池 → garrison 持续净流失(实际驻军远低于目标)。两症状同源。
        var template = PreciseTemplate;
        if (template == null || template.Count == 0)
        {
            Logger.Warn($"StRecruiter(HonorGuard) '{PartyNameFormatter.SafeName(self)}': deposit-time template empty, skipping pool transfer (all troops fall through to garrison)");
            return;
        }

        int transferred = 0;
        int skippedNonTemplate = 0;
        for (int i = roster.Count - 1; i >= 0 && headroom > 0; i--)
        {
            var element = roster.GetElementCopyAtIndex(i);
            if (element.Character == null || element.Character.IsHero) continue;

            // 模板成员判定：升级链命中（与招募端 RecruitHonorGuardFromVillage 的 PickPreciseTemplateMatch 同口径）。
            // 非模板（护卫/异文化）→ 跳过，留在 roster 由 DefaultMergeAndDisband 并回 garrison。
            if (!TroopTemplateMatcher.IsAcceptableVolunteer(
                    element.Character, rule: null, assignedRole: GenericTroopRole.Unknown,
                    RecruiterMode.HonorGuardPrecise, template))
            {
                skippedNonTemplate += element.Number;
                continue;
            }

            int available = element.Number;
            int wounded   = element.WoundedNumber;
            int healthy   = available - wounded;
            int take      = Math.Min(healthy > 0 ? healthy : available, headroom);
            if (take <= 0) continue;

            try
            {
                int takeWounded = (healthy <= 0) ? take : Math.Max(0, Math.Min(wounded, take - healthy));
                bPool.MemberRoster?.AddToCounts(element.Character, take, insertAtFront: false, woundedCount: takeWounded);
                // RemoveTroop(troop, numberToRemove, troopSeed, xp) — no woundedCount param.
                roster.RemoveTroop(element.Character, take, default, 0);
                headroom    -= take;
                transferred += take;
            }
            catch (Exception ex)
            {
                Logger.Warn($"StRecruiter(HonorGuard) '{PartyNameFormatter.SafeName(self)}': transfer '{element.Character?.StringId}' failed: {ex.Message}");
            }
        }
        Logger.Info($"StRecruiter(HonorGuard) '{PartyNameFormatter.SafeName(self)}': transferred {transferred} template troops to honor-guard pool of '{capital.StringId}' (skipped {skippedNonTemplate} non-template/escort → garrison)");
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
            // 2026-05-30 Q2：卫队（HonorGuardPrecise）设计就是跨敌区招募（已绕过出发期 DispatchRisk 否决，见 #16）。
            // 若"逐站高风险跳过"对 HG 也生效，它穿越战时地图时会把每一站都跳光 → recruited=0、卫队永远填不上。
            // 故 HG 不再按风险跳站（仍跳 invalid：被围 / 被劫 / 与本国交战的村 —— 那些 vanilla 本就招不了）。
            bool isHonorGuard = _mode == RecruiterMode.HonorGuardPrecise;
            bool risky = !invalid && !isHonorGuard
                         && RiskAssessmentService.Assess(targetSettlement).Level >= RiskLevel.High;
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
    /// 双模式分支：
    /// <list type="bullet">
    ///   <item><b>GarrisonRole</b>：评分排序 → per-role 饱和检查 → 扣款 + 加兵。</item>
    ///   <item><b>HonorGuardPrecise</b>：按模板插入顺序遍历 deficit → IG 升级链匹配 → 扣款 + 加兵 + 递减 deficit。</item>
    /// </list>
    /// 共用：vanilla VolunteerModel slot 扩展、StRecruitContext、ModTreasury rollback、5 denar/兵 玩家收费。
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

            // 玩家氏族每兵 5 denar；AI 免费。两模式共用同一常量（合并后再无差异）。
            bool shouldChargeRecruit = CapitalRegistry.ShouldChargeClan(home.OwnerClan);
            int costPerRecruit = shouldChargeRecruit ? RecruitChargePerHead : 0;

            if (_mode == RecruiterMode.HonorGuardPrecise)
            {
                recruited = RecruitHonorGuardFromVillage(
                    recruitingParty, village, home, volunteerModel, ownerHero,
                    shouldChargeRecruit, costPerRecruit, ref budgetRemaining);
            }
            else
            {
                recruited = RecruitGarrisonRoleFromVillage(
                    recruitingParty, village, home, volunteerModel, ownerHero, rule,
                    shouldChargeRecruit, costPerRecruit, ref budgetRemaining);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RecruitFromTargetVillage failed", ex);
        }
        return recruited;
    }

    private int RecruitGarrisonRoleFromVillage(
        MobileParty recruitingParty, Settlement village, Settlement home,
        TaleWorlds.CampaignSystem.ComponentInterfaces.VolunteerModel volunteerModel, Hero ownerHero,
        TownGarrisonRule? rule,
        bool shouldChargeRecruit, int costPerRecruit, ref int budgetRemaining)
    {
        int recruited = 0;
        int spent = 0;
        int candidatesScanned = 0;
        var scoredCandidates = new List<(CharacterObject Troop, CharacterObject[] Slots, int SlotIndex, float Score)>();
        var garrisonRoster = home.Town?.GarrisonParty?.MemberRoster;
        // 2026-05-29: per-role 配额删除。改成单一总量 gate —— 已有 garrison + 在飞征兵队人数到目标即停。
        // targetTotal 仍用 PartySizeLimit 做粗保险（不查 manager 缓存避免本类依赖），真正 cap 在 MCMF 侧已收敛。
        int partySizeLimit = home.Town?.GarrisonParty?.Party?.PartySizeLimit ?? 0;
        int targetTotal = Math.Max(1, partySizeLimit);

        int existingTotal =
            (garrisonRoster?.TotalManCount ?? 0)
            + (recruitingParty.MemberRoster?.TotalManCount ?? 0);

        string? requiredCultureId = GenericTroopMatcher.ResolveRequiredCultureId(rule, home.Town);

        foreach (var notable in village.Notables)
        {
            if (notable == null) continue;
            if (!notable.CanHaveRecruits) continue;

            var volunteerTypes = notable.VolunteerTypes;
            if (volunteerTypes == null || volunteerTypes.Length == 0) continue;

            int maxIdx = SafeMaxRecruitableIndex(volunteerModel, ownerHero, notable);
            if (maxIdx < 0) continue;

            int effectiveMaxIdx = Math.Min(
                volunteerTypes.Length - 1,
                Math.Max(maxIdx, (int)Math.Round((maxIdx + 1) * VolunteerMul) - 1));

            for (int i = 0; i < volunteerTypes.Length && i <= effectiveMaxIdx; i++)
            {
                var troop = volunteerTypes[i];
                if (troop == null) continue;
                candidatesScanned++;
                if (!TroopTemplateMatcher.IsAcceptableVolunteer(
                        troop, rule, _assignedRole, RecruiterMode.GarrisonRole, preciseTemplateDeficit: null))
                    continue;
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

            // 2026-05-29: 单一总量 gate —— garrison + recruiter + 本趟新招 >= targetTotal 即停。role 不再卡。
            if (existingTotal + recruited >= targetTotal) break;

            if (!TryChargeAndAdd(home, recruitingParty, candidate.Troop, shouldChargeRecruit, cost, village)) break;

            candidate.Slots[candidate.SlotIndex] = null!;
            budgetRemaining -= cost;
            spent += cost;
            recruited++;
        }

        DecisionAuditLogger.LogRule(
            decisionType: "RecruitFromVillage",
            inputSummary: $"home={home.StringId} village={village.StringId} mode=GarrisonRole notables={village.Notables.Count} candidates={candidatesScanned}",
            decisionJson: $"{{\"home\":\"{home.StringId}\",\"village\":\"{village.StringId}\",\"mode\":\"GarrisonRole\",\"recruited\":{recruited},\"spent\":{spent},\"budgetRemaining\":{budgetRemaining}}}",
            accepted: recruited > 0);

        Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(recruitingParty)}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar，mode=GarrisonRole）");
        return recruited;
    }

    /// <summary>
    /// HonorGuardPrecise 模式：按模板插入顺序遍历 deficit；志愿者命中模板（含可升级链）即招。
    /// deficit = template[id] − pool[id] − recruitedThisTripBy(self) — 每招一个减一。
    /// 模板每个 entry 招满后跳过；遍历到底无可招者返回。
    /// </summary>
    private int RecruitHonorGuardFromVillage(
        MobileParty recruitingParty, Settlement village, Settlement home,
        TaleWorlds.CampaignSystem.ComponentInterfaces.VolunteerModel volunteerModel, Hero ownerHero,
        bool shouldChargeRecruit, int costPerRecruit, ref int budgetRemaining)
    {
        var template = PreciseTemplate;
        if (template == null || template.Count == 0)
        {
            Logger.Warn($"  Recruiter '{PartyNameFormatter.SafeName(recruitingParty)}' HonorGuardPrecise mode without template — skip");
            return 0;
        }

        // 计算当前 deficit：template − HG 池现有 − 本队已招（含本趟和读档前的）。
        var bPool = HonorGuardManager.GetPoolStatic(home);
        var poolRoster = bPool?.MemberRoster;
        var selfRoster = recruitingParty.MemberRoster;
        var deficit = new Dictionary<string, int>(template.Count, StringComparer.Ordinal);
        foreach (var kv in template)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
            int have = CountTroopInRoster(poolRoster, kv.Key) + CountTroopInRoster(selfRoster, kv.Key);
            int need = Math.Max(0, kv.Value - have);
            if (need > 0) deficit[kv.Key] = need;
        }
        if (deficit.Count == 0) return 0;

        int recruited = 0;
        int spent = 0;
        int candidatesScanned = 0;

        foreach (var notable in village.Notables)
        {
            if (deficit.Count == 0) break;
            if (notable == null) continue;
            if (!notable.CanHaveRecruits) continue;

            var volunteerTypes = notable.VolunteerTypes;
            if (volunteerTypes == null || volunteerTypes.Length == 0) continue;

            int maxIdx = SafeMaxRecruitableIndex(volunteerModel, ownerHero, notable);
            if (maxIdx < 0) continue;

            int effectiveMaxIdx = Math.Min(
                volunteerTypes.Length - 1,
                Math.Max(maxIdx, (int)Math.Round((maxIdx + 1) * VolunteerMul) - 1));

            for (int i = 0; i < volunteerTypes.Length && i <= effectiveMaxIdx; i++)
            {
                var troop = volunteerTypes[i];
                if (troop == null) continue;
                candidatesScanned++;

                // PickPreciseTemplateMatch 走 IG 升级链：返回第一个命中（troopId 相等或可升级到）的 deficit 槽位。
                string? matchedKey = TroopTemplateMatcher.PickPreciseTemplateMatch(troop, deficit);
                if (matchedKey == null) continue;

                int cost = costPerRecruit;
                if (budgetRemaining < cost) { Logger.Info($"  RecruitFromTargetVillage(HG): 资金不足，停止招募（已招 {recruited} 人）"); return recruited; }
                if (!TryChargeAndAdd(home, recruitingParty, troop, shouldChargeRecruit, cost, village)) return recruited;

                volunteerTypes[i] = null!;
                deficit[matchedKey]--;
                if (deficit[matchedKey] <= 0) deficit.Remove(matchedKey);
                budgetRemaining -= cost;
                spent += cost;
                recruited++;
                if (deficit.Count == 0) break;
            }
        }

        DecisionAuditLogger.LogRule(
            decisionType: "RecruitFromVillage",
            inputSummary: $"home={home.StringId} village={village.StringId} mode=HonorGuardPrecise notables={village.Notables.Count} candidates={candidatesScanned}",
            decisionJson: $"{{\"home\":\"{home.StringId}\",\"village\":\"{village.StringId}\",\"mode\":\"HonorGuardPrecise\",\"recruited\":{recruited},\"spent\":{spent},\"budgetRemaining\":{budgetRemaining}}}",
            accepted: recruited > 0);

        Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(recruitingParty)}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar，mode=HonorGuardPrecise）");
        return recruited;
    }

    private static int SafeMaxRecruitableIndex(
        TaleWorlds.CampaignSystem.ComponentInterfaces.VolunteerModel volunteerModel,
        Hero ownerHero, Hero notable)
    {
        try
        {
            using (StRecruitContext.Enter())
            {
                return volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"  SafeMaxRecruitableIndex threw for notable '{notable?.Name}': {ex.Message}");
            return -1;
        }
    }

    /// <summary>共用扣款 + 加兵 + 失败 rollback 路径。返回 true 表示已成功加入 roster。</summary>
    private static bool TryChargeAndAdd(
        Settlement home, MobileParty recruitingParty, CharacterObject troop,
        bool shouldCharge, int cost, Settlement village)
    {
        if (shouldCharge && cost > 0 && !ModTreasury.CanAfford(home.OwnerClan, cost))
        {
            Logger.Info($"  RecruitFromTargetVillage: 资金不足，停止招募");
            return false;
        }
        bool charged = false;
        if (shouldCharge && cost > 0)
        {
            if (!ModTreasury.Charge(home.OwnerClan, ExpenseCategory.RecruiterWage, cost,
                    $"recruit village={village.StringId} troop={troop.StringId}"))
            {
                Logger.Info($"  RecruitFromTargetVillage: ModTreasury.Charge 失败");
                return false;
            }
            charged = true;
        }
        try
        {
            recruitingParty.AddElementToMemberRoster(troop, 1, false);
        }
        catch (Exception ex)
        {
            if (charged)
            {
                try
                {
                    ModTreasury.Refund(home.OwnerClan, ExpenseCategory.RecruiterWage, cost,
                        $"rollback recruit add failed village={village.StringId} troop={troop.StringId}");
                }
                catch (Exception refundEx)
                {
                    Logger.Warn($"  TryChargeAndAdd refund failed after add failure for '{troop.StringId}': {refundEx.Message}");
                }
            }
            Logger.Warn($"  TryChargeAndAdd: AddElementToMemberRoster threw for '{troop.StringId}' after charge; refunded={charged}: {ex.Message}");
            return false;
        }
        return true;
    }

    private static int CountTroopInRoster(TroopRoster? roster, string troopId)
    {
        if (roster == null || string.IsNullOrEmpty(troopId)) return 0;
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var e = roster.GetElementCopyAtIndex(i);
            if (e.Character?.StringId == troopId) n += e.Number;
        }
        return n;
    }
}
