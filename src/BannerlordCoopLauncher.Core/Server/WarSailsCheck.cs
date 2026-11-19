using System.Text.Json;
using BannerlordCoopLauncher.Core.Configuration;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// War Sails (NavalDLC) on a Coop dedicated server. The server turns it on with "warSails" in the
/// "modOptions" block of mod-config.json, then wants NavalDLC in its module list and loads NavalDLC's
/// own campaign map, never an overhaul's. These tools can't host it yet, so every engine start
/// (Start server, the mod test) refuses it first instead of letting the server stop with exit code 10.
/// </summary>
public static class WarSailsCheck
{
    public const string ModuleId = "NavalDLC";

    public const string ModuleRefusal =
        "This tool can't host War Sails yet. NavalDLC (War Sails) is in the server's module list. Turn NavalDLC off for the server, then start the server again.";

    /// <summary>Is War Sails on, read the way the server reads it: the key in "modOptions" without regard to
    /// case, true, "true" or a number that is not 0. A missing key or a file the server can't read is off.</summary>
    public static bool IsOn(string? modConfigJson)
    {
        if (string.IsNullOrWhiteSpace(modConfigJson))
            return false;
        try
        {
            using var doc = JsoncEditor.Parse(modConfigJson).ToJsonDocument();
            if (Property(doc.RootElement, "modOptions") is not { ValueKind: JsonValueKind.Object } options
                || Property(options, "warSails") is not { } value)
                return false;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => value.TryGetDouble(out var number) && number != 0,
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Null when the server can start; otherwise the line that says why it was not started.</summary>
    /// <param name="modConfigPath">The mod-config.json the server reads (COOP_DATA_DIR).</param>
    /// <param name="serverModules">The module list the engine would start with.</param>
    public static string? Refusal(string modConfigPath, IEnumerable<string> serverModules)
    {
        if (serverModules.Contains(ModuleId, StringComparer.OrdinalIgnoreCase))
            return ModuleRefusal;
        string? text = null;
        try
        {
            if (File.Exists(modConfigPath))
                text = File.ReadAllText(modConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server can't read it either, so it runs without War Sails.
        }

        return IsOn(text)
            ? "This tool can't host War Sails yet. \"warSails\" is true in mod-config.json. Set it to false in " + modConfigPath + ", then start the server again."
            : null;
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}
