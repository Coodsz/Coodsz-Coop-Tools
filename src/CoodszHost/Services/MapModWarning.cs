namespace CoodszHost.Services;

// The Coop dedicated server reads a map mod's Main_map but still loads SandBox's own distance cache, so
// a new campaign on a map mod crashes a few minutes in with exit code -532462766 (Coop crash report #48,
// CR-55WBT, Europe1100). A Start click on a new campaign asks once per setup and map in a tool run;
// the host can still start. The AD1259 and Burning Empires compat modules fix the cache, so they skip it.
public static class MapModWarning
{
    public const string Title = "Map mod on a new campaign";
    public const string OkText = "Start anyway";
    public const string Declined = "The server did not start. Pick the Vanilla campaign Map for a new campaign, or press Start server again to start on the map mod anyway.";

    // The map id the Map box uses for the game's own map (MainWindow.VanillaMapId).
    public const string VanillaMapId = "Vanilla";

    // Compat modules that send NavigationCache.Deserialize to the map mod's own cache.
    public static readonly string[] CacheFixModules = ["DedicatedServer.AD1259CoopCompat", "BurningEmpires.CoopCompat"];

    // Maps that keep every vanilla settlement, so the vanilla cache still loads and the server does not crash:
    // More Nations Remastered, the tools' own recommended map setup (checked by Coop PRs on the Workshop DS).
    public static readonly string[] SafeMaps = ["MoreNationsRemastered"];

    /// <summary>
    /// True when Start would add a replacement map mod to a campaign made on another map, a save that never
    /// loads (AD1259 crash report #9). A safe map keeps every vanilla settlement, so a vanilla save still asks.
    /// </summary>
    public static bool RefusesAdding(BannerlordCoopLauncher.Core.Modules.SaveModChange change, string? map) =>
        change.AddsMap(map) && !SafeMaps.Contains(map!.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Ask(string mapName) =>
        "Coop servers crash a few minutes into a new campaign on a map mod like " + mapName
        + ". The Coop server reads the mod's map but the Vanilla map's travel data, and that does not fit. Until Coop fixes it, start on the Vanilla campaign Map, or start anyway to try it.";

    /// <summary>The setup and map a question is asked for, so it comes once per tool run.</summary>
    public static string Key(string? setup, string? map) => (setup ?? "") + "|" + (map ?? "");

    /// <summary>True when a Start click on a new campaign picks a map mod without a compat module that fixes it, not asked yet.</summary>
    public static bool ShouldAsk(bool ad1259, bool startClicked, bool newCampaign, string? map, IEnumerable<string> enabledIds, IReadOnlySet<string> asked, string key)
    {
        if (ad1259 || !startClicked || !newCampaign || string.IsNullOrWhiteSpace(map)
            || map.Trim().Equals(VanillaMapId, StringComparison.OrdinalIgnoreCase) || SafeMaps.Contains(map.Trim(), StringComparer.OrdinalIgnoreCase)
            || asked.Contains(key))
            return false;
        return !enabledIds.Any(id => CacheFixModules.Contains(id, StringComparer.OrdinalIgnoreCase));
    }
}
