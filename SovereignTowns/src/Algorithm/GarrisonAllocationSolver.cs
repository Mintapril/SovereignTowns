using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Algorithm;

/// <summary>Pass A 分配结果:每定居点目标头数 + 价值分解(日志/评估用)。</summary>
public sealed class GarrisonAllocationResult
{
    public Dictionary<Settlement, int> Target { get; } = new();
    public Dictionary<Settlement, string> Breakdown { get; } = new();

    /// <summary>本次分配所用的氏族驻军工资预算(金币/日)。看板 / feed 决策叙述用。</summary>
    public long Budget { get; set; }

    /// <summary>预算可供养的满级士兵头数(Budget / 满级单兵工资)。</summary>
    public int BudgetTroopCap { get; set; }
}

/// <summary>
/// 中央驻军调度器 Pass A:分配 MCMF。把氏族工资预算按"防御价值"分配到各城/堡。
/// 单商品(头数)、凸费用层、复用 MinCostFlow。设计文档 central-garrison-dispatcher §3-§4。
///
/// 注意 — 费用偏移变换(advisor 复核):MinCostFlow.AddEdge 拒绝负费用
/// (cost &lt; 0 抛 ArgumentOutOfRangeException)。设计文档 §3.1 用负费用表达价值层,
/// 这里改用 <see cref="CostOffset"/> 偏移成非负:
///   价值层(floor/core)  cost = CostOffset - round(value)   —— 价值越高费用越低,MCMF 越偏好
///   budgetNode→unspent  cost = CostOffset                  —— 比任何正价值层贵,比 surplus 便宜
///   surplus 层          cost = CostOffset + SurplusEdgeCost —— 严格劣于"留着不花"(设计 §3.1 关键修正)
/// 决策与设计文档语义完全一致;只是 TotalCost 数值不再有意义(EdgeFlows 解码不受影响)。
/// </summary>
public static class GarrisonAllocationSolver
{
    /// <summary>
    /// 费用偏移常量。须 ≥ 任何价值层可能取到的最大 value,使 CostOffset - round(value) 恒 ≥ 0。
    /// 极端但合法配置下的上界:ValueFloorBase(校验上限 1_000_000) × threat_max(8)
    /// × strategic_max(1.3×1.5≈1.95) ≈ 15.6M。取 20_000_000 安全高于此上界 —— BuildCost
    /// 内的 clamp 仅作防御性兜底,正常路径不触发(若触发会把不同 floor 压成同一 cost,丢失排序)。
    /// 每条 MCMF 边费用 ≤ ~20M、累加几条边仍在 int 范围内;aggregate TotalCost 本就是无意义大数,
    /// 不影响流的正确性(EdgeFlows 解码与费用绝对值无关)。
    /// </summary>
    private const int CostOffset = 20_000_000;

    // —— 价值函数常量(参与 CostOffset 安全不变式;见 §4) ——

    /// <summary>core 段 diminishing 斜降的总幅度。最低子层价值 = 1.0 − CoreDimRange = 0.2。</summary>
    private const float CoreDimRange = 0.8f;

    /// <summary>core 子层取样的中点偏移:第 k 子层用 (k + CoreDimMidpoint) / K 作为归一化位置。</summary>
    private const float CoreDimMidpoint = 0.5f;

    /// <summary>strategic(S) 的繁荣度归一化除数:Prosperity / ProsperityNormalizer 再 clamp 到 [0.5,1.5]。</summary>
    private const float ProsperityNormalizer = 4000f;

    /// <summary>
    /// 首府在 strategic(S) 中的加成系数(非首府为 1.0)。
    /// 注意:此值参与 CostOffset 上界推导 —— strategic_max = CapitalStrategicBonus × 1.5(繁荣度 clamp 上界),
    /// 与 ValueFloorBase_max × threat_max 相乘得 ~15.6M(见 CostOffset 文档)。
    /// </summary>
    private const float CapitalStrategicBonus = 1.3f;

    /// <summary>threat(S) 风险等级映射值。参与 CostOffset 上界推导的 threat_max == ThreatWeightCritical。</summary>
    private const float ThreatWeightSafe = 0.5f;
    private const float ThreatWeightLow = 1.0f;
    private const float ThreatWeightMedium = 2.0f;
    private const float ThreatWeightHigh = 4.0f;
    private const float ThreatWeightCritical = 8.0f;

    public static GarrisonAllocationResult Solve(CapitalManager manager)
    {
        var result = new GarrisonAllocationResult();
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return result;
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();

            var towns = clan.Fiefs.Where(t => t?.Settlement != null && t.Settlement.IsActive).ToList();
            if (towns.Count == 0) return result;

            int wagePerTroop = Math.Max(1, WagePerTroopAtMaxTier(manager, towns));
            long clanWageBudget = ClanWageBudget(manager, towns, cfg, wagePerTroop);
            int budgetCap = (int)Math.Min(int.MaxValue, clanWageBudget / wagePerTroop);
            // 预算口径在 budgetCap<=0 提前返回前就发布,看板 / feed 能展示"预算不足"态。
            result.Budget = clanWageBudget;
            result.BudgetTroopCap = budgetCap;
            if (budgetCap <= 0) return result;

            var graph = new MinCostFlow();
            int next = 1;
            int superSource = next++, budgetNode = next++, unspentNode = next++, superSink = next++;
            graph.AddEdge(superSource, budgetNode, budgetCap, 0);
            // 未花掉的预算出口:费用 = CostOffset。任何正价值层(cost < CostOffset)都比它便宜 → 优先填;
            // surplus 层(cost > CostOffset)比它贵 → MCMF 严格偏好"留着不花"而非过度驻军。
            graph.AddEdge(budgetNode, unspentNode, budgetCap, CostOffset);
            graph.AddEdge(unspentNode, superSink, budgetCap, 0);

            var tierOwner = new Dictionary<int, Settlement>();
            foreach (var t in towns)
            {
                var s = t.Settlement;
                int floor = Math.Max(0, cfg.MinGarrisonFloor);
                int hardCap = HardCapFor(t, cfg);
                int adequate = AdequateFor(t, cfg, floor, hardCap);
                float threat = ThreatWeight(s);
                float strat = StrategicWeight(s, manager);

                if (floor > 0)
                {
                    int n = next++; tierOwner[n] = s;
                    int cost = BuildCost(cfg.ValueFloorBase * threat * strat);
                    graph.AddEdge(budgetNode, n, floor, cost);
                    graph.AddEdge(n, superSink, floor, 0);
                }
                int coreSpan = Math.Max(0, adequate - floor);
                if (coreSpan > 0)
                {
                    int K = Math.Max(1, cfg.CoreTierCount);
                    for (int k = 0; k < K; k++)
                    {
                        int cap = (coreSpan * (k + 1) / K) - (coreSpan * k / K);
                        if (cap <= 0) continue;
                        // diminishing: 在 core 段从 1.0 线性降到 1.0−CoreDimRange=0.2(取每子层中点)。
                        float dim = 1.0f - CoreDimRange * ((k + CoreDimMidpoint) / K);
                        int cost = BuildCost(cfg.ValueCoreBase * dim * threat * strat);
                        int n = next++; tierOwner[n] = s;
                        graph.AddEdge(budgetNode, n, cap, cost);
                        graph.AddEdge(n, superSink, cap, 0);
                    }
                }
                int surplusSpan = Math.Max(0, hardCap - adequate);
                if (surplusSpan > 0)
                {
                    int n = next++; tierOwner[n] = s;
                    // surplus 层费用严格 > CostOffset(留着不花),MCMF 仅在所有价值层填满后才会用。
                    int surplusCost = CostOffset + Math.Max(1, cfg.SurplusEdgeCost);
                    graph.AddEdge(budgetNode, n, surplusSpan, surplusCost);
                    graph.AddEdge(n, superSink, surplusSpan, 0);
                }
                result.Breakdown[s] = $"threat={threat:F1} strat={strat:F2} floor={floor} adequate={adequate} hardCap={hardCap}";
            }

            var flow = graph.Solve(superSource, superSink);
            foreach (var t in towns) result.Target[t.Settlement] = 0;
            foreach (var kv in flow.EdgeFlows)
            {
                if (kv.Value <= 0) continue;
                if (tierOwner.TryGetValue(kv.Key.To, out var s))
                    result.Target[s] = result.Target.TryGetValue(s, out var c) ? c + kv.Value : kv.Value;
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonAllocationSolver.Solve failed", ex);
            return result;
        }
    }

    /// <summary>把 float 价值映射成非负 MCMF 费用:价值越高费用越低。clamp 到 [0, CostOffset]。</summary>
    private static int BuildCost(float value)
    {
        int v = (int)Math.Round(value);
        if (v < 0) v = 0;
        if (v > CostOffset) v = CostOffset;
        return CostOffset - v;
    }

    // —— helper 实现(契约见任务说明 + 设计文档 §4-§5) ——

    /// <summary>
    /// 氏族工资预算:GarrisonWageBudgetRatio × Σ(每城税+关税)。村庄收入排除(易被劫,保守)。
    /// 若 clan 处于战争 且 金库余额 &gt; 0 → 取 max(常规预算, Σ adequate×wagePerTroop) ——
    /// 战时始终保证能养满每城"充足"(adequate)驻军,与 Solve 主循环的 floor/hardCap/adequate
    /// 口径完全一致;不再受 manual-mode 的 TargetTotalCount/TargetPower 旋钮污染。
    /// 任何失败 → 返回 0(分配将退化为不养兵,安全)。
    /// </summary>
    // internal: UnifiedGarrisonSolver(方案2 合并 solver)复用同一预算口径,不重新实现战时底线。
    internal static long ClanWageBudget(CapitalManager manager, List<Town> towns, FiscalAutonomyConfig cfg, int wagePerTroop)
    {
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return 0;

            // 全限定:本项目存在 SovereignTowns.Campaign 子命名空间,裸 Campaign 在此会被解析成它。
            long sustainable = 0;
            var taxModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.SettlementTaxModel;
            var financeModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.ClanFinanceModel;
            foreach (var t in towns)
            {
                if (t?.Settlement == null) continue;
                try
                {
                    if (taxModel != null)
                        sustainable += (long)taxModel.CalculateTownTax(t).ResultNumber;
                    if (financeModel != null)
                        sustainable += (long)financeModel.CalculateTownIncomeFromTariffs(clan, t, false).ResultNumber;
                }
                catch (Exception ex)
                {
                    Logger.Error($"GarrisonAllocationSolver.ClanWageBudget income failed for '{t?.Settlement?.StringId}'", ex);
                }
            }
            sustainable = Math.Max(0, sustainable);

            float ratio = cfg.GarrisonWageBudgetRatio;
            if (ratio < 0f) ratio = 0f;
            long regular = (long)Math.Round(sustainable * ratio);

            // 战时上调:clan 与任意势力交战 且 金库有余额 → 取 max(常规, 全额充足驻军工资)。
            // configFull 用各城 adequate 头数(与 Solve 主循环同一 floor/hardCap/adequate 口径),
            // 确保战时预算恰好够养满价值层 —— 而不是被 manual-mode 旋钮间接放大或卡死。
            if (IsClanAtWar(clan) && manager.Treasury != null && manager.Treasury.Balance > 0)
            {
                long fullGarrisonHeads = 0;
                foreach (var t in towns)
                {
                    if (t?.Settlement == null) continue;
                    int floor = Math.Max(0, cfg.MinGarrisonFloor);
                    int hardCap = HardCapFor(t, cfg);
                    fullGarrisonHeads += AdequateFor(t, cfg, floor, hardCap);
                }
                long configFull = fullGarrisonHeads * (long)Math.Max(1, wagePerTroop);
                return Math.Max(regular, configFull);
            }
            return regular;
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonAllocationSolver.ClanWageBudget failed", ex);
            return 0;
        }
    }

    /// <summary>clan 是否与任意势力交战。FactionHelper.GetStances + IsAtWarWith(DefaultClanFinanceModel 同套路)。</summary>
    // internal: also called by GarrisonXpInjector (war-buffer upgrade gate).
    internal static bool IsClanAtWar(Clan clan)
    {
        try
        {
            var mapFaction = clan?.MapFaction;
            if (mapFaction == null) return false;
            var stances = FactionHelper.GetStances(mapFaction);
            if (stances == null) return false;
            foreach (var stance in stances)
            {
                if (stance == null) continue;
                var other = stance.Faction1 == mapFaction ? stance.Faction2 : stance.Faction1;
                if (other != null && mapFaction.IsAtWarWith(other)) return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonAllocationSolver.IsClanAtWar failed", ex);
            return false;
        }
    }

    /// <summary>
    /// 满级单兵工资 = PartyWageModel.GetCharacterWage(满级 tier 的代表兵种)。
    /// 满级 tier 取自首府 TownGarrisonRule.MaxTier(取不到默认 5)。
    /// 代表兵种用 GarrisonPowerEvaluator.MakeStubTroop 的 tier 查找。任何失败 → 返回 1(保守)。
    /// internal:CapitalLogisticsManager 的 GarrisonAssessment.DailyWageDelta 复用此口径
    /// (避免重复实现)。<paramref name="towns"/> 仅作 GetCapital 失败时的首府兜底来源。
    /// </summary>
    internal static int WagePerTroopAtMaxTier(CapitalManager manager, List<Town> towns)
    {
        try
        {
            int maxTier = 5;
            // 首府优先;无首府(刚失守等)退化为 clan.Fiefs 里第一个 town。
            Town? capitalTown = null;
            try
            {
                capitalTown = manager?.GetCapital();
            }
            catch (Exception ex)
            {
                Logger.Warn($"GarrisonAllocationSolver.WagePerTroopAtMaxTier: GetCapital threw, falling back to first town: {ex.Message}");
                capitalTown = null;
            }
            if (capitalTown == null)
                capitalTown = towns.FirstOrDefault(t => t != null && t.IsTown);
            if (capitalTown != null)
            {
                var rule = ConfigurationManager.GetRuleFor(capitalTown);
                if (rule != null && rule.MaxTier > 0) maxTier = rule.MaxTier;
            }

            // 全限定:见 ClanWageBudget 注释。
            var wageModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PartyWageModel;
            if (wageModel == null) return 1;

            // 代表兵种:优先匹配 tier 的非英雄兵种;MakeStubTroop 找不到时退化为任意非英雄兵。
            var rep = GarrisonPowerEvaluator.MakeStubTroop(maxTier, mounted: false)
                      ?? GarrisonPowerEvaluator.MakeStubTroop(maxTier, mounted: true);
            if (rep == null) return 1;

            int wage = wageModel.GetCharacterWage(rep);
            return Math.Max(1, wage);
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonAllocationSolver.WagePerTroopAtMaxTier failed", ex);
            return 1;
        }
    }

    /// <summary>
    /// adequate(S):clamp(AdequateBase + Prosperity/AdequateProsperityDivisor
    ///   + round(NearbyLandThreatIntensity × AdequateThreatWeight), floor, hardCap)。
    /// 城镇额外下限锚定:adequate 不低于 round(hardCap × TownAdequateVanillaAnchorRatio) ——
    /// hardCap 即 vanilla 驻军 PartySizeLimit,公式基线对普通城镇偏低时用 vanilla 容量兜底。
    /// 城堡不参与锚定(t.IsTown==false 时跳过)。
    /// 任何失败 → 返回 clamp(floor, hardCap) 的下界(floor)。
    /// </summary>
    // internal: UnifiedGarrisonSolver 复用同一 tier 口径(adequate 头数)。
    internal static int AdequateFor(Town t, FiscalAutonomyConfig cfg, int floor, int hardCap)
    {
        try
        {
            var s = t?.Settlement;
            if (s == null) return Math.Max(0, floor);

            float prosperity = 0f;
            try { prosperity = t!.Prosperity; } catch { prosperity = 0f; }

            float threatIntensity = 0f;
            try { threatIntensity = s.NearbyLandThreatIntensity; } catch { threatIntensity = 0f; }

            int prosperityDivisor = Math.Max(1, cfg.AdequateProsperityDivisor);
            float raw = cfg.AdequateBase
                        + prosperity / prosperityDivisor
                        + (float)Math.Round(threatIntensity * cfg.AdequateThreatWeight);

            int adequate = (int)Math.Round(raw);

            // 城镇 adequate 下限锚定到 vanilla 容量的一部分(城堡跳过)。anchor ≤ hardCap(比例 ≤ 1),
            // 故不会与下面的 hardCap 上限冲突;若 anchor < floor 仍由 floor 兜底。
            if (t!.IsTown)
            {
                float anchorRatio = Math.Max(0f, cfg.TownAdequateVanillaAnchorRatio);
                int anchor = (int)Math.Round(hardCap * anchorRatio);
                if (adequate < anchor) adequate = anchor;
            }

            if (adequate < floor) adequate = floor;
            if (adequate > hardCap) adequate = hardCap;
            return adequate;
        }
        catch (Exception ex)
        {
            Logger.Error($"GarrisonAllocationSolver.AdequateFor failed for '{t?.Settlement?.StringId}'", ex);
            return Math.Max(0, floor);
        }
    }

    /// <summary>
    /// hardCap(S):vanilla 驻军 PartySizeLimit。GarrisonParty 是 MobileParty,其 .Party(PartyBase)
    /// 暴露 PartySizeLimit。取不到 → cfg.MaxGarrisonHardCap(默认 400)。
    /// </summary>
    // internal: UnifiedGarrisonSolver 复用同一 tier 口径(hardCap 头数)。
    internal static int HardCapFor(Town t, FiscalAutonomyConfig cfg)
    {
        int fallback = Math.Max(1, cfg.MaxGarrisonHardCap);
        try
        {
            var garrison = t?.GarrisonParty;
            int limit = garrison?.Party?.PartySizeLimit ?? 0;
            return limit > 0 ? limit : fallback;
        }
        catch (Exception ex)
        {
            Logger.Error($"GarrisonAllocationSolver.HardCapFor failed for '{t?.Settlement?.StringId}'", ex);
            return fallback;
        }
    }

    /// <summary>threat(S):RiskAssessmentService 风险等级映射 Safe .5 / Low 1 / Medium 2 / High 4 / Critical 8。</summary>
    private static float ThreatWeight(Settlement s)
    {
        try
        {
            var level = RiskAssessmentService.Assess(s).Level;
            switch (level)
            {
                case RiskLevel.Safe: return ThreatWeightSafe;
                case RiskLevel.Low: return ThreatWeightLow;
                case RiskLevel.Medium: return ThreatWeightMedium;
                case RiskLevel.High: return ThreatWeightHigh;
                case RiskLevel.Critical: return ThreatWeightCritical;
                default: return ThreatWeightLow;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"GarrisonAllocationSolver.ThreatWeight failed for '{s?.StringId}'", ex);
            return ThreatWeightLow;
        }
    }

    /// <summary>
    /// strategic(S):(是该 clan 当前首府 ? CapitalStrategicBonus : 1.0) × clamp(Prosperity/ProsperityNormalizer, 0.5, 1.5)。
    /// 城堡 / 取不到繁荣度 → 繁荣度项落 0.5 下界。任何失败 → 1.0。
    /// </summary>
    private static float StrategicWeight(Settlement s, CapitalManager manager)
    {
        try
        {
            if (s == null) return 1.0f;

            bool isCapital = false;
            try
            {
                var capital = manager?.GetCapitalSettlement();
                isCapital = s.IsTown && capital != null && capital == s;
            }
            catch { isCapital = false; }

            float prosperity = 0f;
            try { if (s.IsTown && s.Town != null) prosperity = s.Town.Prosperity; }
            catch { prosperity = 0f; }

            float prosperityFactor = prosperity / ProsperityNormalizer;
            if (prosperityFactor < 0.5f) prosperityFactor = 0.5f;
            if (prosperityFactor > 1.5f) prosperityFactor = 1.5f;

            return (isCapital ? CapitalStrategicBonus : 1.0f) * prosperityFactor;
        }
        catch (Exception ex)
        {
            Logger.Error($"GarrisonAllocationSolver.StrategicWeight failed for '{s?.StringId}'", ex);
            return 1.0f;
        }
    }
}
