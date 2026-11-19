using BannerlordCoopLauncher.Core.Server;

namespace BurningEmpiresHost;

// Play hold (BannerlordCoopLauncher.Core\Server\PlayHold.cs): after Play the campaign runs until the host
// presses Pause, also when Coop pauses it because every player is in a settlement or a battle. The server
// stopping or starting again ends the hold.
public partial class MainWindow
{
    private const string PlayHoldResumedText = "Coop paused the game because every player is in a settlement or a battle. Play is held, so the game runs again.";
    private const string PlayHoldFailedText = "Play hold could not start the game again. Press Play.";
    private const string PlayHoldGaveUpText = "Coop kept pausing the game, so Play hold stopped. Press Play to hold it again.";

    private readonly PlayHold _playHold = new();

    // A [Coop] line of this window's own server, on the window thread.
    private void NotePlayHold(string line)
    {
        if (_playHold.Line(line, DateTime.UtcNow, joinPausing: false))
        {
            var sent = SendCommand(_playHold.Command!) == "Sent.";
            NoteLog(sent ? PlayHoldResumedText : PlayHoldFailedText);
            if (!sent)
                SetControl(PlayHoldFailedText, failed: true);
        }
        else if (_playHold.GaveUp)
        {
            NoteLog(PlayHoldGaveUpText);
            SetControl(PlayHoldGaveUpText, failed: true);
        }
    }
}
