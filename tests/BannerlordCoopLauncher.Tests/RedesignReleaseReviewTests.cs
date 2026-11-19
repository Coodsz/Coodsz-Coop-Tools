using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// The release review of the redesign (f5e69502..556cf579): texts that name the new UI, guards and the client pills.
public class RedesignReleaseReviewTests
{
    private static string Src(params string[] parts) => SourceFiles.Read(Path.Combine(["src", .. parts]));

    [Fact]
    public void The_coop_server_tool_names_the_server_online_pill_not_serving()
    {
        foreach (var file in new[] { "MainWindow.ServerControl.cs", "MainWindow.CrashRestart.cs", "MainWindow.AutoRescue.cs" })
        {
            var source = Src("CoodszHost", file);
            Assert.DoesNotContain("changes to SERVING", source);
            Assert.Contains("The pill at the top says Server online when it is ready.", source);
        }
        Assert.DoesNotContain("still shows SERVING", SourceFiles.Read("README.txt"));
    }

    [Fact]
    public void Texts_name_the_mods_page_and_where_fix_antivirus_is()
    {
        var save = Src("BannerlordCoopLauncher.Core", "Modules", "SaveModChange.cs");
        Assert.DoesNotContain("on the Modules page", save);
        Assert.Contains("on the Mods page", save);
        Assert.Equal("Fix antivirus is on the Logs and help page.", AntivirusText.PlaceLogsPage);
        foreach (var client in new[] { "TaomCoopLauncher", "BurningEmpiresLauncher" })
            Assert.Contains("Content=\"Fix antivirus\"", Src(client, "MainWindow.xaml"));
    }

    [Fact]
    public void The_taom_dashboard_shows_the_join_port_it_uses()
    {
        Assert.Contains("DashPort.Text = \"UDP \" + JoinPort;", Src("TaomCoopHost", "MainWindow.Frame.cs"));
    }

    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void Prepare_server_refuses_while_the_server_runs(string project)
    {
        var prepare = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private async void Prepare_Click(");
        var busy = prepare.IndexOf("if (_busy || _startingServer)", StringComparison.Ordinal);
        var running = prepare.IndexOf("if (ServerIsRunning())", StringComparison.Ordinal);
        Assert.True(busy >= 0 && busy < running, "the busy check first, then the running server");
        Assert.Contains("\"Stop the server before Prepare server.\"", prepare);
    }

    [Fact]
    public void Taom_restore_save_backup_waits_for_a_running_action()
    {
        var restore = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost"), "private void RestoreSaveBackup_Click(");
        Assert.True(restore.IndexOf("if (_busy || _startingServer)", StringComparison.Ordinal) < restore.IndexOf("if (ServerIsRunning())", StringComparison.Ordinal));
        Assert.Contains("if (_busy || _startingServer)", restore);
    }

    [Theory]
    [InlineData("TaomCoopLauncher")]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("AD1259Launcher")]
    public void The_join_pill_is_cleared_only_after_the_join_result(string project)
    {
        var source = SourceFiles.Window(SourceFiles.Root(), project);
        var watch = source.IndexOf("await WatchJoinAsync(started, _gameRoot", StringComparison.Ordinal);
        var clear = source.IndexOf("ClearJoinStateWhenClosed(started);", StringComparison.Ordinal);
        Assert.True(watch >= 0 && watch < clear, "the clear is registered in the finally after the watch");
        Assert.Contains("finally", source[watch..clear]);
    }

    [Fact]
    public void The_be_client_rows_use_the_shared_chip()
    {
        var pill = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresLauncher"), "private System.Windows.Controls.Border StatusPill(");
        Assert.Contains("new Coodsz.Ui.Chip", pill);
        Assert.Contains("ModChips.Status(kind)", pill);
        Assert.Equal(ModChipKind.Mute, ModChips.StatusKind("Off"));
    }

    [Theory]
    [InlineData("TaomCoopLauncher", "MainWindow.xaml")]
    [InlineData("BurningEmpiresLauncher", "MainWindow.xaml")]
    [InlineData("TaomCoopHost", "ConfirmWindow.xaml")]
    public void The_old_warm_palette_is_gone(string project, string file)
    {
        var xaml = Src(project, file);
        foreach (var warm in new[] { "#FFE8E6E1", "#FF7DAAA4", "#FF7EA4C4", "#FF121416", "#FF5C6058", "#FFD2A36A" })
            Assert.DoesNotContain(warm, xaml);
    }

    [Theory]
    [InlineData("TaomCoopLauncher")]
    [InlineData("BurningEmpiresLauncher")]
    public void The_steam_dot_and_steam_hint_use_the_keep_colours(string project)
    {
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        var steam = SourceFiles.Method(window, "private void UpdateSteamStatus()");
        Assert.Contains("\"Keep.Success\" : \"Keep.Amber\"", steam);
        Assert.DoesNotContain("Color.FromRgb", steam);
        var hint = SourceFiles.Method(window, "private void ShowSteamHint()");
        Assert.Contains("\"Keep.Accent\"", hint);
        Assert.DoesNotContain("0x7D, 0xAA, 0xA4", hint);
    }

    [Theory]
    [InlineData("README.txt")]
    [InlineData("Coodsz-TAOM-Instructions.txt")]
    public void Taom_guides_tell_the_set_aside_ask_and_put_back(string file)
    {
        var guide = SourceFiles.Read(file);
        Assert.DoesNotContain("Do not click **Restore parked Coop**", guide);
        Assert.Contains("the launcher asks before it sets it aside, and puts it back when you close the launcher, or when Bannerlord closes if the game is still open then. Click **Restore parked Coop** on **Settings** only when that Coop did not come back.", guide);
    }

    [Fact]
    public void The_unused_realm_pictures_are_gone()
    {
        foreach (var project in new[] { "CoodszHost", "TaomCoopHost", "BurningEmpiresHost", "AD1259Launcher", "BurningEmpiresLauncher", "TaomCoopLauncher", "CoodszCoopAndFriends" })
        {
            var csproj = Src(project, project + ".csproj");
            Assert.DoesNotContain("realm-nav.jpg", csproj);
            Assert.DoesNotContain("realm-strip.jpg", csproj);
        }
        Assert.False(File.Exists(Path.Combine(SourceFiles.Root(), "src", "BannerlordCoopLauncher", "Assets", "coodsz-friends-bg.jpg")));
        // The server tools' Join my server card still shows the emblem.
        Assert.True(File.Exists(Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", "Assets", "realm-emblem.png")));
    }
}
