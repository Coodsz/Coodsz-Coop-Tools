using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Profile;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class ReleaseGateTests
{
    [Fact]
    public void Client_and_server_locked_setup_names_match()
    {
        Assert.Equal(StableClientSetup.LegacyName, StableCoop.LegacyName);
        Assert.Equal(StableClientSetup.VanillaName, StableCoop.VanillaName);
        Assert.Equal(StableClientSetup.MoreNationsName, StableCoop.MoreNationsName);
        Assert.Contains("Map", StableClientSetup.VanillaName);
        Assert.Contains("Map", StableClientSetup.MoreNationsName);
        Assert.Equal("Working stable mods for Coop 0.1.6 - Vanilla campaign Map", StableCoop.VanillaName);
        Assert.Equal("Working stable mods for Coop 0.1.6 - More Nations Remastered Map", StableCoop.MoreNationsName);
    }

    [Fact]
    public void The_v015_setup_names_become_0_1_6_once_and_keep_their_settings()
    {
        const string oldVanilla = "Working stable mods for Coop v0.15 - Vanilla campaign Map";
        const string oldNations = "Working stable mods for Coop v0.15 - More Nations Remastered Map";

        // Coodsz Host: the same profile object is renamed, so the save, MCM folder and settings stay.
        var path = Path.Combine(Path.GetTempPath(), "coodsz-host-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new HostSettings
            {
                ActiveProfile = oldNations,
                Profiles =
                [
                    new HostProfile { Name = "Default" },
                    new HostProfile { Name = oldVanilla, Locked = true, SaveName = "vanilla-save", McmFolder = "aaaa", AutosaveMinutes = 15 },
                    new HostProfile { Name = oldNations, Locked = true, SaveName = "nations-save", McmFolder = "bbbb", RestartOnCrash = true },
                    new HostProfile { Name = "My mods" }
                ]
            }));
            var settings = HostSettings.Load(path);
            Assert.Equal(StableCoop.MoreNationsName, settings.ActiveProfile);
            Assert.Equal(["Default", StableCoop.VanillaName, StableCoop.MoreNationsName, StableCoop.PlainName, "My mods"], settings.Profiles.Select(profile => profile.Name));
            var vanilla = settings.Profiles.Single(profile => profile.Name == StableCoop.VanillaName);
            Assert.Equal(("vanilla-save", "aaaa", 15), (vanilla.SaveName, vanilla.McmFolder, vanilla.AutosaveMinutes));
            var nations = settings.Profiles.Single(profile => profile.Name == StableCoop.MoreNationsName);
            Assert.Equal(("nations-save", "bbbb", true), (nations.SaveName, nations.McmFolder, nations.RestartOnCrash));

            // It runs once: saving and loading again changes nothing.
            HostSettings.Save(path, settings);
            var again = HostSettings.Load(path);
            Assert.Equal(settings.Profiles.Select(profile => profile.Name), again.Profiles.Select(profile => profile.Name));
            Assert.Equal("bbbb", again.Profiles.Single(profile => profile.Name == StableCoop.MoreNationsName).McmFolder);
            Assert.DoesNotContain(again.Profiles, profile => profile.Name.Contains("v0.15"));
        }
        finally
        {
            File.Delete(path);
        }

        // Coop and Friends: the selected setup and its own settings carry over the same way.
        var client = new LauncherSettings
        {
            ActiveSetup = oldVanilla,
            Setups =
            [
                new ClientSetup { Name = "Default" },
                new ClientSetup { Name = oldVanilla, Locked = true, SteamHostName = "Coodsz" },
                new ClientSetup { Name = oldNations, Locked = true, CampaignMap = "MoreNations", DirectHost = "1.2.3.4" }
            ]
        };
        client.EnsureSetups();
        Assert.Equal(StableClientSetup.VanillaName, client.ActiveSetup);
        Assert.Equal(["Default", StableClientSetup.VanillaName, StableClientSetup.MoreNationsName, StableClientSetup.PlainName], client.Setups.Select(setup => setup.Name));
        Assert.Equal("Coodsz", client.Setups[1].SteamHostName);
        Assert.Equal("1.2.3.4", client.Setups[2].DirectHost);
        client.EnsureSetups();
        Assert.Equal(4, client.Setups.Count);
    }

    [Fact]
    public void A_new_server_setup_starts_on_the_vanilla_default_order()
    {
        var name = StableCoop.NextProfileName(new[] { "Default", "Profile 2" });
        var profile = StableCoop.CreateFresh(name);

        Assert.Equal("Profile 3", profile.Name);
        Assert.False(profile.Locked);
        Assert.Equal("", profile.SaveName);
        Assert.Equal(StableCoop.VanillaMap, profile.CampaignMap);
        Assert.Equal(StableCoop.Order, profile.ModuleOrder);
        Assert.Contains("MoreNationsRemastered", profile.DisabledModules);
        Assert.Contains("MoreNationsRemasteredFix", profile.DisabledModules);
        Assert.DoesNotContain("StoryMode", profile.DisabledModules);
        Assert.DoesNotContain("CustomBattle", profile.DisabledModules);
        Assert.DoesNotContain("Coop", profile.DisabledModules);
        Assert.False(string.IsNullOrWhiteSpace(profile.McmFolder));
    }

    [Fact]
    public void Server_vanilla_map_turns_the_nation_mods_off()
    {
        var vanilla = StableCoop.DisabledFor(new HostProfile { CampaignMap = StableCoop.VanillaMap }).ToList();
        var nations = StableCoop.DisabledFor(new HostProfile { CampaignMap = StableCoop.MoreNationsMap }).ToList();

        Assert.Contains("MoreNationsRemastered", vanilla);
        Assert.Contains("MoreNationsRemasteredFix", vanilla);
        Assert.DoesNotContain("MoreNationsRemastered", nations);
        Assert.DoesNotContain("MoreNationsRemasteredFix", nations);
    }

    [Fact]
    public void CoopNightly_counts_as_Bannerlord_Coop_when_it_is_the_only_copy()
    {
        var nightlyOnly = new[] { "CoopNightly", "Native" };
        var both = new[] { "Coop", "CoopNightly", "Native" };
        var vanilla = new HostProfile { CampaignMap = StableCoop.VanillaMap };
        Assert.True(StableCoop.ShouldEnable(vanilla, "CoopNightly", nightlyOnly));
        Assert.False(StableCoop.ShouldEnable(vanilla, "CoopNightly", both));
        Assert.True(StableCoop.AlreadyPresent("Coop", nightlyOnly));
        Assert.False(StableCoop.ShouldEnable(vanilla, "StoryMode", nightlyOnly));
        Assert.False(StableCoop.ShouldEnable(vanilla, "CustomBattle", nightlyOnly));
        Assert.False(StableCoop.ShouldEnable(vanilla, "MoreNationsRemastered", nightlyOnly));
        Assert.True(StableCoop.ShouldEnable(new HostProfile { CampaignMap = StableCoop.MoreNationsMap }, "MoreNationsRemastered", new[] { "MoreNationsRemastered" }));
    }

    [Fact]
    public void A_missing_locked_mod_is_copied_into_the_server_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "locked-copy-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "game", "Bannerlord.Harmony");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "SubModule.xml"), "<Module><Id value=\"Bannerlord.Harmony\"/><Name value=\"Harmony\"/></Module>");
        var server = Path.Combine(root, "DedicatedServer");
        var result = ExtraModScan.CopyMissing(server, new[] { "Bannerlord.Harmony", "MissingMod" }, new[] { (Path.Combine(root, "game"), false) });
        Assert.StartsWith("Harmony (from ", Assert.Single(result.Copied));
        Assert.Equal("MissingMod", Assert.Single(result.Missing));
        Assert.True(File.Exists(Path.Combine(server, "engine", "Modules", "Bannerlord.Harmony", "SubModule.xml")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Server_keeps_the_duplicate_coop_mods_off()
    {
        foreach (var id in new[] { "CoopNightly", "FamilyAppearanceEditor", "CoopMarriage", "CoopSycn" })
            Assert.Contains(id, StableCoop.Disabled);
        Assert.DoesNotContain("StoryMode", StableCoop.Disabled);
        Assert.DoesNotContain("CustomBattle", StableCoop.Disabled);
    }

    [Fact]
    public void Server_working_list_keeps_coop_before_the_patches_and_the_map()
    {
        var order = StableCoop.Order;
        var coop = Array.IndexOf(order, "Coop");
        Assert.True(coop >= 0);
        Assert.True(coop < Array.IndexOf(order, "CoopModPatch"));
        Assert.True(coop < Array.IndexOf(order, "MoreNationsRemastered"));
        Assert.True(coop < Array.IndexOf(order, "PlayerSettlement"));
        Assert.NotEqual("Coop", order[^1]);
        Assert.Contains("DedicatedServer.Windows", order);
    }

    [Fact]
    public void Locked_client_and_server_profiles_enable_the_same_mods()
    {
        AssertSameEnabled(includeMoreNations: false);
        AssertSameEnabled(includeMoreNations: true);
    }

    private static void AssertSameEnabled(bool includeMoreNations)
    {
        var client = ServerModuleSet.WorkingOrder(includeMoreNations).ToList();
        var expected = client.ToList();
        var native = expected.FindIndex(id => id.Equals("Native", StringComparison.OrdinalIgnoreCase));
        expected.Insert(native + 1, "DedicatedServer.Windows");

        var profile = new HostProfile
        {
            Locked = true,
            CampaignMap = includeMoreNations ? StableCoop.MoreNationsMap : StableCoop.VanillaMap
        };
        var installed = StableCoop.Order.Where(id => !id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase));
        var server = StableCoop.Order
            .Where(id => StableCoop.ShouldEnable(profile, id, installed))
            .ToList();

        Assert.Equal(expected, server);
        Assert.DoesNotContain("DedicatedServer.Windows", client);
        Assert.DoesNotContain("CoopNightly", server);
        Assert.DoesNotContain("CoopSycn", server);
    }

    [Fact]
    public void Both_locked_profiles_replace_the_five_mod_server_list()
    {
        AssertLockedProfileWrites(includeMoreNations: false);
        AssertLockedProfileWrites(includeMoreNations: true);
    }

    private static void AssertLockedProfileWrites(bool includeMoreNations)
    {
        var root = Path.Combine(Path.GetTempPath(), "locked-list-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "engine", "Modules");
        foreach (var id in StableCoop.Order)
        {
            var folder = Path.Combine(modules, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "SubModule.xml"),
                "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/></Module>");
        }

        File.WriteAllText(Path.Combine(root, "modded-server-modules.txt"), "Native\nSandBoxCore\nSandbox\nCoopNightly\nDedicatedServer.Windows\n");

        try
        {
            var profile = new HostProfile
            {
                Locked = true,
                CampaignMap = includeMoreNations ? StableCoop.MoreNationsMap : StableCoop.VanillaMap
            };
            var loaded = ModuleCatalog.Load(root);
            var ranked = StableCoop.Order.Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index, StringComparer.OrdinalIgnoreCase);
            var rows = loaded
                .OrderBy(row => ranked.TryGetValue(row.Id, out var index) ? index : int.MaxValue)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var installed = rows.Select(row => row.Id).ToList();
            foreach (var row in rows)
                row.Enabled = row.Required || StableCoop.ShouldEnable(profile, row.Id, installed);
            ModuleCatalog.Write(root, rows);

            var written = File.ReadAllLines(Path.Combine(root, "modded-server-modules.txt"))
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToList();
            var enabled = rows.Where(row => row.Enabled).Select(row => row.Id).ToList();
            var client = ServerModuleSet.WorkingOrder(includeMoreNations).ToList();
            var native = client.FindIndex(id => id.Equals("Native", StringComparison.OrdinalIgnoreCase));
            client.Insert(native + 1, "DedicatedServer.Windows");

            Assert.Equal(client, enabled);
            Assert.Contains("Coop", written);
            Assert.DoesNotContain("CoopNightly", written);
            Assert.DoesNotContain("StoryMode", written);
            Assert.DoesNotContain("CustomBattle", written);
            var selection = File.ReadAllText(Path.Combine(root, "modded-server-selection.json"));
            Assert.Contains("\"Bannerlord.Harmony\": true", selection);
            Assert.Contains("\"Bannerlord.ButterLib\": true", selection);
            Assert.Contains("\"Bannerlord.UIExtenderEx\": true", selection);
            Assert.Contains("\"Bannerlord.MBOptionScreen\": true", selection);
            if (includeMoreNations)
            {
                Assert.Contains("\"MoreNationsRemastered\": true", selection);
                Assert.Contains("\"MoreNationsRemasteredFix\": true", selection);
            }
            else
            {
                Assert.Contains("\"MoreNationsRemastered\": false", selection);
                Assert.Contains("\"MoreNationsRemasteredFix\": false", selection);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Client_working_list_omits_the_server_only_modules()
    {
        var order = ServerModuleSet.WorkingOrder(includeMoreNations: true);
        Assert.DoesNotContain("DedicatedServer.Windows", order);
        Assert.DoesNotContain("CoopNightly", order);
        Assert.DoesNotContain("CoopSycn", order);
        Assert.DoesNotContain("CoopMarriage", order);
        Assert.DoesNotContain("FamilyAppearanceEditor", order);
        Assert.DoesNotContain("StoryMode", order);
        Assert.DoesNotContain("CustomBattle", order);
        var coop = order.ToList().IndexOf("Coop");
        Assert.True(coop < order.ToList().IndexOf("CoopModPatch"));
        Assert.NotEqual("Coop", order[^1]);
    }

    [Fact]
    public void Client_vanilla_map_removes_the_nation_mods()
    {
        var vanilla = ServerModuleSet.WorkingOrder(includeMoreNations: false);
        var nations = ServerModuleSet.WorkingOrder(includeMoreNations: true);
        Assert.DoesNotContain("MoreNationsRemastered", vanilla);
        Assert.DoesNotContain("MoreNationsRemasteredFix", vanilla);
        Assert.Contains("MoreNationsRemastered", nations);
        Assert.Contains("MoreNationsRemasteredFix", nations);
    }

    [Fact]
    public void New_host_file_creates_both_locked_maps_under_default()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-host-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = HostSettings.Load(path);
        var names = settings.Profiles.Select(profile => profile.Name).ToList();
        Assert.Equal("Default", names[0]);
        Assert.Equal(StableCoop.VanillaName, names[1]);
        Assert.Equal(StableCoop.MoreNationsName, names[2]);
        Assert.True(settings.Profiles[1].Locked);
        Assert.True(settings.Profiles[2].Locked);
        Assert.Equal(StableCoop.VanillaMap, settings.Profiles[1].CampaignMap);
        Assert.Equal(StableCoop.MoreNationsMap, settings.Profiles[2].CampaignMap);
        Assert.False(settings.Profiles[0].Locked);
    }

    [Fact]
    public void Old_locked_names_are_renamed_without_dropping_the_active_setup()
    {
        var settings = new HostSettings
        {
            ActiveProfile = "Working stable mods for Coop v0.15 - Vanilla map",
            Profiles =
            [
                new HostProfile { Name = "Default" },
                new HostProfile { Name = "Working stable mods for Coop v0.15 - Vanilla map", SaveName = "keep-me" }
            ]
        };
        StableCoop.Ensure(settings);
        Assert.Equal(StableCoop.VanillaName, settings.ActiveProfile);
        Assert.Equal("keep-me", settings.Profiles.Single(profile => profile.Name == StableCoop.VanillaName).SaveName);
    }

    [Fact]
    public void A_blank_profile_password_stays_blank_through_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-host-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = HostSettings.Load(path);
        settings.Profiles[0].Password = "";
        settings.Profiles[1].Password = null;
        HostSettings.Save(path, settings);
        var loaded = HostSettings.Load(path);
        Assert.Equal("", loaded.Profiles[0].Password);
        Assert.Null(loaded.Profiles.Single(profile => profile.Name == StableCoop.VanillaName).Password);
        File.Delete(path);
    }

    [Fact]
    public void An_extra_mod_cannot_be_dragged_into_the_working_list()
    {
        var working = ServerModuleSet.WorkingOrder(true);
        var ids = working.Append("ChaosBattle").ToList();
        Assert.False(BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.CanMove(ids, ids.Count - 1, ids.IndexOf("Coop"), working));
        Assert.False(BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.CanMove(ids, ids.Count - 1, ids.Count - 1, working));
        var withTwo = working.Append("ChaosBattle").Append("BattleSizeResized").ToList();
        Assert.True(BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.CanMove(withTwo, withTwo.Count - 1, withTwo.Count - 2, working));
    }

    [Fact]
    public void Bannerlord_Coop_can_move_and_a_mod_can_sit_beside_it()
    {
        var working = StableCoop.Order;
        var ids = working.Append("ExtraMod").ToList();
        var coop = ids.IndexOf("Coop");
        Assert.True(WorkingLoadOrder.CanMoveKeepingCoop(ids, coop, ids.Count - 1, working));
        Assert.True(WorkingLoadOrder.CanMoveKeepingCoop(ids, ids.Count - 1, coop, working));
        Assert.False(WorkingLoadOrder.CanMoveKeepingCoop(ids, ids.Count - 1, 1, working));
        Assert.True(WorkingLoadOrder.KeepsCoopPlacement(working, working));
    }

    [Fact]
    public void Extra_mods_wait_for_the_mod_they_need()
    {
        var working = new[] { "Coop" };
        var current = new[] { "Coop", "ChildMod", "ParentMod" };
        var dependencies = new Dictionary<string, IReadOnlyList<string>>
        {
            ["ChildMod"] = ["ParentMod"]
        };
        var sorted = ExtraDependencyOrder.Sort(current, working, dependencies);
        Assert.Equal(new[] { "Coop", "ParentMod", "ChildMod" }, sorted);

        var problems = ExtraDependencyOrder.Problems(
            new[] { "Coop", "ChildMod" },
            working,
            dependencies,
            new Dictionary<string, string> { ["ChildMod"] = "Child Mod", ["ParentMod"] = "Parent Mod" },
            new HashSet<string>(["Coop", "ChildMod"]));
        Assert.Contains(problems, line => line.Contains("Child Mod needs Parent Mod") && line.Contains("The game can stop while starting."));
    }

    [Fact]
    public void A_turned_off_dependency_is_named()
    {
        var problems = ExtraDependencyOrder.Problems(
            ["Coop", "ParentMod", "ChildMod"],
            ["Coop"],
            new Dictionary<string, IReadOnlyList<string>> { ["ChildMod"] = ["ParentMod"] },
            new Dictionary<string, string> { ["ChildMod"] = "Child Mod", ["ParentMod"] = "Parent Mod" },
            new HashSet<string>(["Coop", "ChildMod"], StringComparer.OrdinalIgnoreCase));
        Assert.Contains(problems, line => line.Contains("turned off"));
    }

    [Fact]
    public void Story_mode_is_not_treated_as_a_missing_extra_dependency()
    {
        var problems = ExtraDependencyOrder.Problems(
            ["Coop", "ChildMod"],
            ["Coop"],
            new Dictionary<string, IReadOnlyList<string>> { ["ChildMod"] = ["StoryMode"] },
            new Dictionary<string, string>(),
            new HashSet<string>(["Coop", "ChildMod"]));
        Assert.Empty(problems);
    }

    [Fact]
    public void Coop_and_the_map_cannot_be_toggled_off()
    {
        Assert.False(ClientModToggles.CanToggle("Coop"));
        Assert.False(ClientModToggles.CanToggle("MoreNationsRemastered"));
        Assert.True(ClientModToggles.CanToggle("RTSCamera"));
        Assert.True(ClientModToggles.FollowsSettlement("AgricultureEstate"));
    }

    [Fact]
    public void Main_menu_test_does_not_join_or_send_the_steam_name()
    {
        var profile = new ServerProfile { AutoConnect = true, BaseModules = ["Native"], ModuleLoadOrder = ["Native", "Coop"] };
        profile.Mods.Add(new ProfileMod { Name = "Bannerlord Coop", ModuleId = "Coop", WorkshopId = "1", Enabled = true });
        var plan = LaunchArgumentBuilder.Create(@"C:\Game", profile, diagnostic: true, steamName: "Coodsz");
        Assert.Contains("/singleplayer", plan.Arguments);
        Assert.DoesNotContain("/client", plan.Arguments);
        Assert.DoesNotContain("/autoconnect", plan.Arguments);
        Assert.DoesNotContain("Coodsz", plan.Arguments);
        Assert.True(plan.Diagnostic);
        Assert.False(plan.AutoConnect);
    }

    [Fact]
    public void A_join_uses_the_steam_lobby_id_and_not_the_name_flag()
    {
        var profile = new ServerProfile { AutoConnect = true, BaseModules = ["Native"], ModuleLoadOrder = ["Native", "Coop"] };
        profile.Mods.Add(new ProfileMod { Name = "Bannerlord Coop", ModuleId = "Coop", WorkshopId = "1", Enabled = true });
        var plan = LaunchArgumentBuilder.Create(@"C:\Game", profile, diagnostic: false, steamName: "Coodsz", connectLobby: 42);
        Assert.Contains("/client", plan.Arguments);
        Assert.Contains("+connect_lobby 42", plan.Arguments);
        Assert.DoesNotContain("/autoconnect", plan.Arguments);
        Assert.DoesNotContain("/steamname", plan.Arguments);
        Assert.DoesNotContain("Coodsz", plan.Arguments);
    }

    [Fact]
    public void A_hard_crash_is_still_reported_after_cleanup_text()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            -1073741819,
            null,
            "Final_cleanup\nCrashExample could not be loaded correctly",
            [new StartupCrashExplain.ModName("CrashExample", "Crash Example")],
            ["Coop"]);
        Assert.Contains(lines, line => line.Contains("Crash Example") && line.Contains("Test to main menu"));
        Assert.DoesNotContain(lines, line => line.Contains("closed normally"));
    }

    [Fact]
    public void A_running_game_is_not_called_a_crash()
    {
        var lines = StartupCrashExplain.Explain(true, null, null, null, [], ["Coop"]);
        Assert.Contains(lines, line => line.Contains("still open"));
    }

    [Fact]
    public void A_duplicate_client_mod_tells_them_to_move_the_extra_folder()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            1,
            null,
            "module id ChaosBattle is already loaded",
            [new StartupCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);
        Assert.Contains(lines, line => line.Contains("Chaos Battle") && line.Contains("more than one folder"));
    }

    [Fact]
    public void A_client_setup_with_no_extras_does_not_invent_a_mod()
    {
        var lines = StartupCrashExplain.Explain(false, 1, null, "the game stopped", [], ["Coop"]);
        Assert.Contains(lines, line => line.Contains("no extra mods") && line.Contains("Leave Bannerlord Coop where it is"));
    }

    [Fact]
    public void A_crash_that_names_a_working_mod_does_not_say_to_move_it()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            1,
            null,
            "Exception: NullReferenceException in Coop",
            [new StartupCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);
        Assert.Contains(lines, line => line.Contains("working list") && line.Contains("Leave Bannerlord Coop"));
        Assert.DoesNotContain(lines, line => line.Contains("Turn Coop off"));
    }

    [Theory]
    [InlineData("HexTool compatibility mode will attempt startup.")]
    [InlineData("The shared CLR context may select only one variant.")]
    [InlineData("failure is the operator's compatibility responsibility.")]
    [InlineData("incompatible identity for System.Runtime.CompilerServices.Unsafe")]
    [InlineData("permits this mismatch")]
    [InlineData("SHA256=ABC")]
    [InlineData("PublicKeyToken=cc7b13ffcd2ddd51")]
    [InlineData("System.Runtime.CompilerServices.Unsafe.dll")]
    [InlineData("")]
    [InlineData("   ")]
    public void Startup_noise_stays_out_of_the_server_log(string line)
    {
        Assert.Null(LogLineFormatter.Format(line));
    }

    [Fact]
    public void Report_headings_use_the_full_words()
    {
        var happened = LogLineFormatter.Format("What happened: The server stopped.");
        var todo = LogLineFormatter.Format("What to do: Press Start server.");
        var donot = LogLineFormatter.Format("What not to do: Do not move Bannerlord Coop.");
        Assert.Equal("What happened", happened!.Value.Kind);
        Assert.Equal("bad", happened.Value.Tone);
        Assert.Equal("What to do", todo!.Value.Kind);
        Assert.Equal("What not to do", donot!.Value.Kind);
        Assert.Equal("warn", todo.Value.Tone);
        Assert.Equal("The server stopped.", happened.Value.Message);
    }

    [Fact]
    public void A_save_mismatch_line_is_kept()
    {
        var entry = LogLineFormatter.Format("Save 'lol' was not found or is incompatible with the selected module set.");
        Assert.NotNull(entry);
        Assert.Equal("Note", entry.Value.Kind);
        Assert.Contains("lol", entry.Value.Message);
    }

    [Fact]
    public void Every_server_answer_names_coop_and_only_the_clean_stop_says_not_to_change_mods()
    {
        var examples = ServerCrashExplain.Examples();
        Assert.Equal(15, examples.Count);
        Assert.All(examples, example => Assert.Contains(example.Lines, line => line.Contains("Bannerlord Coop", StringComparison.OrdinalIgnoreCase) && line.Contains("do not move", StringComparison.OrdinalIgnoreCase)));
        var clean = Assert.Single(examples, example => example.Lines.Any(line => line.Contains("You do not have to change mods")));
        Assert.Equal("9. The server was up, then stopped cleanly", clean.Title);
        Assert.Contains(examples, example => example.Lines.Any(line => line.Contains("campaign save named lol")));
        Assert.Contains(examples, example => example.Lines.Any(line => line.Contains("Crash Example, Chaos Battle")));
        Assert.Contains(examples, example => example.Lines.Any(line => line.Contains("more than one folder")));
        Assert.Contains(examples, example => example.Lines.Any(line => line.Contains("Diplomacy (Fork) needs Bannerlord.Harmony")));
    }

    [Fact]
    public void A_wrong_coop_dll_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-coop-hash-" + Guid.NewGuid().ToString("N"));
        var workshop = Path.Combine(root, "workshop");
        Directory.CreateDirectory(Path.Combine(workshop, "bin", "Win64_Shipping_Client"));
        File.WriteAllText(Path.Combine(workshop, "bin", "Win64_Shipping_Client", "Coop.Core.dll"), "not-the-pinned-dll");
        // No Workshop SubModule.xml: the Workshop item is not downloaded.
        var error = Assert.Throws<InvalidOperationException>(() => CoopStaging.UsePlaytestCoop(workshop, Path.Combine(root, "game"), null));
        Assert.Equal(CoopStaging.NoPlaytestCoop, error.Message);
        // Coop 0.1.6 descriptor but a changed Coop.Core.dll is refused too.
        File.WriteAllText(Path.Combine(workshop, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
        error = Assert.Throws<InvalidOperationException>(() => CoopStaging.UsePlaytestCoop(workshop, Path.Combine(root, "game"), null));
        Assert.Contains("not the released Coop 0.1.6 file", error.Message);
        // An older Workshop Coop is refused with what TAOM.CoopCompat needs.
        File.WriteAllText(Path.Combine(workshop, "SubModule.xml"), "<Module><Id value=\"CoopNightly\"/><Version value=\"v0.1.5\"/></Module>");
        error = Assert.Throws<InvalidOperationException>(() => CoopStaging.UsePlaytestCoop(workshop, Path.Combine(root, "game"), null));
        Assert.Contains("needs Coop 0.1.6", error.Message);
        Assert.Contains("Wait for Steam to update", error.Message);
        // A newer Workshop Coop (Steam already updated, Coop 0.1.7) gets the Coop 0.1.6 client from Coop's GitHub release, not Steam.
        File.WriteAllText(Path.Combine(workshop, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.7\"/></Module>");
        error = Assert.Throws<InvalidOperationException>(() => CoopStaging.UsePlaytestCoop(workshop, Path.Combine(root, "game"), null));
        Assert.Contains("TAOM.CoopCompat 0.3.25 needs Coop 0.1.6", error.Message);
        Assert.Contains("BannerlordCoop-v0.1.6.zip", error.Message);
        Assert.Contains("Modules\\Coop", error.Message);
        Assert.DoesNotContain("Coodsz TAOM tools update", error.Message);
        Assert.DoesNotContain("Wait for Steam", error.Message);
        Assert.True(CoopStaging.IsNewerCoop("v0.1.7"));
        Assert.False(CoopStaging.IsNewerCoop("v0.1.6"));
        Assert.False(CoopStaging.IsNewerCoop("v0.1.5"));
        Assert.False(CoopStaging.IsNewerCoop("garbage"));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Taom_coop_025_uses_workshop_coop_016_and_moves_the_old_015_copy_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-coop-016-" + Guid.NewGuid().ToString("N"));
        var workshop = Path.Combine(root, "workshop");
        Directory.CreateDirectory(Path.Combine(workshop, "bin", "Win64_Shipping_Client"));
        File.WriteAllText(Path.Combine(workshop, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
        var dll = Path.Combine(workshop, "bin", "Win64_Shipping_Client", "Coop.Core.dll");
        File.WriteAllText(dll, "coop-0.1.6");
        var game = Path.Combine(root, "game");
        var coop = Path.Combine(game, "Modules", "Coop");
        Directory.CreateDirectory(coop);
        File.WriteAllText(Path.Combine(coop, "SubModule.xml"), "<Module><Id value=\"CoopNightly\"/><Version value=\"v0.1.5\"/></Module>");
        try
        {
            // Pin check uses the real 0.1.6 hash, so a fake dll is refused before anything moves.
            Assert.Throws<InvalidOperationException>(() => CoopStaging.UsePlaytestCoop(workshop, game, null));
            Assert.True(Directory.Exists(coop));

            // With the pinned file in place, the old 0.1.5 playtest copy leaves Modules (kept, not deleted).
            Assert.Equal("7CD7F77CCFE88CB482A2D13FCEA593E17C7B9EF05D3CF449D844D38BA5D2A587", TaomStack.CoopCoreSha256);
            Assert.Equal("v0.1.6", TaomStack.CoopVersion);
            Assert.Equal("Coop", TaomStack.CoopModuleId);
            var moved = CoopStaging.ParkExtraCoop(game);
            Assert.Single(moved);
            Assert.False(Directory.Exists(coop));
            Assert.True(Directory.Exists(moved[0]));
            Assert.StartsWith(Path.Combine(game, "Coop-kept-"), moved[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Coop_016_server_terms_are_asked_once_and_explained_on_exit_6()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-eula-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.Null(ServerEula.TermsHash(root));
        Assert.False(ServerEula.NeedsAnswer(ServerEula.TermsHash(root), ""));
        File.WriteAllText(Path.Combine(root, "EULA.md"), "TERMS v1\n");
        File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "LICENSE\n");
        var hash = ServerEula.TermsHash(root);
        Assert.NotNull(hash);
        Assert.Contains("--- LICENSE.txt ---", ServerEula.TermsText(root));
        Assert.True(ServerEula.NeedsAnswer(hash, ""));
        Assert.False(ServerEula.NeedsAnswer(hash, hash!.ToUpperInvariant()));
        File.WriteAllText(Path.Combine(root, "EULA.md"), "TERMS v2\n");
        Assert.True(ServerEula.NeedsAnswer(ServerEula.TermsHash(root), hash));
        Directory.Delete(root, recursive: true);

        var lines = ServerCrashExplain.Explain("engine exited with code 6", false, "[DedicatedServer] EULA acceptance required; server will not start (exit 6).", null, [], []);
        Assert.Contains(lines, line => line.Contains("I accept"));
        Assert.Contains(ServerCrashExplain.Explain("engine exited with code 6", false, "", null, [], []), line => line.Contains("exit code 6"));
        Assert.Equal("true", new LaunchOptions { AcceptEula = true }.AcceptEula ? "true" : "false");
    }

    [Fact]
    public void The_new_world_template_is_not_a_campaign_and_coop_files_are_never_moved()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "default_new_game.sav"), "x");
            File.WriteAllText(Path.Combine(root, "MyWorld.sav"), "x");
            File.WriteAllText(Path.Combine(root, "MyWorld.backup1.sav"), "x");
            Assert.Equal(new[] { "MyWorld" }, SaveCatalog.NamesIn(root));
            Assert.Equal(LaunchOptions.TemplateSaveReason, LaunchOptions.MissingSaveReason("default_new_game", true, false));
            Assert.Null(LaunchOptions.MissingSaveReason("MyWorld", true, false));
            var lines = ServerCrashExplain.Explain("engine exited with code 1", false, "Recovery generations and default_new_game cannot be hosted directly: 'default_new_game'.", null, [], []);
            Assert.Contains(lines, line => line.Contains("New campaign") && line.StartsWith("What to do"));

            var modules = Path.Combine(root, "Modules");
            foreach (var id in new[] { "Bannerlord.Harmony", "Coop" })
            {
                var bin = Path.Combine(modules, id, "bin", "Win64_Shipping_Server");
                Directory.CreateDirectory(bin);
                File.WriteAllText(Path.Combine(modules, id, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Version value=\"v1.0.0\"/></Module>");
                File.WriteAllText(Path.Combine(bin, "0Harmony.dll"), id);
            }

            Assert.Empty(ModuleDllClash.Warnings(modules, new[] { "Bannerlord.Harmony", "Coop" }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Realm_reward_codes_are_one_per_steam_account_and_cannot_be_changed()
    {
        const ulong id = 76561198000000008UL;
        var code = BannerlordCoopLauncher.Core.Realm.RealmRewardCode.Create(id);
        Assert.StartsWith("RLM-", code);
        Assert.Equal(code, BannerlordCoopLauncher.Core.Realm.RealmRewardCode.Create(id));
        Assert.NotEqual(code, BannerlordCoopLauncher.Core.Realm.RealmRewardCode.Create(id + 1));
        Assert.True(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.TryCheck(code, out var back));
        Assert.Equal(id, back);
        Assert.True(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.TryCheck("  " + code.ToLowerInvariant().Replace("-", " ") + " ", out back));
        Assert.Equal(id, back);
        var chars = code.ToCharArray();
        var last = chars.Length - 1;
        chars[last] = chars[last] == 'A' ? 'B' : 'A';
        Assert.False(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.TryCheck(new string(chars), out _));
        Assert.False(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.TryCheck("RLM-1234-5678", out _));
        Assert.False(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.TryCheck("", out _));

        const string vdf = "\"users\"\n{\n\t\"76561198000000001\"\n\t{\n\t\t\"AccountName\"\t\t\"a\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n\t\"76561198000000002\"\n\t{\n\t\t\"AccountName\"\t\t\"b\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n";
        Assert.Equal(76561198000000002UL, BannerlordCoopLauncher.Core.Steam.SteamRegistry.MostRecentUser(vdf));
    }

    // The real key is not in the public source (CONTRIBUTING.md, Private files): a build that has the private
    // realm-reward-key.txt signs with it, a build without it with the dummy key.
    [Fact]
    public void Realm_reward_codes_use_the_private_key_when_the_build_has_it_and_the_dummy_key_otherwise()
    {
        var keyFile = Path.Combine(SourceFiles.PrivateRoot(), "realm-reward-key.txt");
        var hasKey = File.Exists(keyFile);
        var expected = hasKey ? Convert.FromHexString(File.ReadAllText(keyFile).Trim()) : BannerlordCoopLauncher.Core.Realm.RealmRewardCode.DummyKey;
        Assert.Equal(expected, BannerlordCoopLauncher.Core.Realm.RealmRewardCode.Key);
        Assert.Equal(hasKey, typeof(BannerlordCoopLauncher.Core.Realm.RealmRewardCode).Assembly.GetManifestResourceNames().Contains(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.KeyResource));
        Assert.Contains("dummy", System.Text.Encoding.ASCII.GetString(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.DummyKey));

        // The file holds hex digits; spaces and a line break around them are fine, anything else fails loudly.
        Assert.Equal(new byte[] { 0x01, 0xAB, 0xFF }, BannerlordCoopLauncher.Core.Realm.RealmRewardCode.ReadKey(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(" 01abFF\r\n"))));
        Assert.Null(BannerlordCoopLauncher.Core.Realm.RealmRewardCode.ReadKey(null));
        Assert.Throws<FormatException>(() => BannerlordCoopLauncher.Core.Realm.RealmRewardCode.ReadKey(new MemoryStream(System.Text.Encoding.ASCII.GetBytes("not hex"))));
    }

    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Lines { get; } = new();
        public void Report(string value) => Lines.Add(value);
    }

    [Fact]
    public void The_host_window_keeps_the_warning_the_confirm_button_and_the_log_folder_button()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        Assert.Contains("The server ran into a problem. Open the Logs tab, scroll all the way down, and read What happened, What to do, and What not to do. If you need more information, use the server logs.", xaml);
        Assert.Contains("Content=\"Confirm\"", xaml);
        Assert.Contains("Content=\"Open server logs\"", xaml);
        Assert.DoesNotContain("Content=\"Ban list\"", xaml);
        Assert.DoesNotContain("Content=\"Cheat menu\"", xaml);
        Assert.Contains("Content=\"Save game\"", xaml);
        Assert.Contains("Content=\"Save setup\"", xaml);
        Assert.Contains("Opens the server logs. Use this when you need more than the lines above.", xaml);
        Assert.Contains("Bannerlord Coop can move on a normal setup", xaml);
        Assert.Contains("UDP 4200 is port forwarded", xaml);
        Assert.DoesNotContain("Bannerlord Coop needs to stay last", xaml);
    }

    [Fact]
    public void The_client_window_keeps_the_main_menu_test_and_the_coop_reminder()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "MainWindow.xaml"));
        Assert.Contains("Test to main menu", xaml);
        Assert.Contains("does not join the server", xaml);
        Assert.Contains("Bannerlord Coop can move on a normal setup", xaml);
        Assert.DoesNotContain("Bannerlord Coop needs to stay last", xaml);
    }

    [Fact]
    public void A_steam_name_is_not_accepted_as_a_server_address()
    {
        Assert.False(JoinGuidance.IsDirectAddress("Coodsz"));
        Assert.True(JoinGuidance.IsDirectAddress("192.168.1.10"));
        Assert.True(JoinGuidance.IsDirectAddress("203.0.113.10"));
        Assert.True(JoinGuidance.IsDirectAddress("host.example.com"));
        Assert.False(JoinGuidance.IsDirectAddress("host.example.com:4200"));
        var quiet = JoinGuidance.DescribeProbe(new ServerProbeResult(ServerReachability.NoReply, "stopped"), "192.168.1.10");
        Assert.Contains("No Coop server answered", quiet);
        Assert.Contains("port forwarded", quiet);
        Assert.Contains("Make sure the host's Steam username is correct.", JoinGuidance.SteamNameHint);
    }

    [Fact]
    public void Coop_and_Friends_leaves_terrain_quality_alone()
    {
        var client = SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher");
        Assert.DoesNotContain("RaiseLowTerrain", client);
        Assert.DoesNotContain("terrain_quality", client);
    }

    [Fact]
    public void Steam_join_can_clear_a_saved_name_that_is_not_an_ip()
    {
        const string json = """
            {
              "Connection": {
                "DirectConnection": {
                  "lastServerAddress": "Coodsz"
                }
              }
            }
            """;
        Assert.True(JoinGuidance.StoredAddressIsNotIp(json));
        var cleared = JoinGuidance.RemoveDirectAddress(json);
        Assert.NotNull(cleared);
        Assert.DoesNotContain("lastServerAddress", cleared);
        var written = JoinGuidance.SetDirectAddress("{}", "host.example.com");
        Assert.Contains("host.example.com", written);
    }

    [Fact]
    public void A_mod_that_skips_the_dedicated_server_is_named_before_start()
    {
        const string xml = """<Module><SubModules><SubModule><Tags><Tag key="DedicatedServerType" value="none" /></Tags></SubModule></SubModules></Module>""";
        var note = ServerModuleNotes.SkippedCode("Europe 1100", xml);
        Assert.NotNull(note);
        Assert.Contains("Europe 1100", note);
        Assert.Contains("does not run its code", note);
        Assert.Null(ServerModuleNotes.SkippedCode("Bannerlord Coop", "<Module></Module>"));
        Assert.True(ServerModuleNotes.IsLibrary("Bannerlord.Harmony"));
        Assert.Contains("default_new_game", ServerModuleNotes.NewCampaign);
    }

    [Fact]
    public void Mods_steam_can_not_download_point_to_nexus_and_moddb_instead_of_install_mods()
    {
        var window = SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher");
        foreach (var signature in new[] { "private void NoteMissingModules(", "private async void Install_Click(" })
        {
            var body = SourceFiles.Method(window, signature);
            Assert.Contains("LogManualInstall(manual);", body, StringComparison.Ordinal);
            Assert.Contains("NeedsManualInstall", body, StringComparison.Ordinal);
        }

        var status = SourceFiles.Method(window, "private void NoteMissingModules(");
        Assert.Contains("missing.Except(manual)", status, StringComparison.Ordinal);
        var from = window.IndexOf("private bool NeedsManualInstall(", StringComparison.Ordinal);
        Assert.True(from >= 0);
        var check = window[from..window.IndexOf("private void LogManualInstall(", from, StringComparison.Ordinal)];
        Assert.Contains("IsGoneFromWorkshop", check, StringComparison.Ordinal);
        Assert.Contains("BundledModules.ModuleIds", check, StringComparison.Ordinal);
        var lines = SourceFiles.Method(window, "private void LogManualInstall(");
        Assert.Contains("Get it from Nexus Mods or ModDB", lines, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "CoodszHost", "MainWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
