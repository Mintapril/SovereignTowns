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
    public const int CurrentConfigVersion = 19;

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

    /// <summary>
    /// B17.4 B1 / Issue #1：GlobalDefaults 或 BranchDefaults 变更后触发。
    /// 参数：被改的 settlement.StringId，或 null 表示全局/未知（订阅者需对所有 in-flight 队伍重规划）。
    /// 永远从主线程触发 —— Web 路径走 <see cref="WebConfigGameThreadSync.RequestConfigChanged"/>
    /// 入队，下一次 Drain 在 campaign tick 主线程上调用 <see cref="RaiseConfigChanged"/>。
    /// </summary>
    public static event Action<string?>? OnConfigChanged;

    /// <summary>
    /// Issue #1：仅供 <see cref="SovereignTowns.WebConfig.WebConfigGameThreadSync.Drain"/> 在主线程
    /// 调用。其他路径不应直接 invoke 事件（事件 access 受 C# 编译器限制本就只能在本类内）。
    /// </summary>
    internal static void RaiseConfigChanged(string? settlementId)
    {
        // 2026-05-18：配置变更后先同步 Logger 等级（玩家在 WebUI 切 VerboseLogging 立即生效），
        // 再 fire 业务订阅。在主线程调用，Logger.SetMinLevel 是普通字段赋值，零阻塞。
        try { ApplyVerboseLoggingFromConfig(); }
        catch (Exception ex) { Logger.Warn($"ApplyVerboseLoggingFromConfig failed: {ex.Message}"); }

        try { OnConfigChanged?.Invoke(settlementId); }
        catch (Exception ex) { Logger.Warn($"OnConfigChanged invocation failed: {ex.Message}"); }
    }

    /// <summary>
    /// 把 GlobalConfig.EnabledFeatures.VerboseLogging 同步到 Logger.MinLevel。Initialize 末尾 + 每次
    /// OnConfigChanged 都调一次，因此 WebUI 上点开/关 verbose 立即生效，无需重启游戏。
    /// </summary>
    private static void ApplyVerboseLoggingFromConfig()
    {
        bool verbose;
        lock (_gate) { verbose = _current?.EnabledFeatures?.VerboseLogging ?? false; }
        var newLevel = verbose ? Logging.LogLevel.Debug : Logging.LogLevel.Info;
        Logger.SetMinLevel(newLevel);
        Logger.Info($"Logger minLevel set to {newLevel} (VerboseLogging={verbose})");
    }

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
                // Issue #2: 上次写盘途中崩溃可能留下 .bak 但主文件缺失 — 启动时尝试回滚。
                TryRestoreFromBak(configPath);

                if (!File.Exists(configPath))
                {
                    Logger.Info($"Config file not found at '{configPath}', creating defaults");
                    _current = GlobalConfig.CreateDefault();
                    try { WriteToDiskUnlocked(configPath, _current); }
                    catch (Exception writeEx) { Logger.Error($"Initial WriteToDisk failed for '{configPath}'; running with in-memory defaults", writeEx); }
                }
                else
                {
                    var loaded = TryLoadFromDisk(configPath);
                    if (loaded is null)
                    {
                        Logger.Warn("Falling back to default GlobalConfig because load/validation failed");
                        _current = GlobalConfig.CreateDefault();
                        // DeepSeek audit fix (2026-05-18)：fallback 后立即把默认写回磁盘，
                        // 否则磁盘上的旧版本/坏配置永久滞留 → 用户点"重载"反复 422。
                        try
                        {
                            WriteToDiskUnlocked(configPath, _current);
                            Logger.Info($"Persisted default config to '{configPath}' after fallback (disk version synced to in-memory)");
                        }
                        catch (Exception writeEx)
                        {
                            Logger.Error($"Failed to persist default config after fallback for '{configPath}'; in-memory and disk versions may diverge", writeEx);
                        }
                    }
                    else
                    {
                        _current = loaded;
                        Logger.Info($"Config loaded: version={_current.ConfigVersion}");
                    }
                }

                _initialized = true;
            }

            // 2026-05-18：启动时按磁盘配置同步 Logger 等级。在 lock 外调用，避免 SetMinLevel
            // 内部触发的 Info log 在持锁下递归 enqueue（虽然 Logger 用独立 lock，但纪律性更好）。
            try { ApplyVerboseLoggingFromConfig(); }
            catch (Exception ex) { Logger.Warn($"ApplyVerboseLoggingFromConfig at Initialize failed: {ex.Message}"); }
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
    /// 为某 Town 取首府规则（CapitalRule）。仅供首府路径调用 —
    /// 非首府请用 <see cref="GetBranchRuleFor"/>。
    /// AI 城 + ApplyToAiSettlementsToo=true 走 <see cref="AiCulturePresets"/>，否则走 GlobalDefaults。
    /// </summary>
    public static TownGarrisonRule GetRuleFor(Town town)
    {
        try
        {
            lock (_gate)
            {
                if (town?.OwnerClan != null
                    && town.OwnerClan != Clan.PlayerClan
                    && _current.EnabledFeatures?.ApplyToAiSettlementsToo == true)
                {
                    var preset = AiCulturePresets.TryGet(town.OwnerClan.Culture?.StringId);
                    if (preset != null) return preset.Clone();
                }

                return (_current.GlobalDefaults ?? TownGarrisonRule.CreateDefault()).Clone();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetRuleFor failed; returning a fresh default rule", ex);
            return TownGarrisonRule.CreateDefault();
        }
    }

    /// <summary>
    /// 为某 Town 取非首府规则（BranchRule）。
    /// 玩家氏族返回 <see cref="GlobalConfig.BranchDefaults"/>。
    /// AI 氏族（启用 ApplyToAiSettlementsToo 时）调 vanilla 公式动态算 TargetPower；
    /// LowTierMinFraction 沿用全局 BranchDefaults。
    /// </summary>
    public static BranchRule GetBranchRuleFor(Town town)
    {
        try
        {
            lock (_gate)
            {
                var rule = (_current.BranchDefaults ?? BranchRule.CreateDefault()).Clone();

                if (town?.OwnerClan != null
                    && town.OwnerClan != Clan.PlayerClan
                    && _current.EnabledFeatures?.ApplyToAiSettlementsToo == true)
                {
                    int aiTarget = SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeAiVanillaTargetPower(town);
                    if (aiTarget > 0) rule.TargetPower = aiTarget;
                }

                return rule;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetBranchRuleFor failed; returning a fresh default branch rule", ex);
            return BranchRule.CreateDefault();
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

    // -------- content-diff helper --------

    /// <summary>
    /// 把两个 GlobalConfig 序列化为 JSON 字符串后逐字符比较。
    /// 利用 Newtonsoft 默认 reflection 顺序（稳定）保证同字段顺序；
    /// Ignore NullValueHandling 与正常写盘设置一致，防止多余 null 字段造成假差异。
    /// </summary>
    private static bool ConfigsAreEqual(GlobalConfig a, GlobalConfig b)
    {
        try
        {
            string jsonA = JsonConvert.SerializeObject(a, _jsonSettings);
            string jsonB = JsonConvert.SerializeObject(b, _jsonSettings);
            return string.Equals(jsonA, jsonB, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ConfigsAreEqual serialization failed; treating as changed: {ex.Message}");
            return false; // 序列化异常时保守地认为有变化，确保事件仍触发
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
                try
                {
                    WriteToDiskUnlocked(configPath, _current);
                }
                catch (Exception writeEx)
                {
                    Logger.Error("ConfigurationManager.Save: write failed", writeEx);
                    return false;
                }
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
    /// B7.3 / Issue #2：把 newConfig 完整替换到 Current 并写盘。原子操作：
    /// Validate 失败 → in-memory 不动，返回 false + reason；
    /// Validate 通过 → 替换 + 写盘；写盘 IO 失败 → **回滚 in-memory**，返回 false + reason。
    /// 用于网页 PUT /api/config 路径。Issue #9：ConfigVersion 由本端强制覆盖到 CurrentConfigVersion。
    /// P0-1 修复：<paramref name="changed"/> 指示内容是否真正变化（JSON diff），
    /// 调用方仅在 changed=true 时才广播 OnConfigChanged，避免无变化 PUT 重置 in-flight 队伍。
    /// </summary>
    public static bool ReplaceAndSave(GlobalConfig newConfig, out string reason, out bool changed)
    {
        changed = false;
        if (newConfig is null)
        {
            reason = "newConfig is null";
            return false;
        }

        try
        {
            lock (_gate)
            {
                // Issue #9：ConfigVersion 永远由本端管理 — 客户端提交什么都强制对齐，
                // 避免下次启动被 TryLoadFromDisk 的版本校验丢回默认。
                newConfig.ConfigVersion = CurrentConfigVersion;

                if (!ValidateConfig(newConfig, out reason))
                {
                    _lastValidationError = reason;
                    Logger.Warn($"ReplaceAndSave refused: {reason}");
                    return false;
                }

                // P0-1 修复：JSON content-diff，UI-only 字段（ShowDailySummary 等）
                // 和真正影响 in-flight recruiter 的字段一起比较；只有真变化才让上层广播。
                changed = !ConfigsAreEqual(_current, newConfig);

                var previousConfig = _current;
                _current = newConfig;
                _lastValidationError = "";

                string configPath = GetConfigFilePath();
                EnsureConfigDirectoryExists(configPath);
                _current.LastModified = DateTime.UtcNow.ToString("O");
                try
                {
                    WriteToDiskUnlocked(configPath, _current);
                }
                catch (Exception writeEx)
                {
                    // Issue #2：写盘真正失败必须让上层知道；同时回滚 in-memory，
                    // 避免"内存已换、磁盘是旧的"导致重启后玩家配置静默丢失。
                    _current = previousConfig;
                    changed = false;
                    reason = $"WriteToDisk failed: {writeEx.Message}";
                    Logger.Error("ReplaceAndSave: write failed; rolled back in-memory config", writeEx);
                    return false;
                }

                if (changed)
                    Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}' (content changed → will broadcast OnConfigChanged)");
                else
                    Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}' (content identical → no broadcast)");

                // Issue #1：OnConfigChanged 已迁移到 WebConfigGameThreadSync.Drain
                // 在主线程触发；此处不再直接 invoke（HTTP 路径下我们在 ThreadPool 线程上）。

                reason = "";
                return true;
            }
        }
        catch (Exception ex)
        {
            reason = $"ReplaceAndSave threw: {ex.Message}";
            changed = false;
            Logger.Error("ReplaceAndSave failed", ex);
            return false;
        }
    }

    /// <summary>兼容旧调用方（不关心 changed 信号）的重载。</summary>
    public static bool ReplaceAndSave(GlobalConfig newConfig, out string reason)
        => ReplaceAndSave(newConfig, out reason, out _);

    /// <summary>
    /// 从磁盘重新读取（用户手编 JSON 后可调用）。失败回退到上次成功的 Current。
    /// 返回 (ok, reason)：ok=true 表示已替换内存配置；false 时 reason 给出失败原因（UI 用）。
    /// P0-1 修复：<paramref name="changed"/> 指示磁盘内容是否与当前 in-memory 不同（JSON diff）。
    /// 调用方仅在 changed=true 时才广播 OnConfigChanged，避免无变化 reload 重置 in-flight 队伍。
    /// </summary>
    public static bool TryReload(out string reason, out bool changed)
    {
        changed = false;
        try
        {
            lock (_gate)
            {
                string configPath = GetConfigFilePath();
                if (!File.Exists(configPath))
                {
                    reason = $"config file not found: {configPath}";
                    Logger.Warn($"Reload requested but '{configPath}' does not exist; keeping current in-memory config");
                    return false;
                }

                var loaded = TryLoadFromDisk(configPath);
                if (loaded is null)
                {
                    reason = "config load/parse/validation failed (see logs); kept previous in-memory config";
                    Logger.Warn("Reload failed; keeping previous in-memory config");
                    return false;
                }

                // P0-1 修复：JSON content-diff — 只有磁盘内容与内存不同才算"真变化"。
                changed = !ConfigsAreEqual(_current, loaded);
                _current = loaded;
                reason = "";

                if (changed)
                    Logger.Info($"Config reloaded: version={_current.ConfigVersion} (content changed → will broadcast OnConfigChanged)");
                else
                    Logger.Info($"Config reloaded: version={_current.ConfigVersion} (content identical → no broadcast)");

                return true;
            }
        }
        catch (Exception ex)
        {
            reason = $"reload threw: {ex.Message}";
            Logger.Error("ConfigurationManager.Reload failed", ex);
            return false;
        }
    }

    /// <summary>兼容旧调用方（不关心 changed 信号）的重载。</summary>
    public static bool TryReload(out string reason) => TryReload(out reason, out _);

    /// <summary>旧 void 包装 — 兼容现有调用方。新代码请用 <see cref="TryReload"/>。</summary>
    public static void Reload() => TryReload(out _, out _);

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
            parsed.BranchDefaults ??= BranchRule.CreateDefault();
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.ClanPatrol ??= new ClanPatrolConfig();
            parsed.ClanRecruiter ??= new ClanRecruiterConfig();
            parsed.Thresholds ??= new PartyThresholds();
            parsed.LastModified ??= "";

            // B7.25：不再做版本迁移。版本不符即丢弃，由 Initialize() 兜底为默认。
            if (parsed.ConfigVersion != CurrentConfigVersion)
            {
                string msg = $"[主权城镇] global.json 版本不匹配 (file v{parsed.ConfigVersion}, expected v{CurrentConfigVersion}) — 已重置为默认，请重新在网页面板配置";
                Logger.Warn(msg);
                // B17.4 A4：升级到 UI 黄色 — 玩家不会再"静默丢配置"
                // 注：InformationManager / InformationMessage 实际驻 TaleWorlds.Library（与 Colors 同程序集）。
                try
                {
                    TaleWorlds.Library.InformationManager.DisplayMessage(
                        new TaleWorlds.Library.InformationMessage(msg, TaleWorlds.Library.Colors.Yellow));
                }
                catch (Exception uiEx) { Logger.Warn($"version-mismatch UI display failed: {uiEx.Message}"); }
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

    /// <summary>
    /// B17.4 S4 / Issue #2：原子写盘 — tmp → swap → backup。崩溃/断电中途绝不留半截/0 字节文件。
    /// net472 缺 File.Replace 跨卷保证；用 Delete + Move 替代,前一份保留为 .bak。
    /// 失败时清残留 tmp，并尽力把刚搬走的 main 从 .bak 滚回；**始终向调用方抛**，
    /// 让 <see cref="Save"/> / <see cref="ReplaceAndSave"/> 把失败传递到上层（HTTP/UI 报错）。
    /// </summary>
    private static void WriteToDiskUnlocked(string configPath, GlobalConfig config)
    {
        string tmpPath = configPath + ".tmp";
        string bakPath = configPath + ".bak";
        bool mainMovedToBak = false;
        try
        {
            string json = JsonConvert.SerializeObject(config, _jsonSettings);

            // 1. 全量写到 tmp（独立文件，失败不污染主文件）
            File.WriteAllText(tmpPath, json);

            // 2. 把当前 main 备份到 .bak（若 main 存在）
            if (File.Exists(configPath))
            {
                if (File.Exists(bakPath)) File.Delete(bakPath);
                File.Move(configPath, bakPath);
                mainMovedToBak = true;
            }

            // 3. tmp → main（这一刻起新内容生效）
            File.Move(tmpPath, configPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to write config to '{configPath}' (atomic swap)", ex);

            // 残留 tmp 清理：不留半截文件给下次 Reload 误读
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
            catch (Exception cleanupEx) { Logger.Warn($"WriteToDiskUnlocked: failed to clean up '{tmpPath}': {cleanupEx.Message}"); }

            // 若 main 已搬到 .bak 但 swap 失败 → 主文件丢失。尽力滚回。
            if (mainMovedToBak)
            {
                try
                {
                    if (!File.Exists(configPath) && File.Exists(bakPath))
                    {
                        File.Move(bakPath, configPath);
                        Logger.Info($"WriteToDiskUnlocked: restored '{configPath}' from '.bak' after swap failure");
                    }
                }
                catch (Exception restoreEx)
                {
                    Logger.Error($"WriteToDiskUnlocked: failed to restore '{configPath}' from '.bak'; manual recovery required", restoreEx);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Issue #2 启动期兜底：若主文件不存在但 .bak 存在 → 把 .bak 滚回主文件。
    /// 通常发生在上次写盘途中进程崩溃 / 断电。失败仅 warn，不阻断 Initialize。
    /// </summary>
    private static void TryRestoreFromBak(string configPath)
    {
        try
        {
            if (File.Exists(configPath)) return;
            string bakPath = configPath + ".bak";
            if (!File.Exists(bakPath)) return;
            File.Move(bakPath, configPath);
            Logger.Warn($"Restored config from '.bak' (main missing): '{configPath}' — previous write likely interrupted");
        }
        catch (Exception ex)
        {
            Logger.Error($"TryRestoreFromBak failed for '{configPath}'", ex);
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
        if (config.BranchDefaults is null)
        {
            reason = "BranchDefaults is null";
            return false;
        }
        if (!ValidateBranchRule(config.BranchDefaults, "BranchDefaults", out reason))
        {
            return false;
        }

        if (config.VillageCooldownHours < 12 || config.VillageCooldownHours > 240)
        {
            reason = $"VillageCooldownHours invalid ({config.VillageCooldownHours}); [12, 240]";
            return false;
        }
        if (config.Thresholds != null && !ValidateThresholds(config.Thresholds, out reason))
        {
            return false;
        }
        // Issue：ClanPatrol / ClanRecruiter 之前缺校验，负数 / NaN 会进 scheduler。
        if (config.ClanPatrol != null && !ValidateClanPatrol(config.ClanPatrol, out reason))
        {
            return false;
        }
        if (config.ClanRecruiter != null && !ValidateClanRecruiter(config.ClanRecruiter, out reason))
        {
            return false;
        }

        reason = "";
        return true;
    }

    private static bool ValidateClanPatrol(ClanPatrolConfig c, out string reason)
    {
        if (!IsNonNegativeFloat(c.EtaBufferHours) || c.EtaBufferHours > 168f)
        { reason = $"ClanPatrol.EtaBufferHours invalid ({c.EtaBufferHours}); [0, 168]"; return false; }
        if (!IsNonNegativeFloat(c.StuckTimeoutHours) || c.StuckTimeoutHours > 720f || c.StuckTimeoutHours < 1f)
        { reason = $"ClanPatrol.StuckTimeoutHours invalid ({c.StuckTimeoutHours}); [1, 720]"; return false; }
        if (!IsNonNegativeFloat(c.MinVisitGapHours) || c.MinVisitGapHours > 720f)
        { reason = $"ClanPatrol.MinVisitGapHours invalid ({c.MinVisitGapHours}); [0, 720]"; return false; }
        if (!IsNonNegativeFloat(c.DistanceWeightHoursPerTile) || c.DistanceWeightHoursPerTile > 100f)
        { reason = $"ClanPatrol.DistanceWeightHoursPerTile invalid ({c.DistanceWeightHoursPerTile}); [0, 100]"; return false; }
        if (!IsNonNegativeFloat(c.SupportEtaThresholdHours) || c.SupportEtaThresholdHours > 168f)
        { reason = $"ClanPatrol.SupportEtaThresholdHours invalid ({c.SupportEtaThresholdHours}); [0, 168]"; return false; }
        reason = "";
        return true;
    }

    private static bool ValidateClanRecruiter(ClanRecruiterConfig c, out string reason)
    {
        if (!IsNonNegativeFloat(c.EtaBufferHours) || c.EtaBufferHours > 168f)
        { reason = $"ClanRecruiter.EtaBufferHours invalid ({c.EtaBufferHours}); [0, 168]"; return false; }
        if (!IsNonNegativeFloat(c.MinVisitGapHours) || c.MinVisitGapHours > 720f)
        { reason = $"ClanRecruiter.MinVisitGapHours invalid ({c.MinVisitGapHours}); [0, 720]"; return false; }
        if (!IsNonNegativeFloat(c.DistanceWeightHoursPerTile) || c.DistanceWeightHoursPerTile > 100f)
        { reason = $"ClanRecruiter.DistanceWeightHoursPerTile invalid ({c.DistanceWeightHoursPerTile}); [0, 100]"; return false; }
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
        if (t.RecruiterReturnRecruitedCount > 1000)
        { reason = $"Thresholds.RecruiterReturnRecruitedCount {t.RecruiterReturnRecruitedCount} 超过上限 1000"; return false; }
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
        // C (DeepSeek audit 2026-05-18)：与前端 thresholdSpecs max=5 对齐。原 100 上限远超合理范围。
        if (t.SallyTargetPartySizeMultiplier > 5f)
        { reason = $"Thresholds.SallyTargetPartySizeMultiplier {t.SallyTargetPartySizeMultiplier} 超过上限 5"; return false; }
        if (t.SallyCreateMinPartyCount < 1)
        { reason = $"Thresholds.SallyCreateMinPartyCount invalid ({t.SallyCreateMinPartyCount}); must be >= 1"; return false; }
        if (t.SallyCreateMinPartyCount > 1000)
        { reason = $"Thresholds.SallyCreateMinPartyCount {t.SallyCreateMinPartyCount} 超过上限 1000"; return false; }

        // Issue #5：B17.4 新增阈值校验。
        if (t.RecruiterMinHomeGarrison < 0)
        { reason = $"Thresholds.RecruiterMinHomeGarrison invalid ({t.RecruiterMinHomeGarrison}); must be >= 0"; return false; }
        if (t.RecruiterMinHomeGarrison > 10000)
        { reason = $"Thresholds.RecruiterMinHomeGarrison {t.RecruiterMinHomeGarrison} 超过上限 10000"; return false; }
        if (t.PartyPrisonerCap < 0)
        { reason = $"Thresholds.PartyPrisonerCap invalid ({t.PartyPrisonerCap}); must be >= 0"; return false; }
        if (t.PartyPrisonerCap > 10000)
        { reason = $"Thresholds.PartyPrisonerCap {t.PartyPrisonerCap} 超过上限 10000"; return false; }
        if (!IsNonNegativeFloat(t.StuckTeleportHours))
        { reason = $"Thresholds.StuckTeleportHours invalid ({t.StuckTeleportHours}); must be >= 0"; return false; }
        if (t.StuckTeleportHours > 720f)
        { reason = $"Thresholds.StuckTeleportHours {t.StuckTeleportHours} 超过上限 720"; return false; }
        if (!IsNonNegativeFloat(t.PatrolMaxLifetimeHours) || t.PatrolMaxLifetimeHours > 720f)
        { reason = $"Thresholds.PatrolMaxLifetimeHours invalid ({t.PatrolMaxLifetimeHours}); [0, 720]"; return false; }
        if (t.PatrolMinDispatchSize < 0)
        { reason = $"Thresholds.PatrolMinDispatchSize invalid ({t.PatrolMinDispatchSize}); must be >= 0"; return false; }
        if (t.PatrolMinDispatchSize > 500)
        { reason = $"Thresholds.PatrolMinDispatchSize {t.PatrolMinDispatchSize} 超过上限 500"; return false; }
        if (!IsNonNegativeFloat(t.FoodReplenishMinDays))
        { reason = $"Thresholds.FoodReplenishMinDays invalid ({t.FoodReplenishMinDays})"; return false; }
        if (t.FoodReplenishMinDays > 365f)
        { reason = $"Thresholds.FoodReplenishMinDays {t.FoodReplenishMinDays} 超过上限 365"; return false; }
        if (!IsNonNegativeFloat(t.FoodReplenishTopUpDays))
        { reason = $"Thresholds.FoodReplenishTopUpDays invalid ({t.FoodReplenishTopUpDays})"; return false; }
        if (t.FoodReplenishTopUpDays > 365f)
        { reason = $"Thresholds.FoodReplenishTopUpDays {t.FoodReplenishTopUpDays} 超过上限 365"; return false; }

        // DeepSeek audit 2026-05-18 新增字段校验
        if (!IsNonNegativeFloat(t.IdleHoursBeforeForceReturn) || t.IdleHoursBeforeForceReturn < 1f || t.IdleHoursBeforeForceReturn > 720f)
        { reason = $"Thresholds.IdleHoursBeforeForceReturn invalid ({t.IdleHoursBeforeForceReturn}); [1, 720]"; return false; }
        if (!IsNonNegativeFloat(t.IdleHoursBeforeDisband) || t.IdleHoursBeforeDisband < 1f || t.IdleHoursBeforeDisband > 720f)
        { reason = $"Thresholds.IdleHoursBeforeDisband invalid ({t.IdleHoursBeforeDisband}); [1, 720]"; return false; }
        if (t.IdleHoursBeforeDisband < t.IdleHoursBeforeForceReturn)
        { reason = $"Thresholds.IdleHoursBeforeDisband ({t.IdleHoursBeforeDisband}) 必须 ≥ IdleHoursBeforeForceReturn ({t.IdleHoursBeforeForceReturn})"; return false; }
        if (!IsNonNegativeFloat(t.SallyDetectionRadius) || t.SallyDetectionRadius < 10f || t.SallyDetectionRadius > 500f)
        { reason = $"Thresholds.SallyDetectionRadius invalid ({t.SallyDetectionRadius}); [10, 500]"; return false; }
        if (!IsNonNegativeFloat(t.SallyCooldownHours) || t.SallyCooldownHours > 168f)
        { reason = $"Thresholds.SallyCooldownHours invalid ({t.SallyCooldownHours}); [0, 168]"; return false; }
        if (t.SallyMinSustainedTicks < 1 || t.SallyMinSustainedTicks > 48)
        { reason = $"Thresholds.SallyMinSustainedTicks invalid ({t.SallyMinSustainedTicks}); [1, 48]"; return false; }
        if (!IsRatio(t.TransferCapacityWeight))
        { reason = $"Thresholds.TransferCapacityWeight invalid ({t.TransferCapacityWeight}); [0, 1]"; return false; }
        if (!IsNonNegativeFloat(t.TransferBranchToBranchPenalty) || t.TransferBranchToBranchPenalty > 100f)
        { reason = $"Thresholds.TransferBranchToBranchPenalty invalid ({t.TransferBranchToBranchPenalty}); [0, 100]"; return false; }
        if (!IsNonNegativeFloat(t.TransferCapitalSourcePenalty) || t.TransferCapitalSourcePenalty > 100f)
        { reason = $"Thresholds.TransferCapitalSourcePenalty invalid ({t.TransferCapitalSourcePenalty}); [0, 100]"; return false; }
        if (!IsRatio(t.AutoUpgradeMinTierRatio))
        { reason = $"Thresholds.AutoUpgradeMinTierRatio invalid ({t.AutoUpgradeMinTierRatio}); [0, 1]"; return false; }
        if (t.AutoUpgradeMinBudget < 0 || t.AutoUpgradeMinBudget > 50000)
        { reason = $"Thresholds.AutoUpgradeMinBudget invalid ({t.AutoUpgradeMinBudget}); [0, 50000]"; return false; }
        if (t.AutoUpgradeMaxPerCall < 1 || t.AutoUpgradeMaxPerCall > 500)
        { reason = $"Thresholds.AutoUpgradeMaxPerCall invalid ({t.AutoUpgradeMaxPerCall}); [1, 500]"; return false; }
        // T1 重整 2026-05-18：seed gold 统一到 StPartyComponent.DefaultSeedGold，删除 RecruiterSeedGold/SallySeedGold 字段及其验证。
        if (t.RecruitmentCandidateBatchSize < 1 || t.RecruitmentCandidateBatchSize > 50)
        { reason = $"Thresholds.RecruitmentCandidateBatchSize invalid ({t.RecruitmentCandidateBatchSize}); [1, 50]"; return false; }
        if (t.McmfHardPenalty < 0 || t.McmfHardPenalty > 100000)
        { reason = $"Thresholds.McmfHardPenalty invalid ({t.McmfHardPenalty}); [0, 100000]"; return false; }
        if (t.McmfTierPenalty < 0 || t.McmfTierPenalty > 100000)
        { reason = $"Thresholds.McmfTierPenalty invalid ({t.McmfTierPenalty}); [0, 100000]"; return false; }
        if (!IsRatio(t.McmfLeniency))
        { reason = $"Thresholds.McmfLeniency invalid ({t.McmfLeniency}); [0, 1]"; return false; }
        if (t.McmfUnmetCost < 0 || t.McmfUnmetCost > 100000)
        { reason = $"Thresholds.McmfUnmetCost invalid ({t.McmfUnmetCost}); [0, 100000]"; return false; }
        if (t.McmfRecruiterOverhead < 0 || t.McmfRecruiterOverhead > 100000)
        { reason = $"Thresholds.McmfRecruiterOverhead invalid ({t.McmfRecruiterOverhead}); [0, 100000]"; return false; }
        if (t.McmfTransferOverhead < 0 || t.McmfTransferOverhead > 100000)
        { reason = $"Thresholds.McmfTransferOverhead invalid ({t.McmfTransferOverhead}); [0, 100000]"; return false; }

        reason = "";
        return true;
    }

    private static bool IsRatio(float v)
        => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f && v <= 1f;

    private static bool IsNonNegativeFloat(float v)
        => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f;

    private static bool IsFiniteFloat(float v)
        => !float.IsNaN(v) && !float.IsInfinity(v);

    private static bool ValidateRule(TownGarrisonRule rule, string ctx, out string reason)
    {
        if (rule.TargetTotalCount < 0)
        {
            reason = $"{ctx}.TargetTotalCount < 0";
            return false;
        }
        if (rule.TargetTotalCount > 100_000)
        {
            reason = $"{ctx}.TargetTotalCount {rule.TargetTotalCount} 超过上限 100000";
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
        if (!rule.UseGenericMatching && rule.ExactTroopTemplate.Count > 0)
        {
            float exactTemplateSum = 0f;
            foreach (var ratio in rule.ExactTroopTemplate.Values)
            {
                exactTemplateSum += ratio;
            }
            if (exactTemplateSum < RatioSumMin || exactTemplateSum > RatioSumMax)
            {
                reason = $"{ctx}.ExactTroopTemplate ratio sum={exactTemplateSum:F3} outside [{RatioSumMin},{RatioSumMax}]";
                return false;
            }
        }
        // Vanilla CharacterObject.Tier 实际范围 1..6（spnpccharacters.xml + native CharacterTiers），
        // 上限设 6 防止玩家手填 7 后通用匹配始终查不到兵种、模式静默失效。
        if (rule.MinTier < 1 || rule.MinTier > 6)
        {
            reason = $"{ctx}.MinTier {rule.MinTier} 必须在 [1,6]";
            return false;
        }
        if (rule.MaxTier > 6)
        {
            reason = $"{ctx}.MaxTier {rule.MaxTier} 超过 vanilla 上限 6";
            return false;
        }
        if (rule.MaxTier < rule.MinTier)
        {
            reason = $"{ctx}.MaxTier {rule.MaxTier} < MinTier {rule.MinTier}";
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
        if (rule.BudgetLimit > 10_000_000)
        {
            reason = $"{ctx}.BudgetLimit {rule.BudgetLimit} 超过上限 10000000";
            return false;
        }
        if (rule.WartimeMultiplier < 0f || rule.PeacetimeMultiplier < 0f)
        {
            reason = $"{ctx}.WartimeMultiplier/PeacetimeMultiplier must be >= 0";
            return false;
        }
        if (rule.WartimeMultiplier > 10f)
        {
            reason = $"{ctx}.WartimeMultiplier {rule.WartimeMultiplier} 超过上限 10";
            return false;
        }
        if (rule.PeacetimeMultiplier > 10f)
        {
            reason = $"{ctx}.PeacetimeMultiplier {rule.PeacetimeMultiplier} 超过上限 10";
            return false;
        }
        if (!IsFiniteFloat(rule.FoodSafetyThreshold))
        {
            reason = $"{ctx}.FoodSafetyThreshold {rule.FoodSafetyThreshold} 必须是有限数值（排 NaN/Infinity）";
            return false;
        }
        if (rule.FoodSafetyThreshold < -1000f || rule.FoodSafetyThreshold > 1000f)
        {
            reason = $"{ctx}.FoodSafetyThreshold {rule.FoodSafetyThreshold} 必须在 [-1000, 1000]";
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

    private static bool ValidateBranchRule(BranchRule rule, string ctx, out string reason)
    {
        if (rule.TargetPower < 0)
        { reason = $"{ctx}.TargetPower < 0"; return false; }
        if (rule.TargetPower > 100_000)
        { reason = $"{ctx}.TargetPower {rule.TargetPower} 超过上限 100000"; return false; }
        if (!IsRatio(rule.LowTierMinFraction))
        { reason = $"{ctx}.LowTierMinFraction {rule.LowTierMinFraction} 必须在 [0,1]"; return false; }
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
