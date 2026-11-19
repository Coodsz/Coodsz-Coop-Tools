using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Tests;

// Automatic restart (autoRestart and autoRestartHours in server-config.json): the Coop server ignores
// both keys, so Coodsz Host restarts the server itself once it has run for the set hours.
public sealed class ScheduledRestartTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 12, 0, 0);

    [Theory]
    [InlineData("3", 3)]
    [InlineData(" 6 ", 6)]
    [InlineData("1.5", 1.5)]
    [InlineData("0.25", 1)]
    [InlineData("1000", 168)]
    public void Interval_reads_the_hours_and_clamps_them(string hours, double expected)
    {
        Assert.Equal(TimeSpan.FromHours(expected), ScheduledRestart.Interval(true, hours));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("three")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Interval_is_off_for_empty_zero_or_broken_hours(string? hours)
    {
        Assert.Null(ScheduledRestart.Interval(true, hours));
    }

    [Fact]
    public void Interval_is_off_when_automatic_restart_is_unticked()
    {
        Assert.Null(ScheduledRestart.Interval(false, "3"));
    }

    [Fact]
    public void IsDue_waits_for_the_whole_interval()
    {
        var every = TimeSpan.FromHours(3);
        Assert.False(ScheduledRestart.IsDue(Start, Start, Start.AddHours(2).AddMinutes(59), every));
        Assert.True(ScheduledRestart.IsDue(Start, Start, Start.AddHours(3), every));
        Assert.True(ScheduledRestart.IsDue(Start, Start, Start.AddHours(5), every));
    }

    [Fact]
    public void IsDue_counts_from_when_the_tool_opened_on_an_older_server()
    {
        var every = TimeSpan.FromHours(3);
        var opened = Start.AddDays(2);
        Assert.False(ScheduledRestart.IsDue(Start, opened, opened.AddMinutes(1), every));
        Assert.True(ScheduledRestart.IsDue(Start, opened, opened.AddHours(3), every));
    }

    [Fact]
    public void IsDue_is_false_without_a_start_time_or_an_interval()
    {
        Assert.False(ScheduledRestart.IsDue(null, Start, Start.AddDays(1), TimeSpan.FromHours(1)));
        Assert.False(ScheduledRestart.IsDue(Start, Start, Start.AddDays(1), null));
    }

    [Theory]
    [InlineData("World1", "World1", true)]
    [InlineData("World1", "world1 ", true)]
    [InlineData("World1", "World2", false)]
    [InlineData("World1", null, false)]
    [InlineData(null, "World1", false)]
    [InlineData("", "", false)]
    public void KeepsRunningSave_only_when_the_picked_save_is_the_running_one(string? running, string? picked, bool expected)
    {
        Assert.Equal(expected, ScheduledRestart.KeepsRunningSave(running, picked));
    }

    [Fact]
    public void An_older_config_stays_off_until_Settings_are_saved_once()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-autorestart-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.False(new CoodszHost.Services.HostSettings().AutoRestartSaved);
            File.WriteAllText(path, "{\"ActiveProfile\": \"Default\"}");
            Assert.False(CoodszHost.Services.HostSettings.Load(path).AutoRestartSaved);
            var saved = CoodszHost.Services.HostSettings.Load(path);
            saved.AutoRestartSaved = true;
            CoodszHost.Services.HostSettings.Save(path, saved);
            Assert.True(CoodszHost.Services.HostSettings.Load(path).AutoRestartSaved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_box_starts_unticked_on_an_older_config_so_saving_another_setting_never_turns_restarts_on()
    {
        var settings = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Settings.cs"));
        Assert.Contains("AutoRestartBox.IsChecked = _settings.AutoRestartSaved && Flag(server, \"autoRestart\", true);", settings);
    }

    [Fact]
    public void Line_names_the_hours()
    {
        Assert.Equal("Automatic restart: the server has run for 3 hours, so it saves and starts again now.", ScheduledRestart.Line(TimeSpan.FromHours(3)));
        Assert.Contains("for 1 hour,", ScheduledRestart.Line(TimeSpan.FromHours(1)));
        Assert.Contains("for 1.5 hours,", ScheduledRestart.Line(TimeSpan.FromHours(1.5)));
    }
}
