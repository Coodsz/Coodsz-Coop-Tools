// Tests ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// tests/CoopKeep.Core.Tests/Protocol/DsEventParserTests.cs, Server/CrashRestartPolicyTests.cs and
// Server/ServerCommandTests.cs, translated from Turkish and changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Tests;

// The event lines are real dedicated server output (CoopKeep's sessions and this PC's
// coop-server logs); the protocol comes from a closed binary, so real output is the
// only reference.
public class DsEventParserTests
{
    [Theory]
    [InlineData("boot", ServerPhase.Boot)]
    [InlineData("loading", ServerPhase.Loading)]
    [InlineData("serving", ServerPhase.Serving)]
    [InlineData("stopping", ServerPhase.Stopping)]
    [InlineData("shutdown", ServerPhase.Shutdown)]
    [InlineData("starting", ServerPhase.Starting)]
    public void State_event_phase_is_read(string raw, ServerPhase expected)
    {
        var line = "@DS@{\"ev\":\"state\",\"phase\":\"" + raw + "\",\"save\":\"faz0test\",\"pw\":false}";

        Assert.True(DsEventParser.TryParse(line, out var evt));
        var state = Assert.IsType<DsEvent.State>(evt);
        Assert.Equal(expected, state.Phase);
        Assert.Equal(raw, state.RawPhase);
        Assert.Equal("faz0test", state.SaveName);
        Assert.False(state.PasswordRequired);
    }

    [Fact]
    public void Password_server_reports_pw_true()
    {
        Assert.True(DsEventParser.TryParse("""@DS@{"ev":"state","phase":"serving","save":"saveauto1","pw":true}""", out var evt));
        var state = Assert.IsType<DsEvent.State>(evt);
        Assert.True(state.PasswordRequired);
        Assert.Equal("saveauto1", state.SaveName);
    }

    [Fact]
    public void Unknown_phase_gives_Unknown_and_keeps_the_raw_name()
    {
        Assert.True(DsEventParser.TryParse("""@DS@{"ev":"state","phase":"hibernating","save":"x","pw":false}""", out var evt));
        var state = Assert.IsType<DsEvent.State>(evt);
        Assert.Equal(ServerPhase.Unknown, state.Phase);
        Assert.Equal("hibernating", state.RawPhase);
    }

    [Fact]
    public void Empty_player_list_is_read()
    {
        Assert.True(DsEventParser.TryParse("""@DS@{"ev":"players","list":[]}""", out var evt));
        Assert.Empty(Assert.IsType<DsEvent.Players>(evt).List);
    }

    [Fact]
    public void Two_players_from_a_real_session_are_read()
    {
        var line = """@DS@{"ev":"players","list":[{"id":0,"name":"Aderimo","state":"on map","addr":"127.0.0.1:52010"},{"id":1,"name":"CengizHan","state":"loading","addr":"127.0.0.1:56016"}]}""";

        Assert.True(DsEventParser.TryParse(line, out var evt));
        var players = Assert.IsType<DsEvent.Players>(evt);
        Assert.Equal(2, players.List.Count);
        Assert.Equal(0, players.List[0].Id);
        Assert.Equal("Aderimo", players.List[0].Name);
        Assert.True(players.List[0].IsOnMap);
        Assert.Equal("CengizHan", players.List[1].Name);
        Assert.Equal("loading", players.List[1].State);
        Assert.False(players.List[1].IsOnMap);
    }

    [Fact]
    public void Missing_fields_fall_back_to_safe_defaults()
    {
        Assert.True(DsEventParser.TryParse("""@DS@{"ev":"players","list":[{"id":3}]}""", out var evt));
        var player = Assert.Single(Assert.IsType<DsEvent.Players>(evt).List);
        Assert.Equal(3, player.Id);
        Assert.Equal(ConnectedPlayer.JoiningPlaceholder, player.Name);
        Assert.True(player.IsJoining);
        Assert.Equal("unknown", player.State);
        Assert.Null(player.Address);
    }

    [Fact]
    public void Builtin_command_set_is_read()
    {
        var line = """@DS@{"ev":"commands","builtin":["status","players","save","say","kick","stop","help"],"game":["ai.formation_speed_adjustment_enabled","coop.delete_player"]}""";

        Assert.True(DsEventParser.TryParse(line, out var evt));
        var commands = Assert.IsType<DsEvent.Commands>(evt);
        Assert.Equal(7, commands.Builtin.Count);
        Assert.Contains("kick", commands.Builtin);
        Assert.Contains("stop", commands.Builtin);
        Assert.Equal(2, commands.Game.Count);
    }

    [Fact]
    public void Time_stamped_log_line_is_read()
    {
        Assert.True(DsEventParser.TryParse("""21:24:25.353  @DS@{"ev":"state","phase":"serving","save":"faz0test","pw":false}""", out var evt));
        Assert.Equal(ServerPhase.Serving, Assert.IsType<DsEvent.State>(evt).Phase);
    }

    [Fact]
    public void Event_glued_after_another_engine_line_is_read_and_trailing_text_is_ignored()
    {
        Assert.True(DsEventParser.TryParse("""14:49:18.816  [Engine] save block started@DS@{"ev":"state","phase":"stopping","save":"rescuetest","pw":true} trailing""", out var evt));
        var state = Assert.IsType<DsEvent.State>(evt);
        Assert.Equal(ServerPhase.Stopping, state.Phase);
        Assert.Equal("rescuetest", state.SaveName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[DedicatedServer] SERVING - coop server up, waiting for clients")]
    [InlineData("21:24:49.097  [DedicatedServer] pulse: time=Summer 1, 1084 players=0")]
    [InlineData("ERROR: 0Harmony.dll: Could not load file or assembly")]
    public void Lines_that_are_not_events_are_refused_quietly(string? line)
    {
        Assert.False(DsEventParser.TryParse(line, out var evt));
        Assert.Null(evt);
    }

    [Theory]
    [InlineData("@DS@")]
    [InlineData("@DS@{")]
    [InlineData("""@DS@{"ev":"players","list":[{"id":0,""")]
    [InlineData("@DS@not-json")]
    [InlineData("""@DS@{"phase":"serving"}""")]
    [InlineData("""@DS@["ev","state"]""")]
    public void Broken_or_half_json_never_throws(string line)
    {
        Assert.Null(Record.Exception(() => DsEventParser.TryParse(line, out _)));
        Assert.False(DsEventParser.TryParse(line, out _));
    }

    [Fact]
    public void Unknown_event_type_is_kept_as_Unknown()
    {
        Assert.True(DsEventParser.TryParse("""@DS@{"ev":"metrics","cpu":42}""", out var evt));
        var unknown = Assert.IsType<DsEvent.Unknown>(evt);
        Assert.Equal("metrics", unknown.EventName);
        Assert.Contains("cpu", unknown.RawJson);
    }

    [Fact]
    public void Last_state_and_players_come_from_the_newest_lines_of_a_log()
    {
        var log = string.Join("\r\n",
            """17:04:02.001  @DS@{"ev":"state","phase":"boot","save":"a","pw":true}""",
            """17:04:30.001  @DS@{"ev":"players","list":[{"id":0,"name":"Old","state":"on map","addr":"127.0.0.1:1"}]}""",
            """17:04:50.001  @DS@{"ev":"state","phase":"serving","save":"a","pw":true}""",
            """17:05:00.001  @DS@{"ev":"players","list":[{"id":0,"name":"New","state":"on map","addr":"127.0.0.1:1"}]}""",
            "17:05:01.001  [DedicatedServer] pulse: players=1");

        Assert.Equal(ServerPhase.Serving, DsEventParser.LastState(log)!.Phase);
        Assert.Equal("New", Assert.Single(DsEventParser.LastPlayers(log)!.List).Name);
        Assert.Null(DsEventParser.LastState("no events here"));
        Assert.Null(DsEventParser.LastPlayers(null));
    }
}

public class CrashRestartPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static ServerExitInfo Crash(int code = 3) => ServerExitInfo.From(code, wasExpected: false);

    [Fact]
    public void A_stop_this_tool_asked_for_is_not_a_crash()
    {
        var decision = new CrashRestartPolicy().Evaluate(ServerExitInfo.From(0, wasExpected: true), T0);
        Assert.False(decision.ShouldRestart);
        Assert.Equal(CrashDecisionReason.NotACrash, decision.Reason);
    }

    [Fact]
    public void A_real_crash_is_restarted_after_ten_seconds()
    {
        var decision = new CrashRestartPolicy().Evaluate(Crash(), T0);
        Assert.True(decision.ShouldRestart);
        Assert.Equal(CrashDecisionReason.Restarting, decision.Reason);
        Assert.Equal(TimeSpan.FromSeconds(10), decision.Delay);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void Exits_a_restart_cannot_fix_are_never_restarted(int code)
    {
        var decision = new CrashRestartPolicy().Evaluate(Crash(code), T0);
        Assert.False(decision.ShouldRestart);
        Assert.Equal(CrashDecisionReason.NotRecoverable, decision.Reason);
    }

    [Fact]
    public void Wait_doubles_on_each_crash()
    {
        var policy = new CrashRestartPolicy(maxRestartsPerWindow: 10);
        Assert.Equal(TimeSpan.FromSeconds(10), policy.Evaluate(Crash(), T0).Delay);
        Assert.Equal(TimeSpan.FromSeconds(20), policy.Evaluate(Crash(), T0.AddSeconds(30)).Delay);
        Assert.Equal(TimeSpan.FromSeconds(40), policy.Evaluate(Crash(), T0.AddSeconds(60)).Delay);
    }

    [Fact]
    public void Wait_stops_at_five_minutes()
    {
        var policy = new CrashRestartPolicy(window: TimeSpan.FromHours(10), maxRestartsPerWindow: 100);
        var last = TimeSpan.Zero;
        for (var i = 0; i < 10; i++)
            last = policy.Evaluate(Crash(), T0.AddMinutes(i)).Delay;
        Assert.Equal(TimeSpan.FromMinutes(5), last);
    }

    [Fact]
    public void More_than_three_crashes_in_ten_minutes_stop_the_restarts()
    {
        var policy = new CrashRestartPolicy();
        Assert.True(policy.Evaluate(Crash(), T0).ShouldRestart);
        Assert.True(policy.Evaluate(Crash(), T0.AddSeconds(10)).ShouldRestart);
        Assert.True(policy.Evaluate(Crash(), T0.AddSeconds(20)).ShouldRestart);

        var fourth = policy.Evaluate(Crash(), T0.AddMinutes(9));
        Assert.False(fourth.ShouldRestart);
        Assert.Equal(CrashDecisionReason.CrashLoop, fourth.Reason);
    }

    [Fact]
    public void Crashes_outside_the_window_are_forgotten()
    {
        var policy = new CrashRestartPolicy(maxRestartsPerWindow: 2);
        policy.Evaluate(Crash(), T0);
        policy.Evaluate(Crash(), T0.AddSeconds(5));

        var later = policy.Evaluate(Crash(), T0.AddHours(1));
        Assert.True(later.ShouldRestart);
        Assert.Equal(TimeSpan.FromSeconds(10), later.Delay);
    }

    [Fact]
    public void A_clean_exit_clears_the_count()
    {
        var policy = new CrashRestartPolicy(maxRestartsPerWindow: 2);
        policy.Evaluate(Crash(), T0);
        policy.Evaluate(Crash(), T0.AddSeconds(5));
        policy.Evaluate(ServerExitInfo.From(0, wasExpected: true), T0.AddSeconds(10));

        Assert.True(policy.Evaluate(Crash(), T0.AddSeconds(20)).ShouldRestart);
    }

    [Fact]
    public void Turned_off_never_restarts()
    {
        var decision = new CrashRestartPolicy { Enabled = false }.Evaluate(Crash(), T0);
        Assert.False(decision.ShouldRestart);
        Assert.Equal(CrashDecisionReason.Disabled, decision.Reason);
    }

    [Fact]
    public void Reset_clears_the_count()
    {
        var policy = new CrashRestartPolicy(maxRestartsPerWindow: 1);
        policy.Evaluate(Crash(), T0);
        Assert.False(policy.Evaluate(Crash(), T0.AddSeconds(5)).ShouldRestart);
        policy.Reset();
        Assert.True(policy.Evaluate(Crash(), T0.AddSeconds(10)).ShouldRestart);
    }
}

public class ServerCommandTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("players")]
    [InlineData("save")]
    [InlineData("stop")]
    [InlineData("help")]
    [InlineData("say hello")]
    [InlineData("kick 3")]
    public void Allowed_commands_are_accepted(string command) => Assert.True(ServerCommand.IsAllowed(command));

    [Theory]
    [InlineData("campaign.add_gold_to_hero 100000")]
    [InlineData("campaign.kill_hero Aderimo")]
    [InlineData("coop.delete_player")]
    [InlineData("launcher.server.save")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Game_console_commands_and_empty_input_are_refused(string? command) => Assert.False(ServerCommand.IsAllowed(command));

    [Theory]
    [InlineData("say hello\nkick 0")]
    [InlineData("say hello\r\nstop")]
    [InlineData("status\ncampaign.add_gold_to_hero 99999")]
    public void Commands_with_a_line_break_are_refused(string command) => Assert.False(ServerCommand.IsAllowed(command));

    // Coop #54: save typed on the Console page went to the command queue, which only runs game console commands.
    [Theory]
    [InlineData("save", "save")]
    [InlineData("  Save ", "save")]
    [InlineData("STATUS", "status")]
    [InlineData("players", "players")]
    [InlineData("say  hello   all", "say hello all")]
    [InlineData("kick 3", "kick 3")]
    [InlineData("kick", "kick")]
    [InlineData("say", "say")]
    [InlineData("say   ", "say")]
    [InlineData("help", "help")]
    [InlineData("stop", "stop")]
    // Extra words after save are dropped; save takes none.
    [InlineData("save extra", "save")]
    public void Typed_builtins_go_to_the_server_console(string typed, string line)
    {
        Assert.Equal(line, ServerCommand.TypedBuiltin(typed));
        Assert.True(ServerCommand.IsAllowed(line));
    }

    [Theory]
    [InlineData("campaign.add_gold_to_hero 99999")]
    [InlineData("coop.debug.players")]
    [InlineData("saved")]
    [InlineData("save\ncampaign.add_gold_to_hero 1")]
    [InlineData("save\tx")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_typed_lines_stay_on_the_command_queue(string? typed) => Assert.Null(ServerCommand.TypedBuiltin(typed));

    [Fact]
    public void The_console_page_sends_typed_builtins_like_the_buttons()
    {
        var send = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs")), "public string Send(string command)");
        Assert.Contains("ServerCommand.TypedBuiltin(text)", send);
        Assert.Contains("return RequestSave();", send);
        Assert.Contains("TryConsoleLine(builtin)", send);
        // A typed stop points to the Stop button, which saves first; a bare kick asks for the player.
        Assert.Contains("return StopUseButton;", send);
        Assert.Contains("return KickNeedsName;", send);
        Assert.Contains("return SayNeedsText;", send);
        Assert.True(send.IndexOf("StopUseButton", StringComparison.Ordinal) < send.IndexOf("TryConsoleLine(builtin)", StringComparison.Ordinal));
        // Before the queue, so a builtin never reaches it.
        Assert.True(send.IndexOf("TypedBuiltin", StringComparison.Ordinal) < send.IndexOf("ConsoleQueue.Send", StringComparison.Ordinal));
    }

    [Fact]
    public void Say_becomes_one_line()
    {
        var command = ServerCommand.Say("Server restarts\nin 30\r\nseconds");
        Assert.Equal("say Server restarts in 30 seconds", command);
        Assert.True(ServerCommand.IsAllowed(command));
    }

    [Fact]
    public void Long_say_is_cut()
    {
        var command = ServerCommand.Say(new string('a', 500));
        Assert.True(command.Length <= ServerCommand.MaxBroadcastLength + "say ".Length);
        Assert.True(ServerCommand.IsAllowed(command));
    }

    [Fact]
    public void Kick_takes_an_id_or_a_name_and_injection_stays_one_line()
    {
        Assert.Equal("kick 3", ServerCommand.Kick("3"));
        Assert.Equal("kick Aderimo", ServerCommand.Kick("Aderimo"));
        var injected = ServerCommand.Kick("Aderimo\ncampaign.add_gold_to_hero 99999")!;
        Assert.DoesNotContain('\n', injected);
        Assert.True(ServerCommand.IsAllowed(injected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r")]
    public void Empty_kick_target_gives_null(string target) => Assert.Null(ServerCommand.Kick(target));

    [Fact]
    public void Control_characters_become_spaces_and_runs_of_spaces_shrink()
    {
        Assert.Equal("ki ck", ServerCommand.SanitizeArgument("ki\nck", 64));
        Assert.Equal("a b c", ServerCommand.SanitizeArgument("  a    b\t\tc  ", 64));
    }
}

public class ServerExitTests
{
    [Theory]
    [InlineData(0, ServerExitKind.Clean)]
    [InlineData(2, ServerExitKind.LoadFailure)]
    [InlineData(3, ServerExitKind.FatalWhileServing)]
    [InlineData(4, ServerExitKind.ModuleVerificationFailed)]
    [InlineData(6, ServerExitKind.EulaRequired)]
    [InlineData(-1073741819, ServerExitKind.Unknown)]
    [InlineData(137, ServerExitKind.Unknown)]
    public void Exit_codes_are_classified(int code, ServerExitKind expected) => Assert.Equal(expected, ServerExit.Classify(code));

    [Fact]
    public void Asked_for_stop_is_never_a_crash()
    {
        Assert.False(ServerExitInfo.From(0, wasExpected: true).IsCrash);
        Assert.True(ServerExitInfo.From(3, wasExpected: false).IsCrash);
        Assert.False(ServerExitInfo.From(3, wasExpected: true).IsCrash);
        Assert.False(ServerExitInfo.From(0, wasExpected: false).IsCrash);
    }
}

public class KnownLogLinesTests
{
    [Theory]
    [InlineData("21:43:37.819  [DedicatedServer] autosave: world saved to 'en bastan.sav' (1142 ms)", "en bastan")]
    [InlineData("17:05:19.671  [DedicatedServer] shutdown save: world saved to 'saveauto1.sav' (1022 ms)", "saveauto1")]
    [InlineData("[DedicatedServer] world saved to 'camp' (3 ms)", "camp")]
    public void Saved_world_lines_give_the_save_name(string line, string name)
    {
        Assert.Equal(name, KnownLogLines.SavedWorldName(line));
        Assert.Equal("saved", KnownLogLines.Match(line)!.Key);
    }

    [Theory]
    [InlineData("[DedicatedServer] operator save skipped: server not serving yet (phase=a)")]
    [InlineData("world saved to '..\\..\\evil.sav'")]
    [InlineData(null)]
    public void Other_lines_give_no_save_name(string? line) => Assert.Null(KnownLogLines.SavedWorldName(line));

    [Fact]
    public void Problems_are_told_apart_from_noise()
    {
        Assert.True(KnownLogLines.IsProblem("EULA acceptance required; server will not start (exit 6)."));
        Assert.False(KnownLogLines.IsProblem("Cannot load: ..\\Modules\\X\\bin\\X.dll"));
        Assert.True(KnownLogLines.IsProblem("System.NullReferenceException: boom"));
    }
}

public class CoopKeepHostWiringTests
{
    [Theory]
    [InlineData("""17:04:02.001  @DS@{"ev":"state","phase":"loading","save":"a","pw":true}""", ServerPhase.Loading)]
    [InlineData("""@DS@{"ev":"state","phase":"stopping","save":"a","pw":true}""", ServerPhase.Stopping)]
    [InlineData("[DedicatedServer] SERVING - coop server up, waiting for clients", ServerPhase.Serving)]
    [InlineData("[BannerlordCoopServer] engine exited with code 3", ServerPhase.Shutdown)]
    public void Session_reads_the_phase_from_events_with_the_log_line_as_fallback(string line, ServerPhase phase) =>
        Assert.Equal(phase, CoodszHost.Services.ServerSession.PhaseFromLine(line));

    [Theory]
    [InlineData("[DedicatedServer] operator save skipped: server not serving yet (phase=a)")]
    [InlineData("""@DS@{"ev":"state","phase":"hibernating"}""")]
    [InlineData("""@DS@{"ev":"players","list":[]}""")]
    public void Lines_that_only_mention_serving_do_not_change_the_phase(string line) =>
        Assert.Null(CoodszHost.Services.ServerSession.PhaseFromLine(line));

    [Fact]
    public void Crash_restart_is_off_until_the_host_turns_it_on() =>
        Assert.False(new CoodszHost.Services.HostProfile().RestartOnCrash);

    [Fact]
    public void Exit_code_2_points_at_a_backup_and_the_process_exit_line_carries_the_code()
    {
        Assert.Equal(2, CoodszHost.Services.ServerCrashExplain.ReadExitCode("Server process exited with code 2."));
        var lines = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code 2.", false, "loading the campaign", null, [], []);
        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("exit code 2"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Load backup"));
    }

    // The live Coodsz Host test of 2026-10-18: the start banner printed the ready marker on
    // its own indented line at 01:08:51, and the server really served at 01:10:04.
    [Theory]
    [InlineData("01:08:51.913    [DedicatedServer] SERVING")]
    [InlineData("  [DedicatedServer] SERVING")]
    [InlineData("01:08:51.912  Starting the native engine. The server is ready when the log shows:")]
    [InlineData("23:05:11.310  server is READY when you see:  [DedicatedServer] SERVING")]
    public void The_start_banner_does_not_count_as_serving(string line)
    {
        Assert.Null(CoodszHost.Services.ServerSession.PhaseFromLine(line));
        Assert.False(BannerlordCoopLauncher.Core.Support.ServingLine.IsServing(line));
    }

    [Theory]
    [InlineData("01:10:04.544  [DedicatedServer] SERVING — coop server up, waiting for clients")]
    [InlineData("[DedicatedServer] SERVING - coop server up, waiting for clients")]
    [InlineData("[01:10:04.544] [DedicatedServer] SERVING")]
    public void The_real_serving_line_still_counts(string line)
    {
        Assert.Equal(ServerPhase.Serving, CoodszHost.Services.ServerSession.PhaseFromLine(line));
        Assert.True(BannerlordCoopLauncher.Core.Support.ServingLine.IsServing(line));
    }

    [Fact]
    public void The_serving_state_event_is_read_from_the_live_log_line() =>
        Assert.Equal(ServerPhase.Serving, CoodszHost.Services.ServerSession.PhaseFromLine(
            """01:10:04.547  @DS@{"ev":"state","phase":"serving","save":"cktest","pw":true}"""));

    [Fact]
    public void A_crash_after_serving_is_not_told_as_a_stop_before_ready()
    {
        // Killed while serving (exit code -1), a setup of 26 working list mods.
        var lines = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code -1.", true, "", null, [], [], 26);
        var happened = Assert.Single(lines, line => line.StartsWith("What happened:", StringComparison.Ordinal));
        Assert.DoesNotContain("before it was ready", happened);
        Assert.DoesNotContain("no extra mods", happened);
        Assert.Contains("was serving players", happened);
        Assert.Contains("exit code -1", happened);
        Assert.Contains("All 26 mods in this setup are on the working list", happened);

        var early = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code -1.", false, "", null, [], [], 26);
        Assert.Contains(early, line => line.StartsWith("What happened: The server stopped before it was ready for players. All 26 mods", StringComparison.Ordinal));

        var extras = new[] { new CoodszHost.Services.ServerCrashExplain.ModName("Extra", "Extra"), new CoodszHost.Services.ServerCrashExplain.ModName("Other", "Other") };
        var withExtras = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code 5.", true, "", null, extras, [], 28);
        Assert.Contains(withExtras, line => line.StartsWith("What happened: The server was serving players, then it stopped.", StringComparison.Ordinal) && line.Contains("one of the 2 extra mods"));
    }

    // Coop #55: the start script's own write error was blamed on No Companion Limit, the mod named on the line before.
    [Fact]
    public void A_start_script_write_error_is_not_blamed_on_a_mod()
    {
        const string log = "Adding mods to an existing campaign: NoCompanionLimit. Removing them later can break the save.\n"
            + "Write-Utf8TextAtomically : Exception calling \"WriteAllText\" with \"3\" argument(s): \"Could not find file 'C:\\Users\\[user]\\Documents\\Mount and Blade II Bannerlord\\CoopData\\DedicatedServer\\.server-config.json.19144.0123456789abcdef0123456789abcdef.tmp'.\"\n"
            + "At C:\\x\\DedicatedServer\\Coodsz-Start-ModdedServer.ps1:1598 char:5\n"
            + "    + CategoryInfo          : NotSpecified: (:) [Write-Utf8TextAtomically], MethodInvocationException\n"
            + "    + FullyQualifiedErrorId : FileNotFoundException,Write-Utf8TextAtomically\n"
            + "NoCompanionLimit\n";
        var extras = new[] { new CoodszHost.Services.ServerCrashExplain.ModName("NoCompanionLimit", "No Companion Limit") };
        var lines = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code 1.", false, log, null, extras, []);
        Assert.Equal(CoodszHost.Services.ServerCrashExplain.StartScriptWriteLines, lines);
        Assert.DoesNotContain(lines, line => line.Contains("No Companion Limit", StringComparison.Ordinal));

        // A full disk is not a blocked file: the general text, and still no mod is blamed for the script's error.
        var full = log.Replace("Could not find file 'C:\\Users\\[user]\\Documents\\Mount and Blade II Bannerlord\\CoopData\\DedicatedServer\\.server-config.json.19144.0123456789abcdef0123456789abcdef.tmp'.", "There is not enough space on the disk.")
            .Replace("FileNotFoundException,Write-Utf8TextAtomically", "IOException,Write-Utf8TextAtomically");
        var fullLines = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code 1.", false, full, null, extras, []);
        Assert.NotEqual(CoodszHost.Services.ServerCrashExplain.StartScriptWriteLines, fullLines);
        Assert.DoesNotContain(fullLines, line => line.Contains("No Companion Limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_builtin_commands_reach_the_server_console()
    {
        var session = new CoodszHost.Services.ServerSession();
        // Refused before any process is looked at.
        Assert.Equal(TaomCoop.Core.ConsoleWrite.NotSent, session.TryConsoleLine("campaign.add_gold_to_hero 100000"));
        Assert.Equal(TaomCoop.Core.ConsoleWrite.NotSent, session.TryConsoleLine("say hi\nstop"));
    }
}

public class CoopKeepLicenseTests
{
    [Fact]
    public void Every_ported_file_keeps_the_copyright_and_the_MIT_notice_and_the_zips_carry_the_notices()
    {
        var root = RepoRoot();
        var ported = Directory.GetFiles(Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Server"), "*.cs")
            .Concat(new[] { "BackupService.cs", "SaveSet.cs" }.Select(file => Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Saves", file)))
            .Concat(Directory.GetFiles(Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Configuration"), "*.cs"))
            .Append(Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Modules", "ModCompatDatabase.cs"))
            .Concat(Directory.GetFiles(Path.Combine(root, "tests", "BannerlordCoopLauncher.Tests"), "CoopKeep*.cs"))
            // Part 7: the server tools protocol, catalogs and admin log, the two game modules and the WPF ports.
            .Concat(new[] { "ToolsCommand.cs", "ToolsParser.cs", "ItemCatalog.cs", "SkillLayout.cs", "KeepRoster.cs", "AuditLog.cs", "Encyclopedia.cs" }
                .Select(file => Path.Combine(root, "src", "BannerlordCoopLauncher.Core", "Keep", file)))
            .Concat(Directory.GetFiles(Path.Combine(root, "compat", "CoodszServerTools", "src"), "*.cs"))
            .Concat(Directory.GetFiles(Path.Combine(root, "compat", "CoodszImageExporter", "src"), "*.cs"))
            .Concat(new[] { "SkillSheet.cs", "PlayerProfileWindow.xaml.cs", "ItemsWindow.xaml.cs", "CheatsPage.xaml.cs", "EncyclopediaPage.xaml.cs" }
                .Select(file => Path.Combine(root, "src", "CoodszHost", "Keep", file)))
            // Max clans per kingdom (ClanCap*.cs), the server rules (ServerRules*.cs, KingdomProtection.cs, PvpRules*.cs, JoinPause.cs, JoinPauseGate.cs and
            // the CoopBridge they share with ClanCap), My Little Warband recruits (MlwRecruit*.cs), the shared Steam ID note, ScheduledRestart.cs,
            // PlayHold.cs, NightlyServer.cs, WarSailsCheck.cs, the Coodsz Server Tools install (ServerToolsModule.cs) and the build marks (CopyMark.cs) are not ported from CoopKeep.
            .Where(file => !Path.GetFileName(file).StartsWith("ClanCap", StringComparison.Ordinal)
                && !Path.GetFileName(file).StartsWith("ServerRules", StringComparison.Ordinal)
                && !Path.GetFileName(file).StartsWith("PvpRules", StringComparison.Ordinal)
                && !Path.GetFileName(file).StartsWith("MlwRecruit", StringComparison.Ordinal)
                && Path.GetFileName(file) is not ("SharedSteamIdWatch.cs" or "JoinPause.cs" or "JoinPauseGate.cs" or "CoopBridge.cs" or "KingdomProtection.cs" or "ScheduledRestart.cs" or "PlayHold.cs" or "NightlyServer.cs" or "WarSailsCheck.cs" or "ServerToolsModule.cs" or "CopyMark.cs"))
            .ToList();
        Assert.NotEmpty(ported);
        foreach (var file in ported)
        {
            var text = File.ReadAllText(file);
            Assert.True(text.Contains("Copyright (c) 2026 aderimo") && text.Contains("Permission is hereby granted, free of charge"), file);
        }

        var notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("Copyright (c) 2026 aderimo", notices);
        Assert.Contains("https://github.com/Aderimo/bannerlord-coopkeep", notices);
        foreach (var script in new[] { "Make-Release.ps1", "Make-AD1259-Release.ps1" })
            Assert.Contains("Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md'", File.ReadAllText(Path.Combine(root, "tools", script)));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
