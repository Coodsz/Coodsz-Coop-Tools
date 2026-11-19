using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using TaomCoop.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private void ServerLog_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ServerLog.Document.PageWidth = Math.Max(200, ServerLog.ActualWidth - 28);
    }

    private void QueueExitReport(string exitLine, bool wasServing)
    {
        if (!_watchExits || _userStop || _exitExplainQueued)
            return;
        // CoopModPatch's stop after its first time setup: OnServerExited already said to start again.
        // A line without a code waits for the process exit line, which has it.
        if (_modPatchSetup.Seen && ServerCrashExplain.ReadExitCode(exitLine) is null or ModPatchSetup.ExitCode)
            return;
        // The start script found no files for the picked map: OnServerExited already said so (IsMapMissingStop).
        if (_mapMissing)
            return;
        _exitExplainQueued = true;
        var extras = _modules
            .Where(row => row.Enabled && !StableCoop.Order.Contains(row.Id, StringComparer.OrdinalIgnoreCase))
            .Select(row => new ServerCrashExplain.ModName(row.Id, string.IsNullOrWhiteSpace(row.Name) ? row.Id : row.Name))
            .ToList();
        var enabledIds = _modules.Where(row => row.Enabled).Select(row => row.Id).ToList();
        var setupModCount = enabledIds.Count;
        var started = _session.StartedAt;
        var recent = string.Join("\n", _recentServerLines);
        var fatalLine = _fatalLine;
        var serverRoot = ServerRoot;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                var logs = ServerCrashExplain.Read(ServerSession.LogsPath, started, ServerCrashExplain.EngineLogDirectory());
                var lines = ServerCrashExplain.Explain(exitLine, wasServing, recent + "\n" + logs.ServerLog, logs.EngineLog, extras, StableCoop.Order, setupModCount, enabledIds,
                    new ServerCrashExplain.Apart(recent, logs.ServerLog), fatalLine);
                // A game (client) Coop in the server's own Coop folder is told after the cause (AD1259 crash report #19).
                if (CoopServerBin.ClientCoopInServer(serverRoot) is { } clientCopy)
                    lines = lines.Append(clientCopy).ToList();
                _actions.Note("Exit", exitLine + " " + string.Join(" ", lines.Where(line => line.StartsWith("What happened:", StringComparison.OrdinalIgnoreCase))));
                var blockedFix = lines.Count > 0 && lines[0].Contains("cannot load a DLL", StringComparison.OrdinalIgnoreCase);
                if (_blockedDllNoted && blockedFix)
                    return;
                var problem = lines.Any(line => line.StartsWith("What happened:", StringComparison.OrdinalIgnoreCase))
                    && !lines.Any(line => line.Contains("Nothing in the log says a mod broke", StringComparison.Ordinal));
                foreach (var line in lines)
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        // The mod-config.json notice may already be in the log from the live line.
                        if (!_joinAdvice.Contains(line))
                            Append(line);
                    });
                if (blockedFix)
                    _ = Dispatcher.BeginInvoke(ShowBlockedDllFix);
                else if (problem)
                    _ = Dispatcher.BeginInvoke(StartLogAlert);
            }
            catch (Exception ex)
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    Append("Could not read the server log. " + ex.Message);
                    StartLogAlert();
                });
            }
        });
    }

    private void NoteBlockedDll(string line)
    {
        if (_blockedDllNoted)
            return;
        var notice = BlockedDllNotice.Explain(line);
        if (notice == null)
            return;
        _blockedDllNoted = true;
        foreach (var item in notice)
            Append(item);
        SetControl("The server cannot load a DLL. Click the red Unblock DLLs button, then start again.", failed: true);
        ShowBlockedDllFix();
    }

    // Unblock DLLs sits in Settings, Advanced: Windows; that group opens and scrolls into view with the red button.
    private void ShowBlockedDllFix()
    {
        _blockedDllNoted = true;
        ShowPage("Settings");
        WindowsPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => UnblockButton.BringIntoView()));
        StartUnblockAlert();
        StartLogAlert();
    }

    private void StartUnblockAlert()
    {
        _logAlertDefault ??= LogAlertText.Text;
        LogAlertText.Text = "Windows blocked a DLL. Click the red Unblock DLLs button, then press Start server again.";
        if (!_unblockPulse.IsEnabled)
            _unblockPulse.Start();
        PulseUnblockButton();
    }

    private void StopUnblockAlert()
    {
        _unblockPulse.Stop();
        if (_logAlertDefault != null)
            LogAlertText.Text = _logAlertDefault;
        UnblockButton.ClearValue(Control.BackgroundProperty);
        UnblockButton.ClearValue(Control.ForegroundProperty);
        UnblockButton.ClearValue(Control.BorderBrushProperty);
        UnblockButton.ClearValue(Control.BorderThicknessProperty);
        UnblockButton.ClearValue(Control.FontSizeProperty);
        UnblockButton.FontWeight = FontWeights.Normal;
    }

    private void PulseUnblockButton()
    {
        _unblockBright = !_unblockBright;
        var color = _unblockBright ? Color.FromRgb(176, 42, 36) : Color.FromRgb(92, 22, 20);
        UnblockButton.Background = new SolidColorBrush(color);
        UnblockButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 236, 232));
        UnblockButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 120, 108));
        UnblockButton.BorderThickness = new Thickness(3);
        UnblockButton.FontSize = 15;
        UnblockButton.FontWeight = FontWeights.Bold;
    }

    private void NoteServerLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        _recentServerLines.Enqueue(line);
        while (_recentServerLines.Count > 60)
            _recentServerLines.Dequeue();
        foreach (var advice in JoinResultExplain.Explain(string.Join("\n", _recentServerLines)))
        {
            if (_joinAdvice.Add(advice))
                Append(advice);
        }

        if (ServerCrashExplain.FindUnreadableModConfig(line) is { } unreadable)
        {
            var added = false;
            foreach (var advice in unreadable)
                if (_joinAdvice.Add(advice))
                {
                    Append(advice);
                    added = true;
                }
            if (added)
                StartLogAlert();
        }
    }

    // The lines of this server run, kept apart from the window (which still shows earlier runs) so the
    // crash line of a crash comes from the run that crashed. Start server clears it.
    private readonly List<string> _runLines = new();
    private const int MaxRunLines = 3000;

    private void NoteRunLine(string line)
    {
        _runLines.Add(line);
        if (_runLines.Count > MaxRunLines)
            _runLines.RemoveRange(0, _runLines.Count - MaxRunLines);
    }

    private void Append(string line)
    {
        line = ForScreen(line);
        if (LogLineFormatter.Format(line) is not LogLineFormatter.Entry entry)
            return;
        // What the window shows: masked in streamer mode, without the startup noise it hides.
        NoteRunLine(line);

        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10), LineHeight = 22 };
        if (entry.Time.Length > 0)
        {
            paragraph.Inlines.Add(new Run(entry.Time) { Foreground = new SolidColorBrush(Color.FromRgb(110, 122, 144)) });
            paragraph.Inlines.Add(new Run("   "));
        }

        var report = entry.Kind.StartsWith("What ", StringComparison.Ordinal);
        // Shown in the picked language; the log zip still gets the English (LogRun keeps it).
        var kind = Coodsz.Ui.UiLanguage.LogRun(entry.Kind);
        kind.FontWeight = FontWeights.SemiBold;
        kind.Foreground = BrushFor(entry.Tone);
        paragraph.Inlines.Add(kind);
        if (report)
            paragraph.Inlines.Add(new LineBreak());
        var message = report
            ? Coodsz.Ui.UiLanguage.LogRun(entry.Message, LogLineFormatter.ShownMessage(entry))
            : Coodsz.Ui.UiLanguage.LogRun("   " + entry.Message);
        message.Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        paragraph.Inlines.Add(message);
        ServerLog.Document.Blocks.Add(paragraph);
        // The Dashboard's Recent server output card keeps the newest lines.
        PushDashboardLine(entry.Kind, entry.Message, entry.Tone);
        while (ServerLog.Document.Blocks.Count > ServerLogBlocks)
            ServerLog.Document.Blocks.Remove(ServerLog.Document.Blocks.FirstBlock);
        if (!_drainingLines)
            ServerLog.ScrollToEnd();
    }

    // The server log box keeps this many lines.
    private const int ServerLogBlocks = 400;
    // At most this long per round, so the window keeps drawing during a long burst.
    private static readonly TimeSpan DrainBudget = TimeSpan.FromMilliseconds(50);

    // Each queued handler gets true when its server line is drawn; lines that the 400 line box
    // would trim right away are still handled (players, join notes, saves), just not drawn.
    private readonly ServerLineQueue<Action<bool>> _windowLines = new();
    private DispatcherTimer? _lineDrain;
    private bool _drainingLines;
    private bool _drainQueued;

    // Called from the server's output threads; never waits for the window.
    private void PostToWindow(Action<bool> handle) => _windowLines.Add(handle);

    private void StartLineDrain()
    {
        _lineDrain = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _lineDrain.Tick += (_, _) => DrainWindowLines();
        _lineDrain.Start();
    }

    private void DrainWindowLines()
    {
        // A handler that opens a dialog pumps the dispatcher; a nested drain would take lines out of order.
        if (_drainingLines || _windowLines.IsEmpty)
            return;
        _drainingLines = true;
        try
        {
            _windowLines.Drain((handle, draw) =>
            {
                try
                {
                    handle(draw);
                }
                catch (Exception ex)
                {
                    // One bad line must not drop the lines after it.
                    _actions.Note("Server line", ex.GetType().Name + ": " + ex.Message);
                }
            }, ServerLogBlocks, DrainBudget);
        }
        finally
        {
            _drainingLines = false;
            ServerLog.ScrollToEnd();
        }

        // A burst is left over: drain again once the window has drawn and handled input.
        if (!_windowLines.IsEmpty && !_drainQueued)
        {
            _drainQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _drainQueued = false;
                DrainWindowLines();
            }));
        }
    }

    private static SolidColorBrush BrushFor(string tone) => tone switch
    {
        "bad" => new SolidColorBrush(Color.FromRgb(232, 120, 108)),
        "good" => new SolidColorBrush(Color.FromRgb(90, 138, 96)),
        "warn" => new SolidColorBrush(Color.FromRgb(176, 132, 72)),
        _ => new SolidColorBrush(Color.FromRgb(142, 153, 174))
    };

    private void Remember(string line) =>
        ColoredConsole.Append(HistoryBox, line, AutoScroll.IsChecked == true);
}
