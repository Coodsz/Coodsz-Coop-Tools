using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CoodszHost.Services;

public static class CoopServerBin
{
    public const string Restored =
        "The dedicated server was missing its Coop server files. They were copied from the Workshop folder. The Coop folder in the game Modules folder was left alone.";

    public const string ClientCopyRefused =
        "The dedicated server is missing Coop.Core.dll in DedicatedServer\\engine\\Modules\\Coop\\bin\\Win64_Shipping_Server. The Workshop client copy was not used because it is not the build this server checks for, and the server would stop with COOP MODULE VERIFICATION FAILED. In Steam, verify the Bannerlord files so Workshop item 3770450698 downloads again, then press Start server again.";

    public const string HarmonyRestored =
        "The packaged server 0Harmony.dll was missing. It was copied into engine\\bin\\Win64_Shipping_Server.";

    public const string StillMissing =
        "The dedicated server is missing Coop.Core.dll in DedicatedServer\\engine\\Modules\\Coop\\bin\\Win64_Shipping_Server. The Coop folder in the game Modules folder is the client, so it was not copied. In Steam, verify Workshop item 3770450698 and press Start server again.";

    public const string HarmonyStillMissing =
        "The dedicated server is missing engine\\bin\\Win64_Shipping_Server\\0Harmony.dll. Steam often installs a thin DedicatedServer without it. Mods that need Harmony will not start. Put 0Harmony.dll in that folder (same place as TaleWorlds.Starter.DotNetCore.dll), then press Start server again.";

    public const string StoryModeCopied =
        "The dedicated server had no StoryMode.dll in engine\\bin\\Win64_Shipping_Server, which Coop 0.1.7's server leaves out. It was copied from the game, since mods like Improved Garrisons need it.";

    // The game's Bannerlord v1.4.8 StoryMode.dll (the TAOM server tool pins the same file, ServerProgramRepair.ModuleFiles).
    // It needs only SandBox and TaleWorlds files the server already loads.
    public const long StoryModeLength = 535000;
    public const string StoryModeSha256 = "cc205186bca26ea04197c543f06bfd14ec7ca8a94b559a3a2753921c4bb27ef0";

    public static string? Ensure(string serverRoot) => Ensure(serverRoot, GameModulesOrNull());

    /// <param name="gameModules">The game's Modules folder StoryMode.dll comes from; null skips that copy.</param>
    public static string? Ensure(string serverRoot, string? gameModules)
    {
        var notes = new List<string>();
        var coop = EnsureCoopBin(serverRoot);
        if (coop != null)
            notes.Add(coop);
        var healed = RestorePatchedCoop(serverRoot);
        if (healed != null)
            notes.Add(healed);
        var clientCopy = ClientCoopInServer(serverRoot);
        if (clientCopy != null)
            notes.Add(clientCopy);
        var wrong = WrongCoopFiles(serverRoot);
        if (wrong.Count > 0)
            throw new InvalidOperationException(WrongCoopMessage(serverRoot, wrong));
        var harmony = EnsureHarmony(serverRoot);
        if (harmony != null)
            notes.Add(harmony);
        var storyMode = EnsureStoryMode(serverRoot, gameModules);
        if (storyMode != null)
            notes.Add(storyMode);
        // One note per line: Translator matches each whole note, and a sentence split of several in one line would not.
        return notes.Count == 0 ? null : string.Join("\n", notes);
    }

    private static string? EnsureCoopBin(string serverRoot)
    {
        var destination = Path.Combine(serverRoot, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
        var needed = Path.Combine(destination, "Coop.Core.dll");
        if (File.Exists(needed))
            return null;

        var source = DedicatedServerFinder.WorkshopClientBin(serverRoot);
        if (source == null)
            throw new InvalidOperationException(StillMissing);

        // The server refuses any Coop build other than the one it pins in
        // release-info.txt (exit code 4). The Workshop client Coop.Core.dll is a
        // different build, so it is only copied when its bytes match those pins.
        var pins = PinnedHashes(serverRoot);
        if (pins.Count == 0)
            throw new InvalidOperationException(NoPinList);
        if (pins.Any(pin => !HashMatches(Path.Combine(source, pin.Key), pin.Value)))
            throw new InvalidOperationException(ClientCopyRefused);

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target);
        }

        if (!File.Exists(needed))
            throw new InvalidOperationException(StillMissing);
        return Restored;
    }

    // Coodsz Coop and Friends before 1.2.28 patched every Coop.Core.dll under
    // the Workshop folder for the server password, the dedicated server's copy
    // included, and kept the original as Coop.Core.dll.pre-password.bak. That
    // original still matches the pin, so it is put back.
    public static string? RestorePatchedCoop(string serverRoot)
    {
        var folder = CoopServerBinFolder(serverRoot);
        var restored = new List<string>();
        foreach (var pin in PinnedHashes(serverRoot))
        {
            var file = Path.Combine(folder, pin.Key);
            var backup = file + ".pre-password.bak";
            if (HashMatches(file, pin.Value) || !HashMatches(backup, pin.Value))
                continue;
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Copy(backup, file, overwrite: true);
                restored.Add(pin.Key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return restored.Count == 0
            ? null
            : "Put back the original " + string.Join(", ", restored) + " in the dedicated server. An older version of the client launcher had changed it, which made Start server fail with COOP MODULE VERIFICATION FAILED.";
    }

    public static string CoopServerBinFolder(string serverRoot) =>
        Path.Combine(serverRoot, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");

    public const string NoPinList =
        "The dedicated server is missing Coop.Core.dll, and its release-info.txt (which lists the Coop files this server accepts) is missing too, so no Coop files were copied. In Steam, verify the Bannerlord files so Workshop item 3770450698 downloads again, then press Start server again.";

    // The SHA-256 pins listed under "Pinned Coop assemblies" in release-info.txt:
    // the lines after that heading, up to the first blank line.
    public static IReadOnlyDictionary<string, string> PinnedHashes(string serverRoot)
    {
        var info = Path.Combine(serverRoot, "release-info.txt");
        if (!File.Exists(info))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return ParsePins(File.ReadAllText(info));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static IReadOnlyDictionary<string, string> ParsePins(string releaseInfo)
    {
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;
        foreach (var raw in releaseInfo.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (!inSection)
            {
                inSection = line.StartsWith("Pinned Coop assemblies", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Length == 0)
                break;
            var match = Regex.Match(line, @"^(?<file>[A-Za-z0-9_.]+\.dll)\s+(?<hash>[0-9a-fA-F]{64})$");
            if (match.Success)
                pins[match.Groups["file"].Value] = match.Groups["hash"].Value.ToLowerInvariant();
        }

        return pins;
    }

    // Pinned Coop files that are missing or do not match release-info.txt.
    public static IReadOnlyList<string> WrongCoopFiles(string serverRoot)
    {
        var folder = CoopServerBinFolder(serverRoot);
        return PinnedHashes(serverRoot)
            .Where(pin => !HashMatches(Path.Combine(folder, pin.Key), pin.Value))
            .Select(pin => pin.Key)
            .ToList();
    }

    public static string WrongCoopMessage(string serverRoot, IReadOnlyList<string> wrong)
    {
        var names = string.Join(", ", wrong);
        var client = DedicatedServerFinder.WorkshopClientBin(serverRoot);
        var fromClient = client != null && wrong.Any(file =>
        {
            var server = Path.Combine(CoopServerBinFolder(serverRoot), file);
            var copy = Path.Combine(client, file);
            var serverHash = Sha256(server);
            return serverHash.Length > 0 && File.Exists(copy) && serverHash == Sha256(copy);
        });
        return "The server was not started, because it would stop with COOP MODULE VERIFICATION FAILED. "
            + names + " in DedicatedServer\\engine\\Modules\\Coop\\bin\\Win64_Shipping_Server "
            + (wrong.Count == 1 ? "is" : "are") + " not the Coop build this server was released with. "
            + (fromClient ? "It is the Workshop client copy. Older Coodsz Coop Server Tool versions put it there when the server file was missing. " : "")
            + "What to do: In Steam, open Bannerlord Properties, Installed Files, and click Verify integrity of game files. Wait until Workshop item 3770450698 finishes, then press Start server again. If this message comes back, unsubscribe from Workshop item 3770450698, subscribe again, wait for the download, and press Start server. "
            + "What not to do: Do not copy Coop files from the game Modules folder or the Workshop bin folder into the dedicated server.";
    }

    public const string ClientCoopInWorkshopServer =
        "The server's own Coop folder, DedicatedServer\\engine\\Modules\\Coop, has a game (client) Coop copied into it. The server can't load that copy and stops with Assembly with same name is already loaded. To fix it, unsubscribe from Workshop item 3770450698 in Steam, subscribe again, wait for the download, then press Start server. Steam's Verify does not repair Workshop items. Do not copy Coop files into the DedicatedServer folder.";

    public const string ClientCoopInOwnServer =
        "The server's own Coop folder, DedicatedServer\\engine\\Modules\\Coop, has a game (client) Coop copied into it. The server can't load that copy and stops with Assembly with same name is already loaded. To fix it, download the Coop server again and put its DedicatedServer folder in place of this one, then press Start server. Your saves are in Documents and stay. Do not copy Coop files into the DedicatedServer folder.";

    /// <summary>
    /// A game (client) Coop copied over the server's own engine\Modules\Coop stops the server with "FileLoadException:
    /// Assembly with same name is already loaded" from CoopDriver.EnsureLoaded (AD1259 crash report #19, CR-SR59T), while
    /// the pinned server files still match. The server Coop of 0.1.6 and 0.1.7 ships only bin\Win64_Shipping_Server, so a
    /// client Coop.Core.dll there is a copy. A SubModule.xml version alone says nothing: it can differ from the Workshop
    /// item's own while Steam updates the item. Nothing is moved.
    /// </summary>
    public static string? ClientCoopInServer(string serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return null;
        var coop = Path.Combine(serverRoot, "engine", "Modules", "Coop");
        if (!File.Exists(Path.Combine(coop, "bin", "Win64_Shipping_Client", "Coop.Core.dll")))
            return null;
        return InCoopWorkshopItem(serverRoot) ? ClientCoopInWorkshopServer : ClientCoopInOwnServer;
    }

    private static bool InCoopWorkshopItem(string serverRoot) =>
        (serverRoot.Replace('/', '\\').TrimEnd('\\') + "\\").Contains(@"\workshop\content\261550\" + DedicatedServerFinder.CoopWorkshopId + @"\", StringComparison.OrdinalIgnoreCase);

    private static bool HashMatches(string path, string expected) =>
        File.Exists(path) && Sha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase);

    // An empty string when the file cannot be read (locked by antivirus, for example).
    public static string Sha256(string path) =>
        BannerlordCoopLauncher.Core.IO.FileHash.TrySha256(path).ToLowerInvariant();

    /// <summary>
    /// The server loads no StoryMode module, so a mod that uses a StoryMode type looks for StoryMode.dll in the engine
    /// bin. Coop 0.1.7's server has none there, and Improved Garrisons stopped a new campaign with FileNotFoundException
    /// (Coop crash report #73, CR-N8TBU). The game's own copy is put there when missing; anything else is left alone.
    /// </summary>
    public static string? EnsureStoryMode(string serverRoot, string? gameModules)
    {
        try
        {
            var bin = Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server");
            var target = Path.Combine(bin, "StoryMode.dll");
            if (File.Exists(target) || !Directory.Exists(bin) || string.IsNullOrWhiteSpace(gameModules))
                return null;
            var source = Path.Combine(gameModules, "StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll");
            var info = new FileInfo(source);
            if (!info.Exists || info.Length != StoryModeLength || Sha256(source) != StoryModeSha256)
                return null;
            File.Copy(source, target, overwrite: false);
            return StoryModeCopied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a help for some mods: a locked or unreadable file never stops the start.
            return null;
        }
    }

    private static string? GameModulesOrNull()
    {
        try
        {
            return ExtraModScan.GameModulesDirectory();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? EnsureHarmony(string serverRoot)
    {
        var harmony = Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll");
        if (File.Exists(harmony))
            return null;

        var source = FindHarmonySource(serverRoot);
        if (source == null)
            throw new InvalidOperationException(HarmonyStillMissing);

        Directory.CreateDirectory(Path.GetDirectoryName(harmony)!);
        File.Copy(source, harmony, overwrite: false);
        return HarmonyRestored;
    }

    private static string? FindHarmonySource(string serverRoot)
    {
        foreach (var candidate in HarmonyCandidates(serverRoot))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> HarmonyCandidates(string serverRoot)
    {
        var harmonyRoot = Path.Combine(serverRoot, "engine", "Modules", "Bannerlord.Harmony");
        yield return Path.Combine(harmonyRoot, "bin", "Win64_Shipping_Server", "0Harmony.dll");
        yield return Path.Combine(harmonyRoot, "bin", "Win64_Shipping_Client", "0Harmony.dll");
        yield return Path.Combine(harmonyRoot, "bin", "Gaming.Desktop.x64_Shipping_Client", "0Harmony.dll");

        var workshopClient = DedicatedServerFinder.WorkshopClientBin(serverRoot);
        if (workshopClient != null)
            yield return Path.Combine(workshopClient, "0Harmony.dll");
    }
}
