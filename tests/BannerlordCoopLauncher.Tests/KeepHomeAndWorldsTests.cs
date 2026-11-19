using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// CoopKeep part 7, phase 1b: the Home page words, world presets, the new world wizard, streamer
// mode and the Modules filter chips (the Core logic behind them).
public sealed class StreamerMaskTests
{
    [Fact]
    public void Off_leaves_text_alone()
    {
        Assert.Equal("Join 192.168.1.5:4200", StreamerMask.Text("Join 192.168.1.5:4200", false));
        Assert.Equal("192.168.1.5", StreamerMask.Value("192.168.1.5", false));
        Assert.Equal("Lucon  76561198000000009", StreamerMask.PlayerLine("Lucon", "76561198000000009", false));
    }

    [Fact]
    public void On_hides_addresses_steam_ids_and_secrets()
    {
        var text = StreamerMask.Text("Direct 81.2.3.4:4200 or [fe80::1]:4200, Steam 76561198000000009, password hunter2, name Coodsz", true, "hunter2", "Coodsz");
        Assert.DoesNotContain("81.2.3.4", text);
        Assert.DoesNotContain("fe80", text);
        Assert.DoesNotContain("76561198000000009", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("Coodsz", text);
        Assert.StartsWith("Direct " + StreamerMask.Hidden, text);
    }

    [Fact]
    public void Version_numbers_and_short_secrets_stay()
    {
        Assert.Equal("Coop v1.2.53 and 1.4.8.119303", StreamerMask.Text("Coop v1.2.53 and 1.4.8.119303", true));
        // A one or two letter name would blank half the line.
        Assert.Equal("Bo is here", StreamerMask.Text("Bo is here", true, "Bo"));
    }

    [Fact]
    public void Values_and_labels()
    {
        Assert.Equal(StreamerMask.Hidden, StreamerMask.Value("192.168.1.5:4200", true));
        Assert.Equal("", StreamerMask.Value("", true));
        Assert.Equal("Steam ••••0009", StreamerMask.SteamLabel("76561198000000009", false));
        Assert.Equal("Steam " + StreamerMask.Hidden, StreamerMask.SteamLabel("76561198000000009", true));
        Assert.Equal("", StreamerMask.SteamLabel("", true));
        Assert.Equal("Lucon", StreamerMask.PlayerLine("Lucon", "76561198000000009", true));
        Assert.Equal("Kaan", StreamerMask.PlayerLine("Kaan", "", false));
        Assert.True(StreamerMask.HasPrivate("at 10.0.0.2"));
        Assert.False(StreamerMask.HasPrivate("nothing here"));
    }

    [Fact]
    public void Streamer_mode_is_off_until_turned_on_and_is_kept()
    {
        Assert.False(new HostSettings().StreamerMode);
        var path = Path.Combine(Path.GetTempPath(), "coodsz-streamer-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = HostSettings.Load(path);
            settings.StreamerMode = true;
            HostSettings.Save(path, settings);
            Assert.True(HostSettings.Load(path).StreamerMode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class ModFilterTests
{
    [Theory]
    [InlineData(ModFilterKind.All, true, "Broken", true)]
    [InlineData(ModFilterKind.Enabled, false, "Works", false)]
    [InlineData(ModFilterKind.Disabled, false, "Works", true)]
    [InlineData(ModFilterKind.Works, false, "Works", true)]
    [InlineData(ModFilterKind.Broken, true, "Works", false)]
    [InlineData(ModFilterKind.ClientOnly, true, "ClientOnly", true)]
    [InlineData(ModFilterKind.Unknown, true, null, true)]
    [InlineData(ModFilterKind.Unknown, true, "Unknown", true)]
    [InlineData(ModFilterKind.Unknown, true, "Works", false)]
    public void A_chip_matches_the_row(ModFilterKind kind, bool enabled, string? badge, bool expected) =>
        Assert.Equal(expected, ModFilter.Matches(kind, enabled, badge));

    [Fact]
    public void Counts_cover_every_chip()
    {
        var counts = ModFilter.Counts(new (bool, string?)[] { (true, "Works"), (false, "Broken"), (true, null), (true, "ClientOnly") });
        Assert.Equal(ModFilter.Chips.Count, counts.Count);
        Assert.Equal(4, counts[ModFilterKind.All]);
        Assert.Equal(3, counts[ModFilterKind.Enabled]);
        Assert.Equal(1, counts[ModFilterKind.Disabled]);
        Assert.Equal(1, counts[ModFilterKind.Works]);
        Assert.Equal(1, counts[ModFilterKind.Broken]);
        Assert.Equal(1, counts[ModFilterKind.ClientOnly]);
        Assert.Equal(1, counts[ModFilterKind.Unknown]);
        Assert.All(ModFilter.Chips, chip => Assert.False(string.IsNullOrWhiteSpace(ModFilter.Title(chip))));
    }
}

public sealed class WorldPresetsTests
{
    [Fact]
    public void Presets_only_set_settings_the_server_reads()
    {
        Assert.Equal(new[] { WorldPresets.Casual, WorldPresets.Standard, WorldPresets.Hardcore }, WorldPresets.All.Select(p => p.Id));
        foreach (var preset in WorldPresets.All)
            foreach (var (id, value) in preset.Values)
            {
                var setting = ModSettingsCatalog.Find(id);
                Assert.True(setting != null, preset.Id + " sets " + id + ", which mod-config.json does not have.");
                Assert.Equal(value, ModSettingsCatalog.Normalize(setting!, value));
            }
    }

    [Fact]
    public void Each_preset_is_found_again_after_it_is_used()
    {
        foreach (var preset in WorldPresets.All)
        {
            var values = ModSettingsCatalog.Defaults();
            WorldPresets.Apply(values, preset);
            Assert.Equal(preset.Id, WorldPresets.Detect(values)?.Id);
        }
    }

    [Fact]
    public void Coop_defaults_are_Casual_and_a_mixed_world_is_custom()
    {
        Assert.Equal(WorldPresets.Casual, WorldPresets.Detect(ModSettingsCatalog.Defaults())?.Id);
        var values = ModSettingsCatalog.Defaults();
        WorldPresets.Apply(values, WorldPresets.Find(WorldPresets.Hardcore)!);
        values["difficulty.combatAIDifficulty"] = "Easy";
        Assert.Null(WorldPresets.Detect(values));
    }

    [Fact]
    public void A_preset_leaves_other_settings_alone()
    {
        var values = ModSettingsCatalog.Defaults();
        values["modOptions.battleSize"] = 600;
        values["modOptions.clientsCanUseCheats"] = true;
        WorldPresets.Apply(values, WorldPresets.Find(WorldPresets.Hardcore)!);
        Assert.Equal(600, values["modOptions.battleSize"]);
        Assert.Equal(true, values["modOptions.clientsCanUseCheats"]);
        Assert.Equal("Realistic", values["difficulty.battleDeath"]);
    }

    [Fact]
    public void Chips_name_difficulty_battle_size_death_and_cheats()
    {
        var values = ModSettingsCatalog.Defaults();
        Assert.Equal(new[] { "Very easy", "battle size 1000", "battle death off", "player cheats off" }, WorldPresets.Chips(values));
        WorldPresets.Apply(values, WorldPresets.Find(WorldPresets.Hardcore)!);
        values["modOptions.battleSize"] = 400;
        values["modOptions.clientsCanUseCheats"] = true;
        Assert.Equal(new[] { "Realistic", "battle size 400", "battle death on", "player cheats on" }, WorldPresets.Chips(values));
        values["difficulty.combatAIDifficulty"] = "Easy";
        Assert.Equal("Mixed difficulty", WorldPresets.Chips(values)[0]);
    }
}

public sealed class HomeDashboardTests
{
    private static HomeFacts Facts(bool running = false, bool serving = false, bool exited = false, bool stopping = false, string save = "Calradia") =>
        new(running, serving, exited, stopping, save, null, 0, 5, null, null);

    [Fact]
    public void State_follows_the_server()
    {
        Assert.Equal(HomeState.NoWorld, HomeDashboard.State(Facts(save: "")));
        Assert.Equal(HomeState.Ready, HomeDashboard.State(Facts()));
        Assert.Equal(HomeState.Starting, HomeDashboard.State(Facts(running: true)));
        Assert.Equal(HomeState.Online, HomeDashboard.State(Facts(running: true, serving: true)));
        Assert.Equal(HomeState.Stopping, HomeDashboard.State(Facts(running: true, serving: true, stopping: true)));
        Assert.Equal(HomeState.Exited, HomeDashboard.State(Facts(running: true, exited: true)));
        // An exit after the process ended is just stopped.
        Assert.Equal(HomeState.Ready, HomeDashboard.State(Facts(exited: true)));
    }

    [Fact]
    public void Crashed_lasts_until_a_start_and_a_restart_wins_over_everything()
    {
        Assert.Equal(HomeState.Crashed, HomeDashboard.State(Facts() with { Crashed = true }));
        Assert.Equal(HomeState.Crashed, HomeDashboard.State(Facts(save: "") with { Crashed = true }));
        // Once it runs again the crash is history.
        Assert.Equal(HomeState.Starting, HomeDashboard.State(Facts(running: true) with { Crashed = true }));
        Assert.Equal(HomeState.Restarting, HomeDashboard.State(Facts() with { Crashed = true, Restarting = true }));
        Assert.Equal(HomeState.Restarting, HomeDashboard.State(Facts(running: true, serving: true, stopping: true) with { Restarting = true }));
        Assert.Equal("Crashed", HomeDashboard.StatusWord(Facts() with { Crashed = true }));
    }

    [Fact]
    public void The_big_button_has_one_label_per_state_and_the_pending_ones_cannot_be_pressed()
    {
        Assert.Equal(new PrimaryAction("Start server", false, true), HomeDashboard.Primary(HomeState.Ready));
        Assert.Equal(new PrimaryAction("Start server", false, true), HomeDashboard.Primary(HomeState.NoWorld));
        Assert.Equal(new PrimaryAction("Starting...", false, false), HomeDashboard.Primary(HomeState.Starting));
        Assert.Equal(new PrimaryAction("Stop server", true, true), HomeDashboard.Primary(HomeState.Online));
        Assert.Equal(new PrimaryAction("Stop server", true, true), HomeDashboard.Primary(HomeState.Exited));
        Assert.Equal(new PrimaryAction("Saving and stopping...", true, false), HomeDashboard.Primary(HomeState.Stopping));
        Assert.Equal(new PrimaryAction("Restarting...", false, false), HomeDashboard.Primary(HomeState.Restarting));
        // After a crash it is Start server again.
        Assert.Equal(new PrimaryAction("Start server", false, true), HomeDashboard.Primary(HomeState.Crashed));
    }

    [Fact]
    public void The_smaller_buttons_follow_the_state()
    {
        Assert.Equal(DashboardActions.StopStarting, HomeDashboard.Secondary(HomeState.Starting, false));
        Assert.True(HomeDashboard.Secondary(HomeState.Online, false).HasFlag(DashboardActions.SaveGame | DashboardActions.Restart));
        Assert.True(HomeDashboard.Secondary(HomeState.Crashed, false).HasFlag(DashboardActions.LastGoodSave));
        Assert.Equal(DashboardActions.StopAutoRestart, HomeDashboard.Secondary(HomeState.Restarting, true));
        // A restart by hand has nothing to cancel.
        Assert.Equal(DashboardActions.None, HomeDashboard.Secondary(HomeState.Restarting, false));
        Assert.Equal(DashboardActions.None, HomeDashboard.Secondary(HomeState.Stopping, false));
        // Save game only while it serves.
        foreach (var state in new[] { HomeState.Ready, HomeState.Starting, HomeState.Stopping, HomeState.Crashed, HomeState.Exited })
            Assert.False(HomeDashboard.Secondary(state, false).HasFlag(DashboardActions.SaveGame));
    }

    [Fact]
    public void Words_name_the_world()
    {
        Assert.Equal("Your server is ready", HomeDashboard.Headline(Facts()));
        Assert.Contains("Calradia", HomeDashboard.Detail(Facts()));
        Assert.Equal("Your server is online", HomeDashboard.Headline(Facts(running: true, serving: true)));
        Assert.Contains("Calradia", HomeDashboard.Detail(Facts(running: true, serving: true)));
        Assert.Equal("Create your first world", HomeDashboard.Headline(Facts(save: "")));
        Assert.Equal("Stopped", HomeDashboard.StatusWord(Facts()));
        Assert.Equal("Online", HomeDashboard.StatusWord(Facts(running: true, serving: true)));
    }

    [Fact]
    public void Numbers_read_plainly()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0);
        Assert.Equal("-", HomeDashboard.Ago(null, now));
        Assert.Equal("just now", HomeDashboard.Ago(now.AddSeconds(-20), now));
        Assert.Equal("12 min ago", HomeDashboard.Ago(now.AddMinutes(-12), now));
        Assert.Equal("3 hr ago", HomeDashboard.Ago(now.AddHours(-3), now));
        Assert.Equal("14 Oct 2026", HomeDashboard.Ago(new DateTime(2026, 10, 14, 9, 0, 0), now));
        Assert.Equal("Off", HomeDashboard.Autosave(0));
        Assert.Equal("Saves every 5 min", HomeDashboard.Autosave(5));
        Assert.Equal("-", HomeDashboard.Uptime(null));
        Assert.Equal("1:23:00", HomeDashboard.Uptime(TimeSpan.FromMinutes(83)));
        Assert.Equal("0:00:00", HomeDashboard.Uptime(TimeSpan.FromSeconds(-5)));
    }
}

public sealed class NewWorldPlanTests
{
    [Theory]
    [InlineData("", "Type a name for the world.")]
    [InlineData("   ", "Type a name for the world.")]
    [InlineData("default_new_game", "Pick a campaign name. default_new_game is the template.")]
    [InlineData("a/b", "That save name cannot be used as a file name.")]
    [InlineData("what?", "That save name cannot be used as a file name.")]
    [InlineData("..", "That save name cannot be used as a file name.")]
    [InlineData("calradia", "A save named calradia already exists. Pick another name.")]
    [InlineData("Vlandia", "")]
    [InlineData("My World_2-b", "")]
    public void Names_are_checked(string name, string problem) =>
        Assert.Equal(problem, NewWorldPlan.NameProblem(name, new[] { "Calradia" }));

    // A NEW campaign name follows the server start script rule (-NewSaveName): any letter or digit, no symbols.
    [Theory]
    [InlineData("YÜZÜK TEST", "")]
    [InlineData("Yüzüklerİn", "")]
    [InlineData("_lead", BannerlordCoopLauncher.Core.Saves.SaveSet.NewSaveNameRule)]
    [InlineData("a+b", BannerlordCoopLauncher.Core.Saves.SaveSet.NewSaveNameRule)]
    public void A_new_world_name_follows_the_start_script_rule(string name, string problem) =>
        Assert.Equal(problem, NewWorldPlan.NameProblem(name, new[] { "Calradia" }));

    [Fact]
    public void Steps_go_forward_and_back()
    {
        Assert.Equal(5, NewWorldPlan.Steps.Count);
        Assert.Equal(NewWorldStep.Name, NewWorldPlan.Next(NewWorldStep.Start));
        Assert.Null(NewWorldPlan.Next(NewWorldStep.Server));
        Assert.Null(NewWorldPlan.Back(NewWorldStep.Start));
        Assert.Equal(NewWorldStep.Rules, NewWorldPlan.Back(NewWorldStep.Server));
        Assert.All(NewWorldPlan.Steps, step => Assert.False(string.IsNullOrWhiteSpace(NewWorldPlan.StepTitle(step))));
    }

    [Fact]
    public void The_plan_starts_from_the_current_settings()
    {
        var current = ModSettingsCatalog.Defaults();
        current["modOptions.battleSize"] = 700;
        var plan = new NewWorldPlan(current);
        Assert.Equal(700, plan.BattleSize);
        Assert.Equal(WorldPresets.Casual, plan.PresetId);

        // A world that matches no preset starts on Standard.
        current["difficulty.combatAIDifficulty"] = "Realistic";
        Assert.Equal(WorldPresets.Standard, new NewWorldPlan(current).PresetId);
        Assert.Equal("Easy", new NewWorldPlan(current).Values["difficulty.combatAIDifficulty"]);
    }

    [Fact]
    public void Presets_rules_and_battle_size_change_the_values()
    {
        var plan = new NewWorldPlan();
        plan.UsePreset(WorldPresets.Hardcore);
        Assert.Equal("Realistic", plan.Values["difficulty.battleDeath"]);
        plan.SetRule("modOptions.clientsCanUseCheats", true);
        Assert.True(plan.Rule("modOptions.clientsCanUseCheats"));
        Assert.Throws<ArgumentException>(() => plan.SetRule("modOptions.battleSize", true));
        Assert.Throws<ArgumentException>(() => plan.UsePreset("nightmare"));
        plan.BattleSize = 5000;
        Assert.Equal(ModSettingsCatalog.BattleSizeMax, plan.BattleSize);
        plan.BattleSize = 10;
        Assert.Equal(ModSettingsCatalog.BattleSizeMin, plan.BattleSize);
        Assert.All(NewWorldPlan.RuleIds, id => Assert.Equal(ModSettingKind.Bool, ModSettingsCatalog.Find(id)!.Kind));
    }

    [Fact]
    public void Only_the_name_step_can_stop_the_wizard()
    {
        var plan = new NewWorldPlan();
        Assert.Equal("Type a name for the world.", plan.Problem(NewWorldStep.Name, Array.Empty<string>()));
        plan.Name = "Vlandia";
        Assert.All(NewWorldPlan.Steps, step => Assert.Equal("", plan.Problem(step, Array.Empty<string>())));
    }
}
