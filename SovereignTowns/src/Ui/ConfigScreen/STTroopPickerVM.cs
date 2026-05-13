using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Templates;
using SovereignTowns.Ui.ConfigScreen.Options;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ConfigScreen;

/// <summary>
/// CustomBattle 风格士兵定义编辑器顶层 VM。模板始终是 IG 风格的
/// (CharacterObject.StringId → 目标数量) 字典；本 VM 维护一份工作副本，Done 时回写到 rule。
/// </summary>
/// <remarks>
/// 数据组织：兵种按文化分组（<see cref="STCultureGroupVM"/>），每组内是一组
/// <see cref="STTroopRowVM"/>。每行带兵种缩略图 + Tier/Role/Noble/文化徽章 + 数量编辑控件。
/// Filter 栏（搜索 / Tier 范围 / Noble 平民开关 / 仅显示已选）只切换每行的 IsVisible，
/// 同时联动每组的 IsGroupVisible（组内全空时整组隐藏）。
/// Reset 通过构造时保存的初始模板快照恢复；Cancel 丢弃工作副本；Done 才回写 rule + Save。
/// 父 <see cref="STTroopPickerScreen"/> 还会监听 Enter/Esc 实现快捷键。
/// </remarks>
public sealed class STTroopPickerVM : ViewModel
{
    private readonly TownGarrisonRule _rule;
    private readonly string _ownerName;
    private readonly Dictionary<string, int> _initialTemplate;
    private readonly Dictionary<string, int> _workingTemplate;
    // 全部 row 平铺索引，仅供 ApplyFilters / Reset 用；显示在 prefab 是 CultureGroups → Rows。
    private readonly List<STTroopRowVM> _allRows = new();

    private MBBindingList<STCultureGroupVM> _cultureGroups;
    private string _title;
    private string _totalText = "";
    private bool _isOverTarget;
    private bool _isEmptyFiltered;
    private string _filterCultureId = ""; // "" = 全部
    private int _filterMinTier = 1;
    private int _filterMaxTier = 6;
    private string _searchText = "";
    private bool _showOnlySelected;
    private bool _includeNoble = true;
    private bool _includeCommon = true;
    private string _doneText = "保存";
    private string _cancelText = "取消";
    private string _resetText = "撤销修改";
    private string _clearText = "清空";
    // _filterCultureId 预留给未来的 culture 下拉控件；当前 prefab 没绑控件，
    // 永远是 "" → 全部文化匹配，filter 在 ApplyFilters 内短路（cultureOk=true）。
    // 删掉无显著节省，保留作为 future hook。

    public bool IsFinished { get; private set; }
    public bool SaveOnClose { get; private set; }

    [DataSourceProperty]
    public string Title
    {
        get => _title;
        set { if (value != _title) { _title = value ?? ""; OnPropertyChanged(nameof(Title)); } }
    }

    [DataSourceProperty]
    public string TotalText
    {
        get => _totalText;
        set { if (value != _totalText) { _totalText = value ?? ""; OnPropertyChanged(nameof(TotalText)); } }
    }

    [DataSourceProperty]
    public bool IsOverTarget
    {
        get => _isOverTarget;
        set { if (value != _isOverTarget) { _isOverTarget = value; OnPropertyChanged(nameof(IsOverTarget)); } }
    }

    [DataSourceProperty]
    public bool IsEmptyFiltered
    {
        get => _isEmptyFiltered;
        set { if (value != _isEmptyFiltered) { _isEmptyFiltered = value; OnPropertyChanged(nameof(IsEmptyFiltered)); } }
    }

    [DataSourceProperty]
    public string DoneText
    {
        get => _doneText;
        set { if (value != _doneText) { _doneText = value ?? ""; OnPropertyChanged(nameof(DoneText)); } }
    }

    [DataSourceProperty]
    public string CancelText
    {
        get => _cancelText;
        set { if (value != _cancelText) { _cancelText = value ?? ""; OnPropertyChanged(nameof(CancelText)); } }
    }

    [DataSourceProperty]
    public string ResetText
    {
        get => _resetText;
        set { if (value != _resetText) { _resetText = value ?? ""; OnPropertyChanged(nameof(ResetText)); } }
    }

    [DataSourceProperty]
    public string ClearText
    {
        get => _clearText;
        set { if (value != _clearText) { _clearText = value ?? ""; OnPropertyChanged(nameof(ClearText)); } }
    }

    [DataSourceProperty]
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (value == _searchText) return;
            _searchText = value ?? "";
            OnPropertyChanged(nameof(SearchText));
            ApplyFilters();
        }
    }

    [DataSourceProperty]
    public bool ShowOnlySelected
    {
        get => _showOnlySelected;
        set
        {
            if (value == _showOnlySelected) return;
            _showOnlySelected = value;
            OnPropertyChanged(nameof(ShowOnlySelected));
            ApplyFilters();
        }
    }

    [DataSourceProperty]
    public bool IncludeNoble
    {
        get => _includeNoble;
        set
        {
            if (value == _includeNoble) return;
            _includeNoble = value;
            OnPropertyChanged(nameof(IncludeNoble));
            ApplyFilters();
        }
    }

    [DataSourceProperty]
    public bool IncludeCommon
    {
        get => _includeCommon;
        set
        {
            if (value == _includeCommon) return;
            _includeCommon = value;
            OnPropertyChanged(nameof(IncludeCommon));
            ApplyFilters();
        }
    }

    [DataSourceProperty]
    public int FilterMinTier
    {
        get => _filterMinTier;
        set
        {
            int v = Math.Max(1, Math.Min(6, value));
            if (v == _filterMinTier) return;
            _filterMinTier = v;
            OnPropertyChanged(nameof(FilterMinTier));
            ApplyFilters();
        }
    }

    [DataSourceProperty]
    public int FilterMaxTier
    {
        get => _filterMaxTier;
        set
        {
            int v = Math.Max(1, Math.Min(6, value));
            if (v == _filterMaxTier) return;
            _filterMaxTier = v;
            OnPropertyChanged(nameof(FilterMaxTier));
            ApplyFilters();
        }
    }

    [DataSourceProperty]
    public MBBindingList<STCultureGroupVM> CultureGroups
    {
        get => _cultureGroups;
        set { if (value != _cultureGroups) { _cultureGroups = value; OnPropertyChanged(nameof(CultureGroups)); } }
    }

    public STTroopPickerVM(TownGarrisonRule rule, string ownerName)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _ownerName = ownerName ?? "";
        _title = $"士兵定义模板 — {_ownerName}";
        _cultureGroups = new MBBindingList<STCultureGroupVM>();

        rule.ExactTroopTemplate ??= new Dictionary<string, int>();
        _initialTemplate = new Dictionary<string, int>(rule.ExactTroopTemplate, StringComparer.OrdinalIgnoreCase);
        _workingTemplate = new Dictionary<string, int>(rule.ExactTroopTemplate, StringComparer.OrdinalIgnoreCase);

        BuildGroups();
        UpdateTotalText();
    }

    private void BuildGroups()
    {
        _cultureGroups.Clear();
        _allRows.Clear();

        IEnumerable<CharacterObject> all;
        try
        {
            all = CharacterObject.FindAll(t => TroopTemplateModeService.IsValidTemplateTroop(t, _rule));
        }
        catch (Exception ex)
        {
            Logger.Error("STTroopPickerVM.BuildGroups: CharacterObject.FindAll failed", ex);
            return;
        }

        // 按 culture 分组,每组内按 Tier 降序 + 名称
        var grouped = all
            .Where(t => !string.IsNullOrEmpty(t.StringId))
            .GroupBy(t => t.Culture?.StringId ?? "")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            string cultureId = group.Key;
            string display = group.FirstOrDefault()?.Culture?.Name?.ToString() ?? cultureId;
            var groupVm = new STCultureGroupVM(cultureId, display);

            foreach (var troop in group
                         .OrderByDescending(t => t.Tier)
                         .ThenBy(t => t.Name?.ToString() ?? t.StringId))
            {
                try
                {
                    int currentCount = _workingTemplate.TryGetValue(troop.StringId, out var c) ? c : 0;
                    var role = GenericTroopMatcher.GetRole(troop);
                    var row = new STTroopRowVM(
                        character: troop,
                        tier: troop.Tier,
                        roleBadge: RoleBadgeText(role),
                        cultureBadge: cultureId,
                        isNoble: TroopClassifier.IsNoble(troop),
                        initialCount: currentCount,
                        onCountChanged: OnRowCountChanged);
                    groupVm.Rows.Add(row);
                    _allRows.Add(row);
                }
                catch (Exception ex)
                {
                    Logger.Error($"STTroopPickerVM.BuildGroups: skipping troop '{troop?.StringId}'", ex);
                }
            }

            if (groupVm.Rows.Count > 0) _cultureGroups.Add(groupVm);
        }
        ApplyFilters();
    }

    private static string RoleBadgeText(GenericTroopRole role) => role switch
    {
        GenericTroopRole.Cavalry => "骑",
        GenericTroopRole.Infantry => "步",
        GenericTroopRole.Archer => "弓",
        GenericTroopRole.Crossbow => "弩",
        GenericTroopRole.Thrower => "投",
        _ => "?"
    };

    private void OnRowCountChanged(string troopId, int newCount)
    {
        if (string.IsNullOrEmpty(troopId)) return;
        if (newCount <= 0) _workingTemplate.Remove(troopId);
        else _workingTemplate[troopId] = newCount;
        UpdateTotalText();
    }

    private void UpdateTotalText()
    {
        int total = 0;
        foreach (var v in _workingTemplate.Values) total += Math.Max(0, v);
        int target = Math.Max(0, _rule.TargetTotalCount);
        TotalText = target > 0
            ? $"目标总数：{total} / {target}（{_workingTemplate.Count} 种兵种）"
            : $"目标总数：{total}（{_workingTemplate.Count} 种兵种）";
        IsOverTarget = target > 0 && total > target;
    }

    private void ApplyFilters()
    {
        string search = (_searchText ?? "").Trim();
        int anyVisible = 0;
        foreach (var row in _allRows)
        {
            bool tierOk = row.Tier >= _filterMinTier && row.Tier <= _filterMaxTier;
            bool nobleOk = row.IsNoble ? _includeNoble : _includeCommon;
            bool cultureOk = string.IsNullOrEmpty(_filterCultureId)
                             || string.Equals(row.CultureId, _filterCultureId, StringComparison.OrdinalIgnoreCase);
            bool selectedOk = !_showOnlySelected || row.Count > 0;
            bool searchOk = search.Length == 0
                            || (row.Name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (row.TroopId?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            row.IsVisible = tierOk && nobleOk && cultureOk && selectedOk && searchOk;
            if (row.IsVisible) anyVisible++;
        }
        foreach (var group in _cultureGroups) group.RecomputeGroupVisibility();
        IsEmptyFiltered = anyVisible == 0 && _allRows.Count > 0;
    }

    /// <summary>XML Command.Click 目标：保存模板并关闭。也由 Screen 的 Enter 快捷键调用。</summary>
    public void ExecuteDone()
    {
        try
        {
            _rule.ExactTroopTemplate = new Dictionary<string, int>(_workingTemplate, StringComparer.OrdinalIgnoreCase);
            ConfigurationManager.Save();
            Logger.Info($"STTroopPickerVM: saved template for '{_ownerName}' entries={_rule.ExactTroopTemplate.Count}");
        }
        catch (Exception ex)
        {
            Logger.Error("STTroopPickerVM.ExecuteDone failed", ex);
        }
        SaveOnClose = true;
        IsFinished = true;
    }

    /// <summary>XML Command.Click 目标：丢弃工作副本，关闭。也由 Screen 的 Esc 快捷键调用。</summary>
    public void ExecuteCancel()
    {
        SaveOnClose = false;
        IsFinished = true;
    }

    /// <summary>XML Command.Click 目标：撤销本次会话所有修改（回到打开时的快照）。</summary>
    public void ExecuteReset()
    {
        _workingTemplate.Clear();
        foreach (var kv in _initialTemplate) _workingTemplate[kv.Key] = kv.Value;
        foreach (var row in _allRows)
        {
            int target = _initialTemplate.TryGetValue(row.TroopId, out var v) ? v : 0;
            row.SetCountSilent(target);
        }
        UpdateTotalText();
    }

    /// <summary>XML Command.Click 目标：清空所有数量（可再次 Cancel 撤销）。</summary>
    public void ExecuteClearAll()
    {
        _workingTemplate.Clear();
        foreach (var row in _allRows)
        {
            if (row.Count > 0) row.SetCountSilent(0);
        }
        UpdateTotalText();
    }
}
