using System;
using SovereignTowns.Capital;
using SovereignTowns.Common;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Battle;

/// <summary>
/// Central battle-loot coordinator for ST-controlled parties. Managers remain responsible for
/// movement/state transitions; this class decides which battle participants should run the
/// post-battle loot pipeline.
/// </summary>
public sealed class BattleLootManager
{
    private readonly CapitalRegistry _capitalRegistry;

    public BattleLootManager(CapitalRegistry capitalRegistry)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
    }

    public void OnMapEventEnded(MapEvent mapEvent)
    {
        if (mapEvent == null) return;
        try
        {
            ProcessSide(mapEvent.AttackerSide);
            ProcessSide(mapEvent.DefenderSide);
        }
        catch (Exception ex)
        {
            Logger.Error("BattleLootManager.OnMapEventEnded failed", ex);
        }
    }

    public void ProcessPartyIfEligible(MobileParty? party)
    {
        try
        {
            if (!IsEligibleStParty(party)) return;
            BattleLootHandler.ProcessPartyLoot(party, _capitalRegistry);
        }
        catch (Exception ex)
        {
            Logger.Error($"BattleLootManager.ProcessPartyIfEligible failed for '{PartyNameFormatter.SafeName(party)}'", ex);
        }
    }

    private void ProcessSide(MapEventSide? side)
    {
        if (side?.Parties == null) return;

        foreach (var eventParty in side.Parties)
        {
            MobileParty? party = null;
            try { party = eventParty.Party?.MobileParty; }
            catch { continue; }
            ProcessPartyIfEligible(party);
        }
    }

    private bool IsEligibleStParty(MobileParty? party)
    {
        try
        {
            if (party == null || !party.IsActive) return false;

            var clan = ResolvePartyClan(party);
            if (!_capitalRegistry.IsManagedClanWithCapital(clan)) return false;

            if (party.PartyComponent is StSallyPartyComponent) return true;
            if (party.PartyComponent is StPatrolPartyComponent) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static TaleWorlds.CampaignSystem.Clan? ResolvePartyClan(MobileParty party)
    {
        try
        {
            if (party.ActualClan != null) return party.ActualClan;
            if (party.PartyComponent is StSallyPartyComponent sally) return sally.HomeSettlement?.OwnerClan;
            if (party.PartyComponent is StPatrolPartyComponent patrol) return patrol.HomeSettlement?.OwnerClan;
        }
        catch
        {
            return null;
        }
        return null;
    }
}
