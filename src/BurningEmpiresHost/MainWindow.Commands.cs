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
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

public partial class MainWindow
{
    private bool ServerIsRunning() => DedicatedServerWatch.IsRunning(_settings.ServerRoot);

    private bool OwnsServerProcess() => _process is { HasExited: false };

    // A server this window did not start (the tool was closed and opened again)
    // still reads commands from server-data\console-queue.
    private bool CommandPipeReady() => OwnsServerProcess() || ServerIsRunning();

    private string CheatBlockReason() => "Server is not running.";

    private IReadOnlyList<string> CommandQueueRoots() => new[]
    {
        Path.Combine(_settings.ServerRoot, "server-data"),
        Path.Combine(_settings.DataRoot, "server-data")
    };

    // A server this window did not start saves through the command queue.
    // launcher.server.save is the dedicated server's own save command.
    private bool AskQueuedSave()
    {
        var reply = ConsoleQueue.Send(CommandQueueRoots(), "launcher.server.save");
        if (reply.Text.StartsWith("Usage", StringComparison.OrdinalIgnoreCase))
            reply = ConsoleQueue.Send(CommandQueueRoots(), "launcher.server.save " + ConfiguredSaveName());
        Dispatcher.Invoke(() => NoteLog("Save: " + reply.Text));
        return reply.Succeeded
            && !reply.Text.Contains("failed", StringComparison.OrdinalIgnoreCase)
            && !reply.Text.Contains("not started", StringComparison.OrdinalIgnoreCase)
            && !reply.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> SendThroughQueue(string command)
    {
        Remember("> " + command);
        try
        {
            var reply = await Task.Run(() => ConsoleQueue.Send(CommandQueueRoots(), command));
            var text = reply.Failed ? "The server refused that command: " + reply.Text : reply.Text;
            Remember(text);
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Remember(ex.Message);
            return ex.Message;
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandBox.Text.Trim();
        CommandBox.Clear();
        // Checks the line against the Coop command list first (MainWindow.Console.cs).
        SendConsoleCommand(command);
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Send_Click(sender, e);
    }

    // A PC with no browser set makes the open throw, which would close the tool (this tool has no
    // unhandled exception handler), so a failure is a log line with the link instead.
    private void OpenCommandReference_Click(object sender, RoutedEventArgs e)
    {
        const string cheats = "https://bannerlordcoop.com/cheats?tab=all";
        if (!ShellOpen.TryOpen(cheats, out _))
            NoteLog("Could not open the browser. " + cheats);
    }

    // The Full Burning Empires mod list link on the Mods page: MODS.md in the Burning Empires tools repository.
    private void FullModList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(BeCompatList.ModsPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    // ---- Pause and Play ------------------------------------------------------------------------

    private void PauseServer_Click(object sender, RoutedEventArgs e) => _ = SetServerTime(BannerlordCoopLauncher.Core.Server.JoinPause.PauseMode);

    private void PlayServer_Click(object sender, RoutedEventArgs e) => _ = SetServerTime(BannerlordCoopLauncher.Core.Server.JoinPause.NormalMode);

    // The Play button's right click menu.
    private void PlayServerFast_Click(object sender, RoutedEventArgs e) => _ = SetServerTime(BannerlordCoopLauncher.Core.Server.JoinPause.FastMode);

    // Goes out the way the Cheats page sends (stdin, or the command queue for a server this window did
    // not start) and reads the reply, so a refusal shows instead of a done line.
    private async Task SetServerTime(string mode)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }

        if (!CommandPipeReady())
        {
            SetControl(CheatBlockReason(), failed: true);
            ShowTimeButtons(false);
            return;
        }

        var command = ServerTime.Command(mode);
        // Play holds its speed until Pause. The hold changes before the send, so a Coop pause handled meanwhile
        // can't put the old speed back. A server this window did not start holds nothing: its lines never reach
        // the hold.
        var heldBefore = _playHold.Command;
        var holds = OwnsServerProcess();
        if (holds)
            _playHold.HostSent(command);
        else
            _playHold.Stop();
        var press = _playHold.Generation;
        var reply = await RunCommandAndRead(command);
        var result = ServerTime.Result(mode, true, string.Join("\n", reply), holds);
        // A refused press changed nothing on the server, so the hold goes back to what it was.
        if (result.Failed)
            _playHold.Restore(heldBefore, press);
        SetControl(result.Line, failed: result.Failed);
    }

    // Pause and Play only while the campaign is up and no other server action runs.
    private void ShowTimeButtons(bool serving)
    {
        var on = ServerTime.CanUse(serving, _busy || _startingServer);
        PauseServerButton.IsEnabled = on;
        PlayServerButton.IsEnabled = on;
    }

    private string SendCommand(string command)
    {
        var refusal = ServerCommandPolicy.Refusal(command);
        if (refusal != null)
        {
            Remember(refusal);
            return refusal;
        }

        if (command.Trim().Equals("save", StringComparison.OrdinalIgnoreCase))
        {
            _ = ShowWorldSave();
            return "Saving the world.";
        }

        if (!OwnsServerProcess())
        {
            if (!ServerIsRunning())
            {
                var message = CheatBlockReason();
                Remember(message);
                return message;
            }

            _ = SendThroughQueue(command);
            return "Sent.";
        }

        try
        {
            ArmCommandReply(command);
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
            Remember("> " + command);
            return "Sent.";
        }
        catch (Exception ex)
        {
            Remember(ex.Message);
            return ex.Message;
        }
    }
}
