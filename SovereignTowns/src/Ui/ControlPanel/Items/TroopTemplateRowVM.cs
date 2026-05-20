using System;
using TaleWorlds.Library;
using SovereignTowns.WebConfig;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 已选名单里的一行（右侧）。绑定契约见 STCPTroopTemplateRow.xml。
/// Ratio 的 getter 直接读底层字典，setter 走 tab.UpdateTroopRatio（联动归一化）。
/// Refresh() 供联动级联：调整一行时其他行需重新读字典刷新显示。
/// </summary>
public sealed class TroopTemplateRowVM : ViewModel
{
    private readonly Func<string, float> _getRatio;
    private readonly Action<string, float> _updateRatio;
    private readonly Action<string> _onRemove;
    private readonly Func<int> _getTargetTotal;

    [DataSourceProperty] public string Id { get; }
    [DataSourceProperty] public string Name { get; }
    [DataSourceProperty] public string CultureName { get; }
    [DataSourceProperty] public string TierText { get; }
    [DataSourceProperty] public string TypeLabel { get; }

    [DataSourceProperty]
    public float Ratio
    {
        get => _getRatio(Id);
        set
        {
            // 再入保护：联动级联 Refresh() 会让滑条把当前值回灌进 setter，
            // 镜像 SliderRowVM.Value 的 epsilon guard，打断回环。
            float cur = _getRatio(Id);
            if (Math.Abs(cur - value) < 1e-9) return;
            _updateRatio(Id, value);
        }
    }

    [DataSourceProperty]
    public string RatioPercent => (_getRatio(Id) * 100f).ToString("0") + "%";

    [DataSourceProperty]
    public string EstimatedCount
    {
        get
        {
            int target = _getTargetTotal();
            int est = (int)Math.Round(_getRatio(Id) * target);
            return ControlPanelLoc.Tr("≈ " + est + " 人", "≈ " + est + " men");
        }
    }

    public TroopTemplateRowVM(
        string id,
        TroopDumper.TroopEntry entry,
        Func<string, float> getRatio,
        Action<string, float> updateRatio,
        Action<string> onRemove,
        Func<int> getTargetTotal)
    {
        Id = id;
        _getRatio = getRatio;
        _updateRatio = updateRatio;
        _onRemove = onRemove;
        _getTargetTotal = getTargetTotal;

        if (entry != null)
        {
            Name = entry.name;
            CultureName = string.IsNullOrEmpty(entry.cultureName) ? entry.culture : entry.cultureName;
            TierText = "T" + entry.tier;
            TypeLabel = TroopRowVM.LabelFor(entry.type);
        }
        else
        {
            // 字典里有 id 但 troop 列表里查不到（兵种来自已卸载的 mod）：回退显示 id。
            Name = id;
            CultureName = "";
            TierText = "T?";
            TypeLabel = "";
        }
    }

    public void ExecuteRemove() => _onRemove?.Invoke(Id);

    /// <summary>外部改了底层占比后强制刷新绑定（联动级联用）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Ratio));
        OnPropertyChanged(nameof(RatioPercent));
        OnPropertyChanged(nameof(EstimatedCount));
    }
}
