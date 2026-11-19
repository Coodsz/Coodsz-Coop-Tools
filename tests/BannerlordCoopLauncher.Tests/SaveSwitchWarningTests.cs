using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// TAOM report #44: Name server copy and Start server quietly switched the hosted campaign to an earlier save.
public class SaveSwitchWarningTests
{
    [Fact]
    public void An_earlier_save_asks_before_the_copy_and_before_start()
    {
        Assert.True(SaveSwitchWarning.IsEarlier("Gondor", "212.75", "save001", "40.2"));

        var copy = SaveSwitchWarning.CopyQuestion(ServerSave("Gondor"), "212.75", SinglePlayerSave("save001"), "40.2");
        Assert.Equal("save001 is on campaign day 40. The save the server last ran, Gondor, is on day 212. Make the copy anyway?", copy);
        var start = SaveSwitchWarning.StartQuestion("Gondor", "212.75", "save001", "40.2");
        Assert.Equal("Host save001 (day 40) instead of Gondor (day 212)? The server last ran Gondor.", start);
        // Player text: no colons, no hyphens between words.
        foreach (var text in new[] { copy!, start!, SaveSwitchWarning.NowHosts("save001"), SaveSwitchWarning.NotStarted("Gondor") })
        {
            Assert.DoesNotContain(":", text);
            Assert.DoesNotMatch(@"[a-z]-[a-z]", text);
        }
    }

    [Theory]
    [InlineData("Gondor", "212", "save001", "212")]
    [InlineData("Gondor", "212", "save001", "212.9")]
    [InlineData("Gondor", "212", "save001", "300")]
    [InlineData("Gondor", "212", "gondor", "10")]
    [InlineData("Gondor", "", "save001", "10")]
    [InlineData("Gondor", "212", "save001", null)]
    [InlineData("Gondor", "not a day", "save001", "10")]
    [InlineData("Gondor", "212", "save001", "NaN")]
    [InlineData("", "212", "save001", "10")]
    [InlineData(null, "212", "save001", "10")]
    public void A_later_or_same_day_the_same_save_or_an_unreadable_day_never_asks(string? hosted, string? hostedDay, string picked, string? pickedDay)
    {
        Assert.False(SaveSwitchWarning.IsEarlier(hosted, hostedDay, picked, pickedDay));
        Assert.Null(SaveSwitchWarning.StartQuestion(hosted, hostedDay, picked, pickedDay));
        // The copy compares files: only the day cases are quiet there, a same name file elsewhere is another save.
        if (!string.IsNullOrEmpty(hosted) && !picked.Equals(hosted, StringComparison.OrdinalIgnoreCase))
            Assert.Null(SaveSwitchWarning.CopyQuestion(ServerSave(hosted), hostedDay, SinglePlayerSave(picked), pickedDay));
    }

    // Review of #44: an older single player save001 copied while the server hosts its own save001 asked nothing.
    [Fact]
    public void A_copy_compares_files_so_a_same_name_older_save_still_asks()
    {
        var copy = SaveSwitchWarning.CopyQuestion(ServerSave("save001"), "212", SinglePlayerSave("save001"), "40");
        Assert.Equal("save001 is on campaign day 40. The save the server last ran, save001, is on day 212. Make the copy anyway?", copy);
        // The very same file, a later day, an unreadable day or no hosted save never asks.
        Assert.Null(SaveSwitchWarning.CopyQuestion(ServerSave("save001"), "212", ServerSave("save001").ToUpperInvariant(), "40"));
        Assert.Null(SaveSwitchWarning.CopyQuestion(ServerSave("save001"), "212", SinglePlayerSave("save001"), "300"));
        Assert.Null(SaveSwitchWarning.CopyQuestion(ServerSave("save001"), "?", SinglePlayerSave("save001"), "40"));
        Assert.Null(SaveSwitchWarning.CopyQuestion(null, "212", SinglePlayerSave("save001"), "40"));
        // Start stays name based: the box only hosts server copies, one per name.
        Assert.Null(SaveSwitchWarning.StartQuestion("save001", "212", "save001", "40"));
    }

    private static string ServerSave(string name) => Path.Combine(Path.GetTempPath(), "taom-data", "Game Saves", name + ".sav");

    private static string SinglePlayerSave(string name) => Path.Combine(Path.GetTempPath(), "Documents", "Mount and Blade II Bannerlord", "Game Saves", name + ".sav");

    [Fact]
    public void The_save_box_shows_the_campaign_day_or_nothing()
    {
        Assert.Equal("Campaign day 40", SaveSwitchWarning.BoxDay("40.6"));
        Assert.Equal("Campaign day 1", SaveSwitchWarning.BoxDay("0.2"));
        Assert.Equal("", SaveSwitchWarning.BoxDay(""));
        Assert.Equal("", SaveSwitchWarning.BoxDay(null));
        Assert.Equal("", SaveSwitchWarning.BoxDay("soon"));
    }

    // Both server tools run the same code: the copy asks, the copy line names the save Start hosts, Start asks
    // once, the save box shows the day, and a running server keeps its save in server-config.json and on Restart.
    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void Both_server_tools_ask_and_keep_the_running_save(string project)
    {
        var host = SourceFiles.Window(RepoRoot(), project);
        var name = SourceFiles.Method(host, "private void NameSave_Click");
        Assert.Contains("if (!renamingServerCopy && !ConfirmOlderCopy(selected))", name);
        Assert.Contains("SaveSwitchWarning.NowHosts(name)", name);
        Assert.True(name.IndexOf("ConfirmOlderCopy(selected)", StringComparison.Ordinal) < name.IndexOf("File.Copy(", StringComparison.Ordinal));
        Assert.Contains("SaveSwitchWarning.StartQuestion(hosted, HostedSaveDay(hosted)", SourceFiles.Method(host, "private bool ConfirmSaveSwitch"));
        Assert.Contains("if (!ConfirmSaveSwitch(save))", SourceFiles.Method(host, "private async Task StartServerCoreAsync"));
        var config = SourceFiles.Method(host, "private void WriteConfig(");
        Assert.Contains("var hosted = ServerIsRunning() ? ConfiguredSaveName() : \"\";", config);
        var stop = SourceFiles.Method(host, "private async Task SaveThenStop(bool restart)");
        Assert.True(stop.IndexOf("SelectHostedSave(LastRanSaveName());", StringComparison.Ordinal) is > 0 and var at
            && at < stop.LastIndexOf("Start_Click(this, new RoutedEventArgs());", StringComparison.Ordinal));
        // The copy question compares the two files, not their names.
        Assert.Contains("SaveSwitchWarning.CopyQuestion(hostedPath, CampaignSaveInfoReader.Read(hostedPath)?.Day, selected.FullPath,",
            SourceFiles.Method(host, "private bool ConfirmOlderCopy"));
        // The tool's own starts never open the question and host the save the server ran.
        Assert.Contains("if (_automaticRun || hosted.Length == 0", SourceFiles.Method(host, "private bool ConfirmSaveSwitch"));
        var retry = SourceFiles.Method(host, "private async Task StartAfterLoadCrashAsync()");
        Assert.True(retry.IndexOf("SelectHostedSave(hosted);", StringComparison.Ordinal) is > 0 and var selected
            && selected < retry.IndexOf("await StartServerAsync(automatic: true);", StringComparison.Ordinal));
        Assert.Contains("_startFromTool = true;", stop);
        var lastGood = host[host.IndexOf("Restart on the save from", StringComparison.Ordinal)..];
        lastGood = lastGood[..lastGood.IndexOf("Start_Click(this, new RoutedEventArgs());", StringComparison.Ordinal)];
        Assert.Contains("SelectHostedSave(name);", lastGood);
        Assert.Contains("_startFromTool = true;", lastGood);
        var click = SourceFiles.Method(host, "private async void Start_Click");
        Assert.True(click.IndexOf("_startFromTool = false;", StringComparison.Ordinal) < click.IndexOf("return;", StringComparison.Ordinal));
        // A yes holds only for the start again after a setup switch, and goes when the box pick changes.
        var start = SourceFiles.Method(host, "private async Task StartServerAsync(");
        Assert.Contains("_automaticRun = automatic;", start);
        Assert.Contains("if (!_keepSaveSwitchAgreed)", start);
        Assert.Contains("_saveSwitchAgreed = null;", start);
        Assert.Contains("_keepSaveSwitchAgreed = true;", host);
        Assert.Contains("_saveSwitchAgreed = null;", SourceFiles.Method(host, "private void SaveBox_SelectionChanged"));
        var xaml = SourceFiles.Read("src/" + project + "/MainWindow.xaml");
        Assert.Contains("Text=\"{Binding DayText}\"", xaml);
        Assert.Contains("public string DayText", SourceFiles.Read("src/" + project + "/MainWindow.xaml.cs"));
    }

    // v1.3.7 review: host Gondor, Stop server, pick the older save001, Save settings, Start server asked nothing,
    // since Save settings had already written save001 into server-config.json and Start compared against that.
    [Fact]
    public void Save_settings_after_picking_an_older_save_still_asks_at_start()
    {
        // Gondor's server ran; the stopped server's config now names the box pick.
        var hosted = SaveSwitchWarning.LastRan("Gondor", keptExists: true, configuredName: "save001");
        Assert.Equal("Gondor", hosted);
        Assert.Equal("Host save001 (day 40) instead of Gondor (day 212)? The server last ran Gondor.",
            SaveSwitchWarning.StartQuestion(hosted, "212", "save001", "40"));
        // Nothing kept yet, or the kept save is gone: the config name, as before.
        Assert.Equal("save001", SaveSwitchWarning.LastRan("", keptExists: false, configuredName: "save001"));
        Assert.Equal("save001", SaveSwitchWarning.LastRan("Gondor", keptExists: false, configuredName: "save001"));
        Assert.Equal("", SaveSwitchWarning.LastRan(null, keptExists: false, configuredName: null));
    }

    // The same sequence in both server tools: only a Start whose server ran keeps the name, Save settings never does,
    // and the tool's own starts still never ask.
    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void Both_server_tools_compare_against_the_save_the_server_last_ran(string project)
    {
        var host = SourceFiles.Window(RepoRoot(), project);
        var confirm = SourceFiles.Method(host, "private bool ConfirmSaveSwitch");
        Assert.Contains("var hosted = LastRanSaveName();", confirm);
        Assert.Contains("if (_automaticRun || hosted.Length == 0", confirm);
        Assert.Contains("var hosted = LastRanSaveName();", SourceFiles.Method(host, "private bool ConfirmOlderCopy"));
        Assert.Contains("SaveSwitchWarning.LastRan(kept, kept.Length > 0 && File.Exists(HostedSavePath(kept)), ConfiguredSaveName())",
            SourceFiles.Method(host, "private string LastRanSaveName()"));
        Assert.DoesNotContain("LastHostedSave", SourceFiles.Method(host, "private void WriteConfig("));
        Assert.DoesNotContain("LastHostedSave", SourceFiles.Method(host, "private void SaveSettings()"));
        var start = SourceFiles.Method(host, "private async Task StartServerCoreAsync");
        var kept = start.IndexOf("_settings.LastHostedSave = Path.GetFileNameWithoutExtension(save);", StringComparison.Ordinal);
        Assert.True(kept > start.IndexOf("process.Start()", StringComparison.Ordinal));
        Assert.True(kept > start.IndexOf("_process = process;", StringComparison.Ordinal));
        Assert.Contains("public string LastHostedSave { get; set; } = \"\";", SourceFiles.Read("src/" + project + "/MainWindow.xaml.cs"));
    }

    // v1.3.7 review: a load crash, then Save settings with another pick in the 5 second retry wait. The retry read
    // server-config.json, so it hosted the new pick. The tool's own starts host the save the server last ran.
    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void Automatic_starts_host_the_save_the_server_last_ran(string project)
    {
        var host = SourceFiles.Window(RepoRoot(), project);
        var select = SourceFiles.Method(host, "private void SelectHostedSave(string hosted)");
        Assert.DoesNotContain("ConfiguredSaveName()", select);
        var retry = SourceFiles.Method(host, "private async Task StartAfterLoadCrashAsync()");
        Assert.Contains("var hosted = LastRanSaveName();", retry);
        // The backup before the deeper fixes is of the save that loads, not the new pick.
        Assert.Contains("await BackupBeforeFixesAsync(hosted);", retry);
        Assert.DoesNotContain("ConfiguredSaveName()", retry);
        Assert.Contains("SelectHostedSave(LastRanSaveName());", SourceFiles.Method(host, "private async Task SaveThenStop(bool restart)"));
        // Last good save puts back the save it names, so Start hosts that one.
        var lastGood = SourceFiles.Method(host, "void RestartLastGood_Click(");
        Assert.Contains("var name = ConfiguredSaveName();", lastGood);
        Assert.Contains("SelectHostedSave(name);", lastGood);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
