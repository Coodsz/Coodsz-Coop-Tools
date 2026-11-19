using System.Text.Json;

namespace BurningEmpires.Core;

/// <summary>
/// be-launcher.json, next to the Burning Empires client launcher. The fields match the TAOM
/// launcher's, with the player's own Burning Empires archives (the Nexus main file and the hotfix)
/// instead of the TAOM zips. A file without ModSetups gets the one locked Burning Empires setup.
/// </summary>
public sealed class BeClientSettings
{
    public string GamePath { get; set; } = "";
    /// <summary>The Burning Empires main file the player downloaded from Nexus (2.0.1).</summary>
    public string BeArchive { get; set; } = "";
    /// <summary>The hotfix archive that goes over it (2.0.2).</summary>
    public string BeHotfixArchive { get; set; } = "";
    public bool UseSteam { get; set; }
    public string SteamName { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = BeStack.DirectPort;
    public string Password { get; set; } = "";
    public bool DllsUnblocked { get; set; }
    public BeSetupBook ModSetups { get; set; } = new();
    // What this player last joined each host with (JoinMemory), to warn before a different mod list.
    public Dictionary<string, List<string>> LastJoins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>A missing or unreadable file gives the defaults.</summary>
    public static BeClientSettings Load(string path)
    {
        BeClientSettings settings;
        try
        {
            settings = File.Exists(path) ? Parse(File.ReadAllText(path)) : new BeClientSettings();
        }
        catch (IOException)
        {
            settings = new BeClientSettings();
        }

        settings.ModSetups.Ensure();
        return settings;
    }

    public static BeClientSettings Parse(string json)
    {
        BeClientSettings? settings = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                settings = JsonSerializer.Deserialize<BeClientSettings>(json, ReadOptions);
        }
        catch (JsonException)
        {
        }

        settings ??= new BeClientSettings();
        settings.ModSetups ??= new BeSetupBook();
        settings.LastJoins = new Dictionary<string, List<string>>(settings.LastJoins ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
        settings.ModSetups.Ensure();
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(this, WriteOptions);

    public void Save(string path) => File.WriteAllText(path, Serialize());
}
