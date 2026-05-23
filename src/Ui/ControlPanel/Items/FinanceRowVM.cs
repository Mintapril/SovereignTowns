using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 通用财务表格行。复用于三张汇总表（2列：类别/金额）和近期流水表（4列：时间/类别/金额/备注）。
/// IsTotal = true 时 prefab 渲染金色合计行。
/// </summary>
public sealed class FinanceRowVM : ViewModel
{
    [DataSourceProperty] public string Col1 { get; }
    [DataSourceProperty] public string Col2 { get; }
    [DataSourceProperty] public string Col3 { get; }
    [DataSourceProperty] public string Col4 { get; }
    [DataSourceProperty] public bool IsTotal { get; }

    public FinanceRowVM(string c1, string c2, string c3 = "", string c4 = "", bool isTotal = false)
    {
        Col1 = c1;
        Col2 = c2;
        Col3 = c3;
        Col4 = c4;
        IsTotal = isTotal;
    }
}
