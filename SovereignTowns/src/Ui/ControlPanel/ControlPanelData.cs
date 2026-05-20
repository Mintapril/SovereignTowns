using System;
using Newtonsoft.Json;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.WebConfig;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 游戏内控制面板的数据适配层。与配置系统同进程，直接调 ConfigurationManager /
/// TroopDumper / ModExpenseLedger，不经 WebConfig 的 HTTP+JSON。
/// </summary>
internal static class ControlPanelData
{
    /// <summary>深拷贝当前配置成一份工作副本（面板在副本上改，保存时整体写回）。</summary>
    public static GlobalConfig CloneCurrentConfig()
    {
        var current = ConfigurationManager.Current;
        string json = JsonConvert.SerializeObject(current);
        return JsonConvert.DeserializeObject<GlobalConfig>(json)
               ?? throw new InvalidOperationException("GlobalConfig 深拷贝失败");
    }

    /// <summary>把工作副本写回配置系统并落盘。返回是否成功 + 失败原因。</summary>
    public static bool Save(GlobalConfig working, out string reason)
    {
        try
        {
            bool ok = ConfigurationManager.ReplaceAndSave(working, out reason);
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.Save failed", ex);
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>从磁盘重读配置，成功后返回新的工作副本。</summary>
    public static GlobalConfig Reload(out string reason)
    {
        try
        {
            ConfigurationManager.TryReload(out reason);
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.Reload failed", ex);
            reason = ex.Message;
        }
        return CloneCurrentConfig();
    }

    /// <summary>取可招募兵种列表（进程内枚举，含 RBM 等 mod 兵种）。</summary>
    public static System.Collections.Generic.List<TroopDumper.TroopEntry> CollectTroops()
    {
        try { return TroopDumper.Collect(); }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.CollectTroops failed", ex);
            return new System.Collections.Generic.List<TroopDumper.TroopEntry>();
        }
    }

    /// <summary>取财务报告（今日/本周/全部 + 近期流水）。</summary>
    public static FinanceReport BuildFinanceReport()
    {
        try
        {
            return ModExpenseLedger.BuildReport();
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.BuildFinanceReport failed", ex);
            return new FinanceReport
            {
                Today = new System.Collections.Generic.Dictionary<string, long>(),
                Week = new System.Collections.Generic.Dictionary<string, long>(),
                AllTime = new System.Collections.Generic.Dictionary<string, long>(),
                RecentEntries = new System.Collections.Generic.List<ExpenseEntry>(),
            };
        }
    }
}
