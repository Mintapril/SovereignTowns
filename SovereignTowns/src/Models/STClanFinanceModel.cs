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
/// 仅对玩家氏族:把受管领地的收入(税+关税+村庄+项目)与驻军军饷从家族金币中抽出,
/// 改走每氏族 <see cref="Economy.ClanTreasury"/>。
///
/// 2026-05-23 起金库与 Hero.Gold 之间不再有自动通道:
///   - 收入单向流入金库(不再"超过缓冲就溢出回流玩家金币")
///   - 工资单向流出金库;金库余额不足以付当日工资 → 差额 unpaid 真欠付,
///     vanilla GarrisonParty 的 morale 会随欠饷下降并自动逃兵(vanilla 既有行为)
///   - 玩家通过 <see cref="Economy.TreasuryUserActions"/> 主动存款/取款来调度个人金币与金库
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
    private static readonly TextObject TreasuryLine =
        new TextObject("{=ST_ClanFinanceSettlement}Sovereign Towns treasury settlement");

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

        ExplainedNumber en;
        try
        {
            en = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);
        }
        catch (Exception ex)
        {
            Logger.Error("STClanFinanceModel base.CalculateClanGoldChange failed", ex);
            return new ExplainedNumber(0f, includeDescriptions, null);
        }

        // ── base 之后:用快照值做金库结算并调整 en ──
        try
        {
            if (handle.Treasury == null) return en;

            long income = handle.Income;
            long wage = handle.Wage;
            var treasury = handle.Treasury;

            // 金库实际付出的工资仅在 applyWithdrawals=true 时落账(daily 真实结算);
            // applyWithdrawals=false 是 UI 预览,不动金库。两种情况下对玩家 Hero.Gold
            // 的净影响都是 0:income 单向进金库不进玩家钱包,wage 单向从金库出不从玩家钱包扣,
            // 金库不足以付工资的部分 unpaid 真欠付(不再从 Hero.Gold 兜底)。
            long unpaid = 0;
            if (applyWithdrawals)
            {
                treasury.RollDay();
                treasury.Credit(income);
                unpaid = treasury.Debit(wage);  // 不足时扣到 0,返回 shortfall;不再兜底
            }

            // base 已把 +income 和 -wage 计入 en。玩家钱包对受管领地收支零参与,故撤回这两项:
            //   -income:全进金库,不进玩家钱包
            //   +wage:全由金库出(或欠付),不从玩家钱包扣
            // adjustment 与 unpaid 无关 —— unpaid 是金库 vs garrison wage 之间的事,
            // 跟玩家钱包无关。玩家想要金库的钱必须走 TreasuryUserActions 主动取款。
            en.Add(-income + wage, TreasuryLine);

            // ── garrison 欠饷 → morale 惩罚补救 ──
            // vanilla 内部 ApplyMoraleEffect 已在 base.CalculateClanGoldChange 中按
            // `clan.Gold + 累计 income ≥ wage` 判断 ── 看不到金库,所以基本不会触发。
            // 我们在金库视角下重跑一次:如果金库不足以付 wage(unpaid > 0),手动给该 clan
            // 的每个 garrison party 追加 vanilla 等价的 morale penalty + HasUnpaidWages。
            // 完全镜像 vanilla `_research/Vanilla/...DefaultClanFinanceModel.decompiled.cs:892-914`。
            // 仅 applyWithdrawals=true 触发(预览不动 morale,同 vanilla 行为)。
            if (applyWithdrawals && unpaid > 0 && wage > 0)
                ApplyTreasuryShortfallMorale(clan, unpaid, wage);
        }
        catch (Exception ex)
        {
            Logger.Error("STClanFinanceModel settlement failed; fell back to base", ex);
        }
        return en;
    }

    /// <summary>
    /// 金库视角下 garrison 欠饷的 morale 补救。镜像 vanilla
    /// <c>DefaultClanFinanceModel.ApplyMoraleEffect</c>(`_research/...DefaultClanFinanceModel.decompiled.cs:892-914`):
    ///   ratio = unpaid / totalWage
    ///   penalty = GetDailyNoWageMoralePenalty × ratio
    ///   若 HasUnpaidWages &lt; ratio,额外再加 penalty × (ratio − oldHasUnpaidWages)(escalating)
    ///   HasUnpaidWages := ratio
    /// 每个 garrison party 平摊整体 ratio —— 简化处理(细分到每城需要重做 base 的 budget 推演,
    /// 不值得)。
    /// </summary>
    private static void ApplyTreasuryShortfallMorale(Clan clan, long unpaid, long totalWage)
    {
        try
        {
            if (clan == null || unpaid <= 0 || totalWage <= 0) return;
            float ratio = (float)unpaid / totalWage;
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;
            if (ratio <= 0f) return;

            var pm = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PartyMoraleModel;
            if (pm == null) return;

            foreach (var fief in clan.Fiefs)
            {
                var gp = fief?.GarrisonParty;
                if (gp == null || !gp.IsActive || gp.TotalWage <= 0) continue;
                try
                {
                    float dailyPenalty;
                    try { dailyPenalty = pm.GetDailyNoWageMoralePenalty(gp); }
                    catch (Exception modelEx)
                    {
                        Logger.Error($"STClanFinanceModel: PartyMoraleModel.GetDailyNoWageMoralePenalty failed for garrison '{fief?.Settlement?.StringId}'", modelEx);
                        continue;
                    }

                    float penalty = dailyPenalty * ratio;
                    if (gp.HasUnpaidWages < ratio)
                        penalty += dailyPenalty * (ratio - gp.HasUnpaidWages);
                    gp.RecentEventsMorale += penalty;
                    gp.HasUnpaidWages = ratio;
                }
                catch (Exception innerEx)
                {
                    Logger.Error($"STClanFinanceModel: garrison morale apply failed for '{fief?.Settlement?.StringId}'", innerEx);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("STClanFinanceModel.ApplyTreasuryShortfallMorale failed", ex);
        }
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
