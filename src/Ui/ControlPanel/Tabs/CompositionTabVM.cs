using System;
using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 2「兵种编制 / Troop composition」VM。
/// 2026-05-29 简化：role 比例滑条全删 (rule 不再带 role 比例)；保留文化过滤 chips + 模式开关 + tier 占位。
/// </summary>
public sealed class CompositionTabVM : ViewModel
{
    private readonly GlobalConfig _config;
    private readonly Action _markDirty;
    private readonly Action _gotoTemplates;

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

    // ── tier range (predigest, reserved) ──
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
            "当前招募仅按文化过滤；驻军总头数由调度器（财政自治）自动决定。",
            "Recruitment is filtered by culture only; total garrison headcount is set automatically by the fiscal-autonomy dispatcher.");

        GenericModeLabel = ControlPanelLoc.Tr("通用比例匹配", "Generic ratio matching");
        GenericModeDesc  = ControlPanelLoc.Tr(
            "按下方文化过滤招募，不读取具体兵种模板。省心，适合大多数玩家。",
            "Recruit by the culture filter below, without reading a specific troop template. Low-effort, suits most players.");
        ExactModeLabel   = ControlPanelLoc.Tr("精确兵员模板（预留）", "Exact troop template (reserved)");
        ExactModeDesc    = ControlPanelLoc.Tr(
            "后续版本会用于具体兵种模板；当前不会改变招募决策。",
            "A later version will use this for concrete troop templates; it does not change recruitment decisions yet.");

        ExactModeCardTitle  = ControlPanelLoc.Tr("「精确兵员模板」暂未启用", "\"Exact troop template\" is not active yet");
        ExactModeCardDesc1  = ControlPanelLoc.Tr(
            "模板入口会保留在界面中，方便后续接入；当前招募仅走文化过滤。",
            "The template entry stays in the UI for future wiring; recruitment currently filters by culture only.");
        ExactModeCardNoEffect = ControlPanelLoc.Tr("当前不生效", "not active yet");
        ExactModeCardDesc2  = ControlPanelLoc.Tr("（预留给后续功能）。", " (reserved for a later feature).");
        GoToTemplatesLabel  = ControlPanelLoc.Tr("前往「兵员模板」标签页 →", "Go to the \"Templates\" tab →");

        CultureSectionLabel = ControlPanelLoc.Tr("文化过滤", "Culture filter");
        CultureResetLabel   = ControlPanelLoc.Tr("↺ 恢复默认（玩家文化）", "↺ Reset to default (player culture)");

        TierSectionLabel = ControlPanelLoc.Tr("Tier 范围（预留）", "Tier range (reserved)");
        MinTierLabel     = ControlPanelLoc.Tr("最低 Tier", "Min tier");
        MaxTierLabel     = ControlPanelLoc.Tr("最高 Tier", "Max tier");
        TierHintPrefix   = ControlPanelLoc.Tr("当前通用匹配不读取此设置，保留给后续精确模板。", "Generic matching does not read this setting yet; reserved for the later exact template.");
        TierResetLabel   = ControlPanelLoc.Tr("↺ 恢复默认 T2 – T5", "↺ Reset to default T2 – T5");

        // ── mode ──
        _isGenericMode = true;

        // ── culture chips ──
        BuildCultureChips();
        RefreshCultureState();

        // ── tier chips ──
        BuildTierChips();
        RefreshTierState();
    }

    // ══════════════════════════════════════════════
    //  Commands
    // ══════════════════════════════════════════════

    public void ExecuteSetGenericMode()
    {
        // PR-5'(2026-05-24): UseGenericMatching removed; always generic — no-op.
    }

    public void ExecuteSetExactMode()
    {
        // PR-5'(2026-05-24): UseGenericMatching removed; exact mode no longer exists — no-op.
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

    public void ExecuteResetTier()
    {
        // PR-5'(2026-05-24): MinTier/MaxTier removed from TownGarrisonRule — no-op.
    }

    // ══════════════════════════════════════════════
    //  Internal helpers
    // ══════════════════════════════════════════════

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
        // PR-5'(2026-05-24): MinTier/MaxTier removed from TownGarrisonRule; tier chips are inert.
        MinTierChips.Clear();
        MaxTierChips.Clear();
        for (int tier = 1; tier <= 6; tier++)
        {
            MinTierChips.Add(new ChipVM(tier.ToString(), () => { }) { IsDimmed = true });
            MaxTierChips.Add(new ChipVM(tier.ToString(), () => { }) { IsDimmed = true });
        }
    }

    private void RefreshTierState()
    {
        // PR-5'(2026-05-24): MinTier/MaxTier removed; kept as a visible reserved affordance.
        TierRangeText = ControlPanelLoc.Tr("预留：当前不影响招募。", "Reserved: currently does not affect recruitment.");
        ShowTierReset = false;
    }
}
