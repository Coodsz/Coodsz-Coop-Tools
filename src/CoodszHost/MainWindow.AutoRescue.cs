using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace CoodszHost;

// Coop Save Rescue on Start, Restart and Stop (AutoRescue). Every start loads the module in auto
// mode: when its FIXED and Report lines come, a backup of the save from before the fixes is kept,
// the game is saved, and a note stays at the top until it is dismissed (players may be away). A
// crash while the save loads starts the server once more in load mode, never twice in a row. After
// a clean Stop the run's log is read for the known warning signs, and a note says Start fixes them.
// The manual Run Rescue save (Logs page) starts once in full mode for support tickets.
public partial class MainWindow
{
    private readonly RescueRun _rescueRun = new();
    private readonly LoadCrashRetry _loadRetry = new();
    private RescueMode _rescueNextMode = RescueMode.Auto;
    private DispatcherTimer? _rescueRetryTimer;
    // The load crash that waits for the load mode retry, with its crash line. Its Send server crash report bar shows
    // when that retry does not start a server.
    private (ServerExitInfo Exit, string? Line)? _retryCrash;
    // This run made a new campaign, so a crash before SERVING is no save to fix.
    private bool _runCreatesCampaign;

    private void RescueHelp_Click(object sender, RoutedEventArgs e) =>
        ConfirmWindow.Show(this, SaveRescue.HelpTitle, SaveRescue.Steps);

    private void RescueSave_Click(object sender, RoutedEventArgs e)
    {
        if (_session.IsRunning)
        {
            SetControl(RescueSaveText.StopFirst, failed: true);
            ShowPage("Server");
            return;
        }

        if (!ConfirmWindow.Ask(this, SaveRescue.Title, SaveRescue.Explain))
            return;
        if (!BeginControl("Starting the server with Coop Save Rescue..."))
            return;
        try
        {
            _loadRetry.ManualStart();
            _rescueNextMode = RescueMode.Full;
            _startClicked = true;
            StartServer();
            SetControl(RescueSaveText.Running);
            ShowPage("Logs");
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
            _actions.Note("Rescue save failed", ex.Message);
        }
        finally
        {
            _rescueNextMode = RescueMode.Auto;
            _startClicked = false;
            EndControl();
        }
    }

    // StartServer: the module goes into the server on every start. Only the manual run refuses to
    // start without it; a normal start goes on with the copy already there, or without the fixes.
    private bool InstallRescue()
    {
        try
        {
            SaveRescue.Install(ServerRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (_rescueRun.Mode == RescueMode.Full)
                throw new InvalidOperationException(ex.Message);
            var there = File.Exists(Path.Combine(ModuleCatalog.ModulesRoot(ServerRoot), SaveRescue.ModuleId, "SubModule.xml"));
            Append(AutoRescue.InstallFailed(there, ex.Message));
            return there;
        }
    }

    // Every server line (on the window thread). A line replayed from an earlier run is not this run's.
    private void OnRescueLine(string line, bool replaying)
    {
        if (replaying)
            return;
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
        _actions.Note(_rescueRun.Mode == RescueMode.Full ? "Rescue save" : "Save fixes", report);
        if (_rescueRun.Fixes.Count > 0)
        {
            _ = SaveRescueFixesAsync(report);
            return;
        }

        if (_rescueRun.Mode == RescueMode.Full)
            SetControl(RescueSaveText.NothingFound + " " + RescueSaveText.Done, done: true);
        else if (_rescueRun.AfterLoadCrash)
            ShowRescueNotice(AutoRescue.RetryAfterCrash, problem: false, new[] { report });
    }

    // A backup of the save as it is on disk (before the fixes), then the save with the fixes.
    private async Task SaveRescueFixesAsync(string report)
    {
        var name = _settings.Current().SaveName?.Trim() ?? "";
        await BackupBeforeFixesAsync(name);
        string? reply = null;
        var failure = SaveRefusal() ?? (_session.CanSendCommands ? "" : _session.CommandBlockReason());
        // Stop and Restart save by themselves; a save of their own is not sent while they run.
        if (failure.Length == 0 && !_controlBusy)
        {
            try
            {
                reply = await Task.Run(() => _session.SaveCurrentWorld(name));
                Append(reply);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                Append(ex.Message);
            }
        }

        ShowRescueFixes(saved: reply != null);
        if (_rescueRun.Mode != RescueMode.Full)
            return;
        if (reply != null)
            SetControl(report + " " + reply + " " + RescueSaveText.Done, done: true);
        else
            SetControl("Rescue: " + report + " Saving failed: " + failure + " Wait for the 5-minute autosave, then press Stop server.", failed: true);
    }

    private async Task BackupBeforeFixesAsync(string name)
    {
        if (!SaveSet.IsValidSaveName(name) || !File.Exists(Path.Combine(ServerSession.SavesPath, name + ".sav")))
            return;
        try
        {
            await Task.Run(() => SaveBackups.Create(name, BackupReason.BeforeRescue));
            Append(AutoRescue.BackupKept(name));
        }
        // Any failure is only logged. The save fixes and the start after a load crash go on
        // without the backup, and the reason never says the save itself is broken.
        catch (Exception ex)
        {
            Append("Could not back up " + name + ". " + ex.Message);
            _actions.Note("Backup", "Before the save fixes failed for " + name + ": " + ex.Message);
        }
    }

    private void ShowRescueFixes(bool saved)
    {
        var lines = new List<string>();
        if (_rescueRun.AfterLoadCrash)
            lines.Add(AutoRescue.RetryAfterCrash);
        lines.AddRange(_rescueRun.Fixes.Select(fix => "- " + fix));
        lines.Add(saved ? AutoRescue.FixedSaved : AutoRescue.FixedNotSaved);
        ShowRescueNotice(AutoRescue.FixedTitle(_rescueRun.Fixes.Count), problem: false, lines);
    }

    // The note at the top stays until Dismiss. The lines go to the Logs page and the action log too.
    private void ShowRescueNotice(string title, bool problem, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        RescueNoticeTitle.Text = title;
        RescueNoticeTitle.Foreground = new SolidColorBrush(problem ? Color.FromRgb(226, 194, 122) : Color.FromRgb(143, 203, 155));
        RescueNotice.BorderBrush = RescueNoticeTitle.Foreground;
        RescueNoticeBody.Text = string.Join("\n", list.Select(line => Translator.T(line)));
        RescueNoticeBody.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RescueNotice.Visibility = Visibility.Visible;
        Append(title);
        foreach (var line in list)
            Append(line);
        _actions.Note("Save fix note", title + (list.Count == 0 ? "" : " " + string.Join(" ", list)));
    }

    private void RescueNoticeDismiss_Click(object sender, RoutedEventArgs e)
    {
        RescueNotice.Visibility = Visibility.Collapsed;
        _actions.Note("Save fix note", "Dismissed");
    }

    // OnServerExited: true when the crash was handled here (one more start with the deeper fixes).
    private bool RetryAfterLoadCrash(ServerExitInfo exit, LoadCrashStep step, string? crashLine)
    {
        if (step == LoadCrashStep.GaveUp)
        {
            ShowRescueNotice(AutoRescue.RetryFailedTitle, problem: true, new[] { AutoRescue.RetryFailedBody });
            return true;
        }

        if (step != LoadCrashStep.RetryInLoadMode)
            return false;

        _actions.Note("Save fixes", "Load crash, exit code " + exit.ExitCode + ". Starting once more in load mode");
        ShowRescueNotice(AutoRescue.RetryTitle, problem: true, Array.Empty<string>());
        _rescueRetryTimer?.Stop();
        _retryCrash = (exit, crashLine);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_rescueRetryTimer, timer))
                return;
            // A mod copy still runs (WhileCopying): try again after the same wait.
            if (_copyingMods)
            {
                timer.Start();
                return;
            }

            _rescueRetryTimer = null;
            await StartAfterLoadCrashAsync();
        };
        _rescueRetryTimer = timer;
        timer.Start();
        return true;
    }

    // Catches everything: it runs from the timer, where an error would close the tool.
    private async Task StartAfterLoadCrashAsync()
    {
        var crashed = _retryCrash;
        _retryCrash = null;
        if (_session.IsRunning || !BeginControl(AutoRescue.RetryTitle))
        {
            // Another action runs now, so the retry is dropped and the crash bar it held back shows.
            OfferHeldCrashReport(crashed);
            return;
        }

        try
        {
            var name = _settings.Current().SaveName?.Trim() ?? "";
            // Load mode can skip a hero that fails to load, and the next save keeps that. The backup
            // comes first, and a backup that fails is logged without stopping the start.
            await BackupBeforeFixesAsync(name);

            _rescueNextMode = RescueMode.Load;
            StartServer();
            SetControl("Server is starting again with the deeper save fixes. The pill at the top says Server online when it is ready.");
            ShowPage("Logs");
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            SetControl(ex.Message, failed: true);
            _actions.Note("Save fixes", "Start after the load crash failed: " + ex.Message);
            // The retry stopped before a server ran: the crash bar shows after all.
            OfferHeldCrashReport(crashed);
        }
        finally
        {
            _rescueNextMode = RescueMode.Auto;
            EndControl();
        }
    }

    private void CancelRescueRetry()
    {
        var waiting = _rescueRetryTimer != null;
        _rescueRetryTimer?.Stop();
        _rescueRetryTimer = null;
        var crashed = _retryCrash;
        _retryCrash = null;
        // A retry that was waiting will not run now (Stop auto restart, another action), so the crash bar it held back shows.
        if (waiting)
            OfferHeldCrashReport(crashed);
    }

    private void OfferHeldCrashReport((ServerExitInfo Exit, string? Line)? crashed)
    {
        if (crashed is { } crash)
            OfferServerCrashReport(crash.Exit, crash.Line);
    }

    // The console log of one run is read up to this much from its end (a day of a busy server).
    private const int StopScanBytes = 32_000_000;

    // Stop_Click, after a clean stop: the known warning signs in this run's server log. The server
    // Start-ModdedServer.ps1 runs writes no coop-server log, so the console this window kept of the
    // run (host-console-*.log) is read too; it has the server's own lines and Coop's warnings.
    private void ScanAfterStop(DateTime? startedAt, string? consoleLog)
    {
        if (startedAt == null)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                var logs = ServerCrashExplain.Read(ServerSession.LogsPath, startedAt, ServerCrashExplain.EngineLogDirectory());
                var text = logs.ServerLog ?? "";
                if (consoleLog != null && File.Exists(consoleLog))
                    text += "\n" + TaomCoop.Core.HostLogFile.ReadTail(consoleLog, StopScanBytes);
                var problems = AutoRescue.StopScan(text);
                var freed = AutoRescue.PartiesFreedByCoop(text);
                if (problems.Count > 0)
                    _ = Dispatcher.BeginInvoke(() => ShowRescueNotice(AutoRescue.ProblemTitle, problem: true, problems));
                else if (freed > 0)
                    _ = Dispatcher.BeginInvoke(() => ShowRescueNotice(AutoRescue.FreedByCoopTitle(freed), problem: false, new[] { AutoRescue.FreedByCoopBody }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }
}
