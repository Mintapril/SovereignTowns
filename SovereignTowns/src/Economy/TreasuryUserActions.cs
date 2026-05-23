using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// 玩家氏族金库的主动存款 / 取款入口。
///
/// 2026-05-23 起金库与 Hero.Gold 之间不再有自动通道(详见 ClanTreasury / STClanFinanceModel),
/// 玩家通过本类提供的两个原子操作显式调度资金:
///   - <see cref="TryDeposit"/>:Hero.MainHero.Gold -= amount, treasury.Credit(amount)
///   - <see cref="TryWithdraw"/>:treasury.Debit(amount), Hero.MainHero.Gold += amount
///
/// 全部 API **必须在主线程调用**(碰 Hero.MainHero.ChangeHeroGold)。HTTP 线程请通过
/// <see cref="WebConfig.WebConfigGameThreadSync.EnqueueAction"/> 排队到主线程执行。
///
/// 操作记 DecisionAuditLogger(decisionType=TreasuryDeposit / TreasuryWithdraw)便于日后审计。
/// 成功后即时刷新 <see cref="WebConfig.FinancialSnapshot"/> 该 clan 的 TreasuryBalance,
/// UI 下一次读到的就是新值。
/// </summary>
public static class TreasuryUserActions
{
    // 本地化探针 — 与 ActivityNarrator / ControlPanelLoc 重复(刻意 YAGNI,不抽公共助手)。
    // 第一次访问触发一次 {=ST_WebUiLang} 解析,失败默认 false(英文)。
    private static bool? _isZh;

    private static bool IsChinese
    {
        get
        {
            if (_isZh == null)
            {
                try { _isZh = new TextObject("{=ST_WebUiLang}en").ToString() == "zh"; }
                catch { _isZh = false; }
            }
            return _isZh.Value;
        }
    }

    private static string Tr(string zh, string en) => IsChinese ? zh : en;

    /// <summary>
    /// 玩家主动从 Hero.MainHero.Gold 存入 <paramref name="amount"/> 金币到玩家氏族金库。
    /// 校验:amount &gt; 0、clan 在 CapitalRegistry 注册、Hero.MainHero.Gold ≥ amount。
    /// </summary>
    /// <returns>true 表示扣 Hero.Gold 并入金库成功;false 表示校验失败或操作失败,reason 含原因。</returns>
    public static bool TryDeposit(long amount, out string reason, out long treasuryBalanceAfter, out int heroGoldAfter)
    {
        treasuryBalanceAfter = 0;
        heroGoldAfter = 0;
        reason = "";
        try
        {
            if (amount <= 0)
            {
                reason = Tr("金额必须为正整数", "amount must be positive");
                return false;
            }
            if (amount > int.MaxValue)
            {
                reason = Tr("金额超出 int 上限", "amount exceeds int max");
                return false;
            }

            var clan = Clan.PlayerClan;
            if (clan == null) { reason = Tr("玩家氏族不可用", "player clan unavailable"); return false; }

            var treasury = CapitalRegistry.Instance?.GetForClan(clan)?.Treasury;
            if (treasury == null)
            {
                reason = Tr("金库未初始化(氏族尚无首府?)", "treasury not initialized (clan has no capital?)");
                return false;
            }

            var hero = Hero.MainHero;
            if (hero == null) { reason = Tr("主角对象不可用", "Hero.MainHero unavailable"); return false; }
            if (hero.Gold < amount)
            {
                reason = Tr($"主角金币 {hero.Gold} 少于请求 {amount}", $"hero gold {hero.Gold} < requested {amount}");
                return false;
            }

            int amt = checked((int)amount);
            // 先扣 Hero,后存入金库;Hero 扣失败 → 不动金库。
            try { hero.ChangeHeroGold(-amt); }
            catch (Exception ex)
            {
                Logger.Error("TreasuryUserActions.TryDeposit: ChangeHeroGold failed", ex);
                reason = Tr("主角金币变更失败:", "hero gold change failed: ") + ex.Message;
                return false;
            }
            treasury.DepositFromUser(amt);

            treasuryBalanceAfter = treasury.Balance;
            heroGoldAfter = hero.Gold;

            // 即时刷 snapshot 让 UI 立刻反映新余额。
            try
            {
                var id = clan.StringId;
                if (!string.IsNullOrEmpty(id))
                    WebConfig.FinancialSnapshot.PatchTreasuryBalance(id, treasury.Balance);
            }
            catch { /* swallow */ }

            DecisionAuditLogger.LogRule(
                decisionType: "TreasuryDeposit",
                inputSummary: $"clan={clan.StringId} amount={amt} heroAfter={heroGoldAfter} treasuryAfter={treasuryBalanceAfter}",
                decisionJson: $"{{\"clan\":\"{clan.StringId}\",\"amount\":{amt},\"heroAfter\":{heroGoldAfter},\"treasuryAfter\":{treasuryBalanceAfter}}}",
                accepted: true);
            Logger.Info($"TreasuryUserActions.Deposit clan={clan.StringId} amount={amt}d → hero={heroGoldAfter}d treasury={treasuryBalanceAfter}d");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TreasuryUserActions.TryDeposit failed", ex);
            reason = Tr("内部错误:", "internal_error:") + " " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 玩家主动从玩家氏族金库取出 <paramref name="amount"/> 金币转入 Hero.MainHero.Gold。
    /// 校验:amount &gt; 0、clan 在 CapitalRegistry 注册、treasury.Balance ≥ amount。
    /// </summary>
    /// <returns>true 表示从金库扣并入 Hero.Gold 成功;false 表示校验失败或操作失败。</returns>
    public static bool TryWithdraw(long amount, out string reason, out long treasuryBalanceAfter, out int heroGoldAfter)
    {
        treasuryBalanceAfter = 0;
        heroGoldAfter = 0;
        reason = "";
        try
        {
            if (amount <= 0)
            {
                reason = Tr("金额必须为正整数", "amount must be positive");
                return false;
            }
            if (amount > int.MaxValue)
            {
                reason = Tr("金额超出 int 上限", "amount exceeds int max");
                return false;
            }

            var clan = Clan.PlayerClan;
            if (clan == null) { reason = Tr("玩家氏族不可用", "player clan unavailable"); return false; }

            var treasury = CapitalRegistry.Instance?.GetForClan(clan)?.Treasury;
            if (treasury == null)
            {
                reason = Tr("金库未初始化(氏族尚无首府?)", "treasury not initialized (clan has no capital?)");
                return false;
            }

            var hero = Hero.MainHero;
            if (hero == null) { reason = Tr("主角对象不可用", "Hero.MainHero unavailable"); return false; }

            if (treasury.Balance < amount)
            {
                reason = Tr($"金库余额 {treasury.Balance} 少于请求 {amount}", $"treasury balance {treasury.Balance} < requested {amount}");
                return false;
            }

            int amt = checked((int)amount);
            // WithdrawForUser 直接动余额、不进开销环(取款不是开销,不该抬 TrailingDailyExpense)。
            long actual = treasury.WithdrawForUser(amt);
            if (actual < amt)
            {
                // 不应发生(已 CanAfford 校验)。回滚并返回失败。
                Logger.Warn($"TreasuryUserActions.TryWithdraw: WithdrawForUser 实际扣 {actual} < 请求 {amt};回滚");
                treasury.DepositFromUser(actual);
                reason = Tr("金库取款不足", "treasury withdraw shortfall");
                return false;
            }

            try { hero.ChangeHeroGold(amt); }
            catch (Exception ex)
            {
                Logger.Error("TreasuryUserActions.TryWithdraw: ChangeHeroGold failed; 回滚金库", ex);
                treasury.DepositFromUser(amt);
                reason = Tr("主角金币变更失败:", "hero gold change failed: ") + ex.Message;
                return false;
            }

            treasuryBalanceAfter = treasury.Balance;
            heroGoldAfter = hero.Gold;

            try
            {
                var id = clan.StringId;
                if (!string.IsNullOrEmpty(id))
                    WebConfig.FinancialSnapshot.PatchTreasuryBalance(id, treasury.Balance);
            }
            catch { /* swallow */ }

            DecisionAuditLogger.LogRule(
                decisionType: "TreasuryWithdraw",
                inputSummary: $"clan={clan.StringId} amount={amt} heroAfter={heroGoldAfter} treasuryAfter={treasuryBalanceAfter}",
                decisionJson: $"{{\"clan\":\"{clan.StringId}\",\"amount\":{amt},\"heroAfter\":{heroGoldAfter},\"treasuryAfter\":{treasuryBalanceAfter}}}",
                accepted: true);
            Logger.Info($"TreasuryUserActions.Withdraw clan={clan.StringId} amount={amt}d → hero={heroGoldAfter}d treasury={treasuryBalanceAfter}d");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TreasuryUserActions.TryWithdraw failed", ex);
            reason = Tr("内部错误:", "internal_error:") + " " + ex.Message;
            return false;
        }
    }
}
