namespace SovereignTowns.Upgrades;

/// <summary>
/// 2026-05-24 deprecated:升级触发权完全移交 vanilla PartyUpgraderCampaignBehavior。
///
/// 之前路径:GarrisonXpInjector 每日 tick 调本类的 TryUpgradeGarrison,按 vanilla
/// UpgradeTargets + ScoreUpgradeTarget(模板分数)选最匹配升级目标 + 走 ApplyUpgradeAction。
///
/// 新路径:
///   1. UnifiedGarrisonSolver 在 G[s,r,t,τ]→G[s,r,t+1,τ+xpTicks] 上建 upgrade edge,
///      cost = vanilla tier-aware GoldByTier / XpByTier 公式(反编译核实)。
///   2. MCMF 决定升级流量(模板内 holding cap > 0 → 流量经 upgrade edge 升 tier 到模板内)。
///   3. 实际升级动作由 vanilla PartyUpgraderCampaignBehavior 在 MapEventEnded /
///      DailyTickPartyEvent 自动触发(garrison party 走 vanilla 加权随机)。
///   4. 模板偏差由 next-tick MCMF 在 holding cap / disband / patrol 上 self-correct。
///
/// 整个本服务保留为空壳占位以避免存档迁移意外;后续可由用户直接删除整个文件。
///
/// 相关清理:
///   - GlobalConfig.AutoUpgrade* 三字段(MinTierRatio / MinBudget / MaxPerCall)已删除。
///   - ConfigurationManager 对应 validation 已删除。
///   - ControlPanelSpecs 对应 spec entries 已删除。
/// </summary>
internal static class TroopUpgradeService
{
}
