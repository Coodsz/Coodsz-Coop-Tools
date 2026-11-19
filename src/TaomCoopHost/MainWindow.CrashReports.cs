using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace TaomCoopHost;

// Crash reports in the TAOM server tool, the same as in Coodsz Host: Send my logs in the sidebar
// (every page) and on the Logs page, Something went wrong? Send your logs under Start server, the
// one time New: send crash reports right from here card, and after the server crashed or stopped
// by itself the red bar with Send server crash report. Each one opens the consent window, which
// shows every file before anything is sent. A report carries at most the one newest server crash
// dump, and only while Add the crash dump is ticked.
public partial class MainWindow
{
    private Coodsz.Ui.ServerCrashBar? _crashBar;

    private void SetUpCrashReports()
    {
        if (!CrashReports.HasEndpoint())
            return;
        _crashBar = new Coodsz.Ui.ServerCrashBar(this, CrashBarHost, "taom", CrashRequest, line => NoteLog(line));
        SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, _crashBar.OpenLogs);
        SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, _crashBar.OpenLogs);
        // My reports under Send my logs in the sidebar, and the check for Coodsz's answers (ReportReplyWindow.cs).
        Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, _crashBar.OpenLogs, line => NoteLog(line), below: true, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);
        // My reports beside Send my logs on the Logs page, once a report was sent.
        Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot, _crashBar.OpenLogs, line => NoteLog(line), logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs));
        // Coodsz's notice above every page, like Coodsz is on vacation (AnnouncementUi.cs). It names Send my logs, so it only shows where that button is.
        Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, "taom");
        Loaded += (_, _) => _crashBar.ShowIntroOnce();
    }

    private void SendLogs_Click(object sender, RoutedEventArgs e) => _crashBar?.OpenLogs();

    // After the server process exited: a crash or a stop nobody asked for brings Send server crash report.
    // A known cause brings its own title and line instead of the log's crash line.
    private void OfferServerCrashReport(Process process, string? title = null, string? crashLine = null)
    {
        if (_crashBar == null)
            return;
        int exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var exit = ServerExitInfo.From(exitCode, _stopAsked);
        if (!CrashReports.ShowsServerCrashBar(exit))
            return;
        _crashBar.ServerCrashed(crashLine ?? CrashReports.FindCrashLine(_logTail.ToString()), title ?? CrashReports.ServerCrashTitle(exit.Kind), exit.ExitCode);
    }

    // The TAOM server logs Send my logs and Send server crash report pack (HostLogPack),
    // text only and cleaned, and the server's newest crash dump only while Add the crash dump is
    // ticked. Everything is read here, on the window thread.
    private CrashReportRequest CrashRequest(Coodsz.Ui.CrashContext context)
    {
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        var windowsDumps = LogZipBuilder.WindowsDumpRoot();
        var serverRoot = _settings.ServerRoot;
        var dataRoot = _settings.DataRoot;
        var windowLog = Coodsz.Ui.UiLanguage.EnglishLog(LogBox);
        var dumpSince = context.DumpSince(DateTime.UtcNow);
        var dumpUntil = context.DumpUntil();
        _actions.Note("Crash report", context.Reason == CrashReason.Logs ? "Packed the logs for Send my logs." : "Packed the logs for a server crash report.");
        return new CrashReportRequest
        {
            Tool = "taom",
            Side = "server",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = TaomStack.CoopVersion,
            Game = TaomStack.GameVersion,
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            Secrets = KnownPasswords(),
            Modules = TaomLoadOrder.ServerOrder(HostSetup).Select(id => (id, (string?)null)).ToList(),
            FileStem = "taom-host-logs",
            ModuleRoots = ModuleFingerprints.Roots(null, [serverRoot]),
            Collect = (zip, limits) => HostLogPack.Create(zip, serverRoot, dataRoot, programData, windowLog, includeDumps: false, windowsDumpRoot: windowsDumps, limits: limits).Skipped,
            ServerFolders = string.IsNullOrWhiteSpace(serverRoot) ? [] : [serverRoot],
            FindDump = () => CrashDumps.LatestServerDump(Path.Combine(programData, "crashes"), windowsDumps, dumpSince, dumpUntil, Path.Combine(programData, "logs"))
        };
    }

    // The server passwords this tool knows, like Coodsz Host: the box, the saved one and the one in
    // the server's own server-config.json. The TAOM mod setups carry no password of their own.
    private IReadOnlyList<string> KnownPasswords()
    {
        string? configPassword = null;
        try
        {
            var path = Path.Combine(_settings.DataRoot, "server-config.json");
            if (!string.IsNullOrWhiteSpace(_settings.DataRoot) && File.Exists(path))
                configPassword = ReadString(File.ReadAllText(path), "password");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        return CrashReports.KnownSecrets(PasswordBox.Text, _settings.Password, configPassword);
    }
}
