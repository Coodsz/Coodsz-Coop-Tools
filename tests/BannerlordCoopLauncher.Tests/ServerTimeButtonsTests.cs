using BannerlordCoopLauncher.Core.Server;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The Pause and Play buttons next to Stop in the TAOM and Burning Empires server tools (ServerTime): the
// commands they send, when they can be pressed and the line they show. Coodsz Host keeps its Pause game
// and Resume game buttons, which its pause on join takes as the host's own choice.
public class ServerTimeButtonsTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static ConnectedPlayer P(int id, string name, string state) => new(id, name, state, "127.0.0.1:1");

    [Fact]
    public void The_buttons_send_the_time_modes_pause_on_join_uses()
    {
        Assert.Equal("coop.debug.set_time_mode Pause", ServerTime.PauseCommand);
        Assert.Equal("coop.debug.set_time_mode Play_1x", ServerTime.PlayCommand);
        Assert.Equal("coop.debug.set_time_mode Play_2x", ServerTime.FastCommand);
        Assert.Equal(JoinPause.PauseCommand, ServerTime.Command(JoinPause.PauseMode));
        Assert.Equal(ServerTime.PlayCommand, ServerTime.Command(JoinPause.NormalMode));
        Assert.Equal(ServerTime.FastCommand, ServerTime.Command(JoinPause.FastMode));
    }

    [Fact]
    public void The_buttons_only_work_while_the_campaign_is_up_and_nothing_else_runs()
    {
        Assert.True(ServerTime.CanUse(serving: true, busy: false));
        Assert.False(ServerTime.CanUse(serving: false, busy: false));
        Assert.False(ServerTime.CanUse(serving: true, busy: true));
        Assert.False(ServerTime.CanUse(serving: false, busy: true));
    }

    [Fact]
    public void A_press_shows_a_short_line_or_the_reason_it_failed()
    {
        Assert.Equal(("The server is paused.", false), ServerTime.Result(JoinPause.PauseMode, true, "OK|Time control set to Pause"));
        Assert.Equal(("Play held. The game runs until you press Pause.", false), ServerTime.Result(JoinPause.NormalMode, true, ""));
        Assert.Equal(("Play held at 2x speed. The game runs until you press Pause.", false), ServerTime.Result(JoinPause.FastMode, true, null));
        // A server the window did not start runs without the hold.
        Assert.Equal(("Server running at normal speed.", false), ServerTime.Result(JoinPause.NormalMode, true, "", held: false));
        Assert.Equal(("Server running at 2x speed.", false), ServerTime.Result(JoinPause.FastMode, true, "", held: false));
        // The console queue never handed the command over: nothing happened, so it is no done line.
        var notPickedUp = BannerlordCoopLauncher.Core.Support.ConsoleQueue.NotPickedUp;
        Assert.Equal((notPickedUp, true), ServerTime.Result(JoinPause.PauseMode, true, notPickedUp, held: false));
        Assert.Equal(("Unknown command coop.debug.set_time_mode", true),
            ServerTime.Result(JoinPause.PauseMode, true, "\r\nUnknown command coop.debug.set_time_mode\r\n"));
        Assert.True(ServerTime.Result(JoinPause.NormalMode, true, "The server refused that command: no").Failed);
        Assert.Equal(("Server is not running.", true), ServerTime.Result(JoinPause.PauseMode, false, "Server is not running."));
        Assert.True(ServerTime.Result(JoinPause.PauseMode, false, "").Failed);
    }

    [Fact]
    public void A_pause_or_play_press_counts_as_the_hosts_choice_so_a_join_pause_never_overrides_it()
    {
        // Play pressed during a join pause: the pause is no longer the tool's to resume.
        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        Assert.True(pause.Paused("Play_1x", Now));
        pause.HostSent(ServerTime.PlayCommand);
        Assert.False(pause.Holding);
        Assert.Equal(JoinPause.NormalMode, pause.HostChoice);
        Assert.Equal(JoinPauseStep.None, pause.Update([P(1, "Bo", "on map")], Now.AddSeconds(10), true));

        // Pause pressed while the tool's own pause was on its way: no resume over the host's Pause.
        var paused = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, paused.Update([P(1, "Bo", "loading")], Now, true));
        paused.HostSent(ServerTime.PauseCommand);
        Assert.False(paused.Paused("Play_1x", Now));
        Assert.False(paused.Holding);
        Assert.Equal(JoinPause.PauseMode, paused.HostChoice);
        Assert.Equal(JoinPauseStep.None, paused.Update([P(1, "Bo", "on map")], Now.AddSeconds(10), true));

        // 2x from the right click menu is remembered and put back after the next join pause.
        var fast = new JoinPause();
        fast.HostSent(ServerTime.FastCommand);
        Assert.Equal(JoinPause.FastMode, fast.HostChoice);
        Assert.Equal(JoinPauseStep.Pause, fast.Update([P(2, "Cy", "loading")], Now, true));
        Assert.True(fast.Paused(null, Now));
        Assert.Equal(ServerTime.FastCommand, fast.ResumeCommand);
    }

    // Coodsz Host (and AD1259) keep their own Pause game and Resume game buttons in the Game time card:
    // the command is noted as the host's before it goes out, and launcher.time.set counts as a host time
    // command, so pause on join never overrides them.
    [Fact]
    public void Coodsz_host_pause_game_and_resume_game_count_as_the_hosts_choice()
    {
        var players = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.Players.cs")).Replace("\r\n", "\n");
        Assert.Contains("PauseGame_Click(object sender, RoutedEventArgs e) =>\n        await SendServerCommand(ServerSession.PauseCommand);", players);
        Assert.Contains("ResumeGame_Click(object sender, RoutedEventArgs e) =>\n        await SendServerCommand(ServerSession.PlayCommand);", players);
        var start = players.IndexOf("private async Task SendServerCommand(string command)", StringComparison.Ordinal);
        Assert.True(start > 0);
        var note = players.IndexOf("NoteHostCommand(command);", start, StringComparison.Ordinal);
        var send = players.IndexOf("await SendHostCommand(command)", start, StringComparison.Ordinal);
        Assert.True(note > start && note < send);
        // A host time command waits for a join pause step on its way, so an older join resume can't overwrite it.
        var rules = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.ServerRules.cs")).Replace("\r\n", "\n");
        var helper = rules[rules.IndexOf("private async Task<string> SendHostCommand(string command)", StringComparison.Ordinal)..];
        var gate = helper.IndexOf("await JoinGate.EnterHost(command);", StringComparison.Ordinal);
        var sent = helper.IndexOf("_session.Send(command)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && gate < sent);
        Assert.Contains("var reply = await SendHostCommand(command);", File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.Console.cs")));

        var pause = new JoinPause();
        Assert.Equal(JoinPauseStep.Pause, pause.Update([P(1, "Bo", "loading")], Now, true));
        Assert.True(pause.Paused("Play_1x", Now));
        pause.HostSent(CoodszHost.Services.ServerSession.PlayCommand);
        Assert.False(pause.Holding);
        Assert.Equal(JoinPause.NormalMode, pause.HostChoice);
        pause.HostSent(CoodszHost.Services.ServerSession.PauseCommand);
        Assert.Equal(JoinPause.PauseMode, pause.HostChoice);

        // Coodsz Host gets each server line from the process and again from the log tail; Play hold reads only the
        // process output, so one Coop pause is one resend.
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml.cs"));
        Assert.Contains("_session.ProcessLine +=", window);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(window, @"NotePlayHold\("));

        // No second pair next to Stop; the Resume game tip says the game runs until Pause game (Play hold).
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        Assert.DoesNotContain("PauseServerButton", xaml);
        Assert.Contains("<TextBlock Text=\"Auto pause\"", xaml);
        Assert.Contains("Click=\"ResumeGame_Click\" ToolTip=\"Lets campaign time run again on the running server until you press Pause game,", xaml);
    }

    [Theory]
    [InlineData("TaomCoopHost", "MainWindow.Commands.cs")]
    [InlineData("BurningEmpiresHost", "MainWindow.Commands.cs")]
    public void Every_server_tool_has_pause_and_play_next_to_stop(string tool, string code)
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", tool, "MainWindow.xaml"));
        var pause = xaml.IndexOf("x:Name=\"PauseServerButton\"", StringComparison.Ordinal);
        var play = xaml.IndexOf("x:Name=\"PlayServerButton\"", StringComparison.Ordinal);
        Assert.True(pause > 0 && play > pause);
        // In the same row as Stop server (the big button since redesign wave 6), and off until the server serves.
        var stop = xaml.IndexOf("x:Name=\"StopButton\"", StringComparison.Ordinal);
        Assert.True(stop > 0 && pause > stop && pause - stop < 2000);
        Assert.Contains("Click=\"PauseServer_Click\" IsEnabled=\"False\"", xaml);
        Assert.Contains("Click=\"PlayServer_Click\" IsEnabled=\"False\"", xaml);
        Assert.Contains("Click=\"PlayServerFast_Click\"", xaml);
        // These tools have no Auto pause setting, so the tips do not name one.
        Assert.DoesNotContain("<TextBlock Text=\"Auto pause\"", xaml);
        Assert.DoesNotContain("With Auto pause on Settings", xaml);

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", tool, code));
        Assert.Contains("ServerTime.Command(mode)", source);
        Assert.Contains("ServerTime.CanUse(", source);
        Assert.DoesNotContain("\"coop.debug.set_time_mode", source);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
