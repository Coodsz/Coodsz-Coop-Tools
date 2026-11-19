using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Tests;

// Play hold: after the host's Play the campaign keeps running until the host pauses, also when Coop pauses it
// because every player is in a settlement or a battle. The lines are real Coop console lines (coop-server logs).
public class PlayHoldTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);

    private const string PlayToPause = "21:17:54.114  [Coop] Applying server time control: current=\"Play_1x\" requested=Pause effective=Pause liveTestFastForwardBypass=False";
    private const string FastToPause = "21:17:54.114  [Coop] Applying server time control: current=Play_2x requested=\"Pause\" effective=\"Pause\" liveTestFastForwardBypass=false";
    private const string PauseToPause = "21:18:03.116  [Coop] Applying server time control: current=\"Pause\" requested=Pause effective=Pause liveTestFastForwardBypass=False";
    private const string Occupied = "21:17:54.114  [Coop] Pausing campaign because every connected player is occupied: triggerParty=\"Player439\" players=[\"[steam id]:party=MobileParty_Player439,settlement=town_V1,mapEvent=none\"]";
    // The real line from the v1.3.3 live test on the Workshop Coop server (5f4d63cc0): Serilog puts the state in quotes.
    private const string Acquired = "19:44:05.836  [Coop] Occupancy pause ownership \"acquired\": trigger=\"Player1254\" mode=Pause players=[\"[steam id]:party=MobileParty_Player1254,settlement=none,mapEvent=siege\"]";
    private const string AcquiredUnquoted = "18:22:17.000  [Coop] Occupancy pause ownership acquired: trigger=Player777 mode=\"Pause\" players=[\"[steam id]:party=MobileParty_Player777,settlement=none,mapEvent=siege\"]";
    private const string Empty = "18:28:19.319  [Coop] Pausing campaign because no players are connected";
    private const string PlayerPause = "20:01:00.000  [Coop] Peer requested time control: peer=1 mode=Pause";

    private static PlayHold Held(string command = "coop.debug.set_time_mode Play_1x")
    {
        var hold = new PlayHold();
        hold.HostSent(command);
        return hold;
    }

    [Fact]
    public void Coops_auto_pause_puts_the_held_speed_back_in_either_line_order()
    {
        // Newer Coop builds write the time change first, older ones the reason first.
        var newer = Held();
        Assert.False(newer.Line(PlayToPause, Now, joinPausing: false));
        Assert.True(newer.Line(Acquired, Now.AddMilliseconds(5), joinPausing: false));
        Assert.Equal("coop.debug.set_time_mode Play_1x", newer.Command);

        var older = Held("coop.debug.set_time_mode Play_2x");
        Assert.False(older.Line(Occupied, Now, joinPausing: false));
        Assert.True(older.Line(FastToPause, Now, joinPausing: false));
        Assert.Equal(JoinPause.FastMode, older.Mode);

        // One auto pause is put back once.
        Assert.False(older.Line("21:17:54.200  [Coop] Packet profile over 10 seconds", Now, joinPausing: false));
        Assert.True(older.Held);

        // The live test's own pair (quoted state) and the same line without quotes both count.
        foreach (var line in new[] { Acquired, AcquiredUnquoted })
        {
            var hold = Held();
            hold.Line("19:44:05.835  [Coop] Applying server time control: current=\"Play_1x\" requested=Pause effective=Pause liveTestFastForwardBypass=False", Now, joinPausing: false);
            Assert.True(hold.Line(line, Now.AddMilliseconds(1), joinPausing: false), line);
        }
    }

    [Fact]
    public void Coodsz_Hosts_Resume_game_is_held_and_its_Pause_game_ends_the_hold()
    {
        var hold = Held("launcher.time.set Play");
        Assert.Equal(JoinPause.NormalMode, hold.Mode);
        hold.Line(PlayToPause, Now, joinPausing: false);
        Assert.True(hold.Line(Acquired, Now, joinPausing: false));
        Assert.Equal("launcher.time.set Play", hold.Command);

        hold.HostSent("launcher.time.set Pause");
        Assert.False(hold.Held);
        hold.Line(PlayToPause, Now, joinPausing: false);
        Assert.False(hold.Line(Acquired, Now, joinPausing: false));

        // Another console command changes nothing.
        var other = Held();
        other.HostSent("kick 2");
        Assert.True(other.Held);
    }

    [Fact]
    public void Nothing_is_held_before_Play_or_after_the_server_stops()
    {
        var none = new PlayHold();
        none.Line(PlayToPause, Now, joinPausing: false);
        Assert.False(none.Line(Acquired, Now, joinPausing: false));

        var stopped = Held();
        stopped.Stop();
        stopped.Line(PlayToPause, Now, joinPausing: false);
        Assert.False(stopped.Line(Acquired, Now, joinPausing: false));
    }

    [Fact]
    public void Pauses_that_are_not_Coops_auto_pause_are_left_alone()
    {
        // A player pressed pause in the game, then everyone was in a settlement.
        var player = Held();
        player.Line(PlayerPause, Now, joinPausing: false);
        player.Line(PlayToPause, Now, joinPausing: false);
        Assert.False(player.Line(Acquired, Now.AddMilliseconds(10), joinPausing: false));

        // The game was already paused: Coop writes the reason but changes nothing.
        var paused = Held();
        Assert.False(paused.Line(Acquired, Now, joinPausing: false));
        paused.Line(Occupied, Now, joinPausing: false);
        Assert.False(paused.Line(PauseToPause, Now, joinPausing: false));

        // The last player left: Coop pauses an empty server.
        var empty = Held();
        empty.Line(Empty, Now, joinPausing: false);
        Assert.False(empty.Line(PlayToPause, Now, joinPausing: false));

        // A pause on join is the tool's own.
        var joining = Held();
        joining.Line(PlayToPause, Now, joinPausing: false);
        Assert.False(joining.Line(Acquired, Now, joinPausing: true));
        Assert.True(joining.Held);

        // A time change long before the reason is another pause.
        var apart = Held();
        apart.Line(PlayToPause, Now, joinPausing: false);
        Assert.False(apart.Line(Acquired, Now + PlayHold.PairWindow + TimeSpan.FromSeconds(1), joinPausing: false));
    }

    [Fact]
    public void A_refused_press_puts_the_hold_back_and_a_new_run_starts_the_count_over()
    {
        // The hold changes before the send; the server refused the Pause, so Play stays held.
        var hold = Held();
        var before = hold.Command;
        hold.HostSent("coop.debug.set_time_mode Pause");
        var press = hold.Generation;
        Assert.False(hold.Held);
        hold.Restore(before, press);
        Assert.Equal("coop.debug.set_time_mode Play_1x", hold.Command);
        hold.Line(PlayToPause, Now, joinPausing: false);
        Assert.True(hold.Line(Acquired, Now, joinPausing: false));

        // A refused first Play holds nothing.
        var first = new PlayHold();
        first.HostSent("coop.debug.set_time_mode Play_1x");
        first.Restore(null, first.Generation);
        Assert.False(first.Held);

        // Play held, 2x pressed, then Pause pressed before the 2x answer: the refused 2x can't put Play back over the Pause.
        var newer = Held();
        var beforeFast = newer.Command;
        newer.HostSent("coop.debug.set_time_mode Play_2x");
        var fast = newer.Generation;
        newer.HostSent("coop.debug.set_time_mode Pause");
        newer.Restore(beforeFast, fast);
        Assert.False(newer.Held);

        // A stop is newer than any press too.
        var stopped = Held();
        var stopPress = stopped.Generation;
        stopped.Stop();
        stopped.Restore("coop.debug.set_time_mode Play_1x", stopPress);
        Assert.False(stopped.Held);

        // Resends of the last run don't count toward the next run's limit.
        var runs = Held();
        for (var i = 0; i < PlayHold.MostResends; i++)
        {
            runs.Line(PlayToPause, Now, joinPausing: false);
            Assert.True(runs.Line(Acquired, Now, joinPausing: false));
        }

        runs.Stop();
        runs.HostSent("coop.debug.set_time_mode Play_1x");
        runs.Line(PlayToPause, Now.AddSeconds(5), joinPausing: false);
        Assert.True(runs.Line(Acquired, Now.AddSeconds(5), joinPausing: false));
    }

    [Fact]
    public void A_server_that_keeps_pausing_lets_the_hold_go()
    {
        var hold = Held();
        var at = Now;
        for (var i = 0; i < PlayHold.MostResends; i++)
        {
            hold.Line(PlayToPause, at, joinPausing: false);
            Assert.True(hold.Line(Acquired, at, joinPausing: false));
            Assert.False(hold.GaveUp);
            at = at.AddSeconds(5);
        }

        hold.Line(PlayToPause, at, joinPausing: false);
        Assert.False(hold.Line(Acquired, at, joinPausing: false));
        Assert.True(hold.GaveUp);
        Assert.False(hold.Held);

        // Spread over more than a minute it keeps holding.
        var slow = Held();
        for (var i = 0; i < PlayHold.MostResends * 2; i++)
        {
            var when = Now + TimeSpan.FromSeconds(20 * i);
            slow.Line(PlayToPause, when, joinPausing: false);
            Assert.True(slow.Line(Acquired, when, joinPausing: false));
        }
    }
}
