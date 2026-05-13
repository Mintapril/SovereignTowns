using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Lifecycle;

/// <summary>
/// 监管本 Mod 创建的所有 MobileParty 的生命周期：
///   - 按 (Home, Kind) 维度强制队伍上限
///   - 检测空闲并强制返回 Home / 解散
///   - 监听 MobilePartyDestroyed 自动取消跟踪
///
/// 重要约束：
///   - 仅跟踪通过 <see cref="RegisterTrackedParty"/> 显式注册的队伍
///   - HourlyTickPartyEvent 回调首行用 _tracked.ContainsKey(party) 过滤，
///     绝不触碰非本 Mod 创建的 vanilla / 第三方 mod 队伍
///   - 状态保存：_tracked 是运行时缓存，存档恢复时由各创建器重新注册即可
/// </summary>
public sealed class PartyLifecycleManager
{
    // ────────── 上限（按城镇 × kind） ──────────
    public const int MaxRecruitersPerTown = 1;
    public const int MaxTransfersPerTown  = 1;
    public const int MaxSallyForthPerTown = 1;
    public const int MaxDismissPerTown    = 1;

    // ────────── 空闲检测阈值（小时） ──────────
    public const int IdleHoursBeforeForceReturn = 24;
    public const int IdleHoursBeforeDisband     = 36;

    // ────────── kind 常量（推荐使用者使用） ──────────
    public const string KindRecruiter  = "recruiter";
    public const string KindTransfer   = "transfer";
    public const string KindPatrol     = "patrol";
    public const string KindSallyForth = "sallyforth";
    public const string KindDismiss    = "dismiss";

    private readonly Dictionary<MobileParty, TrackedPartyMeta> _tracked = new Dictionary<MobileParty, TrackedPartyMeta>();
    private bool _initialized;

    /// <summary>OnSessionLaunched 时调用：订阅 HourlyTickPartyEvent + MobilePartyDestroyed。</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            Logger.Debug("PartyLifecycleManager.Initialize: already initialized, skipping");
            return;
        }
        try
        {
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            _initialized = true;
            Logger.Info("PartyLifecycleManager initialized (HourlyTickPartyEvent + MobilePartyDestroyed subscribed)");
        }
        catch (Exception ex)
        {
            Logger.Error("PartyLifecycleManager.Initialize failed", ex);
        }
    }

    /// <summary>注册新创建的 MobileParty 进入本管理器跟踪。</summary>
    /// <param name="party">本 Mod 刚创建的队伍（非 null）</param>
    /// <param name="home">该队伍所属的 home settlement（非 null）</param>
    /// <param name="kind">"recruiter" / "transfer" / "patrol" 等（用于上限计数）</param>
    public void RegisterTrackedParty(MobileParty party, Settlement home, string kind)
    {
        try
        {
            if (party is null)
            {
                Logger.Warn("RegisterTrackedParty: party is null, ignored");
                return;
            }
            if (home is null)
            {
                Logger.Warn($"RegisterTrackedParty: home is null for party '{SafeName(party)}', ignored");
                return;
            }
            if (string.IsNullOrEmpty(kind))
            {
                Logger.Warn($"RegisterTrackedParty: kind is null/empty for party '{SafeName(party)}', ignored");
                return;
            }

            var meta = new TrackedPartyMeta(home, kind, CampaignTime.Now, party.TargetSettlement, SafeMemberCount(party));
            _tracked[party] = meta;
            Logger.Info($"RegisterTrackedParty: '{SafeName(party)}' kind={kind} home='{home.Name}' (tracked total={_tracked.Count})");
        }
        catch (Exception ex)
        {
            Logger.Error($"RegisterTrackedParty failed for party '{SafeName(party)}'", ex);
        }
    }

    /// <summary>查询：某城镇当前是否还能再创建一支某 kind 的队伍（未达上限）。</summary>
    public bool CanCreateAnotherParty(Settlement home, string kind)
    {
        try
        {
            if (home is null || string.IsNullOrEmpty(kind)) return false;
            var active = CountActive(home, kind);
            var max = GetMaxFor(kind);
            return active < max;
        }
        catch (Exception ex)
        {
            Logger.Error($"CanCreateAnotherParty failed (home='{home?.Name}', kind={kind})", ex);
            return false;
        }
    }

    /// <summary>查询：某城镇当前指定 kind 的 active 队伍数。</summary>
    public int CountActive(Settlement home, string kind)
    {
        try
        {
            if (home is null || string.IsNullOrEmpty(kind)) return 0;
            return _tracked.Count(kv =>
                kv.Value.Home == home &&
                kv.Value.Kind == kind &&
                kv.Key != null &&
                kv.Key.IsActive);
        }
        catch (Exception ex)
        {
            Logger.Error($"CountActive failed (home='{home?.Name}', kind={kind})", ex);
            return 0;
        }
    }

    /// <summary>
    /// 读档恢复后由 <c>OnGameLoadedEvent</c> 调用：清空 <see cref="_tracked"/> 并基于 vanilla
    /// 已恢复的 <see cref="MobileParty"/> 列表重建索引。
    /// 必须的过滤（与各 Manager 创建端保持一致）：
    ///   - RecruitingPartyComponent / TransferPartyComponent：本 Mod 自有类型，直接收编；
    ///   - vanilla PatrolPartyComponent：仅在 HomeSettlement.OwnerClan == Clan.PlayerClan
    ///     时纳入（避免误抓 AI 领主的巡逻队）。
    /// 单 party 失败 try-catch，不影响整体；幂等：可多次调用。
    /// </summary>
    public void RebuildFromCampaign()
    {
        try
        {
            _tracked.Clear();

            int recruiters = 0, transfers = 0, patrols = 0, sallyforths = 0, dismisses = 0, skipped = 0;
            var now = CampaignTime.Now;

            // 1) RecruitingPartyComponent / TransferPartyComponent（均继承自 CustomPartyComponent）
            try
            {
                var customs = MobileParty.AllCustomParties;
                if (customs != null)
                {
                    foreach (var party in customs)
                    {
                        try
                        {
                            if (party == null) continue;
                            var comp = party.PartyComponent;
                            if (comp is RecruitingPartyComponent rp)
                            {
                                var home = rp.HomeSettlement;
                                if (home == null) { skipped++; continue; }
                                _tracked[party] = new TrackedPartyMeta(home, KindRecruiter, now, party.TargetSettlement, SafeMemberCount(party));
                                recruiters++;
                            }
                            else if (comp is TransferPartyComponent tp)
                            {
                                var home = tp.Source;
                                if (home == null) { skipped++; continue; }
                                _tracked[party] = new TrackedPartyMeta(home, KindTransfer, now, party.TargetSettlement, SafeMemberCount(party));
                                transfers++;
                            }
                            else if (comp is SallyForthPartyComponent sp)
                            {
                                var home = sp.HomeSettlement;
                                if (home == null) { skipped++; continue; }
                                _tracked[party] = new TrackedPartyMeta(home, KindSallyForth, now, party.TargetSettlement, SafeMemberCount(party));
                                sallyforths++;
                            }
                            else if (comp is DismissPartyComponent dp)
                            {
                                // Home = source town (DismissedFromSettlement), so MigrateByHomeSettlement
                                // can sweep in-flight dismiss parties when source town falls.
                                var home = dp.DismissedFromSettlement;
                                if (home == null) { skipped++; continue; }
                                _tracked[party] = new TrackedPartyMeta(home, KindDismiss, now, party.TargetSettlement, SafeMemberCount(party));
                                dismisses++;
                            }
                            // 其他 CustomPartyComponent（vanilla quest 等）忽略
                        }
                        catch (Exception oneEx)
                        {
                            Logger.Error($"RebuildFromCampaign: failed to register custom party '{SafeName(party)}'", oneEx);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RebuildFromCampaign: AllCustomParties enumeration failed", ex);
            }

            // 2) vanilla PatrolPartyComponent — 仅玩家自家 town 的
            try
            {
                var patrolList = MobileParty.AllPatrolParties;
                if (patrolList != null)
                {
                    foreach (var party in patrolList)
                    {
                        try
                        {
                            if (party == null) continue;
                            var pp = party.PartyComponent as PatrolPartyComponent;
                            if (pp == null) continue;
                            var home = pp.HomeSettlement;
                            if (home == null) continue;
                            if (home.OwnerClan != Clan.PlayerClan) continue; // 关键过滤
                            _tracked[party] = new TrackedPartyMeta(home, KindPatrol, now, party.TargetSettlement, SafeMemberCount(party));
                            patrols++;
                        }
                        catch (Exception oneEx)
                        {
                            Logger.Error($"RebuildFromCampaign: failed to register patrol party '{SafeName(party)}'", oneEx);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RebuildFromCampaign: AllPatrolParties enumeration failed", ex);
            }

            Logger.Info($"PartyLifecycleManager.RebuildFromCampaign: recruiters={recruiters} transfers={transfers} patrols={patrols} sallyforths={sallyforths} dismisses={dismisses} skipped={skipped} (total tracked={_tracked.Count})");
        }
        catch (Exception ex)
        {
            Logger.Error("RebuildFromCampaign failed", ex);
        }
    }

    /// <summary>
    /// 首府切换（失守 / 手动切换）时，一次性清理所有 in-flight party：
    ///   - 若给定新首府且其有 GarrisonParty：把每个 party 的非英雄兵员转入新 garrison
    ///   - 否则：兵员蒸发（仅记日志）
    ///   - 随后 DisbandPartyAction.StartDisband 解散
    /// 单 party 失败 try-catch 不影响整体；结束清空 _tracked。
    /// </summary>
    public void MigrateAllOrDisband(Settlement? newCapital)
    {
        try
        {
            var newGarrison = newCapital?.Town?.GarrisonParty;
            var newCapitalName = newCapital?.Name?.ToString() ?? "<none>";
            int migratedTroops = 0;
            int partiesDisbanded = 0;

            // 拷贝 keys 快照，避免边迭代边修改
            var snapshot = new List<MobileParty>(_tracked.Keys);
            foreach (var party in snapshot)
            {
                try
                {
                    if (party == null) continue;

                    // B1 #6.B: dismiss parties evaporate — do not migrate roster to new garrison
                    if (_tracked.TryGetValue(party, out var meta) && meta.Kind == KindDismiss)
                    {
                        if (party.IsActive)
                        {
                            try { DestroyPartyAction.Apply(null, party); }
                            catch (Exception destroyEx)
                            {
                                Logger.Error($"MigrateAllOrDisband: dismiss-party DestroyPartyAction failed for '{SafeName(party)}'", destroyEx);
                            }
                            partiesDisbanded++;
                        }
                        _tracked.Remove(party);
                        continue;
                    }

                    if (party.IsActive && newGarrison?.MemberRoster != null && party.MemberRoster != null)
                    {
                        var elements = party.MemberRoster.GetTroopRoster();
                        foreach (var elem in elements)
                        {
                            if (elem.Character == null || elem.Character.IsHero) continue;
                            if (elem.Number <= 0) continue;
                            newGarrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                            migratedTroops += elem.Number;
                        }
                    }
                    if (party.IsActive)
                    {
                        DisbandPartyAction.StartDisband(party);
                        partiesDisbanded++;
                    }
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"MigrateAllOrDisband: failed for party '{SafeName(party)}'", oneEx);
                }
            }
            _tracked.Clear();
            Logger.Info($"MigrateAllOrDisband: migrated_troops={migratedTroops} parties_disbanded={partiesDisbanded} newCapital='{newCapitalName}'");
        }
        catch (Exception ex)
        {
            Logger.Error("MigrateAllOrDisband outer failure", ex);
        }
    }

    /// <summary>
    /// 2026-05-12 审查 D-WARN-11 修复：非首府失守时，仅清理该 settlement 关联的 in-flight party，
    /// 不动其它城归属的 party。把存活兵搬到 <paramref name="fallback"/>（一般为当前首府）。
    /// </summary>
    public void MigrateByHomeSettlement(Settlement lostSettlement, Settlement? fallback)
    {
        if (lostSettlement == null) return;
        try
        {
            var fallbackGarrison = fallback?.Town?.GarrisonParty;
            var fallbackName = fallback?.Name?.ToString() ?? "<none>";
            int migrated = 0;
            int disbanded = 0;

            var snapshot = new List<MobileParty>(_tracked.Keys);
            foreach (var party in snapshot)
            {
                try
                {
                    if (party == null) continue;
                    if (!_tracked.TryGetValue(party, out var rec)) continue;
                    if (rec.Home != lostSettlement) continue; // 仅清理该 settlement 的 in-flight

                    // B1 #6.B: dismiss parties evaporate, do not migrate roster
                    if (rec.Kind == KindDismiss)
                    {
                        if (party.IsActive)
                        {
                            try { DestroyPartyAction.Apply(null, party); }
                            catch (Exception destroyEx)
                            {
                                Logger.Error($"MigrateByHomeSettlement: dismiss-party DestroyPartyAction failed for '{SafeName(party)}'", destroyEx);
                            }
                            disbanded++;
                        }
                        _tracked.Remove(party);
                        continue;
                    }

                    if (party.IsActive && fallbackGarrison?.MemberRoster != null && party.MemberRoster != null)
                    {
                        var elements = party.MemberRoster.GetTroopRoster();
                        foreach (var elem in elements)
                        {
                            if (elem.Character == null || elem.Character.IsHero) continue;
                            if (elem.Number <= 0) continue;
                            fallbackGarrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                            migrated += elem.Number;
                        }
                    }
                    if (party.IsActive)
                    {
                        DisbandPartyAction.StartDisband(party);
                        disbanded++;
                    }
                    _tracked.Remove(party);
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"MigrateByHomeSettlement: failed for party '{SafeName(party)}'", oneEx);
                }
            }
            Logger.Info($"MigrateByHomeSettlement: lost='{lostSettlement.Name}' migrated={migrated} disbanded={disbanded} fallback='{fallbackName}'");
        }
        catch (Exception ex)
        {
            Logger.Error("MigrateByHomeSettlement outer failure", ex);
        }
    }

    /// <summary>解除跟踪（队伍销毁时由 MobilePartyDestroyed 自动触发，也可手动调用）。</summary>
    public void UntrackParty(MobileParty party)
    {
        if (party is null) return;
        try
        {
            if (_tracked.Remove(party))
            {
                Logger.Info($"UntrackParty: '{SafeName(party)}' removed (remaining={_tracked.Count})");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"UntrackParty failed for '{SafeName(party)}'", ex);
        }
    }

    // ────────── 事件回调 ──────────

    private void OnHourlyTickParty(MobileParty party)
    {
        // 首行过滤：非本 Mod 队伍立即返回
        if (party is null) return;
        if (!_tracked.TryGetValue(party, out var meta)) return;

        // B1 #6.B: dismiss party reached its target village → evaporate
        if (meta.Kind == KindDismiss && party.PartyComponent is DismissPartyComponent dp)
        {
            var homeVillage = dp.HomeVillage;
            if (homeVillage != null && (party.CurrentSettlement == homeVillage || party.LastVisitedSettlement == homeVillage))
            {
                Logger.Info($"HourlyTick '{SafeName(party)}': dismiss arrived at '{homeVillage.Name}' → DestroyPartyAction.Apply");
                try { DestroyPartyAction.Apply(null, party); }
                catch (Exception destroyEx) { Logger.Error($"DestroyPartyAction failed for dismiss '{SafeName(party)}'", destroyEx); }
                UntrackParty(party);
                return;
            }
        }

        try
        {
            // 1) 进展检测：TargetSettlement 改变 或 兵员数量改变 → 视为有进展，刷新 LastActiveAt
            var currentTarget = party.TargetSettlement;
            var currentMembers = SafeMemberCount(party);
            var hasProgress = (currentTarget != meta.LastTargetSettlement) ||
                              (currentMembers != meta.LastMemberCount);

            if (hasProgress)
            {
                meta.LastTargetSettlement = currentTarget;
                meta.LastMemberCount = currentMembers;
                meta.LastActiveAt = CampaignTime.Now;
                _tracked[party] = meta;
                var targetName = currentTarget?.Name?.ToString() ?? "<none>";
                Logger.Debug($"HourlyTick '{SafeName(party)}': progress detected (target={targetName} members={currentMembers}) — LastActiveAt refreshed");
                return;
            }

            // 2) 空闲检测：在地图事件 / 围攻中视为忙碌，跳过
            if (party.MapEvent != null) return;
            if (party.BesiegedSettlement != null) return;

            var idleHours = ComputeIdleHours(meta.LastActiveAt);
            if (idleHours < IdleHoursBeforeForceReturn) return;

            // 3) 超过 disband 阈值 → 解散
            if (idleHours >= IdleHoursBeforeDisband)
            {
                Logger.Warn($"HourlyTick '{SafeName(party)}': idle {idleHours:F1}h >= {IdleHoursBeforeDisband}h — issuing DisbandPartyAction.StartDisband");
                try
                {
                    DisbandPartyAction.StartDisband(party);
                }
                catch (Exception ex)
                {
                    Logger.Error($"StartDisband failed for '{SafeName(party)}'", ex);
                }
                UntrackParty(party);
                return;
            }

            // 4) 超过 force-return 阈值 → 重定向至 home
            if (idleHours >= IdleHoursBeforeForceReturn)
            {
                if (meta.Home != null && party.TargetSettlement != meta.Home)
                {
                    Logger.Warn($"HourlyTick '{SafeName(party)}': idle {idleHours:F1}h >= {IdleHoursBeforeForceReturn}h — forcing return to home '{meta.Home.Name}'");
                    try
                    {
                        party.SetTargetSettlement(meta.Home, isTargetingPort: false);
                        // 强制重定向也视为一次"刷新"，避免每小时都重发
                        meta.LastActiveAt = CampaignTime.Now;
                        meta.LastTargetSettlement = meta.Home;
                        _tracked[party] = meta;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"SetTargetSettlement failed for '{SafeName(party)}'", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"OnHourlyTickParty failed for '{SafeName(party)}'", ex);
        }
    }

    private void OnMobilePartyDestroyed(MobileParty party, PartyBase? destroyerParty)
    {
        if (party is null) return;
        if (!_tracked.ContainsKey(party)) return;
        try
        {
            Logger.Info($"OnMobilePartyDestroyed: '{SafeName(party)}' — auto-untracking");
            UntrackParty(party);
        }
        catch (Exception ex)
        {
            Logger.Error($"OnMobilePartyDestroyed failed for '{SafeName(party)}'", ex);
        }
    }

    // ────────── 辅助 ──────────

    private static int GetMaxFor(string kind)
    {
        if (kind == KindRecruiter)  return MaxRecruitersPerTown;
        if (kind == KindTransfer)   return MaxTransfersPerTown;
        if (kind == KindSallyForth) return MaxSallyForthPerTown;
        if (kind == KindDismiss)    return MaxDismissPerTown;
        // 未知 kind：保守上限 1，避免失控创建
        return 1;
    }

    private static double ComputeIdleHours(CampaignTime lastActive)
    {
        // 主路径：CampaignTime 减法返回 CampaignTime, 然后 .ToHours（实际为 double）
        try
        {
            return (CampaignTime.Now - lastActive).ToHours;
        }
        catch
        {
            // 退化：直接调用 lastActive 实例的 ElapsedHoursUntilNow 等价方法
            try
            {
                return lastActive.ElapsedHoursUntilNow;
            }
            catch
            {
                return 0d;
            }
        }
    }

    private static int SafeMemberCount(MobileParty party)
    {
        try
        {
            return party?.MemberRoster?.TotalManCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeName(MobileParty? party)
    {
        if (party is null) return "<null>";
        try
        {
            return party.Name?.ToString() ?? party.StringId ?? "<unnamed>";
        }
        catch
        {
            return "<error>";
        }
    }

    /// <summary>跟踪 meta；可变 struct 通过 _tracked[key]=meta 回写保证一致性。</summary>
    private struct TrackedPartyMeta
    {
        public Settlement Home;
        public string Kind;
        public CampaignTime LastActiveAt;
        public Settlement? LastTargetSettlement;
        public int LastMemberCount;

        public TrackedPartyMeta(Settlement home, string kind, CampaignTime lastActiveAt, Settlement? lastTarget, int lastMembers)
        {
            Home = home;
            Kind = kind;
            LastActiveAt = lastActiveAt;
            LastTargetSettlement = lastTarget;
            LastMemberCount = lastMembers;
        }
    }
}
