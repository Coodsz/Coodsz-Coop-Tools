using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>One extra mod of the setup as the server will get it.</summary>
public sealed record BeServerExtra(TaomExtraMod Mod, string SourceDirectory, ServerCode Code);

public sealed record BeServerPlan(
    IReadOnlyList<string> ServerModules,
    IReadOnlyList<string> ClientModules,
    IReadOnlyList<BeServerExtra> Extras,
    IReadOnlyList<TaomExtraMod> Missing,
    string? JoinProblem,
    TaomSetup Setup);

/// <summary>Where the core modules come from on the host's PC.</summary>
/// <param name="GameRoot">The host's Bannerlord folder (BurningEmpires, Elephants, CustomBattle, MCM).</param>
/// <param name="SteamLibraries">Steam libraries to find the BUTR Workshop items in.</param>
/// <param name="CompatModule">The bundled ServerAddOns\BurningEmpires.CoopCompat folder.</param>
/// <param name="UIExtenderExServerDll">The bundled ServerAddOns\UIExtenderEx-2.13.3\Bannerlord.UIExtenderEx.dll.</param>
public sealed record BeCoreSources(string GameRoot, IReadOnlyList<string> SteamLibraries, string CompatModule, string UIExtenderExServerDll)
{
    // The files the tool was made for. Only tests give other values.
    public string BeDllSha256 { get; init; } = BeServerPath.BeDllSha256;
    public string ProbeDllSha256 { get; init; } = BeServerPath.ProbeDllSha256;
    public string UIExtenderExSha256 { get; init; } = BeServerPath.UIExtenderExSha256;

    /// <summary>The server folder. When it already has an MCM the server loads, no other MCM is needed.</summary>
    public string? ServerRoot { get; init; }
}

/// <summary>
/// The Burning Empires server path. The server folder is a whitelist copy of the Workshop Coop
/// dedicated server (BeServerPrepare). Each start stages the core into engine\Modules by copying
/// over what is there: the three BUTR Workshop items, MCM from the game's Modules folder or the old
/// Workshop item's copy already on disk (only a newer one replaces the server copy), the bundled Burning Empires Coop Compat,
/// Burning Empires and Burning Empires Elephants. A module folder is
/// never deleted, so engine\Modules\BurningEmpires\BESavedata (Burning Empires' own save data)
/// survives every restage; ServerModuleInstall.Install deletes the folder first, so it is never
/// used for the core. Burning Empires ships no server build, so the server bin copies go into
/// BurningEmpires\bin\Win64_Shipping_Server and are byte compared on every start. Client code is
/// never switched off on a core module and the TAOM dependency fix never runs here; extras still
/// get the server build and client code rules TAOM's server uses.
/// </summary>
public static class BeServerPath
{
    // The ids, versions and hashes come from BeStack, the one place the Burning Empires set is written down.
    public const string CoopModuleId = BeStack.CoopModuleId;
    public const string CompatModuleId = BeStack.CompatModuleId;
    public const string BeModuleId = BeStack.BeModuleId;
    public const string ElephantsModuleId = BeStack.ElephantsModuleId;
    public const string ServerHostModuleId = "DedicatedServer.Windows";
    public const string SaveDataFolder = "BESavedata";

    public const string BeVersion = BeStack.BeVersion;
    public const string ElephantsVersion = BeStack.ElephantsVersion;
    public const string BeDllSha256 = BeStack.BeDllSha256;
    public const string ProbeDllSha256 = BeStack.DistanceCacheProbeDllSha256;

    public const string BeDll = BeStack.BeDllName;
    public const string ProbeDll = BeStack.DistanceCacheProbeDllName;
    public const string UIExtenderExDll = BeStack.ServerUiExtenderExDllName;
    public const string CustomBattleDll = "TaleWorlds.MountAndBlade.CustomBattle.dll";
    // UIExtenderEx 2.13.3.0 from BUTR's GitHub release (269824 bytes), the exact version
    // Burning Empires was built with. The UIExtenderEx module itself stays 3.0.3.
    public const string UIExtenderExVersion = "2.13.3";
    public const string UIExtenderExSha256 = BeStack.ServerUiExtenderExSha256;

    public const string NexusUrl = BeStack.BeNexusPage;

    public const string OnNexus = "Burning Empires is on Nexus Mods at " + NexusUrl;

    public const string FreshTool = "Download this tool again into a fresh folder.";

    /// <summary>The BUTR modules in load order, with their Workshop items. MCM has "0": it comes from
    /// Nexus Mods into the game's Modules folder (BestMcm), and is never subscribed.</summary>
    public static readonly IReadOnlyList<(string ModuleId, string WorkshopId, string Label)> Butr =
        BeStack.Butr.Select(mod => (mod.ModuleId, mod.WorkshopId, mod.Name)).ToList();

    /// <summary>The game block between BUTR and Coop on the server, in order. Start-ModdedServer.ps1
    /// pins DedicatedServer.Windows right after Native as well.</summary>
    public static readonly IReadOnlyList<string> ServerGameBlock = ["Native", ServerHostModuleId, "SandBoxCore", "Sandbox"];

    /// <summary>The ids the tool stages itself. An extra with one of these ids is refused.</summary>
    public static IReadOnlyList<string> CoreIds { get; } =
        Butr.Select(item => item.ModuleId)
            .Concat(ServerGameBlock)
            .Concat([CoopModuleId, CompatModuleId, BeModuleId, ElephantsModuleId, "StoryMode", "CustomBattle", "SandBox"])
            .ToList();

    public static bool IsCore(string moduleId) => CoreIds.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string ModulesRoot(string serverRoot) => ServerModuleInstall.ModulesRoot(serverRoot);

    public static string BeServerBin(string serverRoot) => Path.Combine(ModulesRoot(serverRoot), BeModuleId, "bin", "Win64_Shipping_Server");

    /// <summary>MCM's folder in the game's Modules folder, where players put the one from Nexus Mods.</summary>
    public static string McmSource(string gameRoot) => Path.Combine(gameRoot, "Modules", BeStack.Mcm.ModuleId);

    /// <summary>The copies Steam downloaded before of the removed official MCM Workshop item, one per Steam
    /// library. Only read from disk; the item is never subscribed or downloaded. Only this item counts, and only at
    /// McmServerMinimum or newer (BestMcm): re-uploads under other ids can be old builds that crash the dedicated
    /// server (RetiredWorkshopItems). The client takes MCM from any Workshop item on purpose, since the game loads it.</summary>
    public static IEnumerable<string> OldWorkshopMcm(IEnumerable<string> steamLibraries)
    {
        var retired = RetiredWorkshopItems.For(BeStack.Mcm.ModuleId);
        return retired == null
            ? []
            : steamLibraries.Select(library => Path.Combine(library, "steamapps", "workshop", "content", "261550", retired));
    }

    /// <summary>The MCM Start copies to the server, one the server loads (McmServerMinimum or newer): the
    /// game's Modules folder copy, even when an old Workshop copy is newer (like the shared tools, a retired
    /// Workshop id never wins over the game folder), else the newest of the old Workshop item's copies.
    /// Null when there is none.</summary>
    public static string? BestMcm(string? gameRoot, IEnumerable<string> steamLibraries)
    {
        if (!string.IsNullOrWhiteSpace(gameRoot) && ServerLoads(VersionOf(McmSource(gameRoot)), out _))
            return McmSource(gameRoot);
        string? best = null;
        Version? bestVersion = null;
        foreach (var folder in OldWorkshopMcm(steamLibraries))
        {
            if (!ServerLoads(VersionOf(folder), out var version) || (bestVersion != null && version <= bestVersion))
                continue;
            best = folder;
            bestVersion = version;
        }

        return best;
    }

    /// <summary>True when the server folder already has an MCM the server loads.</summary>
    public static bool ServerMcmReady(string? serverRoot) =>
        !string.IsNullOrWhiteSpace(serverRoot) && ServerLoads(VersionOf(Path.Combine(ModulesRoot(serverRoot), BeStack.Mcm.ModuleId)), out _);

    // True when the version can be read and is McmServerMinimum or newer.
    private static bool ServerLoads(string text, out Version version)
    {
        var parsed = text.Length == 0 ? null : ParseVersion(text);
        version = parsed ?? new Version();
        return parsed != null && parsed >= ParseVersion(BeStack.McmServerMinimum)!;
    }

    private static bool IsOldWorkshopMcm(string folder) =>
        RetiredWorkshopItems.For(BeStack.Mcm.ModuleId) is { } retired
        && Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Equals(retired, StringComparison.OrdinalIgnoreCase);

    public static string McmOnNexus => "MCM is on Nexus Mods at " + BeStack.Mcm.NexusPage;

    /// <summary>The server folder's folder Start moves an older MCM into when a newer one replaces it.</summary>
    public const string RemovedFolder = "removed-mods";

    public static string? WorkshopItem(IEnumerable<string> steamLibraries, string workshopId)
    {
        foreach (var library in steamLibraries)
        {
            var path = Path.Combine(library, "steamapps", "workshop", "content", "261550", workshopId);
            if (File.Exists(Path.Combine(path, "SubModule.xml")))
                return path;
        }

        return null;
    }

    // ----- Core check -----

    /// <summary>Everything that stops the core from being staged, one plain line each. Empty when
    /// the host's PC has it all.</summary>
    public static IReadOnlyList<string> CoreProblems(BeCoreSources sources)
    {
        var problems = new List<string>();
        var modules = Path.Combine(sources.GameRoot, "Modules");
        var be = Path.Combine(modules, BeModuleId);
        var version = VersionOf(be);
        if (version.Length == 0)
            problems.Add("Burning Empires is not in the game's Modules folder. " + BeArchive.MainThenHotfix + " Or click Install or repair Burning Empires in step 3. " + OnNexus);
        else if (!version.Equals(BeVersion, StringComparison.OrdinalIgnoreCase))
            problems.Add("Burning Empires is " + version + ". The server needs " + BeVersion + ". " + BeArchive.MainThenHotfix + " " + OnNexus);
        else if (HashProblem(Path.Combine(be, "bin", "Win64_Shipping_Client", BeDll), sources.BeDllSha256) is { } beDll)
            problems.Add(beDll + " " + BeArchive.MainThenHotfix + " " + OnNexus);
        // CopyServerBin copies the DistanceCacheProbe DLL next to BurningEmpires.dll, so it is checked the same way.
        else if (HashProblem(Path.Combine(be, "bin", "Win64_Shipping_Client", ProbeDll), sources.ProbeDllSha256) is { } probe)
            problems.Add(probe + " " + BeArchive.MainThenHotfix + " " + OnNexus);

        var elephantsVersion = VersionOf(Path.Combine(modules, ElephantsModuleId));
        if (elephantsVersion.Length == 0)
            problems.Add("Burning Empires Elephants is not in the game's Modules folder. It comes in the Burning Empires main file from Nexus Mods.");
        else if (!elephantsVersion.Equals(ElephantsVersion, StringComparison.OrdinalIgnoreCase))
            problems.Add("Burning Empires Elephants is " + elephantsVersion + ". The server needs " + ElephantsVersion + ".");

        foreach (var (_, workshopId, label) in Butr.Where(item => SharedModList.IsWorkshopId(item.WorkshopId)))
        {
            if (WorkshopItem(sources.SteamLibraries, workshopId) == null)
                problems.Add(label + " is not downloaded. Burning Empires needs " + BeHostReport.HelperMods + ". Click Install mods on the Mods page and let Steam finish, then try again.");
        }

        if (McmProblem(sources.GameRoot, sources.SteamLibraries, sources.ServerRoot) is { } mcm)
            problems.Add(mcm);

        if (!File.Exists(Path.Combine(sources.CompatModule, "SubModule.xml")))
            problems.Add(ToolPayload.MissingText("Burning Empires Coop Compat", sources.CompatModule));
        if (!File.Exists(sources.UIExtenderExServerDll))
            problems.Add(ToolPayload.MissingText("UIExtenderEx " + UIExtenderExVersion, sources.UIExtenderExServerDll));
        if (!File.Exists(CustomBattleSource(sources.GameRoot)))
            problems.Add("The CustomBattle DLL is missing from the game's Modules\\CustomBattle folder. Verify the game files in Steam.");
        return problems;
    }

    /// <summary>The BUTR modules Install mods on the server tool still has to get: a Workshop one whose item
    /// is not downloaded, and MCM when neither this PC (BestMcm) nor the server folder has one the server can
    /// take (it comes from Nexus Mods, never from Steam). A blank gameRoot means no game copy.</summary>
    public static IReadOnlyList<ButrModule> MissingButr(IEnumerable<string> steamLibraries, string? gameRoot, string? serverRoot = null)
    {
        var libraries = steamLibraries.ToList();
        var missing = BeStack.WorkshopButr.Where(mod => WorkshopItem(libraries, mod.WorkshopId) == null).ToList();
        if (gameRoot != null && McmProblem(gameRoot, libraries, serverRoot) != null)
            missing.Add(BeStack.Mcm);
        return missing;
    }

    /// <summary>Why the server cannot get an MCM it loads, or null: null when this PC has one (BestMcm) or
    /// the server folder already has one. A blank gameRoot means no game copy.</summary>
    public static string? McmProblem(string? gameRoot, IEnumerable<string> steamLibraries, string? serverRoot = null)
    {
        var libraries = steamLibraries.ToList();
        if (BestMcm(gameRoot, libraries) != null || ServerMcmReady(serverRoot))
            return null;
        var version = string.IsNullOrWhiteSpace(gameRoot) ? "" : VersionOf(McmSource(gameRoot));
        if (version.Length > 0 && ParseVersion(version) == null)
            return "The tool cannot read the version of the MCM in the game's Modules folder (" + version + "). Get MCM " + BeStack.Mcm.TestedVersion + " or newer from Nexus Mods and put it in place of that one, then try again. " + McmOnNexus;
        if (version.Length > 0)
            return "MCM " + version + " in the game's Modules folder is too old for the server. Get MCM " + BeStack.Mcm.TestedVersion + " or newer from Nexus Mods and put it in place of the old one, then try again. " + McmOnNexus;
        var workshop = OldWorkshopMcm(libraries).Select(VersionOf).FirstOrDefault(found => ParseVersion(found) != null);
        if (workshop != null)
            return "MCM " + workshop + " from the old Steam Workshop item is too old for the server. Get MCM " + BeStack.Mcm.TestedVersion + " or newer from Nexus Mods and put it in the game's Modules folder, then try again. " + McmOnNexus;
        return "MCM is not on this PC. Get MCM " + BeStack.Mcm.TestedVersion + " or newer from Nexus Mods and put it in the game's Modules folder, then try again. " + McmOnNexus;
    }

    public static string CustomBattleSource(string gameRoot) =>
        Path.Combine(gameRoot, "Modules", "CustomBattle", "bin", "Win64_Shipping_Client", CustomBattleDll);

    // ----- Staging -----

    /// <summary>
    /// Stages the core into the server's engine\Modules by copying over what is there, then makes
    /// the server bin copies. Throws with a plain reason when CoreProblems has one. Returns one
    /// plain line per step.
    /// </summary>
    public static IReadOnlyList<string> StageCore(string serverRoot, BeCoreSources sources)
    {
        var problems = CoreProblems(sources with { ServerRoot = serverRoot });
        if (problems.Count > 0)
            throw new InvalidOperationException(problems[0]);

        var lines = new List<string>();
        var modulesRoot = ModulesRoot(serverRoot);
        foreach (var (id, workshopId, label) in Butr)
        {
            lines.Add(SharedModList.IsWorkshopId(workshopId)
                ? Overlay(WorkshopItem(sources.SteamLibraries, workshopId)!, Path.Combine(modulesRoot, id), label)
                : StageMcm(serverRoot, BestMcm(sources.GameRoot, sources.SteamLibraries)));
        }
        lines.Add(Overlay(sources.CompatModule, Path.Combine(modulesRoot, CompatModuleId), "Burning Empires Coop Compat"));
        lines.Add(Overlay(Path.Combine(sources.GameRoot, "Modules", BeModuleId), Path.Combine(modulesRoot, BeModuleId), "Burning Empires"));
        lines.Add(Overlay(Path.Combine(sources.GameRoot, "Modules", ElephantsModuleId), Path.Combine(modulesRoot, ElephantsModuleId), "Burning Empires Elephants"));

        lines.AddRange(CopyServerBin(serverRoot, sources));

        // A Nexus download keeps the Windows download mark; the server cannot load a marked DLL.
        foreach (var id in new[] { CompatModuleId, BeModuleId })
        {
            var unblocked = ModUnblock.UnblockDlls(Path.Combine(modulesRoot, id));
            if (unblocked > 0)
                lines.Add(ModUnblock.Line(id, unblocked));
        }

        return lines;
    }

    /// <summary>
    /// The four server bin copies Burning Empires needs on the server, byte compared on every
    /// start: BurningEmpires.dll and its DistanceCacheProbe from the host's client bin, UIExtenderEx
    /// 2.13.3 from ServerAddOns (hash checked) and the game's CustomBattle DLL.
    /// </summary>
    public static IReadOnlyList<string> CopyServerBin(string serverRoot, BeCoreSources sources)
    {
        if (!File.Exists(sources.UIExtenderExServerDll))
            throw new InvalidOperationException(ToolPayload.MissingText("UIExtenderEx " + UIExtenderExVersion, sources.UIExtenderExServerDll));
        var uiProblem = HashProblem(sources.UIExtenderExServerDll, sources.UIExtenderExSha256);
        if (uiProblem != null)
            throw new InvalidOperationException(uiProblem + " " + FreshTool);

        var client = Path.Combine(sources.GameRoot, "Modules", BeModuleId, "bin", "Win64_Shipping_Client");
        var copies = new[]
        {
            Path.Combine(client, BeDll),
            Path.Combine(client, ProbeDll),
            sources.UIExtenderExServerDll,
            CustomBattleSource(sources.GameRoot)
        };
        var missing = copies.FirstOrDefault(path => !File.Exists(path));
        if (missing != null)
            throw new InvalidOperationException(Path.GetFileName(missing) + " was not found at " + missing + ".");

        var bin = BeServerBin(serverRoot);
        Directory.CreateDirectory(bin);
        var copied = 0;
        foreach (var source in copies)
        {
            var target = Path.Combine(bin, Path.GetFileName(source));
            if (BeServerPrepare.SameBytes(source, target))
                continue;
            File.Copy(source, target, overwrite: true);
            copied++;
        }

        return [copied == 0
            ? "The Burning Empires server files are already in place."
            : "Copied " + copied + " Burning Empires server files into BurningEmpires\\bin\\Win64_Shipping_Server."];
    }

    /// <summary>The server bin copies that are missing or differ from their sources. Empty when all four match.</summary>
    public static IReadOnlyList<string> ServerBinProblems(string serverRoot, BeCoreSources sources)
    {
        var client = Path.Combine(sources.GameRoot, "Modules", BeModuleId, "bin", "Win64_Shipping_Client");
        var bin = BeServerBin(serverRoot);
        return new[] { Path.Combine(client, BeDll), Path.Combine(client, ProbeDll), sources.UIExtenderExServerDll, CustomBattleSource(sources.GameRoot) }
            .Where(source => !BeServerPrepare.SameBytes(source, Path.Combine(bin, Path.GetFileName(source))))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
    }

    // MCM comes from BestMcm: the game's Modules folder (Nexus Mods) or the removed official Workshop item's
    // copy already on disk, never a re-upload. A newer copy replaces the server copy whole, so an old build's
    // server bin folder is not left behind (a stale v5.11 server copy refused every start, crash reports #32
    // and #34); an older one never replaces a newer server copy. With no source McmProblem already found the
    // server copy good, so it stays.
    internal static string StageMcm(string serverRoot, string? source)
    {
        var destination = Path.Combine(ModulesRoot(serverRoot), BeStack.Mcm.ModuleId);
        var server = VersionOf(destination);
        if (source == null)
            return "The server keeps its own MCM " + server + ", since this PC has no other MCM the server can take.";
        var game = VersionOf(source);
        var workshop = IsOldWorkshopMcm(source);
        if (server.Length == 0)
            return Overlay(source, destination, BeStack.Mcm.Name);
        // A server copy whose version cannot be read is replaced too; McmProblem already checked the source.
        var replace = ParseVersion(server) == null || IsNewer(game, server);
        if (!replace && IsNewer(server, game))
            return workshop
                ? "The server keeps MCM " + server + ", since the old Steam Workshop item has the older MCM " + game + "."
                : "The server keeps MCM " + server + ", since the game's Modules folder has the older MCM " + game + ".";
        if (!replace)
            return Overlay(source, destination, BeStack.Mcm.Name);

        var removed = Path.Combine(serverRoot, RemovedFolder);
        string aside;
        try
        {
            Directory.CreateDirectory(removed);
            aside = AsideFolder(removed, server);
            Directory.Move(destination, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("MCM on the server could not be replaced, because a file in it is in use. Stop the server and try again.", ex);
        }

        try
        {
            OverlayTree(source, destination, top: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Put the old copy back, so a failed Start never leaves the server without MCM.
            try
            {
                if (Directory.Exists(destination))
                    Directory.Delete(destination, recursive: true);
                Directory.Move(aside, destination);
            }
            catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("MCM could not be copied to the server, and the old copy could not be put back. It is in the server folder's " + RemovedFolder + " folder. Stop the server and try again.", ex);
            }

            throw new InvalidOperationException("MCM could not be copied to the server, so the old copy was put back. Stop the server and try again.", ex);
        }

        return workshop
            ? "MCM " + game + " from the old Steam Workshop item replaced MCM " + server + " on the server. The old copy is in the server folder's " + RemovedFolder + " folder."
            : "MCM " + game + " from the game's Modules folder replaced MCM " + server + " on the server. The old copy is in the server folder's " + RemovedFolder + " folder.";
    }

    // A free folder in removed-mods for the old MCM, named after its version with the characters a folder
    // name cannot have taken out, or after the time when nothing is left. now is for tests, so two calls
    // share one time stamp even when the clock ticks over a second between them.
    internal static string AsideFolder(string removed, string version, DateTime? now = null)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(version.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).Take(40).ToArray()).Trim(' ', '.', '_');
        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
        var name = BeStack.Mcm.ModuleId + "-" + (safe.Length > 0 ? safe : stamp);
        bool Taken(string path) => Directory.Exists(path) || File.Exists(path);
        var aside = Path.Combine(removed, name);
        if (!Taken(aside))
            return aside;
        if (safe.Length > 0)
            name += "-" + stamp;
        aside = Path.Combine(removed, name);
        for (var n = 2; Taken(aside); n++)
            aside = Path.Combine(removed, name + "-" + n);
        return aside;
    }

    // True when version a is newer than b ("v5.12.3" and the like). Unreadable versions are never newer.
    internal static bool IsNewer(string a, string b) =>
        ParseVersion(a) is { } left && ParseVersion(b) is { } right && left > right;

    private static Version? ParseVersion(string text)
    {
        var digits = new string(text.Trim().TrimStart('v', 'V').TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.');
        if (!digits.Contains('.'))
            digits += ".0";
        // v5.12.3 and v5.12.3.0 are the same version.
        return Version.TryParse(digits, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0))
            : null;
    }

    // Copies a module over the server copy: missing or changed files only, nothing deleted, and
    // never the source's own BESavedata (single player data must not reach the server's saves). Editor
    // caches are left out, the server never reads them.
    private static string Overlay(string source, string destination, string label)
    {
        var copied = OverlayTree(source, destination, top: true);
        return copied == 0 ? label + " is already on the server." : "Copied " + label + " to the server (" + copied + " files).";
    }

    internal static int OverlayTree(string source, string destination, bool top)
    {
        Directory.CreateDirectory(destination);
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            var from = new FileInfo(file);
            var to = new FileInfo(target);
            if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
                continue;
            File.Copy(file, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, from.LastWriteTimeUtc);
            copied++;
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (top && name.Equals(SaveDataFolder, StringComparison.OrdinalIgnoreCase))
                continue;
            if (top && ServerModuleInstall.EditorCacheFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            copied += OverlayTree(directory, Path.Combine(destination, name), top: false);
        }

        return copied;
    }

    // ----- Load order -----

    /// <summary>
    /// Null when the server order has the Burning Empires core the way the server needs it: the
    /// BUTR modules first in their order, then Native, DedicatedServer.Windows, SandBoxCore and
    /// Sandbox, then Coop, with Burning Empires Coop Compat directly before Burning Empires and
    /// Elephants directly after Burning Empires. The dedicated server does not sort modules; a run with
    /// Burning Empires first failed.
    /// </summary>
    public static string? OrderProblem(IReadOnlyList<string> serverOrder)
    {
        var list = serverOrder.Select(id => id.Trim()).ToList();
        int At(string id) => list.FindIndex(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));

        var duplicate = list.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            return duplicate.Key + " is in the server list twice.";

        var core = Butr.Select(item => item.ModuleId).Concat(ServerGameBlock).Append(CoopModuleId).Append(CompatModuleId).Append(BeModuleId).Append(ElephantsModuleId).ToList();
        var missing = core.FirstOrDefault(id => At(id) < 0);
        if (missing != null)
            return missing + " is missing from the server list.";

        for (var i = 0; i < Butr.Count; i++)
        {
            if (At(Butr[i].ModuleId) != i)
                return "The server list has to start with Harmony, ButterLib, UIExtenderEx and MCM, in that order.";
        }

        var previous = At(Butr[^1].ModuleId);
        foreach (var id in ServerGameBlock)
        {
            if (At(id) != previous + 1)
                return "Native, DedicatedServer.Windows, SandBoxCore and Sandbox have to come right after MCM, in that order.";
            previous = At(id);
        }

        if (At(CoopModuleId) < At("Sandbox"))
            return "Coop has to come after Sandbox.";
        if (At(CompatModuleId) < At(CoopModuleId))
            return "Burning Empires Coop Compat has to come after Coop.";
        if (At(BeModuleId) != At(CompatModuleId) + 1)
            return "Burning Empires Coop Compat has to come directly before Burning Empires.";
        if (At(ElephantsModuleId) != At(BeModuleId) + 1)
            return "Burning Empires Elephants has to load right after Burning Empires.";
        return null;
    }

    public static string ListPath(string serverRoot) => Path.Combine(serverRoot, "modded-server-modules.txt");

    public static string SelectionPath(string serverRoot) => Path.Combine(serverRoot, "modded-server-selection.json");

    /// <summary>Writes modded-server-modules.txt and modded-server-selection.json for
    /// Start-ModdedServer.ps1, with every listed module turned on. Refuses a wrong order.</summary>
    public static void WriteOrder(string serverRoot, IReadOnlyList<string> serverOrder)
    {
        var problem = OrderProblem(serverOrder);
        if (problem != null)
            throw new InvalidOperationException("The server was not started. " + problem);
        foreach (var id in serverOrder)
        {
            if (!ProfileJson.IsSafeModuleId(id))
                throw new InvalidOperationException("The server was not started. " + id + " cannot be a module id.");
        }

        var list = new StringBuilder();
        list.Append("# Bannerlord module IDs in dedicated-server load order. Written by the Burning Empires server tool on every start.\r\n");
        var selection = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var id in serverOrder)
        {
            list.Append(id.Trim()).Append("\r\n");
            selection[id.Trim()] = true;
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(ListPath(serverRoot), list.ToString(), encoding);
        File.WriteAllText(SelectionPath(serverRoot), JsonSerializer.Serialize(selection, new JsonSerializerOptions { WriteIndented = true }), encoding);
    }

    /// <summary>
    /// The folder the next Start copies a module from, the same choice StageCore and Plan make: the BUTR
    /// Workshop items, the MCM BestMcm picks, the bundled compat module, Burning Empires and Elephants from
    /// the game's Modules folder, and an extra from onThisPc. Null for Coop and the game modules, which come
    /// with the server folder, and for a module Start can't find on this PC.
    /// </summary>
    public static string? StartSource(string moduleId, BeCoreSources sources, IReadOnlyList<FoundMod> onThisPc)
    {
        var id = moduleId.Trim();
        foreach (var (butrId, workshopId, _) in Butr)
        {
            if (butrId.Equals(id, StringComparison.OrdinalIgnoreCase))
                return SharedModList.IsWorkshopId(workshopId) ? WorkshopItem(sources.SteamLibraries, workshopId) : BestMcm(sources.GameRoot, sources.SteamLibraries);
        }

        if (id.Equals(CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return sources.CompatModule;
        if (id.Equals(BeModuleId, StringComparison.OrdinalIgnoreCase) || id.Equals(ElephantsModuleId, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(sources.GameRoot) ? null : Path.Combine(sources.GameRoot, "Modules", id);
        if (IsCore(id))
            return null;
        return onThisPc.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Directory;
    }

    // ----- Extras -----

    /// <summary>What the server needs for this setup. serverOrder and clientOrder come from the
    /// load order rules; onThisPc are the mods found on the host's PC.</summary>
    public static BeServerPlan Plan(TaomSetup setup, IReadOnlyList<string> serverOrder, IReadOnlyList<string> clientOrder, IReadOnlyList<FoundMod> onThisPc)
    {
        setup = TaomModIds.Resolve(setup, onThisPc);
        var extras = new List<BeServerExtra>();
        var missing = new List<TaomExtraMod>();
        foreach (var mod in setup.EnabledExtras.Where(mod => !IsCore(mod.ModuleId) && serverOrder.Contains(mod.ModuleId.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                missing.Add(mod);
                continue;
            }

            ServerCode code;
            try
            {
                code = ServerModuleInstall.Classify(found.Directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                code = ServerCode.ClientCodeOnly;
            }

            if (code == ServerCode.ClientCodeSkipped && RunsForCoopModPatch(serverOrder, mod.ModuleId))
                code = ServerCode.ServerBuild;
            extras.Add(new BeServerExtra(mod, found.Directory, code));
        }

        return new BeServerPlan(serverOrder, clientOrder, extras, missing, CoopJoinRules.JoinProblem(clientOrder, serverOrder), setup);
    }

    /// <summary>
    /// Copies the extras into the server's engine\Modules: a mod that is not there yet, or whose
    /// version changed, is copied again. Client code without a server build is switched off in
    /// the server copy. A core id is never touched here. Returns one plain line per mod.
    /// </summary>
    public static IReadOnlyList<string> StageExtras(string serverRoot, BeServerPlan plan)
    {
        var lines = new List<string>();
        var modulesRoot = ModulesRoot(serverRoot);
        foreach (var extra in plan.Extras)
        {
            var id = extra.Mod.ModuleId.Trim();
            if (IsCore(id))
                continue;
            var destination = Path.Combine(modulesRoot, id);
            var copied = false;
            var forCoopModPatch = RunsForCoopModPatch(plan.ServerModules, id);
            if (!Directory.Exists(destination) || VersionOf(destination) != VersionOf(extra.SourceDirectory)
                || MissesServerBuild(destination, extra.SourceDirectory, forCoopModPatch))
            {
                ServerModuleInstall.Install(extra.SourceDirectory, serverRoot, id);
                copied = true;
            }

            var unblocked = ModUnblock.UnblockDlls(destination);
            if (unblocked > 0)
                lines.Add(ModUnblock.Line(extra.Mod.Label, unblocked));
            if (copied)
                DownloadMark.Clear(destination);

            if (ServerModuleInstall.CoopModPatchServerCode.Contains(id))
            {
                if (forCoopModPatch)
                    ServerModuleInstall.AddServerBuildFromClient(destination);
                SetCoopModPatchServerCode(destination, extra.SourceDirectory, forCoopModPatch);
            }
            TurnOffClientCode(destination, id);
            var label = extra.Mod.Label;
            lines.Add((copied ? "Copied " + label + " to the server." : label + " is already on the server.") + " " + TaomServerPath.NoteFor(extra.Code));
        }

        return lines;
    }

    /// <summary>True when the server list has CoopModPatch and this mod is one its server adapter
    /// needs running on the server (Improved Garrisons, Settlement Culture Drift).</summary>
    public static bool RunsForCoopModPatch(IReadOnlyList<string> serverOrder, string moduleId) =>
        ServerModuleInstall.CoopModPatchServerCode.Contains(moduleId.Trim())
        && serverOrder.Contains(ServerModuleInstall.CoopModPatchId, StringComparer.OrdinalIgnoreCase);

    // The same switch the TAOM server uses (TaomServerPath): these mods mark their code
    // DedicatedServerType none, so without it the engine skips the DLL and CoopModPatch's adapter
    // fails with "Could not load file or assembly". The server copy's SubModule.xml is made from the
    // mod's own each start, so dropping CoopModPatch puts the mod's own mark back. A copy that
    // already is what it should be is not written again.
    private static void SetCoopModPatchServerCode(string moduleDirectory, string sourceDirectory, bool on)
    {
        var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
        var sourcePath = Path.Combine(sourceDirectory, "SubModule.xml");
        if (!File.Exists(xmlPath))
            return;
        var samePlace = Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(xmlPath), StringComparison.OrdinalIgnoreCase);
        var source = ReadXml(File.Exists(sourcePath) && !samePlace ? sourcePath : xmlPath, out var bom);
        var wanted = on ? ServerModuleInstall.TurnOnServerCode(source, ServerModuleInstall.ServerDlls(moduleDirectory)) : source;
        if (ReadXml(xmlPath, out _) == wanted)
            return;
        File.WriteAllText(xmlPath, wanted, new UTF8Encoding(bom));
    }

    // A same-version server copy without the server DLL keeps its skip mark, so CoopModPatch's adapter can't load it.
    private static bool MissesServerBuild(string destination, string sourceDirectory, bool needed) =>
        needed && ServerModuleInstall.ServerDlls(sourceDirectory).Except(ServerModuleInstall.ServerDlls(destination), StringComparer.OrdinalIgnoreCase).Any();

    private static string ReadXml(string path, out bool bom)
    {
        var raw = File.ReadAllBytes(path);
        bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        return new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
    }

    // Switches client code off in an extra's server copy. Never called for a core module.
    private static void TurnOffClientCode(string moduleDirectory, string moduleId)
    {
        var xmlPath = Path.Combine(moduleDirectory, "SubModule.xml");
        if (IsCore(moduleId) || !File.Exists(xmlPath))
            return;
        var raw = File.ReadAllBytes(xmlPath);
        var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        var xml = new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
        var dlls = ServerModuleInstall.ServerDlls(moduleDirectory);
        if (ServerModuleInstall.Classify(xml, dlls, moduleId) != ServerCode.ClientCodeOnly)
            return;
        var patched = ServerModuleInstall.TurnOffClientCode(xml, dlls, moduleId);
        if (patched != xml)
            File.WriteAllText(xmlPath, patched, new UTF8Encoding(bom));
    }

    // ----- Saves -----

    /// <summary>Null when the save remembers the same mods as the server list (or remembers none).</summary>
    public static string? SaveProblem(string savePath, IReadOnlyList<string> serverOrder)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        return info == null ? null : CoopJoinRules.SaveProblem(info.Modules, serverOrder);
    }

    /// <summary>Whether the server list only adds mods to the save (ask), leaves one out (warn),
    /// or matches. A save that cannot be read starts as it is.</summary>
    public static SaveModChange SaveChange(string savePath, IReadOnlyList<string> serverOrder)
    {
        var info = CampaignSaveInfoReader.Read(savePath);
        return SaveModChange.Check(info?.Modules ?? Array.Empty<string>(), serverOrder);
    }

    // ----- Helpers -----

    internal static string? HashProblem(string path, string sha256)
    {
        if (!File.Exists(path))
            return Path.GetFileName(path) + " is missing.";
        return Sha256Files.Hash(path).Equals(sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : Path.GetFileName(path) + " is not the file this tool was made for.";
    }

    internal static string VersionOf(string moduleDirectory)
    {
        var xml = Path.Combine(moduleDirectory, "SubModule.xml");
        try
        {
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    internal static string Short(string version) => version.Trim().TrimStart('v', 'V');
}
