namespace SovereignTowns.Configuration;

/// <summary>
/// 方案2(双层 MCMF 合并)parallel-run 三态。详见 audits/mcmf-merge-design.md §7。
/// <list type="bullet">
///   <item><c>LegacyOnly</c>：仅跑旧 Pass A + Pass B,派发旧结果(= 合并前行为)。</item>
///   <item><c>ShadowMerged</c>：旧两层照常派发;额外跑合并 solver,只记差异日志、不派发。</item>
///   <item><c>MergedOnly</c>：仅派发合并 solver 结果。M1 阶段 decode 未落地,暂等同 ShadowMerged。</item>
/// </list>
/// </summary>
public enum MergedSolverMode
{
    LegacyOnly,
    ShadowMerged,
    MergedOnly,
}

/// <summary>财政自治 + 中央驻军调度器配置。详见两份设计文档。</summary>
public sealed class FiscalAutonomyConfig
{
    // ── 金库 / 预算 ──
    public float GarrisonWageBudgetRatio { get; set; } = 0.55f;
    public int   TreasuryBufferDays      { get; set; } = 30;
    public bool  PlayerClanSubsidyWhenTreasuryEmpty { get; set; } = true;

    // ── 遣散超额 ──
    public int   MinGarrisonFloor        { get; set; } = 40;
    public float DisbandExcessThreshold  { get; set; } = 1.2f;
    public bool  DisbandUnaffordableExcess { get; set; } = true;

    // ── 手动模式 ──
    public bool  AllowManualGarrisonTargets { get; set; } = false;

    // ── 分配 MCMF 价值函数 ──
    public int   ValueFloorBase          { get; set; } = 1000;
    public int   ValueCoreBase           { get; set; } = 100;
    public int   SurplusEdgeCost         { get; set; } = 1;
    public int   AdequateBase            { get; set; } = 60;
    public int   AdequateProsperityDivisor { get; set; } = 80;
    public int   AdequateThreatWeight    { get; set; } = 8;
    public int   CoreTierCount           { get; set; } = 5;
    public int   MaxGarrisonHardCap      { get; set; } = 400;

    /// <summary>城镇 adequate 下限锚定:adequate 不低于 vanilla 驻军容量(PartySizeLimit)的此倍数。
    /// 公式基线对普通城镇偏低,用 vanilla 自身的容量评估兜底。城堡不参与锚定。</summary>
    public float TownAdequateVanillaAnchorRatio { get; set; } = 0.5f;

    // ── 合并 solver(方案2 parallel-run)──
    /// <summary>双层 MCMF 合并的 parallel-run 模式。默认 LegacyOnly(= 合并前行为)。
    /// tuning 期停在 ShadowMerged 比对差异。详见 audits/mcmf-merge-design.md §7。</summary>
    public MergedSolverMode MergedSolverMode { get; set; } = MergedSolverMode.LegacyOnly;
}
