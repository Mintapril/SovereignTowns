using System;
using SovereignTowns.Common;
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
/// <b>已废弃</b>（2026-05-28，卫队招募统一）：本类的全部行为已折进
/// <see cref="StRecruiterPartyComponent"/>（Mode=HonorGuardPrecise）。
///
/// 保留本类仅出于存档兼容：CLAUDE.md #3 规定 SaveableField 槽位与 TypeDefiner 注册项永不复用/重排。
/// SovereignTownsTypeDefiner 仍按 local id 10 注册类型、103 注册 <see cref="RecruiterPhase"/> 枚举，
/// 槽位 20–23 仍声明字段并保留同类型，旧存档反序列化可命中本类槽位不报错。
/// 新代码不应再实例化本类；运行时不挂任何 OnHourlyTick / OnArrivedHome 行为。
///
/// 老存档中若有正在飞行的本类 party，读档后将被基类 ValidateAliveAndManaged + 空 core
/// 转入 lifecycle idle 检测，36h 后自动 DisbandPartyAction.StartDisband 解散（兵员通过 OnDestroyed
/// 兜底 MergeNonHeroTroopsIntoGarrison 救回 garrison；玩家氏族 seed 金通过 TryRefundOnDestroy 退还）。
/// </summary>
[Obsolete("Replaced by StRecruiterPartyComponent(Mode=HonorGuardPrecise). Retained only for save-load compat.")]
public sealed class HonorGuardRecruiterPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_bpr_";

    // ── 阶段枚举（保留 — TypeDefiner enum id 103 注册依赖此类型存在） ──
    public enum RecruiterPhase
    {
        Travelling = 0,
        AtVillage  = 1,
        Returning  = 2,
    }

    // ── SaveableField 槽位（CLAUDE.md #3 永不复用/重排） ──
    [SaveableField(20)] private string _troopId = "";
    [SaveableField(21)] private int    _targetCount;
    [SaveableField(22)] private Settlement? _targetVillage;
    [SaveableField(23)] private RecruiterPhase _phase = RecruiterPhase.Travelling;

    [CachedData] private TextObject? _cachedName;

    // ── 只读 accessors（保留供向后兼容外部读取，不再用于调度决策） ──
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

    protected override Economy.ExpenseCategory GetExpenseCategoryForKind() => Economy.ExpenseCategory.RecruiterSeed;

    /// <summary>本类已废弃，构造函数仅为 SaveSystem 反序列化保留。新代码请改用 <see cref="StRecruiterPartyComponent"/>。</summary>
    [Obsolete("Use StRecruiterPartyComponent with Mode=HonorGuardPrecise instead.")]
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

    /// <summary>
    /// inert：本类不再驱动状态机。基类 OnHourlyTick 调本方法时 no-op，让 lifecycle idle 检测最终
    /// 把残留旧存档中的本类 party 自然解散（兵员通过 OnDestroyed 兜底救回 garrison）。
    /// </summary>
    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        Logger.Debug($"[INERT] HonorGuardRecruiterPartyComponent.OnHourlyTickCore (deprecated stub) '{PartyNameFormatter.SafeName(self)}' — awaiting idle disband");
    }
}
