using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

// Crash reports in the Burning Empires server tool, the same as in Coodsz Host: Send my logs in the sidebar
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
        _crashBar = new Coodsz.Ui.ServerCrashBar(this, CrashBarHost, "be", CrashRequest, line => NoteLog(line));
        SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, _crashBar.OpenLogs);
        SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, _crashBar.OpenLogs);
        // My reports under Send my logs in the sidebar, and the check for Coodsz's answers (ReportReplyWindow.cs).
        Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, _crashBar.OpenLogs, line => NoteLog(line), below: true, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);
        // My reports beside Send my logs on the Logs page, once a report was sent.
        Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot, _crashBar.OpenLogs, line => NoteLog(line), logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs));
        // Coodsz's notice above every page, like Coodsz is on vacation (AnnouncementUi.cs). It names Send my logs, so it only shows where that button is.
        Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, "be");
        Loaded += (_, _) => _crashBar.ShowIntroOnce();
    }

    private void SendLogs_Click(object sender, RoutedEventArgs e) => _crashBar?.OpenLogs();

    // After the server process exited: a crash or a stop nobody asked for brings Send server crash report.
    // A new crash also clears a problem note left from an earlier run (ClearStaleRescueNotice); a note
    // about this crash (the load mode retry) comes after, from OnRescueExit.
    private void OfferServerCrashReport(Process process)
    {
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
        ClearStaleRescueNotice();
        if (_crashBar == null)
            return;
        // The known harmless lines (BurningEmpires.XmlSerializers, the Seq sink on localhost:5341,
        // a bare TargetInvocationException) are never the cause; with nothing real the bar says so.
        var line = BeHostReport.CrashLine(_logTail.ToString());
        var title = _fatalState != null ? CrashReports.ServerCrashTitle(exit.Kind) : BeHostReport.CrashTitle(line, exit.Kind);
        _crashBar.ServerCrashed(line, title, exit.ExitCode);
    }

    // The Burning Empires server logs Send my logs and Send server crash report pack (BeHostLogPack:
    // the console log, the module list and policy, and the BESavedata file names), text only and
    // cleaned, and the server's newest crash dump only while Add the crash dump is ticked.
    // Everything is read here, on the window thread.
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
            Tool = "be",
            Side = "server",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = BeServerPrepare.ReportCoopVersion(_settings.ServerRoot),
            Game = BeStack.GameVersion,
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            Secrets = KnownPasswords(),
            Modules = ReportModules(),
            FileStem = "be-host-logs",
            ModuleRoots = ModuleFingerprints.Roots(null, [serverRoot]),
            Collect = (zip, limits) => BeHostLogPack.Create(zip, serverRoot, dataRoot, programData, windowLog, includeDumps: false, windowsDumpRoot: windowsDumps, limits: limits).Skipped,
            ServerFolders = string.IsNullOrWhiteSpace(serverRoot) ? [] : [serverRoot],
            FindDump = () => BeHostLogPack.LatestDump(programData, windowsDumps, dumpSince, dumpUntil)
        };
    }

    // The setup's server order; a setup that breaks the core order still sends the core.
    private List<(string, string?)> ReportModules()
    {
        IReadOnlyList<string> order;
        try
        {
            order = BeLoadOrder.ServerOrder(HostSetup);
        }
        catch (InvalidOperationException)
        {
            order = BeStack.ServerModules;
        }

        return order.Select(id => (id, (string?)null)).ToList();
    }

    // The server passwords this tool knows, like Coodsz Host: the box, the saved one and the one in
    // the server's own server-config.json. The Burning Empires mod setups carry no password of their own.
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
