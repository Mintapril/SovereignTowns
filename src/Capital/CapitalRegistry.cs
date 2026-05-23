using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Capital;

/// <summary>
/// B7.15：多 Clan 首府注册表。
///
/// 玩家氏族永远拥有一个 CapitalManager（保持向后兼容；这是 A+C 之前的唯一路径）。
/// 当 <see cref="EnabledFeatures.ApplyToAiSettlementsToo"/> 开启时，额外给每个有至少 1 个
/// Town/Castle 的 NPC clan 也建一个 CapitalManager。
///
/// 调用方约定：
///   - 所有下游 Manager 通过 <see cref="GetForClan"/> / <see cref="GetForSettlement"/>
///     间接查询，不再持有单一 CapitalManager 引用。
///   - 事件转发：CampaignBehavior 收到 OnSettlementOwnerChangedEvent 后调
///     <see cref="OnSettlementOwnerChanged"/>，本类内部分派到所有相关的 manager。
///   - Toggle 时（用户在网页面板切 ApplyToAiSettlementsToo）调
///     <see cref="SyncFromConfig"/> 增减 AI manager。
/// </summary>
public sealed class CapitalRegistry
{
    /// <summary>
    /// 全局单例引用，让 WebConfigEndpoints 在 PUT /api/config / POST /api/reload 后能调到
    /// <see cref="SyncFromConfig"/>，避免 toggle ApplyToAiSettlementsToo 后需要重启游戏。
    /// CampaignBehavior 析构后置 null。
    /// </summary>
    public static CapitalRegistry? Instance { get; private set; }

    private readonly PartyLifecycleManager _lifecycle;
    private readonly Dictionary<Clan, CapitalManager> _managers = new();

    /// <summary>
    /// 2026-05-14 Task X：由 SovereignTownsCampaignBehavior.SyncData 在加载阶段灌入的
    /// clanStringId → settlementStringId 映射；在本类 Initialize / EnsureForAllAiClans 内消费完即清空。
    /// 玩家 + AI 都在此 dict 内（取决于存档时刻 ApplyToAiSettlementsToo 是否开启）。
    /// </summary>
    private Dictionary<string, string>? _pendingCapitals;

    public CapitalRegistry(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// <summary>
    /// OnSessionLaunched 调用：
    ///   1) 玩家氏族 → 创建 manager + Initialize
    ///   2) 若 ApplyToAiSettlementsToo 开启 → 给每个有 Town/Castle 的 AI clan 同样处理
    /// </summary>
    public void Initialize()
    {
        try
        {
            // 玩家氏族永远有 —— 从 _pendingCapitals 取持久化 stringId（若存在）。
            // 极少数边缘场景下 Clan.PlayerClan 可能为 null（未启动 campaign 时）→ 直接跳过玩家分支，
            // 让 EnsureForAllAiClans 仍可在 AI toggle 开启时把 AI manager 建好。
            var playerClan = Clan.PlayerClan;
            if (playerClan != null)
            {
                string? playerPending = null;
                if (_pendingCapitals != null
                    && _pendingCapitals.TryGetValue(playerClan.StringId, out var psid))
                {
                    playerPending = psid;
                }
                EnsureForClan(playerClan, playerPending);
            }
            else
            {
                Logger.Warn("CapitalRegistry.Initialize: Clan.PlayerClan is null at session-launch time; player manager not created");
            }

            // AI 阵营按配置接管 —— EnsureForAllAiClans 内部按 clanStringId 查 _pendingCapitals 注入
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat?.ApplyToAiSettlementsToo == true)
            {
                EnsureForAllAiClans();
            }

            // 消费完毕 → 清空。后续 EnsureForClan 调用（OnSettlementOwnerChanged 内 AI 获新城）
            // 不再走持久化分支，按规则随机/获城自动选首府即可。
            _pendingCapitals = null;

            Instance = this;

            Logger.Info($"CapitalRegistry initialized: {_managers.Count} clan manager(s) " +
                        $"(player + {_managers.Count - 1} AI)");
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.Initialize failed", ex);
        }
    }

    /// <summary>
    /// 2026-05-14 Task X：由 SovereignTownsCampaignBehavior.SyncData 在加载阶段调用，
    /// 把磁盘上的 clan→capital 映射暂存。<see cref="Initialize"/> 会消费此 dict 并立即清空。
    /// 传 null / 空字典都安全：会走 EnsureForClan 的"无 pending"分支（随机抽取兜底）。
    /// </summary>
    public void RestoreCapitals(Dictionary<string, string>? dict)
    {
        _pendingCapitals = dict;
    }

    /// <summary>
    /// 2026-05-14 Task X：导出当前所有受管 clan 的 clanStringId → capitalSettlementStringId 映射，
    /// 供 SovereignTownsCampaignBehavior.SyncData 在 IsSaving 阶段序列化写盘。
    /// 没有首府的 clan（GetCapitalStringId 返回 null/empty）会被略过。
    /// </summary>
    public Dictionary<string, string> ExportCapitals()
    {
        var dict = new Dictionary<string, string>();
        try
        {
            foreach (var kv in _managers)
            {
                var clan = kv.Key;
                var mgr = kv.Value;
                if (clan == null || mgr == null) continue;
                var sid = mgr.GetCapitalStringId();
                if (!string.IsNullOrEmpty(sid))
                {
                    dict[clan.StringId] = sid!;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.ExportCapitals failed", ex);
        }
        return dict;
    }

    /// <summary>查询某 clan 的 CapitalManager；不存在返回 null（不主动创建，避免误打开）。</summary>
    public CapitalManager? GetForClan(Clan? clan)
    {
        if (clan is null) return null;
        return _managers.TryGetValue(clan, out var mgr) ? mgr : null;
    }

    /// <summary>查询某 settlement 所属 clan 的 CapitalManager。settlement 为 null 或 ownerClan 不在注册表 → null。</summary>
    public CapitalManager? GetForSettlement(Settlement? settlement)
    {
        try
        {
            var clan = settlement?.OwnerClan;
            if (clan is null) return null;
            return GetForClan(clan);
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.GetForSettlement failed", ex);
            return null;
        }
    }

    /// <summary>玩家氏族的 CapitalManager（保持向后兼容旧调用）。</summary>
    public CapitalManager? GetForPlayer() => GetForClan(Clan.PlayerClan);

    /// <summary>注册表里是否有此 clan（即该 clan 纳入 ST 首府 / 驻军 / 招募等受管范围）。</summary>
    public bool IsManagedClan(Clan? clan)
    {
        if (clan is null) return false;
        return _managers.ContainsKey(clan);
    }

    /// <summary>判断 settlement 是否属于某个 managed clan。</summary>
    public bool IsManagedSettlement(Settlement? settlement)
    {
        try { return IsManagedClan(settlement?.OwnerClan); }
        catch { return false; }
    }

    /// <summary>查询某 clan 的当前首府 settlement；未接管或无首府返回 null。</summary>
    public Settlement? GetCapitalForClan(Clan? clan)
    {
        try
        {
            var settlement = GetForClan(clan)?.GetCapitalSettlement();
            if (settlement == null || !settlement.IsTown) return null;
            if (clan != null && settlement.OwnerClan != clan) return null;
            return settlement;
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalRegistry.GetCapitalForClan failed (clan={clan?.StringId})", ex);
            return null;
        }
    }

    /// <summary>该 clan 是否已由 ST 接管且当前有可用首府。</summary>
    public bool IsManagedClanWithCapital(Clan? clan)
    {
        try { return clan != null && GetCapitalForClan(clan) != null; }
        catch { return false; }
    }

    /// <summary>该 settlement 是否属于一个有可用首府的 ST 受管 clan。</summary>
    public bool IsManagedSettlementWithCapital(Settlement? settlement)
    {
        try { return IsManagedClanWithCapital(settlement?.OwnerClan); }
        catch { return false; }
    }

    /// <summary>经济扣费策略：玩家氏族扣个人金币，AI 受管氏族免费。</summary>
    public static bool ShouldChargeClan(Clan? clan) => clan == Clan.PlayerClan;

    /// <summary>
    /// 玩家 + 所有 AI managed clan 的快照枚举。
    /// 返回快照（ToList）而非 live view 作为防御性写法 —
    /// 当前 SyncFromConfig 已通过 WebConfigGameThreadSync.Drain 切到主线程，无活跃竞争；
    /// 快照仍保留以防未来引入新的调用方导致 InvalidOperationException。
    /// </summary>
    public IReadOnlyList<CapitalManager> AllManagers
    {
        get
        {
            try { return _managers.Values.ToList(); }
            catch
            {
                // 极端情况：枚举刚开始就被改 → 返回空列表，下次 tick 重试
                return Array.Empty<CapitalManager>();
            }
        }
    }

    /// <summary>
    /// 用户 toggle ApplyToAiSettlementsToo 后调（网页 PUT /api/config / POST /api/reload）。
    /// - 开启：补齐所有有 Town/Castle 的 AI clan。
    /// - 关闭（R2 修复）：先主动迁移/解散每个 AI clan 的 in-flight ST party 到该 clan 的首府
    ///   (兵员注入 garrison + 队伍 disband)，再移除 manager。UI 的"即将解散"提示与后端行为一致。
    /// </summary>
    public void SyncFromConfig()
    {
        try
        {
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            bool shouldManageAi = feat?.ApplyToAiSettlementsToo ?? false;

            if (shouldManageAi)
            {
                int before = _managers.Count;
                EnsureForAllAiClans();
                Logger.Info($"CapitalRegistry.SyncFromConfig: enabled AI clans (+{_managers.Count - before})");
            }
            else
            {
                int removed = 0;
                var aiKeys = new List<Clan>();
                foreach (var c in _managers.Keys)
                {
                    if (c != Clan.PlayerClan) aiKeys.Add(c);
                }
                foreach (var c in aiKeys)
                {
                    // R2：迁移 / 解散此 AI clan 的所有 in-flight ST party。fallback 优先取该 clan 当前
                    // 首府；若 manager 已无可用首府再退化到 clan 名下任意 town/castle；都没有就让兵员蒸发。
                    Settlement? fallback = GetCapitalForClan(c);
                    if (fallback == null)
                    {
                        try
                        {
                            foreach (var s in c.Settlements)
                            {
                                if (s != null && (s.IsTown || s.IsCastle)) { fallback = s; break; }
                            }
                        }
                        catch (Exception sEx) { Logger.Warn($"SyncFromConfig: clan '{c.StringId}' settlement scan failed", sEx); }
                    }

                    try { _lifecycle.MigrateAllOrDisband(c, fallback); }
                    catch (Exception migEx) { Logger.Warn($"SyncFromConfig: MigrateAllOrDisband for AI clan '{c.StringId}' failed", migEx); }

                    _managers.Remove(c);
                    removed++;
                }
                if (removed > 0)
                {
                    Logger.Info($"CapitalRegistry.SyncFromConfig: dissolved AI manager(s) and their in-flight parties (toggle off, removed={removed})");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.SyncFromConfig failed", ex);
        }
    }

    /// <summary>
    /// settlement 易主事件转发：
    ///   1) 若某 manager 的氏族失守该 settlement 且无剩余 town → 该 manager 从注册表移除；
    ///   2) 若 newOwner 的氏族应被接管（玩家氏族或开了 AI toggle 后的 AI 氏族）且尚未注册 → 创建 manager；
    ///   3) 转发事件到所有相关 manager 的 OnSettlementOwnerChanged。
    /// </summary>
    public void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero? newOwner,
        Hero? oldOwner,
        Hero? capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        try
        {
            // 转发到 oldOwner clan 和 newOwner clan 的 manager（如果存在）；其他 manager 不关心。
            var oldClan = oldOwner?.Clan;
            var newClan = newOwner?.Clan;

            if (oldClan != null && _managers.TryGetValue(oldClan, out var oldMgr))
            {
                oldMgr.OnSettlementOwnerChanged(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail);
            }
            if (newClan != null && newClan != oldClan && _managers.TryGetValue(newClan, out var newMgr))
            {
                newMgr.OnSettlementOwnerChanged(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail);
            }

            // 如果 AI 接管开启 + newOwner 是 NPC clan 且尚未注册 → 创建。
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat?.ApplyToAiSettlementsToo == true
                && newClan != null
                && newClan != Clan.PlayerClan
                && !_managers.ContainsKey(newClan)
                && (settlement.IsTown || settlement.IsCastle))
            {
                EnsureForClan(newClan);
                Logger.Info($"CapitalRegistry: AI clan '{newClan.StringId}' 获新城 → 创建 manager 接管");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.OnSettlementOwnerChanged failed", ex);
        }
    }

    /// <summary>
    /// P0-4：玩家换氏族时调用。
    /// 解散 <paramref name="oldPlayerClan"/> 所有在途 ST 队伍，从 registry 移除其 manager，
    /// 然后为 <paramref name="newPlayerClan"/> 执行 EnsureForClan（若不为 null）。
    /// </summary>
    public void HandlePlayerClanSwap(Clan? oldPlayerClan, Clan? newPlayerClan)
    {
        try
        {
            // P1-C 防御：同对象不应触发（vanilla 一般不发，但防御性）
            if (oldPlayerClan != null && oldPlayerClan == newPlayerClan) return;

            if (oldPlayerClan != null)
            {
                // P1 #1：在 _managers.Remove 之前先抢救 capital 引用，
                // 让 MigrateAllOrDisband 走 merge 路径而非 disband-only。
                // edge case：若 swap 由 oldClan 失去最后 town 触发，capital 已 null，
                // 此时 fall through 到 null 接受蒸发 — 玩家已无 garrison 可 merge。
                Settlement? oldCapital = GetCapitalForClan(oldPlayerClan);
                _lifecycle?.MigrateAllOrDisband(oldPlayerClan, oldCapital);
                _managers.Remove(oldPlayerClan);
                Logger.Info($"HandlePlayerClanSwap: removed manager for old clan '{oldPlayerClan.StringId}' (merged to '{oldCapital?.Name?.ToString() ?? "<none>"}')");
            }
            if (newPlayerClan != null)
            {
                EnsureForClan(newPlayerClan);
                Logger.Info($"HandlePlayerClanSwap: ensured manager for new clan '{newPlayerClan.StringId}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.HandlePlayerClanSwap failed", ex);
        }
    }

    /// <summary>
    /// 反注册路径（由 SovereignTownsCampaignBehavior.Uninstall 链调用）。
    /// CapitalRegistry 本身不直接订阅 vanilla 事件（OnSettlementOwnerChanged 由 behavior 转发进来），
    /// 故只需把全局 Instance 清空，并幂等防重入。
    /// 多次调用安全：若 Instance 已是 null 或已被新实例替代，直接 return。
    /// </summary>
    public void Uninstall()
    {
        try
        {
            if (Instance != this) return; // 幂等：已被替换 / 已清空
            Instance = null;
            Logger.Info("CapitalRegistry: uninstalled (Instance cleared)");
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.Uninstall failed", ex);
        }
    }

    // ───────── 私有 ─────────

    /// <summary>
    /// 给指定 clan 创建 manager（如果尚未存在）并立即初始化。
    /// <paramref name="pendingStringId"/> 非空时会在 Initialize 之前调
    /// <see cref="CapitalManager.RestoreFromStringId"/>，让 Initialize 走"沿用持久化 stringId"分支。
    /// </summary>
    private CapitalManager EnsureForClan(Clan clan, string? pendingStringId = null)
    {
        if (_managers.TryGetValue(clan, out var existing)) return existing;
        var mgr = new CapitalManager(_lifecycle, clan);
        if (!string.IsNullOrEmpty(pendingStringId))
        {
            mgr.RestoreFromStringId(pendingStringId);
        }
        mgr.Initialize();
        _managers[clan] = mgr;
        return mgr;
    }

    /// <summary>枚举所有 Kingdom 中的非玩家 Clan，给每个有 Town/Castle 的 NPC clan 建 manager。</summary>
    private void EnsureForAllAiClans()
    {
        try
        {
            foreach (var c in Clan.All)
            {
                if (c is null) continue;
                if (c == Clan.PlayerClan) continue;
                if (c.IsEliminated) continue;
                if (_managers.ContainsKey(c)) continue;
                // 仅在该 clan 至少拥有 1 个 Town（不含 Castle，首府按 Town 算）时创建。
                if (!HasAnyTown(c)) continue;

                string? pending = null;
                if (_pendingCapitals != null
                    && _pendingCapitals.TryGetValue(c.StringId, out var sid))
                {
                    pending = sid;
                }
                EnsureForClan(c, pending);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalRegistry.EnsureForAllAiClans failed", ex);
        }
    }

    private static bool HasAnyTown(Clan clan)
    {
        try
        {
            foreach (var t in Town.AllTowns)
            {
                if (t != null && t.IsTown && t.OwnerClan == clan) return true;
            }
        }
        catch { /* swallow */ }
        return false;
    }
}
