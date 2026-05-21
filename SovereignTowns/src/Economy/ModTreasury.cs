using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// B7.27 / Task 4：mod 引发的金币开销统一扣钱入口。
///
/// 扣费优先级（玩家路径）：
///   1) 解析 clan 的 ClanTreasury（通过 CapitalRegistry）。
///   2) 若金库余额充足 → 全额从金库 Debit，无需碰 Hero.MainHero。
///   3) 若金库不足：
///      - PlayerClanSubsidyWhenTreasuryEmpty=true（默认）→ 金库扣到 0，差额从 Hero.MainHero 扣（子弹银行兜底）。
///      - PlayerClanSubsidyWhenTreasuryEmpty=false → 拒绝扣款（返回 false）。
///   4) 若 clan 无对应金库（treasury == null）→ 退化为旧路径：直接扣 Hero.MainHero，受 PauseSpendingWhenBroke 门控。
///
/// CanAfford 与 Charge 保持语义一致（CanAfford=true 当且仅当 Charge 将成功），
/// 以避免 shouldCharge && !CanAfford(clan, cost) 短路把补贴路径（subsidy=true）也误判为不可负担。
///
/// 调用契约：
///   - 所有调用方先 ShouldChargeClan(clan) 判断是否收费，AI clan 免费不应到达此类。
///   - Charge 返回 false 时调用方应跳过本次动作（不派遣 / 不升级），不要硬塞。
/// </summary>
public static class ModTreasury
{
    /// <summary>解析 clan 的 ClanTreasury（通过 CapitalRegistry）；无注册 / 无金库 → null。</summary>
    private static ClanTreasury? ResolveFor(Clan? clan)
        => CapitalRegistry.Instance?.GetForClan(clan)?.Treasury;

    /// <summary>
    /// 查询 clan 当前能否承担 amount。与 Charge 语义对齐：
    ///   - 金库余额 ≥ amount → true
    ///   - 金库余额不足 + subsidy=true → true（Charge 会从 hero 兜底）
    ///   - 金库余额不足 + subsidy=false → false
    ///   - treasury == null（无金库）→ 退化旧逻辑：PauseSpendingWhenBroke=false → true；否则 hero.Gold ≥ amount
    /// </summary>
    public static bool CanAfford(Clan? clan, int amount)
    {
        try
        {
            if (amount <= 0) return true;

            var treasury = ResolveFor(clan);
            if (treasury != null)
            {
                if (treasury.CanAfford(amount)) return true;
                // 金库短缺 — 看补贴开关
                bool subsidy = ConfigurationManager.Current?.FiscalAutonomy?.PlayerClanSubsidyWhenTreasuryEmpty ?? true;
                return subsidy; // subsidy=true → Charge 会兜底；subsidy=false → 拒绝
            }

            // 无金库 → 旧路径
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat?.PauseSpendingWhenBroke == false) return true;
            var hero = Hero.MainHero;
            if (hero == null) return false;
            return hero.Gold >= amount;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从 clan 的 ClanTreasury 扣 amount（不足时按补贴策略决定是否从 Hero.MainHero 兜底）。
    /// 记 ledger + audit。
    /// </summary>
    /// <returns>true = 扣款成功；false = 金库不足且补贴关闭（或无 hero 兜底）</returns>
    public static bool Charge(Clan? clan, ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            var treasury = ResolveFor(clan);

            if (treasury != null)
            {
                bool subsidy = ConfigurationManager.Current?.FiscalAutonomy?.PlayerClanSubsidyWhenTreasuryEmpty ?? true;

                if (!treasury.CanAfford(amount) && !subsidy)
                {
                    Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d clan={clan?.StringId} 因金库余额不足且 PlayerClanSubsidyWhenTreasuryEmpty=false");
                    return false;
                }

                // Debit 金库（不足时扣到 0，返回 shortfall）
                long shortfall = treasury.Debit(amount);

                if (shortfall > 0)
                {
                    // 补贴：从 Hero.MainHero 扣差额
                    var hero = Hero.MainHero;
                    if (hero == null)
                    {
                        Logger.Warn($"ModTreasury: 金库短缺 {shortfall}d 但 Hero.MainHero == null — 拒绝 {category} -{amount}d");
                        // 回滚：hero 为 null，差额无法兜底 → 归还金库已扣的 (amount - shortfall)，整体视为失败
                        treasury.Credit(amount - shortfall);
                        return false;
                    }

                    try
                    {
                        // checked 包裹收窄转换：shortfall 是 long，溢出抛异常（被外层 catch 接住返 false），
                        // 避免静默截断错误地给 hero 记一笔异常金额。
                        hero.ChangeHeroGold(-checked((int)shortfall));
                        Logger.Info($"ModTreasury: {category} -{amount}d clan={clan?.StringId} 金库扣 {amount - shortfall}d + 补贴 {shortfall}d from hero");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"ModTreasury: hero.ChangeHeroGold(-{shortfall}) failed for {category}", ex);
                        // 部分失败：金库已扣，hero 扣失败。归还金库的已扣部分，整体返回 false。
                        treasury.Credit(amount - shortfall);
                        return false;
                    }
                }

                // 记账（全额 amount，不拆分补贴与金库部分）
                string auditNote = shortfall > 0 ? $"{note} subsidy={shortfall}" : note;
                ModExpenseLedger.Record(category, amount, auditNote);
                DecisionAuditLogger.LogRule(
                    decisionType: "mod_expense",
                    inputSummary: $"category={category} amount={amount} clan={clan?.StringId} shortfall={shortfall} note={note}",
                    decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"shortfall\":{shortfall},\"note\":\"{EscapeJson(auditNote ?? "")}\"}}",
                    accepted: true);
                return true;
            }

            // 无金库 → 旧路径（直接扣 Hero.MainHero，受 PauseSpendingWhenBroke 门控）
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat?.PauseSpendingWhenBroke == true && !CanAfford(clan, amount))
            {
                Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d 因玩家金币不足 (PauseSpendingWhenBroke=true, no treasury)");
                return false;
            }

            var mainHero = Hero.MainHero;
            if (mainHero == null)
            {
                Logger.Warn($"ModTreasury: 拒绝 {category} -{amount}d 因 Hero.MainHero == null (no treasury)");
                return false;
            }

            try
            {
                // 用 ChangeHeroGold 而非 GiveGoldAction.ApplyBetweenCharacters：后者会把转出额 clamp 到 giver
                // 当前金币；直接改 Hero.Gold 才能在 PauseSpendingWhenBroke=false 时完整扣款并允许负余额。
                mainHero.ChangeHeroGold(-amount);
            }
            catch (Exception ex)
            {
                Logger.Error($"ModTreasury: ChangeHeroGold failed for {category} -{amount}d (no treasury)", ex);
                return false;
            }

            ModExpenseLedger.Record(category, amount, note);
            DecisionAuditLogger.LogRule(
                decisionType: "mod_expense",
                inputSummary: $"category={category} amount={amount} note={note}",
                decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note ?? "")}\"}}",
                accepted: true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ModTreasury.Charge failed for {category} -{amount}d", ex);
            return false;
        }
    }

    /// <summary>
    /// 把先前 <see cref="Charge"/> 已扣的金币退回 clan 的 ClanTreasury（若有）或 Hero.MainHero（旧路径）。
    /// 专为"扣款 → 后续步骤失败 → 回滚"路径准备。
    /// 记 ledger（负 amount 即"退款"）+ audit；amount &lt;= 0 时 no-op 并返回 true。
    /// AI clan 路径不应调用 Refund —— 它们也没走 Charge。
    /// </summary>
    public static bool Refund(Clan? clan, ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            var treasury = ResolveFor(clan);
            if (treasury != null)
            {
                // Use Refund (not Credit) so the expense ring is also corrected, preventing
                // phantom trailing-expense inflation that would inflate TrailingDailyExpense / BufferCap.
                treasury.Refund(amount);

                ModExpenseLedger.Record(category, -amount, "refund:" + note);
                DecisionAuditLogger.LogRule(
                    decisionType: "mod_refund",
                    inputSummary: $"category={category} amount={amount} clan={clan?.StringId} note={note}",
                    decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note ?? "")}\"}}",
                    accepted: true);
                return true;
            }

            // 无金库 → 旧路径：退回 Hero.MainHero
            var hero = Hero.MainHero;
            if (hero == null)
            {
                Logger.Warn($"ModTreasury.Refund: Hero.MainHero == null; cannot refund {category} +{amount}d (no treasury)");
                return false;
            }

            try { hero.ChangeHeroGold(amount); }
            catch (Exception ex)
            {
                Logger.Error($"ModTreasury.Refund: ChangeHeroGold failed for {category} +{amount}d", ex);
                return false;
            }

            ModExpenseLedger.Record(category, -amount, "refund:" + note);
            DecisionAuditLogger.LogRule(
                decisionType: "mod_refund",
                inputSummary: $"category={category} amount={amount} note={note}",
                decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note ?? "")}\"}}",
                accepted: true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ModTreasury.Refund failed for {category} +{amount}d", ex);
            return false;
        }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}

/// <summary>mod 支出分类。</summary>
public enum ExpenseCategory
{
    /// <summary>每招 1 人的工资（外派征兵队 / 首府原地征兵）</summary>
    RecruiterWage,
    /// <summary>派出征兵队的初始本钱（1000 denar）</summary>
    RecruiterSeed,
    /// <summary>驻军升级的单兵金币成本</summary>
    Upgrade,
    /// <summary>出击队的初始本钱（100 denar）</summary>
    SallySeed,
    /// <summary>派出巡逻队的初始本钱（2000 denar）</summary>
    PatrolSeed,
    /// <summary>派出调拨队的初始本钱（200 denar）</summary>
    TransferSeed,
    /// <summary>兜底</summary>
    Other
}
