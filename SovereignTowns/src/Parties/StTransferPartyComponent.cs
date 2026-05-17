using System;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using SovereignTowns.Lifecycle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// 调拨队伍组件。隐式状态（不用 enum），由 Source / Destination / TargetSettlement 推断。
/// 不应用 ShouldReturnAndDisband 判定 — 兵员是货物，到达前不应中途解散。
/// </summary>
public sealed class StTransferPartyComponent : StPartyComponent
{
    public const string StringIdPrefix = "st_transfer_";

    [SaveableField(20)] private Settlement? _source;
    [SaveableField(21)] private Settlement? _destination;
    [CachedData] private TextObject? _cachedName;

    public Settlement? Source => _source;
    public Settlement? Destination => _destination;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var srcName = _source?.Name?.ToString() ?? "未知";
            var dstName = _destination?.Name?.ToString() ?? "未知";
            _cachedName = new TextObject("{=ST_TransferPartyName}调拨队 - " + srcName + " → " + dstName);
            return _cachedName;
        }
    }

    public override bool AvoidHostileActions => true;
    protected override bool AppliesReturnDisbandCondition => false;  // 调拨队不应用回城解散判定

    private StTransferPartyComponent(
        Settlement source, Settlement destination,
        TextObject name, Hero owner,
        string partyMountStringId, string partyHarnessStringId,
        float customPartyBaseSpeed, bool avoidHostileActions,
        InitializationArgs args, Hero? leader = null)
        : base(source, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _source = source;
        _destination = destination;
    }

    /// 工厂：创建调拨队伍。失败返回 null，不抛。
    public static MobileParty? CreateForRoute(Settlement source, Settlement destination, TroopRoster troops)
    {
        if (source == null || destination == null || troops == null) return null;
        try
        {
            var ownerClan = source.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"StTransferPartyComponent.CreateForRoute: source '{source.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();
            var args = new InitializationArgs(source.GatePosition, 1f, ownerClan, troops, emptyPrisoners);

            var nameObj = new TextObject(
                "{=ST_TransferPartyName}调拨队 - " + source.Name + " → " + destination.Name);

            var component = new StTransferPartyComponent(
                source: source, destination: destination,
                name: nameObj, owner: ownerLeader,
                partyMountStringId: string.Empty, partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f, avoidHostileActions: true,
                args: args, leader: null);

            var stringId = StringIdPrefix + source.StringId + "_" + DateTime.UtcNow.Ticks.ToString();
            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"StTransferPartyComponent.CreateForRoute: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }
            try { mobileParty.Aggressiveness = 0f; } catch { }

            component.SnapshotInitialMembers(mobileParty);

            Logger.Info($"StTransferPartyComponent: created '{stringId}' for '{source.StringId}' → '{destination.StringId}'");
            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error("StTransferPartyComponent.CreateForRoute: unexpected exception", ex);
            return null;
        }
    }

    /// 隐式状态机：dest owner 变更 / dest 危险 → 改返 source；正常情况由基类 IsAtHome 接管。
    /// 注意：基类的 `home` 是 _source（构造时传入），所以 IsAtHome 自动检测"回到 source"。
    /// 到达 destination 不会触发 IsAtHome，要单独检测。
    protected override void OnHourlyTickCore(MobileParty self, Settlement capital)
    {
        var dest = _destination;
        if (dest == null) return;

        // 1) 已到达 destination → 注入 garrison + 解散（不走基类的 OnArrivedHome，因为 home == source）
        if (self.LastVisitedSettlement == dest)
        {
            DeliverAndDisband(self, dest);
            return;
        }

        var partyClan = self.ActualClan ?? _source?.OwnerClan ?? dest.OwnerClan;

        // 2) destination owner 变更 → 改返安全 fallback
        if (partyClan != null && dest.OwnerClan != partyClan)
        {
            var fallback = ResolveSafeFallback(partyClan);
            if (fallback != null)
            {
                if (self.LastVisitedSettlement == fallback)
                {
                    Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed; merging into fallback '{fallback.Name}'");
                    DeliverAndDisband(self, fallback);
                }
                else if (self.TargetSettlement != fallback)
                {
                    Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed; rerouting to '{fallback.Name}'");
                    SafeMoveHelper.GoTo(self, fallback, "reroute-to-fallback");
                }
            }
            else
            {
                Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed and no safe fallback; disbanding");
                PartyMergeService.Instance.DisbandAndUntrack(self, "StTransferPartyComponent destination lost");
            }
            return;
        }

        // 3) destination 极端危险 → 改返 source（不解散）
        var risk = RiskAssessmentService.Assess(dest);
        if (risk.Level >= RiskLevel.Critical)
        {
            var src = _source;
            if (src != null && self.TargetSettlement != src)
            {
                Logger.Warn($"StTransferParty '{self.Name}': 目的地 '{dest.Name}' risk={risk.Level}，改返 '{src.Name}'");
                SafeMoveHelper.GoTo(self, src, "risk-reroute-to-source");
            }
        }
    }

    // 调拨队的 IsAtHome 含义即"已抵达 source"——dest 危险被改回，到家后解散。
    // 由基类 DefaultMergeAndDisband 处理（home == source，行为等价于把兵塞回源城驻军 + 解散）。
    // "到 dest"路径单独由 OnHourlyTickCore 分支 1 触发 DeliverAndDisband(self, dest)。

    private void DeliverAndDisband(MobileParty self, Settlement target)
    {
        int delivered = PartyMergeService.Instance.MergeNonHeroTroopsIntoGarrison(self, target, "StTransferPartyComponent.DeliverAndDisband");
        Logger.Info($"StTransferParty '{self.Name}': 注入 {delivered} 名兵员到 '{target?.Name}' 驻军，解散队伍");
        PartyMergeService.Instance.DisbandAndUntrack(self, "StTransferPartyComponent.DeliverAndDisband");
    }

    private Settlement? ResolveSafeFallback(Clan partyClan)
    {
        try
        {
            if (partyClan == null) return null;
            var src = _source;
            if (src != null && src.OwnerClan == partyClan) return src;
            var capital = CapitalRegistry.Instance?.GetCapitalForClan(partyClan);
            if (capital != null) return capital;

            // B17.4 B4：第三 fallback — 本 clan 名下任意 fortification(town/castle)，路径附近优先
            // (按 party 当前 2D 位置最近排序)。绝不跨 clan，防止把 ST 兵塞给别人。
            return FindNearestClanFortification(partyClan);
        }
        catch { return null; }
    }

    private Settlement? FindNearestClanFortification(Clan partyClan)
    {
        try
        {
            var settlements = partyClan?.Settlements;
            if (settlements == null) return null;
            var party = MobileParty;  // 'this' StPartyComponent.MobileParty (inherited from CustomPartyComponent)
            var partyPos = party?.GetPosition2D ?? default;
            Settlement? best = null;
            float bestDist = float.MaxValue;
            foreach (var s in settlements)
            {
                if (s == null) continue;
                if (s.OwnerClan != partyClan) continue;  // 防御：Clan.Settlements 在 ownership 转移瞬间可能 lag
                if (!s.IsFortification) continue;
                if (s.IsUnderSiege) continue;
                float d = (s.GetPosition2D - partyPos).Length;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            if (best != null)
                Logger.Info($"StTransferParty: third-tier fallback selected '{best.Name}' (dist={bestDist:F1})");
            return best;
        }
        catch (Exception ex) { Logger.Warn($"FindNearestClanFortification failed: {ex.Message}"); return null; }
    }
}
