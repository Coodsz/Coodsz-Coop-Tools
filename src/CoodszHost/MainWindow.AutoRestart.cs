using System.IO;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// "Automatic restart" on the Settings page (autoRestart and autoRestartHours in server-config.json).
// The Coop dedicated server ignores both keys, so this window does the restart: once the server has
// served for the set hours while this window was open, Restart's own steps run (warn players, save,
// stop, start the same save).
public partial class MainWindow
{
    private DispatcherTimer? _autoRestartTimer;
    private DateTime _autoRestartWatchingSince = DateTime.Now;
    // The run (its start time) whose skip reason was already written, so a skip is written once per run.
    private DateTime? _autoRestartSkipNoted;

    private void StartAutoRestartTimer()
    {
        _autoRestartWatchingSince = DateTime.Now;
        _autoRestartTimer ??= new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => CheckAutoRestart(), Dispatcher);
        _autoRestartTimer.Start();
        Closed += (_, _) => _autoRestartTimer?.Stop();
    }

    private void CheckAutoRestart()
    {
        // Only a server that serves now, never while it shuts down or during another server action or a mod copy (the timer checks again next minute).
        if (!IsLoaded || !_session.IsRunning || !_serving || _session.Phase != ServerPhase.Serving || _controlBusy || _copyingMods || _crashRestartTimer != null)
            return;
        // An untouched older config counts as off until the host saves the Settings page once.
        var interval = _settings.AutoRestartSaved ? AutoRestartInterval() : null;
        var startedAt = _session.StartedAt;
        if (!ScheduledRestart.IsDue(startedAt, _autoRestartWatchingSince, DateTime.Now, interval))
            return;

        // Restart starts the picked save; a different pick would change the players' campaign.
        if (!ScheduledRestart.KeepsRunningSave(_launchedSave.Length > 0 ? _launchedSave : null, _settings.Current().SaveName))
        {
            NoteAutoRestartSkip(startedAt, ScheduledRestart.OtherSaveLine);
            return;
        }

        // The broken world and failed load guards would stop without saving; leave that to the host.
        if (SaveRefusal() is { } refusal)
        {
            NoteAutoRestartSkip(startedAt, refusal == BrokenWorldGuard.RefusalText ? ScheduledRestart.BrokenWorldLine : ScheduledRestart.FailedLoadLine);
            return;
        }

        // Changed server terms need the host's own answer, so the start after the stop would wait on a window.
        var folder = Path.GetDirectoryName(Executable);
        if (!string.IsNullOrWhiteSpace(folder) && ServerEula.NeedsAnswer(ServerEula.TermsHash(folder), _settings.AcceptedEulaSha256))
        {
            NoteAutoRestartSkip(startedAt, ScheduledRestart.TermsLine);
            return;
        }

        var line = ScheduledRestart.Line(interval!.Value);
        Append(line);
        _actions.Note("Automatic restart", line);
        _ = RestartServerAsync().ContinueWith(
            task => Append("Automatic restart failed: " + task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void NoteAutoRestartSkip(DateTime? run, string line)
    {
        if (_autoRestartSkipNoted == run)
            return;
        _autoRestartSkipNoted = run;
        Append(line);
        _actions.Note("Automatic restart", line);
    }

    private static TimeSpan? AutoRestartInterval()
    {
        try
        {
            if (!File.Exists(ServerSession.ServerConfigPath))
                return null;
            var server = JsoncEditor.Read(ServerSession.ServerConfigPath);
            return ScheduledRestart.Interval(Flag(server, "autoRestart", true), JsoncEditor.GetRaw(server, "autoRestartHours"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}
