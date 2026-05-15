using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Battle;

/// <summary>
/// 战斗后战利品处置：俘虏招募 / 出售非匹配俘虏 / 出售物品 / 金钱回流。
///
/// 触发路径（双层兜底）：
///   - 层 A：<c>MapEventEnded</c> 立即处理（首选 — 收益最及时）；
///   - 层 B：<c>TransferAndDisband</c> / <c>TransferAndDestroy</c> 兜底
///     （巡逻队/出击队回家解散前再扫一遍，捕捉层 A 漏网情况，例如多场战斗后未到家）。
///
/// 设计原则：
///   - 严格过滤：仅 <c>IsActive</c> 且所属 clan 已被 <see cref="CapitalRegistry"/> 接管的 party；
///   - 三段独立 try-catch（俘虏 / 物品 / 金钱），单段失败不污染其他段；
///   - 用 <see cref="CapitalManager"/> 提供的 capital 作俘虏招募首选目的地；
///   - 出售目的地用"最近自家 town"（squared distance，避免 sqrt 开销）；
///   - 兵种分类逻辑与 <c>CapitalInPlaceRecruiter</c> 完全一致，避免行为漂移。
/// </summary>
public static class BattleLootHandler
{
    /// <summary>
    /// 处置一支胜方 party 的战利品。null / 非受管 clan / 非活跃 / 三个 toggle 全关 → no-op。
    /// </summary>
    /// <param name="winningParty">胜方 party（巡逻队 / 出击队等 ST 管理队伍）。</param>
    /// <param name="capitalRegistry">首府注册表（可空时直接跳过）。</param>
    public static void ProcessPartyLoot(MobileParty? winningParty, CapitalRegistry? capitalRegistry)
    {
        if (winningParty == null) return;

        Clan? partyClan = null;
        try
        {
            if (!winningParty.IsActive) return;
            partyClan = ResolvePartyClan(winningParty);
            if (capitalRegistry == null || !capitalRegistry.IsManagedClanWithCapital(partyClan)) return;
        }
        catch (Exception filterEx)
        {
            Logger.Error($"BattleLootHandler: pre-check failed for '{SafeName(winningParty)}'", filterEx);
            return;
        }

        EnabledFeatures features;
        try
        {
            features = ConfigurationManager.Current.EnabledFeatures;
        }
        catch (Exception cfgEx)
        {
            Logger.Error("BattleLootHandler: failed to read EnabledFeatures, abort", cfgEx);
            return;
        }

        // 三个 toggle 全关 → 早退（避免无谓日志 / 计算）
        if (!features.AutoRecruitMatchingPrisoners
            && !features.AutoSellNonMatchingPrisoners
            && !features.AutoSellLoot)
        {
            return;
        }

        Settlement? capital = null;
        try { capital = capitalRegistry?.GetCapitalForClan(partyClan); }
        catch (Exception capEx) { Logger.Error("BattleLootHandler: GetCapitalSettlement threw", capEx); }

        // ───── 1) 俘虏处理 ─────
        try
        {
            ProcessPrisoners(winningParty, capital, features);
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootHandler.ProcessPrisoners failed for '{SafeName(winningParty)}'", ex);
        }

        // ───── 2) 物品处理 ─────
        try
        {
            ProcessItems(winningParty, partyClan, features);
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootHandler.ProcessItems failed for '{SafeName(winningParty)}'", ex);
        }

        // ───── 3) 金钱处理 ─────
        try
        {
            ProcessGold(winningParty, capital);
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootHandler.ProcessGold failed for '{SafeName(winningParty)}'", ex);
        }
    }

    // ─────────────────────── 俘虏 ───────────────────────

    private static void ProcessPrisoners(MobileParty party, Settlement? capital, EnabledFeatures features)
    {
        var prisonRoster = party.PrisonRoster;
        if (prisonRoster == null || prisonRoster.Count == 0) return;

        int recruited = 0;

        // A) 招募匹配兵种到首府驻军
        if (features.AutoRecruitMatchingPrisoners && capital != null && capital.IsTown)
        {
            recruited = RecruitMatchingPrisonersToCapital(party, capital);
        }

        // B) 出售剩余非匹配俘虏到最近自家 town
        if (features.AutoSellNonMatchingPrisoners)
        {
            int remaining = SafeCount(prisonRoster);
            if (remaining > 0)
            {
                var market = FindNearestOwnedTown(party, ResolvePartyClan(party));
                if (market == null)
                {
                    Logger.Debug($"BattleLootHandler: '{SafeName(party)}' has {remaining} prisoners but no owned market town nearby, keeping in roster");
                }
                else
                {
                    try
                    {
                        // v1.3.15: SellPrisonersAction.ApplyForAllPrisoners(PartyBase seller, PartyBase buyer)
                        SellPrisonersAction.ApplyForAllPrisoners(party.Party, market.Settlement.Party);
                        Logger.Info($"BattleLootHandler: '{SafeName(party)}' sold {remaining} prisoner(s) to '{market.Name}'");
                        DecisionAuditLogger.LogRule(
                            decisionType: "battle_loot_sell_prisoners",
                            inputSummary: $"party={party.StringId} market={market.Settlement.StringId} sold={remaining}",
                            decisionJson: $"{{\"party\":\"{party.StringId}\",\"market\":\"{market.Settlement.StringId}\",\"sold\":{remaining}}}",
                            accepted: true);
                    }
                    catch (Exception sellEx)
                    {
                        Logger.Error($"BattleLootHandler: SellPrisonersAction failed for '{SafeName(party)}' → '{SafeName(market.Settlement)}'", sellEx);
                    }
                }
            }
        }

        if (recruited > 0)
        {
            DecisionAuditLogger.LogRule(
                decisionType: "battle_loot_recruit_prisoners",
                inputSummary: $"party={party.StringId} capital={capital!.StringId} recruited={recruited}",
                decisionJson: $"{{\"party\":\"{party.StringId}\",\"capital\":\"{capital.StringId}\",\"recruited\":{recruited}}}",
                accepted: true);
        }
    }

    /// <summary>
    /// 把 party.PrisonRoster 中匹配首府 rule 非零 ratio 桶的兵种招入首府驻军。
    /// 受 PartySizeLimit + rule.TargetTotalCount 钳制（与 CapitalInPlaceRecruiter 同款语义）。
    /// </summary>
    private static int RecruitMatchingPrisonersToCapital(MobileParty party, Settlement capital)
    {
        var prisonRoster = party.PrisonRoster;
        if (prisonRoster == null) return 0;

        var town = capital.Town;
        if (town == null) return 0;

        var garrison = town.GarrisonParty;
        if (garrison == null)
        {
            try { capital.AddGarrisonParty(); }
            catch (Exception addEx)
            {
                Logger.Error($"BattleLootHandler: AddGarrisonParty failed for '{capital.Name}'", addEx);
                return 0;
            }
            garrison = town.GarrisonParty;
            if (garrison == null) return 0;
        }

        var memberRoster = garrison.MemberRoster;
        if (memberRoster == null) return 0;

        var rule = ConfigurationManager.GetRuleFor(town);
        if (rule == null) return 0;

        int partySizeLimit = garrison.Party?.PartySizeLimit ?? int.MaxValue;
        int cap = Math.Min(partySizeLimit, rule.TargetTotalCount);

        int recruited = 0;
        try
        {
            var snapshot = new List<TroopRosterElement>(prisonRoster.GetTroopRoster());
            foreach (var elem in snapshot)
            {
                if (memberRoster.TotalManCount >= cap) break;
                var ch = elem.Character;
                if (ch == null || ch.IsHero) continue;
                if (elem.Number <= 0) continue;
                if (!TroopMatchesRule(ch, rule)) continue;

                int available = elem.Number;
                int space = cap - memberRoster.TotalManCount;
                int take = Math.Min(available, space);
                if (take <= 0) continue;

                try
                {
                    memberRoster.AddToCounts(ch, take, false, 0, 0);
                    prisonRoster.RemoveTroop(ch, take, default(UniqueTroopDescriptor), 0);
                    recruited += take;
                }
                catch (Exception oneEx)
                {
                    Logger.Warn($"BattleLootHandler: prisoner recruit failed (char='{ch.StringId}', n={take}): {oneEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootHandler.RecruitMatchingPrisonersToCapital iteration failed for '{capital.Name}'", ex);
        }

        if (recruited > 0)
        {
            Logger.Info($"BattleLootHandler: '{SafeName(party)}' recruited {recruited} matching prisoner(s) into '{capital.Name}' garrison");
        }
        return recruited;
    }

    // ─────────────────────── 物品 ───────────────────────

    private static void ProcessItems(MobileParty party, Clan? ownerClan, EnabledFeatures features)
    {
        if (!features.AutoSellLoot) return;
        var itemRoster = party.ItemRoster;
        if (itemRoster == null) return;

        // 快照：避免迭代时修改 ItemRoster
        var snapshot = new List<ItemRosterElement>();
        try
        {
            foreach (var elem in itemRoster) snapshot.Add(elem);
        }
        catch (Exception snapEx)
        {
            Logger.Error($"BattleLootHandler.ProcessItems: snapshot failed for '{SafeName(party)}'", snapEx);
            return;
        }
        if (snapshot.Count == 0) return;

        var market = FindNearestOwnedTown(party, ownerClan);
        if (market == null)
        {
            Logger.Debug($"BattleLootHandler: '{SafeName(party)}' has {snapshot.Count} loot stack(s) but no owned market, keeping items");
            return;
        }

        int soldStacks = 0;
        int soldUnits = 0;
        // B7.22 Fix C：vanilla SellItemsAction 在某些路径下把收益直接 credit 给 hero/clan 而非
        // party.PartyTradeGold，所以单纯看 PartyTradeGold delta 经常显示 +0。
        // 这里同时跟踪 leader.Gold 来正确反映实际入账。
        int partyGoldBefore = party.PartyTradeGold;
        var sellerHero = party.LeaderHero ?? party.ActualClan?.Leader;
        int heroGoldBefore = sellerHero?.Gold ?? 0;

        foreach (var elem in snapshot)
        {
            if (elem.IsEmpty) continue;
            var item = elem.EquipmentElement.Item;
            if (item == null) continue;
            int count = elem.Amount;
            if (count <= 0) continue;

            try
            {
                // v1.3.15: SellItemsAction.Apply(PartyBase seller, PartyBase buyer, ItemRosterElement, Int32, Settlement)
                SellItemsAction.Apply(
                    party.Party,
                    market.Settlement.Party,
                    elem,
                    count,
                    market.Settlement);
                soldStacks++;
                soldUnits += count;
            }
            catch (Exception sellEx)
            {
                Logger.Warn($"BattleLootHandler: SellItemsAction failed for item='{item.StringId}' n={count} ({sellEx.Message}), trying manual fallback");
                // Fallback：手动算价 + 直接 roster 操作
                try
                {
                    int unitPrice = market.GetItemPrice(item, party, isSelling: true);
                    int gross = unitPrice * count;
                    itemRoster.AddToCounts(item, -count);
                    party.PartyTradeGold += gross;
                    soldStacks++;
                    soldUnits += count;
                }
                catch (Exception fbEx)
                {
                    Logger.Error($"BattleLootHandler: manual sell fallback also failed for '{item.StringId}'", fbEx);
                }
            }
        }

        int partyGoldDelta = party.PartyTradeGold - partyGoldBefore;
        int heroGoldDelta = (sellerHero?.Gold ?? 0) - heroGoldBefore;
        int goldEarned = partyGoldDelta + heroGoldDelta;
        if (soldStacks > 0)
        {
            Logger.Info($"BattleLootHandler: '{SafeName(party)}' sold {soldStacks} stack(s) ({soldUnits} unit(s)) at '{market.Name}', +{goldEarned} gold (party Δ={partyGoldDelta}, hero Δ={heroGoldDelta})");
            DecisionAuditLogger.LogRule(
                decisionType: "battle_loot_sell_items",
                inputSummary: $"party={party.StringId} market={market.Settlement.StringId} stacks={soldStacks} units={soldUnits} gold={goldEarned}",
                decisionJson: $"{{\"party\":\"{party.StringId}\",\"market\":\"{market.Settlement.StringId}\",\"stacks\":{soldStacks},\"units\":{soldUnits},\"gold\":{goldEarned}}}",
                accepted: true);
        }
    }

    // ─────────────────────── 金钱 ───────────────────────

    private static void ProcessGold(MobileParty party, Settlement? capital)
    {
        int gold = party.PartyTradeGold;
        if (gold <= 0) return;

        // 优先汇给首府所有人；若 capital == null 直接给 MainHero
        Hero? recipient = null;
        try { recipient = capital?.OwnerClan?.Leader ?? Hero.MainHero; }
        catch { recipient = Hero.MainHero; }

        // party 没有"作为 Hero 的 sender"语义；v1.3.15 GiveGoldAction.ApplyBetweenCharacters 是 Hero→Hero。
        // 出于稳健性，直接走"清空 party gold + 给 MainHero/Leader 加金"的等价路径，避免 sender=null 抛错。
        try
        {
            if (recipient != null && recipient.IsAlive)
            {
                try
                {
                    recipient.ChangeHeroGold(gold);
                }
                catch (Exception ghEx)
                {
                    Logger.Error($"BattleLootHandler: ChangeHeroGold failed for recipient '{recipient.Name}'", ghEx);
                    return;
                }
                party.PartyTradeGold = 0;
                Logger.Info($"BattleLootHandler: '{SafeName(party)}' transferred {gold} gold → '{recipient.Name}' (capital='{capital?.Name?.ToString() ?? "<none>"}')");
                DecisionAuditLogger.LogRule(
                    decisionType: "battle_loot_gold",
                    inputSummary: $"party={party.StringId} gold={gold} recipient={recipient.StringId}",
                    decisionJson: $"{{\"party\":\"{party.StringId}\",\"gold\":{gold},\"recipient\":\"{recipient.StringId}\"}}",
                    accepted: true);
            }
            else
            {
                Logger.Debug($"BattleLootHandler: '{SafeName(party)}' has {gold} gold but no valid recipient, leaving in party");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootHandler.ProcessGold: gold transfer failed for '{SafeName(party)}'", ex);
        }
    }

    // ─────────────────────── 通用兵种匹配 ───────────────────────

    internal static bool TroopMatchesRule(CharacterObject troop, TownGarrisonRule rule)
    {
        try
        {
            return TroopTemplateMatcher.MatchesRule(troop, rule);
        }
        catch { return false; }
    }

    // ─────────────────────── 工具 ───────────────────────

    /// <summary>
    /// 找到离 party 最近的本 clan 自有 town（用 Vec2.DistanceSquared，避免 sqrt）。无 → null。
    /// </summary>
    private static Town? FindNearestOwnedTown(MobileParty party, Clan? ownerClan)
    {
        try
        {
            if (ownerClan == null) return null;

            var partyPos = party.GetPosition2D;
            Town? best = null;
            float bestDistSq = float.MaxValue;

            foreach (var t in Town.AllTowns)
            {
                if (t == null || !t.IsTown) continue;
                if (t.OwnerClan != ownerClan) continue;
                var s = t.Settlement;
                if (s == null) continue;
                try
                {
                    var d2 = partyPos.DistanceSquared(s.GetPosition2D);
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        best = t;
                    }
                }
                catch { /* per-town fault: skip */ }
            }
            return best;
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootHandler.FindNearestOwnedTown failed for '{SafeName(party)}'", ex);
            return null;
        }
    }

    private static int SafeCount(TroopRoster? roster)
    {
        try { return roster?.TotalManCount ?? 0; }
        catch { return 0; }
    }

    private static Clan? ResolvePartyClan(MobileParty party)
    {
        try
        {
            if (party.ActualClan != null) return party.ActualClan;
            if (party.PartyComponent is SallyForthPartyComponent sally) return sally.HomeSettlement?.OwnerClan;
            if (party.PartyComponent is PatrolPartyComponent patrol) return patrol.HomeSettlement?.OwnerClan;
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static string SafeName(MobileParty? party)
    {
        if (party == null) return "<null>";
        try { return party.Name?.ToString() ?? party.StringId ?? "<unnamed>"; }
        catch { return "<error>"; }
    }

    private static string SafeName(Settlement? settlement)
    {
        if (settlement == null) return "<null>";
        try { return settlement.Name?.ToString() ?? settlement.StringId ?? "<unnamed>"; }
        catch { return "<error>"; }
    }
}
