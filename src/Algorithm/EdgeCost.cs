using SovereignTowns.Configuration;

namespace SovereignTowns.Algorithm;

/// <summary>
/// MCMF 边权 4 通道结构。把"金币 / 时间 / 路径风险 / 战略价值"四类语义独立的
/// cost 项拆开，由 <see cref="EdgeCostCompose.ToFlowCost"/> 用 Config 权重线性合成成
/// SSP 需要的单一 long。
///
/// 设计目标：让 cost 的物理含义可解释（玩家/作者调权重时能想清楚），且增加新 cost
/// 项时不再需要校准"K 守恒"那一坨魔法常量。
/// </summary>
public readonly struct EdgeCost
{
    /// <summary>金币消耗（wage / upgrade gold / routing fuel / recruit cost / starvation 等价金币）。</summary>
    public readonly int Gold;

    /// <summary>占用 tick 数。乘以 <see cref="FiscalAutonomyConfig.TickHoldingValueK"/> 得到时间机会成本。
    /// holding 边 = 1，transfer 边 = d，disband / patrol 边 = T-τ，upgrade 边 = xpTicks。</summary>
    public readonly int TimeUnits;

    /// <summary>路径风险打分（HostilePartyScanner × DispatchRiskCostScale）。</summary>
    public readonly int PathRisk;

    /// <summary>战略价值（正向值）。合成时取负 → 战略价值越高，cost 越低。</summary>
    public readonly int Strategic;

    public EdgeCost(int gold = 0, int timeUnits = 0, int pathRisk = 0, int strategic = 0)
    {
        Gold = gold;
        TimeUnits = timeUnits;
        PathRisk = pathRisk;
        Strategic = strategic;
    }

    /// <summary>全 0 边权（用于 superSource → 初始驻军 / disbandGate → superSink 正常段 等纯结构边）。</summary>
    public static readonly EdgeCost Zero = new EdgeCost();
}

/// <summary>
/// EdgeCost → long flow cost 的合成器。求解器调用唯一入口。
///
/// 合成公式：cost = α·Gold + K·TimeUnits + γ·PathRisk − δ·Strategic
/// 其中 α/γ/δ = Config 权重，K = TickHoldingValueK（替代旧 const K = 20_000_000）。
///
/// 默认 Config（α=γ=δ=1, K=20_000_000）下与旧 cost 公式逐字节相等 —— 这是 PR-1
/// 行为不变性的代码层依据，见 SelfTest 用例。
/// </summary>
public static class EdgeCostCompose
{
    /// <summary>把 4 通道 EdgeCost 合成成 SSP 使用的 long cost。SSP 是单标量优化算法，必须线性合成。</summary>
    public static long ToFlowCost(in EdgeCost ec, FiscalAutonomyConfig cfg)
    {
        return (long)cfg.CostWeightGold      * ec.Gold
             + (long)cfg.TickHoldingValueK   * ec.TimeUnits
             + (long)cfg.CostWeightRisk      * ec.PathRisk
             - (long)cfg.CostWeightStrategic * ec.Strategic;
    }

    /// <summary>
    /// SSP graph public edges must be non-negative because MinCostFlow uses Dijkstra with
    /// Johnson potentials. Strategic rewards are still allowed to drive a cost down to 0,
    /// but not below it.
    /// </summary>
    public static long ToPublicFlowCost(in EdgeCost ec, FiscalAutonomyConfig cfg, out long rawCost)
    {
        rawCost = ToFlowCost(ec, cfg);
        return rawCost < 0 ? 0L : rawCost;
    }

    /// <summary>
    /// 启动期 self-test。验证：
    /// (1) Zero 边权在默认 Config 下 cost = 0
    /// (2) holding 边样本（K + wage − value）在默认 Config 下与旧公式相等
    /// (3) transfer 边样本（d·K + overhead + risk）在默认 Config 下与旧公式相等
    /// (4) disband 边样本 ((T-τ)·K) 在默认 Config 下与旧公式相等
    /// (5) 调高某通道权重时，对应分量按比例放大
    /// 失败必须 Logger.Error，由 SovereignTownsSubModule.OnGameStart 调用。
    /// </summary>
    public static bool SelfTest(out string message)
    {
        var cfg = new FiscalAutonomyConfig();  // 默认值
        const int LegacyK = 20_000_000;

        // (1) Zero
        long zero = ToFlowCost(EdgeCost.Zero, cfg);
        if (zero != 0)
        {
            message = $"EdgeCost.Zero expected cost=0, got {zero}";
            return false;
        }

        // (2) holding sample: tier=3 → wage=5, suppose value*power = 800
        //     legacy: K + 5 − 800 = 19_999_205
        var hold = new EdgeCost(gold: 5, timeUnits: 1, strategic: 800);
        long holdCost = ToFlowCost(hold, cfg);
        long holdLegacy = LegacyK + 5 - 800;
        if (holdCost != holdLegacy)
        {
            message = $"holding sample expected {holdLegacy}, got {holdCost}";
            return false;
        }

        // (3) transfer sample: d=3, overhead=100, risk=50
        //     legacy: 3·K + 100 + 50 = 60_000_150
        var tx = new EdgeCost(gold: 100, timeUnits: 3, pathRisk: 50);
        long txCost = ToFlowCost(tx, cfg);
        long txLegacy = 3 * LegacyK + 100 + 50;
        if (txCost != txLegacy)
        {
            message = $"transfer sample expected {txLegacy}, got {txCost}";
            return false;
        }

        // (4) disband sample: τ=0, T=16 → (T-τ)·K = 16·K
        var dis = new EdgeCost(timeUnits: 16);
        long disCost = ToFlowCost(dis, cfg);
        long disLegacy = 16 * LegacyK;
        if (disCost != disLegacy)
        {
            message = $"disband sample expected {disLegacy}, got {disCost}";
            return false;
        }

        // (5) weight scaling: 把 CostWeightStrategic 设为 3，strategic 分量应放大 3 倍
        var cfgScaled = new FiscalAutonomyConfig { CostWeightStrategic = 3 };
        var strat = new EdgeCost(strategic: 100);
        long stratCost = ToFlowCost(strat, cfgScaled);
        if (stratCost != -300)
        {
            message = $"strategic weight scaling expected -300, got {stratCost}";
            return false;
        }

        // (6) public graph edge safety: raw strategic rewards may go negative, but the
        // public SSP cost passed to MinCostFlow is clamped at zero.
        var cfgReward = new FiscalAutonomyConfig { TickHoldingValueK = 100, CostWeightStrategic = 1 };
        var reward = new EdgeCost(timeUnits: 1, strategic: 150);
        long publicCost = ToPublicFlowCost(reward, cfgReward, out long rawRewardCost);
        if (rawRewardCost != -50 || publicCost != 0)
        {
            message = $"public cost clamp expected raw=-50 public=0, got raw={rawRewardCost} public={publicCost}";
            return false;
        }

        message = "EdgeCostCompose self-test passed (6 cases)";
        return true;
    }
}
