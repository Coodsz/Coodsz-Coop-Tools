using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.IO;

namespace CoodszHost.Services;

public sealed class McmSettingFile
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public List<McmValue> Values { get; init; } = new();
}

public sealed class McmValue
{
    public string Name { get; init; } = "";
    public string Text { get; set; } = "";
    public string Kind { get; init; } = "string";
}

public static class McmCatalog
{
    public static string Root =>
        Path.Combine(ServerSession.DocumentsData, "Configs", "ModSettings");

    public static string SinglePlayerRoot =>
        Path.Combine(ClientSync.DocumentsRoot, "Configs", "ModSettings");

    public static string SnapshotRoot(string folderId) =>
        Path.Combine(ToolData.Folder, "McmProfiles", folderId);

    public static void SeedFromLive(string folderId) =>
        CopySettings(Root, SnapshotRoot(folderId), overwrite: false);

    public static void CopySnapshot(string fromId, string toId) =>
        CopySettings(SnapshotRoot(fromId), SnapshotRoot(toId), overwrite: true);

    // Files in skip (paths under the setup's folder) stay as the live server has them.
    public static void ApplyToLive(string folderId, IEnumerable<string>? skip = null) =>
        CopySettings(SnapshotRoot(folderId), Root, overwrite: true, skip);

    // The values COOP Server Settings Lock used to force, for the mods this setup runs, only where the
    // setup has no value of its own yet. A mod file the setup has no copy of comes in whole from the live
    // server first. When the setup has its own copy, that copy (the MCM page) is the source at Start as
    // always; only a value missing from it is taken from the live file, and the defaults fill the rest.
    public static BannerlordCoopLauncher.Core.Profile.SettingsLockDefaults.SeedResult SeedLockDefaults(string folderId, IEnumerable<string> enabledModuleIds)
    {
        CopySettings(Root, SnapshotRoot(folderId), overwrite: false);
        return BannerlordCoopLauncher.Core.Profile.SettingsLockDefaults.Seed(SnapshotRoot(folderId), enabledModuleIds, Root);
    }

    public static void DeleteSnapshot(string folderId)
    {
        var path = SnapshotRoot(folderId);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public static bool SnapshotHasSettings(string folderId) => SettingFiles(SnapshotRoot(folderId)).Count > 0;

    public static int SinglePlayerFileCount() => SettingFiles(SinglePlayerRoot).Count;

    public static int ImportSinglePlayer(string folderId) => ImportFrom(SinglePlayerRoot, folderId);

    public static int ImportFrom(string source, string folderId)
    {
        var files = SettingFiles(source);
        if (files.Count == 0)
            throw new InvalidOperationException("No single-player MCM settings were found in Documents\\Mount and Blade II Bannerlord\\Configs\\ModSettings.");
        var destination = SnapshotRoot(folderId);
        if (Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The single-player MCM folder and this setup are the same folder.");
        var staging = destination + ".incoming";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        CopySettings(source, staging, overwrite: true);
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Directory.Move(staging, destination);
        return files.Count;
    }

    private static List<string> SettingFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return new List<string>();
        return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<McmSettingFile> Load(string folderId)
    {
        CopySettings(Root, SnapshotRoot(folderId), overwrite: false);
        var root = SnapshotRoot(folderId);
        if (!Directory.Exists(root))
            return Array.Empty<McmSettingFile>();
        return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(Read)
            .Where(file => file != null)
            .Cast<McmSettingFile>()
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CopySettings(string source, string destination, bool overwrite, IEnumerable<string>? skip = null)
    {
        if (!Directory.Exists(source))
            return;
        var skipped = new HashSet<string>(skip ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = Path.GetRelativePath(source, path);
            var target = Path.Combine(destination, relative);
            if ((!overwrite && File.Exists(target)) || skipped.Contains(relative))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
        }
    }

    public static void Apply(McmSettingFile file, McmValue value, string newText)
    {
        var backup = file.Path + ".bak";
        File.Copy(file.Path, backup, overwrite: true);
        value.Text = newText;
        Write(file);
    }

    public static int EnableAll(McmSettingFile file)
    {
        var switches = file.Values.Where(value => value.Kind == "bool").ToList();
        if (switches.Count == 0)
            return 0;
        File.Copy(file.Path, file.Path + ".bak", overwrite: true);
        foreach (var value in switches)
            value.Text = "true";
        Write(file);
        return switches.Count;
    }

    private static void Write(McmSettingFile file)
    {
        if (file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            WriteJson(file);
        else
            WriteXml(file);
    }

    private static McmSettingFile? Read(string path)
    {
        try
        {
            var values = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ReadJson(path) : ReadXml(path);
            if (values.Count == 0)
                return null;
            var folder = new DirectoryInfo(Path.GetDirectoryName(path)!).Name;
            return new McmSettingFile
            {
                Name = folder,
                Path = path,
                Values = values
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<McmValue> ReadJson(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return new List<McmValue>();
        var values = new List<McmValue>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var kind = property.Value.ValueKind switch
            {
                JsonValueKind.True or JsonValueKind.False => "bool",
                JsonValueKind.Number => "number",
                JsonValueKind.String => "string",
                _ => "complex"
            };
            var text = kind == "complex" ? property.Value.GetRawText() : property.Value.ToString();
            values.Add(new McmValue { Name = property.Name, Text = text, Kind = kind });
        }

        return values;
    }

    private static List<McmValue> ReadXml(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root;
        if (root == null)
            return new List<McmValue>();
        return root.Elements()
            .Select(element =>
            {
                var text = element.Value;
                var kind = bool.TryParse(text, out _) ? "bool" : double.TryParse(text, out _) ? "number" : "string";
                return new McmValue { Name = element.Name.LocalName, Text = text, Kind = kind };
            })
            .ToList();
    }

    private static void WriteJson(McmSettingFile file)
    {
        var map = new Dictionary<string, object?>();
        foreach (var value in file.Values)
        {
            if (value.Kind == "bool")
                map[value.Name] = bool.Parse(value.Text);
            else if (value.Kind == "number" && double.TryParse(value.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                map[value.Name] = number;
            else if (value.Kind == "complex")
                map[value.Name] = JsonSerializer.Deserialize<JsonElement>(value.Text);
            else
                map[value.Name] = value.Text;
        }

        File.WriteAllText(file.Path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteXml(McmSettingFile file)
    {
        var document = XDocument.Load(file.Path);
        var root = document.Root ?? throw new InvalidOperationException("The settings file has no root.");
        foreach (var value in file.Values)
        {
            var element = root.Element(value.Name);
            if (element != null)
                element.Value = value.Text;
        }

        document.Save(file.Path);
    }
}
