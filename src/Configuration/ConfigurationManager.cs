using System;
using System.IO;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
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
    public const int CurrentConfigVersion = 22;

    private const string ModuleId = "SovereignTowns";
    private const string ConfigSubDir = "Configs";
    private const string ConfigFileName = "global.json";

    // 2026-05-29: RatioSumMin/Max 删除（rule 不再带 role 比例）。

    private static readonly object _gate = new object();
    private static GlobalConfig _current = GlobalConfig.CreateDefault();
    private static bool _initialized;
    private static string _lastValidationError = "";

    // 中英双语 reason 助手 —— 玩家可见的校验失败原因在游戏内控制面板弹窗中原样显示。
    // 语言探测合并到 SovereignTowns.Common.LanguageProbe（commit 2026-05-23 审计去重）。
    private static string Tr(string zh, string en) => SovereignTowns.Common.LanguageProbe.Tr(zh, en);

    /// <summary>
    /// GlobalDefaults 变更后触发。参数：被改的 settlement.StringId，或 null 表示全局/未知
    /// （订阅者需对所有 in-flight 队伍重规划）。永远从主线程触发 —— 由 ReplaceAndSave / TryReload 同步 invoke。
    /// </summary>
    public static event Action<string?>? OnConfigChanged;

    /// <summary>主线程同步触发 OnConfigChanged。</summary>
    internal static void RaiseConfigChanged(string? settlementId)
    {
        // 配置变更后先同步 Logger 等级（控制面板切 VerboseLogging 立即生效），再 fire 业务订阅。
        try { ApplyVerboseLoggingFromConfig(); }
        catch (Exception ex) { Logger.Warn($"ApplyVerboseLoggingFromConfig failed: {ex.Message}"); }

        var handlers = OnConfigChanged;
        if (handlers == null) return;

        foreach (Action<string?> handler in handlers.GetInvocationList())
        {
            try { handler(settlementId); }
            catch (Exception ex) { Logger.Warn($"OnConfigChanged handler '{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}' failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 把 GlobalConfig.EnabledFeatures.VerboseLogging 同步到 Logger.MinLevel。Initialize 末尾 + 每次
    /// OnConfigChanged 都调一次，因此控制面板切 verbose 立即生效，无需重启游戏。
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
            reason = Tr("校验抛出异常：", "validation threw: ") + ex.Message;
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
                string previousLastModified = _current.LastModified;
                _current.LastModified = DateTime.UtcNow.ToString("O");
                try
                {
                    WriteToDiskUnlocked(configPath, _current);
                }
                catch (Exception writeEx)
                {
                    _current.LastModified = previousLastModified;
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
            reason = Tr("newConfig 为 null", "newConfig is null");
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
                    reason = Tr("写入磁盘失败：", "WriteToDisk failed: ") + writeEx.Message;
                    Logger.Error("ReplaceAndSave: write failed; rolled back in-memory config", writeEx);
                    return false;
                }

                if (changed)
                {
                    Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}' (content changed → broadcasting OnConfigChanged)");
                }
                else
                {
                    Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}' (content identical → no broadcast)");
                }

                reason = "";
                // 落到 lock 外触发：订阅者会扫 MobileParty.All 等重活，不应延长 _gate 持有时间。
            }
            if (changed) RaiseConfigChanged(null);
            return true;
        }
        catch (Exception ex)
        {
            reason = Tr("ReplaceAndSave 抛出异常：", "ReplaceAndSave threw: ") + ex.Message;
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
                    reason = Tr("配置文件不存在：", "config file not found: ") + configPath;
                    Logger.Warn($"Reload requested but '{configPath}' does not exist; keeping current in-memory config");
                    return false;
                }

                var loaded = TryLoadFromDisk(configPath);
                if (loaded is null)
                {
                    reason = Tr("配置加载/解析/校验失败（详见日志）；保留之前的内存配置", "config load/parse/validation failed (see logs); kept previous in-memory config");
                    Logger.Warn("Reload failed; keeping previous in-memory config");
                    return false;
                }

                // P0-1 修复：JSON content-diff — 只有磁盘内容与内存不同才算"真变化"。
                changed = !ConfigsAreEqual(_current, loaded);
                _current = loaded;
                reason = "";

                if (changed)
                {
                    Logger.Info($"Config reloaded: version={_current.ConfigVersion} (content changed → broadcasting OnConfigChanged)");
                }
                else
                {
                    Logger.Info($"Config reloaded: version={_current.ConfigVersion} (content identical → no broadcast)");
                }
                // 落到 lock 外触发：见 ReplaceAndSave 同处注释。
            }
            if (changed) RaiseConfigChanged(null);
            return true;
        }
        catch (Exception ex)
        {
            reason = Tr("重新加载抛出异常：", "reload threw: ") + ex.Message;
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
    /// B7.2: 主配置路径在玩家文档目录，避开 Steam C:\ 写盘 UAC 提示。
    /// </summary>
    private static string GetConfigFilePath()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "SovereignTowns", ConfigFileName);
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
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.ClanPatrol ??= new ClanPatrolConfig();
            parsed.Thresholds ??= new PartyThresholds();
            parsed.BuildingBonus ??= new BuildingBonusConfig();
            parsed.FiscalAutonomy ??= new FiscalAutonomyConfig();
            parsed.LastModified ??= "";

            // B7.25：不再做版本迁移。版本不符即丢弃，由 Initialize() 兜底为默认。
            if (parsed.ConfigVersion != CurrentConfigVersion)
            {
                var template = new TextObject(
                    "{=ST_Msg_Config_VersionMismatch}[Sovereign Towns] global.json version mismatch (file v{FILE_VERSION}, expected v{EXPECTED_VERSION}) — reset to defaults; please reconfigure via the web control panel.");
                template.SetTextVariable("FILE_VERSION", parsed.ConfigVersion);
                template.SetTextVariable("EXPECTED_VERSION", CurrentConfigVersion);
                string msg = template.ToString();
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
            reason = Tr("GlobalDefaults 为 null", "GlobalDefaults is null");
            return false;
        }
        if (!ValidateRule(config.GlobalDefaults, "GlobalDefaults", out reason))
        {
            return false;
        }

        if (config.VillageCooldownHours < 12 || config.VillageCooldownHours > 240)
        {
            reason = Tr($"VillageCooldownHours 非法 ({config.VillageCooldownHours})；范围 [12, 240]", $"VillageCooldownHours invalid ({config.VillageCooldownHours}); [12, 240]");
            return false;
        }
        if (config.Thresholds != null && !ValidateThresholds(config.Thresholds, out reason))
        {
            return false;
        }
        // Issue：ClanPatrol 之前缺校验，负数 / NaN 会进 scheduler。
        if (config.ClanPatrol != null && !ValidateClanPatrol(config.ClanPatrol, out reason))
        {
            return false;
        }
        if (config.BuildingBonus != null && !ValidateBuildingBonus(config.BuildingBonus, out reason))
        {
            return false;
        }
        if (config.FiscalAutonomy != null && !ValidateFiscalAutonomy(config.FiscalAutonomy, out reason))
        {
            return false;
        }

        reason = "";
        return true;
    }

    private static bool ValidateClanPatrol(ClanPatrolConfig c, out string reason)
    {
        if (!IsNonNegativeFloat(c.EtaBufferHours) || c.EtaBufferHours > 168f)
        { reason = Tr($"ClanPatrol.EtaBufferHours 非法 ({c.EtaBufferHours})；范围 [0, 168]", $"ClanPatrol.EtaBufferHours invalid ({c.EtaBufferHours}); [0, 168]"); return false; }
        if (!IsNonNegativeFloat(c.StuckTimeoutHours) || c.StuckTimeoutHours > 720f || c.StuckTimeoutHours < 1f)
        { reason = Tr($"ClanPatrol.StuckTimeoutHours 非法 ({c.StuckTimeoutHours})；范围 [1, 720]", $"ClanPatrol.StuckTimeoutHours invalid ({c.StuckTimeoutHours}); [1, 720]"); return false; }
        if (!IsNonNegativeFloat(c.MinVisitGapHours) || c.MinVisitGapHours > 720f)
        { reason = Tr($"ClanPatrol.MinVisitGapHours 非法 ({c.MinVisitGapHours})；范围 [0, 720]", $"ClanPatrol.MinVisitGapHours invalid ({c.MinVisitGapHours}); [0, 720]"); return false; }
        if (!IsNonNegativeFloat(c.DistanceWeightHoursPerTile) || c.DistanceWeightHoursPerTile > 100f)
        { reason = Tr($"ClanPatrol.DistanceWeightHoursPerTile 非法 ({c.DistanceWeightHoursPerTile})；范围 [0, 100]", $"ClanPatrol.DistanceWeightHoursPerTile invalid ({c.DistanceWeightHoursPerTile}); [0, 100]"); return false; }
        if (!IsNonNegativeFloat(c.SupportEtaThresholdHours) || c.SupportEtaThresholdHours > 168f)
        { reason = Tr($"ClanPatrol.SupportEtaThresholdHours 非法 ({c.SupportEtaThresholdHours})；范围 [0, 168]", $"ClanPatrol.SupportEtaThresholdHours invalid ({c.SupportEtaThresholdHours}); [0, 168]"); return false; }
        reason = "";
        return true;
    }

    private static bool ValidateBuildingBonus(BuildingBonusConfig b, out string reason)
    {
        foreach (var (name, val, lo, hi) in new (string, int, int, int)[]
        {
            ("RecruiterBaseCap",             b.RecruiterBaseCap,             1, 10),
            ("RecruiterCapPerBarracksLevel", b.RecruiterCapPerBarracksLevel, 0, 5),
            ("TransferBaseCap",              b.TransferBaseCap,              1, 10),
            ("TransferCapPerBarracksLevel",  b.TransferCapPerBarracksLevel,  0, 5),
            ("SallyBaseCap",                 b.SallyBaseCap,                 1, 10),
            ("SallyCapPerBarracksLevel",     b.SallyCapPerBarracksLevel,     0, 5),
            ("PatrolBaseCap",                b.PatrolBaseCap,                1, 10),
            ("PatrolCapPerGuardHouseLevel",  b.PatrolCapPerGuardHouseLevel,  0, 5),
            ("GarrisonXpBasePerDay",         b.GarrisonXpBasePerDay,         0, 50),
            ("GarrisonXpPerBarracksLevel",   b.GarrisonXpPerBarracksLevel,   0, 50),
        })
        {
            if (val < lo || val > hi)
            {
                reason = Tr($"BuildingBonus.{name} 非法 ({val})；范围 [{lo}, {hi}]", $"BuildingBonus.{name} invalid ({val}); [{lo}, {hi}]");
                return false;
            }
        }
        reason = "";
        return true;
    }

    private static bool ValidateFiscalAutonomy(FiscalAutonomyConfig f, out string reason)
    {
        if (!IsFiniteFloat(f.GarrisonWageBudgetRatio) || f.GarrisonWageBudgetRatio < 0.1f || f.GarrisonWageBudgetRatio > 1.0f)
        { reason = Tr($"FiscalAutonomy.GarrisonWageBudgetRatio 非法 ({f.GarrisonWageBudgetRatio})；范围 [0.1, 1.0]", $"FiscalAutonomy.GarrisonWageBudgetRatio invalid ({f.GarrisonWageBudgetRatio}); [0.1, 1.0]"); return false; }
        if (!IsFiniteFloat(f.DisbandExcessThreshold) || f.DisbandExcessThreshold < 1.0f || f.DisbandExcessThreshold > 3.0f)
        { reason = Tr($"FiscalAutonomy.DisbandExcessThreshold 非法 ({f.DisbandExcessThreshold})；范围 [1.0, 3.0]", $"FiscalAutonomy.DisbandExcessThreshold invalid ({f.DisbandExcessThreshold}); [1.0, 3.0]"); return false; }

        if (f.PatrolValue < 0 || f.PatrolValue > 5000)
        { reason = Tr($"FiscalAutonomy.PatrolValue 非法 ({f.PatrolValue})；范围 [0, 5000]", $"FiscalAutonomy.PatrolValue invalid ({f.PatrolValue}); [0, 5000]"); return false; }
        if (f.DisbandPerDayCap < 0 || f.DisbandPerDayCap > 200)
        { reason = Tr($"FiscalAutonomy.DisbandPerDayCap 非法 ({f.DisbandPerDayCap})；范围 [0, 200]", $"FiscalAutonomy.DisbandPerDayCap invalid ({f.DisbandPerDayCap}); [0, 200]"); return false; }
        if (f.BypassOverflowPenalty < 0 || f.BypassOverflowPenalty > 10000)
        { reason = Tr($"FiscalAutonomy.BypassOverflowPenalty 非法 ({f.BypassOverflowPenalty})；范围 [0, 10000]", $"FiscalAutonomy.BypassOverflowPenalty invalid ({f.BypassOverflowPenalty}); [0, 10000]"); return false; }
        if (f.HorizonTicks < 1 || f.HorizonTicks > 64)
        { reason = Tr($"FiscalAutonomy.HorizonTicks 非法 ({f.HorizonTicks})；范围 [1, 64]", $"FiscalAutonomy.HorizonTicks invalid ({f.HorizonTicks}); [1, 64]"); return false; }
if (!IsFiniteFloat(f.ThreatForecastScanRadius) || f.ThreatForecastScanRadius < 0f || f.ThreatForecastScanRadius > 500f)
        { reason = Tr($"FiscalAutonomy.ThreatForecastScanRadius 非法 ({f.ThreatForecastScanRadius})；范围 [0, 500]", $"FiscalAutonomy.ThreatForecastScanRadius invalid ({f.ThreatForecastScanRadius}); [0, 500]"); return false; }
        if (f.CapitalLogisticsTickHours < 1 || f.CapitalLogisticsTickHours > 24)
        { reason = Tr($"FiscalAutonomy.CapitalLogisticsTickHours 非法 ({f.CapitalLogisticsTickHours})；范围 [1, 24]", $"FiscalAutonomy.CapitalLogisticsTickHours invalid ({f.CapitalLogisticsTickHours}); [1, 24]"); return false; }
        if (!IsFiniteFloat(f.DispatchRiskScanRadius) || f.DispatchRiskScanRadius < 0f || f.DispatchRiskScanRadius > 300f)
        { reason = Tr($"FiscalAutonomy.DispatchRiskScanRadius 非法 ({f.DispatchRiskScanRadius})；范围 [0, 300]", $"FiscalAutonomy.DispatchRiskScanRadius invalid ({f.DispatchRiskScanRadius}); [0, 300]"); return false; }
        if (!IsFiniteFloat(f.DispatchRiskVetoThreshold) || f.DispatchRiskVetoThreshold < 0f || f.DispatchRiskVetoThreshold > 500f)
        { reason = Tr($"FiscalAutonomy.DispatchRiskVetoThreshold 非法 ({f.DispatchRiskVetoThreshold})；范围 [0, 500]", $"FiscalAutonomy.DispatchRiskVetoThreshold invalid ({f.DispatchRiskVetoThreshold}); [0, 500]"); return false; }

        // ── value 函数曲线 tunables ──
        foreach (var (name, val) in new (string, float)[]
        {
            ("ThreatWeightSafe", f.ThreatWeightSafe),
            ("ThreatWeightLow", f.ThreatWeightLow),
            ("ThreatWeightMedium", f.ThreatWeightMedium),
            ("ThreatWeightHigh", f.ThreatWeightHigh),
            ("ThreatWeightCritical", f.ThreatWeightCritical),
        })
        {
            if (!IsFiniteFloat(val) || val < 0f || val > 8f)
            { reason = Tr($"FiscalAutonomy.{name} 非法 ({val})；范围 [0, 8]", $"FiscalAutonomy.{name} invalid ({val}); [0, 8]"); return false; }
        }
        if (!IsFiniteFloat(f.ProsperityNormalizer) || f.ProsperityNormalizer < 500f || f.ProsperityNormalizer > 20000f)
        { reason = Tr($"FiscalAutonomy.ProsperityNormalizer 非法 ({f.ProsperityNormalizer})；范围 [500, 20000]", $"FiscalAutonomy.ProsperityNormalizer invalid ({f.ProsperityNormalizer}); [500, 20000]"); return false; }
        if (!IsFiniteFloat(f.CapitalStrategicBonus) || f.CapitalStrategicBonus < 1f || f.CapitalStrategicBonus > 3f)
        { reason = Tr($"FiscalAutonomy.CapitalStrategicBonus 非法 ({f.CapitalStrategicBonus})；范围 [1, 3]", $"FiscalAutonomy.CapitalStrategicBonus invalid ({f.CapitalStrategicBonus}); [1, 3]"); return false; }
        // ── PR-3 (2026-05-24): EdgeCost 4 通道权重校验 ──
        // TickHoldingValueK 上限 33M = int.MaxValue / 64（HorizonTicks clamp 上限）。
        // 下限 1M 防止 K 太小让 strategic 通道把 cost 推到负无穷,SSP 找不到最短路。
        if (f.TickHoldingValueK < 1_000_000 || f.TickHoldingValueK > 33_000_000)
        { reason = Tr($"FiscalAutonomy.TickHoldingValueK 非法 ({f.TickHoldingValueK})；范围 [1_000_000, 33_000_000]", $"FiscalAutonomy.TickHoldingValueK invalid ({f.TickHoldingValueK}); [1_000_000, 33_000_000]"); return false; }
        // CostWeight* 通道权重：0 = 关闭该通道；10 = 强烈偏重（实战极少超 3）。
        if (f.CostWeightGold < 0 || f.CostWeightGold > 10)
        { reason = Tr($"FiscalAutonomy.CostWeightGold 非法 ({f.CostWeightGold})；范围 [0, 10]", $"FiscalAutonomy.CostWeightGold invalid ({f.CostWeightGold}); [0, 10]"); return false; }
        if (f.CostWeightRisk < 0 || f.CostWeightRisk > 10)
        { reason = Tr($"FiscalAutonomy.CostWeightRisk 非法 ({f.CostWeightRisk})；范围 [0, 10]", $"FiscalAutonomy.CostWeightRisk invalid ({f.CostWeightRisk}); [0, 10]"); return false; }
        if (f.CostWeightStrategic < 0 || f.CostWeightStrategic > 10)
        { reason = Tr($"FiscalAutonomy.CostWeightStrategic 非法 ({f.CostWeightStrategic})；范围 [0, 10]", $"FiscalAutonomy.CostWeightStrategic invalid ({f.CostWeightStrategic}); [0, 10]"); return false; }
        if (f.DispatchRiskCostScale < 0 || f.DispatchRiskCostScale > 200)
        { reason = Tr($"FiscalAutonomy.DispatchRiskCostScale 非法 ({f.DispatchRiskCostScale})；范围 [0, 200]", $"FiscalAutonomy.DispatchRiskCostScale invalid ({f.DispatchRiskCostScale}); [0, 200]"); return false; }
        // ── PR-4 (2026-05-24): S6 Sally 校验 ──
        if (f.SallyValueBase < 0 || f.SallyValueBase > 50_000)
        { reason = Tr($"FiscalAutonomy.SallyValueBase 非法 ({f.SallyValueBase})；范围 [0, 50000]", $"FiscalAutonomy.SallyValueBase invalid ({f.SallyValueBase}); [0, 50000]"); return false; }
        if (!IsFiniteFloat(f.CapitalSallyBonus) || f.CapitalSallyBonus < 1f || f.CapitalSallyBonus > 5f)
        { reason = Tr($"FiscalAutonomy.CapitalSallyBonus 非法 ({f.CapitalSallyBonus})；范围 [1, 5]", $"FiscalAutonomy.CapitalSallyBonus invalid ({f.CapitalSallyBonus}); [1, 5]"); return false; }
        if (f.SallyTeamSize < 5 || f.SallyTeamSize > 200)
        { reason = Tr($"FiscalAutonomy.SallyTeamSize 非法 ({f.SallyTeamSize})；范围 [5, 200]", $"FiscalAutonomy.SallyTeamSize invalid ({f.SallyTeamSize}); [5, 200]"); return false; }
        if (f.MaxSallyPartiesPerCity < 0 || f.MaxSallyPartiesPerCity > 5)
        { reason = Tr($"FiscalAutonomy.MaxSallyPartiesPerCity 非法 ({f.MaxSallyPartiesPerCity})；范围 [0, 5]", $"FiscalAutonomy.MaxSallyPartiesPerCity invalid ({f.MaxSallyPartiesPerCity}); [0, 5]"); return false; }
        // ── PR-5' (2026-05-24) 新字段校验 ──
        // 2026-05-28: TargetFraction 删除,其作用被 perCityCapacity 公式取代(见 FiscalAutonomyConfig 注释)。
        if (!IsFiniteFloat(f.TownStrategicBonus) || f.TownStrategicBonus < 1f || f.TownStrategicBonus > 3f)
        { reason = Tr($"FiscalAutonomy.TownStrategicBonus 非法 ({f.TownStrategicBonus})；范围 [1, 3]", $"FiscalAutonomy.TownStrategicBonus invalid ({f.TownStrategicBonus}); [1, 3]"); return false; }
        if (f.BaseValuePerTier < 0 || f.BaseValuePerTier > 10000)
        { reason = Tr($"FiscalAutonomy.BaseValuePerTier 非法 ({f.BaseValuePerTier})；范围 [0, 10000]", $"FiscalAutonomy.BaseValuePerTier invalid ({f.BaseValuePerTier}); [0, 10000]"); return false; }
        // ── 卫队容量上限 + 价值常数 + 招募模板校验 ──
        if (f.HonorGuardCap < 0 || f.HonorGuardCap > 1000)
        { reason = Tr($"FiscalAutonomy.HonorGuardCap 非法 ({f.HonorGuardCap})；范围 [0, 1000]（0 = 关闭卫队）", $"FiscalAutonomy.HonorGuardCap invalid ({f.HonorGuardCap}); [0, 1000] (0 = disable honor guard)"); return false; }
        if (f.HonorGuardValueBase < 0 || f.HonorGuardValueBase > 50_000)
        { reason = Tr($"FiscalAutonomy.HonorGuardValueBase 非法 ({f.HonorGuardValueBase})；范围 [0, 50000]", $"FiscalAutonomy.HonorGuardValueBase invalid ({f.HonorGuardValueBase}); [0, 50000]"); return false; }
        if (f.HgDistanceGoldPerTick < 0 || f.HgDistanceGoldPerTick > 10000)
        { reason = Tr($"FiscalAutonomy.HgDistanceGoldPerTick 非法 ({f.HgDistanceGoldPerTick})；范围 [0, 10000]", $"FiscalAutonomy.HgDistanceGoldPerTick invalid ({f.HgDistanceGoldPerTick}); [0, 10000]"); return false; }
        if (f.HonorGuardTemplate != null)
        {
            if (f.HonorGuardTemplate.Count > 50)
            { reason = Tr($"FiscalAutonomy.HonorGuardTemplate 条目过多 ({f.HonorGuardTemplate.Count})；上限 50", $"FiscalAutonomy.HonorGuardTemplate too many entries ({f.HonorGuardTemplate.Count}); max 50"); return false; }
            foreach (var kv in f.HonorGuardTemplate)
            {
                if (string.IsNullOrEmpty(kv.Key))
                { reason = Tr("FiscalAutonomy.HonorGuardTemplate 存在空 key", "FiscalAutonomy.HonorGuardTemplate has an empty key"); return false; }
                if (kv.Value < 0 || kv.Value > 100)
                { reason = Tr($"FiscalAutonomy.HonorGuardTemplate['{kv.Key}'] 非法 ({kv.Value})；范围 [0, 100]", $"FiscalAutonomy.HonorGuardTemplate['{kv.Key}'] invalid ({kv.Value}); [0, 100]"); return false; }
            }
        }

        // ── 跨字段不变量：K × T + 边际贡献不得溢出 int ──
        // TickHoldingValueK × HorizonTicks 是最大单项；gold/risk 边际 ≤ ~200K；
        // 给 int.MaxValue 留 100M 余量以覆盖所有 per-edge 加项。
        long kTimesT = (long)f.TickHoldingValueK * f.HorizonTicks;
        if (kTimesT > int.MaxValue - 100_000_000L)
        { reason = Tr($"FiscalAutonomy 跨字段溢出: TickHoldingValueK({f.TickHoldingValueK}) × HorizonTicks({f.HorizonTicks}) = {kTimesT}，超出安全范围", $"FiscalAutonomy cross-field overflow: TickHoldingValueK({f.TickHoldingValueK}) × HorizonTicks({f.HorizonTicks}) = {kTimesT}, exceeds safe range"); return false; }

        // strategic 通道峰值：覆盖 holding / patrol / sally / honor-guard 的一 tick 最坏公共边。
        // 实际建图还会把负 public cost clamp 到 0；这里用于提前拦截明显失衡的配置。
        double maxStrategicWeight = (double)f.CapitalStrategicBonus * f.TownStrategicBonus * 1.5d; // prosperity_factor max
        const double maxPower = 2.56d;
        double holdStrategic = f.BaseValuePerTier * f.ThreatWeightCritical * maxStrategicWeight * maxPower;
        double sallyStrategic = f.SallyValueBase * f.CapitalSallyBonus * f.ThreatWeightCritical * maxStrategicWeight * maxPower;
        double patrolStrategic = Math.Min(f.PatrolValue, Math.Max(0, f.TickHoldingValueK - 1)) * maxPower;
        double honorGuardStrategic = f.HonorGuardValueBase * maxPower;
        double maxStrategicPerTick = Math.Max(Math.Max(holdStrategic, sallyStrategic), Math.Max(patrolStrategic, honorGuardStrategic));
        double weightedStrategic = f.CostWeightStrategic * maxStrategicPerTick;
        if (weightedStrategic > f.TickHoldingValueK)
        { reason = Tr($"FiscalAutonomy 跨字段失衡: weighted strategic 峰值({weightedStrategic:F0}) > TickHoldingValueK({f.TickHoldingValueK})，会被调度器钳到 0 cost", $"FiscalAutonomy cross-field imbalance: weighted strategic peak ({weightedStrategic:F0}) > TickHoldingValueK({f.TickHoldingValueK}); scheduler would clamp it to 0 cost"); return false; }

        reason = "";
        return true;
    }

    private static bool ValidateThresholds(PartyThresholds t, out string reason)
    {
        if (!IsRatio(t.PartyReturnSizeRatio))
        { reason = Tr($"Thresholds.PartyReturnSizeRatio 非法 ({t.PartyReturnSizeRatio})；必须在 [0,1] 区间", $"Thresholds.PartyReturnSizeRatio invalid ({t.PartyReturnSizeRatio}); must be in [0,1]"); return false; }
        if (!IsRatio(t.PartyReturnWoundedRatio))
        { reason = Tr($"Thresholds.PartyReturnWoundedRatio 非法 ({t.PartyReturnWoundedRatio})；必须在 [0,1] 区间", $"Thresholds.PartyReturnWoundedRatio invalid ({t.PartyReturnWoundedRatio}); must be in [0,1]"); return false; }
        if (!IsRatio(t.PatrolTroopBatchRatio))
        { reason = Tr($"Thresholds.PatrolTroopBatchRatio 非法 ({t.PatrolTroopBatchRatio})；必须在 [0,1] 区间", $"Thresholds.PatrolTroopBatchRatio invalid ({t.PatrolTroopBatchRatio}); must be in [0,1]"); return false; }
        if (!IsRatio(t.RecruiterEscortRatio))
        { reason = Tr($"Thresholds.RecruiterEscortRatio 非法 ({t.RecruiterEscortRatio})；必须在 [0,1] 区间", $"Thresholds.RecruiterEscortRatio invalid ({t.RecruiterEscortRatio}); must be in [0,1]"); return false; }
        if (t.RecruiterReturnRecruitedCount < 1)
        { reason = Tr($"Thresholds.RecruiterReturnRecruitedCount 非法 ({t.RecruiterReturnRecruitedCount})；必须 >= 1", $"Thresholds.RecruiterReturnRecruitedCount invalid ({t.RecruiterReturnRecruitedCount}); must be >= 1"); return false; }
        if (t.RecruiterReturnRecruitedCount > 1000)
        { reason = Tr($"Thresholds.RecruiterReturnRecruitedCount {t.RecruiterReturnRecruitedCount} 超过上限 1000", $"Thresholds.RecruiterReturnRecruitedCount {t.RecruiterReturnRecruitedCount} exceeds upper bound 1000"); return false; }
        if (!IsRatio(t.TransferMaxTroopsPerTaskRatio))
        { reason = Tr($"Thresholds.TransferMaxTroopsPerTaskRatio 非法 ({t.TransferMaxTroopsPerTaskRatio})；必须在 [0,1] 区间", $"Thresholds.TransferMaxTroopsPerTaskRatio invalid ({t.TransferMaxTroopsPerTaskRatio}); must be in [0,1]"); return false; }
        if (!IsRatio(t.RecruitmentMinDemandRatio))
        { reason = Tr($"Thresholds.RecruitmentMinDemandRatio 非法 ({t.RecruitmentMinDemandRatio})；必须在 [0,1] 区间", $"Thresholds.RecruitmentMinDemandRatio invalid ({t.RecruitmentMinDemandRatio}); must be in [0,1]"); return false; }
        if (!IsRatio(t.SallyExtractionRatio))
        { reason = Tr($"Thresholds.SallyExtractionRatio 非法 ({t.SallyExtractionRatio})；必须在 [0,1] 区间", $"Thresholds.SallyExtractionRatio invalid ({t.SallyExtractionRatio}); must be in [0,1]"); return false; }
        if (!IsNonNegativeFloat(t.SallyTargetPartySizeMultiplier))
        { reason = Tr($"Thresholds.SallyTargetPartySizeMultiplier 非法 ({t.SallyTargetPartySizeMultiplier})", $"Thresholds.SallyTargetPartySizeMultiplier invalid ({t.SallyTargetPartySizeMultiplier})"); return false; }
        // C (DeepSeek audit 2026-05-18)：与前端 thresholdSpecs max=5 对齐。原 100 上限远超合理范围。
        if (t.SallyTargetPartySizeMultiplier > 5f)
        { reason = Tr($"Thresholds.SallyTargetPartySizeMultiplier {t.SallyTargetPartySizeMultiplier} 超过上限 5", $"Thresholds.SallyTargetPartySizeMultiplier {t.SallyTargetPartySizeMultiplier} exceeds upper bound 5"); return false; }
        if (t.SallyCreateMinPartyCount < 1)
        { reason = Tr($"Thresholds.SallyCreateMinPartyCount 非法 ({t.SallyCreateMinPartyCount})；必须 >= 1", $"Thresholds.SallyCreateMinPartyCount invalid ({t.SallyCreateMinPartyCount}); must be >= 1"); return false; }
        if (t.SallyCreateMinPartyCount > 1000)
        { reason = Tr($"Thresholds.SallyCreateMinPartyCount {t.SallyCreateMinPartyCount} 超过上限 1000", $"Thresholds.SallyCreateMinPartyCount {t.SallyCreateMinPartyCount} exceeds upper bound 1000"); return false; }

        // Issue #5：B17.4 新增阈值校验。
        if (t.RecruiterMinHomeGarrison < 0)
        { reason = Tr($"Thresholds.RecruiterMinHomeGarrison 非法 ({t.RecruiterMinHomeGarrison})；必须 >= 0", $"Thresholds.RecruiterMinHomeGarrison invalid ({t.RecruiterMinHomeGarrison}); must be >= 0"); return false; }
        if (t.RecruiterMinHomeGarrison > 500)
        { reason = Tr($"Thresholds.RecruiterMinHomeGarrison {t.RecruiterMinHomeGarrison} 超过上限 500", $"Thresholds.RecruiterMinHomeGarrison {t.RecruiterMinHomeGarrison} exceeds upper bound 500"); return false; }
        if (t.PartyPrisonerCap < 0)
        { reason = Tr($"Thresholds.PartyPrisonerCap 非法 ({t.PartyPrisonerCap})；必须 >= 0", $"Thresholds.PartyPrisonerCap invalid ({t.PartyPrisonerCap}); must be >= 0"); return false; }
        if (t.PartyPrisonerCap > 500)
        { reason = Tr($"Thresholds.PartyPrisonerCap {t.PartyPrisonerCap} 超过上限 500", $"Thresholds.PartyPrisonerCap {t.PartyPrisonerCap} exceeds upper bound 500"); return false; }
        if (!IsNonNegativeFloat(t.StuckTeleportHours))
        { reason = Tr($"Thresholds.StuckTeleportHours 非法 ({t.StuckTeleportHours})；必须 >= 0", $"Thresholds.StuckTeleportHours invalid ({t.StuckTeleportHours}); must be >= 0"); return false; }
        if (t.StuckTeleportHours > 168f)
        { reason = Tr($"Thresholds.StuckTeleportHours {t.StuckTeleportHours} 超过上限 168", $"Thresholds.StuckTeleportHours {t.StuckTeleportHours} exceeds upper bound 168"); return false; }
        if (!IsNonNegativeFloat(t.PatrolMaxLifetimeHours) || t.PatrolMaxLifetimeHours > 720f)
        { reason = Tr($"Thresholds.PatrolMaxLifetimeHours 非法 ({t.PatrolMaxLifetimeHours})；范围 [0, 720]", $"Thresholds.PatrolMaxLifetimeHours invalid ({t.PatrolMaxLifetimeHours}); [0, 720]"); return false; }
        if (t.PatrolMinDispatchSize < 0)
        { reason = Tr($"Thresholds.PatrolMinDispatchSize 非法 ({t.PatrolMinDispatchSize})；必须 >= 0", $"Thresholds.PatrolMinDispatchSize invalid ({t.PatrolMinDispatchSize}); must be >= 0"); return false; }
        if (t.PatrolMinDispatchSize > 500)
        { reason = Tr($"Thresholds.PatrolMinDispatchSize {t.PatrolMinDispatchSize} 超过上限 500", $"Thresholds.PatrolMinDispatchSize {t.PatrolMinDispatchSize} exceeds upper bound 500"); return false; }

        // DeepSeek audit 2026-05-18 新增字段校验
        if (!IsNonNegativeFloat(t.IdleHoursBeforeForceReturn) || t.IdleHoursBeforeForceReturn < 1f || t.IdleHoursBeforeForceReturn > 720f)
        { reason = Tr($"Thresholds.IdleHoursBeforeForceReturn 非法 ({t.IdleHoursBeforeForceReturn})；范围 [1, 720]", $"Thresholds.IdleHoursBeforeForceReturn invalid ({t.IdleHoursBeforeForceReturn}); [1, 720]"); return false; }
        if (!IsNonNegativeFloat(t.IdleHoursBeforeDisband) || t.IdleHoursBeforeDisband < 1f || t.IdleHoursBeforeDisband > 720f)
        { reason = Tr($"Thresholds.IdleHoursBeforeDisband 非法 ({t.IdleHoursBeforeDisband})；范围 [1, 720]", $"Thresholds.IdleHoursBeforeDisband invalid ({t.IdleHoursBeforeDisband}); [1, 720]"); return false; }
        if (t.IdleHoursBeforeDisband < t.IdleHoursBeforeForceReturn)
        { reason = Tr($"Thresholds.IdleHoursBeforeDisband ({t.IdleHoursBeforeDisband}) 必须 ≥ IdleHoursBeforeForceReturn ({t.IdleHoursBeforeForceReturn})", $"Thresholds.IdleHoursBeforeDisband ({t.IdleHoursBeforeDisband}) must be >= IdleHoursBeforeForceReturn ({t.IdleHoursBeforeForceReturn})"); return false; }
        if (!IsNonNegativeFloat(t.SallyDetectionRadius) || t.SallyDetectionRadius < 10f || t.SallyDetectionRadius > 500f)
        { reason = Tr($"Thresholds.SallyDetectionRadius 非法 ({t.SallyDetectionRadius})；范围 [10, 500]", $"Thresholds.SallyDetectionRadius invalid ({t.SallyDetectionRadius}); [10, 500]"); return false; }
        if (!IsNonNegativeFloat(t.SallyCooldownHours) || t.SallyCooldownHours > 168f)
        { reason = Tr($"Thresholds.SallyCooldownHours 非法 ({t.SallyCooldownHours})；范围 [0, 168]", $"Thresholds.SallyCooldownHours invalid ({t.SallyCooldownHours}); [0, 168]"); return false; }
        // 2026-05-28:P1-5 SallyMinSustainedTicks validation deleted — field removed.
        // 2026-05-24:AutoUpgrade* 三字段已删除,vanilla 接管升级,不再需要 mod 端 validation。
        // T1 重整 2026-05-18：seed gold 统一到 StPartyComponent.DefaultSeedGold，删除 RecruiterSeedGold/SallySeedGold 字段及其验证。
        if (t.RecruiterVillageCandidateCap < 4 || t.RecruiterVillageCandidateCap > 300)
        { reason = Tr($"Thresholds.RecruiterVillageCandidateCap 非法 ({t.RecruiterVillageCandidateCap})；范围 [4, 300]", $"Thresholds.RecruiterVillageCandidateCap invalid ({t.RecruiterVillageCandidateCap}); [4, 300]"); return false; }
        if (t.McmfRecruiterOverhead < 0 || t.McmfRecruiterOverhead > 10000)
        { reason = Tr($"Thresholds.McmfRecruiterOverhead 非法 ({t.McmfRecruiterOverhead})；范围 [0, 10000]", $"Thresholds.McmfRecruiterOverhead invalid ({t.McmfRecruiterOverhead}); [0, 10000]"); return false; }
        if (t.McmfTransferOverhead < 0 || t.McmfTransferOverhead > 10000)
        { reason = Tr($"Thresholds.McmfTransferOverhead 非法 ({t.McmfTransferOverhead})；范围 [0, 10000]", $"Thresholds.McmfTransferOverhead invalid ({t.McmfTransferOverhead}); [0, 10000]"); return false; }

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
        // 2026-05-28：TargetTotalCount / WartimeMultiplier / PeacetimeMultiplier 字段已删除，校验同步删除。
        //   目标人数 → MCMF perCityCapacity 公式
        //   战时强度 → FiscalAutonomyConfig.ThreatWeightX
        // PR-5'(2026-05-24)：ExactTroopTemplate / UseGenericMatching / MinTier / MaxTier 已删除。
        // 所有 settlement 使用通用匹配，tier 由 MCMF solver 自由选择。
        if (!IsRatio(rule.MinimumDefenderRatio))
        {
            reason = Tr($"{ctx}.MinimumDefenderRatio 非法 ({rule.MinimumDefenderRatio})；必须在 [0,1] 区间", $"{ctx}.MinimumDefenderRatio invalid ({rule.MinimumDefenderRatio}); must be in [0,1]");
            return false;
        }
        if (rule.BudgetLimit < 0)
        {
            reason = Tr($"{ctx}.BudgetLimit 小于 0", $"{ctx}.BudgetLimit < 0");
            return false;
        }
        if (rule.BudgetLimit > 10_000_000)
        {
            reason = Tr($"{ctx}.BudgetLimit {rule.BudgetLimit} 超过上限 10000000", $"{ctx}.BudgetLimit {rule.BudgetLimit} exceeds upper bound 10000000");
            return false;
        }
        if (!IsFiniteFloat(rule.FoodSafetyThreshold))
        {
            reason = Tr($"{ctx}.FoodSafetyThreshold {rule.FoodSafetyThreshold} 必须是有限数值（排 NaN/Infinity）", $"{ctx}.FoodSafetyThreshold {rule.FoodSafetyThreshold} must be a finite number (no NaN/Infinity)");
            return false;
        }
        if (rule.FoodSafetyThreshold < -1000f || rule.FoodSafetyThreshold > 1000f)
        {
            reason = Tr($"{ctx}.FoodSafetyThreshold {rule.FoodSafetyThreshold} 必须在 [-1000, 1000]", $"{ctx}.FoodSafetyThreshold {rule.FoodSafetyThreshold} must be in [-1000, 1000]");
            return false;
        }
        // 2026-05-29: ratio range + sum 校验删除。rule 不再带 role 比例。

        reason = "";
        return true;
    }

    // PR-5'(2026-05-24): ValidateBranchRule 已删除，BranchRule 整体移除。
    // 2026-05-29: ValidateRatio 删除（rule 不再带 role 比例）。

}
