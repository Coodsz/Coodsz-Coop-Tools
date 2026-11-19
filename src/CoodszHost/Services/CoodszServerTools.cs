using System.IO;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Server;

namespace CoodszHost.Services;

/// <summary>
/// The Coodsz Server Tools module (DedicatedServer.CoodszTools): the coodsz.* console commands the
/// Cheats page, the player profile and the Items window use. It is Coodsz Host's own server only
/// module, shipped next to CoodszHost.exe (ServerTools\DedicatedServer.CoodszTools) and copied into
/// DedicatedServer\engine\Modules on every Start, then added last to that start's module list.
/// Coop leaves DedicatedServer.* modules out of the client check, so players do not need it, and it
/// never goes into Coop's own folder. Both builds ship it and add it to every Start: the AD1259 server is the same
/// Workshop Coop 0.1.7 server and reads the same coodsz-server-tools.json in CoopData, so the AD1259 build shows every
/// card that file holds (server rules, Max clans per kingdom, PvP rules) and runs the settings sync for players too.
/// Only its Cheats page stays as it was (<see cref="CheatsUseTools"/>).
/// </summary>
public static class CoodszServerTools
{
    public const string ModuleId = ToolsCommand.ModuleId;
    public const string FolderName = "ServerTools";
    public const string Name = ServerToolsModule.Name;

    /// <summary>
    /// The Cheats page sends coodsz.* commands. Not in the AD1259 build: its Cheats page was made for Coop's coop.debug
    /// cheats with no player profile, Players, Map or Encyclopedia page (StartKeep), so that switch is its own feature.
    /// </summary>
    public static bool CheatsUseTools => !Edition.IsAD1259;

    public static string PayloadRoot(string? baseDirectory = null) =>
        baseDirectory == null ? Path.Combine(ToolPayload.Folder(FolderName), ModuleId) : Path.Combine(baseDirectory, FolderName, ModuleId);

    public static string ModuleFolder(string serverRoot) => ServerToolsModule.ModuleFolder(serverRoot);

    /// <summary>
    /// Copies the shipped module into the server when a file is missing or different. Returns a
    /// line for the server log when something was copied, or null. A file in use by a server that
    /// still runs is left for the next Start.
    /// </summary>
    public static string? Install(string serverRoot, string? payloadRoot = null)
    {
        var shown = payloadRoot ?? ToolPayload.Expected(FolderName, ModuleId);
        payloadRoot ??= PayloadRoot();
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            return ToolPayload.MissingText(Name, shown);

        var copied = ServerToolsModule.CopyInto(payloadRoot, serverRoot);
        return copied == 0 ? null : AddedText(Edition.IsAD1259);
    }

    /// <summary>The Start line after a copy. The AD1259 build uses the module for the host rules and the settings sync only.</summary>
    public static string AddedText(bool ad1259) => ad1259
        ? "Added " + Name + " to the server (engine\\Modules\\" + ModuleId + ") for the host rules and the MCM settings players get. Players do not need it."
        : "Added " + Name + " to the server (engine\\Modules\\" + ModuleId + "), so the Cheats page and player profiles can read and change heroes. Players do not need it.";

    /// <summary>This start's module list with the tools module added last (after Coop), once.</summary>
    public static List<ModuleRow> WithTools(IEnumerable<ModuleRow> rows)
    {
        var list = rows.Where(row => !row.Id.Equals(ModuleId, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(new ModuleRow { Id = ModuleId, Name = Name, Version = ToolsCommand.ModuleVersion, Enabled = true });
        return list;
    }
}
