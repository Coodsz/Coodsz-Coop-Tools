using System.IO;

namespace CoodszHost.Services;

// A clean dedicated server's SandBox module has no SceneObj\Main_map: the campaign map comes from
// DedicatedServer.Windows, or from the map mod the host picks. A SandBox\SceneObj\Main_map (a copy
// of the game's map, or the link older tool versions made) next to a mod map makes
// Start-ModdedServer.ps1 stop with "Multiple active modules provide SceneObj\Main_map: Sandbox,
// AD1259" before the game loads. The repair script only removes the old link, never a real folder,
// so Start moves it aside: after asking on a Start click, by itself on an automatic restart, since
// nobody is there to answer and the move can be undone (Fraus, AD1259, 2026-10-25).
public static class ExtraBaseMap
{
    public const string Title = "Extra map in the server";
    public const string OkText = "Move it aside";
    public const string Refused = "The server did not start, because SandBox has its own campaign map next to the map mod. Move the SandBox Main_map folder out of the dedicated server, then press Start server again.";

    // Only Main_map: the script counts nothing else, and other SandBox scenes stay where they are.
    public static string Folder(string serverRoot) =>
        Path.Combine(ModuleCatalog.ModulesRoot(serverRoot), "SandBox", "SceneObj", "Main_map");

    /// <summary>
    /// The turned on mod that brings a map too, when SandBox brings one. The start script counts
    /// every turned on module with a SceneObj\Main_map, not just the picked map. Null when it starts.
    /// </summary>
    public static string? OtherMap(string serverRoot, IEnumerable<string> enabledIds)
    {
        if (!Directory.Exists(Folder(serverRoot)))
            return null;
        var modules = ModuleCatalog.ModulesRoot(serverRoot);
        return enabledIds.FirstOrDefault(id =>
            !id.Equals("SandBox", StringComparison.OrdinalIgnoreCase)
            && !id.Equals("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(modules, id, "SceneObj", "Main_map")));
    }

    public static string Ask(string mapName) =>
        "The server's SandBox module has its own campaign map, which a clean server does not have. With " + mapName
        + " as the map too, the server stops before it loads or loads the wrong map. Move the SandBox map aside? It goes to DedicatedServer\\"
        + ModuleRemoval.RemovedFolderName + ", so it can be put back.";

    /// <summary>
    /// Moves SandBox\SceneObj\Main_map to DedicatedServer\removed-mods and returns where it went.
    /// A link is moved as a link, so the folder it points to is left alone.
    /// </summary>
    public static string MoveAside(string serverRoot)
    {
        var root = ModuleRemoval.RemovedRoot(serverRoot);
        var name = "SandBox-Main_map-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(root, name);
        for (var attempt = 2; Directory.Exists(target); attempt++)
            target = Path.Combine(root, name + "-" + attempt);
        Directory.CreateDirectory(root);
        Directory.Move(Folder(serverRoot), target);
        return target;
    }

    public static string Moved(string serverRoot, string target) =>
        "Moved the SandBox map out of the dedicated server, so only the picked map loads. It is in DedicatedServer\\"
        + Path.GetRelativePath(serverRoot, target) + ".";
}
