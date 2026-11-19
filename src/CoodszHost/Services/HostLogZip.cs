using System.IO;
using System.Text;
using BannerlordCoopLauncher.Core.Support;

namespace CoodszHost.Services;

// One zip a host can send: server logs, what this window captured, the action
// log, the server settings without the password, a check of the server's Coop
// files, and the server's crash reports. Crash dumps only when asked for.
public static class HostLogZip
{
    public const int DumpCount = 2;

    public static LogZipResult Create(
        string zipPath,
        string logsRoot,
        string dataRoot,
        IReadOnlyList<string> serverRoots,
        string? engineLogRoot,
        string? windowLog,
        string? consoleText,
        string? engineCrashRoot = null,
        string? windowsDumpRoot = null,
        bool includeDumps = false,
        LogZipLimits? limits = null,
        IReadOnlyList<(string Entry, string Text)>? extraTexts = null,
        string? modLogsRoot = null,
        string? toolFolder = null,
        IReadOnlyList<string>? steamLibraries = null)
    {
        var zip = new LogZipBuilder(limits);
        zip.AddNewest(LogZipBuilder.Files(logsRoot, "coop-server-*.log"), "server-logs", 3);
        zip.AddNewest(LogZipBuilder.Files(logsRoot, ServerSession.ConsoleLogPattern), "host-console", 3);
        zip.AddFile(Path.Combine(logsRoot, "host-action.log"), "host/host-action.log");
        zip.AddFile(Path.Combine(logsRoot, "Coop_server.log"), "server-logs/Coop_server.log");
        zip.AddFile(Path.Combine(dataRoot, "server-config.json"), "settings/server-config.json");
        zip.AddFile(Path.Combine(dataRoot, "ServerSettingsLock.log"), "server-logs/ServerSettingsLock.log");
        zip.AddFile(Path.Combine(Path.GetDirectoryName(dataRoot) ?? dataRoot, "mod-config.json"), "settings/mod-config.json");
        var number = 0;
        foreach (var root in serverRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            number++;
            var tag = (number == 1 ? "server-folder" : "server-folder-" + number) + "/";
            zip.AddFile(Path.Combine(root, "release-info.txt"), tag + "release-info.txt");
            zip.AddFile(Path.Combine(root, "modded-server-selection.json"), tag + "modded-server-selection.json");
            zip.AddFile(Path.Combine(root, "server-data", "invalid-module-versions.log"), tag + "invalid-module-versions.log");
            // Coop_server.log and its copies sit in the engine folder, not in the logs folder above.
            zip.AddCoopServerLogs(root, tag + "coop-logs");
        }

        // CoopModPatch's logs and Improved Garrisons settings, so its problems can be looked into.
        zip.AddCoopModPatch(modLogsRoot ?? LogZipBuilder.ModLogsRoot(),
            serverRoots.Distinct(StringComparer.OrdinalIgnoreCase).Select(root => Path.Combine(root, "server-data")));

        foreach (var log in LogZipBuilder.ServerEngineLogs(engineLogRoot, 4))
            zip.AddFile(log, "engine-logs/" + Path.GetFileName(log));
        zip.AddNewest(LogZipBuilder.Files(engineLogRoot, "watchdog_log_*.txt"), "engine-logs", 2);

        var crashFolders = LogZipBuilder.ServerCrashFolders(engineCrashRoot);
        zip.AddCrashReports(crashFolders.Take(3));
        if (includeDumps)
            zip.AddDumps(ServerDumps(crashFolders, windowsDumpRoot));

        zip.AddText("host/server-window.txt", windowLog);
        zip.AddToolUpdateLogs(toolFolder, "host");
        zip.AddText("host/console-window.txt", consoleText);
        // What this PC has of the Workshop Coop server, so a "not found" report shows which case it is (CR-P75R3).
        zip.AddText(WorkshopCoopFacts.Entry, WorkshopCoopFacts.ForPack(steamLibraries, TaomCoop.Core.BannerlordFinder.SteamLibraries));
        // After a crash: the Windows event log entries around it and the end of the engine log,
        // and after an exit code 1 nobody asked for, host/exit-evidence.txt.
        foreach (var extra in extraTexts ?? [])
            zip.AddText(extra.Entry, extra.Text);
        return zip.Write(zipPath, Notes(serverRoots, includeDumps));
    }

    public static IReadOnlyList<string> ServerDumps(IReadOnlyList<string> serverCrashFolders, string? windowsDumpRoot) =>
        LogZipBuilder.ServerDumps(serverCrashFolders, windowsDumpRoot, DumpCount);

    public static string CoopCheck(string serverRoot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Server folder: " + ProblemReport.Redact(serverRoot));
        var pins = CoopServerBin.PinnedHashes(serverRoot);
        if (pins.Count == 0)
        {
            builder.AppendLine("release-info.txt has no pinned Coop files.");
            return builder.ToString();
        }

        var folder = CoopServerBin.CoopServerBinFolder(serverRoot);
        foreach (var pin in pins)
        {
            var path = Path.Combine(folder, pin.Key);
            var actual = File.Exists(path) ? CoopServerBin.Sha256(path) : "missing";
            if (actual.Length == 0)
                actual = "unreadable";
            builder.AppendLine(pin.Key + ": " + (actual == pin.Value ? "matches" : "DOES NOT MATCH") + " (found " + actual + ", expected " + pin.Value + ")");
        }

        return builder.ToString();
    }

    private static string Notes(IReadOnlyList<string> serverRoots, bool includeDumps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Coodsz Coop Server Tool log pack, made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Saves are left out. Addresses, Steam ids and passwords are removed from the text files.");
        builder.AppendLine(includeDumps
            ? "Crash dumps (.dmp) are copied as they are. A dump is a snapshot of the server's memory and can hold the password and player addresses, so only send this zip to someone you trust."
            : "Crash dumps were left out.");
        builder.AppendLine();
        builder.AppendLine("Coop files in the dedicated server:");
        if (serverRoots.Count == 0)
            builder.AppendLine("No dedicated server folder was found.");
        foreach (var root in serverRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            builder.Append(CoopCheck(root));
        return builder.ToString();
    }
}
