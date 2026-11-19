using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace CoodszHost;

// Crash reports in Coodsz Host and the AD1259 server tool: Send my logs in the sidebar (every page)
// and on the Logs page, Something went wrong? Send your logs under Start server, the one time New:
// send crash reports right from here card, and after the server crashed or stopped by itself the
// red bar with Send server crash report. Each one opens the consent window, which shows every file
// before anything is sent (CrashReportWindow.cs). A report carries at most the one newest server
// crash dump, and only while Add the crash dump is ticked.
public partial class MainWindow
{
    private Coodsz.Ui.ServerCrashBar? _crashBar;

    private static string CrashTool => Edition.IsAD1259 ? "ad1259" : "coop";

    private void SetUpCrashReports()
    {
        if (!CrashReports.HasEndpoint())
            return;
        _crashBar = new Coodsz.Ui.ServerCrashBar(this, CrashBarHost, CrashTool, CrashRequest, Append);
        SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, _crashBar.OpenLogs);
        SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, _crashBar.OpenLogs);
        // My reports under Send my logs in the sidebar, and the check for Coodsz's answers (ReportReplyWindow.cs).
        Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, _crashBar.OpenLogs, Append, below: true, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);
        // And beside Send my logs on the Logs page.
        Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot, _crashBar.OpenLogs, Append, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs));
        // Coodsz's notice above every page, like Coodsz is on vacation (AnnouncementUi.cs). It names Send my logs, so it only shows where that button is.
        Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, CrashTool);
        Loaded += (_, _) => _crashBar.ShowIntroOnce();
    }

    private void SendLogs_Click(object sender, RoutedEventArgs e) => _crashBar?.OpenLogs();

    // Only this run's lines: the window still shows earlier runs, and an old error there was named
    // as the cause of a start the old MCM check had refused (crash report #32).
    private string? RunCrashLine() => CrashReports.FindCrashLine(string.Join('\n', _runLines));

    // After the server exited: a crash or a stop the host did not ask for brings Send server crash report.
    // line: the crash line read before the tool wrote its own exit notes (RunCrashLine), so a note is never named.
    private void OfferServerCrashReport(ServerExitInfo exit, string? line)
    {
        // The blocked DLL fix has its own red bar and action, so it stays the one bar.
        if (_crashBar == null || !CrashReports.ShowsServerCrashBar(exit) || _unblockPulse.IsEnabled)
            return;
        _crashBar.ServerCrashed(line, CrashReports.ServerCrashTitle(exit.Kind), exit.ExitCode);
        // One red bar: the crash bar replaces the old Open the Logs tab banner. The Logs tab still pulses.
        HideLogAlertBanner();
    }

    // The server logs Send my logs and Send server crash report pack (HostLogZip), text only
    // and cleaned, and the server's newest crash dump only while Add the crash dump is ticked.
    // Everything is read here, on the window thread.
    private CrashReportRequest CrashRequest(Coodsz.Ui.CrashContext context)
    {
        var crashRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord", "crashes");
        var windowsDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
        var roots = _session.ServerDataRoots()
            .Select(dataRoot => Path.GetDirectoryName(dataRoot)!)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .ToList();
        var windowText = ColoredConsole.Read(ServerLog);
        var consoleText = ColoredConsole.Read(HistoryBox);
        var engineLogs = ServerCrashExplain.EngineLogDirectory();
        var crashExtras = CrashExtras();
        var dumpSince = context.DumpSince(DateTime.UtcNow);
        var dumpUntil = context.DumpUntil();
        _actions.Note("Crash report", context.Reason == CrashReason.Logs ? "Packed the logs for Send my logs." : "Packed the logs for a server crash report.");
        return new CrashReportRequest
        {
            Tool = CrashTool,
            Side = "server",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = ModuleVersion("Coop") is { Length: > 0 } coop ? coop : Edition.IsAD1259 ? AD1259Stack.Find("Coop")?.Version ?? "" : "",
            Game = Edition.IsAD1259 ? AD1259Stack.GameVersion : ModuleVersion("Native", "DedicatedServer.Windows"),
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            Secrets = KnownPasswords(),
            Modules = _modules.Where(row => row.Enabled).OrderBy(row => row.Order).Select(row => (row.Id, (string?)row.Version)).ToList(),
            FileStem = Edition.LogZipPrefix.TrimEnd('-'),
            ModuleRoots = ModuleFingerprints.Roots(null, [_session.ServerRoot]),
            Collect = (zip, limits) => HostLogZip.Create(zip, ServerSession.LogsPath, ServerSession.DocumentsData, roots, engineLogs,
                windowText, consoleText, crashRoot, windowsDumps, includeDumps: false, limits: limits, extraTexts: crashExtras).Skipped,
            ServerFolders = [_session.ServerRoot],
            FindDump = () => CrashDumps.LatestServerDump(crashRoot, windowsDumps, dumpSince, dumpUntil, engineLogs)
        };
    }

    // The version the server's module list shows for the first of these modules that has one, or empty.
    // The Coop row reads the Coop SubModule.xml and Native the game's own, so the report says v0.1.6 and v1.4.8.
    private string ModuleVersion(params string[] ids) =>
        ids.Select(id => _modules.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))?.Version)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version)) ?? "";

    // The server passwords this tool knows: the box, the server settings and every setup's.
    private IReadOnlyList<string> KnownPasswords() =>
        CrashReports.KnownSecrets(new[] { PasswordBox.Text, SharedServerPassword() }
            .Concat(_settings.Profiles.Select(profile => profile.Password)).ToArray());
}
