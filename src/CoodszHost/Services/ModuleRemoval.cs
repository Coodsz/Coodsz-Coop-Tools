using System.IO;

namespace CoodszHost.Services;

// Takes a mod out of the dedicated server. The folder is moved to
// DedicatedServer\removed-mods, so a wrong click can be undone by moving it back.
public static class ModuleRemoval
{
    public const string RemovedFolderName = "removed-mods";

    public static string RemovedRoot(string serverRoot) => Path.Combine(serverRoot, RemovedFolderName);

    public static string? Refusal(string moduleId, bool required, bool locked, bool serverRunning, IReadOnlyCollection<string> workingIds)
    {
        if (serverRunning)
            return "Stop the server before you delete a mod. The server keeps its mod files open while it runs.";
        if (locked)
            return "This setup is locked. Its mods cannot be deleted. Pick or make a normal setup first.";
        if (required)
            return moduleId + " is required by the server, so it cannot be deleted.";
        if (workingIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase)
            || moduleId.Equals("Coop", StringComparison.OrdinalIgnoreCase)
            || moduleId.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase))
            return moduleId + " is one of the working Coop mods, so it cannot be deleted here. Turn it off instead.";
        return null;
    }

    public static IReadOnlyList<string> MoveOut(string serverRoot, string moduleId)
    {
        if (!ModuleCatalog.IsSafeModuleId(moduleId))
            throw new InvalidOperationException("That module id cannot be used as a folder name.");
        var folders = ModuleCatalog.FoldersFor(ModuleCatalog.ModulesRoot(serverRoot), moduleId);
        if (folders.Count == 0)
            throw new InvalidOperationException(moduleId + " is not in the server Modules folder any more. Click Refresh.");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var moved = new List<string>();
        Directory.CreateDirectory(RemovedRoot(serverRoot));
        foreach (var folder in folders)
        {
            var target = Path.Combine(RemovedRoot(serverRoot), Path.GetFileName(folder) + "-" + stamp);
            for (var attempt = 2; Directory.Exists(target); attempt++)
                target = Path.Combine(RemovedRoot(serverRoot), Path.GetFileName(folder) + "-" + stamp + "-" + attempt);
            Directory.Move(folder, target);
            moved.Add(target);
        }

        return moved;
    }
}
