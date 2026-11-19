using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher;

// Crash reports in plain view: Send my logs on Play and on Logs and help (the Something went wrong?
// link stays hidden on Play beside them), Send crash report as the biggest button of the crash bar and the
// one time New: send crash reports right from here card. Each one opens the consent window,
// which shows every file before anything is sent (CrashReportWindow.cs).
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
        Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, "coop");
    }

    private void OpenSendLogs() => Coodsz.Ui.CrashReportWindow.Open(this, CrashRequest(Coodsz.Ui.CrashContext.Logs), Log);

    private void SendLogs_Click(object sender, RoutedEventArgs e) => OpenSendLogs();

    // The one line of a crash log that says what crashed, for the crash report summary.
    private static string? CrashLine(StartupCrashExplain.Capture capture) =>
        CrashReports.FindCrashLine(capture.ReportText, capture.GameLog);

    // The client logs Send my logs and Send crash report pack (ClientLogZip), text only and
    // cleaned, and the game's newest crash dump while Add the crash dump is ticked. Everything is
    // read here, on the window thread; Collect and FindDump run later off it.
    private CrashReportRequest CrashRequest(Coodsz.Ui.CrashContext context)
    {
        var gameRoot = GameRoot.Normalize(GamePathBox.Text, _files);
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        var windowsDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
        var activity = Coodsz.Ui.UiLanguage.EnglishLog(LogBox);
        var actionLog = _actions.FilePath;
        var dumpSince = context.DumpSince(DateTime.UtcNow);
        var dumpUntil = context.DumpUntil();
        NoteAction("Crash report", context.Reason == CrashReason.Logs ? "Packed the logs for Send my logs." : "Packed the logs for a crash report.");
        return new CrashReportRequest
        {
            Tool = "coop",
            Side = "client",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = CoopVersion(),
            Game = VersionOf("Native"),
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            Modules = _resolution?.Modules.Where(module => module.Enabled).Select(module => (module.ModuleId, (string?)ManifestVersion(module.Directory))).ToList()
                ?? new List<(string Id, string? Version)>(),
            Secrets = KnownPasswords(),
            FileStem = "coodsz-client-logs",
            ModuleRoots = ModuleFingerprints.Roots(gameRoot),
            Collect = (zip, limits) => ClientLogZip.Create(zip, gameRoot, documents, programData, windowsDumps, activity, actionLog, limits: limits).Skipped,
            FindDump = () => CrashDumps.LatestClientDump(ClientLogZip.ProgramDataCrashes(programData), windowsDumps, dumpSince, dumpUntil)
        };
    }

    // The join passwords this launcher knows: the box, and every saved setup's.
    private IReadOnlyList<string> KnownPasswords() =>
        new[] { CurrentPassword(), _settings.ServerPassword }
            .Concat(_settings.Setups.Select(setup => setup.ServerPassword))
            .Where(password => !string.IsNullOrEmpty(password))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
