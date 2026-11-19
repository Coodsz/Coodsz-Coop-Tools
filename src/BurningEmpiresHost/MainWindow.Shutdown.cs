using System.IO;
using BannerlordCoopLauncher.Core.Server;

namespace BurningEmpiresHost;

// Stop and Restart send the server its own "stop" over the console pipe (it writes its
// save on stop and exits) and wait up to 2 minutes before StopServer ends what is left.
// Restart first warns the players who are on with "say". Only a server this window
// started has that pipe; any other one is ended as before.
public partial class MainWindow
{
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(120);

    private bool WriteToServerConsole(string line)
    {
        if (!ServerCommand.IsAllowed(line))
            return false;
        var process = _process;
        if (process is not { HasExited: false })
            return false;
        try
        {
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    // Runs off the window thread.
    private void AskServerToStop()
    {
        _stopAsked = true;
        var process = _process;
        if (!WriteToServerConsole(ServerCommand.Stop()) || process == null)
            return;
        NoteLog("Sent stop. Waiting up to 2 minutes for the server's save on stop.");
        try
        {
            if (!process.WaitForExit((int)StopWait.TotalMilliseconds))
                NoteLog("The server did not finish stopping within 2 minutes, so it was ended. The last save it wrote is kept.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task WarnPlayersOfRestart()
    {
        if (_onlinePlayers.Count == 0)
            return;
        if (!WriteToServerConsole(ServerCommand.Say("The server restarts in 30 seconds. The campaign is saved first.")))
        {
            NoteLog("Players were not warned before the restart. A server that was running before this tool opened does not take chat messages from it.");
            return;
        }

        SetControl("Players were told the server restarts in 30 seconds.");
        await Task.Delay(TimeSpan.FromSeconds(20));
        WriteToServerConsole(ServerCommand.Say("The server restarts in 10 seconds."));
        await Task.Delay(TimeSpan.FromSeconds(10));
    }
}
