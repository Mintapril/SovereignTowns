using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TaleWorlds.Library;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Parties;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 6「B 池状态」VM（PR-6）。
/// 展示当前受管首府的 B 池（Capital Reserve Pool）party 状态：
/// 是否存在、当前驻军头数、容量上限。
///
/// PR-6 阶段仅做只读展示，MCMF 注入逻辑在 PR-7 实现。
/// </summary>
public sealed class ReservePoolTabVM : ViewModel
{
    // ── 静态文案 ──
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }

    // ── 状态属性 ──
    private string _poolStatus = "";
    private string _poolHeadcount = "";
    private string _poolCap = "";
    private string _vanillaGarrisonCount = "";
    private string _combinedCount = "";

    // ── 模板编辑属性（PR-7） ──
    private string _reserveTemplateJson = "";
    private string _templateSaveStatus = "";

    [DataSourceProperty]
    public string PoolStatus
    {
        get => _poolStatus;
        private set { if (_poolStatus != value) { _poolStatus = value; OnPropertyChanged(nameof(PoolStatus)); } }
    }

    [DataSourceProperty]
    public string PoolHeadcount
    {
        get => _poolHeadcount;
        private set { if (_poolHeadcount != value) { _poolHeadcount = value; OnPropertyChanged(nameof(PoolHeadcount)); } }
    }

    [DataSourceProperty]
    public string PoolCap
    {
        get => _poolCap;
        private set { if (_poolCap != value) { _poolCap = value; OnPropertyChanged(nameof(PoolCap)); } }
    }

    /// <summary>PR-8：vanilla GarrisonParty 驻军头数（不含 B 池）。</summary>
    [DataSourceProperty]
    public string VanillaGarrisonCount
    {
        get => _vanillaGarrisonCount;
        private set { if (_vanillaGarrisonCount != value) { _vanillaGarrisonCount = value; OnPropertyChanged(nameof(VanillaGarrisonCount)); } }
    }

    /// <summary>PR-8：vanilla + B 池合计头数（玩家实际可用守城兵力）。</summary>
    [DataSourceProperty]
    public string CombinedCount
    {
        get => _combinedCount;
        private set { if (_combinedCount != value) { _combinedCount = value; OnPropertyChanged(nameof(CombinedCount)); } }
    }

    /// <summary>PR-7：B 池招募模板，JSON 格式（用户直接编辑）。
    /// 示例：{"imperial_veteran_infantry": 30, "imperial_palatine_guard": 20}
    /// 空字符串 = 清空模板（不派征兵队）。</summary>
    [DataSourceProperty]
    public string ReserveTemplateJson
    {
        get => _reserveTemplateJson;
        set { if (_reserveTemplateJson != value) { _reserveTemplateJson = value; OnPropertyChanged(nameof(ReserveTemplateJson)); } }
    }

    /// <summary>PR-7：保存结果反馈（"已保存" / 错误原因）。</summary>
    [DataSourceProperty]
    public string TemplateSaveStatus
    {
        get => _templateSaveStatus;
        private set { if (_templateSaveStatus != value) { _templateSaveStatus = value; OnPropertyChanged(nameof(TemplateSaveStatus)); } }
    }

    public ReservePoolTabVM()
    {
        Title  = ControlPanelLoc.Tr("B 池状态", "Reserve Pool");
        Intro  = ControlPanelLoc.Tr(
            "首府储备兵力池（B 池）— 永驻首府内、仅在围城时参与防御的储备队伍。PR-7 调度器每日检查 B 池，按模板补充对应兵种。",
            "The capital reserve pool (B-pool) — a party permanently stationed inside the capital that joins siege defence. The PR-7 dispatcher checks daily and fills it according to the template below.");
        Refresh();
    }

    /// <summary>刷新 B 池状态和模板 JSON（由 ControlPanelVM 在 tab 切换时调用）。</summary>
    public void Refresh()
    {
        try
        {
            var capital = CapitalRegistry.Instance?.GetForPlayer()?.GetCapitalSettlement();
            if (capital == null)
            {
                PoolStatus           = ControlPanelLoc.Tr("无首府", "No capital");
                PoolHeadcount        = "-";
                PoolCap              = "-";
                VanillaGarrisonCount = "-";
                CombinedCount        = "-";
                RefreshTemplateJson();
                return;
            }

            // 从 settlement.Parties 找 B 池 party（ReservePoolManager 未暴露公开 accessor，直接扫）。
            TaleWorlds.CampaignSystem.Party.MobileParty? pool = null;
            foreach (var party in capital.Parties)
            {
                if (party?.PartyComponent is StGarrisonReservePartyComponent)
                {
                    pool = party;
                    break;
                }
            }

            int cap = ConfigurationManager.Current?.FiscalAutonomy?.ReservePoolCap ?? 0;

            // PR-8：vanilla GarrisonParty 头数（encyclopedia 等 vanilla UI 只显示这个数字）。
            int vanillaCount = capital.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;

            if (pool == null)
            {
                PoolStatus    = cap == 0
                    ? ControlPanelLoc.Tr("未启用（容量上限=0）", "Disabled (cap=0)")
                    : ControlPanelLoc.Tr("未创建", "Not created");
                PoolHeadcount = "0";
                PoolCap       = cap.ToString();
                VanillaGarrisonCount = vanillaCount.ToString();
                CombinedCount        = vanillaCount.ToString();
            }
            else
            {
                int headcount = pool.MemberRoster?.TotalManCount ?? 0;
                PoolStatus    = ControlPanelLoc.Tr("运行中", "Active");
                PoolHeadcount = headcount.ToString();
                PoolCap       = cap == 0
                    ? ControlPanelLoc.Tr("0（调度器不注入兵员）", "0 (scheduler will not fill)")
                    : cap.ToString();
                VanillaGarrisonCount = vanillaCount.ToString();
                CombinedCount        = (vanillaCount + headcount).ToString();
            }

            RefreshTemplateJson();
        }
        catch (Exception ex)
        {
            SovereignTowns.Logging.Logger.Error("ReservePoolTabVM.Refresh failed", ex);
            PoolStatus           = ControlPanelLoc.Tr("刷新失败", "Refresh error");
            PoolHeadcount        = "-";
            PoolCap              = "-";
            VanillaGarrisonCount = "-";
            CombinedCount        = "-";
        }
    }

    /// <summary>PR-7：从当前配置加载模板 JSON 到文本框（tab 切换时调用）。</summary>
    private void RefreshTemplateJson()
    {
        try
        {
            var template = ConfigurationManager.Current?.FiscalAutonomy?.ReserveTemplate;
            ReserveTemplateJson = template != null && template.Count > 0
                ? JsonConvert.SerializeObject(template, Formatting.Indented)
                : "";
            TemplateSaveStatus = "";
        }
        catch (Exception ex)
        {
            SovereignTowns.Logging.Logger.Warn($"ReservePoolTabVM.RefreshTemplateJson failed: {ex.Message}");
            ReserveTemplateJson = "";
        }
    }

    /// <summary>
    /// PR-7：保存模板 JSON 到配置并写盘。
    /// 空字符串 → 清空模板（null）。非空字符串 → 解析为 Dictionary&lt;string,int&gt; 后校验并保存。
    /// 由 Gauntlet 面板「保存模板」按钮调用（ExecuteCommand 绑定）。
    /// </summary>
    public void ExecuteSaveTemplate()
    {
        try
        {
            var cfg = ConfigurationManager.Current;
            if (cfg?.FiscalAutonomy == null)
            {
                TemplateSaveStatus = ControlPanelLoc.Tr("配置未初始化", "Config not initialized");
                return;
            }

            string json = ReserveTemplateJson?.Trim() ?? "";

            Dictionary<string, int>? newTemplate;
            if (string.IsNullOrEmpty(json))
            {
                newTemplate = null;
            }
            else
            {
                try
                {
                    newTemplate = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                }
                catch (Exception parseEx)
                {
                    TemplateSaveStatus = ControlPanelLoc.Tr($"JSON 解析失败：{parseEx.Message}", $"JSON parse error: {parseEx.Message}");
                    return;
                }
            }

            cfg.FiscalAutonomy.ReserveTemplate = newTemplate;

            if (!ConfigurationManager.Save())
            {
                TemplateSaveStatus = ControlPanelLoc.Tr(
                    $"保存失败：{ConfigurationManager.LastValidationError}",
                    $"Save failed: {ConfigurationManager.LastValidationError}");
                return;
            }

            TemplateSaveStatus = ControlPanelLoc.Tr("已保存", "Saved");
            SovereignTowns.Logging.Logger.Info($"ReservePoolTabVM: template saved ({newTemplate?.Count ?? 0} entries)");
        }
        catch (Exception ex)
        {
            SovereignTowns.Logging.Logger.Error("ReservePoolTabVM.ExecuteSaveTemplate failed", ex);
            TemplateSaveStatus = ControlPanelLoc.Tr("保存时出错", "Error during save");
        }
    }
}
