using BannerlordCoopLauncher.Core.Configuration;

namespace BannerlordCoopLauncher.Tests;

// The mod-config.json keys Coop added after 0.1.6: each is offered, and only written for a server whose Coop has it.
public class CoopConfigKeysTests
{
    private const string Amnesty = "modOptions.postBattleAiAmnestySeconds";
    private const string Clans = "modOptions.clansAfterKingdomDestruction";
    private const string Hosts = "modOptions.preferredBattleHosts";
    private const string Vote = "modOptions.kingdomDecisionVoteTimeoutSeconds";
    private const string Wanderers = "modOptions.spawnRateWanderers";

    private const string IdA = "76561198000000001";
    private const string IdB = "76561198000000002";

    private const string Config = "{\n  \"modOptions\": {\n    \"battleSize\": 1000\n  }\n}\n";

    private static Dictionary<string, object> NewKeys() => new()
    {
        [Amnesty] = 30,
        [Clans] = "KeepClans",
        [Hosts] = new[] { IdA },
        [Vote] = 120,
        [Wanderers] = 2,
    };

    [Theory]
    [InlineData(Amnesty, ModSettingKind.Int, "v0.1.8")]
    [InlineData(Clans, ModSettingKind.Choice, "v0.1.8")]
    [InlineData(Hosts, ModSettingKind.SteamIdList, "v0.1.8")]
    [InlineData(Vote, ModSettingKind.Int, "v0.1.7")]
    [InlineData(Wanderers, ModSettingKind.Int, "v0.1.7")]
    public void The_new_keys_are_in_the_catalogue_with_their_Coop_version(string id, ModSettingKind kind, string since)
    {
        var setting = ModSettingsCatalog.Find(id)!;
        Assert.Equal(ModSettingsCatalog.OptionsSection, setting.Section);
        Assert.Equal(kind, setting.Kind);
        Assert.Equal(since, setting.Since);
        Assert.Null(ModSettingsCatalog.Find("modOptions.warSails"));
    }

    [Fact]
    public void The_new_keys_have_Coops_defaults()
    {
        var defaults = ModSettingsCatalog.Defaults("v0.1.8");
        Assert.Equal(0, defaults[Amnesty]);
        Assert.Equal("Vanilla", defaults[Clans]);
        Assert.Empty((string[])defaults[Hosts]);
        Assert.Equal(60, defaults[Vote]);
        Assert.Equal(["Vanilla", "KeepClans"], ModSettingsCatalog.Find(Clans)!.Choices!);
        // Every key still has a default for an older or unknown Coop, so the pages never miss one.
        Assert.Equal(ModSettingsCatalog.All.Count, ModSettingsCatalog.Defaults(null).Count);
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("v0.1.6", new string[0])]
    [InlineData("garbled", new string[0])]
    [InlineData("v0.1.7", new[] { "kingdomDecisionVoteTimeoutSeconds", "spawnRateWanderers" })]
    [InlineData("0.1.7.0", new[] { "kingdomDecisionVoteTimeoutSeconds", "spawnRateWanderers" })]
    [InlineData("v0.1.8", new[] { "kingdomDecisionVoteTimeoutSeconds", "spawnRateWanderers", "postBattleAiAmnestySeconds", "clansAfterKingdomDestruction", "preferredBattleHosts" })]
    public void A_key_is_only_added_for_a_Coop_that_knows_it(string? coopVersion, string[] added)
    {
        var text = ModConfigStore.WriteText(Config, NewKeys(), out var changed, coopVersion);

        Assert.Equal(added.Length, changed);
        foreach (var key in new[] { "kingdomDecisionVoteTimeoutSeconds", "spawnRateWanderers", "postBattleAiAmnestySeconds", "clansAfterKingdomDestruction", "preferredBattleHosts" })
            Assert.Equal(added.Contains(key), JsoncEditor.Parse(text).GetRawValue("modOptions", key) != null);
    }

    [Fact]
    public void The_old_write_without_a_version_adds_no_new_key()
    {
        var text = ModConfigStore.WriteText(Config, NewKeys(), out var changed);

        Assert.Equal(0, changed);
        Assert.Equal(Config, text);
    }

    [Fact]
    public void A_key_the_file_already_has_is_updated_for_an_unknown_Coop()
    {
        // The server's own template put the keys there, so its Coop knows them.
        var file = "{\n  \"modOptions\": {\n    \"postBattleAiAmnestySeconds\": 0,\n    \"preferredBattleHosts\": []\n  }\n}\n";

        var text = ModConfigStore.WriteText(file, NewKeys(), out var changed, null);

        Assert.Equal(2, changed);
        var editor = JsoncEditor.Parse(text);
        Assert.Equal("30", editor.GetRawValue("modOptions", "postBattleAiAmnestySeconds"));
        Assert.Equal("[\"" + IdA + "\"]", editor.GetRawValue("modOptions", "preferredBattleHosts"));
        Assert.Null(editor.GetRawValue("modOptions", "clansAfterKingdomDestruction"));
        Assert.Null(editor.GetRawValue("modOptions", "kingdomDecisionVoteTimeoutSeconds"));
    }

    [Fact]
    public void The_same_host_list_is_not_written_again()
    {
        var file = "{\n  \"modOptions\": {\n    \"preferredBattleHosts\": [ \"" + IdA + "\", \"" + IdB + "\" ]\n  }\n}\n";

        var text = ModConfigStore.WriteText(file, new Dictionary<string, object> { [Hosts] = new[] { IdA, IdB } }, out var changed, "v0.1.8");

        Assert.Equal(0, changed);
        Assert.Equal(file, text);
    }

    [Fact]
    public void Steam_ids_are_trimmed_checked_kept_once_and_in_order()
    {
        var setting = ModSettingsCatalog.Find(Hosts)!;

        var normalized = (string[])ModSettingsCatalog.Normalize(setting, new[] { " " + IdB + " ", "not an id", IdA, IdB, "7656119800000000", "" });
        Assert.Equal([IdB, IdA], normalized);
        Assert.Equal("[\"" + IdB + "\",\"" + IdA + "\"]", ModSettingsCatalog.ToJsonLiteral(setting, normalized));
        Assert.Empty((string[])ModSettingsCatalog.Normalize(setting, 5));

        Assert.Equal([IdA, IdB], (string[])ModSettingsCatalog.ParseText(setting, IdA + ",\n" + IdB + " " + IdA)!);
        Assert.Null(ModSettingsCatalog.ParseText(setting, IdA + ", Bob"));
    }

    [Fact]
    public void The_host_list_reads_back_from_the_file_and_a_save_profile()
    {
        var text = ModConfigStore.WriteText(Config, new Dictionary<string, object> { [Hosts] = new[] { IdB, IdA } }, out _, "v0.1.8");
        Assert.Equal([IdB, IdA], (string[])ModConfigStore.ReadText(text, "v0.1.8")[Hosts]);

        var directory = Path.Combine(Path.GetTempPath(), "coodsz-coop-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new WorldProfileStore(directory);
            var values = ModSettingsCatalog.Defaults("v0.1.8");
            values[Hosts] = new[] { IdB, IdA };
            profiles.Save("World", values);
            Assert.Equal([IdB, IdA], (string[])profiles.Load("World", "v0.1.8")![Hosts]);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("3600", 3600)]
    [InlineData("0", null)]
    [InlineData("3601", null)]
    public void The_vote_timer_takes_one_second_to_one_hour(string text, int? expected)
    {
        var setting = ModSettingsCatalog.Find(Vote)!;
        Assert.Equal(expected, (int?)ModSettingsCatalog.ParseText(setting, text));
        Assert.Equal(1, ModSettingsCatalog.Normalize(setting, -5));
    }

    [Fact]
    public void Streamer_mode_masks_the_battle_host_list_and_hides_the_paste_box()
    {
        // The list was drawn with the old mask, and a pasted Steam ID shows as typed (pr-reviewer, Codex).
        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("StreamerPreferredHosts(on);", SourceFiles.Method(window, "private void ApplyStreamerMode()"));
        var streamer = SourceFiles.Method(window, "private void StreamerPreferredHosts(bool on)");
        Assert.Contains("ShowPreferredHosts();", streamer);
        Assert.Contains("_preferredHostsPasteRow.Visibility = on ? Visibility.Collapsed", streamer);
    }

    [Fact]
    public void A_new_server_folder_greys_or_offers_the_newer_settings_again()
    {
        // Set server folder and Use the Steam server reload the modules; the server's Coop version may have changed.
        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("ShowCoopSettingSupport();", SourceFiles.Method(window, "private void ReloadModules(bool install = true)"));
    }

    [Fact]
    public void An_older_save_profile_keeps_the_newer_values_mod_config_has()
    {
        // A profile kept by v1.3.13 has none of the new keys; Start server must not reset a host's hand set value
        // in mod-config.json to Coop's default (release-reviewer).
        var folder = Path.Combine(Path.GetTempPath(), "old-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Old.settings.json"), "{ \"modOptions.battleSize\": 500 }");
            var file = ModSettingsCatalog.Defaults("v0.1.8");
            file[Hosts] = new[] { "76561198000000002" };
            file[Vote] = 120;
            file["modOptions.battleSize"] = 900;

            var kept = new WorldProfileStore(folder).Load("Old", "v0.1.8", file)!;

            Assert.Equal(new[] { "76561198000000002" }, (string[])kept[Hosts]);
            Assert.Equal(120, kept[Vote]);
            // What the profile has still wins.
            Assert.Equal(500, kept["modOptions.battleSize"]);
            Assert.Empty((string[])new WorldProfileStore(folder).Load("Old", "v0.1.8")![Hosts]);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }

        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("SaveProfiles.Load(saveName, ServerCoopVersion(), store.Read(ServerCoopVersion()))", SourceFiles.Method(window, "private void ApplySaveProfile("));
        Assert.Contains("SaveProfiles.Load(saveName, ServerCoopVersion(), file)", SourceFiles.Method(window, "private Dictionary<string, object> CoopSettingsFor("));
    }

    [Fact]
    public void The_ad1259_server_tool_hides_the_nightly_only_rows()
    {
        var build = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void BuildMoreCoopSettings()");
        Assert.Contains("!(Edition.IsAD1259 && d.Since == \"v0.1.8\")", build);
    }
}
