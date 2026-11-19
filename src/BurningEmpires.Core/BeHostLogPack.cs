using System.Text;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BurningEmpires.Core;

// The Burning Empires host's log pack for Send my logs and Send server crash report: server logs,
// what the window captured from the server console, Burning Empires Coop Compat's own log,
// engine logs from the dedicated server, server crash reports, the module list and policy the
// server started with, a BESavedata file list (names and sizes only) and the settings without the
// password. Crash dumps only when the host agreed.
public static class BeHostLogPack
{
    public const long MaxFileBytes = LogZipBuilder.MaxTextBytes;
    public const int DumpCount = 2;
    public const string Title = "Burning Empires host log pack";

    public static ClientLogPackResult Create(
        string zipPath,
        string? serverRoot,
        string? dataRoot,
        string programDataRoot,
        string? windowLog = null,
        bool includeDumps = false,
        string? windowsDumpRoot = null,
        LogZipLimits? limits = null,
        string? toolFolder = null,
        IReadOnlyList<string>? steamLibraries = null)
    {
        var zip = new LogZipBuilder(limits);
        var logs = ServerLogs(dataRoot);
        zip.AddNewest(LogZipBuilder.Files(logs, ConsoleLogFile.Pattern), "host-console", 3);
        zip.AddFile(logs == null ? null : Path.Combine(logs, "host-action.log"), "host/host-action.log");
        // Burning Empires Coop Compat's own coopcompat.log and coopcompat.old.log, kept out of the
        // newest 12 below so Coop's logs can never push them out.
        var compatLogs = BeClientLogPack.ServerCompatLogs(logs);
        zip.AddNewest(LogZipBuilder.Files(compatLogs, BeClientLogPack.CompatLogPattern), "compat", 2);
        // The Data folder is shared with other Coop servers, so TAOM debug logs stay out here too.
        zip.AddNewest(LogZipBuilder.Files(logs, "*.log", recursive: true)
            .Where(path => !Path.GetFileName(path).StartsWith("host-console-", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Equals("host-action.log", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetDirectoryName(path), compatLogs, StringComparison.OrdinalIgnoreCase)
                && !BeClientLogPack.LeftOutGameLogs.Any(start => Path.GetFileName(path).StartsWith(start, StringComparison.OrdinalIgnoreCase))), "server-logs", 12);
        zip.AddNewest(LogZipBuilder.Files(logs, "*.txt", recursive: true), "server-logs", 8);
        // Coop_server.log and its copies sit in the server folder's engine folder, not in logs.
        zip.AddCoopServerLogs(serverRoot, "server-folder/coop-logs");
        var engineLogs = HostLogPack.ProgramDataLogs(programDataRoot);
        var engine = LogZipBuilder.ServerEngineLogs(engineLogs, 6);
        foreach (var log in engine)
        {
            zip.AddFile(log, "engine-logs/" + Path.GetFileName(log));
            // The watchdog log of the same session names the build and the module list.
            var watchdog = "watchdog_log_" + ClientLogZip.SessionId(log) + ".txt";
            zip.AddFile(Path.Combine(engineLogs, watchdog), "engine-logs/" + watchdog);
        }

        if (engine.Count > 0 && !engine.Any(LogZipBuilder.IsServerEngineLog))
            zip.Skip("No engine log was from the dedicated server, so the newest engine logs were included.");

        zip.AddFile(dataRoot == null ? null : BeHostFolders.ServerConfig(dataRoot), "host/server-config.redacted.json");
        zip.AddFile(SmallText(serverRoot, "modded-server-modules.txt"), "host/modded-server-modules.txt");
        zip.AddFile(SmallText(serverRoot, BeServerPrepare.PolicyFile), "host/" + BeServerPrepare.PolicyFile);
        zip.AddFile(SmallText(serverRoot, "release-info.txt"), "host/release-info.txt");
        if (!string.IsNullOrWhiteSpace(serverRoot))
        {
            var be = BeSaveData.ServerModule(serverRoot);
            zip.AddFile(SmallText(be, "SubModule.xml"), "host/BurningEmpires-SubModule.xml");
            zip.AddText("host/besavedata-files.txt", BeSaveData.Listing(be));
        }

        var crashFolders = LogZipBuilder.ServerCrashFolders(Path.Combine(programDataRoot, "crashes"));
        zip.AddCrashReports(crashFolders.Take(3));
        if (includeDumps)
            zip.AddDumps(LogZipBuilder.ServerDumps(crashFolders, windowsDumpRoot, DumpCount));
        zip.AddText("host/server-window.txt", windowLog);
        zip.AddToolUpdateLogs(toolFolder, "host");
        // What this PC has of the Workshop Coop server, so a "not found" report shows which case it is (CR-P75R3).
        zip.AddText(WorkshopCoopFacts.Entry, WorkshopCoopFacts.ForPack(steamLibraries, BannerlordFinder.SteamLibraries));

        var result = zip.Write(zipPath, Notes(includeDumps));
        return new ClientLogPackResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    public static IReadOnlyList<string> Dumps(string programDataRoot, string? windowsDumpRoot) =>
        LogZipBuilder.ServerDumps(LogZipBuilder.ServerCrashFolders(Path.Combine(programDataRoot, "crashes")), windowsDumpRoot, DumpCount);

    /// <summary>
    /// The one dump a server crash report or Send my logs may carry (CrashReportRequest.FindDump),
    /// the newest between sinceUtc and untilUtc: the server's crash folders and engine logs are in
    /// ProgramData wherever the server folder is (BANNERLORD_USER_DIR\logs has no engine log), and the
    /// engine's own dotnet.exe.(pid).dmp counts when its rgl_log_(pid).txt is there (CrashDumps.EngineDotnetDumps).
    /// The server and the mod load test runs write the same logs, so both are covered.
    /// </summary>
    public static string? LatestDump(string programDataRoot, string? windowsDumpRoot, DateTime sinceUtc, DateTime untilUtc) =>
        CrashDumps.LatestServerDump(Path.Combine(programDataRoot, "crashes"), windowsDumpRoot, sinceUtc, untilUtc, HostLogPack.ProgramDataLogs(programDataRoot));

    public static string? ServerLogs(string? dataRoot) =>
        string.IsNullOrWhiteSpace(dataRoot) ? null : BeHostFolders.Logs(dataRoot);

    private static string? SmallText(string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        var path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    private static string Notes(bool includeDumps)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Title + ", made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Saves, BESavedata contents and shader caches are left out; BESavedata is only listed by name and size. Addresses, Steam ids and passwords are removed from the text files.");
        builder.AppendLine(includeDumps
            ? "Crash dumps (.dmp) are copied as they are. A dump is a snapshot of the server's memory and can hold the password and player addresses, so only send this zip to someone you trust."
            : "Crash dumps were left out.");
        return builder.ToString();
    }
}
