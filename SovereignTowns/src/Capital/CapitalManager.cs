using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Capital;

/// <summary>
/// 首府制（CapitalTown）核心 Manager。
///
/// 设计要点：
///   - 唯一决策中心：玩家所有自动化（驻军 / 招募 / 巡逻 / 调拨）只对 *当前首府* 生效；
///   - 玩家失守首府 → 从剩余玩家自有 Town 中随机选新首府，并通过
///     <see cref="PartyLifecycleManager.MigrateAllOrDisband"/> 把所有 in-flight party
///     的非英雄兵员迁入新首府或蒸发；
///   - 玩家从 0 Town → 拥有第 1 个 Town → 自动设为新首府；
///   - 0 Town → 系统"无首府"，所有 EvaluateAll 类轮询自我抑制；
///   - 持久化只保存 <see cref="Settlement.StringId"/>（vanilla state 已稳）。
///
/// 与同伴 Manager 的契约：调用方传入的 ctor 参数 <see cref="PartyLifecycleManager"/>
/// 必须 *非 null*；其余依赖通过 vanilla 静态入口取得（Clan.PlayerClan / Town.AllTowns）。
/// </summary>
public sealed class CapitalManager
{
    private readonly PartyLifecycleManager _lifecycle;
    // 2026-05-12 审查 D-I2 修复：用 MBRandom（vanilla 可控随机）替代默认时钟种子 Random，
    // 让首府随机切换在同存档重放下可重现。

    /// <summary>当前首府的 settlement StringId（持久化字段）。null 表示无首府。</summary>
    private string? _capitalStringId;

    public CapitalManager(PartyLifecycleManager lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

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

    /// <summary>给 SyncData 用的当前 stringId（可能 null）。</summary>
    public string? GetCapitalStringId() => _capitalStringId;

    /// <summary>读档恢复：仅设置内部字段，真正校验在 <see cref="Initialize"/> 中完成。</summary>
    public void RestoreFromStringId(string? settlementStringId)
    {
        _capitalStringId = string.IsNullOrEmpty(settlementStringId) ? null : settlementStringId;
        Logger.Info($"CapitalManager.RestoreFromStringId: pending='{_capitalStringId ?? "<null>"}'");
    }

    /// <summary>
    /// 启动时（OnSessionLaunched）选首府：
    ///   1) 若 <see cref="_capitalStringId"/> 已设且能解析回有效 Town 且仍属玩家 → 沿用；
    ///   2) 否则在玩家自有 Town 列表中随机抽 1 个；
    ///   3) 玩家 Town=0 → 保持 null（系统"无首府"）。
    /// </summary>
    public void Initialize()
    {
        try
        {
            // 1) 优先尝试沿用持久化的 stringId
            if (!string.IsNullOrEmpty(_capitalStringId))
            {
                var loaded = GetCapitalSettlement();
                if (loaded != null && loaded.IsTown && loaded.OwnerClan == Clan.PlayerClan)
                {
                    Logger.Info($"Capital initialized: '{loaded.Name}' (restored from save, stringId='{_capitalStringId}')");
                    return;
                }
                Logger.Warn($"Capital initialized: restored stringId='{_capitalStringId}' invalid (loaded={loaded?.Name?.ToString() ?? "<null>"} isTown={loaded?.IsTown} ownerClan={loaded?.OwnerClan?.Name?.ToString() ?? "<null>"}); falling back to random pick");
                _capitalStringId = null;
            }

            // 2) 从玩家自有 Town 随机抽
            var owned = ListPlayerOwnedTowns();
            if (owned.Count == 0)
            {
                _capitalStringId = null;
                Logger.Info("Capital: none (player owns 0 towns)");
                return;
            }

            var pick = owned[MBRandom.RandomInt(owned.Count)];
            _capitalStringId = pick.Settlement?.StringId;
            Logger.Info($"Capital initialized: '{pick.Name}' (random pick from {owned.Count} player-owned town(s))");
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
            bool playerLostThisSettlement = oldOwner?.Clan == Clan.PlayerClan && newOwner?.Clan != Clan.PlayerClan;
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
                    _lifecycle.MigrateAllOrDisband(newCapital?.Settlement);
                }
                catch (Exception ex)
                {
                    Logger.Error("CapitalManager: MigrateAllOrDisband failed during capital loss", ex);
                }

                _capitalStringId = newCapStringId;

                Logger.Info($"Capital lost: '{lostName}' (detail={detail}). New capital='{newName}' (player now owns {owned.Count} town(s))");
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
            bool playerGainedThisSettlement = newOwner?.Clan == Clan.PlayerClan && oldOwner?.Clan != Clan.PlayerClan;
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
            if (newCapital.OwnerClan != Clan.PlayerClan)
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
                _lifecycle.MigrateAllOrDisband(newCapital.Settlement);
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

    private static List<Town> ListPlayerOwnedTowns()
    {
        var result = new List<Town>();
        try
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null) return result;
            foreach (var t in Town.AllTowns)
            {
                if (t != null && t.IsTown && t.OwnerClan == playerClan)
                {
                    result.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalManager.ListPlayerOwnedTowns failed", ex);
        }
        return result;
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
