using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Steam;

/// <summary>
/// The line every tool shows when this PC's Bannerlord is newer than 1.4.8, the game the tools and Coop 0.1.6 and 0.1.7
/// are made for (Steam moved many players to 1.5.7 on 2026-11-11). Only a warning: it blocks nothing by itself.
/// The Realm of Thrones tools run on a Coop nightly build, not 0.1.6 or 0.1.7, so their line names Realm of Thrones.
/// </summary>
public static class NewerGame
{
    public static readonly Version Supported = new(1, 4, 8);

    /// <summary>The line for the game in this folder (its Modules\Native version), or null when that is 1.4.8 or older,
    /// or the folder or its version can't be read.</summary>
    public static string? Notice(string? gameRoot, bool realmOfThrones = false)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return null;
        try
        {
            var xml = Path.Combine(gameRoot, "Modules", "Native", "SubModule.xml");
            return File.Exists(xml) && SubModuleXml.TryRead(File.ReadAllText(xml), out var info, out _) ? Line(info!.Version, realmOfThrones) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The line for a Native version ("v1.5.7", "v1.5.7.12345"), or null when it is 1.4.8 or older or not a version.
    /// Only the first three numbers count, since the build number after them differs between PCs.</summary>
    public static string? Line(string? version, bool realmOfThrones = false)
    {
        var match = Regex.Match((version ?? "").Trim(), @"^[vV]?(\d+)\.(\d+)\.(\d+)(?:\.\d+)*$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
            return null;
        var found = new Version(major, minor, patch);
        if (found <= Supported)
            return null;
        return realmOfThrones
            ? "Bannerlord " + found + " isn't supported yet. Realm of Thrones in Coop runs on Bannerlord 1.4.8. To switch back, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8."
            : "Bannerlord " + found + " isn't supported yet. The tools work on Bannerlord 1.4.8 with Coop 0.1.6 and 0.1.7. To switch back, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8.";
    }
}
