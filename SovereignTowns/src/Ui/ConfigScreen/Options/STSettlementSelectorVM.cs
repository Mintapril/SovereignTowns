using System;
using SovereignTowns.Configuration;
using SovereignTowns.Templates;
using SovereignTowns.Ui.ConfigScreen;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// One row per player-owned Town/Castle on the per-settlement override panel.
/// Holds:
///   <list type="bullet">
///     <item>Settlement display name + Town/Castle badge.</item>
///     <item>An "override enabled" toggle that inserts / removes the entry in
///           <see cref="GlobalConfig.PerSettlementOverrides"/>.</item>
///     <item>A nested <see cref="MBBindingList{STNumericOptionVM}"/> with the same per-rule numeric
///           fields as GlobalDefaults; only meaningful when the toggle is on.</item>
///   </list>
/// </summary>
/// <remarks>
/// Every numeric setter first does <c>TryGetValue</c> on the overrides dictionary and no-ops if
/// the entry is missing (the player turned the toggle off after dragging). This keeps the closures
/// safe from NRE after a toggle-off.
/// </remarks>
public sealed class STSettlementSelectorVM : ViewModel
{
    private readonly string _settlementStringId;
    private readonly bool _isCastle;
    private readonly Func<TownGarrisonRule> _cloneFromDefaults;

    private string _settlementName = "";
    private string _settlementTypeBadge = "";
    private bool _overrideEnabled;
    private MBBindingList<STNumericOptionVM> _overrides = new MBBindingList<STNumericOptionVM>();
    private MBBindingList<STToggleOptionVM> _ruleToggles = new MBBindingList<STToggleOptionVM>();
    private MBBindingList<STButtonOptionVM> _ruleButtons = new MBBindingList<STButtonOptionVM>();

    /// <summary>Settlement display name (e.g. "Marunath"). XML: <c>@SettlementName</c>.</summary>
    [DataSourceProperty]
    public string SettlementName
    {
        get => _settlementName;
        set { if (value != _settlementName) { _settlementName = value ?? ""; OnPropertyChanged(nameof(SettlementName)); } }
    }

    /// <summary>"Town" / "Castle" textual badge. XML: <c>@SettlementTypeBadge</c>.</summary>
    [DataSourceProperty]
    public string SettlementTypeBadge
    {
        get => _settlementTypeBadge;
        set { if (value != _settlementTypeBadge) { _settlementTypeBadge = value ?? ""; OnPropertyChanged(nameof(SettlementTypeBadge)); } }
    }

    /// <summary>True when this settlement has an entry in PerSettlementOverrides.
    /// Setter inserts (clone-from-defaults) on true; removes the key on false; rebuilds
    /// <see cref="Overrides"/> either way. XML: <c>@OverrideEnabled</c>.</summary>
    [DataSourceProperty]
    public bool OverrideEnabled
    {
        get => _overrideEnabled;
        set
        {
            if (value == _overrideEnabled) return;
            _overrideEnabled = value;
            OnPropertyChanged(nameof(OverrideEnabled));
            try { ApplyToggle(value); }
            catch (Exception ex) { Logger.Error($"STSettlementSelectorVM.ApplyToggle({_settlementStringId}) failed", ex); }
        }
    }

    /// <summary>Numeric override rows. Empty until <see cref="OverrideEnabled"/> flips on.
    /// XML: <c>{Overrides}</c>. Use <c>IsVisible="@OverrideEnabled"</c> on the host ListPanel
    /// to collapse the section when the toggle is off.</summary>
    [DataSourceProperty]
    public MBBindingList<STNumericOptionVM> Overrides
    {
        get => _overrides;
        set
        {
            if (value != _overrides)
            {
                _overrides = value;
                OnPropertyChanged(nameof(Overrides));
            }
        }
    }

    [DataSourceProperty]
    public MBBindingList<STToggleOptionVM> RuleToggles
    {
        get => _ruleToggles;
        set
        {
            if (value != _ruleToggles)
            {
                _ruleToggles = value;
                OnPropertyChanged(nameof(RuleToggles));
            }
        }
    }

    [DataSourceProperty]
    public MBBindingList<STButtonOptionVM> RuleButtons
    {
        get => _ruleButtons;
        set
        {
            if (value != _ruleButtons)
            {
                _ruleButtons = value;
                OnPropertyChanged(nameof(RuleButtons));
            }
        }
    }

    /// <summary>
    /// Create a row for the given settlement.
    /// </summary>
    /// <param name="settlementStringId">Settlement.StringId (key into PerSettlementOverrides).</param>
    /// <param name="displayName">Player-facing name.</param>
    /// <param name="isCastle">True for castle, false for town.</param>
    /// <param name="cloneFromDefaults">Factory that returns a fresh deep-cloned rule from current
    ///   GlobalDefaults (used when the toggle is flipped on).</param>
    public STSettlementSelectorVM(
        string settlementStringId,
        string displayName,
        bool isCastle,
        Func<TownGarrisonRule> cloneFromDefaults)
    {
        _settlementStringId = settlementStringId ?? "";
        _isCastle = isCastle;
        _cloneFromDefaults = cloneFromDefaults ?? (() => TownGarrisonRule.CreateDefault());
        _settlementName = displayName ?? _settlementStringId;
        _settlementTypeBadge = isCastle ? "[Castle]" : "[Town]";

        // Seed initial state from the live config.
        var dict = ConfigurationManager.Current?.PerSettlementOverrides;
        _overrideEnabled = dict != null && dict.ContainsKey(_settlementStringId);
        if (_overrideEnabled)
        {
            RebuildOverrideRows();
        }
    }

    /// <summary>Toggle-on path: clone GlobalDefaults into the dict and build the slider rows.
    /// Toggle-off path: drop the dict entry and clear the row list.</summary>
    private void ApplyToggle(bool enabled)
    {
        var dict = ConfigurationManager.Current?.PerSettlementOverrides;
        if (dict is null) return;

        if (enabled)
        {
            if (!dict.ContainsKey(_settlementStringId))
            {
                dict[_settlementStringId] = _cloneFromDefaults();
            }
            RebuildOverrideRows();
        }
        else
        {
            dict.Remove(_settlementStringId);
            _overrides.Clear();
            _ruleToggles.Clear();
            _ruleButtons.Clear();
            OnPropertyChanged(nameof(Overrides));
            OnPropertyChanged(nameof(RuleToggles));
            OnPropertyChanged(nameof(RuleButtons));
        }
    }

    /// <summary>Populate <see cref="Overrides"/> with numeric rows that mutate the per-settlement
    /// rule in-place. Each setter does <c>TryGetValue</c> to no-op if the override was meanwhile
    /// removed by a toggle-off, preventing NRE.</summary>
    private void RebuildOverrideRows()
    {
        _overrides.Clear();
        _ruleToggles.Clear();
        _ruleButtons.Clear();

        var dict = ConfigurationManager.Current?.PerSettlementOverrides;
        if (dict is null || !dict.TryGetValue(_settlementStringId, out var rule) || rule is null) return;

        string id = _settlementStringId;
        STButtonOptionVM? exactTemplateButton = null;

        _ruleToggles.Add(new STToggleOptionVM(
            "通用匹配 (UseGenericMatching)",
            "开启：忽略阵营，候选兵能升级到与模板目标同 Tier+同兵种类型的任意兵即匹配；关闭：完全 IG 风格，候选必须能升级到模板里的具体目标兵种。",
            rule.UseGenericMatching,
            v =>
            {
                if (TryGetRule(id, out var r))
                {
                    TroopTemplateModeService.SetUseGenericMatching(r, v);
                }
            }));

        exactTemplateButton = new STButtonOptionVM(
            "具体兵员模板",
            "打开原版部队管理页编辑 IG 风格的目标兵种和数量；左侧保存为模板，右侧为可选兵种。",
            $"编辑士兵定义 ({rule.ExactTroopTemplate.Count})",
            isVisible: true,
            () =>
            {
                if (!TryGetRule(id, out var r) || r is null) return;
                ExactTroopTemplateEditor.OpenForRule(
                    r,
                    id,
                    () => exactTemplateButton!.ButtonText = $"编辑士兵定义 ({r.ExactTroopTemplate.Count})");
            });
        _ruleButtons.Add(exactTemplateButton);

        _overrides.Add(new STNumericOptionVM(
            "目标驻军总数 (TargetTotalCount)",
            "驻军应维持的目标兵员数 (50–500)。",
            min: 50, max: 500, current: rule.TargetTotalCount, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.TargetTotalCount = (int)v; }));

        _overrides.Add(new STNumericOptionVM(
            "最少防守人数 (MinimumDefenders)",
            "至少保留的防守人数 (0–300)。",
            min: 0, max: 300, current: rule.MinimumDefenders, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.MinimumDefenders = (int)v; }));

        _overrides.Add(new STNumericOptionVM(
            "招募预算上限 (BudgetLimit)",
            "单日招募预算上限 denar (0–50000)。",
            min: 0, max: 50000, current: rule.BudgetLimit, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.BudgetLimit = (int)v; }));

        var troopRatios = new STRatioOptionGroup();
        _overrides.Add(troopRatios.Add(
            "骑兵占比 (CavalryRatio)", "驻军骑兵目标占比。",
            rule.CavalryRatio,
            v => { if (TryGetRule(id, out var r)) r!.CavalryRatio = v; }));

        _overrides.Add(troopRatios.Add(
            "步兵占比 (InfantryRatio)", "驻军步兵目标占比。",
            rule.InfantryRatio,
            v => { if (TryGetRule(id, out var r)) r!.InfantryRatio = v; }));

        _overrides.Add(troopRatios.Add(
            "弓手占比 (ArcherRatio)", "驻军弓手目标占比。",
            rule.ArcherRatio,
            v => { if (TryGetRule(id, out var r)) r!.ArcherRatio = v; }));

        _overrides.Add(troopRatios.Add(
            "弩手占比 (CrossbowRatio)", "驻军弩手目标占比。",
            rule.CrossbowRatio,
            v => { if (TryGetRule(id, out var r)) r!.CrossbowRatio = v; }));

        _overrides.Add(troopRatios.Add(
            "投掷兵占比 (ThrowerRatio)", "驻军投掷兵目标占比。",
            rule.ThrowerRatio,
            v => { if (TryGetRule(id, out var r)) r!.ThrowerRatio = v; }));
        troopRatios.NormalizeInitial();

        var tierRatios = new STRatioOptionGroup();
        _overrides.Add(tierRatios.Add(
            "Tier 1 占比 (Tier1Ratio)", "目标驻军中 Tier 1 兵员比例。",
            rule.Tier1Ratio,
            v => { if (TryGetRule(id, out var r)) r!.Tier1Ratio = v; }));

        _overrides.Add(tierRatios.Add(
            "Tier 2 占比 (Tier2Ratio)", "目标驻军中 Tier 2 兵员比例。",
            rule.Tier2Ratio,
            v => { if (TryGetRule(id, out var r)) r!.Tier2Ratio = v; }));

        _overrides.Add(tierRatios.Add(
            "Tier 3 占比 (Tier3Ratio)", "目标驻军中 Tier 3 兵员比例。",
            rule.Tier3Ratio,
            v => { if (TryGetRule(id, out var r)) r!.Tier3Ratio = v; }));

        _overrides.Add(tierRatios.Add(
            "Tier 4 占比 (Tier4Ratio)", "目标驻军中 Tier 4 兵员比例。",
            rule.Tier4Ratio,
            v => { if (TryGetRule(id, out var r)) r!.Tier4Ratio = v; }));

        _overrides.Add(tierRatios.Add(
            "Tier 5 占比 (Tier5Ratio)", "目标驻军中 Tier 5 兵员比例。",
            rule.Tier5Ratio,
            v => { if (TryGetRule(id, out var r)) r!.Tier5Ratio = v; }));

        _overrides.Add(tierRatios.Add(
            "Tier 6 占比 (Tier6Ratio)", "目标驻军中 Tier 6+ 兵员比例。",
            rule.Tier6Ratio,
            v => { if (TryGetRule(id, out var r)) r!.Tier6Ratio = v; }));
        tierRatios.NormalizeInitial();

        _overrides.Add(new STNumericOptionVM(
            "最低 Tier (MinTier)", "允许招募的最低兵种 Tier (1–6)。",
            min: 1, max: 6, current: rule.MinTier, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.MinTier = (int)v; }));

        _overrides.Add(new STNumericOptionVM(
            "最高 Tier (MaxTier)", "允许招募的最高兵种 Tier (1–6)。",
            min: 1, max: 6, current: rule.MaxTier, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.MaxTier = (int)v; }));

        _overrides.Add(new STNumericOptionVM(
            "战时目标乘数 (WartimeMultiplier)", "战争时 TargetTotalCount 乘数 (0.5–2.0)。",
            min: 0.5f, max: 2.0f, current: rule.WartimeMultiplier, isDiscrete: false,
            v => { if (TryGetRule(id, out var r)) r!.WartimeMultiplier = v; }));

        _overrides.Add(new STNumericOptionVM(
            "和平目标乘数 (PeacetimeMultiplier)", "和平时 TargetTotalCount 乘数 (0.5–2.0)。",
            min: 0.5f, max: 2.0f, current: rule.PeacetimeMultiplier, isDiscrete: false,
            v => { if (TryGetRule(id, out var r)) r!.PeacetimeMultiplier = v; }));

        _overrides.Add(new STNumericOptionVM(
            "食物安全阈值 (FoodSafetyThreshold)", "Town.FoodChange 低于此值暂停招募 (-50~50)。",
            min: -50, max: 50, current: rule.FoodSafetyThreshold, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.FoodSafetyThreshold = v; }));

        _overrides.Add(new STNumericOptionVM(
            "每日驻军 XP 奖励 (DailyTroopXpBonus)", "每日给非 hero 兵员注入的固定 XP (0–30)。",
            min: 0, max: 30, current: rule.DailyTroopXpBonus, isDiscrete: true,
            v => { if (TryGetRule(id, out var r)) r!.DailyTroopXpBonus = (int)v; }));

        OnPropertyChanged(nameof(Overrides));
        OnPropertyChanged(nameof(RuleToggles));
        OnPropertyChanged(nameof(RuleButtons));
    }

    /// <summary>NRE-safe lookup used by every numeric setter. Returns false (no-op) if the override
    /// was removed by a toggle-off after this row's closure was captured.</summary>
    private static bool TryGetRule(string stringId, out TownGarrisonRule? rule)
    {
        rule = null;
        var dict = ConfigurationManager.Current?.PerSettlementOverrides;
        if (dict is null) return false;
        if (!dict.TryGetValue(stringId, out var r) || r is null) return false;
        rule = r;
        return true;
    }
}
