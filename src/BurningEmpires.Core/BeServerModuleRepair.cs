using BannerlordCoopLauncher.Core.Server;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// The Burning Empires side of ServerProgramRepair's module DLL check (TAOM, crash report CR-KFRG6).
/// Antivirus can take a module DLL out of a prepared server folder, and the server then dies a few
/// seconds in before any save loads. The modules come from BeStack.ServerModules: the ones Prepare
/// server copied from the Workshop Coop dedicated server (WorkshopModuleFiles), and the server bin
/// copies and Burning Empires Coop Compat each start stages (StagedModuleFiles). Start server puts a
/// missing Workshop module DLL back from the Workshop server only, never overwrites a file and never
/// writes into the Data folder. A missing file whose source is on this PC stops the start, and a run
/// that dies with one gone is no load crash, so there is no load mode retry and no Coop Save Rescue.
/// </summary>
public static class BeServerModuleRepair
{
    public static readonly string ServerBin = Path.Combine("bin", "Win64_Shipping_Server");

    /// <summary>
    /// Every DLL in the server bin of each Workshop server module the Burning Empires server loads, with
    /// the Workshop server's copy as its source. Target is relative to the server folder.
    /// </summary>
    public static IReadOnlyList<(string Target, string Source)> WorkshopModuleFiles(string? workshopServer)
    {
        var files = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(workshopServer))
            return files;
        foreach (var id in BeServerPrepare.Modules.Where(module => BeStack.ServerModules.Contains(module, StringComparer.OrdinalIgnoreCase)))
        {
            var bin = Path.Combine(workshopServer, "engine", "Modules", id, ServerBin);
            try
            {
                if (!Directory.Exists(bin))
                    continue;
                foreach (var dll in Directory.EnumerateFiles(bin, "*.dll").Order(StringComparer.OrdinalIgnoreCase))
                    files.Add((Path.Combine("engine", "Modules", id, ServerBin, Path.GetFileName(dll)), dll));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return files;
    }

    /// <summary>
    /// The DLLs each start stages from this PC (BeServerPath.StageCore): the four Burning Empires server bin
    /// copies and Burning Empires Coop Compat's server DLLs. Staging copies them on every start, so they are
    /// only checked, never put back here.
    /// </summary>
    public static IReadOnlyList<(string Target, string Source)> StagedModuleFiles(BeCoreSources sources)
    {
        var files = new List<(string, string)>();
        var beBin = Path.Combine("engine", "Modules", BeServerPath.BeModuleId, ServerBin);
        var client = Path.Combine(sources.GameRoot, "Modules", BeServerPath.BeModuleId, "bin", "Win64_Shipping_Client");
        foreach (var source in new[] { Path.Combine(client, BeServerPath.BeDll), Path.Combine(client, BeServerPath.ProbeDll), sources.UIExtenderExServerDll, BeServerPath.CustomBattleSource(sources.GameRoot) })
            files.Add((Path.Combine(beBin, Path.GetFileName(source)), source));
        var compatBin = Path.Combine(sources.CompatModule, ServerBin);
        try
        {
            if (Directory.Exists(compatBin))
            {
                foreach (var dll in Directory.EnumerateFiles(compatBin, "*.dll").Order(StringComparer.OrdinalIgnoreCase))
                    files.Add((Path.Combine("engine", "Modules", BeServerPath.CompatModuleId, ServerBin, Path.GetFileName(dll)), dll));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return files;
    }

    /// <summary>The targets that are not in the server folder.</summary>
    public static IReadOnlyList<string> MissingModuleFiles(string? serverRoot, IEnumerable<(string Target, string Source)> files) =>
        string.IsNullOrWhiteSpace(serverRoot)
            ? Array.Empty<string>()
            : files.Where(file => !File.Exists(Path.Combine(serverRoot, file.Target))).Select(file => file.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The missing targets whose source is on this PC, so a healthy server has them. These stop the start
    /// when Repair cannot put them back, and a run that dies with one gone never loaded the save. A name the
    /// source lacks too (an older Coop or Burning Empires build) never blocks.
    /// </summary>
    public static IReadOnlyList<string> BlockingModuleFiles(string? serverRoot, IEnumerable<(string Target, string Source)> files) =>
        string.IsNullOrWhiteSpace(serverRoot)
            ? Array.Empty<string>()
            : files.Where(file => !File.Exists(Path.Combine(serverRoot, file.Target)) && File.Exists(file.Source))
                .Select(file => file.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// What a server exit says about missing module DLLs. Only a crash before serving that this tool did not
    /// ask for counts; then the BlockingModuleFiles of the folder the run started from. With none, the exit
    /// goes on to the load crash path.
    /// </summary>
    public static IReadOnlyList<string> MissingAtExit(ServerExitInfo exit, bool servedThisRun, string? serverRoot, IEnumerable<(string Target, string Source)> files) =>
        !exit.IsCrash || servedThisRun ? Array.Empty<string>() : BlockingModuleFiles(serverRoot, files);

    /// <summary>
    /// Puts each missing target back from its source and checks the copy byte for byte. Only adds missing
    /// files: nothing is overwritten, and nothing is written when the Data folder and the server folder
    /// overlap. Pass WorkshopModuleFiles; the staged files are copied by each start's staging.
    /// </summary>
    public static ServerProgramRepairResult Repair(string serverRoot, string? dataRoot, IEnumerable<(string Target, string Source)> files)
    {
        var restored = new List<string>();
        var missing = files.Where(file => !File.Exists(Path.Combine(serverRoot, file.Target))).ToList();
        if (missing.Count == 0)
            return new ServerProgramRepairResult(restored, Array.Empty<string>());
        if (Overlaps(dataRoot, serverRoot))
            return new ServerProgramRepairResult(restored, missing.Select(file => file.Target).ToList());

        var stillMissing = new List<string>();
        foreach (var (target, source) in missing)
        {
            if (File.Exists(source) && CopyChecked(source, Path.Combine(serverRoot, target)))
                restored.Add(target);
            else
                stillMissing.Add(target);
        }

        return new ServerProgramRepairResult(restored, stillMissing);
    }

    /// <summary>What Start server and Prepare server say when a module DLL could not be put back. Each path is relative to the server folder.</summary>
    public static string MissingText(IEnumerable<string> paths) =>
        "The server folder is missing " + string.Join(", ", paths) + ", so the server cannot start. Antivirus most likely removed them. Restore them from your antivirus quarantine and add the server folder as an exclusion, then click Start server again. If they are not in quarantine, verify Bannerlord in Steam and check that Burning Empires is installed, and Start server puts them back. Your campaign was not touched.";

    private static bool CopyChecked(string source, string destination)
    {
        // Copied under a temp name in the same folder, then moved in without overwrite, so a file something
        // else put there after the scan is never replaced or deleted. A failure removes only the temp.
        var temp = destination + ".coodsz-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, temp, overwrite: false);
            // A copy that does not match never goes in.
            if (!BeServerPrepare.SameBytes(source, temp))
            {
                TryDelete(temp);
                return false;
            }
            File.Move(temp, destination, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }

        // Antivirus can take the copy away again at once. Whatever is there now is left alone,
        // since another program may have put it there after the move.
        try
        {
            return BeServerPrepare.SameBytes(source, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A Data folder path that cannot be read counts as overlapping, so nothing is written.
    private static bool Overlaps(string? dataRoot, string serverRoot)
    {
        try
        {
            return DedicatedServerWatch.IsUnder(dataRoot, serverRoot) || DedicatedServerWatch.IsUnder(serverRoot, dataRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
