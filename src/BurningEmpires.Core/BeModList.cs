using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// What the client launcher's Mod list window shows: the Burning Empires compatibility list in four
/// groups (tested in Coop, loads on the server, works in Burning Empires but not tested in Coop, risky
/// and broken), filtered by a search box, with a Workshop or Nexus link per mod and the mod to add to a
/// setup. The groups are the TAOM CompatGroup values; WorksInTaom is the works in Burning Empires group.
/// </summary>
public static class BeModList
{
    public const string WorkshopPage = TaomModList.WorkshopPage;
    public const string NexusPage = TaomModList.NexusPage;

    public static IReadOnlyList<CompatGroup> Order { get; } =
        [CompatGroup.TestedInCoop, CompatGroup.LoadsOnServer, CompatGroup.WorksInTaom, CompatGroup.RiskyOrBroken];

    public static string Title(CompatGroup group) => group switch
    {
        CompatGroup.TestedInCoop => "Tested in Coop",
        CompatGroup.LoadsOnServer => "Loads on the server",
        CompatGroup.WorksInTaom => "Works in Burning Empires, not tested in Coop",
        _ => "Risky and Broken"
    };

    /// <summary>Risky and broken first (whoever says so), then by source.</summary>
    public static CompatGroup GroupOf(CompatEntry entry) => TaomModList.GroupOf(entry);

    /// <summary>True when every word of the search is in the mod's name, id, note, source or Workshop id.</summary>
    public static bool Matches(CompatEntry entry, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var text = string.Join(" ", entry.Name, entry.Id, entry.Note, BeCompatSource.Label(entry.Source), entry.WorkshopId, entry.NexusId,
            BeCompatList.BadgeText(entry.Kind) ?? "");
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The groups in window order, each sorted by name. Empty groups are kept so the window
    /// can say a group has nothing for this search.</summary>
    public static IReadOnlyList<(CompatGroup Group, IReadOnlyList<CompatEntry> Mods)> Groups(BeCompatList list, string? search = null) =>
        Order.Select(group => (group, (IReadOnlyList<CompatEntry>)list.Mods
                .Where(entry => GroupOf(entry) == group && Matches(entry, search))
                .OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .ToList();

    /// <summary>The Steam Workshop page, else the Nexus Mods page, else null.</summary>
    public static string? Link(CompatEntry entry) => TaomModList.Link(entry);

    public static string? LinkText(CompatEntry entry) => TaomModList.LinkText(entry);

    /// <summary>The copy of this mod on this PC: by module id, then Workshop id, then name.</summary>
    public static FoundMod? OnThisPc(CompatEntry entry, IEnumerable<FoundMod> mods) => TaomModList.OnThisPc(entry, mods);

    /// <summary>
    /// What Add to setup puts on the setup: the copy on this PC when there is one, else the list's
    /// module id with its Workshop id (Install mods then downloads it). Null with a reason when the
    /// mod cannot be added yet: the list does not know its module id and it is not on this PC.
    /// </summary>
    public static (string Name, string ModuleId, string WorkshopId)? AddTarget(CompatEntry entry, FoundMod? found, out string? reason) =>
        TaomModList.AddTarget(entry, found, out reason);

    /// <summary>The line Add to setup shows for a locked setup.</summary>
    public static string LockedText(string setupName) => TaomModList.LockedText(setupName);
}
