using BannerlordCoopLauncher.Core.Server;

namespace CoodszHost;

// Play hold (BannerlordCoopLauncher.Core\Server\PlayHold.cs): after Resume game the campaign runs until the
// host presses Pause game, also when Coop pauses it because every player is in a settlement or a battle.
// A join pause is left alone, and the server stopping ends the hold.
public partial class MainWindow
{
    private const string PlayHeldText = "Play held. The game runs until you press Pause game.";
    private const string PlayHoldResumedText = "Coop paused the game because every player is in a settlement or a battle. Play is held, so the game runs again.";
    private const string PlayHoldFailedText = "Play hold could not start the game again. Press Resume game.";
    private const string PlayHoldGaveUpText = "Coop kept pausing the game, so Play hold stopped. Press Resume game to hold it again.";

    private readonly PlayHold _playHold = new();

    // Every live server line (not a replayed log).
    private void NotePlayHold(string line)
    {
        if (_playHold.Line(line, DateTime.UtcNow, _joinPause.Pausing))
            ResumeHeldPlay(_playHold.Command!);
        else if (_playHold.GaveUp)
        {
            Append(PlayHoldGaveUpText);
            GameTimeStatus.Text = PlayHoldGaveUpText;
        }
    }

    private async void ResumeHeldPlay(string command)
    {
        // A Pause the host pressed while this was on its way wins: the resend goes out only for the same hold.
        var generation = _playHold.Generation;
        try
        {
            var sent = await Task.Run(() => _playHold.Generation == generation ? _session.SendDetailed(command, TimeSpan.FromSeconds(5)) : null);
            if (sent == null)
                return;
            Append(sent.Succeeded ? PlayHoldResumedText : PlayHoldFailedText);
            if (!sent.Succeeded)
                GameTimeStatus.Text = PlayHoldFailedText;
        }
        catch (Exception ex)
        {
            Append(PlayHoldFailedText + " " + ex.Message);
            GameTimeStatus.Text = PlayHoldFailedText;
        }
    }
}
