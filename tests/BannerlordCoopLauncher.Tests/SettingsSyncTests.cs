using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CoodszHost.Services;
using CoodszSettingsSync;
using SettingsSyncFakes;

namespace BannerlordCoopLauncher.Tests
{
    public class SettingsSyncWireTests
    {
        [Theory]
        [InlineData(WireKind.Bool, "Enabled", "1")]
        [InlineData(WireKind.Int, "MaxTowns", "150")]
        [InlineData(WireKind.Float, "Chance", "0.25")]
        [InlineData(WireKind.String, "Title", "a\tb with tab")]
        [InlineData(WireKind.Dropdown, "Mode", "2")]
        public void Value_lines_round_trip(WireKind kind, string id, string text)
        {
            var line = SettingsWire.Encode(new WireValue(kind, id, text));
            Assert.NotNull(line);
            var back = SettingsWire.Decode(line);
            Assert.NotNull(back);
            Assert.Equal(kind, back!.Kind);
            Assert.Equal(id, back.Id);
            Assert.Equal(text, back.Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("x\tId\t1")]
        [InlineData("b")]
        [InlineData("bId1")]
        [InlineData("b\t\t1")]
        public void Broken_lines_are_dropped(string? line) => Assert.Null(SettingsWire.Decode(line));

        [Fact]
        public void Long_strings_and_bad_ids_never_travel()
        {
            var tooLong = new string('a', SettingsWire.MaxStringLength + 1);
            Assert.Null(SettingsWire.Encode(new WireValue(WireKind.String, "Name", tooLong)));
            Assert.Null(SettingsWire.Decode("s\tName\t" + tooLong));
            Assert.Null(SettingsWire.FormatValue(WireKind.String, tooLong));
            Assert.Null(SettingsWire.Encode(new WireValue(WireKind.Bool, "Bad\tId", "1")));
            Assert.Null(SettingsWire.Encode(new WireValue(WireKind.Bool, new string('x', SettingsWire.MaxIdLength + 1), "1")));
        }

        [Fact]
        public void Values_format_in_invariant_culture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("0.5", SettingsWire.FormatValue(WireKind.Float, 0.5f));
                Assert.Equal("1", SettingsWire.FormatValue(WireKind.Bool, true));
                Assert.Equal("-3", SettingsWire.FormatValue(WireKind.Int, -3));
                Assert.Null(SettingsWire.FormatValue(WireKind.Float, float.NaN));
                Assert.Null(SettingsWire.FormatValue(WireKind.Int, "3"));
                Assert.Null(SettingsWire.FormatValue(WireKind.Dropdown, -1));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Theory]
        [InlineData("500", 0, 100, 100)]
        [InlineData("-5", 0, 100, 0)]
        [InlineData("50", 0, 100, 50)]
        [InlineData("500", 0, 0, 500)]
        public void Whole_numbers_are_held_to_the_clients_limits(string text, int min, int max, int expected)
        {
            Assert.True(SettingsWire.TryDecide(new WireValue(WireKind.Int, "N", text), WireKind.Int, min, max, 0, false, out var value, out _));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Floats_are_held_to_the_clients_limits_and_whole_numbers_widen()
        {
            Assert.True(SettingsWire.TryDecide(new WireValue(WireKind.Float, "F", "2.5"), WireKind.Float, 0m, 1m, 0, false, out var high, out _));
            Assert.Equal(1f, high);
            Assert.True(SettingsWire.TryDecide(new WireValue(WireKind.Int, "F", "3"), WireKind.Float, 0m, 0m, 0, false, out var widened, out _));
            Assert.Equal(3f, widened);
            Assert.False(SettingsWire.TryDecide(new WireValue(WireKind.Float, "F", "NaN"), WireKind.Float, 0m, 0m, 0, false, out _, out var reason));
            Assert.Equal("value", reason);
        }

        [Theory]
        [InlineData(WireKind.Bool, "1", WireKind.Int)]
        [InlineData(WireKind.Float, "1.5", WireKind.Int)]
        [InlineData(WireKind.String, "x", WireKind.Bool)]
        [InlineData(WireKind.Int, "1", WireKind.Dropdown)]
        [InlineData(WireKind.Dropdown, "1", WireKind.Int)]
        public void A_value_of_another_kind_is_skipped(WireKind sent, string text, WireKind target)
        {
            Assert.False(SettingsWire.TryDecide(new WireValue(sent, "X", text), target, 0m, 0m, 5, false, out _, out var reason));
            Assert.Equal("kind", reason);
        }

        [Fact]
        public void A_file_whole_number_fills_a_dropdown_only_when_in_range()
        {
            Assert.True(SettingsWire.TryDecide(new WireValue(WireKind.Int, "D", "1"), WireKind.Dropdown, 0m, 0m, 3, true, out var index, out _));
            Assert.Equal(1, index);
            Assert.False(SettingsWire.TryDecide(new WireValue(WireKind.Int, "D", "3"), WireKind.Dropdown, 0m, 0m, 3, true, out _, out var reason));
            Assert.Equal("range", reason);
        }

        [Theory]
        [InlineData("3", 3, false)]
        [InlineData("-1", 3, false)]
        [InlineData("2", 3, true)]
        [InlineData("0", 0, false)]
        public void Dropdown_choices_outside_the_clients_list_are_skipped(string text, int count, bool applied)
        {
            var ok = SettingsWire.TryDecide(new WireValue(WireKind.Dropdown, "D", text), WireKind.Dropdown, 0m, 0m, count, false, out _, out var reason);
            Assert.Equal(applied, ok);
            if (!applied) Assert.Equal("range", reason);
        }

        [Theory]
        [InlineData("OpenMenuHotkey", true)]
        [InlineData("ToggleKey", true)]
        [InlineData("UIScale", true)]
        [InlineData("ShowHud", true)]
        [InlineData("MasterVolume", true)]
        [InlineData("RTSCamera", true)]
        [InlineData("camera_distance", true)]
        [InlineData("{=MyMod_Zoom}Zoom speed", true)]
        [InlineData("MonkeyCount", false)]
        [InlineData("Keyword", false)]
        [InlineData("MaxTowns", false)]
        [InlineData("Polygamy", false)]
        public void Hotkey_interface_sound_and_camera_settings_never_travel(string name, bool skipped) =>
            Assert.Equal(skipped, SettingsWire.IsAlwaysSkipped(name));

        [Fact]
        public void Words_split_at_camel_case_acronyms_and_symbols()
        {
            Assert.Equal(new[] { "ui", "scale" }, SettingsWire.Words("UIScale"));
            Assert.Equal(new[] { "rts", "camera", "2" }, SettingsWire.Words("RTSCamera 2"));
            Assert.Equal(new[] { "max", "towns" }, SettingsWire.Words("{=abc}Max_Towns"));
            Assert.True(SettingsWire.IsInputKeyType("TaleWorlds.InputSystem.InputKey"));
        }

        [Fact]
        public void Keep_own_list_matches_any_of_a_mods_names()
        {
            var list = new KeepOwnList(new[] { "MarryAnyone", " ", "Diplomacy" });
            Assert.Equal(2, list.Count);
            Assert.True(list.Keeps("marryanyone"));
            Assert.True(list.Keeps(null, "Other", "Diplomacy"));
            Assert.False(list.Keeps("Other", null));
            Assert.Equal("recipes.json", KeepOwnList.FileName);
            Assert.Equal("PlayersOwnSettings", KeepOwnList.Field);
        }

        [Theory]
        [InlineData(@"C:\Steam\common\Mount & Blade II Bannerlord\Modules\MarryAnyone\bin\Win64_Shipping_Client\MarryAnyone.dll", "MarryAnyone")]
        [InlineData("/x/Modules/Diplomacy/bin/Win64_Shipping_Server/Diplomacy.dll", "Diplomacy")]
        [InlineData(@"C:\Windows\Microsoft.NET\mscorlib.dll", null)]
        [InlineData("", null)]
        public void A_settings_class_belongs_to_the_module_its_dll_is_in(string path, string? module) =>
            Assert.Equal(module, McmSettings.ModuleIdOfPath(path));

        [Fact]
        public void Modderlords_compat_turns_this_sync_off()
        {
            Assert.True(CoopProbe.ModderLordsLoaded(new[] { "Common", "ModderLords.CompatSync" }));
            Assert.True(CoopProbe.ModderLordsLoaded(new[] { "modderlords.compatsync.coop" }));
            Assert.False(CoopProbe.ModderLordsLoaded(new[] { "Common", "ModderLords.Core", null }));
        }

        [Fact]
        public void Only_this_protocol_version_is_understood()
        {
            Assert.True(SettingsWire.IsKnownProtocol(SettingsWire.ProtocolVersion));
            Assert.False(SettingsWire.IsKnownProtocol(SettingsWire.ProtocolVersion + 1));
            Assert.False(SettingsWire.IsKnownProtocol(0));
        }

        [Fact]
        public void The_server_mark_carries_its_protocol_version()
        {
            Assert.Equal(SettingsWire.ProtocolVersion, SettingsWire.ReadServerMark(SettingsWire.ServerMark));
            Assert.Equal(7, SettingsWire.ReadServerMark("CoodszSettingsSync/7"));
            Assert.Null(SettingsWire.ReadServerMark(null));
            Assert.Null(SettingsWire.ReadServerMark("Wrong version of module 'Coop'"));
            Assert.Null(SettingsWire.ReadServerMark("CoodszSettingsSync/x"));
            Assert.Null(SettingsWire.ReadServerMark("CoodszSettingsSync/-1"));
        }
    }

    public class SettingsSyncProbeTests
    {
        private static readonly Dictionary<string, Type> All = new()
        {
            ["Common.Messaging.IHandler"] = typeof(IDisposable),
            ["Common.Messaging.IMessageBroker"] = typeof(FakeBroker),
            ["Common.Messaging.MessagePayload`1"] = typeof(FakePayload<>),
            ["Common.Network.INetwork"] = typeof(FakeNetwork),
            ["Common.Messaging.ICommand"] = typeof(IComparable),
            ["Common.ModInformation"] = typeof(FakeModInformation),
            ["LiteNetLib.NetPeer"] = typeof(object),
            ["ProtoBuf.ProtoContractAttribute"] = typeof(Attribute),
            ["Coop.Core.Client.ClientModule"] = typeof(object),
            ["Coop.Core.Server.ServerModule"] = typeof(object),
            ["Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated"] = typeof(object),
            ["GameInterface.Services.GameState.Messages.CampaignReady"] = typeof(object),
        };

        private static Func<string, string, Type?> Without(params string[] missing) =>
            (_, name) => missing.Contains(name) ? null : All.GetValueOrDefault(name);

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Ready_when_every_type_and_member_is_there(bool client)
        {
            var result = CoopProbe.Check(client, Without());
            Assert.True(result.Ready, result.Describe());
            Assert.Empty(result.Missing);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Ready_on_a_coop_without_the_server_to_client_marker(bool client)
        {
            // Coop removed Common.Messaging.IServerToClientCommand on 2026-10-30, while v0.1.7 still has it. The
            // probe must not need it, and having it there must not matter either.
            var withMarker = new Dictionary<string, Type>(All) { ["Common.Messaging.IServerToClientCommand"] = typeof(IComparable) };
            Assert.True(CoopProbe.Check(client, Without("Common.Messaging.IServerToClientCommand")).Ready);
            Assert.True(CoopProbe.Check(client, (_, name) => withMarker.GetValueOrDefault(name)).Ready);
        }

        [Fact]
        public void Off_and_waiting_while_coop_is_not_loaded()
        {
            var result = CoopProbe.Check(true, (_, _) => null);
            Assert.False(result.Ready);
            Assert.True(result.NotLoadedYet);
        }

        [Theory]
        [InlineData(true, "Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated", "NetworkModuleVersionsValidated")]
        [InlineData(true, "GameInterface.Services.GameState.Messages.CampaignReady", "CampaignReady")]
        [InlineData(true, "Coop.Core.Client.ClientModule", "Coop.Core.Client.ClientModule")]
        [InlineData(false, "Coop.Core.Server.ServerModule", "Coop.Core.Server.ServerModule")]
        [InlineData(false, "Common.Network.INetwork", "INetwork")]
        [InlineData(false, "LiteNetLib.NetPeer", "NetPeer")]
        public void Off_when_a_type_is_missing(bool client, string typeName, string named)
        {
            var result = CoopProbe.Check(client, Without(typeName));
            Assert.False(result.Ready);
            Assert.False(result.NotLoadedYet);
            Assert.Contains(named, result.Missing);
        }

        [Fact]
        public void Off_when_a_member_is_missing()
        {
            var types = new Dictionary<string, Type>(All) { ["Common.Network.INetwork"] = typeof(FakeNetworkWithoutSendAll) };
            var result = CoopProbe.Check(false, (_, name) => types.GetValueOrDefault(name));
            Assert.False(result.Ready);
            Assert.Contains("INetwork.SendAll", result.Missing);
        }

        [Fact]
        public void The_loader_stays_off_with_one_line_when_coop_never_loads()
        {
            var lines = new List<string>();
            var loader = new SettingsSyncLoader(true, Path.GetTempPath(), Path.GetTempPath(), lines.Add);
            loader.TryLoad();
            Assert.Empty(lines);
            for (var i = 0; i < 70; i++)
                loader.Tick(1f);
            Assert.False(loader.Active);
            var line = Assert.Single(lines);
            Assert.StartsWith("Settings sync disabled", line);
        }
    }

    public class SettingsSyncMcmTests
    {
        [Fact]
        public void Scope_comes_from_the_mcm_base_class()
        {
            Assert.Equal(SettingsScope.Global, McmSettings.ScopeOfType(typeof(FakeGlobal)));
            Assert.Equal(SettingsScope.PerCampaign, McmSettings.ScopeOfType(typeof(FakeCampaign)));
            Assert.Equal(SettingsScope.PerSave, McmSettings.ScopeOfType(typeof(FakeSave)));
            Assert.Equal(SettingsScope.Unknown, McmSettings.ScopeOfType(typeof(object)));
            Assert.Equal(SettingsScope.Unknown, McmSettings.ScopeOfType(null));
        }

        [Fact]
        public void Server_capture_leaves_out_hotkeys_kept_mods_per_save_and_buttons()
        {
            var host = FakeMcm.Host();
            var snapshots = SettingsCapture.Capture(new McmSettings(host), new KeepOwnList(new[] { "Kept" }));

            Assert.Equal(new[] { "Marry", "Campaign" }, snapshots.Select(s => s.SettingsId));
            var marry = snapshots[0];
            Assert.Equal(SettingsScope.Global, marry.Scope);
            Assert.False(marry.FromFile);
            Assert.Equal(new[] { "b\tPolygamy\t1", "b\tDebug\t0", "i\tMaxWives\t4", "f\tChance\t0.75", "s\tTitle\tLord", "d\tOrientation\t2" }, marry.Values);
            Assert.Equal(SettingsScope.PerCampaign, snapshots[1].Scope);
        }

        [Fact]
        public void Client_apply_sets_what_it_has_and_skips_the_rest()
        {
            var host = new McmSettings(FakeMcm.Host());
            var snapshot = SettingsCapture.Capture(host, new KeepOwnList()).First(s => s.SettingsId == "Marry");
            snapshot.Values.Add("b\tNotHere\t1");
            snapshot.Values.Add("i\tPolygamy\t1");
            snapshot.Values.Add("b\tOpenKey\t1");

            var client = FakeMcm.Client();
            var session = new SyncSession();
            var report = SettingsApplier.Apply(new McmSettings(client.Provider), snapshot, session, 1);

            // Debug, Polygamy, MaxWives (clamped to 3), Chance, Title and Orientation are set.
            Assert.Equal(6, report.Set);
            Assert.Equal(3, report.Skipped);
            Assert.Equal(1, report.Reasons["missing"]);
            Assert.Equal(1, report.Reasons["kind"]);
            Assert.Equal(1, report.Reasons["own"]);
            Assert.True(client.Marry.Polygamy);
            Assert.Equal(3, client.Marry.MaxWives);
            Assert.Equal(0.75f, client.Marry.Chance);
            Assert.Equal("Lord", client.Marry.Title);
            Assert.Equal(2, client.Marry.Orientation.SelectedIndex);
            Assert.Equal("Key.F5", client.Marry.OpenKey);
            Assert.True(session.IsSynced("Marry"));
            Assert.False(session.IsSynced("Campaign"));
            Assert.Contains("6 set, 3 skipped", report.Describe());
        }

        [Fact]
        public void A_dropdown_choice_the_client_does_not_have_is_skipped()
        {
            var client = FakeMcm.Client();
            var snapshot = new SettingsSnapshotData("Marry", SettingsScope.Global, false, new List<string> { "d\tOrientation\t9" });
            var report = SettingsApplier.Apply(new McmSettings(client.Provider), snapshot, new SyncSession(), 1);
            Assert.Equal(0, report.Set);
            Assert.Equal(1, report.Reasons["range"]);
            Assert.Equal(0, client.Marry.Orientation.SelectedIndex);
        }

        [Fact]
        public void A_settings_object_the_client_does_not_have_is_skipped()
        {
            var client = FakeMcm.Client();
            var snapshot = new SettingsSnapshotData("Other", SettingsScope.Global, false, new List<string> { "b\tX\t1" });
            var session = new SyncSession();
            var report = SettingsApplier.Apply(new McmSettings(client.Provider), snapshot, session, 1);
            Assert.Equal(0, report.Set);
            Assert.Equal(1, report.Reasons["missing"]);
            Assert.False(session.IsSynced("Other"));
        }

        [Fact]
        public void The_end_of_the_session_puts_the_players_values_back()
        {
            var host = new McmSettings(FakeMcm.Host());
            var snapshot = SettingsCapture.Capture(host, new KeepOwnList()).First(s => s.SettingsId == "Marry");
            var client = FakeMcm.Client();
            var session = new SyncSession();
            SettingsApplier.Apply(new McmSettings(client.Provider), snapshot, session, 1);
            // The same snapshot again (after the campaign loads) must not record the host's values as the player's.
            SettingsApplier.Apply(new McmSettings(client.Provider), snapshot, session, 1);

            Assert.True(session.Restore() >= 5);
            Assert.False(client.Marry.Polygamy);
            Assert.Equal(1, client.Marry.MaxWives);
            Assert.Equal(0.1f, client.Marry.Chance);
            Assert.Equal("Sir", client.Marry.Title);
            Assert.Equal(0, client.Marry.Orientation.SelectedIndex);
            Assert.False(session.IsSynced("Marry"));
        }
    }

    public class SettingsSyncFileTests
    {
        [Fact]
        public void The_lock_list_the_host_writes_is_the_one_the_server_reads()
        {
            var folder = Temp();
            try
            {
                Assert.True(McmKeepOwn.Write(folder, new[] { "Marry", "marry", "Diplomacy", " " }));
                var list = SettingsFiles.ReadKeepOwn(Path.Combine(folder, KeepOwnList.FileName));
                Assert.Equal(KeepOwnList.FileName, McmKeepOwn.FileName);
                Assert.Equal(KeepOwnList.Field, McmKeepOwn.Field);
                Assert.Equal(2, list.Count);
                Assert.True(list.Keeps("Marry"));
                Assert.True(list.Keeps("Diplomacy"));
                Assert.False(McmKeepOwn.Write(Path.Combine(folder, "missing"), new[] { "Marry" }));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void The_lock_list_round_trips_in_modderlords_recipe_format()
        {
            // A recipe as ModderLords writes it (RecipeSet.ToJson, nulls left out).
            const string modderLords = "{\n  \"SchemaVersion\": 1,\n  \"GeneratedBy\": \"ModderLords\",\n  \"Mods\": [\n    { \"Id\": \"Diplomacy\", \"CampaignBehaviors\": [\"Diplomacy.X\"], \"MissionBehaviors\": [], \"Handlers\": [], \"Unpatch\": [], \"PlayerComparisons\": [], \"Relays\": [], \"SyncState\": [] }\n  ],\n  \"PlayersOwnSettings\": [\"MarryAnyone\", \"RTSCamera\"]\n}";
            var folder = Temp();
            try
            {
                var path = Path.Combine(folder, McmKeepOwn.FileName);
                File.WriteAllText(path, modderLords);
                Assert.Equal(new[] { "MarryAnyone", "RTSCamera" }, McmKeepOwn.Read(path));
                Assert.True(SettingsFiles.ReadKeepOwn(path).Keeps("MarryAnyone"));

                // Coodsz Host changes the list and keeps the rest of his recipe as it was.
                Assert.True(McmKeepOwn.Write(folder, new[] { "MarryAnyone" }));
                using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                {
                    var root = doc.RootElement;
                    Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
                    Assert.Equal("ModderLords", root.GetProperty("GeneratedBy").GetString());
                    Assert.Equal("Diplomacy.X", root.GetProperty("Mods")[0].GetProperty("CampaignBehaviors")[0].GetString());
                    Assert.Equal(new[] { "MarryAnyone" }, root.GetProperty("PlayersOwnSettings").EnumerateArray().Select(e => e.GetString()));
                }

                // Every mod locked again: the field is left out, as ModderLords writes it.
                Assert.True(McmKeepOwn.Write(folder, Array.Empty<string>()));
                Assert.DoesNotContain("PlayersOwnSettings", File.ReadAllText(path));
                Assert.Empty(McmKeepOwn.Read(path));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void A_fresh_recipe_has_the_fields_modderlords_needs()
        {
            var json = McmKeepOwn.Json(new[] { "MarryAnyone" });
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.GetProperty("Mods").ValueKind);
            Assert.Equal(0, doc.RootElement.GetProperty("Mods").GetArrayLength());
            Assert.Equal("MarryAnyone", doc.RootElement.GetProperty("PlayersOwnSettings")[0].GetString());
        }

        [Fact]
        public void A_missing_or_broken_keep_own_file_keeps_nothing()
        {
            var folder = Temp();
            try
            {
                var path = Path.Combine(folder, KeepOwnList.FileName);
                Assert.Equal(0, SettingsFiles.ReadKeepOwn(path).Count);
                File.WriteAllText(path, "{ not json");
                Assert.Equal(0, SettingsFiles.ReadKeepOwn(path).Count);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Every_mod_is_locked_to_the_host_until_the_host_unlocks_it()
        {
            var profile = new HostProfile();
            Assert.False(McmKeepOwn.Keeps(profile.PlayersOwnSettings, "Marry"));
            Assert.True(McmKeepOwn.SetLocked(profile.PlayersOwnSettings, "Marry", false));
            Assert.False(McmKeepOwn.SetLocked(profile.PlayersOwnSettings, "marry", false));
            Assert.True(McmKeepOwn.Keeps(profile.PlayersOwnSettings, null, "MARRY"));
            Assert.True(McmKeepOwn.SetLocked(profile.PlayersOwnSettings, "Marry", true));
            Assert.Empty(profile.PlayersOwnSettings);
        }

        [Fact]
        public void The_lock_flag_survives_saving_the_host_settings()
        {
            var settings = new HostSettings();
            settings.Current().PlayersOwnSettings.Add("MarryAnyone");
            var path = Path.Combine(Temp(), "host.json");
            try
            {
                HostSettings.Save(path, settings);
                var text = File.ReadAllText(path);
                Assert.Contains("PlayersOwnSettings", text);
                var back = System.Text.Json.JsonSerializer.Deserialize<HostSettings>(text)!;
                Assert.Equal(new[] { "MarryAnyone" }, back.Current().PlayersOwnSettings);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
        }

        [Fact]
        public void Host_overrides_save_load_and_clear()
        {
            var folder = Temp();
            try
            {
                var path = ModSettingsLive.OverridesPath("setup1", folder);
                var overrides = new SettingsOverrides();
                overrides.Set("Marry", new Dictionary<string, string> { ["Polygamy"] = "true", ["MaxWives"] = "3" });
                ModSettingsLive.SaveOverrides(path, overrides);

                var back = ModSettingsLive.LoadOverrides(path);
                Assert.Equal(2, back.Count);
                Assert.Equal("true", back.Get("Marry", "Polygamy"));
                Assert.Equal("3", back.Get("Marry", "MaxWives"));
                Assert.Null(back.Get("Marry", "Other"));
                Assert.Contains("\"Objects\"", File.ReadAllText(path));

                back.Clear("Marry");
                ModSettingsLive.SaveOverrides(path, back);
                Assert.False(File.Exists(path));
                Assert.True(ModSettingsLive.LoadOverrides(path).IsEmpty);
                File.WriteAllText(path, "{ broken");
                Assert.True(ModSettingsLive.LoadOverrides(path).IsEmpty);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Settings_files_are_read_when_mcm_does_not_run_on_the_server()
        {
            var root = Temp();
            try
            {
                var marry = Path.Combine(root, "Global", "MarryAnyone");
                Directory.CreateDirectory(marry);
                File.WriteAllText(Path.Combine(marry, "MarryAnyone.json"),
                    "{\"Polygamy\": true, \"MaxWives\": 4, \"Chance\": 1.0, \"Title\": \"Lord\", \"OpenMenuKey\": 5, \"Nested\": {\"a\": 1}}");
                var kept = Path.Combine(root, "Global", "Kept");
                Directory.CreateDirectory(kept);
                File.WriteAllText(Path.Combine(kept, "Kept.json"), "{\"A\": true}");
                File.WriteAllText(Path.Combine(marry, "Broken.json"), "{");

                var snapshots = SettingsFiles.CaptureFromFiles(root, new KeepOwnList(new[] { "Kept" }));

                var snapshot = Assert.Single(snapshots);
                Assert.Equal("MarryAnyone", snapshot.SettingsId);
                Assert.True(snapshot.FromFile);
                Assert.Equal(SettingsScope.Global, snapshot.Scope);
                Assert.Equal(new[] { "b\tPolygamy\t1", "i\tMaxWives\t4", "f\tChance\t1", "s\tTitle\tLord" }, snapshot.Values);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void The_dedicated_servers_mod_settings_folder_comes_first()
        {
            var documents = Temp();
            try
            {
                Assert.Null(SettingsFiles.FindModSettingsRoot(documents));
                var game = Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModSettings");
                Directory.CreateDirectory(game);
                Assert.Equal(game, SettingsFiles.FindModSettingsRoot(documents));
                var server = Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "Configs", "ModSettings");
                Directory.CreateDirectory(server);
                Assert.Equal(server, SettingsFiles.FindModSettingsRoot(documents));
            }
            finally
            {
                Directory.Delete(documents, recursive: true);
            }
        }

        /// <summary>
        /// MCM on the dedicated server reads its Global settings at boot from the game's own ModSettings folder, before
        /// Coop points the game at CoopData\DedicatedServer. Without CoopModPatch the server ran MCM's boot values and
        /// shared them, so the host's files there go into the live MCM: once, Global only, never over a host set value.
        /// </summary>
        [Fact]
        public void The_hosts_coopdata_files_replace_the_values_mcm_read_at_boot_once()
        {
            var documents = Temp();
            try
            {
                Assert.Null(SettingsFiles.FindCoopDataModSettings(documents));
                Directory.CreateDirectory(Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModSettings"));
                Assert.Null(SettingsFiles.FindCoopDataModSettings(documents));
                var server = Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "Configs", "ModSettings");
                Write(server, "Marry", "{ \"Polygamy\": false, \"Debug\": \"yes\", \"MaxWives\": 50, \"Chance\": 0.5, \"Title\": \"Duke\", \"Orientation\": 1 }");
                Write(server, "Campaign", "{ \"Enabled\": true }");
                Write(server, "Rates", "{ \"Rate\": 0.35 }");
                Assert.Equal(server, SettingsFiles.FindCoopDataModSettings(documents));

                var provider = FakeMcm.Host();
                var marry = (MarrySettings)provider.Settings["Marry"];
                var host = new McmSettings(provider);
                var reads = 0;
                var log = new List<string>();
                var files = new HostSettingsFiles(() => SettingsFiles.FindCoopDataModSettings(documents), () => false, log.Add,
                    (folder, unread) => { reads++; return SettingsFiles.CaptureFromFiles(folder, new KeepOwnList(), unread); });

                var first = files.Apply(host, 1, (id, property) => id == "Marry" && property == "Title");
                // Marry only: Campaign is PerCampaign, Rates is not on this server yet.
                Assert.Equal(1, first.Objects);
                Assert.Equal(4, first.Values);
                Assert.Equal(4, first.Changed);
                Assert.Equal(1, first.Skipped);
                Assert.Equal(new[]
                {
                    "Settings sync: CoopModPatch's MCM reload was not found, so the sync puts the host's mod settings files into MCM.",
                    "Settings sync: put 4 values (4 changed) of 1 settings object from the host's files in " + server
                        + " into the server's MCM. 1 kept the value MCM read at boot: Marry.Debug (a different type here).",
                }, log);
                Assert.False(marry.Polygamy);
                Assert.Equal(10, marry.MaxWives);
                Assert.Equal(0.5f, marry.Chance);
                Assert.Equal(1, marry.Orientation.SelectedIndex);
                // Debug's file text is no bool, so the boot value stays. Title was set through the live channel.
                Assert.False(marry.Debug);
                Assert.Equal("Lord", marry.Title);
                // A PerCampaign object rides with the campaign; a Global file never replaces it.
                Assert.False((bool)host.PropertiesOf("Campaign").Single().Read()!);
                // The capture reads the live MCM, which now holds the host's values.
                Assert.Equal(new[] { "b\tPolygamy\t0", "b\tDebug\t0", "i\tMaxWives\t10", "f\tChance\t0.5", "s\tTitle\tLord", "d\tOrientation\t1" },
                    SettingsCapture.Capture(host, new KeepOwnList()).Single(s => s.SettingsId == "Marry").Values);

                // Later passes in the same campaign neither read the files again nor put the values back in.
                marry.MaxWives = 3;
                var second = files.Apply(host, 1, null);
                Assert.Equal(0, second.Objects);
                Assert.Equal(3, marry.MaxWives);
                Assert.Equal(1, reads);
                Assert.Equal(1, files.Reads);

                // A settings object MCM makes late gets its values when it shows up, with the exact double.
                var (ratesProvider, rates) = FakeMcm.RatesClient();
                provider.Settings["Rates"] = rates;
                provider.Definitions.AddRange(ratesProvider.Definitions);
                var late = files.Apply(new McmSettings(provider), 1, null);
                Assert.Equal(1, late.Objects);
                Assert.Equal(0.35, rates.Rate);
                Assert.Equal(1, reads);

                // A new campaign puts them in again, from the same read.
                var reload = files.Apply(new McmSettings(provider), 2, null);
                Assert.Equal(2, reload.Objects);
                Assert.Equal(10, marry.MaxWives);
                Assert.Equal("Duke", marry.Title);
                Assert.Equal(1, reads);

                // The server applies the files on its tick and captures the live MCM only.
                var source = File.ReadAllText(Path.Combine(SyncRoot(), "compat", "CoodszSettingsSync", "src", "Server", "ServerSync.cs"));
                Assert.Contains("Safe(\"host settings files\", ApplyHostFiles);", source);
                Assert.Contains("_hostFiles.Apply(mcm, generation, channel == null ? null : channel.HostSet)", source);
                Assert.Contains("List<SettingsSnapshotData> live = SettingsCapture.Capture(mcm, _keepOwn);", source);
                Assert.Contains("CoopModPatch.Adapters.Mcm.McmServerReload", source);
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, @"SettingsFiles\.CaptureFromFiles\("));
            }
            finally
            {
                Directory.Delete(documents, recursive: true);
            }
        }

        [Fact]
        public void The_host_files_stay_off_when_coopmodpatch_reloads_them()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"MaxWives\": 7 }");
                var provider = FakeMcm.Host();
                var marry = (MarrySettings)provider.Settings["Marry"];

                var log = new List<string>();
                var found = 0;
                var withPatch = new HostSettingsFiles(() => { found++; return root; }, () => true, log.Add, retryPasses: 1);
                Assert.Equal(0, withPatch.Apply(new McmSettings(provider), 1, null).Objects);
                Assert.Equal(0, withPatch.Apply(new McmSettings(provider), 2, null).Objects);
                Assert.True(withPatch.Off);
                Assert.Equal(0, found);
                Assert.Equal(0, withPatch.Reads);
                Assert.Equal(4, marry.MaxWives);
                Assert.Contains("CoopModPatch reloads", log.Single());

                var withoutLog = new List<string>();
                var without = new HostSettingsFiles(() => root, () => false, withoutLog.Add, retryPasses: 1);
                Assert.Equal(1, without.Apply(new McmSettings(provider), 1, null).Objects);
                Assert.False(without.Off);
                Assert.Equal(7, marry.MaxWives);
                Assert.Equal("Settings sync: CoopModPatch's MCM reload was not found, so the sync puts the host's mod settings files into MCM.", withoutLog[0]);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>Every file value that does not go in is named in a server log line, never skipped without a word.</summary>
        [Fact]
        public void Host_file_values_that_cannot_go_in_are_named_in_the_log()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"Title\": \"https://evil.example/x\", \"OpenKey\": \"F6\", \"MaxWives\": 2, \"Chance\": [1], \"Gone\": true, \"Orientation\": 7 }");
                var provider = FakeMcm.Host();
                var marry = (MarrySettings)provider.Settings["Marry"];
                marry.FailWrites = true;
                var log = new List<string>();
                var result = new HostSettingsFiles(() => root, () => false, log.Add).Apply(new McmSettings(provider), 1, null);

                Assert.Equal(0, result.Values);
                Assert.Equal(3, result.Skipped);
                Assert.Equal(4, marry.MaxWives);
                Assert.Equal("Lord", marry.Title);
                // Left out while reading: private text, a per PC setting, a list.
                var leftOut = Assert.Single(log, line => line.Contains("values in the host's settings files are not put into the server's MCM"));
                Assert.Contains("3 values", leftOut);
                Assert.Contains("Marry.Title (text that looks private", leftOut);
                Assert.Contains("Marry.OpenKey (a hotkey, interface, sound or camera setting)", leftOut);
                Assert.Contains("Marry.Chance (not a plain value (a list or an object))", leftOut);
                // Left out while applying: a write that throws, a setting the server does not have, a choice out of range.
                var applied = Assert.Single(log, line => line.StartsWith("Settings sync: put 0 values", StringComparison.Ordinal));
                Assert.Contains("3 kept the value MCM read at boot", applied);
                Assert.Contains("Marry.MaxWives (could not be set)", applied);
                Assert.Contains("Marry.Gone (not a setting MCM shares on this server)", applied);
                Assert.Contains("Marry.Orientation (a choice out of range)", applied);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void A_missing_host_folder_is_looked_for_again_and_used_once_it_is_there()
        {
            var documents = Temp();
            try
            {
                var provider = FakeMcm.Host();
                var marry = (MarrySettings)provider.Settings["Marry"];
                var log = new List<string>();
                var looks = 0;
                var files = new HostSettingsFiles(() => { looks++; return SettingsFiles.FindCoopDataModSettings(documents); }, () => false, log.Add, retryPasses: 3);

                Assert.Equal(0, files.Apply(new McmSettings(provider), 1, null).Objects);
                Assert.Null(files.Root);
                Assert.Equal(1, looks);
                // Not every pass: the next look waits a few passes.
                files.Apply(new McmSettings(provider), 1, null);
                files.Apply(new McmSettings(provider), 1, null);
                Assert.Equal(1, looks);

                var server = Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "Configs", "ModSettings");
                Write(server, "Marry", "{ \"MaxWives\": 6 }");
                Assert.Equal(1, files.Apply(new McmSettings(provider), 1, null).Objects);
                Assert.Equal(2, looks);
                Assert.Equal(server, files.Root);
                Assert.Equal(6, marry.MaxWives);
                // The missing folder is logged once.
                Assert.Single(log, line => line.Contains("no CoopData\\DedicatedServer\\Configs\\ModSettings folder yet"));
            }
            finally
            {
                Directory.Delete(documents, recursive: true);
            }
        }

        [Fact]
        public void A_host_file_that_cannot_be_read_is_logged_and_read_again()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"MaxWives\": ");
                Write(root, "Kept", "{ \"Enabled\": true }");
                var provider = FakeMcm.Host();
                var marry = (MarrySettings)provider.Settings["Marry"];
                var log = new List<string>();
                var files = new HostSettingsFiles(() => root, () => false, log.Add, retryPasses: 2);

                // The good file goes in; the half written one is logged and left out.
                Assert.Equal(1, files.Apply(new McmSettings(provider), 1, null).Objects);
                Assert.Equal(4, marry.MaxWives);
                Assert.True((bool)new McmSettings(provider).PropertiesOf("Kept").Single().Read()!);
                Assert.Single(log, line => line.Contains("Marry.json could not be read (Json"));

                // Still broken on the next read: not logged again.
                files.Apply(new McmSettings(provider), 1, null);
                files.Apply(new McmSettings(provider), 1, null);
                Assert.Equal(2, files.Reads);
                Assert.Single(log, line => line.Contains("could not be read"));

                File.WriteAllText(Path.Combine(root, "Global", "Marry", "Marry.json"), "{ \"MaxWives\": 9 }");
                files.Apply(new McmSettings(provider), 1, null);
                Assert.Equal(1, files.Apply(new McmSettings(provider), 1, null).Objects);
                Assert.Equal(3, files.Reads);
                Assert.Equal(9, marry.MaxWives);
                // All read now: no more reads.
                for (var i = 0; i < 10; i++) files.Apply(new McmSettings(provider), 1, null);
                Assert.Equal(3, files.Reads);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void A_host_folder_that_throws_is_read_a_bounded_number_of_times()
        {
            var log = new List<string>();
            var files = new HostSettingsFiles(() => "C:\\nowhere", () => false, log.Add,
                (_, _) => throw new UnauthorizedAccessException("denied"), retryPasses: 1);
            var mcm = new McmSettings(FakeMcm.Host());
            for (var i = 0; i < 50; i++) Assert.Equal(0, files.Apply(mcm, 1, null).Objects);
            Assert.Equal(HostSettingsFiles.MaxReads, files.Reads);
            Assert.Equal(HostSettingsFiles.MaxReads, log.Count(line => line.Contains("failed: UnauthorizedAccessException: denied")));
            Assert.Single(log, line => line.StartsWith("Settings sync: gave up reading", StringComparison.Ordinal));
        }

        [Fact]
        public void A_settings_object_that_throws_is_tried_again_and_not_marked_done()
        {
            var root = Temp();
            try
            {
                Write(root, "Odd", "{ \"Level\": 5 }");
                var level = 1;
                var broken = true;
                var provider = new FakeProvider();
                provider.Settings["Odd"] = new FakeGlobal();
                var group = new FakeGroup();
                group.SettingProperties.Add(new FakeProperty
                {
                    Id = "Level", DisplayName = "Level", SettingType = FakeSettingType.Int,
                    PropertyReference = new FakeRef(typeof(int), () => level, v => level = (int)v!),
                });
                var definition = new FakeDefinition { SettingsId = "Odd", DisplayName = "Odd" };
                definition.SettingPropertyGroups.Add(group);
                provider.Definitions.Add(definition);
                var log = new List<string>();
                var files = new HostSettingsFiles(() => root, () => false, log.Add, retryPasses: 1);
                // The walk itself throws (a single setting's getter is handled on its own, see the test below).
                Func<string, string, bool> hostSet = (_, _) => broken ? throw new InvalidOperationException("not ready") : false;

                Assert.Equal(0, files.Apply(new McmSettings(provider), 1, hostSet).Objects);
                Assert.Contains(log, line => line.Contains("into Odd failed: InvalidOperationException: not ready. It is tried again."));
                broken = false;
                Assert.Equal(1, files.Apply(new McmSettings(provider), 1, hostSet).Objects);
                Assert.Equal(5, level);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>A file stores a dropdown's index. Choices named "1", "2", "3" must not be matched by name.</summary>
        [Fact]
        public void A_host_file_dropdown_index_is_never_read_as_a_choice_name()
        {
            var root = Temp();
            try
            {
                Write(root, "Picks", "{ \"Pick\": 2 }");
                var pick = new FakeDropdown("1", "2", "3");
                var provider = new FakeProvider();
                provider.Settings["Picks"] = new FakeGlobal();
                var group = new FakeGroup();
                group.SettingProperties.Add(new FakeProperty
                {
                    Id = "Pick", DisplayName = "Pick", SettingType = FakeSettingType.Dropdown,
                    PropertyReference = new FakeRef(typeof(FakeDropdown), () => pick, _ => { }),
                });
                var definition = new FakeDefinition { SettingsId = "Picks", DisplayName = "Picks" };
                definition.SettingPropertyGroups.Add(group);
                provider.Definitions.Add(definition);

                var result = new HostSettingsFiles(() => root, () => false, _ => { }).Apply(new McmSettings(provider), 1, null);
                Assert.Equal(1, result.Values);
                Assert.Equal(2, pick.SelectedIndex);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>The server's tick runs ApplyHostFiles, then the live channel's poll, then a campaign load runs both again.</summary>
        [Fact]
        public void Host_files_then_the_live_channel_leave_the_host_override_in_place()
        {
            var root = Temp();
            var live = Temp();
            try
            {
                Write(root, "Marry", "{ \"MaxWives\": 9, \"Title\": \"Duke\" }");
                var overrides = new SettingsOverrides();
                overrides.Set("Marry", new Dictionary<string, string> { ["MaxWives"] = "2" });
                ModSettingsLive.Stage(live, overrides);
                var provider = FakeMcm.Host();
                var marry = (MarrySettings)provider.Settings["Marry"];
                var channel = new LiveChannel(live, _ => { });
                var files = new HostSettingsFiles(() => root, () => false, _ => { });

                files.Apply(new McmSettings(provider), 1, channel.HostSet);
                channel.Poll(new McmSettings(provider), new KeepOwnList(), 1);
                Assert.Equal(2, marry.MaxWives);
                Assert.Equal("Duke", marry.Title);
                Assert.True(channel.HostSet("Marry", "MaxWives"));

                // A new campaign: the files go in again, never over the override, and the override goes in again too.
                marry.Title = "Sir";
                files.Apply(new McmSettings(provider), 2, channel.HostSet);
                Assert.Equal(2, marry.MaxWives);
                Assert.Equal("Duke", marry.Title);
                channel.Poll(new McmSettings(provider), new KeepOwnList(), 2);
                Assert.Equal(2, marry.MaxWives);

                // The settings page shows the values the server and its players have.
                var document = ModSettingsLive.ReadDocument(Path.Combine(live, LiveChannel.SettingsFileName));
                var properties = document!.Objects.Single(o => o.SettingsId == "Marry").Groups.SelectMany(g => g.Properties).ToDictionary(p => p.Id);
                Assert.Equal("2", properties["MaxWives"].Value);
                Assert.Equal("Duke", properties["Title"].Value);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(live, recursive: true);
            }
        }

        /// <summary>A host edit for a settings object the server does not have yet sets nothing, so the files still fill it.</summary>
        [Fact]
        public void A_failed_host_edit_does_not_keep_the_files_out()
        {
            var root = Temp();
            var live = Temp();
            try
            {
                Write(root, "Rates", "{ \"Rate\": 0.35 }");
                var provider = FakeMcm.Host();
                var channel = new LiveChannel(live, _ => { });
                var token = ModSettingsLive.WriteRequest(live, new LiveApplyRequest { SettingsId = "Rates", Values = new Dictionary<string, string> { ["Rate"] = "0.9" } });
                channel.Poll(new McmSettings(provider), new KeepOwnList(), 1);
                Assert.False(ModSettingsLive.TakeAck(live, token)!.Ok);
                Assert.False(channel.HostSet("Rates", "Rate"));

                var (ratesProvider, rates) = FakeMcm.RatesClient();
                provider.Settings["Rates"] = rates;
                provider.Definitions.AddRange(ratesProvider.Definitions);
                Assert.Equal(1, new HostSettingsFiles(() => root, () => false, _ => { }).Apply(new McmSettings(provider), 1, channel.HostSet).Objects);
                Assert.Equal(0.35, rates.Rate);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(live, recursive: true);
            }
        }

        /// <summary>One setting whose getter throws is named, and the other values of its object still go in.</summary>
        [Fact]
        public void A_setting_whose_getter_throws_is_named_and_the_rest_go_in()
        {
            var root = Temp();
            try
            {
                Write(root, "Odd", "{ \"Level\": 5, \"Size\": 7 }");
                int size = 1;
                var provider = new FakeProvider();
                provider.Settings["Odd"] = new FakeGlobal();
                var group = new FakeGroup();
                group.SettingProperties.Add(new FakeProperty
                {
                    Id = "Level", DisplayName = "Level", SettingType = FakeSettingType.Int,
                    PropertyReference = new FakeRef(typeof(int), () => throw new InvalidOperationException("not ready"), _ => { }),
                });
                group.SettingProperties.Add(new FakeProperty
                {
                    Id = "Size", DisplayName = "Size", SettingType = FakeSettingType.Int,
                    PropertyReference = new FakeRef(typeof(int), () => size, v => size = (int)v!),
                });
                var definition = new FakeDefinition { SettingsId = "Odd", DisplayName = "Odd" };
                definition.SettingPropertyGroups.Add(group);
                provider.Definitions.Add(definition);
                var log = new List<string>();

                var result = new HostSettingsFiles(() => root, () => false, log.Add).Apply(new McmSettings(provider), 1, null);
                Assert.Equal(1, result.Objects);
                Assert.Equal(1, result.Values);
                Assert.Equal(1, result.Skipped);
                Assert.Equal(7, size);
                Assert.Contains(log, line => line.Contains("1 kept the value MCM read at boot: Odd.Level (could not be read)."));
                Assert.DoesNotContain(log, line => line.Contains("failed:"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void A_second_file_for_an_id_and_a_bad_id_are_named()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"MaxWives\": 6 }");
                var second = Path.Combine(root, "Global", "Zother", "Marry.json");
                Directory.CreateDirectory(Path.GetDirectoryName(second)!);
                File.WriteAllText(second, "{ \"MaxWives\": 2 }");
                var badId = Path.Combine(root, "Global", "Long", new string('x', SettingsWire.MaxIdLength + 1) + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(badId)!);
                File.WriteAllText(badId, "{ \"A\": 1 }");
                var provider = FakeMcm.Host();
                var log = new List<string>();

                new HostSettingsFiles(() => root, () => false, log.Add).Apply(new McmSettings(provider), 1, null);
                Assert.Equal(6, ((MarrySettings)provider.Settings["Marry"]).MaxWives);
                var leftOut = Assert.Single(log, line => line.Contains("values in the host's settings files are not put into the server's MCM"));
                Assert.Contains(second + " (a second file for settings id Marry, the first one is used)", leftOut);
                Assert.Contains(badId + " (a file name that is no usable settings id)", leftOut);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>When an object gives up after a throw, the values it had already found it can't set are still named.</summary>
        [Fact]
        public void The_give_up_line_names_the_values_skipped_before_the_throw()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"Gone\": 1, \"MaxWives\": 3 }");
                var provider = FakeMcm.Host();
                var log = new List<string>();
                var files = new HostSettingsFiles(() => root, () => false, log.Add, retryPasses: 1);
                Func<string, string, bool> hostSet = (_, property) => property == "MaxWives" ? throw new InvalidOperationException("broken") : false;

                for (var i = 0; i < 5; i++) files.Apply(new McmSettings(provider), 1, hostSet);
                var giveUp = Assert.Single(log, line => line.Contains("It keeps the values it has for this campaign."));
                Assert.Contains("into Marry failed: InvalidOperationException: broken", giveUp);
                Assert.Contains("Before that, 1 kept the value MCM read at boot: Marry.Gone (not a setting MCM shares on this server).", giveUp);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void The_last_read_of_an_unreadable_file_does_not_promise_another()
        {
            var log = new List<string>();
            var reads = 0;
            var files = new HostSettingsFiles(() => "C:\\host", () => false, log.Add, (_, unread) =>
            {
                reads++;
                unread("C:\\host\\Global\\Marry\\Marry" + reads + ".json", "locked");
                return new List<SettingsSnapshotData>();
            }, retryPasses: 1);
            var mcm = new McmSettings(FakeMcm.Host());
            for (var i = 0; i < 40; i++) files.Apply(mcm, 1, null);

            Assert.Equal(HostSettingsFiles.MaxReads, reads);
            var lines = log.Where(line => line.Contains("could not be read (locked)")).ToList();
            Assert.Equal(HostSettingsFiles.MaxReads, lines.Count);
            Assert.All(lines.Take(HostSettingsFiles.MaxReads - 1), line => Assert.EndsWith("; it is read again later.", line));
            Assert.EndsWith("; this was the last try, so it keeps the values MCM read at boot.", lines.Last());
            Assert.DoesNotContain(log, line => line.Contains("has no Global settings files"));
        }

        /// <summary>A file locked on every read is named once, then again on the last try, which says what the server keeps.</summary>
        [Fact]
        public void A_file_locked_on_every_read_still_says_it_was_the_last_try()
        {
            var log = new List<string>();
            var reads = 0;
            var files = new HostSettingsFiles(() => "C:\\host", () => false, log.Add, (_, unread) =>
            {
                reads++;
                unread("C:\\host\\Global\\Other\\Other.json", "locked");
                if (reads == 1)
                    return new List<SettingsSnapshotData> { new("Marry", SettingsScope.Global, true, new List<string> { "i\tMaxWives\t6" }) };
                unread("C:\\host\\Global\\Marry\\Marry.json", "locked");
                return new List<SettingsSnapshotData>();
            }, retryPasses: 1);
            var provider = FakeMcm.Host();
            var marry = (MarrySettings)provider.Settings["Marry"];
            for (var i = 0; i < 40; i++) files.Apply(new McmSettings(provider), 1, null);
            Assert.Equal(HostSettingsFiles.MaxReads, reads);

            var other = log.Where(line => line.Contains("Other.json could not be read")).ToList();
            Assert.Equal(2, other.Count);
            Assert.EndsWith("; it is read again later.", other[0]);
            Assert.EndsWith("; this was the last try, so it keeps the values MCM read at boot.", other[1]);
            // Marry had a good first read, so its last try says the server keeps it.
            var marryLines = log.Where(line => line.Contains("Marry.json could not be read")).ToList();
            Assert.Equal(2, marryLines.Count);
            Assert.EndsWith("; this was the last try, so the server keeps its last good read.", marryLines[1]);
            Assert.Single(log, line => line.EndsWith("keep their last good read, or the values MCM read at boot when there was none.", StringComparison.Ordinal));

            marry.MaxWives = 1;
            files.Apply(new McmSettings(provider), 2, null);
            Assert.Equal(6, marry.MaxWives);
        }

        /// <summary>
        /// A second file for the id of a locked file is left out, so a reread keeps the locked file's last good read
        /// instead of taking the second file's values in its place.
        /// </summary>
        [Fact]
        public void A_second_file_never_stands_in_for_a_locked_one()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"MaxWives\": 6 }");
                var first = Path.Combine(root, "Global", "Marry", "Marry.json");
                var second = Path.Combine(root, "Global", "Zother", "Marry.json");
                Directory.CreateDirectory(Path.GetDirectoryName(second)!);
                File.WriteAllText(second, "{ \"MaxWives\": 2 }");
                var unread = new List<string>();
                var leftOut = new List<string>();
                List<SettingsSnapshotData> snapshots;
                using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
                    snapshots = SettingsFiles.CaptureFromFiles(root, new KeepOwnList(), (path, _) => unread.Add(path), (name, why) => leftOut.Add(name + " (" + why + ")"));

                Assert.Empty(snapshots);
                Assert.Equal([first], unread);
                Assert.Contains(second + " (a second file for settings id Marry, left out too since the first one could not be read or has no values)", leftOut);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void A_host_folder_without_global_files_says_so()
        {
            var root = Temp();
            try
            {
                var log = new List<string>();
                var files = new HostSettingsFiles(() => root, () => false, log.Add, retryPasses: 1);
                for (var i = 0; i < 5; i++) Assert.Equal(0, files.Apply(new McmSettings(FakeMcm.Host()), 1, null).Objects);
                Assert.Equal(1, files.Reads);
                Assert.Single(log, line => line == "Settings sync: " + root + " has no Global settings files with values, so the server keeps the mod settings MCM read at boot.");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void An_object_whose_values_the_host_all_set_is_not_counted_as_applied()
        {
            var root = Temp();
            try
            {
                Write(root, "Marry", "{ \"MaxWives\": 2, \"Title\": \"Duke\" }");
                var provider = FakeMcm.Host();
                var log = new List<string>();
                var result = new HostSettingsFiles(() => root, () => false, log.Add).Apply(new McmSettings(provider), 1, (_, _) => true);
                Assert.Equal(0, result.Objects);
                Assert.Equal(4, ((MarrySettings)provider.Settings["Marry"]).MaxWives);
                Assert.DoesNotContain(log, line => line.Contains("Settings sync: put "));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>A reread replaces nothing it could not read: a file locked on the second read keeps its first read.</summary>
        [Fact]
        public void A_reread_keeps_the_last_good_read_of_a_file_locked_this_time()
        {
            var log = new List<string>();
            var reads = 0;
            var files = new HostSettingsFiles(() => "C:\\host", () => false, log.Add, (_, unread) =>
            {
                reads++;
                if (reads == 1)
                {
                    unread("C:\\host\\Global\\Rates\\Rates.json", "locked");
                    return new List<SettingsSnapshotData> { new("Marry", SettingsScope.Global, true, new List<string> { "i\tMaxWives\t6" }) };
                }
                unread("C:\\host\\Global\\Marry\\Marry.json", "locked");
                return new List<SettingsSnapshotData> { new("Rates", SettingsScope.Global, true, new List<string> { "f\tRate\t0.35" }) };
            }, retryPasses: 1);
            var provider = FakeMcm.Host();
            var marry = (MarrySettings)provider.Settings["Marry"];
            var (ratesProvider, rates) = FakeMcm.RatesClient();
            provider.Settings["Rates"] = rates;
            provider.Definitions.AddRange(ratesProvider.Definitions);

            files.Apply(new McmSettings(provider), 1, null);
            files.Apply(new McmSettings(provider), 1, null);
            Assert.Equal(2, reads);
            Assert.Equal(0.35, rates.Rate);
            // A new campaign puts both in again: Marry's values from the first read were kept.
            marry.MaxWives = 1;
            rates.Rate = 0.2;
            files.Apply(new McmSettings(provider), 2, null);
            Assert.Equal(6, marry.MaxWives);
            Assert.Equal(0.35, rates.Rate);
        }

        /// <summary>A file the host deleted between two reads is not carried over; its old values never go in again.</summary>
        [Fact]
        public void A_file_deleted_between_reads_is_not_applied_any_more()
        {
            var reads = 0;
            var files = new HostSettingsFiles(() => "C:\\host", () => false, _ => { }, (_, unread) =>
            {
                reads++;
                // Another file stays locked, so the folder is read again.
                unread("C:\\host\\Global\\Other\\Other.json", "locked");
                var list = new List<SettingsSnapshotData> { new("Rates", SettingsScope.Global, true, new List<string> { "f\tRate\t0.35" }) };
                if (reads == 1) list.Add(new("Marry", SettingsScope.Global, true, new List<string> { "i\tMaxWives\t6" }));
                return list;
            }, retryPasses: 1);
            var provider = FakeMcm.Host();
            var marry = (MarrySettings)provider.Settings["Marry"];
            var (ratesProvider, rates) = FakeMcm.RatesClient();
            provider.Settings["Rates"] = rates;
            provider.Definitions.AddRange(ratesProvider.Definitions);

            files.Apply(new McmSettings(provider), 1, null);
            Assert.Equal(6, marry.MaxWives);
            files.Apply(new McmSettings(provider), 1, null);
            Assert.Equal(2, reads);

            // A new campaign: Rates goes in again, Marry's deleted file does not.
            marry.MaxWives = 1;
            rates.Rate = 0.2;
            var result = files.Apply(new McmSettings(provider), 2, null);
            Assert.Equal(1, result.Objects);
            Assert.Equal(1, marry.MaxWives);
            Assert.Equal(0.35, rates.Rate);
        }

        /// <summary>Every left out or skipped name is logged, in lines of at most NamesPerLine names, never cut to "and N more".</summary>
        [Fact]
        public void Every_name_is_logged_even_in_a_long_list()
        {
            var root = Temp();
            try
            {
                var fields = Enumerable.Range(1, 25).Select(i => "\"List" + i + "\": [1]")
                    .Concat(Enumerable.Range(1, 23).Select(i => "\"Gone" + i + "\": 1"));
                Write(root, "Marry", "{ " + string.Join(", ", fields) + ", \"MaxWives\": 3 }");
                var provider = FakeMcm.Host();
                var log = new List<string>();
                var files = new HostSettingsFiles(() => root, () => false, log.Add);
                files.Apply(new McmSettings(provider), 1, null);

                var all = string.Join("\n", log);
                for (var i = 1; i <= 25; i++) Assert.Contains("Marry.List" + i + " (not a plain value (a list or an object))", all);
                for (var i = 1; i <= 23; i++) Assert.Contains("Marry.Gone" + i + " (not a setting MCM shares on this server)", all);
                Assert.DoesNotContain(" more.", all);
                Assert.Contains(log, line => line.StartsWith("Settings sync: continued (21 to 25 of 25): ", StringComparison.Ordinal));
                Assert.Contains(log, line => line.StartsWith("Settings sync: continued (21 to 23 of 23): ", StringComparison.Ordinal));
                // No line holds more than NamesPerLine names.
                Assert.All(log, line => Assert.True(System.Text.RegularExpressions.Regex.Matches(line, @"Marry\.(List|Gone)\d+ \(").Count <= HostSettingsFiles.NamesPerLine, line));

                // The give up line names every value skipped before the throw too.
                var giveUpLog = new List<string>();
                var giveUp = new HostSettingsFiles(() => root, () => false, giveUpLog.Add, retryPasses: 1);
                Func<string, string, bool> hostSet = (_, property) => property == "MaxWives" ? throw new InvalidOperationException("broken") : false;
                for (var i = 0; i < 5; i++) giveUp.Apply(new McmSettings(FakeMcm.Host()), 1, hostSet);
                var start = giveUpLog.FindIndex(line => line.Contains("It keeps the values it has for this campaign."));
                Assert.True(start >= 0);
                var giveUpText = string.Join("\n", giveUpLog.Skip(start).Take(3));
                Assert.Contains("Before that, 23 kept the value MCM read at boot:", giveUpLog[start]);
                for (var i = 1; i <= 23; i++) Assert.Contains("Marry.Gone" + i + " (", giveUpText);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void Write(string root, string id, string json)
        {
            var folder = Path.Combine(root, "Global", id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, id + ".json"), json);
        }

        private static string SyncRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }

        private static string Temp()
        {
            var path = Path.Combine(Path.GetTempPath(), "coodsz-settings-sync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public class SettingsSyncLiveChannelTests
    {
        [Fact]
        public void The_server_describes_its_mods_for_the_host_page()
        {
            var dir = Temp();
            try
            {
                var host = FakeMcm.Host();
                var channel = new LiveChannel(dir, _ => { });
                channel.Poll(new McmSettings(host), new KeepOwnList(new[] { "Kept" }), 1);

                var document = ModSettingsLive.ReadDocument(Path.Combine(dir, LiveChannel.SettingsFileName));
                Assert.NotNull(document);
                Assert.True(document!.McmRunning);
                Assert.Equal(new[] { "Marry", "Kept", "Campaign" }, document.Objects.Select(o => o.SettingsId));
                Assert.True(document.Objects.Single(o => o.SettingsId == "Kept").PlayersOwn);
                var marry = document.Objects[0];
                var properties = marry.Groups.SelectMany(g => g.Properties).ToDictionary(p => p.Id);
                Assert.Equal(new[] { "Polygamy", "Debug", "MaxWives", "Chance", "Title", "Orientation" }, properties.Keys);
                Assert.Equal("true", properties["Polygamy"].Value);
                Assert.Equal("int", properties["MaxWives"].Kind);
                Assert.Equal(10, properties["MaxWives"].Max);
                Assert.Equal("enum", properties["Orientation"].Kind);
                Assert.Equal("Bi", properties["Orientation"].Value);
                Assert.Equal(new[] { "Straight", "Gay", "Bi" }, properties["Orientation"].Choices);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Overrides_staged_at_launch_are_set_in_memory_and_acknowledged()
        {
            var dir = Temp();
            try
            {
                var overrides = new SettingsOverrides();
                overrides.Set("Marry", new Dictionary<string, string> { ["MaxWives"] = "50", ["Orientation"] = "Gay", ["Polygamy"] = "false", ["Gone"] = "1" });
                overrides.Set("NotYet", new Dictionary<string, string> { ["X"] = "true" });
                ModSettingsLive.Stage(dir, overrides);

                var client = FakeMcm.Client();
                var channel = new LiveChannel(dir, _ => { });
                channel.Poll(new McmSettings(client.Provider), new KeepOwnList(), 1);

                Assert.Equal(3, client.Marry.MaxWives);
                Assert.Equal(1, client.Marry.Orientation.SelectedIndex);
                Assert.False(client.Marry.Polygamy);
                var ack = File.ReadAllText(Path.Combine(dir, LiveChannel.OverridesAckFileName));
                Assert.Contains("NotYet", ack);
                Assert.Contains("skipped Gone", ack);
                // What the overrides set stays live over the host's settings files; what was skipped or waits does not.
                Assert.True(channel.HostSet("Marry", "MaxWives"));
                Assert.True(channel.HostSet("Marry", "Polygamy"));
                Assert.False(channel.HostSet("Marry", "Gone"));
                Assert.False(channel.HostSet("Marry", "Chance"));
                Assert.False(channel.HostSet("NotYet", "X"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void A_host_edit_while_running_is_applied_and_answered()
        {
            var dir = Temp();
            try
            {
                var client = FakeMcm.Client();
                var channel = new LiveChannel(dir, _ => { });
                var token = ModSettingsLive.WriteRequest(dir, new LiveApplyRequest
                {
                    SettingsId = "Marry",
                    Values = new Dictionary<string, string> { ["Chance"] = "0.5", ["Title"] = "Duke", ["Orientation"] = "Nope" },
                });
                channel.Poll(new McmSettings(client.Provider), new KeepOwnList(), 1);

                var ack = ModSettingsLive.TakeAck(dir, token);
                Assert.NotNull(ack);
                Assert.True(ack!.Ok);
                Assert.Equal(2, ack.Changed);
                Assert.Contains("skipped Orientation", ack.Report);
                Assert.Equal(0.5f, client.Marry.Chance);
                Assert.Equal("Duke", client.Marry.Title);
                Assert.Empty(Directory.GetFiles(dir, "apply-*.json"));
                Assert.Null(ModSettingsLive.TakeAck(dir, token));
                Assert.True(channel.HostSet("Marry", "Chance"));
                Assert.True(channel.HostSet("Marry", "Title"));
                Assert.False(channel.HostSet("Marry", "Orientation"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// The value a host edit decides for a whole number setting is an int. A long setting that already holds it
        /// used to fail the exact compare (a boxed long is never Equals a boxed int), so it was written again, and a
        /// setter that takes only a long threw and the edit reported the value as skipped.
        /// </summary>
        [Fact]
        public void A_host_edit_compares_numbers_as_numbers_before_writing()
        {
            long count = 5;
            int writes = 0;
            var provider = new FakeProvider();
            provider.Settings["Big"] = new FakeGlobal();
            var group = new FakeGroup();
            group.SettingProperties.Add(new FakeProperty
            {
                Id = "Count", DisplayName = "Count", SettingType = FakeSettingType.Int, MinValue = 0, MaxValue = 100,
                PropertyReference = new FakeRef(typeof(long), () => count, v => { writes++; count = (long)v!; }),
            });
            var definition = new FakeDefinition { SettingsId = "Big", DisplayName = "Big" };
            definition.SettingPropertyGroups.Add(group);
            provider.Definitions.Add(definition);
            var property = new McmSettings(provider).PropertiesOf("Big").Single();

            Assert.True(LiveChannel.TryApplyText(property, "5"));
            Assert.Equal(0, writes);
            var ack = LiveChannel.Apply(new McmSettings(provider), "Big", new Dictionary<string, string> { ["Count"] = "5" });
            Assert.Equal(1, ack["Changed"]);
            Assert.Empty((List<string>)ack["Skipped"]!);
            Assert.Equal(0, writes);
            // A new value goes in as the setting's own long.
            Assert.True(LiveChannel.TryApplyText(property, "7"));
            Assert.Equal(1, writes);
            Assert.Equal(7L, count);

            Assert.True(LiveChannel.SameExactly(5L, 5));
            Assert.True(LiveChannel.SameExactly(2, 2.0));
            Assert.True(LiveChannel.SameExactly(0.5f, 0.5));
            Assert.False(LiveChannel.SameExactly(0.1f, 0.1));
            Assert.False(LiveChannel.SameExactly(long.MaxValue, long.MaxValue - 1));
            Assert.False(LiveChannel.SameExactly(1, 2));
            Assert.False(LiveChannel.SameExactly("1", 1));
            Assert.False(LiveChannel.SameExactly(true, 1));
            Assert.True(LiveChannel.SameExactly("Duke", "Duke"));
        }

        [Fact]
        public void The_host_page_keeps_edits_and_overrides_apart()
        {
            var document = new LiveSettingsDocument
            {
                Objects = { new LiveSettingsObject
                {
                    SettingsId = "Marry", DisplayName = "Marry Anyone", ModuleId = "MarryAnyone",
                    Groups = { new LiveSettingsGroup { Name = "General", Properties =
                    {
                        new LiveSettingsProperty { Id = "MaxWives", Kind = "int", Value = "1", Min = 1, Max = 3, Editable = true },
                        new LiveSettingsProperty { Id = "Polygamy", Kind = "bool", Value = "false", Editable = true },
                    } } },
                } },
            };
            var overrides = new SettingsOverrides();
            overrides.Set("Marry", new Dictionary<string, string> { ["Polygamy"] = "true" });

            var shown = ModSettingsMerge.Merge(new List<ModSettingObject>(), document, overrides, offline: true, _ => true);
            var item = Assert.Single(shown);
            Assert.Equal("MarryAnyone", item.LockKey);
            var polygamy = item.AllProperties.Single(p => p.Id == "Polygamy");
            Assert.True(polygamy.BoolValue);
            Assert.False(polygamy.IsDirty);
            var wives = item.AllProperties.Single(p => p.Id == "MaxWives");
            wives.Value = "9";
            Assert.True(wives.HasError);
            Assert.Null(ModSettingsMerge.Dirty(item, out var problem));
            Assert.NotNull(problem);
            wives.Value = "2";
            Assert.Equal(new Dictionary<string, string> { ["MaxWives"] = "2" }, ModSettingsMerge.Dirty(item, out _));
            Assert.Equal("1 to 3", wives.RangeText);
            wives.Revert();
            Assert.Equal("1", wives.Value);
        }

        private static string Temp()
        {
            var path = Path.Combine(Path.GetTempPath(), "coodsz-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>
    /// The client's log never goes quiet: the live test of v1.2.63 saw "using the host's mod settings" and then
    /// nothing at all, because the snapshot never arrived and nothing said so.
    /// </summary>
    public class SettingsSyncClientLogTests
    {
        private static SettingsSnapshotData Drift() =>
            new("SettlementCultureDrift_v1", SettingsScope.Global, false, new List<string> { "i\tFullSwitchDays\t200" });

        [Fact]
        public void A_settings_object_this_pc_does_not_have_is_named_in_the_log()
        {
            var client = FakeMcm.Client();
            var report = SettingsApplier.Apply(new McmSettings(client.Provider), Drift(), new SyncSession(), 1);
            Assert.True(report.ObjectMissing);
            Assert.Equal(0, report.Set);
            Assert.Equal("Settings sync: SettlementCultureDrift_v1 before the save loads: it is not in this PC's mod settings, skipped; it is tried again when it appears.",
                SyncNotes.Applied("SettlementCultureDrift_v1", "before the save loads", report));
        }

        [Fact]
        public void A_missing_setting_inside_a_known_object_is_not_a_missing_object()
        {
            var client = FakeMcm.Client();
            var data = new SettingsSnapshotData("Marry", SettingsScope.Global, false, new List<string> { "b\tNoSuchSetting\t1" });
            var report = SettingsApplier.Apply(new McmSettings(client.Provider), data, new SyncSession(), 1);
            Assert.False(report.ObjectMissing);
            Assert.Equal("Settings sync: Marry from the host: 0 set, 1 skipped (1 not on this PC).", SyncNotes.Applied("Marry", "from the host", report));
        }

        [Fact]
        public void An_object_the_mod_makes_later_is_applied_once_it_appears()
        {
            var client = FakeMcm.Client();
            var definition = client.Provider.Definitions.Single();
            client.Provider.Definitions.Clear();
            client.Provider.Settings.Clear();
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();
            var data = new SettingsSnapshotData("Marry", SettingsScope.Global, false, new List<string> { "b\tPolygamy\t1" });
            var missing = new MissingObjects();

            Assert.True(SettingsApplier.Apply(mcm, data, session, 1).ObjectMissing);
            Assert.True(missing.Add("Marry"));
            Assert.False(missing.Add("Marry"));
            Assert.Empty(missing.Due(1f));

            client.Provider.Definitions.Add(definition);
            client.Provider.Settings["Marry"] = client.Marry;
            Assert.Equal(new[] { "Marry" }, missing.Due(MissingObjects.RetrySeconds));
            var report = SettingsApplier.Apply(mcm, data, session, 1);
            Assert.False(report.ObjectMissing);
            Assert.Equal(1, report.Set);
            Assert.True(client.Marry.Polygamy);
            missing.Remove("Marry");
            Assert.Equal(0, missing.Count);
            Assert.Empty(missing.Due(MissingObjects.RetrySeconds));
        }

        /// <summary>
        /// v1.2.64: a settings object of a mod this PC does not have was retried every 5 seconds for the whole
        /// session, with a full MCM rebuild each time. Now only a listed id is tried every 5 seconds; an unlisted
        /// one backs off to 5, 15, 30 and then 60 seconds, and every id is given up after 10 minutes, once.
        /// </summary>
        [Fact]
        public void A_missing_object_backs_off_unless_listed_and_is_given_up_after_ten_minutes()
        {
            var missing = new MissingObjects();
            Assert.True(missing.Add("NotHere"));
            Assert.True(missing.Add("Later", listed: true));
            Assert.False(missing.IsListed("NotHere"));
            Assert.True(missing.IsListed("Later"));

            // One second per tick: the time of each try.
            var tries = new Dictionary<string, List<int>>(StringComparer.Ordinal) { ["NotHere"] = new(), ["Later"] = new() };
            var expired = new List<string>();
            for (int second = 1; second <= 700; second++)
            {
                foreach (string id in missing.Due(1f)) tries[id].Add(second);
                expired.AddRange(missing.TakeExpired());
            }

            Assert.Equal(new[] { 5, 20, 50, 110, 170 }, tries["NotHere"].Take(5));
            Assert.All(tries["NotHere"].Skip(3).Zip(tries["NotHere"].Skip(4), (a, b) => b - a), gap => Assert.Equal(60, gap));
            Assert.Equal(119, tries["Later"].Count);
            Assert.All(tries["Later"].Zip(tries["Later"].Skip(1), (a, b) => b - a), gap => Assert.Equal(5, gap));
            Assert.True(tries["NotHere"].Max() < 600 && tries["Later"].Max() < 600);
            Assert.Equal(new[] { "Later", "NotHere" }, expired);
            Assert.Equal(0, missing.Count);

            // Given up stays given up for the session; a campaign load gives it a fresh start.
            Assert.False(missing.Add("NotHere"));
            Assert.Equal(0, missing.Count);
            missing.Rearm();
            Assert.True(missing.Add("NotHere"));

            // An id that turns up in MCM's list goes back to the short retry.
            missing.Due(5f);
            missing.Due(15f);
            Assert.Equal(30f, missing.IntervalOf("NotHere"));
            Assert.False(missing.Add("NotHere", listed: true));
            Assert.Equal(MissingObjects.RetrySeconds, missing.IntervalOf("NotHere"));
            Assert.Equal(new[] { "NotHere" }, missing.Due(MissingObjects.RetrySeconds));
            Assert.Equal("Settings sync: NotHere is still not on this PC after 10 minutes, so it is no longer tried this session.",
                SyncNotes.GaveUp("NotHere", 10));
        }

        private static readonly SettingsSnapshotData Polygamy = new("Marry", SettingsScope.Global, false, new List<string> { "b\tPolygamy\t1" });

        /// <summary>
        /// The v1.2.63 second live test: on a Coop client CoopModPatch filters MCM's SettingsDefinitions and
        /// drops the mods the server controls, so a lookup through that list said "not in this PC's mod settings"
        /// for SettlementCultureDrift_v1 although the mod and its settings object were there.
        /// </summary>
        [Fact]
        public void A_mod_hidden_from_mcms_definitions_list_still_gets_the_hosts_values()
        {
            var marry = new MarrySettings();
            var provider = new FakeProvider();
            provider.Settings["Marry"] = new SelfDescribingSettings("Marry", FakeMcm.Definition("Marry", marry, 3));
            // What CoopModPatch's postfix leaves: the definitions list without the server controlled id.
            Assert.Empty(provider.Definitions);

            var report = SettingsApplier.Apply(new McmSettings(provider), Polygamy, new SyncSession(), 1);
            Assert.False(report.ObjectMissing);
            Assert.Equal(1, report.Set);
            Assert.True(marry.Polygamy);
        }

        [Fact]
        public void When_mcm_swaps_the_settings_object_the_new_one_gets_the_values_and_the_restore()
        {
            var first = new MarrySettings();
            var second = new MarrySettings();
            var provider = new FakeProvider();
            provider.Settings["Marry"] = new SelfDescribingSettings("Marry", FakeMcm.Definition("Marry", first, 3));
            var mcm = new McmSettings(provider);
            var session = new SyncSession();

            Assert.Equal(1, SettingsApplier.Apply(mcm, Polygamy, session, 1).Set);
            Assert.True(first.Polygamy);

            // MCM reloads the object (a campaign start, another mod's settings reload); the cache must not write the old one.
            provider.Settings["Marry"] = new SelfDescribingSettings("Marry", FakeMcm.Definition("Marry", second, 3));
            Assert.Equal(1, SettingsApplier.Apply(mcm, Polygamy, session, 1).Set);
            Assert.True(second.Polygamy);

            Assert.Equal(1, session.Restore());
            Assert.False(second.Polygamy);
        }

        [Fact]
        public void An_id_mcm_lists_before_its_object_exists_waits_and_then_applies()
        {
            var client = FakeMcm.Client();
            client.Provider.Settings.Clear();
            var mcm = new McmSettings(client.Provider);
            var session = new SyncSession();

            var report = SettingsApplier.Apply(mcm, Polygamy, session, 1);
            Assert.True(report.ObjectMissing);
            Assert.True(report.NotCreatedYet);
            Assert.Equal("Settings sync: Marry once it appeared: MCM lists it on this PC but has not made its settings object yet, skipped; it is tried again when it appears.",
                SyncNotes.Applied("Marry", "once it appeared", report));
            Assert.False(client.Marry.Polygamy);

            client.Provider.Settings["Marry"] = client.Marry;
            report = SettingsApplier.Apply(mcm, Polygamy, session, 1);
            Assert.False(report.ObjectMissing);
            Assert.True(client.Marry.Polygamy);
        }

        [Fact]
        public void Every_received_snapshot_gets_a_line_and_a_reason_when_unused()
        {
            Assert.Equal("Settings sync: received SettlementCultureDrift_v1 from the host (Global, 1 value).", SyncNotes.Received("SettlementCultureDrift_v1", 0, 1));
            Assert.Equal("Settings sync: received Marry from the host (PerCampaign, 3 values).", SyncNotes.Received("Marry", 1, 3));
            // A bad id from the wire never reaches the log as it came.
            Assert.Equal("Settings sync: received a settings object with an invalid id from the host (Global, 0 values).", SyncNotes.Received("bad\nid", 0, 0));
            Assert.Null(SyncNotes.Rejected(true, SettingsWire.ProtocolVersion, "Marry", 0));
            Assert.Contains("off", SyncNotes.Rejected(false, SettingsWire.ProtocolVersion, "Marry", 0));
            Assert.Contains("version", SyncNotes.Rejected(true, SettingsWire.ProtocolVersion + 1, "Marry", 0));
            Assert.Contains("not a valid", SyncNotes.Rejected(true, SettingsWire.ProtocolVersion, "", 0));
            Assert.Contains("only Global and PerCampaign", SyncNotes.Rejected(true, SettingsWire.ProtocolVersion, "Marry", (int)SettingsScope.PerSave));
        }

        [Fact]
        public void The_end_of_a_session_is_logged_even_with_nothing_to_put_back()
        {
            Assert.Equal("Settings sync: session ended; nothing to put back, no host value was in use. Your settings files were never changed.", SyncNotes.SessionEnded(0, 0, 0));
            Assert.StartsWith("Settings sync: session ended; put back 2 of your own values.", SyncNotes.SessionEnded(1, 2, 0));
            Assert.EndsWith("they stay guarded until the game closes.", SyncNotes.SessionEnded(1, 1, 1));
        }

        [Fact]
        public void The_client_logs_every_snapshot_and_every_session_end()
        {
            var source = File.ReadAllText(Path.Combine(SettingsSyncBuildTests.Root(), "compat", "CoodszSettingsSync", "src", "Client", "ClientSync.cs"));
            var receive = source.Substring(source.IndexOf("internal static void Receive(", StringComparison.Ordinal));
            receive = receive.Substring(0, receive.IndexOf("internal static void CampaignReady(", StringComparison.Ordinal));
            // The first thing Receive does with a snapshot is log it.
            Assert.True(receive.IndexOf("SyncNotes.Received(", StringComparison.Ordinal) < receive.IndexOf("SyncNotes.Rejected(", StringComparison.Ordinal));
            var ended = source.Substring(source.IndexOf("internal static void SessionEnded(", StringComparison.Ordinal));
            ended = ended.Substring(0, ended.IndexOf("private static void ApplyOne(", StringComparison.Ordinal));
            Assert.Contains("_log(SyncNotes.SessionEnded(", ended);
            Assert.DoesNotContain("if (synced > 0)", ended);
            Assert.Contains("SyncNotes.NoAnswer(", source);
        }
    }

    public class SettingsSyncBuildTests
    {
        internal static string Root() => RepoRoot();

        /// <summary>
        /// The v1.2.63 root cause. A player asks for the host's settings right after Coop's module check, when
        /// Coop's join gate (ConnectionMessageQueue) still has them in its Dropping phase; CoopServer.Send drops
        /// every packet for such a player without a word. Only INetwork.SendImmediate gets through, as Coop's own
        /// join messages do. The built server DLL must never call INetwork.Send.
        /// </summary>
        [Fact]
        public void The_server_sends_snapshots_past_coops_join_gate()
        {
            var calls = NetworkCalls(ServerDll);
            Assert.Contains("SendImmediate", calls);
            Assert.DoesNotContain("Send", calls);
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszSettingsSync", "src", "Server", "ServerSync.cs"));
            Assert.DoesNotContain("network.Send(", source);
            Assert.Contains("network.SendImmediate(peer, ToMessage(snapshot))", source);
        }

        /// <summary>The INetwork members a built DLL calls, read from its metadata without loading it.</summary>
        private static List<string> NetworkCalls(string path)
        {
            Assert.True(File.Exists(path), path + " is missing; run the module's build.ps1.");
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var result = new List<string>();
            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                if (member.Parent.Kind != HandleKind.TypeReference) continue;
                var parent = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                if (reader.GetString(parent.Namespace) == "Common.Network" && reader.GetString(parent.Name) == "INetwork")
                    result.Add(reader.GetString(member.Name));
            }
            return result;
        }

        private const string Messages = "CoodszSettingsSync.Messages.";
        private static readonly string[] MessageTypes =
        {
            Messages + "NetworkModSettingsRequest", Messages + "NetworkModSettingsSnapshot",
            Messages + "NetworkPvpCountdown", Messages + "NetworkPvpCountdownStop", Messages + "NetworkModSettingsNone",
        };

        private static string ServerDll => Path.Combine(RepoRoot(), "src", "CoodszHost", "ServerTools", "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "CoodszSettingsSync.Coop.dll");
        private static string ClientDll => Path.Combine(RepoRoot(), "compat", "CoodszJoinPassword", "module", "bin", "Win64_Shipping_Client", "CoodszSettingsSync.Coop.dll");

        [Fact]
        public void Both_builds_carry_the_same_message_types_and_members()
        {
            var server = Types(ServerDll);
            var client = Types(ClientDll);
            foreach (var name in MessageTypes)
            {
                Assert.True(server.ContainsKey(name), "server build lacks " + name);
                Assert.True(client.ContainsKey(name), "client build lacks " + name);
                Assert.Equal(server[name], client[name]);
            }
        }

        [Fact]
        public void Each_build_has_only_its_own_side_of_the_coop_handler()
        {
            var server = Types(ServerDll).Keys.ToList();
            var client = Types(ClientDll).Keys.ToList();
            Assert.Contains("Coop.Core.Server.Services.CoodszSettingsSync.CoodszSettingsSyncServerHandler", server);
            Assert.DoesNotContain(server, t => t.StartsWith("Coop.Core.Client.", StringComparison.Ordinal));
            Assert.Contains("Coop.Core.Client.Services.CoodszSettingsSync.CoodszSettingsSyncClientHandler", client);
            Assert.DoesNotContain(client, t => t.StartsWith("Coop.Core.Server.", StringComparison.Ordinal));
            Assert.Contains("CoodszSettingsSync.ServerSync", server);
            Assert.Contains("CoodszSettingsSync.ClientSync", client);
            // The PvP countdown: the server sends it, the client shows it.
            Assert.Contains("Coop.Core.Server.Services.CoodszSettingsSync.CoodszPvpCountdownServerHandler", server);
            Assert.Contains("CoodszSettingsSync.PvpCountdownServer", server);
            Assert.Contains("Coop.Core.Client.Services.CoodszSettingsSync.CoodszPvpCountdownClientHandler", client);
            Assert.Contains("CoodszSettingsSync.PvpCountdownClient", client);
            Assert.DoesNotContain("CoodszSettingsSync.PvpCountdownClient", server);
        }

        // A player's change to a synced value that goes back to the host's says so on screen, once per mod per session.
        [Fact]
        public void The_client_build_shows_the_host_sets_this_once_per_mod_per_session()
        {
            // Mod A twice, then mod B, then A again after a new session: one log line per session, one on screen per mod.
            var notes = new HeldNotes();
            Assert.True(notes.Held("A_v1", out var show));
            Assert.True(show);
            Assert.False(notes.Held("A_v1", out show));
            Assert.False(show);
            Assert.False(notes.Held("B_v1", out show));
            Assert.True(show);
            notes.NewSession();
            Assert.True(notes.Held("A_v1", out show));
            Assert.True(show);

            // The mod's name from MCM without its localization tag, else its id, in the game's language.
            Assert.Equal("The host sets this (Better Battles). Your own value comes back when you leave the server.",
                HeldNotes.Text("en", HeldNotes.Name("{=bb01}Better Battles", "BB_v1")));
            Assert.Equal("BB_v1", HeldNotes.Name("  ", "BB_v1"));
            Assert.Equal("The host sets this. Your own value comes back when you leave the server.", HeldNotes.Text("xx", ""));
            Assert.Contains("(Better Battles)", HeldNotes.Text(PvpCountdownText.LanguageFor("Deutsch"), "Better Battles"));
            Assert.StartsWith("Der Host", HeldNotes.Text(PvpCountdownText.LanguageFor("Deutsch"), "Better Battles"));
            Assert.Equal(PvpCountdownText.Languages.OrderBy(code => code), HeldNotes.Languages.OrderBy(code => code));

            // Only the build with HeldNotes has its German line; the server build has none of it.
            var text = System.Text.Encoding.Unicode.GetBytes("Der Host legt das fest");
            Assert.True(File.ReadAllBytes(ClientDll).AsSpan().IndexOf(text) >= 0, "the client build is older: run compat\\CoodszJoinPassword\\build.ps1");
            Assert.True(File.ReadAllBytes(ServerDll).AsSpan().IndexOf(text) < 0);
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszSettingsSync", "src", "Client", "ClientSync.cs"));
            Assert.Contains("Held.NewSession();", SourceFiles.Method(source, "private static void StartSession(object owner)"));
            var note = SourceFiles.Method(source, "private static void NoteHeld(string? id)");
            foreach (var part in new[] { "Held.Held(id, out bool show)", "HeldNotes.Text(PvpCountdownText.LanguageFor(PvpCountdownClient.GameLanguage()), HeldNotes.Name(name, id))" })
                Assert.Contains(part, note);
        }

        [Fact]
        public void Both_builds_compile_the_one_shared_messages_file()
        {
            var root = RepoRoot();
            Assert.True(File.Exists(Path.Combine(root, "compat", "CoodszSettingsSync", "src", "Shared", "SettingsSyncMessages.cs")));
            var server = File.ReadAllText(Path.Combine(root, "compat", "CoodszSettingsSync", "CoodszSettingsSync.Server.csproj"));
            Assert.Contains(@"<Compile Include=""src\Shared\*.cs"" />", server);
            var client = File.ReadAllText(Path.Combine(root, "compat", "CoodszJoinPassword", "build.ps1"));
            Assert.Contains(@"(Join-Path $sync 'src\Shared')", client);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "compat", "CoodszSettingsSync", "src", "Server"), "*Messages*.cs"));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "compat", "CoodszSettingsSync", "src", "Client"), "*Messages*.cs"));
        }

        [Fact]
        public void Files_that_reuse_modderlords_code_credit_it()
        {
            var root = Path.Combine(RepoRoot(), "compat", "CoodszSettingsSync", "src");
            foreach (var name in new[] { @"Shared\SettingsWire.cs", @"Shared\McmSettings.cs", @"Shared\CoopProbe.cs", @"Shared\SettingsSyncMessages.cs", @"Server\SettingsFiles.cs", @"Server\ServerSync.cs", @"Server\LiveChannel.cs", @"Client\ClientSync.cs" })
                Assert.Contains("Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.", File.ReadAllText(Path.Combine(root, name)));
        }

        /// <summary>Full type name to its sorted property names, read from the DLL without loading it.</summary>
        private static Dictionary<string, string> Types(string path)
        {
            Assert.True(File.Exists(path), path + " is missing; run the module's build.ps1.");
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                var name = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
                var properties = type.GetProperties().Select(p => reader.GetString(reader.GetPropertyDefinition(p).Name)).OrderBy(n => n, StringComparer.Ordinal);
                result[name] = string.Join(",", properties);
            }
            return result;
        }

        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}

// ---- stand ins shaped like Coop's and MCM's types ------------------------------------------------------

namespace SettingsSyncFakes
{
    public interface FakeBroker
    {
        void Subscribe<T>(Action<T> subscription);
        void Unsubscribe<T>(Action<T> subscription);
    }

    public sealed class FakePayload<T>
    {
        public object? Who { get; set; }
        public T? What { get; set; }
    }

    public interface FakeNetwork
    {
        void Send(object peer, object message);
        void SendAll(object message);
    }

    public interface FakeNetworkWithoutSendAll
    {
        void Send(object peer, object message);
    }

    public static class FakeModInformation
    {
        public static bool IsServer { get; set; }
    }

    public enum FakeSettingType { NONE = -1, Bool, Int, Float, String, Dropdown, Button }

    public sealed class FakeRef
    {
        private readonly Func<object?> _get;
        private readonly Action<object?> _set;

        public FakeRef(Type type, Func<object?> get, Action<object?> set)
        {
            Type = type;
            _get = get;
            _set = set;
        }

        public Type Type { get; }
        public object? Value { get => _get(); set => _set(value); }
    }

    public sealed class FakeDropdown : List<string>
    {
        public FakeDropdown(params string[] items) : base(items) { }
        public int SelectedIndex { get; set; }
    }

    public sealed class FakeProperty
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public FakeSettingType SettingType { get; set; }
        public FakeRef PropertyReference { get; set; } = null!;
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
    }

    public sealed class FakeGroup
    {
        public string GroupName { get; set; } = "General";
        public List<FakeProperty> SettingProperties { get; } = new();
        public List<FakeGroup> SubGroups { get; } = new();
    }

    public sealed class FakeDefinition
    {
        public string SettingsId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<FakeGroup> SettingPropertyGroups { get; } = new();
    }

    public sealed class FakeProvider
    {
        public List<FakeDefinition> Definitions { get; } = new();
        public Dictionary<string, object> Settings { get; } = new();
        public int Reads { get; private set; }
        public IEnumerable SettingsDefinitions { get { Reads++; return Definitions; } }
        public object? GetSettings(string id) => Settings.GetValueOrDefault(id);
        public void SaveSettings(object settings) { }
    }

    public class FakeGlobal : MCM.Abstractions.Base.Global.GlobalSettings { }
    public class FakeCampaign : MCM.Abstractions.Base.PerCampaign.PerCampaignSettings { }
    public class FakeSave : MCM.Abstractions.Base.PerSave.PerSaveSettings { }

    public sealed class MarrySettings : FakeGlobal
    {
        public string Id => "Marry";
        public bool FailWrites { get; set; }
        public bool Polygamy { get; set; }
        public bool Debug { get; set; }
        public int MaxWives { get; set; } = 1;
        public float Chance { get; set; } = 0.1f;
        public string Title { get; set; } = "Sir";
        public string OpenKey { get; set; } = "Key.F5";
        public FakeDropdown Orientation { get; } = new("Straight", "Gay", "Bi");
    }

    /// <summary>A settings object that builds its own groups, as MCM's GetSettingPropertyGroups does for a real one.</summary>
    public sealed class SelfDescribingSettings : FakeGlobal
    {
        private readonly FakeDefinition _definition;

        public SelfDescribingSettings(string id, FakeDefinition definition)
        {
            Id = id;
            _definition = definition;
        }

        public string Id { get; }
        public List<FakeGroup> GetSettingPropertyGroups() => _definition.SettingPropertyGroups;
    }

    public sealed class FakeClient
    {
        public FakeProvider Provider { get; } = new();
        public MarrySettings Marry { get; } = new();
    }

    /// <summary>Settings with double properties, as some mods use; MCM sends them as Float.</summary>
    public sealed class RateSettings : FakeGlobal
    {
        public string Id => "Rates";
        public double Rate { get; set; } = 0.2;
        public double Spare { get; set; } = 0.5;
    }

    public static class FakeMcm
    {
        public static FakeProvider Host()
        {
            var marry = new MarrySettings { Polygamy = true, MaxWives = 4, Chance = 0.75f, Title = "Lord" };
            marry.Orientation.SelectedIndex = 2;
            var provider = new FakeProvider();
            provider.Settings["Marry"] = marry;
            provider.Definitions.Add(Definition("Marry", marry, maxWives: 10));
            provider.Settings["Kept"] = new FakeGlobal();
            provider.Definitions.Add(Simple("Kept", "Enabled"));
            provider.Settings["Saved"] = new FakeSave();
            provider.Definitions.Add(Simple("Saved", "Enabled"));
            provider.Settings["Campaign"] = new FakeCampaign();
            provider.Definitions.Add(Simple("Campaign", "Enabled"));
            provider.Settings["RTSCamera"] = new FakeGlobal();
            provider.Definitions.Add(Simple("RTSCamera", "Enabled"));
            return provider;
        }

        public static FakeClient Client()
        {
            var client = new FakeClient();
            client.Provider.Settings["Marry"] = client.Marry;
            client.Provider.Definitions.Add(Definition("Marry", client.Marry, maxWives: 3));
            return client;
        }

        public static FakeDefinition Definition(string id, MarrySettings s, int maxWives)
        {
            var group = new FakeGroup { GroupName = "General" };
            group.SettingProperties.Add(Prop("Polygamy", FakeSettingType.Bool, typeof(bool), () => s.Polygamy, v => s.Polygamy = (bool)v!));
            group.SettingProperties.Add(Prop("Debug", FakeSettingType.Bool, typeof(bool), () => s.Debug, v => s.Debug = (bool)v!));
            group.SettingProperties.Add(Prop("MaxWives", FakeSettingType.Int, typeof(int), () => s.MaxWives, v =>
            {
                if (s.FailWrites) throw new InvalidOperationException("read only now");
                s.MaxWives = (int)v!;
            }, 1, maxWives));
            var sub = new FakeGroup { GroupName = "General/Odds" };
            sub.SettingProperties.Add(Prop("Chance", FakeSettingType.Float, typeof(float), () => s.Chance, v => s.Chance = (float)v!, 0, 1));
            sub.SettingProperties.Add(Prop("Title", FakeSettingType.String, typeof(string), () => s.Title, v => s.Title = (string)v!));
            sub.SettingProperties.Add(Prop("Orientation", FakeSettingType.Dropdown, typeof(FakeDropdown), () => s.Orientation, _ => { }));
            sub.SettingProperties.Add(Prop("OpenKey", FakeSettingType.String, typeof(string), () => s.OpenKey, v => s.OpenKey = (string)v!));
            sub.SettingProperties.Add(Prop("Reset", FakeSettingType.Button, typeof(Action), () => null, _ => { }));
            group.SubGroups.Add(sub);
            var definition = new FakeDefinition { SettingsId = id, DisplayName = id };
            definition.SettingPropertyGroups.Add(group);
            return definition;
        }

        /// <summary>A client with only the double settings. The setters take a double only, so a float would throw.</summary>
        public static (FakeProvider Provider, RateSettings Rates) RatesClient()
        {
            var rates = new RateSettings();
            var provider = new FakeProvider();
            provider.Settings["Rates"] = rates;
            var group = new FakeGroup();
            group.SettingProperties.Add(Prop("Rate", FakeSettingType.Float, typeof(double), () => rates.Rate, v => rates.Rate = (double)v!, 0, 1));
            group.SettingProperties.Add(Prop("Spare", FakeSettingType.Float, typeof(double), () => rates.Spare, v => rates.Spare = (double)v!, 0, 1));
            var definition = new FakeDefinition { SettingsId = "Rates", DisplayName = "Rates" };
            definition.SettingPropertyGroups.Add(group);
            provider.Definitions.Add(definition);
            return (provider, rates);
        }

        private static FakeDefinition Simple(string id, string property)
        {
            var value = false;
            var group = new FakeGroup();
            group.SettingProperties.Add(Prop(property, FakeSettingType.Bool, typeof(bool), () => value, v => value = (bool)v!));
            var definition = new FakeDefinition { SettingsId = id, DisplayName = id };
            definition.SettingPropertyGroups.Add(group);
            return definition;
        }

        private static FakeProperty Prop(string id, FakeSettingType type, Type valueType, Func<object?> get, Action<object?> set, decimal min = 0, decimal max = 0) =>
            new() { Id = id, DisplayName = id, SettingType = type, PropertyReference = new FakeRef(valueType, get, set), MinValue = min, MaxValue = max };
    }
}

namespace MCM.Abstractions.Base.Global
{
    public abstract class GlobalSettings { }
}

namespace MCM.Abstractions.Base.PerCampaign
{
    public abstract class PerCampaignSettings { }
}

namespace MCM.Abstractions.Base.PerSave
{
    public abstract class PerSaveSettings { }
}
