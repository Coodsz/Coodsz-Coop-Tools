// Tests ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// tests/CoopKeep.Core.Tests/Configuration/JsoncEditorTests.cs and ModConfigStoreTests.cs and the
// Fixtures/mod-config.default.json test file, translated from Turkish and changed for the Coodsz tools.
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

using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;
using SectionEditor = BannerlordCoopLauncher.Core.Configuration.JsoncEditor;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The in place JSONC editor.
/// </summary>
/// <remarks>
/// The test file is a shortened copy of a real <c>server-config.json</c> from a user's PC
/// (from 30 August, the build before the <c>"port"</c> key was added). The comments are kept
/// on purpose: what is being tested is that they survive.
/// </remarks>
public class CoopKeepJsoncEditorTests
{
    private const string RealConfig = """
        {
          // Bannerlord Coop dedicated server configuration — the single source of
          // server settings. Comments and trailing commas are allowed in this file.

          // Save to host, from "Game Saves" in this folder (no .sav extension).
          "saveName": "saveauto1",

          // Minutes between world autosaves; 0 disables autosaving.
          "autosaveMinutes": 5,

          // Connection password (up to 128 characters). Empty = no password.
          "password": "",

          // Write everything the server prints to logs\coop-server-*.log
          "logFile": true,

          // Advertise this server on Steam.
          "steam": true,

          // Diagnostics. Only enable when asked to capture logs for a bug report.
          "traceTick": false,
          "tracePublish": false,
          "traceBandits": false,
        }
        """;

    [Fact]
    public void A_file_with_comments_and_trailing_commas_reads()
    {
        using var doc = SectionEditor.Parse(RealConfig).ToJsonDocument();

        Assert.Equal("saveauto1", doc.RootElement.GetProperty("saveName").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("autosaveMinutes").GetInt32());
        Assert.True(doc.RootElement.GetProperty("steam").GetBoolean());
    }

    [Fact]
    public void Keys_in_the_file_are_found_and_missing_ones_are_not()
    {
        var editor = SectionEditor.Parse(RealConfig);

        Assert.True(editor.ContainsKey("saveName"));
        Assert.True(editor.ContainsKey("traceBandits"));
        Assert.False(editor.ContainsKey("port"));      // really not in this file
        Assert.False(editor.ContainsKey("maxPlayers")); // the mod has no such setting
    }

    [Fact]
    public void Changing_the_save_keeps_the_comments_and_the_order()
    {
        var editor = SectionEditor.Parse(RealConfig);

        editor.SetString("saveName", "my_new_world");

        var result = editor.Text;

        // The value changed
        Assert.Contains("\"saveName\": \"my_new_world\"", result);
        Assert.DoesNotContain("saveauto1", result);

        // EVERY comment is still there
        Assert.Contains("// Bannerlord Coop dedicated server configuration", result);
        Assert.Contains("// Minutes between world autosaves; 0 disables autosaving.", result);
        Assert.Contains("// Advertise this server on Steam.", result);

        // The other keys were not touched
        Assert.Contains("\"autosaveMinutes\": 5", result);
        Assert.Contains("\"traceBandits\": false", result);

        // The order is kept
        Assert.True(result.IndexOf("saveName") < result.IndexOf("autosaveMinutes"));
        Assert.True(result.IndexOf("autosaveMinutes") < result.IndexOf("password"));
    }

    [Fact]
    public void Numbers_and_bools_are_written()
    {
        var editor = SectionEditor.Parse(RealConfig);

        editor.SetInt("autosaveMinutes", 15);
        editor.SetBool("steam", false);

        Assert.Contains("\"autosaveMinutes\": 15", editor.Text);
        Assert.Contains("\"steam\": false", editor.Text);

        using var doc = editor.ToJsonDocument();
        Assert.Equal(15, doc.RootElement.GetProperty("autosaveMinutes").GetInt32());
        Assert.False(doc.RootElement.GetProperty("steam").GetBoolean());
    }

    [Fact]
    public void A_password_with_special_characters_is_escaped()
    {
        var editor = SectionEditor.Parse(RealConfig);

        editor.SetString("password", "a\"b\\c");

        using var doc = editor.ToJsonDocument();
        Assert.Equal("a\"b\\c", doc.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public void A_missing_port_key_is_added_and_the_file_stays_valid()
    {
        // The user's real file is like this: no "port" key, so the server falls back to 4200.
        var editor = SectionEditor.Parse(RealConfig);
        Assert.False(editor.ContainsKey("port"));

        editor.SetInt("port", 4210);

        Assert.True(editor.ContainsKey("port"));
        using var doc = editor.ToJsonDocument();
        Assert.Equal(4210, doc.RootElement.GetProperty("port").GetInt32());

        // Adding it did not damage what was there
        Assert.Contains("\"saveName\": \"saveauto1\"", editor.Text);
        Assert.Contains("// Advertise this server on Steam.", editor.Text);
    }

    [Fact]
    public void An_added_key_can_be_changed_later()
    {
        var editor = SectionEditor.Parse(RealConfig);

        editor.SetInt("port", 4210);
        editor.SetInt("port", 4300);

        using var doc = editor.ToJsonDocument();
        Assert.Equal(4300, doc.RootElement.GetProperty("port").GetInt32());
        // Make sure it was not added twice
        Assert.Equal(1, editor.Text.Split("\"port\"").Length - 1);
    }

    [Fact]
    public void A_key_inside_a_comment_does_not_count()
    {
        // The server's own migration can leave settings behind as comment lines. Taking a
        // key in a comment for the real setting would write to the wrong place.
        var text = """
            {
              // "saveName": "value_in_comment",
              "saveName": "real_value"
            }
            """;

        var editor = SectionEditor.Parse(text);
        editor.SetString("saveName", "new");

        Assert.Contains("// \"saveName\": \"value_in_comment\"", editor.Text); // the comment stayed as it was
        Assert.Contains("\"saveName\": \"new\"", editor.Text);
        Assert.DoesNotContain("real_value", editor.Text);
    }

    [Fact]
    public void Keys_this_tool_does_not_know_are_kept()
    {
        // Coop gets new builds often. Deleting a setting we do not know would quietly
        // destroy the host's configuration.
        var text = """
            {
              "saveName": "x",
              "someFutureSetting": { "inner": [1, 2, 3] },
              "otherUnknown": "keep me"
            }
            """;

        var editor = SectionEditor.Parse(text);
        editor.SetString("saveName", "y");

        Assert.Contains("someFutureSetting", editor.Text);
        Assert.Contains("[1, 2, 3]", editor.Text);
        Assert.Contains("\"otherUnknown\": \"keep me\"", editor.Text);
    }

    [Fact]
    public void A_key_with_the_same_name_in_a_nested_object_does_not_damage_the_root()
    {
        // mod-config.json has difficulty and modOptions blocks, so the same name can turn up nested.
        var text = """
            {
              "difficulty": { "battleDeath": "VeryEasy" },
              "battleDeath": "rootLevel"
            }
            """;

        var editor = SectionEditor.Parse(text);
        editor.SetString("battleDeath", "changed");

        // The first match is the nested one; this test pins that behaviour down.
        using var doc = editor.ToJsonDocument();
        Assert.Equal("changed", doc.RootElement.GetProperty("difficulty").GetProperty("battleDeath").GetString());
        Assert.Equal("rootLevel", doc.RootElement.GetProperty("battleDeath").GetString());
    }

    [Fact]
    public void Saving_leaves_a_backup_and_keeps_the_content()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coodsz-jsonc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "server-config.json");
            File.WriteAllText(path, RealConfig);

            var editor = SectionEditor.Load(path);
            editor.SetString("saveName", "saved");
            editor.Save(path);

            Assert.True(File.Exists(path + ".bak"), "a backup file should be made");
            Assert.Contains("saveauto1", File.ReadAllText(path + ".bak")); // the old value in the backup
            Assert.Contains("saved", File.ReadAllText(path));              // the new value in the file
            Assert.False(File.Exists(path + ".tmp"), "the temporary file should be cleaned up");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // --- Coodsz additions ---------------------------------------------

    [Fact]
    public void Keys_match_without_regard_to_case_like_the_server()
    {
        var editor = SectionEditor.Parse("{\n  \"modOptions\": {\n    \"BattleSize\": 800\n  }\n}");
        Assert.Equal("800", editor.GetRawValue("modOptions", "battleSize"));
        editor.SetRawValue("modOptions", "battleSize", "600");
        Assert.Equal(1, editor.Text.Split("attleSize").Length - 1);
        Assert.Equal("600", editor.GetRawValue("MODOPTIONS", "battlesize"));
    }

    [Theory]
    [InlineData("{\n  \"modOptions\": {\n    \"voiceEnabled\": true // why\n  }\n}")]
    [InlineData("{\n  \"modOptions\": {\n    \"voiceEnabled\": true /* x */\n  }\n}")]
    [InlineData("{\r\n  \"modOptions\": {\r\n    \"voiceEnabled\": true,\r\n    // last comment\r\n  }\r\n}")]
    [InlineData("{\n  \"modOptions\": {}\n}")]
    public void A_key_added_after_a_trailing_comment_keeps_the_file_readable(string text)
    {
        var editor = SectionEditor.Parse(text);
        editor.SetRawValue("modOptions", "battleSize", "700");
        using var doc = editor.ToJsonDocument();
        Assert.Equal(700, doc.RootElement.GetProperty("modOptions").GetProperty("battleSize").GetInt32());
        if (text.Contains("//"))
            Assert.Contains("//", editor.Text);

        var root = SectionEditor.Parse(text);
        root.SetRawValue("port", "4200");
        using var rootDoc = root.ToJsonDocument();
        Assert.Equal(4200, rootDoc.RootElement.GetProperty("port").GetInt32());
    }
}

/// <summary>
/// <c>mod-config.json</c>, the world settings: reading and writing.
/// </summary>
/// <remarks>
/// The test file has the same shape and the same defaults as the <c>mod-config.default.json</c>
/// that comes with Coop v0.1.6, written for the tests: commented and nested. It is used to check
/// that writing keeps the comments and the keys the tool does not know.
/// </remarks>
public sealed class CoopKeepModConfigStoreTests : IDisposable
{
    // CoopKeep's test file, with spawnRateWanderers (in Coop's own default since 0.1.6) added.
    private const string Template = """
        {
          "voice": {
            "mapFullVolumeDistance": 5,
            "mapMaximumDistance": 20,
            "sceneFullVolumeDistance": 10,
            "sceneMaximumDistance": 50
          },
          // Test fixture written for CoopKeep. Same shape and default values as the
          // mod-config.default.json shipped with Coop v0.1.6 (nested sections, comments,
          // trailing keys we don't manage), but none of its text.

          // Difficulty levels: "VeryEasy" | "Easy" | "Realistic".
          "difficulty": {
            // Player damage.
            "playerReceivedDamage": "VeryEasy",

            // Troop damage.
            "playerTroopsReceivedDamage": "VeryEasy",

            // Enemy combat AI.
            "combatAIDifficulty": "VeryEasy",

            // Recruits from notables.
            "recruitmentDifficulty": "VeryEasy",

            // Party speed on the map.
            "playerMapMovementSpeed": "VeryEasy",

            // Stealth missions.
            "stealthAndDisguiseDifficulty": "VeryEasy",

            // Persuasion.
            "persuasionSuccessChance": "VeryEasy",

            // Clan member death in battle.
            "clanMemberDeathChance": "VeryEasy",

            // Hero death in battle.
            "battleDeath": "VeryEasy",

            // Aging, births and natural death.
            "birthAndDeath": true,

            // Perks for clan members.
            "autoAllocateClanMemberPerks": false
          },
          "modOptions": {
            "voiceEnabled": true,
            // Troops on the field, 200-1000.
            "battleSize": 1000,
            "fastForwardEnabled": true,
            "autoPauseEnabled": true,
            "clientsCanUseCheats": false,
            "goldFoodInfluenceChangeInSettlements": false,
            // "Disabled" | "OneDayMax" | "Enabled"
            "goldFoodInfluenceChangeInBattles": "Disabled",
            "goldFoodInfluenceChangeForDisconnectedPlayers": false,
            "blockAiWarDeclarationsOnOfflinePlayers": false,
            "playerBattleAiJoinWindowHours": 6,
            "speedLimitWhilePlayersInBattle": true,
            "ensureUnaffiliatedWanderers": true,
            "wandererLimit": 32,
            "wandererLimitScalesWithPlayers": false,
            "spawnRateWanderers": 1,
            "playerKingdomClanTierRequired": 4,
            "coopClansEnabled": true,
            "smithingStaminaRecoveryOutsideSettlements": true,
            "smithingStaminaRecoveryMultiplier": 0.1,
            "maximumLootersMultiplier": 1.0,
            "looterPartySizeMultiplier": 1.0,
            // "Vanilla" | "NeverExpire" | "AlwaysRetry"
            "lordDefectionRetries": "Vanilla",
            "enableHeroExecutions": false,
            "enablePlayerClanMemberExecutions": false,
            "enablePlayerExecutions": false,
            "showPlayerNameplates": true,
            "playerWoundedBattleEntry": true
          }
        }
        """;

    private readonly string _directory;
    private readonly string _template;
    private readonly ModConfigStore _store;

    public CoopKeepModConfigStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "coodsz-modconfig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _template = Path.Combine(_directory, "mod-config.default.json");
        File.WriteAllText(_template, Template);
        _store = new ModConfigStore(Path.Combine(_directory, "mod-config.json"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_missing_file_is_made_from_the_template()
    {
        _store.EnsureExists(_template);

        Assert.True(_store.Exists);
        Assert.Equal(File.ReadAllText(_template), File.ReadAllText(_store.Path));
    }

    [Fact]
    public void The_template_values_are_the_mods_defaults()
    {
        _store.EnsureExists(_template);

        var read = _store.Read();

        foreach (var d in ModSettingsCatalog.All)
            Assert.True(Equals(d.Default, read[d.Id]), $"{d.Id}: expected {d.Default}, read {read[d.Id]}");
    }

    [Fact]
    public void Difficulty_and_options_are_written_and_read_back()
    {
        _store.EnsureExists(_template);

        var values = _store.Read();
        ModSettingsCatalog.ApplyPreset(values, "Realistic");
        values["modOptions.battleSize"] = 600;
        values["modOptions.clientsCanUseCheats"] = true;
        values["modOptions.maximumLootersMultiplier"] = 1.5;
        values["modOptions.goldFoodInfluenceChangeInBattles"] = "OneDayMax";
        values["voice.mapMaximumDistance"] = 40;

        _store.Write(values);
        var read = _store.Read();

        Assert.Equal("Realistic", read["difficulty.playerReceivedDamage"]);
        Assert.Equal("Realistic", read["difficulty.battleDeath"]);
        Assert.Equal(600, read["modOptions.battleSize"]);
        Assert.Equal(true, read["modOptions.clientsCanUseCheats"]);
        Assert.Equal(1.5, read["modOptions.maximumLootersMultiplier"]);
        Assert.Equal("OneDayMax", read["modOptions.goldFoodInfluenceChangeInBattles"]);
        Assert.Equal(40, read["voice.mapMaximumDistance"]);
        Assert.Equal("Realistic", ModSettingsCatalog.DetectPreset(read));
    }

    [Fact]
    public void Writing_keeps_the_comments_and_the_keys_the_tool_does_not_know()
    {
        _store.EnsureExists(_template);

        var values = _store.Read();
        values["modOptions.battleSize"] = 500;
        _store.Write(values);

        var text = File.ReadAllText(_store.Path);
        Assert.Contains("// Player damage.", text);
        Assert.Contains("\"mapFullVolumeDistance\": 5", text);
        Assert.Contains("\"smithingStaminaRecoveryMultiplier\": 0.1", text);
        Assert.Contains("\"battleSize\": 500", text);

        // The lines that did not change stay exactly the same.
        var before = File.ReadAllLines(_template);
        var after = File.ReadAllLines(_store.Path);
        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after).Where(p => p.First != p.Second));
    }

    [Fact]
    public void The_same_key_in_another_section_does_not_get_the_write()
    {
        File.WriteAllText(_store.Path, """
            {
              "voice": { "battleSize": 1 },
              "modOptions": {
                "battleSize": 1000
              }
            }
            """);

        _store.Write(new Dictionary<string, object> { ["modOptions.battleSize"] = 300 });

        var text = File.ReadAllText(_store.Path);
        Assert.Contains("\"voice\": { \"battleSize\": 1 }", text);
        Assert.Contains("\"battleSize\": 300", text);
    }

    [Fact]
    public void A_key_missing_from_its_section_is_added_inside_it()
    {
        File.WriteAllText(_store.Path, """
            {
              "difficulty": {
                "playerReceivedDamage": "Easy"
              },
              "modOptions": {
                "battleSize": 1000
              }
            }
            """);

        _store.Write(new Dictionary<string, object> { ["modOptions.clientsCanUseCheats"] = true });

        var read = _store.Read();
        Assert.Equal(true, read["modOptions.clientsCanUseCheats"]);
        Assert.Equal("Easy", read["difficulty.playerReceivedDamage"]);

        var editor = SectionEditor.Load(_store.Path);
        Assert.Equal("true", editor.GetRawValue("modOptions", "clientsCanUseCheats"));
        Assert.Null(editor.GetRawValue("difficulty", "clientsCanUseCheats"));
    }

    [Fact]
    public void Values_out_of_range_are_clamped()
    {
        _store.EnsureExists(_template);

        _store.Write(new Dictionary<string, object>
        {
            ["modOptions.battleSize"] = 50_000,
            ["difficulty.battleDeath"] = "Impossible",
        });

        var read = _store.Read();
        // The file range is the widest one (Limitless); the screens clamp to the mod list.
        Assert.Equal(BigBattles.Max, read["modOptions.battleSize"]);
        Assert.Equal("VeryEasy", read["difficulty.battleDeath"]);
    }

    [Fact]
    public void A_world_profile_is_saved_copied_and_moved()
    {
        var profiles = new WorldProfileStore(Path.Combine(_directory, "worlds"));
        var values = ModSettingsCatalog.Defaults();
        ModSettingsCatalog.ApplyPreset(values, "Easy");

        profiles.Save("Calradia", values);
        profiles.Duplicate("Calradia", "Copy");
        profiles.Rename("Calradia", "New");

        Assert.False(profiles.Exists("Calradia"));
        Assert.Equal("Easy", profiles.Load("New")!["difficulty.combatAIDifficulty"]);
        Assert.Equal("Easy", profiles.Load("Copy")!["difficulty.combatAIDifficulty"]);

        profiles.Delete("Copy");
        Assert.Null(profiles.Load("Copy"));
    }

    // --- Coodsz additions ---------------------------------------------

    [Fact]
    public void A_profile_for_an_unusable_save_name_is_never_found()
    {
        var profiles = new WorldProfileStore(Path.Combine(_directory, "worlds"));
        Assert.False(profiles.Exists("..\\x"));
        Assert.Null(profiles.Load(""));
        Assert.Throws<ArgumentException>(() => profiles.Save("a/b", ModSettingsCatalog.Defaults()));
    }

    [Fact]
    public void A_capital_True_is_fixed_by_the_health_check_before_the_store_writes()
    {
        // A capital True makes the server ignore the whole file, and the store will not read it either.
        const string broken = "{\n  \"modOptions\": {\n    \"voiceEnabled\": True,\n    \"battleSize\": 1000\n  }\n}";
        Assert.Throws<InvalidOperationException>(() => ModConfigStore.ReadText(broken));

        var health = CoopModConfig.Check(broken);
        var written = ModConfigStore.WriteText(health.Text, new Dictionary<string, object> { ["modOptions.battleSize"] = 400 }, out var changed);
        Assert.Equal(1, changed);
        Assert.Equal(400, CoopModConfig.ReadBattleSize(written));
        Assert.True(CoopModConfig.ReadBool(written, "voiceEnabled"));
        Assert.Null(CoopModConfig.Check(written).BadLine);
    }

    [Fact]
    public void Writing_the_same_values_changes_nothing()
    {
        _store.EnsureExists(_template);
        Assert.Equal(0, _store.Write(_store.Read()));
        Assert.False(File.Exists(_store.Path + ".bak"));
    }
}

/// <summary>The settings catalogue is the one list the tools use for mod-config.json.</summary>
public class CoopKeepModSettingsCatalogTests
{
    [Fact]
    public void Every_setting_has_one_id_a_section_and_a_default_inside_its_range()
    {
        Assert.True(ModSettingsCatalog.All.Count >= 40);
        Assert.Equal(ModSettingsCatalog.All.Count, ModSettingsCatalog.All.Select(d => d.Key.ToLowerInvariant()).Distinct().Count());
        foreach (var d in ModSettingsCatalog.All)
        {
            Assert.Contains(d.Section, ModSettingsCatalog.Sections);
            Assert.Equal(d.Default, ModSettingsCatalog.Normalize(d, d.Default));
            Assert.False(string.IsNullOrWhiteSpace(d.Label));
        }
    }

    [Theory]
    [InlineData("battleSize", "modOptions")]
    [InlineData("spawnRateWanderers", "modOptions")]
    [InlineData("voiceEnabled", "modOptions")]
    [InlineData("birthAndDeath", "difficulty")]
    [InlineData("mapFullVolumeDistance", "voice")]
    [InlineData("sceneMaximumDistance", "voice")]
    public void CoopModConfig_reads_its_sections_from_the_catalogue(string key, string section)
    {
        Assert.Equal(section, CoopModConfig.SectionOf(key));
        Assert.Equal(section, ModSettingsCatalog.SectionOf(key.ToUpperInvariant()));
    }

    [Fact]
    public void Battle_size_and_difficulty_levels_come_from_the_catalogue()
    {
        var battle = ModSettingsCatalog.Find("modOptions.battleSize")!;
        // The file range goes up to the Limitless top; the screens clamp to the mod list.
        Assert.Equal((200d, (double)BigBattles.Max, 1000), (battle.Min, battle.Max, (int)battle.Default));
        Assert.Equal(CoopModConfig.BattleSizeDefault, (int)battle.Default);
        Assert.Same(ModSettingsCatalog.Levels, CoopDifficulty.Levels);
        foreach (var setting in CoopDifficulty.Settings)
            Assert.Equal("difficulty", ModSettingsCatalog.SectionOf(setting.Key));
        Assert.Null(ModSettingsCatalog.SectionOf("movementOutgoingMiBPerSecond"));
        Assert.Null(ModSettingsCatalog.SectionOf("saveName"));
    }

    [Theory]
    [InlineData("modOptions.wandererLimit", "40", 40)]
    [InlineData("modOptions.wandererLimit", "4.5", null)]
    [InlineData("modOptions.wandererLimit", "-1", null)]
    [InlineData("modOptions.battleSize", "1200", 1200)]
    [InlineData("modOptions.battleSize", "12000", null)]
    [InlineData("modOptions.battleSize", "abc", null)]
    public void Typed_numbers_outside_the_range_are_refused_not_clamped(string id, string text, int? expected)
    {
        var parsed = ModSettingsCatalog.ParseText(ModSettingsCatalog.Find(id)!, text);
        Assert.Equal(expected, parsed is int value ? value : null);
    }

    [Fact]
    public void Coodsz_Hosts_helpers_read_the_setting_inside_its_block_not_a_stray_copy()
    {
        const string json = "{\n  \"playerReceivedDamage\": \"VeryEasy\",\n  \"wandererLimit\": 99,\n  \"difficulty\": {\n    \"playerReceivedDamage\": \"Realistic\"\n  },\n  \"modOptions\": {\n    \"wandererLimit\": 32\n  }\n}";
        Assert.Equal("Realistic", CoodszHost.Services.JsoncEditor.GetString(json, "playerReceivedDamage"));
        Assert.Equal("32", CoodszHost.Services.JsoncEditor.GetRaw(json, "wandererLimit"));
        Assert.Equal("Realistic", CoopDifficulty.Read(json, "playerReceivedDamage"));
        // Keys that are not Coop settings keep the plain lookup.
        Assert.Equal("abc", CoodszHost.Services.JsoncEditor.GetString("{ \"password\": \"abc\" }", "password"));
    }
}
