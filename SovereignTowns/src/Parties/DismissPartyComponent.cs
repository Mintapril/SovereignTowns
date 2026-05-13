using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// B1 #6.B: A short-lived party carrying dismissed-troop excess from a player-owned
/// town back to a home village. Once the party reaches the village (or idle thresholds
/// trigger), it is destroyed — soldiers "go home" semantically.
///
/// <para>
/// Same lifecycle category as recruiter / transfer / sallyforth: persists through
/// save/load via SaveableTypeDefiner (LocalSaveId=4), tracked by
/// <see cref="Lifecycle.PartyLifecycleManager"/>, swept by
/// <see cref="Ui.SafeUninstallMenu"/> on uninstall.
/// </para>
/// </summary>
public sealed class DismissPartyComponent : CustomPartyComponent
{
    /// <summary>stringId prefix; allows pattern matching in uninstall + audit paths.</summary>
    public const string StringIdPrefix = "st_dismiss_";

    [SaveableField(1)]
    private string? _homeVillageStringId;

    [SaveableField(2)]
    private string? _dismissedFromTownStringId;

    [SaveableField(3)]
    private CampaignTime _departureTime;

    [CachedData]
    private TextObject? _cachedName;

    /// <summary>Resolves the dismissal source town's settlement from saved stringId; null if missing/destroyed.</summary>
    public Settlement? DismissedFromSettlement
        => string.IsNullOrEmpty(_dismissedFromTownStringId)
            ? null
            : MBObjectManager.Instance?.GetObject<Settlement>(_dismissedFromTownStringId);

    /// <summary>Resolves the destination village from saved stringId; null if missing/destroyed.</summary>
    public Settlement? HomeVillage
        => string.IsNullOrEmpty(_homeVillageStringId)
            ? null
            : MBObjectManager.Instance?.GetObject<Settlement>(_homeVillageStringId);

    /// <summary>Hour the dispatcher created the party. Lifecycle idle math reads it.</summary>
    public CampaignTime DepartureTime => _departureTime;

    public override Hero? PartyOwner => DismissedFromSettlement?.OwnerClan?.Leader;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var srcName = DismissedFromSettlement?.Name?.ToString() ?? "Unknown";
            _cachedName = new TextObject("{=ST_DismissedParty}Dismissed Troops of " + srcName);
            return _cachedName;
        }
    }

    public override Settlement HomeSettlement => HomeVillage ?? DismissedFromSettlement!;

    public override bool AvoidHostileActions => true;

    private DismissPartyComponent(
        Settlement homeVillage,
        TextObject name,
        Hero owner,
        Settlement dismissedFromTown,
        string partyMountStringId,
        string partyHarnessStringId,
        float customPartyBaseSpeed,
        bool avoidHostileActions,
        InitializationArgs args,
        Hero? leader = null)
        : base(homeVillage, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _homeVillageStringId = homeVillage?.StringId;
        _dismissedFromTownStringId = dismissedFromTown?.StringId;
        _departureTime = CampaignTime.Now;
    }

    /// <summary>
    /// Factory: build a dismiss party in <paramref name="sourceTown"/> bound for
    /// <paramref name="homeVillage"/>. Empty roster — dispatcher fills it after creation.
    /// Returns null + Logger.Error on any failure (never throws).
    /// </summary>
    public static MobileParty? CreateForTown(Town sourceTown, Settlement homeVillage)
    {
        if (sourceTown == null || homeVillage == null)
        {
            Logger.Error("DismissPartyComponent.CreateForTown: null sourceTown or homeVillage");
            return null;
        }

        try
        {
            var sourceSettlement = sourceTown.Settlement;
            if (sourceSettlement == null)
            {
                Logger.Error("DismissPartyComponent.CreateForTown: sourceTown.Settlement is null");
                return null;
            }

            var ownerClan = sourceSettlement.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"DismissPartyComponent.CreateForTown: town '{sourceSettlement.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyTroops = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();

            var args = new InitializationArgs(
                sourceSettlement.GatePosition,
                spawnRadius: 1f,
                ownerClan,
                emptyTroops,
                emptyPrisoners);

            var nameObj = new TextObject(
                "{=ST_DismissedParty}Dismissed Troops of " + sourceSettlement.Name);

            var component = new DismissPartyComponent(
                homeVillage: homeVillage,
                name: nameObj,
                owner: ownerLeader,
                dismissedFromTown: sourceSettlement,
                partyMountStringId: string.Empty,
                partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f,
                avoidHostileActions: true,
                args: args,
                leader: null);

            var stringId = StringIdPrefix
                           + sourceSettlement.StringId
                           + "_"
                           + DateTime.UtcNow.Ticks.ToString();

            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"DismissPartyComponent.CreateForTown: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }

            Logger.Info(
                $"DismissPartyComponent: created '{stringId}' from town '{sourceSettlement.StringId}' headed to '{homeVillage.StringId}' (owner={ownerLeader.Name})");

            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"DismissPartyComponent.CreateForTown: unexpected exception for town '{sourceTown?.Settlement?.StringId ?? "<null>"}'",
                ex);
            return null;
        }
    }
}
