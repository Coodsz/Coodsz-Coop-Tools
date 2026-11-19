using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Tests;

public class NightlyServerTests
{
    // The server-config.json of a real nightly run (rot-wave10), with its comment and trailing comma.
    private const string RealConfig =
        "{\n" +
        "  \"autoRestart\": false,\n" +
        "  \"saveName\": \"rotjoin3\",\n" +
        "  \"autosaveMinutes\": 0,\n" +
        "  \"password\": \"\",\n" +
        "  \"modules\": [\"Bannerlord.Harmony\",\"RealmOfThrones.CoopCompat\",\"ROT-Core\"]\n" +
        ",\n" +
        "  // Diagnostics. Only enable when asked to capture logs for a bug report.\n" +
        "  \"traceTick\": false,\n" +
        "}\n";

    private const string ModConfig =
        "{\n" +
        "  \"difficulty\": {\n    \"playerReceivedDamage\": \"VeryEasy\"\n  },\n" +
        "  \"modOptions\": {\n" +
        "    // Enables proximity voice for every player.\n" +
        "    \"voiceEnabled\": true,\n" +
        "    // (Default) false: the dedicated server runs without NavalDLC.\n" +
        "    \"warSails\": false\n" +
        "  }\n" +
        "}\n";

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-nightly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // ----- server-config.json -----

    [Fact]
    public void The_module_list_and_save_name_change_and_everything_else_stays()
    {
        var text = NightlyServer.WriteConfig(RealConfig, ["Bannerlord.Harmony", " ROT-Core ", "RealmOfThrones.CoopCompat"], " world2 ");
        Assert.Equal(new[] { "Bannerlord.Harmony", "ROT-Core", "RealmOfThrones.CoopCompat" }, NightlyServer.ReadModules(text));
        Assert.Equal("world2", NightlyServer.ReadSaveName(text));
        Assert.Contains("// Diagnostics. Only enable when asked to capture logs for a bug report.", text);
        Assert.Contains("\"traceTick\": false,", text);
        Assert.Contains("\"autosaveMinutes\": 0,", text);
        Assert.Contains("\"autoRestart\": false,", text);
        Assert.Equal(text, NightlyServer.WriteConfig(text, NightlyServer.ReadModules(text), "world2"));
    }

    [Fact]
    public void A_missing_or_empty_config_gets_both_keys()
    {
        var text = NightlyServer.WriteConfig("", ["A", "B"], "camp");
        Assert.Equal(new[] { "A", "B" }, NightlyServer.ReadModules(text));
        Assert.Equal("camp", NightlyServer.ReadSaveName(text));
        Assert.Empty(NightlyServer.ReadModules("{ \"saveName\": \"x\" }"));
        Assert.Null(NightlyServer.ReadSaveName("{ }"));
    }

    [Fact]
    public void Apply_config_writes_the_file_once_without_a_byte_order_mark_and_keeps_a_backup()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            Directory.CreateDirectory(dataDir);
            var path = NightlyServer.ConfigPath(dataDir);
            File.WriteAllText(path, RealConfig);
            Assert.True(NightlyServer.ApplyConfig(dataDir, ["X"], "camp"));
            Assert.False(NightlyServer.ApplyConfig(dataDir, ["X"], "camp"));
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Equal(RealConfig, File.ReadAllText(path + ".bak"));
            Assert.Equal(new[] { "X" }, NightlyServer.ReadModules(File.ReadAllText(path)));

            var fresh = NightlyServer.DataDir(Path.Combine(root, "other"));
            Assert.True(NightlyServer.ApplyConfig(fresh, ["Y"], "new"));
            Assert.Equal("new", NightlyServer.ReadSaveName(File.ReadAllText(NightlyServer.ConfigPath(fresh))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ----- mod-config.json -----

    [Fact]
    public void War_sails_is_set_in_mod_options_and_the_comments_stay()
    {
        Assert.False(NightlyServer.ReadWarSails(ModConfig));
        var on = NightlyServer.WriteWarSails(ModConfig, true);
        Assert.True(NightlyServer.ReadWarSails(on));
        Assert.Contains("// (Default) false: the dedicated server runs without NavalDLC.", on);
        Assert.Contains("\"voiceEnabled\": true,", on);
        Assert.Null(NightlyServer.ReadWarSails("{ \"modOptions\": { } }"));
        Assert.True(NightlyServer.ReadWarSails(NightlyServer.WriteWarSails("{ \"voice\": { } }", true)));
    }

    [Fact]
    public void Mod_config_is_one_level_above_the_data_folder_and_is_made_from_the_template_when_missing()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            Assert.Equal(Path.Combine(root, "mod-config.json"), NightlyServer.ModConfigPath(dataDir));
            Assert.Equal(Path.Combine(root, "mod-config.json"), NightlyServer.ModConfigPath(dataDir + Path.DirectorySeparatorChar));

            var server = Path.Combine(root, "server");
            var template = NightlyServer.ModConfigTemplate(server);
            Assert.Equal(Path.Combine(server, "engine", "Modules", "Coop", "mod-config.default.json"), template);
            Directory.CreateDirectory(Path.GetDirectoryName(template)!);
            File.WriteAllText(template, ModConfig);

            Assert.True(NightlyServer.ApplyWarSails(dataDir, true, template));
            var written = File.ReadAllText(NightlyServer.ModConfigPath(dataDir));
            Assert.True(NightlyServer.ReadWarSails(written));
            Assert.Contains("\"playerReceivedDamage\": \"VeryEasy\"", written);
            Assert.False(NightlyServer.ApplyWarSails(dataDir, true, template));
            Assert.True(NightlyServer.ApplyWarSails(dataDir, false, template));
            Assert.False(NightlyServer.ReadWarSails(File.ReadAllText(NightlyServer.ModConfigPath(dataDir))));

            var bare = NightlyServer.DataDir(Path.Combine(root, "bare"));
            Assert.True(NightlyServer.ApplyWarSails(bare, true));
            Assert.True(NightlyServer.ReadWarSails(File.ReadAllText(NightlyServer.ModConfigPath(bare))));

            // A file made from a template that already has the wanted mode was still written.
            var made = NightlyServer.DataDir(Path.Combine(root, "made"));
            File.WriteAllText(template, NightlyServer.WriteWarSails(ModConfig, true));
            Assert.True(NightlyServer.ApplyWarSails(made, true, template));
            Assert.True(NightlyServer.ReadWarSails(File.ReadAllText(NightlyServer.ModConfigPath(made))));
            Assert.False(NightlyServer.ApplyWarSails(made, true, template));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ----- Saves -----

    [Fact]
    public void A_new_save_name_gets_the_blank_for_its_war_sails_mode_and_a_leftover_json_goes()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            var server = Path.Combine(root, "server");
            var saves = NightlyServer.GameSaves(dataDir);
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "default_new_game.sav"), "plain");
            File.WriteAllText(Path.Combine(saves, "default_new_game_warsails.sav"), "naval");
            File.WriteAllText(Path.Combine(saves, "world.json"), "old world");

            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, server, "world.sav", warSails: true));
            Assert.Equal("naval", File.ReadAllText(Path.Combine(saves, "world.sav")));
            Assert.False(File.Exists(Path.Combine(saves, "world.json")));

            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, server, "land", warSails: false));
            Assert.Equal("plain", File.ReadAllText(Path.Combine(saves, "land.sav")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_existing_save_is_never_overwritten_and_keeps_its_json()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            var saves = NightlyServer.GameSaves(dataDir);
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "default_new_game_warsails.sav"), "naval");
            File.WriteAllText(Path.Combine(saves, "Keep me.sav"), "my world");
            File.WriteAllText(Path.Combine(saves, "Keep me.json"), "its data");
            Assert.Equal(SeedResult.Existing, NightlyServer.SeedSave(dataDir, root, "keep me", warSails: true));
            Assert.Equal("my world", File.ReadAllText(Path.Combine(saves, "Keep me.sav")));
            Assert.Equal("its data", File.ReadAllText(Path.Combine(saves, "Keep me.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Spaces_around_a_name_are_dropped_and_another_casing_finds_the_existing_save()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            var saves = NightlyServer.GameSaves(dataDir);
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "default_new_game.sav"), "plain");
            File.WriteAllText(Path.Combine(saves, "Keep me.sav"), "my world");
            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, root, "a ", warSails: false));
            Assert.True(File.Exists(Path.Combine(saves, "a.sav")));
            Assert.Equal(SeedResult.Existing, NightlyServer.SeedSave(dataDir, root, "KEEP ME", warSails: false));
            Assert.Equal("my world", File.ReadAllText(Path.Combine(saves, "Keep me.sav")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_blank_is_taken_from_the_server_folder_when_the_data_folder_has_none()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            var server = Path.Combine(root, "server");
            var bin = Path.Combine(server, "engine", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "default_new_game_warsails.sav"), "from bin");
            Assert.Equal(SeedResult.NoBlank, NightlyServer.SeedSave(dataDir, server, "a", warSails: false));
            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, server, "a", warSails: true));
            Assert.Equal("from bin", File.ReadAllText(Path.Combine(NightlyServer.GameSaves(dataDir), "a.sav")));

            var serverData = Path.Combine(server, "server-data", "Game Saves");
            Directory.CreateDirectory(serverData);
            File.WriteAllText(Path.Combine(serverData, "default_new_game_warsails.sav"), "from server-data");
            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, server, "b", warSails: true));
            Assert.Equal("from server-data", File.ReadAllText(Path.Combine(NightlyServer.GameSaves(dataDir), "b.sav")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_seed_whose_world_was_never_built_is_removed_but_a_saved_world_never_is()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            var server = Path.Combine(root, "server");
            var bin = Path.Combine(server, "engine", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "default_new_game_warsails.sav"), "blank naval");
            var saves = NightlyServer.GameSaves(dataDir);

            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, server, "Seven", warSails: true));
            File.WriteAllText(Path.Combine(saves, "Seven.json"), "half written");
            Assert.True(NightlyServer.RemoveUnbuiltSeed(dataDir, server, "Seven.sav"));
            Assert.False(File.Exists(Path.Combine(saves, "Seven.sav")));
            Assert.False(File.Exists(Path.Combine(saves, "Seven.json")));
            Assert.False(NightlyServer.RemoveUnbuiltSeed(dataDir, server, "Seven"));

            // The server saved the world: the same length is not enough, every byte has to match.
            Assert.Equal(SeedResult.Seeded, NightlyServer.SeedSave(dataDir, server, "Built", warSails: true));
            File.WriteAllText(Path.Combine(saves, "Built.sav"), "built naval");
            File.WriteAllText(Path.Combine(saves, "Built.json"), "its data");
            Assert.False(NightlyServer.RemoveUnbuiltSeed(dataDir, server, "Built"));
            Assert.Equal("built naval", File.ReadAllText(Path.Combine(saves, "Built.sav")));
            Assert.True(File.Exists(Path.Combine(saves, "Built.json")));

            // The blank itself is never a campaign to remove.
            File.WriteAllText(Path.Combine(saves, "default_new_game_warsails.sav"), "blank naval");
            Assert.False(NightlyServer.RemoveUnbuiltSeed(dataDir, server, "default_new_game_warsails"));
            Assert.True(File.Exists(Path.Combine(saves, "default_new_game_warsails.sav")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("", "Type a name for the new campaign.")]
    [InlineData("default_new_game", "blank new game")]
    [InlineData("default_new_game_warsails.sav", "blank new game")]
    [InlineData("..\\evil", "English letters A to Z")]
    [InlineData("Wörld", "English letters A to Z")]
    [InlineData("CON", "Windows keeps for itself")]
    [InlineData("a.", "English letters A to Z")]
    [InlineData("x.sav.sav", "English letters A to Z")]
    public void A_name_that_cannot_be_a_new_campaign_is_refused_before_anything_is_written(string name, string reason)
    {
        Assert.Contains(reason, NightlyServer.NewSaveProblem(name));
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            var saves = NightlyServer.GameSaves(dataDir);
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "default_new_game.sav"), "plain");
            File.WriteAllText(Path.Combine(saves, "default_new_game_warsails.sav"), "naval");
            Assert.Throws<InvalidOperationException>(() => NightlyServer.SeedSave(dataDir, root, name, warSails: true));
            Assert.Equal(new[] { "default_new_game.sav", "default_new_game_warsails.sav" }, Directory.GetFiles(saves).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Equal("naval", File.ReadAllText(Path.Combine(saves, "default_new_game_warsails.sav")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ----- Start -----

    [Fact]
    public void The_start_plan_points_every_folder_into_the_tools_own_data_folder()
    {
        var plan = NightlyServer.StartPlan(@"C:\Servers\Rot Server", @"C:\Data\Rot", acceptTerms: false);
        Assert.Equal(@"C:\Servers\Rot Server\BannerlordCoopServer.exe", plan.FileName);
        Assert.Equal(@"C:\Servers\Rot Server", plan.WorkingDirectory);
        Assert.Equal(new[] { "--data-dir", @"C:\Data\Rot\DedicatedServer", "--pipe-input", "--no-watchdog" }, plan.Arguments);
        Assert.Equal(@"--data-dir C:\Data\Rot\DedicatedServer --pipe-input --no-watchdog", plan.ArgumentLine);
        Assert.Equal(@"C:\Data\Rot", plan.Environment["COOP_DATA_DIR"]);
        Assert.Equal(@"C:\Data\Rot\DedicatedServer", plan.Environment["BANNERLORD_USER_DIR"]);
        Assert.False(plan.Environment.ContainsKey("DS_ACCEPT_EULA"));
        Assert.DoesNotContain(plan.Arguments, argument => argument.Contains("eula", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(plan.InputEncoding.GetPreamble());
        Assert.Equal("é", plan.InputEncoding.GetString(plan.InputEncoding.GetBytes("é")));

        var accepted = NightlyServer.StartPlan(@"C:\S", @"D:\My Data", acceptTerms: true, new Dictionary<string, string> { ["COODSZ_ROT_HOST"] = "full", ["COOP_DATA_DIR"] = @"C:\Elsewhere", ["DS_ACCEPT_EULA"] = "false" });
        Assert.Equal("true", accepted.Environment["DS_ACCEPT_EULA"]);
        Assert.Equal("full", accepted.Environment["COODSZ_ROT_HOST"]);
        Assert.Equal(@"D:\My Data", accepted.Environment["COOP_DATA_DIR"]);
        var refused = NightlyServer.StartPlan(@"C:\S", @"D:\D", acceptTerms: false, new Dictionary<string, string> { ["ds_accept_eula"] = "true" });
        Assert.False(refused.Environment.ContainsKey("DS_ACCEPT_EULA"));
        Assert.Equal("--data-dir \"D:\\My Data\\DedicatedServer\" --pipe-input --no-watchdog", accepted.ArgumentLine);
    }

    [Fact]
    public void The_environment_is_exact_and_an_inherited_terms_answer_is_removed_unless_the_host_accepted()
    {
        var plan = NightlyServer.StartPlan(@"C:\S", @"D:\D", acceptTerms: false);
        Assert.Equal(new[] { "DS_ACCEPT_EULA" }, plan.RemoveVariables);
        Assert.Equal("0", plan.Environment["DOTNET_TC_DeleteCallCountingStubsAfter"]);
        Assert.Equal(new[] { "BANNERLORD_USER_DIR", "COOP_DATA_DIR", "DOTNET_TC_DeleteCallCountingStubsAfter" }, plan.Environment.Keys.Order(StringComparer.Ordinal));

        var accepted = NightlyServer.StartPlan(@"C:\S", @"D:\D", acceptTerms: true);
        Assert.Empty(accepted.RemoveVariables);
        Assert.Equal("true", accepted.Environment["DS_ACCEPT_EULA"]);

        var tries = new Dictionary<string, string>
        {
            ["coop_data_dir"] = @"C:\X", ["Bannerlord_User_Dir"] = @"C:\Y", ["Ds_Accept_Eula"] = "true",
            [" DS_ACCEPT_EULA "] = "true", ["dotnet_tc_deletecallcountingstubsafter"] = "4", ["MY_SWITCH"] = "on"
        };
        var tried = NightlyServer.StartPlan(@"C:\S", @"D:\D", acceptTerms: false, tries);
        Assert.Equal(@"D:\D", tried.Environment["COOP_DATA_DIR"]);
        Assert.Equal(@"D:\D\DedicatedServer", tried.Environment["BANNERLORD_USER_DIR"]);
        Assert.Equal("0", tried.Environment["DOTNET_TC_DeleteCallCountingStubsAfter"]);
        Assert.False(tried.Environment.ContainsKey("DS_ACCEPT_EULA"));
        Assert.Equal(new[] { "DS_ACCEPT_EULA" }, tried.RemoveVariables);
        Assert.Equal("on", tried.Environment["MY_SWITCH"]);
        Assert.Equal(4, tried.Environment.Count);
    }

    // ----- Damaged and odd files -----

    [Theory]
    [InlineData("{ \"saveName\": \"x\" \"modules\": [] }")]
    [InlineData("{ \"modules\": [\"a\", { }")]
    [InlineData("not json at all")]
    public void A_damaged_config_is_never_written(string damaged)
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(NightlyServer.ConfigPath(dataDir), damaged);
            File.WriteAllText(NightlyServer.ModConfigPath(dataDir), damaged);
            Assert.Contains("does not read as a settings file", Assert.Throws<InvalidOperationException>(() => NightlyServer.ApplyConfig(dataDir, ["A"], "camp")).Message);
            Assert.Contains("does not read as a settings file", Assert.Throws<InvalidOperationException>(() => NightlyServer.ApplyWarSails(dataDir, true)).Message);
            Assert.Equal(damaged, File.ReadAllText(NightlyServer.ConfigPath(dataDir)));
            Assert.Equal(damaged, File.ReadAllText(NightlyServer.ModConfigPath(dataDir)));
            Assert.False(File.Exists(NightlyServer.ConfigPath(dataDir) + ".bak"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_a_crlf_file_with_a_trailing_comma_and_an_empty_file_are_written()
    {
        var root = NewRoot();
        try
        {
            var dataDir = NightlyServer.DataDir(root);
            Directory.CreateDirectory(dataDir);
            var config = NightlyServer.ConfigPath(dataDir);
            var mod = NightlyServer.ModConfigPath(dataDir);

            File.WriteAllText(config, RealConfig, new System.Text.UTF8Encoding(true));
            File.WriteAllText(mod, ModConfig, new System.Text.UTF8Encoding(true));
            Assert.True(NightlyServer.ApplyConfig(dataDir, ["A"], "camp"));
            Assert.True(NightlyServer.ApplyWarSails(dataDir, true));
            Assert.Equal(new[] { "A" }, NightlyServer.ReadModules(File.ReadAllText(config)));
            Assert.True(NightlyServer.ReadWarSails(File.ReadAllText(mod)));

            var crlf = "{\r\n  \"saveName\": \"old\",\r\n  \"modules\": [\"X\"],\r\n  // keep me\r\n  \"steam\": false,\r\n}\r\n";
            File.WriteAllText(config, crlf);
            File.WriteAllText(mod, "{\r\n  \"modOptions\": {\r\n    \"warSails\": true,\r\n  },\r\n}\r\n");
            Assert.True(NightlyServer.ApplyConfig(dataDir, ["Y", "Z"], "new"));
            Assert.True(NightlyServer.ApplyWarSails(dataDir, false));
            var written = File.ReadAllText(config);
            Assert.Equal(new[] { "Y", "Z" }, NightlyServer.ReadModules(written));
            Assert.Equal("new", NightlyServer.ReadSaveName(written));
            Assert.Contains("  // keep me\r\n  \"steam\": false,\r\n}", written);
            Assert.False(NightlyServer.ReadWarSails(File.ReadAllText(mod)));

            File.WriteAllText(config, "");
            File.WriteAllText(mod, "");
            Assert.True(NightlyServer.ApplyConfig(dataDir, ["E"], "empty"));
            Assert.True(NightlyServer.ApplyWarSails(dataDir, true));
            Assert.Equal("empty", NightlyServer.ReadSaveName(File.ReadAllText(config)));
            Assert.True(NightlyServer.ReadWarSails(File.ReadAllText(mod)));

            // The first .bak is the host's own file, and later saves keep it.
            Assert.Equal(RealConfig, File.ReadAllText(config + ".bak"));
            Assert.Equal(ModConfig, File.ReadAllText(mod + ".bak"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_save_stem_is_written_and_a_list_that_already_fits_keeps_its_own_layout()
    {
        var spread = "{\n  \"saveName\": \"old\",\n  \"modules\": [\n    \"A\",\n    \"B\"\n  ]\n}\n";
        var text = NightlyServer.WriteConfig(spread, ["A", "B"], "camp.sav");
        Assert.Equal(spread.Replace("\"old\"", "\"camp\""), text);
        Assert.Equal(text, NightlyServer.WriteConfig(text, [" A ", "B"], " camp "));
        Assert.Contains("\"modules\": [\"A\",\"C\"]", NightlyServer.WriteConfig(text, ["A", "C"], "camp"));
    }

    [Fact]
    public void The_server_loads_its_fixed_modules_around_the_list_and_NavalDLC_only_with_War_Sails()
    {
        Assert.Equal(new[] { "Native", "SandBoxCore", "Sandbox", "CoopNightly", "NavalDLC", "A", "B", "DedicatedServer.Windows" },
            NightlyServer.FullOrder("CoopNightly", ["A", "B"], warSails: true));
        Assert.Equal(new[] { "Native", "SandBoxCore", "Sandbox", "CoopNightly", "A", "B", "DedicatedServer.Windows" },
            NightlyServer.FullOrder("CoopNightly", ["A", "B"], warSails: false));
        Assert.DoesNotContain("NavalDLC", NightlyServer.LoadsFirst("CoopNightly", warSails: false));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void Every_known_exit_code_has_a_line_that_names_it(int code)
    {
        Assert.Contains("(exit code " + code + ")", NightlyServer.ExitHint(code));
        Assert.False(NightlyServer.ExitHint(code)!.StartsWith("What happened", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(-1073741819)]
    public void Other_exit_codes_have_no_line(int code)
    {
        Assert.Null(NightlyServer.ExitHint(code));
    }
}
