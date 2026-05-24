using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 已选名单里的一行（右侧）。绑定契约见 STCPTroopTemplateRow.xml。
/// Count 的 getter 直接读底层字典，+/- 按钮走 tab.SetTroopCount。
/// Refresh() 供联动级联：调整一行后其他行需要重新读字典刷新显示。
/// PR-10 (2026-05-24): 从 ratio/float 改为 count/int，适配 B 池招募模板编辑语义。
/// </summary>
public sealed class TroopTemplateRowVM : ViewModel
{
    private readonly Func<string, int> _getCount;
    private readonly Action<string, int> _setCount;
    private readonly Action<string> _onRemove;

    [DataSourceProperty] public string Id { get; }
    [DataSourceProperty] public string Name { get; }
    [DataSourceProperty] public string CultureName { get; }
    [DataSourceProperty] public string TierText { get; }
    [DataSourceProperty] public string TypeLabel { get; }

    /// <summary>当前计划招募数量（整数，[0,100]）。</summary>
    [DataSourceProperty]
    public int Count
    {
        get => _getCount(Id);
        set
        {
            int cur = _getCount(Id);
            if (cur == value) return;
            _setCount(Id, value);
        }
    }

    /// <summary>显示文本："N 人"。</summary>
    [DataSourceProperty]
    public string RatioPercent => _getCount(Id) + ControlPanelLoc.Tr(" 人", " troops");

    /// <summary>供 XML 占位：空字符串（PR-10 起不再计算估算值）。</summary>
    [DataSourceProperty]
    public string EstimatedCount => "";

    public TroopTemplateRowVM(
        string id,
        TroopCatalog.TroopEntry entry,
        Func<string, int> getCount,
        Action<string, int> setCount,
        Action<string> onRemove)
    {
        Id = id;
        _getCount = getCount;
        _setCount = setCount;
        _onRemove = onRemove;

        if (entry != null)
        {
            Name = entry.name;
            CultureName = string.IsNullOrEmpty(entry.cultureName) ? entry.culture : entry.cultureName;
            TierText = "T" + entry.tier;
            TypeLabel = TroopRowVM.LabelFor(entry.type);
        }
        else
        {
            // 字典里有 id 但兵种列表里查不到（来自已卸载 mod）：回退显示 id。
            Name = id;
            CultureName = "";
            TierText = "T?";
            TypeLabel = "";
        }
    }

    public void ExecuteRemove() => _onRemove?.Invoke(Id);

    /// <summary>步进 +5（最大 100）。</summary>
    public void ExecuteIncrease()
    {
        int cur = _getCount(Id);
        if (cur < 100)
            _setCount(Id, Math.Min(100, cur + 5));
    }

    /// <summary>步进 -5（最小 0，0 时移除）。</summary>
    public void ExecuteDecrease()
    {
        int cur = _getCount(Id);
        if (cur > 5)
            _setCount(Id, cur - 5);
        else
            _onRemove?.Invoke(Id);
    }

    /// <summary>外部改了底层计数后强制刷新绑定（联动级联用）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(RatioPercent));
    }
}
