using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TaomCoop.Core;

namespace TaomCoopHost;

// Coop Save Rescue on Start, Restart and Stop (AutoRescue), riding in DedicatedServer.Windows on every
// start in auto mode. It fixes known save problems (a saved open menu, the host party inside an enemy
// town, broken or empty battles, parties tied to a battle that already ended, a killed lord still
// alive). When its FIXED and Report lines come, a backup from before the fixes is kept, the game is
// saved and a note stays at the top until it is dismissed. A crash while the save loads starts the
// server once more in load mode, never twice in a row. After a clean Stop the run's log is read for
// the known warning signs. Run Rescue save on the Logs page is the full run for support tickets.
public partial class MainWindow
{
    private readonly RescueRun _rescueRun = new();
    private readonly LoadCrashRetry _loadRetry = new();
    private RescueMode _rescueNextMode = RescueMode.Auto;
    private DispatcherTimer? _rescueRetryTimer;
    // The run whose load crash waits for the load mode retry. Its Send server crash report bar shows when that retry
    // does not start a server.
    private Process? _retryCrashProcess;
    // Stop, Restart, Rescue save, Last good save, the broken world guard and closing the tool ended this
    // run, so its exit is no crash (no load crash retry, no server crash report bar).
    private volatile bool _stopAsked;
    private volatile bool _servedThisRun;
    // TAOM.CoopCompat's own pre serving check stopped this run (TaomStartupCheck). Reset on every start.
    private volatile bool _startCheckStop;
    // The line for a stop by its preflight about one of its own files (antivirus CR-HZYX5, a changed file CR-XXP67).
    private volatile string? _startCheckFileText;
    private int _startCheckStopsInARow;

    private static string RescueDll => Path.Combine(ToolPayload.Folder("SaveRescue"), RescueSubModule.DllName);

    private async void RescueSave_Click(object sender, RoutedEventArgs e)
    {
        if (ServerIsRunning())
        {
            SetControl(RescueSaveText.StopFirst, failed: true);
            ShowPage("Server");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ServerRoot) || !Directory.Exists(_settings.ServerRoot))
        {
            SetControl("Prepare the server before starting it.", failed: true);
            return;
        }

        if (!ConfirmWindow.Ask(this, RescueSaveText.Title, RescueSaveText.Confirm))
            return;

        _actions.Note("Rescue save", "Started a rescue run. Save=" + Path.GetFileNameWithoutExtension(SelectedSavePath() ?? ""));
        _loadRetry.ManualStart();
        _rescueNextMode = RescueMode.Full;
        try
        {
            await StartServerAsync();
        }
        finally
        {
            _rescueNextMode = RescueMode.Auto;
        }

        if (ServerIsRunning())
            SetControl(RescueSaveText.Running);
    }

    private void RescueHelp_Click(object sender, RoutedEventArgs e) =>
        ConfirmWindow.Ask(this, RescueSaveText.HelpTitle, RescueSaveText.Steps, null, "Got it");

    // StartServerCoreAsync: the module goes in on every start. Only the manual run refuses to start
    // without it; a normal start goes on with the copy already there, or without the fixes.
    private bool InstallRescue(string server)
    {
        try
        {
            RescueSubModule.Install(server, RescueDll);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (_rescueNextMode == RescueMode.Full)
            {
                SetControl(ex.Message, failed: true);
                return false;
            }

            NoteLog(AutoRescue.InstallFailed(RescueSubModule.IsInstalled(server), ex.Message));
            return true;
        }
    }

    // A new run: what the fixer says is this run's, and its exit is read fresh.
    private void BeginRescueRun()
    {
        _rescueRun.Begin(_rescueNextMode);
        _stopAsked = false;
        _servedThisRun = false;
        _startCheckStop = false;
        _startCheckFileText = null;
        if (_rescueNextMode == RescueMode.Full)
            NoteLog("Rescue save: Coop Save Rescue runs every save fix on this start. After the Report, press Stop server, then Start server to play normally.");
        else if (_rescueNextMode == RescueMode.Load)
            _actions.Note("Save fixes", "Started with Coop Save Rescue in load mode");
    }

    // Called for every server line (from the output thread).
    private void WatchRescueReport(string line)
    {
        if (ServerStateEvent.IsServingLine(line))
            _servedThisRun = true;
        if (!line.Contains(AutoRescue.LogTag, StringComparison.Ordinal))
            return;
        Dispatcher.BeginInvoke(() => OnRescueLine(line));
    }

    private void OnRescueLine(string line)
    {
        var seen = _rescueRun.Observe(line);
        if (seen == RescueRun.Seen.Fixed)
        {
            _actions.Note("Save fix", AutoRescue.FixedText(line) ?? line);
            // A fix after the report (a battle that broke while playing): the next save keeps it.
            if (_rescueRun.Reported)
                ShowRescueFixes(saved: false);
            return;
        }

        if (seen != RescueRun.Seen.Report)
            return;
        var report = _rescueRun.Report ?? "";
        _actions.Note(_rescueRun.Mode == RescueMode.Full ? "Rescue save" : "Save fixes", "Report: " + report);
        if (_rescueRun.Fixes.Count > 0)
        {
            _ = SaveRescueFixesAsync(report);
            return;
        }

        if (_rescueRun.Mode == RescueMode.Full)
            SetControl(RescueSaveText.NothingFound + " " + RescueSaveText.Done);
        else if (_rescueRun.AfterLoadCrash)
            ShowRescueNotice(AutoRescue.RetryAfterCrash, problem: false, new[] { report });
    }

    // A backup of the save as it is on disk (before the fixes), then the save with the fixes.
    private async Task SaveRescueFixesAsync(string report)
    {
        await BackupBeforeFixesAsync(ConfiguredSaveName());
        var saved = false;
        if (!_busy && ServerIsRunning() && SaveRefusal() == null)
        {
            await ShowWorldSave();
            saved = Coodsz.Ui.UiLanguage.English(ControlStatus).StartsWith("Saved ", StringComparison.Ordinal);
        }

        ShowRescueFixes(saved);
        if (_rescueRun.Mode == RescueMode.Full)
            SetControl(report + " " + RescueSaveText.Done);
    }

    private async Task BackupBeforeFixesAsync(string name)
    {
        if (!SaveSet.IsValidSaveName(name) || string.IsNullOrWhiteSpace(_settings.DataRoot)
            || !File.Exists(Path.Combine(ServerSavesDirectory(), name + ".sav")))
            return;
        try
        {
            var backups = AutoBackups();
            await Task.Run(() => backups.Create(name, BackupReason.BeforeRescue));
            NoteLog(TaomRescueText.BackupKept(name));
        }
        // Any failure is only logged. The save fixes and the start after a load crash go on
        // without the backup, and the reason never says the save itself is broken.
        catch (Exception ex)
        {
            _actions.Note("Backup", "Before the save fixes failed for " + name + ": " + ex.Message);
            NoteLog("Could not back up " + name + ". " + ex.Message);
        }
    }

    private void ShowRescueFixes(bool saved)
    {
        var lines = new List<string>();
        if (_rescueRun.AfterLoadCrash)
            lines.Add(AutoRescue.RetryAfterCrash);
        lines.AddRange(_rescueRun.Fixes.Select(fix => "- " + fix));
        lines.Add(saved ? TaomRescueText.FixedSaved : TaomRescueText.FixedNotSaved);
        ShowRescueNotice(AutoRescue.FixedTitle(_rescueRun.Fixes.Count), problem: false, lines);
    }

    // The note at the top stays until Dismiss. The lines go to the Logs page and the action log too.
    private void ShowRescueNotice(string title, bool problem, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        RescueNoticeTitle.Text = title;
        RescueNoticeTitle.Foreground = new SolidColorBrush(problem ? Color.FromRgb(226, 194, 122) : Color.FromRgb(143, 191, 134));
        RescueNotice.BorderBrush = RescueNoticeTitle.Foreground;
        RescueNoticeBody.Text = string.Join("\n", list.Select(line => Translator.T(line)));
        RescueNoticeBody.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RescueNotice.Visibility = Visibility.Visible;
        NoteLog(title);
        foreach (var line in list)
            NoteLog(line);
        _actions.Note("Save fix note", title + (list.Count == 0 ? "" : " " + string.Join(" ", list)));
    }

    private void RescueNoticeDismiss_Click(object sender, RoutedEventArgs e)
    {
        RescueNotice.Visibility = Visibility.Collapsed;
        _actions.Note("Save fix note", "Dismissed");
    }

    // The process.Exited handler (window thread), before the crash bar and OnRescueExit: TAOM.CoopCompat's
    // own startup check stopped this run. The save never loaded, so there is no load mode retry, and the
    // crash bar only comes when it stops the same way twice in a row.
    private bool IsStartupCheckStop(int? exitCode)
    {
        if (_stopAsked || !_startCheckStop)
        {
            _startCheckStopsInARow = 0;
            return false;
        }

        _startCheckStopsInARow++;
        _actions.Note("Exit", "TAOM.CoopCompat stopped the start with its own pre serving check (exit code " + (exitCode?.ToString() ?? "unknown")
            + "). Not a load crash: no load mode retry. Stops like this in a row: " + _startCheckStopsInARow);
        SetControl(_startCheckFileText ?? TaomStartupCheck.Text, failed: true);
        return true;
    }

    private bool StartupCheckStopShowsBar => _startCheckStopsInARow >= 2;

    // The process.Exited handler (window thread), before the crash bar and OnRescueExit: the engine could
    // not load DLLs that are gone from engine\bin\Win64_Shipping_Server (antivirus, player report CR-WRTH2),
    // or a module DLL the game's Modules folder has a source for is gone from server (CR-KFRG6), checked
    // on disk since the engine logs most of them only as type load errors with no path. server is the
    // folder this run started from. The save never loaded, so there is no load mode retry and no Coop
    // Save Rescue. The crash bar names the missing files.
    private bool IsMissingServerFilesStop(System.Diagnostics.Process process, int? exitCode, string server)
    {
        var (missing, missingModules) = ServerProgramRepair.MissingAtExit(ServerExitInfo.From(exitCode ?? -1, _stopAsked), _servedThisRun,
            _logTail.ToString(), server, GameModulesFolder());
        if (missing.Count == 0 && missingModules.Count == 0)
            return false;

        var names = missing.Concat(missingModules).ToList();
        _actions.Note("Exit", "The engine could not load missing server files (exit code " + (exitCode?.ToString() ?? "unknown")
            + "): " + string.Join(", ", names) + ". Not a load crash: no load mode retry.");
        // OnRescueExit does not run, so the rescue state of this run is cleared here. No retry waits,
        // and the next Start is a plain one with a fresh load crash retry.
        CancelRescueRetry();
        _rescueNextMode = RescueMode.Auto;
        _rescueRun.Begin(RescueMode.Auto);
        _loadRetry.ManualStart();
        SetControl(missingModules.Count > 0
            ? MissingModuleFilesText(missing.Select(name => Path.Combine(ServerProgramRepair.EngineBin, name)).Concat(missingModules))
            : MissingServerFilesText(missing), failed: true);
        OfferServerCrashReport(process, "Server files are missing", string.Join(", ", names));
        return true;
    }

    // The process.Exited handler (window thread): what this exit means for the load mode retry.
    private LoadCrashStep DecideLoadCrash(int? exitCode)
    {
        var exit = ServerExitInfo.From(exitCode ?? -1, _stopAsked);
        // Start server only loads a save already in the server Game Saves folder, so a run always loads one.
        return _loadRetry.Decide(exit, _servedThisRun, _rescueRun.Mode);
    }

    // The process.Exited handler (window thread): one more start with the deeper fixes after a
    // crash while the save loaded.
    private void OnRescueExit(Process process, int? exitCode, LoadCrashStep step)
    {
        if (step == LoadCrashStep.GaveUp)
            ShowRescueNotice(AutoRescue.RetryFailedTitle, problem: true, new[] { TaomRescueText.RetryFailedBody });
        if (step != LoadCrashStep.RetryInLoadMode)
            return;

        _actions.Note("Save fixes", "Load crash, exit code " + (exitCode?.ToString() ?? "unknown") + ". Starting once more in load mode");
        ShowRescueNotice(AutoRescue.RetryTitle, problem: true, Array.Empty<string>());
        _rescueRetryTimer?.Stop();
        _retryCrashProcess = process;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_rescueRetryTimer, timer))
                return;
            _rescueRetryTimer = null;
            await StartAfterLoadCrashAsync();
        };
        _rescueRetryTimer = timer;
        timer.Start();
    }

    private async Task StartAfterLoadCrashAsync()
    {
        var crashed = _retryCrashProcess;
        _retryCrashProcess = null;
        if (ServerIsRunning() || _busy || _startingServer)
        {
            // Another action runs now, so the retry is dropped and the crash bar it held back shows.
            OfferHeldCrashReport(crashed);
            return;
        }

        // A timer start with nobody watching: it loads the save that crashed, never a later pick in the box, and asks nothing.
        var hosted = LastRanSaveName();
        SelectHostedSave(hosted);
        // Load mode can skip a hero that fails to load, and the next save keeps that.
        // A backup that fails is logged without stopping the start.
        await BackupBeforeFixesAsync(hosted);
        _rescueNextMode = RescueMode.Load;
        try
        {
            await StartServerAsync(automatic: true);
        }
        finally
        {
            _rescueNextMode = RescueMode.Auto;
        }

        // The retry stopped before a server ran (a start check refused it): the crash bar shows after all.
        if (_process == null || ReferenceEquals(_process, crashed))
            OfferHeldCrashReport(crashed);
    }

    private void CancelRescueRetry()
    {
        var waiting = _rescueRetryTimer != null;
        _rescueRetryTimer?.Stop();
        _rescueRetryTimer = null;
        var crashed = _retryCrashProcess;
        _retryCrashProcess = null;
        // A retry that was waiting will not run now, so the crash bar it held back shows.
        if (waiting)
            OfferHeldCrashReport(crashed);
    }

    private void OfferHeldCrashReport(Process? crashed)
    {
        if (crashed != null)
            OfferServerCrashReport(crashed);
    }

    // SaveThenStop, after a clean stop: the known warning signs in this run's server log.
    private void ScanAfterStop()
    {
        var console = _logTail.ToString();
        List<FileInfo> files;
        try
        {
            files = LogFiles().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = new List<FileInfo>();
        }

        _ = Task.Run(() =>
        {
            var text = new System.Text.StringBuilder(console);
            foreach (var file in files)
            {
                try
                {
                    text.AppendLine(HostLogFile.ReadTail(file.FullName, 400_000));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            var log = text.ToString();
            var problems = AutoRescue.StopScan(log);
            var freed = AutoRescue.PartiesFreedByCoop(log);
            if (problems.Count > 0)
                _ = Dispatcher.BeginInvoke(() => ShowRescueNotice(AutoRescue.ProblemTitle, problem: true, problems));
            else if (freed > 0)
                _ = Dispatcher.BeginInvoke(() => ShowRescueNotice(AutoRescue.FreedByCoopTitle(freed), problem: false, new[] { AutoRescue.FreedByCoopBody }));
        });
    }
}
