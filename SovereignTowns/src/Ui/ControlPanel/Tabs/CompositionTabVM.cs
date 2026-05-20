using System;
using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 2「兵种编制 / Troop composition」VM。
/// 模式选择（通用比例 / 精确模板） + 文化过滤 chips + 4 比例联动滑条 + Tier 范围 chips。
/// </summary>
public sealed class CompositionTabVM : ViewModel
{
    private readonly GlobalConfig _config;
    private readonly Action _markDirty;
    private readonly Action _gotoTemplates;

    // ── ratio keys (must match property names on TownGarrisonRule) ──
    private static readonly string[] RatioKeys = { "CavalryRatio", "HorseArcherRatio", "InfantryRatio", "RangedRatio" };

    // ── static text ──
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }

    // ── mode button labels / descs ──
    [DataSourceProperty] public string GenericModeLabel { get; }
    [DataSourceProperty] public string GenericModeDesc { get; }
    [DataSourceProperty] public string ExactModeLabel { get; }
    [DataSourceProperty] public string ExactModeDesc { get; }

    // ── mode toggle ──
    private bool _isGenericMode;

    [DataSourceProperty]
    public bool IsGenericMode
    {
        get => _isGenericMode;
        private set { if (_isGenericMode != value) { _isGenericMode = value; OnPropertyChanged(nameof(IsGenericMode)); OnPropertyChanged(nameof(IsExactMode)); } }
    }

    [DataSourceProperty] public bool IsExactMode => !_isGenericMode;

    // ── exact mode card labels ──
    [DataSourceProperty] public string ExactModeCardTitle { get; }
    [DataSourceProperty] public string ExactModeCardDesc1 { get; }
    [DataSourceProperty] public string ExactModeCardNoEffect { get; }
    [DataSourceProperty] public string ExactModeCardDesc2 { get; }
    [DataSourceProperty] public string GoToTemplatesLabel { get; }

    // ── culture filter ──
    [DataSourceProperty] public MBBindingList<ChipVM> CultureChips { get; } = new MBBindingList<ChipVM>();

    [DataSourceProperty] public string CultureSectionLabel { get; }
    [DataSourceProperty] public string CultureResetLabel { get; }

    private string _cultureFilterHint = "";
    [DataSourceProperty]
    public string CultureFilterHint
    {
        get => _cultureFilterHint;
        private set { if (_cultureFilterHint != value) { _cultureFilterHint = value; OnPropertyChanged(nameof(CultureFilterHint)); } }
    }

    private bool _showCultureReset;
    [DataSourceProperty]
    public bool ShowCultureReset
    {
        get => _showCultureReset;
        private set { if (_showCultureReset != value) { _showCultureReset = value; OnPropertyChanged(nameof(ShowCultureReset)); } }
    }

    // ── ratio sliders ──
    [DataSourceProperty] public SliderRowVM CavalryRow { get; private set; }
    [DataSourceProperty] public SliderRowVM HorseArcherRow { get; private set; }
    [DataSourceProperty] public SliderRowVM InfantryRow { get; private set; }
    [DataSourceProperty] public SliderRowVM RangedRow { get; private set; }

    [DataSourceProperty] public string RatioSectionLabel { get; }
    [DataSourceProperty] public string RatioHintText { get; }
    [DataSourceProperty] public string RatioSumPrefix { get; }
    [DataSourceProperty] public string RatioNormalizedSuffix { get; }
    [DataSourceProperty] public string RatioResetLabel { get; }

    private string _ratioSumText = "Σ = 1.00";
    [DataSourceProperty]
    public string RatioSumText
    {
        get => _ratioSumText;
        private set { if (_ratioSumText != value) { _ratioSumText = value; OnPropertyChanged(nameof(RatioSumText)); } }
    }

    private bool _ratioSumOk = true;
    [DataSourceProperty]
    public bool RatioSumOk
    {
        get => _ratioSumOk;
        private set { if (_ratioSumOk != value) { _ratioSumOk = value; OnPropertyChanged(nameof(RatioSumOk)); } }
    }

    private bool _showRatioReset;
    [DataSourceProperty]
    public bool ShowRatioReset
    {
        get => _showRatioReset;
        private set { if (_showRatioReset != value) { _showRatioReset = value; OnPropertyChanged(nameof(ShowRatioReset)); } }
    }

    // ── tier range ──
    [DataSourceProperty] public MBBindingList<ChipVM> MinTierChips { get; } = new MBBindingList<ChipVM>();
    [DataSourceProperty] public MBBindingList<ChipVM> MaxTierChips { get; } = new MBBindingList<ChipVM>();

    [DataSourceProperty] public string TierSectionLabel { get; }
    [DataSourceProperty] public string MinTierLabel { get; }
    [DataSourceProperty] public string MaxTierLabel { get; }
    [DataSourceProperty] public string TierHintPrefix { get; }
    [DataSourceProperty] public string TierResetLabel { get; }

    private string _tierRangeText = "";
    [DataSourceProperty]
    public string TierRangeText
    {
        get => _tierRangeText;
        private set { if (_tierRangeText != value) { _tierRangeText = value; OnPropertyChanged(nameof(TierRangeText)); } }
    }

    private bool _showTierReset;
    [DataSourceProperty]
    public bool ShowTierReset
    {
        get => _showTierReset;
        private set { if (_showTierReset != value) { _showTierReset = value; OnPropertyChanged(nameof(ShowTierReset)); } }
    }

    // ── culture filter options ──
    private static readonly (string Value, string LabelZh, string LabelEn, string HintZh, string HintEn)[] CultureOptions =
    {
        ("PlayerCulture",  "玩家文化",  "Player culture",  "只招募玩家氏族文化的兵种。默认选项，最稳妥。", "Recruit only troops of your clan's culture. The default, safest option."),
        ("CapitalCulture", "首府文化",  "Capital culture", "只招募首府所在城镇本身文化的兵种 —— 被征服的异文化城会招当地兵，省去跨文化运兵。", "Recruit only troops of the capital town's own culture — a conquered foreign town recruits local troops, avoiding cross-culture troop hauling."),
        ("Any",            "不过滤",    "No filter",       "不按文化过滤，任何文化的兵种都可进入招募候选。", "No culture filter; troops of any culture are eligible for recruitment."),
    };

    public CompositionTabVM(GlobalConfig config, Action markDirty, Action gotoTemplates)
    {
        _config = config;
        _markDirty = markDirty;
        _gotoTemplates = gotoTemplates;

        // ── static labels ──
        Title = ControlPanelLoc.Tr("兵种编制", "Troop composition");
        Intro = ControlPanelLoc.Tr(
            "先选一种招募模式 —— 决定 mod 如何挑选要招募的兵。",
            "First choose a recruitment mode — it decides how the mod picks which troops to recruit.");

        GenericModeLabel = ControlPanelLoc.Tr("通用比例匹配", "Generic ratio matching");
        GenericModeDesc  = ControlPanelLoc.Tr(
            "按下方文化过滤、兵种比例和 Tier 范围招募，不读取具体兵种模板。省心，适合大多数玩家。",
            "Recruit by the culture filter, troop ratios and tier range below, without reading a specific troop template. Low-effort, suits most players.");
        ExactModeLabel   = ControlPanelLoc.Tr("精确兵员模板", "Exact troop template");
        ExactModeDesc    = ControlPanelLoc.Tr(
            "只招「兵员模板」标签页里勾选的具体兵种。精确控制驻军编成。",
            "Recruit only the specific troops ticked on the \"Templates\" tab. Precise control of garrison composition.");

        ExactModeCardTitle  = ControlPanelLoc.Tr("当前为「精确兵员模板」模式", "Currently in \"Exact troop template\" mode");
        ExactModeCardDesc1  = ControlPanelLoc.Tr(
            "招募只读取「兵员模板」标签页里勾选的兵种。下方的兵种比例与 Tier 范围在此模式下",
            "Recruitment reads only the troops ticked on the \"Templates\" tab. The troop ratios and tier range below ");
        ExactModeCardNoEffect = ControlPanelLoc.Tr("不生效", "have no effect");
        ExactModeCardDesc2  = ControlPanelLoc.Tr("（仅保留备用）。", " in this mode (kept only as a fallback).");
        GoToTemplatesLabel  = ControlPanelLoc.Tr("前往「兵员模板」标签页 →", "Go to the \"Templates\" tab →");

        CultureSectionLabel = ControlPanelLoc.Tr("文化过滤", "Culture filter");
        CultureResetLabel   = ControlPanelLoc.Tr("↺ 恢复默认（玩家文化）", "↺ Reset to default (player culture)");

        RatioSectionLabel      = ControlPanelLoc.Tr("兵种比例", "Troop ratios");
        RatioHintText          = ControlPanelLoc.Tr("拖动任一比例，其余项会按当前相对比例自动缩放使 Σ 保持 1.00。", "Drag any ratio and the others rescale proportionally so Σ stays at 1.00.");
        RatioSumPrefix         = "Σ = ";
        RatioNormalizedSuffix  = ControlPanelLoc.Tr("（自动归一化）", "(auto-normalized)");
        RatioResetLabel        = ControlPanelLoc.Tr("↺ 恢复默认比例", "↺ Reset ratios");

        TierSectionLabel = ControlPanelLoc.Tr("Tier 范围", "Tier range");
        MinTierLabel     = ControlPanelLoc.Tr("最低 Tier", "Min tier");
        MaxTierLabel     = ControlPanelLoc.Tr("最高 Tier", "Max tier");
        TierHintPrefix   = ControlPanelLoc.Tr("仅作为可招募 tier 的硬边界。", "A hard boundary on recruitable tiers only.");
        TierResetLabel   = ControlPanelLoc.Tr("↺ 恢复默认 T2 – T5", "↺ Reset to default T2 – T5");

        // ── mode ──
        _isGenericMode = _config.GlobalDefaults.UseGenericMatching;

        // ── culture chips ──
        BuildCultureChips();
        RefreshCultureState();

        // ── ratio sliders ──
        CavalryRow    = MakeRatioRow("骑兵",  "Cavalry",    "CavalryRatio");
        HorseArcherRow = MakeRatioRow("骑射", "Horse Archer","HorseArcherRatio");
        InfantryRow   = MakeRatioRow("步兵",  "Infantry",   "InfantryRatio");
        RangedRow     = MakeRatioRow("远程",  "Ranged",     "RangedRatio");
        RefreshRatioSum();
        RefreshShowRatioReset();

        // ── tier chips ──
        BuildTierChips();
        RefreshTierState();
    }

    // ══════════════════════════════════════════════
    //  Commands
    // ══════════════════════════════════════════════

    public void ExecuteSetGenericMode()
    {
        if (_config.GlobalDefaults.UseGenericMatching) return;
        _config.GlobalDefaults.UseGenericMatching = true;
        _markDirty();
        IsGenericMode = true;
    }

    public void ExecuteSetExactMode()
    {
        if (!_config.GlobalDefaults.UseGenericMatching) return;
        _config.GlobalDefaults.UseGenericMatching = false;
        _markDirty();
        IsGenericMode = false;
    }

    public void ExecuteGoToTemplatesTab()
    {
        _gotoTemplates?.Invoke();
    }

    public void ExecuteResetCulture()
    {
        _config.GlobalDefaults.GenericCultureFilter = "PlayerCulture";
        _markDirty();
        RefreshCultureState();
    }

    public void ExecuteResetRatios()
    {
        var d = _config.GlobalDefaults;
        d.CavalryRatio    = 0.20f;
        d.HorseArcherRatio = 0.05f;
        d.InfantryRatio   = 0.50f;
        d.RangedRatio     = 0.25f;
        _markDirty();
        CavalryRow.Refresh(); HorseArcherRow.Refresh(); InfantryRow.Refresh(); RangedRow.Refresh();
        RefreshRatioSum();
        RefreshShowRatioReset();
    }

    public void ExecuteResetTier()
    {
        _config.GlobalDefaults.MinTier = 2;
        _config.GlobalDefaults.MaxTier = 5;
        _markDirty();
        RefreshTierState();
    }

    // ══════════════════════════════════════════════
    //  AdjustRatio — ports adjustRatio from WebUI
    // ══════════════════════════════════════════════

    public void AdjustRatio(string key, double raw)
    {
        double v = System.Math.Max(0, System.Math.Min(1, double.IsNaN(raw) ? 0 : raw));
        SetRatio(key, (float)v);

        var others = System.Array.FindAll(RatioKeys, k => k != key);
        double remaining = 1 - v;
        double otherSum = 0;
        foreach (var k in others) otherSum += GetRatio(k);

        if (otherSum > 0.0001)
        {
            double f = remaining / otherSum;
            foreach (var k in others)
                SetRatio(k, (float)System.Math.Max(0, System.Math.Min(1, GetRatio(k) * f)));
        }
        else
        {
            double each = System.Math.Max(0, remaining / others.Length);
            foreach (var k in others) SetRatio(k, (float)each);
        }

        // drift correction
        double total = 0;
        foreach (var k in RatioKeys) total += GetRatio(k);
        double drift = 1 - total;
        if (System.Math.Abs(drift) > 0.0001)
        {
            string biggest = others[0];
            foreach (var k in others) if (GetRatio(k) > GetRatio(biggest)) biggest = k;
            SetRatio(biggest, (float)System.Math.Max(0, System.Math.Min(1, GetRatio(biggest) + drift)));
        }

        _markDirty();
        CavalryRow.Refresh(); HorseArcherRow.Refresh(); InfantryRow.Refresh(); RangedRow.Refresh();
        RefreshRatioSum();
        RefreshShowRatioReset();
    }

    // ══════════════════════════════════════════════
    //  Internal helpers
    // ══════════════════════════════════════════════

    private SliderRowVM MakeRatioRow(string labelZh, string labelEn, string key)
    {
        var spec = new SpecEntry
        {
            Root = "GlobalDefaults", Key = key,
            LabelZh = labelZh, LabelEn = labelEn,
            HintZh = "", HintEn = "",
            Min = 0, Max = 1, Step = 0.01,
            Discrete = false, Def = null,
            Advanced = false
        };
        return new SliderRowVM(
            spec,
            () => GetRatio(key),
            v => AdjustRatio(key, v),
            null /* AdjustRatio already calls markDirty */);
    }

    private float GetRatio(string key)
    {
        switch (key)
        {
            case "CavalryRatio":     return _config.GlobalDefaults.CavalryRatio;
            case "HorseArcherRatio": return _config.GlobalDefaults.HorseArcherRatio;
            case "InfantryRatio":    return _config.GlobalDefaults.InfantryRatio;
            case "RangedRatio":      return _config.GlobalDefaults.RangedRatio;
            default: return 0f;
        }
    }

    private void SetRatio(string key, float value)
    {
        switch (key)
        {
            case "CavalryRatio":     _config.GlobalDefaults.CavalryRatio     = value; break;
            case "HorseArcherRatio": _config.GlobalDefaults.HorseArcherRatio = value; break;
            case "InfantryRatio":    _config.GlobalDefaults.InfantryRatio    = value; break;
            case "RangedRatio":      _config.GlobalDefaults.RangedRatio      = value; break;
        }
    }

    private void RefreshRatioSum()
    {
        var d = _config.GlobalDefaults;
        double sum = d.CavalryRatio + d.HorseArcherRatio + d.InfantryRatio + d.RangedRatio;
        RatioSumText = "Σ = " + sum.ToString("0.00");
        RatioSumOk = sum >= 0.9 && sum <= 1.1;
    }

    private void RefreshShowRatioReset()
    {
        var d = _config.GlobalDefaults;
        ShowRatioReset = System.Math.Abs(d.CavalryRatio - 0.20f) > 0.001f
                      || System.Math.Abs(d.HorseArcherRatio - 0.05f) > 0.001f
                      || System.Math.Abs(d.InfantryRatio - 0.50f) > 0.001f
                      || System.Math.Abs(d.RangedRatio - 0.25f) > 0.001f;
    }

    // ── culture ──

    private void BuildCultureChips()
    {
        CultureChips.Clear();
        foreach (var opt in CultureOptions)
        {
            var localValue = opt.Value;
            CultureChips.Add(new ChipVM(
                ControlPanelLoc.Tr(opt.LabelZh, opt.LabelEn),
                () =>
                {
                    _config.GlobalDefaults.GenericCultureFilter = localValue;
                    _markDirty();
                    RefreshCultureState();
                }));
        }
    }

    private void RefreshCultureState()
    {
        var current = _config.GlobalDefaults.GenericCultureFilter ?? "PlayerCulture";
        string hint = "";
        for (int i = 0; i < CultureOptions.Length; i++)
        {
            bool active = CultureOptions[i].Value == current;
            CultureChips[i].IsActive = active;
            if (active) hint = ControlPanelLoc.Tr(CultureOptions[i].HintZh, CultureOptions[i].HintEn);
        }
        CultureFilterHint = hint;
        ShowCultureReset = current != "PlayerCulture";
    }

    // ── tier ──

    private void BuildTierChips()
    {
        MinTierChips.Clear();
        MaxTierChips.Clear();
        for (int tier = 1; tier <= 6; tier++)
        {
            var t = tier;
            MinTierChips.Add(new ChipVM(t.ToString(), () =>
            {
                _config.GlobalDefaults.MinTier = t;
                if (_config.GlobalDefaults.MaxTier < t)
                    _config.GlobalDefaults.MaxTier = t;
                _markDirty();
                RefreshTierState();
            }));
            MaxTierChips.Add(new ChipVM(t.ToString(), () =>
            {
                if (t >= _config.GlobalDefaults.MinTier)
                {
                    _config.GlobalDefaults.MaxTier = t;
                    _markDirty();
                    RefreshTierState();
                }
            }));
        }
    }

    private void RefreshTierState()
    {
        var d = _config.GlobalDefaults;
        for (int i = 0; i < 6; i++)
        {
            int tier = i + 1;
            MinTierChips[i].IsActive = tier == d.MinTier;
            MaxTierChips[i].IsActive  = tier == d.MaxTier;
            MaxTierChips[i].IsDimmed  = tier < d.MinTier;
        }
        TierRangeText = ControlPanelLoc.Tr("当前范围：", "Current range: ")
            + $"T{d.MinTier} – T{d.MaxTier}";
        ShowTierReset = d.MinTier != 2 || d.MaxTier != 5;
    }
}
