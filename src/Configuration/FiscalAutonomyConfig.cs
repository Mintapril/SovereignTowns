namespace SovereignTowns.Configuration;

/// <summary>P3 时间展开 solver 各 tick 的威胁来源。详见 audits/2026-05-22-p3-lookahead-design.md §7。
/// Newtonsoft 序列化为整数。</summary>
public enum ForecastMode
{
    Flat,    // 所有 tick 用当前威胁,不前瞻
    Threat,  // tick>0 用敌军 ETA 投影
}

/// <summary>财政自治 + 中央驻军调度器配置。详见两份设计文档。</summary>
public sealed class FiscalAutonomyConfig
{
    // ── 金库 / 预算 ──
    // 注:玩家氏族金库改为完全手动 —— 收入单向流入金库,工资单向流出金库;
    // 不再有溢出回流家族金币或 Hero.Gold 兜底欠饷的自动通道。玩家需在控制面板/WebUI
    // 主动存款/取款来调度个人金币与金库间的资金流。详见 Economy/TreasuryUserActions。
    public float GarrisonWageBudgetRatio { get; set; } = 0.55f;

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

    // ── 驻军 tier 口径 ──
    public int   AdequateBase            { get; set; } = 60;
    public int   AdequateProsperityDivisor { get; set; } = 80;
    public int   AdequateThreatWeight    { get; set; } = 8;
    public int   CoreTierCount           { get; set; } = 5;
    public int   MaxGarrisonHardCap      { get; set; } = 400;

    /// <summary>城镇 adequate 下限锚定:adequate 不低于 vanilla 驻军容量(PartySizeLimit)的此倍数。
    /// 公式基线对普通城镇偏低,用 vanilla 自身的容量评估兜底。城堡不参与锚定。</summary>
    public float TownAdequateVanillaAnchorRatio { get; set; } = 0.5f;

    // ── 时间展开调度器 value 函数 ──
    /// <summary>
    /// 调度器 value 基常数。合并图费用 = `K + routing − value`,value 必须与
    /// routing(村→首府距离 + overhead,约数百~千)同量级,否则 core/surplus 招募恒亏本
    /// → solver 欠驻军。tuning 项。
    /// 首轮试值:floor value ≈ 数千、core ≈ 数百~低千、surplus ≈ 0。
    /// </summary>
    public int ValueFloorBase  { get; set; } = 3000;
    public int ValueCoreBase   { get; set; } = 800;
    public int SurplusEdgeCost { get; set; } = 1;

    /// <summary>
    /// 巡逻并入调度器:patrol sink 边的回报值 —— patrol 边真实费用 = −此值。
    /// 本质是「用盈余兵巡逻 vs 直接遣散」的强度旋钮:&gt; 0 即让 patrol 胜过 disband(费用 0),
    /// MCMF 把首府盈余兵优先送去巡逻而非销毁;值越大越优先填满 patrol 容量。
    /// patrol **不会**抽走 core 守军 —— patrolHeadroom 是硬容量,逐 slot 经济上盈余兵恒比 core
    /// 兵先占该 slot(盈余省 disband 成本、core 省 hold 成本,前者更大),与本值大小无关。
    /// 首轮取保守值 200,须 in-game 调;clamp [0, K−1]。
    /// </summary>
    public int PatrolValue { get; set; } = 200;

    /// <summary>调度器两段 bypass:每城每【天】经「正常段」遣散的头数上限。
    /// solver 按 <see cref="CapitalLogisticsTickHours"/> 折算成每 tick 上限。
    /// 正常段费用 0(总费 K),溢出段费用 <see cref="BypassOverflowPenalty"/>。
    /// 设 0 = 关闭正常遣散段,只保留溢出段(仅遣散物理塞不下 hardCap 的兵)。</summary>
    public int DisbandPerDayCap { get; set; } = 20;

    /// <summary>调度器两段 bypass:溢出段附加费用。须 > |surplus tier value|
    /// (= <see cref="SurplusEdgeCost"/>)以保证正常段耗尽后 solver 优先"surplus 留驻"
    /// 而非"溢出遣散" —— 从而把超额遣散限制在每 tick <see cref="DisbandPerDayCap"/>
    /// 折算出的上限。</summary>
    public int BypassOverflowPenalty { get; set; } = 1000;

    // ── P3 时间展开 solver ──
    /// <summary>P3 时域 T(tick 数)。须 ≥ 典型征兵队 ETA,否则只能原地招募
    /// (见 audits/2026-05-22-p3-lookahead-design.md §6.5)。读取时 clamp [1,64]。</summary>
    public int HorizonTicks { get; set; } = 16;

    /// <summary>SSP 求解分帧粒度:每完成此数量的增广就经 AsyncSimulator yield 一帧。读取时 clamp ≥ 1。
    /// 越小每帧 CPU 越低(卡顿越轻),整次求解跨越的帧数越多、墙钟越长。默认 8 —— 实测典型求解
    /// ~600 次增广、每次 ≈0.4ms,8/帧 ≈ 3ms/帧(远低于 64/帧的 ≈28ms)。仅影响分帧观感,不改求解结果。</summary>
    public int SspYieldEvery { get; set; } = 8;

    /// <summary>P3 各 tick 威胁来源。默认 Flat(不前瞻)。</summary>
    public ForecastMode ForecastMode { get; set; } = ForecastMode.Flat;

    // ── 时间展开调度器 value 函数曲线 ──
    /// <summary>威胁等级 → value 乘子。Safe/Low 锚定和平期,Med/High/Crit 决定战时涨兵幅度。</summary>
    public float ThreatWeightSafe     { get; set; } = 0.5f;
    public float ThreatWeightLow      { get; set; } = 1.0f;
    public float ThreatWeightMedium   { get; set; } = 1.5f;
    public float ThreatWeightHigh     { get; set; } = 2.0f;
    public float ThreatWeightCritical { get; set; } = 3.0f;

    /// <summary>core 段 diminishing 斜降的总幅度:最低子层 value 乘子 = 1 − 此值。</summary>
    public float CoreDimRange { get; set; } = 0.8f;
    /// <summary>core 子层取样的中点偏移:第 k 子层用 (k + 此值) / K 作归一化位置。</summary>
    public float CoreDimMidpoint { get; set; } = 0.5f;

    /// <summary>strategic 乘子的繁荣度归一化除数:Prosperity / 此值 再 clamp 到 [0.5, 1.5]。</summary>
    public float ProsperityNormalizer { get; set; } = 4000f;
    /// <summary>首府在 strategic 乘子中的加成系数(非首府为 1.0)。</summary>
    public float CapitalStrategicBonus { get; set; } = 1.3f;

    /// <summary>ETA 估算用参考队伍速度(地图单位/天)。近似(tuning),与 vanilla 单队速度无关。</summary>
    public float ReferenceSpeedPerDay { get; set; } = 5.0f;

    /// <summary>威胁预测器扫描半径(地图单位)—— 探测数天行程外正逼近的敌军,
    /// 须远大于 DispatchRiskScanRadius。初值 150,须 in-game 调。</summary>
    public float ThreatForecastScanRadius { get; set; } = 150f;

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
