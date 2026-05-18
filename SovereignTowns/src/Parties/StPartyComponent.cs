using System;
using SovereignTowns.Common;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 4 种 ST 部件的抽象基类。提供 Template Method 编排：
///   - OnHourlyTick / OnMapEventEnded 由基类编排"通用前置 → 子类核心 → 通用后置"
///   - 受管 clan 校验、回城解散判定、Merge 流程全部在基类
///   - 子类只 override `*Core` 与 enum 状态机
///
/// SaveableField 槽位约定：基类用 [10, 20)；子类用 [20, +∞)。
/// </summary>
public abstract class StPartyComponent : CustomPartyComponent
{
    // ── 持久化字段 ──
    [SaveableField(10)] private Settlement? _homeSettlement;
    [SaveableField(11)] private int _initialMemberCount;
    // doc §20 #1 (T1)：所有 ST 队伍共享的自资金闭环。
    [SaveableField(12)] private int _teamFunds;

    // ── vanilla CustomPartyComponent 抽象成员 ──
    // 注：Name cache 留给子类（每个子类的 Name 文案不同），基类不持有 _cachedName。
    public override Settlement HomeSettlement
    {
        get
        {
            if (_homeSettlement == null)
                throw new InvalidOperationException($"{GetType().Name}.HomeSettlement is null — likely caused by corrupted save or missing SaveableField(10).");
            return _homeSettlement;
        }
    }

    /// <summary>
    /// 仅用于"必须容忍 null"的诊断 / 恢复路径（B16.4a P1-7 修复）：
    /// 例如 <see cref="SovereignTowns.Lifecycle.PartyLifecycleManager.RebuildFromCampaign"/>
    /// 在 collect 阶段需要安静跳过损坏存档中 _homeSettlement 为 null 的 component。
    /// 常规运行路径请仍走 <see cref="HomeSettlement"/>，由其抛出明确异常以便定位损坏数据。
    /// </summary>
    public Settlement? HomeSettlementOrNull => _homeSettlement;

    public override Hero? PartyOwner => _homeSettlement?.OwnerClan?.Leader;
    public abstract override TextObject Name { get; }
    public abstract override bool AvoidHostileActions { get; }

    /// 出发时兵员快照，用于"当前 / 出发 &lt; ratio"判定。
    public int InitialMemberCount => _initialMemberCount;

    /// 子类工厂在 MobileParty.CreateParty 之后立即调用，快照出发兵员数。
    /// 调用前 party.MemberRoster 必须已包含初始 troops。
    public void SnapshotInitialMembers(MobileParty self)
        => _initialMemberCount = self?.MemberRoster?.TotalManCount ?? 0;

    // ── 队伍资金 API（doc §20 #1, T1）──
    /// <summary>doc §20 #1 (T1) 重整：所有 4 类 ST 队伍统一初始资金。</summary>
    public const int DefaultSeedGold = 2000;

    /// <summary>当前队伍资金（第纳尔）。</summary>
    public int TeamFunds => _teamFunds;

    /// <summary>从首府所有者扣 amount 第纳尔作为初始资金（hero 不够时按可负担扣）。</summary>
    public void InitTeamFundsFromHomeOwner(MobileParty self, int amount)
    {
        var owner = HomeSettlementOrNull?.OwnerClan?.Leader;
        int charged = PartyEconomyHelper.ChargeHero(owner, amount);
        _teamFunds = charged;
        Logger.Info($"{GetType().Name} '{PartyNameFormatter.SafeName(self)}': team funds initialized = {charged}d (charged from '{owner?.StringId ?? "null"}')");
    }

    /// <summary>在 settlement 内用队伍资金购买 days 天食物。返回花费的第纳尔。</summary>
    public int BuyFoodAtSettlement(MobileParty self, Settlement settlement, float days)
        => PartyEconomyHelper.BuyFoodFromSettlement(self, settlement, days, ref _teamFunds);

    /// <summary>把战利品（非食物 item）卖给 settlement，金额加入队伍资金。返回收益。</summary>
    public int SellLootAtSettlement(MobileParty self, Settlement settlement)
        => PartyEconomyHelper.SellLootToSettlement(self, settlement, ref _teamFunds);

    /// <summary>把剩余资金还给首府所有者；清零 _teamFunds。返回还回的数额。</summary>
    public int RefundTeamFundsToOwner()
    {
        if (_teamFunds <= 0) return 0;
        var owner = HomeSettlementOrNull?.OwnerClan?.Leader;
        int refunded = PartyEconomyHelper.RefundHero(owner, _teamFunds);
        if (refunded > 0)
            Logger.Info($"{GetType().Name}: refunded {refunded}d team funds to '{owner?.StringId ?? "null"}'");
        _teamFunds = 0;
        return refunded;
    }

    /// <summary>由 Dispatcher 在玩家氏族 ModTreasury.Charge 成功后调用，直接把队伍资金设为 amount。</summary>
    public void SetTeamFunds(int amount)
    {
        _teamFunds = amount < 0 ? 0 : amount;
    }

    /// <summary>子类返回本类型对应的 ExpenseCategory（用于 ModTreasury 玩家路径退款记账）。</summary>
    protected abstract ExpenseCategory GetExpenseCategoryForKind();

    /// <summary>
    /// doc §20 #1 (T1)：统一 Dispatcher 端的"扣 seed gold + 注入 _teamFunds + 出发地买 3 天粮"。
    /// 玩家氏族：走 ModTreasury（受 PauseSpendingWhenBroke 门控，写 ledger + audit），扣款成功 SetTeamFunds(DefaultSeedGold)；
    ///           扣款失败返 false，调用方应回滚兵 + 销毁 party。
    /// AI 氏族：从 home owner hero.Gold 扣（vanilla 路径，按可用余额取），InitTeamFundsFromHomeOwner 内部不会失败（最低 0 资金继续）；
    ///         返 true。
    /// 创建后立即在 origin 用资金 BuyFoodAtSettlement(3 天)。
    /// </summary>
    public static bool TrySeedAndBuyInitialFood(
        StPartyComponent component,
        MobileParty party,
        Settlement origin,
        ExpenseCategory expenseCategory,
        Clan? chargeFromClan,
        string noteContext)
    {
        if (component == null || party == null || origin == null)
        {
            Logger.Warn($"TrySeedAndBuyInitialFood: null arg (component={(component == null ? "null" : "ok")} party={(party == null ? "null" : "ok")} origin={(origin == null ? "null" : "ok")})");
            return false;
        }
        bool shouldCharge = CapitalRegistry.ShouldChargeClan(chargeFromClan);
        if (shouldCharge)
        {
            if (!ModTreasury.CanAfford(DefaultSeedGold))
            {
                Logger.Info($"TrySeedAndBuyInitialFood: 玩家金币不足 (need {DefaultSeedGold}) — {noteContext}");
                return false;
            }
            if (!ModTreasury.Charge(expenseCategory, DefaultSeedGold, noteContext))
            {
                Logger.Info($"TrySeedAndBuyInitialFood: ModTreasury.Charge 拒绝 — {noteContext}");
                return false;
            }
            component.SetTeamFunds(DefaultSeedGold);
        }
        else
        {
            component.InitTeamFundsFromHomeOwner(party, DefaultSeedGold);
        }
        int spent = component.BuyFoodAtSettlement(party, origin, 3f);
        if (spent == 0)
        {
            // 非作弊基调：origin 食物缺货 → 取消派遣。
            // 调用方应 TransferBackToGarrison + DestroyAndUntrack，OnDestroyed → TryRefundOnDestroy 退还种子金。
            Logger.Info($"{component.GetType().Name}: '{PartyNameFormatter.SafeName(party)}' 出发地 '{origin.Name?.ToString() ?? origin.StringId}' 食物缺货 (BuyFood=0) — 取消派遣 ({noteContext})");
            return false;
        }
        return true;
    }

    // ── 通用调度（Template Method 模式）──

    /// vanilla HourlyTickPartyEvent 路由入口，由 PartyLifecycleManager 单点调用。
    public void OnHourlyTick(MobileParty self)
    {
        if (self == null) return;
        try
        {
            if (!ValidateAliveAndManaged(self, out var capital)) return;

            // 2026-05-18 诊断日志：印 hourly tick 入口状态，便于定位"出门即返回"等行为。
            try
            {
                var homeDbg = HomeSettlementOrNull;
                Logger.Debug($"[DIAG] {GetType().Name}.OnHourlyTick '{PartyNameFormatter.SafeName(self)}' home='{homeDbg?.Name?.ToString() ?? "null"}' cur='{self.CurrentSettlement?.Name?.ToString() ?? "null"}' lastVisited='{self.LastVisitedSettlement?.Name?.ToString() ?? "null"}' target='{self.TargetSettlement?.Name?.ToString() ?? "null"}' members={self.MemberRoster?.TotalManCount ?? -1}");
            }
            catch { /* swallow diagnostic */ }

            // 2026-05-18 fix: ST party (patrol/recruiter/transfer) 必须全程 SetDoNotMakeNewDecisions(true)，
            // 否则 vanilla AI 在两次 hourly tick 之间会接管 ST party 把 target 改回 home（CustomPartyComponent
            // 的 vanilla 默认行为是"无任务就回家"）。我们自己的 SetMoveGoToSettlement 不受此 flag 影响仍能强制
            // 设 target，所以全程 true 不影响 mod 的状态机；vanilla 不再自作主张回家。
            //
            // 旧 B17.4 A1 修复假设"入口 reset(false) 让 vanilla 自然接管，TryHoldForPlayerTarget 命中时再
            // 设 true"。实测：(false) 让 vanilla 在 tick 间把巡逻队/征兵队拖回 home。改为 (true) 后 hold
            // 释放仍工作 — 下次 tick mod 调 SetMoveGoToSettlement 自动覆盖 hold 状态。
            if (!AvoidsPlayerTargetHold)
            {
                try { self.Ai?.SetDoNotMakeNewDecisions(true); } catch { /* swallow */ }
            }

            // B17.4 B7：玩家被自家 ST 队伍俘获 → 立刻送回首府（IG MobileGarrison.CheckIfPlayerIsPrisonerInParty）
            if (TryReturnIfPlayerCaptured(self)) return;

            // B17.4 A1：玩家右键 attack/follow 我 → SetMoveModeHold 让玩家追上（IG OrderStopIfPlayerTarget）。
            // 注意：sally 队不应套用（冲锋中被玩家拦说明出问题），仍正常运行。
            if (!AvoidsPlayerTargetHold && TryHoldForPlayerTarget(self)) return;

            if (IsAtHome(self))
            {
                Logger.Debug($"[DIAG] {GetType().Name}.OnHourlyTick '{PartyNameFormatter.SafeName(self)}' IsAtHome=true → OnArrivedHome");
                OnArrivedHome(self);
                return;
            }

            // doc §20 #1 (T1)：经济维护——卖战利品 + 食物 <1 天买 3 天（所有 ST 队伍共享）
            TryEconomicMaintenance(self);

            OnHourlyTickCore(self, capital!);

            // B17.4 A6：tick 末尾通用维护 — 俘虏 cap。失败不影响 core 已完成的工作。
            // B5 食物补给已 deferred（out-of-scope）— IG 实现是凭空塞，与项目"非作弊基调"冲突；
            // vanilla 没有合适的 settlement-级 ItemRoster API 做"经济闭环"扣减。
            try { TryEnforcePrisonerCap(self); }
            catch (Exception capEx) { Logger.Warn($"{GetType().Name}.TryEnforcePrisonerCap failed: {capEx.Message}"); }
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnHourlyTick failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    /// vanilla MapEventEnded 路由入口，由 PartyLifecycleManager 单点调用。
    public void OnMapEventEnded(MapEvent ev, MobileParty self)
    {
        if (self == null) return;
        try
        {
            if (!ValidateAliveAndManaged(self, out _)) return;
            if (AppliesReturnDisbandCondition
                && PartyReturnConditionChecker.ShouldReturnAndDisband(self, _initialMemberCount, out var reason, out var detail))
            {
                Logger.Info($"{GetType().Name}.MapEventEnded: '{PartyNameFormatter.SafeName(self)}' return-disband ({reason}: {detail})");
                ReturnToHome(self);
                return;
            }
            OnMapEventEndedCore(ev, self);
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnMapEventEnded failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    /// MobilePartyDestroyed 路由入口，由 PartyLifecycleManager 单点调用。
    /// 默认行为：尝试将残余兵员合并入 home garrison（或 clan capital 作为 fallback）。
    /// 子类 override 时必须调用 base.OnDestroyed 并将副作用放在 finally 块中（如 SallyDispatcher 通知）。
    public virtual void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        try
        {
            // doc §20 #1 (T1)：退款剩余 _teamFunds（所有 ST 队伍共享）
            TryRefundOnDestroy(self);

            var home = HomeSettlementOrNull;
            var partyClan = self.ActualClan ?? home?.OwnerClan;
            Settlement? rescueTarget = null;
            var registry = CapitalRegistry.Instance;
            if (registry != null && partyClan != null)
            {
                if (home != null && home.OwnerClan == partyClan && registry.IsManagedClanWithCapital(partyClan))
                    rescueTarget = home;
                else
                    rescueTarget = registry.GetCapitalForClan(partyClan);
            }
            if (rescueTarget == null)
                Logger.Info($"{GetType().Name}.OnDestroyed: '{PartyNameFormatter.SafeName(self)}' home unavailable, no rescue");
            else
            {
                int rescued = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, rescueTarget, $"{GetType().Name}.OnDestroyed");
                if (rescued > 0)
                    Logger.Info($"{GetType().Name}.OnDestroyed: rescued {rescued} survivors to '{rescueTarget.Name}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnDestroyed failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    // ── 子类必须 / 可以实现的差异化部分 ──

    /// 子类的状态机核心。基类已确保：party.IsActive、受管 clan 合法、不在 home。
    protected abstract void OnHourlyTickCore(MobileParty self, Settlement capital);

    /// 战后自定义行为（基类已先判 ShouldReturnAndDisband，命中即回家，未到此方法）。默认 no-op。
    protected virtual void OnMapEventEndedCore(MapEvent ev, MobileParty self) { }

    /// 到达 home 时的处理。默认：转兵进 garrison + 解散。
    protected virtual void OnArrivedHome(MobileParty self) => DefaultMergeAndDisband(self);

    /// 是否应用回城解散条件（PartyReturnConditionChecker）。调拨队 override 为 false。
    protected virtual bool AppliesReturnDisbandCondition => true;

    /// <summary>B17.4 A1：sally 队等"冲锋型"队伍应 override = true，避免被玩家拦截后停下让冲锋失败。</summary>
    protected virtual bool AvoidsPlayerTargetHold => false;

    // ── 基类提供的通用动作（protected，子类可直接调用）──

    /// 校验 party.IsActive + 受管 clan + home 仍属本 clan。返回 true 表示后续逻辑可继续；
    /// false 时 capital 输出为 null，调用方应立即 return（基类调度路径会自动 return）。
    /// 失去归属 / home 失守等异常路径在此处理（DisbandAndUntrack 或 MergeGarrison 到 fallback capital）。
    protected bool ValidateAliveAndManaged(MobileParty self, out Settlement? capital)
    {
        capital = null;
        if (!self.IsActive) return false;

        var partyClan = self.ActualClan;
        if (partyClan == null)
        {
            Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' has null ActualClan; disbanding");
            PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} null ActualClan");
            return false;
        }

        var registry = CapitalRegistry.Instance;
        if (registry == null) return false;

        // B16.4a P1-7：用 OrNull 保持原 null 防御语义；HomeSettlement 在损坏存档下会抛诊断异常。
        var home = HomeSettlementOrNull;
        capital = registry.GetCapitalForClan(partyClan);
        if (home == null) return false;

        if (home.OwnerClan != partyClan)
        {
            if (capital != null)
            {
                Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' home '{home.Name}' lost; merging at capital '{capital.Name}'");
                MergeToFallback(self, capital);
            }
            else
            {
                Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' home '{home.Name}' lost and no fallback capital; disbanding");
                PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} lost home");
            }
            capital = null;
            return false;
        }

        if (capital == null) return false;  // managed clan 但当前无首府
        return true;
    }

    /// party 当前位置是否在 home。
    /// 2026-05-18 修复：原实现 `CurrentSettlement==home || LastVisitedSettlement==home` 在 v1.3.15
    /// 是错的 — vanilla 的 `LastVisitedSettlement` 在 party **离开** settlement 后仍指向那个 settlement，
    /// 直到进入下一个 settlement。结果：刚从首府出发的征兵队/巡逻队 第一个 hourly tick 被误判 IsAtHome=true → OnArrivedHome：
    ///   - 征兵队 → DefaultMergeAndDisband 立刻解散（"出门即返回"）
    ///   - 巡逻队 → 重新 OnHourlyTickCore 把 home 当抵达点 RecordVisit 一次（首府 MinVisitGap 卡住下次回访）
    /// 改为严格 `CurrentSettlement==home`：仅当 party 真停在 home 内才视为到家。
    protected bool IsAtHome(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return false;
        return self.CurrentSettlement == home;
    }

    /// 把 party 设回 home 方向（vanilla AI 接管移动）。
    protected void ReturnToHome(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return;
        try { self.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false); }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.ReturnToHome SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }

    /// 转兵进 home garrison + 解散 + untrack。
    protected void DefaultMergeAndDisband(MobileParty self)
    {
        // 2026-05-18 诊断日志：印出调用栈到 home，帮诊断"出门即解散"。
        try
        {
            var stack = new System.Diagnostics.StackTrace(true);
            Logger.Debug($"[DIAG] {GetType().Name}.DefaultMergeAndDisband ENTRY '{PartyNameFormatter.SafeName(self)}' members={self?.MemberRoster?.TotalManCount ?? -1} caller=\n{stack}");
        }
        catch { }

        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null)
        {
            PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} null home in DefaultMergeAndDisband");
            return;
        }
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, home, $"{GetType().Name}.DefaultMergeAndDisband");
        Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into '{home.Name}', disbanding");
        PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name}.DefaultMergeAndDisband");
    }

    /// 转兵进 fallback settlement + 解散 + untrack（home 失守时调用）。
    protected void MergeToFallback(MobileParty self, Settlement fallback)
    {
        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, fallback, $"{GetType().Name}.MergeToFallback");
        Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into fallback '{fallback.Name}', disbanding");
        PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name}.MergeToFallback");
    }

    // ── B17.4 共享 helpers ──

    /// <summary>
    /// doc §20 #1 (T1)：经济维护——所有 ST 队伍都"在 settlement.Town 内卖战利品入 _teamFunds"；
    /// 只有 Patrol/Recruiter（多 settlement 移动）需要"食物 &lt;1 天买 3 天"补粮逻辑。
    /// Sally/Transfer 是单目的地短命任务，没有沿途补给机会，故 override ShouldReplenishFoodEnRoute=false 跳过补粮。
    /// </summary>
    private void TryEconomicMaintenance(MobileParty self)
    {
        var atSettlement = self.CurrentSettlement;
        if (atSettlement == null || atSettlement.Town == null) return;

        // 1) 卖战利品（所有 ST 队伍共享，到 settlement 即变现入资金）
        try
        {
            int gained = SellLootAtSettlement(self, atSettlement);
            if (gained > 0)
                Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' sold loot at '{atSettlement.Name}' +{gained}d (funds={TeamFunds})");
        }
        catch (Exception ex) { Logger.Warn($"{GetType().Name}.TryEconomicMaintenance sell-loot threw: {ex.Message}"); }

        // 2) 补粮（仅 Patrol/Recruiter — 多 settlement 移动需要沿途补给）
        if (!ShouldReplenishFoodEnRoute) return;

        try
        {
            float daysLeft = PartyEconomyHelper.FoodDaysRemaining(self);
            if (daysLeft < 1f && TeamFunds > 0)
            {
                int spent = BuyFoodAtSettlement(self, atSettlement, 3f);
                if (spent > 0)
                    Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' food low ({daysLeft:F1}d) → bought at '{atSettlement.Name}' for {spent}d (funds={TeamFunds})");
            }
        }
        catch (Exception ex) { Logger.Warn($"{GetType().Name}.TryEconomicMaintenance food top-up threw: {ex.Message}"); }
    }

    /// <summary>
    /// 是否沿途自动补给（"食物 &lt;1 天买 3 天"）。
    /// 默认 true（Patrol/Recruiter）。Sally/Transfer override 为 false（单目的地短命任务，无沿途补给场景）。
    /// </summary>
    protected virtual bool ShouldReplenishFoodEnRoute => true;

    /// <summary>
    /// doc §20 #1 (T1)：销毁时退款。玩家氏族走 ModTreasury.Refund 保账目对称；AI 氏族走 RefundTeamFundsToOwner（vanilla hero.Gold）。
    /// _teamFunds=0 时双路径均 no-op。
    /// </summary>
    private void TryRefundOnDestroy(MobileParty self)
    {
        if (_teamFunds <= 0) return;
        try
        {
            var refundClan = self.ActualClan ?? HomeSettlementOrNull?.OwnerClan;
            if (CapitalRegistry.ShouldChargeClan(refundClan))
            {
                int toRefund = _teamFunds;
                _teamFunds = 0;
                Economy.ModTreasury.Refund(GetExpenseCategoryForKind(), toRefund,
                    $"{GetType().Name}_destroyed home={HomeSettlementOrNull?.StringId ?? "null"}");
                if (toRefund > 0)
                    Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {toRefund}d via ModTreasury (player clan)");
            }
            else
            {
                int refunded = RefundTeamFundsToOwner();
                if (refunded > 0)
                    Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {refunded}d to AI owner");
            }
        }
        catch (Exception ex) { Logger.Warn($"{GetType().Name}.TryRefundOnDestroy threw: {ex.Message}"); }
    }

    /// <summary>
    /// B17.4 B7：MainHero 被本 party 俘获 → 立刻返回 home 走 vanilla Dungeon 路径。返回 true 表示本 hour 提前结束。
    /// </summary>
    private bool TryReturnIfPlayerCaptured(MobileParty self)
    {
        try
        {
            var mainHero = Hero.MainHero;
            if (mainHero == null) return false;
            var prisoners = self.PrisonRoster;
            if (prisoners == null) return false;
            // PrisonRoster.Contains(CharacterObject) 在 v1.3.15 走 GetTroopRoster + element.Character 比对，
            // 对玩家 hero 也走这条路径（vanilla 把 MainHero 也封成 CharacterObject）。
            var characterObj = mainHero.CharacterObject;
            if (characterObj == null) return false;
            bool isPrisoner = false;
            foreach (var elt in prisoners.GetTroopRoster())
            {
                if (elt.Character == characterObj) { isPrisoner = true; break; }
            }
            if (!isPrisoner) return false;

            Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' captured MainHero — returning home '{HomeSettlementOrNull?.Name}' for normal release path");
            ReturnToHome(self);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryReturnIfPlayerCaptured failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// B17.4 A1：玩家氏族主队若 target == self（玩家右键 attack/follow），SetMoveModeHold 让玩家追上。
    /// 玩家放弃 target 后下一 hour 自然恢复（本方法只在 tick 入口短路）。返回 true 表示本 hour 提前结束。
    /// </summary>
    private bool TryHoldForPlayerTarget(MobileParty self)
    {
        try
        {
            var playerParty = Hero.MainHero?.PartyBelongedTo;
            if (playerParty == null || playerParty == self) return false;
            if (playerParty.TargetParty != self) return false;
            // 玩家锁定我 → hold + 让 AI 不再做新决策（仅本 hour 内）
            try { self.SetMoveModeHold(); }
            catch (Exception holdEx) { Logger.Warn($"SetMoveModeHold failed: {holdEx.Message}"); }
            try { self.Ai?.SetDoNotMakeNewDecisions(true); }
            catch (Exception aiEx) { Logger.Warn($"SetDoNotMakeNewDecisions(true) failed: {aiEx.Message}"); }
            Logger.Debug($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' holding for player target");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryHoldForPlayerTarget failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// B17.4 A6：俘虏 roster 超 cap 时随机踢非英雄。0 cap 关闭此功能。
    /// </summary>
    private void TryEnforcePrisonerCap(MobileParty self)
    {
        int cap = ConfigurationManager.Current?.Thresholds?.PartyPrisonerCap ?? 0;
        if (cap <= 0) return;
        var prisoners = self.PrisonRoster;
        if (prisoners == null) return;
        int total = prisoners.TotalManCount;
        if (total <= cap) return;
        int excess = total - cap;
        try
        {
            // IG MobileGarrison.CheckIfPrisonersIsAboveThreshold 走的就是 RemoveNumberOfNonHeroTroopsRandomly。
            // 不要用 RemoveIf(closure) — 闭包里的 excess 不会被 decrement,会一次性删光所有非英雄俘虏。
            prisoners.RemoveNumberOfNonHeroTroopsRandomly(excess);
            Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' prisoner overflow {total} > {cap}, dropped {excess} non-hero");
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryEnforcePrisonerCap RemoveNumberOfNonHeroTroopsRandomly failed: {ex.Message}");
        }
    }

    // B17.4 B5（食物补给）已 deferred — 留空。下面解释：
    // - IG GivePartyFood（PartyManager.cs:329）是凭空塞食物（party.ItemRoster.AddToCounts(item, N)，无源头扣减），
    //   IG 论坛投诉过"无限粮草作弊"。
    // - "从 home town ItemRoster 扣减"做不到：vanilla Town.Owner 是 Hero（无 ItemRoster），settlement 级也没有公开的市场粮食操作 API。
    // - 项目 CLAUDE.md "非作弊基调" + B5 是 Tier B（非关键），现阶段直接放弃；
    //   ST idle 检测（PartyLifecycleManager.IdleHoursBeforeDisband=36）会兜底"完全卡死饿死的队伍"。

    // ── 构造函数：透传 vanilla CustomPartyComponent 既有 protected 形参 ──
    protected StPartyComponent(
        Settlement home, TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(home, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _homeSettlement = home;
    }
}
