using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BurningEmpires.Core;

// The Burning Empires client's log zip: the shared client pack plus, when a Burning Empires server
// runs on the same PC, that server's logs. Burning Empires Coop Compat's own coopcompat.log is in
// compat, and that server's one in server/logs/compat.
public static class BeClientLogPack
{
    public const long MaxFileBytes = ClientLogZip.MaxFileBytes;
    public const long ServerLogTotalBytes = 60_000_000;
    public const string Title = "Burning Empires client logs";

    // BootPatches.log grew to 16 MB in the live test; its end is what matters, so the pack keeps the
    // same 4 MB end a crash report keeps of each log (CrashReports.FirstTextBytes).
    public const long BootPatchesBytes = CrashReports.FirstTextBytes;

    // TAOM's debug logs in the game's Logs folder say nothing about Burning Empires, and old ones
    // filled the pack's game logs in the live test.
    public static readonly IReadOnlyList<string> LeftOutGameLogs = ["taom_debug_"];

    public const string ExtraNotes =
        "Burning Empires: Burning Empires Coop Compat's own log is in compat. When a Burning Empires server is on this PC, its Coop_server.log, coop-server, host-console and Coop Compat logs are in server.";

    public static ClientLogPackResult Create(
        string zipPath,
        string? gameRoot,
        string documentsRoot,
        string programDataRoot,
        string? windowsDumpRoot = null,
        string? activityLog = null,
        string? actionLogPath = null,
        IReadOnlyList<BeServerFolder>? servers = null,
        LogZipLimits? limits = null)
    {
        var all = servers ?? Array.Empty<BeServerFolder>();
        limits = WithServerPasswords(limits, all);
        var serverLogs = ServerLogs(all);
        void Extra(LogZipBuilder zip)
        {
            foreach (var log in serverLogs.Files)
                zip.AddFile(log.Path, log.Entry);
            // Coop_server.log and its copies sit in each server folder's engine folder, apart from the logs above.
            foreach (var (server, label) in all.Zip(ServerLabels(all)))
                zip.AddCoopServerLogs(server.ServerRoot, label + "coop-logs");
            foreach (var line in serverLogs.Skipped)
                zip.Skip(line);
        }

        var result = ClientLogZip.Create(
            zipPath,
            gameRoot,
            documentsRoot,
            programDataRoot,
            windowsDumpRoot,
            activityLog,
            actionLogPath,
            new ClientLogZipTaom(CompatLogs(documentsRoot), null, Extra, ExtraNotes)
            {
                BootPatchesMaxBytes = BootPatchesBytes,
                LeaveOutGameLogs = LeftOutGameLogs
            },
            Title,
            limits);
        return new ClientLogPackResult(result.Included, result.Skipped) { DumpIncluded = result.DumpIncluded };
    }

    // The server logs can repeat the join password, so the password of every server found on
    // this PC is taken out of the text files along with the ones the caller knows.
    public static LogZipLimits WithServerPasswords(LogZipLimits? limits, IReadOnlyList<BeServerFolder> servers)
    {
        var current = limits ?? LogZipLimits.Full;
        var passwords = ServerPasswords(servers);
        if (passwords.Count == 0)
            return current;
        return current with { Secrets = current.Secrets.Concat(passwords).Distinct(StringComparer.Ordinal).ToList() };
    }

    /// <summary>The password in each server's DataRoot\server-config.json, where one is set.</summary>
    public static IReadOnlyList<string> ServerPasswords(IReadOnlyList<BeServerFolder> servers)
    {
        var passwords = new List<string>();
        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.DataRoot))
                continue;
            var password = ClientLogPack.ConfigPassword(BeHostFolders.ServerConfig(server.DataRoot));
            if (!string.IsNullOrEmpty(password) && !passwords.Contains(password, StringComparer.Ordinal))
                passwords.Add(password);
        }

        return passwords;
    }

    // Where the compat module writes coopcompat.log and coopcompat.old.log on a game client
    // (CompatLogFile.Folder in the module).
    public static string CompatLogs(string documentsRoot) =>
        Path.Combine(documentsRoot, "Logs", BeServerPath.CompatModuleId);

    // The same on a dedicated server: its logs folder, from the BANNERLORD_USER_DIR the start
    // script sets to the server's data folder.
    public static string? ServerCompatLogs(string? serverLogs) =>
        string.IsNullOrWhiteSpace(serverLogs) ? null : Path.Combine(serverLogs, BeServerPath.CompatModuleId);

    public const string CompatLogPattern = "coopcompat*.log";

    // The logs of a Burning Empires server on this PC, which the client folders do not have when
    // the player hosts and plays on one PC. Text files keep their last 20 MB, and all of them
    // together stay under ServerLogTotalBytes.
    public static LogPick ServerLogs(IReadOnlyList<BeServerFolder> servers, long totalBytes = ServerLogTotalBytes)
    {
        var files = new List<(string Path, string Entry)>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (server, label) in servers.Zip(ServerLabels(servers)))
        {
            var logs = BeHostLogPack.ServerLogs(server.DataRoot);
            var picks = Newest(LogZipBuilder.Files(logs, "Coop_server*.log"), 3, label + "logs/")
                .Concat(Newest(LogZipBuilder.Files(logs, "coop-server-*.log"), 3, label + "logs/"))
                .Concat(Newest(LogZipBuilder.Files(logs, ConsoleLogFile.Pattern), 2, label + "logs/"))
                .Concat(Newest(LogZipBuilder.Files(ServerCompatLogs(logs), CompatLogPattern), 2, label + "logs/compat/"));
            foreach (var pick in picks)
            {
                if (!seen.Add(Path.GetFullPath(pick.Path)))
                    continue;
                var length = Math.Min(Length(pick.Path), MaxFileBytes);
                if (length == 0)
                    continue;
                if (total + length > totalBytes)
                {
                    skipped.Add(Path.GetFileName(pick.Path) + " was left out so the zip stays small enough to send.");
                    continue;
                }

                total += length;
                files.Add(pick);
            }
        }

        return new LogPick(files, skipped);
    }

    // The folder in the zip for each server's logs, with its own name when there are more, and -2, -3
    // after a name another server already has, so two folders both named Server keep their logs apart.
    private static IReadOnlyList<string> ServerLabels(IReadOnlyList<BeServerFolder> servers)
    {
        if (servers.Count <= 1)
            return servers.Select(_ => "server/").ToList();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<string>();
        foreach (var server in servers)
        {
            var name = FolderName(server.ServerRoot ?? server.DataRoot);
            var unique = name;
            for (var n = 2; !taken.Add(unique); n++)
                unique = name + "-" + n;
            labels.Add("server/" + unique + "/");
        }

        return labels;
    }

    private static IEnumerable<(string Path, string Entry)> Newest(IEnumerable<string> paths, int count, string folder) =>
        paths.OrderByDescending(LogZipBuilder.SafeWriteTime).Take(count).Select(path => (path, folder + Path.GetFileName(path)));

    // C:\BurningEmpires-Coop-Host\Server is "C-Server", so two drives keep their own folders.
    private static string FolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "server";
        var drive = (Path.GetPathRoot(path) ?? "").TrimEnd(':', '\\', '/');
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return drive.Length > 0 && drive.Length <= 2 ? drive + "-" + name : name;
    }

    private static long Length(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
