using System.Text.Json.Nodes;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Tests;

/// <summary>COOP Server Settings Lock left the Coop and Friends setup in v1.3.4.</summary>
public class RetiredSettingsLockTests
{
    private static readonly string[] SaveWithLock = ["Native", "SandBoxCore", "Sandbox", "Bannerlord.Harmony", "Coop", "ImprovedGarrisons", "ServerSettingsLock"];

    [Fact]
    public void A_save_made_with_settings_lock_still_starts_without_it()
    {
        // It only patches MCM values and keeps nothing in a save (no CampaignSystem or SaveSystem types in its DLL).
        var change = SaveModChange.Check(SaveWithLock, new[] { "Native", "SandBoxCore", "SandBox", "Bannerlord.Harmony", "Coop", "ImprovedGarrisons" });
        Assert.Equal(SaveModAction.Start, change.Action);
        Assert.Empty(change.Removed);
        Assert.Contains("ServerSettingsLock", SaveModChange.NoSaveDataModules);

        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        Assert.Contains("'Bannerlord.UIExtenderEx', 'ServerSettingsLock')", script);
    }

    [Fact]
    public void The_note_shows_each_run_while_a_setup_still_turns_it_on()
    {
        string[] on = ["Coop", "ServerSettingsLock"];
        string[] off = ["Coop", "CoopModPatch"];
        var noted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(RetiredSettingsLock.ShowNote("Mine", locked: false, on, noted));
        // Once per run: the module list loads again (twice on start, again on every setup switch).
        Assert.False(RetiredSettingsLock.ShowNote("Mine", locked: false, on, noted));
        Assert.True(RetiredSettingsLock.ShowNote("Other", locked: false, ["serversettingslock"], noted));
        Assert.False(RetiredSettingsLock.ShowNote("Plain", locked: true, on, noted));
        Assert.False(RetiredSettingsLock.ShowNote("Clean", locked: false, off, noted));

        // Turned off, then on again in the same run: the note comes back.
        Assert.False(RetiredSettingsLock.ShowNote("Mine", locked: false, off, noted));
        Assert.True(RetiredSettingsLock.ShowNote("Mine", locked: false, on, noted));

        // Nothing is kept in the settings, so the next start shows it again while the mod is still on.
        Assert.True(RetiredSettingsLock.ShowNote("Mine", locked: false, on, new HashSet<string>()));
        Assert.Null(typeof(ClientSetup).GetProperty("SettingsLockNoteSeen"));
        Assert.Null(typeof(CoodszHost.Services.HostProfile).GetProperty("SettingsLockNoteSeen"));
    }

    [Fact]
    public void Start_does_not_tell_players_to_keep_settings_lock()
    {
        // Its SubModule says DedicatedServerType none, which normally gets "Players can keep it." on Start. That
        // would contradict the note that says to turn it off, so it gets no such line.
        const string xml = "<Module><SubModules><SubModule><Tags><Tag key=\"DedicatedServerType\" value=\"none\"/></Tags></SubModule></SubModules></Module>";
        Assert.Null(ServerModuleNotes.SkippedCodeNote("ServerSettingsLock", "Server Settings Lock", xml));
        Assert.Null(ServerModuleNotes.SkippedCodeNote("serversettingslock", "Server Settings Lock", xml));
        Assert.NotNull(ServerModuleNotes.SkippedCode("Server Settings Lock", xml));
        Assert.Contains("Players can keep it.", ServerModuleNotes.SkippedCodeNote("SomeClientMod", "Some Client Mod", xml));

        // Both Start paths use the note that knows about it.
        foreach (var file in new[] { Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"), Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Launch.cs") })
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), file));
            Assert.Contains("ServerModuleNotes.SkippedCodeNote(", source);
            Assert.DoesNotContain("ServerModuleNotes.SkippedCode(", source);
        }
    }

    [Fact]
    public void Start_copies_no_setup_mcm_file_when_seeding_stopped_partway()
    {
        // A failure outside one file (the live copy into the setup) returns null, and Start then keeps every live file.
        var start = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.ServerControl.cs"));
        Assert.Contains("var keepLive = SeedSettingsLockDefaults(profile);\r\n        if (keepLive != null)\r\n            McmCatalog.ApplyToLive(EnsureMcmFolder(profile), keepLive);", start.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        var seed = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.Mcm.cs"));
        Assert.Contains("private IReadOnlyList<string>? SeedSettingsLockDefaults(", seed);
        Assert.Contains("return null;", seed);
    }

    [Fact]
    public void The_note_texts_have_no_colons_or_joined_words()
    {
        foreach (var text in new[] { RetiredSettingsLock.HostNote, RetiredSettingsLock.PlayerNote })
        {
            Assert.DoesNotContain(":", text);
            Assert.DoesNotMatch(@"\w-\w", text);
            Assert.StartsWith("COOP Server Settings Lock is no longer part of the setup.", text);
        }
    }

    [Fact]
    public void Settings_lock_values_become_defaults_only_where_the_host_has_none()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-lock-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            var xorberax = Path.Combine(root, "Global", "XorberaxLegacy", "XorberaxLegacy.json");
            Directory.CreateDirectory(Path.GetDirectoryName(xorberax)!);
            // The host's own values stay; only the values the file lacks are added.
            File.WriteAllText(xorberax, "{ \"MaxSkillValue\": 300, \"EnableBanksofCalradiaMod\": true }");
            var diplomacy = Path.Combine(root, "Global", "Diplomacy", "DiplomacySettings_v1.2.json");
            Directory.CreateDirectory(Path.GetDirectoryName(diplomacy)!);
            File.WriteAllText(diplomacy, "not json");

            var seeded = SettingsLockDefaults.Seed(root, ["XorberaxLegacy", "SettlementCultureDrift", "Bannerlord.Diplomacy"]).Seeded;

            Assert.Equal(new[] { "XorberaxLegacy", "SettlementCultureDrift" }, seeded);
            var xor = JsonNode.Parse(File.ReadAllText(xorberax))!.AsObject();
            Assert.Equal(300, xor["MaxSkillValue"]!.GetValue<int>());
            Assert.True(xor["EnableBanksofCalradiaMod"]!.GetValue<bool>());
            Assert.False(xor["EnableHonestWorkMod"]!.GetValue<bool>());
            Assert.False(xor["EnableTrainingFieldMod"]!.GetValue<bool>());
            Assert.False(xor["EnableScholarsofCalradiaMod"]!.GetValue<bool>());
            Assert.Equal(4.0, xor["ChargeDamageMultiplier"]!.GetValue<double>());
            // Every player keeps their own shoulder camera.
            Assert.False(xor.ContainsKey("EnableShoulderCameraMod"));
            Assert.False(xor.ContainsKey("ThirdPersonFieldOfView"));

            // A mod with no file yet gets the whole set; a file that can not be read is left alone.
            var drift = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "Global", "SettlementCultureDrift", "SettlementCultureDrift_v1.json")))!.AsObject();
            Assert.Equal(84, drift["FullSwitchDays"]!.GetValue<int>());
            Assert.Equal(25, drift["ResistanceDays"]!.GetValue<int>());
            Assert.Equal("not json", File.ReadAllText(diplomacy));
            // A mod this setup does not turn on gets nothing.
            Assert.False(Directory.Exists(Path.Combine(root, "Global", "AgricultureEstate")));

            // A second run changes nothing.
            Assert.Empty(SettingsLockDefaults.Seed(root, ["XorberaxLegacy", "SettlementCultureDrift"]).Seeded);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Seeding_keeps_names_in_any_case_skips_a_broken_file_and_leaves_no_temp_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-lock-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            // MCM reads names without minding case, so "maxskillvalue" is the host's own MaxSkillValue.
            var xorberax = Path.Combine(root, "Global", "XorberaxLegacy", "XorberaxLegacy.json");
            Directory.CreateDirectory(Path.GetDirectoryName(xorberax)!);
            File.WriteAllText(xorberax, "{ \"maxskillvalue\": 300 }");
            // A name written twice makes JsonObject throw ArgumentException; the start must go on and the file stays.
            var drift = Path.Combine(root, "Global", "SettlementCultureDrift", "SettlementCultureDrift_v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(drift)!);
            const string twice = "{ \"FullSwitchDays\": 10, \"FullSwitchDays\": 20 }";
            File.WriteAllText(drift, twice);

            var result = SettingsLockDefaults.Seed(root, ["XorberaxLegacy", "SettlementCultureDrift"]);
            var seeded = result.Seeded;

            Assert.Equal(new[] { "XorberaxLegacy" }, seeded);
            // The broken file is reported, so Start keeps the server's good live copy instead of copying it over.
            var driftPath = Path.Combine("Global", "SettlementCultureDrift", "SettlementCultureDrift_v1.json");
            Assert.Equal(new[] { driftPath }, result.Unreadable);
            Assert.Equal(new[] { driftPath }, result.KeepLive);
            var xor = JsonNode.Parse(File.ReadAllText(xorberax))!.AsObject();
            Assert.Equal(300, xor["maxskillvalue"]!.GetValue<int>());
            Assert.DoesNotContain(xor, pair => pair.Key == "MaxSkillValue");
            Assert.Equal(twice, File.ReadAllText(drift));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_value_only_the_live_file_has_wins_over_the_default()
    {
        // The setup's copy lacks MaxSkillValue, but the server used 300 last time; Start copies the setup's
        // files over the live ones, so the default must not take that value's place.
        var root = Path.Combine(Path.GetTempPath(), "coodsz-lock-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = Path.Combine(root, "snapshot");
            var live = Path.Combine(root, "live");
            foreach (var (folder, json) in new[] { (snapshot, "{ \"MaxSkillValue\": 250 }"), (live, "{ \"maxskillvalue\": 999, \"MaximumHoursToTrain\": 30 }") })
            {
                var file = Path.Combine(folder, "Global", "XorberaxLegacy", "XorberaxLegacy.json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, json);
            }

            Assert.Equal(new[] { "XorberaxLegacy" }, SettingsLockDefaults.Seed(snapshot, ["XorberaxLegacy"], live).Seeded);

            var xor = JsonNode.Parse(File.ReadAllText(Path.Combine(snapshot, "Global", "XorberaxLegacy", "XorberaxLegacy.json")))!.AsObject();
            Assert.Equal(250, xor["MaxSkillValue"]!.GetValue<int>());
            Assert.Equal(30, xor["MaximumHoursToTrain"]!.GetValue<int>());
            Assert.False(xor.ContainsKey("maxskillvalue"));
            Assert.Equal(72, xor["TrainingCooldownHours"]!.GetValue<int>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_file_that_can_not_be_written_is_reported_so_start_keeps_the_live_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-lock-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = Path.Combine(root, "snapshot");
            var file = Path.Combine(snapshot, "Global", "XorberaxLegacy", "XorberaxLegacy.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "{ \"MaxSkillValue\": 250 }");
            // A folder where the temp file goes makes the write fail, like a locked or read only file.
            Directory.CreateDirectory(file + ".tmp");

            var result = SettingsLockDefaults.Seed(snapshot, ["XorberaxLegacy"]);

            Assert.Empty(result.Seeded);
            Assert.Equal(new[] { Path.Combine("Global", "XorberaxLegacy", "XorberaxLegacy.json") }, result.Unwritten);
            Assert.Equal("{ \"MaxSkillValue\": 250 }", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Settings_lock_is_marked_retired_and_not_needed_in_the_compat_list()
    {
        var line = File.ReadLines(Path.Combine(RepoRoot(), "compat-list", "mods.json")).Single(text => text.Contains("\"id\": \"ServerSettingsLock\""));
        Assert.Contains("\"status\": \"Unknown\"", line);
        Assert.Contains("Retired and not needed.", line);
        Assert.DoesNotContain("need the same version", line);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
