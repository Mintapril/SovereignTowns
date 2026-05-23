using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// B7.27 / Task 4:mod 引发的金币开销统一扣钱入口。
///
/// 扣费策略:
///   1) 解析 clan 的 ClanTreasury(通过 CapitalRegistry)。
///   2) 金库余额充足 → 全额从金库 Debit,无需碰 Hero.MainHero。
///   3) 金库不足 → 直接拒绝扣款(返回 false)。2026-05-23 起取消"差额由 Hero.MainHero 兜底"
///      的旧通道 —— 玩家氏族金库与个人金币之间不再有自动通道,玩家通过 TreasuryUserActions
///      主动存款/取款来调度。调用方应跳过此次动作(不派遣 / 不升级)。
///   4) 若 clan 无对应金库(treasury == null,即 AI clan 路径)→ 退化为旧路径:
///      直接扣 Hero.MainHero,受 PauseSpendingWhenBroke 门控。
///
/// CanAfford 与 Charge 保持语义一致(CanAfford=true 当且仅当 Charge 将成功)。
///
/// 调用契约:
///   - 所有调用方先 ShouldChargeClan(clan) 判断是否收费,AI clan 免费不应到达此类。
///   - Charge 返回 false 时调用方应跳过本次动作,不要硬塞。
/// </summary>
public static class ModTreasury
{
    /// <summary>解析 clan 的 ClanTreasury(通过 CapitalRegistry);无注册 / 无金库 → null。</summary>
    private static ClanTreasury? ResolveFor(Clan? clan)
        => CapitalRegistry.Instance?.GetForClan(clan)?.Treasury;

    /// <summary>
    /// 查询 clan 当前能否承担 amount。与 Charge 语义对齐:
    ///   - 金库余额 ≥ amount → true
    ///   - 金库余额不足 → false(无 subsidy 兜底)
    ///   - treasury == null(无金库,AI clan) → 退化旧逻辑:PauseSpendingWhenBroke=false → true;
    ///     否则 hero.Gold ≥ amount
    /// </summary>
    public static bool CanAfford(Clan? clan, int amount)
    {
        try
        {
            if (amount <= 0) return true;

            var treasury = ResolveFor(clan);
            if (treasury != null)
            {
                return treasury.CanAfford(amount);
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
    /// 从 clan 的 ClanTreasury 扣 amount。金库不足时直接拒绝(不再从 Hero.MainHero 兜底)。
    /// 记 ledger + audit。
    /// </summary>
    /// <returns>true = 扣款成功;false = 金库不足(或无金库时玩家金币不足)</returns>
    public static bool Charge(Clan? clan, ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            var treasury = ResolveFor(clan);

            if (treasury != null)
            {
                if (!treasury.CanAfford(amount))
                {
                    Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d clan={clan?.StringId} 因金库余额不足");
                    return false;
                }

                // Debit 金库(已确认 CanAfford → shortfall 必为 0)
                long shortfall = treasury.Debit(amount);
                if (shortfall > 0)
                {
                    // 防御性回滚 —— 理论不该发生(CanAfford 已 gate)。
                    Logger.Warn($"ModTreasury: 金库 Debit 返回意外 shortfall={shortfall} for {category} -{amount}d; 回滚");
                    treasury.Credit(amount - shortfall);
                    return false;
                }

                // 即时刷新 snapshot 给 UI(主线程内调用,WebUI/控制面板下一次读到的就是新余额)。
                TryPatchSnapshotBalance(clan, treasury);

                ModExpenseLedger.Record(category, amount, note);
                DecisionAuditLogger.LogRule(
                    decisionType: "mod_expense",
                    inputSummary: $"category={category} amount={amount} clan={clan?.StringId} note={note}",
                    decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note ?? "")}\"}}",
                    accepted: true);
                return true;
            }

            // 无金库 → 旧路径(直接扣 Hero.MainHero,受 PauseSpendingWhenBroke 门控)
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
                // 用 ChangeHeroGold 而非 GiveGoldAction.ApplyBetweenCharacters:后者会把转出额 clamp 到 giver
                // 当前金币;直接改 Hero.Gold 才能在 PauseSpendingWhenBroke=false 时完整扣款并允许负余额。
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
    /// 把先前 <see cref="Charge"/> 已扣的金币退回 clan 的 ClanTreasury(若有)或 Hero.MainHero(旧路径)。
    /// 专为"扣款 → 后续步骤失败 → 回滚"路径准备。
    /// 记 ledger(负 amount 即"退款")+ audit;amount &lt;= 0 时 no-op 并返回 true。
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
                // 用 Refund(不是 Credit)同时回滚开销环,避免 TrailingDailyExpense 虚高。
                treasury.Refund(amount);

                TryPatchSnapshotBalance(clan, treasury);

                ModExpenseLedger.Record(category, -amount, "refund:" + note);
                DecisionAuditLogger.LogRule(
                    decisionType: "mod_refund",
                    inputSummary: $"category={category} amount={amount} clan={clan?.StringId} note={note}",
                    decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note ?? "")}\"}}",
                    accepted: true);
                return true;
            }

            // 无金库 → 旧路径:退回 Hero.MainHero
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

    /// <summary>金库变动后即时刷新 FinancialSnapshot 该 clan 的 TreasuryBalance,
    /// 让 WebUI / 控制面板的下一次读取拿到新值(不必等下一次 daily 全量重算)。
    /// 仅在该 clan 已在 snapshot 中时生效;失败/无 snapshot → 静默 no-op。</summary>
    private static void TryPatchSnapshotBalance(Clan? clan, ClanTreasury treasury)
    {
        try
        {
            var id = clan?.StringId;
            if (!string.IsNullOrEmpty(id))
                WebConfig.FinancialSnapshot.PatchTreasuryBalance(id!, treasury.Balance);
        }
        catch { /* swallow — snapshot 刷新失败不影响金库本身的扣款 */ }
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
    /// <summary>每招 1 人的工资(外派征兵队 / 首府原地征兵)</summary>
    RecruiterWage,
    /// <summary>派出征兵队的初始本钱(1000 denar)</summary>
    RecruiterSeed,
    /// <summary>驻军升级的单兵金币成本</summary>
    Upgrade,
    /// <summary>出击队的初始本钱(100 denar)</summary>
    SallySeed,
    /// <summary>派出巡逻队的初始本钱(2000 denar)</summary>
    PatrolSeed,
    /// <summary>派出调拨队的初始本钱(200 denar)</summary>
    TransferSeed,
    /// <summary>兜底</summary>
    Other
}
