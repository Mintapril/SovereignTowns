using System;
using SovereignTowns.Audit;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// B7.27：mod 引发的金币开销统一扣钱入口。所有原本"从城金库扣"的路径改走本门面，
/// 一律扣玩家个人金币（Hero.MainHero），并写 ledger + audit。
///
/// 调用契约：
///   - 派出新小队前先 CanAfford 预检，按 PauseSpendingWhenBroke 策略确认是否允许支出
///   - Charge 返回 false 时调用方应跳过本次动作（不派遣 / 不升级），不要硬塞
/// </summary>
public static class ModTreasury
{
    /// <summary>按当前支出策略查询玩家是否允许承担 amount，不扣款。</summary>
    public static bool CanAfford(int amount)
    {
        try
        {
            if (amount <= 0) return true;
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
    /// 从玩家 Hero.MainHero 扣 amount。记 ledger + audit。
    /// </summary>
    /// <returns>true = 扣款成功；false = 玩家金币不足且 PauseSpendingWhenBroke=true，拒绝扣款</returns>
    public static bool Charge(ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            // 软门控：玩家金币不足且开关开启 → 拒绝扣款
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat?.PauseSpendingWhenBroke == true && !CanAfford(amount))
            {
                Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d 因玩家金币不足 (PauseSpendingWhenBroke=true)");
                return false;
            }

            var hero = Hero.MainHero;
            if (hero == null)
            {
                Logger.Warn($"ModTreasury: 拒绝 {category} -{amount}d 因 Hero.MainHero == null");
                return false;
            }

            try
            {
                // v1.3.15 GiveGoldAction.ApplyBetweenCharacters 会把转出金额 clamp 到 giver 当前金币。
                // 这里直接改 Hero.Gold，才能在 PauseSpendingWhenBroke=false 时完整扣款并允许负余额。
                hero.ChangeHeroGold(-amount);
            }
            catch (Exception ex)
            {
                Logger.Error($"ModTreasury: ChangeHeroGold failed for {category} -{amount}d", ex);
                return false;
            }

            // 记账
            ModExpenseLedger.Record(category, amount, note);

            // 审计
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
    /// 把先前 <see cref="Charge"/> 已扣的金币退回 Hero.MainHero。专为"扣款 → 后续步骤失败 → 回滚"
    /// 路径准备（recruiter / sally 创建失败时撤销 seed cost）。
    /// 记 ledger（负 amount 即"退款"）+ audit；amount &lt;= 0 时 no-op 并返回 true。
    /// AI clan 路径不应调用 Refund —— 它们也没走 Charge。
    /// </summary>
    public static bool Refund(ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            var hero = Hero.MainHero;
            if (hero == null)
            {
                Logger.Warn($"ModTreasury.Refund: Hero.MainHero == null; cannot refund {category} +{amount}d");
                return false;
            }

            try { hero.ChangeHeroGold(amount); }
            catch (Exception ex)
            {
                Logger.Error($"ModTreasury.Refund: ChangeHeroGold failed for {category} +{amount}d", ex);
                return false;
            }

            // 负 amount 写入 ledger 以便报告看到"今日 -1000 + refund 1000 = 净 0"
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
