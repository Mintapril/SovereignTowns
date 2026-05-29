using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
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
///   - <see cref="NotifySallyEnded"/>：component 在到家/销毁时回调，重置冷却
/// </summary>
public sealed class SallyDispatcher
{
    // === 触发参数 ===
    // R2 (DeepSeek audit 2026-05-18)：常量改为 PartyThresholds 配置；缺省时回退到原硬编码。
    // T1 重整 2026-05-18：seed gold 统一到 StPartyComponent.DefaultSeedGold，删除 SallySeedGold 配置项。
    private const float DetectionRadiusDefault = 50f;
    private const float SallyCooldownHoursDefault = 24f;
    private static float DetectionRadius
        => ConfigurationManager.Current?.Thresholds?.SallyDetectionRadius ?? DetectionRadiusDefault;
    private static float SallyCooldownHours
        => ConfigurationManager.Current?.Thresholds?.SallyCooldownHours ?? SallyCooldownHoursDefault;

    private static float SallyExtractionRatio
        => ConfigurationManager.Current?.Thresholds?.SallyExtractionRatio ?? 0.60f;
    private static float SallyTargetPartySizeMultiplier
        => ConfigurationManager.Current?.Thresholds?.SallyTargetPartySizeMultiplier ?? 2.0f;
    private static int SallyCreateMinPartyCount
        => ConfigurationManager.Current?.Thresholds?.SallyCreateMinPartyCount ?? 30;

    // 2026-05-30：聚团敌军 / 必败战斗防护。
    /// <summary>可投送兵力 / 接战时会被卷入的敌军总兵力 的最低比值；低于此值放弃出击（别把驻军送进必败战斗）。</summary>
    private const float SallyMinWinRatio = 1.25f;
    /// <summary>vanilla <c>EncounterModel.GetEncounterJoiningRadius</c> 取不到时的兜底 join 半径（地图单位，v1.3.15=3f）。</summary>
    private const float ClusterJoinRadiusFallback = 3f;
    /// <summary>join 半径放大系数：追击期间敌军会聚拢，保守高估更安全。</summary>
    private const float ClusterRadiusSlack = 1.5f;

    /// <summary>每城上次出击结束（合并/销毁）的时间戳；冷却用。</summary>
    private readonly Dictionary<Settlement, CampaignTime> _lastSallyEndedAt = new();

    private readonly PartyLifecycleManager _lifecycle;

    public SallyDispatcher(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    // ────────── 批量评估接口 ──────────

    /// <summary>
    /// 由 CapitalLogisticsManager 在 logistics tick 派完 MCMF 指令后调用一次。
    /// 遍历 clan 的所有 fief,对每个 Town 独立评估是否要出击。
    /// MCMF 不再决策 sally 头数(2026-05-28 重构,MCMF instruction.Count 从未被实际消费)。
    /// </summary>
    public void EvaluateAllFiefs(Clan clan)
    {
        if (clan == null) return;
        if (!ConfigurationManager.Current.EnabledFeatures.SallyForth) return;
        try
        {
            foreach (var t in clan.Fiefs)
            {
                if (t?.Settlement == null) continue;
                EvaluateFief(t.Settlement);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyDispatcher.EvaluateAllFiefs failed (clan={clan?.StringId})", ex);
        }
    }

    private void EvaluateFief(Settlement settlement)
    {
        try
        {
            if (!settlement.IsTown) return;
            if (settlement.IsUnderSiege) return;
            if (!_lifecycle.CanCreateAnotherParty(settlement, PartyLifecycleManager.KindSallyForth)) return;

            if (_lastSallyEndedAt.TryGetValue(settlement, out var lastEnd))
            {
                var hoursSinceLast = (CampaignTime.Now - lastEnd).ToHours;
                if (hoursSinceLast < SallyCooldownHours)
                {
                    Logger.Debug($"SallyDispatcher.EvaluateFief '{PartyNameFormatter.SafeName(settlement)}': 冷却中 ({hoursSinceLast:F1}h < {SallyCooldownHours}h),跳过");
                    return;
                }
            }

            var garrison = settlement.Town?.GarrisonParty;
            var garrisonCount = garrison?.MemberRoster?.TotalManCount ?? 0;
            if (garrison == null) return;

            var raidTarget = FindRaiderTargetingBoundVillage(settlement);
            if (raidTarget != null)
            {
                TryCreateSallyParty(settlement, garrison, garrisonCount, raidTarget);
                return;
            }

            var target = FindBestEnemyTarget(settlement);
            if (target == null) return;
            TryCreateSallyParty(settlement, garrison, garrisonCount, target);
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyDispatcher.EvaluateFief failed for settlement '{settlement?.StringId}'", ex);
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
                // R6 (DeepSeek audit 2026-05-18)：用 OrNull 避免 HomeSettlement getter 抛 → 整个 GetActiveCombatSallyParties 列表丢失。
                if (sc.HomeSettlementOrNull?.OwnerClan != clan) continue;
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
    /// 重置冷却时间戳。
    /// </summary>
    public void NotifySallyEnded(Settlement home)
    {
        if (home == null) return;
        try
        {
            _lastSallyEndedAt[home] = CampaignTime.Now;
        }
        catch { /* swallow */ }
    }

    // ────────── 内部辅助：找目标 ──────────

    /// <summary>
    /// doc §10.5: 扫本 settlement 下辖（Town.Villages）的被劫掠村庄；若找到，返回村庄附近的敌方 party 作为出击目标。
    /// doc:859 「被劫场景不设搜索半径」语义：村庄层面不限距离（必是下辖村），但实际 sally engage 的是村庄附近的劫掠者 party，
    /// 因此用一个较小的本地半径找具体劫掠者（30 地图单位，远超劫掠者贴近村庄的实际范围）。
    /// 找不到具体劫掠者时返回 null（劫掠状态可能瞬态消失或劫掠者已离开），调用方会 fallthrough 到普通敌方目标扫描。
    /// </summary>
    private static MobileParty? FindRaiderTargetingBoundVillage(Settlement settlement)
    {
        try
        {
            var town = settlement?.Town;
            if (town?.Villages == null) return null;
            var ownFaction = settlement!.MapFaction;
            if (ownFaction == null) return null;

            const float raidSearchRadius = 30f;
            foreach (var bound in town.Villages)
            {
                if (bound?.Settlement == null) continue;
                if (bound.VillageState != Village.VillageStates.BeingRaided) continue;

                var search = MobileParty.StartFindingLocatablesAroundPosition(bound.Settlement.GetPosition2D, raidSearchRadius);
                for (var candidate = MobileParty.FindNextLocatable(ref search);
                     candidate != null;
                     candidate = MobileParty.FindNextLocatable(ref search))
                {
                    if (!candidate.IsActive) continue;
                    if (candidate == MobileParty.MainParty) continue;
                    var faction = candidate.MapFaction;
                    if (faction == null) continue;
                    if (!faction.IsAtWarWith(ownFaction)) continue;
                    return candidate;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"FindRaiderTargetingBoundVillage failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
            return null;
        }
    }

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
                // E15 (DeepSeek audit 2026-05-18)：TotalManCount 含伤兵 + 俘虏 → 高估敌方力量。
                // 用 TotalHealthyCount，再退化到 TotalManCount-TotalWounded，最后兜底 TotalManCount。
                var strength = 0f;
                try
                {
                    var roster = candidate.MemberRoster;
                    if (roster != null)
                    {
                        // 健康兵员 = 全员 - 伤兵；vanilla TotalHealthyCount 在某些版本不存在，用算术。
                        int total = roster.TotalManCount;
                        int wounded = roster.TotalWounded;
                        strength = (float)Math.Max(0, total - wounded);
                    }
                }
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

    /// <summary>估算"出击队接战时会被 vanilla 卷入同一 MapEvent 的敌方总兵力（健康兵）"。
    /// vanilla <c>DefaultEncounterModel.GetEncounterJoiningRadius=3f</c>：战斗发起点该半径内、可加入战斗的
    /// 敌方 party（散兵/劫匪/领主队）都会作为援军加入（<c>FindNonAttachedNpcPartiesWhoWillJoinPlayerEncounter</c>
    /// 反编译实证，join 条件含 <c>MapEvent==null &amp;&amp; CurrentSettlement==null</c>）。多股小劫匪聚团时，只按单只
    /// 目标 2× 抽兵会被淹没，故按目标位置 + join 半径汇总所有交战方兵力。取 max(聚团合计, 单只目标)，半径留余量。</summary>
    private static int EstimateEngagedEnemyStrength(MobileParty target, IFaction? ownFaction)
    {
        if (target == null || ownFaction == null) return 0;
        float radius;
        try { radius = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.EncounterModel?.GetEncounterJoiningRadius ?? ClusterJoinRadiusFallback; }
        catch { radius = ClusterJoinRadiusFallback; }
        radius = Math.Max(radius, ClusterJoinRadiusFallback) * ClusterRadiusSlack;

        int total = 0;
        try
        {
            var search = MobileParty.StartFindingLocatablesAroundPosition(target.GetPosition2D, radius);
            for (var c = MobileParty.FindNextLocatable(ref search); c != null; c = MobileParty.FindNextLocatable(ref search))
            {
                if (c == null || !c.IsActive) continue;
                if (c == MobileParty.MainParty) continue;
                // 已在战斗 / 在城内 → 不会加入新战斗（镜像 vanilla join 条件），不计入。
                if (c.MapEvent != null || c.CurrentSettlement != null) continue;
                var f = c.MapFaction;
                if (f == null || !f.IsAtWarWith(ownFaction)) continue;
                total += HealthyManCount(c);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"SallyDispatcher.EstimateEngagedEnemyStrength failed: {ex.Message}");
            return HealthyManCount(target);
        }
        return Math.Max(total, HealthyManCount(target));
    }

    /// <summary>队伍健康兵力 = TotalManCount − TotalWounded（镜像 <see cref="FindBestEnemyTarget"/> 口径）。</summary>
    private static int HealthyManCount(MobileParty party)
    {
        try
        {
            var roster = party?.MemberRoster;
            if (roster == null) return 0;
            return Math.Max(0, roster.TotalManCount - roster.TotalWounded);
        }
        catch { return 0; }
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

            // 2026-05-30：按"接战时会被 vanilla 卷入同一 MapEvent 的敌方总兵力"估算，而非单只目标。
            // 多股小队聚团（劫匪常见）时，vanilla 会把 join 半径内的同阵营 party 一起拉进战斗，
            // 只按单只目标抽兵会被淹没。详见 EstimateEngagedEnemyStrength。
            int clusterMen = EstimateEngagedEnemyStrength(target, settlement.MapFaction);
            int byTarget = Math.Max(0, (int)Math.Ceiling(clusterMen * SallyTargetPartySizeMultiplier));
            int sallySize = Math.Min(byTarget, Math.Min(extractable, byGarrisonRatio));
            int createMin = SallyCreateMinPartyCount;
            if (sallySize < createMin)
            {
                Logger.Debug($"SallyDispatcher: '{settlement.Name}' sallySize={sallySize} < {createMin} (cluster={clusterMen}×{SallyTargetPartySizeMultiplier:F2}->{byTarget}, garrison={garrisonCount}, cap={SallyExtractionRatio:P0}->{byGarrisonRatio}, minDef={minimumDefenderRatio:P0}->{minDef}), 抽兵过少");
                return;
            }
            // 必败防护：可投送兵力打不过聚团敌军时放弃出击（HighestTierFirst 精锐 + ≥1.25× 才出门）。
            int minWin = (int)Math.Ceiling(clusterMen * SallyMinWinRatio);
            if (sallySize < minWin)
            {
                Logger.Info($"SallyDispatcher: '{settlement.Name}' 放弃出击 — 可投送 {sallySize} < 需 {minWin}（敌军聚团 {clusterMen}×{SallyMinWinRatio:F2}，garrison={garrisonCount} extractable={extractable}）");
                return;
            }

            if (settlement.Town == null) return;

            // T1 重整 (doc §20 #1)：新流程"先创建 party → 抽兵 → helper 扣款 + 注资 + 买粮"。
            // ModTreasury.Charge / Refund 全部由 helper 内部完成，外部不再单独扣款 + 退款。
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

            // ★ 兵员注入完成后立即 snapshot 出发兵员 + 走 helper 完成 seed/资金/买粮
            if (sallyParty.PartyComponent is StSallyPartyComponent sc)
            {
                sc.SnapshotInitialMembers(sallyParty);

                // T1 重整 (doc §20 #1)：统一走基类 helper 处理"扣款 + 注资 + 买粮"。
                // 玩家路径扣款失败 → 把兵还 garrison 并销毁 party；AI 路径不会失败。
                if (!StPartyComponent.TrySeedAndBuyInitialFood(
                    sc, sallyParty, settlement,
                    ExpenseCategory.SallySeed,
                    settlement.OwnerClan,
                    $"sally_seed home={settlement.StringId}"))
                {
                    TroopTransferHelper.TransferBackToGarrison(sallyParty.MemberRoster, garrison.MemberRoster);
                    PartyMergeService.Instance.DestroyAndUntrack(sallyParty, "SallyDispatcher seed failed rollback", deferIfInMapEvent: false);
                    return;
                }
            }

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
                inputSummary: $"home={settlement.StringId} garrison={garrisonCount} moved={moved} target={target.StringId} clusterMen={clusterMen}",
                decisionJson: $"{{\"home\":\"{settlement.StringId}\",\"party\":\"{sallyParty.StringId}\",\"target\":\"{target.StringId}\",\"moved\":{moved},\"clusterMen\":{clusterMen},\"targetMultiplier\":{SallyTargetPartySizeMultiplier:F2},\"garrisonCapRatio\":{SallyExtractionRatio:F2},\"radius\":{DetectionRadius}}}",
                accepted: true);
            Logger.Info($"SallyDispatcher: created sally '{PartyNameFormatter.SafeName(sallyParty)}' for '{settlement.Name}' (moved={moved} troops, target='{PartyNameFormatter.SafeName(target)}')");
        }
        catch (Exception ex)
        {
            Logger.Error($"SallyDispatcher.TryCreateSallyParty failed for '{PartyNameFormatter.SafeName(settlement)}'", ex);
        }
    }
}
