using System.Text.Json;
using BannerlordCoopLauncher.Core.IO;

namespace BurningEmpires.Core;

public sealed record BeServerFolder(string? ServerRoot, string? DataRoot);

// Where a Burning Empires server tool on this PC keeps its server and Data folders, so the
// client launcher's log zip can carry the server's logs when one PC hosts and plays.
public static class BeServerFolders
{
    public const string SettingsFile = "be-server.json";

    // The default folders on every fixed drive, the script's data folder in Documents, and the
    // folders named in a be-server.json next to this launcher or in a folder beside it (both
    // tools unzipped in one place).
    public static IReadOnlyList<BeServerFolder> OnThisPc(string launcherFolder)
    {
        var drives = new List<string>();
        try
        {
            drives.AddRange(DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady).Select(drive => drive.RootDirectory.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return Find(drives, SettingsFiles(launcherFolder), BeHostFolders.ScriptDataRoot());
    }

    public static IReadOnlyList<BeServerFolder> Find(IEnumerable<string> driveRoots, IEnumerable<string> settingsFiles, string? scriptDataRoot = null)
    {
        var found = new List<BeServerFolder>();
        foreach (var file in settingsFiles)
        {
            var folder = Read(file);
            if (folder != null)
                found.Add(folder);
        }

        // The script route keeps its data in Documents, so a default server folder pairs with it.
        // That folder is shared with Coodsz Host, so it only counts next to a real Burning
        // Empires server folder.
        found.AddRange(driveRoots
            .Select(BeHostFolders.DefaultServerRoot)
            .Where(IsBeServer)
            .Select(server => new BeServerFolder(server, Exists(scriptDataRoot) ? scriptDataRoot : null)));
        return found
            .Where(folder => Exists(folder.ServerRoot) || Exists(folder.DataRoot))
            .DistinctBy(folder => Key(folder.DataRoot) + "|" + Key(folder.ServerRoot))
            .ToList();
    }

    // Only ServerRoot and DataRoot are read. The file also holds the server password,
    // which never leaves it.
    public static BeServerFolder? Read(string settingsFile)
    {
        try
        {
            if (!File.Exists(settingsFile))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsFile));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var server = Text(root, "ServerRoot");
            var data = Text(root, "DataRoot");
            return server == null && data == null ? null : new BeServerFolder(server, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SettingsFiles(string launcherFolder)
    {
        var files = new List<string> { Path.Combine(ToolData.FolderOf(ToolData.BeServer), SettingsFile), Path.Combine(launcherFolder, SettingsFile) };
        try
        {
            var parent = Directory.GetParent(launcherFolder.TrimEnd('\\', '/'));
            if (parent != null)
                files.AddRange(Directory.EnumerateDirectories(parent.FullName).Take(200).Select(folder => Path.Combine(folder, SettingsFile)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return files.Where(File.Exists);
    }

    // A server folder with BurningEmpires staged in.
    private static bool IsBeServer(string? serverRoot) =>
        Exists(serverRoot) && Directory.Exists(Path.Combine(serverRoot!, "engine", "Modules", BeServerPath.BeModuleId));

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static string Key(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.ToUpperInvariant();
        }
    }
}
