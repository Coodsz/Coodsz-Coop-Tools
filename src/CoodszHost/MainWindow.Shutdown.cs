using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// Stop and Restart send the server its own "stop" (it writes its save on stop and
// exits), wait up to 2 minutes and only then end the engine's process tree. Restart
// first warns the players who are on with "say".
public partial class MainWindow
{
    private const string StopEndedNote =
        "The server did not finish stopping within 2 minutes, so its engine was ended. The last save it wrote is kept, and Load backup has the automatic backups.";

    // StopOutcome.NotTaken: Stop() ends the process tree and deletes no save.
    private const string StopNotTakenNote =
        "The server's console did not take stop, so the server was ended. The last save it wrote is kept.";

    // The note for a stop that had to end the server, or null.
    private static string? StopNote(ServerSession.StopOutcome outcome) => outcome switch
    {
        ServerSession.StopOutcome.Ended => StopEndedNote,
        ServerSession.StopOutcome.NotTaken => StopNotTakenNote,
        _ => null
    };

    private async Task WarnPlayersOfRestart()
    {
        if (ReadLivePlayers().Count == 0)
            return;
        var warned = await SayToPlayers("The server restarts in 30 seconds. The campaign is saved first.");
        if (warned == ConsoleWrite.NotSent)
        {
            // This window's own server has a console; it just did not take the line in time.
            Append(_session.StartedHere
                ? ServerCommandQueue.ConsoleTimeout
                : $"Players were not warned before the restart. A server that was running before {Edition.Name} opened does not take chat messages from it.");
            return;
        }

        // A warning stuck on a full pipe may still reach the players, so the countdown runs but no done line is shown.
        if (warned == ConsoleWrite.MaybeSent)
        {
            Append(ServerCommandQueue.ConsoleSlow);
        }
        else
        {
            SetControl("Players were told the server restarts in 30 seconds.");
            Append("Players were told the server restarts in 30 seconds.");
        }

        await Task.Delay(TimeSpan.FromSeconds(20));
        await SayToPlayers("The server restarts in 10 seconds.");
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    // say goes to the server's own console, which only a server this window started has.
    // The console queue is tried as well and its answer read; today it only runs dotted
    // commands, so say through it is expected to be refused (ServerSession.Say).
    private Task<ConsoleWrite> SayToPlayers(string message) => Task.Run(() => _session.Say(message));
}
