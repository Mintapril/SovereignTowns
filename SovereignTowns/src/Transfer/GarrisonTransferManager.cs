using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Transfer;

/// <summary>
/// 驻军调拨执行器：消费首府级调度器产出的 <see cref="TransferTask"/>，
/// 从源城驻军抽兵，创建真实 <see cref="TransferPartyComponent"/> 调拨队伍并护送到目的地，
/// 到达后把兵员注入目的地驻军并解散队伍。
///
/// 设计要点：
///   - 抽兵策略：低 Tier 优先（高 Tier 留守源城防御），并强制源城最终人数 &gt;= MinimumDefenders。
///   - 队伍跟踪：通过 <see cref="PartyLifecycleManager"/> 注册 kind="transfer"，
///     由 lifecycle manager 负责上限 / 空闲检测。
///   - 风险中断：HourlyTick 检测目的地极端危险（Critical），改返回 Source（不转兵）。
/// </summary>
public sealed class GarrisonTransferManager
{
    private const string PartyKind = PartyLifecycleManager.KindTransfer;

    private readonly PartyLifecycleManager _lifecycle;
    private readonly PartyMergeService _mergeService;

    public GarrisonTransferManager(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _mergeService = new PartyMergeService(_lifecycle);
    }

    /// <summary>
    /// 主入口：把一个 <see cref="TransferTask"/> 转换为真实运输队伍。
    /// 任一前置条件失败立即返回 false；不抛异常给调用方。
    /// </summary>
    public bool TryDispatchTransfer(TransferTask task)
    {
        try
        {
            if (task == null)
            {
                Logger.Warn("TryDispatchTransfer: task is null");
                return false;
            }

            var source = task.Source;
            var destination = task.Destination;
            var requested = task.RequestedTroops;

            if (source == null || destination == null)
            {
                Logger.Warn("TryDispatchTransfer: task.Source/Destination is null");
                return false;
            }
            if (requested <= 0)
            {
                Logger.Debug($"  GarrisonTransferManager: requested={requested} <= 0, skip ({source.StringId} -> {destination.StringId})");
                return false;
            }
            if (source == destination)
            {
                Logger.Debug($"  GarrisonTransferManager: source == destination '{source.Name}', skip");
                return false;
            }
            if (source.OwnerClan == null || source.OwnerClan != destination.OwnerClan)
            {
                Logger.Warn($"  GarrisonTransferManager: cross-clan transfer rejected ({source.Name} -> {destination.Name})");
                return false;
            }

            if (!ConfigurationManager.Current.EnabledFeatures.TroopTransfers)
            {
                Logger.Debug($"  GarrisonTransferManager: skipped '{source.Name}' -> '{destination.Name}' — TroopTransfers disabled");
                return false;
            }

            if (!_lifecycle.CanCreateAnotherParty(source, PartyKind))
            {
                Logger.Info($"  GarrisonTransferManager: '{source.Name}' 已达调拨队上限，跳过");
                return false;
            }

            var sourceTown = source.Town;
            var sourceGarrison = sourceTown?.GarrisonParty;
            var sourceRoster = sourceGarrison?.MemberRoster;
            if (sourceTown == null || sourceGarrison == null || sourceRoster == null)
            {
                Logger.Warn($"  GarrisonTransferManager: source '{source.Name}' has no Town/GarrisonParty/MemberRoster");
                return false;
            }

            var rule = ConfigurationManager.GetRuleFor(sourceTown);
            int minDefenders = Math.Max(0, rule.MinimumDefenders);
            int totalAvailable = sourceRoster.TotalManCount;

            // 前置：源城必须有富余兵员承担本次调拨（除留下 MinimumDefenders 外仍 >= requested）
            if (totalAvailable < minDefenders + requested)
            {
                Logger.Info(
                    $"  GarrisonTransferManager: '{source.Name}' total={totalAvailable} < min({minDefenders})+req({requested}), 跳过");
                return false;
            }

            // 从源 GarrisonParty.MemberRoster 抽 roster：低 Tier 优先（高 Tier 留守）
            var transferRoster = TroopRoster.CreateDummyTroopRoster();
            int extracted = ExtractLowestTierTroops(
                sourceRoster, transferRoster, requested, minDefenders);

            if (extracted <= 0)
            {
                Logger.Warn($"  GarrisonTransferManager: '{source.Name}' extracted 0 troops (req={requested}, min={minDefenders}, available={totalAvailable})");
                return false;
            }

            // 创建调拨队伍
            var party = TransferPartyComponent.CreateForRoute(source, destination, transferRoster);
            if (party == null)
            {
                Logger.Warn($"  GarrisonTransferManager: CreateForRoute 返回 null ({source.Name} -> {destination.Name})");
                // 兵员已抽离源城但队伍未创建 → 把兵员还回去，避免凭空蒸发
                TryRestoreToSource(sourceRoster, transferRoster);
                return false;
            }

            _lifecycle.RegisterTrackedParty(party, source, PartyKind);
            try
            {
                party.SetMoveGoToSettlement(destination, MobileParty.NavigationType.Default, false);
            }
            catch (Exception moveEx)
            {
                Logger.Error($"  GarrisonTransferManager: SetMoveGoToSettlement failed ({source.Name} -> {destination.Name})", moveEx);
            }

            DecisionAuditLogger.LogRule(
                decisionType: "DispatchTransfer",
                inputSummary: $"source={source.StringId} dest={destination.StringId} requested={requested} extracted={extracted} priority={task.Priority:F2} reason={task.Reason}",
                decisionJson: $"{{\"source\":\"{source.StringId}\",\"dest\":\"{destination.StringId}\",\"requested\":{requested},\"extracted\":{extracted},\"priority\":{task.Priority:F2}}}",
                accepted: true);

            Logger.Info($"  GarrisonTransferManager: 派出调拨队 '{source.Name}' -> '{destination.Name}' (兵员={extracted}, priority={task.Priority:F1})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TryDispatchTransfer failed", ex);
            return false;
        }
    }

    /// <summary>
    /// HourlyTickPartyEvent 转发：只处理 <see cref="TransferPartyComponent"/> 队伍。
    /// 状态机：到达目的地 → 注入驻军 + 解散；目的地危险 → 改返回 Source。
    /// </summary>
    public void OnHourlyTickParty(MobileParty party)
    {
        try
        {
            // 首行过滤：非本 Mod 调拨队伍立即返回
            if (party?.PartyComponent is not TransferPartyComponent tp) return;
            if (!party.IsActive) return;

            var dest = tp.Destination;
            if (dest == null) return;
            var partyClan = party.ActualClan ?? tp.Source?.OwnerClan ?? dest.OwnerClan;
            if (partyClan != null && dest.OwnerClan != partyClan)
            {
                var fallback = ResolveSafeFallback(tp, partyClan);
                if (fallback != null)
                {
                    if (party.LastVisitedSettlement == fallback)
                    {
                        Logger.Warn($"  TransferParty '{party.Name}': destination '{dest.Name}' owner changed; merging into fallback '{fallback.Name}'");
                        DeliverAndDisband(party, fallback);
                    }
                    else if (party.TargetSettlement != fallback)
                    {
                        Logger.Warn($"  TransferParty '{party.Name}': destination '{dest.Name}' owner changed; rerouting to '{fallback.Name}'");
                        party.SetMoveGoToSettlement(fallback, MobileParty.NavigationType.Default, false);
                    }
                }
                else
                {
                    Logger.Warn($"  TransferParty '{party.Name}': destination '{dest.Name}' owner changed and no safe fallback; disbanding");
                    _mergeService.DisbandAndUntrack(party, "GarrisonTransferManager destination lost");
                }
                return;
            }

            // 1) 已经到达目的地 → 注入驻军 + 解散
            if (party.LastVisitedSettlement == dest)
            {
                DeliverAndDisband(party, dest);
                return;
            }

            // 2) 目的地极端危险 → 改返回 Source（不转兵；等下个 tick 抵达 Source 时由分支 3 解散）
            var risk = RiskAssessmentService.Assess(dest);
            if (risk.Level >= RiskLevel.Critical)
            {
                var src = tp.Source;
                if (src != null && party.TargetSettlement != src)
                {
                    Logger.Warn($"  TransferParty '{party.Name}': 目的地 '{dest.Name}' risk={risk.Level}，改返回 '{src.Name}'");
                    party.SetMoveGoToSettlement(src, MobileParty.NavigationType.Default, false);
                }
                return;
            }

            // 3) 若已回到 Source（被改向后抵达原地），把兵员还给源驻军并解散
            var sourceSettlement = tp.Source;
            if (sourceSettlement != null && party.LastVisitedSettlement == sourceSettlement)
            {
                if (partyClan == null || sourceSettlement.OwnerClan == partyClan)
                {
                    DeliverAndDisband(party, sourceSettlement);
                }
                else
                {
                    var fallback = ResolveSafeFallback(tp, partyClan);
                    if (fallback != null && fallback != sourceSettlement)
                    {
                        Logger.Warn($"  TransferParty '{party.Name}': source '{sourceSettlement.Name}' owner changed; rerouting to '{fallback.Name}'");
                        party.SetMoveGoToSettlement(fallback, MobileParty.NavigationType.Default, false);
                    }
                    else
                    {
                        Logger.Warn($"  TransferParty '{party.Name}': source '{sourceSettlement.Name}' owner changed and no safe fallback; disbanding");
                        _mergeService.DisbandAndUntrack(party, "GarrisonTransferManager source lost");
                    }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTickParty failed", ex);
        }
    }

    private static Settlement? ResolveSafeFallback(TransferPartyComponent transfer, Clan? partyClan)
    {
        try
        {
            if (partyClan == null) return null;
            var source = transfer.Source;
            if (source != null && source.OwnerClan == partyClan) return source;
            return CapitalRegistry.Instance?.GetCapitalForClan(partyClan);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从源驻军 roster 按 Tier 升序抽取兵员到 <paramref name="transferRoster"/>。
    /// 抽到达到 <paramref name="requested"/> 或源 roster 即将跌破 <paramref name="minDefenders"/> 为止。
    /// 返回实际抽出的兵员数。
    /// </summary>
    private static int ExtractLowestTierTroops(
        TroopRoster sourceRoster,
        TroopRoster transferRoster,
        int requested,
        int minDefenders)
    {
        int extracted = 0;
        try
        {
            // 复制元素并按 Tier 升序排序（先抽低 Tier；高 Tier 留守源城）
            List<TroopRosterElement> elements;
            try
            {
                elements = sourceRoster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0)
                    .OrderBy(e => e.Character.Tier)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Error("ExtractLowestTierTroops: GetTroopRoster/sort failed", ex);
                return 0;
            }

            foreach (var elem in elements)
            {
                if (extracted >= requested) break;

                var ch = elem.Character;
                if (ch == null) continue;

                // 运行时再卡一次下限：源 roster 总数不可跌破 minDefenders
                int currentSourceTotal = sourceRoster.TotalManCount;
                int floor = currentSourceTotal - minDefenders;
                if (floor <= 0) break;

                int want = Math.Min(elem.Number, requested - extracted);
                int take = Math.Min(want, floor);
                if (take <= 0) break;

                try
                {
                    sourceRoster.RemoveTroop(ch, take, default, 0);
                    // 2026-05-12 审查 A-WARN：显式 removeDepleted=false xpChange=0 woundedNumber=0，
                    // 与其它处调用方式一致；避免隐式默认 removeDepleted=true 造成语义不一致。
                    transferRoster.AddToCounts(ch, take, false, 0, 0);
                    extracted += take;
                }
                catch (Exception ex)
                {
                    Logger.Error($"ExtractLowestTierTroops: transfer failed for '{ch.StringId}' (take={take})", ex);
                    // 继续尝试后续兵种，不抛
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ExtractLowestTierTroops failed", ex);
        }
        return extracted;
    }

    /// <summary>
    /// 在到达目的地（或被改向后回到 Source）时调用：把队伍 roster 中的非英雄兵员
    /// 注入 <paramref name="targetSettlement"/> 的 GarrisonParty，然后解散并 untrack。
    /// </summary>
    private void DeliverAndDisband(MobileParty party, Settlement targetSettlement)
    {
        try
        {
            int delivered = _mergeService.MergeNonHeroTroopsIntoGarrison(
                party,
                targetSettlement,
                "GarrisonTransferManager.DeliverAndDisband");

            DecisionAuditLogger.LogRule(
                decisionType: "DeliverTransfer",
                inputSummary: $"target={targetSettlement?.StringId} party={party.StringId} delivered={delivered}",
                decisionJson: $"{{\"target\":\"{targetSettlement?.StringId}\",\"delivered\":{delivered}}}",
                accepted: true);

            Logger.Info($"  TransferParty '{party.Name}': 注入 {delivered} 名兵员到 '{targetSettlement?.Name}' 驻军，解散队伍");

            _mergeService.DisbandAndUntrack(party, "GarrisonTransferManager");
        }
        catch (Exception ex)
        {
            Logger.Error("DeliverAndDisband failed", ex);
        }
    }

    /// <summary>
    /// CreateForRoute 失败时把已抽离的兵员还回源驻军，避免凭空蒸发。
    /// </summary>
    private static void TryRestoreToSource(TroopRoster sourceRoster, TroopRoster transferRoster)
    {
        try
        {
            if (sourceRoster == null || transferRoster == null) return;
            foreach (var elem in transferRoster.GetTroopRoster())
            {
                if (elem.Character == null || elem.Character.IsHero) continue;
                sourceRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
            }
            transferRoster.RemoveIf(e => e.Character != null && !e.Character.IsHero);
        }
        catch (Exception ex)
        {
            Logger.Error("TryRestoreToSource failed", ex);
        }
    }
}
