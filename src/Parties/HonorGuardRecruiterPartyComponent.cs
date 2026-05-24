using System;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Economy;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 卫队（Honor Guard）专用征兵队组件。
///
/// 比 <see cref="StRecruiterPartyComponent"/> 简化：
///   - 单站（单一 targetVillage），无多站行程；
///   - 仅招募指定 <see cref="_troopId"/> 的兵种，不做 role 评分；
///   - 回到首府后把所有非英雄兵员转入卫队 Roster，然后自销毁（不调 DefaultMergeAndDisband）。
///
/// SaveableField 槽位：基类占 [10, 20)；本类占 [20, ∞)。
/// 当前字段：20=_troopId，21=_targetCount，22=_targetVillage，23=_phase。
/// StringId 前缀沿用 "st_bpr_"（per CLAUDE.md #3：已用过的 SaveId 永不复用）。
/// </summary>
public sealed class HonorGuardRecruiterPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_bpr_";

    // ── 阶段枚举（保存为 SaveableField，须在 TypeDefiner 注册） ──
    public enum RecruiterPhase
    {
        Travelling = 0,
        AtVillage  = 1,
        Returning  = 2,
    }

    [SaveableField(20)] private string _troopId = "";
    [SaveableField(21)] private int    _targetCount;
    [SaveableField(22)] private Settlement? _targetVillage;
    [SaveableField(23)] private RecruiterPhase _phase = RecruiterPhase.Travelling;

    [CachedData] private TextObject? _cachedName;

    // ── 只读 accessors（供 Dispatcher 统计在途数量） ──
    public string  TroopId      => _troopId;
    public int     TargetCount  => _targetCount;
    public Settlement? TargetVillage => _targetVillage;
    public RecruiterPhase Phase => _phase;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var home = HomeSettlementOrNull;
            var n = new TextObject("{=ST_HonorGuardRecruiterName}Honor Guard Recruiter — {SETTLEMENT}");
            n.SetTextVariable("SETTLEMENT",
                home?.Name ?? new TextObject("{=ST_Common_Unknown}unknown"));
            _cachedName = n;
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => true;

    protected override ExpenseCategory GetExpenseCategoryForKind() => ExpenseCategory.RecruiterSeed;

    // ── 构造函数（私有，由工厂调用） ──

    private HonorGuardRecruiterPartyComponent(
        Settlement home, TextObject name, Hero owner, InitializationArgs args)
        : base(home, name, owner,
               partyMountStringId: string.Empty,
               partyHarnessStringId: string.Empty,
               customPartyBaseSpeed: 0f,
               avoidHostileActions: true,
               args: args,
               leader: null)
    {
    }

    // ── 工厂 ──

    /// <summary>
    /// 创建 B 池征兵队 party。调用方负责：
    ///   1. 调 <see cref="StPartyComponent.TrySeedAndBuyInitialFood"/> 扣款/注资/买粮；
    ///   2. 调 <c>lifecycle.RegisterTrackedParty</c> 注册到 PartyLifecycleManager；
    ///   3. 发出 SetMoveGoToSettlement 到 targetVillage。
    /// </summary>
    public static MobileParty? Create(
        Settlement capital, Settlement targetVillage, string troopId, int targetCount)
    {
        if (capital == null || targetVillage == null || string.IsNullOrEmpty(troopId) || targetCount <= 0)
        {
            Logger.Warn($"HonorGuardRecruiterPartyComponent.Create: invalid args capital={capital?.StringId ?? "null"} village={targetVillage?.StringId ?? "null"} troop={troopId} count={targetCount}");
            return null;
        }
        try
        {
            var ownerClan   = capital.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"HonorGuardRecruiterPartyComponent.Create: capital '{capital.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyTroops    = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(capital.GatePosition, 1f, ownerClan, emptyTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_HonorGuardRecruiterName}Honor Guard Recruiter — {SETTLEMENT}");
            nameObj.SetTextVariable("SETTLEMENT", capital.Name);

            var component = new HonorGuardRecruiterPartyComponent(
                home: capital, name: nameObj, owner: ownerLeader, args: args);

            // StringId：日期 Ticks 后缀防多次并发派遣撞 ID。
            var stringId = StringIdPrefix + capital.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"HonorGuardRecruiterPartyComponent.Create: CreateParty returned null for '{stringId}'");
                return null;
            }

            // 写入派遣参数（保存字段，跨读档保持）。
            component._troopId      = troopId;
            component._targetCount  = targetCount;
            component._targetVillage = targetVillage;
            component._phase        = RecruiterPhase.Travelling;

            try { mobileParty.Aggressiveness = 0f;                   } catch { /* swallow */ }
            try { mobileParty.Ai?.SetDoNotMakeNewDecisions(true);    } catch { /* swallow */ }

            component.SnapshotInitialMembers(mobileParty);
            Logger.Info($"HonorGuardRecruiterPartyComponent: created '{stringId}' (troop={troopId} target={targetCount}) for '{capital.StringId}'");
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardRecruiterPartyComponent.Create failed", ex);
            return null;
        }
    }

    // ── 状态机 ──

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        switch (_phase)
        {
            case RecruiterPhase.Travelling: HandleTravelling(self, capital); break;
            case RecruiterPhase.AtVillage:  HandleAtVillage(self, capital);  break;
            case RecruiterPhase.Returning:  /* IsAtHome → OnArrivedHome 接管 */ break;
        }
    }

    private void HandleTravelling(MobileParty self, Settlement capital)
    {
        var currentSettlement = self.CurrentSettlement ?? self.LastVisitedSettlement;
        if (_targetVillage != null
            && currentSettlement == _targetVillage
            && currentSettlement.IsVillage
            && (self.TargetSettlement == null || self.TargetSettlement == _targetVillage))
        {
            _phase = RecruiterPhase.AtVillage;
            HandleAtVillage(self, capital);
            return;
        }

        // 目标失效 → 直接返航
        if (_targetVillage == null || !IsVillageRecruitmentValid(_targetVillage, capital))
        {
            Logger.Warn($"HonorGuardRecruiter '{PartyNameFormatter.SafeName(self)}': village invalid/null → returning");
            _phase = RecruiterPhase.Returning;
            SafeMoveHelper.GoToWithLeave(self, capital, "village invalid, return");
        }
        else if (self.TargetSettlement == null)
        {
            // 安全网：目标丢失时补设
            SafeMoveHelper.GoToWithLeave(self, _targetVillage, "re-set lost target");
        }
    }

    private void HandleAtVillage(MobileParty self, Settlement capital)
    {
        var currentSettlement = self.CurrentSettlement ?? self.LastVisitedSettlement;
        if (currentSettlement == null || !currentSettlement.IsVillage)
        {
            _phase = RecruiterPhase.Travelling;
            HandleTravelling(self, capital);
            return;
        }

        // 招募
        int recruited = 0;
        try { recruited = RecruitByTroopId(self, currentSettlement, capital); }
        catch (Exception ex) { Logger.Error($"HonorGuardRecruiter.HandleAtVillage RecruitByTroopId failed", ex); }

        if (recruited > 0)
            RecruitmentCooldown.MarkRecruited(currentSettlement);

        Logger.Info($"HonorGuardRecruiter '{PartyNameFormatter.SafeName(self)}': recruited {recruited} '{_troopId}' at '{currentSettlement.Name}'");

        // 完成 → 返航
        _phase = RecruiterPhase.Returning;
        SafeMoveHelper.GoToWithLeave(self, capital, "recruitment done, returning");
    }

    /// <summary>
    /// 抵达首府时：把 party 所有兵员转入 B 池 roster，退还资金，然后自销毁。
    /// 不调 DefaultMergeAndDisband（那会送到 garrison）。
    /// </summary>
    protected override void OnArrivedHome(MobileParty self)
    {
        try
        {
            var capital = HomeSettlementOrNull;
            if (capital == null)
            {
                Logger.Warn("HonorGuardRecruiter.OnArrivedHome: capital is null, destroying without transfer");
                Lifecycle.PartyMergeService.Instance?.DestroyAndUntrack(self, "HonorGuardRecruiter.OnArrivedHome(no capital)", deferIfInMapEvent: false);
                return;
            }

            var bPool = HonorGuardManager.GetPoolStatic(capital);
            var roster = self.MemberRoster;

            if (bPool != null && bPool.IsActive && roster != null)
            {
                // 按 B 池剩余容量转入（不超 HonorGuardCap 硬上限）
                int cap         = SovereignTowns.Configuration.ConfigurationManager.Current?.FiscalAutonomy?.HonorGuardCap ?? 0;
                int currentPool = bPool.MemberRoster?.TotalManCount ?? 0;
                int headroom    = Math.Max(0, cap - currentPool);

                int transferred = 0;
                // 遍历非英雄行，最多转 headroom 人
                for (int i = roster.Count - 1; i >= 0 && headroom > 0; i--)
                {
                    var element = roster.GetElementCopyAtIndex(i);
                    if (element.Character == null || element.Character.IsHero) continue;

                    int available    = element.Number;
                    int wounded      = element.WoundedNumber;
                    int healthy      = available - wounded;
                    int take         = Math.Min(healthy > 0 ? healthy : available, headroom);
                    if (take <= 0) continue;

                    // 同类兵放入 B 池，从本 roster 移除
                    try
                    {
                        int takeWounded = (healthy <= 0) ? take : Math.Max(0, Math.Min(wounded, take - healthy));
                        bPool.MemberRoster?.AddToCounts(element.Character, take, insertAtFront: false, woundedCount: takeWounded);
                        // RemoveTroop(troop, numberToRemove, troopSeed, xp) — no woundedCount param (ilspycmd verified).
                        // Remove all `take` (including wounded); AddToCounts above handles wounded transfer.
                        roster.RemoveTroop(element.Character, take, default, 0);
                        headroom    -= take;
                        transferred += take;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"HonorGuardRecruiter.OnArrivedHome: transfer troop '{element.Character?.StringId}' failed: {ex.Message}");
                    }
                }
                Logger.Info($"HonorGuardRecruiter '{PartyNameFormatter.SafeName(self)}': transferred {transferred} troops to honor-guard of '{capital.StringId}'");
            }
            else
            {
                Logger.Warn($"HonorGuardRecruiter '{PartyNameFormatter.SafeName(self)}': no active honor-guard for '{capital.StringId}', troops lost");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardRecruiter.OnArrivedHome: transfer phase failed", ex);
        }

        // 无论转移是否成功，退还资金并销毁
        try { RefundTeamFundsToOwner(); } catch { /* swallow */ }

        try
        {
            Lifecycle.PartyMergeService.Instance?.DestroyAndUntrack(self, "HonorGuardRecruiter.OnArrivedHome", deferIfInMapEvent: false);
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardRecruiter.OnArrivedHome: DestroyAndUntrack failed", ex);
        }
    }

    // ── 招募逻辑 ──

    /// <summary>
    /// 在 village 中查找 <see cref="_troopId"/> 匹配的志愿兵并招募。
    /// 使用 StRecruitContext.Enter() 放行 ST 自身的 VolunteerModel 过滤。
    /// (ilspycmd 核实：Hero.VolunteerTypes = CharacterObject[6]；
    ///  MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, -101) 用 -101 忽略关系门槛)
    /// </summary>
    private int RecruitByTroopId(MobileParty self, Settlement village, Settlement capital)
    {
        if (village?.Notables == null) return 0;
        var ownerHero = capital.OwnerClan?.Leader;
        if (ownerHero == null) return 0;

        var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
        if (volunteerModel == null) return 0;

        bool shouldCharge  = CapitalRegistry.ShouldChargeClan(capital.OwnerClan);
        const int CostPerRecruit = 5; // 5 denar/兵（玩家折半，与 StRecruiterPartyComponent 一致）
        int costPerRecruit = shouldCharge ? CostPerRecruit : 0;

        // 需要招的数量 = 目标 − 当前 roster 中同类兵
        int alreadyHave = CountTroopInRoster(self.MemberRoster, _troopId);
        int deficit     = Math.Max(0, _targetCount - alreadyHave);
        if (deficit <= 0) return 0;

        int recruited = 0;
        foreach (var notable in village.Notables)
        {
            if (notable == null || !notable.CanHaveRecruits) continue;
            var volunteerTypes = notable.VolunteerTypes;
            if (volunteerTypes == null) continue;

            int maxIdx;
            try
            {
                using (StRecruitContext.Enter())
                {
                    maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"HonorGuardRecruiter: MaximumIndexHeroCanRecruitFromHero threw: {ex.Message}");
                continue;
            }
            if (maxIdx < 0) continue;

            for (int i = 0; i < volunteerTypes.Length && i <= maxIdx; i++)
            {
                var troop = volunteerTypes[i];
                if (troop == null) continue;
                if (!string.Equals(troop.StringId, _troopId, StringComparison.Ordinal)) continue;
                if (deficit <= 0) break;

                // 扣费检查
                if (shouldCharge && costPerRecruit > 0
                    && !Economy.ModTreasury.CanAfford(capital.OwnerClan, costPerRecruit))
                    break;

                bool added = false;
                try
                {
                    self.AddElementToMemberRoster(troop, 1, false);
                    added = true;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"HonorGuardRecruiter: AddElementToMemberRoster failed for '{troop.StringId}': {ex.Message}");
                    continue;
                }

                if (shouldCharge && costPerRecruit > 0)
                {
                    if (!Economy.ModTreasury.Charge(capital.OwnerClan, ExpenseCategory.RecruiterWage, costPerRecruit,
                            $"honor_guard_recruit village={village.StringId} troop={_troopId}"))
                    {
                        // rollback
                        if (added)
                        {
                            try { self.MemberRoster?.RemoveTroop(troop, 1, default, 0); }
                            catch (Exception rollbackEx)
                            { Logger.Warn($"HonorGuardRecruiter: rollback failed: {rollbackEx.Message}"); }
                        }
                        break;
                    }
                }

                volunteerTypes[i] = null!; // 消费 slot（与 StRecruiterPartyComponent 同做法）
                recruited++;
                deficit--;
            }
        }

        return recruited;
    }

    // ── 小工具 ──

    private static bool IsVillageRecruitmentValid(Settlement village, Settlement home)
    {
        try
        {
            if (village == null || !village.IsVillage || !village.IsActive) return false;
            var villageFaction = village.MapFaction;
            var homeFaction    = home.MapFaction;
            if (villageFaction == null || homeFaction == null) return false;
            if (villageFaction != homeFaction && homeFaction.IsAtWarWith(villageFaction)) return false;
            var v = village.Village;
            return v != null
                && v.VillageState != Village.VillageStates.BeingRaided
                && v.VillageState != Village.VillageStates.Looted;
        }
        catch { return false; }
    }

    private static int CountTroopInRoster(TroopRoster? roster, string troopId)
    {
        if (roster == null || string.IsNullOrEmpty(troopId)) return 0;
        int count = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var e = roster.GetElementCopyAtIndex(i);
            if (e.Character?.StringId == troopId) count += e.Number;
        }
        return count;
    }
}
