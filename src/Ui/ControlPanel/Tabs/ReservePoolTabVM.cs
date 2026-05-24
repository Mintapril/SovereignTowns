using System;
using TaleWorlds.Library;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Parties;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 6「B 池状态」VM（PR-6 / PR-7 / PR-9）。
/// 展示当前受管首府的 B 池（Capital Reserve Pool）party 状态及招募模板编辑入口。
///
/// PR-9 (2026-05-24): troop list view moved to vanilla PartyScreen via town menu.
/// Mod tab now only edits the recruitment template.
/// VanillaGarrisonCount / CombinedCount removed — PartyScreen provides the live troop view.
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


    public ReservePoolTabVM()
    {
        Title  = ControlPanelLoc.Tr("B 池状态", "Reserve Pool");
        Intro  = ControlPanelLoc.Tr(
            "首府储备兵力池（B 池）— 永驻首府内、仅在围城时参与防御的储备队伍。招募模板在「B 池模板」标签页编辑。",
            "The capital reserve pool (B-pool) — a party permanently stationed inside the capital that joins siege defence. Edit the recruitment template in the \"Reserve pool template\" tab.");
        Refresh();
    }

    /// <summary>刷新 B 池状态（由 ControlPanelVM 在 tab 切换时调用）。</summary>
    public void Refresh()
    {
        try
        {
            var capital = CapitalRegistry.Instance?.GetForPlayer()?.GetCapitalSettlement();
            if (capital == null)
            {
                PoolStatus    = ControlPanelLoc.Tr("无首府", "No capital");
                PoolHeadcount = "-";
                PoolCap       = "-";
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

            if (pool == null)
            {
                PoolStatus    = cap == 0
                    ? ControlPanelLoc.Tr("未启用（容量上限=0）", "Disabled (cap=0)")
                    : ControlPanelLoc.Tr("未创建", "Not created");
                PoolHeadcount = "0";
                PoolCap       = cap.ToString();
            }
            else
            {
                int headcount = pool.MemberRoster?.TotalManCount ?? 0;
                PoolStatus    = ControlPanelLoc.Tr("运行中", "Active");
                PoolHeadcount = headcount.ToString();
                PoolCap       = cap == 0
                    ? ControlPanelLoc.Tr("0（调度器不注入兵员）", "0 (scheduler will not fill)")
                    : cap.ToString();
            }
        }
        catch (Exception ex)
        {
            SovereignTowns.Logging.Logger.Error("ReservePoolTabVM.Refresh failed", ex);
            PoolStatus    = ControlPanelLoc.Tr("刷新失败", "Refresh error");
            PoolHeadcount = "-";
            PoolCap       = "-";
        }
    }
}
