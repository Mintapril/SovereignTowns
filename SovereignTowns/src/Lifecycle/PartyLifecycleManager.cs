using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Common;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
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
    /// <summary>B7.27 起：KindRecruiter 不再使用此常量，改用 ComputePatrolCapForHome。常量保留作为 fallback / 历史参考。</summary>
    public const int MaxRecruitersPerTown = 1;
    public const int MaxTransfersPerTown  = 1;
    public const int MaxSallyForthPerTown = 1;

    // ────────── 空闲检测阈值（小时） ──────────
    public const int IdleHoursBeforeForceReturn = 24;
    public const int IdleHoursBeforeDisband     = 36;

    // ────────── kind 常量（推荐使用者使用） ──────────
    public const string KindRecruiter  = "recruiter";
    public const string KindTransfer   = "transfer";
    public const string KindPatrol     = "patrol";
    public const string KindSallyForth = "sallyforth";

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
                Logger.Warn($"RegisterTrackedParty: home is null for party '{PartyNameFormatter.SafeName(party)}', ignored");
                return;
            }
            if (string.IsNullOrEmpty(kind))
            {
                Logger.Warn($"RegisterTrackedParty: kind is null/empty for party '{PartyNameFormatter.SafeName(party)}', ignored");
                return;
            }

            int initialMembers = PartyNameFormatter.SafeMemberCount(party);
            var meta = new TrackedPartyMeta(
                home,
                kind,
                CampaignTime.Now,
                party.TargetSettlement,
                initialMembers,
                SafeActualClan(party, home));
            _tracked[party] = meta;
            Logger.Info($"RegisterTrackedParty: '{PartyNameFormatter.SafeName(party)}' kind={kind} home='{home.Name}' (tracked total={_tracked.Count})");
        }
        catch (Exception ex)
        {
            Logger.Error($"RegisterTrackedParty failed for party '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    /// <summary>查询：某城镇当前是否还能再创建一支某 kind 的队伍（未达上限）。
    /// B7.16：Patrol 上限改为按 town 的 Garrison Barracks 建筑等级缩放（无建筑=1，每升一级 +1）。
    /// 其他 kind 仍走静态常量。</summary>
    public bool CanCreateAnotherParty(Settlement home, string kind)
    {
        try
        {
            if (home is null || string.IsNullOrEmpty(kind)) return false;
            var active = CountActive(home, kind);
            var max = GetMaxFor(home, kind);
            return active < max;
        }
        catch (Exception ex)
        {
            Logger.Error($"CanCreateAnotherParty failed (home='{home?.Name}', kind={kind})", ex);
            return false;
        }
    }

    /// <summary>外露：让 PatrolDispatcher 之类查询某城镇当前 kind 的硬上限。</summary>
    public int GetCapFor(Settlement home, string kind) => GetMaxFor(home, kind);

    /// <summary>查询：某城镇当前指定 kind 的 active 队伍数。</summary>
    public int CountActive(Settlement home, string kind)
    {
        try
        {
            if (home is null || string.IsNullOrEmpty(kind)) return 0;
            var ownerClan = home.OwnerClan;
            return _tracked.Count(kv =>
                kv.Value.Home == home &&
                kv.Value.Kind == kind &&
                kv.Value.OwnerClan == ownerClan &&
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
    /// B16.4：4 种 StPartyComponent 子类（recruiter / transfer / sally / patrol）均继承自
    /// CustomPartyComponent，统一在 <see cref="MobileParty.AllCustomParties"/> 单次扫描收编。
    /// vanilla auto-spawn 的 <c>PatrolPartyComponent</c> 不再纳入跟踪（B16.4 ST 巡逻独立类型）。
    /// 单 party 失败 try-catch，不影响整体；幂等：可多次调用。
    /// </summary>
    public void RebuildFromCampaign()
    {
        try
        {
            _tracked.Clear();

            int recruiters = 0, transfers = 0, patrols = 0, sallyforths = 0, skipped = 0;
            // R7 (DeepSeek audit 2026-05-18)：在某些模组冲突场景下 OnGameLoadedEvent 可能在
            // Campaign / CampaignTime 完全就绪前触发。Campaign.Current 为 null 时直接退出，
            // 避免给所有重建 party 一个零基线，进而被错判 idle 立即遣返 / 解散。
            if (TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                Logger.Error("RebuildFromCampaign: Campaign.Current is null — skipping rebuild to avoid corrupting LastActiveAt baselines");
                return;
            }
            var now = CampaignTime.Now;

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
                            if (party.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
                            {
                                // B16.4a P1-7：损坏存档防御 — _homeSettlement 可能为 null。
                                // 使用 HomeSettlementOrNull 避免触发 HomeSettlement 的诊断异常。
                                var home = stc.HomeSettlementOrNull;
                                if (home == null) { skipped++; continue; }
                                int mc = PartyNameFormatter.SafeMemberCount(party);
                                string kind = stc switch
                                {
                                    SovereignTowns.Parties.StRecruiterPartyComponent => KindRecruiter,
                                    SovereignTowns.Parties.StTransferPartyComponent  => KindTransfer,
                                    SovereignTowns.Parties.StSallyPartyComponent     => KindSallyForth,
                                    SovereignTowns.Parties.StPatrolPartyComponent    => KindPatrol,
                                    _ => null!,
                                };
                                if (kind == null!) continue;
                                _tracked[party] = new TrackedPartyMeta(home, kind, now, party.TargetSettlement, mc, SafeActualClan(party, home));
                                switch (kind)
                                {
                                    case KindRecruiter: recruiters++; break;
                                    case KindTransfer: transfers++; break;
                                    case KindSallyForth: sallyforths++; break;
                                    case KindPatrol: patrols++; break;
                                }
                            }
                            // 其他 CustomPartyComponent（vanilla quest 等）忽略
                        }
                        catch (Exception oneEx)
                        {
                            Logger.Error($"RebuildFromCampaign: failed to register custom party '{PartyNameFormatter.SafeName(party)}'", oneEx);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RebuildFromCampaign: AllCustomParties enumeration failed", ex);
            }

            Logger.Info($"PartyLifecycleManager.RebuildFromCampaign: recruiters={recruiters} transfers={transfers} patrols={patrols} sallyforths={sallyforths} skipped={skipped} (total tracked={_tracked.Count})");
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
    /// <summary>
    /// 把 *指定氏族* 名下的全部 in-flight party 迁移到该氏族的新首府或解散。
    /// B7.15 多 clan 化：必须传入 <paramref name="ownerClan"/>；只处理创建时属于该氏族的
    /// party，避免跨氏族污染（旧版本无过滤，AI 失守首府会把玩家 / 其他 AI 的 party 全部清空）。
    /// 不能用当前 home.OwnerClan 判定，因为易主事件触发时 home 通常已经归新主人。
    /// 失败 try-catch 不影响整体；只 Remove 处理过的 entry，不再 _tracked.Clear()。
    /// </summary>
    public void MigrateAllOrDisband(Clan ownerClan, Settlement? newCapital)
    {
        if (ownerClan is null)
        {
            Logger.Warn("MigrateAllOrDisband: ownerClan is null, refusing to migrate (would risk cross-clan contamination)");
            return;
        }
        try
        {
            var newCapitalName = newCapital?.Name?.ToString() ?? "<none>";
            int migratedTroops = 0;
            int partiesDisbanded = 0;
            int skippedOtherClan = 0;
            var mergeService = PartyMergeService.Instance;

            // 拷贝 keys 快照，避免边迭代边修改
            var snapshot = new List<MobileParty>(_tracked.Keys);
            foreach (var party in snapshot)
            {
                try
                {
                    if (party == null) continue;
                    if (!_tracked.TryGetValue(party, out var meta)) continue;

                    // B7.15 关键过滤：只动创建时属于本氏族的 party。
                    // 不能看 meta.Home.OwnerClan：SettlementOwnerChanged 触发时 home 可能已经易主。
                    if (meta.OwnerClan != ownerClan)
                    {
                        skippedOtherClan++;
                        continue;
                    }

                    if (party.IsActive && newCapital != null)
                    {
                        migratedTroops += mergeService.MergeNonHeroTroopsIntoGarrison(
                            party,
                            newCapital,
                            "PartyLifecycleManager.MigrateAllOrDisband");
                    }
                    if (party.IsActive)
                    {
                        mergeService.DisbandAndUntrack(party, "PartyLifecycleManager.MigrateAllOrDisband");
                        partiesDisbanded++;
                    }
                    else
                    {
                        UntrackParty(party);
                    }
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"MigrateAllOrDisband: failed for party '{PartyNameFormatter.SafeName(party)}'", oneEx);
                }
            }
            Logger.Info($"MigrateAllOrDisband(clan={ownerClan.StringId}): migrated_troops={migratedTroops} parties_disbanded={partiesDisbanded} skipped_other_clan={skippedOtherClan} newCapital='{newCapitalName}'");
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
            var fallbackName = fallback?.Name?.ToString() ?? "<none>";
            int migrated = 0;
            int disbanded = 0;
            var mergeService = PartyMergeService.Instance;

            var snapshot = new List<MobileParty>(_tracked.Keys);
            foreach (var party in snapshot)
            {
                try
                {
                    if (party == null) continue;
                    if (!_tracked.TryGetValue(party, out var rec)) continue;
                    if (rec.Home != lostSettlement) continue; // 仅清理该 settlement 的 in-flight

                    if (party.IsActive && fallback != null)
                    {
                        migrated += mergeService.MergeNonHeroTroopsIntoGarrison(
                            party,
                            fallback,
                            "PartyLifecycleManager.MigrateByHomeSettlement");
                    }
                    if (party.IsActive)
                    {
                        mergeService.DisbandAndUntrack(party, "PartyLifecycleManager.MigrateByHomeSettlement");
                        disbanded++;
                    }
                    else
                    {
                        UntrackParty(party);
                    }
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"MigrateByHomeSettlement: failed for party '{PartyNameFormatter.SafeName(party)}'", oneEx);
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
                Logger.Info($"UntrackParty: '{PartyNameFormatter.SafeName(party)}' removed (remaining={_tracked.Count})");
            }

            // B7.26：通知所有 clan scheduler 该 party 已销毁，清掉瞬态字典里的条目（防 MBGUID 复用脏数据）。
            // B16.4a P1-8：RecruiterScheduler 同步加入通知，否则 _lastStopChangedAt / _lastSeenLocation 内存泄漏。
            try
            {
                var reg = SovereignTowns.Capital.CapitalRegistry.Instance;
                if (reg != null)
                {
                    foreach (var mgr in reg.AllManagers)
                    {
                        try { mgr?.PatrolScheduler?.NotifyPartyDestroyed(party); }
                        catch (Exception ex) { Logger.Warn("PatrolScheduler NotifyPartyDestroyed (per-mgr) failed", ex); }
                        try { mgr?.RecruiterScheduler?.NotifyPartyDestroyed(party); }
                        catch (Exception ex) { Logger.Warn("RecruiterScheduler NotifyPartyDestroyed (per-mgr) failed", ex); }
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("scheduler NotifyPartyDestroyed iteration failed", ex); }
        }
        catch (Exception ex)
        {
            Logger.Error($"UntrackParty failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    // ────────── 事件回调 ──────────

    private void OnHourlyTickParty(MobileParty party)
    {
        try
        {
            // CLAUDE.md 硬约束 #5：事件 handler 整段必须包在 try / catch 内，
            // 因此首行过滤也放进 try（B16.4a H2 修复 — 原版 line 391-392 在 try 外）。
            if (party is null) return;
            if (!_tracked.TryGetValue(party, out var meta)) return;

            // B16.1：StPartyComponent 状态机分派必须在所有 early-return（idle 检测）之前，
            // 否则正常 in-flight 队伍（无 progress + idle < 24h）走不到这里，
            // OnHourlyTickCore（含到达 dest → DeliverAndDisband 分支）永远不触发。
            if (party.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
            {
                try { stc.OnHourlyTick(party); }
                catch (Exception ex) { Logger.Error($"StPartyComponent.OnHourlyTick failed for '{PartyNameFormatter.SafeName(party)}'", ex); }
                // component 可能已 DisbandAndUntrack（如 DeliverAndDisband）：移除条目后不再跑 idle 兜底
                if (!_tracked.TryGetValue(party, out meta)) return;
            }

            // 1) 进展检测：TargetSettlement 改变 或 兵员数量改变 → 视为有进展，刷新 LastActiveAt
            var currentTarget = party.TargetSettlement;
            var currentMembers = PartyNameFormatter.SafeMemberCount(party);
            var hasProgress = (currentTarget != meta.LastTargetSettlement) ||
                              (currentMembers != meta.LastMemberCount);

            if (hasProgress)
            {
                meta.LastTargetSettlement = currentTarget;
                meta.LastMemberCount = currentMembers;
                meta.LastActiveAt = CampaignTime.Now;
                _tracked[party] = meta;
                var targetName = currentTarget?.Name?.ToString() ?? "<none>";
                Logger.Debug($"HourlyTick '{PartyNameFormatter.SafeName(party)}': progress detected (target={targetName} members={currentMembers}) — LastActiveAt refreshed");
                return;
            }

            // 2) 空闲检测：在地图事件 / 围攻中视为忙碌，跳过
            if (party.MapEvent != null) return;
            if (party.BesiegedSettlement != null) return;

            var idleHours = ComputeIdleHours(meta.LastActiveAt);
            // R1 (DeepSeek audit 2026-05-18)：从 const 切换为 PartyThresholds，玩家可配。
            // ConfigurationManager.Current 为 null 时回退到 const 默认值。
            float forceReturnHours = SovereignTowns.Configuration.ConfigurationManager.Current?.Thresholds?.IdleHoursBeforeForceReturn ?? IdleHoursBeforeForceReturn;
            float disbandHours = SovereignTowns.Configuration.ConfigurationManager.Current?.Thresholds?.IdleHoursBeforeDisband ?? IdleHoursBeforeDisband;
            if (idleHours < forceReturnHours) return;

            // 3) 超过 disband 阈值 → 解散
            if (idleHours >= disbandHours)
            {
                Logger.Warn($"HourlyTick '{PartyNameFormatter.SafeName(party)}': idle {idleHours:F1}h >= {disbandHours}h — issuing DisbandPartyAction.StartDisband");
                try
                {
                    DisbandPartyAction.StartDisband(party);
                }
                catch (Exception ex)
                {
                    Logger.Error($"StartDisband failed for '{PartyNameFormatter.SafeName(party)}'", ex);
                }
                UntrackParty(party);
                return;
            }

            // 4) 超过 force-return 阈值 → 重定向至 home
            if (idleHours >= forceReturnHours)
            {
                if (meta.Home != null && party.TargetSettlement != meta.Home)
                {
                    Logger.Warn($"HourlyTick '{PartyNameFormatter.SafeName(party)}': idle {idleHours:F1}h >= {forceReturnHours}h — forcing return to home '{meta.Home.Name}'");
                    try
                    {
                        if (party.PartyComponent is SovereignTowns.Parties.StRecruiterPartyComponent rp)
                        {
                            rp.SetAssignedTarget(meta.Home);
                        }

                        // 2026-05-18 修复 v2：用 GoToWithLeave 显式 LeaveSettlement 处理
                        // "party 在远端 settlement 里因 DoNotMakeNewDecisions(true) 离不开" 的情况。
                        SovereignTowns.Common.SafeMoveHelper.GoToWithLeave(party, meta.Home, "PartyLifecycle force-return idle");
                        // 强制重定向也视为一次"刷新"，避免每小时都重发
                        meta.LastActiveAt = CampaignTime.Now;
                        meta.LastTargetSettlement = meta.Home;
                        _tracked[party] = meta;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(party)}'", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"OnHourlyTickParty failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    /// <summary>
    /// B16.1：vanilla MapEventEnded 路由入口。由 SovereignTownsCampaignBehavior 转发，
    /// 单点分派给所有参战 StPartyComponent。
    /// </summary>
    public void OnMapEventEnded(TaleWorlds.CampaignSystem.MapEvents.MapEvent ev)
    {
        if (ev == null) return;
        try
        {
            HandleSideEndOfEvent(ev.AttackerSide, ev);
            HandleSideEndOfEvent(ev.DefenderSide, ev);
        }
        catch (Exception ex)
        {
            Logger.Error("PartyLifecycleManager.OnMapEventEnded failed", ex);
        }
    }

    private void HandleSideEndOfEvent(TaleWorlds.CampaignSystem.MapEvents.MapEventSide? side, TaleWorlds.CampaignSystem.MapEvents.MapEvent ev)
    {
        if (side?.Parties == null) return;
        try
        {
            // E22 (DeepSeek audit 2026-05-18)：vanilla side.Parties 在 MapEvent 收尾阶段可能被
            // vanilla 自身修改（DisbandPartyAction 链入俘虏迁移等）→ 直接 foreach 会抛
            // InvalidOperationException。先快照再迭代。
            var snapshot = new System.Collections.Generic.List<TaleWorlds.CampaignSystem.MapEvents.MapEventParty>();
            try
            {
                foreach (var uop in side.Parties) snapshot.Add(uop);
            }
            catch (Exception snapEx)
            {
                Logger.Error("HandleSideEndOfEvent snapshot failed", snapEx);
                return;
            }
            foreach (var uop in snapshot)
            {
                MobileParty? mp = null;
                try { mp = uop.Party?.MobileParty; }
                catch { continue; }
                if (mp == null || !mp.IsActive) continue;
                if (mp.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
                {
                    try { stc.OnMapEventEnded(ev, mp); }
                    catch (Exception ex) { Logger.Error($"StPartyComponent.OnMapEventEnded failed for '{PartyNameFormatter.SafeName(mp)}'", ex); }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HandleSideEndOfEvent iteration failed", ex);
        }
    }

    private void OnMobilePartyDestroyed(MobileParty party, PartyBase? destroyerParty)
    {
        if (party is null) return;
        if (!_tracked.ContainsKey(party)) return;
        try
        {
            Logger.Info($"OnMobilePartyDestroyed: '{PartyNameFormatter.SafeName(party)}' — auto-untracking");
            UntrackParty(party);
        }
        catch (Exception ex)
        {
            Logger.Error($"OnMobilePartyDestroyed failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    // ────────── 辅助 ──────────

    /// <summary>
    /// 按 (home, kind) 返回硬上限。
    /// B7.16：KindPatrol 按 vanilla "settlement_garrison" 建筑（兵营）等级缩放：
    ///   未建造 / 0 级 → 1 支
    ///   1 级 → 2 支
    ///   2 级 → 3 支
    ///   3 级 → 4 支
    /// 城堡（IsCastle）走 castle 对应建筑 stringId "castle_barracks"。
    /// 其他 kind 与 home 无关，沿用静态常量。
    /// </summary>
    private static int GetMaxFor(Settlement home, string kind)
    {
        // B7.27：征兵队上限改用与巡逻队相同的兵营建筑等级公式（首府 barracks lvl + 1，0级 1 支，3级 4 支）
        if (kind == KindRecruiter)  return ComputePatrolCapForHome(home);
        if (kind == KindTransfer)   return MaxTransfersPerTown;
        if (kind == KindSallyForth) return MaxSallyForthPerTown;
        if (kind == KindPatrol)     return ComputePatrolCapForHome(home);
        // 未知 kind：保守上限 1，避免失控创建
        return 1;
    }

    /// <summary>
    /// 兵营（Town: "settlement_garrison" / Castle: "castle_barracks"）等级 + 1。
    /// 找不到 building 或 town == null → 退回 1（最低保底）。失败也返回 1，绝不抛。
    /// </summary>
    private static int ComputePatrolCapForHome(Settlement? home)
    {
        try
        {
            var town = home?.Town;
            if (town?.Buildings == null) return 1;
            string targetId = (home != null && home.IsCastle) ? "castle_barracks" : "settlement_garrison";
            foreach (var b in town.Buildings)
            {
                if (b?.BuildingType == null) continue;
                string id;
                try { id = b.BuildingType.StringId ?? ""; }
                catch { continue; }
                if (string.Equals(id, targetId, StringComparison.Ordinal))
                {
                    int level;
                    try { level = b.CurrentLevel; } catch { level = 0; }
                    if (level < 0) level = 0;
                    if (level > 3) level = 3;
                    return level + 1;
                }
            }
            return 1; // 未建造该建筑（建造槽空着）
        }
        catch (Exception ex)
        {
            Logger.Error($"ComputePatrolCapForHome failed for '{home?.Name}'", ex);
            return 1;
        }
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

    private static Clan? SafeActualClan(MobileParty? party, Settlement? home)
    {
        try
        {
            return party?.ActualClan ?? home?.OwnerClan;
        }
        catch
        {
            try { return home?.OwnerClan; }
            catch { return null; }
        }
    }

    /// <summary>跟踪 meta；可变 struct 通过 _tracked[key]=meta 回写保证一致性。
    /// B16.4：出发兵员快照已迁移到 <see cref="SovereignTowns.Parties.StPartyComponent.InitialMemberCount"/>
    /// （component 实例自带 [SaveableField]，无需在 lifecycle 内再存一份）。</summary>
    private struct TrackedPartyMeta
    {
        public Settlement Home;
        public string Kind;
        public Clan? OwnerClan;
        public CampaignTime LastActiveAt;
        public Settlement? LastTargetSettlement;
        public int LastMemberCount;

        public TrackedPartyMeta(
            Settlement home,
            string kind,
            CampaignTime lastActiveAt,
            Settlement? lastTarget,
            int lastMembers,
            Clan? ownerClan)
        {
            Home = home;
            Kind = kind;
            OwnerClan = ownerClan ?? home.OwnerClan;
            LastActiveAt = lastActiveAt;
            LastTargetSettlement = lastTarget;
            LastMemberCount = lastMembers;
        }
    }
}
