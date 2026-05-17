using System;
using System.IO;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ModuleManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Configuration;

/// <summary>
/// 静态配置管理器。负责加载 / 校验 / 保存 Modules/SovereignTowns/Configs/global.json，
/// 并为各 Manager 提供 GetRuleFor(town) 查询入口。
/// 所有公开方法都做异常隔离，任何失败回退到 GlobalConfig.CreateDefault() 而不向调用方抛异常。
/// </summary>
/// <remarks>
/// 序列化实现说明：
/// 复用游戏 bin 目录自带的 Newtonsoft.Json.dll（与 ButterLib 等 mod 同一做法），通过 GameBinPath 引用，
/// Private=false 不进发布产物。
/// GlobalConfig 等 POCO 字段均为 public get/set，可直接 SerializeObject / DeserializeObject<T>，无需自定义 converter。
/// 反序列化后仍走 <see cref="ValidateConfig"/>，失败时回退到 <see cref="GlobalConfig.CreateDefault"/>。
/// </remarks>
public static class ConfigurationManager
{
    /// <summary>当前内置 schema 版本号。与磁盘 JSON 的 ConfigVersion 字段比对；不匹配即重置默认。</summary>
    public const int CurrentConfigVersion = 15;

    private const string ModuleId = "SovereignTowns";
    private const string ConfigSubDir = "Configs";
    private const string ConfigFileName = "global.json";

    // ratios sum 容忍区间（包含浮点累计误差与玩家手工填值）
    private const float RatioSumMin = 0.9f;
    private const float RatioSumMax = 1.1f;

    private static readonly object _gate = new object();
    private static GlobalConfig _current = GlobalConfig.CreateDefault();
    private static bool _initialized;
    private static string _lastValidationError = "";

    /// <summary>当前已加载的全局配置。Initialize 之前调用返回默认配置。</summary>
    public static GlobalConfig Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    /// <summary>最近一次 Save / ValidateCurrent 失败的原因。校验通过时为空字符串。</summary>
    public static string LastValidationError
    {
        get
        {
            lock (_gate) return _lastValidationError;
        }
    }

    /// <summary>启动期一次性初始化。从 Documents/Mount and Blade II Bannerlord/Configs/SovereignTowns/global.json
    /// 加载（B7.2 迁出 Modules 路径以避开 Steam C:\ UAC）；若新路径不存在但旧 Modules 路径有文件，自动迁移过来；
    /// 都不存在则创建默认值。校验失败时回退到默认值并写日志。</summary>
    public static void Initialize()
    {
        try
        {
            lock (_gate)
            {
                if (_initialized)
                {
                    Logger.Warn("ConfigurationManager.Initialize called more than once; ignored");
                    return;
                }

                string configPath = GetConfigFilePath();
                EnsureConfigDirectoryExists(configPath);
                TryMigrateLegacyConfigPath(configPath);

                if (!File.Exists(configPath))
                {
                    Logger.Info($"Config file not found at '{configPath}', creating defaults");
                    _current = GlobalConfig.CreateDefault();
                    WriteToDiskUnlocked(configPath, _current);
                }
                else
                {
                    var loaded = TryLoadFromDisk(configPath);
                    if (loaded is null)
                    {
                        Logger.Warn("Falling back to default GlobalConfig because load/validation failed");
                        _current = GlobalConfig.CreateDefault();
                    }
                    else
                    {
                        _current = loaded;
                        Logger.Info($"Config loaded: version={_current.ConfigVersion}, overrides={_current.PerSettlementOverrides.Count}");
                    }
                }

                _initialized = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigurationManager.Initialize failed", ex);
            lock (_gate)
            {
                _current = GlobalConfig.CreateDefault();
                _initialized = true;
            }
        }
    }

    /// <summary>
    /// 为某 Town 取最终生效规则。lookup 优先级：
    ///   1) PerSettlementOverrides[town.Settlement.StringId] —— 玩家手工 override（最高，玩家 + AI 城都适用）
    ///   2) AI 城 + ApplyToAiSettlementsToo=true → <see cref="AiCulturePresets"/> 按 OwnerClan.Culture.StringId 查
    ///   3) GlobalDefaults —— 兜底（玩家城无 override 时走此；AI 城未识别 culture 时也走此）
    /// </summary>
    public static TownGarrisonRule GetRuleFor(Town town)
    {
        try
        {
            lock (_gate)
            {
                if (town?.Settlement?.StringId is { } id
                    && _current.PerSettlementOverrides.TryGetValue(id, out var rule)
                    && rule is not null)
                {
                    return rule;
                }

                if (town?.OwnerClan != null
                    && town.OwnerClan != Clan.PlayerClan
                    && _current.EnabledFeatures?.ApplyToAiSettlementsToo == true)
                {
                    var preset = AiCulturePresets.TryGet(town.OwnerClan.Culture?.StringId);
                    if (preset != null) return preset;
                }

                return _current.GlobalDefaults;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetRuleFor failed; returning a fresh default rule", ex);
            return TownGarrisonRule.CreateDefault();
        }
    }

    /// <summary>
    /// 在写盘前对当前内存配置做一次校验。校验通过返回 true，并清空 <see cref="LastValidationError"/>；
    /// 否则返回 false 并填写错误原因。给 UI 在保存前实时回显警告用。
    /// </summary>
    public static bool TryValidateCurrent(out string reason)
    {
        try
        {
            lock (_gate)
            {
                bool ok = ValidateConfig(_current, out reason);
                _lastValidationError = ok ? "" : reason;
                return ok;
            }
        }
        catch (Exception ex)
        {
            reason = $"validation threw: {ex.Message}";
            lock (_gate) _lastValidationError = reason;
            Logger.Error("TryValidateCurrent failed", ex);
            return false;
        }
    }

    /// <summary>
    /// 将 Current 序列化到 global.json。覆盖式写入。
    /// 写盘前先做 <see cref="ValidateConfig"/>；若失败则拒绝写盘，记录原因并返回 false，
    /// 保证磁盘上的 last-known-good 配置永远不被坏值覆盖（防止"我的设置丢了"）。
    /// </summary>
    /// <returns>true = 写盘成功；false = 校验失败 / IO 异常，磁盘保持旧值。</returns>
    public static bool Save()
    {
        try
        {
            lock (_gate)
            {
                if (!ValidateConfig(_current, out var reason))
                {
                    _lastValidationError = reason;
                    Logger.Warn($"ConfigurationManager.Save refused: config invalid ({reason}); on-disk file left untouched");
                    return false;
                }
                _lastValidationError = "";

                string configPath = GetConfigFilePath();
                EnsureConfigDirectoryExists(configPath);
                _current.LastModified = DateTime.UtcNow.ToString("O");
                WriteToDiskUnlocked(configPath, _current);
                Logger.Info($"Config saved to '{configPath}'");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigurationManager.Save failed", ex);
            return false;
        }
    }

    /// <summary>
    /// B7.3 引入：把 newConfig 完整替换到 Current 并写盘。原子操作：
    /// Validate 失败 → in-memory 不动，返回 false + reason；
    /// Validate 通过 → 替换 + 写盘；写盘 IO 失败 → 替换仍然生效（与 Save 行为一致），返回 false。
    /// 用于网页 PUT /api/config 路径。
    /// </summary>
    public static bool ReplaceAndSave(GlobalConfig newConfig, out string reason)
    {
        if (newConfig is null)
        {
            reason = "newConfig is null";
            return false;
        }

        try
        {
            lock (_gate)
            {
                if (!ValidateConfig(newConfig, out reason))
                {
                    _lastValidationError = reason;
                    Logger.Warn($"ReplaceAndSave refused: {reason}");
                    return false;
                }

                _current = newConfig;
                _lastValidationError = "";

                string configPath = GetConfigFilePath();
                EnsureConfigDirectoryExists(configPath);
                _current.LastModified = DateTime.UtcNow.ToString("O");
                WriteToDiskUnlocked(configPath, _current);
                Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}'");
                reason = "";
                return true;
            }
        }
        catch (Exception ex)
        {
            reason = $"ReplaceAndSave threw: {ex.Message}";
            Logger.Error("ReplaceAndSave failed", ex);
            return false;
        }
    }

    /// <summary>从磁盘重新读取（用户手编 JSON 后可调用）。失败回退到上次成功的 Current。</summary>
    public static void Reload()
    {
        try
        {
            lock (_gate)
            {
                string configPath = GetConfigFilePath();
                if (!File.Exists(configPath))
                {
                    Logger.Warn($"Reload requested but '{configPath}' does not exist; keeping current in-memory config");
                    return;
                }

                var loaded = TryLoadFromDisk(configPath);
                if (loaded is null)
                {
                    Logger.Warn("Reload failed; keeping previous in-memory config");
                    return;
                }

                _current = loaded;
                Logger.Info($"Config reloaded: version={_current.ConfigVersion}, overrides={_current.PerSettlementOverrides.Count}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigurationManager.Reload failed", ex);
        }
    }

    // -------- path / IO helpers --------

    /// <summary>
    /// B7.2: 主配置路径迁到玩家文档目录，避开 Steam C:\ 写盘 UAC 提示。
    /// 与 TroopDumper.GetBaseDirectory() 保持同一根目录 SovereignTowns/。
    /// </summary>
    private static string GetConfigFilePath()
    {
        return Path.Combine(SovereignTowns.WebConfig.TroopDumper.GetBaseDirectory(), ConfigFileName);
    }

    /// <summary>
    /// B7.2: 历史上 global.json 写在 Modules/SovereignTowns/Configs/。如果新文档路径无文件、
    /// 旧 Modules 路径有文件，把旧文件拷过去做一次性迁移。原文件保留不删（玩家可手动清理）。
    /// </summary>
    private static void TryMigrateLegacyConfigPath(string newPath)
    {
        try
        {
            if (File.Exists(newPath)) return;

            string modulePath = ModuleHelper.GetModuleFullPath(ModuleId);
            string legacyPath = Path.Combine(modulePath, ConfigSubDir, ConfigFileName);
            if (!File.Exists(legacyPath)) return;

            File.Copy(legacyPath, newPath, overwrite: false);
            Logger.Info($"Migrated legacy config from '{legacyPath}' to '{newPath}' (legacy file kept; player can delete manually)");
        }
        catch (Exception ex)
        {
            Logger.Error("TryMigrateLegacyConfigPath failed (continuing with defaults)", ex);
        }
    }

    private static void EnsureConfigDirectoryExists(string configPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to ensure config directory for '{configPath}'", ex);
        }
    }

    /// <summary>读 + 反序列化 + 校验。任一步骤失败返回 null。</summary>
    private static GlobalConfig? TryLoadFromDisk(string configPath)
    {
        try
        {
            string text = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(text))
            {
                Logger.Error($"Config file '{configPath}' is empty");
                return null;
            }

            GlobalConfig? parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<GlobalConfig>(text, _jsonSettings);
            }
            catch (JsonException jex)
            {
                Logger.Error($"Config JSON parse error in '{configPath}': {jex.Message}", jex);
                return null;
            }

            if (parsed is null)
            {
                Logger.Error($"Config root in '{configPath}' deserialized to null");
                return null;
            }

            // Newtonsoft 不会自动调用 POCO 的字段默认初始化器去填 null 嵌套对象，
            // 这里兜底确保后续校验/调用不会 NRE。
            parsed.GlobalDefaults ??= TownGarrisonRule.CreateDefault();
            parsed.PerSettlementOverrides ??= new System.Collections.Generic.Dictionary<string, TownGarrisonRule>();
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.ClanPatrol ??= new ClanPatrolConfig();
            parsed.ClanRecruiter ??= new ClanRecruiterConfig();
            parsed.Thresholds ??= new PartyThresholds();
            parsed.LastModified ??= "";

            // B7.25：不再做版本迁移。版本不符即丢弃，由 Initialize() 兜底为默认。
            if (parsed.ConfigVersion != CurrentConfigVersion)
            {
                Logger.Warn($"Config 版本不匹配 (file={parsed.ConfigVersion}, expected={CurrentConfigVersion})；不做迁移，重置为默认。请重新在网页面板配置。");
                return null;
            }

            if (!ValidateConfig(parsed, out var reason))
            {
                Logger.Error($"Config validation failed: {reason}");
                return null;
            }

            return parsed;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load config from '{configPath}'", ex);
            return null;
        }
    }

    private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Culture = System.Globalization.CultureInfo.InvariantCulture,
    };

    private static void WriteToDiskUnlocked(string configPath, GlobalConfig config)
    {
        try
        {
            string json = JsonConvert.SerializeObject(config, _jsonSettings);
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to write config to '{configPath}'", ex);
        }
    }

    // -------- validation --------

    private static bool ValidateConfig(GlobalConfig config, out string reason)
    {
        if (config.GlobalDefaults is null)
        {
            reason = "GlobalDefaults is null";
            return false;
        }
        if (!ValidateRule(config.GlobalDefaults, "GlobalDefaults", out reason))
        {
            return false;
        }

        foreach (var kv in config.PerSettlementOverrides)
        {
            if (kv.Value is null)
            {
                reason = $"PerSettlementOverrides['{kv.Key}'] is null";
                return false;
            }
            if (!ValidateRule(kv.Value, $"PerSettlementOverrides['{kv.Key}']", out reason))
            {
                return false;
            }
        }

        if (config.VillageCooldownHours < 0)
        {
            reason = "VillageCooldownHours < 0";
            return false;
        }
        if (config.Thresholds != null && !ValidateThresholds(config.Thresholds, out reason))
        {
            return false;
        }

        reason = "";
        return true;
    }

    private static bool ValidateThresholds(PartyThresholds t, out string reason)
    {
        if (!IsRatio(t.PartyReturnSizeRatio))
        { reason = $"Thresholds.PartyReturnSizeRatio invalid ({t.PartyReturnSizeRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.PartyReturnWoundedRatio))
        { reason = $"Thresholds.PartyReturnWoundedRatio invalid ({t.PartyReturnWoundedRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.PatrolReserveAfterCreationRatio))
        { reason = $"Thresholds.PatrolReserveAfterCreationRatio invalid ({t.PatrolReserveAfterCreationRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.PatrolTroopBatchRatio))
        { reason = $"Thresholds.PatrolTroopBatchRatio invalid ({t.PatrolTroopBatchRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.RecruiterEscortRatio))
        { reason = $"Thresholds.RecruiterEscortRatio invalid ({t.RecruiterEscortRatio}); must be in [0,1]"; return false; }
        if (t.RecruiterReturnRecruitedCount < 1)
        { reason = $"Thresholds.RecruiterReturnRecruitedCount invalid ({t.RecruiterReturnRecruitedCount}); must be >= 1"; return false; }
        if (!IsRatio(t.TransferCriticalProjectedRatio))
        { reason = $"Thresholds.TransferCriticalProjectedRatio invalid ({t.TransferCriticalProjectedRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.TransferRatio))
        { reason = $"Thresholds.TransferRatio invalid ({t.TransferRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.TransferMaxTroopsPerTaskRatio))
        { reason = $"Thresholds.TransferMaxTroopsPerTaskRatio invalid ({t.TransferMaxTroopsPerTaskRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.TransferMinTroopRatio))
        { reason = $"Thresholds.TransferMinTroopRatio invalid ({t.TransferMinTroopRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.RecruitmentMinDemandRatio))
        { reason = $"Thresholds.RecruitmentMinDemandRatio invalid ({t.RecruitmentMinDemandRatio}); must be in [0,1]"; return false; }
        if (!IsRatio(t.SallyExtractionRatio))
        { reason = $"Thresholds.SallyExtractionRatio invalid ({t.SallyExtractionRatio}); must be in [0,1]"; return false; }
        if (!IsNonNegativeFloat(t.SallyTargetPartySizeMultiplier))
        { reason = $"Thresholds.SallyTargetPartySizeMultiplier invalid ({t.SallyTargetPartySizeMultiplier})"; return false; }
        if (t.SallyCreateMinPartyCount < 1)
        { reason = $"Thresholds.SallyCreateMinPartyCount invalid ({t.SallyCreateMinPartyCount}); must be >= 1"; return false; }

        reason = "";
        return true;
    }

    private static bool IsRatio(float v)
        => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f && v <= 1f;

    private static bool IsNonNegativeFloat(float v)
        => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f;

    private static bool ValidateRule(TownGarrisonRule rule, string ctx, out string reason)
    {
        if (rule.TargetTotalCount < 0)
        {
            reason = $"{ctx}.TargetTotalCount < 0";
            return false;
        }
        if (rule.ExactTroopTemplate is null)
        {
            reason = $"{ctx}.ExactTroopTemplate is null";
            return false;
        }
        foreach (var kv in rule.ExactTroopTemplate)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                reason = $"{ctx}.ExactTroopTemplate contains empty troop id";
                return false;
            }
            if (float.IsNaN(kv.Value) || float.IsInfinity(kv.Value) || kv.Value < 0f || kv.Value > 1f)
            {
                reason = $"{ctx}.ExactTroopTemplate['{kv.Key}'] = {kv.Value} 不在 [0,1] 占比范围";
                return false;
            }
        }
        if (rule.MinTier < 1 || rule.MaxTier < rule.MinTier || rule.MaxTier > 7)
        {
            reason = $"{ctx}.MinTier/MaxTier invalid ({rule.MinTier}..{rule.MaxTier})";
            return false;
        }
        if (!IsRatio(rule.MinimumDefenderRatio))
        {
            reason = $"{ctx}.MinimumDefenderRatio invalid ({rule.MinimumDefenderRatio}); must be in [0,1]";
            return false;
        }
        if (rule.BudgetLimit < 0)
        {
            reason = $"{ctx}.BudgetLimit < 0";
            return false;
        }
        if (rule.WartimeMultiplier < 0f || rule.PeacetimeMultiplier < 0f)
        {
            reason = $"{ctx}.WartimeMultiplier/PeacetimeMultiplier must be >= 0";
            return false;
        }
        if (!ValidateRatio(rule.CavalryRatio, $"{ctx}.CavalryRatio", out reason)
            || !ValidateRatio(rule.HorseArcherRatio, $"{ctx}.HorseArcherRatio", out reason)
            || !ValidateRatio(rule.InfantryRatio, $"{ctx}.InfantryRatio", out reason)
            || !ValidateRatio(rule.RangedRatio, $"{ctx}.RangedRatio", out reason))
        {
            return false;
        }

        float troopSum = rule.CavalryRatio + rule.HorseArcherRatio + rule.InfantryRatio + rule.RangedRatio;
        if (troopSum < RatioSumMin || troopSum > RatioSumMax)
        {
            reason = $"{ctx} troop ratios sum={troopSum:F3} outside [{RatioSumMin},{RatioSumMax}]";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool ValidateRatio(float value, string field, out string reason)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
        {
            reason = $"{field} invalid ({value})";
            return false;
        }

        reason = "";
        return true;
    }




}
