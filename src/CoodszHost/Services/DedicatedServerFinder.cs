using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CoodszHost.Services;

public static class DedicatedServerFinder
{
    public const string CoopWorkshopId = "3770450698";
    // Coop 0.1.7's server is a .7z linked from Coop's release (CoopServerDownload) before it reaches the Workshop item. A short
    // folder, since the server engine crashes from a deep path (0xC0000409 at Module Initialize). Goes back to the
    // earlier text, which names both folders Find looks in, with the other "not on Steam yet" lines (CoopInstall.NotOnSteamYet).
    public const string MissingMessage = !BannerlordCoopLauncher.Core.Modules.CoopInstall.NotOnSteamYet
        ? "Bannerlord Coop's dedicated server was not found. Click Set server folder and choose BannerlordCoopServer.exe. It is in Modules\\Bannerlord Coop\\DedicatedServer, or in Workshop folder 3770450698\\DedicatedServer."
        : "Bannerlord Coop's dedicated server was not found. Coop v0.1.7 is not on Steam yet. Click Download Coop server under Advanced: server folder on the Settings page to get the Coop v0.1.7 dedicated server download (about 4.5 GB), linked at the bottom of Coop's release notes, not under Assets. Unzip BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z to a short folder like C:\\CoopServer, then click Set server folder and choose BannerlordCoopServer.exe in it. The Steam Workshop server is in Workshop folder 3770450698\\DedicatedServer.";

    public static string? Resolve(string? savedExecutable, IEnumerable<string> libraryRoots)
    {
        if (!string.IsNullOrWhiteSpace(savedExecutable) && File.Exists(savedExecutable))
            return savedExecutable;
        return Find(libraryRoots);
    }

    public static string? Find(IEnumerable<string> libraryRoots)
    {
        foreach (var library in libraryRoots)
        {
            if (string.IsNullOrWhiteSpace(library))
                continue;
            var inGame = GameExecutable(library);
            if (File.Exists(inGame))
                return inGame;
            var workshop = WorkshopExecutable(library, CoopWorkshopId);
            if (File.Exists(workshop))
                return workshop;
            var content = Path.Combine(library, "steamapps", "workshop", "content", "261550");
            if (!Directory.Exists(content))
                continue;
            foreach (var item in Directory.EnumerateDirectories(content))
            {
                var exe = Path.Combine(item, "DedicatedServer", "BannerlordCoopServer.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }

        return null;
    }

    public static string? ExeInFolder(string? folder)
    {
        // The folder, its DedicatedServer folder, or the one folder inside it that holds the server (a package unzipped
        // one folder too deep).
        return BannerlordCoopLauncher.Core.Modules.CoopInstall.ServerFolderIn(folder) is { } server
            ? Path.Combine(server, BannerlordCoopLauncher.Core.Modules.CoopInstall.ServerExeName)
            : null;
    }

    public static string? ResolveInstalled(string? savedExecutable)
    {
        if (!string.IsNullOrWhiteSpace(savedExecutable) && File.Exists(savedExecutable))
            return savedExecutable;
        return FindInstalled();
    }

    public static string? FindInstalled()
    {
        try
        {
            return Find(ReadSteamLibraries());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> ReadLibraries(string steamPath, string? libraryFoldersVdf)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(steamPath))
            paths.Add(steamPath);
        if (!string.IsNullOrWhiteSpace(libraryFoldersVdf))
        {
            foreach (Match match in Regex.Matches(libraryFoldersVdf, "\"path\"\\s+\"([^\"]+)\""))
                paths.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string GameExecutable(string libraryRoot) =>
        Path.Combine(libraryRoot, "steamapps", "common", "Mount & Blade II Bannerlord", "Modules", "Bannerlord Coop", "DedicatedServer", "BannerlordCoopServer.exe");

    public static string WorkshopExecutable(string libraryRoot, string workshopId) =>
        Path.Combine(libraryRoot, "steamapps", "workshop", "content", "261550", workshopId, "DedicatedServer", "BannerlordCoopServer.exe");

    public static string? WorkshopClientBin(string? serverRoot)
    {
        var beside = BesideDedicatedServer(serverRoot);
        if (beside != null)
            return beside;
        try
        {
            foreach (var library in ReadSteamLibraries())
            {
                var path = Path.Combine(library, "steamapps", "workshop", "content", "261550", CoopWorkshopId, "bin", "Win64_Shipping_Client");
                if (File.Exists(Path.Combine(path, "Coop.Core.dll")))
                    return path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? BesideDedicatedServer(string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return null;
        var parent = Directory.GetParent(serverRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
            return null;
        var path = Path.Combine(parent, "bin", "Win64_Shipping_Client");
        if (!path.Replace('/', '\\').Contains(@"\workshop\content\261550\" + CoopWorkshopId + @"\bin\Win64_Shipping_Client", StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(Path.Combine(path, "Coop.Core.dll")) ? path : null;
    }

    private static IEnumerable<string> ReadSteamLibraries()
    {
        // Steam's install path is only in the Windows registry.
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steam = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steam))
            return Array.Empty<string>();
        var vdfPath = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var vdf = File.Exists(vdfPath) ? File.ReadAllText(vdfPath) : null;
        return ReadLibraries(steam, vdf);
    }
}
