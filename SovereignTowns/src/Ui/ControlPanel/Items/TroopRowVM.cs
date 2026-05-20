using System;
using TaleWorlds.Library;
using SovereignTowns.WebConfig;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 兵种名录里的一行（左侧目录）。绑定契约见 STCPTroopCatalogRow.xml。
/// ExecuteToggle 回调进 TemplatesTabVM：未加入则加入，已加入则移除。
/// IsAdded / AddLabel 可由 TemplatesTabVM 在选择变化时翻转。
/// </summary>
public sealed class TroopRowVM : ViewModel
{
    private readonly Action<string> _onToggle;
    private bool _isAdded;

    [DataSourceProperty] public string Id { get; }
    [DataSourceProperty] public string Name { get; }
    [DataSourceProperty] public string CultureName { get; }
    [DataSourceProperty] public int Tier { get; }
    [DataSourceProperty] public string TierText { get; }
    [DataSourceProperty] public string Type { get; }
    [DataSourceProperty] public string TypeGlyph { get; }
    [DataSourceProperty] public string TypeLabel { get; }

    [DataSourceProperty]
    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (_isAdded != value)
            {
                _isAdded = value;
                OnPropertyChanged(nameof(IsAdded));
                OnPropertyChanged(nameof(AddLabel));
            }
        }
    }

    [DataSourceProperty]
    public string AddLabel => _isAdded
        ? ControlPanelLoc.Tr("✓ 已加", "✓ Added")
        : ControlPanelLoc.Tr("＋ 加入", "+ Add");

    public TroopRowVM(TroopDumper.TroopEntry entry, bool isAdded, Action<string> onToggle)
    {
        _onToggle = onToggle;
        Id = entry.id;
        Name = entry.name;
        CultureName = string.IsNullOrEmpty(entry.cultureName) ? entry.culture : entry.cultureName;
        Tier = entry.tier;
        TierText = "T" + entry.tier;
        Type = entry.type;
        TypeGlyph = GlyphFor(entry.type);
        TypeLabel = LabelFor(entry.type);
        _isAdded = isAdded;
    }

    public void ExecuteToggle() => _onToggle?.Invoke(Id);

    /// <summary>port typeGlyph (index.html ~1727).</summary>
    public static string GlyphFor(string type)
    {
        switch (type)
        {
            case "cavalry":     return "♞";
            case "horsearcher": return "↝";
            case "infantry":    return "⚔";
            case "ranged":      return "⤧";
            default:            return "·";
        }
    }

    /// <summary>port typeLabel (index.html ~1730).</summary>
    public static string LabelFor(string type)
    {
        switch (type)
        {
            case "cavalry":     return ControlPanelLoc.Tr("骑兵", "Cavalry");
            case "horsearcher": return ControlPanelLoc.Tr("骑射", "Horse archer");
            case "infantry":    return ControlPanelLoc.Tr("步兵", "Infantry");
            case "ranged":      return ControlPanelLoc.Tr("远程", "Ranged");
            default:            return type ?? "";
        }
    }
}
