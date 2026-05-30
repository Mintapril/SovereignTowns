using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>
/// ST party 食物 / 队伍资金共用工具。
///
/// 设计原则（与 CLAUDE.md「非作弊基调」对齐 + #6 反编译实证）：
///   - Sally / Transfer：短命任务，简化复杂度。
///   - Patrol / Recruiter：派出时扣 seed 进 <see cref="MobileParty.PartyTradeGold"/>；用此钱袋买食物 +
///     战利品卖回补充；销毁时余款退还首府所有者（自负盈亏）。
///
/// 经济闭环（2026-05-24 反编译实证）：
///   - 钱袋 = vanilla <see cref="MobileParty.PartyTradeGold"/>（自动持久化，自动 clamp ≥0），不再有 mod 自建的
///     <c>_teamFunds</c> 副本。
///   - 物品转移：mod 手动 <c>ItemRoster.AddToCounts</c> 两侧（不通过 <c>SellItemsAction.Apply</c>，因为
///     custom party <c>IsCaravan=false</c>，后者会走 LeaderHero 分支，custom party LeaderHero==null 导致漏单边）。
///   - 金币转移：mod 调 <see cref="GiveGoldAction.ApplyForPartyToSettlement"/> /
///     <see cref="GiveGoldAction.ApplyForSettlementToParty"/> —— vanilla 公开 API，按 IsMobile/IsSettlement
///     正确双侧增减 PartyTradeGold ↔ Settlement.Gold，与 vanilla caravan 走 SellItemsAction 的 IsCaravan 分支
///     等价闭环。
/// </summary>
public static class PartyEconomyHelper
{
    private static ItemObject? _cheapestFoodCache;

    public static void ResetCaches()
    {
        _cheapestFoodCache = null;
    }

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

    /// <summary>把 party 的食物库存「免费」补足到 <paramref name="targetDays"/> 天用量 —— 专给卫队这类
    /// 「常驻首府、驻军等价」的独立 MobileParty 用。
    ///
    /// 背景（2026-05-30 ilspycmd 反编译实证 v1.3.15）：
    ///   vanilla 驻军（<c>GarrisonPartyComponent</c>）由 settlement 食物模型免费供养、永不靠自带 ItemRoster；
    ///   卫队刻意**不**继承 GarrisonPartyComponent（规避军饷 / vanilla AI），因此 vanilla 不会喂它。
    ///   一旦 <c>PartyBase.IsStarving</c>（= <c>_remainingFoodPercentage &lt; 0</c>）为真，
    ///   <c>DefaultPartyHealingModel.GetDailyHealingForRegulars</c> 对非驻军返回 <c>-0.25×TotalRegulars</c>（负值），
    ///   <c>PartyHealCampaignBehavior.OnQuarterDailyPartyTick → ReduceHpMemberRegulars</c> 会把健康兵
    ///   逐日转成伤兵且永不痊愈 —— 即「卫队全员受伤且不恢复」的根因。
    ///   喂饱后卫队驻扎要塞（首府）→ vanilla 每日 <c>+5（基础）+10（In Settlement）</c> 自动痊愈，无需手动 heal。
    ///
    /// 为何「免费」而非走 <see cref="BuyFoodFromSettlement"/>：围城时市场买不到食物，但被围的卫队恰恰最需要
    /// 痊愈；驻军等价物理应像 vanilla 驻军一样被无条件供养。仅补差额，不会无限堆叠，也不动金币 / 市场库存。
    /// 返回实际补充的食物单位数。</summary>
    public static int TopUpFoodFree(MobileParty? party, float targetDays)
    {
        try
        {
            var roster = party?.ItemRoster;
            if (roster == null || targetDays <= 0f) return 0;
            // 空队不需供养（无人可饿 / 可伤）。
            if ((party!.MemberRoster?.TotalManCount ?? 0) <= 0) return 0;

            int target = EstimateFoodForDays(party, targetDays);
            if (target <= 0) return 0;

            // 统计现有食物单位（已喂过的就不重复堆叠）。
            int have = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var item = roster.GetItemAtIndex(i);
                if (item == null || !item.IsFood) continue;
                int amount = roster.GetElementCopyAtIndex(i).Amount;
                if (amount > 0) have += amount;
            }

            int deficit = target - have;
            if (deficit <= 0) return 0;

            var food = GetCheapestFood();
            if (food == null) return 0;

            roster.AddToCounts(new EquipmentElement(food), deficit);
            try { roster.UpdateVersion(); } catch { }
            Logger.Debug($"PartyEconomyHelper.TopUpFoodFree '{PartyNameFormatter.SafeName(party)}': +{deficit} '{food.StringId}' (have={have} target={target}@{targetDays:F0}d)");
            return deficit;
        }
        catch (Exception ex)
        {
            Logger.Warn("PartyEconomyHelper.TopUpFoodFree failed", ex);
            return 0;
        }
    }

    /// <summary>估算 party 从 from 走到 to 需要的游戏天数：寻路距离(units) ÷ 速度(units/游戏小时) ÷ 24。
    /// 口径与 BaseSettlementVisitScheduler.ComputeEtaHours / UnifiedGarrisonSolver.EtaTicks 一致。
    /// 不可达 / 异常 / from==to → 返回 0（调用方用兜底天数）。</summary>
    public static float EstimateTravelDays(MobileParty? party, Settlement? from, Settlement? to)
    {
        try
        {
            if (party == null || from == null || to == null || from == to) return 0f;
            float dist;
            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MapDistanceModel;
            if (model != null)
            {
                float d = model.GetDistance(from, to, false, false,
                    MobileParty.NavigationType.Default, out _);
                dist = (d > 0f && d < TaleWorlds.CampaignSystem.ComponentInterfaces.MapDistanceModel.PossibleMaximumMapBoundary)
                    ? d
                    : (from.GetPosition2D - to.GetPosition2D).Length;
            }
            else
            {
                dist = (from.GetPosition2D - to.GetPosition2D).Length;
            }
            if (dist <= 0f || float.IsNaN(dist) || float.IsInfinity(dist)) return 0f;
            float speed = Math.Max(party.Speed, 0.1f);   // units / 游戏小时
            float daysOut = dist / speed / 24f;
            if (float.IsNaN(daysOut) || float.IsInfinity(daysOut) || daysOut < 0f) return 0f;
            return daysOut;
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.EstimateTravelDays failed: {ex.Message}");
            return 0f;
        }
    }

    /// <summary>从 settlement 库存购买 days 天食物。预算 = <c>party.PartyTradeGold</c>。
    /// 物品手动 AddToCounts 两侧；金币用 <see cref="GiveGoldAction.ApplyForPartyToSettlement"/>
    /// 让 vanilla 同时更新 party.PartyTradeGold ↔ settlement.Gold（双侧闭环）。
    /// 成功后左下角推一条玩家消息（仅玩家氏族部队）。返回实际花费第纳尔。</summary>
    public static int BuyFoodFromSettlement(MobileParty? party, Settlement? settlement, float days)
    {
        if (party?.ItemRoster == null || settlement == null)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (basic): party='{PartyNameFormatter.SafeName(party)}' settlement='{settlement?.Name?.ToString() ?? "<null>"}' days={days:F1}");
            return 0;
        }
        int budget = party.PartyTradeGold;
        if (budget <= 0)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (no funds): party='{PartyNameFormatter.SafeName(party)}' PartyTradeGold={budget} days={days:F1}");
            return 0;
        }

        // (pricingTown, settlementInv) 三元组：town 走 town.Owner.ItemRoster，village 走 village.Party.ItemRoster + Bound.Town 定价
        Town? pricingTown;
        ItemRoster? settlementInv;
        if (settlement.Town != null)
        {
            pricingTown = settlement.Town;
            settlementInv = ((SettlementComponent)pricingTown).Owner?.ItemRoster;
        }
        else if (settlement.IsVillage)
        {
            pricingTown = settlement.Village?.Bound?.Town;
            settlementInv = settlement.Party?.ItemRoster;
            if (pricingTown == null)
            {
                Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (village Bound.Town null): settlement='{settlement.Name}'");
                return 0;
            }
        }
        else
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (not town/village): settlement='{settlement.Name}'");
            return 0;
        }
        if (settlementInv == null)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement skip (no inv): settlement='{settlement.Name}' isVillage={settlement.IsVillage}");
            return 0;
        }

        int wantUnits = EstimateFoodForDays(party, days);
        if (wantUnits <= 0)
        {
            Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement wantUnits=0: party='{PartyNameFormatter.SafeName(party)}' members={party.MemberRoster?.TotalManCount ?? -1} foodChange={party.FoodChange:F2} days={days:F1}");
            return 0;
        }

        int totalSpent = 0;
        int totalUnits = 0;
        string? firstItemId = null;
        int foodItemsScanned = 0;
        try
        {
            while (wantUnits > 0 && party.PartyTradeGold > 0)
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
                int affordable = party.PartyTradeGold / bestPricePerUnit;
                int actual = Math.Min(Math.Min(wantUnits, bestElem.Amount), affordable);
                if (actual <= 0) break;
                int chunkCost = actual * bestPricePerUnit;
                try
                {
                    // 1) 物品转移：settlement -= actual; party += actual
                    settlementInv.AddToCounts(bestElem.EquipmentElement, -actual);
                    party.ItemRoster.AddToCounts(bestElem.EquipmentElement, actual);
                    // 2) 金币转移（vanilla 闭环 API）：party.PartyTradeGold -= chunkCost; settlement.Gold += chunkCost
                    GiveGoldAction.ApplyForPartyToSettlement(party.Party, settlement, chunkCost, disableNotification: true);
                    totalSpent += chunkCost;
                    totalUnits += actual;
                    wantUnits -= actual;
                    firstItemId ??= bestElem.EquipmentElement.Item?.StringId;
                    var itemName = bestElem.EquipmentElement.Item?.StringId ?? "?";
                    string tag = settlement.IsVillage ? $" (village←Bound={pricingTown.Settlement?.StringId ?? "?"})" : "";
                    Logger.Info($"PartyEconomyHelper.BuyFoodFromSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}'{tag}: bought {actual} '{itemName}' @ {bestPricePerUnit}d (chunk {chunkCost}d, partyTradeGold={party.PartyTradeGold})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.BuyFoodFromSettlement transfer failed", apEx);
                    break;
                }
            }
            if (totalSpent == 0)
            {
                Logger.Info($"[ECON-DIAG] BuyFoodFromSettlement no-food: settlement='{settlement.Name}' isVillage={settlement.IsVillage} invCount={settlementInv.Count} foodItemsScanned={foodItemsScanned} villageType='{settlement.Village?.VillageType?.StringId ?? "<n/a>"}'");
            }
            try { party.ItemRoster.UpdateVersion(); } catch { }
            // 玩家可见提示（仅玩家氏族部队）
            if (totalSpent > 0) TryShowBuyMessage(party, settlement, totalUnits, firstItemId, totalSpent);
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.BuyFoodFromSettlement threw", ex);
        }
        return totalSpent;
    }

    private static void TryShowBuyMessage(MobileParty party, Settlement settlement, int units, string? itemId, int totalCost)
    {
        try
        {
            if (party?.ActualClan != Clan.PlayerClan) return;  // 仅玩家氏族部队
            var template = new TextObject(
                "{=ST_Msg_PartyBoughtFood}[Sovereign Towns] {PARTY} bought {UNITS} {ITEM} at {WHERE} (-{COST}d).");
            template.SetTextVariable("PARTY", (TextObject?)party.Name ?? new TextObject("{=ST_Common_UnknownEntity}(unknown)"));
            template.SetTextVariable("UNITS", units);
            template.SetTextVariable("ITEM", itemId ?? "food");
            template.SetTextVariable("WHERE", (TextObject?)settlement?.Name ?? new TextObject("{=ST_Common_Unknown}unknown"));
            template.SetTextVariable("COST", totalCost);
            InformationManager.DisplayMessage(new InformationMessage(template.ToString()));
        }
        catch (Exception ex) { Logger.Warn($"TryShowBuyMessage failed: {ex.Message}"); }
    }

    /// <summary>从 settlement 内购买松散坐骑（无马步兵骑乘以提升大地图移速）。预算 = <c>party.PartyTradeGold</c>。
    /// 目标 = NumberOfMenWithoutHorse − 已有松散坐骑数。物品 + 金币转移同 <see cref="BuyFoodFromSettlement"/>。</summary>
    public static int BuyHorsesFromSettlement(MobileParty? party, Settlement? settlement)
    {
        if (party?.ItemRoster == null || settlement == null || party.PartyTradeGold <= 0) return 0;

        Town? pricingTown;
        ItemRoster? settlementInv;
        if (settlement.Town != null)
        {
            pricingTown = settlement.Town;
            settlementInv = ((SettlementComponent)pricingTown).Owner?.ItemRoster;
        }
        else if (settlement.IsVillage)
        {
            pricingTown = settlement.Village?.Bound?.Town;
            settlementInv = settlement.Party?.ItemRoster;
            if (pricingTown == null) return 0;
        }
        else
        {
            return 0;
        }
        if (settlementInv == null) return 0;

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
            while (wantUnits > 0 && party.PartyTradeGold > 0)
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
                int affordable = party.PartyTradeGold / bestPricePerUnit;
                int actual = Math.Min(Math.Min(wantUnits, bestElem.Amount), affordable);
                if (actual <= 0) break;
                int chunkCost = actual * bestPricePerUnit;
                try
                {
                    settlementInv.AddToCounts(bestElem.EquipmentElement, -actual);
                    party.ItemRoster.AddToCounts(bestElem.EquipmentElement, actual);
                    GiveGoldAction.ApplyForPartyToSettlement(party.Party, settlement, chunkCost, disableNotification: true);
                    totalSpent += chunkCost;
                    wantUnits -= actual;
                    var itemName = bestElem.EquipmentElement.Item?.StringId ?? "?";
                    Logger.Info($"PartyEconomyHelper.BuyHorsesFromSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}': bought {actual} '{itemName}' @ {bestPricePerUnit}d (chunk {chunkCost}d, partyTradeGold={party.PartyTradeGold})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.BuyHorsesFromSettlement transfer failed", apEx);
                    break;
                }
            }
            if (totalSpent == 0)
                Logger.Info($"[ECON-DIAG] BuyHorsesFromSettlement no-mount: settlement='{settlement.Name}' isVillage={settlement.IsVillage} wantUnits={wantUnits}");
            try { party.ItemRoster.UpdateVersion(); } catch { }
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.BuyHorsesFromSettlement threw", ex);
        }
        return totalSpent;
    }

    /// <summary>把 party.ItemRoster 中所有非食物物品卖给 settlement：物品手动 AddToCounts 两侧，
    /// 金币用 <see cref="GiveGoldAction.ApplyForSettlementToParty"/>（settlement.Gold -= price; party.PartyTradeGold += price）。
    /// 食物保留供巡逻队自用。返回总收益。</summary>
    public static int SellLootToSettlement(MobileParty? party, Settlement? settlement)
    {
        if (party?.ItemRoster == null || settlement == null) return 0;
        var pricingTown = settlement.Town ?? settlement.Village?.Bound?.Town;
        if (pricingTown == null)
        {
            Logger.Info($"[ECON-DIAG] SellLootToSettlement skip (no pricingTown): settlement='{settlement.Name}' isVillage={settlement.IsVillage}");
            return 0;
        }
        var settlementRoster = settlement.Party?.ItemRoster;
        if (settlementRoster == null)
        {
            Logger.Warn($"PartyEconomyHelper.SellLootToSettlement skip (settlement roster missing): settlement='{settlement.Name}'");
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
                    party.ItemRoster.AddToCounts(slot.EquipmentElement, -count);
                    settlementRoster.AddToCounts(slot.EquipmentElement, count);
                    GiveGoldAction.ApplyForSettlementToParty(settlement, party.Party, totalPrice, disableNotification: true);
                    gained += totalPrice;
                    string tag = settlement.IsVillage ? $" (village←Bound={pricingTown.Settlement?.StringId ?? "?"})" : "";
                    Logger.Info($"PartyEconomyHelper.SellLootToSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}'{tag}: sold {count} '{item.StringId}' @ {price}d (+{totalPrice}d, partyTradeGold={party.PartyTradeGold})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.SellLootToSettlement transfer failed for '{item.StringId}'", apEx);
                }
            }
            if (gained > 0) { try { party.ItemRoster.UpdateVersion(); } catch { } }
        }
        catch (Exception ex)
        {
            Logger.Warn($"PartyEconomyHelper.SellLootToSettlement threw", ex);
        }
        return gained;
    }

    /// <summary>解散前最终清算专用 — 把 party.ItemRoster 中所有非空物品（含 IsFood）卖给 settlement。
    /// 与 <see cref="SellLootToSettlement"/> 的区别：不跳过食物，因部队解散后食物没有意义。返回总收益。</summary>
    public static int SellAllItemsToSettlement(MobileParty? party, Settlement? settlement)
    {
        if (party?.ItemRoster == null || settlement == null) return 0;
        var pricingTown = settlement.Town ?? settlement.Village?.Bound?.Town;
        if (pricingTown == null)
        {
            Logger.Info($"[ECON-DIAG] SellAllItemsToSettlement skip (no pricingTown): settlement='{settlement.Name}' isVillage={settlement.IsVillage}");
            return 0;
        }
        var settlementRoster = settlement.Party?.ItemRoster;
        if (settlementRoster == null)
        {
            Logger.Warn($"PartyEconomyHelper.SellAllItemsToSettlement skip (settlement roster missing): settlement='{settlement.Name}'");
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
                    party.ItemRoster.AddToCounts(slot.EquipmentElement, -count);
                    settlementRoster.AddToCounts(slot.EquipmentElement, count);
                    GiveGoldAction.ApplyForSettlementToParty(settlement, party.Party, totalPrice, disableNotification: true);
                    gained += totalPrice;
                    Logger.Info($"PartyEconomyHelper.SellAllItemsToSettlement '{PartyNameFormatter.SafeName(party)}' @ '{settlement.Name}': sold {count} '{item.StringId}'{(item.IsFood ? " (food)" : "")} @ {price}d (+{totalPrice}d, partyTradeGold={party.PartyTradeGold})");
                }
                catch (Exception apEx)
                {
                    Logger.Warn($"PartyEconomyHelper.SellAllItemsToSettlement transfer failed for '{item.StringId}'", apEx);
                }
            }
            if (gained > 0) { try { party.ItemRoster.UpdateVersion(); } catch { } }
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
            if (party == null) return float.MaxValue;
            float food = 0f;
            try { food = party.Food; } catch { food = 0f; }
            float change = 0f;
            try { change = party.FoodChange; } catch { change = 0f; }
            // I3: vanilla FoodChange 在 buff 链异常时可能返回 NaN / ±Infinity → 视为安全（0 风险）
            if (float.IsNaN(food) || float.IsInfinity(food)) return float.MaxValue;
            if (float.IsNaN(change) || float.IsInfinity(change)) return float.MaxValue;
            if (change >= 0f) return float.MaxValue;
            return food / -change;
        }
        catch
        {
            return float.MaxValue;
        }
    }

    // ChargeHero / RefundHero 已删除（2026-05-24 Plan C 重构）：
    //   - 队伍 seed: 改调 GiveGoldAction.ApplyForCharacterToParty(leader, party.Party, amount)
    //     一步完成 hero.Gold -= + party.PartyTradeGold += 双侧闭环。
    //   - 队伍 refund: 改调 GiveGoldAction.ApplyForPartyToCharacter(party.Party, leader, party.PartyTradeGold)。
    // 调用方搬迁完成后本注释可删。
}
