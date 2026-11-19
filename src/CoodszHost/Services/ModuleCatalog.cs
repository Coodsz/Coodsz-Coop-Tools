using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using BannerlordCoopLauncher.Core.Modules;

namespace CoodszHost.Services;

public sealed class ModuleRow : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public bool Required { get; init; }

    private bool _enabled = true;
    // Raised, so Space on a focused row moves its switch too.
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    private bool _canToggle = true;
    // Off for Always on mods, locked setups and while the mod list is blocked (ModListBlock).
    public bool CanToggle
    {
        get => _canToggle;
        set
        {
            if (_canToggle == value)
                return;
            _canToggle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanToggle)));
        }
    }

    private bool _canMove = true;
    // Move up, Move down, dragging and Delete: off for locked setups and while the mod list is blocked.
    public bool CanMove
    {
        get => _canMove;
        set
        {
            if (_canMove == value)
                return;
            _canMove = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanMove)));
        }
    }

    private int _order;
    // Raised, so Move up and Move down renumber the rows without rebuilding them (the focus stays on the button).
    public int Order
    {
        get => _order;
        set
        {
            if (_order == value)
                return;
            _order = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
        }
    }

    public bool IsNew { get; set; }

    /// <summary>The New or role chip of the row (ModChips); the community badge and this PC's result sit beside them.</summary>
    public IReadOnlyList<ModChip> Chips =>
        ModChips.For(IsNew ? ModChips.Status(ModChips.New) : null, ModChips.Role(Id, Required));

    /// <summary>The mono line under the name: the module id and its version.</summary>
    public string IdLine => Version.Length == 0 ? Id : Id + "  " + Version;

    private bool _canDelete;
    // Shown as the Delete button on the row. Off for required mods, the working
    // Coop list, and locked setups.
    public bool CanDelete
    {
        get => _canDelete;
        set
        {
            if (_canDelete == value)
                return;
            _canDelete = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDelete)));
        }
    }

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging == value)
                return;
            _isDragging = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
        }
    }

    // The community list badge (compat-list/mods.json); null for the official modules and Coop.
    private ModCompatBadge? _compat;
    public ModCompatBadge? Compat
    {
        get => _compat;
        set
        {
            if (Equals(_compat, value))
                return;
            _compat = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Compat)));
        }
    }

    // This PC's own result for the mod (mod-results.json), shown next to the list badge.
    private ModResultPill? _mine;
    public ModResultPill? Mine
    {
        get => _mine;
        set
        {
            if (Equals(_mine, value))
                return;
            _mine = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mine)));
        }
    }

    // What players reported for the mod (count chips, ModderLords' verdict) and the Report link, built
    // by the Modules page (MainWindow.ReportChips); null for the base game modules and Coop.
    private object? _reports;
    public object? Reports
    {
        get => _reports;
        set
        {
            if (ReferenceEquals(_reports, value))
                return;
            _reports = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reports)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public List<string> Dependencies { get; init; } = new();
    public string Subtitle => Id;
}

public static class ModuleCatalog
{
    private static readonly string[] RequiredIds =
    {
        "Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox",
        "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen"
    };

    private static readonly HashSet<string> SoftDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustomBattle", "StoryMode", "BirthAndDeath"
    };

    private static readonly HashSet<string> ExcludedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "CoopMarriage", "FamilyAppearanceEditor",
        // Coop Save Rescue: every start adds it by itself (the old id is gone from new servers).
        BannerlordCoopLauncher.Core.Support.AutoRescue.ModuleId, BannerlordCoopLauncher.Core.Support.AutoRescue.LegacyModuleId,
        // Coodsz Host adds its server tools module to every start by itself.
        BannerlordCoopLauncher.Core.Keep.ToolsCommand.ModuleId,
        // Improved Garrisons Coop Compat: a start with ImprovedGarrisons adds it by itself.
        ImprovedGarrisonsCompat.ModuleId
    };

    public static string ModulesRoot(string serverRoot) =>
        Path.Combine(serverRoot, "engine", "Modules");

    public static string ListPath(string serverRoot) =>
        Path.Combine(serverRoot, "modded-server-modules.txt");

    public static string SelectionPath(string serverRoot) =>
        Path.Combine(serverRoot, "modded-server-selection.json");

    public static List<ModuleRow> Load(string serverRoot)
    {
        var installed = Scan(ModulesRoot(serverRoot));
        var order = ReadOrder(ListPath(serverRoot));
        var disabled = ReadDisabled(SelectionPath(serverRoot));
        var rows = new List<ModuleRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
        {
            if (ExcludedIds.Contains(id))
                continue;
            if (!installed.TryGetValue(id, out var row) || !seen.Add(row.Id))
                continue;
            row.Enabled = row.Required || !disabled.Contains(row.Id);
            rows.Add(row);
        }

        foreach (var row in installed.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (ExcludedIds.Contains(row.Id) || !seen.Add(row.Id))
                continue;
            row.Enabled = true;
            rows.Add(row);
        }

        var ids = rows.Select(row => row.Id).ToList();
        return rows.Where(row => !BannerlordCoopModules.Hide(row.Id, ids)).ToList();
    }

    public static void Write(string serverRoot, IReadOnlyList<ModuleRow> rows)
    {
        if (string.IsNullOrWhiteSpace(serverRoot) || !Directory.Exists(serverRoot))
            throw new InvalidOperationException(DedicatedServerFinder.MissingMessage);
        var list = new StringBuilder();
        list.AppendLine("# Bannerlord module IDs in dedicated-server load order.");
        var selection = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            // A library the setup turns off (Plain Coop) stays out of the list: the server script
            // loads Harmony whenever it is listed, and Coop would then ask players for it.
            if (row.Required && !row.Enabled && ServerModuleNotes.IsLibrary(row.Id))
                continue;
            list.AppendLine(row.Id);
            // Start-ModdedServer only auto-loads Native/SandBox/Coop/DedicatedServer.Windows.
            // Harmony, ButterLib, UIExtender, and MCM are required in this tool, so they used
            // to be left out of selection.json and the server started without them.
            if (!row.Required || ServerModuleNotes.IsLibrary(row.Id))
                selection[row.Id] = row.Enabled;
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(ListPath(serverRoot), list.ToString(), encoding);
        File.WriteAllText(
            SelectionPath(serverRoot),
            JsonSerializer.Serialize(selection, new JsonSerializerOptions { WriteIndented = true }),
            encoding);
    }

    public static List<ModuleRow> AutoOrder(IReadOnlyList<ModuleRow> rows)
    {
        var index = rows.Select((row, i) => (row.Id, i)).ToDictionary(item => item.Id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var incoming = rows.ToDictionary(row => row.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var next = rows.ToDictionary(row => row.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var dependency in row.Dependencies)
            {
                if (!index.ContainsKey(dependency) || SoftDependencies.Contains(dependency))
                    continue;
                next[dependency].Add(row.Id);
                incoming[row.Id]++;
            }
        }

        var ready = new SortedSet<string>(
            rows.Where(row => incoming[row.Id] == 0).Select(row => row.Id),
            Comparer<string>.Create((a, b) =>
            {
                var order = index[a].CompareTo(index[b]);
                return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            }));
        var sorted = new List<ModuleRow>();
        var byId = rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            sorted.Add(byId[id]);
            foreach (var child in next[id])
            {
                incoming[child]--;
                if (incoming[child] == 0)
                    ready.Add(child);
            }
        }

        foreach (var row in rows)
        {
            if (sorted.All(item => !item.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase)))
                sorted.Add(row);
        }

        PinServerHost(sorted);
        return sorted;
    }

    /// <summary>What Validate says when nothing is wrong. The Mods page hides that line (ShowsNote).</summary>
    public const string ValidNote = "Dependency order is valid for this installed server.";

    /// <summary>False for an empty line and for the all is well line, so the Mods page shows only problems and notices.</summary>
    public static bool ShowsNote(string? text) => !string.IsNullOrWhiteSpace(text) && text != ValidNote;

    public static string Validate(IReadOnlyList<ModuleRow> rows)
    {
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
            position[rows[i].Id] = i;
        var problems = new List<string>();
        foreach (var row in rows.Where(item => item.Enabled))
        {
            foreach (var dependency in row.Dependencies)
            {
                if (SoftDependencies.Contains(dependency))
                    continue;
                if (!position.TryGetValue(dependency, out var at))
                {
                    problems.Add(row.Name + " needs " + dependency + ", which is not installed.");
                    continue;
                }

                var dependencyRow = rows[at];
                if (!dependencyRow.Enabled)
                    problems.Add(row.Name + " needs " + dependencyRow.Name + ", which is disabled.");
                else if (at > position[row.Id])
                    problems.Add(row.Name + " is above " + dependencyRow.Name + ".");
            }
        }

        return problems.Count == 0
            ? ValidNote
            : string.Join(" ", problems.Take(4));
    }

    private static void PinServerHost(List<ModuleRow> rows)
    {
        var host = rows.FindIndex(row => row.Id.Equals("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase));
        var native = rows.FindIndex(row => row.Id.Equals("Native", StringComparison.OrdinalIgnoreCase));
        if (host < 0 || native < 0 || host == native + 1)
            return;
        var row = rows[host];
        rows.RemoveAt(host);
        native = rows.FindIndex(item => item.Id.Equals("Native", StringComparison.OrdinalIgnoreCase));
        rows.Insert(native + 1, row);
    }

    private static Dictionary<string, ModuleRow> Scan(string modulesRoot)
    {
        var found = new Dictionary<string, ModuleRow>(StringComparer.OrdinalIgnoreCase);
        var chosenFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(modulesRoot))
        {
            DuplicateNote = "";
            return found;
        }
        foreach (var folder in Directory.EnumerateDirectories(modulesRoot))
        {
            var manifest = Path.Combine(folder, "SubModule.xml");
            if (!File.Exists(manifest))
                continue;
            if (!TryRead(manifest, out var row) || row == null)
                continue;
            if (!folders.TryGetValue(row.Id, out var list))
            {
                list = new List<string>();
                folders[row.Id] = list;
            }
            list.Add(folder);
            if (!chosenFolder.TryGetValue(row.Id, out var current) || BetterFolder(folder, current, row.Id))
            {
                found[row.Id] = row;
                chosenFolder[row.Id] = folder;
            }
        }

        var notes = new List<string>();
        foreach (var pair in folders.Where(pair => pair.Value.Count > 1))
        {
            var names = string.Join(", ", pair.Value.Select(Path.GetFileName));
            notes.Add(pair.Key + " is in more than one folder (" + names + "). The server uses one copy. Move the extra folders out of the server Modules folder.");
        }
        DuplicateNote = string.Join(" ", notes.Take(2));
        return found;
    }

    public static string DuplicateNote { get; private set; } = "";

    private static bool BetterFolder(string candidate, string current, string moduleId)
    {
        static int Score(string folder, string id)
        {
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

        return Score(candidate, moduleId) > Score(current, moduleId);
    }

    public static bool IsSafeModuleId(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return false;
        var id = moduleId.Trim();
        if (id is "." or "..")
            return false;
        return id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    // Every folder under Modules whose SubModule.xml has this module id.
    public static IReadOnlyList<string> FoldersFor(string modulesRoot, string moduleId)
    {
        var folders = new List<string>();
        if (!Directory.Exists(modulesRoot))
            return folders;
        foreach (var folder in Directory.EnumerateDirectories(modulesRoot))
        {
            var manifest = Path.Combine(folder, "SubModule.xml");
            if (File.Exists(manifest) && TryRead(manifest, out var row) && row != null
                && row.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
                folders.Add(folder);
        }

        return folders;
    }

    private static bool TryRead(string path, out ModuleRow? row)
    {
        row = null;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            };
            using var reader = XmlReader.Create(path, settings);
            string? id = null;
            string? name = null;
            string? version = null;
            var dependencies = new List<string>();
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.Name is "Id" or "Name" or "Version")
                {
                    var value = reader.GetAttribute("value");
                    if (reader.Name == "Id" && id == null)
                        id = value;
                    else if (reader.Name == "Name" && name == null)
                        name = value;
                    else if (reader.Name == "Version" && version == null)
                        version = value;
                }
                else if (reader.Name == "DependedModule")
                {
                    var optional = reader.GetAttribute("Optional");
                    var dependency = reader.GetAttribute("Id") ?? reader.GetAttribute("id");
                    if (!string.Equals(optional, "true", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(dependency))
                        dependencies.Add(dependency.Trim());
                }
            }

            if (string.IsNullOrWhiteSpace(id))
                return false;
            var moduleId = id.Trim();
            if (!IsSafeModuleId(moduleId))
                return false;
            row = new ModuleRow
            {
                Id = moduleId,
                Name = string.IsNullOrWhiteSpace(name) ? moduleId : name.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? "" : version.Trim(),
                Required = RequiredIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase),
                Dependencies = dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            return true;
        }
        // A manifest gone or locked between the folder scan and the read (a copy cut short) reads as no mod.
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<string> ReadOrder(string path)
    {
        if (!File.Exists(path))
            return new List<string>();
        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    private static HashSet<string> ReadDisabled(string path)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return disabled;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path));
            if (map == null)
                return disabled;
            foreach (var pair in map)
            {
                if (!pair.Value)
                    disabled.Add(pair.Key);
            }
        }
        catch (JsonException)
        {
        }

        return disabled;
    }
}
