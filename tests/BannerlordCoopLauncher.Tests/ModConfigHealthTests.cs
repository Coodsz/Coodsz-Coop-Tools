using System.Text.Json;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class ModConfigHealthTests
{
    private static readonly JsonDocumentOptions ServerLike = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // A real looking mod-config.json with the hand edit from ticket 4187 on line 44.
    private static string TicketConfig(string cheats = "True")
    {
        var lines = new List<string>
        {
            "{",
            "  \"voice\": {",
            "    \"mapFullVolumeDistance\": 5,",
            "    \"mapMaximumDistance\": 20",
            "  },",
            "  // Campaign difficulty, applied by the HOSTING side at every boot.",
            "  \"difficulty\": {",
            "    \"playerReceivedDamage\": \"VeryEasy\",",
            "    \"playerTroopsReceivedDamage\": \"VeryEasy\",",
            "    \"combatAIDifficulty\": \"VeryEasy\",",
            "    \"recruitmentDifficulty\": \"VeryEasy\",",
            "    \"playerMapMovementSpeed\": \"VeryEasy\",",
            "    \"stealthAndDisguiseDifficulty\": \"VeryEasy\",",
            "    \"persuasionSuccessChance\": \"VeryEasy\",",
            "    \"clanMemberDeathChance\": \"VeryEasy\",",
            "    \"battleDeath\": \"VeryEasy\",",
            "    \"birthAndDeath\": true,",
            "    \"autoAllocateClanMemberPerks\": false",
            "  },",
            "  \"modOptions\": {",
            "    // Enables proximity voice for every player.",
            "    \"voiceEnabled\": true,",
            "    // Total human troops initially allocated across both sides of a battle.",
            "    // Valid range: 200-1000. Default is 1000.",
            "    \"battleSize\": 450,",
            "",
        };
        while (lines.Count < 39)
            lines.Add("    // Toggle fast forward (2x speed) being enabled");
        lines.Add("    \"fastForwardEnabled\": true,");
        lines.Add("    \"autoPauseEnabled\": true,");
        lines.Add("    // Toggle clients being able to use cheats");
        lines.Add("    // Default is false.");
        lines.Add("    \"clientsCanUseCheats\": " + cheats + ",");
        lines.Add("    \"note\": \"Keep True here, it is text\",");
        lines.Add("    \"goldFoodInfluenceChangeInBattles\": \"Disabled\",");
        lines.Add("    \"wandererLimit\": 32,");
        lines.Add("  }");
        lines.Add("}");
        Assert.Equal("    \"clientsCanUseCheats\": " + cheats + ",", lines[43]);
        return string.Join("\r\n", lines) + "\r\n";
    }

    [Fact]
    public void A_capital_True_on_line_44_is_fixed_and_named_and_the_rest_of_the_file_is_kept()
    {
        var broken = TicketConfig();
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(broken, ServerLike).Dispose());

        var health = CoopModConfig.Check(broken);

        Assert.True(health.Changed);
        Assert.Null(health.BadLine);
        Assert.False(health.OtherRepairs);
        var fix = Assert.Single(health.BoolFixes);
        Assert.Equal(44, fix.Line);
        Assert.Equal("clientsCanUseCheats", fix.Key);
        Assert.Equal("True", fix.Written);
        Assert.Equal("true", fix.Fixed);
        Assert.Equal(TicketConfig("true"), health.Text);
        Assert.Contains("\"note\": \"Keep True here, it is text\"", health.Text);
        Assert.Contains("// Toggle clients being able to use cheats", health.Text);
        using (var parsed = JsonDocument.Parse(health.Text, ServerLike))
        {
            var options = parsed.RootElement.GetProperty("modOptions");
            Assert.True(options.GetProperty("clientsCanUseCheats").GetBoolean());
            Assert.Equal(450, options.GetProperty("battleSize").GetInt32());
        }

        var note = CoopModConfig.BoolFixNote(fix);
        Assert.Contains("line 44", note);
        Assert.Contains("clientsCanUseCheats", note);
        Assert.Contains("battle size 1000", note);
        Assert.Equal("", CoopModConfig.BadLineWarning(health, "mod-config.json"));

        // Repaired once, the file stays the same.
        var again = CoopModConfig.Check(health.Text);
        Assert.False(again.Changed);
        Assert.Empty(again.BoolFixes);
        Assert.Equal(health.Text, JsoncValues.Repair(health.Text));
    }

    [Fact]
    public void Every_casing_of_true_and_false_is_fixed_but_text_and_lowercase_values_are_left_alone()
    {
        const string json = "{\n  \"modOptions\": {\n    \"a\": TRUE,\n    \"b\": False\n  , \"c\": true, \"d\": \"False\" /* x */, \"e\": fAlSe // why\n  }\n}";
        var fixes = new List<JsoncValues.BoolFix>();
        var fixedText = JsoncValues.FixBoolCase(json, fixes);
        Assert.Equal("{\n  \"modOptions\": {\n    \"a\": true,\n    \"b\": false\n  , \"c\": true, \"d\": \"False\" /* x */, \"e\": false // why\n  }\n}", fixedText);
        Assert.Equal(new[] { "a", "b", "e" }, fixes.Select(item => item.Key));
        Assert.Equal(new[] { 3, 4, 5 }, fixes.Select(item => item.Line));
        // The shared repair every setting write goes through fixes it too.
        Assert.Equal(fixedText, JsoncValues.Repair(json));
        Assert.True(CoopModConfig.ReadBool(CoopModConfig.SetBool(json, "c", true), "a"));
        // JsoncEditor (Coodsz Host's Save settings) replaces the value instead of adding a second key.
        var saved = JsoncEditor.SetBool(json, "a", false);
        Assert.Contains("\"a\": false", saved);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(saved, "\"a\""));
    }

    [Fact]
    public void A_mistake_that_is_not_safe_to_guess_is_warned_with_its_line_and_left_for_the_host()
    {
        var broken = TicketConfig("true").Replace("\"wandererLimit\": 32,", "\"wandererLimit\": 32 players,", StringComparison.Ordinal);
        var health = CoopModConfig.Check(broken);

        Assert.False(health.Changed);
        Assert.Equal(47, health.BadLine);
        Assert.Equal("\"wandererLimit\": 32 players,", health.BadText);
        var warning = CoopModConfig.BadLineWarning(health, @"C:\Users\Host\Documents\Mount and Blade II Bannerlord\CoopData\mod-config.json");
        Assert.Contains("line 47", warning);
        Assert.Contains("\"wandererLimit\": 32 players,", warning);
        Assert.Contains("ignores the whole file", warning);
        Assert.Contains("battle size 1000", warning);
        Assert.Contains(@"CoopData\mod-config.json", warning);

        // A capital True and a real mistake together: the True is fixed, the mistake is still named.
        var both = CoopModConfig.Check(TicketConfig().Replace("\"wandererLimit\": 32,", "\"wandererLimit\": 32 players,", StringComparison.Ordinal));
        Assert.True(both.Changed);
        Assert.Single(both.BoolFixes);
        Assert.Equal(47, both.BadLine);

        var cut = CoopModConfig.Check("{\n  \"modOptions\": {\n    \"battleSize\": 600,\n");
        Assert.NotNull(cut.BadLine);
        Assert.Contains("battle size 1000", CoopModConfig.BadLineWarning(cut, "mod-config.json"));

        var fine = CoopModConfig.Check(TicketConfig("false"));
        Assert.False(fine.Changed);
        Assert.Null(fine.BadLine);
        Assert.Equal("", CoopModConfig.BadLineWarning(fine, "mod-config.json"));
    }

    [Fact]
    public void Battle_size_is_read_from_mod_options_like_the_server_and_defaults_to_1000()
    {
        Assert.Equal(450, CoopModConfig.ReadBattleSize(TicketConfig()));
        Assert.Equal(1000, CoopModConfig.ReadBattleSize(""));
        Assert.Equal(1000, CoopModConfig.ReadBattleSize("{\n  \"modOptions\": {\n    \"voiceEnabled\": true\n  }\n}"));
        Assert.Equal(200, CoopModConfig.ReadBattleSize("{ \"modOptions\": { \"battleSize\": 150 } }"));
        Assert.Equal(1000, CoopModConfig.ReadBattleSize("{ \"modOptions\": { \"battleSize\": 5000 } }"));
        // The server does not read a battleSize outside modOptions, so neither does the tool.
        Assert.Equal(1000, CoopModConfig.ReadBattleSize("{ \"battleSize\": 300, \"modOptions\": { \"voiceEnabled\": true } }"));
        Assert.Equal(1000, CoopModConfig.ReadBattleSize("{ \"modOptions\": { \"battleSize\": \"lots\" } }"));
    }

    [Fact]
    public void Battle_size_updates_in_place_and_keeps_comments_and_other_settings()
    {
        var original = TicketConfig("false");
        var updated = CoopModConfig.SetBattleSize(original, 700);
        Assert.Equal(original.Replace("\"battleSize\": 450,", "\"battleSize\": 700,", StringComparison.Ordinal), updated);
        Assert.Equal(700, CoopModConfig.ReadBattleSize(updated));
        Assert.Equal(200, CoopModConfig.ReadBattleSize(CoopModConfig.SetBattleSize(original, 50)));
        Assert.Equal(1000, CoopModConfig.ReadBattleSize(CoopModConfig.SetBattleSize(original, 4000)));
        Assert.Contains("\"battleSize\": 200,", CoopModConfig.SetBattleSize(original, -3));

        // Saving also fixes a capital True somewhere else in the file.
        var fromBroken = CoopModConfig.SetBattleSize(TicketConfig(), 800);
        Assert.Equal(TicketConfig("true").Replace("\"battleSize\": 450,", "\"battleSize\": 800,", StringComparison.Ordinal), fromBroken);
    }

    [Fact]
    public void A_missing_battle_size_is_added_inside_mod_options()
    {
        var withoutKey = TicketConfig("false").Replace("    \"battleSize\": 450,\r\n", "", StringComparison.Ordinal);
        Assert.DoesNotContain("battleSize\":", withoutKey);
        AssertBattleSizeInModOptions(CoopModConfig.SetBattleSize(withoutKey, 600), 600, "\"wandererLimit\": 32");

        // Last value without a comma and a comment after it.
        const string tight = "{\n  \"modOptions\": {\n    \"voiceEnabled\": true // on\n  },\n  \"voice\": { \"mapMaximumDistance\": 20 }\n}";
        var added = CoopModConfig.SetBattleSize(tight, 500);
        AssertBattleSizeInModOptions(added, 500, "\"voiceEnabled\": true, // on");
        Assert.Equal(20, JsonDocument.Parse(added, ServerLike).RootElement.GetProperty("voice").GetProperty("mapMaximumDistance").GetInt32());

        // An empty block, and a file with no modOptions block at all.
        AssertBattleSizeInModOptions(CoopModConfig.SetBattleSize("{\n  \"modOptions\": {}\n}", 300), 300, null);
        AssertBattleSizeInModOptions(CoopModConfig.SetBattleSize("{\n  \"voice\": { \"mapMaximumDistance\": 20 }\n}", 900), 900, "\"mapMaximumDistance\": 20");

        // A battleSize an older save wrote at the top level is not the one the server reads.
        var rootOnly = "{\n  \"battleSize\": 300,\n  \"modOptions\": {\n    \"voiceEnabled\": true\n  }\n}";
        AssertBattleSizeInModOptions(CoopModConfig.SetBattleSize(rootOnly, 400), 400, "\"voiceEnabled\": true,");
    }

    private void AssertBattleSizeInModOptions(string json, int expected, string? kept)
    {
        using var parsed = JsonDocument.Parse(json, ServerLike);
        Assert.Equal(expected, parsed.RootElement.GetProperty("modOptions").GetProperty("battleSize").GetInt32());
        Assert.Equal(expected, CoopModConfig.ReadBattleSize(json));
        if (kept != null)
            Assert.Contains(kept, json);
        Assert.Equal(json, CoopModConfig.SetBattleSize(json, expected));
    }

    private const string TicketLog =
        "[12:01:44 ERR] [Coop] mod-config.json UNREADABLE (C:\\Users\\Host\\Documents\\Mount and Blade II Bannerlord\\CoopData\\mod-config.json) — continuing on defaults: \"Unexpected character encountered while parsing value: r. Path 'modOptions.clientsCanUseCheats', line 44, position 28.\"";

    [Fact]
    public void The_server_log_line_for_an_unreadable_mod_config_is_explained_in_plain_words()
    {
        var lines = ServerCrashExplain.FindUnreadableModConfig("starting\n" + TicketLog + "\nserver ready");
        Assert.NotNull(lines);
        Assert.Equal(3, lines!.Count);
        Assert.StartsWith("What happened:", lines[0]);
        Assert.Contains("line 44", lines[0]);
        Assert.Contains("battle size 1000", lines[0]);
        Assert.StartsWith("What to do:", lines[1]);
        Assert.StartsWith("What not to do:", lines[2]);
        Assert.Contains("game options", lines[2]);
        foreach (var line in lines)
            Assert.DoesNotContain(":", line[(line.IndexOf(':') + 1)..]);

        var noLine = ServerCrashExplain.FindUnreadableModConfig("[Coop] mod-config.json UNREADABLE (x) — continuing on defaults: Access denied");
        Assert.DoesNotContain("line", noLine![0][..60]);

        Assert.Null(ServerCrashExplain.FindUnreadableModConfig("mod-config.json loaded (C:\\x\\mod-config.json)"));
        Assert.Null(ServerCrashExplain.FindUnreadableModConfig(null));

        // After a stop, the cause comes first and the config notice is added under it.
        var explained = ServerCrashExplain.Explain("engine exited with code 0", true, TicketLog, null, [], ["ImprovedGarrisons"]);
        Assert.Contains("normal exit", explained[0]);
        Assert.Equal(lines, explained.Skip(explained.Count - 3).ToList());
        Assert.Equal(3, ServerCrashExplain.Explain("engine exited with code 0", true, "", null, [], ["ImprovedGarrisons"]).Count);
    }

    private const string NoCheatsKey = "{\r\n  // keep this comment\r\n  \"difficulty\": {\r\n    \"battleDeath\": \"Easy\"\r\n  },\r\n  \"modOptions\": {\r\n    // \"clientsCanUseCheats\": true,\r\n    \"voiceEnabled\": true, // voice\r\n    \"wandererLimit\": 32\r\n  },\r\n  \"network\": { \"movementOutgoingMiBPerSecond\": 10 }\r\n}\r\n";

    [Fact]
    public void A_missing_mod_option_is_added_inside_mod_options_by_every_setter()
    {
        foreach (var write in new Func<string, string>[]
        {
            json => JsoncEditor.SetBool(json, "clientsCanUseCheats", true),
            json => CoopModConfig.SetBool(json, "clientsCanUseCheats", true),
            json => JsoncEditor.SetRaw(json, "clientsCanUseCheats", "true"),
        })
        {
            var saved = write(NoCheatsKey);
            using var parsed = JsonDocument.Parse(saved, ServerLike);
            Assert.True(parsed.RootElement.GetProperty("modOptions").GetProperty("clientsCanUseCheats").GetBoolean());
            Assert.False(parsed.RootElement.TryGetProperty("clientsCanUseCheats", out _));
            Assert.Contains("// keep this comment", saved);
            Assert.Contains("// \"clientsCanUseCheats\": true,", saved);
            Assert.Contains("\"voiceEnabled\": true, // voice", saved);
            Assert.Equal("true", CoopModConfig.ReadRaw(saved, "clientsCanUseCheats"));
            Assert.Equal("true", CoopModConfig.ReadRaw(CoopModConfig.SetBool(saved, "voiceEnabled", false), "clientsCanUseCheats"));
        }

        var numbers = JsoncEditor.SetNumber(NoCheatsKey, "playerBattleAiJoinWindowHours", "6");
        numbers = JsoncEditor.SetNumber(numbers, "wandererLimit", "40");
        numbers = JsoncEditor.SetString(numbers, "lordDefectionRetries", "NeverExpire");
        numbers = JsoncEditor.SetString(numbers, "playerReceivedDamage", "Realistic");
        numbers = JsoncEditor.SetString(numbers, "battleDeath", "Realistic");
        using (var parsed = JsonDocument.Parse(numbers, ServerLike))
        {
            var options = parsed.RootElement.GetProperty("modOptions");
            Assert.Equal(6, options.GetProperty("playerBattleAiJoinWindowHours").GetInt32());
            Assert.Equal(40, options.GetProperty("wandererLimit").GetInt32());
            Assert.Equal("NeverExpire", options.GetProperty("lordDefectionRetries").GetString());
            var difficulty = parsed.RootElement.GetProperty("difficulty");
            Assert.Equal("Realistic", difficulty.GetProperty("playerReceivedDamage").GetString());
            Assert.Equal("Realistic", difficulty.GetProperty("battleDeath").GetString());
            // The old network block stays where it is.
            Assert.Equal(10, parsed.RootElement.GetProperty("network").GetProperty("movementOutgoingMiBPerSecond").GetInt32());
            Assert.Equal(3, parsed.RootElement.EnumerateObject().Count());
        }

        // A file without a modOptions block gets one.
        var bare = JsoncEditor.SetBool("{\r\n  \"voice\": { \"mapMaximumDistance\": 20 }\r\n}", "autoPauseEnabled", false);
        using (var parsed = JsonDocument.Parse(bare, ServerLike))
            Assert.False(parsed.RootElement.GetProperty("modOptions").GetProperty("autoPauseEnabled").GetBoolean());
    }

    [Fact]
    public void Server_config_keys_stay_at_the_top_level()
    {
        const string server = "{\r\n  // Minutes between world autosaves\r\n  // \"autosaveMinutes\": 99,\r\n  \"saveName\": \"saveauto1\",\r\n  \"steam\": true\r\n}";
        var saved = JsoncEditor.SetNumber(server, "autosaveMinutes", "5");
        saved = JsoncEditor.SetBool(saved, "steam", false);
        saved = JsoncEditor.SetString(saved, "password", "abc");
        saved = JsoncEditor.SetString(saved, "saveName", "other");
        using var parsed = JsonDocument.Parse(saved, ServerLike);
        Assert.Equal(5, parsed.RootElement.GetProperty("autosaveMinutes").GetInt32());
        Assert.False(parsed.RootElement.GetProperty("steam").GetBoolean());
        Assert.Equal("abc", parsed.RootElement.GetProperty("password").GetString());
        Assert.Equal("other", parsed.RootElement.GetProperty("saveName").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("modOptions", out _));
        // The commented example is left alone; the real value was added.
        Assert.Contains("// \"autosaveMinutes\": 99,", saved);
    }

    [Fact]
    public void Stray_top_level_settings_from_older_tools_are_moved_where_the_server_reads_them()
    {
        // Older tools added missing settings above the closing brace, at the top level.
        var stray = NoCheatsKey.Replace("}\r\n}\r\n", "},\r\n  \"clientsCanUseCheats\": true,\r\n  \"wandererLimit\": 99,\r\n  \"birthAndDeath\": false\r\n}\r\n", StringComparison.Ordinal);
        using (var before = JsonDocument.Parse(stray, ServerLike))
            Assert.True(before.RootElement.TryGetProperty("clientsCanUseCheats", out _));

        var health = CoopModConfig.Check(stray);
        Assert.True(health.Changed);
        Assert.Null(health.BadLine);
        Assert.Equal(new[]
        {
            new CoopModConfig.StrayFix("clientsCanUseCheats", "modOptions", true),
            new CoopModConfig.StrayFix("wandererLimit", "modOptions", false),
            new CoopModConfig.StrayFix("birthAndDeath", "difficulty", true),
        }, health.Strays);
        using (var parsed = JsonDocument.Parse(health.Text, ServerLike))
        {
            var root = parsed.RootElement;
            Assert.Equal(new[] { "difficulty", "modOptions", "network" }, root.EnumerateObject().Select(p => p.Name));
            Assert.True(root.GetProperty("modOptions").GetProperty("clientsCanUseCheats").GetBoolean());
            // modOptions already had wandererLimit, and that is the value the server used.
            Assert.Equal(32, root.GetProperty("modOptions").GetProperty("wandererLimit").GetInt32());
            Assert.False(root.GetProperty("difficulty").GetProperty("birthAndDeath").GetBoolean());
            Assert.Equal("Easy", root.GetProperty("difficulty").GetProperty("battleDeath").GetString());
        }

        Assert.Contains("// keep this comment", health.Text);
        Assert.Contains("\"voiceEnabled\": true, // voice", health.Text);
        Assert.Contains("Moved clientsCanUseCheats into modOptions", CoopModConfig.StrayFixNote(health.Strays[0]));
        Assert.Contains("Removed an extra wandererLimit", CoopModConfig.StrayFixNote(health.Strays[1]));
        var again = CoopModConfig.Check(health.Text);
        Assert.False(again.Changed);
        Assert.Empty(again.Strays);

        // Saving a setting also clears its stray copy instead of changing the one the server ignores.
        var saved = JsoncEditor.SetBool(stray, "clientsCanUseCheats", false);
        using (var parsed = JsonDocument.Parse(saved, ServerLike))
        {
            Assert.False(parsed.RootElement.TryGetProperty("clientsCanUseCheats", out _));
            Assert.False(parsed.RootElement.GetProperty("modOptions").GetProperty("clientsCanUseCheats").GetBoolean());
        }

        // A file the server cannot read is not reshuffled, only warned about.
        var broken = CoopModConfig.Check(stray.Replace("\"wandererLimit\": 32", "\"wandererLimit\": 32 players", StringComparison.Ordinal));
        Assert.NotNull(broken.BadLine);
        Assert.Empty(broken.Strays);
    }
}
