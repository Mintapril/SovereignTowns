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
    private string _globalsHeader = "全局默认规则";
    private string _perSettlementHeader = "按城/堡覆盖（可选）";
    private string _closeText = "关闭";
    private string _saveText = "保存并关闭";
    private string _ratioSumText = "";
    private string _ratioSumWarning = "";
    private string _settlementSelectorsEmptyHint = "";
    private MBBindingList<STToggleOptionVM> _toggleOptions;
    private MBBindingList<STNumericOptionVM> _numericOptions;
    private MBBindingList<STButtonOptionVM> _buttonOptions;
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
    public string GlobalsHeader
    {
        get => _globalsHeader;
        set { if (value != _globalsHeader) { _globalsHeader = value ?? ""; OnPropertyChanged(nameof(GlobalsHeader)); } }
    }

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

    /// <summary>Numeric (slider) options. Bound from XML as <c>{NumericOptions}</c>.</summary>
    [DataSourceProperty]
    public MBBindingList<STNumericOptionVM> NumericOptions
    {
        get => _numericOptions;
        set
        {
            if (value != _numericOptions)
            {
                _numericOptions = value;
                OnPropertyChanged(nameof(NumericOptions));
            }
        }
    }

    /// <summary>Command rows for global rule actions such as opening the exact troop template editor.</summary>
    [DataSourceProperty]
    public MBBindingList<STButtonOptionVM> ButtonOptions
    {
        get => _buttonOptions;
        set
        {
            if (value != _buttonOptions)
            {
                _buttonOptions = value;
                OnPropertyChanged(nameof(ButtonOptions));
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
        _toggleOptions = new MBBindingList<STToggleOptionVM>();
        _numericOptions = new MBBindingList<STNumericOptionVM>();
        _buttonOptions = new MBBindingList<STButtonOptionVM>();
        _settlementSelectors = new MBBindingList<STSettlementSelectorVM>();
        BuildOptions();
        BuildSettlementSelectors();
        RecomputeRatioSum();
        RefreshValues();
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

    /// <summary>Construct one row per editable field. Each setter writes through to
    /// <see cref="ConfigurationManager.Current"/>.</summary>
    private void BuildOptions()
    {
        var cfg = ConfigurationManager.Current;
        var features = cfg.EnabledFeatures;
        var globals = cfg.GlobalDefaults;
        STButtonOptionVM? exactTemplateButton = null;

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

        exactTemplateButton = new STButtonOptionVM(
            "具体兵员模板",
            "打开原版部队管理页编辑 IG 风格的目标兵种和数量；左侧保存为模板，右侧为可选兵种。",
            "编辑士兵定义",
            isVisible: true,
            () => ExactTroopTemplateEditor.OpenForRule(
                ConfigurationManager.Current.GlobalDefaults,
                "GlobalDefaults",
                () =>
                {
                    exactTemplateButton!.ButtonText = $"编辑士兵定义 ({ConfigurationManager.Current.GlobalDefaults.ExactTroopTemplate.Count})";
                    RecomputeRatioSum();
                }));
        exactTemplateButton.ButtonText = $"编辑士兵定义 ({globals.ExactTroopTemplate.Count})";
        _buttonOptions.Add(exactTemplateButton);

        // -- GlobalDefaults (6 numeric sliders) --
        _numericOptions.Add(new STNumericOptionVM(
            "目标驻军总数",
            "驻军应维持的目标兵员数 (50–500)。",
            min: 50, max: 500, current: globals.TargetTotalCount, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.TargetTotalCount = (int)v));

        _numericOptions.Add(new STNumericOptionVM(
            "最少防守人数",
            "无论目标人数为何，至少保留的防守人数 (0–300)。",
            min: 0, max: 300, current: globals.MinimumDefenders, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.MinimumDefenders = (int)v));

        _numericOptions.Add(new STNumericOptionVM(
            "招募预算上限",
            "单日招募预算上限 denar (0–50000)。",
            min: 0, max: 50000, current: globals.BudgetLimit, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.BudgetLimit = (int)v));

        // -- 通用兵种比例：任意一个 slider 变化时，其它兵种自动重分配，确保总和保持 1.0。 --
        var troopRatios = new STRatioOptionGroup(RecomputeRatioSum);
        _numericOptions.Add(troopRatios.Add(
            "骑兵占比",
            "通用匹配：所有文化/阵营的骑兵与骑射都按此比例计入。",
            globals.CavalryRatio,
            v => ConfigurationManager.Current.GlobalDefaults.CavalryRatio = v));

        _numericOptions.Add(troopRatios.Add(
            "步兵占比",
            "通用匹配：盾兵、枪兵、双手步兵等步行近战兵。",
            globals.InfantryRatio,
            v => ConfigurationManager.Current.GlobalDefaults.InfantryRatio = v));

        _numericOptions.Add(troopRatios.Add(
            "弓手占比",
            "通用匹配：步行弓手，不限制文化。",
            globals.ArcherRatio,
            v => ConfigurationManager.Current.GlobalDefaults.ArcherRatio = v));

        _numericOptions.Add(troopRatios.Add(
            "弩手占比",
            "通用匹配：装备弩的步行远程兵。",
            globals.CrossbowRatio,
            v => ConfigurationManager.Current.GlobalDefaults.CrossbowRatio = v));

        _numericOptions.Add(troopRatios.Add(
            "投掷兵占比",
            "通用匹配：标枪、飞斧、飞刀或 Skirmisher 编队兵种。",
            globals.ThrowerRatio,
            v => ConfigurationManager.Current.GlobalDefaults.ThrowerRatio = v));
        troopRatios.NormalizeInitial();

        // -- 通用 Tier 比例：同样自动联动，总和保持 1.0。 --
        var tierRatios = new STRatioOptionGroup(RecomputeRatioSum);
        _numericOptions.Add(tierRatios.Add(
            "Tier 1 占比",
            "通用匹配：目标驻军中 Tier 1 兵员比例。",
            globals.Tier1Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier1Ratio = v));

        _numericOptions.Add(tierRatios.Add(
            "Tier 2 占比",
            "通用匹配：目标驻军中 Tier 2 兵员比例。",
            globals.Tier2Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier2Ratio = v));

        _numericOptions.Add(tierRatios.Add(
            "Tier 3 占比",
            "通用匹配：目标驻军中 Tier 3 兵员比例。",
            globals.Tier3Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier3Ratio = v));

        _numericOptions.Add(tierRatios.Add(
            "Tier 4 占比",
            "通用匹配：目标驻军中 Tier 4 兵员比例。",
            globals.Tier4Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier4Ratio = v));

        _numericOptions.Add(tierRatios.Add(
            "Tier 5 占比",
            "通用匹配：目标驻军中 Tier 5 兵员比例。",
            globals.Tier5Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier5Ratio = v));

        _numericOptions.Add(tierRatios.Add(
            "Tier 6 占比",
            "通用匹配：目标驻军中 Tier 6+ 兵员比例。",
            globals.Tier6Ratio,
            v => ConfigurationManager.Current.GlobalDefaults.Tier6Ratio = v));
        tierRatios.NormalizeInitial();

        // -- Tier bounds --
        _numericOptions.Add(new STNumericOptionVM(
            "最低 Tier",
            "允许招募的最低兵种 Tier（含）；范围 1–6。",
            min: 1, max: 6, current: globals.MinTier, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.MinTier = (int)v));

        _numericOptions.Add(new STNumericOptionVM(
            "最高 Tier",
            "允许招募的最高兵种 Tier（含）；范围 1–6。",
            min: 1, max: 6, current: globals.MaxTier, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.MaxTier = (int)v));

        // -- War / peace multipliers --
        _numericOptions.Add(new STNumericOptionVM(
            "战时目标乘数",
            "处于战争状态时，TargetTotalCount 的乘数 (0.5–2.0)。",
            min: 0.5f, max: 2.0f, current: globals.WartimeMultiplier, isDiscrete: false,
            v => ConfigurationManager.Current.GlobalDefaults.WartimeMultiplier = v));

        _numericOptions.Add(new STNumericOptionVM(
            "和平目标乘数",
            "和平时期 TargetTotalCount 的乘数 (0.5–2.0)。",
            min: 0.5f, max: 2.0f, current: globals.PeacetimeMultiplier, isDiscrete: false,
            v => ConfigurationManager.Current.GlobalDefaults.PeacetimeMultiplier = v));

        // -- Food / training / prisoner --
        _numericOptions.Add(new STNumericOptionVM(
            "食物安全阈值",
            "Town.FoodChange 低于此值时暂停招募，避免饿城 (-50 ~ 50)。",
            min: -50, max: 50, current: globals.FoodSafetyThreshold, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.FoodSafetyThreshold = v));

        _numericOptions.Add(new STNumericOptionVM(
            "每日驻军 XP 奖励",
            "每日给驻军每个非 hero 兵员注入的固定 XP (0–30)。",
            min: 0, max: 30, current: globals.DailyTroopXpBonus, isDiscrete: true,
            v => ConfigurationManager.Current.GlobalDefaults.DailyTroopXpBonus = (int)v));

        // DailyPrisonerConformityAmount 在 GlobalConfig，不在 TownGarrisonRule
        _numericOptions.Add(new STNumericOptionVM(
            "每日俘虏 Conformity",
            "每日为驻军中每名俘虏累加的 conformity XP (0–30)。",
            min: 0, max: 30, current: cfg.DailyPrisonerConformityAmount, isDiscrete: true,
            v => ConfigurationManager.Current.DailyPrisonerConformityAmount = (int)v));

        // -- P1 globals (位置 16/17/18，与上述 15 个 numeric 并列；全部 GlobalConfig 顶层字段，
        //    不放进 per-settlement override 页面) --
        _numericOptions.Add(new STNumericOptionVM(
            "征兵护卫数",
            "征兵队出发时从首府 GarrisonParty 抽取多少低 Tier 兵作为基础护卫 (0–50)。",
            min: 0, max: 50, current: cfg.RecruiterEscortSize, isDiscrete: true,
            v => ConfigurationManager.Current.RecruiterEscortSize = (int)v));

        _numericOptions.Add(new STNumericOptionVM(
            "村庄招募冷却",
            "同一 village 被招过后多少小时内不再列为候选 (12–240)。",
            min: 12, max: 240, current: cfg.VillageCooldownHours, isDiscrete: true,
            v => ConfigurationManager.Current.VillageCooldownHours = (int)v));

        _numericOptions.Add(new STNumericOptionVM(
            "征兵队回首府阈值",
            "征兵队总人数达此值立即回首府 (10–200)。",
            min: 10, max: 200, current: cfg.RecruiterReturnThreshold, isDiscrete: true,
            v => ConfigurationManager.Current.RecruiterReturnThreshold = (int)v));

        // 说明：IdleHoursBeforeDisband 当前是 PartyLifecycleManager.cs 中的 const，
        // 不属于 TownGarrisonRule POCO；按任务指引跳过并记录日志。
        Logger.Info("ConfigScreen: skipping IdleHoursBeforeDisband (currently a const in PartyLifecycleManager, not a config field)");
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
