using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Templates;

/// <summary>
/// 驻军训练模板。每个模板封装一份完整的 <see cref="TownGarrisonRule"/>，
/// 供玩家通过 UI 或脚本一键应用到指定 Town。
/// MVP 3 内置三个模板：FrontierDefense / TradeHub / EliteNobleGarrison。
/// </summary>
public sealed class TrainingTemplate
{
    /// <summary>唯一标识，用作字典 / 序列化键。例如 "frontier-defense"。</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>给玩家看的名字。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>简短描述。</summary>
    public string Description { get; set; } = "";

    /// <summary>模板内置的规则。注意：应用时调用方应深拷贝一份，避免共享引用污染。</summary>
    public TownGarrisonRule Rule { get; set; } = TownGarrisonRule.CreateDefault();

    /// <summary>边疆型：高目标 + 高战时倍率 + 偏重防御兵种。</summary>
    public static TrainingTemplate FrontierDefense()
    {
        var rule = TownGarrisonRule.CreateDefault();
        rule.TargetTotalCount = 120;
        rule.CavalryRatio = 0.10f;
        rule.InfantryRatio = 0.55f;
        rule.ArcherRatio = 0.25f;
        rule.CrossbowRatio = 0.05f;
        rule.ThrowerRatio = 0.05f;
        rule.MinTier = 3;
        rule.MinimumDefenders = 60;
        rule.WartimeMultiplier = 2.0f;
        return new TrainingTemplate
        {
            TemplateId = "frontier-defense",
            DisplayName = "Frontier Defense",
            Description = "High target count, high wartime multiplier, defensive-heavy composition",
            Rule = rule
        };
    }

    /// <summary>贸易枢纽：低驻军 + 低战时倍率 + 平衡兵种 + 低预算。</summary>
    public static TrainingTemplate TradeHub()
    {
        var rule = TownGarrisonRule.CreateDefault();
        rule.TargetTotalCount = 50;
        rule.CavalryRatio = 0.20f;
        rule.InfantryRatio = 0.50f;
        rule.ArcherRatio = 0.20f;
        rule.CrossbowRatio = 0.05f;
        rule.ThrowerRatio = 0.05f;
        rule.MinTier = 2;
        rule.MaxTier = 4;
        rule.MinimumDefenders = 25;
        rule.WartimeMultiplier = 1.2f;
        rule.BudgetLimit = 3000;
        return new TrainingTemplate
        {
            TemplateId = "trade-hub",
            DisplayName = "Trade Hub",
            Description = "Lower garrison, lighter recruitment to preserve economy",
            Rule = rule
        };
    }

    /// <summary>精英驻军：高 Tier + 允许贵族兵 + 高预算。</summary>
    public static TrainingTemplate EliteNobleGarrison()
    {
        var rule = TownGarrisonRule.CreateDefault();
        rule.TargetTotalCount = 80;
        rule.CavalryRatio = 0.30f;
        rule.InfantryRatio = 0.40f;
        rule.ArcherRatio = 0.25f;
        rule.CrossbowRatio = 0.05f;
        rule.ThrowerRatio = 0f;
        rule.MinTier = 4;
        rule.MaxTier = 6;
        rule.AllowNobleTroops = true;
        rule.MinimumDefenders = 50;
        rule.BudgetLimit = 10000;
        return new TrainingTemplate
        {
            TemplateId = "elite-noble",
            DisplayName = "Elite Noble Garrison",
            Description = "High tier troops including noble cavalry, expensive",
            Rule = rule
        };
    }
}

/// <summary>
/// 模板枚举与应用入口。所有方法都做异常隔离，错误仅 Logger 落盘，不向调用方抛出。
/// </summary>
public static class TemplateManager
{
    /// <summary>
    /// 返回内置模板（MVP 3 暂不支持用户自定义模板，未来 GlobalConfig 可扩展 CustomTemplates 字段后由此合并）。
    /// </summary>
    public static IReadOnlyList<TrainingTemplate> GetAllTemplates()
    {
        try
        {
            return new List<TrainingTemplate>
            {
                TrainingTemplate.FrontierDefense(),
                TrainingTemplate.TradeHub(),
                TrainingTemplate.EliteNobleGarrison()
            };
        }
        catch (Exception ex)
        {
            Logger.Error("TemplateManager.GetAllTemplates failed; returning empty list", ex);
            return Array.Empty<TrainingTemplate>();
        }
    }

    /// <summary>按 TemplateId 线性查找。找不到 / 入参为空时返回 null。</summary>
    public static TrainingTemplate? FindById(string templateId)
    {
        try
        {
            if (string.IsNullOrEmpty(templateId)) return null;
            foreach (var t in GetAllTemplates())
            {
                if (string.Equals(t.TemplateId, templateId, StringComparison.Ordinal))
                {
                    return t;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"TemplateManager.FindById('{templateId}') failed", ex);
            return null;
        }
    }

    /// <summary>
    /// 把模板规则的深拷贝写入 <see cref="GlobalConfig.PerSettlementOverrides"/>[town.Settlement.StringId]
    /// 并触发 <see cref="ConfigurationManager.Save"/>。
    /// </summary>
    public static bool ApplyToTown(string templateId, Town town)
    {
        try
        {
            if (town?.Settlement?.StringId is not { } settlementId || string.IsNullOrEmpty(settlementId))
            {
                Logger.Warn("TemplateManager.ApplyToTown: town or settlement id is null");
                return false;
            }

            var template = FindById(templateId);
            if (template is null)
            {
                Logger.Warn($"TemplateManager.ApplyToTown: template '{templateId}' not found");
                return false;
            }

            var ruleCopy = DeepCopy(template.Rule);

            var overrides = ConfigurationManager.Current.PerSettlementOverrides;
            if (overrides is null)
            {
                Logger.Error("TemplateManager.ApplyToTown: PerSettlementOverrides dictionary is null");
                return false;
            }

            overrides[settlementId] = ruleCopy;
            ConfigurationManager.Save();
            Logger.Info($"Applied template '{templateId}' to town '{settlementId}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"TemplateManager.ApplyToTown('{templateId}') failed", ex);
            return false;
        }
    }

    /// <summary>移除单 Town 覆盖（回退到 GlobalDefaults）。</summary>
    public static bool RemoveOverride(Town town)
    {
        try
        {
            if (town?.Settlement?.StringId is not { } settlementId || string.IsNullOrEmpty(settlementId))
            {
                Logger.Warn("TemplateManager.RemoveOverride: town or settlement id is null");
                return false;
            }

            var overrides = ConfigurationManager.Current.PerSettlementOverrides;
            if (overrides is null)
            {
                Logger.Error("TemplateManager.RemoveOverride: PerSettlementOverrides dictionary is null");
                return false;
            }

            bool removed = overrides.Remove(settlementId);
            if (removed)
            {
                ConfigurationManager.Save();
                Logger.Info($"Removed override for town '{settlementId}'");
            }
            else
            {
                Logger.Info($"TemplateManager.RemoveOverride: no override found for '{settlementId}'");
            }
            return removed;
        }
        catch (Exception ex)
        {
            Logger.Error("TemplateManager.RemoveOverride failed", ex);
            return false;
        }
    }

    // ------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------

    /// <summary>
    /// 手动深拷贝 <see cref="TownGarrisonRule"/> 的全部 23 个字段。
    /// 字符串字段是不可变引用类型，List&lt;string&gt; 用 new List(src) 拷贝（条目本身共享，仍安全）。
    /// </summary>
    private static TownGarrisonRule DeepCopy(TownGarrisonRule src)
    {
        return new TownGarrisonRule
        {
            TargetTotalCount = src.TargetTotalCount,
            UseGenericMatching = src.UseGenericMatching,
            ExactTroopTemplate = new Dictionary<string, int>(src.ExactTroopTemplate ?? new Dictionary<string, int>()),
            CavalryRatio = src.CavalryRatio,
            InfantryRatio = src.InfantryRatio,
            ArcherRatio = src.ArcherRatio,
            CrossbowRatio = src.CrossbowRatio,
            ThrowerRatio = src.ThrowerRatio,
            Tier1Ratio = src.Tier1Ratio,
            Tier2Ratio = src.Tier2Ratio,
            Tier3Ratio = src.Tier3Ratio,
            Tier4Ratio = src.Tier4Ratio,
            Tier5Ratio = src.Tier5Ratio,
            Tier6Ratio = src.Tier6Ratio,
            MinTier = src.MinTier,
            MaxTier = src.MaxTier,
            RestrictToFactionCultures = src.RestrictToFactionCultures,
            AllowedCultureIds = new List<string>(src.AllowedCultureIds ?? new List<string>()),
            PriorityTroopIds = new List<string>(src.PriorityTroopIds ?? new List<string>()),
            BannedTroopIds = new List<string>(src.BannedTroopIds ?? new List<string>()),
            AllowLowTierFiller = src.AllowLowTierFiller,
            AllowNobleTroops = src.AllowNobleTroops,
            AllowPrisonerConversion = src.AllowPrisonerConversion,
            AllowAutoUpgrade = src.AllowAutoUpgrade,
            PreserveExisting = src.PreserveExisting,
            AutoDisbandExcess = src.AutoDisbandExcess,
            MinimumDefenders = src.MinimumDefenders,
            WartimeMultiplier = src.WartimeMultiplier,
            PeacetimeMultiplier = src.PeacetimeMultiplier,
            BudgetLimit = src.BudgetLimit,
            FoodSafetyThreshold = src.FoodSafetyThreshold,
            DailyTroopXpBonus = src.DailyTroopXpBonus
        };
    }
}
