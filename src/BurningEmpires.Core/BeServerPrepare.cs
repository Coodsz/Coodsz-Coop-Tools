using System.Text;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>Repaired is what laying the tool's own server files over the folder changed, or null.</summary>
public sealed record BePrepareResult(int Copied, int Same, IReadOnlyList<string> Lines, string? Repaired = null);

/// <summary>Where Prepare server copies the Coop server from: the Workshop server, or a folder the host picked (Picked).</summary>
public sealed record BeServerSource(string Folder, bool Picked);

/// <summary>
/// Prepare server for Burning Empires: a whitelist copy of the Workshop Coop dedicated server
/// (workshop\content\261550\3770450698\DedicatedServer) into the tool's own server folder. Only
/// the engine, the server exe and its scripts, the official modules, DedicatedServer.Windows and
/// the paired Coop are copied. Mods other tools put in the Workshop folder (removed-mods, AD1259,
/// RBM and so on), its module lists, its module policy and any .bak or .coodsz-old file stay out,
/// because a PC's Workshop folder can be hand edited. A file is copied when it is missing or its
/// size or time differ, and nothing in the server folder is ever deleted, so the Burning Empires
/// module and its BESavedata survive every prepare. The Workshop server checks the SHA of its
/// paired Coop at boot, so the server files and Coop are always copied together. Coodsz Host's own
/// server files (its DedicatedServerRepair folder, shipped next to this tool) are laid over the
/// copy afterwards, the same way Coodsz Host does on every start (ServerFolderRepair).
/// </summary>
public static class BeServerPrepare
{
    public const string ScriptName = "Start-ModdedServer.ps1";
    public const string RuntimeBootstrap = "BannerlordCoop.RuntimeBootstrap.dll";
    public const string PolicyFile = "server-module-policy.json";
    public const string TemplateSave = "default_new_game.sav";

    /// <summary>Files from the top of the Workshop server folder.</summary>
    public static readonly IReadOnlyList<string> TopFiles =
    [
        "BannerlordCoopServer.exe",
        RuntimeBootstrap,
        ScriptName,
        "Start-ModdedServer.cmd",
        "Server-Console.ps1",
        "SERVER-CONSOLE.bat",
        "CHECKSUMS.sha256",
        "EULA.md",
        "EULA.txt",
        "LICENSE.txt",
        "release-info.txt"
    ];

    /// <summary>Whole folders under engine.</summary>
    public static readonly IReadOnlyList<string> EngineFolders = ["bin", "dotnet", "Parameters", "XmlSchemas"];

    /// <summary>Modules under engine\Modules: the official ones, the server host module and Coop.</summary>
    public static readonly IReadOnlyList<string> Modules =
    [
        "Native",
        "SandBoxCore",
        "SandBox",
        "StoryMode",
        "CustomBattle",
        "DedicatedServer.Windows",
        BeServerPath.CoopModuleId
    ];

    /// <summary>The button the Prepare messages send the host back to.</summary>
    public const string PrepareButton = "Prepare server";

    // The antivirus and Workshop download messages are the ones every tool shows (AntivirusText).
    public static string NoWorkshopServer => AntivirusText.NoWorkshopCoop(PrepareButton);

    /// <summary>The button that picks a Coop dedicated server the host unzipped himself (Coop 0.1.7's server
    /// download is linked at the bottom of Coop's release notes; the Workshop Coop has its own since 2026-10-30).</summary>
    public const string PickButton = "Choose Coop server";

    public const string PickedNotServer = CoopInstall.NotServerFolder;

    public const string PickedInsideServer =
        "Pick a Coop server folder outside this tool's server folder. Prepare server copies from the folder you pick into the server folder.";

    /// <summary>The folder the host picked lost an engine file Start needs (dotnet.exe or the starter).</summary>
    public static string PickedEngineMissing(string pickedServer, string missing) =>
        "The Coop server folder you picked is missing " + AntivirusFiles.Shown(missing, pickedServer)
        + ". Unzip the Coop server package there again, then click " + PrepareButton + " again.";

    public const string PickedChangedNote =
        "The Coop server folder you picked has another Coop now. The server files and Coop were copied again together, because the server only runs with its own Coop.";

    /// <summary>
    /// The Coop dedicated server in a folder the host picked: the folder itself, its DedicatedServer folder, or the one
    /// folder inside it that holds the server, whichever holds BannerlordCoopServer.exe and the engine folder
    /// (CoopInstall.ServerFolderIn, which CoodszHost's ExeInFolder takes too). Null when none does.
    /// </summary>
    public static string? PickedServer(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        try
        {
            // The folder, its DedicatedServer folder, or the one folder inside it that holds the server (CoopInstall.ServerFolderIn).
            if (CoopInstall.ServerFolderIn(folder) is { } candidate && Directory.Exists(Path.Combine(candidate, "engine")))
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return null;
    }

    /// <summary>The version of the Coop a Coop server folder carries (engine\Modules\Coop), or "".</summary>
    public static string ServerCoopVersion(string server)
    {
        var xml = Path.Combine(server, "engine", "Modules", BeServerPath.CoopModuleId, "SubModule.xml");
        try
        {
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Where Prepare server and Start server copy the Coop server from. The folder the host picked when it
    /// holds a Coop server, unless the Workshop server's Coop is newer by then (Coop reached the Workshop
    /// after the host unzipped it). Otherwise the Workshop server, the default. Null when there is neither.
    /// </summary>
    public static BeServerSource? Source(string? pickedFolder, string? workshopServer)
    {
        var picked = PickedServer(pickedFolder);
        // An unreadable version is unknown, not older: the picked server stays unless the Workshop's Coop is surely newer.
        if (picked != null && (workshopServer == null || !CoopInstall.KnownNewer(ServerCoopVersion(workshopServer), ServerCoopVersion(picked))))
            return new BeServerSource(picked, Picked: true);
        return workshopServer == null ? null : new BeServerSource(workshopServer, Picked: false);
    }

    /// <summary>True when the host picked a Coop server folder (Choose Coop server) that no longer holds one: it was
    /// deleted or moved, or lost BannerlordCoopServer.exe or its engine folder. Source then falls back to the Workshop.</summary>
    public static bool PickedGone(string? pickedFolder) => !string.IsNullOrWhiteSpace(pickedFolder) && PickedServer(pickedFolder) == null;

    public const string PickedGoneNote = "The Coop server folder you picked is gone, so the Steam Workshop server is used.";

    /// <summary>The Coop version crash and mod reports send: the server folder's own Coop, else the version the set was tested with.</summary>
    public static string ReportCoopVersion(string? serverRoot) =>
        !string.IsNullOrWhiteSpace(serverRoot) && ServerCoopVersion(serverRoot) is { Length: > 0 } version ? version : BeStack.CoopVersion;

    /// <summary>
    /// True when the server folder runs a Coop surely newer than the Workshop server's (a Coop 0.1.7 copied from a picked
    /// folder that is gone now). Start server never copies the Workshop server over it by itself, and Prepare server
    /// asks first: that would put the older Coop back. An unreadable version on either side is unknown, so this is
    /// false and Start server updates the server from the Workshop as before.
    /// </summary>
    public static bool ServerIsNewerThan(string serverRoot, string workshopServer) =>
        CoopInstall.KnownNewer(ServerCoopVersion(serverRoot), ServerCoopVersion(workshopServer));

    /// <summary>Start server's line when it keeps a server Coop newer than the Workshop's (ServerIsNewerThan).</summary>
    public static string KeptNewerNote(string serverRoot, string workshopServer) =>
        "The server keeps Coop " + ServerCoopVersion(serverRoot) + ", which is newer than the Steam Workshop Coop " + ServerCoopVersion(workshopServer) + ", so the Workshop server was not copied over it.";

    /// <summary>
    /// Where Start puts back a missing server module DLL from (BeServerModuleRepair): the Coop server Prepare server copies,
    /// unless the server runs a newer Coop than that one (it kept the newer Coop after a picked folder was gone). Files from
    /// the older package would mix two Coop versions, so then there is no repair source.
    /// </summary>
    public static string? RepairSource(string serverRoot, BeServerSource? source) =>
        source == null || ServerIsNewerThan(serverRoot, source.Folder) ? null : source.Folder;

    public const string RepairSourceSkipped =
        "The server runs a newer Coop than the Coop server Prepare server would copy, so missing server files are not put back from it. Choose the matching Coop server again with Choose Coop server.";

    /// <summary>Prepare server's question before it copies an older Workshop Coop over a newer server Coop.</summary>
    public static string DowngradeQuestion(string serverRoot, string workshopServer) =>
        "This puts Coop " + ServerCoopVersion(serverRoot) + " back to " + ServerCoopVersion(workshopServer) + ". Continue?";

    /// <summary>Start server's stop when it would copy an older Coop over the server's (ServerIsNewerThan): from the
    /// Workshop server, or from a picked folder older than the server's Coop.</summary>
    public static string AutoPrepareKeptNewer(string serverRoot, string source, bool picked = false) => picked
        ? "The server runs Coop " + ServerCoopVersion(serverRoot) + ", which is newer than Coop " + ServerCoopVersion(source)
            + " in the Coop server folder you picked, so Start server did not copy it over the server. Pick the newer Coop server again with " + PickButton + ", or click Prepare server to use the older one anyway."
        : "The server runs Coop " + ServerCoopVersion(serverRoot) + ", which is newer than the Steam Workshop Coop " + ServerCoopVersion(source)
            + ", so Start server did not copy the Workshop server over it. Pick the newer Coop server again with " + PickButton + ", then click Start server.";

    /// <summary>The status after Choose Coop server when the Workshop's Coop is newer than the folder picked (Source).</summary>
    public static string PickedButWorkshopNewer(string picked, string workshopServer) =>
        "The Steam Workshop Coop server (Coop " + ServerCoopVersion(workshopServer) + ") is newer than the one in " + picked
        + ", so Prepare server copies the Workshop one.";

    public const string PickedNoScript =
        "The Coop server folder you picked has no Start-ModdedServer.ps1, and this tool's own copy is missing too. Unzip the Coop server package there again, then click Prepare server again.";

    /// <summary>The thin Workshop Coop server has no Start-ModdedServer.ps1, so Prepare takes the tool's own
    /// copy. When that is gone too, subscribing again doesn't help.</summary>
    public static string ToolScriptMissing(string repairPayload) =>
        AntivirusText.Taken(Path.Combine(repairPayload, ScriptName), PrepareButton);

    /// <summary>Prepare refuses a Workshop Coop server without the engine Start needs (engine\dotnet\dotnet.exe
    /// and the starter), which a download Steam left half done or antivirus took a file out of gives.
    /// Without this check Prepare said it was done and Start then said dotnet.exe was missing.</summary>
    public static string WorkshopEngineMissing(string workshopServer, string missing) =>
        AntivirusText.WorkshopIncomplete(AntivirusFiles.Shown(missing, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workshopServer)))), PrepareButton);

    /// <summary>Start server's note when Coop changed on the Workshop but its download has no engine files
    /// yet: the server keeps the files it has instead of being prepared from a half download.</summary>
    public const string WorkshopUnfinishedNote =
        "Bannerlord Coop's Workshop download isn't complete yet, so the server keeps the files it has. Let Steam finish the download before the next start.";

    /// <summary>The line when an engine file Prepare just copied is gone again: antivirus took it.</summary>
    public static string EngineTakenText(string missing, string serverRoot) =>
        AntivirusText.Taken(AntivirusFiles.Shown(missing, serverRoot), PrepareButton);

    /// <summary>The first engine file (AntivirusFiles.Engine) missing from a server folder, or null.</summary>
    public static string? MissingEngineFile(string serverRoot) =>
        AntivirusFiles.Engine(serverRoot).FirstOrDefault(file => !File.Exists(file));

    /// <summary>The files the antivirus watch remembers after a good Prepare or Start: the server folder's engine,
    /// the DLLs the tool's own server files put there, and the Workshop Coop server files Prepare copies from
    /// (the ones AntivirusFiles.WorkshopServerProblem checks).</summary>
    public static List<string> WatchedFiles(string serverRoot, string? workshopCoop, string? repairPayload)
    {
        var files = new List<string>(AntivirusFiles.Engine(serverRoot));
        files.AddRange(AntivirusFiles.RepairPayloadDlls(serverRoot, repairPayload));
        if (workshopCoop != null)
        {
            files.Add(AntivirusFiles.WorkshopServerExe(workshopCoop));
            files.AddRange(AntivirusFiles.Engine(Path.Combine(workshopCoop, "DedicatedServer")));
        }

        return files;
    }

    /// <summary>The Workshop Coop dedicated server folder in one of these Steam libraries.</summary>
    public static string? FindWorkshopServer(IEnumerable<string> steamLibraries) =>
        CoopStaging.FindDedicatedServer(steamLibraries);

    /// <summary>Why the Workshop Coop server can't be prepared from, or null when it can: no Workshop Coop at all,
    /// a download Steam is still on, or a Workshop Coop without its server program or engine files (antivirus often
    /// takes DedicatedServer\BannerlordCoopServer.exe right after the host ran it). gone is what the antivirus watch
    /// saw there before and is gone now, so only those are called antivirus (CR-88BP7).</summary>
    public static string? WorkshopServerProblem(IEnumerable<string> steamLibraries, IReadOnlyCollection<string>? gone = null) =>
        AntivirusFiles.WorkshopServerProblem(CoopStaging.FindWorkshopModule(steamLibraries), PrepareButton, gone, steamLibraries);

    // Windows' MAX_PATH is 260 with the closing null, so a full path has at most 259 characters. The
    // deepest file of a prepared server is 132 characters under the server folder (engine\Modules\
    // Bannerlord.ButterLib\bin\Gaming.Desktop.x64_Shipping_Client\Microsoft.Extensions.
    // DependencyInjection.Abstractions.dll); the deepest one in the Workshop server whitelist is the
    // 120 character engine\bin\Win64_Shipping_Server\Microsoft.AspNetCore.App\Microsoft.Extensions.
    // Diagnostics.HealthChecks.Abstractions.dll. A 157 character server folder made the engine stop
    // at init ("Could not find ... engine\bin\Win64_Shipping_Server\...dll", exit -1073740791). 20
    // characters stay spare for deeper files in a later Coop, Burning Empires or BUTR update.
    public const int MaxPath = 259;
    public const int DeepestRelativePath = 132;
    public const int SpareCharacters = 20;
    public const int MaxServerFolderLength = MaxPath - 1 - DeepestRelativePath - SpareCharacters;

    /// <summary>The short folder the messages suggest (the default server folder on C:).</summary>
    public static string ShortFolderExample => BeHostFolders.DefaultServerRoot("C:\\");

    /// <summary>The length the server folder path has once it is a full path.</summary>
    public static int FolderLength(string serverRoot)
    {
        try
        {
            return Path.GetFullPath(serverRoot).TrimEnd('\\', '/').Length;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return serverRoot.TrimEnd('\\', '/').Length;
        }
    }

    public static bool FolderIsTooLong(string serverRoot) => FolderLength(serverRoot) > MaxServerFolderLength;

    /// <summary>Why the server folder path is too long for the server's deepest files, or null when it fits.</summary>
    public static string? FolderTooLong(string serverRoot) =>
        FolderIsTooLong(serverRoot) ? TooLongText(FolderLength(serverRoot)) : null;

    public static string TooLongText(int length) =>
        "The server folder path is " + length + " characters long, and the most that works is " + MaxServerFolderLength
        + ". The server cannot load its files from a folder that deep.";

    /// <summary>Null when the server folder can start; otherwise what is missing.</summary>
    public static string? MissingPiece(string serverRoot)
    {
        if (FolderTooLong(serverRoot) is { } tooLong)
            return tooLong + " Click Prepare server to use a short folder like " + ShortFolderExample + ".";
        var engine = DedicatedEngineLaunch.MissingPiece(serverRoot);
        if (engine != null)
            return engine + " Click Prepare server.";
        if (!File.Exists(Path.Combine(serverRoot, ScriptName)))
            return ScriptName + " is missing from the server folder. Click Prepare server.";
        if (!File.Exists(Path.Combine(serverRoot, RuntimeBootstrap)))
            return RuntimeBootstrap + " is missing from the server folder. Click Prepare server.";
        foreach (var module in Modules.Where(id => id is not ("StoryMode" or "CustomBattle")))
        {
            if (!File.Exists(Path.Combine(serverRoot, "engine", "Modules", module, "SubModule.xml")))
                return module + " is missing from the server folder. Click Prepare server.";
        }

        return null;
    }

    /// <summary>The tool's name in the line about the server files it laid over the folder.</summary>
    public const string ToolName = "Coodsz Burning Empires Server Tool";

    /// <summary>
    /// Lays Coodsz Host's own server files over the server folder (ServerFolderRepair): its
    /// Start-ModdedServer.ps1, which knows -AllowAddedModules and takes any letter in a new campaign
    /// name, and the Launcher Cheats and Launcher Battle Size submodules the Workshop server lacks
    /// (the console queue, the player list and the queued save run through Launcher Cheats). An older
    /// script is kept as .coodsz-old. Start server runs it on every start, since a Coop update on the
    /// Workshop puts the original files back. Null when nothing changed or the tool has no copy.
    /// </summary>
    public static string? LayOverRepair(string serverRoot, string? payloadRoot = null) =>
        ServerFolderRepair.Ensure(serverRoot, payloadRoot, ToolName);

    /// <summary>Copies the whitelist from the Workshop server folder, or from the Coop server folder the host
    /// picked when picked is true (Source), then lays the tool's own server files over it (LayOverRepair) when
    /// repairPayload is given. Deletes nothing.</summary>
    public static BePrepareResult Prepare(string workshopServer, string serverRoot, IProgress<string>? progress = null, string? repairPayload = null, bool picked = false)
    {
        if (picked)
        {
            if (PickedServer(workshopServer) == null)
                throw new InvalidOperationException(PickedNotServer);
            if (SamePlace(workshopServer, serverRoot) || Inside(serverRoot, workshopServer) || Inside(workshopServer, serverRoot))
                throw new InvalidOperationException(PickedInsideServer);
            if (MissingEngineFile(workshopServer) is { } pickedMissing)
                throw new InvalidOperationException(PickedEngineMissing(workshopServer, pickedMissing));
        }

        // The Workshop Coop's DedicatedServer is thin: since the 2026-10-06 build it has no Start-ModdedServer.ps1
        // (nor the runtime bootstrap or 0Harmony), so a clean download was refused as no Workshop server at all
        // (raven, CR-X7WJQ). The tool's own copy is laid over below, so the Workshop only needs its script when
        // no tool copy is given.
        var script = File.Exists(Path.Combine(workshopServer, ScriptName))
            || (repairPayload != null && File.Exists(Path.Combine(repairPayload, ScriptName)));
        if (!Directory.Exists(Path.Combine(workshopServer, "engine")) || (!script && repairPayload == null))
            throw new InvalidOperationException(picked ? PickedNoScript : NoWorkshopServer);
        if (!script)
            throw new InvalidOperationException(ToolScriptMissing(repairPayload!));
        if (MissingEngineFile(workshopServer) is { } workshopMissing)
            throw new InvalidOperationException(WorkshopEngineMissing(workshopServer, workshopMissing));
        if (SamePlace(workshopServer, serverRoot) || Inside(serverRoot, workshopServer))
            throw new InvalidOperationException("Pick a server folder outside the Steam Workshop folder. The Workshop copy stays as Steam made it.");
        if (FolderTooLong(serverRoot) is { } tooLong)
            throw new InvalidOperationException(tooLong + " Use a short folder like " + ShortFolderExample + ".");

        Directory.CreateDirectory(serverRoot);
        var counts = new int[2];
        // The managed scripts the tool lays over the folder are not copied from the Workshop first,
        // or every prepare would put the Workshop copy back and replace it again.
        var laidOver = LaidOverFiles(serverRoot, repairPayload);
        foreach (var name in TopFiles)
        {
            var source = Path.Combine(workshopServer, name);
            if (File.Exists(source) && !laidOver.Contains(Path.Combine(serverRoot, name)))
                CopyIfChanged(source, Path.Combine(serverRoot, name), counts);
        }

        foreach (var folder in EngineFolders)
        {
            progress?.Report("Copying engine\\" + folder + ".");
            CopyTree(Path.Combine(workshopServer, "engine", folder), Path.Combine(serverRoot, "engine", folder), counts, laidOver);
        }

        foreach (var module in Modules)
        {
            var source = Path.Combine(workshopServer, "engine", "Modules", module);
            if (!Directory.Exists(source))
                continue;
            progress?.Report("Copying " + module + ".");
            CopyTree(source, Path.Combine(serverRoot, "engine", "Modules", module), counts, laidOver);
        }

        // The Workshop copy had the engine files (checked above), so one missing here now was copied and
        // taken again right away.
        if (MissingEngineFile(serverRoot) is { } taken)
            throw new InvalidOperationException(EngineTakenText(taken, serverRoot));

        var lines = new List<string>
        {
            picked
                ? counts[0] == 0
                    ? "The server folder already matches the Coop server folder you picked."
                    : "Copied " + counts[0] + " files from the Coop server folder you picked. " + counts[1] + " were already there."
                : counts[0] == 0
                    ? "The server folder already matches the Workshop Coop server."
                    : "Copied " + counts[0] + " files from the Workshop Coop server. " + counts[1] + " were already there."
        };
        var repaired = repairPayload == null ? null : LayOverRepair(serverRoot, repairPayload);
        if (repaired != null)
            lines.Add(repaired);
        return new BePrepareResult(counts[0], counts[1], lines, repaired);
    }

    // The full paths in the server folder that the tool's own copy replaces (ServerFolderRepair.ManagedScripts).
    private static HashSet<string> LaidOverFiles(string serverRoot, string? repairPayload)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (repairPayload == null)
            return files;
        foreach (var relative in ServerFolderRepair.ManagedScripts)
        {
            if (File.Exists(Path.Combine(repairPayload, relative)))
                files.Add(Path.Combine(serverRoot, relative));
        }

        return files;
    }

    /// <summary>True when the Workshop Coop or its server changed since the last prepare
    /// (release-info.txt or Coop.Core.dll differ). Prepare again copies both together.</summary>
    public static bool CoopChanged(string workshopServer, string serverRoot)
    {
        foreach (var relative in new[]
                 {
                     "release-info.txt",
                     Path.Combine("engine", "Modules", BeServerPath.CoopModuleId, "bin", "Win64_Shipping_Server", "Coop.Core.dll")
                 })
        {
            var source = Path.Combine(workshopServer, relative);
            var copy = Path.Combine(serverRoot, relative);
            if (File.Exists(source) != File.Exists(copy))
                return true;
            if (File.Exists(source) && !SameBytes(source, copy))
                return true;
        }

        return false;
    }

    public const string CoopChangedNote =
        "Bannerlord Coop was updated on the Steam Workshop. The server files and Coop were copied again together, because the server only runs with its own Coop.";

    /// <summary>
    /// The Data folder, server-config.json, mod-config.json and the new campaign template. Files
    /// that are there are left as they are. Returns one line per file made.
    /// </summary>
    public static IReadOnlyList<string> EnsureData(string serverRoot, string dataRoot, string coopDataRoot)
    {
        var lines = new List<string>();
        Directory.CreateDirectory(BeHostFolders.GameSaves(dataRoot));
        Directory.CreateDirectory(coopDataRoot);

        var config = BeHostFolders.ServerConfig(dataRoot);
        if (!File.Exists(config))
        {
            File.WriteAllText(config, DefaultServerConfig, new UTF8Encoding(false));
            lines.Add("Made server-config.json in the Data folder.");
        }

        var modConfig = BeHostFolders.ModConfig(coopDataRoot);
        if (!File.Exists(modConfig))
        {
            var packaged = Path.Combine(serverRoot, "engine", "Modules", BeServerPath.CoopModuleId, "mod-config.default.json");
            if (File.Exists(packaged))
                File.Copy(packaged, modConfig);
            else
                File.WriteAllText(modConfig, "{\"modOptions\":{}}" + Environment.NewLine, new UTF8Encoding(false));
            lines.Add("Made mod-config.json in CoopData.");
        }

        // Start-ModdedServer.ps1 -NewSaveName starts a new campaign from this template; the
        // Burning Empires Coop Compat module turns it into a fresh Burning Empires campaign.
        var template = Path.Combine(BeHostFolders.GameSaves(dataRoot), TemplateSave);
        var packagedTemplate = Path.Combine(DedicatedEngineLaunch.ServerBin(serverRoot), TemplateSave);
        if (!File.Exists(template) && File.Exists(packagedTemplate))
        {
            File.Copy(packagedTemplate, template);
            lines.Add("Put the new campaign template in Game Saves.");
        }

        return lines;
    }

    // The keys Coop's server reads from server-config.json. Coop makes the same file itself on
    // its first start; the tool makes it earlier so the password and save can be set first. No
    // battleSize: the server does not know that key, and Start server passes -BattleSize from
    // mod-config.json (BeServerLaunch.StartBattleSize). No autoRestart or autoRestartHours either:
    // nothing acts on them and the server logs them as unknown keys.
    public const string DefaultServerConfig =
        "{\r\n"
        + "  \"saveName\": \"default_new_game\",\r\n"
        + "  \"autosaveMinutes\": 15,\r\n"
        + "  \"password\": \"\",\r\n"
        + "  \"port\": 4200,\r\n"
        + "  \"logFile\": true,\r\n"
        + "  \"steam\": true,\r\n"
        + "  \"allowClientTimeControls\": true\r\n"
        + "}\r\n";

    /// <summary>
    /// The tool's own server-module-policy.json, written on every prepare so a hand edited one
    /// from the Workshop folder never applies. MCM is loaded headless (its settings core only),
    /// the way the Burning Empires live test server ran. The live test will confirm on a clean
    /// Workshop server that this is all Burning Empires needs.
    ///
    /// Burning Empires Coop Compat is a rolling save version module, like CoopModPatch in Coodsz
    /// Host's policy. After a clean stop the script records each module's exact version next to the
    /// save and hides the save when one differs. The tool stages its own compat module on every start,
    /// so without this the first tools release with a newer compat would hide every cleanly stopped
    /// campaign. With it, the script sets the compat version in the save to the installed one.
    /// </summary>
    public static void WritePolicy(string serverRoot)
    {
        var path = Path.Combine(serverRoot, PolicyFile);
        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        if (current != Policy)
            File.WriteAllText(path, Policy, new UTF8Encoding(false));
    }

    public const string Policy =
        "{\r\n"
        + "  \"policySchemaVersion\": 3,\r\n"
        + "  \"allowUnsupportedMods\": false,\r\n"
        + "  \"forceAllSelectedGameplayModulesHeadless\": false,\r\n"
        + "  \"blockManagedAssemblyCollisions\": false,\r\n"
        + "  \"forceHeadlessModules\": [\r\n"
        + "    \"Bannerlord.MBOptionScreen\"\r\n"
        + "  ],\r\n"
        + "  \"rollingSaveVersionModules\": [\r\n"
        + "    \"" + BeStack.CompatModuleId + "\"\r\n"
        + "  ],\r\n"
        + "  \"strictDependencyValidation\": false,\r\n"
        + "  \"softDependencyModules\": [\r\n"
        + "    \"CustomBattle\",\r\n"
        + "    \"StoryMode\",\r\n"
        + "    \"BirthAndDeath\"\r\n"
        + "  ],\r\n"
        + "  \"allowedAssemblyCollisions\": []\r\n"
        + "}\r\n";

    private static void CopyTree(string source, string destination, int[] counts, HashSet<string> laidOver)
    {
        if (!Directory.Exists(source))
            return;
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (Skipped(file) || laidOver.Contains(target))
                continue;
            CopyIfChanged(file, target, counts);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            // A link in the Workshop folder could point anywhere; only real folders are copied.
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)), counts, laidOver);
        }
    }

    private static bool Skipped(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".bak.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".coodsz-old", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyIfChanged(string source, string destination, int[] counts)
    {
        var from = new FileInfo(source);
        var to = new FileInfo(destination);
        if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
        {
            counts[1]++;
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        File.SetLastWriteTimeUtc(destination, from.LastWriteTimeUtc);
        counts[0]++;
    }

    internal static bool SameBytes(string a, string b)
    {
        var first = new FileInfo(a);
        var second = new FileInfo(b);
        if (!first.Exists || !second.Exists || first.Length != second.Length)
            return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    private static bool SamePlace(string a, string b) =>
        Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
