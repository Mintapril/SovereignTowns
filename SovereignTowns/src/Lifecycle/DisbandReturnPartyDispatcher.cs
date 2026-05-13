using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Lifecycle;

/// <summary>
/// B1 #6.B: turn a <c>RequestDisbandExcess</c> intent into a real "discharge home"
/// MobileParty. Picks dominant-culture troops from the garrison (low-tier first),
/// selects a home village within distance, creates a <see cref="DismissPartyComponent"/>,
/// transfers the troops into it, registers tracking, and aims the party at the village.
///
/// <para>
/// On failure at any step, the partial state is left intact (troops returned where
/// possible); the method always returns the count of troops actually dismissed and
/// never throws to the caller.
/// </para>
/// </summary>
public static class DisbandReturnPartyDispatcher
{
    /// <summary>Search radius around the source town for a home village (map units).</summary>
    private const float HomeVillageMaxDistance = 80f;

    /// <summary>Among matching villages, randomise within the closest <c>N</c>.</summary>
    private const int HomeVillageTopRandom = 3;

    /// <summary>
    /// Try to discharge up to <paramref name="magnitude"/> excess troops from
    /// <paramref name="town"/>'s garrison home. Returns the number actually moved
    /// into a created dismiss party.
    /// </summary>
    public static int DismissExcess(
        Town town,
        int magnitude,
        PartyLifecycleManager lifecycle)
    {
        if (town == null || town.Settlement == null || lifecycle == null) return 0;
        if (magnitude <= 0) return 0;

        try
        {
            if (!lifecycle.CanCreateAnotherParty(town.Settlement, PartyLifecycleManager.KindDismiss))
            {
                Logger.Info($"DismissExcess '{town.Name}': already at MaxDismissPerTown limit, skip");
                return 0;
            }

            var roster = town.GarrisonParty?.MemberRoster;
            if (roster == null) return 0;

            // 1) Pick low-tier non-hero regulars up to magnitude
            var picked = PickLowTierTroops(roster, magnitude);
            if (picked.Count == 0)
            {
                Logger.Info($"DismissExcess '{town.Name}': no eligible regular troops found");
                return 0;
            }

            // 2) Dominant culture argmax
            var dominantCulture = DominantCulture(picked);

            // 3) Find home village
            var homeVillage = FindHomeVillage(town.Settlement, dominantCulture);
            if (homeVillage == null)
            {
                Logger.Info($"DismissExcess '{town.Name}': no eligible home village within {HomeVillageMaxDistance} — fallback: RemoveTroop only (no party)");
                int evaporated = 0;
                foreach (var p in picked)
                {
                    try
                    {
                        roster.RemoveTroop(p.Character, p.Count, default(UniqueTroopDescriptor), 0);
                        evaporated += p.Count;
                    }
                    catch (Exception removeEx)
                    {
                        Logger.Error($"DismissExcess '{town.Name}': RemoveTroop fallback failed for '{p.Character.StringId}'", removeEx);
                    }
                }
                DecisionAuditLogger.LogRule(
                    decisionType: "DisbandExcess",
                    inputSummary: $"town={town.Settlement.StringId} mode=evaporate magnitude={magnitude}",
                    decisionJson: $"{{\"dismissed\":{evaporated},\"mode\":\"evaporate\"}}",
                    accepted: evaporated > 0);
                return evaporated;
            }

            // 4) Create dismiss party
            var party = DismissPartyComponent.CreateForTown(town, homeVillage);
            if (party == null)
            {
                Logger.Error($"DismissExcess '{town.Name}': CreateForTown returned null");
                return 0;
            }

            // 5) Move picked troops into the party + remove from garrison
            int actuallyMoved = 0;
            foreach (var p in picked)
            {
                try
                {
                    party.MemberRoster.AddToCounts(p.Character, p.Count, false, 0, 0);
                    roster.RemoveTroop(p.Character, p.Count, default(UniqueTroopDescriptor), 0);
                    actuallyMoved += p.Count;
                }
                catch (Exception moveEx)
                {
                    Logger.Error($"DismissExcess '{town.Name}': troop move failed for '{p.Character.StringId}'", moveEx);
                }
            }

            if (actuallyMoved == 0)
            {
                Logger.Warn($"DismissExcess '{town.Name}': created party but moved 0 troops — destroying party");
                DecisionAuditLogger.LogRule(
                    decisionType: "DisbandExcess",
                    inputSummary: $"town={town.Settlement.StringId} mode=party_destroyed magnitude={magnitude}",
                    decisionJson: $"{{\"dismissed\":0,\"mode\":\"party_destroyed\",\"reason\":\"all_troop_moves_failed\"}}",
                    accepted: false,
                    rejectionReason: "All troop move attempts failed; created party destroyed");
                try { TaleWorlds.CampaignSystem.Actions.DestroyPartyAction.Apply(null, party); }
                catch { /* swallow */ }
                return 0;
            }

            // 6) Track + aim at village
            lifecycle.RegisterTrackedParty(party, town.Settlement, PartyLifecycleManager.KindDismiss);
            try { party.SetMoveGoToSettlement(homeVillage, MobileParty.NavigationType.Default, false); }
            catch (Exception navEx) { Logger.Error($"DismissExcess '{town.Name}': SetMoveGoToSettlement failed", navEx); }

            DecisionAuditLogger.LogRule(
                decisionType: "DisbandExcess",
                inputSummary: $"town={town.Settlement.StringId} mode=party home={homeVillage.StringId} magnitude={magnitude}",
                decisionJson: $"{{\"dismissed\":{actuallyMoved},\"home_village\":\"{homeVillage.StringId}\",\"culture\":\"{dominantCulture?.StringId ?? "<mixed>"}\"}}",
                accepted: true);

            Logger.Info($"DismissExcess '{town.Name}': dispatched {actuallyMoved} troops home to '{homeVillage.Name}' (culture={dominantCulture?.StringId ?? "<mixed>"})");
            return actuallyMoved;
        }
        catch (Exception ex)
        {
            Logger.Error($"DisbandReturnPartyDispatcher.DismissExcess outer failure for '{town?.Name}'", ex);
            return 0;
        }
    }

    private readonly struct PickedTroop
    {
        public PickedTroop(CharacterObject character, int count) { Character = character; Count = count; }
        public CharacterObject Character { get; }
        public int Count { get; }
    }

    private static List<PickedTroop> PickLowTierTroops(TroopRoster roster, int magnitude)
    {
        var result = new List<PickedTroop>();
        try
        {
            var elements = roster.GetTroopRoster();
            // Sort ascending tier so the cheapest leave first
            var sorted = elements
                .Where(e => e.Character != null && !e.Character.IsHero && e.Character.IsRegular && e.Number > 0)
                .OrderBy(e => e.Character.Tier)
                .ToList();

            int remaining = magnitude;
            foreach (var elem in sorted)
            {
                if (remaining <= 0) break;
                int take = Math.Min(elem.Number, remaining);
                if (take <= 0) continue;
                result.Add(new PickedTroop(elem.Character, take));
                remaining -= take;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DisbandReturnPartyDispatcher.PickLowTierTroops failed", ex);
        }
        return result;
    }

    private static CultureObject? DominantCulture(List<PickedTroop> picked)
    {
        var counts = new Dictionary<CultureObject, int>();
        foreach (var p in picked)
        {
            var c = p.Character?.Culture;
            if (c == null) continue;
            counts.TryGetValue(c, out var prev);
            counts[c] = prev + p.Count;
        }
        if (counts.Count == 0) return null;

        CultureObject? best = null;
        int bestCount = -1;
        foreach (var kv in counts)
        {
            if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
        }
        return best;
    }

    private static Settlement? FindHomeVillage(Settlement sourceSettlement, CultureObject? dominantCulture)
    {
        try
        {
            var pos = sourceSettlement.GetPosition2D;

            // (a) Same culture, ≤ 80f, not raided
            var sameCulture = Settlement.All
                .Where(s => s.IsVillage && !IsRaided(s) && (s.GetPosition2D - pos).Length <= HomeVillageMaxDistance
                            && dominantCulture != null && s.Culture == dominantCulture)
                .OrderBy(s => (s.GetPosition2D - pos).Length)
                .Take(HomeVillageTopRandom)
                .ToList();
            if (sameCulture.Count > 0)
            {
                return sameCulture[MBRandom.RandomInt(sameCulture.Count)];
            }

            // (b) Any culture, ≤ 80f, not raided
            var anyCulture = Settlement.All
                .Where(s => s.IsVillage && !IsRaided(s) && (s.GetPosition2D - pos).Length <= HomeVillageMaxDistance)
                .OrderBy(s => (s.GetPosition2D - pos).Length)
                .Take(HomeVillageTopRandom)
                .ToList();
            if (anyCulture.Count > 0)
            {
                return anyCulture[MBRandom.RandomInt(anyCulture.Count)];
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("DisbandReturnPartyDispatcher.FindHomeVillage failed", ex);
            return null;
        }
    }

    private static bool IsRaided(Settlement v)
    {
        try { return v.IsUnderRaid; }
        catch { return false; }
    }
}
