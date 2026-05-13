using System;
using SovereignTowns.Configuration;
using SovereignTowns.Templates;
using SovereignTowns.Ui.ConfigScreen.Options;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ConfigScreen;

/// <summary>
/// Top-level view-model for the Sovereign Towns config Gauntlet screen.
/// Maintains two MBBindingList collections (feature toggles + numeric global defaults) and
/// exposes <c>ExecuteClose</c> / <c>ExecuteSave</c> as XML <c>Command.Click</c> targets.
/// </summary>
/// <remarks>
/// Each option's setter mutates <see cref="ConfigurationManager.Current"/> immediately, so the
/// VM holds no editing state of its own. The two button handlers just flip <see cref="IsFinished"/>;
/// the actual <c>ScreenManager.PopScreen()</c> + <c>ConfigurationManager.Save()</c> happens in
/// <see cref="SovereignTownsConfigScreen.OnFrameTick"/> to avoid tearing the layer down from inside
/// a click callback.
/// </remarks>
public sealed class SovereignTownsConfigVM : ViewModel
{
    private readonly Action<bool>? _finishCallback;
    private string _title = "Sovereign Towns 控制面板";
    private string _featuresHeader = "功能开关";
    private string _budgetHeader = "数量与预算";
    private string _ratiosHeader = "兵种与 Tier 比例";
    private string _templatesHeader = "模板与资源";
    private string _perSettlementHeader = "按城/堡覆盖（可选）";
    private string _closeText = "关闭";
    private string _saveText = "保存并关闭";
    private string _ratioSumText = "";
    private string _ratioSumWarning = "";
    private string _settlementSelectorsEmptyHint = "";
    private int _selectedTabIndex; // 0..4 — drives the 5 IsXxxTab boolean properties below.
    private MBBindingList<STToggleOptionVM> _toggleOptions;
    private MBBindingList<STButtonOptionVM> _templateButtons;
    private MBBindingList<STNumericOptionVM> _budgetNumerics;
    private MBBindingList<STNumericOptionVM> _ratioNumerics;
    private MBBindingList<STNumericOptionVM> _resourceNumerics;
    private MBBindingList<STSettlementSelectorVM> _settlementSelectors;

    // ratio sum 容忍区间（与 ConfigurationManager.ValidateRule 中常量保持一致）
    private const float RatioSumMin = 0.9f;
    private const float RatioSumMax = 1.1f;

    /// <summary>Set to true by Close or Save click; polled by ScreenBase to pop the screen.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>True when Save was clicked (vs. Close). Drives whether
    /// <see cref="ConfigurationManager.Save"/> runs on teardown.</summary>
    public bool SaveOnClose { get; private set; }

    [DataSourceProperty]
    public string Title
    {
        get => _title;
        set { if (value != _title) { _title = value ?? ""; OnPropertyChanged(nameof(Title)); } }
    }

    [DataSourceProperty]
    public string FeaturesHeader
    {
        get => _featuresHeader;
        set { if (value != _featuresHeader) { _featuresHeader = value ?? ""; OnPropertyChanged(nameof(FeaturesHeader)); } }
    }

    [DataSourceProperty]
    public string BudgetHeader
    {
        get => _budgetHeader;
        set { if (value != _budgetHeader) { _budgetHeader = value ?? ""; OnPropertyChanged(nameof(BudgetHeader)); } }
    }

    [DataSourceProperty]
    public string RatiosHeader
    {
        get => _ratiosHeader;
        set { if (value != _ratiosHeader) { _ratiosHeader = value ?? ""; OnPropertyChanged(nameof(RatiosHeader)); } }
    }

    [DataSourceProperty]
    public string TemplatesHeader
    {
        get => _templatesHeader;
        set { if (value != _templatesHeader) { _templatesHeader = value ?? ""; OnPropertyChanged(nameof(TemplatesHeader)); } }
    }

    // Static tab-bar labels. No setters / no OnPropertyChanged — constant once VM exists.
    [DataSourceProperty] public string Tab1Title => "功能开关";
    [DataSourceProperty] public string Tab2Title => "数量与预算";
    [DataSourceProperty] public string Tab3Title => "兵种与 Tier 比例";
    [DataSourceProperty] public string Tab4Title => "模板与资源";
    [DataSourceProperty] public string Tab5Title => "按城/堡覆盖";

    // Five derived booleans driven by _selectedTabIndex.
    // XML: ListPanel IsVisible="@IsXxxTab"; tab button IsSelected="@IsXxxTab".
    [DataSourceProperty] public bool IsFeaturesTab    => _selectedTabIndex == 0;
    [DataSourceProperty] public bool IsBudgetTab      => _selectedTabIndex == 1;
    [DataSourceProperty] public bool IsRatiosTab      => _selectedTabIndex == 2;
    [DataSourceProperty] public bool IsTemplatesTab   => _selectedTabIndex == 3;
    [DataSourceProperty] public bool IsSettlementsTab => _selectedTabIndex == 4;

    [DataSourceProperty]
    public string PerSettlementHeader
    {
        get => _perSettlementHeader;
        set { if (value != _perSettlementHeader) { _perSettlementHeader = value ?? ""; OnPropertyChanged(nameof(PerSettlementHeader)); } }
    }

    /// <summary>Placeholder text shown when the player owns no town/castle.
    /// XML <c>@SettlementSelectorsEmptyHint</c>.</summary>
    [DataSourceProperty]
    public string SettlementSelectorsEmptyHint
    {
        get => _settlementSelectorsEmptyHint;
        set { if (value != _settlementSelectorsEmptyHint) { _settlementSelectorsEmptyHint = value ?? ""; OnPropertyChanged(nameof(SettlementSelectorsEmptyHint)); } }
    }

    [DataSourceProperty]
    public string CloseText
    {
        get => _closeText;
        set { if (value != _closeText) { _closeText = value ?? ""; OnPropertyChanged(nameof(CloseText)); } }
    }

    [DataSourceProperty]
    public string SaveText
    {
        get => _saveText;
        set { if (value != _saveText) { _saveText = value ?? ""; OnPropertyChanged(nameof(SaveText)); } }
    }

    /// <summary>
    /// 5 个兵种占比的实时总和（"Cav+Inf+Arc+Crossbow+Thrower = 1.00"）。
    /// 任意一个 Ratio slider 触发时刷新。XML 用 <c>@RatioSumText</c> 绑定。
    /// </summary>
    [DataSourceProperty]
    public string RatioSumText
    {
        get => _ratioSumText;
        set { if (value != _ratioSumText) { _ratioSumText = value ?? ""; OnPropertyChanged(nameof(RatioSumText)); } }
    }

    /// <summary>
    /// 当 ratio sum ∉ [0.9, 1.1] 时给出红字警告；否则空串。
    /// 同样兜底 Save 校验失败时把 Validator.reason 投递到此处。XML 用 <c>@RatioSumWarning</c> 绑定。
    /// </summary>
    [DataSourceProperty]
    public string RatioSumWarning
    {
        get => _ratioSumWarning;
        set { if (value != _ratioSumWarning) { _ratioSumWarning = value ?? ""; OnPropertyChanged(nameof(RatioSumWarning)); } }
    }

    /// <summary>Toggle (boolean) options. Bound from XML as <c>{ToggleOptions}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STToggleOptionVM> ToggleOptions
    {
        get => _toggleOptions;
        set
        {
            if (value != _toggleOptions)
            {
                _toggleOptions = value;
                OnPropertyChanged(nameof(ToggleOptions));
            }
        }
    }

    /// <summary>Tab 2 numerics: 目标人数 / 最少防守 / 预算 / Min-MaxTier / 战时-和平倍率.
    /// Bound from XML as <c>{BudgetNumerics}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STNumericOptionVM> BudgetNumerics
    {
        get => _budgetNumerics;
        set
        {
            if (value != _budgetNumerics)
            {
                _budgetNumerics = value;
                OnPropertyChanged(nameof(BudgetNumerics));
            }
        }
    }

    /// <summary>Tab 3 numerics: 5 兵种 + 6 Tier 占比. XML: <c>{RatioNumerics}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STNumericOptionVM> RatioNumerics
    {
        get => _ratioNumerics;
        set
        {
            if (value != _ratioNumerics)
            {
                _ratioNumerics = value;
                OnPropertyChanged(nameof(RatioNumerics));
            }
        }
    }

    /// <summary>Tab 4 (bottom) numerics: 食物 / XP / Conformity / 征兵护卫 / 村庄冷却 / 回首府阈值.
    /// XML: <c>{ResourceNumerics}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STNumericOptionVM> ResourceNumerics
    {
        get => _resourceNumerics;
        set
        {
            if (value != _resourceNumerics)
            {
                _resourceNumerics = value;
                OnPropertyChanged(nameof(ResourceNumerics));
            }
        }
    }

    /// <summary>Tab 4 (top) buttons: ExactTroopTemplate editor + 3 TrainingTemplate Apply.
    /// XML: <c>{TemplateButtons}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STButtonOptionVM> TemplateButtons
    {
        get => _templateButtons;
        set
        {
            if (value != _templateButtons)
            {
                _templateButtons = value;
                OnPropertyChanged(nameof(TemplateButtons));
            }
        }
    }

    /// <summary>One row per player-owned Town/Castle. Bound from XML as <c>{SettlementSelectors}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STSettlementSelectorVM> SettlementSelectors
    {
        get => _settlementSelectors;
        set
        {
            if (value != _settlementSelectors)
            {
                _settlementSelectors = value;
                OnPropertyChanged(nameof(SettlementSelectors));
            }
        }
    }

    public SovereignTownsConfigVM(Action<bool>? finishCallback = null)
    {
        _finishCallback = finishCallback;
        _toggleOptions     = new MBBindingList<STToggleOptionVM>();
        _templateButtons   = new MBBindingList<STButtonOptionVM>();
        _budgetNumerics    = new MBBindingList<STNumericOptionVM>();
        _ratioNumerics     = new MBBindingList<STNumericOptionVM>();
        _resourceNumerics  = new MBBindingList<STNumericOptionVM>();
        _settlementSelectors = new MBBindingList<STSettlementSelectorVM>();
        BuildToggleOptions();
        BuildBudgetNumerics();
        BuildRatioNumerics();
        BuildResourceNumerics();
        BuildTemplateButtons();
        BuildSettlementSelectors();
        RecomputeRatioSum();
        RefreshValues();
    }

    /// <summary>
    /// XML <c>Command.Click="SelectXxxTab"</c> targets. Toggling _selectedTabIndex and
    /// pushing OnPropertyChanged for the 5 IsXxxTab booleans causes each section's
    /// <c>IsVisible="@IsXxxTab"</c> to update, switching pages.
    /// </summary>
    public void SelectFeaturesTab()    => SetTab(0);
    public void SelectBudgetTab()      => SetTab(1);
    public void SelectRatiosTab()      => SetTab(2);
    public void SelectTemplatesTab()   => SetTab(3);
    public void SelectSettlementsTab() => SetTab(4);

    private void SetTab(int idx)
    {
        try
        {
            if (_selectedTabIndex == idx) return;
            _selectedTabIndex = idx;
            OnPropertyChanged(nameof(IsFeaturesTab));
            OnPropertyChanged(nameof(IsBudgetTab));
            OnPropertyChanged(nameof(IsRatiosTab));
            OnPropertyChanged(nameof(IsTemplatesTab));
            OnPropertyChanged(nameof(IsSettlementsTab));
        }
        catch (Exception ex) { Logger.Error($"SetTab({idx}) failed", ex); }
    }

    /// <summary>
    /// Walk <c>Town.AllTowns</c> (already covers both Town and Castle entities), keep only those
    /// whose <c>OwnerClan == Clan.PlayerClan</c>, and add one <see cref="STSettlementSelectorVM"/>
    /// row per settlement. Empty list → set <see cref="SettlementSelectorsEmptyHint"/> placeholder.
    /// </summary>
    private void BuildSettlementSelectors()
    {
        try
        {
            _settlementSelectors.Clear();

            var playerClan = TaleWorlds.CampaignSystem.Clan.PlayerClan;
            if (playerClan is null)
            {
                _settlementSelectorsEmptyHint = "玩家阵营未就绪";
                OnPropertyChanged(nameof(SettlementSelectorsEmptyHint));
                return;
            }

            int added = 0;
            foreach (var t in TaleWorlds.CampaignSystem.Settlements.Town.AllTowns)
            {
                try
                {
                    if (t is null || t.Settlement is null) continue;
                    if (t.OwnerClan != playerClan) continue;

                    var s = t.Settlement;
                    string id = s.StringId ?? "";
                    if (string.IsNullOrEmpty(id)) continue;

                    string displayName = s.Name?.ToString() ?? id;
                    bool isCastle = s.IsCastle;

                    _settlementSelectors.Add(new STSettlementSelectorVM(
                        settlementStringId: id,
                        displayName: displayName,
                        isCastle: isCastle,
                        cloneFromDefaults: () => ConfigurationManager.Current.GlobalDefaults.Clone()));
                    added++;
                }
                catch (Exception ex)
                {
                    Logger.Error("BuildSettlementSelectors: failed to add a row, skipping", ex);
                }
            }

            SettlementSelectorsEmptyHint = added == 0
                ? "玩家暂无城/堡。占领后再回到此面板配置覆盖规则。"
                : "";
        }
        catch (Exception ex)
        {
            Logger.Error("BuildSettlementSelectors failed", ex);
            SettlementSelectorsEmptyHint = "加载城/堡列表失败（详情见日志）。";
        }
    }

    /// <summary>
    /// 重算 5 个兵种占比之和并更新警告文本。在每个 ratio slider 提交时调用，
    /// 以及构造时 / ExecuteSave 失败兜底时调用。
    /// </summary>
    private void RecomputeRatioSum()
    {
        try
        {
            var g = ConfigurationManager.Current?.GlobalDefaults;
            if (g is null)
            {
                RatioSumText = "Σ = ?";
                RatioSumWarning = "";
                return;
            }
            float troopSum = g.CavalryRatio + g.InfantryRatio + g.ArcherRatio + g.CrossbowRatio + g.ThrowerRatio;
            float tierSum = g.Tier1Ratio + g.Tier2Ratio + g.Tier3Ratio + g.Tier4Ratio + g.Tier5Ratio + g.Tier6Ratio;
            RatioSumText = $"兵种 Σ = {troopSum:F2} / Tier Σ = {tierSum:F2}";
            if (troopSum < RatioSumMin || troopSum > RatioSumMax)
            {
                RatioSumWarning = $"警告：兵种占比之和 {troopSum:F2} 不在 [{RatioSumMin:F2}, {RatioSumMax:F2}] 区间；保存将被拒绝。";
            }
            else if (tierSum < RatioSumMin || tierSum > RatioSumMax)
            {
                RatioSumWarning = $"警告：Tier 占比之和 {tierSum:F2} 不在 [{RatioSumMin:F2}, {RatioSumMax:F2}] 区间；保存将被拒绝。";
            }
            else
            {
                RatioSumWarning = "";
            }
        }
        catch { /* never crash UI from a setter callback */ }
    }

    /// <summary>Tab 1 (功能开关): 11 boolean toggles for EnabledFeatures + UseGenericMatching.</summary>
    private void BuildToggleOptions()
    {
        var cfg = ConfigurationManager.Current;
        var features = cfg.EnabledFeatures;
        var globals = cfg.GlobalDefaults;

        // -- EnabledFeatures (6 toggles) --
        _toggleOptions.Add(new STToggleOptionVM(
            "自动驻军",
            "自动维持驻军规模到 TargetTotalCount。",
            features.AutoGarrison,
            v => ConfigurationManager.Current.EnabledFeatures.AutoGarrison = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "自动招募",
            "在领地内自动招募新兵补充驻军。",
            features.AutoRecruitment,
            v => ConfigurationManager.Current.EnabledFeatures.AutoRecruitment = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "自动巡逻",
            "自动派出巡逻队保护领地。",
            features.AutoPatrol,
            v => ConfigurationManager.Current.EnabledFeatures.AutoPatrol = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "城堡支持",
            "对玩家归属城堡同等启用上述功能。",
            features.CastleSupport,
            v => ConfigurationManager.Current.EnabledFeatures.CastleSupport = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "LLM 推理建议",
            "启用 LLM 提供决策建议（仅建议，不动手）。",
            features.LlmReasoning,
            v => ConfigurationManager.Current.EnabledFeatures.LlmReasoning = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "LLM 自动执行",
            "允许 LLM 直接执行决策（高风险）。",
            features.LlmAutoExecute,
            v => ConfigurationManager.Current.EnabledFeatures.LlmAutoExecute = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "主动出击",
            "无巡逻队时附近有敌对势力则出城攻击。",
            features.SallyForth,
            v => ConfigurationManager.Current.EnabledFeatures.SallyForth = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "战利品：招募匹配俘虏",
            "巡逻/出击队战后俘虏若兵种匹配首府目标桶(非零 ratio)，直接进首府驻军。",
            features.AutoRecruitMatchingPrisoners,
            v => ConfigurationManager.Current.EnabledFeatures.AutoRecruitMatchingPrisoners = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "战利品：出售非匹配俘虏",
            "招募后剩余的非匹配俘虏自动卖到最近自家 town。",
            features.AutoSellNonMatchingPrisoners,
            v => ConfigurationManager.Current.EnabledFeatures.AutoSellNonMatchingPrisoners = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "战利品：出售装备物品",
            "战后缴获的装备/物品自动卖到最近自家 town，金钱回流玩家。",
            features.AutoSellLoot,
            v => ConfigurationManager.Current.EnabledFeatures.AutoSellLoot = v));

        _toggleOptions.Add(new STToggleOptionVM(
            "通用匹配（忽略文化）",
            "开启：忽略阵营，候选兵能升级到与模板目标同 Tier+同兵种类型的任意兵即匹配；关闭：完全 IG 风格，候选必须能升级到模板里的具体目标兵种。",
            globals.UseGenericMatching,
            v =>
            {
                TroopTemplateModeService.SetUseGenericMatching(ConfigurationManager.Current.GlobalDefaults, v);
                RecomputeRatioSum();
            }));
    }

    /// <summary>Tab 2 (数量与预算): TargetTotalCount, MinimumDefenders, BudgetLimit, Min/MaxTier,
    /// Wartime/PeacetimeMultiplier — 7 sliders driving GlobalDefaults size & cost knobs.</summary>
    private void BuildBudgetNumerics()
    {
        var globals = ConfigurationManager.Current.GlobalDefaults;

        _budgetNumerics.Add(new STNumericOptionVM(
            "目标驻军总数",
            "驻军应维持的目标兵员数 (50–500)。",
            min: 50, max: 500, current: globals.TargetTotalCount, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.TargetTotalCount = (int)v));

        _budgetNumerics.Add(new STNumericOptionVM(
            "最少防守人数",
            "无论目标人数为何，至少保留的防守人数 (0–300)。",
            min: 0, max: 300, current: globals.MinimumDefenders, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.MinimumDefenders = (int)v));

        _budgetNumerics.Add(new STNumericOptionVM(
            "招募预算上限",
            "单日招募预算上限 denar (0–50000)。",
            min: 0, max: 50000, current: globals.BudgetLimit, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.BudgetLimit = (int)v));

        _budgetNumerics.Add(new STNumericOptionVM(
            "最低 Tier",
            "允许招募的最低兵种 Tier（含）；范围 1–6。",
            min: 1, max: 6, current: globals.MinTier, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.MinTier = (int)v));

        _budgetNumerics.Add(new STNumericOptionVM(
            "最高 Tier",
            "允许招募的最高兵种 Tier（含）；范围 1–6。",
            min: 1, max: 6, current: globals.MaxTier, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.MaxTier = (int)v));

        _budgetNumerics.Add(new STNumericOptionVM(
            "战时目标乘数",
            "处于战争状态时，TargetTotalCount 的乘数 (0.5–2.0)。",
            min: 0.5f, max: 2.0f, current: globals.WartimeMultiplier, isDiscrete: false,
            v => ConfigurationManager.Current.GlobalDefaults.WartimeMultiplier = v));

        _budgetNumerics.Add(new STNumericOptionVM(
            "和平目标乘数",
            "和平时期 TargetTotalCount 的乘数 (0.5–2.0)。",
            min: 0.5f, max: 2.0f, current: globals.PeacetimeMultiplier, isDiscrete: false,
            v => ConfigurationManager.Current.GlobalDefaults.PeacetimeMultiplier = v));
    }

    /// <summary>Tab 3 (兵种与 Tier 比例): 5 troop + 6 tier ratios in two RatioOptionGroup buckets
    /// for auto-normalization. Σ readout shown via RatioSumText.</summary>
    private void BuildRatioNumerics()
    {
        var globals = ConfigurationManager.Current.GlobalDefaults;

        var troopRatios = new STRatioOptionGroup(RecomputeRatioSum);
        _ratioNumerics.Add(troopRatios.Add(
            "骑兵占比",
            "通用匹配：所有文化/阵营的骑兵与骑射都按此比例计入。",
            globals.CavalryRatio,
            v => ConfigurationManager.Current.GlobalDefaults.CavalryRatio = v));

        _ratioNumerics.Add(troopRatios.Add(
            "步兵占比",
            "通用匹配：盾兵、枪兵、双手步兵等步行近战兵。",
            globals.InfantryRatio,
            v => ConfigurationManager.Current.GlobalDefaults.InfantryRatio = v));

        _ratioNumerics.Add(troopRatios.Add(
            "弓手占比",
            "通用匹配：步行弓手，不限制文化。",
            globals.ArcherRatio,
            v => ConfigurationManager.Current.GlobalDefaults.ArcherRatio = v));

        _ratioNumerics.Add(troopRatios.Add(
            "弩手占比",
            "通用匹配：装备弩的步行远程兵。",
            globals.CrossbowRatio,
            v => ConfigurationManager.Current.GlobalDefaults.CrossbowRatio = v));

        _ratioNumerics.Add(troopRatios.Add(
            "投掷兵占比",
            "通用匹配：标枪、飞斧、飞刀或 Skirmisher 编队兵种。",
            globals.ThrowerRatio,
            v => ConfigurationManager.Current.GlobalDefaults.ThrowerRatio = v));
        troopRatios.NormalizeInitial();

        var tierRatios = new STRatioOptionGroup(RecomputeRatioSum);
        _ratioNumerics.Add(tierRatios.Add(
            "Tier 1 占比",
            "通用匹配：目标驻军中 Tier 1 兵员比例。",
            globals.Tier1Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier1Ratio = v));

        _ratioNumerics.Add(tierRatios.Add(
            "Tier 2 占比",
            "通用匹配：目标驻军中 Tier 2 兵员比例。",
            globals.Tier2Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier2Ratio = v));

        _ratioNumerics.Add(tierRatios.Add(
            "Tier 3 占比",
            "通用匹配：目标驻军中 Tier 3 兵员比例。",
            globals.Tier3Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier3Ratio = v));

        _ratioNumerics.Add(tierRatios.Add(
            "Tier 4 占比",
            "通用匹配：目标驻军中 Tier 4 兵员比例。",
            globals.Tier4Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier4Ratio = v));

        _ratioNumerics.Add(tierRatios.Add(
            "Tier 5 占比",
            "通用匹配：目标驻军中 Tier 5 兵员比例。",
            globals.Tier5Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier5Ratio = v));

        _ratioNumerics.Add(tierRatios.Add(
            "Tier 6 占比",
            "通用匹配：目标驻军中 Tier 6+ 兵员比例。",
            globals.Tier6Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier6Ratio = v));
        tierRatios.NormalizeInitial();
    }

    /// <summary>Tab 4 (模板与资源) top: ExactTroopTemplate edit button + 3 TrainingTemplate Apply
    /// buttons (FrontierDefense / TradeHub / EliteNoble).</summary>
    private void BuildTemplateButtons()
    {
        var globals = ConfigurationManager.Current.GlobalDefaults;
        STButtonOptionVM? exactTemplateButton = null;

        exactTemplateButton = new STButtonOptionVM(
            "具体兵员模板",
            "打开原版部队管理页编辑 IG 风格的目标兵种和数量；左侧保存为模板，右侧为可选兵种。",
            $"编辑士兵定义 ({globals.ExactTroopTemplate.Count})",
            isVisible: true,
            () => ExactTroopTemplateEditor.OpenForRule(
                ConfigurationManager.Current.GlobalDefaults,
                "GlobalDefaults",
                () =>
                {
                    exactTemplateButton!.ButtonText = $"编辑士兵定义 ({ConfigurationManager.Current.GlobalDefaults.ExactTroopTemplate.Count})";
                    RecomputeRatioSum();
                }));
        _templateButtons.Add(exactTemplateButton);

        // 3 TrainingTemplate Apply buttons. Each click overwrites GlobalDefaults' numeric/ratio fields
        // with the preset's rule, **preserving** ExactTroopTemplate + EnabledFeatures + PerSettlement.
        _templateButtons.Add(new STButtonOptionVM(
            "预设：边疆防御",
            "高目标驻军、高战时倍率、防御兵种为主。覆盖目标人数 / 兵种比例 / Tier 比例 / 倍率；不动你的功能开关、具体兵员模板和按城堡覆盖。",
            "应用预设",
            isVisible: true,
            () => ApplyTrainingTemplate(TrainingTemplate.FrontierDefense())));

        _templateButtons.Add(new STButtonOptionVM(
            "预设：贸易枢纽",
            "低驻军规模、低战时倍率、兵种平衡、预算紧缩。覆盖目标人数 / 兵种比例 / Tier 比例 / 倍率；不动你的功能开关、具体兵员模板和按城堡覆盖。",
            "应用预设",
            isVisible: true,
            () => ApplyTrainingTemplate(TrainingTemplate.TradeHub())));

        _templateButtons.Add(new STButtonOptionVM(
            "预设:精锐贵族",
            "中等规模、高 Tier、高战时倍率、骑兵+弓手为主。覆盖目标人数 / 兵种比例 / Tier 比例 / 倍率；不动你的功能开关、具体兵员模板和按城堡覆盖。",
            "应用预设",
            isVisible: true,
            () => ApplyTrainingTemplate(TrainingTemplate.EliteNobleGarrison())));
    }

    /// <summary>Tab 4 (模板与资源) bottom: food / XP / conformity / escort / cooldown / return threshold.
    /// Mix of GlobalDefaults numeric fields and GlobalConfig top-level fields.</summary>
    private void BuildResourceNumerics()
    {
        var cfg = ConfigurationManager.Current;
        var globals = cfg.GlobalDefaults;

        _resourceNumerics.Add(new STNumericOptionVM(
            "食物安全阈值",
            "Town.FoodChange 低于此值时暂停招募，避免饿城 (-50 ~ 50)。",
            min: -50, max: 50, current: globals.FoodSafetyThreshold, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.FoodSafetyThreshold = v));

        _resourceNumerics.Add(new STNumericOptionVM(
            "每日驻军 XP 奖励",
            "每日给驻军每个非 hero 兵员注入的固定 XP (0–30)。",
            min: 0, max: 30, current: globals.DailyTroopXpBonus, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.DailyTroopXpBonus = (int)v));

        _resourceNumerics.Add(new STNumericOptionVM(
            "每日俘虏 Conformity",
            "每日为驻军中每名俘虏累加的 conformity XP (0–30)。",
            min: 0, max: 30, current: cfg.DailyPrisonerConformityAmount, isDiscrete: true,
            v => ConfigurationManager.Current.DailyPrisonerConformityAmount = (int)v));

        _resourceNumerics.Add(new STNumericOptionVM(
            "征兵护卫数",
            "征兵队出发时从首府 GarrisonParty 抽取多少低 Tier 兵作为基础护卫 (0–50)。",
            min: 0, max: 50, current: cfg.RecruiterEscortSize, isDiscrete: true,
            v => ConfigurationManager.Current.RecruiterEscortSize = (int)v));

        _resourceNumerics.Add(new STNumericOptionVM(
            "村庄招募冷却",
            "同一 village 被招过后多少小时内不再列为候选 (12–240)。",
            min: 12, max: 240, current: cfg.VillageCooldownHours, isDiscrete: true,
            v => ConfigurationManager.Current.VillageCooldownHours = (int)v));

        _resourceNumerics.Add(new STNumericOptionVM(
            "征兵队回首府阈值",
            "征兵队总人数达此值立即回首府 (10–200)。",
            min: 10, max: 200, current: cfg.RecruiterReturnThreshold, isDiscrete: true,
            v => ConfigurationManager.Current.RecruiterReturnThreshold = (int)v));

        // 说明：IdleHoursBeforeDisband 当前是 PartyLifecycleManager.cs 中的 const，
        // 不属于 TownGarrisonRule POCO；按任务指引跳过并记录日志。
        Logger.Info("ConfigScreen: skipping IdleHoursBeforeDisband (currently a const in PartyLifecycleManager, not a config field)");
    }

    /// <summary>Apply a <see cref="TrainingTemplate"/> preset to GlobalDefaults.
    /// Overwrites numeric / ratio fields. **Preserves** ExactTroopTemplate (user's custom troops),
    /// UseGenericMatching (treated as a user-mode preference), EnabledFeatures (top-level config),
    /// and PerSettlementOverrides. After overwrite, rebuilds the 3 numeric collections so the
    /// slider rows reflect the new values; refreshes Σ readout.</summary>
    private void ApplyTrainingTemplate(TrainingTemplate t)
    {
        try
        {
            if (t?.Rule is null)
            {
                Logger.Warn("ApplyTrainingTemplate called with null template");
                return;
            }

            var current = ConfigurationManager.Current.GlobalDefaults;
            var src = t.Rule;

            current.TargetTotalCount    = src.TargetTotalCount;
            current.MinimumDefenders    = src.MinimumDefenders;
            current.BudgetLimit         = src.BudgetLimit;
            current.MinTier             = src.MinTier;
            current.MaxTier             = src.MaxTier;
            current.CavalryRatio        = src.CavalryRatio;
            current.InfantryRatio       = src.InfantryRatio;
            current.ArcherRatio         = src.ArcherRatio;
            current.CrossbowRatio       = src.CrossbowRatio;
            current.ThrowerRatio        = src.ThrowerRatio;
            current.Tier1Ratio          = src.Tier1Ratio;
            current.Tier2Ratio          = src.Tier2Ratio;
            current.Tier3Ratio          = src.Tier3Ratio;
            current.Tier4Ratio          = src.Tier4Ratio;
            current.Tier5Ratio          = src.Tier5Ratio;
            current.Tier6Ratio          = src.Tier6Ratio;
            current.WartimeMultiplier   = src.WartimeMultiplier;
            current.PeacetimeMultiplier = src.PeacetimeMultiplier;
            current.FoodSafetyThreshold = src.FoodSafetyThreshold;
            current.DailyTroopXpBonus   = src.DailyTroopXpBonus;
            // 不动：UseGenericMatching / ExactTroopTemplate / EnabledFeatures / PerSettlementOverrides

            Logger.Info($"Applied TrainingTemplate '{t.TemplateId}' to GlobalDefaults");

            // 重建 3 个 numeric 集合，让 slider rows 反映新值。Toggle / template button 集合不动
            // （toggle 列表里的 UseGenericMatching 没改；template button 列表无 numeric 状态）。
            _budgetNumerics.Clear();
            _ratioNumerics.Clear();
            _resourceNumerics.Clear();
            BuildBudgetNumerics();
            BuildRatioNumerics();
            BuildResourceNumerics();
            OnPropertyChanged(nameof(BudgetNumerics));
            OnPropertyChanged(nameof(RatioNumerics));
            OnPropertyChanged(nameof(ResourceNumerics));

            RecomputeRatioSum();
            RatioSumWarning = ""; // 清掉旧警告，新预设是合法的 (Σ=1.0)
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplyTrainingTemplate({t?.TemplateId}) failed", ex);
            RatioSumWarning = $"应用预设失败：{ex.Message}";
        }
    }

    /// <summary>XML <c>Command.Click="ExecuteClose"</c> target — close without persisting.</summary>
    public void ExecuteClose()
    {
        SaveOnClose = false;
        IsFinished = true;
        _finishCallback?.Invoke(false);
    }

    /// <summary>
    /// XML <c>Command.Click="ExecuteSave"</c> target — verify validation passes first.
    /// 校验失败：写入 <see cref="RatioSumWarning"/>（含 validator reason），不设 IsFinished，
    /// 玩家被留在面板继续修正；磁盘 last-known-good 保持完整。
    /// </summary>
    public void ExecuteSave()
    {
        try
        {
            if (!ConfigurationManager.TryValidateCurrent(out var reason))
            {
                RatioSumWarning = $"无法保存：{reason}";
                Logger.Warn($"ConfigScreen.ExecuteSave blocked by validator: {reason}");
                SaveOnClose = false;
                IsFinished = false;     // keep panel open so player can fix
                RecomputeRatioSum();    // also refresh Σ text in case ratios are the culprit
                return;
            }
            RatioSumWarning = "";
            SaveOnClose = true;
            IsFinished = true;
            _finishCallback?.Invoke(true);
        }
        catch (System.Exception ex)
        {
            Logger.Error("ExecuteSave threw", ex);
            // Fail closed: don't pop the screen on unexpected error; surface to UI.
            RatioSumWarning = $"保存异常：{ex.Message}";
            SaveOnClose = false;
            IsFinished = false;
        }
    }
}
