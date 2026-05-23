using System;
using SovereignTowns.Audit;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// 玩家氏族首府金库的主动存款 / 取款入口。
///
/// 2026-05-23 Plan B 起，首府金库 ≡ vanilla <c>Clan.Gold</c>。vanilla 不提供 player ↔ Clan.Gold
/// 的 UI 转账通道，所以本类是 mod 自加的两个原子操作：
///   - <see cref="TryDeposit"/>：Hero.MainHero.Gold -= amount，Clan.PlayerClan.Gold += amount
///   - <see cref="TryWithdraw"/>：Clan.PlayerClan.Gold -= amount，Hero.MainHero.Gold += amount
///
/// 全部 API **必须在主线程调用**（碰 Hero.MainHero.ChangeHeroGold 和 Clan.Gold）。HTTP 线程请通过
/// <see cref="WebConfig.WebConfigGameThreadSync.EnqueueAction"/> 排队到主线程执行。
///
/// 操作记 DecisionAuditLogger（decisionType=TreasuryDeposit / TreasuryWithdraw）便于日后审计。
/// 成功后即时刷新 <see cref="WebConfig.FinancialSnapshot"/> 该 clan 的 TreasuryBalance，
/// UI 下一次读到的就是新值。
/// </summary>
public static class TreasuryUserActions
{
    // 本地化探针 — 与 ActivityNarrator / ControlPanelLoc 重复（刻意 YAGNI，不抽公共助手）。
    // 第一次访问触发一次 {=ST_WebUiLang} 解析，失败默认 false（英文）。
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
    /// 玩家主动从 Hero.MainHero.Gold 存入 <paramref name="amount"/> 金币到玩家氏族 Clan.Gold。
    /// 校验：amount &gt; 0、Clan.PlayerClan 可用、Hero.MainHero.Gold ≥ amount。
    /// </summary>
    /// <returns>true 表示扣 Hero.Gold 并入 Clan.Gold 成功；false 表示校验失败或操作失败，reason 含原因。</returns>
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

            var hero = Hero.MainHero;
            if (hero == null) { reason = Tr("主角对象不可用", "Hero.MainHero unavailable"); return false; }
            if (hero.Gold < amount)
            {
                reason = Tr($"主角金币 {hero.Gold} 少于请求 {amount}", $"hero gold {hero.Gold} < requested {amount}");
                return false;
            }

            int amt = checked((int)amount);
            // 先扣 Hero，后加 Clan.Gold；Hero 扣失败 → 不动 Clan.Gold。
            try { hero.ChangeHeroGold(-amt); }
            catch (Exception ex)
            {
                Logger.Error("TreasuryUserActions.TryDeposit: ChangeHeroGold failed", ex);
                reason = Tr("主角金币变更失败：", "hero gold change failed: ") + ex.Message;
                return false;
            }
            if (!ClanGoldAccess.Change(clan, amt))
            {
                // 反射写入失败 → 回滚 Hero
                Logger.Error($"TreasuryUserActions.TryDeposit: ClanGoldAccess.Change failed; 回滚 Hero (clan={clan.StringId}, amt={amt})");
                try { hero.ChangeHeroGold(amt); } catch { }
                reason = Tr("氏族金币写入失败（反射不可用？）", "clan gold write failed (reflection unavailable?)");
                return false;
            }

            treasuryBalanceAfter = clan.Gold;
            heroGoldAfter = hero.Gold;

            // 即时刷 snapshot 让 UI 立刻反映新余额。
            try
            {
                var id = clan.StringId;
                if (!string.IsNullOrEmpty(id))
                    WebConfig.FinancialSnapshot.PatchTreasuryBalance(id, clan.Gold);
            }
            catch { /* swallow */ }

            DecisionAuditLogger.LogRule(
                decisionType: "TreasuryDeposit",
                inputSummary: $"clan={clan.StringId} amount={amt} heroAfter={heroGoldAfter} treasuryAfter={treasuryBalanceAfter}",
                decisionJson: $"{{\"clan\":\"{clan.StringId}\",\"amount\":{amt},\"heroAfter\":{heroGoldAfter},\"treasuryAfter\":{treasuryBalanceAfter}}}",
                accepted: true);
            Logger.Info($"TreasuryUserActions.Deposit clan={clan.StringId} amount={amt}d → hero={heroGoldAfter}d clanGold={treasuryBalanceAfter}d");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TreasuryUserActions.TryDeposit failed", ex);
            reason = Tr("内部错误：", "internal_error:") + " " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 玩家主动从玩家氏族 Clan.Gold 取出 <paramref name="amount"/> 金币转入 Hero.MainHero.Gold。
    /// 校验：amount &gt; 0、Clan.PlayerClan 可用、Clan.Gold ≥ amount。
    /// </summary>
    /// <returns>true 表示从 Clan.Gold 扣并入 Hero.Gold 成功；false 表示校验失败或操作失败。</returns>
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

            var hero = Hero.MainHero;
            if (hero == null) { reason = Tr("主角对象不可用", "Hero.MainHero unavailable"); return false; }

            if (clan.Gold < amount)
            {
                reason = Tr($"氏族金币 {clan.Gold} 少于请求 {amount}", $"clan gold {clan.Gold} < requested {amount}");
                return false;
            }

            int amt = checked((int)amount);
            // 先扣 Clan.Gold，后加 Hero；Hero 加失败 → 回滚 Clan.Gold。
            if (!ClanGoldAccess.Change(clan, -amt))
            {
                Logger.Error($"TreasuryUserActions.TryWithdraw: ClanGoldAccess.Change failed (clan={clan.StringId}, amt={amt})");
                reason = Tr("氏族金币写入失败（反射不可用？）", "clan gold write failed (reflection unavailable?)");
                return false;
            }

            try { hero.ChangeHeroGold(amt); }
            catch (Exception ex)
            {
                Logger.Error("TreasuryUserActions.TryWithdraw: ChangeHeroGold failed; 回滚 Clan.Gold", ex);
                ClanGoldAccess.Change(clan, amt);  // 回滚
                reason = Tr("主角金币变更失败：", "hero gold change failed: ") + ex.Message;
                return false;
            }

            treasuryBalanceAfter = clan.Gold;
            heroGoldAfter = hero.Gold;

            try
            {
                var id = clan.StringId;
                if (!string.IsNullOrEmpty(id))
                    WebConfig.FinancialSnapshot.PatchTreasuryBalance(id, clan.Gold);
            }
            catch { /* swallow */ }

            DecisionAuditLogger.LogRule(
                decisionType: "TreasuryWithdraw",
                inputSummary: $"clan={clan.StringId} amount={amt} heroAfter={heroGoldAfter} treasuryAfter={treasuryBalanceAfter}",
                decisionJson: $"{{\"clan\":\"{clan.StringId}\",\"amount\":{amt},\"heroAfter\":{heroGoldAfter},\"treasuryAfter\":{treasuryBalanceAfter}}}",
                accepted: true);
            Logger.Info($"TreasuryUserActions.Withdraw clan={clan.StringId} amount={amt}d → hero={heroGoldAfter}d clanGold={treasuryBalanceAfter}d");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TreasuryUserActions.TryWithdraw failed", ex);
            reason = Tr("内部错误：", "internal_error:") + " " + ex.Message;
            return false;
        }
    }
}
