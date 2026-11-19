using BannerlordCoopLauncher.Core.Configuration;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Turkish players on Discord could not find 2x speed. The Game speed area on the Server page
// shows it, and its buttons save through the Settings page's own Save settings.
public class GameSpeedTests
{
    [Fact]
    public void Game_speed_reads_the_keys_the_server_reads()
    {
        Assert.Equal("modOptions.fastForwardEnabled", GameSpeed.FastForwardId);
        Assert.Equal("modOptions.speedLimitWhilePlayersInBattle", GameSpeed.BattleLimitId);
        Assert.NotNull(ModSettingsCatalog.Find(GameSpeed.FastForwardId));
        Assert.NotNull(ModSettingsCatalog.Find(GameSpeed.BattleLimitId));
    }

    [Fact]
    public void Missing_values_are_coops_defaults()
    {
        var state = GameSpeed.From(new Dictionary<string, object>());
        Assert.True(state.FastForward);
        Assert.True(state.BattleSpeedLimit);
        Assert.True(state.TwoTimesOn);
        Assert.Equal(GameSpeed.From(ModSettingsCatalog.Defaults()), state);
    }

    [Fact]
    public void Two_times_speed_needs_fast_forward_and_client_time_controls()
    {
        var values = ModSettingsCatalog.Defaults();
        values[GameSpeed.FastForwardId] = false;
        var off = GameSpeed.From(values);
        Assert.False(off.TwoTimesOn);
        Assert.Equal("2x speed is off. Fast forward is turned off.", GameSpeed.SpeedLine(off));
        Assert.Equal("Turn on 2x speed", GameSpeed.SpeedButton(off));

        var noClientTime = GameSpeed.From(ModSettingsCatalog.Defaults(), clientTimeControls: false);
        Assert.False(noClientTime.TwoTimesOn);
        Assert.Contains("Allow clients to control campaign time is turned off", GameSpeed.SpeedLine(noClientTime));

        var on = GameSpeed.From(ModSettingsCatalog.Defaults(), clientTimeControls: true);
        Assert.True(on.TwoTimesOn);
        Assert.StartsWith("2x speed is on.", GameSpeed.SpeedLine(on));
        Assert.Equal("Turn off 2x speed", GameSpeed.SpeedButton(on));
    }

    [Fact]
    public void Turning_on_two_times_speed_also_lets_players_control_time()
    {
        var start = new GameSpeedState(FastForward: false, BattleSpeedLimit: true, ClientTimeControls: false);
        var on = GameSpeed.WithTwoTimes(start, on: true);
        Assert.True(on.FastForward);
        Assert.True(on.ClientTimeControls);
        Assert.True(on.TwoTimesOn);
        Assert.True(on.BattleSpeedLimit);

        // Turning it off leaves client time controls alone, so Pause still works for players.
        var off = GameSpeed.WithTwoTimes(on, on: false);
        Assert.False(off.FastForward);
        Assert.True(off.ClientTimeControls);
        Assert.False(off.TwoTimesOn);
    }

    [Fact]
    public void Battle_limit_button_flips_only_the_battle_limit()
    {
        var state = new GameSpeedState(FastForward: true, BattleSpeedLimit: true);
        Assert.Equal("Turn off battle speed limit", GameSpeed.BattleButton(state));
        Assert.StartsWith("Battle speed limit is on.", GameSpeed.BattleLine(state));
        var off = GameSpeed.WithBattleLimit(state, on: false);
        Assert.Equal(state with { BattleSpeedLimit = false }, off);
        Assert.Equal("Turn on battle speed limit", GameSpeed.BattleButton(off));
        Assert.StartsWith("Battle speed limit is off.", GameSpeed.BattleLine(off));
    }

    [Fact]
    public void A_running_server_needs_a_restart_only_when_the_saved_speed_changed()
    {
        var started = new GameSpeedState(FastForward: false, BattleSpeedLimit: true);
        var saved = GameSpeed.WithTwoTimes(started, on: true);

        Assert.False(GameSpeed.NeedsRestart(running: false, started, saved));
        Assert.False(GameSpeed.NeedsRestart(running: true, atStart: null, saved));
        Assert.False(GameSpeed.NeedsRestart(running: true, started, started));
        Assert.True(GameSpeed.NeedsRestart(running: true, started, saved));

        Assert.Contains("Restart the server so the new speed applies.", GameSpeed.RestartLine(true, started, saved));
        Assert.Contains("only uses the new speed after a restart", GameSpeed.RestartLine(true, started, started));
        Assert.Contains("applies the next time the server starts", GameSpeed.RestartLine(false, started, saved));
    }

    [Fact]
    public void Saved_values_read_back_as_the_same_game_speed()
    {
        var values = ModSettingsCatalog.Defaults();
        values[GameSpeed.FastForwardId] = true;
        values[GameSpeed.BattleLimitId] = false;
        var json = ModConfigStore.WriteText("{\n  \"modOptions\": {\n    \"fastForwardEnabled\": false\n  }\n}\n", values, out _);
        Assert.Contains("\"fastForwardEnabled\": true", json);
        Assert.Contains("\"speedLimitWhilePlayersInBattle\": false", json);
        Assert.True(CoopModConfig.ReadBool(json, "fastForwardEnabled"));
        Assert.False(CoopModConfig.ReadBool(json, "speedLimitWhilePlayersInBattle"));
    }

    [Fact]
    public void Coodsz_host_game_speed_uses_the_settings_page_save_and_skips_ad1259()
    {
        var code = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var save = SourceFiles.Method(code, "private void SaveGameSpeed(");
        Assert.Contains("SaveSettingsPage()", save);
        Assert.Contains("FastForwardBox.SelectedItem", save);
        Assert.Contains("SpeedLimitBox.SelectedItem", save);
        Assert.Contains("ClientTimeBox.IsChecked", save);
        Assert.DoesNotContain("JsoncEditor.Write", save);
        Assert.Contains("private static bool ShowsGameSpeed => !Edition.IsAD1259;", code);
        Assert.Contains("if (!ShowsGameSpeed)", SourceFiles.Method(code, "private void ShowGameSpeedGroup("));
        Assert.Contains("RefreshGameSpeed();", SourceFiles.Method(code, "private bool SaveSettingsPage("));
        Assert.Contains("_speedAtStart = SavedGameSpeed();", SourceFiles.Method(code, "private void StartServer("));

        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        // Hidden until Coodsz Host shows them, so the AD1259 build looks as it did.
        Assert.Contains("<Border x:Name=\"GameSpeedArea\" Visibility=\"Collapsed\"", xaml);
        Assert.Contains("<StackPanel x:Name=\"GameSpeedGroup\" Visibility=\"Collapsed\">", xaml);
        Assert.Contains("x:Name=\"GameSpeedRestartButton\" Style=\"{StaticResource Ghost}\" Content=\"Restart server\" Click=\"Restart_Click\"", xaml);
        Assert.Contains("keys 1, 2 and 3", xaml);
    }

    [Fact]
    public void Taom_host_game_speed_uses_the_settings_page_save_and_sits_first_on_settings()
    {
        var code = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        var save = SourceFiles.Method(code, "private void SaveGameSpeed(");
        Assert.Contains("SaveSettingsPage(fromSettingsPage: false)", save);
        Assert.DoesNotContain("File.WriteAllText", save);
        Assert.Contains("SaveTimePermissions();", SourceFiles.Method(code, "private bool SaveSettingsPage("));
        Assert.Contains("_speedAtStart = SavedGameSpeed();", code);

        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopHost", "MainWindow.xaml"));
        var settings = xaml[xaml.IndexOf("x:Name=\"SettingsPage\"", StringComparison.Ordinal)..];
        Assert.True(settings.IndexOf("AutomationId=\"SettingTime\"", StringComparison.Ordinal) < settings.IndexOf("AutomationId=\"SettingHost\"", StringComparison.Ordinal));
        Assert.Contains("AutomationProperties.AutomationId=\"GameSpeedArea\"", xaml);
        Assert.Contains("keys 1, 2 and 3", xaml);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
