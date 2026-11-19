using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

// Crash reports in plain view, the same as in Coop and Friends: Send my logs in the Next step card and in the
// trouble row on Logs and help, Something went wrong? Send your logs under Connect, Send crash report as the biggest
// button of the crash bar and the one time New: send crash reports right from here card. Each one
// opens the consent window, which shows every file before anything is sent (CrashReportWindow.cs).
public partial class MainWindow
{
    private void SetUpCrashReports()
    {
        if (!CrashReports.HasEndpoint())
            return;
        SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, OpenSendLogs);
        SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, OpenSendLogs);
        // My reports next to Send my logs, and the check for Coodsz's answers (ReportReplyWindow.cs).
        Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, OpenSendLogs, Log, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);
        // A second My reports beside Send my logs on the Logs and help page.
        Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot, OpenSendLogs, Log, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs));
        // Coodsz's notice above every page, like Coodsz is on vacation (AnnouncementUi.cs). It names Send my logs, so it only shows where that button is.
        Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, "be");
    }

    private void OpenSendLogs() => Coodsz.Ui.CrashReportWindow.Open(this, CrashRequest(Coodsz.Ui.CrashContext.Logs), Log);

    private void SendLogs_Click(object sender, RoutedEventArgs e) => OpenSendLogs();

    // The one line of a crash log that says what crashed, for the crash report summary.
    private static string? CrashLine(StartupCrashExplain.Capture capture) =>
        CrashReports.FindCrashLine(capture.ReportText, capture.GameLog);

    // The Burning Empires client logs Send my logs and Send crash report pack (ClientLogZip, the
    // client logs and the logs of a Burning Empires server on this PC, BeClientLogPack), text only and
    // cleaned, and the game's newest crash dump while Add the crash dump is ticked. Everything is
    // read here, on the window thread; Collect and FindDump run later off it.
    private CrashReportRequest CrashRequest(Coodsz.Ui.CrashContext context)
    {
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        var windowsDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
        var gameRoot = _gameRoot;
        var activity = Coodsz.Ui.UiLanguage.EnglishLog(LogBox);
        var actionLog = _actions.FilePath;
        var servers = BeServerFolders.OnThisPc(AppContext.BaseDirectory);
        var dumpSince = context.DumpSince(DateTime.UtcNow);
        var dumpUntil = context.DumpUntil();
        _actions.Note("Crash report", context.Reason == CrashReason.Logs ? "Packed the logs for Send my logs." : "Packed the logs for a crash report.");
        return new CrashReportRequest
        {
            Tool = "be",
            Side = "client",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = CoopVersionHere(),
            Game = BeStack.GameVersion,
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            // Every Burning Empires server's join password too, so player-notes.txt is cleaned of them like the logs.
            Secrets = new[] { CurrentPassword(), _settings.Password }.Concat(BeClientLogPack.ServerPasswords(servers)).Where(password => !string.IsNullOrEmpty(password)).Distinct(StringComparer.Ordinal).ToList(),
            Modules = LaunchOrder().Select(id => (id, (string?)InstalledVersion(id))).ToList(),
            FileStem = "be-client-logs",
            ModuleRoots = ModuleFingerprints.Roots(gameRoot, servers.Select(server => server.ServerRoot)),
            Collect = (zip, limits) => BeClientLogPack.Create(zip, gameRoot, documents, programData, windowsDumps, activity, actionLog, servers, limits: limits).Skipped,
            FindDump = () => CrashDumps.LatestClientDump(ClientLogZip.ProgramDataCrashes(programData), windowsDumps, dumpSince, dumpUntil)
        };
    }
}
