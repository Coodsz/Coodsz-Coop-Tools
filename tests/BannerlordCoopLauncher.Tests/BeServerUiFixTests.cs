namespace BannerlordCoopLauncher.Tests;

// Release review fixes in the Burning Empires server tool's settings, saves, backups, log and window.
public sealed class BeServerUiFixTests
{
    private static string Window() => SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");

    [Fact]
    public void A_new_campaign_or_save_to_a_new_name_is_picked_in_the_box_so_the_next_start_hosts_it()
    {
        var source = Window();
        // SaveSettings stores what the box shows, so the new save has to be picked there first.
        Assert.Contains("_settings.SaveFile = SelectedSavePath();", SourceFiles.Method(source, "private void SaveSettings()"));
        var point = SourceFiles.Method(source, "private void PointAtSave(string sav)");
        var reload = point.IndexOf("ReloadSaves();", StringComparison.Ordinal);
        var remember = point.IndexOf("RememberSave(sav);", StringComparison.Ordinal);
        var save = point.IndexOf("SaveSettings();", StringComparison.Ordinal);
        Assert.True(reload >= 0 && reload < remember && remember < save, point);

        var pending = SourceFiles.Method(source, "private string? PendingNewCampaign()");
        Assert.Contains("PointAtSave(sav);", pending);
        Assert.DoesNotContain("_settings.SaveFile = sav;", pending);
        var saveAs = SourceFiles.Method(source, "private async void SaveToNewName_Click(");
        Assert.Contains("PointAtSave(sav);", saveAs);
        Assert.DoesNotContain("_settings.SaveFile = sav;", saveAs);
        // The new campaign's first save lets the name go while the server still runs.
        Assert.Contains("PendingNewCampaign();", SourceFiles.Method(source, "private void NoteSavedWorld("));
    }

    [Fact]
    public void A_stopped_run_with_empty_Burning_Empires_data_does_not_block_Restore_save_backup_or_Last_good_save()
    {
        var source = Window();
        // The stopped run's lines stay in _logTail until the next start, so only a running server's log counts.
        Assert.Contains("private string? RestoreBlockLog() => ServerIsRunning() ? _logTail.ToString() : null;", source);
        var restore = SourceFiles.Method(source, "private void RestoreBeSaveData(");
        Assert.Contains("RestoreBlockLog()", restore);
        Assert.DoesNotContain("_logTail", restore);
        var lastGood = SourceFiles.Method(source, "private async void RestartLastGood_Click(");
        Assert.Contains("BeSaveData.RestoreBlocked(RestoreBlockLog())", lastGood);
        Assert.DoesNotContain("_logTail", lastGood);
        // Restore save backup refuses a running server before it puts any file back.
        var click = SourceFiles.Method(source, "private void RestoreSaveBackup_Click(");
        Assert.True(click.IndexOf("ServerIsRunning()", StringComparison.Ordinal) < click.IndexOf("Restore(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_report_that_does_not_fail_leaves_room_for_a_later_failure_in_the_same_run()
    {
        // While the server runs, a setup step is informational and the empty data line is a failure.
        var step = BurningEmpires.Core.BeHostReport.Explain("[BurningEmpires.CoopCompat] MapBounds could not be set up: boom", wasServing: false, exited: false);
        Assert.False(step!.Value.Failed);
        var restore = BurningEmpires.Core.BeHostReport.Explain("ERROR Save safety: Burning Empires could not restore this save's data (load failed).", wasServing: true, exited: false);
        Assert.True(restore!.Value.Failed);

        var source = Window();
        var note = SourceFiles.Method(source, "private void NoteHostReport(");
        // Only a failing report, or the exit report, takes the run's one report.
        Assert.Contains("if (!report.Value.Failed && !exited)", note);
        Assert.Contains("_infoReportsNoted.Add(report.Value.Status)", note);
        Assert.True(note.IndexOf("else", StringComparison.Ordinal) < note.IndexOf("_blockedDllNoted = true;", StringComparison.Ordinal));
        Assert.Contains("_infoReportsNoted.Clear();", SourceFiles.Method(source, "private async Task StartServerCoreAsync("));
    }

    [Fact]
    public void A_mod_whose_code_the_server_skips_gets_no_server_Report_link()
    {
        var source = Window();
        var chips = SourceFiles.Method(source, "private FrameworkElement? ReportChips(");
        Assert.Contains("ModReports.HasEndpoint() && !ServerSkipsCode(moduleId, loadOrder)", chips);
        var skips = SourceFiles.Method(source, "private bool ServerSkipsCode(");
        Assert.Contains("ServerModuleNotes.SkippedCode(", skips);
        // The core and the mods CoopModPatch turns on always run on the server.
        Assert.Contains("BeServerPath.IsCore(moduleId)", skips);
        Assert.Contains("BeServerPath.RunsForCoopModPatch(", skips);
        Assert.True(BurningEmpires.Core.BeServerPath.RunsForCoopModPatch(["Coop", "CoopModPatch", "ImprovedGarrisons"], "ImprovedGarrisons"));
        Assert.False(BurningEmpires.Core.BeServerPath.RunsForCoopModPatch(["Coop", "ImprovedGarrisons"], "ImprovedGarrisons"));
    }

    [Fact]
    public void The_window_and_the_terms_box_fit_a_small_scaled_screen()
    {
        // The main window is fitted to the work area right after it loads its XAML, like the launchers.
        var ctor = SourceFiles.Method(Window(), "public MainWindow()");
        var fit = ctor.IndexOf("WindowFit.Fit(new WindowSize(Width, Height, MinWidth, MinHeight), SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height)", StringComparison.Ordinal);
        Assert.True(fit > ctor.IndexOf("InitializeComponent();", StringComparison.Ordinal), ctor);
        Assert.Contains("(Width, Height, MinWidth, MinHeight) = (fit.Width, fit.Height, fit.MinWidth, fit.MinHeight);", ctor);
        // 1366x768 at 150 percent leaves about 910 by 485.
        var size = BannerlordCoopLauncher.Core.Support.WindowFit.Fit(new(1280, 900, 1100, 720), 910, 485);
        Assert.True(size.Width <= 910 && size.Height <= 485 && size.MinWidth <= 910 && size.MinHeight <= 485);

        // The terms box: the details shrink and scroll in a star row, the buttons row stays Auto at the bottom.
        var project = Path.Combine(RepoRoot(), "src", "BurningEmpiresHost");
        var xaml = File.ReadAllText(Path.Combine(project, "ConfirmWindow.xaml"));
        Assert.Contains("<RowDefinition Height=\"*\"/>", xaml);
        var details = System.Text.RegularExpressions.Regex.Match(xaml, "<TextBox x:Name=\"DetailsBox\"[^>]*>").Value;
        Assert.Contains("Grid.Row=\"2\"", details);
        Assert.Contains("MaxHeight=\"320\"", details);
        Assert.DoesNotContain(" Height=\"320\"", details);
        Assert.Contains("<StackPanel Grid.Row=\"3\"", xaml);
        Assert.Contains("Coodsz.Ui.WindowPlacement.KeepOnScreen(this);", File.ReadAllText(Path.Combine(project, "ConfirmWindow.xaml.cs")));
    }

    [Fact]
    public void A_battle_size_under_300_in_the_shared_mod_config_shows_as_300_so_it_saves()
    {
        // Coodsz Host and Coop's own server allow 200, so the shared file can hold 250.
        var shown = BurningEmpires.Core.BeServerLaunch.StartBattleSize("{ \"modOptions\": { \"battleSize\": 250 } }");
        Assert.Equal(BurningEmpires.Core.BeServerLaunch.MinBattleSize, shown);
        var load = SourceFiles.Method(Window(), "private void LoadBattleSize()");
        Assert.Contains("_battleSizeBox.Text = BeServerLaunch.StartBattleSize(File.Exists(path) ? File.ReadAllText(path) : null).ToString();", load);
        Assert.DoesNotContain("CoopModConfig.ReadBattleSize(", load);
        // What the box shows passes the Save settings check, which Game speed runs too.
        Assert.Contains("size < MinBattleSize", SourceFiles.Method(Window(), "private string SaveBattleSize(string json)"));
    }

    [Fact]
    public void Start_server_says_it_can_make_the_new_campaign_and_takes_a_typed_name()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BurningEmpiresHost", "MainWindow.xaml"));
        var start = System.Text.RegularExpressions.Regex.Match(xaml, "<Button x:Name=\"StartButton\"[^>]*>").Value;
        Assert.Contains("ToolTip=\"Starts the server with the save you picked, or makes the new campaign you named in step 1.\"", start);
        Assert.Contains("Click=\"StartButton_Click\"", start);
        Assert.DoesNotContain("It does not create a campaign.", xaml);
        // The step 4 card went in redesign wave 6: Start server is on the Dashboard, and its hover says the same.
        Assert.DoesNotContain("Hosts the picked save, or makes the new campaign.", xaml);
        Assert.Contains("Type a name, then click New campaign or Start server.", xaml);

        var source = Window();
        // A typed name goes through New campaign's own checks first; Restart and Last good save call Start_Click and skip it.
        var click = SourceFiles.Method(source, "private void StartButton_Click(");
        Assert.True(click.IndexOf("NewCampaign_Click(sender, e);", StringComparison.Ordinal) < click.IndexOf("Start_Click(sender, e);", StringComparison.Ordinal));
        Assert.Contains("!ServerIsRunning()", click);
        Assert.Contains("NewCampaignBox.Clear();", SourceFiles.Method(source, "private void SaveBox_SelectionChanged("));
        // Pick save and Name server copy pick through RememberSave, which skips SaveBox_SelectionChanged.
        foreach (var picker in new[] { "private void BrowseSave_Click(", "private void NameSave_Click(" })
        {
            var body = SourceFiles.Method(source, picker);
            Assert.True(body.IndexOf("NewCampaignBox.Clear();", StringComparison.Ordinal) is var clear && clear >= 0
                && clear < body.IndexOf("RememberSave(", StringComparison.Ordinal), picker + " keeps a typed campaign name");
        }

        foreach (var language in BannerlordCoopLauncher.Core.Localization.Translator.Languages.Where(language => language.Code != BannerlordCoopLauncher.Core.Localization.Translator.English))
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language.Code);
            foreach (var text in new[]
            {
                "Starts the server with the save you picked, or makes the new campaign you named in step 1.",
                "Hosts the picked save, or makes the new campaign. Friends use the address below and UDP 4200.",
                "Type a name, then click New campaign or Start server. Start server makes a fresh Burning Empires world, no save needed. To host a campaign you already have, leave this empty and pick it in step 2."
            })
                Assert.True(table.ContainsKey(text), language.Code + " has no translation for " + text);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
