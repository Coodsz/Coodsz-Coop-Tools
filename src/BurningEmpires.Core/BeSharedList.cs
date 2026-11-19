using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// Share mod list and Import mod list for Burning Empires setups, with the same coodsz-mod-list.json
/// file Coop and Friends, Coodsz Host and the TAOM tools use (SharedModList). The file lists the full
/// load order, the Burning Empires core included, so the other side can check its core versions too.
/// A list made for TAOM is refused: its core is another overhaul.
/// </summary>
public static class BeSharedList
{
    /// <summary>The entries to write for a setup, in load order. describe gives a module's name,
    /// version and folder when it is on this PC.</summary>
    public static IReadOnlyList<SharedModEntry> Entries(TaomSetup setup, Func<string, (string Name, string Version, string? Directory)?> describe)
    {
        var extras = BeLoadOrder.ExtrasById(setup);
        var list = new List<SharedModEntry>();
        foreach (var id in BeLoadOrder.ClientOrder(setup))
        {
            var found = describe(id);
            extras.TryGetValue(id, out var extra);
            var butr = BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            var name = extra?.Label ?? found?.Name ?? (id == BeStack.CoopModuleId ? "Bannerlord Coop" : butr?.Name ?? id);
            var workshop = id == BeStack.CoopModuleId ? BeStack.CoopWorkshopId
                : butr != null ? butr.WorkshopId
                : SharedModList.WorkshopIdFor(extra?.WorkshopId, found?.Directory);
            list.Add(new SharedModEntry(id, name, found?.Version ?? "", workshop));
        }

        return list;
    }

    /// <summary>Why this list cannot be imported on a Burning Empires setup, or null when it can. A
    /// list with a TAOM core module is a TAOM list.</summary>
    public static string? ForeignGame(IReadOnlyList<SharedModEntry> shared)
    {
        var taom = shared.FirstOrDefault(entry => BeLoadOrder.IsTaomCore(entry.Id));
        return taom == null
            ? null
            : "This mod list is for TAOM (it has " + taom.Id.Trim() + "), not Burning Empires. Ask the host for the list from the Burning Empires tools.";
    }

    /// <summary>
    /// Matches a setup to a shared list. Extra mods on the list are added (a mod not on this PC
    /// needs a Workshop link), turned on and put in the list's order; an extra that is listed before
    /// Coop loads before Coop. Extras on this setup that the list does not have are turned off, not
    /// removed. Core modules are never added or moved. The setup must be unlocked, and a TAOM list is
    /// refused before anything changes. A list from a Plain Burning Empires setup of the first tools
    /// has no Elephants; the core loads it anyway, so that is not reported as missing.
    /// </summary>
    public static TaomImportResult Import(TaomSetup setup, IReadOnlyList<SharedModEntry> shared, IReadOnlyList<FoundMod> onThisPc)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Make a new setup for the list.");
        var foreign = ForeignGame(shared);
        if (foreign != null)
            throw new InvalidOperationException(foreign);

        var before = setup.Clone();
        var added = new List<string>();
        var turnedOn = new List<string>();
        var turnedOff = new List<string>();
        var refused = new List<string>();
        var noLink = new List<string>();
        var coopAt = -1;
        for (var i = 0; i < shared.Count; i++)
        {
            if (shared[i].Id.Trim().Equals(BeLoadOrder.FirstBeModule, StringComparison.OrdinalIgnoreCase))
            {
                coopAt = i;
                break;
            }
        }

        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wantedBefore = new List<TaomExtraMod>();
        var wantedAfter = new List<TaomExtraMod>();
        for (var i = 0; i < shared.Count; i++)
        {
            var entry = shared[i];
            var id = entry.Id.Trim();
            if (BeLoadOrder.IsCore(id) || !listed.Add(id))
                continue;
            var label = string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name.Trim();
            // Client add-ons such as the join password module come with the launcher.
            if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
                continue;
            if (BeLoadOrder.RefusalReason(id) != null)
            {
                refused.Add(label);
                continue;
            }

            var mod = setup.Extras.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
            {
                var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                var workshop = found != null && SharedModList.IsWorkshopId(found.WorkshopId)
                    ? found.WorkshopId
                    : SharedModList.IsWorkshopId(entry.WorkshopId) ? entry.WorkshopId.Trim() : "0";
                if (found == null && workshop == "0")
                {
                    noLink.Add(label);
                    continue;
                }

                mod = new TaomExtraMod { Name = found?.Name ?? label, ModuleId = id, WorkshopId = workshop, Enabled = true };
                added.Add(mod.Label);
            }
            else if (!mod.Enabled)
            {
                mod.Enabled = true;
                turnedOn.Add(mod.Label);
            }

            mod.Slot = coopAt >= 0 && i < coopAt ? BeLoadOrder.SlotBeforeCoop : BeLoadOrder.SlotAfterBe;
            (mod.Slot == BeLoadOrder.SlotBeforeCoop ? wantedBefore : wantedAfter).Add(mod);
        }

        var others = setup.Extras.Where(mod => !listed.Contains(mod.ModuleId)).ToList();
        foreach (var mod in others.Where(mod => mod.Enabled))
        {
            mod.Enabled = false;
            turnedOff.Add(mod.Label);
        }

        setup.Extras = wantedBefore
            .Concat(others.Where(mod => mod.Slot == BeLoadOrder.SlotBeforeCoop))
            .Concat(wantedAfter)
            .Concat(others.Where(mod => mod.Slot != BeLoadOrder.SlotBeforeCoop))
            .ToList();
        BeLoadOrder.Normalize(setup);

        var missingCore = BeStack.ClientModules
            .Where(id => !BeLoadOrder.IsElephants(id))
            .Where(id => !shared.Any(entry => entry.Id.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new TaomImportResult(added, turnedOn, turnedOff, refused, noLink, missingCore, !before.SameMods(setup));
    }

    /// <summary>Enabled extras that are not on this PC. A Workshop one can be downloaded.</summary>
    public static IReadOnlyList<TaomExtraMod> Missing(TaomSetup setup, IEnumerable<string> installedIds) =>
        TaomSharedList.Missing(setup, installedIds);

    /// <summary>
    /// The extras the server needs that are not on this PC, for the Connect prompt: a Workshop mod is
    /// subscribed through Steam, one that comes with the launcher folder is copied, and the rest are
    /// got by hand. The Burning Empires core is not here; Install or repair Burning Empires puts it in.
    /// </summary>
    public static IReadOnlyList<ServerModNeed> ServerNeeds(TaomSetup setup, IEnumerable<string> installedIds, Func<string, bool> bundled) =>
        TaomSharedList.ServerNeeds(setup, installedIds, bundled);

    /// <summary>The Workshop items Steam is asked for when these BUTR modules are missing. MCM is never one:
    /// its official Workshop item was removed and it comes from Nexus Mods.</summary>
    public static Dictionary<ulong, string> ButrDownloads(IEnumerable<ButrModule> missing)
    {
        var names = new Dictionary<ulong, string>();
        foreach (var mod in missing.Where(mod => !mod.FromNexus))
        {
            if (ulong.TryParse(mod.WorkshopId, out var id))
                names.TryAdd(id, mod.Name);
        }

        return names;
    }

    /// <summary>The version line of a missing BUTR core row on the client.</summary>
    public static string ButrMissingText(ButrModule mod) => mod.FromNexus
        ? "missing. Get it from Nexus Mods and put it in the game's Modules folder."
        : "missing. Connect gets it from the Steam Workshop.";

    /// <summary>A missing BUTR module for the Connect prompt: Steam gets a Workshop one, MCM is got by hand
    /// from its Nexus Mods page (its official Workshop item was removed).</summary>
    public static ServerModNeed ButrNeed(ButrModule mod) => mod.FromNexus
        ? new ServerModNeed(mod.ModuleId, mod.Name, ServerModProblem.Missing, ServerModFetch.ByHand, mod.NexusPage)
        : new ServerModNeed(mod.ModuleId, mod.Name, ServerModProblem.Missing, ServerModFetch.Workshop, ServerModNeeds.WorkshopPage(mod.WorkshopId));
}
