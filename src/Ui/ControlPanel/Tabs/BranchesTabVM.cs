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

    /// <summary>
    /// 目标兵力旋钮仅在「财政自治 → 允许手动驻军目标」开启时可见。
    /// auto 模式下非首府目标由中央调度器（Pass A）按防御价值决定，手动旋钮无意义。
    ///
    /// 这是手动驻军目标两套门控机制之一（门控 <c>BranchDefaults.TargetPower</c>）；
    /// 另一套是 <see cref="SpecEntry.RequiresManualMode"/>（门控 spec 列表内的
    /// <c>GlobalDefaults.TargetTotalCount</c>）。新增第三个手动门控旋钮时两处都要顾及。
    /// </summary>
    [DataSourceProperty] public bool ManualMode { get; }
    [DataSourceProperty] public bool AutoMode => !ManualMode;
    [DataSourceProperty] public string AutoModeNotice { get; }

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
        ManualMode = config?.FiscalAutonomy?.AllowManualGarrisonTargets ?? false;
        AutoModeNotice = ControlPanelLoc.Tr(
            "当前为自动驻军调度模式：非首府目标兵力由中央调度器按防御价值与氏族预算决定，下方「目标兵力」旋钮已隐藏。如需手动设定，请在「策略参数 → 财政自治」中开启「允许手动驻军目标」。",
            "Auto garrison dispatch mode is active: branch target strength is decided by the central dispatcher from defensive value and clan budget, so the \"Target strength\" knob below is hidden. To set it manually, enable \"Allow manual garrison targets\" under Strategy → Fiscal Autonomy.");
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
