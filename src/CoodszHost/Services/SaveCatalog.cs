using System.IO;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Saves;
using TaomCoop.Core;

namespace CoodszHost.Services;

public sealed class SaveRow
{
    public string Name { get; init; } = "";
    public string When { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string Match { get; init; } = "";
    public bool Matches { get; init; }
    public string Source { get; init; } = "Server";
    public string Folder { get; init; } = "";
    public string Profiles { get; set; } = "Profiles: none";
    public List<string> Modules { get; init; } = new();
}

public static class SaveCatalog
{
    public static IReadOnlyList<SaveRow> Scan(IEnumerable<(string Folder, string Source)> roots, IEnumerable<string> enabledModuleIds)
    {
        var enabled = new HashSet<string>(enabledModuleIds, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<SaveRow>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Folder))
                continue;
            foreach (var path in Directory.GetFiles(root.Folder, "*.sav"))
            {
                var row = Create(path, enabled, root.Source, root.Folder);
                if (row == null || !seen.Add(row.Name))
                    continue;
                rows.Add(row);
            }
        }

        return rows.OrderByDescending(row => row.When, StringComparer.Ordinal).ToList();
    }

    // default_new_game is the server's new-world template. Coop 0.1.6 puts it in
    // Game Saves, but the server refuses to host it directly, so it is not a
    // campaign the host can pick.
    public const string Template = "default_new_game";

    public static bool IsTemplate(string? name) =>
        name != null && name.Trim().Equals(Template, StringComparison.OrdinalIgnoreCase);

    public static List<string> NamesIn(string savesDirectory)
    {
        var names = new List<string>();
        if (!Directory.Exists(savesDirectory))
            return names;
        foreach (var path in Directory.GetFiles(savesDirectory, "*.sav"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length == 0 || IsTemplate(name) || name.Contains(".backup", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".last-good", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        return names;
    }

    public readonly record struct ImportResult(bool Ok, string Message);

    public static ImportResult ImportSinglePlayer(string sourceSav, string serverSaves, string destinationName)
    {
        if (string.IsNullOrWhiteSpace(sourceSav) || !File.Exists(sourceSav))
            return new ImportResult(false, "That single-player save was not found.");
        if (!sourceSav.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            return new ImportResult(false, "Pick a campaign .sav file.");

        var nameError = SaveSlot.NameError(destinationName);
        if (nameError != null)
            return new ImportResult(false, nameError);

        var name = destinationName.Trim();
        Directory.CreateDirectory(serverSaves);
        var destination = Path.Combine(serverSaves, name + ".sav");
        var sourceFull = Path.GetFullPath(sourceSav);
        if (sourceFull.Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return new ImportResult(true, name + " is already in the server Game Saves folder.");
        if (SaveSlot.IsInsideDirectory(sourceFull, serverSaves))
            return new ImportResult(false, "That file is already a server save. Pick it in the campaign list.");
        if (File.Exists(destination))
            return new ImportResult(false, "A server save named " + name + " already exists. Type another name in the box. Existing saves are not overwritten.");

        File.Copy(sourceFull, destination, overwrite: false);
        var sourceName = Path.GetFileNameWithoutExtension(sourceFull);
        var sourceJson = Path.ChangeExtension(sourceFull, ".json");
        var destinationJson = Path.Combine(serverSaves, name + ".json");
        if (File.Exists(sourceJson) && !File.Exists(destinationJson))
            SaveSlot.CopySidecar(sourceJson, serverSaves, sourceName, name);
        var launcher = Path.ChangeExtension(sourceFull, ".launcher.json");
        var destinationLauncher = Path.Combine(serverSaves, name + ".launcher.json");
        if (File.Exists(launcher) && !File.Exists(destinationLauncher))
            File.Copy(launcher, destinationLauncher, overwrite: false);
        return new ImportResult(true, "");
    }

    // Zip backups of the server saves (BackupService, ported from CoopKeep), kept in
    // CoopData\DedicatedServer\Save Backups\<save> next to Game Saves so the server never lists them.
    public const string BackupFolderName = "Save Backups";

    public static BackupService Backups(string savesDirectory) =>
        new(savesDirectory, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savesDirectory))!, BackupFolderName));

    public static BackupEntry Backup(string savesDirectory, string name) =>
        Backups(savesDirectory).Create(name, BackupReason.Manual);

    private static SaveRow? Create(string path, HashSet<string> enabled, string source, string folder)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name == null || name.Contains(".backup", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".last-good", StringComparison.OrdinalIgnoreCase))
            return null;
        var info = new FileInfo(path);
        var launcher = path[..^4] + ".launcher.json";
        var match = "No module list";
        var matches = false;
        var saved = new List<string>();
        if (File.Exists(launcher))
        {
            saved = ReadModules(launcher);
            if (saved.Count > 0)
            {
                matches = saved.All(id => enabled.Contains(id));
                match = matches ? "Modules match" : "Modules do not match";
            }
        }

        return new SaveRow
        {
            Name = name,
            When = info.LastWriteTime.ToString("M/d/yyyy h:mm tt"),
            SizeText = (info.Length / 1048576.0).ToString("0.0") + " MB",
            Match = match,
            Matches = matches,
            Source = source,
            Folder = folder,
            Modules = saved
        };
    }

    private static List<string> ReadModules(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
                return new List<string>();
            return modules.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .Where(id => id.Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
