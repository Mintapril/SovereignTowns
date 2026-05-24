using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using SovereignTowns.Configuration;
using SovereignTowns.WebConfig;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// Tab 4「兵员模板 / Troop template」VM。
/// 左侧：可搜索 / 过滤的兵种名录；右侧：已选名单 + 自动归一化的占比滑条。
/// 占比归一化数学逐行 port 自 WebUI 的 addTroop / updateTroopRatio / removeTroop /
/// clearTroops / _snapTroopSumTo1（index.html ~1635-1714），必须与 WebUI 完全一致。
/// </summary>
public sealed class TemplatesTabVM : ViewModel
{
    private const int DisplayCap = 200;

    private readonly GlobalConfig _config;
    private readonly Action _markDirty;
    private readonly Action _gotoCompositionTab;

    // ── 兵种全集 ──
    private readonly List<TroopDumper.TroopEntry> _allTroops;
    private readonly Dictionary<string, TroopDumper.TroopEntry> _troopById = new Dictionary<string, TroopDumper.TroopEntry>();

    // 文化 chip 全集（含「全部」）—— 用于激活态刷新；展示时均分到 CultureChipsRow1/2。
    private readonly List<ChipVM> _cultureChips = new List<ChipVM>();

    // ── 过滤状态 ──
    private string _searchText = "";
    private string _cultureFilter = "";   // culture stringId, "" = all
    private string _typeFilter = "";      // cavalry/horsearcher/infantry/ranged, "" = all
    private int _tierFilter;              // 0 = all, 1..6
    private bool _hideSelected;
    private int _matchCount;              // 未截断的匹配总数

    // ══════════════════════════════════════════════
    //  静态文本（逐字 port index.html ~696-845）
    // ══════════════════════════════════════════════

    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro { get; }
    [DataSourceProperty] public string CatalogSectionLabel { get; }
    [DataSourceProperty] public string CatalogHint { get; }
    [DataSourceProperty] public string SearchPlaceholder { get; }
    [DataSourceProperty] public string CultureLabel { get; }
    [DataSourceProperty] public string TypeLabel { get; }
    [DataSourceProperty] public string TierLabel => "Tier";
    [DataSourceProperty] public string AllLabel { get; }
    [DataSourceProperty] public string HideSelectedLabel { get; }
    [DataSourceProperty] public string SelectedSectionLabel { get; }
    [DataSourceProperty] public string ClearLabel { get; }
    [DataSourceProperty] public string EmptyListText { get; }
    [DataSourceProperty] public string NoMatchText { get; }
    [DataSourceProperty] public string RatioNormalizedSuffix { get; }
    [DataSourceProperty] public string CapHintText { get; }

    // ── mode banner ──
    [DataSourceProperty] public string ModeBannerMark => "!";
    [DataSourceProperty] public string ModeBannerText { get; }
    [DataSourceProperty] public string SwitchToExactLabel { get; }
    [DataSourceProperty] public string ExactModeOkText { get; }

    // ══════════════════════════════════════════════
    //  绑定列表
    // ══════════════════════════════════════════════

    // 文化 chip 数量较多（随兵种文化动态生成,约 13 个）—— 单行会超出左栏,均分两行渲染。
    [DataSourceProperty] public MBBindingList<ChipVM> CultureChipsRow1 { get; } = new MBBindingList<ChipVM>();
    [DataSourceProperty] public MBBindingList<ChipVM> CultureChipsRow2 { get; } = new MBBindingList<ChipVM>();
    [DataSourceProperty] public MBBindingList<ChipVM> TypeChips { get; } = new MBBindingList<ChipVM>();
    [DataSourceProperty] public MBBindingList<ChipVM> TierChips { get; } = new MBBindingList<ChipVM>();
    [DataSourceProperty] public MBBindingList<TroopRowVM> FilteredTroops { get; } = new MBBindingList<TroopRowVM>();
    [DataSourceProperty] public MBBindingList<TroopTemplateRowVM> SelectedTroops { get; } = new MBBindingList<TroopTemplateRowVM>();

    // ══════════════════════════════════════════════
    //  绑定属性
    // ══════════════════════════════════════════════

    [DataSourceProperty]
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value ?? "";
                OnPropertyChanged(nameof(SearchText));
                RefreshFilteredList();
            }
        }
    }

    private bool _isGenericMode;
    [DataSourceProperty]
    public bool IsGenericMode
    {
        get => _isGenericMode;
        private set
        {
            if (_isGenericMode != value)
            {
                _isGenericMode = value;
                OnPropertyChanged(nameof(IsGenericMode));
                OnPropertyChanged(nameof(IsExactMode));
            }
        }
    }

    [DataSourceProperty] public bool IsExactMode => !_isGenericMode;

    private bool _hideSelectedActive;
    [DataSourceProperty]
    public bool HideSelectedActive
    {
        get => _hideSelectedActive;
        private set { if (_hideSelectedActive != value) { _hideSelectedActive = value; OnPropertyChanged(nameof(HideSelectedActive)); } }
    }

    // ── header counts ──
    // SelectedCount 仅供内部派生 HasSelection / IsEmpty；prefab 文本绑定必须用
    // 字符串版 SelectedCountText —— Gauntlet 的 TextWidget.Text setter 是 string，
    // 直接绑 int 会在 LoadMovie 期抛 ArgumentException 并使整个面板加载失败。
    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (_selectedCount != value)
            {
                _selectedCount = value;
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [DataSourceProperty] public string SelectedCountText => _selectedCount.ToString();
    [DataSourceProperty] public bool HasSelection => _selectedCount > 0;
    [DataSourceProperty] public bool IsEmpty => _selectedCount == 0;

    private string _exactTroopTotalText = "0";
    [DataSourceProperty]
    public string ExactTroopTotalText
    {
        get => _exactTroopTotalText;
        private set { if (_exactTroopTotalText != value) { _exactTroopTotalText = value; OnPropertyChanged(nameof(ExactTroopTotalText)); } }
    }

    private string _targetTotalText = "0";
    [DataSourceProperty]
    public string TargetTotalText
    {
        get => _targetTotalText;
        private set { if (_targetTotalText != value) { _targetTotalText = value; OnPropertyChanged(nameof(TargetTotalText)); } }
    }

    private string _ratioSumPercentText = "0%";
    [DataSourceProperty]
    public string RatioSumPercentText
    {
        get => _ratioSumPercentText;
        private set { if (_ratioSumPercentText != value) { _ratioSumPercentText = value; OnPropertyChanged(nameof(RatioSumPercentText)); } }
    }

    private bool _ratioSumOk = true;
    [DataSourceProperty]
    public bool RatioSumOk
    {
        get => _ratioSumOk;
        private set { if (_ratioSumOk != value) { _ratioSumOk = value; OnPropertyChanged(nameof(RatioSumOk)); } }
    }

    private string _matchCountText = "";
    [DataSourceProperty]
    public string MatchCountText
    {
        get => _matchCountText;
        private set { if (_matchCountText != value) { _matchCountText = value; OnPropertyChanged(nameof(MatchCountText)); } }
    }

    private bool _showCapHint;
    [DataSourceProperty]
    public bool ShowCapHint
    {
        get => _showCapHint;
        private set { if (_showCapHint != value) { _showCapHint = value; OnPropertyChanged(nameof(ShowCapHint)); } }
    }

    // ══════════════════════════════════════════════
    //  构造
    // ══════════════════════════════════════════════

    public TemplatesTabVM(GlobalConfig config, Action markDirty, Action gotoCompositionTab)
    {
        _config = config;
        _markDirty = markDirty;
        _gotoCompositionTab = gotoCompositionTab;

        _allTroops = ControlPanelData.CollectTroops() ?? new List<TroopDumper.TroopEntry>();
        foreach (var t in _allTroops)
            if (t != null && !string.IsNullOrEmpty(t.id) && !_troopById.ContainsKey(t.id))
                _troopById[t.id] = t;

        // ── 静态文案（逐字 port index.html ~696-845）──
        Title = ControlPanelLoc.Tr("兵员模板", "Troop template");
        Intro = ControlPanelLoc.Tr(
            "指定每个兵种的占比（自动归一化到 100%），实际人数 = 占比 × 目标驻军人数。",
            "Set the share of each troop type (auto-normalized to 100%); actual count = share × target garrison size.");
        CatalogSectionLabel = ControlPanelLoc.Tr("兵种名录", "Troop catalog");
        CatalogHint = ControlPanelLoc.Tr("点 ＋ 加入名单", "Click + to add to the list");
        SearchPlaceholder = ControlPanelLoc.Tr(
            "搜索兵种（中文 / 英文 id 都支持）", "Search troops (by name or English id)");
        CultureLabel = ControlPanelLoc.Tr("文化", "Culture");
        TypeLabel = ControlPanelLoc.Tr("兵种", "Type");
        AllLabel = ControlPanelLoc.Tr("全部", "All");
        HideSelectedLabel = ControlPanelLoc.Tr("隐藏已选", "Hide selected");
        SelectedSectionLabel = ControlPanelLoc.Tr("已选名单", "Selected list");
        ClearLabel = ControlPanelLoc.Tr("清空", "Clear");
        EmptyListText = ControlPanelLoc.Tr(
            "名单为空。从左侧「兵种名录」点 ＋ 加入。",
            "The list is empty. Add troops with + from the \"Troop catalog\" on the left.");
        NoMatchText = ControlPanelLoc.Tr("无匹配兵种", "No matching troops");
        RatioNormalizedSuffix = ControlPanelLoc.Tr("（自动归一化）", "(auto-normalized)");
        CapHintText = ControlPanelLoc.Tr(
            "· 仅显示前 200 条，继续输入收缩", "· showing first 200 only, keep typing to narrow");

        ModeBannerText = ControlPanelLoc.Tr(
            "当前为「通用比例匹配」模式 —— 下面勾选的兵种不会生效。招募只看兵种比例和 Tier 范围。",
            "Currently in \"Generic ratio matching\" mode — the troops ticked below have no effect. Recruitment looks only at troop ratios and tier range.");
        SwitchToExactLabel = ControlPanelLoc.Tr("改用精确模板模式", "Switch to exact template mode");
        ExactModeOkText = ControlPanelLoc.Tr(
            "✓ 当前为「精确兵员模板」模式 —— 下面勾选的兵种即驻军招募目标。",
            "✓ Currently in \"Exact troop template\" mode — the troops ticked below are the garrison recruitment targets.");

        // PR-5'(2026-05-24): UseGenericMatching removed; generic matching is always on.
        _isGenericMode = true;

        BuildCultureChips();
        BuildTypeChips();
        BuildTierChips();
        RefreshChipStates();

        RefreshFilteredList();
        RebuildSelectedList();
        RefreshHeaderCounts();
    }

    private TownGarrisonRule Defaults => _config.GlobalDefaults;

    // PR-5'(2026-05-24): ExactTroopTemplate removed from TownGarrisonRule.
    // TemplatesTab is now a visual stub; all mutations go to a local ephemeral dict (not saved).
    private readonly Dictionary<string, float> _ephemeralTemplate = new Dictionary<string, float>();
    private Dictionary<string, float> Template => _ephemeralTemplate;

    // ══════════════════════════════════════════════
    //  Commands
    // ══════════════════════════════════════════════

    /// <summary>PR-5'(2026-05-24): UseGenericMatching removed; exact mode no longer exists — no-op.</summary>
    public void ExecuteSwitchToExactMode()
    {
        // no-op: UseGenericMatching removed from TownGarrisonRule
    }

    public void ExecuteToggleHideSelected()
    {
        _hideSelected = !_hideSelected;
        HideSelectedActive = _hideSelected;
        RefreshFilteredList();
    }

    /// <summary>port clearTroops（index.html ~1691）—— 二次确认后清空。</summary>
    public void ExecuteClearTroops()
    {
        if (Template.Count == 0) return;
        InformationManager.ShowInquiry(
            new InquiryData(
                ControlPanelLoc.Tr("清空兵员模板", "Clear troop template"),
                ControlPanelLoc.Tr("清空全部已选兵种？", "Clear all selected troops?"),
                true,
                true,
                ControlPanelLoc.Tr("确定", "OK"),
                ControlPanelLoc.Tr("取消", "Cancel"),
                (Action)DoClearTroops,
                (Action)null,
                "",
                0f,
                (Action)null,
                (Func<ValueTuple<bool, string>>)null,
                (Func<ValueTuple<bool, string>>)null),
            false,
            false);
    }

    private void DoClearTroops()
    {
        // PR-5'(2026-05-24): ExactTroopTemplate removed; clear the ephemeral local template only.
        _ephemeralTemplate.Clear();
        RebuildSelectedList();
        RefreshFilteredAddedFlags();
        RefreshHeaderCounts();
    }

    // ══════════════════════════════════════════════
    //  占比归一化 — 逐行 port 自 WebUI（index.html ~1635-1714）
    //  总和约 = 1.0；新增 / 删除 / 调整时按比例 rescale 让 Σ 保持 1.0。
    // ══════════════════════════════════════════════

    /// <summary>port addTroop（index.html ~1635）。</summary>
    public void AddTroop(string id)
    {
        var tmpl = Template;
        if (string.IsNullOrEmpty(id) || tmpl.ContainsKey(id)) return;

        // 新条目分得 1/(N+1) 份，其他条目按比例缩 N/(N+1)
        var ids = new List<string>(tmpl.Keys);
        float newRatio = ids.Count == 0 ? 1.0f : 1.0f / (ids.Count + 1);
        float oldSum = 0f;
        foreach (var k in ids) oldSum += tmpl[k];
        float factor = ids.Count == 0 ? 0f : (1.0f - newRatio) / Math.Max(1e-6f, oldSum);
        foreach (var k in ids) tmpl[k] = Math.Max(0f, tmpl[k] * factor);
        tmpl[id] = newRatio;
        SnapTroopSumTo1(id);
        _markDirty?.Invoke();

        RebuildSelectedList();
        RefreshFilteredAddedFlags();
        RefreshHeaderCounts();
    }

    /// <summary>port updateTroopRatio（index.html ~1647）。SelectedTroops 不重建 —— 只刷新行。</summary>
    public void UpdateTroopRatio(string id, float raw)
    {
        var tmpl = Template;
        if (string.IsNullOrEmpty(id) || !tmpl.ContainsKey(id)) return;
        _markDirty?.Invoke();

        float v = raw;
        if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
        v = Math.Max(0f, Math.Min(1f, v));
        tmpl[id] = v;

        // 把剩余 (1 - v) 在其他条目上按原占比 rescale
        var others = new List<string>();
        foreach (var k in tmpl.Keys) if (k != id) others.Add(k);
        float remaining = 1f - v;

        if (others.Count == 0)
        {
            tmpl[id] = 1.0f; // 唯一条目强制独占 100%
            RefreshSelectedRows();
            RefreshHeaderCounts();
            return;
        }

        float otherSum = 0f;
        foreach (var k in others) otherSum += tmpl[k];
        if (otherSum > 1e-6f)
        {
            float f = remaining / otherSum;
            foreach (var k in others) tmpl[k] = Math.Max(0f, tmpl[k] * f);
        }
        else
        {
            float each = Math.Max(0f, remaining / others.Count);
            foreach (var k in others) tmpl[k] = each;
        }
        SnapTroopSumTo1(id);

        RefreshSelectedRows();
        RefreshHeaderCounts();
    }

    /// <summary>port removeTroop（index.html ~1673）。</summary>
    public void RemoveTroop(string id)
    {
        var tmpl = Template;
        if (string.IsNullOrEmpty(id) || !tmpl.ContainsKey(id)) return;
        _markDirty?.Invoke();
        tmpl.Remove(id);

        // 把空出的份额按原占比分给剩余条目
        var ids = new List<string>(tmpl.Keys);
        if (ids.Count > 0)
        {
            float s = 0f;
            foreach (var k in ids) s += tmpl[k];
            if (s < 1e-6f)
            {
                float each = 1.0f / ids.Count;
                foreach (var k in ids) tmpl[k] = each;
            }
            else
            {
                float f = 1.0f / s;
                foreach (var k in ids) tmpl[k] = Math.Max(0f, tmpl[k] * f);
            }
        }

        RebuildSelectedList();
        RefreshFilteredAddedFlags();
        RefreshHeaderCounts();
    }

    /// <summary>port _snapTroopSumTo1（index.html ~1698）—— 修浮点漂移让 Σ 精确 = 1。</summary>
    private void SnapTroopSumTo1(string lockedKey)
    {
        var tmpl = Template;
        var ids = new List<string>(tmpl.Keys);
        if (ids.Count == 0) return;
        float total = 0f;
        foreach (var k in ids) total += tmpl[k];
        float drift = 1f - total;
        if (Math.Abs(drift) < 1e-4f) return;

        var others = new List<string>();
        foreach (var k in ids) if (k != lockedKey) others.Add(k);
        if (others.Count == 0)
        {
            tmpl[lockedKey] = Math.Max(0f, Math.Min(1f, tmpl[lockedKey] + drift));
            return;
        }
        string biggest = others[0];
        foreach (var k in others) if (tmpl[k] > tmpl[biggest]) biggest = k;
        tmpl[biggest] = Math.Max(0f, Math.Min(1f, tmpl[biggest] + drift));
    }

    // ══════════════════════════════════════════════
    //  内部刷新
    // ══════════════════════════════════════════════

    /// <summary>port filteredTroops 谓词（index.html ~1346）。Clear + Add 重建（MBBindingList 不批量通知），截断 200。</summary>
    private void RefreshFilteredList()
    {
        string q = (_searchText ?? "").ToLowerInvariant().Trim();
        var tmpl = Template;

        FilteredTroops.Clear();
        _matchCount = 0;
        foreach (var t in _allTroops)
        {
            if (t == null) continue;
            if (_hideSelected && tmpl.ContainsKey(t.id)) continue;
            if (!string.IsNullOrEmpty(_cultureFilter) && t.culture != _cultureFilter) continue;
            if (!string.IsNullOrEmpty(_typeFilter) && t.type != _typeFilter) continue;
            if (_tierFilter > 0 && t.tier != _tierFilter) continue;
            if (q.Length > 0
                && !(t.name ?? "").ToLowerInvariant().Contains(q)
                && !(t.id ?? "").ToLowerInvariant().Contains(q)) continue;

            _matchCount++;
            if (FilteredTroops.Count < DisplayCap)
                FilteredTroops.Add(new TroopRowVM(t, tmpl.ContainsKey(t.id), AddOrRemoveToggle));
        }

        ShowCapHint = _matchCount > DisplayCap;
        MatchCountText = ControlPanelLoc.Tr("匹配 ", "Matched ")
            + _matchCount + " / " + _allTroops.Count;
    }

    /// <summary>名录行点 ＋／✓ 的 toggle：未加入则加入，已加入则移除。</summary>
    private void AddOrRemoveToggle(string id)
    {
        if (Template.ContainsKey(id)) RemoveTroop(id);
        else AddTroop(id);
    }

    /// <summary>翻转可见名录行的 IsAdded（选择变化后调用，避免整表重建）。</summary>
    private void RefreshFilteredAddedFlags()
    {
        var tmpl = Template;
        if (_hideSelected)
        {
            // 隐藏已选模式下，加入 / 移除会改变行集合 —— 必须整表重建。
            RefreshFilteredList();
            return;
        }
        foreach (var row in FilteredTroops)
            row.IsAdded = tmpl.ContainsKey(row.Id);
    }

    /// <summary>整表重建 SelectedTroops（仅在 Add / Remove / Clear 时调用，绝不在拖动滑条时）。</summary>
    private void RebuildSelectedList()
    {
        SelectedTroops.Clear();
        foreach (var kv in Template)
        {
            _troopById.TryGetValue(kv.Key, out var entry);
            SelectedTroops.Add(new TroopTemplateRowVM(
                kv.Key, entry,
                GetRatio, UpdateTroopRatio, RemoveTroop, GetTargetTotal));
        }
    }

    /// <summary>联动级联：调整一行后让其余行重新读字典刷新（不重建，避免毁掉正在拖的滑条）。</summary>
    private void RefreshSelectedRows()
    {
        foreach (var row in SelectedTroops) row.Refresh();
    }

    private float GetRatio(string id)
    {
        var tmpl = Template;
        return tmpl.TryGetValue(id, out float v) ? v : 0f;
    }

    private int GetTargetTotal()
    {
        int t = Defaults.TargetTotalCount;
        return t < 0 ? 0 : t;
    }

    /// <summary>port exactTroopTotal / troopRatioSum / ratioSumOk（index.html ~1330-1340, 1726）。</summary>
    private void RefreshHeaderCounts()
    {
        var tmpl = Template;
        float sum = 0f;
        foreach (var v in tmpl.Values) sum += v;
        int target = GetTargetTotal();

        SelectedCount = tmpl.Count;
        TargetTotalText = target.ToString();
        ExactTroopTotalText = ((int)Math.Round(sum * target)).ToString();
        RatioSumPercentText = (sum * 100f).ToString("0") + "%";
        RatioSumOk = sum >= 0.9f && sum <= 1.1f;
    }

    // ══════════════════════════════════════════════
    //  Chips
    // ══════════════════════════════════════════════

    /// <summary>port cultureList（index.html ~1312）—— 去重 + 按显示名排序，stringId 作过滤键。</summary>
    private void BuildCultureChips()
    {
        _cultureChips.Clear();
        CultureChipsRow1.Clear();
        CultureChipsRow2.Clear();

        _cultureChips.Add(new ChipVM(AllLabel, () => SetCultureFilter("")));

        var seen = new Dictionary<string, string>();
        var order = new List<string>();
        foreach (var t in _allTroops)
        {
            if (t == null || string.IsNullOrEmpty(t.culture)) continue;
            if (!seen.ContainsKey(t.culture))
            {
                seen[t.culture] = string.IsNullOrEmpty(t.cultureName) ? t.culture : t.cultureName;
                order.Add(t.culture);
            }
        }
        order.Sort((a, b) => string.Compare(seen[a], seen[b], StringComparison.CurrentCulture));
        foreach (var cid in order)
        {
            var localCid = cid;
            _cultureChips.Add(new ChipVM(seen[cid], () => SetCultureFilter(localCid)));
        }

        // 均分两行：行 1 取上半（含「全部」），行 2 取下半。
        int row1Count = (_cultureChips.Count + 1) / 2;
        for (int i = 0; i < _cultureChips.Count; i++)
            (i < row1Count ? CultureChipsRow1 : CultureChipsRow2).Add(_cultureChips[i]);
    }

    private static readonly string[] TypeOrder = { "cavalry", "horsearcher", "infantry", "ranged" };

    private void BuildTypeChips()
    {
        TypeChips.Clear();
        TypeChips.Add(new ChipVM(AllLabel, () => SetTypeFilter("")));
        foreach (var ty in TypeOrder)
        {
            var localTy = ty;
            TypeChips.Add(new ChipVM(TroopRowVM.LabelFor(ty), () => SetTypeFilter(localTy)));
        }
    }

    private void BuildTierChips()
    {
        TierChips.Clear();
        TierChips.Add(new ChipVM(AllLabel, () => SetTierFilter(0)));
        for (int tier = 1; tier <= 6; tier++)
        {
            var t = tier;
            TierChips.Add(new ChipVM("T" + t, () => SetTierFilter(t)));
        }
    }

    private void SetCultureFilter(string cid)
    {
        // 点已选项 → 取消（port: troopCultureFilter === cid ? '' : cid）
        _cultureFilter = _cultureFilter == cid ? "" : cid;
        RefreshChipStates();
        RefreshFilteredList();
    }

    private void SetTypeFilter(string ty)
    {
        _typeFilter = _typeFilter == ty ? "" : ty;
        RefreshChipStates();
        RefreshFilteredList();
    }

    private void SetTierFilter(int tier)
    {
        _tierFilter = _tierFilter == tier ? 0 : tier;
        RefreshChipStates();
        RefreshFilteredList();
    }

    private void RefreshChipStates()
    {
        // culture: index 0 = All
        if (_cultureChips.Count > 0) _cultureChips[0].IsActive = string.IsNullOrEmpty(_cultureFilter);
        // type
        if (TypeChips.Count > 0)
        {
            TypeChips[0].IsActive = string.IsNullOrEmpty(_typeFilter);
            for (int i = 0; i < TypeOrder.Length && i + 1 < TypeChips.Count; i++)
                TypeChips[i + 1].IsActive = _typeFilter == TypeOrder[i];
        }
        // tier
        if (TierChips.Count > 0)
        {
            TierChips[0].IsActive = _tierFilter == 0;
            for (int tier = 1; tier <= 6 && tier < TierChips.Count; tier++)
                TierChips[tier].IsActive = _tierFilter == tier;
        }
        // culture active flags (rebuild order: chip[i+1] ↔ i-th sorted culture)
        RefreshCultureChipActive();
    }

    private void RefreshCultureChipActive()
    {
        // chip 0 done above; chips 1..n match the sorted culture order.
        // We re-derive the order the same way BuildCultureChips did.
        var seen = new Dictionary<string, string>();
        var order = new List<string>();
        foreach (var t in _allTroops)
        {
            if (t == null || string.IsNullOrEmpty(t.culture)) continue;
            if (!seen.ContainsKey(t.culture))
            {
                seen[t.culture] = string.IsNullOrEmpty(t.cultureName) ? t.culture : t.cultureName;
                order.Add(t.culture);
            }
        }
        order.Sort((a, b) => string.Compare(seen[a], seen[b], StringComparison.CurrentCulture));
        for (int i = 0; i < order.Count && i + 1 < _cultureChips.Count; i++)
            _cultureChips[i + 1].IsActive = _cultureFilter == order[i];
    }
}
