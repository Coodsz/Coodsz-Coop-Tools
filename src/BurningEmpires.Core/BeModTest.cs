using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// The Burning Empires server tool's side of the automatic server load test. Running a test is
/// ModLoadTest.RunOneAsync and RunAllAsync with the Burning Empires flavor and SetupFor below; the
/// candidates, the results file kind and the export use the Burning Empires core and mod list.
/// </summary>
public static class BeModTest
{
    public const string ExportFileName = "modtest-be-mod-compat.json";
    public const string ResultsKind = "CoodszBeModTest";
    public const string ResultsFileName = ModLoadTest.ResultsFileName;
    public const string SetupName = ModLoadTest.SetupName;

    /// <summary>What a result is stamped with: the Burning Empires version under the core.</summary>
    public const string Base = "Burning Empires " + BeStack.BeVersionText;

    /// <summary>The Burning Empires flavor of the load test. explainExit gives the reason for a server
    /// that closed before serving (the host report); without it the exit code is the reason.</summary>
    public static ModTestFlavor Flavor(Func<string, int?, string?>? explainExit = null) => new(
        mod => "Starting the server with the Burning Empires core and " + mod.Label + ".",
        explainExit ?? ((_, _) => null),
        result => result.Base = Base);

    /// <summary>
    /// The mods ticked when the window opens: each installed mod the list knows that Coodsz has not
    /// tested in Coop. Workshop mods are matched by module id, Workshop id or name; a mod in the game's
    /// Modules folder only by its module id. Each carries the extra mods on this PC it needs.
    /// </summary>
    public static IReadOnlyList<ModTestCandidate> DefaultCandidates(BeCompatList list, IReadOnlyList<FoundMod> onThisPc, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        var picked = new List<ModTestCandidate>();
        foreach (var mod in onThisPc)
        {
            if (BeLoadOrder.RefusalReason(mod.ModuleId) != null)
                continue;
            CompatEntry? entry;
            if (SharedModList.IsWorkshopId(mod.WorkshopId))
                entry = list.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
            else if (!mod.Source.Equals("Workshop", StringComparison.OrdinalIgnoreCase))
                entry = list.Find(mod.ModuleId);
            else
                continue;
            if (entry == null || entry.Source.Equals(CompatSource.CoodszCoop, StringComparison.OrdinalIgnoreCase))
                continue;
            if (picked.Any(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
                continue;
            picked.Add(Candidate(mod, onThisPc, manifests));
        }

        return picked.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True for a mod the list says is broken; the window leaves those unticked.</summary>
    public static bool KnownBroken(BeCompatList list, ModTestCandidate mod) =>
        list.Find(mod.ModuleId, mod.WorkshopId, mod.Name) is { Kind: CompatTag.Broken };

    public static ModTestCandidate Candidate(FoundMod mod, IReadOnlyList<FoundMod> onThisPc, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        manifests ??= new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase);
        var manifest = manifests.TryGetValue(mod.ModuleId, out var known) ? known : ModuleManifestReader.Read(mod.Directory);
        var needs = new List<TaomExtraMod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mod.ModuleId };
        void Walk(ModuleManifest? of)
        {
            if (of == null)
                return;
            foreach (var id in of.RequiredIds)
            {
                if (!seen.Add(id) || BeLoadOrder.RefusalReason(id) != null)
                    continue;
                var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                    continue;
                Walk(manifests.TryGetValue(found.ModuleId, out var next) ? next : ModuleManifestReader.Read(found.Directory));
                needs.Add(new TaomExtraMod { Name = found.Name, ModuleId = found.ModuleId, WorkshopId = found.WorkshopId, Enabled = true, Slot = BeLoadOrder.SlotAfterBe });
            }
        }

        Walk(manifest);
        return new ModTestCandidate(mod.ModuleId, mod.Name, mod.WorkshopId, manifest?.Version ?? "", needs);
    }

    /// <summary>The setup a test starts: the Burning Empires core, the mods this one needs, then this
    /// mod, in a good order (BeAutoSort).</summary>
    public static TaomSetup SetupFor(ModTestCandidate mod, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        var setup = new TaomSetup { Name = SetupName };
        foreach (var need in mod.Needs)
            setup.Extras.Add(need.Copy());
        setup.Extras.Add(new TaomExtraMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId, Enabled = true, Slot = BeLoadOrder.SlotAfterBe });
        BeLoadOrder.Normalize(setup);
        if (manifests != null)
            BeAutoSort.Sort(setup, manifests);
        return setup;
    }

    /// <summary>Adds a result to the results file, with the Burning Empires kind.</summary>
    public static List<ModTestResult> Record(string path, ModTestResult result) => ModLoadTest.Record(path, result, ResultsKind);

    /// <summary>
    /// The Burning Empires mod list with the passed mods written in as source server-loads, with the
    /// same rules as the TAOM export (ModLoadTest.MergePassed): mods Coodsz tested in Coop are left as
    /// they are, a reported or unknown tag becomes loads, and failed results change nothing.
    /// </summary>
    public static BeCompatList MergePassed(BeCompatList current, IEnumerable<ModTestResult> results, string today)
    {
        var merged = ModLoadTest.MergePassed(AsTaomList(current, TaomCompatList.Kind), results, today);
        var list = BeCompatList.Parse(Serialize(current));
        list.SchemaVersion = merged.SchemaVersion;
        list.Updated = merged.Updated;
        list.Mods = merged.Mods;
        return list;
    }

    /// <summary>The list as the repository keeps it: one line per mod, CRLF line ends.</summary>
    public static string Serialize(BeCompatList list)
    {
        var text = ModLoadTest.Serialize(AsTaomList(list));
        // The TAOM writer names the overhaul version "taom"; this list calls it "be".
        return text.Replace("\r\n  \"taom\": ", "\r\n  \"be\": ");
    }

    // kind is the file kind to write; MergePassed reads its copy back as a TAOM list, so it gets the TAOM kind.
    private static TaomCompatList AsTaomList(BeCompatList list, string? kind = null) => new()
    {
        FileKind = kind ?? list.FileKind,
        SchemaVersion = list.SchemaVersion,
        Taom = list.Be,
        Game = list.Game,
        Coop = list.Coop,
        Updated = list.Updated,
        Mods = list.Mods
    };
}
