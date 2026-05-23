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

/// <summary>
/// 驻军 tier / 预算口径的共享 helper —— 由 <see cref="UnifiedGarrisonSolver"/> 复用:
/// 每城工资预算、满级单兵工资、hardCap / adequate tier 头数、clan 交战判定。
/// </summary>
public static class GarrisonAllocationSolver
{
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
            if (IsClanAtWar(clan) && clan.Gold > 0)
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

}
