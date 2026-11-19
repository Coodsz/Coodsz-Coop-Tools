// Coodsz Server Tools: the My Little Warband recruit cart rewrite and troop swap, on Coop's ids and plain counts
// without the game or Coop, so the launcher tests can run it (tests\BannerlordCoopLauncher.Tests links this file).
// MlwRecruit.cs is the thin Harmony side that hands it Coop's cart and the party's roster.

using System;
using System.Collections.Generic;
using System.Linq;

// Its own namespace: the tests also see Coodsz Host's CoodszServerTools class.
namespace Coodsz.ServerRules;

/// <summary>One recruit cart entry as Coop gets it: the notable's Coop id, the troop's Coop id and the slot index.</summary>
public readonly record struct MlwCartEntry(string? HeroId, string? TroopId, int Index);

/// <summary>A rewritten cart entry: its place in the cart, the slot, and the Coop ids of the slot's normal troop and the custom troop.</summary>
public readonly record struct MlwMapped(int Position, string HeroId, int Index, string NativeId, string CustomId);

/// <summary>
/// What the server knows, asked by Coop id only. Coop's ids are its object manager's keys (a type prefix, a compacted
/// form or a remapped id), so the rules never read or build one; they only pass them back here.
/// </summary>
public interface IMlwRecruitWorld
{
    /// <summary>Whether Coop's object manager resolves this cart troop id, and whether that troop is a My Little Warband custom troop.</summary>
    bool TryCartTroop(string troopId, out bool custom);

    /// <summary>How many volunteer slots the notable has, 0 when Coop does not resolve the notable.</summary>
    int SlotCount(string heroId);

    /// <summary>
    /// False when the slot is empty. Otherwise the slot troop's Coop id (null when Coop has no id for it) and whether
    /// it is a custom troop.
    /// </summary>
    bool TrySlotTroop(string heroId, int index, out string? troopId, out bool custom);

    /// <summary>Whether the cart troop is the very troop in that slot, the check Coop itself makes.</summary>
    bool SameTroop(string troopId, string heroId, int index);
}

/// <summary>The recruiting party's member roster, by Coop troop id.</summary>
public interface IMlwRoster
{
    int Count(string troopId);
    int Xp(string troopId);

    /// <summary>The game's AddToCounts: <paramref name="count"/> healthy troops and <paramref name="xpChange"/> xp on that stack.</summary>
    void Add(string troopId, int count, int xpChange);
}

/// <summary>
/// My Little Warband custom troops as CoopModPatch names them (game ids starting <c>_basic_root</c> or
/// <c>_elite_root</c>), and the two steps around Coop's HandleOnRecruitmentDone.
/// </summary>
public static class MlwRecruitRules
{
    public const string BasicRoot = "_basic_root";
    public const string EliteRoot = "_elite_root";

    /// <summary>Whether a troop's game id (its StringId, never a Coop id) is a My Little Warband custom troop.</summary>
    public static bool IsCustomId(string? stringId) =>
        stringId != null && (stringId.StartsWith(BasicRoot, StringComparison.Ordinal) || stringId.StartsWith(EliteRoot, StringComparison.Ordinal));

    /// <summary>
    /// Before Coop: the cart with each custom entry put back to its slot's normal troop, so Coop's own check (cart troop
    /// is slot troop) takes it. An entry is rewritten when Coop resolves its troop to a custom troop, the slot holds a
    /// normal troop Coop has an id for, and no earlier entry of this cart has that slot. Coop takes the first entry for
    /// a slot, so an entry Coop takes as it is claims the slot too. Every other entry stays as it came. If a slot
    /// changed after the recruit screen opened, the custom troop is recruited in place of the normal troop now in that
    /// slot, at that troop's price.
    /// </summary>
    /// <param name="unknownCustom">
    /// A hint only: true when a cart troop id Coop does not resolve has <c>_basic_root</c> or <c>_elite_root</c> in it,
    /// which most likely means the server does not have CoopModPatch's troops.
    /// </param>
    public static MlwCartEntry[] Rewrite(IReadOnlyList<MlwCartEntry> cart, IMlwRecruitWorld world, List<MlwMapped> mapped, out bool unknownCustom)
    {
        unknownCustom = false;
        var result = cart.ToArray();
        var claimed = new HashSet<(string, int)>();
        for (var i = 0; i < result.Length; i++)
        {
            var (heroId, troopId, index) = result[i];
            var custom = false;
            var known = troopId != null && world.TryCartTroop(troopId, out custom);
            if (!known && troopId != null && (troopId.Contains(BasicRoot) || troopId.Contains(EliteRoot))) unknownCustom = true;
            if (heroId is null || index < 0 || index >= world.SlotCount(heroId)) continue;
            if (!world.TrySlotTroop(heroId, index, out var slotId, out var slotCustom)) continue;

            if (known && world.SameTroop(troopId!, heroId, index))
            {
                claimed.Add((heroId, index));
                continue;
            }

            if (!custom || slotCustom || slotId is null || !claimed.Add((heroId, index))) continue;
            result[i] = new MlwCartEntry(heroId, slotId, index);
            mapped.Add(new MlwMapped(i, heroId, index, slotId, troopId!));
        }

        return result;
    }

    /// <summary>
    /// After Coop: for each mapped slot Coop really recruited (the slot is now empty and the party gained that normal
    /// troop; neither holds when Coop turned the cart down for gold), adds one custom troop and then takes one normal
    /// troop away, so a failure halfway leaves the party with the troop Coop gave it. The xp the normal stack gained in
    /// this recruit (Coop's Famous Commander perk xp) moves with each swapped troop, split evenly over the troops gained;
    /// a remainder stays on the normal stack.
    /// </summary>
    /// <param name="before">Each mapped normal troop's count and xp in the party before Coop recruited.</param>
    /// <returns>How many troops were swapped.</returns>
    public static int Swap(IReadOnlyList<MlwMapped> mapped, IReadOnlyDictionary<string, (int Count, int Xp)> before, IMlwRecruitWorld world, IMlwRoster roster)
    {
        var swapped = 0;
        foreach (var group in mapped.GroupBy(m => m.NativeId))
        {
            var native = group.Key;
            var gained = roster.Count(native) - before[native].Count;
            var emptied = group.Where(m => !world.TrySlotTroop(m.HeroId, m.Index, out _, out _)).ToList();
            var count = Math.Max(0, Math.Min(gained, emptied.Count));
            if (count == 0) continue;
            var xpEach = Math.Max(0, roster.Xp(native) - before[native].Xp) / gained;
            for (var i = 0; i < count; i++)
            {
                roster.Add(emptied[i].CustomId, 1, xpEach);
                roster.Add(native, -1, -xpEach);
                swapped++;
            }
        }

        return swapped;
    }

    /// <summary>The name of the first target that was not found, or null when every one was.</summary>
    public static string? FirstMissing(params (string Name, object? Found)[] targets)
    {
        foreach (var (name, found) in targets)
            if (found is null) return name;
        return null;
    }
}
