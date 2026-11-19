using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using Microsoft.Win32;

namespace CoodszHost.Services;

public sealed record FoundServerMod(string Name, string ModuleId, string Directory, string Source);

/// <summary>
/// A server copy with the right version that lacks files the good copy on this PC (Source) has, and the lines Start
/// writes about copying it again. The lines name the first missing files, so a copy that keeps coming back shows in reports.
/// </summary>
public sealed record ShortServerCopy(string Id, string Name, string Source, IReadOnlyList<string> Missing)
{
    public static string Example(IEnumerable<string> paths) => string.Join(", ", paths.Take(3));

    /// <summary>The line before a first copy (a mod missing on the server or at the wrong version).</summary>
    public static string CopyingNew(string name) =>
        "Copying " + name + " into the server. This can take a few minutes, so leave the tool open.";

    public string Copying =>
        "Copying " + Name + " into the server again, because the server copy is missing files. This can take a few minutes, so leave the tool open.";

    public string Copied =>
        "Copied " + Name + " into the server again from " + Source + ", because the server copy was missing files (for example " + Example(Missing) + ").";

    public string StillShort(IReadOnlyList<string> still) =>
        Name + " still lacks files on the server after a new copy (for example " + Example(still) + "). It is not copied again until you close and open the tool.";

    public string Failed(string reason) =>
        "Could not copy " + Name + " into the server again (" + reason.Trim().TrimEnd('.') + "). It is not tried again until you close and open the tool.";

    public static string CheckFailed(string reason) =>
        "Could not compare the map mod's server copy with the copy on this PC, so it was not copied again (" + reason.Trim().TrimEnd('.') + ").";
}

public static class ExtraModScan
{
    private static readonly HashSet<string> SkipIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Coop", "CoopNightly", "CoopMarriage", "FamilyAppearanceEditor",
        "Native", "SandBoxCore", "SandBox", "StoryMode", "CustomBattle",
        "BirthAndDeath", "Multiplayer", "NavalDLC", "DedicatedCustomServer", "DedicatedServer.Windows"
    };

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "engine", "McmProfiles"
    };

    public static IReadOnlyList<FoundServerMod> Scan(string toolDirectory, IEnumerable<string> alreadyInstalled, string? bundledDirectory = null)
    {
        var installed = new HashSet<string>(alreadyInstalled, StringComparer.OrdinalIgnoreCase);
        var found = new List<FoundServerMod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in LibraryPaths())
        {
            var content = Path.Combine(library, "steamapps", "workshop", "content", "261550");
            Collect(content, workshop: true, "Workshop", installed, found, seen);
        }

        var gameModules = GameModulesDirectory();
        if (!string.IsNullOrWhiteSpace(gameModules))
            Collect(gameModules, workshop: false, "From Nexus or ModDB  —  auto detected in your module folder", installed, found, seen);
        // A bundled mod an older version left beside the tool is the one the tool carries, so it is listed once.
        Collect(toolDirectory, workshop: false, "From Nexus or ModDB", installed, found, seen, bundledDirectory);
        if (!string.IsNullOrWhiteSpace(bundledDirectory))
            Collect(bundledDirectory, workshop: false, "From Nexus or ModDB", installed, found, seen);
        return found.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <param name="mapProblems">Gets a line for a map mod that landed without its SceneObj\Main_map\scene.xscene.</param>
    /// <param name="runCopy">Runs each copy (ExtraModScan.RunCopy); a whole AD1259 copy takes minutes.</param>
    public static (IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) InstallLockedSetup(string serverRoot, List<MapCopyProblem>? mapProblems = null,
        Action<string, Action>? runCopy = null)
    {
        var sources = new List<(string Root, bool Workshop)>();
        foreach (var library in LibraryPaths())
        {
            var content = Path.Combine(library, "steamapps", "workshop", "content", "261550");
            if (Directory.Exists(content))
                sources.Add((content, true));
        }

        var game = GameModulesDirectory();
        if (!string.IsNullOrWhiteSpace(game))
            sources.Add((game, false));
        var order = Edition.IsAD1259 ? StableCoop.Order : ServerModuleSet.WorkingOrder(includeMoreNations: true);
        var result = CopyMissing(serverRoot, order, sources, mapProblems, runCopy);
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        if (Directory.Exists(modulesRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
            {
                var name = Path.GetFileName(directory);
                if (name.Equals("Native", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SandBox", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SandBoxCore", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase))
                    continue;
                StripEditorCaches(directory);
            }
        }

        return result;
    }

    /// <summary>
    /// Copies the mods the server lacks. Each copied entry names the folder it came from, so reports show where a
    /// copy came from. A locked setup's map mod (LockedMaps) that lands without its map scene gets a line in mapProblems.
    /// </summary>
    public static (IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) CopyMissing(
        string serverRoot,
        IEnumerable<string> moduleIds,
        IEnumerable<(string Root, bool Workshop)> sources,
        List<MapCopyProblem>? mapProblems = null,
        Action<string, Action>? runCopy = null)
    {
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        var present = ReadInstalledIds(modulesRoot);
        var available = new Dictionary<string, (string Directory, string Name, bool Workshop)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            IndexModules(source.Root, source.Workshop, modulesRoot, available);

        var copied = new List<string>();
        var missing = new List<string>();
        foreach (var id in moduleIds)
        {
            if (StableCoop.AlreadyPresent(id, present))
                continue;
            if (!TryResolve(id, available, out var found))
            {
                missing.Add(id);
                continue;
            }

            var destination = Path.Combine(modulesRoot, found.Id);
            if (ClientOverwriteReason(found.Directory, destination, found.Id) != null)
                continue;

            RunCopy(runCopy, found.Name, () => Install(found.Directory, serverRoot, found.Id));
            present.Add(found.Id);
            copied.Add(found.Name + " (from " + found.Directory + ")");
            if (mapProblems != null && LockedMaps.Contains(found.Id) && !ServerModuleInstall.HasMapScene(destination))
                mapProblems.Add(new MapCopyProblem(found.Id, MissingCampaignMap.CopiedWithoutMap(found.Name, found.Directory)));
        }

        return (copied, missing);
    }

    /// <summary>The map mods of the locked setups: AD1259 and More Nations.</summary>
    public static readonly IReadOnlySet<string> LockedMaps =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AD1259Server.CampaignMap, StableCoop.MoreNationsMap };

    // Steam updates the Workshop copy of a mod, but the dedicated server keeps
    // its own copy. Players then load the new version and the server the old
    // one, and the join is refused. A server copy whose version differs from the
    // Workshop copy is replaced; the old folder goes to removed-mods.
    public static IReadOnlyList<string> UpdateFromWorkshop(string serverRoot, IEnumerable<string> moduleIds, IReadOnlyDictionary<string, string>? pins = null) =>
        UpdateFromWorkshop(serverRoot, moduleIds, WorkshopContentRoots(), pins);

    /// <param name="pins">A locked pack's exact versions (AD1259 pins MCM): only a copy of that version is taken.</param>
    public static IReadOnlyList<string> UpdateFromWorkshop(string serverRoot, IEnumerable<string> moduleIds, IEnumerable<string> workshopRoots, IReadOnlyDictionary<string, string>? pins = null)
    {
        var updated = new List<string>();
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        if (!Directory.Exists(modulesRoot))
            return updated;

        var available = new Dictionary<string, (string Directory, string Name, bool Workshop)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in workshopRoots)
            IndexModules(root, true, modulesRoot, available);
        var serverCopies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
        {
            if (TryReadId(Path.Combine(directory, "SubModule.xml"), out var id, out _) && !serverCopies.ContainsKey(id))
                serverCopies[id] = directory;
        }

        foreach (var id in moduleIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (NeverReplaced.Contains(id) || IsBannerlordCoop(id))
                continue;
            if (!serverCopies.TryGetValue(id, out var serverCopy) || !available.TryGetValue(id, out var workshop))
                continue;
            var serverVersion = ReadVersion(serverCopy);
            var workshopVersion = ReadVersion(workshop.Directory);
            if (workshopVersion.Length == 0 || string.Equals(serverVersion, workshopVersion, StringComparison.OrdinalIgnoreCase))
                continue;
            if (NewerOnly.Contains(id) && !IsNewer(workshopVersion, serverVersion))
                continue;
            if (!MatchesPin(pins, id, workshopVersion))
                continue;
            // A newer MCM replaces an old server build too: that old build is what stops the server.
            if (HasServerBin(serverCopy) && !HasServerBin(workshop.Directory) && !NewerOnly.Contains(id))
                continue;

            updated.Add(Replace(serverRoot, id, serverCopy, serverVersion, workshop.Directory, workshop.Name, workshopVersion));
        }

        return updated;
    }

    // Refresh on the Modules page: Steam copies replace a server copy whose
    // version differs (same as Start), and a Nexus/ModDB copy in the game's
    // Modules folder replaces it only when that copy is newer.
    public static IReadOnlyList<string> UpdateFromNewerCopies(string serverRoot, IEnumerable<string> moduleIds) =>
        UpdateFromNewerCopies(serverRoot, moduleIds, WorkshopContentRoots(), GameModulesRoots());

    public static IReadOnlyList<string> UpdateFromNewerCopies(string serverRoot, IEnumerable<string> moduleIds, IEnumerable<string> workshopRoots, IEnumerable<string> gameRoots) =>
        UpdateFromNewerCopies(serverRoot, moduleIds, workshopRoots, gameRoots, pins: null);

    // pins: a module id whose game folder copy is only taken at exactly this version (a locked pack like AD1259).
    private static IReadOnlyList<string> UpdateFromNewerCopies(string serverRoot, IEnumerable<string> moduleIds, IEnumerable<string> workshopRoots, IEnumerable<string> gameRoots,
        IReadOnlyDictionary<string, string>? pins)
    {
        var ids = moduleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var updated = UpdateFromWorkshop(serverRoot, ids, workshopRoots, pins).ToList();
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        if (!Directory.Exists(modulesRoot))
            return updated;

        var available = new Dictionary<string, (string Directory, string Name, bool Workshop)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in gameRoots)
            IndexModules(root, false, modulesRoot, available);
        var serverCopies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
        {
            if (TryReadId(Path.Combine(directory, "SubModule.xml"), out var id, out _) && !serverCopies.ContainsKey(id))
                serverCopies[id] = directory;
        }

        foreach (var id in ids)
        {
            if (NeverReplaced.Contains(id) || IsBannerlordCoop(id))
                continue;
            if (!serverCopies.TryGetValue(id, out var serverCopy) || !available.TryGetValue(id, out var local))
                continue;
            var serverVersion = ReadVersion(serverCopy);
            var localVersion = ReadVersion(local.Directory);
            if (!IsNewer(localVersion, serverVersion))
                continue;
            if (!MatchesPin(pins, id, localVersion))
                continue;
            if (HasServerBin(serverCopy) && !HasServerBin(local.Directory) && !NewerOnly.Contains(id))
                continue;

            updated.Add(Replace(serverRoot, id, serverCopy, serverVersion, local.Directory, local.Name, localVersion));
        }

        return updated;
    }

    // The server copy goes to removed-mods and the new copy is installed. When the install fails, the old copy
    // is put back, so the server never ends up without the mod.
    private static string Replace(string serverRoot, string id, string serverCopy, string serverVersion, string source, string name, string newVersion)
    {
        string? backup = null;
        try
        {
            var backupRoot = Path.Combine(serverRoot, ModuleRemoval.RemovedFolderName);
            Directory.CreateDirectory(backupRoot);
            backup = Path.Combine(backupRoot, id + "-" + SafeName(serverVersion) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.Move(serverCopy, backup);
            Install(source, serverRoot, id);
            return name + " " + serverVersion + " -> " + newVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            try
            {
                if (backup != null && Directory.Exists(backup))
                {
                    // An install that failed part way leaves a half copied folder; it goes before the old copy comes back.
                    if (Directory.Exists(serverCopy))
                        Directory.Delete(serverCopy, recursive: true);
                    Directory.Move(backup, serverCopy);
                }
            }
            catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
            {
            }

            return name + " could not be updated (" + ex.Message + ")";
        }
    }

    // A pinned id only takes its exact version, read the same with or without a leading v and trailing .0 parts.
    private static bool MatchesPin(IReadOnlyDictionary<string, string>? pins, string id, string version) =>
        pins == null || !pins.TryGetValue(id, out var pin) || SameVersion(version, pin);

    internal static bool SameVersion(string a, string b)
    {
        static string Normal(string text)
        {
            var parts = text.Trim().TrimStart('v', 'V').Split('.').ToList();
            while (parts.Count > 1 && parts[^1] == "0")
                parts.RemoveAt(parts.Count - 1);
            return string.Join(".", parts);
        }

        return Normal(a).Equals(Normal(b), StringComparison.OrdinalIgnoreCase);
    }

    // "v1.1.4" is newer than "v1.1.3". Versions that can't be read never count as newer.
    internal static bool IsNewer(string candidate, string current)
    {
        static Version? Parse(string text)
        {
            var match = Regex.Match(text ?? "", @"\d+(\.\d+){0,3}");
            return match.Success && Version.TryParse(match.Value.Contains('.') ? match.Value : match.Value + ".0", out var version) ? version : null;
        }

        var a = Parse(candidate);
        var b = Parse(current);
        return a != null && b != null && a > b;
    }

    private static IEnumerable<string> GameModulesRoots()
    {
        var game = GameModulesDirectory();
        return string.IsNullOrWhiteSpace(game) ? Array.Empty<string>() : new[] { game! };
    }

    // MCM: the official Workshop item is gone and the Workshop reuploads can be older builds (v5.11) that crash
    // the dedicated server, while players get v5.12 or newer from Nexus into the game's Modules folder. Its server
    // copy is only ever replaced by a newer one, and Start takes a newer game folder copy even in a locked setup
    // (crash reports #32 and #34).
    public static readonly IReadOnlySet<string> NewerOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bannerlord.MBOptionScreen" };

    /// <summary>Start: a newer copy of a <see cref="NewerOnly"/> mod in the game's Modules folder replaces the server copy.</summary>
    public static IReadOnlyList<string> UpdateNewerOnlyFromGame(string serverRoot, IEnumerable<string> moduleIds, IReadOnlyDictionary<string, string>? pins = null) =>
        UpdateNewerOnlyFromGame(serverRoot, moduleIds, GameModulesRoots(), pins);

    /// <param name="pins">A locked pack's exact versions (AD1259 pins MCM): only a game folder copy of that version is taken.</param>
    public static IReadOnlyList<string> UpdateNewerOnlyFromGame(string serverRoot, IEnumerable<string> moduleIds, IEnumerable<string> gameRoots, IReadOnlyDictionary<string, string>? pins = null) =>
        UpdateFromNewerCopies(serverRoot, moduleIds.Where(NewerOnly.Contains), Array.Empty<string>(), gameRoots, pins);

    private static readonly HashSet<string> NeverReplaced = new(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBox", "SandBoxCore", "StoryMode", "CustomBattle", "DedicatedServer.Windows"
    };

    private static string SafeName(string text)
    {
        var clean = new string(text.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_').ToArray());
        return clean.Length == 0 ? "old" : clean;
    }

    private static string ReadVersion(string moduleDirectory)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(moduleDirectory, "SubModule.xml"));
            return ((string?)document.Root?.Element("Version")?.Attribute("value") ?? "").Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return "";
        }
    }

    internal static IEnumerable<string> WorkshopContentRoots() =>
        LibraryPaths()
            .Select(library => Path.Combine(library, "steamapps", "workshop", "content", "261550"))
            .Where(Directory.Exists)
            .ToList();

    // The copy itself is shared with the TAOM server tool (BannerlordCoopLauncher.Core).
    public static void Install(string sourceDirectory, string serverRoot, string moduleId) =>
        ServerModuleInstall.Install(sourceDirectory, serverRoot, moduleId);

    /// <summary>Runs one mod copy through runCopy (Coodsz Host: off the UI thread with a line first), or right here.</summary>
    public static void RunCopy(Action<string, Action>? runCopy, string name, Action copy)
    {
        if (runCopy == null)
            copy();
        else
            runCopy(name, copy);
    }

    /// <summary>
    /// The server copy (engine\Modules\id) checked against a good copy on this PC (ServerModuleInstall.MissingFiles).
    /// Null when it is whole, not there, or a copy the tool never puts over it (ClientOverwriteReason).
    /// </summary>
    public static ShortServerCopy? FindShortCopy(string sourceDirectory, string serverRoot, string moduleId, string name)
    {
        var serverCopy = Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), moduleId);
        if (!Directory.Exists(serverCopy) || ClientOverwriteReason(sourceDirectory, serverCopy, moduleId) != null)
            return null;
        var missing = ServerModuleInstall.MissingFiles(sourceDirectory, serverCopy);
        return missing.Count == 0 ? null : new ShortServerCopy(moduleId, name, sourceDirectory, missing);
    }

    /// <summary>
    /// Copies a short server copy again. Returns what the new copy still lacks, empty when it is whole now. The
    /// campaign map mod (mapId) that lands without its map scene counts as a failed copy (InvalidOperationException).
    /// </summary>
    public static IReadOnlyList<string> Recopy(ShortServerCopy copy, string serverRoot, string? mapId = null)
    {
        Install(copy.Source, serverRoot, copy.Id);
        var installed = Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), copy.Id);
        UnparkSubModule(installed);
        if (mapId != null && copy.Id.Equals(mapId.Trim(), StringComparison.OrdinalIgnoreCase) && !ServerModuleInstall.HasMapScene(installed))
            throw new InvalidOperationException(MissingCampaignMap.CopiedWithoutMap(copy.Name, copy.Source));
        return ServerModuleInstall.MissingFiles(copy.Source, installed);
    }

    // A source the launcher turned off has its SubModule.xml renamed; the server copy gets it back.
    internal static void UnparkSubModule(string installed)
    {
        if (!File.Exists(Path.Combine(installed, "SubModule.xml")) && File.Exists(Path.Combine(installed, WorkshopDuplicates.ParkedFile)))
            File.Move(Path.Combine(installed, WorkshopDuplicates.ParkedFile), Path.Combine(installed, "SubModule.xml"));
    }

    /// <summary>
    /// Coop Server Tool: the picked map mod when its server copy lacks files the Workshop or game folder copy of the
    /// same version has, or null.
    /// </summary>
    public static ShortServerCopy? ShortMapCopy(string serverRoot, string? mapId) =>
        ShortMapCopy(serverRoot, mapId, WorkshopContentRoots().Concat(GameModulesRoots()));

    public static ShortServerCopy? ShortMapCopy(string serverRoot, string? mapId, IEnumerable<string> sourceRoots)
    {
        if (string.IsNullOrWhiteSpace(mapId) || mapId.Equals(StableCoop.VanillaMap, StringComparison.OrdinalIgnoreCase)
            || NeverReplaced.Contains(mapId) || IsBannerlordCoop(mapId))
            return null;
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        // By folder name, like Install writes it and the Map box lists it (MapChoices). A map mod in a folder with
        // another name is left alone here; Start still refuses it when its Main_map is missing (MissingCampaignMap).
        var serverCopy = Path.Combine(modulesRoot, mapId);
        if (!Directory.Exists(serverCopy))
            return null;
        var serverVersion = ReadVersion(serverCopy);
        if (serverVersion.Length == 0)
            return null;
        // Every same version copy; one with the map scene first, since a copy without it would bring no map.
        var candidates = new List<(string Directory, string Name)>();
        foreach (var root in sourceRoots)
        {
            var available = new Dictionary<string, (string Directory, string Name, bool Workshop)>(StringComparer.OrdinalIgnoreCase);
            IndexModules(root, false, modulesRoot, available);
            if (available.TryGetValue(mapId, out var copy) && SameVersion(ReadVersion(copy.Directory), serverVersion))
                candidates.Add((copy.Directory, copy.Name));
        }

        if (candidates.Count == 0)
            return null;
        var good = candidates.OrderBy(candidate => ServerModuleInstall.HasMapScene(candidate.Directory) ? 0 : 1).First();
        return FindShortCopy(good.Directory, serverRoot, mapId, good.Name);
    }

    /// <summary>True when a Steam Workshop folder on this PC has this mod, so the fix for a broken copy is to subscribe again.</summary>
    public static bool IsOnWorkshop(string serverRoot, string moduleId) => IsOnWorkshop(serverRoot, moduleId, WorkshopContentRoots());

    public static bool IsOnWorkshop(string serverRoot, string moduleId, IEnumerable<string> workshopRoots)
    {
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        foreach (var root in workshopRoots)
        {
            var available = new Dictionary<string, (string Directory, string Name, bool Workshop)>(StringComparer.OrdinalIgnoreCase);
            IndexModules(root, true, modulesRoot, available);
            if (available.ContainsKey(moduleId))
                return true;
        }

        return false;
    }

    internal static string[] EditorCacheFolders => ServerModuleInstall.EditorCacheFolders;

    internal static string? ClientOverwriteReason(string source, string destination, string moduleId) =>
        ServerModuleInstall.ClientOverwriteReason(source, destination, moduleId);

    private static bool IsBannerlordCoop(string moduleId) =>
        moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
        || moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase);

    private static bool HasServerBin(string moduleDirectory) => ServerModuleInstall.HasServerBin(moduleDirectory);

    internal static void StripEditorCaches(string moduleDirectory) => ServerModuleInstall.StripEditorCaches(moduleDirectory);


    private static void Collect(string root, bool workshop, string source, HashSet<string> installed, List<FoundServerMod> found, HashSet<string> seen, string? skipCopiesOf = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var folderName = Path.GetFileName(directory);
            if (!workshop && SkipFolders.Contains(folderName))
                continue;
            if (BannerlordCoopLauncher.Core.Launch.BundledModules.IsCopyOf(directory, skipCopiesOf))
                continue;
            foreach (var moduleDirectory in FindModules(directory, 0))
            {
                if (!TryReadId(Path.Combine(moduleDirectory, "SubModule.xml"), out var id, out var name))
                    continue;
                if (SkipIds.Contains(id) || installed.Contains(id))
                    continue;
                var label = workshop ? "Workshop" : source;
                var existing = found.FindIndex(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    if (BetterCopy(moduleDirectory, workshop, found[existing].Directory, found[existing].Source == "Workshop", id))
                        found[existing] = new FoundServerMod(name, id, moduleDirectory, label);
                    continue;
                }
                if (!seen.Add(id))
                    continue;
                found.Add(new FoundServerMod(name, id, moduleDirectory, label));
            }
        }
    }

    private static bool BetterCopy(string candidate, bool candidateWorkshop, string current, bool currentWorkshop, string moduleId)
    {
        if (Preference(Edition.IsAD1259, candidate, candidateWorkshop, current, currentWorkshop, moduleId) is { } pick)
            return pick;

        static int Score(string folder, bool fromWorkshop, string id)
        {
            if (fromWorkshop)
                return 3;
            var name = Path.GetFileName(folder);
            if (name.Equals(id, StringComparison.OrdinalIgnoreCase))
                return 2;
            if (name.Contains("playtest", StringComparison.OrdinalIgnoreCase)
                || name.Contains("kept", StringComparison.OrdinalIgnoreCase)
                || name.Contains("backup", StringComparison.OrdinalIgnoreCase)
                || name.Contains("-old", StringComparison.OrdinalIgnoreCase))
                return 0;
            return 1;
        }

        return Score(candidate, candidateWorkshop, moduleId) > Score(current, currentWorkshop, moduleId);
    }

    /// <summary>
    /// The CR-JTQ45 rules before the usual score, or null when neither decides. True when the candidate wins.
    /// A copy with the campaign map scene beats one without (a Workshop re-upload or partial mirror with the same id
    /// lacks it), but only when it is the wanted build: the AD1259 set's pinned version, or else the same version as
    /// the other copy. In the AD1259 edition, a mod the set pins to Nexus Mods comes from the game's Modules folder
    /// when that copy has the pinned version, never from a Workshop copy with the same id. So does an Open Source mod
    /// with an accepted version (the 1.x set from Nexus next to the Workshop's 2.0), the copy the host's game loads, as
    /// AD1259Server.GoodCopy takes it. The Coop Server Tool keeps its own preference for a newer Workshop copy (an MCM
    /// from the Workshop over an older game folder one).
    /// </summary>
    internal static bool? Preference(bool ad1259, string candidate, bool candidateWorkshop, string current, bool currentWorkshop, string moduleId)
    {
        var pinned = ad1259 ? AD1259Stack.Find(moduleId) : null;
        var candidateMap = ServerModuleInstall.HasMapScene(candidate);
        if (candidateMap != ServerModuleInstall.HasMapScene(current))
        {
            var withMap = candidateMap ? candidate : current;
            var wanted = pinned != null
                ? AD1259Stack.VersionMatches(pinned, ReadVersion(withMap))
                : SameVersion(ReadVersion(candidate), ReadVersion(current));
            if (wanted)
                return candidateMap;
        }

        if (candidateWorkshop != currentWorkshop && pinned != null
            && (pinned is { WorkshopId: null, NexusId: not null } || OpenSourceSet.IsOpenSource(moduleId)))
        {
            var game = candidateWorkshop ? current : candidate;
            if (AD1259Stack.VersionMatches(pinned, ReadVersion(game)))
                return !candidateWorkshop;
        }

        return null;
    }

    private static IEnumerable<string> FindModules(string directory, int depth)
    {
        if (depth > 3 || !Directory.Exists(directory))
            yield break;
        if (File.Exists(Path.Combine(directory, "SubModule.xml")))
        {
            yield return directory;
            yield break;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            foreach (var nested in FindModules(child, depth + 1))
                yield return nested;
        }
    }

    private static bool TryResolve(string wantedId, Dictionary<string, (string Directory, string Name, bool Workshop)> available, out (string Directory, string Id, string Name) found)
    {
        found = default;
        if (available.TryGetValue(wantedId, out var match))
        {
            found = (match.Directory, wantedId, match.Name);
            return true;
        }

        if (wantedId.Equals("Coop", StringComparison.OrdinalIgnoreCase) && available.TryGetValue("CoopNightly", out match))
        {
            found = (match.Directory, "CoopNightly", match.Name);
            return true;
        }

        return false;
    }

    private static HashSet<string> ReadInstalledIds(string modulesRoot)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(modulesRoot))
            return ids;
        foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
        {
            if (TryReadId(Path.Combine(directory, "SubModule.xml"), out var id, out _))
                ids.Add(id);
        }

        return ids;
    }

    private static void IndexModules(string root, bool workshop, string serverModules, Dictionary<string, (string Directory, string Name, bool Workshop)> available)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;
        var serverFull = Path.GetFullPath(serverModules);
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            foreach (var moduleDirectory in FindModules(directory, 0))
            {
                var full = Path.GetFullPath(moduleDirectory);
                if (full.StartsWith(serverFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryReadId(Path.Combine(moduleDirectory, "SubModule.xml"), out var id, out var name))
                    continue;
                if (!available.TryGetValue(id, out var current) || BetterCopy(moduleDirectory, workshop, current.Directory, current.Workshop, id))
                    available[id] = (moduleDirectory, name, workshop);
            }
        }
    }

    private static bool TryReadId(string path, out string id, out string name)
    {
        id = "";
        name = "";
        // A copy cut short has no SubModule.xml yet (Install writes it last); that folder reads as no mod.
        if (!File.Exists(path))
            return false;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            using var reader = XmlReader.Create(path, settings);
            string? readId = null;
            string? readName = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name is not ("Id" or "Name"))
                    continue;
                var value = reader.GetAttribute("value");
                if (reader.Name == "Id" && readId == null)
                    readId = value;
                else if (reader.Name == "Name" && readName == null)
                    readName = value;
            }

            if (!ModuleCatalog.IsSafeModuleId(readId))
                return false;
            id = readId!.Trim();
            name = string.IsNullOrWhiteSpace(readName) ? id : readName.Trim();
            return true;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string? GameModulesDirectory()
    {
        // A folder picked with Find Bannerlord comes first, the same as for the pictures.
        var saved = GameFolderPick.CheckSaved(PictureBuilder.SavedGamePath?.Invoke());
        if (saved.Result == GameFolderPickResult.Ok && ModulesIfGame(saved.Root!) is { } picked)
            return picked;

        foreach (var library in LibraryPaths())
        {
            var named = ModulesIfGame(Path.Combine(library, "steamapps", "common", "Mount & Blade II Bannerlord"));
            if (named != null)
                return named;

            var manifest = Path.Combine(library, "steamapps", "appmanifest_261550.acf");
            if (!File.Exists(manifest))
                continue;
            var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!match.Success)
                continue;
            var fromManifest = ModulesIfGame(Path.Combine(library, "steamapps", "common", match.Groups[1].Value));
            if (fromManifest != null)
                return fromManifest;
        }

        return null;
    }

    private static string? ModulesIfGame(string gameRoot)
    {
        var modules = Path.Combine(gameRoot, "Modules");
        var exe = Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "Bannerlord.exe");
        return File.Exists(exe) && Directory.Exists(modules) ? modules : null;
    }

    private static IEnumerable<string> LibraryPaths()
    {
        var paths = new List<string>();
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steam = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steam))
            return paths;
        paths.Add(steam);
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                paths.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

}
