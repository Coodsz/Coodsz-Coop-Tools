using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using System.IO;

namespace CoodszHost.Services;

/// <summary>
/// Coop Save Rescue (DedicatedServer.CoopSaveRescue) on every start: copied into
/// DedicatedServer\engine\Modules and added last to that start's module list, like Coodsz Server
/// Tools. Coop leaves DedicatedServer.* modules out of the join check, so players can join while it
/// is on. It fixes known save problems (a saved open menu that crashes the server on load, the main
/// party inside a hostile town, broken or empty battles, parties tied to an ended battle, a killed
/// lord still alive) and prints FIXED and Report lines; Coodsz Host shows them and saves. How much it
/// may change comes from the start (AutoRescue.EnvironmentVariable): auto on every start, load once
/// after a crash while the save loaded, full for the manual Run Rescue save.
/// </summary>
public static class SaveRescue
{
    public const string ModuleId = AutoRescue.ModuleId;
    public const string FolderName = "SaveRescue";
    public const string Name = "Coop Save Rescue";
    public const string LogTag = AutoRescue.LogTag;
    public const string ReportTag = AutoRescue.ReportTag;

    // The help and status text is shared with the TAOM server tool.
    public const string Title = RescueSaveText.Title;
    public const string HelpTitle = RescueSaveText.HelpTitle;
    public const string Steps = RescueSaveText.Steps;
    public const string Explain = RescueSaveText.Confirm;

    public static string PayloadRoot(string? baseDirectory = null) =>
        baseDirectory == null ? Path.Combine(ToolPayload.Folder(FolderName), ModuleId) : Path.Combine(baseDirectory, FolderName, ModuleId);

    /// <summary>
    /// Copies the module into DedicatedServer\engine\Modules\DedicatedServer.CoopSaveRescue (always the
    /// shipped copy) and removes what older versions left: the CoopSaveRescue folder (the old id) and a
    /// stray CoopSaveRescue.mode file, which would turn on every fix on every start.
    /// </summary>
    public static void Install(string serverRoot, string? payloadRoot = null)
    {
        var shown = payloadRoot ?? ToolPayload.Expected(FolderName, ModuleId);
        payloadRoot ??= PayloadRoot();
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            throw new InvalidOperationException(ToolPayload.MissingText(Name, shown));
        var modules = ModuleCatalog.ModulesRoot(serverRoot);
        var target = Path.Combine(modules, ModuleId);
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payloadRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        var modeFile = Path.Combine(target, "bin", "Win64_Shipping_Server", AutoRescue.ModeFile);
        if (File.Exists(modeFile))
            File.Delete(modeFile);
        // Only a folder Coodsz Host put there (it holds the rescue DLL) is removed.
        var legacy = Path.Combine(modules, AutoRescue.LegacyModuleId);
        if (Directory.Exists(legacy) && File.Exists(Path.Combine(legacy, "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll")))
            Directory.Delete(legacy, recursive: true);
    }

    /// <summary>The start's mod list with the rescue module added last (the old id taken out).</summary>
    public static List<ModuleRow> WithRescue(IEnumerable<ModuleRow> rows)
    {
        var list = rows.Where(row => !AutoRescue.IsModule(row.Id)).ToList();
        list.Add(new ModuleRow { Id = ModuleId, Name = Name, Version = "v1.3.0", Enabled = true });
        return list;
    }

    public static bool IsReport(string line) => AutoRescue.IsReport(line);

    public static bool FoundNothing(string reportLine) => AutoRescue.FoundNothing(reportLine);

    public static string ReportText(string line) => AutoRescue.ReportText(line);
}
