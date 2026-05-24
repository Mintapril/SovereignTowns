using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Algorithm;

/// <summary>
/// 驻军 tier / 预算口径的共享 helper —— 由 <see cref="UnifiedGarrisonSolver"/> 复用:
/// 每城工资预算、满级单兵工资、hardCap / adequate tier 头数、clan 交战判定。
/// </summary>
public static class GarrisonAllocationSolver
{
    // —— helper 实现(契约见任务说明 + 设计文档 §4-§5) ——

    /// <summary>
    /// 氏族工资预算:GarrisonWageBudgetRatio × Σ(每城税+关税)。村庄收入排除(易被劫,保守)。
    /// 任何失败 → 返回 0(分配将退化为不养兵,安全)。
    /// PR-5'(2026-05-24)：删除了战时 IsClanAtWar 上调分支 + AdequateFor floor/hardCap 口径依赖。
    /// </summary>
    // internal: UnifiedGarrisonSolver(方案2 合并 solver)复用同一预算口径。
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
            return (long)Math.Round(sustainable * ratio);
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonAllocationSolver.ClanWageBudget failed", ex);
            return 0;
        }
    }

    /// <summary>
    /// 满级单兵工资 = PartyWageModel.GetCharacterWage(tier 5 的代表兵种)。
    /// PR-5'(2026-05-24)：MaxTier 已从 TownGarrisonRule 删除，固定使用 tier=5。
    /// 代表兵种用 GarrisonPowerEvaluator.MakeStubTroop 的 tier 查找。任何失败 → 返回 1(保守)。
    /// internal:CapitalLogisticsManager 的 GarrisonAssessment.DailyWageDelta 复用此口径。
    /// </summary>
    internal static int WagePerTroopAtMaxTier(CapitalManager manager, List<Town> towns)
    {
        try
        {
            const int maxTier = 5;

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
    /// hardCap(S):vanilla 驻军 PartySizeLimit。GarrisonParty 是 MobileParty,其 .Party(PartyBase)
    /// 暴露 PartySizeLimit。取不到 → 硬编码 400。
    /// PR-5'(2026-05-24)：MaxGarrisonHardCap 字段已从 FiscalAutonomyConfig 删除，fallback 改为硬编码 400。
    /// </summary>
    // internal: UnifiedGarrisonSolver 复用同一 tier 口径(hardCap 头数)。
    internal static int HardCapFor(Town t, FiscalAutonomyConfig cfg)
    {
        const int fallback = 400;
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

    // PR-5'(2026-05-24): AdequateFor 已删除。单段 TierDefs cap = hardCap × TargetFraction。
    // IsClanAtWar 已删除。ClanWageBudget 不再区分战时/和平期。

}
