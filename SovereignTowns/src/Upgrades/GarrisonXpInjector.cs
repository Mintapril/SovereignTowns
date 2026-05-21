using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using SovereignTowns.Common;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Upgrades;

/// <summary>
/// 每日 XP 注入服务。
/// <para>
/// 背景：vanilla 给 town GarrisonParty 的兵种 XP 极少，导致驻军兵种无法触发自然升级。
/// 本服务在 DailyTickSettlement 给每个受管氏族所属 Town 的驻军逐兵种注入一定 XP，使
/// <see cref="TroopUpgradeService.TryUpgradeGarrison"/> 在下一次评估时能读到足以升级的 XP。
/// </para>
/// <para>
/// XP 模型策略（关键技术决策，v1.3.15）：
///   - vanilla 暴露 <c>Campaign.Current.Models.DailyTroopXpBonusModel</c>（实现：
///     <c>DefaultDailyTroopXpBonusModel</c>），用于读取 town 加成；
///   - 总 XP = (PerTroopBaseXp + 城镇加成round(daily*multiplier)) * 兵员数；
///   - 若 vanilla 模型异常（mod 覆盖错误 / 反射失败），回退到纯固定 <c>DailyTroopXpBonus</c>。
/// </para>
/// 全部 vanilla API try-catch 包裹；只走 Logger.Debug 避免日志洪水。
/// </summary>
public static class GarrisonXpInjector
{
    /// <summary>
    /// 当日给单个受管 Town 驻军注入 XP。非受管 Town / 围攻中 / 功能关闭时直接跳过。
    /// </summary>
    /// <param name="settlement">来自 DailyTickSettlement 的 settlement；可能为 null。</param>
    public static void GiveDailyXpToGarrison(Settlement? settlement)
    {
        try
        {
            // 用户明确："xp注入应只在首府进行"。非首府的 town/castle 走首府级兵力调拨而非本城训练。
            // B7.15 multi-clan：广义到任意受管 clan；外层 OnDailyTickSettlement 已按 "settlement == 该 clan 首府"
            // 进行了路由，所以这里只需校验 clan 在 registry 中即可（玩家 / AI 都允许）。
            if (settlement == null) return;
            if (!settlement.IsTown) return;
            var registry = SovereignTowns.Capital.CapitalRegistry.Instance;
            if (registry != null)
            {
                if (!registry.IsManagedClanWithCapital(settlement.OwnerClan)) return;
            }
            else if (settlement.OwnerClan != Clan.PlayerClan) return;

            var features = ConfigurationManager.Current?.EnabledFeatures;
            if (features == null || !features.AutoRecruitment) return;

            var town = settlement.Town;
            if (town == null) return;
            if (town.IsUnderSiege)
            {
                Logger.Debug($"GarrisonXpInjector: '{settlement.StringId}' under siege, skip");
                return;
            }

            var garrison = town.GarrisonParty;
            if (garrison == null)
            {
                // B7.21 Fix A：被清 0 后 vanilla 移除 GarrisonParty，主动重建以便后续招募/XP 能继续运行
                try
                {
                    settlement.AddGarrisonParty();
                    garrison = town.GarrisonParty;
                    Logger.Debug($"GarrisonXpInjector: '{settlement.StringId}' GarrisonParty 重建");
                }
                catch (Exception ex) { Logger.Error($"GarrisonXpInjector: AddGarrisonParty for '{settlement.StringId}' 失败", ex); return; }
                if (garrison == null) return;
            }

            var roster = garrison.MemberRoster;
            if (roster == null)
            {
                Logger.Debug($"GarrisonXpInjector: '{settlement.StringId}' MemberRoster is null, skip");
                return;
            }

            var rule = ConfigurationManager.GetRuleFor(town);
            // 每日 XP = GarrisonXpBasePerDay + 军营(Barracks)等级 × GarrisonXpPerBarracksLevel。
            int perTroopBase = ComputeXpFromBarracks(settlement);

            // vanilla daily bonus：失败时回退 0
            int townBonus = 0;
            try
            {
                var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.DailyTroopXpBonusModel;
                if (model != null)
                {
                    int baseXp = model.CalculateDailyTroopXpBonus(town);
                    float mult = model.CalculateGarrisonXpBonusMultiplier(town);
                    townBonus = TaleWorlds.Library.MathF.Round(baseXp * mult);
                    // doc §13.4：按首府拥有者所属城镇/城堡数量额外乘算（城 1.5、堡 0.5）
                    if (townBonus > 0)
                    {
                        int townCount = 0, castleCount = 0;
                        var ownerClan = town.OwnerClan;
                        if (ownerClan?.Settlements != null)
                        {
                            foreach (var s in ownerClan.Settlements)
                            {
                                if (s == null) continue;
                                if (s.IsTown) townCount++;
                                else if (s.IsCastle) castleCount++;
                            }
                        }
                        float ownerMult = townCount * 1.5f + castleCount * 0.5f;
                        if (ownerMult > 0f)
                        {
                            townBonus = TaleWorlds.Library.MathF.Round(townBonus * ownerMult);
                        }
                    }
                    if (townBonus < 0) townBonus = 0;
                }
            }
            catch (Exception modelEx)
            {
                Logger.Debug($"GarrisonXpInjector: vanilla DailyTroopXpBonusModel failed for '{settlement.StringId}', fallback to fixed only: {modelEx.Message}");
                townBonus = 0;
            }

            int injectedElements = 0;
            int injectedXpTotal = 0;

            // 快照：避免边遍历边改的迭代失效（理论上 AddXpToTroopAtIndex 不改 roster size，但守稳）
            var snapshot = new MBList<TroopRosterElement>();
            try
            {
                foreach (var elem in roster.GetTroopRoster())
                {
                    snapshot.Add(elem);
                }
            }
            catch (Exception snapEx)
            {
                Logger.Debug($"GarrisonXpInjector: snapshot roster failed for '{settlement.StringId}': {snapEx.Message}");
                return;
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                var elem = snapshot[i];
                var ch = elem.Character;
                if (ch == null) continue;
                if (ch.IsHero) continue;
                if (elem.Number <= 0) continue;
                // 仅当兵种 Tier 在规则上限（含）以内才注入，超过上限的精锐不浪费 XP
                if (ch.Tier > rule.MaxTier) continue;

                try
                {
                    // 重新定位 index（理论上 snapshot 顺序与 roster 一致，但 roster 可能被
                    // 同 tick 的其他事件改动 — 用 FindIndexOfTroop 兜底）
                    int idx = roster.FindIndexOfTroop(ch);
                    if (idx < 0) continue;

                    int totalXp = (perTroopBase + townBonus) * elem.Number;
                    if (totalXp <= 0) continue;

                    roster.AddXpToTroopAtIndex(idx, totalXp);
                    injectedElements++;
                    injectedXpTotal += totalXp;
                }
                catch (Exception perElemEx)
                {
                    Logger.Debug($"GarrisonXpInjector: per-element inject failed for '{ch.StringId}' in '{settlement.StringId}': {perElemEx.Message}");
                }
            }

            if (injectedElements > 0)
            {
                Logger.Debug($"GarrisonXpInjector: town='{settlement.StringId}' elements={injectedElements} totalXp={injectedXpTotal} base={perTroopBase} townBonus={townBonus}");
            }

            // 2026-05-12 差距 2 修复：原 TryUpgradeGarrison 仅在 capital-only 路径触发，
            // 导致非首府 town 与所有 castle 累积 XP 却"卡 tier"。XP 注入后立即触发本城升级，
            // 让受管氏族自有的每个城/堡都能正常升级兵种。
            try
            {
                int budgetCap = Math.Max(0, rule?.BudgetLimit ?? 5000);
                TroopUpgradeService.TryUpgradeGarrison(town, budgetCap, maxUpgradesPerCall: 20);
            }
            catch (Exception upgEx)
            {
                Logger.Error($"GarrisonXpInjector: per-settlement upgrade trigger failed for '{settlement.StringId}'", upgEx);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonXpInjector.GiveDailyXpToGarrison failed", ex);
        }
    }

    /// <summary>
    /// 按军营(Barracks)建筑等级派生每日 XP:GarrisonXpBasePerDay + level × GarrisonXpPerBarracksLevel。
    /// 系数取自 GlobalConfig.BuildingBonus;读不到建筑按 0 级。
    /// </summary>
    private static int ComputeXpFromBarracks(Settlement? settlement)
    {
        var cfg = ConfigurationManager.Current?.BuildingBonus;
        int xpBase = cfg?.GarrisonXpBasePerDay ?? 5;
        int xpPerLevel = cfg?.GarrisonXpPerBarracksLevel ?? 5;
        int level = BuildingLevelReader.GetLevel(settlement, StBuilding.Barracks);
        return xpBase + level * xpPerLevel;
    }
}
