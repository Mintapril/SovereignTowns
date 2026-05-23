using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>
/// ST party 食物 / 队伍资金共用工具。
///
/// 设计原则（与 CLAUDE.md「非作弊基调」对齐）：
///   - Sally / Transfer：短命任务，凭空塞 2-3 天食物，简化复杂度，无队伍资金。
///   - Patrol：终身户外巡逻，从首府所有者扣 2000 第纳尔作启动资金；用资金买食物 + 战利品卖掉补充资金；
///     销毁时余款还首府所有者（自负盈亏）。
///
/// "购买"的实现：vanilla 没有公开的 settlement 级市场粮食购入 API（Town.MarketData 是只读统计），
/// 因此 buy = "估值 + 扣资金 + 凭空塞食物"。这与 IG 的"凭空塞食物"区别是：有资金来源（首府）和经济
/// 闭环（资金随战利品流入 / 食物购买流出），不是无源凭空。
/// </summary>
public static class PartyEconomyHelper
{
    private static ItemObject? _cheapestFoodCache;

    /// <summary>遍历 MBObjectManager 内所有 ItemObject，挑最便宜的 IsFood item；缓存结果。</summary>
    public static ItemObject? GetCheapestFood()
    {
        if (_cheapestFoodCache != null) return _cheapestFoodCache;
        try
        {
            ItemObject? best = null;
            int bestPrice = int.MaxValue;
            var all = MBObjectManager.Instance.GetObjectTypeList<ItemObject>();
            if (all == null) return null;
            foreach (var item in all)
            {
                if (item == null || !item.IsFood) continue;
                int v = item.Value;
                if (v <= 0 || v >= bestPrice) continue;
                best = item;
                bestPrice = v;
            }
            _cheapestFoodCache = best;
            if (best != null) Logger.Info($"PartyEconomyHelper: cheapest food = '{best.StringId}' (value={best.Value})");
            return best;
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.GetCheapestFood failed", ex);
            return null;
        }
    }

    /// <summary>估算 party 在 days 天内的食物需求量（单位）。
    /// 优先用 vanilla MobileParty.FoodChange（已含人数、buff、buildings 等），fallback 用 troops × 0.5。</summary>
    public static int EstimateFoodForDays(MobileParty? party, float days)
    {
        try
        {
            if (party == null || days <= 0f) return 0;
            float abs = 0f;
            try { abs = Math.Abs(party.FoodChange); } catch { abs = 0f; }
            // I3: 防御 NaN / ±Infinity（极端 vanilla buff 链可能产生异常 float）→ 退回 troops 估算
            if (float.IsNaN(abs) || float.IsInfinity(abs)) abs = 0f;
            if (abs < 0.1f)
            {
                int troops = party.MemberRoster?.TotalManCount ?? 0;
                abs = Math.Max(1f, troops * 0.5f);
            }
            return (int)Math.Ceiling(abs * days);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>用 teamFunds 在 settlement 内购买 days 天食物：选 settlement 库存中最便宜的食物，
    /// 用 vanilla SellItemsAction 真实把物品从 settlement.ItemRoster 转到 party.ItemRoster；teamFunds 扣实际花费。
    /// 没在 settlement 内 / settlement 食物缺货 / 资金不够时按可买数量买；返回实际花费。
    /// 注：vanilla SellItemsAction 不自动调 settlement.Gold，巡逻队队伍资金是 mod 内部账，与 hero 钱包 / settlement.Gold 独立。</summary>
    public static int BuyFoodFromSettlement(MobileParty? party, Settlement? settlement, float days, ref int teamFunds)
    {
        // 2026-05-18 v4：放开"必须是 Town"的限制 — village 用 Village.Bound.Town 定价 + village PartyBase 库存。
        // 保守：town 路径完全不变（之前在 Sanala 买 11 grain @ 7d 已验证）；只为 village 加新分支。
        // 早 return 时印 ECON-DIAG（Info 级）以诊断"wool 村连片无食物 / Village.Bound 失效"等真实 fallback 场景。
        if (party?.ItemRoster == null || settlement == null || teamFunds <= 0)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (basic): party='{PartyNameFormatter.SafeName(party)}' settlement='{settlement?.Name?.ToString() ?? "<null>"}' teamFunds={teamFunds} days={days:F1}");
            return 0;
        }

        // 抽取 (pricingTown, counterparty, inventory) 三元组：town 路径与 village 路径不同，但后续买卖逻辑相同。
        Town? pricingTown;
        PartyBase? counterparty;
        ItemRoster? settlementInv;
        if (settlement.Town != null)
        {
            // ── Town 路径（保持与旧版本一致；已在线上验证 ──
            pricingTown = settlement.Town;
            var settlementOwner = ((SettlementComponent)pricingTown).Owner;
            counterparty = settlementOwner;
            settlementInv = settlementOwner?.ItemRoster;
        }
        else if (settlement.IsVillage)
        {
            // ── Village 路径（新增）── 用绑定 town 定价 + village.Party 当库存与 counterparty。
            pricingTown = settlement.Village?.Bound?.Town;
            counterparty = settlement.Party;
            settlementInv = counterparty?.ItemRoster;
            if (pricingTown == null)
            {
                Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (village Bound.Town null): settlement='{settlement.Name}' villageBound='{settlement.Village?.Bound?.StringId ?? "<null>"}'");
                return 0;
            }
        }
        else
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (not town/village): settlement='{settlement.Name}'");
            return 0;
        }
        if (counterparty == null || settlementInv == null)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (no inv): settlement='{settlement.Name}' isVillage={settlement.IsVillage} counterparty='{counterparty?.GetType().Name ?? "<null>"}'");
            return 0;
        }

        int wantUnits = EstimateFoodForDays(party, days);
        if (wantUnits <= 0)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement wantUnits=0: party='{PartyNameFormatter.SafeName(party)}' members={party.MemberRoster?.TotalManCount ?? -1} foodChange={party.FoodChange:F2} days={days:F1}");
            return 0;
        }

        int totalSpent = 0;
        int foodItemsScanned = 0;
        try
        {
            while (wantUnits > 0 && teamFunds > 0)
            {
                int bestIdx = -1;
                int bestPricePerUnit = int.MaxValue;
                for (int i = 0; i < settlementInv.Count; i++)
                {
                    var item = settlementInv.GetItemAtIndex(i);
                    if (item == null || !item.IsFood) continue;
                    var elem = settlementInv.GetElementCopyAtIndex(i);
                    if (elem.Amount <= 0) continue;
                    foodItemsScanned++;
                    int price = pricingTown.GetItemPrice(elem.EquipmentElement, party, isSelling: false);
                    if (price <= 0 || price >= bestPricePerUnit) continue;
                    bestIdx = i;
                    bestPricePerUnit = price;
                }
                if (bestIdx < 0) break;
                var bestElem = settlementInv.GetElementCopyAtIndex(bestIdx);
                int affordable = teamFunds / bestPricePerUnit;
                int actual = Math.Min(Math.Min(wantUnits, bestElem.Amount), affordable);
                if (actual <= 0) break;
                int chunkCost = actual * bestPricePerUnit;
                try
                {
                    SellItemsAction.Apply(counterparty, party.Party, bestElem, actual, settlement);
                    teamFunds -= chunkCost;
                    totalSpent += chunkCost;
                    wantUnits -= actual;
                    var itemName = bestElem.EquipmentElement.Item?.StringId ?? "?";
                    string tag = settlement.IsVillage ? $" (village←Bound={pricingTown.Settlement?.StringId ?? "?"})" : "";
                    Logger.Info($"PartyEconomyHelper.BuyFoodFromSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}'{tag}: bought {actual} '{itemName}' @ {bestPricePerUnit}d (chunk {chunkCost}d, funds={teamFunds})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.BuyFoodFromSettlement SellItemsAction failed", apEx);
                    break;
                }
            }
            if (totalSpent == 0)
            {
                Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement no-food: settlement='{settlement.Name}' isVillage={settlement.IsVillage} invCount={settlementInv.Count} foodItemsScanned={foodItemsScanned} villageType='{settlement.Village?.VillageType?.StringId ?? "<n/a>"}' — 无可买食物（库存无 IsFood 或 amount=0 或定价异常）");
            }
            try { party.ItemRoster.UpdateVersion(); } catch { }
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.BuyFoodFromSettlement threw", ex);
        }
        return totalSpent;
    }

    /// <summary>用 teamFunds 在 settlement 内购买松散坐骑，让无马步兵骑乘以提升大地图移速。
    /// 目标数量 = party.Party.NumberOfMenWithoutHorse − 已有松散坐骑数（每名无马步兵 1 匹即满速，
    /// 多买会进畜群反而减速）。资金不足按可买数量买；返回实际花费。
    /// 坐骑判定：item.HasHorseComponent &amp;&amp; HorseComponent.IsMount。town / village 定价同 BuyFoodFromSettlement。</summary>
    public static int BuyHorsesFromSettlement(MobileParty? party, Settlement? settlement, ref int teamFunds)
    {
        if (party?.ItemRoster == null || settlement == null || teamFunds <= 0) return 0;

        Town? pricingTown;
        PartyBase? counterparty;
        ItemRoster? settlementInv;
        if (settlement.Town != null)
        {
            pricingTown = settlement.Town;
            var settlementOwner = ((SettlementComponent)pricingTown).Owner;
            counterparty = settlementOwner;
            settlementInv = settlementOwner?.ItemRoster;
        }
        else if (settlement.IsVillage)
        {
            pricingTown = settlement.Village?.Bound?.Town;
            counterparty = settlement.Party;
            settlementInv = counterparty?.ItemRoster;
            if (pricingTown == null) return 0;
        }
        else
        {
            return 0;
        }
        if (counterparty == null || settlementInv == null) return 0;

        int wantUnits;
        try
        {
            int footmen = party.Party?.NumberOfMenWithoutHorse ?? 0;
            int existingMounts = party.ItemRoster.NumberOfMounts;
            wantUnits = footmen - existingMounts;
        }
        catch
        {
            return 0;
        }
        if (wantUnits <= 0) return 0;

        int totalSpent = 0;
        try
        {
            while (wantUnits > 0 && teamFunds > 0)
            {
                int bestIdx = -1;
                int bestPricePerUnit = int.MaxValue;
                for (int i = 0; i < settlementInv.Count; i++)
                {
                    var item = settlementInv.GetItemAtIndex(i);
                    if (item == null || !item.HasHorseComponent || item.HorseComponent == null || !item.HorseComponent.IsMount) continue;
                    var elem = settlementInv.GetElementCopyAtIndex(i);
                    if (elem.Amount <= 0) continue;
                    int price = pricingTown.GetItemPrice(elem.EquipmentElement, party, isSelling: false);
                    if (price <= 0 || price >= bestPricePerUnit) continue;
                    bestIdx = i;
                    bestPricePerUnit = price;
                }
                if (bestIdx < 0) break;
                var bestElem = settlementInv.GetElementCopyAtIndex(bestIdx);
                int affordable = teamFunds / bestPricePerUnit;
                int actual = Math.Min(Math.Min(wantUnits, bestElem.Amount), affordable);
                if (actual <= 0) break;
                int chunkCost = actual * bestPricePerUnit;
                try
                {
                    SellItemsAction.Apply(counterparty, party.Party, bestElem, actual, settlement);
                    teamFunds -= chunkCost;
                    totalSpent += chunkCost;
                    wantUnits -= actual;
                    var itemName = bestElem.EquipmentElement.Item?.StringId ?? "?";
                    Logger.Info($"PartyEconomyHelper.BuyHorsesFromSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}': bought {actual} '{itemName}' @ {bestPricePerUnit}d (chunk {chunkCost}d, funds={teamFunds})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.BuyHorsesFromSettlement SellItemsAction failed", apEx);
                    break;
                }
            }
            if (totalSpent == 0)
                Logger.Info($"[ECON-DIAG] BuyHorsesFromSettlement no-mount: settlement='{settlement.Name}' isVillage={settlement.IsVillage} wantUnits={wantUnits} — 无可买坐骑（库存无 mount 物品 / 定价异常 / 资金不足）");
            try { party.ItemRoster.UpdateVersion(); } catch { }
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.BuyHorsesFromSettlement threw", ex);
        }
        return totalSpent;
    }

    /// <summary>把 party.ItemRoster 中所有非食物物品卖给 settlement：用 vanilla SellItemsAction 真实转 + 用 settlement.GetItemPrice 算价。
    /// 收益加到 teamFunds。settlement.Gold 不动（与 vanilla caravan 同模式 — SellItemsAction 不自动转金币）。返回总收益。</summary>
    public static int SellLootToSettlement(MobileParty? party, Settlement? settlement, ref int teamFunds)
    {
        // 2026-05-18 v4：放开"必须是 Town"的限制 — village 用 Village.Bound.Town 定价。settlement-side counterparty 一直是 Settlement.Party。
        if (party?.ItemRoster == null || settlement == null) return 0;
        var pricingTown = settlement.Town ?? settlement.Village?.Bound?.Town;
        if (pricingTown == null)
        {
            Logger.Info($"[ECON-DIAG] SellLootToSettlement skip (no pricingTown): settlement='{settlement.Name}' isVillage={settlement.IsVillage} villageBound='{settlement.Village?.Bound?.StringId ?? "<null>"}'");
            return 0;
        }
        int gained = 0;
        try
        {
            var snapshot = new List<ItemRosterElement>();
            foreach (var slot in party.ItemRoster) snapshot.Add(slot);
            foreach (var slot in snapshot)
            {
                var item = slot.EquipmentElement.Item;
                if (item == null || item.IsFood) continue;  // 食物保留（巡逻队自用）
                int count = slot.Amount;
                if (count <= 0) continue;
                int price = pricingTown.GetItemPrice(slot.EquipmentElement, party, isSelling: true);
                if (price <= 0) continue;
                int totalPrice = price * count;
                try
                {
                    SellItemsAction.Apply(party.Party, settlement.Party, slot, count, settlement);
                    teamFunds += totalPrice;
                    gained += totalPrice;
                    string tag = settlement.IsVillage ? $" (village←Bound={pricingTown.Settlement?.StringId ?? "?"})" : "";
                    Logger.Info($"PartyEconomyHelper.SellLootToSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}'{tag}: sold {count} '{item.StringId}' @ {price}d (+{totalPrice}d, funds={teamFunds})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.SellLootToSettlement SellItemsAction failed for '{item.StringId}'", apEx);
                }
            }
            if (gained > 0)
            {
                try { party.ItemRoster.UpdateVersion(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.SellLootToSettlement threw", ex);
        }
        return gained;
    }

    /// <summary>
    /// 2026-05-18 v4：解散前最终清算专用 — 把 party.ItemRoster 中所有非空物品（含 IsFood）卖给 settlement。
    /// 与 <see cref="SellLootToSettlement"/> 的区别：本方法不跳过食物，因为部队解散后食物没有意义。
    /// 返回总收益，加到 teamFunds。
    /// </summary>
    public static int SellAllItemsToSettlement(MobileParty? party, Settlement? settlement, ref int teamFunds)
    {
        if (party?.ItemRoster == null || settlement == null) return 0;
        var pricingTown = settlement.Town ?? settlement.Village?.Bound?.Town;
        if (pricingTown == null)
        {
            Logger.Info($"[ECON-DIAG] SellAllItemsToSettlement skip (no pricingTown): settlement='{settlement.Name}' isVillage={settlement.IsVillage}");
            return 0;
        }
        int gained = 0;
        try
        {
            var snapshot = new List<ItemRosterElement>();
            foreach (var slot in party.ItemRoster) snapshot.Add(slot);
            foreach (var slot in snapshot)
            {
                var item = slot.EquipmentElement.Item;
                if (item == null) continue;
                int count = slot.Amount;
                if (count <= 0) continue;
                int price = pricingTown.GetItemPrice(slot.EquipmentElement, party, isSelling: true);
                if (price <= 0) continue;
                int totalPrice = price * count;
                try
                {
                    SellItemsAction.Apply(party.Party, settlement.Party, slot, count, settlement);
                    teamFunds += totalPrice;
                    gained += totalPrice;
                    Logger.Info($"PartyEconomyHelper.SellAllItemsToSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}': sold {count} '{item.StringId}'{(item.IsFood ? " (food)" : "")} @ {price}d (+{totalPrice}d, funds={teamFunds})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.SellAllItemsToSettlement SellItemsAction failed for '{item.StringId}'", apEx);
                }
            }
            if (gained > 0)
            {
                try { party.ItemRoster.UpdateVersion(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.SellAllItemsToSettlement threw", ex);
        }
        return gained;
    }

    /// <summary>食物剩余天数 = Food / |FoodChange|。负 / 零变化时返 float.MaxValue（无饿死风险）。</summary>
    public static float FoodDaysRemaining(MobileParty? party)
    {
        try
        {
            if (party == null) return 0f;
            float food = 0f;
            try { food = party.Food; } catch { food = 0f; }
            float change = 0f;
            try { change = party.FoodChange; } catch { change = 0f; }
            // I3: vanilla FoodChange 在 buff 链异常时可能返回 NaN / ±Infinity → 视为安全（0 风险）
            if (float.IsNaN(food) || float.IsInfinity(food)) return 0f;
            if (float.IsNaN(change) || float.IsInfinity(change)) return 0f;
            if (change >= 0f) return float.MaxValue;
            return food / -change;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>从 hero 扣金，最多扣可用余额。返回实际扣到的数。</summary>
    public static int ChargeHero(Hero? hero, int amount)
    {
        if (hero == null || amount <= 0) return 0;
        try
        {
            int available = hero.Gold;
            int deduct = Math.Min(amount, available);
            if (deduct <= 0) return 0;
            // ApplyBetweenCharacters(giver, recipient, amount, disableNotification)
            GiveGoldAction.ApplyBetweenCharacters(hero, null, deduct, disableNotification: true);
            return deduct;
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.ChargeHero '{hero?.StringId}' x{amount} failed", ex);
            return 0;
        }
    }

    /// <summary>还金给 hero。</summary>
    public static int RefundHero(Hero? hero, int amount)
    {
        if (hero == null || amount <= 0) return 0;
        try
        {
            GiveGoldAction.ApplyBetweenCharacters(null, hero, amount, disableNotification: true);
            return amount;
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.RefundHero '{hero?.StringId}' x{amount} failed", ex);
            return 0;
        }
    }
}
