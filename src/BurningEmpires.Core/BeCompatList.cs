using System.Text.Json;
using System.Text.Json.Serialization;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>Where what the Burning Empires list says about a mod comes from (the "source" field).
/// The Coop test, load test and notes sources are the same words the TAOM list uses.</summary>
public static class BeCompatSource
{
    /// <summary>Reported in Burning Empires singleplayer on the Burning Empires Discord, not tested in Coop.</summary>
    public const string BeDiscord = "be-discord";
    /// <summary>Said by the Burning Empires developers.</summary>
    public const string BeDev = "be-dev";

    /// <summary>The plain label for a source. An unknown or empty source counts as Coodsz notes.</summary>
    public static string Label(string? source) => (source ?? "").Trim().ToLowerInvariant() switch
    {
        CompatSource.CoodszCoop => "Tested in Coop by Coodsz",
        CompatSource.ServerLoads => "Passed the automatic server load test",
        BeDiscord => "Reported on the Burning Empires Discord in Burning Empires singleplayer, not tested in Coop",
        BeDev => "Said by the Burning Empires developer",
        _ => "Coodsz notes"
    };
}

/// <summary>
/// The Burning Empires compatibility list: be-mod-compat.json in the public Burning Empires tools
/// repository (compat/be-mod-compat.json on its main branch). A copy ships inside BurningEmpires.Core
/// (Compat\be-mod-compat.json) and is used when the online file cannot be read or is older; the last
/// good download is kept next to the tool. There is no second place to download it from.
///
/// Format: the TAOM list's schema 2 with kind "CoodszBeCompat" and "be" (the Burning Empires version)
/// instead of "taom": { "kind", "schemaVersion": 2, "be", "game", "coop", "updated", "mods": [ ... ] }.
/// A mod is matched by module id, then by Workshop id, then by its name.
/// </summary>
public sealed class BeCompatList
{
    public const string Kind = "CoodszBeCompat";
    public const string FileName = "be-mod-compat.json";
    public const int CurrentSchema = 2;
    public const string Repository = "Coodsz/Coodsz-BurningEmpires";
    public const string RepositoryPath = "compat/" + FileName;
    public const string OnlineUrl = "https://raw.githubusercontent.com/" + Repository + "/main/" + RepositoryPath;
    /// <summary>The readable page of the whole list with the player counts (MODS.md, written by the tally there).</summary>
    public const string ModsPageUrl = "https://github.com/" + Repository + "/blob/main/MODS.md";

    [JsonPropertyName("kind")]
    public string FileKind { get; set; } = Kind;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Be { get; set; } = "";
    public string Game { get; set; } = "";
    public string Coop { get; set; } = "";
    public string Updated { get; set; } = "";
    public List<CompatEntry> Mods { get; set; } = new();

    /// <summary>Reported mods that are not on the hand list, with the player counts the tally gives them
    /// ("community" next to "mods"). They never get a list badge, only the count chips.</summary>
    public List<BeModCounts> Community { get; set; } = new();

    /// <summary>The player counts the tally writes on the entries of "mods" (works, broken), read from the same
    /// file. CompatEntry is the TAOM list's entry and skips those fields.</summary>
    [JsonIgnore]
    public List<BeModCounts> ModCounts { get; private set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static BeCompatList Parse(string json)
    {
        BeCompatList? list;
        try
        {
            list = JsonSerializer.Deserialize<BeCompatList>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The Burning Empires mod list is not valid. " + ex.Message);
        }

        if (list == null || !Kind.Equals(list.FileKind, StringComparison.Ordinal))
            throw new InvalidOperationException("That file is not a Burning Empires mod list.");
        if (list.SchemaVersion < 1)
            throw new InvalidOperationException("The Burning Empires mod list is not valid.");
        list.Mods = (list.Mods ?? new List<CompatEntry>()).Where(entry => entry != null && (entry.Id.Length > 0 || entry.WorkshopId.Length > 0 || entry.Name.Length > 0)).ToList();
        foreach (var entry in list.Mods)
        {
            entry.Id ??= "";
            entry.Name ??= "";
            entry.WorkshopId ??= "";
            entry.NexusId ??= "";
            entry.Tag ??= "";
            entry.Class ??= "";
            entry.Note ??= "";
            entry.Source ??= "";
            entry.Checked ??= "";
        }

        list.Be ??= "";
        list.Updated ??= "";
        list.Community = (list.Community ?? new List<BeModCounts>()).Where(counts => counts != null).Select(BeModCounts.Clean).ToList();
        list.ModCounts = (JsonSerializer.Deserialize<CountsFile>(json, Options)?.Mods ?? new List<BeModCounts>())
            .Where(counts => counts != null).Select(BeModCounts.Clean).ToList();
        return list;
    }

    // The "mods" array again, for the count fields only.
    private sealed class CountsFile
    {
        public List<BeModCounts>? Mods { get; set; }
    }

    public static BeCompatList BuiltIn()
    {
        using var stream = typeof(BeCompatList).Assembly.GetManifestResourceStream("BurningEmpires.Compat." + FileName)
            ?? throw new InvalidOperationException("The built in Burning Empires mod list is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>True when this list says it is older than the other one (both have an updated date).
    /// A list without a date is never called older.</summary>
    public bool OlderThan(BeCompatList other) =>
        Updated.Length > 0 && other.Updated.Length > 0 && string.CompareOrdinal(Updated.Trim(), other.Updated.Trim()) < 0;

    /// <summary>The saved download next to the tool when it is valid and not older than the built in
    /// list (a tool update brings a newer list), else the built in list.</summary>
    public static BeCompatList Load(string cachePath)
    {
        var builtIn = BuiltIn();
        try
        {
            if (File.Exists(cachePath))
            {
                var saved = Parse(File.ReadAllText(cachePath));
                if (!saved.OlderThan(builtIn))
                    return saved;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return builtIn;
    }

    /// <summary>Downloads the online list (or the given url). A valid list that is not older than the
    /// built in one is saved to cachePath and returned; on any problem null is returned and the list
    /// in use stays.</summary>
    public static async Task<BeCompatList?> DownloadAsync(HttpClient http, string cachePath, CancellationToken cancellationToken, string? url = null)
    {
        var builtIn = BuiltIn();
        try
        {
            var json = await http.GetStringAsync(url ?? OnlineUrl, cancellationToken);
            var list = Parse(json);
            if (list.OlderThan(builtIn))
                return null;
            File.WriteAllText(cachePath, json);
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
        catch (TaskCanceledException)
        {
            // A timeout or Stop; the list in use stays.
        }

        return null;
    }

    public CompatEntry? Find(string? moduleId, string? workshopId = null, string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            var byId = Mods.FirstOrDefault(entry => entry.Id.Length > 0 && entry.Id.Equals(moduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(workshopId) && workshopId.Trim() != "0")
        {
            var byWorkshop = Mods.FirstOrDefault(entry => entry.WorkshopId.Length > 0 && entry.WorkshopId.Equals(workshopId.Trim(), StringComparison.Ordinal));
            if (byWorkshop != null)
                return byWorkshop;
        }

        if (!string.IsNullOrWhiteSpace(name))
            return Mods.FirstOrDefault(entry => entry.Id.Length == 0 && entry.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>What players reported for a mod through the Burning Empires tools: the counts on its list
    /// entry (matched like Find), else on its "community" entry by module id. Null when nobody reported it.</summary>
    public BeModCounts? CountsFor(string? moduleId, string? workshopId = null, string? name = null)
    {
        var id = (moduleId ?? "").Trim();
        BeModCounts? counts = null;
        if (id.Length > 0)
            counts = ModCounts.FirstOrDefault(entry => entry.Id.Length > 0 && entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (counts == null && !string.IsNullOrWhiteSpace(workshopId) && workshopId.Trim() != "0")
            counts = ModCounts.FirstOrDefault(entry => entry.WorkshopId.Length > 0 && entry.WorkshopId.Equals(workshopId.Trim(), StringComparison.Ordinal));
        if (counts == null && !string.IsNullOrWhiteSpace(name))
            counts = ModCounts.FirstOrDefault(entry => entry.Id.Length == 0 && entry.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (counts == null && id.Length > 0)
            counts = Community.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return counts is { Works: > 0 } or { Broken: > 0 } ? counts : null;
    }

    /// <summary>The short badge text for a tag, or null when nothing is known.</summary>
    public static string? BadgeText(CompatTag tag) => tag switch
    {
        CompatTag.Safe => "Tested safe",
        CompatTag.Everyone => "Everyone needs it",
        CompatTag.Risky => "Risky",
        CompatTag.Broken => "Known broken",
        CompatTag.Reported => "Works in Burning Empires, not tested in Coop",
        CompatTag.Loads => "Loads on the server",
        _ => null
    };

    /// <summary>Green, yellow, red, or blue for what is reported or only load tested.</summary>
    public static string BadgeColor(CompatTag tag) => TaomCompatList.BadgeColor(tag);

    /// <summary>Where the entry comes from and when it was checked, as one line.</summary>
    public static string SourceLine(CompatEntry entry)
    {
        var source = BeCompatSource.Label(entry.Source);
        return entry.Checked.Length > 0 ? source + ", checked " + entry.Checked + "." : source + ".";
    }

    /// <summary>The hover text of a badge: the note, then where it comes from and when it was checked.
    /// translate turns the source line into the tool language (the note is list data and stays English).</summary>
    public static string Hover(CompatEntry entry, Func<string, string>? translate = null)
    {
        var from = (translate ?? (text => text))(SourceLine(entry));
        return entry.Note.Length > 0 ? entry.Note + "\n\n" + from : from;
    }

    /// <summary>The warning to show before a known broken mod is added or launched.</summary>
    public static string BrokenWarning(string label, CompatEntry entry) =>
        label + " is known to be broken with Burning Empires. " + (entry.Note.Length > 0 ? entry.Note + " " : "") + "Leave it off unless you are testing it.";
}
