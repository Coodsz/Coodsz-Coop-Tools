using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Configuration;
using TaomCoop.Core;
using SectionEditor = BannerlordCoopLauncher.Core.Configuration.JsoncEditor;

namespace CoodszHost.Services;

/// <summary>
/// The text helpers the Coodsz Host pages use on server-config.json and mod-config.json.
/// A Coop gameplay setting (one in <see cref="ModSettingsCatalog"/>) is read and written
/// inside its block (modOptions, difficulty, voice) through the section aware editor in
/// Core, where the server reads it. Other keys keep the plain lookups they always had.
/// </summary>
public static class JsoncEditor
{
    public static string Read(string path) => File.ReadAllText(path);

    public static void Write(string path, string text)
    {
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, text);
    }

    public static string GetString(string json, string key)
    {
        if (ModSettingsCatalog.SectionOf(key) is { } section)
            return SectionEditor.Parse(json).GetRawValue(section, key) is { Length: > 1 } raw && raw[0] == '"' && raw[^1] == '"'
                ? Unquote(raw)
                : "";
        var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<v>[^\"]*)\"");
        return match.Success ? match.Groups["v"].Value : "";
    }

    public static string GetRaw(string json, string key)
    {
        if (ModSettingsCatalog.SectionOf(key) is { } section)
            return SectionEditor.Parse(json).GetRawValue(section, key) is { } raw
                && Regex.IsMatch(raw, "^(-?\\d+(?:\\.\\d+)?|true|false)$")
                ? raw
                : "";
        var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(?<v>-?\\d+(?:\\.\\d+)?|true|false)");
        return match.Success ? match.Groups["v"].Value : "";
    }

    public static string SetString(string json, string key, string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return Replace(json, key, "\"" + escaped + "\"");
    }

    public static string SetRaw(string json, string key, string raw) => Replace(json, key, raw);

    public static string SetBool(string json, string key, bool value) =>
        SetRaw(json, key, value ? "true" : "false");

    public static string SetNumber(string json, string key, string value)
    {
        // An empty box (a key an older config never had) keeps the file as it is instead of
        // blocking every other setting from saving.
        if (string.IsNullOrWhiteSpace(value))
            return json;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new InvalidOperationException(key + " must be a number.");
        var raw = number.ToString(number % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);
        return SetRaw(json, key, raw);
    }

    public static string Repair(string json) => JsoncValues.Repair(json);

    private static string Unquote(string raw)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? "";
        }
        catch (System.Text.Json.JsonException)
        {
            return raw[1..^1];
        }
    }

    // One shared setter: a Coop mod-config setting lands inside its block (modOptions,
    // difficulty, voice), where the server reads it. Other keys (server-config.json) stay
    // at the top level.
    private static string Replace(string json, string key, string raw) => CoopModConfig.SetValue(json, key, raw);
}
