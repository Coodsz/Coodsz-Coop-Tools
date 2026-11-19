using System.IO;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using TaomCoop.Core;

namespace CoodszHost.Services;

/// <summary>
/// "Test mods on the server" in Coodsz Host (TaomCoop.Core.ModLoadTest runs it). For each picked mod
/// the server starts once with the core of the Working stable mods setup (the frameworks, the official
/// modules, the server host module and Coop) plus that mod and the mods it needs, on a throwaway new
/// campaign named modtest-&lt;random&gt;. The host's own saves are never loaded or written, and the
/// throwaway campaign is deleted afterwards. Only Coodsz Host has it: the AD1259 server tool builds
/// from the same source but is a locked AD1259 setup without add mods or mod lists.
/// </summary>
public static class HostModTest
{
    public const string SavePrefix = "modtest-";
    public const string ResultsFileName = "modtest-results.json";
    public const string ResultsKind = "CoodszHostModTest";
    public const string ExportFileName = "modtest-compat-mods.json";

    /// <summary>The core every test starts with, in the Working stable mods order.</summary>
    public static readonly string[] CoreIds =
    [
        "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen",
        "Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop"
    ];

    // Optional official game modes mods often list as needed; the server leaves them out.
    private static readonly HashSet<string> SoftNeeds = new(StringComparer.OrdinalIgnoreCase) { "CustomBattle", "StoryMode", "BirthAndDeath", "Multiplayer", "NavalDLC" };

    /// <summary>False for the AD1259 server tool, which has no Test mods on the server.</summary>
    public static bool Available(bool ad1259) => !ad1259;

    public static bool Offered => Available(Edition.IsAD1259);

    /// <summary>The Data folder next to the tool, where the results and the mod list file go.</summary>
    public static string DataFolder(string toolDirectory) => Path.Combine(toolDirectory, "Data");

    /// <summary>
    /// The core in load order, from the locked Working stable mods setup. CoopNightly stands in for
    /// Coop on a server that only has the old name.
    /// </summary>
    public static IReadOnlyList<string> BaseOrder(IEnumerable<string> lockedOrder, IReadOnlyCollection<string> serverInstalled)
    {
        var installed = new HashSet<string>(serverInstalled, StringComparer.OrdinalIgnoreCase);
        var order = lockedOrder.Where(id => CoreIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var id in CoreIds)
            if (!order.Contains(id, StringComparer.OrdinalIgnoreCase))
                order.Add(id);
        var coop = order.FindIndex(id => id.Equals("Coop", StringComparison.OrdinalIgnoreCase));
        if (coop >= 0 && !installed.Contains("Coop") && installed.Contains("CoopNightly"))
            order[coop] = "CoopNightly";
        return order;
    }

    /// <summary>A new throwaway campaign name, modtest- and eight letters or digits.</summary>
    public static string NewSaveName(Random? random = null) =>
        SavePrefix + (random ?? Random.Shared).Next(0, int.MaxValue).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsTestSave(string? name) =>
        name != null && name.StartsWith(SavePrefix, StringComparison.OrdinalIgnoreCase) && name.Length > SavePrefix.Length;

    /// <summary>Deletes the throwaway campaign and what the server made of it (its .json, backups and
    /// launcher files). Only files of a modtest- save are touched. Returns how many.</summary>
    public static int RemoveTestSave(string savesDirectory, string saveName)
    {
        if (!IsTestSave(saveName) || !Directory.Exists(savesDirectory))
            return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(savesDirectory, saveName + ".*"))
        {
            if (!Path.GetFileName(file).StartsWith(saveName + ".", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Delete(file);
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// The mods to offer: Workshop mods and Modules folder mods on this PC that are not in the locked
    /// working list (and are not official modules, Coop, or DedicatedServer.* host modules). Each one
    /// carries the mods it needs (DependedModules in its SubModule.xml) that are on this PC or already
    /// on the server, so the test is about that one mod.
    /// </summary>
    public static IReadOnlyList<ModTestCandidate> Candidates(
        IReadOnlyList<FoundServerMod> onThisPc,
        IEnumerable<string> lockedOrder,
        IReadOnlyList<ModuleRow> onServer,
        Func<string, ModuleManifest?>? readManifest = null)
    {
        readManifest ??= ModuleManifestReader.Read;
        var working = new HashSet<string>(lockedOrder, StringComparer.OrdinalIgnoreCase);
        var picked = new List<ModTestCandidate>();
        foreach (var mod in onThisPc)
        {
            if (!Offerable(mod.ModuleId, working) || picked.Any(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
                continue;
            var manifest = SafeRead(readManifest, mod.Directory);
            if (IsOverhaulPiece(mod.ModuleId) || IsOverhaulPiece(mod.Name))
                continue;
            var needs = new List<TaomExtraMod>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mod.ModuleId };
            var needsOverhaul = false;
            void Walk(ModuleManifest? of)
            {
                if (of == null)
                    return;
                foreach (var id in of.RequiredIds)
                {
                    if (IsOverhaulPiece(id))
                        needsOverhaul = true;
                    if (!seen.Add(id) || SoftNeeds.Contains(id) || CoreIds.Contains(id, StringComparer.OrdinalIgnoreCase) || CoopJoinRules.IsIgnored(id))
                        continue;
                    var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                    var server = onServer.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (found == null && server == null)
                        continue;
                    if (found != null)
                        Walk(SafeRead(readManifest, found.Directory));
                    else
                        Walk(new ModuleManifest(server!.Id, server.Name, server.Version, server.Dependencies.Select(dep => new ModuleLink(dep, null, false)).ToList(), [], [], []));
                    needs.Add(new TaomExtraMod { Name = found?.Name ?? server!.Name, ModuleId = found?.ModuleId ?? server!.Id, WorkshopId = found == null ? "0" : WorkshopIdOf(found) });
                }
            }

            Walk(manifest);
            // A submod or patch of a total overhaul (TAOM, AD1259) only runs with that overhaul, which a
            // Coodsz Host server never loads, so testing it alone tells nothing.
            if (needsOverhaul)
                continue;
            picked.Add(new ModTestCandidate(mod.ModuleId, mod.Name, WorkshopIdOf(mod), manifest?.Version ?? "", needs));
        }

        return picked.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Total overhaul modules and their pieces: TAOM and its parts (TAOM_*, TAOM.*, the TAOM
    // standalone compat patches, LOTRLOME_Armory), AD1259 itself, and mods named for TAOM such as
    // AIInfluence-TAOM.
    private static readonly HashSet<string> OverhaulModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "TAOM", "TAOM_Map", "TAOM.Dependencies", "TAOM.CoopCompat", "LOTRLOME_Armory", "AD1259"
    };

    private static readonly System.Text.RegularExpressions.Regex TaomWord =
        new(@"(^|[^A-Za-z])TAOM([^A-Za-z]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool IsOverhaulPiece(string? moduleIdOrName)
    {
        var id = (moduleIdOrName ?? "").Trim();
        return id.Length > 0 && (TotalOverhaul.Is(id) || OverhaulModules.Contains(id) || TaomWord.IsMatch(id));
    }

    private static bool Offerable(string moduleId, HashSet<string> working) =>
        moduleId.Length > 0
        && !working.Contains(moduleId)
        && !CoopJoinRules.IsIgnored(moduleId)
        && !moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
        && !moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)
        && !moduleId.Equals("CoopMarriage", StringComparison.OrdinalIgnoreCase)
        && !moduleId.Equals("FamilyAppearanceEditor", StringComparison.OrdinalIgnoreCase);

    private static ModuleManifest? SafeRead(Func<string, ModuleManifest?> read, string directory)
    {
        try
        {
            return read(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The Workshop item id from a Workshop folder (...\content\261550\&lt;id&gt;\...), else "0".</summary>
    public static string WorkshopIdOf(FoundServerMod mod)
    {
        var parts = mod.Directory.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.FindIndex(parts, part => part == "261550");
        return at >= 0 && at + 1 < parts.Length && parts[at + 1].All(char.IsAsciiDigit) ? parts[at + 1] : "0";
    }

    /// <summary>Known broken in the community list, or a total overhaul: the window leaves it unticked.</summary>
    public static bool LeaveUnticked(ModCompatDatabase list, ModTestCandidate mod) =>
        TotalOverhaul.Is(mod.ModuleId) || list.Find(mod.ModuleId, mod.WorkshopId)?.Status == CommunityStatus.Broken;

    /// <summary>The Community list badge of a row: the same lookup as the Modules page, by module id and
    /// then by the Workshop item id, so a list entry with a guessed id still matches.</summary>
    public static ModCompatBadge? CommunityBadge(ModCompatDatabase list, ModTestCandidate mod) =>
        CommunityCompat.Badge(mod.ModuleId, list, workshopId: mod.WorkshopId);

    /// <summary>
    /// The server module list for one test: the core in its order, then the mods the tested mod needs
    /// and the mod itself, sorted so every mod comes after the mods it needs (ModuleCatalog.AutoOrder).
    /// Every other module in the server Modules folder follows, turned off, so the server does not
    /// load it. Missing is what the test needs but the server Modules folder does not have.
    /// </summary>
    public static (List<ModuleRow> Rows, IReadOnlyList<string> Missing) TestRows(IReadOnlyList<ModuleRow> installed, IReadOnlyList<string> baseOrder, ModTestCandidate mod)
    {
        var wanted = baseOrder.Concat(mod.Needs.Select(need => need.ModuleId)).Append(mod.ModuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var byId = installed.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var missing = wanted.Where(id => !byId.ContainsKey(id)).ToList();
        var chosen = wanted.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var rows = new List<ModuleRow>();
        foreach (var row in ModuleCatalog.AutoOrder(chosen))
        {
            row.Enabled = true;
            rows.Add(row);
        }

        foreach (var row in installed)
        {
            if (rows.Any(item => item.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            row.Enabled = false;
            rows.Add(row);
        }

        return (rows, missing);
    }

    /// <summary>Why a test server closed before serving, from the log (ServerCrashExplain), or null.</summary>
    public static string? ExitReason(string log, int? exitCode, ModTestCandidate mod, IReadOnlyCollection<string> workingIds)
    {
        var extras = mod.Needs.Select(need => new ServerCrashExplain.ModName(need.ModuleId, need.Label))
            .Append(new ServerCrashExplain.ModName(mod.ModuleId, mod.Label)).ToList();
        var exitLine = exitCode is { } code ? "Server process exited with code " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : null;
        var lines = ServerCrashExplain.Explain(exitLine, wasServing: false, log, null, extras, workingIds);
        var happened = lines.FirstOrDefault(line => line.StartsWith("What happened", StringComparison.OrdinalIgnoreCase));
        if (happened == null)
            return null;
        var text = happened["What happened".Length..].TrimStart(' ', ':').Trim();
        return text.Length > 0 ? text : null;
    }

    /// <summary>The Coodsz Host flavor of the load test: its start line, exit reasons and stamp.</summary>
    public static ModTestFlavor Flavor(string coopVersion, IReadOnlyCollection<string> workingIds, Func<ModTestCandidate?> current) => new(
        mod => "Starting the server with the working list core and " + mod.Label + ".",
        (log, code) => current() is { } mod ? ExitReason(log, code, mod, workingIds) : null,
        result => result.Base = coopVersion.Length > 0 ? "Coop " + coopVersion : null);

    /// <summary>The passes the mod list file is written from.</summary>
    public static IReadOnlyList<ServerLoadPass> Passes(IEnumerable<ModTestResult> results) =>
        results.Where(result => result.Passed)
            .Select(result => new ServerLoadPass(result.Id, result.Name, result.WorkshopId, result.Version, result.Date))
            .ToList();
}
