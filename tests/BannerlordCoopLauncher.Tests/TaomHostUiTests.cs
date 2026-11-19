using System.Globalization;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Server;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomHostUiTests
{
    [Fact]
    public void A_client_command_is_light_red_and_a_failed_command_is_red()
    {
        Assert.Equal(CommandReplyTone.InGame, CommandReply.Tone("This command is only available on the client"));
        Assert.Contains("Alt+`", CommandReply.InGameNotice);
        Assert.Equal(CommandReplyTone.Failed, CommandReply.Tone("Unable to find hero with name or id of Hero_Player"));
        Assert.Equal(CommandReplyTone.Failed, CommandReply.Tone("No active coop battle mission"));
        Assert.Equal(CommandReplyTone.Normal, CommandReply.Tone("Set gold to 25000 for 1 hero(es) named 'Ashe'"));
    }

    [Fact]
    public void Config_edit_keeps_the_other_keys_and_refuses_a_vanilla_save()
    {
        var raw = """
            {
                "saveName": "old",
                "autosaveMinutes": 5,
                "password": "",
                "logFile": true,
                "steam": false
            }
            """;

        var updated = HostConfigEditor.Apply(raw, "save001", "secret", 15, false, true);

        Assert.Contains("\"saveName\": \"save001\"", updated);
        Assert.Contains("\"password\": \"secret\"", updated);
        Assert.Contains("\"autosaveMinutes\": 15", updated);
        Assert.Contains("\"steam\": false", updated);
        Assert.Contains("\"logFile\": true", updated);
        Assert.Throws<InvalidOperationException>(() => HostConfigEditor.Apply(raw, "default_new_game", "", 5, false, true));

        var missingAutosave = """
            {
              "saveName": "old",
              "password": "",
              "logFile": true,
              "steam": false,
            }
            """;
        var added = HostConfigEditor.Apply(missingAutosave, "save001", "secret", 15, false, true);
        Assert.Contains("\"autosaveMinutes\": 15", added);
        Assert.Contains("\"saveName\": \"save001\"", added);
        Assert.DoesNotContain(",,", added);

        var corrupted = """
            {
              // Hours between scheduled restarts. 0 disables them.
              $13,
              "saveName": "save001",
              // Minutes between autosaves.
              $15,
              "password": "$15",
              "logFile": true,
              "steam": false
            }
            """;
        var repaired = HostConfigEditor.Apply(corrupted, "save001", "$15", 5, false, true);
        Assert.Contains("\"autoRestartHours\": 3", repaired);
        Assert.Contains("\"autosaveMinutes\": 5", repaired);
        Assert.Contains("\"password\": \"$15\"", repaired);
        Assert.DoesNotContain("$13", repaired);
        Assert.DoesNotContain("\n  $15", repaired);
        var again = HostConfigEditor.Apply(repaired, "save001", "$15", 15, false, true);
        Assert.Contains("\"autosaveMinutes\": 15", again);
        Assert.Contains("\"password\": \"$15\"", again);
        Assert.Contains("\"autoRestartHours\": 3", again);
    }

    [Fact]
    public void Fast_forward_permission_changes_only_that_flag()
    {
        var raw = """
            {
                // (Default) true: Fast forward is allowed.
                "fastForwardEnabled": false,
                "speedLimitWhilePlayersInBattle": true
            }
            """;

        var updated = CoopModConfig.SetBool(raw, "fastForwardEnabled", true);

        Assert.Contains("\"fastForwardEnabled\": true", updated);
        Assert.Contains("\"speedLimitWhilePlayersInBattle\": true", updated);
        Assert.True(CoopModConfig.ReadBool(updated, "fastForwardEnabled"));
        Assert.Contains("true: Fast forward is allowed.", updated);

        var broken = """
            {
              "modOptions": {
              ,
              "fastForwardEnabled": false,
              "speedLimitWhilePlayersInBattle": true
              }
            }
            """;
        var repaired = CoopModConfig.SetBool(broken, "fastForwardEnabled", true);
        Assert.Contains("\"fastForwardEnabled\": true", repaired);
        Assert.Contains("\"speedLimitWhilePlayersInBattle\": true", repaired);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex("\\{\\s*,"), repaired);
    }

    [Fact]
    public void Console_sends_typed_commands_and_refuses_empty_input()
    {
        Assert.Null(ServerCommandPolicy.Refusal("status"));
        Assert.Null(ServerCommandPolicy.Refusal("say hello"));
        Assert.Null(ServerCommandPolicy.Refusal("kick 0"));
        Assert.Null(ServerCommandPolicy.Refusal(AdminCommand.SetGoldByName("Guthric", 10000)));
        Assert.Null(ServerCommandPolicy.Refusal(AdminCommand.GiveInfluence("clan_empire_south_1", 100)));
        Assert.Equal("coop.debug.hero.set_gold \"John Smith\" 10000", AdminCommand.SetGoldByName("John Smith", int.Parse(CoopCheats.Amount("nope", 10000))));
        Assert.Equal("coop.debug.clan.give_influence clan_1 100", AdminCommand.GiveInfluence("clan_1", int.Parse(CoopCheats.Amount("-5", 100))));
        Assert.Null(ServerCommandPolicy.Refusal("coop.unstuck"));
        Assert.Null(ServerCommandPolicy.Refusal("coop.debug.hero.list"));
        Assert.Null(ServerCommandPolicy.Refusal("coop.debug.advance_time 2"));
        Assert.Null(ServerCommandPolicy.Refusal("campaign.add_gold_to_hero 1000"));
        Assert.Null(ServerCommandPolicy.Refusal("campaign.add_influence_to_clan 100"));
        Assert.Null(ServerCommandPolicy.Refusal("load custom mod"));
        Assert.Null(ServerCommandPolicy.Refusal("stop"));
        Assert.Equal("Enter a command.", ServerCommandPolicy.Refusal("   "));
        Assert.Equal("Enter a command.", ServerCommandPolicy.Refusal(""));
        Assert.NotNull(ServerCommandPolicy.Refusal("say" + "\n" + "hello"));
        Assert.NotNull(ServerCommandPolicy.Refusal(new string('a', 501)));
        Assert.Null(ServerCommandPolicy.Refusal(new string('a', 500)));
    }

    [Fact]
    public void Last_good_save_is_the_newest_copy_from_before_the_break()
    {
        var copies = new[]
        {
            new SaveCopy("current", new DateTime(2026, 9, 28, 1, 32, 56)),
            new SaveCopy("backup1", new DateTime(2026, 9, 28, 1, 27, 56)),
            new SaveCopy("backup2", new DateTime(2026, 9, 28, 1, 17, 56))
        };
        var brokenAt = new DateTime(2026, 9, 28, 1, 22, 3);
        Assert.Equal("backup2", LastGoodSave.Pick(copies, brokenAt));
        Assert.Equal("current", LastGoodSave.Pick(copies, null));
        Assert.Null(LastGoodSave.Pick(copies.Take(2).ToList(), brokenAt));
    }

    [Fact]
    public void A_broken_party_blocks_the_save_until_the_hero_is_on_the_map_again()
    {
        var broken = """
            00:45:04 @DS@{"ev":"players","list":[{"id":0,"name":"Ashe","state":"on map","addr":"127.0.0.1:1"}]}
            [01:22:03 ERR] Controller 76561198000000014 hero Hero_Player has no recoverable player party; disconnecting the joiner
            """;
        Assert.Equal(BrokenWorldGuard.RefusalText, BrokenWorldGuard.Refusal(broken));

        var recovered = broken + "\n01:40:00 @DS@{\"ev\":\"players\",\"list\":[{\"id\":0,\"name\":\"Ashe\",\"state\":\"on map\",\"addr\":\"127.0.0.1:1\"}]}";
        Assert.Null(BrokenWorldGuard.Refusal(recovered));
        var errorFirst = """
            [01:22:03 ERR] hero Hero_Player has no recoverable player party
            00:45:04 @DS@{"ev":"players","list":[{"state":"on map"}]}
            """;
        Assert.Equal(BrokenWorldGuard.RefusalText, BrokenWorldGuard.Refusal(errorFirst));
        Assert.Null(BrokenWorldGuard.Refusal("00:45:04 @DS@{\"ev\":\"players\",\"list\":[{\"state\":\"on map\"}]}"));
        var brokenAt = new DateTime(2026, 9, 28, 1, 22, 3);
        var after = new DateTime(2026, 9, 28, 1, 37, 56);
        var before = new DateTime(2026, 9, 28, 1, 17, 56);
        Assert.Equal(0, BrokenWorldGuard.AutosaveMinutes(5, after, brokenAt));
        Assert.Equal(5, BrokenWorldGuard.AutosaveMinutes(5, before, brokenAt));
        Assert.Equal(5, BrokenWorldGuard.AutosaveMinutes(5, after, null));
        Assert.True(BrokenWorldGuard.StopOwnedServer(ownsProcess: true, alreadyHeld: false, spareUntilOnMap: false, broken));
        Assert.False(BrokenWorldGuard.StopOwnedServer(ownsProcess: true, alreadyHeld: true, spareUntilOnMap: false, broken));
        Assert.False(BrokenWorldGuard.StopOwnedServer(ownsProcess: false, alreadyHeld: false, spareUntilOnMap: false, broken));
        Assert.False(BrokenWorldGuard.StopOwnedServer(ownsProcess: true, alreadyHeld: false, spareUntilOnMap: true, broken));
        Assert.False(BrokenWorldGuard.StopOwnedServer(ownsProcess: true, alreadyHeld: false, spareUntilOnMap: false, recovered));
    }

    // TAOM crash #31: a clan with an empty banner made every load fail, and Save and stop then saved.
    private const string FailedLoad = """
        23:06:20.100  @DS@{"ev":"state","phase":"loading","save":"camp1","pw":false}
        23:06:28.948  @DS@{"ev":"state","phase":"fatal","save":"camp1","detail":"The campaign is incomplete and must not be served. Original error: System.NullReferenceException at TaleWorlds.Core.Banner.ChangeBackgroundColor"}
        """;

    [Fact]
    public void A_failed_load_means_save_and_stop_do_not_save()
    {
        Assert.True(BrokenWorldGuard.LoadFailed(FailedLoad));
        Assert.True(BrokenWorldGuard.LoadFailed(null, FailedLoad));
        // The console's own fatal state counts even when the log file has not caught up.
        Assert.True(BrokenWorldGuard.LoadFailed(ServerStateEvent.Last(FailedLoad), ""));
        Assert.Contains(FailedLoad.Split('\n'), BrokenWorldGuard.LoadFailed);
        // The server writes "stopping" after the fatal one; after a tool reopen only the log is left.
        Assert.True(BrokenWorldGuard.LoadFailed(FailedLoad + "\n" + Stopping));
        Assert.True(BrokenWorldGuard.LoadFailed(null, FailedLoad + "\n" + Stopping));
        Assert.False(BrokenWorldGuard.LoadFailed(ServerStateEvent.Last(Serving), ""));
        Assert.True(BrokenWorldGuard.LoadFailedAfter(true, Stopping));
        Assert.False(BrokenWorldGuard.LoadFailedAfter(true, Serving));
        Assert.False(BrokenWorldGuard.LoadFailedAfter(false, Stopping));
    }

    private const string Stopping = "23:06:29.100  @DS@{\"ev\":\"state\",\"phase\":\"stopping\",\"save\":\"camp1\",\"pw\":false}";
    private const string Serving = "23:14:40.000  @DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"camp0\",\"pw\":false}";

    [Fact]
    public void Coodsz_host_session_keeps_a_failed_load_until_a_new_run_starts()
    {
        var session = new CoodszHost.Services.ServerSession();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var observe = typeof(CoodszHost.Services.ServerSession).GetMethod("Observe", flags)!;
        var forget = typeof(CoodszHost.Services.ServerSession).GetMethod("ForgetLoadFailure", flags)!;
        void Feed(string text)
        {
            foreach (var line in text.Split('\n'))
                observe.Invoke(session, new object?[] { line });
        }

        Feed(FailedLoad);
        Assert.True(session.LoadFailed);
        // The server says "stopping" on its way down after the fatal one; the flag stays.
        Feed(Stopping + "\n23:06:29.200 [ERR] some later line");
        Assert.True(session.LoadFailed);
        Feed(Serving);
        Assert.False(session.LoadFailed);

        // Restart: Stop, then Start clears it, then the follower replays the old run's log (fatal and
        // stopping) before the new run's boot line arrives. The boot line clears it again.
        Feed(FailedLoad + "\n" + Stopping);
        forget.Invoke(session, null);
        Assert.False(session.LoadFailed);
        Feed(FailedLoad + "\n" + Stopping);
        Feed("23:12:01.000  @DS@{\"ev\":\"state\",\"phase\":\"boot\",\"pw\":false}");
        Assert.False(session.LoadFailed);

        // Start and Stop act on real server processes, so the test checks they clear the flag in the source.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs")).Replace("\r\n", "\n");
        foreach (var method in new[] { "    public void Start(string executable", "    public void Stop()\n" })
        {
            var at = source.IndexOf(method, StringComparison.Ordinal);
            Assert.True(at >= 0, method);
            var end = source.IndexOf("\n    }\n", at, StringComparison.Ordinal);
            Assert.Contains("ForgetLoadFailure();", source[at..end]);
        }
    }

    [Fact]
    public void A_normal_log_still_saves()
    {
        var good = """
            23:06:20.100  @DS@{"ev":"state","phase":"loading","save":"camp1","pw":false}
            23:08:02.500  @DS@{"ev":"state","phase":"serving","save":"camp1","pw":false}
            """;
        Assert.False(BrokenWorldGuard.LoadFailed(good));
        Assert.False(BrokenWorldGuard.LoadFailed(null, good));
        Assert.False(BrokenWorldGuard.LoadFailed(""));
        Assert.False(BrokenWorldGuard.LoadFailed(null, null));
        // The broken world case keeps its own refusal and is not a failed load.
        Assert.False(BrokenWorldGuard.LoadFailed("[01:22:03 ERR] hero Hero_Player has no recoverable player party"));
    }

    [Fact]
    public void An_earlier_failed_load_in_the_console_does_not_block_a_later_good_run()
    {
        var console = FailedLoad + """

            23:10:00.000 [CoodszHost] Server process exited with code 2.
            23:12:01.000  @DS@{"ev":"state","phase":"boot","pw":false}
            23:12:05.000  @DS@{"ev":"state","phase":"loading","save":"camp0","pw":false}
            23:14:40.000  @DS@{"ev":"state","phase":"serving","save":"camp0","pw":false}
            """;
        Assert.False(BrokenWorldGuard.LoadFailed(console));
        // The later run is still loading: no fatal yet, so it is not blocked either.
        var loading = FailedLoad + "\n23:12:05.000  @DS@{\"ev\":\"state\",\"phase\":\"loading\",\"save\":\"camp0\",\"pw\":false}";
        Assert.False(BrokenWorldGuard.LoadFailed(loading));
    }

    [Fact]
    public void Player_log_reads_the_latest_serving_list()
    {
        var text = """
            @DS@{"ev":"players","list":[{"id":0,"name":"(joining)","state":"creating character","addr":"127.0.0.1:1"}]}
            @DS@{"ev":"players","list":[{"id":0,"name":"Guthric","state":"on map","addr":"127.0.0.1:54458"}]}
            """;

        var players = HostPlayerLog.Read(text);

        Assert.Single(players);
        Assert.Equal("Guthric", players[0].Name);
        Assert.Equal("on map", players[0].State);
    }

    [Fact]
    public void Player_list_is_found_when_the_join_line_is_older_than_the_tail()
    {
        var dir = Directory.CreateTempSubdirectory("taom-players");
        try
        {
            var path = Path.Combine(dir.FullName, "coop-server.log");
            var player = "@DS@{\"ev\":\"players\",\"list\":[{\"id\":0,\"name\":\"Ashe\",\"state\":\"on map\",\"addr\":\"127.0.0.1:1\"}]}\n";
            File.WriteAllText(path, player + new string('p', 300_000));
            var recent = HostLogFile.ReadTail(path, 8_000);
            Assert.DoesNotContain("Ashe", recent);
            var chunk = HostLogFile.ReadTailContaining(path, "\"ev\":\"players\"", 8_000);
            var players = HostPlayerLog.Read(chunk);
            Assert.Equal("Ashe", Assert.Single(players).Name);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Server_log_tail_reads_unicode_and_prefers_the_dedicated_server()
    {
        var dir = Directory.CreateTempSubdirectory("taom-host-log");
        try
        {
            var serverLog = Path.Combine(dir.FullName, "server-run-save001.log");
            using (var writer = new StreamWriter(serverLog, false, Encoding.Unicode))
            {
                writer.Write(new string('x', 150_000));
                writer.Write("[DedicatedServer] pulse: players=0\n");
                writer.Write("@DS@{\"ev\":\"players\",\"list\":[]}\n");
            }

            var notes = Path.Combine(dir.FullName, "navigation.log");
            File.WriteAllText(notes, "general-navigation-recovery");
            File.SetLastWriteTimeUtc(notes, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(serverLog, DateTime.UtcNow.AddMinutes(-5));

            var tail = HostLogFile.ReadTail(serverLog);
            var nullAt = tail.IndexOf('\0');
            Assert.True(nullAt < 0, "null at " + nullAt + " first=" + string.Join(",", tail.Take(8).Select(ch => ((int)ch).ToString())));
            Assert.Contains("[DedicatedServer] pulse: players=0", tail);
            Assert.Contains("\"ev\":\"players\"", tail);

            var chosen = HostLogFile.Prefer(new DirectoryInfo(dir.FullName).GetFiles()).First();
            Assert.Equal("server-run-save001.log", chosen.Name);

            var older = Path.Combine(dir.FullName, "coop-server-old.log");
            var newer = Path.Combine(dir.FullName, "coop-server-new.log");
            File.WriteAllText(older, "OLD-HEADER\n" + new string('a', 80_000) + "\nOLD-END");
            File.WriteAllText(newer, "NEW-HEADER\n" + new string('b', 80_000) + "\nNEW-END");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);
            var coopLogs = new DirectoryInfo(dir.FullName).GetFiles("coop-server-*.log");
            var newestCoop = HostLogFile.Prefer(coopLogs).First();
            Assert.Equal("coop-server-new.log", newestCoop.Name);
            var live = HostLogFile.Prefer(new DirectoryInfo(dir.FullName).GetFiles()).First();
            Assert.Equal("coop-server-new.log", live.Name);
            var smallTail = HostLogFile.ReadTail(newer, 64_000);
            Assert.DoesNotContain("NEW-HEADER", smallTail);
            Assert.Contains("NEW-END", smallTail);
            Assert.True(smallTail.Length < 70_000);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Ban_list_stores_a_name_once_and_removes_it()
    {
        var written = PlayerBanList.Write([" Guthric ", "guthric", "", "John Smith"]);
        var names = PlayerBanList.Read(written);

        Assert.Equal(["Guthric", "John Smith"], names);
        Assert.True(PlayerBanList.IsBanned(names, "guthric"));
        Assert.False(PlayerBanList.IsBanned(names, " "));
        names.RemoveAll(name => name.Equals("Guthric", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["John Smith"], PlayerBanList.Read(PlayerBanList.Write(names)));
        Assert.Empty(PlayerBanList.Read("not json"));
    }

    // The window's thread for a test: posted work waits until the test runs it, one item at a time.
    private sealed class UiPump : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Work, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
                _queue.Enqueue((d, state));
        }

        public void RunUntil(Func<bool> done)
        {
            for (var i = 0; i < 500 && !done(); i++)
            {
                while (true)
                {
                    (SendOrPostCallback Work, object? State) next;
                    lock (_queue)
                    {
                        if (_queue.Count == 0)
                            break;
                        next = _queue.Dequeue();
                    }
                    next.Work(next.State);
                }
                if (!done())
                    Thread.Sleep(5);
            }
            Assert.True(done());
        }
    }

    [Fact]
    public void Five_connect_clicks_start_one_game_and_a_click_during_the_lobby_search_starts_it_over()
    {
        // TAOM CR-W4H9X: five Connect clicks in 8 seconds started two games at the same moment, and the second
        // crashed on TAOM's log file the first had open. The same loop as ConnectOnceAsync, with a search that waits.
        var previous = SynchronizationContext.Current;
        var pump = new UiPump();
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            ClicksOnTheWindowThread(pump);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var window = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        var connect = SourceFiles.Method(window, "private async Task ConnectAsync(");
        Assert.Contains("if (!_connect.TryBegin())", connect);
        Assert.True(connect.IndexOf("_connect.TryBegin()", StringComparison.Ordinal) < connect.IndexOf("await ConnectOnceAsync();", StringComparison.Ordinal));
        Assert.Contains("_connect.End();", connect[connect.IndexOf("finally", StringComparison.Ordinal)..]);
        var once = SourceFiles.Method(window, "private async Task ConnectOnceAsync(");
        Assert.Contains("FindWithRetryAsync(_gameRoot, steamName, Log, search)", once);
        Assert.Contains("catch (Exception) when (search.IsCancellationRequested && _connect.TakeSearchAgain())", once);
        // The guard holds through the join watch, which ends when the game joined, was turned down or closed.
        Assert.True(once.IndexOf("await WatchJoinAsync(started, _gameRoot, setup,", StringComparison.Ordinal) > once.IndexOf("Process.Start(start)", StringComparison.Ordinal));
        Assert.DoesNotContain("_lobbySearch", window);
        var safeMode = SourceFiles.Method(window, "private async void SafeMode_Click(");
        Assert.True(safeMode.IndexOf("if (_connect.Running)", StringComparison.Ordinal) < safeMode.IndexOf("_safeMode.RunAsync(", StringComparison.Ordinal));
        // Safe mode says it only works on a Plain TAOM host before the game starts (Report Replies suggestion).
        var warn = safeMode.IndexOf("Log(SafeModeNeedsPlainHost);", StringComparison.Ordinal);
        Assert.True(warn > 0 && warn < safeMode.IndexOf("_safeMode.RunAsync(", StringComparison.Ordinal));
        Assert.Contains("public const string SafeModeNeedsPlainHost = \"Safe mode only works when the host's server runs Plain TAOM too.", window);
        Assert.Contains("if (_busy || _connect.Running)", SourceFiles.Method(window, "private async void FindCrash_Click("));
    }

    private static void ClicksOnTheWindowThread(UiPump pump)
    {
        var gate = new OneConnect();
        var games = 0;
        var searches = new List<TaskCompletionSource>();
        var notes = new List<string>();

        async Task Click()
        {
            if (!gate.TryBegin())
            {
                notes.Add(gate.SearchAgain() ? "again" : "running");
                return;
            }
            try
            {
                while (true)
                {
                    var token = gate.BeginSearch();
                    var search = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    searches.Add(search);
                    try
                    {
                        await search.Task.WaitAsync(token);
                        token.ThrowIfCancellationRequested();
                        break;
                    }
                    catch (Exception) when (token.IsCancellationRequested && gate.TakeSearchAgain())
                    {
                    }
                    finally
                    {
                        gate.EndSearch();
                    }
                }
                games++;
            }
            finally
            {
                gate.End();
            }
        }

        // Each found lobby completes off the window thread, so the Connect resumes only when the pump runs it.
        void Found(int search)
        {
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                searches[search].SetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(pump);
            }
        }

        var first = Click();
        Assert.Single(searches);
        Assert.True(Click().IsCompleted);
        pump.RunUntil(() => searches.Count == 2);
        Assert.True(Click().IsCompleted);
        pump.RunUntil(() => searches.Count == 3);
        // The lobby is found just as another click comes in, before the Connect resumes: that click still starts
        // the search over, and no game starts from the stale find.
        Found(2);
        Assert.True(Click().IsCompleted);
        pump.RunUntil(() => searches.Count == 4);
        Assert.Equal(0, games);
        Found(3);
        pump.RunUntil(() => first.IsCompleted);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal(1, games);
        Assert.Equal(["again", "again", "again"], notes);
        Assert.False(gate.Running);

        // While the game starts (no search running) a later click does nothing at all.
        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());
        Assert.False(gate.SearchAgain());
        Assert.False(gate.TakeSearchAgain());
        gate.End();
        Assert.True(gate.TryBegin());
        gate.End();
    }

    [Fact]
    public void A_game_that_closes_while_another_bannerlord_runs_is_not_blamed_on_a_mod()
    {
        // TAOM CR-W4H9X: the second copy closed on TAOM's locked log, and the hint blamed the last DLL it loaded
        // (Better Exception Window) and said "This setup has no extra mods" on TAOM plus.
        var log = "[20:17:27.627] Loading assembly: C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\261550\\2875478172\\bin\\Win64_Shipping_Client\\TaleWorlds.MountAndBlade.Launcher.Library.dll\n"
            + "[20:17:27.628] Loading assembly: C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\261550\\2875478172\\bin\\Win64_Shipping_Client\\BetterExceptionWindow.dll\n"
            + "[20:17:31.251] MusicParameters have been resetted. \n";
        var lines = StartupCrashExplain.Explain(false, null, null, log, Array.Empty<StartupCrashExplain.ModName>(),
            ["TAOM", "Coop", "BetterExceptionWindow", "RTSCamera"], setupUsesWorkshop: false);
        Assert.Equal([StartupCrashExplain.NoExtraModsLine], lines);
        // A real exception that names a mod in the list still points at it.
        var real = StartupCrashExplain.Explain(false, null, null, "System.NullReferenceException: Object reference\n   at RTSCamera.SubModule.OnTick()",
            Array.Empty<StartupCrashExplain.ModName>(), ["TAOM", "RTSCamera"], setupUsesWorkshop: false);
        Assert.Contains("RTSCamera is in the working list", real[^1]);
        var native = StartupCrashExplain.Explain(false, null, null, "EXCEPTION_ACCESS_VIOLATION in RTSCamera.dll",
            Array.Empty<StartupCrashExplain.ModName>(), ["TAOM", "RTSCamera"], setupUsesWorkshop: false);
        Assert.Contains("RTSCamera is in the working list", native[^1]);

        var explain = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "TaomCoopLauncher"), "private async Task ExplainEarlyExitAsync(");
        var other = explain.IndexOf("ClientLaunchGuard.OtherGameRunning(started.Id)", StringComparison.Ordinal);
        Assert.True(other > 0 && other < explain.IndexOf("StartupCrashExplain.Explain(", StringComparison.Ordinal));
        Assert.Contains("Log(ClientLaunchGuard.SecondCopyClosed);", explain);
        // With another copy open and no mod named, the hint stops there: no "no extra mods" line, no crash report.
        // A log or report that names a mod still gets its hint and the crash report.
        Assert.True(StartupCrashExplain.OnlyTheOtherCopy(true, [], lines, null, log));
        Assert.False(StartupCrashExplain.OnlyTheOtherCopy(true, ["RTSCamera"], lines, null, log));
        Assert.False(StartupCrashExplain.OnlyTheOtherCopy(false, [], lines, null, log));
        Assert.False(StartupCrashExplain.OnlyTheOtherCopy(true, [], real, null, log));
        // An exception only in report.txt, naming a core module no extra mod list has, still gets the report.
        Assert.False(StartupCrashExplain.OnlyTheOtherCopy(true, [], lines,
            "Exception: System.NullReferenceException\nSource: TAOM.CoopCompat", log));
        var stop = explain.IndexOf("if (StartupCrashExplain.OnlyTheOtherCopy(otherGame, named, lines, capture.ReportText, capture.GameLog))", StringComparison.Ordinal);
        Assert.True(stop > 0 && stop < explain.IndexOf("NoModNamedLine(setup)", StringComparison.Ordinal)
            && stop < explain.IndexOf("ShowCrashReport(", StringComparison.Ordinal));
        // The lines follow the setup this game was started with, not the saved one.
        Assert.Contains("TaomLoadOrder.ClientOrder(setup)", explain);
        Assert.Contains("if (setup.HasEnabledExtras && lines is [StartupCrashExplain.NoExtraModsLine])", explain);
        Assert.True(explain.IndexOf("var named = CrashNamed(capture, setup);", StringComparison.Ordinal) < stop);
        Assert.DoesNotContain("_settings.ModSetups.Current()", explain);
        Assert.DoesNotContain("LaunchSetup", explain);
    }

    [Fact]
    public async Task A_second_safe_mode_click_does_not_end_the_first_clicks_plain_taom()
    {
        // Two Safe mode clicks about 2 seconds apart started the game with TAOM plus: the second click's
        // Connect ended at once and switched the flag off while the first was still in the join check.
        var safe = new SafeModeJoin();
        var joinCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? firstAtLaunch = null;
        var first = safe.RunAsync(async () =>
        {
            Assert.True(safe.On);
            await joinCheck.Task;
            firstAtLaunch = safe.On;
        });
        // The click handler itself is back to off as soon as the join waits.
        Assert.False(safe.On);
        await safe.RunAsync(() => Task.CompletedTask);
        // Outside a Safe mode join (a plain Connect, the window) it is off.
        Assert.False(safe.On);
        Assert.False(await Task.Run(() => safe.On));
        joinCheck.SetResult();
        await first;
        Assert.True(firstAtLaunch);
        Assert.False(safe.On);

        var crash = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.Crash.cs"));
        Assert.Contains("TaomModIds.Resolve(_safeMode.On", crash);
        Assert.Contains("await _safeMode.RunAsync(", crash);
        Assert.DoesNotContain("_safeModeOnce", crash);

        // After the game closes the Safe mode start is over, so crash naming and the report's mod list use the
        // setup the game was started with, not a fresh read of the saved one.
        Assert.Contains("CrashNamed(capture, setup)", crash);
        // A Find the crashing mod round after a Safe mode game is not that game: reports list the saved setup again.
        var bisect = crash[crash.IndexOf("private async Task<bool?> TestToMainMenuAsync(", StringComparison.Ordinal)..];
        Assert.True(bisect.IndexOf("_safeModeGameSetup = null;", StringComparison.Ordinal) is > 0 and var cleared
            && cleared < bisect.IndexOf("await started.WaitForExitAsync();", StringComparison.Ordinal));
        Assert.Contains("ReportRequest(SessionExtras(setup), LaunchOrder(setup),",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.Reports.cs")));
        var connect = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.xaml.cs"));
        Assert.Contains("_safeModeGameSetup = _safeMode.On ? setup : null;", connect);
        Assert.Contains("RecordSessionAsync(started, setup, sessionExtras, modules)", connect);
        var reports = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.CrashReports.cs"));
        Assert.Contains("_safeModeGameSetup is { } played ? LaunchOrder(played) : LaunchOrder()", reports);
    }

    [Fact]
    public void Save_slot_rename_keeps_the_campaign_id_and_blocks_single_player_paths()
    {
        var json = """
            {
              "UniqueGameId": "save001",
              "Players": [ { "HeroId": "Hero_Player" } ]
            }
            """;
        var rewritten = SaveSlot.RewriteSlotId(json, "save001", "CampaignRenameTest");
        Assert.Contains("\"UniqueGameId\": \"CampaignRenameTest\"", rewritten);
        Assert.Contains("\"HeroId\": \"Hero_Player\"", rewritten);
        Assert.Contains("\"UniqueGameId\": \"$15\"", SaveSlot.RewriteSlotId(json, "save001", "$15"));
        Assert.Equal(json, SaveSlot.RewriteSlotId(json, "other", "CampaignRenameTest"));
        Assert.NotNull(SaveSlot.NameError("default_new_game"));
        Assert.NotNull(SaveSlot.NameError("..\\save001"));
        Assert.Null(SaveSlot.NameError("CampaignRenameTest"));
        Assert.Equal("TAOMHOST", SaveSlot.ResolveCopyName("", "TAOMHOST"));
        Assert.Equal("TAOMHOST", SaveSlot.ResolveCopyName("   ", "TAOMHOST"));
        Assert.Equal("HostCopy", SaveSlot.ResolveCopyName("HostCopy", "TAOMHOST"));

        var root = Path.Combine(Path.GetTempPath(), "taom-slot-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "server");
        var documents = Path.Combine(root, "documents");
        Directory.CreateDirectory(server);
        Directory.CreateDirectory(documents);
        try
        {
            var serverSave = Path.Combine(server, "save001.sav");
            var singlePlayer = Path.Combine(documents, "save001.sav");
            File.WriteAllText(serverSave, "server");
            File.WriteAllText(singlePlayer, "single");
            File.WriteAllText(Path.Combine(documents, "save001.json"), json);
            Assert.True(SaveSlot.IsInsideDirectory(serverSave, server));
            Assert.False(SaveSlot.IsInsideDirectory(singlePlayer, server));
            Assert.True(SaveSlot.CanHost(serverSave, server));
            Assert.False(SaveSlot.CanHost(singlePlayer, server));
            Assert.False(SaveSlot.CanHost(Path.Combine(server, "TAOMHOST.sav"), server));
            File.WriteAllText(Path.Combine(server, "TAOMHOST.sav"), "campaign");
            Assert.True(SaveSlot.CanHost(Path.Combine(server, "TAOMHOST.sav"), server));
            SaveSlot.CopySidecar(Path.Combine(documents, "save001.json"), server, "save001", "Finale");
            Assert.Contains("\"UniqueGameId\": \"Finale\"", File.ReadAllText(Path.Combine(server, "Finale.json")));
            Assert.Contains("\"UniqueGameId\": \"save001\"", File.ReadAllText(Path.Combine(documents, "save001.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Gameplay_settings_keep_the_exact_set_and_refuse_a_bad_battle_size()
    {
        var values = GameplaySettingsEditor.Definitions.ToDictionary(
            item => item.Key,
            item => item.Key switch
            {
                "wotr.phase1Day" => "30",
                "wotr.phase2Day" => "44",
                _ => item.Kind == "bool" ? "true" : item.Min.ToString(CultureInfo.InvariantCulture)
            });
        var taom = string.Join(",", values.Select(pair => "\"" + pair.Key + "\":" + (pair.Value is "true" or "false" ? pair.Value : pair.Value)));
        var json = "{\"schemaVersion\":1,\"battleSize\":{\"field\":2048,\"siege\":2048,\"sallyOut\":800},\"taom\":{" + taom + "}}";

        var updated = GameplaySettingsEditor.Apply(json, values, "1000", "800", "400");
        using var document = JsonDocument.Parse(updated);
        Assert.Equal(1000, document.RootElement.GetProperty("battleSize").GetProperty("field").GetInt32());
        Assert.Equal(GameplaySettingsEditor.Definitions.Count, document.RootElement.GetProperty("taom").EnumerateObject().Count());
        Assert.Throws<InvalidOperationException>(() => GameplaySettingsEditor.Apply(json, values, "10", "800", "400"));
    }

    [Fact]
    public void Gameplay_settings_accept_and_keep_settings_a_newer_kit_adds()
    {
        var values = GameplaySettingsEditor.Definitions.ToDictionary(
            item => item.Key,
            item => item.Key switch
            {
                "wotr.phase1Day" => "30",
                "wotr.phase2Day" => "44",
                _ => item.Kind == "bool" ? "true" : item.Min.ToString(CultureInfo.InvariantCulture)
            });
        // The 0.3.25 kit writes mcm.* settings the tool does not show; the tool used to refuse to open on them.
        var taom = string.Join(",", values.Select(pair => "\"" + pair.Key + "\":" + pair.Value))
            + ",\"mcm.EnableMessengers\":true,\"mcm.MessengerGoldCost\":50,\"party.troopWeight\":false";
        var json = "{\"schemaVersion\":1,\"battleSize\":{\"field\":800,\"siege\":800,\"sallyOut\":800},\"taom\":{" + taom + "}}";

        var read = GameplaySettingsEditor.Read(json, out var field, out _, out _);
        Assert.Equal(800, field);
        Assert.Equal(GameplaySettingsEditor.Definitions.Count + 1, read.Count);
        Assert.Equal("false", read["party.troopWeight"]);

        var updated = GameplaySettingsEditor.Apply(json, values, "1000", "800", "400");
        using var document = JsonDocument.Parse(updated);
        var saved = document.RootElement.GetProperty("taom");
        Assert.True(saved.GetProperty("mcm.EnableMessengers").GetBoolean());
        Assert.Equal(50, saved.GetProperty("mcm.MessengerGoldCost").GetInt32());
        Assert.False(saved.GetProperty("party.troopWeight").GetBoolean());
        Assert.Equal(GameplaySettingsEditor.Definitions.Count + 3, saved.EnumerateObject().Count());

        var missing = json.Replace("\"wotr.enabled\":true,", "");
        Assert.Throws<InvalidOperationException>(() => GameplaySettingsEditor.Read(missing, out _, out _, out _));
    }

    [Fact]
    public void Gameplay_settings_never_add_troop_weight_to_an_older_kit()
    {
        var json = GameplaySettingsJson(includeTroopWeight: false);
        var read = GameplaySettingsEditor.Read(json, out _, out _, out _);
        Assert.False(read.ContainsKey("party.troopWeight"));

        var unchanged = GameplaySettingsEditor.Apply(json, GameplaySettingsEditor.Definitions.ToDictionary(
            item => item.Key,
            item => ValidGameplayValue(item)), "800", "800", "800");
        using (var saved = JsonDocument.Parse(unchanged))
            Assert.False(saved.RootElement.GetProperty("taom").TryGetProperty("party.troopWeight", out _));

        var enabledValues = GameplaySettingsEditor.Definitions.ToDictionary(
            item => item.Key,
            item => ValidGameplayValue(item));
        enabledValues["party.troopWeight"] = "true";
        var enabled = GameplaySettingsEditor.Apply(json, enabledValues, "800", "800", "800");
        using var enabledDocument = JsonDocument.Parse(enabled);
        Assert.False(enabledDocument.RootElement.GetProperty("taom").TryGetProperty("party.troopWeight", out _));
    }

    [Fact]
    public void Gameplay_settings_read_and_change_troop_weight_without_duplicate_keys()
    {
        foreach (var value in new[] { "true", "false" })
        {
            var json = GameplaySettingsJson(includeTroopWeight: true, troopWeight: value);
            var read = GameplaySettingsEditor.Read(json, out _, out _, out _);
            Assert.Equal(value, read["party.troopWeight"]);

            var values = GameplaySettingsEditor.Definitions.ToDictionary(
                item => item.Key,
                item => ValidGameplayValue(item));
            values["party.troopWeight"] = value == "true" ? "false" : "true";
            var updated = GameplaySettingsEditor.Apply(json, values, "800", "800", "800");
            using var saved = JsonDocument.Parse(updated);
            var taom = saved.RootElement.GetProperty("taom");
            Assert.Equal(value != "true", taom.GetProperty("party.troopWeight").GetBoolean());
            Assert.Equal(1, taom.EnumerateObject().Count(property => property.Name == "party.troopWeight"));
        }
    }

    [Fact]
    public void Gameplay_settings_refuse_a_troop_weight_that_is_not_true_or_false()
    {
        // Strict like every other true or false setting, so a broken kit says so instead of saving a guess.
        foreach (var value in new[] { "\"yes\"", "1" })
            Assert.Throws<InvalidOperationException>(() => GameplaySettingsEditor.Read(GameplaySettingsJson(includeTroopWeight: true, troopWeight: value), out _, out _, out _));
    }

    private static string GameplaySettingsJson(bool includeTroopWeight, string troopWeight = "false")
    {
        var values = GameplaySettingsEditor.Definitions.ToDictionary(
            item => item.Key,
            item => ValidGameplayValue(item));
        var taom = string.Join(",", values.Select(pair => "\"" + pair.Key + "\":" + pair.Value));
        if (includeTroopWeight)
            taom += ",\"party.troopWeight\":" + troopWeight;
        return "{\"schemaVersion\":1,\"battleSize\":{\"field\":800,\"siege\":800,\"sallyOut\":800},\"taom\":{" + taom + "}}";
    }

    private static string ValidGameplayValue(GameplaySetting item) => item.Key switch
    {
        "wotr.phase1Day" => "30",
        "wotr.phase2Day" => "44",
        _ => item.Kind == "bool" ? "true" : item.Min.ToString(CultureInfo.InvariantCulture)
    };

    [Fact]
    public void Ready_note_replaces_every_starting_note_and_nothing_else()
    {
        // StarZ: the status said Serving but the note still said the server was starting, so players pressed Start again.
        Assert.True(ServerReadyNote.Replaces("Server is starting. It is ready when the log says SERVING. The first start of a new campaign builds the map cache and can take up to an hour; the status at the top shows how far it is."));
        Assert.True(ServerReadyNote.Replaces("Switched to the setup this save was made with. Starting the server again."));
        Assert.True(ServerReadyNote.Replaces("Loaded the save from 3 hours ago. Starting the server."));
        Assert.True(ServerReadyNote.Replaces("The server cannot load a DLL. Click the red Unblock DLLs button, then start again."));
        Assert.False(ServerReadyNote.Replaces(BrokenWorldGuard.AutosaveHeldText));
        Assert.False(ServerReadyNote.Replaces("Saved settings. Restart the server to use them. Fast forward stays as it is until that restart."));
        Assert.False(ServerReadyNote.Replaces(ServerReadyNote.Ready));
        Assert.False(ServerReadyNote.Replaces(""));
    }

    [Fact]
    public void An_empty_autosave_box_keeps_the_stored_minutes_and_lets_the_rest_save()
    {
        var raw = """
            {
                "saveName": "old",
                "autosaveMinutes": 25,
                "password": "",
                "logFile": true,
                "steam": false
            }
            """;

        // An empty box used to stop Save settings with "Autosave minutes must be from 0 to 1440".
        Assert.Null(HostConfigEditor.ParseAutosave(""));
        Assert.Null(HostConfigEditor.ParseAutosave("   "));
        Assert.Null(HostConfigEditor.ParseAutosave(null));
        var updated = HostConfigEditor.Apply(raw, "save001", "secret", HostConfigEditor.ParseAutosave(" "), true, true);
        Assert.Contains("\"autosaveMinutes\": 25", updated);
        Assert.Contains("\"saveName\": \"save001\"", updated);
        Assert.Contains("\"password\": \"secret\"", updated);
        Assert.Contains("\"steam\": true", updated);

        Assert.Equal(0, HostConfigEditor.ParseAutosave("0"));
        Assert.Equal(1440, HostConfigEditor.ParseAutosave(" 1440 "));
        foreach (var bad in new[] { "abc", "1441", "-1", "12.5" })
        {
            var error = Assert.Throws<InvalidOperationException>(() => HostConfigEditor.ParseAutosave(bad));
            Assert.Equal(HostConfigEditor.AutosaveRange, error.Message);
        }
    }

    [Fact]
    public void Gameplay_settings_keep_the_file_value_for_an_empty_box()
    {
        var values = GameplaySettingsEditor.Definitions.ToDictionary(
            item => item.Key,
            item => item.Key switch
            {
                "wotr.phase1Day" => "30",
                "wotr.phase2Day" => "44",
                "momentum.victoryThreshold" => "750",
                _ => item.Kind == "bool" ? "true" : item.Min.ToString(CultureInfo.InvariantCulture)
            });
        var taom = string.Join(",", values.Select(pair => "\"" + pair.Key + "\":" + pair.Value));
        var json = "{\"schemaVersion\":1,\"battleSize\":{\"field\":900,\"siege\":800,\"sallyOut\":700},\"taom\":{" + taom + "}}";

        // An empty box used to stop Save settings with "must be a number", so nothing after it saved.
        var boxes = new Dictionary<string, string>(values) { ["momentum.victoryThreshold"] = "", ["wotr.phase1Day"] = "31" };
        var updated = GameplaySettingsEditor.Apply(json, boxes, "", "1000", " ");
        using var document = JsonDocument.Parse(updated);
        var battle = document.RootElement.GetProperty("battleSize");
        Assert.Equal(900, battle.GetProperty("field").GetInt32());
        Assert.Equal(1000, battle.GetProperty("siege").GetInt32());
        Assert.Equal(700, battle.GetProperty("sallyOut").GetInt32());
        var saved = document.RootElement.GetProperty("taom");
        Assert.Equal(750, saved.GetProperty("momentum.victoryThreshold").GetInt32());
        Assert.Equal(31, saved.GetProperty("wotr.phase1Day").GetInt32());
        Assert.Throws<InvalidOperationException>(() => GameplaySettingsEditor.Apply(json, boxes, "abc", "1000", ""));
    }

    [Fact]
    public void Client_join_keeps_other_options_and_sets_address_and_battle_size()
    {
        var options = """
            {
              "CrashReporting": { "Consent": { "automaticCrashReports": false } },
              "Connection": { "DirectConnection": { "lastServerAddress": "old" } }
            }
            """;
        var updated = ClientJoin.SetDirectAddress(options, "127.0.0.1");
        using var document = JsonDocument.Parse(updated);
        Assert.False(document.RootElement.GetProperty("CrashReporting").GetProperty("Consent").GetProperty("automaticCrashReports").GetBoolean());
        Assert.Equal("127.0.0.1", document.RootElement.GetProperty("Connection").GetProperty("DirectConnection").GetProperty("lastServerAddress").GetString());

        var config = "Language=English\r\nBattleSize=4\r\nUIScale=1\r\n";
        var sized = ClientJoin.ApplyBattleSize(config, 600);
        Assert.Contains("BattleSize=600", sized);
        Assert.Contains("UIScale=1", sized);
        Assert.Equal(600, ClientJoin.ReadBattleSize(sized));
        Assert.Throws<InvalidOperationException>(() => ClientJoin.ApplyBattleSize(config, 10));
        var lowTerrain = "texture_quality = 2\r\nterrain_quality = 0\r\nwater_quality = 2\r\n";
        var raised = ClientJoin.RaiseLowTerrain(lowTerrain);
        Assert.NotNull(raised);
        Assert.Contains("terrain_quality = 1", raised);
        Assert.Contains("texture_quality = 2", raised);
        Assert.Contains("water_quality = 2", raised);
        Assert.Null(ClientJoin.RaiseLowTerrain(raised));
        Assert.Throws<InvalidOperationException>(() => ClientJoin.SetDirectAddress(options, "Barneykoo"));
    }

    [Fact]
    public void A_saved_name_is_removed_before_a_steam_join_and_a_missing_answer_points_at_the_home_network()
    {
        var options = """
            {
              "CrashReporting": { "Consent": { "automaticCrashReports": false } },
              "Connection": { "DirectConnection": { "lastServerAddress": "Barneykoo" } }
            }
            """;
        var updated = ClientJoin.RemoveNonIpAddress(options);
        Assert.NotNull(updated);
        using var document = JsonDocument.Parse(updated);
        Assert.False(document.RootElement.GetProperty("CrashReporting").GetProperty("Consent").GetProperty("automaticCrashReports").GetBoolean());
        Assert.False(document.RootElement.GetProperty("Connection").GetProperty("DirectConnection").TryGetProperty("lastServerAddress", out _));
        Assert.Null(ClientJoin.RemoveNonIpAddress(ClientJoin.SetDirectAddress(options, "192.168.1.135")));

        var reply = ClientJoin.DescribeProbe(
            new ServerProbeResult(ServerReachability.NoReply, "No answer to the connection handshake."),
            "192.168.1.135");
        Assert.Contains("No Coop server answered", reply);
        Assert.Contains("127.0.0.1", reply);
        Assert.Contains("same home network", reply);
        Assert.Contains("port forwarded", reply);
        Assert.DoesNotContain("may be stopped", reply);

        var publicReply = ClientJoin.DescribeProbe(
            new ServerProbeResult(ServerReachability.NoReply, "No answer to the connection handshake."),
            "8.8.8.8");
        Assert.Contains("custom public address", publicReply);
        Assert.Contains("port forwarded", publicReply);
        Assert.DoesNotContain("same home network", publicReply);

        var cleared = ClientJoin.RemoveDirectAddress(ClientJoin.SetDirectAddress(options, "192.168.1.135"));
        Assert.NotNull(cleared);
        using var clearedDocument = JsonDocument.Parse(cleared);
        Assert.False(clearedDocument.RootElement.GetProperty("Connection").GetProperty("DirectConnection").TryGetProperty("lastServerAddress", out _));
        Assert.False(clearedDocument.RootElement.GetProperty("CrashReporting").GetProperty("Consent").GetProperty("automaticCrashReports").GetBoolean());
    }

    [Fact]
    public void Server_start_copies_the_private_dotnet_desktop_runtime_and_leaves_the_wrong_versions()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-framework-" + Guid.NewGuid().ToString("N"));
        var global = Path.Combine(root, "global");
        var server = Path.Combine(root, "server");
        try
        {
            WriteFramework(global, "Microsoft.WindowsDesktop.App", "6.0.16", "too-old");
            WriteFramework(global, "Microsoft.WindowsDesktop.App", "6.0.35", "desktop");
            WriteFramework(global, "Microsoft.WindowsDesktop.App", "8.0.21", "too-new");
            WriteFramework(global, "Microsoft.NETCore.App", "6.0.36", "core");
            Directory.CreateDirectory(Path.Combine(server, "engine", "dotnet", "shared"));
            var configDir = Path.Combine(server, "engine", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "TaleWorlds.Starter.DotNetCore.runtimeconfig.json"), """
                {
                  "runtimeOptions": {
                    "rollForward": "LatestPatch",
                    "frameworks": [
                      { "name": "Microsoft.NETCore.App", "version": "6.0.0" },
                      { "name": "Microsoft.WindowsDesktop.App", "version": "6.0.35" }
                    ]
                  }
                }
                """);

            var copied = ServerFramework.Ensure(server, global);
            Assert.True(copied.Ready);
            Assert.Contains("Microsoft.WindowsDesktop.App 6.0.35", copied.Detail);
            Assert.Contains("Microsoft.NETCore.App 6.0.36", copied.Detail);
            Assert.Equal("desktop", File.ReadAllText(Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.WindowsDesktop.App", "6.0.35", "marker.txt")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.WindowsDesktop.App", "8.0.21")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.WindowsDesktop.App", "6.0.16")));

            var again = ServerFramework.Ensure(server, global);
            Assert.True(again.Ready);
            Assert.Equal("", again.Detail);

            var bare = Path.Combine(root, "bare");
            Directory.CreateDirectory(Path.Combine(bare, "engine", "dotnet"));
            Directory.CreateDirectory(Path.Combine(bare, "engine", "bin", "Win64_Shipping_Server"));
            File.Copy(
                Path.Combine(configDir, "TaleWorlds.Starter.DotNetCore.runtimeconfig.json"),
                Path.Combine(bare, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.runtimeconfig.json"));
            var missing = ServerFramework.Ensure(bare, Path.Combine(root, "empty-global"));
            Assert.False(missing.Ready);
            Assert.Contains("Microsoft.WindowsDesktop.App 6.0.35", missing.Detail);
            Assert.Contains(".NET 6 Desktop Runtime", missing.Detail);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Influence_lookup_uses_the_hero_clan_id_not_the_player_name()
    {
        var heroId = ClanInfluenceLookup.HeroRegistryId(
            ["ID: 'hero_9', Name: 'Tandiez', Game StringId: tandiez"],
            "Tandiez");
        var clanName = ClanInfluenceLookup.ClanNameFromHeroInfo(
            ["_originClan = (4) Other", "_clan = (88) Snot Pickers"]);
        var clanId = ClanInfluenceLookup.ClanIdForName(
            ["ID: 'Player' Name: 'awdawd'", "ID: 'Player23' Name: 'Snot Pickers'"],
            clanName!);

        Assert.Equal("hero_9", heroId);
        Assert.Equal("Snot Pickers", clanName);
        Assert.Equal("Player23", clanId);
        Assert.Null(ClanInfluenceLookup.HeroRegistryId(
            ["ID: 'a', Name: 'Tandiez', Game StringId: one", "ID: 'b', Name: 'Tandiez', Game StringId: two"],
            "Tandiez"));
    }

    [Fact]
    public void Taom_number_writes_repair_a_copy_of_the_live_files_and_leave_the_originals_alone()
    {
        var documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "CoopData");
        var serverPath = Path.Combine(documents, "DedicatedServer", "server-config.json");
        var modPath = Path.Combine(documents, "mod-config.json");
        if (!File.Exists(serverPath) || !File.Exists(modPath))
            return;

        var serverBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(serverPath)));
        var modBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modPath)));
        var serverStamp = File.GetLastWriteTimeUtc(serverPath);
        var modStamp = File.GetLastWriteTimeUtc(modPath);

        var server = File.ReadAllText(serverPath);
        var repaired = JsoncValues.Repair(server);
        Assert.Equal(repaired, JsoncValues.Repair(repaired));
        Assert.Contains("\"autosaveMinutes\":", repaired);
        Assert.Contains("\"autoRestartHours\":", repaired);
        var saveName = ReadJsonString(repaired, "saveName");
        var password = ReadJsonString(repaired, "password");
        // Only a name Apply takes: a live test can leave one it refuses on purpose, like MoreNationsSP.last-good.
        if (!string.IsNullOrWhiteSpace(saveName)
            && !saveName.Equals("default_new_game", StringComparison.OrdinalIgnoreCase)
            && saveName == Path.GetFileName(saveName)
            && Path.GetExtension(saveName).Length == 0
            && saveName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            var written = HostConfigEditor.Apply(repaired, saveName, password, 5, false, true);
            Assert.Contains("\"autosaveMinutes\": 5", written);
            Assert.Contains("\"password\": \"" + password.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"", written);
            Assert.DoesNotMatch(DollarNumber, written);
            Assert.Equal(written, HostConfigEditor.Apply(written, saveName, password, 5, false, true));
        }

        var mod = File.ReadAllText(modPath);
        var repairedMod = CoopModConfig.Repair(mod);
        Assert.Equal(repairedMod, CoopModConfig.Repair(repairedMod));
        Assert.DoesNotMatch(DollarNumber, repairedMod);

        foreach (var extra in ExtraServerConfigs())
        {
            var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(extra)));
            var stamp = File.GetLastWriteTimeUtc(extra);
            var text = File.ReadAllText(extra);
            var fixedText = JsoncValues.Repair(text);
            Assert.Equal(fixedText, JsoncValues.Repair(fixedText));
            Assert.DoesNotMatch(DollarNumber, fixedText);
            Assert.Equal(before, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(extra))));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(extra));
        }

        var host = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        var start = SourceFiles.Method(host, "private async Task StartServerCoreAsync");
        var body = start[..start.IndexOf("var framework = ServerFramework.Ensure", StringComparison.Ordinal)];
        var repairMod = body.IndexOf("RepairModConfig();", StringComparison.Ordinal);
        var repairServer = body.IndexOf("RepairServerConfig();", StringComparison.Ordinal);
        var write = body.IndexOf("WriteConfig(", StringComparison.Ordinal);
        Assert.True(repairMod >= 0 && repairMod < repairServer && repairServer < write);

        Assert.Equal(serverBefore, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(serverPath))));
        Assert.Equal(modBefore, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modPath))));
        Assert.Equal(serverStamp, File.GetLastWriteTimeUtc(serverPath));
        Assert.Equal(modStamp, File.GetLastWriteTimeUtc(modPath));
    }

    private static readonly System.Text.RegularExpressions.Regex DollarNumber = new(@"\$1\d", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    private static string ReadJsonString(string json, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        return match.Success ? System.Text.Json.JsonSerializer.Deserialize<string>("\"" + match.Groups[1].Value + "\"") ?? "" : "";
    }

    private static IEnumerable<string> ExtraServerConfigs()
    {
        foreach (var root in DriveRoots())
        {
            var path = Path.Combine(root, "TAOM-Coop-Host", "Data", "server-config.json");
            if (File.Exists(path))
                yield return path;
        }
    }

    private static IEnumerable<string> DriveRoots()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                yield return drive.RootDirectory.FullName;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static void WriteFramework(string global, string name, string version, string marker)
    {
        var directory = Path.Combine(global, name, version);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "marker.txt"), marker);
    }
}
