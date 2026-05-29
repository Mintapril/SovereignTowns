using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class FeaturesTabVM : ViewModel
{
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro1 { get; }
    [DataSourceProperty] public string Intro2 { get; }
    [DataSourceProperty] public MBBindingList<ToggleRowVM> Toggles { get; } = new MBBindingList<ToggleRowVM>();

    public FeaturesTabVM(GlobalConfig config, System.Action markDirty)
    {
        Title  = ControlPanelLoc.Tr("功能开关", "Feature switches");
        Intro1 = ControlPanelLoc.Tr(
            "总闸级开关，控制各子系统是否参与决策。所有改动在点上方「保存改动」前不会生效。",
            "Master switches controlling whether each subsystem takes part in decisions. No change takes effect until you click \"Save changes\" above.");
        Intro2 = ControlPanelLoc.Tr(
            "提示：本面板是游戏内控制面板，可直接在大地图界面操作。",
            "Tip: this is an in-game control panel, operated directly from the map screen.");

        var ef = config.EnabledFeatures;

        void Add(string zh, string en, string zhHint, string enHint,
                 System.Func<bool> g, System.Action<bool> s)
            => Toggles.Add(new ToggleRowVM(ControlPanelLoc.Tr(zh, en),
                   ControlPanelLoc.Tr(zhHint, enHint), false, g, s, markDirty));

        Add("自动招募", "Auto-recruitment",
            "首府原地招募、外派征兵、俘虏转化和驻军训练，用来补充驻军兵源",
            "Capital in-place recruitment, recruiter parties, prisoner conversion and garrison training — the sources that replenish garrison troops.",
            () => ef.AutoRecruitment, v => ef.AutoRecruitment = v);

        Add("自动巡逻", "Auto-patrol",
            "自动派出巡逻队保护领地",
            "Automatically dispatch patrol parties to protect your territory.",
            () => ef.AutoPatrol, v => ef.AutoPatrol = v);

        Add("兵力调拨", "Troop transfers",
            "由首府统一协调自有城镇与城堡之间的补兵",
            "The capital coordinates reinforcement between your own towns and castles.",
            () => ef.TroopTransfers, v => ef.TroopTransfers = v);

        Add("主动出击", "Sally forth",
            "附近有敌对势力且满足驻军、冷却和威胁预测条件时出城攻击",
            "Attack out of the settlement when a hostile force is nearby and the garrison, cooldown and threat-forecast conditions are met.",
            () => ef.SallyForth, v => ef.SallyForth = v);

        Add("抑制原版自动招募", "Suppress vanilla auto-recruitment",
            "自动招募开启时，关闭受管城镇/城堡的原版自动驻军生长，让本 Mod 的征兵队成为主要兵源",
            "While auto-recruitment is on, disable vanilla garrison auto-growth in managed towns/castles so this mod's recruiter parties become the main troop source.",
            () => ef.SuppressVanillaGarrisonRecruitment, v => ef.SuppressVanillaGarrisonRecruitment = v);

        Add("金币不足时暂停支出", "Pause spending when broke",
            "玩家金币不足时拒绝扣款 / 派遣 / 升级，防止\"派出去又因没钱半截失败\"",
            "When you are short on gold, refuse charges / dispatches / upgrades, so a party is not sent out only to fail halfway for lack of funds.",
            () => ef.PauseSpendingWhenBroke, v => ef.PauseSpendingWhenBroke = v);

        Add("每日活动汇总弹窗", "Daily activity summary popup",
            "每日 InformationManager 弹窗显示今日招/调/巡逻/出击/俘虏人数",
            "A daily in-game popup showing today's recruited / transferred / patrol / sally / prisoner counts.",
            () => ef.ShowDailySummary, v => ef.ShowDailySummary = v);

        Add("大地图追踪本Mod部队", "Track mod parties on map",
            "开启后把本 Mod 的活动部队（征兵 / 调拨 / 巡逻 / 出击）显示为大地图标记，和\"追踪商队\"一样。部队解散后标记自动消失。修改即时生效（最迟下一游戏小时）。",
            "Shows this mod's active parties (recruiter / transfer / patrol / sally) as campaign-map markers, like tracking a caravan. Markers vanish when a party disbands. Takes effect within one game hour.",
            () => ef.TrackPartiesOnMap, v => ef.TrackPartiesOnMap = v);

        Add("详细诊断日志", "Verbose diagnostic logging",
            "开启后写入详细调试日志（逐小时状态、决策过程、各种跳过原因等），文件会很大，仅排查问题时开启。修改即时生效，无需重启。",
            "Writes a verbose debug log (per-hour state, decision details, skip reasons, etc.). The file gets large — only enable it while troubleshooting. Takes effect immediately, no restart needed.",
            () => ef.VerboseLogging, v => ef.VerboseLogging = v);
    }
}
