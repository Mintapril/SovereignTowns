using System;
using SovereignTowns.Common;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;
// TaleWorlds.Library.ConfigurationManager 与本 mod 的 ConfigurationManager 重名 — 用 alias 消歧。
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

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
    // 槽位 12 已废弃（2026-05-24 Plan C）：原 PartyTradeGold 改为复用 vanilla MobileParty.PartyTradeGold
    // （vanilla 自动持久化 _partyTradeGold；GiveGoldAction.ApplyForXxxToParty 自动维护）。
    // 按 CLAUDE.md #3/#7 槽位永不复用 —— 此槽位空缺,后续 SaveableField 必须从 13+ 起。
    // 2026-05-18 v4：标记 DefaultMergeAndDisband 已显示左下角消息，避免 OnDestroyed 重复显示。
    // CachedData 不持久化 — 部队即将销毁，重复显示概率为零。
    [CachedData] private bool _disbandReportShown;

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

    /// <summary>当前队伍资金（第纳尔）= vanilla <see cref="MobileParty.PartyTradeGold"/>。</summary>
    public int TeamFunds => MobileParty?.PartyTradeGold ?? 0;

    /// <summary>从首府所有者（=氏族领袖）扣 amount 第纳尔注入 party.PartyTradeGold（hero 不够时按可负担扣）。
    /// 调 vanilla <see cref="GiveGoldAction.ApplyForCharacterToParty"/> 原子完成 hero.Gold -= + party.PartyTradeGold +=。</summary>
    public void InitTeamFundsFromHomeOwner(MobileParty self, int amount)
    {
        var owner = HomeSettlementOrNull?.OwnerClan?.Leader;
        if (owner == null || self?.Party == null || amount <= 0) return;
        int wantTransfer = Math.Min(owner.Gold, amount);
        if (wantTransfer <= 0) return;
        GiveGoldAction.ApplyForCharacterToParty(owner, self.Party, wantTransfer, disableNotification: true);
        Logger.Info($"{GetType().Name} '{PartyNameFormatter.SafeName(self)}': team funds initialized = {self.PartyTradeGold}d (charged from '{owner.StringId}')");
    }

    /// <summary>在 settlement 内用 PartyTradeGold 购买 days 天食物。返回花费第纳尔。</summary>
    public int BuyFoodAtSettlement(MobileParty self, Settlement settlement, float days)
        => PartyEconomyHelper.BuyFoodFromSettlement(self, settlement, days);

    /// <summary>购买松散坐骑（无马步兵骑乘提升移速）。返回花费。</summary>
    public int BuyHorsesAtSettlement(MobileParty self, Settlement settlement)
        => PartyEconomyHelper.BuyHorsesFromSettlement(self, settlement);

    /// <summary>把战利品（非食物 item）卖给 settlement，金额自动加到 PartyTradeGold（vanilla GiveGoldAction）。返回收益。</summary>
    public int SellLootAtSettlement(MobileParty self, Settlement settlement)
        => PartyEconomyHelper.SellLootToSettlement(self, settlement);

    /// <summary>把 PartyTradeGold 剩余资金退还首府所有者（=氏族领袖）。返回还回的数额。
    /// 调 vanilla <see cref="GiveGoldAction.ApplyForPartyToCharacter"/> 原子完成 party.PartyTradeGold -= + hero.Gold +=。</summary>
    public int RefundTeamFundsToOwner()
    {
        var self = MobileParty;
        if (self?.Party == null) return 0;
        int refund = self.PartyTradeGold;
        if (refund <= 0) return 0;
        var owner = HomeSettlementOrNull?.OwnerClan?.Leader;
        if (owner == null) return 0;
        GiveGoldAction.ApplyForPartyToCharacter(self.Party, owner, refund, disableNotification: true);
        Logger.Info($"{GetType().Name}: refunded {refund}d team funds to '{owner.StringId}'");
        return refund;
    }

    /// <summary>由 Dispatcher 在玩家氏族 ModTreasury.Charge 成功后调用，把 PartyTradeGold 设为 amount。
    /// vanilla PartyTradeGold setter 自带 Max(value, 0) clamp。</summary>
    public void SetTeamFunds(int amount)
    {
        var self = MobileParty;
        if (self == null) return;
        self.PartyTradeGold = amount < 0 ? 0 : amount;
    }

    /// <summary>子类返回本类型对应的 ExpenseCategory（用于 ModTreasury 玩家路径退款记账）。</summary>
    protected abstract ExpenseCategory GetExpenseCategoryForKind();

    /// <summary>
    /// doc §20 #1 (T1)：统一 Dispatcher 端的"扣 seed gold + 注入 PartyTradeGold + 出发地买 3 天粮"。
    /// 玩家氏族：走 ModTreasury（受 PauseSpendingWhenBroke 门控，写 ledger + audit），扣款成功 SetTeamFunds(DefaultSeedGold)；
    ///           扣款失败返 false，调用方应回滚兵 + 销毁 party。
    /// AI 氏族：从 home.OwnerClan.Leader.Gold 扣（vanilla 路径，按可用余额取；Clan.Gold ≡ Leader.Gold，
    ///         vanilla 没有 per-fief Hero 持有人）。InitTeamFundsFromHomeOwner 内部不会失败（最低 0 资金继续）；
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
            if (!ModTreasury.CanAfford(chargeFromClan, DefaultSeedGold))
            {
                Logger.Info($"TrySeedAndBuyInitialFood: 资金不足 (need {DefaultSeedGold}) — {noteContext}");
                return false;
            }
            if (!ModTreasury.Charge(chargeFromClan, expenseCategory, DefaultSeedGold, noteContext))
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
        // 出门前买马：让无马步兵骑乘，避免大地图移速被拖慢。best-effort，买不到不取消派遣。
        try { component.BuyHorsesAtSettlement(party, origin); }
        catch (Exception horseEx) { Logger.Warn($"TrySeedAndBuyInitialFood: BuyHorses failed — {horseEx.Message}"); }
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

            // 2026-05-18 修复 v3：经济维护从 base 这里移除。原因：hourly tick 入口时 CurrentSettlement
            // 几乎永远是 null（party 进入 settlement 后被 GoToWithLeave 立即弹出），导致 maintenance
            // 永远 early-return。改由 patrol arrival 分支与 recruiter HandleAtVillage 在已知抵达的
            // settlement 上下文中显式调用 TryEconomicMaintenance(self, justArrived) — 语义干净且首次
            // 真正在抵达瞬间触发卖战利品/补粮。Sally/Transfer 是单目的地短命任务，无需此机制。
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

            // 2026-05-18 v4：战斗结果左下角消息（仅玩家氏族部队，避免 AI 部队刷屏）。
            TryDisplayBattleResultMessage(ev, self);

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

    /// <summary>
    /// 2026-05-18 v4：战斗结束后在左下角显示一行简洁的战况报告（仅玩家氏族部队）。
    /// 颜色按损失程度：&lt;20% 黄、20-50% 橙、&gt;50% 红。
    /// </summary>
    private void TryDisplayBattleResultMessage(MapEvent ev, MobileParty self)
    {
        try
        {
            if (!CapitalRegistry.ShouldChargeClan(self.ActualClan)) return;
            int current = self.MemberRoster?.TotalManCount ?? 0;
            int wounded = self.MemberRoster?.TotalWounded ?? 0;
            int initial = _initialMemberCount;
            int casualties = Math.Max(0, initial - current);
            float lossRatio = initial > 0 ? (float)casualties / initial : 0f;
            Color color;
            TextObject verdict;
            if (lossRatio >= 0.5f) { color = Colors.Red; verdict = new TextObject("{=ST_Battle_Verdict_Heavy}took heavy losses"); }
            else if (lossRatio >= 0.2f) { color = new Color(1.0f, 0.6f, 0.2f); verdict = new TextObject("{=ST_Battle_Verdict_Damaged}suffered damage"); }
            else { color = Colors.Yellow; verdict = new TextObject("{=ST_Battle_Verdict_Won}completed the battle"); }
            var partyName = (TextObject?)Name ?? new TextObject("{=ST_Common_UnknownEntity}(unknown)");
            var template = new TextObject(
                "{=ST_Msg_Battle_Report}[Sovereign Towns] {PARTY_NAME} {VERDICT}: troops {CURRENT}/{INITIAL}, wounded {WOUNDED}.");
            template.SetTextVariable("PARTY_NAME", partyName);
            template.SetTextVariable("VERDICT", verdict);
            template.SetTextVariable("CURRENT", current);
            template.SetTextVariable("INITIAL", initial);
            template.SetTextVariable("WOUNDED", wounded);
            InformationManager.DisplayMessage(new InformationMessage(template.ToString(), color));
            Logger.Info($"OnMapEventEnded battle-report '{PartyNameFormatter.SafeName(self)}' current={current}/{initial} casualties={casualties} wounded={wounded} verdict={verdict}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryDisplayBattleResultMessage failed: {ex.Message}");
        }
    }

    /// MobilePartyDestroyed 路由入口，由 PartyLifecycleManager 单点调用。
    /// 默认行为：尝试将残余兵员合并入 home garrison（或 clan capital 作为 fallback）。
    /// 子类 override 时必须调用 base.OnDestroyed 并将副作用放在 finally 块中（如 SallyDispatcher 通知）。
    public virtual void OnDestroyed(MobileParty self, PartyBase? destroyer)
    {
        try
        {
            // 2026-05-18 v4：战败 / 异常销毁路径（未走 DefaultMergeAndDisband）的兜底左下角消息。
            // _disbandReportShown=true 表示已由 DefaultMergeAndDisband 显示过完整汇总，本路径跳过避免重复。
            TryDisplayDestroyedFallbackMessage(self, destroyer);

            // doc §20 #1 (T1)：退款剩余 PartyTradeGold（所有 ST 队伍共享）
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
    /// 2026-05-18 修复 v2：用 GoToWithLeave 显式 LeaveSettlement，避免 ReturnToHome 在 party 当前
    /// 在非-home settlement 内时因 SetDoNotMakeNewDecisions(true) 而无法离开。
    protected void ReturnToHome(MobileParty self)
    {
        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null) return;
        SovereignTowns.Common.SafeMoveHelper.GoToWithLeave(self, home, $"{GetType().Name}.ReturnToHome");
    }

    /// 转兵进 home garrison + 解散 + untrack。
    /// 2026-05-18 v4：解散前最终清算 — 把剩余物资（含食物）卖给 home town，资金随后由 OnDestroyed → TryRefundOnDestroy 退给 owner。
    /// 完成后左下角显示一行汇总：合并兵员、卖物资收益、退款金额。
    protected void DefaultMergeAndDisband(MobileParty self)
    {
        // 2026-05-18 诊断日志：印出调用栈到 home，帮诊断"出门即解散"。
        try
        {
            var stack = new System.Diagnostics.StackTrace(true);
            Logger.Debug($"[DIAG] {GetType().Name}.DefaultMergeAndDisband ENTRY '{PartyNameFormatter.SafeName(self)}' members={self?.MemberRoster?.TotalManCount ?? -1} caller=\n{stack}");
        }
        catch { }

        if (self == null) return;

        // B16.4a P1-7：用 OrNull 保持原 null 防御语义。
        var home = HomeSettlementOrNull;
        if (home == null)
        {
            PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name} null home in DefaultMergeAndDisband");
            return;
        }

        // 2026-05-18 v4：解散前最终清算 — 卖光所有物资（含食物）入 PartyTradeGold。
        int soldGained = 0;
        try { soldGained = SellAllItemsAtSettlement(self, home); }
        catch (Exception sellEx) { Logger.Warn($"{GetType().Name}.DefaultMergeAndDisband final-liquidation failed: {sellEx.Message}"); }

        int transferred = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, home, $"{GetType().Name}.DefaultMergeAndDisband");

        // 2026-05-18 v4：左下角汇总消息（仅玩家氏族部队），含将退给首府所有者的资金额。
        TryDisplayDisbandReport(self, home, transferred, soldGained);
        _disbandReportShown = true;

        Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' merged {transferred} troops into '{home.Name}', sold {soldGained}d in goods, disbanding (funds before refund = {TeamFunds}d)");
        PartyMergeService.Instance.DisbandAndUntrack(self, $"{GetType().Name}.DefaultMergeAndDisband");
    }

    /// <summary>解散前最终清算 wrapper — 把 ItemRoster 所有物品（含食物）卖给 settlement，收益自动入 PartyTradeGold。</summary>
    protected int SellAllItemsAtSettlement(MobileParty self, Settlement settlement)
        => PartyEconomyHelper.SellAllItemsToSettlement(self, settlement);

    /// <summary>
    /// 2026-05-18 v4：DefaultMergeAndDisband 汇总左下角消息（仅玩家氏族）。退款额预读自 PartyTradeGold —
    /// 实际退款由后续 DisbandAndUntrack → OnDestroyed → TryRefundOnDestroy 完成；同 frame 内显示与退款一致。
    /// </summary>
    private void TryDisplayDisbandReport(MobileParty self, Settlement home, int troopsTransferred, int soldGained)
    {
        try
        {
            if (!CapitalRegistry.ShouldChargeClan(self.ActualClan)) return;
            int refundAmount = TeamFunds;  // 退款数 = 当前队伍资金（即将被 TryRefundOnDestroy 退还）
            var ownerNameObj = (TextObject?)home?.OwnerClan?.Leader?.Name
                ?? new TextObject("{=ST_Common_CapitalOwner}the capital owner");
            var partyNameObj = (TextObject?)Name ?? new TextObject("{=ST_Common_UnknownEntity}(unknown)");
            var homeNameObj = (TextObject?)home?.Name ?? new TextObject("{=ST_Common_Unknown}unknown");
            var template = new TextObject(
                "{=ST_Msg_Disband_Report}[Sovereign Towns] {PARTY_NAME} returned to {HOME} and disbanded: merged {TROOPS} troops into the garrison, sold goods for +{SOLD}d, refunded {REFUND}d to {OWNER}.");
            template.SetTextVariable("PARTY_NAME", partyNameObj);
            template.SetTextVariable("HOME", homeNameObj);
            template.SetTextVariable("TROOPS", troopsTransferred);
            template.SetTextVariable("SOLD", soldGained);
            template.SetTextVariable("REFUND", refundAmount);
            template.SetTextVariable("OWNER", ownerNameObj);
            InformationManager.DisplayMessage(new InformationMessage(template.ToString(), Colors.Green));
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryDisplayDisbandReport failed: {ex.Message}");
        }
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
    /// doc §20 #1 (T1)：经济维护——所有 ST 队伍都"在 settlement.Town 内卖战利品入 PartyTradeGold"；
    /// 只有 Patrol/Recruiter（多 settlement 移动）需要"食物 &lt;1 天买 3 天"补粮逻辑。
    /// Sally/Transfer 是单目的地短命任务，没有沿途补给机会，故 override ShouldReplenishFoodEnRoute=false 跳过补粮。
    /// </summary>
    /// <summary>
    /// doc §20 #1 (T1)：经济维护——所有 ST 队伍都在 settlement.Town 内卖战利品入 PartyTradeGold；
    /// Patrol/Recruiter 还会在食物 &lt;1 天时补 3 天粮。
    ///
    /// 2026-05-18 v3：调用时机改由子类在 arrival 上下文中显式触发（patrol arrival 分支 + recruiter
    /// HandleAtVillage 末尾）。<paramref name="overrideSettlement"/> 传 just-arrived 的 settlement，
    /// 绕开 hourly tick 入口时 CurrentSettlement 几乎永远是 null 的时机问题。
    /// 不传则按旧路径用 CurrentSettlement（保留给 sally/transfer 等仍走 base 流程的子类）。
    /// </summary>
    protected void TryEconomicMaintenance(MobileParty self, Settlement? overrideSettlement = null)
    {
        var atSettlement = overrideSettlement ?? self.CurrentSettlement;
        // 2026-05-18：诊断 — 入口印一行 Info 帮助定位"部队不买食物 / 不卖战利品"问题。
        // 是 Info 级（不依赖 VerboseLogging）因为食物 bug 现在是头号 blocker，需要在缺省日志里就看到。
        // 主要诊断维度：本 tick 有没有"在某 settlement 内"、是 town 还是 village、食物剩余、队伍资金。
        try
        {
            float foodDays = PartyEconomyHelper.FoodDaysRemaining(self);
            bool isTown = atSettlement?.IsTown == true;
            bool isVillage = atSettlement?.IsVillage == true;
            bool hasTownComponent = atSettlement?.Town != null;
            string srcTag = overrideSettlement != null ? "arrival-override" : "currentSettlement";
            Logger.Info($"[ECON-DIAG] {GetType().Name}.TryEconomicMaintenance '{PartyNameFormatter.SafeName(self)}' src={srcTag} atSettlement='{atSettlement?.Name?.ToString() ?? "<null>"}' isTown={isTown} isVillage={isVillage} hasTownComponent={hasTownComponent} foodDays={foodDays:F1} teamFunds={TeamFunds} replenishEnRoute={ShouldReplenishFoodEnRoute}");
        }
        catch { /* swallow diagnostic */ }
        // 2026-05-18 v4：放开 Town 检查 — village 也允许走维护（BuyFood / SellLoot 内部用 Village.Bound.Town 定价）。
        // 仅当 atSettlement 完全空（en route 状态）或 既非 Town 又非 Village（hideout 等）才跳过。
        if (atSettlement == null || (!atSettlement.IsTown && !atSettlement.IsVillage))
        {
            return;
        }

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
    /// 2026-05-18 v4：战败 / 异常路径（未走 DefaultMergeAndDisband）的 fallback 左下角消息。
    /// _disbandReportShown=true 表示已被 DefaultMergeAndDisband 覆盖，跳过。
    /// </summary>
    private void TryDisplayDestroyedFallbackMessage(MobileParty self, PartyBase? destroyer)
    {
        try
        {
            if (_disbandReportShown) return;
            if (!CapitalRegistry.ShouldChargeClan(self.ActualClan)) return;
            var destroyerNameObj = (TextObject?)destroyer?.Name
                ?? new TextObject("{=ST_Common_UnknownEntity}(unknown)");
            int refundAmount = TeamFunds;
            var partyNameObj = (TextObject?)Name ?? new TextObject("{=ST_Common_UnknownEntity}(unknown)");
            TextObject template;
            if (refundAmount > 0)
            {
                template = new TextObject(
                    "{=ST_Msg_Destroyed_WithRefund}[Sovereign Towns] {PARTY_NAME} destroyed (by {DESTROYER}): refunded {REFUND}d to the capital owner.");
                template.SetTextVariable("REFUND", refundAmount);
            }
            else
            {
                template = new TextObject(
                    "{=ST_Msg_Destroyed_NoRefund}[Sovereign Towns] {PARTY_NAME} destroyed (by {DESTROYER}).");
            }
            template.SetTextVariable("PARTY_NAME", partyNameObj);
            template.SetTextVariable("DESTROYER", destroyerNameObj);
            InformationManager.DisplayMessage(new InformationMessage(template.ToString(), Colors.Red));
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryDisplayDestroyedFallbackMessage failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 销毁时退款 PartyTradeGold 给首府所有者（=氏族领袖）。
    /// 玩家氏族：ModTreasury.Refund 走 ClanGoldAccess（leader.Gold += amount）+ 做 ledger/audit/snapshot；
    ///           随后 SetTeamFunds(0) 清空 PartyTradeGold —— 两步净效果 = 转账 party→leader。
    /// AI 氏族：RefundTeamFundsToOwner 用 vanilla GiveGoldAction.ApplyForPartyToCharacter 一步原子转账。
    /// PartyTradeGold=0 时双路径均 no-op。
    /// </summary>
    private void TryRefundOnDestroy(MobileParty self)
    {
        int balance = self?.PartyTradeGold ?? 0;
        if (balance <= 0) return;
        try
        {
            var refundClan = self.ActualClan ?? HomeSettlementOrNull?.OwnerClan;
            if (CapitalRegistry.ShouldChargeClan(refundClan))
            {
                // 玩家氏族：先清 party 钱袋，再给 leader（ModTreasury 内部走 leader.ChangeHeroGold + ledger）
                SetTeamFunds(0);
                Economy.ModTreasury.Refund(refundClan, GetExpenseCategoryForKind(), balance,
                    $"{GetType().Name}_destroyed home={HomeSettlementOrNull?.StringId ?? "null"}");
                Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {balance}d via ModTreasury (player clan)");
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
