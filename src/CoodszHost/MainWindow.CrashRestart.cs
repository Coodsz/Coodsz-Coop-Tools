using System.IO;
using System.Windows;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace CoodszHost;

// "Restart the server if it crashes": off unless the host turns it on for the setup.
// The wait starts at 10 seconds and doubles up to 5 minutes; more than 3 crashes in
// 10 minutes stop it, and exit codes 2, 4, 6 and 7 to 11 are never restarted (CrashRestartPolicy).
public partial class MainWindow
{
    private readonly CrashRestartPolicy _crashPolicy = new();
    private DispatcherTimer? _crashRestartTimer;

    private void ShowCrashRestart(HostProfile profile)
    {
        var loading = _loading;
        _loading = true;
        CrashRestartBox.IsChecked = profile.RestartOnCrash;
        _loading = loading;
    }

    private void CrashRestart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var profile = _settings.Current();
        var on = CrashRestartBox.IsChecked == true;
        if (profile.RestartOnCrash == on)
            return;
        profile.RestartOnCrash = on;
        HostSettings.Save(SettingsPath, _settings);
        _actions.Note("Crash restart", on ? "On" : "Off");
        if (!on)
            CancelCrashRestart();
    }

    private void OnServerExited(ServerExitInfo exit)
    {
        // Stop, Restart, Last good save and the broken world guard all set _userStop.
        var info = exit with { WasExpected = exit.WasExpected || _userStop };
        _silentExit = null;
        // A server that stopped took the held speed with it.
        _playHold.Stop();
        // The serving or starting line of the run that ended goes (Stop writes its own line).
        if (!_controlBusy && IsRunLine(Coodsz.Ui.UiLanguage.English(ControlStatus)))
            SetControl("Server is stopped.");
        // CoopModPatch's stop after its first time setup is no crash: no bar, no retry, no restart.
        if (IsModPatchSetupStop(info))
            return;
        // A save the start script refused never loaded: no bar, no save retry, no restart.
        if (IsSaveRefusedStop(info))
            return;
        // The start script found no files for the picked map and stopped before the engine: no bar, no retry, no restart.
        if (IsMapMissingStop(info))
            return;
        // A crash before the new campaign was ever saved: the next Start (or the crash restart) makes it again.
        if (!info.WasExpected)
            KeepNewCampaignIfNotSaved();
        // The Dashboard shows Crashed (Start server, Last good save, Send my logs) until the next start.
        _crashedSinceStart = !info.WasExpected;
        NoteSilentExit(info);
        if (info.IsCrash)
            _lastCrashAt = DateTime.Now;
        // A crash while the save loads first gets one more start with the deeper save fixes,
        // unless a mod's raw Assets folder crashed it (hidden now; the save was never loaded).
        // Decided once, since Decide uses up the one retry. The crash line is read first, so the note
        // HideCrashedAssets writes is never named as the crash.
        var crashLine = RunCrashLine();
        var loadStep = HideCrashedAssets(info) == null
            ? _loadRetry.Decide(info, _servedThisRun, _rescueRun.Mode, loadedASave: !_runCreatesCampaign)
            : LoadCrashStep.NotALoadCrash;
        // A crash, or a stop nobody asked for: the big Send server crash report bar. Not before the
        // load mode retry: its own exit shows it if it crashes too.
        if (LoadCrashRetry.ShowsCrashBar(loadStep))
            OfferServerCrashReport(info, crashLine);
        if (RetryAfterLoadCrash(info, loadStep, crashLine))
            return;
        _crashPolicy.Enabled = _settings.Current().RestartOnCrash;
        var decision = _crashPolicy.Evaluate(info, DateTimeOffset.Now);
        switch (decision.Reason)
        {
            case CrashDecisionReason.NotRecoverable when _crashPolicy.Enabled:
                Append("Restart if it crashes: exit code " + exit.ExitCode + " is not fixed by a restart, so the server was not started again. The lines above say what to do.");
                _actions.Note("Crash restart", "Not restarted. Exit code " + exit.ExitCode);
                break;
            case CrashDecisionReason.CrashLoop:
                Append("Restart if it crashes: the server crashed more than 3 times in 10 minutes, so it was not started again. Read the log, fix the cause, then press Start server.");
                _actions.Note("Crash restart", "Gave up after " + _crashPolicy.RecentCrashCount + " crashes");
                break;
            case CrashDecisionReason.Restarting:
                var seconds = (int)Math.Round(decision.Delay.TotalSeconds);
                Append("Restart if it crashes: the server stopped with exit code " + exit.ExitCode + ". It starts again in " + seconds + " seconds.");
                _actions.Note("Crash restart", "Exit code " + exit.ExitCode + ". Restart in " + seconds + " s");
                ScheduleCrashRestart(decision.Delay);
                break;
        }
    }

    private void ScheduleCrashRestart(TimeSpan delay)
    {
        CancelCrashRestart();
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_crashRestartTimer, timer))
                return;
            // A mod copy still runs (WhileCopying): try again after the same wait.
            if (_copyingMods)
            {
                timer.Start();
                return;
            }

            _crashRestartTimer = null;
            RestartAfterCrash();
        };
        _crashRestartTimer = timer;
        timer.Start();
    }

    private void CancelCrashRestart()
    {
        _crashRestartTimer?.Stop();
        _crashRestartTimer = null;
    }

    private void RestartAfterCrash()
    {
        if (!_settings.Current().RestartOnCrash || _session.IsRunning)
            return;
        // Changed server terms need the host's own answer; a restart never accepts them.
        var folder = Path.GetDirectoryName(Executable);
        if (!string.IsNullOrWhiteSpace(folder) && ServerEula.NeedsAnswer(ServerEula.TermsHash(folder), _settings.AcceptedEulaSha256))
        {
            Append("Restart if it crashes: the Coop server terms changed, so the server was not started again. Press Start server to read them.");
            return;
        }

        if (!BeginControl("Restart if it crashes: starting the server again."))
            return;
        try
        {
            // The crash bar of the crash this answers stays up.
            _restartingAfterCrash = true;
            StartServer();
            SetControl("Server is starting again after a crash. The pill at the top says Server online when it is ready.");
            _actions.Note("Crash restart", "Started again");
        }
        catch (Exception ex)
        {
            Append("Restart if it crashes: the server did not start again. " + ex.Message);
            SetControl(ex.Message, failed: true);
            _actions.Note("Crash restart", "Start failed: " + ex.Message);
        }
        finally
        {
            _restartingAfterCrash = false;
            EndControl();
        }
    }

    private bool _restartingAfterCrash;
}
