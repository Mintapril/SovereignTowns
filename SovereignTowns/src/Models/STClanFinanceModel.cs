using System;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 仅对玩家氏族：把受管领地的收入(税+关税+村庄+项目)与驻军军饷从家族金币中抽出,
/// 改走每氏族 <see cref="Economy.ClanTreasury"/>,只把缓冲上限以上的溢出返还家族金币。
///
/// 关键时序(2026-05-21 advisor 复核):收入/军饷必须在 <c>base.CalculateClanGoldChange</c>
/// 之前算。vanilla 的 <see cref="DefaultClanFinanceModel.CalculateTownIncomeFromTariffs"/> /
/// <see cref="DefaultClanFinanceModel.CalculateVillageIncome"/> 在 applyWithdrawals=true 时
/// 会先返回税额、再扣减 <c>TradeTaxAccumulated</c>。若我们在 base 之后再以 applyWithdrawals=false
/// 重算,读到的是已被排空的累加器 → income 偏低约 20%,差额泄漏回家族金币。
/// 因此在 base 之前(累加器仍满)快照 income/wage,base 之后只做金库结算。
/// </summary>
public sealed class STClanFinanceModel : DefaultClanFinanceModel
{
    private static readonly TextObject _line = new TextObject("主权城镇金库结算");

    public override ExplainedNumber CalculateClanGoldChange(
        Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
    {
        // ── base 之前:在累加器尚未被 base 排空时快照受管领地 income / garrison wage ──
        ClanTreasuryHandle handle = default;
        try
        {
            handle = TrySnapshot(clan);
        }
        catch (Exception ex)
        {
            Logger.Error("STClanFinanceModel snapshot failed; falling back to base", ex);
            handle = default;
        }

        var en = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);

        // ── base 之后:用快照值做金库结算并调整 en ──
        try
        {
            if (handle.Treasury == null) return en;

            long income = handle.Income;
            long wage = handle.Wage;
            int bufferDays = ConfigurationManager.Current?.FiscalAutonomy?.TreasuryBufferDays ?? 30;
            var treasury = handle.Treasury;

            long overflow, shortfall;
            if (applyWithdrawals)
            {
                treasury.RollDay();
                treasury.Credit(income);
                shortfall = treasury.Debit(wage);
                overflow = treasury.SkimAboveBufferCap(bufferDays);
            }
            else
            {
                long projected = treasury.Balance + income - wage;
                long cap = treasury.BufferCap(bufferDays);
                overflow = projected > cap ? projected - cap : 0;
                shortfall = projected < 0 ? -projected : 0;
            }

            // base 已把 +income 和 -wage 计入 en。要改走金库:
            //   -income  撤掉 base 加的收入
            //   +wage    撤掉 base 扣的军饷(base 减了 wage,这里加回)
            //   +overflow 把金库缓冲上限以上的溢出返还家族金币
            //   -shortfall 金库余额不足以付军饷的欠款由家族金币兜底
            en.Add(-income + wage + overflow - shortfall, _line);
        }
        catch (Exception ex)
        {
            Logger.Error("STClanFinanceModel settlement failed; fell back to base", ex);
        }
        return en;
    }

    /// <summary>base 之前的快照:玩家受管氏族才返回非空 Treasury;否则 default(不结算)。</summary>
    private ClanTreasuryHandle TrySnapshot(Clan clan)
    {
        if (clan == null || clan != Clan.PlayerClan) return default;
        var reg = CapitalRegistry.Instance;
        if (reg == null || !reg.IsManagedClan(clan)) return default;
        var treasury = reg.GetForClan(clan)?.Treasury;
        if (treasury == null) return default;

        long income = 0, wage = 0;
        foreach (var fief in clan.Fiefs)
        {
            if (fief?.Settlement == null) continue;
            income += SafeTownIncome(clan, fief);
            var gp = fief.GarrisonParty;
            if (gp != null && gp.IsActive) wage += Math.Max(0, gp.TotalWage);
        }
        return new ClanTreasuryHandle(treasury, income, wage);
    }

    /// <summary>受管领地总收入(税+关税+村庄+项目),只读重算,绝不抛。公开供 Finance 视图复用。</summary>
    public long SafeTownIncome(Clan clan, TaleWorlds.CampaignSystem.Settlements.Town fief)
    {
        try
        {
            // 全限定:本项目存在 SovereignTowns.Campaign 子命名空间,裸 Campaign 在此会被解析成它。
            long sum = (long)TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(fief).ResultNumber;
            sum += (long)CalculateTownIncomeFromTariffs(clan, fief, false).ResultNumber;
            sum += CalculateTownIncomeFromProjects(fief);
            if (fief.Villages != null)
                foreach (var v in fief.Villages) sum += CalculateVillageIncome(clan, v, false);
            return Math.Max(0, sum);
        }
        catch (Exception ex) { Logger.Error($"SafeTownIncome failed '{fief?.Settlement?.StringId}'", ex); return 0; }
    }

    /// <summary>base 之前快照的结果:金库引用 + 受管领地 income/wage。Treasury==null 表示不结算。</summary>
    private readonly struct ClanTreasuryHandle
    {
        public readonly Economy.ClanTreasury? Treasury;
        public readonly long Income;
        public readonly long Wage;

        public ClanTreasuryHandle(Economy.ClanTreasury treasury, long income, long wage)
        {
            Treasury = treasury;
            Income = income;
            Wage = wage;
        }
    }
}
