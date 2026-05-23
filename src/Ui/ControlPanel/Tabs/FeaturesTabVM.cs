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
            "提示：本面板运行在游戏外的浏览器中，调整配置需切出游戏。建议游戏使用「窗口」或「无边框窗口」模式，方便来回切换。",
            "Tip: this panel runs in a browser outside the game, so adjusting config means alt-tabbing out. Running the game in \"Windowed\" or \"Borderless Window\" mode makes switching back and forth easier.");

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

        Add("管理同氏族所有领地", "Manage entire clan's holdings",
            "开启：同氏族所有非首府城镇/城堡都被管理，无论由哪位 hero 持有。\n关闭（默认）：仅管理首府所有者 hero 自己持有的领地；同氏族兄弟 / 子嗣名下的非首府城镇/城堡由 vanilla 处理，ST 不动。",
            "On: every non-capital town/castle the clan owns is managed, regardless of which family member holds it.\nOff (default): only settlements held by the same hero who owns the capital are managed; non-capital fiefs held by brothers or children of the clan are left to vanilla.",
            () => ef.ManageAllClanBranches, v => ef.ManageAllClanBranches = v);

        Add("主动出击", "Sally forth",
            "附近有敌对势力且满足驻军、冷却和持续可见条件时出城攻击",
            "Attack out of the settlement when a hostile force is nearby and the garrison, cooldown and sustained-visibility conditions are met.",
            () => ef.SallyForth, v => ef.SallyForth = v);

        Add("抑制 vanilla 自动招募", "Suppress vanilla auto-recruitment",
            "自动招募开启时，关闭受管城镇/城堡的 vanilla 自动驻军生长，让本 Mod 的征兵队成为主要兵源",
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

        Add("详细诊断日志", "Verbose diagnostic logging",
            "开启后 Logger 等级降到 Debug：落盘所有 [DIAG] 行（hourly tick 状态、状态机迁移、食物维护跳过原因、scheduler 决策等）。日志文件会很大，调试用后请关闭。修改即时生效，无需重启。",
            "Lowers the logger to Debug level and writes every [DIAG] line (hourly tick state, state-machine transitions, food-maintenance skip reasons, scheduler decisions, etc.). The log file gets large — turn this off after debugging. Takes effect immediately, no restart needed.",
            () => ef.VerboseLogging, v => ef.VerboseLogging = v);
    }
}
