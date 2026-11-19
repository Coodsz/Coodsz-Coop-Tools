using System.IO;
using System.Xml.Linq;

namespace CoodszHost.Services;

/// <summary>
/// RBM's four parts and whether each is on. Saved per setup and written into the
/// server account's RBM config before each start.
/// </summary>
public sealed class RbmSettings
{
    public bool Combat { get; set; } = true;
    public bool Ai { get; set; } = true;
    // RBM's Campaign part runs its own economy (market stock, workshop output,
    // treasuries). On a Coop server it drains town stock and stops workshops, so
    // it starts off; a host can turn it back on.
    public bool Campaign { get; set; }
    public bool Tournament { get; set; } = true;
}

/// <summary>
/// RBM reads Documents\Mount and Blade II Bannerlord\Configs\RBM\config.xml of the
/// Windows account that runs the game, and a missing switch counts as on. RBM's code
/// runs only in a player's game: its SubModule.xml has DedicatedServerType none, so the
/// Coop dedicated server loads RBM's XML data but never reads this file.
/// </summary>
public static class RbmSwitches
{
    public const string ModuleId = "RBM";

    public static string ConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "Configs", "RBM", "config.xml");

    public static bool InUse(IEnumerable<string> enabledModuleIds) =>
        enabledModuleIds.Any(id => id.Equals(ModuleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sets the four Enabled switches and keeps every other RBM setting. Returns a
    /// line for the host log when the file changed, otherwise null.
    /// </summary>
    public static string? Apply(RbmSettings settings, string? path = null)
    {
        path ??= ConfigPath();
        if (Write(PartValues(settings), path).Count == 0)
            return null;
        return "RBM: " + Describe(settings) + " (written to " + path + "). Players should set RBM's parts the same way in RBM's menu.";
    }

    /// <summary>
    /// Writes these values into RBM's config.xml with the card's safe edit: only these values change,
    /// the first change keeps config.xml.bak, and a missing file is made with RBM's version so RBM
    /// completes it with its defaults instead of resetting it. Returns the keys that changed.
    /// </summary>
    public static IReadOnlyList<string> Write(IReadOnlyDictionary<RbmOption, string> values, string path)
    {
        if (!File.Exists(path))
        {
            RbmConfigFile.WriteNew(path, values);
            return values.Keys.Select(option => option.Key).ToList();
        }

        var read = RbmConfigFile.Read(path);
        if (read.Problem != null)
            throw new InvalidOperationException(read.Problem + " It was left alone: " + path);
        return RbmConfigFile.Save(path, values);
    }

    /// <summary>
    /// What Keep RBM coop friendly writes into a player's own RBM settings on Connect. The Coop
    /// server does not run RBM's code (its SubModule.xml has DedicatedServerType none), so RBM's
    /// campaign rules run in each player's game. Lords buy equipment there empties the town markets
    /// in coop (the RBM economy report); the Coop friendly button on the Host card is the same change.
    /// </summary>
    public static IReadOnlyDictionary<RbmOption, string> CoopFriendlyValues { get; } =
        new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" };

    /// <summary>
    /// The one path both player launchers take before the game starts (Coop and Friends, and the
    /// AD1259 client launcher, which also passes its four RBM parts). Nothing happens when RBM is not
    /// in the setup. Returns a line for the log, or null when RBM is not in the setup.
    /// </summary>
    public static string? ApplyForPlayer(IEnumerable<string> enabledModuleIds, bool keepCoopFriendly, RbmSettings? parts = null, string? path = null)
    {
        if (!InUse(enabledModuleIds))
            return null;
        path ??= ConfigPath();
        var values = parts == null ? new Dictionary<RbmOption, string>() : PartValues(parts);
        if (keepCoopFriendly)
            foreach (var (option, value) in CoopFriendlyValues)
                values[option] = value;
        if (values.Count == 0)
            return KeepCoopFriendlyOff;

        var changed = Write(values, path);
        var lines = new List<string>();
        if (parts != null)
            lines.Add(PartValues(parts).Keys.Any(option => changed.Contains(option.Key))
                ? "RBM: " + Describe(parts) + " (written to " + path + ")."
                : "RBM: " + Describe(parts) + ".");
        if (!keepCoopFriendly)
            lines.Add(KeepCoopFriendlyOff);
        else if (CoopFriendlyValues.Keys.Any(option => changed.Contains(option.Key)))
            lines.Add("Keep RBM coop friendly turned off Lords buy equipment in this PC's RBM settings (" + path + "), so lords do not empty the town markets in coop.");
        else
            lines.Add("Keep RBM coop friendly: Lords buy equipment is already off in this PC's RBM settings.");
        return string.Join(" ", lines);
    }

    public const string KeepCoopFriendlyOff = "Keep RBM coop friendly is off, so Lords buy equipment stays as this PC's RBM settings have it.";

    /// <summary>The four Enabled values as RBM's config.xml writes them.</summary>
    public static Dictionary<RbmOption, string> PartValues(RbmSettings settings) => new()
    {
        [RbmOptions.Combat] = RbmConfigFile.SwitchValue(settings.Combat),
        [RbmOptions.Ai] = RbmConfigFile.SwitchValue(settings.Ai),
        [RbmOptions.Campaign] = RbmConfigFile.SwitchValue(settings.Campaign),
        [RbmOptions.Tournament] = RbmConfigFile.SwitchValue(settings.Tournament),
    };

    /// <summary>The card shows the RBM settings card: Coodsz Host only, and only when RBM is on in the setup.</summary>
    public static bool ShowsCard(bool ad1259, IEnumerable<string> enabledModuleIds) => !ad1259 && InUse(enabledModuleIds);

    public static string Describe(RbmSettings settings) =>
        "Combat " + OnOff(settings.Combat) + ", AI " + OnOff(settings.Ai) + ", Campaign " + OnOff(settings.Campaign)
        + ", Tournament " + OnOff(settings.Tournament);

    private static string OnOff(bool on) => on ? "on" : "off";
}
