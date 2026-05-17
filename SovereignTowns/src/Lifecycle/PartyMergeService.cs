using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Lifecycle;

/// <summary>
/// Process-wide singleton. 通过 <see cref="Initialize"/> 在 OnSessionLaunched 注入 lifecycle 引用，
/// 之后所有调用方使用 <see cref="Instance"/> 直接访问，避免每个 Manager 自带一份实例。
/// </summary>
public sealed class PartyMergeService
{
    private static PartyMergeService? _instance;
    public static PartyMergeService Instance =>
        _instance ?? throw new InvalidOperationException(
            "PartyMergeService.Initialize must be called once during OnSessionLaunched before Instance access");

    public static void Initialize(PartyLifecycleManager lifecycle)
    {
        if (lifecycle is null) throw new ArgumentNullException(nameof(lifecycle));
        _instance = new PartyMergeService(lifecycle);
    }

    /// 仅用于测试 / 卸载场景（mod unload 时清空，下次 Initialize 重建）。
    public static void ResetForReload() => _instance = null;

    private readonly PartyLifecycleManager _lifecycle;

    private PartyMergeService(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public int MergeNonHeroTroopsIntoGarrison(MobileParty? party, Settlement? targetSettlement, string context)
    {
        try
        {
            var targetTown = targetSettlement?.Town;
            var targetGarrison = targetTown?.GarrisonParty;
            if (targetSettlement != null && targetTown != null && targetGarrison == null)
            {
                try
                {
                    targetSettlement.AddGarrisonParty();
                    targetGarrison = targetTown.GarrisonParty;
                    Logger.Info($"{context}: rebuilt missing GarrisonParty for '{targetSettlement.Name}' before merge");
                }
                catch (Exception addEx)
                {
                    Logger.Error($"{context}: AddGarrisonParty failed for '{targetSettlement.Name}'", addEx);
                }
            }

            var targetRoster = targetGarrison?.MemberRoster;
            var sourceRoster = party?.MemberRoster;
            if (targetRoster == null || sourceRoster == null)
            {
                Logger.Warn($"{context}: cannot merge party '{party?.Name}' into '{targetSettlement?.Name}' (missing roster/garrison)");
                return 0;
            }

            int transferred = 0;
            var snapshot = new List<TroopRosterElement>(sourceRoster.GetTroopRoster());
            foreach (var element in snapshot)
            {
                if (element.Character == null || element.Character.IsHero) continue;
                if (element.Number <= 0) continue;

                // 每个 element 都是 "Add → Remove" 配对原子：先加入 garrison 再从源 roster 删除。
                // 单 element 失败仅丢这一项；不会留下复制兵（旧版统一最后 RemoveIf 的写法在
                // 中途 AddToCounts 抛异常时会让已加进 garrison 的兵留在 source roster 上）。
                try
                {
                    targetRoster.AddToCounts(
                        element.Character,
                        element.Number,
                        false,
                        element.WoundedNumber,
                        element.Xp);
                }
                catch (Exception addEx)
                {
                    Logger.Warn($"{context}: AddToCounts failed for '{element.Character.StringId}' x{element.Number}; element skipped: {addEx.Message}");
                    continue;
                }

                try
                {
                    sourceRoster.RemoveTroop(element.Character, element.Number, default, 0);
                    transferred += element.Number;
                }
                catch (Exception removeEx)
                {
                    // 已加进 garrison 但 source 删除失败 → 立刻回滚 garrison 那一笔，避免复制兵。
                    Logger.Error($"{context}: RemoveTroop failed for '{element.Character.StringId}' x{element.Number}; rolling back garrison add", removeEx);
                    try { targetRoster.RemoveTroop(element.Character, element.Number, default, 0); }
                    catch (Exception rollbackEx)
                    {
                        Logger.Error($"{context}: rollback also failed — duplicate troops in garrison may persist", rollbackEx);
                    }
                }
            }

            return transferred;
        }
        catch (Exception ex)
        {
            Logger.Error($"{context}: MergeNonHeroTroopsIntoGarrison failed", ex);
            return 0;
        }
    }

    public void DisbandAndUntrack(MobileParty? party, string context)
    {
        if (party == null) return;
        try
        {
            DisbandPartyAction.StartDisband(party);
        }
        catch (Exception ex)
        {
            Logger.Error($"{context}: StartDisband failed for '{party.Name}'; will still untrack to avoid index leak", ex);
        }
        // UntrackParty 必须无条件执行：即使 vanilla 的 StartDisband 抛（例如 party 已死亡 / destroyed），
        // 我们也要把它从 _tracked 字典移除，否则 CountActive / GetCapFor 等会持续读到幽灵记录。
        try { _lifecycle.UntrackParty(party); }
        catch (Exception untrackEx)
        {
            Logger.Error($"{context}: UntrackParty also failed for '{party.Name}'", untrackEx);
        }
    }

    public bool DestroyAndUntrack(MobileParty? party, string context, bool deferIfInMapEvent = true)
    {
        if (party == null) return false;

        try
        {
            if (deferIfInMapEvent && party.MapEvent != null)
            {
                Logger.Warn($"{context}: '{party.Name}' is in MapEvent, deferring destroy");
                return false;
            }
        }
        catch
        {
            // If MapEvent access itself fails, continue to the vanilla action fallback chain.
        }

        try
        {
            DestroyPartyAction.Apply(null, party);
            _lifecycle.UntrackParty(party);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{context}: DestroyPartyAction failed for '{party.Name}', falling back to disband", ex);
            try
            {
                DisbandPartyAction.StartDisband(party);
                _lifecycle.UntrackParty(party);
                return true;
            }
            catch (Exception fallbackEx)
            {
                Logger.Error($"{context}: fallback disband failed for '{party.Name}'", fallbackEx);
                return false;
            }
        }
    }
}
