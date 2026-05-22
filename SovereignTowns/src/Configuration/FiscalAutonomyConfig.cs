namespace SovereignTowns.Configuration;

/// <summary>
/// 方案2(双层 MCMF 合并)parallel-run 三态。详见 audits/mcmf-merge-handoff.md §4。
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

    // ── 后勤评估节奏 ──
    /// <summary>首府后勤评估间隔(小时)。默认 6h;读取时 clamp [1,24]。
    /// 改变「一个 tick = 多久」—— P3 时域 T、遣散速率均以此为单位。</summary>
    public int CapitalLogisticsTickHours { get; set; } = 6;

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
    /// tuning 期停在 ShadowMerged 比对差异。详见 audits/mcmf-merge-handoff.md §4。</summary>
    public MergedSolverMode MergedSolverMode { get; set; } = MergedSolverMode.LegacyOnly;

    /// <summary>
    /// 合并 solver 专用 value 基常数 —— 与 Pass A 的 <see cref="ValueFloorBase"/> /
    /// <see cref="ValueCoreBase"/> / <see cref="SurplusEdgeCost"/> **独立**:调合并 solver 的
    /// 标度不会扰动 legacy Pass A 的分配。合并图费用 = `K + routing − value`,value 必须与
    /// routing(村→首府距离 + overhead,约数百~千)同量级,否则 core/surplus 招募恒亏本
    /// → solver 欠驻军。tuning 项,详见 audits/mcmf-merge-handoff.md §5。
    /// 首轮试值:floor value ≈ 数千、core ≈ 数百~低千、surplus ≈ 0。
    /// </summary>
    public int MergedValueFloorBase  { get; set; } = 3000;
    public int MergedValueCoreBase   { get; set; } = 800;
    public int MergedSurplusEdgeCost { get; set; } = 1;

    /// <summary>合并 solver 两段 bypass(M4):每城每【天】经「正常段」遣散的头数上限。
    /// solver 按 <see cref="CapitalLogisticsTickHours"/> 折算成每 tick 上限。
    /// 正常段费用 0(总费 K),溢出段费用 <see cref="MergedBypassOverflowPenalty"/>。
    /// 设 0 = 关闭正常遣散段,只保留溢出段(仅遣散物理塞不下 hardCap 的兵)。
    /// 详见 audits/mcmf-merge-handoff.md §3.4。</summary>
    public int MergedDisbandPerDayCap { get; set; } = 20;

    /// <summary>合并 solver 两段 bypass(M4):溢出段附加费用。须 > |surplus tier value|
    /// (= <see cref="MergedSurplusEdgeCost"/>)以保证正常段耗尽后 solver 优先"surplus 留驻"
    /// 而非"溢出遣散" —— 从而把超额遣散限制在每 tick <see cref="MergedDisbandPerDayCap"/>
    /// 折算出的上限。详见 audits/mcmf-merge-handoff.md §3.4。</summary>
    public int MergedBypassOverflowPenalty { get; set; } = 1000;

    // ── 派发风险(Part D)──
    /// <summary>派发风险否决总开关(D1)。true = 路途有敌军时本 tick 不派征兵 / 调拨队。
    /// 直接影响 live 行为,可一键回退。</summary>
    public bool DispatchRiskEnabled { get; set; } = true;

    /// <summary>HostilePartyScanner 扫描半径(地图单位)。初值 30,须 in-game 调。</summary>
    public float DispatchRiskScanRadius { get; set; } = 30f;

    /// <summary>D1 否决阈值:路线风险分(沿途最大敌对健康兵力)≥ 此值 → 本 tick 不派。
    /// 初值 60,须 in-game 调。</summary>
    public float DispatchRiskVetoThreshold { get; set; } = 60f;

    /// <summary>D2 路线风险→成本 的标度乘子:成本加项 = 风险分 × 此值。
    /// 须与 routing(数百~千)可比。初值 10,须 in-game 调。</summary>
    public int DispatchRiskCostScale { get; set; } = 10;
}
