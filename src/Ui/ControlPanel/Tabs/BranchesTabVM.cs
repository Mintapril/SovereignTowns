using System;
using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 4「非首府驻军 / Branch garrison」VM。
/// 两条数值滑条：TargetPower 和 LowTierMinFraction，读写 GlobalConfig.BranchDefaults。
/// </summary>
public sealed class BranchesTabVM : ViewModel
{
    private readonly GlobalConfig _config;

    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }
    [DataSourceProperty] public SliderRowVM TargetPowerRow { get; }
    [DataSourceProperty] public SliderRowVM LowTierRow { get; }

    // 2026-05-24:ManualMode / AutoMode / AutoModeNotice 三 property 已删除。
    // 手动模式整体下线,TargetPower 旋钮(Step 3 删除)/ LowTier 旋钮一直可见。

    [DataSourceProperty]
    public bool ShowResetAll =>
        Math.Abs(StrategyTabVM.GetD(_config, _targetPowerSpec) - 150.0) > 1e-6 ||
        Math.Abs(StrategyTabVM.GetD(_config, _lowTierSpec) - 0.20) > 1e-6;

    [DataSourceProperty] public string ResetAllLabel => ControlPanelLoc.Tr("↺ 全部恢复默认", "↺ Reset all");

    // keep spec references for ShowResetAll
    private readonly SpecEntry _targetPowerSpec;
    private readonly SpecEntry _lowTierSpec;

    public BranchesTabVM(GlobalConfig config, Action markDirty)
    {
        _config = config;
        Title = ControlPanelLoc.Tr("非首府驻军", "Branch garrison");
        Intro = ControlPanelLoc.Tr(
            "下面两项作用于玩家氏族所有非首府城镇 / 城堡。AI 氏族的目标兵力按 vanilla 公式动态计算（忽略此处设定）。",
            "The two settings below apply to all non-capital towns / castles of the player clan. An AI clan's target strength is computed dynamically by the vanilla formula (this setting is ignored for it).");

        _targetPowerSpec = new SpecEntry
        {
            Root = "BranchDefaults", Key = "TargetPower",
            LabelZh = "目标兵力", LabelEn = "Target strength",
            HintZh = "每座非首府城镇 / 城堡想维持的兵力强度。约 150 ≈ 100 个 T3 步兵；城堡通常 ~80、大城镇 ~250。",
            HintEn = "The military strength each non-capital town / castle should maintain. About 150 ≈ 100 T3 infantry; a castle is usually ~80, a large town ~250.",
            IsBool = false, Min = 0, Max = 100000, Step = 1, Discrete = true, Def = 150, Advanced = false,
        };
        _lowTierSpec = new SpecEntry
        {
            Root = "BranchDefaults", Key = "LowTierMinFraction",
            LabelZh = "低 Tier 头数占比下限", LabelEn = "Low-tier headcount floor",
            HintZh = "T1 + T2 兵的人数 ÷ 驻军总人数必须 ≥ 此值，避免出现「几个 T6 凑满兵力」的退化驻军。",
            HintEn = "The headcount of T1+T2 troops ÷ total garrison headcount must be ≥ this value, preventing a degenerate garrison of \"a few T6 troops filling the strength quota\".",
            IsBool = false, Min = 0, Max = 1, Step = 0.01, Discrete = false, Def = 0.20, Advanced = false,
        };

        TargetPowerRow = new SliderRowVM(_targetPowerSpec,
            () => StrategyTabVM.GetD(_config, _targetPowerSpec),
            v => { StrategyTabVM.SetD(_config, _targetPowerSpec, v); RefreshResetAll(); },
            markDirty);
        LowTierRow = new SliderRowVM(_lowTierSpec,
            () => StrategyTabVM.GetD(_config, _lowTierSpec),
            v => { StrategyTabVM.SetD(_config, _lowTierSpec, v); RefreshResetAll(); },
            markDirty);
    }

    private void RefreshResetAll() => OnPropertyChanged(nameof(ShowResetAll));

    public void ExecuteResetAll()
    {
        TargetPowerRow.ExecuteReset();
        LowTierRow.ExecuteReset();
        RefreshResetAll();
    }
}
