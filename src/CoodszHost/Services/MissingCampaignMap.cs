using System.IO;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Parsing;

namespace CoodszHost.Services;

// A map mod whose server copy lost its SceneObj\Main_map (a copy cut short) still loads, so the start script found
// no custom map and the server quietly made a plain Calradia world. Every player loads the mod's map, so every join
// crashed (Tatache, AD1259 crash reports #13 to #15). Start refuses before the launch, and the start script refuses
// too when the map the tool asked for (COODSZ_CAMPAIGN_MAP) brings no Main_map: it exits with ScriptExitCode.
/// <summary>A map mod copy that landed without its map scene: the mod id (for _recopyGaveUp) and the line.</summary>
public sealed record MapCopyProblem(string Id, string Line);

public static class MissingCampaignMap
{
    public const string EnvironmentVariable = "COODSZ_CAMPAIGN_MAP";
    public const int ScriptExitCode = 20;
    public const string ScriptMarker = "Campaign map missing:";

    public const string ScriptStopText =
        "The server did not start, because the campaign map files are missing on the server or the map mod is turned off. It would start a plain Calradia world, and every player who joins would crash. The Logs page names the map.";

    /// <summary>The map module id the start script checks, or null for the vanilla map.</summary>
    public static string? EnvironmentValue(string? campaignMap) =>
        IsModMap(campaignMap) ? campaignMap!.Trim() : null;

    /// <summary>
    /// engine\Modules\folder\SceneObj\Main_map when the picked map mod has no map scene there (Main_map\scene.xscene;
    /// an empty or cut short Main_map folder brings no map either), else null.
    /// The folder is found by name or by the SubModule.xml id, the way the start script finds modules.
    /// </summary>
    public static string? MissingFolder(string serverRoot, string? campaignMap)
    {
        if (!IsModMap(campaignMap))
            return null;
        var id = campaignMap!.Trim();
        var modulesRoot = ModuleCatalog.ModulesRoot(serverRoot);
        var folder = ModuleFolder(modulesRoot, id) ?? id;
        return BannerlordCoopLauncher.Core.Modules.ServerModuleInstall.HasMapScene(Path.Combine(modulesRoot, folder))
            ? null
            : Path.Combine("engine", "Modules", folder, "SceneObj", "Main_map");
    }

    /// <summary>
    /// The Start refusal: what is missing, what it would do, and where to get the map again. A pinned AD1259 mod
    /// names its own source; any other map mod gets the Workshop line when this PC has it from the Workshop
    /// (ExtraModScan.IsOnWorkshop), else the download line.
    /// </summary>
    public static string Text(string mapName, string folder, AD1259Mod? mod, bool onWorkshop = false)
    {
        if (mod?.NexusId != null)
            return "The server did not start, because the " + mapName + " map files are missing on the server (" + folder
                + "). It would start a plain Calradia world, and every player who joins would crash. Download " + mod.Name + " " + mod.Version
                + " again from Nexus Mods, put its " + mod.Id + " folder in the game's Modules folder and replace the old folder, then press Start server. The tool copies it to the server.";
        if (mod?.WorkshopId != null || onWorkshop)
            return "The server did not start, because the " + mapName + " map files are missing on the server (" + folder
                + "). It would start a plain Calradia world, and every player who joins would crash. Unsubscribe from " + (mod?.Name ?? mapName)
                + " on the Steam Workshop and subscribe again, then press Start server. The tool copies it to the server.";
        // The real folder name (engine\Modules\folder\SceneObj\Main_map), not the display name, so the host knows what to replace.
        var moduleFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(folder))) ?? mapName;
        return "The server did not start, because the " + mapName + " map files are missing on the server (" + folder
            + "). It would start a plain Calradia world, and every player who joins would crash. Download the same version of " + mapName
            + " again from Nexus Mods or wherever you got it, put its " + moduleFolder + " folder in the game's Modules folder and replace the old folder, then press Start server. The tool copies it to the server.";
    }

    /// <summary>
    /// The picked map mod is in the setup but turned off. The start script only counts turned on modules, so it would
    /// find no map from it and refuse.
    /// </summary>
    public static bool IsTurnedOff(string? campaignMap, IEnumerable<(string Id, bool Enabled)> modules) =>
        IsModMap(campaignMap) && modules.Any(module => !module.Enabled && module.Id.Equals(campaignMap!.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A copy of the map mod landed without its map scene: the source named, since that copy is the cause.</summary>
    public static string CopiedWithoutMap(string name, string source) =>
        "The copy of " + name + " from " + source + " has no SceneObj\\Main_map\\scene.xscene, so it brings no campaign map to the server.";

    public static string TurnedOffText(string mapName) =>
        "The server did not start, because the " + mapName + " map mod is turned off in this setup. It would start a plain Calradia world, and every player who joins would crash. On the Mods page, turn "
        + mapName + " on, then press Start server.";

    /// <summary>
    /// The start script's own refusal on a stop nobody asked for. Only its marker line counts: the script passes the
    /// engine's own exit code on, so a bare code 20 could be an engine crash.
    /// </summary>
    public static bool IsScriptStop(bool sawMarker, bool wasExpected) => !wasExpected && sawMarker;

    public static bool IsScriptLine(string? line) =>
        line != null && line.TrimStart().StartsWith(ScriptMarker, StringComparison.Ordinal);

    private static bool IsModMap(string? map) =>
        !string.IsNullOrWhiteSpace(map) && !map.Trim().Equals(StableCoop.VanillaMap, StringComparison.OrdinalIgnoreCase);

    private static string? ModuleFolder(string modulesRoot, string id)
    {
        if (Directory.Exists(Path.Combine(modulesRoot, id)) || !Directory.Exists(modulesRoot))
            return null;
        foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
        {
            try
            {
                var xml = Path.Combine(directory, "SubModule.xml");
                if (File.Exists(xml) && SubModuleXml.TryRead(File.ReadAllText(xml), out var info, out _)
                    && info != null && info.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileName(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
