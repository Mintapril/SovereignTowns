using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.ModuleManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Configuration;

/// <summary>
/// 配置版本迁移器。负责把任意历史版本的 <see cref="GlobalConfig"/> 升级到 <see cref="CurrentVersion"/>。
/// 当前 schema 版本为 v3；保留迁移链骨架以便后续版本演进。
/// 所有公开方法都做异常隔离：失败时回退到 <see cref="GlobalConfig.CreateDefault"/>，不向调用方抛异常。
/// </summary>
/// <remarks>
/// 调用约定：迁移过程不修改原对象，返回新对象（深拷贝 + 升级）。
/// 备份接口与 ConfigurationManager 解耦：用户/调用方可在 Migrate 前主动调用
/// <see cref="BackupCurrentConfigFile"/>，把磁盘上的 global.json 复制到 Configs/Backups/。
/// </remarks>
public static class ConfigMigrator
{
    /// <summary>当前 mod 内置的 config schema 版本号。与 <see cref="ConfigurationManager.CurrentConfigVersion"/> 保持一致。</summary>
    public const int CurrentVersion = ConfigurationManager.CurrentConfigVersion;

    private const string ModuleId = "SovereignTowns";
    private const string ConfigSubDir = "Configs";
    private const string BackupsSubDir = "Backups";
    private const string ConfigFileName = "global.json";

    /// <summary>把任意旧版 config 升级到 <see cref="CurrentVersion"/>。
    /// 若 <paramref name="config"/> 为 null 或版本未知，返回 <see cref="GlobalConfig.CreateDefault"/>。
    /// 若 <c>config.ConfigVersion &gt;= CurrentVersion</c>，原样返回（不深拷贝，调用方语义保持引用透明）。
    /// 否则深拷贝一份并在副本上执行迁移链。失败回退到默认配置。</summary>
    public static GlobalConfig Migrate(GlobalConfig config)
    {
        try
        {
            if (config is null)
            {
                Logger.Warn("ConfigMigrator.Migrate received null config; returning default");
                return GlobalConfig.CreateDefault();
            }

            if (config.ConfigVersion >= CurrentVersion)
            {
                return config;
            }

            // 深拷贝后在副本上执行迁移，避免修改调用方对象。
            var working = DeepClone(config);

            // 迁移链 v0 -> v1 -> v2 -> v3。
            // 占位框架：未来添加新版本时在此追加 case 并 goto case (下一版本)。
            switch (working.ConfigVersion)
            {
                case 0:
                    Logger.Info("ConfigMigrator: v0 -> v1 (placeholder, no-op)");
                    working.ConfigVersion = 1;
                    goto case 1;
                case 1:
                    Logger.Info("ConfigMigrator: v1 -> v2 (generic tier ratios + culture-free matching defaults)");
                    working.ConfigVersion = 2;
                    working.GlobalDefaults ??= TownGarrisonRule.CreateDefault();
                    working.GlobalDefaults.RestrictToFactionCultures = false;
                    if (working.PerSettlementOverrides != null)
                    {
                        foreach (var rule in working.PerSettlementOverrides.Values)
                        {
                            if (rule != null) rule.RestrictToFactionCultures = false;
                        }
                    }
                    goto case 2;
                case 2:
                    Logger.Info("ConfigMigrator: v2 -> v3 (dual generic/exact troop template mode)");
                    working.ConfigVersion = 3;
                    working.GlobalDefaults ??= TownGarrisonRule.CreateDefault();
                    working.GlobalDefaults.UseGenericMatching = true;
                    working.GlobalDefaults.ExactTroopTemplate ??= new Dictionary<string, int>();
                    if (working.PerSettlementOverrides != null)
                    {
                        foreach (var rule in working.PerSettlementOverrides.Values)
                        {
                            if (rule == null) continue;
                            rule.UseGenericMatching = true;
                            rule.ExactTroopTemplate ??= new Dictionary<string, int>();
                        }
                    }
                    goto case 3;
                case 3:
                    break;
                default:
                    Logger.Warn($"ConfigMigrator: 未知 ConfigVersion={working.ConfigVersion}，回退默认");
                    return GlobalConfig.CreateDefault();
            }

            return working;
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigMigrator.Migrate failed", ex);
            return GlobalConfig.CreateDefault();
        }
    }

    /// <summary>检测某 ConfigVersion 是否需要迁移到 <see cref="CurrentVersion"/>。</summary>
    public static bool NeedsMigration(int fromVersion) => fromVersion < CurrentVersion;

    /// <summary>在迁移前把旧文件备份到 <c>Configs/Backups/global_pre-migration_{UTC-timestamp}.json</c>。
    /// 路径基准：<c>ModuleHelper.GetModuleFullPath("SovereignTowns")/Configs</c>。
    /// 失败安静返回（不阻塞主流程，仅写日志）。</summary>
    public static void BackupCurrentConfigFile()
    {
        try
        {
            var modulePath = ModuleHelper.GetModuleFullPath(ModuleId);
            if (string.IsNullOrEmpty(modulePath))
            {
                Logger.Warn("ConfigMigrator.BackupCurrentConfigFile: module path is empty; skipped");
                return;
            }

            var configsDir = Path.Combine(modulePath, ConfigSubDir);
            var src = Path.Combine(configsDir, ConfigFileName);
            if (!File.Exists(src))
            {
                // 没有旧文件就无须备份；属于正常路径，记 Info 即可。
                Logger.Info($"ConfigMigrator.BackupCurrentConfigFile: no existing '{src}', skip");
                return;
            }

            var backupsDir = Path.Combine(configsDir, BackupsSubDir);
            Directory.CreateDirectory(backupsDir);

            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(backupsDir, $"global_pre-migration_{ts}.json");

            // 同一秒内重复调用时，附加序号避免覆盖。
            if (File.Exists(dest))
            {
                int seq = 1;
                string alt;
                do
                {
                    alt = Path.Combine(backupsDir, $"global_pre-migration_{ts}_{seq}.json");
                    seq++;
                } while (File.Exists(alt) && seq < 1000);
                dest = alt;
            }

            File.Copy(src, dest, overwrite: false);
            Logger.Info($"ConfigMigrator: backup -> {dest}");
        }
        catch (Exception ex)
        {
            Logger.Warn("ConfigMigrator.BackupCurrentConfigFile failed (swallowed): " + ex.Message);
        }
    }

    // ============================================================
    // Deep clone helpers (POCO-shaped; mirrors GlobalConfig layout)
    // ============================================================

    private static GlobalConfig DeepClone(GlobalConfig src)
    {
        var dst = new GlobalConfig
        {
            ConfigVersion = src.ConfigVersion,
            LastModified = src.LastModified ?? "",
            GlobalDefaults = CloneRule(src.GlobalDefaults),
            PerSettlementOverrides = new Dictionary<string, TownGarrisonRule>(),
            EnabledFeatures = CloneFeatures(src.EnabledFeatures),
            DailyPrisonerConformityAmount = src.DailyPrisonerConformityAmount,
            RecruiterEscortSize = src.RecruiterEscortSize,
            VillageCooldownHours = src.VillageCooldownHours,
            RecruiterReturnThreshold = src.RecruiterReturnThreshold
        };

        if (src.PerSettlementOverrides is not null)
        {
            foreach (var kv in src.PerSettlementOverrides)
            {
                dst.PerSettlementOverrides[kv.Key] = CloneRule(kv.Value);
            }
        }
        return dst;
    }

    private static TownGarrisonRule CloneRule(TownGarrisonRule? r)
    {
        if (r is null) return TownGarrisonRule.CreateDefault();
        return new TownGarrisonRule
        {
            TargetTotalCount = r.TargetTotalCount,
            UseGenericMatching = r.UseGenericMatching,
            ExactTroopTemplate = r.ExactTroopTemplate is null ? new Dictionary<string, int>() : new Dictionary<string, int>(r.ExactTroopTemplate),
            CavalryRatio = r.CavalryRatio,
            InfantryRatio = r.InfantryRatio,
            ArcherRatio = r.ArcherRatio,
            CrossbowRatio = r.CrossbowRatio,
            ThrowerRatio = r.ThrowerRatio,
            Tier1Ratio = r.Tier1Ratio,
            Tier2Ratio = r.Tier2Ratio,
            Tier3Ratio = r.Tier3Ratio,
            Tier4Ratio = r.Tier4Ratio,
            Tier5Ratio = r.Tier5Ratio,
            Tier6Ratio = r.Tier6Ratio,
            MinTier = r.MinTier,
            MaxTier = r.MaxTier,
            RestrictToFactionCultures = r.RestrictToFactionCultures,
            AllowedCultureIds = r.AllowedCultureIds is null ? new List<string>() : new List<string>(r.AllowedCultureIds),
            PriorityTroopIds = r.PriorityTroopIds is null ? new List<string>() : new List<string>(r.PriorityTroopIds),
            BannedTroopIds = r.BannedTroopIds is null ? new List<string>() : new List<string>(r.BannedTroopIds),
            AllowLowTierFiller = r.AllowLowTierFiller,
            AllowNobleTroops = r.AllowNobleTroops,
            AllowPrisonerConversion = r.AllowPrisonerConversion,
            AllowAutoUpgrade = r.AllowAutoUpgrade,
            PreserveExisting = r.PreserveExisting,
            AutoDisbandExcess = r.AutoDisbandExcess,
            MinimumDefenders = r.MinimumDefenders,
            WartimeMultiplier = r.WartimeMultiplier,
            PeacetimeMultiplier = r.PeacetimeMultiplier,
            BudgetLimit = r.BudgetLimit,
            FoodSafetyThreshold = r.FoodSafetyThreshold,
            DailyTroopXpBonus = r.DailyTroopXpBonus
        };
    }

    private static EnabledFeatures CloneFeatures(EnabledFeatures? f)
    {
        if (f is null) return new EnabledFeatures();
        return new EnabledFeatures
        {
            AutoGarrison = f.AutoGarrison,
            AutoRecruitment = f.AutoRecruitment,
            AutoPatrol = f.AutoPatrol,
            CastleSupport = f.CastleSupport,
            LlmReasoning = f.LlmReasoning,
            LlmAutoExecute = f.LlmAutoExecute,
            SallyForth = f.SallyForth,
            AutoRecruitMatchingPrisoners = f.AutoRecruitMatchingPrisoners,
            AutoSellNonMatchingPrisoners = f.AutoSellNonMatchingPrisoners,
            AutoSellLoot = f.AutoSellLoot
        };
    }
}
