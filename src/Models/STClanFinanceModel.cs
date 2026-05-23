using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 受管领地收入只读重算 helper。
///
/// 2026-05-23 Plan B 起：mod 不再 override <c>CalculateClanGoldChange</c>。vanilla 的
/// <see cref="DefaultClanFinanceModel"/> 直接处理玩家氏族的收支（settlement income → Clan.Gold，
/// garrison wage 走 vanilla 既有路径），mod 的 ST 队伍工资由 <c>STPartyWageModel</c> 强制为 0
/// 不影响 vanilla 流，mod 的派遣/升级支出由 <see cref="Economy.ModTreasury"/> 直接从 Clan.Gold 扣。
///
/// 本类仅保留为一个继承 <see cref="DefaultClanFinanceModel"/> 的只读 helper，因为 SafeTownIncome
/// 需要复用 base 的 protected 方法（<c>CalculateTownIncomeFromTariffs</c> /
/// <c>CalculateTownIncomeFromProjects</c> / <c>CalculateVillageIncome</c>）。它**不**作为 GameModel
/// 注册（保留 vanilla <c>DefaultClanFinanceModel</c> 为活跃模型）。
/// </summary>
public sealed class STClanFinanceModel : DefaultClanFinanceModel
{
    /// <summary>受管领地总收入（税+关税+村庄+项目），只读重算，绝不抛。公开供 Finance 视图复用。</summary>
    public long SafeTownIncome(Clan clan, Town fief)
    {
        try
        {
            // 全限定：本项目存在 SovereignTowns.Campaign 子命名空间，裸 Campaign 在此会被解析成它。
            long sum = (long)TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(fief).ResultNumber;
            sum += (long)CalculateTownIncomeFromTariffs(clan, fief, false).ResultNumber;
            sum += CalculateTownIncomeFromProjects(fief);
            if (fief.Villages != null)
                foreach (var v in fief.Villages) sum += CalculateVillageIncome(clan, v, false);
            return Math.Max(0, sum);
        }
        catch (Exception ex) { Logger.Error($"SafeTownIncome failed '{fief?.Settlement?.StringId}'", ex); return 0; }
    }
}
