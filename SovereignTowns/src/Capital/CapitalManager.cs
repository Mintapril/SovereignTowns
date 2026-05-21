using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Economy;
using SovereignTowns.Lifecycle;
using SovereignTowns.Patrol;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Capital;

/// <summary>
/// 首府制（CapitalTown）核心 Manager —— **per-clan 实例**。
///
/// B7.15 起每个被 ST 接管的 Clan 各自持有一份 CapitalManager（玩家氏族永远有；AI 阵营当
/// <see cref="EnabledFeatures.ApplyToAiSettlementsToo"/> 开启时也有）。所有 manager 通过
/// <see cref="CapitalRegistry"/> 间接查询。
///
/// 设计要点：
///   - 唯一决策中心：本氏族所有自动化（驻军 / 招募 / 巡逻 / 调拨）只对该氏族 *当前首府* 生效；
///   - 本氏族失守首府 → 从剩余自有 Town 中随机选新首府，并通过
///     <see cref="PartyLifecycleManager.MigrateAllOrDisband"/> 把该氏族的所有 in-flight party
///     的非英雄兵员迁入新首府或蒸发；
///   - 本氏族从 0 Town → 拥有第 1 个 Town → 自动设为新首府；
///   - 0 Town → 系统"无首府"，所有 EvaluateAll 类轮询自我抑制；
///   - 首府 stringId 通过 <see cref="SovereignTowns.Campaign.SovereignTownsCampaignBehavior"/>.SyncData
///     的 <c>st_capitals_json</c> 字段持久化（仅此一项；scheduler 历史 / finance ledger / snapshot 仍瞬态）。
///     读档路径：SyncData(load) → <see cref="CapitalRegistry.RestoreCapitals"/> → EnsureForClan(pending) →
///     <see cref="RestoreFromStringId"/> → <see cref="Initialize"/> 的"沿用持久化 stringId"分支。
///
/// 与同伴 Manager 的契约：调用方传入的 ctor 参数 <see cref="PartyLifecycleManager"/>
/// 必须 *非 null*；其余依赖通过 vanilla 静态入口取得。
/// </summary>
public sealed class CapitalManager
{
    private readonly PartyLifecycleManager _lifecycle;
    /// <summary>本 manager 服务的氏族。玩家氏族 == <c>_clan</c>；AI 氏族即原 Clan 实例。</summary>
    private readonly Clan _clan;

    // 2026-05-12 审查 D-I2 修复：用 MBRandom（vanilla 可控随机）替代默认时钟种子 Random，
    // 让首府随机切换在同存档重放下可重现。

    /// <summary>当前首府的 settlement StringId。null 表示无首府。
    /// 持久化字段：由 SovereignTownsCampaignBehavior.SyncData 通过 <see cref="RestoreFromStringId"/>
    /// 在读档时注入；存档时 <see cref="GetCapitalStringId"/> 把它读出来。
    /// 运行时由 <see cref="OnSettlementOwnerChanged"/> / <see cref="ManuallySetCapital"/> / <see cref="Initialize"/> 维护。</summary>
    private string? _capitalStringId;

    /// <summary>本氏族的巡逻调度器（B7.26）。与 manager 同生命周期。</summary>
    private readonly ClanPatrolScheduler _patrolScheduler;

    /// <summary>本氏族的征兵调度器（B7.27）。与 manager 同生命周期。</summary>
    private readonly ClanRecruiterScheduler _recruiterScheduler;

    /// <summary>本氏族的金库（余额 + 7 日开销环）。经 SyncData st_treasuries_json 持久化。</summary>
    private ClanTreasury _treasury = new ClanTreasury();

    public CapitalManager(PartyLifecycleManager lifecycle, Clan clan)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _clan = clan ?? throw new ArgumentNullException(nameof(clan));
        _patrolScheduler = new ClanPatrolScheduler(_clan);
        _recruiterScheduler = new ClanRecruiterScheduler(_clan);
    }

    /// <summary>本 manager 服务的氏族（不可变）。</summary>
    public Clan OwnerClan => _clan;

    /// <summary>方便区分：本氏族是否就是玩家氏族。</summary>
    public bool IsPlayerClan => _clan == Clan.PlayerClan;

    /// <summary>本氏族的巡逻调度器（B7.26 全氏族巡逻）。永不为 null。</summary>
    public ClanPatrolScheduler PatrolScheduler => _patrolScheduler;

    /// <summary>本氏族的征兵调度器（B7.27）。永不为 null。</summary>
    public ClanRecruiterScheduler RecruiterScheduler => _recruiterScheduler;

    /// <summary>本氏族的金库（余额 + 7 日开销环）。永不为 null。</summary>
    public ClanTreasury Treasury => _treasury;

    /// <summary>
    /// SyncData(load) 路径用 —— 从持久化字符串恢复金库状态。
    /// 必须在 <see cref="Initialize"/> 之前由 <see cref="CapitalRegistry.EnsureForClan"/> 调用。
    /// null / 空串安全：退化为空金库。
    /// </summary>
    public void RestoreTreasuryFrom(string? s) => _treasury = ClanTreasury.Deserialize(s);

    /// <summary>当前首府的 <see cref="Town"/>，或 null（无首府 / 该 settlement 已失效）。</summary>
    public Town? GetCapital()
    {
        var s = GetCapitalSettlement();
        return (s != null && s.IsTown) ? s.Town : null;
    }

    /// <summary>当前首府的 <see cref="Settlement"/>，或 null。</summary>
    public Settlement? GetCapitalSettlement()
    {
        if (string.IsNullOrEmpty(_capitalStringId)) return null;
        try
        {
            return MBObjectManager.Instance?.GetObject<Settlement>(_capitalStringId);
        }
        catch (Exception ex)
        {
            Logger.Error($"GetCapitalSettlement lookup failed for stringId='{_capitalStringId}'", ex);
            return null;
        }
    }

    /// <summary>
    /// 2026-05-14 Task X：SyncData(load) 路径用 —— 把磁盘上的 settlement stringId 暂存到本 manager。
    /// 必须在 <see cref="Initialize"/> 之前调用（CapitalRegistry.EnsureForClan 已保证此顺序），
    /// 这样 Initialize 的"已设 stringId"分支才会沿用此值。null/空串表示"该 clan 当时无首府" → 落随机分支。
    /// </summary>
    public void RestoreFromStringId(string? settlementStringId)
    {
        _capitalStringId = string.IsNullOrEmpty(settlementStringId) ? null : settlementStringId;
    }

    /// <summary>
    /// 2026-05-14 Task X：SyncData(save) 路径用 —— 导出当前首府的 settlement StringId 给磁盘写盘。
    /// 返回 null 表示该 clan 当前无首府（CapitalRegistry.ExportCapitals 会把它从 dict 中跳过）。
    /// </summary>
    public string? GetCapitalStringId() => _capitalStringId;

    /// <summary>
    /// 启动时（OnSessionLaunched）选首府：
    ///   1) 若 <see cref="_capitalStringId"/> 已设（由 SyncData → RestoreFromStringId 注入）且能解析回有效 Town
    ///      且仍属本氏族 → 沿用；
    ///   2) 否则在本氏族自有 Town 列表中随机抽 1 个；
    ///   3) 本氏族 Town=0 → 保持 null（系统"无首府"）。
    /// </summary>
    public void Initialize()
    {
        try
        {
            // 1) 优先沿用 SyncData 回写的 stringId（2026-05-14 Task X 起回归）。
            //    构造时 _capitalStringId 必为 null；只有 RestoreFromStringId 能在 Initialize 之前写入。
            //    校验失败（settlement 不存在 / 不是 Town / 已易主）→ 落回随机抽取分支。
            if (!string.IsNullOrEmpty(_capitalStringId))
            {
                var loaded = GetCapitalSettlement();
                if (loaded != null && loaded.IsTown && loaded.OwnerClan == _clan)
                {
                    Logger.Info($"Capital initialized: '{loaded.Name}' (restored from save, stringId='{_capitalStringId}')");
                    return;
                }
                Logger.Warn($"Capital initialized: restored stringId='{_capitalStringId}' invalid (loaded={loaded?.Name?.ToString() ?? "<null>"} isTown={loaded?.IsTown} ownerClan={loaded?.OwnerClan?.Name?.ToString() ?? "<null>"}); falling back to random pick");
                _capitalStringId = null;
            }

            // 2) 从本氏族自有 Town 随机抽
            var owned = ListPlayerOwnedTowns();
            if (owned.Count == 0)
            {
                _capitalStringId = null;
                Logger.Info($"Capital: none (clan '{_clan?.Name?.ToString() ?? "<null>"}' owns 0 towns)");
                return;
            }

            var pick = owned[MBRandom.RandomInt(owned.Count)];
            _capitalStringId = pick.Settlement?.StringId;
            Logger.Info($"Capital initialized: '{pick.Name}' (random pick from {owned.Count} clan-owned town(s))");
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalManager.Initialize failed", ex);
            _capitalStringId = null;
        }
    }

    /// <summary>
    /// settlement 易主回调。参数顺序与 vanilla
    /// <c>CampaignEvents.OnSettlementOwnerChangedEvent</c> 一致：
    /// <c>(Settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementDetail detail)</c>。
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
            if (settlement == null) return;

            // ───── A) 玩家失守首府 ─────
            var currentCapital = GetCapitalSettlement();
            bool playerLostThisSettlement = oldOwner?.Clan == _clan && newOwner?.Clan != _clan;
            bool isCurrentCapital = currentCapital != null && currentCapital == settlement;

            if (playerLostThisSettlement && isCurrentCapital)
            {
                var lostName = settlement.Name?.ToString() ?? "<unnamed>";
                var owned = ListPlayerOwnedTowns();
                Town? newCapital = null;
                if (owned.Count > 0)
                {
                    newCapital = owned[MBRandom.RandomInt(owned.Count)];
                }

                var newCapStringId = newCapital?.Settlement?.StringId;
                var newName = newCapital?.Name?.ToString() ?? "<none>";

                try
                {
                    // B7.15: 只迁移本氏族 (_clan) 的 in-flight party 到本氏族新首府
                    _lifecycle.MigrateAllOrDisband(_clan, newCapital?.Settlement);
                }
                catch (Exception ex)
                {
                    Logger.Error("CapitalManager: MigrateAllOrDisband failed during capital loss", ex);
                }

                _capitalStringId = newCapStringId;

                Logger.Info($"Capital lost: '{lostName}' (detail={detail}). New capital='{newName}' (clan {_clan.StringId} now owns {owned.Count} town(s))");
                DecisionAuditLogger.LogRule(
                    decisionType: "capital_lost",
                    inputSummary: $"lost='{lostName}' detail={detail} remainingTowns={owned.Count}",
                    decisionJson: $"{{\"lost\":\"{EscapeJson(lostName)}\",\"newCapital\":\"{EscapeJson(newName)}\"}}",
                    accepted: true,
                    rejectionReason: null);
                return;
            }

            // ───── A.5) 2026-05-12 审查 D-WARN-11 修复：玩家失守非首府城 → 仅清理该城关联的 in-flight party ─────
            if (playerLostThisSettlement && !isCurrentCapital)
            {
                try
                {
                    _lifecycle.MigrateByHomeSettlement(settlement, currentCapital);
                    Logger.Info($"CapitalManager: non-capital '{settlement.Name}' lost, cleaned in-flight parties homed there (fallback to '{currentCapital?.Name?.ToString() ?? "<none>"}')");
                }
                catch (Exception ex)
                {
                    Logger.Error("CapitalManager: MigrateByHomeSettlement failed on non-capital loss", ex);
                }
                // 继续 fall through 到 B 判定（不太可能同时 gain + lost，但保留对称）
            }

            // ───── B) 玩家获得新 Town 且当前无首府 → 自动设为首府 ─────
            bool playerGainedThisSettlement = newOwner?.Clan == _clan && oldOwner?.Clan != _clan;
            if (playerGainedThisSettlement && settlement.IsTown && string.IsNullOrEmpty(_capitalStringId))
            {
                _capitalStringId = settlement.StringId;
                var gainedName = settlement.Name?.ToString() ?? "<unnamed>";
                Logger.Info($"Capital restored: '{gainedName}' (auto — player acquired first town)");
                DecisionAuditLogger.LogRule(
                    decisionType: "capital_restored",
                    inputSummary: $"gained='{gainedName}' detail={detail}",
                    decisionJson: $"{{\"newCapital\":\"{EscapeJson(gainedName)}\"}}",
                    accepted: true,
                    rejectionReason: null);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalManager.OnSettlementOwnerChanged failed (settlement='{settlement?.Name}')", ex);
        }
    }

    /// <summary>
    /// 玩家通过城镇菜单手动指定首府。
    /// 返回是否实际发生切换（与原首府相同 / 校验失败 → false）。
    /// 切换时一并清理所有 in-flight party 并把兵员迁入新首府。
    /// </summary>
    public bool ManuallySetCapital(Town newCapital)
    {
        try
        {
            if (newCapital == null)
            {
                Logger.Warn("ManuallySetCapital: newCapital is null, ignored");
                return false;
            }
            if (newCapital.OwnerClan != _clan)
            {
                Logger.Warn($"ManuallySetCapital: '{newCapital.Name}' not owned by player clan, ignored");
                return false;
            }
            var newStringId = newCapital.Settlement?.StringId;
            if (string.IsNullOrEmpty(newStringId))
            {
                Logger.Warn($"ManuallySetCapital: '{newCapital.Name}' has no settlement stringId, ignored");
                return false;
            }
            if (!string.IsNullOrEmpty(_capitalStringId) && string.Equals(_capitalStringId, newStringId, StringComparison.Ordinal))
            {
                Logger.Info($"ManuallySetCapital: '{newCapital.Name}' is already the capital — no change");
                return false;
            }

            var oldStringId = _capitalStringId;
            var oldName = GetCapitalSettlement()?.Name?.ToString() ?? "<none>";

            try
            {
                // B7.15: 只迁移本氏族 (_clan) 的 party。玩家手动切首府不应触动 AI 的 party。
                _lifecycle.MigrateAllOrDisband(_clan, newCapital.Settlement);
            }
            catch (Exception ex)
            {
                Logger.Error("CapitalManager: MigrateAllOrDisband failed during manual switch", ex);
            }

            _capitalStringId = newStringId;

            Logger.Info($"Capital manually set: '{oldName}' → '{newCapital.Name}'");
            DecisionAuditLogger.LogRule(
                decisionType: "manual_set_capital",
                inputSummary: $"old='{oldName}' new='{newCapital.Name}'",
                decisionJson: $"{{\"old\":\"{EscapeJson(oldName)}\",\"new\":\"{EscapeJson(newCapital.Name?.ToString() ?? "")}\"}}",
                accepted: true,
                rejectionReason: null);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ManuallySetCapital failed (newCapital='{newCapital?.Name}')", ex);
            return false;
        }
    }

    // ───────── 内部辅助 ─────────

    /// <summary>枚举本氏族当前拥有的全部 Town（不含 Castle，因为首府按 Town 制）。</summary>
    private List<Town> ListPlayerOwnedTowns()
    {
        var result = new List<Town>();
        try
        {
            if (_clan == null) return result;
            foreach (var t in Town.AllTowns)
            {
                if (t != null && t.IsTown && t.OwnerClan == _clan)
                {
                    result.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CapitalManager.ListPlayerOwnedTowns failed (clan={_clan?.StringId})", ex);
        }
        return result;
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
