using System.Collections.Generic;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 今日 / 本周 / 全部 三张汇总表中的一张。
/// Rows = 按类别分组的 FinanceRowVM 列表（最后一行为合计行，IsTotal=true）。
/// </summary>
public sealed class FinanceTableVM : ViewModel
{
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public MBBindingList<FinanceRowVM> Rows { get; }

    public FinanceTableVM(string title, Dictionary<string, long> byCategory, long total)
    {
        Title = title;
        Rows = new MBBindingList<FinanceRowVM>();

        foreach (var kv in byCategory)
        {
            Rows.Add(new FinanceRowVM(kv.Key, "-" + kv.Value + "d"));
        }

        // 合计行（无论是否有明细都显示，数量为零时也显示 0d）
        Rows.Add(new FinanceRowVM(
            ControlPanelLoc.Tr("总", "Total"),
            "-" + total + "d",
            isTotal: true));
    }
}
