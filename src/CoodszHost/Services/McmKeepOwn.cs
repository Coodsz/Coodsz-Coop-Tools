// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoodszHost.Services;

/// <summary>
/// Settings sync, per mod: which mods' settings players keep for themselves. Every other mod is locked to
/// the host, so a host who never opens anything gets the sync with zero setup. The list lives in each setup
/// (HostProfile.PlayersOwnSettings) and is written as recipes.json next to Coodsz Server Tools on every Start
/// and when it changes; the module reads it again while the server runs.
///
/// The file is ModderLords' recipe file and shape, so a list made in either tool works in the other:
/// {"SchemaVersion": 1, "GeneratedBy": "...", "Mods": [], "PlayersOwnSettings": ["ModuleId", ...]}.
/// PlayersOwnSettings is left out when every mod is locked. Any other field already in the file is kept.
/// </summary>
public static class McmKeepOwn
{
    public const string FileName = "recipes.json";
    public const string Field = "PlayersOwnSettings";

    public static bool Keeps(IEnumerable<string> playersOwn, params string?[] names) =>
        names.Any(name => !string.IsNullOrWhiteSpace(name)
            && playersOwn.Any(id => id.Equals(name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Locks a mod to the host (players use the host's settings) or lets players keep their own; true when it changed.</summary>
    public static bool SetLocked(List<string> playersOwn, string moduleId, bool locked)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return false;
        var keeps = Keeps(playersOwn, moduleId);
        if (keeps == !locked)
            return false;
        if (locked)
            playersOwn.RemoveAll(id => id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        else
            playersOwn.Add(moduleId.Trim());
        return true;
    }

    /// <summary>The recipe text with this list, keeping whatever else <paramref name="existing"/> holds.</summary>
    public static string Json(IEnumerable<string> playersOwn, string? existing = null)
    {
        JsonObject root;
        try
        {
            root = existing == null ? new JsonObject() : JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }
        root["SchemaVersion"] ??= 1;
        root["GeneratedBy"] ??= "Coodsz Coop Server Tool";
        root["Mods"] ??= new JsonArray();
        var list = playersOwn.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
            root.Remove(Field);
        else
            root[Field] = new JsonArray(list.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads PlayersOwnSettings from a recipe (ours or ModderLords'). Missing or broken means none.</summary>
    public static List<string> Read(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root || root[Field] is not JsonArray list)
                return new List<string>();
            return list.Select(node => node is JsonValue value && value.TryGetValue<string>(out var id) ? id : null)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<string>();
        }
    }

    /// <summary>Writes the list into the server module folder. False when the module is not installed yet (Start installs it).</summary>
    public static bool Write(string moduleFolder, IEnumerable<string> playersOwn)
    {
        if (!Directory.Exists(moduleFolder))
            return false;
        var path = Path.Combine(moduleFolder, FileName);
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        var text = Json(playersOwn, existing);
        if (existing == text)
            return true;
        // Atomic: a running server reads this file while it changes and must never see half of it.
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
        return true;
    }

    /// <summary>
    /// Every key a mod can be listed under: its module id, its MCM settings id and its settings folder. An
    /// entry may be any of them (an older entry, or one made in ModderLords), so locking removes them all.
    /// </summary>
    public static bool Lock(List<string> playersOwn, params string?[] keys)
    {
        var names = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).ToList();
        return playersOwn.RemoveAll(id => names.Any(name => id.Equals(name, StringComparison.OrdinalIgnoreCase))) > 0;
    }
}
