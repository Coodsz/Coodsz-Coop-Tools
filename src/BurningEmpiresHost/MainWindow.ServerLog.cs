using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

public partial class MainWindow
{
    private void SeedLogs()
    {
        var file = HostLogFile.Prefer(LogFiles()).FirstOrDefault();
        if (file == null)
            return;
        file.Refresh();
        var text = HostLogFile.ReadTail(file.FullName, 8_000).Trim();
        if (text.Length > 0)
            Coodsz.Ui.UiLanguage.SetLog(LogBox, text);
        _followedLogPath = file.FullName;
        _followedLogLength = file.Length;
    }

    private void FollowServerLog()
    {
        var file = HostLogFile.Prefer(LogFiles()).FirstOrDefault();
        if (file == null)
            return;
        file.Refresh();
        if (!file.FullName.Equals(_followedLogPath, StringComparison.OrdinalIgnoreCase) || file.Length < _followedLogLength)
        {
            _followedLogPath = file.FullName;
            _followedLogLength = Math.Max(0, file.Length - 8_000);
        }

        if (file.Length <= _followedLogLength)
            return;
        if (file.Length - _followedLogLength > 64_000)
            _followedLogLength = file.Length - 64_000;

        string chunk;
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            stream.Position = _followedLogLength;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            chunk = reader.ReadToEnd();
            _followedLogLength = length;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        chunk = chunk.Trim('\r', '\n');
        if (chunk.Length == 0)
            return;
        var lines = 1;
        foreach (var ch in chunk)
        {
            if (ch == '\n')
                lines++;
        }

        if (lines > 120)
            NoteLog("The server wrote " + lines + " log lines at once. They stay in the log file. This window skipped them so it would not freeze.");
        else
        {
            NoteLog(chunk);
            MirrorServerLines(chunk);
        }
    }

    private void ArmCommandReply(string command)
    {
        var file = NewestCoopLog();
        file?.Refresh();
        _replyCommand = command.Trim();
        _replyOffset = file?.Length ?? 0;
        _replyCarry = "";
        _replySawEcho = false;
        _replyCount = 0;
        _replyDeadline = DateTime.UtcNow.AddSeconds(8);
    }

    private async Task<IReadOnlyList<string>> RunCommandAndRead(string command)
    {
        if (!OwnsServerProcess())
        {
            if (!ServerIsRunning())
            {
                var message = CheatBlockReason();
                Remember(message);
                return new[] { message };
            }

            var reply = await SendThroughQueue(command);
            return reply.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        var file = NewestCoopLog();
        file?.Refresh();
        var offset = file?.Length ?? 0L;
        _readingCommand = true;
        _replyCommand = null;
        try
        {
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            _readingCommand = false;
            Remember(ex.Message);
            return new[] { ex.Message };
        }

        Remember("> " + command);
        var lines = new List<string>();
        var carry = "";
        var sawEcho = false;
        DateTime? quietSince = null;
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(80);
            file = NewestCoopLog();
            if (file == null)
                continue;
            file.Refresh();
            if (file.Length < offset)
                offset = 0;
            var grew = false;
            if (file.Length > offset)
            {
                try
                {
                    if (!TryReadLogChunk(file, ref offset, MaxReplyReadBytes, out var piece))
                        continue;
                    carry += piece;
                    grew = true;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            var newline = carry.LastIndexOf('\n');
            if (newline >= 0)
            {
                var ready = carry[..newline];
                carry = carry[(newline + 1)..];
                var stop = false;
                foreach (var raw in ready.Split('\n'))
                {
                    var line = raw.TrimEnd('\r').Trim();
                    if (line.Length == 0)
                        continue;
                    if (!sawEcho)
                    {
                        if (line.Contains("> " + command, StringComparison.Ordinal))
                            sawEcho = true;
                        continue;
                    }

                    if (line.Contains("[DedicatedServer]", StringComparison.Ordinal))
                    {
                        stop = true;
                        break;
                    }

                    lines.Add(StripLogTime(line));
                    if (lines.Count >= MaxShownReplyLines)
                    {
                        lines.Add(ReplyCutNotice);
                        offset = file.Length;
                        carry = "";
                        stop = true;
                        break;
                    }
                }

                if (stop)
                    break;
            }

            if (grew)
                quietSince = null;
            else if (sawEcho)
            {
                quietSince ??= DateTime.UtcNow;
                var quiet = lines.Count > 0 ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(2);
                if (DateTime.UtcNow - quietSince > quiet)
                    break;
            }
        }

        _readingCommand = false;
        if (lines.Count > 0)
            Remember(string.Join(Environment.NewLine, lines));
        return lines;
    }

    private void CollectCommandReply()
    {
        if (_readingCommand || _replyCommand == null)
            return;

        var file = NewestCoopLog();
        if (file != null)
        {
            file.Refresh();
            if (file.Length > _replyOffset)
            {
                try
                {
                    if (!TryReadLogChunk(file, ref _replyOffset, MaxReplyReadBytes, out var piece))
                        return;
                    _replyCarry += piece;
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }

        var newline = _replyCarry.LastIndexOf('\n');
        if (newline >= 0)
        {
            var ready = _replyCarry[..newline];
            _replyCarry = _replyCarry[(newline + 1)..];
            var batch = new List<string>();
            foreach (var raw in ready.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0)
                    continue;
                if (!_replySawEcho)
                {
                    if (line.Contains("> " + _replyCommand, StringComparison.Ordinal))
                        _replySawEcho = true;
                    continue;
                }

                if (line.Contains("[DedicatedServer]", StringComparison.Ordinal))
                {
                    ShowCommandBlock(batch);
                    FinishCommandReply();
                    return;
                }

                if (_replyCount + batch.Count >= MaxShownReplyLines)
                {
                    ShowCommandBlock(batch);
                    ShowCommandLine(ReplyCutNotice);
                    file?.Refresh();
                    _replyOffset = file?.Length ?? _replyOffset;
                    _replyCarry = "";
                    FinishCommandReply();
                    return;
                }

                batch.Add(StripLogTime(line));
            }

            ShowCommandBlock(batch);
        }

        if (DateTime.UtcNow >= _replyDeadline)
            FinishCommandReply();
    }

    private void ShowCommandBlock(List<string> lines)
    {
        if (lines.Count == 0)
            return;
        var text = string.Join(Environment.NewLine, lines);
        Remember(text);
        _replyCount += lines.Count;
        lines.Clear();
    }

    private void ShowCommandLine(string line) => ShowCommandBlock([line]);

    private static bool TryReadLogChunk(FileInfo file, ref long offset, int maxBytes, out string text)
    {
        text = "";
        file.Refresh();
        if (file.Length <= offset)
            return false;
        var count = (int)Math.Min(file.Length - offset, maxBytes);
        var buffer = new byte[count];
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = offset;
        var read = stream.Read(buffer, 0, count);
        if (read <= 0)
            return false;
        var end = read;
        if (offset + read < file.Length)
        {
            var last = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (last > 0)
                end = last + 1;
        }

        offset += end;
        text = Encoding.UTF8.GetString(buffer, 0, end);
        return text.Length > 0;
    }

    private void FinishCommandReply()
    {
        if (_replyCommand == null)
            return;
        if (_replyCount == 0)
        {
            var message = _replySawEcho
                ? "Sent. The server printed nothing after that command."
                : "Sent. The server did not print a reply. The server log is on Logs.";
            Remember(message);
        }

        _replyCommand = null;
        _replyCarry = "";
    }

    private static string StripLogTime(string line)
    {
        if (line.Length > 14 && line[2] == ':' && line[5] == ':' && line[8] == '.')
        {
            var cut = line.IndexOf("  ", StringComparison.Ordinal);
            if (cut is > 0 and < 16)
                return line[(cut + 2)..];
        }

        return line;
    }

    private FileInfo? NewestCoopLog()
    {
        var logs = Path.Combine(_settings.DataRoot, "logs");
        if (!Directory.Exists(logs))
            return null;
        return HostLogFile.ConsoleOf(Directory.EnumerateFiles(logs, "*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path)), _runStartedUtc);
    }


    private bool ScanServing(string path)
    {
        const string marker = "[DedicatedServer] SERVING";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_servingCheckedPath != path)
            {
                _servingCheckedPath = path;
                _servingScanOffset = 0;
            }

            if (stream.Length <= _servingScanOffset)
                return false;

            // Step back far enough to read the whole line, so the start banner that only names
            // SERVING is told apart from the real line.
            var start = Math.Max(0, _servingScanOffset - (marker.Length + 400));
            stream.Position = start;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            _servingScanOffset = stream.Length;
            return ServerStateEvent.HasServingLine(text);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string ReadPlayerLog()
    {
        var file = NewestCoopLog();
        if (file == null)
            return _playerSourceText;

        file.Refresh();
        if (file.FullName == _playerSourcePath && file.Length == _playerSourceLength)
            return _playerSourceText;

        const string playersEvent = "\"ev\":\"players\"";
        var tail = HostLogFile.ReadTail(file.FullName, 256_000);
        if (tail.Contains(playersEvent, StringComparison.Ordinal))
            _playerSourceText = tail;
        else if (_playerSourceText.Length == 0 || !file.FullName.Equals(_playerSourcePath, StringComparison.OrdinalIgnoreCase))
            _playerSourceText = HostLogFile.ReadTailContaining(file.FullName, playersEvent, 256_000);

        _playerSourcePath = file.FullName;
        _playerSourceLength = file.Length;
        return _playerSourceText;
    }

    private string ReadNewestLog()
    {
        var file = HostLogFile.Prefer(LogFiles()).FirstOrDefault();
        if (file == null)
        {
            _cachedLogPath = "";
            _cachedLogLength = -1;
            _cachedLogText = _logTail.ToString();
            return _cachedLogText;
        }

        file.Refresh();
        if (file.FullName == _cachedLogPath && file.Length == _cachedLogLength && file.LastWriteTimeUtc == _cachedLogWrite)
            return _cachedLogText;

        _cachedLogPath = file.FullName;
        _cachedLogLength = file.Length;
        _cachedLogWrite = file.LastWriteTimeUtc;
        _cachedLogText = _logTail.ToString() + Environment.NewLine + HostLogFile.ReadTail(file.FullName, 64_000);
        return _cachedLogText;
    }

    private IEnumerable<FileInfo> LogFiles()
    {
        var files = new List<string>();
        var logs = Path.Combine(_settings.DataRoot, "logs");
        if (Directory.Exists(logs))
            files.AddRange(Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories));
        var hostRoot = Path.GetDirectoryName(_settings.ServerRoot);
        if (!string.IsNullOrWhiteSpace(hostRoot) && Directory.Exists(hostRoot))
            files.AddRange(Directory.EnumerateFiles(hostRoot, "server-run*.log", SearchOption.TopDirectoryOnly));
        return HostLogFile.ThisRun(files.Select(path => new FileInfo(path)).Where(file => file.Exists && file.Length > 0), _runStartedUtc);
    }

    private void CaptureLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        // Play hold reads Coop's time lines on the window thread, in the order they came.
        if (line.Contains("[Coop] ", StringComparison.Ordinal))
            Dispatcher.BeginInvoke(() => NotePlayHold(line));
        // The @DS@ serving state is the server's own ready signal; the real SERVING line (not
        // the start banner) is kept for servers that do not send it.
        if (ServerStateEvent.IsServingLine(line))
            _seenServing = true;
        // Burning Empires Coop Compat says when its patch is ready; that is the healthy line.
        if (BeHostReport.PatchReady(line) is { } hooks)
            NoteLog(BeHostReport.ReadyLine(hooks));
        // Start-ModdedServer.ps1's own refusals mean the server never started; say why in plain words.
        if (BeServerLaunch.ScriptRefusal(line) is { } refusal)
            SetControl(refusal, failed: true);
        if (WorldSavedLine.SavedFile(line) is { } savedFile)
        {
            _lastWorldSavedFile = savedFile;
            Interlocked.Increment(ref _worldSavedCount);
        }
        if (line.Contains("@DS@", StringComparison.Ordinal) && ServerStateEvent.Last(line) is { Fatal: true } state)
        {
            _fatalState = state;
            _seenServing = false;
        }
        _logTail.Add(line);
        NoteHostReport(line, exited: false);
        WatchRescueReport(line);
        NoteSavedWorld(line);
        // The join checks need the last few lines, not the whole tail (same window as Coodsz Host).
        foreach (var advice in JoinResultExplain.Explain(_logTail.Recent(60)))
        {
            if (_joinAdvice.Add(advice))
                NoteLog(advice);
        }
    }

    // While the server runs (exited false) a line only counts when it is sure: a blocked DLL or
    // the server's own fatal event. A line that only mentions an exception or a module name (the
    // BetterExceptionWindow mod in the Command Args line) does not mean the server stopped.
    // A report that does not fail while the server runs (a compat setup step, the migration
    // warning) shows once and does not use up the run's one report, so a real failure later in
    // the run, or its exit, still gets its own.
    private void NoteHostReport(string? text, bool exited)
    {
        if (_blockedDllNoted)
            return;
        var report = BeHostReport.Explain(text, _seenServing, exited);
        if (report == null)
            return;
        if (!report.Value.Failed && !exited)
        {
            lock (_infoReportsNoted)
            {
                if (!_infoReportsNoted.Add(report.Value.Status))
                    return;
            }
        }
        else
            _blockedDllNoted = true;
        if (exited)
            _seenServing = false;
        foreach (var item in report.Value.Lines)
            NoteLog(item);
        SetControl(report.Value.Status, report.Value.Failed);
        if (BlockedDllNotice.Matches(text))
            ShowBlockedDllFix();
    }

    private void ShowBlockedDllFix()
    {
        // The blocked DLL line comes from the server output thread; WPF throws off the window thread and the output
        // reader rethrows on a pool thread, which closed the tool. Hop to the window thread first, like TAOM.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ShowBlockedDllFix));
            return;
        }

        ShowPage("Settings");
        if (!_unblockPulse.IsEnabled)
            _unblockPulse.Start();
        PulseUnblockButton();
        SetControl("The server cannot load a DLL. Click the red Unblock DLLs button, then start again.", failed: true);
    }

    private void StopUnblockAlert()
    {
        _unblockPulse.Stop();
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

    private void NoteLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        if (!Dispatcher.CheckAccess())
        {
            var copy = line;
            Dispatcher.BeginInvoke(() => NoteLog(copy));
            return;
        }

        Coodsz.Ui.UiLanguage.AppendLog(LogBox, LogBox.Text.Length > 0 ? Environment.NewLine : "", line);
        PushDashboardLines(line);
        if (LogBox.Text.Length > 80_000)
        {
            var text = LogBox.Text;
            var start = text.Length - 60_000;
            var newline = text.IndexOf('\n', start);
            LogBox.Text = newline >= 0 ? text[(newline + 1)..] : text[start..];
        }

        if (LogsPage.Visibility == Visibility.Visible)
            LogBox.ScrollToEnd();
    }

    private void Remember(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            var copy = line;
            Dispatcher.BeginInvoke(() => Remember(copy));
            return;
        }

        ColoredConsole.Append(HistoryBox, line, ConsolePage.Visibility == Visibility.Visible && AutoScroll.IsChecked == true);
    }
}
