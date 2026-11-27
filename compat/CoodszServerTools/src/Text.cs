// Coodsz Server Tools: shared helpers. FindHero is ported from CoopKeep
// (https://github.com/Aderimo/bannerlord-coopkeep), src/CoopKeep.ServerTools/HeroAndWorldCommands.cs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;

namespace CoodszServerTools;

internal static class Text
{
    public const string End = "COODSZ_END";
    public const string NoCampaign = "COODSZ_ERROR no campaign is running.";

    public static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    public static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    public static string B(bool v) => v ? "1" : "0";
    public static string Clean(string? s) => (s ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
    public static string Line(params string[] fields) => string.Join("|", fields);
    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

    public static string Lines(List<string> lines)
    {
        lines.Add(End);
        return string.Join("\n", lines);
    }

    /// <summary>The answer to a change: the value before and the value read back after it.</summary>
    public static string Set(Hero hero, string what, int old, int now) =>
        "COODSZ_SET hero=" + hero.StringId + " what=" + what + " old=" + I(old) + " new=" + I(now);

    public static bool TryInt(List<string> args, int index, int min, int max, out int value)
    {
        value = 0;
        return args.Count > index
               && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
               && value >= min && value <= max;
    }

    /// <summary>
    /// Finds a hero by the game id or by Coop's registry id (<c>Hero_lord_1_1</c> is the game's
    /// <c>lord_1_1</c>; the player ids in the save's .json look like <c>Hero_Player2863</c>).
    /// It reads the campaign's hero lists, the same ones coodsz.heroes lists: the game's
    /// Hero.FindFirst walks the character objects instead, and a Coop player hero can be missing
    /// there (a hero with the id <c>Player</c> was listed but then not found).
    /// </summary>
    public static Hero? FindHero(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var hero = ById(id, StringComparison.Ordinal);
        if (hero is null && id.StartsWith("Hero_", StringComparison.Ordinal))
            hero = ById(id.Substring(5), StringComparison.Ordinal);

        return hero ?? ById(id, StringComparison.OrdinalIgnoreCase);
    }

    private static Hero? ById(string id, StringComparison comparison) =>
        Hero.AllAliveHeroes.FirstOrDefault(h => string.Equals(h.StringId, id, comparison))
        ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => string.Equals(h.StringId, id, comparison));

    public static bool TryHero(List<string> args, string usage, out Hero? hero, out string error)
    {
        hero = null;
        error = "";
        if (args.Count < 1) { error = "Usage: " + usage; return false; }
        if (Campaign.Current is null) { error = NoCampaign; return false; }
        hero = FindHero(args[0]);
        if (hero is null) { error = "COODSZ_ERROR hero '" + Clean(args[0]) + "' not found."; return false; }
        return true;
    }

    /// <summary>
    /// Long lists (every item, every troop type) go to a file in this module's out folder, so the
    /// console queue and the server log do not carry thousands of lines. The answer names the file.
    /// </summary>
    public static string ToFile(string name, List<string> lines)
    {
        var folder = OutFolder();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".txt");
        var temp = path + ".tmp";
        lines.Add(End);
        File.WriteAllText(temp, string.Join("\n", lines), new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
        return "COODSZ_FILE|" + name + "|" + path + "|" + I(lines.Count - 1) + "\n" + End;
    }

    private static string OutFolder()
    {
        var location = typeof(Text).Assembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            // ...\Modules\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\DedicatedServer.CoodszTools.dll
            var module = Directory.GetParent(Path.GetDirectoryName(location)!)?.Parent;
            if (module != null) return Path.Combine(module.FullName, "out");
        }

        return Path.Combine(Environment.CurrentDirectory, "coodsz-tools-out");
    }

    public static IEnumerable<T> NotNull<T>(IEnumerable<T?> items) where T : class => items.Where(i => i != null)!;
}
