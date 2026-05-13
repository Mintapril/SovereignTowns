using System;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Integration;

/// <summary>
/// MCM v5 soft-dependency integration via reflection.
///
/// 本 Mod 不直接引用 MCM / ButterLib / UIExtenderEx 程序集 — 全部通过 <see cref="Type.GetType(string)"/>
/// 字符串探测。当依赖链完整时启用 MCM 设置界面，否则静默退化为配置文件模式。
///
/// 当前 MVP 阶段仅做"可用性检测 + 诊断"；完整设置注册推迟到 MVP 5.1
/// （等用户装好 ButterLib + UIExtenderEx 之后）。
/// </summary>
public static class MCMIntegration
{
    private static bool? _availableCache;
    private static string _lastError = "(uninitialized)";

    /// <summary>检测 MCM v5 是否可用（关键类型存在 + 可加载）。结果缓存。</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_availableCache.HasValue) return _availableCache.Value;
            _availableCache = DetectAvailability();
            return _availableCache.Value;
        }
    }

    /// <summary>启动期尝试注册本 Mod 的配置到 MCM。失败安静返回。</summary>
    public static void TryRegister()
    {
        try
        {
            if (!IsAvailable)
            {
                Logger.Info($"MCMIntegration: skipped registration - {_lastError}");
                return;
            }

            // MCM 注册逻辑：通过反射构造 AttributeGlobalSettings<T> 实例并调用其注册方法。
            // 由于完整反射注册非常繁琐 + 本 Mod 当前环境 MCM 不可用，MVP 5 阶段只做"可用性检测"。
            // 实际注册推迟到 ButterLib/UIExtenderEx 装好后 — 此时用户能看到本提示。
            Logger.Info("MCMIntegration: MCM available, full settings registration is reserved for MVP 5.1");
        }
        catch (Exception ex)
        {
            Logger.Error("MCMIntegration.TryRegister failed (swallowed)", ex);
        }
    }

    /// <summary>详细诊断信息（给日志用）。</summary>
    public static string GetDiagnosticInfo()
    {
        return $"available={IsAvailable} lastError={_lastError}";
    }

    private static bool DetectAvailability()
    {
        try
        {
            var mcmSubModule = Type.GetType("MCM.MCMSubModule, MCMv5");
            var butterLib    = Type.GetType("Bannerlord.ButterLib.ButterLibSubModule, Bannerlord.ButterLib");
            var uiExt        = Type.GetType("Bannerlord.UIExtenderEx.UIExtender, Bannerlord.UIExtenderEx");

            if (mcmSubModule == null) { _lastError = "MCMv5 not loaded";              return false; }
            if (butterLib    == null) { _lastError = "Bannerlord.ButterLib not loaded"; return false; }
            if (uiExt        == null) { _lastError = "Bannerlord.UIExtenderEx not loaded"; return false; }

            _lastError = "(available)";
            return true;
        }
        catch (Exception ex)
        {
            _lastError = "Detection threw: " + ex.Message;
            return false;
        }
    }
}
