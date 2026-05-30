using System;
using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 2「策略参数」VM：分组过滤芯片 + 高级参数开关 + 当前分组的滑条 / 开关行 + 「本组恢复默认」。
///
/// 绑定策略：避免 <c>@StrategyTab.ActiveGroup.Sliders</c> 这类深路径绑定 —— 改在本 VM
/// 暴露扁平的 <see cref="ActiveSliders"/> / <see cref="ActiveToggles"/> /
/// <see cref="ActiveGroupLabel"/> 等属性，并在切换激活分组时就地增删 MBBindingList。
/// </summary>
public sealed class StrategyTabVM : ViewModel
{
    private readonly GlobalConfig _config;
    private readonly Action _markDirty;

    private bool _showAdvanced;
    private string _activeGroupKey = "";
    private SettingsGroupVM _activeGroup;

    // ── 静态文案 ──
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro1 { get; }
    [DataSourceProperty] public string Intro2 { get; }
    [DataSourceProperty] public string AdvancedToggleLabel { get; }
    [DataSourceProperty] public string ResetGroupLabel { get; }

    // ── 分组芯片 + 当前分组的行 ──
    // VisibleGroups 是逻辑用的全量列表；Row1/Row2 是分两行渲染用的视图（prefab 实际绑定这两个）。
    // Gauntlet ListPanel 不支持换行，单行 7-8 个 chip 会被「显示高级参数」按钮挤出 / 遮住。
    [DataSourceProperty] public MBBindingList<SettingsGroupVM> VisibleGroups { get; } = new MBBindingList<SettingsGroupVM>();
    [DataSourceProperty] public MBBindingList<SettingsGroupVM> VisibleGroupsRow1 { get; } = new MBBindingList<SettingsGroupVM>();
    [DataSourceProperty] public MBBindingList<SettingsGroupVM> VisibleGroupsRow2 { get; } = new MBBindingList<SettingsGroupVM>();
    [DataSourceProperty] public MBBindingList<SliderRowVM> ActiveSliders { get; } = new MBBindingList<SliderRowVM>();
    [DataSourceProperty] public MBBindingList<ToggleRowVM> ActiveToggles { get; } = new MBBindingList<ToggleRowVM>();

    [DataSourceProperty] public string ActiveGroupKey => _activeGroupKey;

    [DataSourceProperty]
    public bool ShowAdvanced
    {
        get => _showAdvanced;
        private set { if (_showAdvanced != value) { _showAdvanced = value; OnPropertyChanged(nameof(ShowAdvanced)); } }
    }

    private string _activeGroupLabel = "";
    private string _activeGroupHint = "";

    [DataSourceProperty]
    public string ActiveGroupLabel
    {
        get => _activeGroupLabel;
        private set { if (_activeGroupLabel != value) { _activeGroupLabel = value; OnPropertyChanged(nameof(ActiveGroupLabel)); } }
    }

    [DataSourceProperty]
    public string ActiveGroupHint
    {
        get => _activeGroupHint;
        private set { if (_activeGroupHint != value) { _activeGroupHint = value; OnPropertyChanged(nameof(ActiveGroupHint)); } }
    }

    public StrategyTabVM(GlobalConfig config, Action markDirty)
    {
        _config = config;
        _markDirty = markDirty;

        Title  = ControlPanelLoc.Tr("策略参数", "Strategy parameters");
        Intro1 = ControlPanelLoc.Tr(
            "驻军目标、招募、巡逻调拨、出击等各子系统的数值参数。全局规则应用于所有受管首府；非首府只受「非首府驻军」标签页约束。",
            "Numeric parameters for the garrison-target, recruitment, patrol/transfer, sally and other subsystems. Global rules apply to all managed capitals; branches are governed only by the \"Branches\" tab.");
        Intro2 = ControlPanelLoc.Tr(
            "每条参数都标注了出厂默认值；改过的项会出现「↺ 恢复默认」。开发者级旋钮（调度器内部参数）默认折叠，普通玩家无需调整。",
            "Each parameter shows its factory default; a changed item gets a \"↺ Reset\" link. Developer-level knobs (scheduler internals) are collapsed by default and ordinary players need not touch them.");
        AdvancedToggleLabel = ControlPanelLoc.Tr(
            "显示高级参数（开发者旋钮）", "Show advanced parameters (developer knobs)");
        ResetGroupLabel = ControlPanelLoc.Tr("本组恢复默认", "Reset this group");

        RebuildGroups();
    }

    // ── 命令 ──

    /// <summary>「显示高级参数」开关。重建分组；若当前分组在关闭高级后不可见，回落到第一个可见分组。</summary>
    public void ExecuteToggleAdvanced()
    {
        ShowAdvanced = !_showAdvanced;
        RebuildGroups();
    }

    /// <summary>把当前激活分组内所有可见 spec 恢复出厂默认（委托给激活分组 VM）。</summary>
    public void ExecuteResetActiveGroup()
    {
        _activeGroup?.ExecuteResetGroup();
    }

    // ── 内部 ──

    /// <summary>
    /// 从 <see cref="ControlPanelSpecs.AllGroups"/> 重建可见分组列表。
    /// visibleSettingsGroups：showAdvanced=false 时丢掉 Advanced spec、
    /// 丢掉过滤后为空的分组，并丢掉整组 Advanced 的分组（当前 8 组里无此情况；
    /// 字段保留以支持将来加入整组开发者级分组）。
    /// </summary>
    private void RebuildGroups()
    {
        VisibleGroups.Clear();

        foreach (var group in ControlPanelSpecs.AllGroups)
        {
            if (!_showAdvanced && group.Advanced) continue; // 整组高级（当前无此情况，保留兜底）

            var vm = new SettingsGroupVM(group, _config, _markDirty, _showAdvanced, OnSelectGroup);
            if (vm.RowCount == 0) continue; // 过滤后无可见行

            VisibleGroups.Add(vm);
        }

        // 拆成两行：前半 → Row1，后半 → Row2。Row1/Row2 与 VisibleGroups 共享同一批
        // SettingsGroupVM 实例，SetActiveGroup 改 IsActive 时两行高亮自动同步。
        VisibleGroupsRow1.Clear();
        VisibleGroupsRow2.Clear();
        int rowSplit = (VisibleGroups.Count + 1) / 2;
        for (int i = 0; i < VisibleGroups.Count; i++)
            (i < rowSplit ? VisibleGroupsRow1 : VisibleGroupsRow2).Add(VisibleGroups[i]);

        // 决定激活分组：保持当前 key（若仍可见），否则回落第一个。
        SettingsGroupVM target = null;
        foreach (var g in VisibleGroups)
            if (g.Key == _activeGroupKey) { target = g; break; }
        if (target == null && VisibleGroups.Count > 0) target = VisibleGroups[0];

        SetActiveGroup(target);
    }

    /// <summary>芯片点击回调（由 SettingsGroupVM.ExecuteClick 调用）。</summary>
    private void OnSelectGroup(SettingsGroupVM group) => SetActiveGroup(group);

    /// <summary>切换激活分组：刷新各芯片 IsActive、激活分组的标签 / 提示和扁平行列表。</summary>
    private void SetActiveGroup(SettingsGroupVM group)
    {
        _activeGroup = group;
        _activeGroupKey = group?.Key ?? "";
        OnPropertyChanged(nameof(ActiveGroupKey));

        // 芯片高亮：每个分组的 IsActive 与激活 key 比对。
        foreach (var g in VisibleGroups)
            g.IsActive = g.Key == _activeGroupKey;

        ActiveGroupLabel = group?.GroupLabel ?? "";
        ActiveGroupHint = group?.GroupHint ?? "";

        // 就地重建扁平行列表（不可整体重新赋值 MBBindingList — prefab 绑定不会跟随）。
        ActiveSliders.Clear();
        ActiveToggles.Clear();
        if (group != null)
        {
            foreach (var s in group.Sliders) ActiveSliders.Add(s);
            foreach (var t in group.Toggles) ActiveToggles.Add(t);
        }
    }

    // ── 反射读写助手（被 SettingsGroupVM 的逐 spec 委托复用）──

    private static object RootObj(GlobalConfig cfg, string root)
    {
        if (cfg == null) throw new ArgumentNullException(nameof(cfg));
        if (string.IsNullOrEmpty(root)) return cfg;
        var prop = cfg.GetType().GetProperty(root)
            ?? throw new InvalidOperationException($"Strategy config root '{root}' not found");
        return prop.GetValue(cfg)
            ?? throw new InvalidOperationException($"Strategy config root '{root}' is null");
    }

    /// <summary>按 SpecEntry 从工作配置读出 double 值。</summary>
    public static double GetD(GlobalConfig cfg, SpecEntry s)
    {
        var o = RootObj(cfg, s.Root);
        var p = o.GetType().GetProperty(s.Key)
            ?? throw new InvalidOperationException($"Strategy config key '{s.Root}.{s.Key}' not found");
        return System.Convert.ToDouble(p.GetValue(o));
    }

    /// <summary>按 SpecEntry 把 double 值写回工作配置，按目标属性类型装箱。</summary>
    public static void SetD(GlobalConfig cfg, SpecEntry s, double v)
    {
        var o = RootObj(cfg, s.Root);
        var p = o.GetType().GetProperty(s.Key)
            ?? throw new InvalidOperationException($"Strategy config key '{s.Root}.{s.Key}' not found");
        object boxed = p.PropertyType == typeof(int) ? (object)(int)System.Math.Round(v)
                     : p.PropertyType == typeof(bool) ? (object)(v != 0.0)
                     : p.PropertyType == typeof(float) ? (object)(float)v
                     // 枚举旋钮（ForecastMode）：反射 SetValue 要求装箱值正是枚举类型；
                     // 直接塞 double 会抛，须经 Enum.ToObject 转过去。
                     : p.PropertyType.IsEnum ? System.Enum.ToObject(p.PropertyType, (int)System.Math.Round(v))
                     : (object)v;
        p.SetValue(o, boxed);
    }
}
