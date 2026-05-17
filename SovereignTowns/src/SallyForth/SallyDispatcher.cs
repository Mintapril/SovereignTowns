using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.SallyForth;

/// <summary>
/// 出击队 Dispatcher。
/// 只负责"何时何地派遣出击队"：评估敌方威胁、扣 ModTreasury、抽兵创建 <see cref="StSallyPartyComponent"/>。
/// 所有"在飞中"的状态机（超时回家、目标失效、战后回家、销毁救援）落在 <see cref="StSallyPartyComponent"/>。
///
/// 保留接口：
///   - <see cref="GetActiveCombatSallyParties"/>：供 StPatrolPartyComponent 评估支援
///   - <see cref="NotifySallyEnded"/>：component 在到家/销毁时回调，重置冷却 + 持续可见计数
/// </summary>
public sealed class SallyDispatcher
{
    // === 触发参数（合理默认；未来挂控制面板可改） ===
    private const float DetectionRadius = 50f;
    private const int InitialSallyGold = 100;

    // B7.22：出击节奏控制（避免一进检测圈就冲、刚回又冲）
    private const float SallyCooldownHours = 24f;   // 上次出击结束后冷却小时数
    private const int MinSustainedTicks = 3;        // 敌人需在视野内连续 N 个 hourly tick 才触发出击

    private static float SallyExtractionRatio
        => ConfigurationManager.Current?.Thresholds?.SallyExtractionRatio ?? 0.60f;
    private static float SallyTargetPartySizeMultiplier
        => ConfigurationManager.Current?.Thresholds?.SallyTargetPartySizeMultiplier ?? 2.0f;
    private static int SallyCreateMinPartyCount
        => ConfigurationManager.Current?.Thresholds?.SallyCreateMinPartyCount ?? 30;

    /// <summary>每城上次出击结束（合并/销毁）的时间戳；冷却用。</summary>
    private readonly Dictionary<Settlement, CampaignTime> _lastSallyEndedAt = new();

    /// <summary>每城连续看到敌方威胁的 tick 计数；用于持续可见性判定。无敌人即清零。</summary>
    private readonly Dictionary<Settlement, int> _enemySustainedTicks = new();

    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public SallyDispatcher(PartyLifecycleManager lifecycle, CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }

    // ────────── Settlement 小时 tick：评估并创建 ──────────

    public void OnHourlyTickSettlement(Settlement settlement)
    {
        if (settlement == null) return;
        if (!settlement.IsTown) return;

        try
        {
            var owningMgr = _capitalRegistry?.GetForSettlement(settlement);
            if (owningMgr is null) return;
            var usableCapital = _capitalRegistry?.GetCapitalForClan(owningMgr.OwnerClan);
            if (usableCapital == null) return;

            if (!ConfigurationManager.Current.EnabledFeatures.SallyForth) return;
            if (usableCapital.Town == null) return;
            if (settlement.IsUnderSiege) return;

            // 上限：每城最多 1 支 sally
            if (!_lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindSallyForth)) return;

            var garrison = settlement.Town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;

            var target = FindBestEnemyTarget(settlement);
            if (target == null)
            {
                _enemySustainedTicks.Remove(settlement);
                return;
            }

            // B7.22：出击冷却 — 上次出击结束后 24h 内不再出
            if (_lastSallyEndedAt.TryGetValue(settlement, out var lastEnd))
            {
                var hoursSinceLast = (CampaignTime.Now - lastEnd).ToHours;
                if (hoursSinceLast < SallyCooldownHours)
                {
                    Logger.Debug($"SallyDispatcher '{PartyNameFormatter.SafeName(settlement)}': 冷却中 ({hoursSinceLast:F1}h < {SallyCooldownHours}h)，跳过");
                    return;
                }
            }

            // B7.22：持续可见性 — 敌人需在视野内连续 N 个 hourly tick 才触发
            int prevTicks = _enemySustainedTicks.TryGetValue(settlement, out var p) ? p : 0;
            int newTicks = prevTicks + 1;
            _enemySustainedTicks[settlement] = newTicks;
            if (newTicks < MinSustainedTicks)
            {
                Logger.Debug($"SallyDispatcher '{PartyNameFormatter.SafeName(settlement)}': 敌方 '{PartyNameFormatter.SafeName(target)}' 已见 {newTicks}/{MinSustainedTicks} 小时");
                return;
            }

            TryCreateSallyParty(settlement, garrison!, garrisonCount, target);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyDispatcher.OnHourlyTickSettlement failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }

    // ────────── 查询接口 ──────────

    /// <summary>
    /// 返回该氏族当前正在 MapEvent 中战斗的 sally 队列表。
    /// 供 StPatrolPartyComponent 评估是否需要派 patrol 赶去支援。
    /// </summary>
    public List<MobileParty> GetActiveCombatSallyParties(Clan clan)
    {
        var result = new List<MobileParty>();
        if (clan == null) return result;
        try
        {
            foreach (var party in MobileParty.AllCustomParties)
            {
                if (party == null || !party.IsActive) continue;
                if (party.PartyComponent is not StSallyPartyComponent sc) continue;
                if (sc.HomeSettlement?.OwnerClan != clan) continue;
                if (party.MapEvent == null) continue;  // 不在战斗中
                result.Add(party);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetActiveCombatSallyParties failed", ex);
        }
        return result;
    }

    /// <summary>
    /// 通知本 settlement 的 sally 周期已结束（StSallyPartyComponent 在到家 / 销毁时调用）。
    /// 重置冷却时间戳 + 清持续可见计数。
    /// </summary>
    public void NotifySallyEnded(Settlement home)
    {
        if (home == null) return;
        try
        {
            _lastSallyEndedAt[home] = CampaignTime.Now;
            _enemySustainedTicks.Remove(home);
        }
        catch { /* swallow */ }
    }

    // ────────── 内部辅助：找目标 ──────────

    private static MobileParty? FindBestEnemyTarget(Settlement settlement)
    {
        try
        {
            var ownFaction = settlement.MapFaction;
            if (ownFaction == null) return null;

            MobileParty? best = null;
            float bestStrength = float.MaxValue;

            var search = MobileParty.StartFindingLocatablesAroundPosition(settlement.GetPosition2D, DetectionRadius);
            for (var candidate = MobileParty.FindNextLocatable(ref search);
                 candidate != null;
                 candidate = MobileParty.FindNextLocatable(ref search))
            {
                if (!candidate.IsActive) continue;
                if (candidate == MobileParty.MainParty) continue;

                var faction = candidate.MapFaction;
                if (faction == null) continue;
                if (!faction.IsAtWarWith(ownFaction)) continue;

                // 评分：优先选力量小的（避免冒险打硬目标）
                var strength = 0f;
                try { strength = (float)(candidate.MemberRoster?.TotalManCount ?? 0); }
                catch { strength = 0f; }
                if (strength < bestStrength)
                {
                    bestStrength = strength;
                    best = candidate;
                }
            }

            return best;
        }
        catch (Exception ex)
        {
            Logger.Error($"FindBestEnemyTarget failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
            return null;
        }
    }

    // ────────── 内部辅助：创建出击队 ──────────

    private void TryCreateSallyParty(Settlement settlement, MobileParty garrison, int garrisonCount, MobileParty target)
    {
        try
        {
            // 目标兵力倍数抽兵，并受实际驻军抽取比例与 MinimumDefenderRatio 钳制。
            var ruleSally = settlement.Town != null ? ConfigurationManager.GetRuleFor(settlement.Town) : null;
            float minimumDefenderRatio = ruleSally?.MinimumDefenderRatio ?? TownGarrisonRule.CreateDefault().MinimumDefenderRatio;
            int minDef = GarrisonThresholdMath.CountFromRatio(garrisonCount, minimumDefenderRatio, minimumWhenPositive: 0);
            int extractable = Math.Max(0, garrisonCount - minDef);
            int byGarrisonRatio = GarrisonThresholdMath.CountFromRatio(garrisonCount, SallyExtractionRatio, minimumWhenPositive: 0);
            int targetMen = Math.Max(0, target.MemberRoster?.TotalManCount ?? 0);
            int byTarget = Math.Max(0, (int)Math.Ceiling(targetMen * SallyTargetPartySizeMultiplier));
            int sallySize = Math.Min(byTarget, Math.Min(extractable, byGarrisonRatio));
            int createMin = SallyCreateMinPartyCount;
            if (sallySize < createMin)
            {
                Logger.Debug($"SallyDispatcher: '{settlement.Name}' sallySize={sallySize} < {createMin} (target={targetMen}×{SallyTargetPartySizeMultiplier:F2}->{byTarget}, garrison={garrisonCount}, cap={SallyExtractionRatio:P0}->{byGarrisonRatio}, minDef={minimumDefenderRatio:P0}->{minDef}), 抽兵过少");
                return;
            }

            if (settlement.Town == null) return;

            // B7.27：派出 sally 前先扣本钱（仅玩家氏族）
            bool shouldChargeSally = CapitalRegistry.ShouldChargeClan(settlement.OwnerClan);
            if (shouldChargeSally)
            {
                if (!ModTreasury.CanAfford(InitialSallyGold))
                {
                    Logger.Info($"SallyDispatcher: '{settlement.Name}' 玩家金币不足 (need {InitialSallyGold})，跳过出击");
                    return;
                }
                if (!ModTreasury.Charge(ExpenseCategory.SallySeed, InitialSallyGold, $"sally_seed home={settlement.StringId}"))
                {
                    Logger.Info($"SallyDispatcher: '{settlement.Name}' ModTreasury.Charge 拒绝，跳过出击");
                    return;
                }
            }

            var sallyParty = StSallyPartyComponent.CreateForTown(settlement.Town, target);
            if (sallyParty == null)
            {
                Logger.Warn($"SallyDispatcher: CreateForTown returned null for '{settlement.Name}'");
                return;
            }

            // 从 garrison 抽兵（HighestTierFirst — 精锐先出击，仿 GDS）
            int moved = TroopTransferHelper.TransferFromGarrison(
                garrison.MemberRoster,
                sallyParty.MemberRoster,
                sallySize,
                TroopTransferHelper.SortStrategy.HighestTierFirst);
            if (moved < createMin)
            {
                Logger.Warn($"SallyDispatcher: '{settlement.Name}' transferred only {moved} troops < createMin {createMin}, aborting");
                TroopTransferHelper.TransferBackToGarrison(sallyParty.MemberRoster, garrison.MemberRoster);
                PartyMergeService.Instance.DestroyAndUntrack(sallyParty, "SallyDispatcher rollback", deferIfInMapEvent: false);
                return;
            }

            // ★ 兵员注入完成后立即 snapshot 出发兵员
            if (sallyParty.PartyComponent is StSallyPartyComponent sc) sc.SnapshotInitialMembers(sallyParty);

            // AI 编排：交给 vanilla 战斗系统
            try
            {
                sallyParty.Ai?.SetDoNotMakeNewDecisions(true);
                sallyParty.SetMoveEngageParty(target, MobileParty.NavigationType.Default);
                // B5 F1: 防止 sally party 被 vanilla 拉去加入玩家战斗
                sallyParty.ShouldJoinPlayerBattles = false;
            }
            catch (Exception aiEx)
            {
                Logger.Error($"SallyDispatcher: AI directive failed for '{PartyNameFormatter.SafeName(sallyParty)}'", aiEx);
            }

            _lifecycle.RegisterTrackedParty(sallyParty, settlement, PartyLifecycleManager.KindSallyForth);

            DecisionAuditLogger.LogRule(
                decisionType: "create_sally_party",
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} moved={moved} target={target.StringId} targetMen={targetMen}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{sallyParty.StringId}\",\"target\":\"{target.StringId}\",\"moved\":{moved},\"targetMen\":{targetMen},\"targetMultiplier\":{SallyTargetPartySizeMultiplier:F2},\"garrisonCapRatio\":{SallyExtractionRatio:F2},\"radius\":{DetectionRadius}}}",
                accepted: true);
            Logger.Info($"SallyDispatcher: created sally '{PartyNameFormatter.SafeName(sallyParty)}' for '{settlement.Name}' (moved={moved} troops, target='{PartyNameFormatter.SafeName(target)}')");
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyDispatcher.TryCreateSallyParty failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }
}
