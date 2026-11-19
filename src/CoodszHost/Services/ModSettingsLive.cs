// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission (his live settings
// channel, host overrides store and Mod settings view models). The file names and JSON shapes match the
// server module's LiveChannel (compat\CoodszSettingsSync\src\Server\LiveChannel.cs) and ModderLords'.
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Services;

public sealed class LiveSettingsDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime WrittenAt { get; set; }
    public bool McmRunning { get; set; } = true;
    public List<LiveSettingsObject> Objects { get; set; } = new();
}

public sealed class LiveSettingsObject
{
    public string SettingsId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Folder { get; set; }
    public string? ModuleId { get; set; }
    public string? Scope { get; set; }
    public bool PlayersOwn { get; set; }
    public List<LiveSettingsGroup> Groups { get; set; } = new();
    [JsonIgnore] public int PropertyCount => Groups.Sum(g => g.Properties.Count);
}

public sealed class LiveSettingsGroup
{
    public string Name { get; set; } = "";
    public List<LiveSettingsProperty> Properties { get; set; } = new();
}

/// <summary>Kind is bool, int, float, string or enum (a dropdown, its Value is the choice's text).</summary>
public sealed class LiveSettingsProperty
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Hint { get; set; }
    public string Kind { get; set; } = "string";
    public string? Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<string>? Choices { get; set; }
    public bool Editable { get; set; }
    public bool RequireRestart { get; set; }
}

public sealed class LiveApplyRequest
{
    public string SettingsId { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
}

public sealed class LiveApplyAck
{
    public bool Ok { get; set; }
    public string? SettingsId { get; set; }
    public int Changed { get; set; }
    public string Report { get; set; } = "";
    public List<string>? Skipped { get; set; }
    public string? Persisted { get; set; }

    /// <summary>What the host page says: applied, some values skipped (with which), or not applied (with why).</summary>
    public string Describe()
    {
        if (!Ok)
            return Translator.T("Not applied:") + " " + Report;
        if (Skipped is { Count: > 0 })
            return Translator.T("Some values were skipped:") + " " + string.Join(", ", Skipped);
        return Translator.T("Applied");
    }
}

/// <summary>ack-overrides.json: how the host overrides staged at Start went on the server.</summary>
public sealed class OverridesAck
{
    public bool Ok { get; set; } = true;
    public string? Report { get; set; }
    public List<LiveApplyAck> Applied { get; set; } = new();
    public List<string> Pending { get; set; } = new();

    /// <summary>The state of one settings object's saved values, or null when nothing was saved for it.</summary>
    public string? StateOf(string settingsId)
    {
        var applied = Applied.LastOrDefault(a => a.SettingsId == settingsId);
        if (applied != null)
            return applied.Ok && applied.Skipped is not { Count: > 0 }
                ? Translator.T("saved values set")
                : Translator.T("some saved values were skipped");
        if (Pending.Contains(settingsId))
            return Translator.T("saved values waiting for the mod");
        return null;
    }
}

/// <summary>Host overrides for one setup: values staged at every launch. SettingsId to property id to text.</summary>
public sealed class SettingsOverrides
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime? UpdatedAt { get; set; }
    public Dictionary<string, Dictionary<string, string>> Objects { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore] public int Count => Objects.Sum(o => o.Value.Count);
    [JsonIgnore] public bool IsEmpty => Objects.Count == 0;

    public string? Get(string settingsId, string propertyId) =>
        Objects.TryGetValue(settingsId, out var values) && values.TryGetValue(propertyId, out var value) ? value : null;

    public void Set(string settingsId, IReadOnlyDictionary<string, string> values)
    {
        if (!Objects.TryGetValue(settingsId, out var stored))
            Objects[settingsId] = stored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
            stored[pair.Key] = pair.Value;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Clear(string settingsId)
    {
        if (Objects.Remove(settingsId))
            UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// ModderLords' DefaultSettings (his compat records, by Ryvius) as host defaults: for every mod the server
/// runs, a record's settings are staged with the host overrides at Start unless the host already set that
/// value. Locked mods then pass them to players, like any host value. The setup's own overrides file is
/// never changed, so the Advanced area keeps showing only what the host set.
/// </summary>
public static class CompatRecordDefaults
{
    /// <summary>The host overrides with the record defaults added; one log line per mod that has defaults. With
    /// <paramref name="pinnedSet"/> (the AD1259 tool) only the host's own values: AD1259 is a pinned set, so a
    /// downloaded record never sets MCM values on its server or its players.</summary>
    public static SettingsOverrides WithDefaults(SettingsOverrides host, IEnumerable<BannerlordCoopLauncher.Core.Modules.CompatRecord> records,
        IEnumerable<string> runningModules, ICollection<string> log, bool pinnedSet = false)
    {
        var merged = new SettingsOverrides { SchemaVersion = host.SchemaVersion, UpdatedAt = host.UpdatedAt };
        foreach (var pair in host.Objects)
            merged.Objects[pair.Key] = new Dictionary<string, string>(pair.Value, StringComparer.Ordinal);
        if (pinnedSet)
            return merged;
        var running = new HashSet<string>(runningModules.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Where(r => r.DefaultSettings.Count > 0 && running.Contains(r.Id)).OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            var set = 0;
            var kept = 0;
            foreach (var (settingsId, values) in record.DefaultSettings)
                foreach (var (name, value) in values)
                {
                    if (host.Get(settingsId, name) != null)
                    {
                        kept++;
                        continue;
                    }
                    if (!merged.Objects.TryGetValue(settingsId, out var stored))
                        merged.Objects[settingsId] = stored = new Dictionary<string, string>(StringComparer.Ordinal);
                    stored[name] = value;
                    set++;
                }
            log.Add(kept == 0
                ? "Mod settings for players: " + record.Id + " uses " + set + " default setting(s) from ModderLords by Ryvius."
                : "Mod settings for players: " + record.Id + " uses " + set + " default setting(s) from ModderLords by Ryvius and keeps " + kept + " you set.");
        }
        return merged;
    }
}

/// <summary>
/// The files of the live channel and the setup's host overrides. The live folder is
/// engine\Modules\DedicatedServer.CoodszTools\live on the server; each setup keeps its overrides and the last
/// description the server wrote under ModSettings\ next to CoodszHost.exe, so the page works while the server is off.
/// </summary>
public static class ModSettingsLive
{
    public const string SettingsFileName = "settings.json";
    public const string RequestPrefix = "apply-";
    public const string AckPrefix = "ack-";
    public const string OverridesFileName = "overrides.json";
    public const string OverridesAckFileName = "ack-overrides.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string LiveDir(string serverRoot) => Path.Combine(BannerlordCoopLauncher.Core.Server.ServerToolsModule.ModuleFolder(serverRoot), "live");

    public static string StoreRoot(string? baseDirectory = null) => Path.Combine(baseDirectory ?? ToolData.Folder, "ModSettings");
    public static string OverridesPath(string setupKey, string? baseDirectory = null) => Path.Combine(StoreRoot(baseDirectory), setupKey + ".overrides.json");
    public static string CachePath(string setupKey, string? baseDirectory = null) => Path.Combine(StoreRoot(baseDirectory), setupKey + ".last-settings.json");

    public static SettingsOverrides LoadOverrides(string path)
    {
        if (!File.Exists(path))
            return new SettingsOverrides();
        try { return JsonSerializer.Deserialize<SettingsOverrides>(File.ReadAllText(path), Json) ?? new SettingsOverrides(); }
        catch (JsonException) { return new SettingsOverrides(); }
        catch (IOException) { return new SettingsOverrides(); }
    }

    /// <summary>Atomic write; an empty set deletes the file.</summary>
    public static void SaveOverrides(string path, SettingsOverrides overrides)
    {
        if (overrides.IsEmpty)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        WriteAtomic(path, JsonSerializer.Serialize(overrides, Json));
    }

    /// <summary>The server's answer to the overrides staged at Start; null before it answered.</summary>
    public static OverridesAck? ReadOverridesAck(string liveDir)
    {
        var path = Path.Combine(liveDir, OverridesAckFileName);
        if (!File.Exists(path))
            return null;
        try { return JsonSerializer.Deserialize<OverridesAck>(File.ReadAllText(path), Json); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public static LiveSettingsDocument? ReadDocument(string path)
    {
        if (!File.Exists(path))
            return null;
        try { return JsonSerializer.Deserialize<LiveSettingsDocument>(File.ReadAllText(path), Json); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public static void SaveCache(string path, LiveSettingsDocument document) => WriteAtomic(path, JsonSerializer.Serialize(document, Json));

    /// <summary>
    /// On Start: a fresh live folder (no settings.json, edit or answer from the last run is taken for live data)
    /// with this setup's overrides staged for the module.
    /// </summary>
    public static void Stage(string liveDir, SettingsOverrides overrides)
    {
        Directory.CreateDirectory(liveDir);
        foreach (var file in Directory.GetFiles(liveDir))
            File.Delete(file);
        if (!overrides.IsEmpty)
            File.WriteAllText(Path.Combine(liveDir, OverridesFileName), JsonSerializer.Serialize(overrides, Json));
    }

    /// <summary>Drops apply-&lt;token&gt;.json for the module (it polls every 3 seconds) and returns the token.</summary>
    public static string WriteRequest(string liveDir, LiveApplyRequest request)
    {
        Directory.CreateDirectory(liveDir);
        var token = DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture);
        WriteAtomic(Path.Combine(liveDir, RequestPrefix + token + ".json"), JsonSerializer.Serialize(request, Json));
        return token;
    }

    /// <summary>The module's answer for a token, deleted once read; null while it has not answered.</summary>
    public static LiveApplyAck? TakeAck(string liveDir, string token)
    {
        var path = Path.Combine(liveDir, AckPrefix + token + ".json");
        if (!File.Exists(path))
            return null;
        try
        {
            var ack = JsonSerializer.Deserialize<LiveApplyAck>(File.ReadAllText(path), Json);
            File.Delete(path);
            return ack;
        }
        catch (IOException) { return null; }
        catch (JsonException)
        {
            File.Delete(path);
            return new LiveApplyAck { Ok = false, Report = "unreadable answer" };
        }
    }

    public static async Task<LiveApplyAck?> ApplyAsync(string liveDir, string settingsId, IReadOnlyDictionary<string, string> values, TimeSpan timeout)
    {
        var token = WriteRequest(liveDir, new LiveApplyRequest { SettingsId = settingsId, Values = new Dictionary<string, string>(values) });
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var ack = TakeAck(liveDir, token);
            if (ack != null)
                return ack;
            await Task.Delay(250).ConfigureAwait(false);
        }
        return null;
    }

    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>
/// One setting in the editor. Value is always the live text; Original is what the server last reported (or
/// the cached copy); OverrideValue is the host override stored for the setup. Live, edits are measured against
/// the server value; offline, against the override (or the cached value).
/// </summary>
public sealed class ModSettingProperty : Notifier
{
    private string _value = "";
    private string _original = "";
    private string? _override;
    private bool _offline;

    public ModSettingProperty(LiveSettingsProperty property, string? overrideValue, bool offline)
    {
        Id = property.Id;
        DisplayName = string.IsNullOrWhiteSpace(property.DisplayName) ? property.Id : property.DisplayName;
        Hint = property.Hint;
        Update(property, overrideValue, offline);
        _value = Baseline;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? Hint { get; }
    public string Kind { get; private set; } = "string";
    public bool Editable { get; private set; }
    public bool RequireRestart { get; private set; }
    public List<string> Choices { get; private set; } = new();
    public double? Min { get; private set; }
    public double? Max { get; private set; }

    public bool IsBool => Editable && Kind == "bool";
    public bool IsChoice => Editable && Kind == "enum";
    public bool IsText => Editable && !IsBool && !IsChoice;
    public string RangeText => Min is double min && Max is double max
        ? min.ToString("0.###", CultureInfo.InvariantCulture) + " " + Translator.T("to") + " " + max.ToString("0.###", CultureInfo.InvariantCulture)
        : "";
    public string ToolTip => (string.IsNullOrWhiteSpace(Hint) ? Id : Hint + "\n(" + Id + ")")
        + (RequireRestart ? "\n" + Translator.T("The mod says this one needs a restart to take effect.") : "");

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value ?? "";
            RaiseAll();
        }
    }

    public string Original
    {
        get => _original;
        set { _original = value ?? ""; RaiseAll(); }
    }

    public string? OverrideValue
    {
        get => _override;
        set { _override = value; RaiseAll(); }
    }

    public bool OfflineMode
    {
        get => _offline;
        set { _offline = value; RaiseAll(); }
    }

    public string Baseline => OfflineMode ? OverrideValue ?? Original : Original;
    public bool IsDirty => Editable && !string.Equals(Value, Baseline, StringComparison.Ordinal);
    public bool HasOverride => OverrideValue != null;

    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    public string? Error
    {
        get
        {
            if (!Editable || !IsDirty)
                return null;
            switch (Kind)
            {
                case "int":
                    if (!long.TryParse(Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                        return Translator.T("Enter a whole number.");
                    return OutOfRange(whole);
                case "float":
                    if (!double.TryParse(Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        return Translator.T("Enter a number. Use a dot for decimals.");
                    return OutOfRange(number);
                case "enum":
                    return Choices.Contains(Value) ? null : Translator.T("Pick one of the choices.");
                default:
                    return null;
            }
        }
    }

    public bool HasError => Error != null;

    private string? OutOfRange(double number) =>
        (Min is double min && number < min) || (Max is double max && number > max) ? Translator.T("Use a value from") + " " + RangeText + "." : null;

    /// <summary>A fresh description (server or cache): an unsaved edit stays, otherwise the value follows the baseline.</summary>
    public void Update(LiveSettingsProperty property, string? overrideValue, bool offline)
    {
        Kind = property.Kind;
        Editable = property.Editable;
        RequireRestart = property.RequireRestart;
        Choices = property.Choices ?? new List<string>();
        Min = property.Min;
        Max = property.Max;
        var wasDirty = IsDirty;
        _offline = offline;
        _override = overrideValue;
        _original = property.Value ?? "";
        if (!wasDirty)
            _value = Baseline;
        RaiseAll();
    }

    public void Revert() => Value = Baseline;
}

public sealed class ModSettingGroup
{
    public ModSettingGroup(string name) => Name = name;
    public string Name { get; }
    public List<ModSettingProperty> Properties { get; } = new();
}

/// <summary>One mod's MCM settings object with its groups, as the list on the left shows it.</summary>
public sealed class ModSettingObject : Notifier
{
    public ModSettingObject(string settingsId) => SettingsId = settingsId;

    public string SettingsId { get; }
    public string DisplayName { get; private set; } = "";
    public string? ModuleId { get; private set; }
    public string? Folder { get; private set; }

    /// <summary>How this object's saved values went on the running server (set, waiting, skipped); null when nothing to say.</summary>
    public string? OverrideState { get; set; }
    public List<ModSettingGroup> Groups { get; private set; } = new();
    public IEnumerable<ModSettingProperty> AllProperties => Groups.SelectMany(g => g.Properties);
    public int OverrideCount => AllProperties.Count(p => p.HasOverride);
    public bool Locked { get; set; } = true;

    /// <summary>The id the lock is kept under: the module the settings belong to, else the settings id.</summary>
    public string LockKey => string.IsNullOrWhiteSpace(ModuleId) ? SettingsId : ModuleId!;

    public string Subtitle
    {
        get
        {
            var count = AllProperties.Count();
            var editable = AllProperties.Count(p => p.Editable);
            var text = count + " " + Translator.T("settings") + ", " + editable + " " + Translator.T("editable") + ", MCM";
            if (!Locked)
                text += ", " + Translator.T("players keep their own");
            if (OverrideCount > 0)
                text += ", " + OverrideCount + " " + Translator.T("saved for the next launch");
            if (!string.IsNullOrEmpty(OverrideState))
                text += ", " + OverrideState;
            return text;
        }
    }

    public void Update(LiveSettingsObject source, SettingsOverrides overrides, bool offline, bool locked)
    {
        DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? source.SettingsId : source.DisplayName;
        ModuleId = source.ModuleId;
        Folder = source.Folder;
        Locked = locked;
        // Rebuilt in place, so an unsent edit survives a new settings.json.
        var existing = AllProperties.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var groups = new List<ModSettingGroup>();
        foreach (var group in source.Groups)
        {
            var built = new ModSettingGroup(string.IsNullOrWhiteSpace(group.Name) ? "General" : group.Name);
            foreach (var property in group.Properties)
            {
                var stored = overrides.Get(source.SettingsId, property.Id);
                if (existing.TryGetValue(property.Id, out var shown))
                    shown.Update(property, stored, offline);
                else
                    shown = new ModSettingProperty(property, stored, offline);
                built.Properties.Add(shown);
            }
            groups.Add(built);
        }
        Groups = groups;
        RaiseAll();
    }

    public void Refresh() => RaiseAll();
}

/// <summary>The Mod settings page's state: the server's description (or the cache) merged with the setup's overrides.</summary>
public static class ModSettingsMerge
{
    public static List<ModSettingObject> Merge(IReadOnlyList<ModSettingObject> shown, LiveSettingsDocument document, SettingsOverrides overrides,
        bool offline, Func<LiveSettingsObject, bool> locked)
    {
        var result = new List<ModSettingObject>();
        foreach (var source in document.Objects)
        {
            var item = shown.FirstOrDefault(o => o.SettingsId == source.SettingsId) ?? new ModSettingObject(source.SettingsId);
            item.Update(source, overrides, offline, locked(source));
            result.Add(item);
        }
        return result;
    }

    /// <summary>The edited values to send or store, or the reason they cannot go yet.</summary>
    public static Dictionary<string, string>? Dirty(ModSettingObject item, out string? problem)
    {
        problem = null;
        var dirty = item.AllProperties.Where(p => p.IsDirty).ToList();
        var bad = dirty.Where(p => p.HasError).ToList();
        if (bad.Count > 0)
        {
            problem = Translator.T("Fix these first:") + " " + string.Join(", ", bad.Select(p => p.DisplayName + " (" + p.Error + ")"));
            return null;
        }
        return dirty.ToDictionary(p => p.Id, p => p.Kind == "string" ? p.Value : p.Value.Trim(), StringComparer.Ordinal);
    }
}
