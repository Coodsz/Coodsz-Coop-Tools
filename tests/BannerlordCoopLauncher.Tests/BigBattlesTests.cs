using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Big battles with Limitless: the battle size range follows the server's mod list.
public class BigBattlesTests
{
    private static readonly string[] Coop = { "Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop" };

    [Theory]
    [InlineData(false, false, BigBattleMode.Normal, 1000)]
    [InlineData(true, true, BigBattleMode.Unlocked, 10000)]
    [InlineData(true, false, BigBattleMode.NeedsPatch, 1000)]
    [InlineData(false, true, BigBattleMode.Normal, 1000)]
    public void The_server_mod_list_picks_the_battle_size_range(bool limitless, bool patch, BigBattleMode expected, int max)
    {
        var ids = Coop.ToList();
        if (limitless)
            ids.Add("Limitless");
        if (patch)
            ids.Add("Limitless.CoopCompat");

        var mode = BigBattles.Detect(ids);
        Assert.Equal(expected, mode);
        Assert.Equal(max, BigBattles.MaxFor(mode));
        Assert.Equal(200, BigBattles.Clamp(50, mode));
        Assert.Equal(max, BigBattles.Clamp(50_000, mode));
    }

    [Fact]
    public void Module_ids_match_without_regard_to_case()
    {
        Assert.Equal(BigBattleMode.Unlocked, BigBattles.Detect(Coop.Append("limitless").Append("LIMITLESS.COOPCOMPAT")));
    }

    [Fact]
    public void A_big_saved_size_comes_back_to_1000_when_limitless_leaves()
    {
        Assert.Equal(1000, BigBattles.ClampBack(5000, BigBattleMode.Normal, out var clamped));
        Assert.True(clamped);
        Assert.Equal(1000, BigBattles.ClampBack(5000, BigBattleMode.NeedsPatch, out clamped));
        Assert.True(clamped);
        Assert.Equal(5000, BigBattles.ClampBack(5000, BigBattleMode.Unlocked, out clamped));
        Assert.False(clamped);
        Assert.Equal(800, BigBattles.ClampBack(800, BigBattleMode.Normal, out clamped));
        Assert.False(clamped);
        // Raising a too small size is not a clamp back worth a note.
        Assert.Equal(200, BigBattles.ClampBack(100, BigBattleMode.Normal, out clamped));
        Assert.False(clamped);

        var note = BigBattles.ClampedNote(5000);
        Assert.Contains("5000", note);
        Assert.Contains("back to 1000", note);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1000, 5)]
    [InlineData(2000, 5)]
    [InlineData(3500, 6)]
    [InlineData(4000, 6)]
    [InlineData(7300, 8)]
    [InlineData(10000, 9)]
    public void Ram_estimate_per_player(int soldiers, int gb)
    {
        // 4.5 GB plus 0.4 GB per 1000 agents, agents 1.15 times soldiers.
        Assert.Equal(gb, BigBattles.EstimatedRamGb(soldiers));
    }

    [Fact]
    public void Ram_estimate_tracks_the_live_test()
    {
        // About 4000 agents used 5.9 GB and about 8400 agents 7.2 GB.
        Assert.InRange(BigBattles.EstimatedRamGb((int)(4000 / 1.15)), 5, 7);
        Assert.InRange(BigBattles.EstimatedRamGb((int)(8400 / 1.15)), 7, 9);
        Assert.Equal("About 9 GB of RAM for the game on each player's PC.", BigBattles.RamText(10000));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(8, 1000)]
    [InlineData(16, 1800)]
    [InlineData(32, 5000)]
    [InlineData(64, 5000)]
    public void Suggested_size_for_this_pc(double ramGb, int size)
    {
        Assert.Equal(size, BigBattles.SuggestedSize(ramGb));
        Assert.Equal(0, BigBattles.SuggestedSize(ramGb) % 100);
    }

    [Theory]
    [InlineData(200, BigBattleTier.Smooth, "Smooth")]
    [InlineData(2000, BigBattleTier.Smooth, "Smooth")]
    [InlineData(2001, BigBattleTier.Big, "Big battle, strong PC recommended")]
    [InlineData(5000, BigBattleTier.Big, "Big battle, strong PC recommended")]
    [InlineData(5001, BigBattleTier.Epic, "Epic, expect low FPS")]
    [InlineData(10000, BigBattleTier.Epic, "Epic, expect low FPS")]
    public void Slider_label_follows_the_size(int size, BigBattleTier tier, string text)
    {
        Assert.Equal(tier, BigBattles.Tier(size));
        Assert.Equal(text, BigBattles.TierText(size));
    }

    [Fact]
    public void Joining_players_with_little_ram_get_a_warning_only_for_a_limitless_server()
    {
        Assert.Null(BigBattles.ClientWarning(Coop, 8));
        Assert.Equal(
            "This server uses big battles. Your PC has 8 GB of RAM, so big fights may lag or run out of memory.",
            BigBattles.ClientWarning(Coop.Append("Limitless").Append("Limitless.CoopCompat"), 8));
        Assert.NotNull(BigBattles.ClientWarning(Coop.Append("Limitless"), 12));
        // Windows reports a 16 GB PC as a little under 16.
        Assert.Null(BigBattles.ClientWarning(Coop.Append("Limitless"), 15.8));
        Assert.Null(BigBattles.ClientWarning(Coop.Append("Limitless"), 32));
        // RAM that cannot be read gives no warning.
        Assert.Null(BigBattles.ClientWarning(Coop.Append("Limitless"), 0));
    }

    [Fact]
    public void A_known_server_battle_size_uses_the_ram_estimate()
    {
        var limitless = Coop.Append("Limitless").Append("Limitless.CoopCompat").ToList();
        Assert.Null(BigBattles.ClientWarning(limitless, 8, battleSize: 1000));
        Assert.NotNull(BigBattles.ClientWarning(limitless, 8, battleSize: 10000));
        Assert.Null(BigBattles.ClientWarning(limitless, 12, battleSize: 10000));
    }

    [Fact]
    public void New_world_plan_clamps_both_ways()
    {
        var plan = new NewWorldPlan();
        plan.BattleSize = 5000;
        Assert.Equal(1000, plan.BattleSize);

        plan.BattleMode = BigBattleMode.Unlocked;
        plan.BattleSize = 5000;
        Assert.Equal(5000, plan.BattleSize);
        plan.BattleSize = 50_000;
        Assert.Equal(BigBattles.Max, plan.BattleSize);
        plan.BattleSize = 10;
        Assert.Equal(200, plan.BattleSize);

        // Leaving the wider range brings the size back to 1000.
        plan.BattleSize = 6000;
        plan.BattleMode = BigBattleMode.Normal;
        Assert.Equal(1000, plan.BattleSize);

        // A saved big battle survives when the server still has Limitless and the patch.
        var current = new Dictionary<string, object> { [NewWorldPlan.BattleSizeId] = 7000 };
        Assert.Equal(7000, new NewWorldPlan(current) { BattleMode = BigBattleMode.Unlocked }.BattleSize);
        Assert.Equal(1000, new NewWorldPlan(current) { BattleMode = BigBattleMode.NeedsPatch }.BattleSize);
    }

    [Fact]
    public void Mod_config_keeps_a_big_battle_size_and_clamps_it_for_the_mod_list()
    {
        const string json = "{\n  \"modOptions\": {\n    \"battleSize\": 1000\n  }\n}";
        var big = CoopModConfig.SetBattleSize(json, 6000, BigBattleMode.Unlocked);
        Assert.Contains("\"battleSize\": 6000", big);
        Assert.Equal(6000, CoopModConfig.ReadBattleSize(big, BigBattleMode.Unlocked));
        Assert.Equal(1000, CoopModConfig.ReadBattleSize(big));
        Assert.Equal(6000d, CoopModConfig.ReadSavedBattleSize(big));
        Assert.Null(CoopModConfig.ReadSavedBattleSize("{ \"modOptions\": { } }"));
        // Without the mode the old range stays.
        Assert.Contains("\"battleSize\": 1000", CoopModConfig.SetBattleSize(json, 6000));

        // The store keeps the saved value, so the screen can still show or clamp it.
        Assert.Equal(6000, ModConfigStore.ReadText(big)["modOptions.battleSize"]);
    }

    [WindowsFact]
    public void Start_script_allows_10000_only_with_limitless_and_its_patch()
    {
        var script = SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var function = System.Text.RegularExpressions.Regex.Match(script, @"(?ms)^function Get-BattleSizeMaximum \{.*?^\}").Value;
        Assert.NotEmpty(function);
        Assert.Contains("$maxBattleSize = Get-BattleSizeMaximum -ModuleIds $engineModuleIds", script);
        Assert.Contains("$selectedBattleSize -gt $maxBattleSize", script);
        Assert.Contains("[Math]::Min($selectedBattleSize, 2000)", script);

        var root = Path.Combine(Path.GetTempPath(), "battle-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "out.txt");
            var run = function + "\r\n" +
                      "$lines = @(" +
                      "(Get-BattleSizeMaximum -ModuleIds @('Native','Coop'))," +
                      "(Get-BattleSizeMaximum -ModuleIds @('Native','Coop','Limitless'))," +
                      "(Get-BattleSizeMaximum -ModuleIds @('Native','Coop','Limitless.CoopCompat'))," +
                      "(Get-BattleSizeMaximum -ModuleIds @('Native','Coop','Limitless','Limitless.CoopCompat'))," +
                      "(Get-BattleSizeMaximum -ModuleIds @('Native','Coop','limitless','LIMITLESS.COOPCOMPAT')))\r\n" +
                      "[System.IO.File]::WriteAllLines('" + output + "', [string[]]$lines)\r\n";
            File.WriteAllText(Path.Combine(root, "run.ps1"), run);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "run.ps1") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            Assert.True(process!.WaitForExit(60000), "The battle size check did not finish.");
            Assert.Equal(new[] { "2000", "2000", "2000", "10000", "10000" }, File.ReadAllLines(output));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Host_follows_the_server_mod_list_for_the_battle_size()
    {
        var window = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.BigBattles.cs"));
        Assert.Contains("BigBattles.Detect(_modules.Where(row => row.Enabled).Select(row => row.Id))", window);
        var control = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        Assert.Contains("ApplySaveProfile(profile.SaveName);\r\n        ApplyBattleSizeLimit();", control.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        var settings = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.CoopSettings.cs"));
        Assert.Contains("CoopModConfig.ClampBattleSize(BattleSizeSlider.Value, BattleMode)", settings);
    }
}

// The clamp note follows the tool's language. Translator.Current is process wide, so this runs in
// the collection that never runs next to other tests.
[Xunit.Collection(TranslatorLanguage.Name)]
public sealed class BigBattlesLanguageTests
{
    [Xunit.Fact]
    public void The_clamp_note_is_translated_with_the_number_filled_in()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            Xunit.Assert.Equal(
                "Die Schlachtgröße war 4000, aber größere Schlachten brauchen Limitless und Limitless Coop Compat auf dem Server. Sie steht wieder auf 1000.",
                BigBattles.ClampedNote(4000));

            BannerlordCoopLauncher.Core.Localization.Translator.Use(BannerlordCoopLauncher.Core.Localization.Translator.English);
            Xunit.Assert.Equal(
                "Battle size was 4000, but bigger battles need Limitless and Limitless Coop Compat on the server. It is back to 1000.",
                BigBattles.ClampedNote(4000));
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
