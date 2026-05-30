using System;
using SovereignTowns.Audit;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// 2026-05-23 Plan B 起：mod 引发的金币开销直接走 vanilla <c>Clan.Gold</c>。
///
/// 反编译事实（CLAUDE.md #1）：
///   - <c>Clan.Gold</c> 是计算属性 <c>=&gt; Leader?.Gold ?? 0</c>，**没有独立账本**。
///     "氏族金库" 在 vanilla 数据模型里**不存在**，它本质就是 leader 的 Hero.Gold。
///     对玩家：clan.Leader == Hero.MainHero，所以 Clan.Gold 就是玩家口袋里的钱。
///   - 本类是 vanilla 公开 API <see cref="Hero.ChangeHeroGold"/> 的薄记账 wrapper：
///     "扣 Clan.Gold N" 在底层等于 "扣 leader.Gold N"，仅多记一笔 ledger + audit。
///
/// 设计要点：
///   - 玩家氏族收入由 vanilla <c>DefaultClanFinanceModel</c> 原样汇入 leader.Gold；
///     mod 的支出（派遣 / 升级 / 队伍种子）从同一个 leader.Gold 扣。
///   - AI 氏族通过 <c>CapitalRegistry.ShouldChargeClan</c> 早返回，
///     永远不会到达 <c>Charge</c>（保留 AI 免费派遣的 vanilla 平衡）。
///   - <c>PauseSpendingWhenBroke=true</c> 时金币不足直接拒绝；
///     <c>PauseSpendingWhenBroke=false</c> 时不拒绝，但实际扣减经 <see cref="Hero.ChangeHeroGold"/>
///     → Hero.Gold setter clamp 到 0（与 vanilla DailyTickClan 走的同一路径一致），
///     所以即便不拒绝余额也只会到 0，不会真正变负。
///
/// CanAfford 与 Charge 保持语义一致（CanAfford=true 当且仅当 Charge 将成功）。
///
/// 调用契约：
///   - 所有调用方先 <c>ShouldChargeClan(clan)</c> 判断；AI clan 免费不应到达此类。
///   - Charge 返回 false 时调用方应跳过本次动作。
/// </summary>
public static class ModTreasury
{
    /// <summary>
    /// 查询 clan 当前能否承担 amount。
    ///   - <c>PauseSpendingWhenBroke=true</c>（默认）→ 检查 <c>clan.Gold &gt;= amount</c>
    ///   - <c>PauseSpendingWhenBroke=false</c> → 允许负余额，总返回 true
    /// </summary>
    public static bool CanAfford(Clan? clan, int amount)
    {
        try
        {
            if (amount <= 0) return true;
            if (clan == null) return false;

            if (!PauseWhenBroke()) return true;
            return clan.Gold >= amount;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从 <c>clan.Gold</c> 直接扣 <paramref name="amount"/>。
    /// PauseSpendingWhenBroke=true 且余额不足时拒绝；否则扣款（允许 Clan.Gold 走负）。
    /// 记 ledger + audit + 即时刷新 FinancialSnapshot。
    /// </summary>
    /// <returns>true = 扣款成功；false = 余额不足且 PauseSpendingWhenBroke=true</returns>
    public static bool Charge(Clan? clan, ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            if (clan == null)
            {
                Logger.Warn($"ModTreasury: 拒绝 {category} -{amount}d 因 clan == null");
                return false;
            }

            if (PauseWhenBroke() && clan.Gold < amount)
            {
                Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d clan={clan.StringId} 因 Clan.Gold {clan.Gold} 不足 (PauseSpendingWhenBroke=true)");
                return false;
            }

            if (!ClanGoldAccess.Change(clan, -amount))
            {
                Logger.Error($"ModTreasury: ClanGoldAccess.Change failed for {category} -{amount}d clan={clan.StringId}");
                return false;
            }

            TryPatchSnapshotBalance(clan);

            ModExpenseLedger.Record(category, amount, note);
            DecisionAuditLogger.LogRule(
                decisionType: "mod_expense",
                inputSummary: $"category={category} amount={amount} clan={clan.StringId} note={note}",
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
    /// 把先前 <see cref="Charge"/> 已扣的金币退回 <c>clan.Gold</c>。
    /// 专为"扣款 → 后续步骤失败 → 回滚"路径准备。
    /// 记 ledger（负 amount 即"退款"）+ audit；amount &lt;= 0 时 no-op 并返回 true。
    /// </summary>
    public static bool Refund(Clan? clan, ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            if (clan == null)
            {
                Logger.Warn($"ModTreasury.Refund: clan == null; cannot refund {category} +{amount}d");
                return false;
            }

            if (!ClanGoldAccess.Change(clan, amount))
            {
                Logger.Error($"ModTreasury.Refund: ClanGoldAccess.Change failed for {category} +{amount}d clan={clan.StringId}");
                return false;
            }

            TryPatchSnapshotBalance(clan);

            ModExpenseLedger.Record(category, -amount, "refund:" + note);
            DecisionAuditLogger.LogRule(
                decisionType: "mod_refund",
                inputSummary: $"category={category} amount={amount} clan={clan.StringId} note={note}",
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

    /// <summary><c>clan.Gold</c> 变动后即时刷新 FinancialSnapshot 该 clan 的 TreasuryBalance，
    /// 让控制面板的下一次读取拿到新值（不必等下一次 daily 全量重算）。
    /// 仅在该 clan 已在 snapshot 中时生效；失败/无 snapshot → 静默 no-op。</summary>
    private static void TryPatchSnapshotBalance(Clan clan)
    {
        try
        {
            var id = clan?.StringId;
            if (!string.IsNullOrEmpty(id))
                FinancialSnapshot.PatchTreasuryBalance(id!, clan!.Gold);
        }
        catch (Exception ex) { Logger.Warn($"ModTreasury.TryPatchSnapshotBalance failed for '{clan?.StringId}'", ex); }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }

    private static bool PauseWhenBroke()
        => ConfigurationManager.Current?.EnabledFeatures?.PauseSpendingWhenBroke ?? true;
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
