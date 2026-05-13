using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Upgrades;

/// <summary>
/// 一次驻军升级批处理的统计报告。结构体不可变。
/// </summary>
public readonly struct UpgradeReport
{
    public UpgradeReport(int upgradedUnits, int xpSpent, int goldSpent, int candidatesSkipped)
    {
        UpgradedUnits = upgradedUnits;
        XpSpent = xpSpent;
        GoldSpent = goldSpent;
        CandidatesSkipped = candidatesSkipped;
    }

    /// <summary>本批次成功完成升级的兵员数量。</summary>
    public int UpgradedUnits { get; }

    /// <summary>累计扣除的 XP。</summary>
    public int XpSpent { get; }

    /// <summary>累计应扣的金币（实际扣减可能因 vanilla API 异常被吞）。</summary>
    public int GoldSpent { get; }

    /// <summary>因不满足前提（无升级路径 / XP 不足 / 预算不够 / 异常）而跳过的候选数量。</summary>
    public int CandidatesSkipped { get; }

    /// <summary>本批次是否实际产生了至少一次升级。</summary>
    public bool DidWork => UpgradedUnits > 0;
}

/// <summary>
/// 驻军内自动升级服务。无状态、静态方法集合。
/// 设计目标：
///   - 不硬编码任何 stringId；仅依赖 vanilla 暴露的升级图（CharacterObject.UpgradeTargets）。
///   - 单次调用上限受 maxUpgradesPerCall 与 budgetCap 共同约束。
///   - 任何单个候选异常被 try-catch 隔离，不影响后续候选。
///   - 选择策略：低 Tier 优先，确保新兵先于精锐获得升级机会。
/// </summary>
public static class TroopUpgradeService
{
    /// <summary>
    /// 在 home town 驻军内尝试升级一批兵种。
    /// </summary>
    /// <param name="homeTown">玩家自有 Town；其 GarrisonParty 提供 MemberRoster。</param>
    /// <param name="budgetCap">本批次总金币预算上限。&lt;0 视为 0；0 表示禁止任何需付费的升级。</param>
    /// <param name="maxUpgradesPerCall">本批次最多执行多少次升级（每个 element 最多升 1 次）。</param>
    /// <returns>本批次升级统计。失败 / 无操作 → DidWork = false。</returns>
    public static UpgradeReport TryUpgradeGarrison(
        Town homeTown,
        int budgetCap,
        int maxUpgradesPerCall = 20)
    {
        int upgraded = 0;
        int xpSpent = 0;
        int goldSpent = 0;
        int skipped = 0;

        try
        {
            if (homeTown?.Settlement == null)
            {
                Logger.Debug("TroopUpgradeService: homeTown or Settlement is null, skip");
                return new UpgradeReport(0, 0, 0, 0);
            }

            if (budgetCap < 0) budgetCap = 0;
            if (maxUpgradesPerCall <= 0)
            {
                return new UpgradeReport(0, 0, 0, 0);
            }

            var garrisonParty = homeTown.GarrisonParty;
            if (garrisonParty == null)
            {
                Logger.Debug($"TroopUpgradeService: '{homeTown.Settlement.StringId}' has no GarrisonParty, skip");
                return new UpgradeReport(0, 0, 0, 0);
            }

            var roster = garrisonParty.MemberRoster;
            if (roster == null)
            {
                Logger.Debug($"TroopUpgradeService: '{homeTown.Settlement.StringId}' garrison MemberRoster is null, skip");
                return new UpgradeReport(0, 0, 0, 0);
            }

            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();

            // 拷贝快照（避免边遍历边改 roster 引发的迭代失效）
            var snapshot = new List<TroopRosterElement>();
            foreach (var elem in roster.GetTroopRoster())
            {
                snapshot.Add(elem);
            }

            // 候选过滤：非 hero、IsRegular、有可走的升级路径、且当前数量 > 0
            var candidates = new List<TroopRosterElement>();
            foreach (var elem in snapshot)
            {
                var ch = elem.Character;
                if (ch == null) continue;
                if (ch.IsHero) continue;
                if (!ch.IsRegular) continue;
                if (ch.Tier >= rule.MaxTier) continue;
                if (elem.Number <= 0) continue;
                var targets = ch.UpgradeTargets;
                if (targets == null || targets.Length == 0) continue;
                candidates.Add(elem);
            }

            // 低 Tier 优先
            candidates.Sort((a, b) => a.Character.Tier.CompareTo(b.Character.Tier));

            int budgetRemaining = budgetCap;
            var partyBase = garrisonParty.Party;
            var settlement = homeTown.Settlement;

            foreach (var elem in candidates)
            {
                if (upgraded >= maxUpgradesPerCall) break;

                var ch = elem.Character;
                try
                {
                    // 二次确认（roster 已被前面的升级修改过，必须重新读取数量/XP）
                    var currentCount = roster.GetTroopCount(ch);
                    if (currentCount <= 0)
                    {
                        skipped++;
                        continue;
                    }

                    var targets = ch.UpgradeTargets;
                    if (targets == null || targets.Length == 0)
                    {
                        skipped++;
                        continue;
                    }

                    // 从可升级目标中选最符合当前模板模式的目标。
                    CharacterObject? target = null;
                    int targetIndex = -1;
                    float bestScore = float.NegativeInfinity;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (targets[i] != null)
                        {
                            float score = TroopTemplateMatcher.ScoreUpgradeTarget(
                                ch,
                                targets[i],
                                rule,
                                roster,
                                Math.Max(rule.TargetTotalCount, roster.TotalManCount + 1));
                            if (float.IsNegativeInfinity(score)) continue;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                target = targets[i];
                                targetIndex = i;
                            }
                        }
                    }
                    if (target == null || targetIndex < 0)
                    {
                        skipped++;
                        continue;
                    }

                    int xpCost = ch.GetUpgradeXpCost(partyBase, targetIndex);
                    int goldCost = ch.GetUpgradeGoldCost(partyBase, targetIndex);
                    if (goldCost < 0) goldCost = 0;
                    if (xpCost < 0) xpCost = 0;

                    int elementXp = roster.GetElementXp(ch);
                    if (elementXp < xpCost)
                    {
                        skipped++;
                        continue;
                    }

                    if (goldCost > 0 && (budgetRemaining - goldCost) < 0)
                    {
                        // 预算耗尽 — 因候选已按 Tier 升序排，后面普遍 goldCost 更高，提前 break 收益更大
                        skipped++;
                        break;
                    }

                    // 执行兵员置换：从原兵种扣 1，目标兵种 +1
                    roster.RemoveTroop(ch, 1, default, 0);
                    roster.AddToCounts(target, 1);

                    // 扣 XP：在 ch 剩余 element 上扣（若 ch 已被完全移除，FindIndexOfTroop 返回 < 0）
                    var idx = roster.FindIndexOfTroop(ch);
                    if (idx >= 0)
                    {
                        var remainingXp = roster.GetElementXp(idx);
                        var newXp = remainingXp - xpCost;
                        if (newXp < 0) newXp = 0;
                        roster.SetElementXp(idx, newXp);
                    }

                    // 扣金币：vanilla GiveGoldAction.amount 不支持负数；party→settlement 表示 garrison 付费给城。
                    // GarrisonParty.PartyTradeGold 实务上由游戏维护，这一步可能因为驻军金库不足而部分扣；任何异常吞掉不阻塞升级流。
                    if (goldCost > 0)
                    {
                        try
                        {
                            GiveGoldAction.ApplyForPartyToSettlement(partyBase, settlement, goldCost, disableNotification: true);
                        }
                        catch (Exception goldEx)
                        {
                            Logger.Warn($"TroopUpgradeService: gold deduction skipped for upgrade '{ch.StringId}'→'{target.StringId}' cost={goldCost}: {goldEx.Message}");
                        }
                    }

                    upgraded++;
                    xpSpent += xpCost;
                    goldSpent += goldCost;
                    budgetRemaining -= goldCost;

                    Logger.Debug($"  TroopUpgradeService: '{homeTown.Settlement.StringId}' upgraded 1×{ch.StringId}→{target.StringId} (xp={xpCost}, gold={goldCost})");
                }
                catch (Exception perElemEx)
                {
                    skipped++;
                    Logger.Error($"TroopUpgradeService: upgrade attempt failed for '{(ch != null ? ch.StringId : "<null>")}'", perElemEx);
                }
            }

            if (upgraded > 0 || skipped > 0)
            {
                Logger.Info($"TroopUpgradeService: town='{homeTown.Settlement.StringId}' upgraded={upgraded} xp={xpSpent} gold={goldSpent} skipped={skipped} budgetCap={budgetCap}");

                DecisionAuditLogger.LogRule(
                    decisionType: "UpgradeGarrison",
                    inputSummary: $"home={homeTown.Settlement.StringId} budgetCap={budgetCap} candidates={candidates.Count}",
                    decisionJson: $"{{\"home\":\"{homeTown.Settlement.StringId}\",\"upgraded\":{upgraded},\"xpSpent\":{xpSpent},\"goldSpent\":{goldSpent},\"skipped\":{skipped}}}",
                    accepted: upgraded > 0);
            }

            return new UpgradeReport(upgraded, xpSpent, goldSpent, skipped);
        }
        catch (Exception ex)
        {
            Logger.Error("TroopUpgradeService.TryUpgradeGarrison failed", ex);
            return new UpgradeReport(upgraded, xpSpent, goldSpent, skipped);
        }
    }
}
