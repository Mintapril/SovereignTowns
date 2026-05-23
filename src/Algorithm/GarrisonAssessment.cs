namespace SovereignTowns.Algorithm;

/// <summary>
/// 手动驻军目标模式下的"评估"DTO：路由 MCMF 使用玩家手动设定的目标，但 Pass A
/// 仍然运行作为推荐值，由此 DTO 把玩家目标 vs 推荐目标的差异（日工资差额、闭环是否成立）
/// 暴露给控制面板。字段为公开字段（按 Task 6 规格，非属性）。
/// </summary>
public sealed class GarrisonAssessment
{
    public string SettlementId = "";
    public int PlayerTarget;
    public int RecommendedTarget;
    public int DailyWageDelta;          // (player - recommended) × wagePerTroop
    public bool LoopClosesAtPlayerTarget;
}
