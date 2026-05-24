using System;
using SovereignTowns.Common;
using SovereignTowns.Economy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 卫队（Honor Guard）party component。
/// 表示驻留在首府内的私属精锐 — 不出城、不巡逻、不被 vanilla AI 管理，
/// 仅在围城时由 <see cref="TaleWorlds.CampaignSystem.Settlements.Town.GetDefenderParties"/>
/// 自动枚举为守城兵力。
///
/// 关键设计点（基于 ilspycmd 反编译核实，2026-05-24）：
///   1. <c>IsGarrison</c> 由 <c>UpdatePartyComponentFlags</c> 按 <c>_partyComponent is GarrisonPartyComponent</c>
///      设置——本类不继承 GarrisonPartyComponent，故 IsGarrison=false，不触发 vanilla 驻军 AI 和薪资计算。
///   2. <c>CurrentSettlement = capital</c> setter 内部调 <c>capital.AddMobileParty(this)</c>，
///      令 party 自动进入 <c>Settlement.Parties</c> 集合——即 <c>GetDefenderParties</c> 枚举的来源。
///   3. <c>MapFaction</c> 的 getter：本组件无 ActualClan/PartyOwner → 回落到
///      <c>HomeSettlement.OwnerClan.MapFaction</c>（首府所属派系），满足 GetDefenderParties 的
///      <c>!IsAtWarWith(besieger)</c> 条件。
///   4. <see cref="PartyLifecycleManager"/> 的空闲检测仅针对 <c>_tracked</c> 字典内的 party；
///      卫队不注册进 _tracked，故不受 idle force-return/disband 影响。
///   5. <see cref="StPartyComponent.OnHourlyTick"/> 也仅由 PartyLifecycleManager 在 _tracked party
///      上调用——卫队的 OnArrivedHome / OnHourlyTickCore 在正常运行时永不触发，但作为 abstract 覆盖
///      的安全兜底均实现为 no-op。
///
/// SaveableField 槽位：基类占 [10, 20)；本类占 [20, +∞)（当前无额外字段，保留起点）。
/// StringId 前缀沿用 "st_bpool_"（per CLAUDE.md #3：已用过的 SaveId 永不复用）—— 老存档保持兼容。
/// </summary>
public sealed class HonorGuardPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_bpool_";

    [CachedData] private TextObject? _cachedName;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var home = HomeSettlementOrNull;
            var n = new TextObject("{=ST_HonorGuardPartyName}Honor Guard — {SETTLEMENT}");
            n.SetTextVariable("SETTLEMENT",
                home?.Name ?? new TextObject("{=ST_Common_Unknown}unknown"));
            _cachedName = n;
            return _cachedName;
        }
    }

    /// <summary>卫队永远驻留首府，不参与地图战斗。</summary>
    public override bool AvoidHostileActions => true;

    protected override ExpenseCategory GetExpenseCategoryForKind() => ExpenseCategory.PatrolSeed;

    private HonorGuardPartyComponent(
        Settlement home, TextObject name, Hero owner,
        InitializationArgs args)
        : base(home, name, owner,
               partyMountStringId: string.Empty,
               partyHarnessStringId: string.Empty,
               customPartyBaseSpeed: 0f,
               avoidHostileActions: true,
               args: args,
               leader: null)
    {
    }

    /// <summary>
    /// 工厂：为指定首府创建卫队 party，并将其安置在该首府内。
    /// StringId 按 <c>st_bpool_{capital.StringId}</c> 固定（前缀沿用历史值，避免与老存档冲突）。
    /// 每座首府全局唯一（无 Ticks 后缀）：HonorGuardManager 先扫 settlement.Parties 找到存量实例
    /// 注册；找不到才调本工厂——不会出现同 ID 重复创建。
    ///
    /// 注意：本工厂不注入兵员，调用方（MCMF 调度器）负责注入 / 遣散兵员。
    /// </summary>
    public static MobileParty? CreateForCapital(Settlement capital)
    {
        if (capital == null) return null;
        try
        {
            var ownerClan = capital.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"HonorGuardPartyComponent.CreateForCapital: capital '{capital.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyTroops = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(capital.GatePosition, 1f, ownerClan, emptyTroops, emptyPrisoners);

            var nameObj = new TextObject("{=ST_HonorGuardPartyName}Honor Guard — {SETTLEMENT}");
            nameObj.SetTextVariable("SETTLEMENT", capital.Name);

            var component = new HonorGuardPartyComponent(
                home: capital, name: nameObj, owner: ownerLeader,
                args: args);

            var stringId = StringIdPrefix + capital.StringId;
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"HonorGuardPartyComponent.CreateForCapital: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }

            // 卫队永驻首府：设 CurrentSettlement 把 party 加入 settlement.Parties，
            // 令 Town.GetDefenderParties() 在围城时枚举到本队。
            mobileParty.CurrentSettlement = capital;

            // 阻止 vanilla AI 干预（CustomPartyComponent 默认"无任务就回家"AI）。
            try { mobileParty.Ai?.SetDoNotMakeNewDecisions(true); } catch { }

            Logger.Info($"HonorGuardPartyComponent: created honor-guard '{stringId}' for capital '{capital.StringId}'");
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardPartyComponent.CreateForCapital failed", ex);
            return null;
        }
    }

    // ── StPartyComponent abstract overrides (no-op: 卫队不离开首府) ──

    protected override void OnArrivedHome(MobileParty self)
    {
        // 卫队永驻：不解散，不归还兵员。no-op.
    }

    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        // 卫队由 HonorGuardManager / MCMF 调度器管理兵员，不需要每 tick 状态机。no-op.
    }
}
