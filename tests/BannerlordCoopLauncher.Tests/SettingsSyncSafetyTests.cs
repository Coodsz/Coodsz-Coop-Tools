using CoodszHost.Services;
using CoodszSettingsSync;
using SettingsSyncFakes;

namespace BannerlordCoopLauncher.Tests
{
    /// <summary>The save guard, the private text rule, the request gate and the other safety checks of settings sync.</summary>
    public class SettingsSyncSafetyTests
    {
        private static SettingsSnapshotData Host(params string[] lines) => new("Marry", SettingsScope.Global, false, lines.ToList());

        private static (FakeClient Client, McmSettings Mcm, SyncSession Session) Synced(params string[] lines)
        {
            var client = FakeMcm.Client();
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();
            SettingsApplier.Apply(mcm, Host(lines), session, 1);
            return (client, mcm, session);
        }

        [Fact]
        public void A_save_writes_the_players_values_and_keeps_their_own_edits()
        {
            var (client, _, session) = Synced("b\tPolygamy\t1", "i\tMaxWives\t3");
            Assert.True(client.Marry.Polygamy);
            // The player changes a setting the host does not send, then presses Done in MCM.
            client.Marry.Chance = 0.9f;

            var swap = session.BeginSave(client.Marry, out var block);
            Assert.False(block);
            Assert.False(client.Marry.Polygamy);
            Assert.Equal(1, client.Marry.MaxWives);
            Assert.Equal(0.9f, client.Marry.Chance);
            session.EndSave(swap);

            Assert.True(client.Marry.Polygamy);
            Assert.Equal(3, client.Marry.MaxWives);
            Assert.Equal(0.9f, client.Marry.Chance);

            session.Restore();
            Assert.False(client.Marry.Polygamy);
            Assert.Equal(1, client.Marry.MaxWives);
            // The player's own edit survives the end of the session.
            Assert.Equal(0.9f, client.Marry.Chance);
        }

        [Fact]
        public void A_synced_setting_the_player_changed_is_saved_as_theirs()
        {
            var (client, _, session) = Synced("i\tMaxWives\t3");
            client.Marry.MaxWives = 2;

            var swap = session.BeginSave(client.Marry, out _);
            Assert.Equal(2, client.Marry.MaxWives);
            session.EndSave(swap);
            Assert.Equal(3, client.Marry.MaxWives);

            session.Restore();
            Assert.Equal(2, client.Marry.MaxWives);
        }

        [Fact]
        public void Nested_saves_swap_once()
        {
            var (client, _, session) = Synced("b\tPolygamy\t1");
            var outer = session.BeginSave(client.Marry, out _);
            var inner = session.BeginSave(client.Marry, out _);
            Assert.False(client.Marry.Polygamy);
            session.EndSave(inner);
            Assert.False(client.Marry.Polygamy);
            session.EndSave(outer);
            Assert.True(client.Marry.Polygamy);
        }

        [Fact]
        public void A_copy_of_a_synced_object_is_not_saved()
        {
            var (_, _, session) = Synced("b\tPolygamy\t1");
            var swap = session.BeginSave(new MarrySettings(), out var block);
            Assert.True(block);
            session.EndSave(swap);
            // An object that is not synced saves as usual.
            session.EndSave(session.BeginSave(new FakeGlobal(), out var other));
            Assert.False(other);
        }

        [Fact]
        public void A_value_that_cannot_be_put_back_stays_guarded()
        {
            var (client, _, session) = Synced("b\tPolygamy\t1", "i\tMaxWives\t3");
            client.Marry.FailWrites = true;
            session.Restore();
            Assert.False(client.Marry.Polygamy);
            Assert.True(session.IsSynced("Marry"));

            client.Marry.FailWrites = false;
            session.Restore();
            Assert.Equal(1, client.Marry.MaxWives);
            Assert.False(session.IsSynced("Marry"));
        }

        [Theory]
        [InlineData("ApiToken", "abc", true)]
        [InlineData("WebhookAddress", "abc", true)]
        [InlineData("SavePath", "abc", true)]
        [InlineData("ExportFolder", "x", true)]
        [InlineData("LogFile", "x", true)]
        [InlineData("Title", "https://example.com", true)]
        [InlineData("Title", "C:\\Games", true)]
        [InlineData("Title", "/home/me", true)]
        [InlineData("Title", "\\\\share\\x", true)]
        [InlineData("Title", "Lord", false)]
        [InlineData("Profile", "Dir: north", false)]
        public void Private_text_is_never_shared(string name, string value, bool privateText) =>
            Assert.Equal(privateText, SettingsWire.IsPrivateText(value, name));

        [Fact]
        public void Private_text_is_left_out_of_the_capture_and_refused_by_the_player()
        {
            var host = FakeMcm.Host();
            ((MarrySettings)host.Settings["Marry"]).Title = "C:\\Users\\Host";
            var snapshot = SettingsCapture.Capture(new McmSettings(host), new KeepOwnList()).First(s => s.SettingsId == "Marry");
            Assert.DoesNotContain(snapshot.Values, v => v.StartsWith("s\tTitle", StringComparison.Ordinal));

            var client = FakeMcm.Client();
            var report = SettingsApplier.Apply(new McmSettings(client.Provider), Host("s\tTitle\thttps://evil.example/x"), new SyncSession(), 1);
            Assert.Equal("Sir", client.Marry.Title);
            Assert.Equal(1, report.Reasons["own"]);
        }

        [Fact]
        public void One_request_per_player_every_ten_seconds()
        {
            var gate = new RequestGate<int>(id => id);
            var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(gate.TryQueue(1, now));
            Assert.False(gate.TryQueue(1, now.AddSeconds(1)));
            Assert.True(gate.TryQueue(2, now));
            Assert.Equal(new[] { 1, 2 }, gate.TakeAll());
            Assert.False(gate.TryQueue(1, now.AddSeconds(9)));
            Assert.True(gate.TryQueue(1, now.AddSeconds(10)));
        }

        // v1.3.7 review: the empty answer flag was set after TryQueue, so the game tick's TakeAll could take the
        // request first and the player waited the 30 seconds the empty answer is there to skip.
        [Fact]
        public void A_queued_request_is_noted_before_the_tick_can_take_it()
        {
            var gate = new RequestGate<int>(id => id);
            var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            var wantsNone = false;
            Task<(List<int> Taken, bool Noted)>? tick = null;
            Assert.True(gate.TryQueue(1, now, () =>
            {
                // The tick runs while the request is being noted: it waits for the note.
                tick = Task.Run(() =>
                {
                    var taken = gate.TakeAll();
                    return (taken, Volatile.Read(ref wantsNone));
                });
                Assert.False(tick.Wait(200));
                Volatile.Write(ref wantsNone, true);
            }));
            var result = tick!.GetAwaiter().GetResult();
            Assert.Equal(new[] { 1 }, result.Taken);
            Assert.True(result.Noted);
            // A dropped request notes nothing.
            var called = false;
            Assert.False(gate.TryQueue(1, now.AddSeconds(1), () => called = true));
            Assert.False(called);
            var sync = File.ReadAllText(Path.Combine(SourceFiles.Root(), "compat", "CoodszSettingsSync", "src", "Server", "ServerSync.cs"));
            Assert.Contains("Asked.TryQueue(peer, DateTime.UtcNow, () => WantsNone[peer.Id] = wantsNoSettingsAnswer);", sync);
        }

        [Fact]
        public void The_request_queue_is_capped()
        {
            var gate = new RequestGate<int>(id => id);
            var now = DateTime.UtcNow;
            for (var i = 0; i < RequestGate<int>.MaxQueued; i++)
                Assert.True(gate.TryQueue(i, now));
            Assert.False(gate.TryQueue(9999, now));
            Assert.Equal(RequestGate<int>.MaxQueued, gate.Queued);
        }

        [Fact]
        public void A_failing_tick_is_logged_and_the_sync_stops_only_after_five_in_a_row()
        {
            var lines = new List<string>();
            var loader = new SettingsSyncLoader(false, Path.GetTempPath(), Path.GetTempPath(), lines.Add);
            var fail = true;
            loader.UseTick(_ => { if (fail) throw new InvalidOperationException("boom"); });
            for (var i = 0; i < 4; i++) loader.Tick(0.1f);
            Assert.True(loader.Running);
            fail = false;
            loader.Tick(0.1f);
            fail = true;
            for (var i = 0; i < 4; i++) loader.Tick(0.1f);
            Assert.True(loader.Running);
            loader.Tick(0.1f);
            Assert.False(loader.Running);
            Assert.StartsWith("Settings sync stopped after 5", lines.Last());
        }

        /// <summary>
        /// Coop builds every handler once, when the server starts hosting, and a slow Realm of Thrones server hosted
        /// before the module screen callbacks came, so the sync loaded too late and never answered (run 38). The loader
        /// is made on the first tick, once, before it ticks, and the server half says so when no handler came.
        /// </summary>
        [Fact]
        public void The_server_tools_make_the_sync_on_the_first_tick_and_say_when_coop_never_built_its_handler()
        {
            var module = SourceFiles.Read("compat/CoodszServerTools/src/ToolsSubModule.cs").Replace("\r\n", "\n");
            var tick = SourceFiles.Method(module, "protected override void OnApplicationTick(float dt)");
            var make = tick.IndexOf("if (_settingsSync == null && !_settingsSyncTried && GameNetwork.IsDedicatedServer)", StringComparison.Ordinal);
            var once = tick.IndexOf("_settingsSyncTried = true;", StringComparison.Ordinal);
            var safely = tick.IndexOf("SettingsSyncSafely();", StringComparison.Ordinal);
            var ticks = tick.IndexOf("_settingsSync?.Tick(dt);", StringComparison.Ordinal);
            Assert.True(make > 0 && once > make && safely > once && ticks > safely, tick);
            Assert.Contains("private static bool _settingsSyncTried;", module);

            var sync = SourceFiles.Read("compat/CoodszSettingsSync/src/Server/ServerSync.cs").Replace("\r\n", "\n");
            Assert.Contains("private const float HandlerWaitSeconds = 60f;", sync);
            var attach = SourceFiles.Method(sync, "internal static void Attach(INetwork network)");
            Assert.Contains("_handlerBuilt = true;", attach);
            var serverTick = SourceFiles.Method(sync, "public static void Tick(float dt)");
            // The wait counts only once Coop hosts (IsServer is set just before its container is built), and a late
            // handler takes the line back once.
            var check = serverTick.IndexOf("if (!_handlerBuilt && !_noHandlerNoted && global::Common.ModInformation.IsServer && (_sinceStart += dt) >= HandlerWaitSeconds)", StringComparison.Ordinal);
            Assert.True(check > serverTick.IndexOf("if (!_active) return;", StringComparison.Ordinal), serverTick);
            Assert.Contains("_noHandlerNoted = true;", serverTick);
            Assert.Contains("_log(\"Settings sync: Coop never built its handler, so players will not get the host's settings. Restart the server.\");", serverTick);
            var late = serverTick.IndexOf("else if (_handlerBuilt && _noHandlerNoted && !_handlerLateNoted)", StringComparison.Ordinal);
            Assert.True(late > check, serverTick);
            Assert.Contains("_handlerLateNoted = true;", serverTick);
            Assert.Contains("_log(\"Settings sync: Coop built its handler after all; players get the host's settings now.\");", serverTick);
        }

        [Fact]
        public void The_server_probe_needs_the_module_check_answer()
        {
            var result = CoopProbe.Check(false, (_, name) => name == "Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated" ? null : typeof(object));
            Assert.Contains("NetworkModuleVersionsValidated", result.Missing);
        }

        [Fact]
        public void A_broken_lock_list_keeps_the_last_good_one()
        {
            var folder = Temp();
            try
            {
                var path = Path.Combine(folder, KeepOwnList.FileName);
                Assert.Equal(0, SettingsFiles.TryReadKeepOwn(path)!.Count);
                File.WriteAllText(path, "{\"PlayersOwnSettings\": [\"Marry\"");
                Assert.Null(SettingsFiles.TryReadKeepOwn(path));
                File.WriteAllText(path, "{\"PlayersOwnSettings\": \"Marry\"}");
                Assert.Null(SettingsFiles.TryReadKeepOwn(path));
                File.WriteAllText(path, "{\"SchemaVersion\": 1}");
                Assert.Equal(0, SettingsFiles.TryReadKeepOwn(path)!.Count);
                File.WriteAllText(path, "{\"PlayersOwnSettings\": [\"Marry\"]}");
                Assert.True(SettingsFiles.TryReadKeepOwn(path)!.Keeps("Marry"));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void The_lock_list_is_written_without_a_half_written_moment()
        {
            var folder = Temp();
            try
            {
                Assert.True(McmKeepOwn.Write(folder, new[] { "Marry" }));
                Assert.True(McmKeepOwn.Write(folder, new[] { "Marry", "Diplomacy" }));
                Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
                Assert.Equal(new[] { "Marry", "Diplomacy" }, McmKeepOwn.Read(Path.Combine(folder, McmKeepOwn.FileName)));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Locking_removes_every_key_the_mod_is_listed_under()
        {
            var profile = new HostProfile { PlayersOwnSettings = { "MarryAnyone", "Marry", "MarryFolder", "Other" } };
            Assert.True(McmKeepOwn.Lock(profile.PlayersOwnSettings, "MarryAnyone", "MarryAnyone", "marry", "MarryFolder"));
            Assert.Equal(new[] { "Other" }, profile.PlayersOwnSettings);
            Assert.False(McmKeepOwn.Lock(profile.PlayersOwnSettings, "MarryAnyone", null));
        }

        [Fact]
        public void Definitions_are_read_once_per_pass()
        {
            var client = FakeMcm.Client();
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();
            SettingsApplier.Apply(mcm, Host("b\tPolygamy\t1"), session, 1);
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t2"), session, 1);
            SettingsCapture.Capture(mcm, new KeepOwnList());
            Assert.Equal(1, client.Provider.Reads);
            mcm.Refresh();
            mcm.Definition("Marry");
            Assert.Equal(2, client.Provider.Reads);
        }

        [Fact]
        public void A_host_edit_answer_names_the_skipped_values()
        {
            var client = FakeMcm.Client();
            var ack = LiveChannel.Apply(new McmSettings(client.Provider), "Marry", new Dictionary<string, string> { ["MaxWives"] = "2", ["Orientation"] = "Nope" });
            Assert.Equal(new List<string> { "Orientation" }, ack["Skipped"]);

            var shown = new LiveApplyAck { Ok = true, Skipped = new List<string> { "Orientation" } };
            Assert.Equal("Some values were skipped: Orientation", shown.Describe());
            Assert.Equal("Applied", new LiveApplyAck { Ok = true }.Describe());
            Assert.Equal("Not applied: not installed on the server", new LiveApplyAck { Ok = false, Report = "not installed on the server" }.Describe());
        }

        [Fact]
        public void A_broken_overrides_file_is_tried_again_when_it_changes()
        {
            var dir = Temp();
            try
            {
                var path = Path.Combine(dir, LiveChannel.OverridesFileName);
                File.WriteAllText(path, "{ \"Objects\": { \"Marry\": ");
                var client = FakeMcm.Client();
                var mcm = new McmSettings(client.Provider);
                var channel = new LiveChannel(dir, _ => { });
                channel.Poll(mcm, new KeepOwnList(), 1);
                var ack = ModSettingsLive.ReadOverridesAck(dir);
                Assert.NotNull(ack);
                Assert.False(ack!.Ok);

                File.WriteAllText(path, "{ \"Objects\": { \"Marry\": { \"MaxWives\": \"2\" } } }");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
                channel.Poll(mcm, new KeepOwnList(), 1);
                Assert.Equal(2, client.Marry.MaxWives);
                ack = ModSettingsLive.ReadOverridesAck(dir);
                Assert.True(ack!.Ok);
                Assert.Equal("saved values set", ack.StateOf("Marry"));
                Assert.Null(ack.StateOf("Other"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Overrides_waiting_for_their_mod_show_as_waiting()
        {
            var ack = new OverridesAck { Pending = { "Later" }, Applied = { new LiveApplyAck { Ok = true, SettingsId = "Marry", Skipped = new List<string> { "X" } } } };
            Assert.Equal("saved values waiting for the mod", ack.StateOf("Later"));
            Assert.Equal("some saved values were skipped", ack.StateOf("Marry"));
        }

        [Fact]
        public void The_hosts_values_win_while_joined_and_the_players_change_comes_back_after()
        {
            // Settings Lock used to fix these values; now the host's values hold while the player is on the server.
            var (client, _, session) = Synced("b\tPolygamy\t1", "i\tMaxWives\t3");
            client.Marry.MaxWives = 2;
            client.Marry.Chance = 0.9f;

            Assert.Equal(new[] { "Marry" }, session.HoldHostValues());
            Assert.Equal(3, client.Marry.MaxWives);
            Assert.True(client.Marry.Polygamy);
            // A setting the host does not send stays the player's.
            Assert.Equal(0.9f, client.Marry.Chance);
            Assert.Empty(session.HoldHostValues());

            // A save still writes the player's change, and the end of the session puts it back.
            var swap = session.BeginSave(client.Marry, out _);
            Assert.Equal(2, client.Marry.MaxWives);
            session.EndSave(swap);
            Assert.Equal(3, client.Marry.MaxWives);
            session.Restore();
            Assert.Equal(2, client.Marry.MaxWives);
            Assert.False(client.Marry.Polygamy);
        }

        [Fact]
        public void Holding_the_hosts_values_waits_for_a_save_to_finish()
        {
            var (client, _, session) = Synced("i\tMaxWives\t3");
            client.Marry.MaxWives = 2;
            var swap = session.BeginSave(client.Marry, out _);
            Assert.Empty(session.HoldHostValues());
            session.EndSave(swap);
            Assert.Equal(3, client.Marry.MaxWives);
        }

        [Fact]
        public void A_double_setting_holds_the_host_value_without_losing_the_players_own()
        {
            // The wire carries Float as a float; a double setting must hold a double, or every check would see a
            // change, overwrite the player's own value with the host's, and lose it when the session ends.
            var (provider, rates) = FakeMcm.RatesClient();
            var mcm = new McmSettings(provider);
            var session = new SyncSession();
            var report = SettingsApplier.Apply(mcm, new SettingsSnapshotData("Rates", SettingsScope.Global, false, new List<string> { "f\tRate\t0.1", "f\tSpare\t0.5" }), session, 1);
            Assert.Equal(2, report.Set);
            Assert.Equal(0.1, rates.Rate);
            Assert.Equal(0.5, rates.Spare);

            // Nothing changed, so nothing is put back and no "keeps the host's values" line is due.
            Assert.Empty(session.HoldHostValues());
            Assert.Empty(session.HoldHostValues());
            Assert.Equal(0.1, rates.Rate);

            // The player changes it: the host's value comes back, and the player's change is kept for after.
            rates.Rate = 0.7;
            Assert.Equal(new[] { "Rates" }, session.HoldHostValues());
            Assert.Equal(0.1, rates.Rate);
            Assert.Empty(session.HoldHostValues());

            var swap = session.BeginSave(rates, out var block);
            Assert.False(block);
            Assert.Equal(0.7, rates.Rate);
            session.EndSave(swap);
            Assert.Equal(0.1, rates.Rate);

            session.Restore();
            Assert.Equal(0.7, rates.Rate);
        }

        [Fact]
        public void A_double_setting_the_player_never_touched_gets_its_own_value_back()
        {
            var (provider, rates) = FakeMcm.RatesClient();
            var session = new SyncSession();
            SettingsApplier.Apply(new McmSettings(provider), new SettingsSnapshotData("Rates", SettingsScope.Global, false, new List<string> { "f\tRate\t0.35" }), session, 1);
            Assert.Equal(0.35, rates.Rate);
            Assert.Empty(session.HoldHostValues());
            session.Restore();
            Assert.Equal(0.2, rates.Rate);
        }

        [Fact]
        public void Numbers_compare_by_value_and_whole_numbers_exactly()
        {
            Assert.True(SyncValues.Same(0.1, 0.1f));
            Assert.True(SyncValues.Same(3, 3.0f));
            Assert.False(SyncValues.Same(0.1, 0.2f));
            Assert.False(SyncValues.Same(100000000, 100000001));
            Assert.False(SyncValues.Same(true, 1));
            Assert.Equal(0.1, SyncValues.AsTypeOf(typeof(double), 0.1f, "0.1"));
            Assert.IsType<float>(SyncValues.AsTypeOf(typeof(float), 0.25, "0.25"));
        }

        [Fact]
        public void A_setting_that_already_had_the_hosts_value_is_held_too()
        {
            // MaxWives is 1 on both sides at join, so nothing is written, but the host still wins while joined.
            var (client, _, session) = Synced("i\tMaxWives\t1");
            Assert.Equal(1, client.Marry.MaxWives);
            client.Marry.MaxWives = 2;
            Assert.Equal(new[] { "Marry" }, session.HoldHostValues());
            Assert.Equal(1, client.Marry.MaxWives);
            session.Restore();
            Assert.Equal(2, client.Marry.MaxWives);
        }

        [Fact]
        public void A_global_setting_applied_before_and_after_the_campaign_loads_has_one_host_value()
        {
            // Before the save loads (generation 0), then again after it with the host's newer value (generation 1).
            var client = FakeMcm.Client();
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t3"), session, 0);
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t2"), session, 1);
            Assert.Equal(2, client.Marry.MaxWives);
            // One entry, so no hold pass writes the old host value back and logs it as a player edit.
            Assert.Empty(session.HoldHostValues());
            Assert.Equal(2, client.Marry.MaxWives);
            session.Restore();
            Assert.Equal(1, client.Marry.MaxWives);
        }

        [Fact]
        public void A_players_edit_before_the_campaign_loads_survives_the_second_apply()
        {
            // Codex's case: own value 1, host 3 before the save loads, the player edits 2, the campaign loads
            // (a new generation) and everything is applied again. Leaving must give back 2, not 1.
            var client = FakeMcm.Client();
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t3"), session, 0);
            client.Marry.MaxWives = 2;
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t3"), session, 1);
            Assert.Equal(3, client.Marry.MaxWives);
            Assert.Empty(session.HoldHostValues());
            session.Restore();
            Assert.Equal(2, client.Marry.MaxWives);
        }

        [Fact]
        public void A_setting_left_from_a_failed_restore_is_not_held_in_the_next_session()
        {
            var (client, _, session) = Synced("i\tMaxWives\t3");
            client.Marry.FailWrites = true;
            session.Restore();
            Assert.True(session.IsSynced("Marry"));

            // The next session, on a server that sends no settings: the player's change stays theirs.
            client.Marry.FailWrites = false;
            session.BeginSession();
            client.Marry.MaxWives = 2;
            Assert.Empty(session.HoldHostValues());
            Assert.Equal(2, client.Marry.MaxWives);
        }

        [Fact]
        public void A_setting_whose_own_value_was_null_gets_null_back()
        {
            // The player's own Title is null; the host's goes in. Leaving, and every save, must give back null,
            // not keep the host's value in memory or write it into the player's file.
            var client = FakeMcm.Client();
            client.Marry.Title = null!;
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();
            SettingsApplier.Apply(mcm, Host("s\tTitle\tLord"), session, 1);
            Assert.Equal("Lord", client.Marry.Title);

            var swap = session.BeginSave(client.Marry, out var block);
            Assert.False(block);
            Assert.Null(client.Marry.Title);
            session.EndSave(swap);
            Assert.Equal("Lord", client.Marry.Title);

            session.Restore();
            Assert.Null(client.Marry.Title);
        }

        [Fact]
        public void A_save_set_off_by_writing_the_host_value_saves_the_players_own()
        {
            // A setter that saves right away (as some mods do on change): that save must already see the host value.
            var marry = new MarrySettings();
            var session = new SyncSession();
            object? saved = null;
            var fired = false;
            var group = new FakeGroup();
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "MaxWives", DisplayName = "MaxWives", SettingType = FakeSettingType.Int, MinValue = 1, MaxValue = 5,
                PropertyReference = new FakeRef(typeof(int), () => marry.MaxWives, v =>
                {
                    marry.MaxWives = (int)v!;
                    if (fired) return;
                    fired = true;
                    var swap = session.BeginSave(marry, out _);
                    saved = marry.MaxWives;
                    session.EndSave(swap);
                })
            });
            var definition = new FakeDefinition { SettingsId = "Marry", DisplayName = "Marry" };
            definition.SettingPropertyGroups.Add(group);
            var provider = new FakeProvider();
            provider.Settings["Marry"] = marry;
            provider.Definitions.Add(definition);

            SettingsApplier.Apply(new McmSettings(provider), Host("i\tMaxWives\t3"), session, 1);

            Assert.Equal(1, saved);
            Assert.Equal(3, marry.MaxWives);
        }

        [Fact]
        public void The_servers_live_edit_keeps_a_double_setting_exact()
        {
            // The host's live edit on the server: a double setting gets the exact double, not the wire's float.
            var (provider, rates) = FakeMcm.RatesClient();
            var mcm = new McmSettings(provider);
            var rate = mcm.PropertiesOf("Rates").Single(property => property.Id == "Rate");
            Assert.True(LiveChannel.TryApplyText(rate, "0.1"));
            Assert.Equal(0.1, rates.Rate);
        }

        // Marry with MaxWives and Polygamy whose setters can store the value and then throw (a change handler that
        // fails), or set off a save right after storing.
        private static (FakeProvider Provider, MarrySettings Marry) Handlers(Func<bool> throwAfterStore, Action<MarrySettings>? afterPolygamy = null)
        {
            var marry = new MarrySettings();
            var group = new FakeGroup();
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "MaxWives", DisplayName = "MaxWives", SettingType = FakeSettingType.Int, MinValue = 1, MaxValue = 5,
                PropertyReference = new FakeRef(typeof(int), () => marry.MaxWives, v =>
                {
                    if (marry.FailWrites) throw new InvalidOperationException("read only now");
                    marry.MaxWives = (int)v!;
                    if (throwAfterStore()) throw new InvalidOperationException("change handler failed");
                })
            });
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "Polygamy", DisplayName = "Polygamy", SettingType = FakeSettingType.Bool,
                PropertyReference = new FakeRef(typeof(bool), () => marry.Polygamy, v =>
                {
                    marry.Polygamy = (bool)v!;
                    afterPolygamy?.Invoke(marry);
                })
            });
            var definition = new FakeDefinition { SettingsId = "Marry", DisplayName = "Marry" };
            definition.SettingPropertyGroups.Add(group);
            var provider = new FakeProvider();
            provider.Settings["Marry"] = marry;
            provider.Definitions.Add(definition);
            return (provider, marry);
        }

        [Fact]
        public void A_host_value_stored_before_its_change_handler_threw_stays_held()
        {
            var (provider, marry) = Handlers(() => true);
            var session = new SyncSession();
            var report = SettingsApplier.Apply(new McmSettings(provider), Host("i\tMaxWives\t3"), session, 1);
            Assert.Equal(1, report.Set);
            Assert.Equal(3, marry.MaxWives);

            // Held like any other: a joined player's change goes back to the host's value, and leaving gives back the change, now the player's own.
            marry.MaxWives = 2;
            Assert.Equal(new[] { "Marry" }, session.HoldHostValues());
            Assert.Equal(3, marry.MaxWives);
            session.Restore();
            Assert.Equal(2, marry.MaxWives);
        }

        [Fact]
        public void A_save_set_off_by_a_hold_can_drop_old_entries_safely()
        {
            // MaxWives is left from a failed restore; on the next server a hold of Polygamy sets off a save, and that
            // save puts MaxWives back and drops its entry while the hold is still going through the entries.
            SyncSession? session = null;
            var saving = false;
            var (provider, marry) = Handlers(() => false, m =>
            {
                if (saving || session == null) return;
                saving = true;
                var swap = session.BeginSave(m, out _);
                session.EndSave(swap);
            });
            session = new SyncSession();
            var mcm = new McmSettings(provider);
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t3"), session, 1);
            marry.FailWrites = true;
            session.Restore();
            marry.FailWrites = false;
            session.BeginSession();
            saving = true;
            SettingsApplier.Apply(mcm, Host("b\tPolygamy\t1"), session, 1);
            saving = false;

            marry.Polygamy = false;
            Assert.Equal(new[] { "Marry" }, session.HoldHostValues());
            Assert.True(marry.Polygamy);
            Assert.Equal(1, marry.MaxWives);
        }

        [Fact]
        public void A_tiny_host_live_edit_still_goes_in()
        {
            var (provider, rates) = FakeMcm.RatesClient();
            var rate = new McmSettings(provider).PropertiesOf("Rates").Single(property => property.Id == "Rate");
            Assert.True(LiveChannel.TryApplyText(rate, "0.2000000001"));
            Assert.Equal(0.2000000001, rates.Rate);
        }

        [Fact]
        public void A_late_end_from_an_older_handler_leaves_the_new_session_alone()
        {
            // The handler itself is the owner, taken in queue order on the game thread, so two handlers made at once
            // can not swap their order.
            var owner = new SessionOwner();
            object first = new object(), second = new object();
            owner.Take(first);
            owner.Take(second);
            Assert.False(owner.Owns(first));
            Assert.True(owner.Owns(second));

            // ClientSync is built against Coop, so its wiring is checked in the source.
            var source = File.ReadAllText(Path.Combine(SyncRepoRoot(), "compat", "CoodszSettingsSync", "src", "Client", "ClientSync.cs"));
            Assert.Contains("global::CoodszSettingsSync.ClientSync.SessionStarted(this);", source);
            Assert.Contains("global::CoodszSettingsSync.ClientSync.SessionEnded(this);", source);
            Assert.Contains("internal static void SessionStarted(object owner) => Work.Enqueue(() => StartSession(owner));", source);
            var ended = source.Substring(source.IndexOf("internal static void SessionEnded(object owner)", StringComparison.Ordinal));
            Assert.True(ended.IndexOf("if (!Owner.Owns(owner))", StringComparison.Ordinal) < ended.IndexOf("_inSession = false;", StringComparison.Ordinal));

            // A new session first puts back what the last server left (a late end is skipped), then begins.
            var start = source.Substring(source.IndexOf("private static void StartSession(object owner)", StringComparison.Ordinal));
            Assert.True(start.IndexOf("Session.Restore();", StringComparison.Ordinal) < start.IndexOf("Session.BeginSession();", StringComparison.Ordinal));
        }

        [Fact]
        public void A_setting_that_saves_on_every_write_does_not_loop()
        {
            // Some mods save on every change. Through the apply, the hold, a normal save and the restore, every save
            // must write the player's own value and the run must end (it used to loop until the game crashed).
            SyncSession? session = null;
            var saved = new List<int>();
            var marry = new MarrySettings();
            var group = new FakeGroup();
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "MaxWives", DisplayName = "MaxWives", SettingType = FakeSettingType.Int, MinValue = 1, MaxValue = 5,
                PropertyReference = new FakeRef(typeof(int), () => marry.MaxWives, v =>
                {
                    marry.MaxWives = (int)v!;
                    if (session == null) return;
                    var swap = session.BeginSave(marry, out var block);
                    if (!block) saved.Add(marry.MaxWives);
                    session.EndSave(swap);
                })
            });
            var definition = new FakeDefinition { SettingsId = "Marry", DisplayName = "Marry" };
            definition.SettingPropertyGroups.Add(group);
            var provider = new FakeProvider();
            provider.Settings["Marry"] = marry;
            provider.Definitions.Add(definition);
            session = new SyncSession();

            SettingsApplier.Apply(new McmSettings(provider), Host("i\tMaxWives\t3"), session, 1);
            Assert.Equal(3, marry.MaxWives);
            Assert.NotEmpty(saved);
            Assert.All(saved, value => Assert.Equal(1, value));

            saved.Clear();
            marry.MaxWives = 2;
            Assert.Equal(new[] { "Marry" }, session.HoldHostValues());
            Assert.Equal(3, marry.MaxWives);
            Assert.All(saved, value => Assert.Equal(2, value));

            saved.Clear();
            var outer = session.BeginSave(marry, out var outerBlock);
            Assert.False(outerBlock);
            saved.Add(marry.MaxWives);
            session.EndSave(outer);
            Assert.Equal(3, marry.MaxWives);
            Assert.All(saved, value => Assert.Equal(2, value));

            saved.Clear();
            session.Restore();
            Assert.Equal(2, marry.MaxWives);
            Assert.All(saved, value => Assert.Equal(2, value));
        }

        [Fact]
        public void Restore_never_saves_a_host_value_still_held_by_another_setting()
        {
            // Two held settings in one object, and setters that save the whole object on every write. Restore puts
            // MaxWives back first while Polygamy still holds the host's value; no save may write that into the file.
            SyncSession? session = null;
            var files = new List<(int MaxWives, bool Polygamy)>();
            var marry = new MarrySettings();
            void SaveNow()
            {
                if (session == null) return;
                var swap = session.BeginSave(marry, out var block);
                if (!block) files.Add((marry.MaxWives, marry.Polygamy));
                session.EndSave(swap);
            }
            var group = new FakeGroup();
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "MaxWives", DisplayName = "MaxWives", SettingType = FakeSettingType.Int, MinValue = 1, MaxValue = 5,
                PropertyReference = new FakeRef(typeof(int), () => marry.MaxWives, v => { marry.MaxWives = (int)v!; SaveNow(); })
            });
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "Polygamy", DisplayName = "Polygamy", SettingType = FakeSettingType.Bool,
                PropertyReference = new FakeRef(typeof(bool), () => marry.Polygamy, v => { marry.Polygamy = (bool)v!; SaveNow(); })
            });
            var definition = new FakeDefinition { SettingsId = "Marry", DisplayName = "Marry" };
            definition.SettingPropertyGroups.Add(group);
            var provider = new FakeProvider();
            provider.Settings["Marry"] = marry;
            provider.Definitions.Add(definition);
            session = new SyncSession();

            SettingsApplier.Apply(new McmSettings(provider), Host("i\tMaxWives\t3", "b\tPolygamy\t1"), session, 1);
            Assert.Equal(3, marry.MaxWives);
            Assert.True(marry.Polygamy);
            // The saves the apply set off wrote the player's values only, never an object caught half swapped.
            Assert.NotEmpty(files);
            Assert.DoesNotContain(files, file => file.MaxWives == 3 || file.Polygamy);

            files.Clear();
            session.Restore();

            Assert.Equal(1, marry.MaxWives);
            Assert.False(marry.Polygamy);
            Assert.DoesNotContain(files, file => file.MaxWives == 3 || file.Polygamy);
        }

        [Fact]
        public void A_setting_that_can_not_be_read_for_its_own_value_is_left_alone()
        {
            // The first read works, the one that records the player's own value throws: no entry, no write.
            var marry = new MarrySettings();
            var reads = 0;
            var group = new FakeGroup();
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "MaxWives", DisplayName = "MaxWives", SettingType = FakeSettingType.Int, MinValue = 1, MaxValue = 5,
                PropertyReference = new FakeRef(typeof(int), () => ++reads == 2 ? throw new InvalidOperationException("busy") : marry.MaxWives, v => marry.MaxWives = (int)v!)
            });
            var definition = new FakeDefinition { SettingsId = "Marry", DisplayName = "Marry" };
            definition.SettingPropertyGroups.Add(group);
            var provider = new FakeProvider();
            provider.Settings["Marry"] = marry;
            provider.Definitions.Add(definition);
            var mcm = new McmSettings(provider);
            mcm.PropertiesOf("Marry");
            reads = 0;
            var session = new SyncSession();

            var report = SettingsApplier.Apply(mcm, Host("i\tMaxWives\t3"), session, 1);

            Assert.Equal(0, report.Set);
            Assert.Equal(1, marry.MaxWives);
            Assert.False(session.IsSynced("Marry"));
        }

        [Fact]
        public void A_save_leaves_a_setting_that_reads_nothing_alone()
        {
            var (client, _, session) = Synced("s\tTitle\tLord");
            client.Marry.Title = null!;
            var swap = session.BeginSave(client.Marry, out var block);
            Assert.False(block);
            session.EndSave(swap);
            // Nothing was swapped, so the host's value is not written in after the save.
            Assert.Null(client.Marry.Title);
            session.Restore();
            Assert.Equal("Sir", client.Marry.Title);
        }

        [Fact]
        public void A_save_on_the_next_server_never_writes_an_old_sessions_host_value()
        {
            var (client, _, session) = Synced("i\tMaxWives\t3");
            client.Marry.FailWrites = true;
            session.Restore();
            client.Marry.FailWrites = false;
            session.BeginSession();

            // The player sets their own value on the next server, then MCM saves.
            client.Marry.MaxWives = 2;
            var swap = session.BeginSave(client.Marry, out _);
            session.EndSave(swap);
            Assert.Equal(2, client.Marry.MaxWives);
            Assert.False(session.IsSynced("Marry"));
        }

        [Fact]
        public void A_save_on_the_next_server_puts_back_a_value_a_failed_restore_left()
        {
            var (client, _, session) = Synced("i\tMaxWives\t3");
            client.Marry.FailWrites = true;
            session.Restore();
            client.Marry.FailWrites = false;
            session.BeginSession();

            var swap = session.BeginSave(client.Marry, out _);
            Assert.Equal(1, client.Marry.MaxWives);
            session.EndSave(swap);
            // Restored for good: the old host's 3 does not come back after the save.
            Assert.Equal(1, client.Marry.MaxWives);
            Assert.False(session.IsSynced("Marry"));
        }

        [Fact]
        public void A_setting_that_reads_nothing_is_not_taken_as_the_players_value()
        {
            var (client, _, session) = Synced("s\tTitle\tLord");
            Assert.Equal("Lord", client.Marry.Title);
            client.Marry.Title = null!;
            Assert.Empty(session.HoldHostValues());
            session.Restore();
            Assert.Equal("Sir", client.Marry.Title);
        }

        [Fact]
        public void A_change_made_just_before_leaving_is_kept()
        {
            // The player changes it inside the 2 second window, so no hold saw it before the session ended.
            var (client, _, session) = Synced("i\tMaxWives\t3");
            client.Marry.MaxWives = 2;
            session.Restore();
            Assert.Equal(2, client.Marry.MaxWives);
        }

        [Fact]
        public void A_snapshot_applied_again_keeps_the_players_change_for_after()
        {
            var (client, mcm, session) = Synced("i\tMaxWives\t3");
            client.Marry.MaxWives = 2;
            SettingsApplier.Apply(mcm, Host("i\tMaxWives\t3"), session, 1);
            Assert.Equal(3, client.Marry.MaxWives);
            session.Restore();
            Assert.Equal(2, client.Marry.MaxWives);
        }

        [Fact]
        public void Holding_the_hosts_values_stops_when_the_session_ends()
        {
            // ClientSync is built against Coop, so its wiring is checked in the source.
            var source = File.ReadAllText(Path.Combine(SyncRepoRoot(), "compat", "CoodszSettingsSync", "src", "Client", "ClientSync.cs"));
            Assert.Contains("_holdFor >= HoldSeconds && _inSession && _campaignReady && _guardReady && !_offForSession && !ModderLordsTookOver()", source);
            var ended = source.Substring(source.IndexOf("internal static void SessionEnded(object owner)", StringComparison.Ordinal));
            Assert.True(ended.IndexOf("_inSession = false;", StringComparison.Ordinal) < ended.IndexOf("if (!_active) return;", StringComparison.Ordinal));
        }

        [Fact]
        public void A_save_that_keeps_host_values_out_says_so_once_per_session()
        {
            // What the save swapped, for the log: kept out of the file, and whether the player had changed one.
            var (client, _, session) = Synced("i\tMaxWives\t3");
            var quiet = session.BeginSave(client.Marry, out _);
            Assert.Equal(1, quiet.SwappedCount);
            Assert.False(quiet.PlayerChanged);
            session.EndSave(quiet);
            client.Marry.MaxWives = 2;
            var changed = session.BeginSave(client.Marry, out _);
            Assert.True(changed.PlayerChanged);
            session.EndSave(changed);
            Assert.StartsWith("Settings sync: a save of Marry wrote your own values", SyncNotes.SavedOwn("Marry"));

            // ClientSync logs each line once per session, from the hold and from the save guard.
            var source = File.ReadAllText(Path.Combine(SyncRepoRoot(), "compat", "CoodszSettingsSync", "src", "Client", "ClientSync.cs"));
            Assert.Contains("if (__state != null && __state.SwappedCount > 0) NoteSave(McmSettings.Text(__0, \"Id\"), __state.PlayerChanged);", source);
            Assert.Contains("NoteHeld(id);", source);
            var start = source.Substring(source.IndexOf("private static void StartSession(object owner)", StringComparison.Ordinal));
            var begin = start.IndexOf("Session.BeginSession();", StringComparison.Ordinal);
            Assert.True(begin > 0);
            foreach (var reset in new[] { "Held.NewSession();", "_saveNoted = false;", "GuardNoted.Clear();" })
            {
                var at = start.IndexOf(reset, StringComparison.Ordinal);
                Assert.True(at >= 0 && at < begin, reset + " must run before Session.BeginSession() in StartSession.");
            }
        }

        [Fact]
        public void A_join_answer_never_leaves_the_players_already_on_the_server_on_older_values()
        {
            static SettingsSnapshotData Rate(string value) => new SettingsSnapshotData("Rates", SettingsScope.Global, false, new List<string> { "f\tRate\t" + value });
            var ledger = new SentLedger();

            // Player A joins: the answer goes to A, and the ledger notes v1 for the players on the server.
            Assert.Single(ledger.TakeChanged(new[] { Rate("0.1") }));
            // The host changes the rate, and player B joins before the next change check. B's answer first hands the
            // change check what A has not had yet (A gets v2), so the answer to B never marks v2 as A's.
            var forA = ledger.TakeChanged(new[] { Rate("0.2") });
            Assert.Equal("f\tRate\t0.2", Assert.Single(Assert.Single(forA).Values));
            // The next change check has nothing left to send: A and B both have v2.
            Assert.Empty(ledger.TakeChanged(new[] { Rate("0.2") }));
            // A later host change reaches both.
            Assert.Single(ledger.TakeChanged(new[] { Rate("0.3") }));

            var sync = SourceFiles.Read("compat/CoodszSettingsSync/src/Server/ServerSync.cs").Replace("\r\n", "\n");
            var answer = SourceFiles.Method(sync, "private static void Answer(List<NetPeer> peers)");
            var broadcast = answer.IndexOf("Broadcast(network, snapshots, peers);", StringComparison.Ordinal);
            Assert.True(broadcast > 0 && broadcast < answer.IndexOf("foreach (NetPeer peer in peers)", StringComparison.Ordinal), answer);
            Assert.DoesNotContain("LastSent[", sync);
            Assert.Contains("foreach (NetPeer peer in Players.Where(p => !skip.Contains(p)).ToList())", SourceFiles.Method(sync, "private static void Broadcast(INetwork network, List<SettingsSnapshotData> snapshots, List<NetPeer> skip)"));

            // Both shipped server copies are this build (the type name sits in the DLL's metadata as ASCII).
            foreach (var dll in new[] { @"src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\CoodszSettingsSync.Coop.dll", @"src\TaomCoopHost\WarSync\CoodszSettingsSync.Coop.dll" })
                Assert.True(File.ReadAllBytes(Path.Combine(SourceFiles.Root(), dll)).AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(nameof(SentLedger))) >= 0, dll + " is an older build: run compat\\CoodszServerTools\\build.ps1");
        }

        private static string SyncRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }

        private static string Temp()
        {
            var path = Path.Combine(Path.GetTempPath(), "coodsz-sync-safety-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
