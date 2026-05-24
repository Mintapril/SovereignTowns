using System;
using TaleWorlds.Library;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Parties;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 6「卫队」VM。展示当前受管首府的卫队（Honor Guard）party 状态。
/// 招募模板编辑在「卫队编制」标签页（TemplatesTabVM）。
/// 兵员清单走 vanilla PartyScreen（town menu「主权城镇：管理卫队」入口）。
/// </summary>
public sealed class HonorGuardTabVM : ViewModel
{
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }

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

    public HonorGuardTabVM()
    {
        Title  = ControlPanelLoc.Tr("卫队", "Honor Guard");
        Intro  = ControlPanelLoc.Tr(
            "首府卫队 — 永驻首府内、仅在围城时参与防御的私属精锐。招募模板在「卫队编制」标签页编辑。",
            "The capital honor guard — a private elite party permanently stationed inside the capital that joins siege defence. Edit the recruitment template in the \"Honor guard composition\" tab.");
        Refresh();
    }

    /// <summary>刷新卫队状态（由 ControlPanelVM 在 tab 切换时调用）。</summary>
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

            TaleWorlds.CampaignSystem.Party.MobileParty? pool = null;
            foreach (var party in capital.Parties)
            {
                if (party?.PartyComponent is HonorGuardPartyComponent)
                {
                    pool = party;
                    break;
                }
            }

            int cap = ConfigurationManager.Current?.FiscalAutonomy?.HonorGuardCap ?? 0;

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
            SovereignTowns.Logging.Logger.Error("HonorGuardTabVM.Refresh failed", ex);
            PoolStatus    = ControlPanelLoc.Tr("刷新失败", "Refresh error");
            PoolHeadcount = "-";
            PoolCap       = "-";
        }
    }
}
