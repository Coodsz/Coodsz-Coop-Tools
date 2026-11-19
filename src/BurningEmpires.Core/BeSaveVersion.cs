using BannerlordCoopLauncher.Core.Saves;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// Whether a campaign save is a Burning Empires save, and the Burning Empires version it was made
/// on. The save header lists each module's version the same way TAOM saves do
/// ({"List":{"Modules":..., "Module_BurningEmpires":"v2.0.2", ...}}), so SaveTaomVersion reads it.
/// The 2.0.2 hotfix is save compatible with 2.0.1, so only the first two parts have to match.
/// </summary>
public static class BeSaveVersion
{
    public const string HeaderKey = BeServerPath.BeModuleId;

    public static IReadOnlyDictionary<string, string> Read(string path) => SaveTaomVersion.Read(path);

    /// <summary>The Burning Empires version in the save header, or null when the save does not say.</summary>
    public static string? BeVersion(IReadOnlyDictionary<string, string> saveVersions) =>
        saveVersions.TryGetValue(HeaderKey, out var version) ? version : null;

    /// <summary>True when the save was made with Burning Empires (its module list or its versions name it).</summary>
    public static bool IsBeSave(string path)
    {
        var info = CampaignSaveInfoReader.Read(path);
        if (info != null && info.Modules.Contains(BeServerPath.BeModuleId, StringComparer.OrdinalIgnoreCase))
            return true;
        return BeVersion(Read(path)) != null;
    }

    /// <summary>
    /// True when the save belongs to Burning Empires: its header names Burning Empires, or the
    /// server's BESavedata has slot folders for its name. The Data folder is shared with Coodsz
    /// Host and Coop's own server, so the host lists, renames, deletes and starts only saves that
    /// pass this. beModuleDirectory is engine\Modules\BurningEmpires (BeSaveData.ServerModule).
    /// </summary>
    public static bool IsBeOwned(string savePath, string? beModuleDirectory)
    {
        try
        {
            if (File.Exists(savePath) && IsBeSave(savePath))
                return true;
            return !string.IsNullOrWhiteSpace(beModuleDirectory)
                && BeSaveData.Has(beModuleDirectory, Path.GetFileNameWithoutExtension(savePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The saves that belong to Burning Empires (IsBeOwned), in the order given. The
    /// host's save list, rename and delete go through this.</summary>
    public static IReadOnlyList<string> OnlyBeSaves(IEnumerable<string> savePaths, string? beModuleDirectory) =>
        savePaths.Where(path => IsBeOwned(path, beModuleDirectory)).ToList();

    public static string NotBeSave(string saveName) =>
        saveName + " was not made with Burning Empires. Pick a Burning Empires save, or click New campaign.";

    /// <summary>Null when the server can host this save; otherwise the reason it cannot.</summary>
    public static string? Problem(string path, string installedVersion)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (BeServerLaunch.IsTemplate(name))
            return BeServerLaunch.TemplateReason;
        if (!File.Exists(path))
            return "That save is not in the server's Game Saves folder.";
        if (!IsBeSave(path))
            return NotBeSave(name);
        var made = BeVersion(Read(path));
        return made != null && Differs(made, installedVersion) ? Warning(made, installedVersion) : null;
    }

    /// <summary>True when the first two parts differ (2.0.2 and 1.9 differ, 2.0.1 and 2.0.2 do not).</summary>
    public static bool Differs(string saveVersion, string installedVersion) =>
        !Two(saveVersion).Equals(Two(installedVersion), StringComparison.OrdinalIgnoreCase);

    public static string Warning(string saveVersion, string installedVersion) =>
        "This campaign was made on Burning Empires " + Clean(saveVersion) + ". It will not load on Burning Empires " + Clean(installedVersion)
        + ". Pick a campaign made on " + Clean(installedVersion) + ", or click New campaign.";

    private static string Two(string version) =>
        string.Join(".", Clean(version).Split('.').Take(2)
            .Select(part => int.TryParse(part, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : part));

    private static string Clean(string version) => version.Trim().TrimStart('v', 'V');
}
