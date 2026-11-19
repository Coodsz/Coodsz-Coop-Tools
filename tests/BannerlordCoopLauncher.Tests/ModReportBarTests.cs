using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// Reporting in plain view: the report bar (the one time card, How did your mods work? and the
/// crash bar), the crash button with the suggested Broken mod, the count chips and how the three
/// clients are wired to them. Nothing here sends a report; the window still shows what is sent first.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class ModReportBarTests : IDisposable
{
    private const string TestEndpoint = "https://reports.example/report";
    private static readonly string[] Clients = ["BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher"];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-report-bar-" + Guid.NewGuid().ToString("N"));

    public ModReportBarTests()
    {
        Directory.CreateDirectory(_directory);
        ModReports.ResetSession();
    }

    public void Dispose()
    {
        ModReports.ResetSession();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_bar_after_a_game_needs_the_endpoint_and_mods_and_shows_once_per_run()
    {
        var settings = new ModReportSettings();
        Assert.Equal(ModReportPrompt.None, ModReports.AfterGame(settings, 3, crashed: false, ""));
        Assert.Equal(ModReportPrompt.None, ModReports.AfterGame(settings, 0, crashed: false, TestEndpoint));
        Assert.Equal(ModReportPrompt.Bar, ModReports.AfterGame(settings, 3, crashed: false, TestEndpoint));

        ModReports.MarkBarShown();
        Assert.False(ModReports.ShouldShowBar(settings, 3, TestEndpoint));
        Assert.Equal(ModReportPrompt.None, ModReports.AfterGame(settings, 3, crashed: false, TestEndpoint));

        // A crash later in the same run still gets the crash bar.
        Assert.Equal(ModReportPrompt.CrashBar, ModReports.AfterGame(settings, 3, crashed: true, TestEndpoint));

        ModReports.ResetSession();
        Assert.True(ModReports.ShouldShowBar(settings, 3, TestEndpoint), "a new run shows it again");
    }

    [Fact]
    public void Three_x_clicks_in_a_row_hide_the_bar_for_good_and_Report_starts_the_count_again()
    {
        var path = Path.Combine(_directory, "mod-reports.json");
        var settings = new ModReportSettings();
        Assert.False(ModReports.CloseBar(settings));
        Assert.False(ModReports.CloseBar(settings));
        ModReports.UsedBar(settings);
        Assert.Equal(0, settings.BarClosed);
        Assert.False(ModReports.CloseBar(settings));
        Assert.False(ModReports.CloseBar(settings));
        Assert.True(ModReports.CloseBar(settings), "the third x in a row");
        Assert.True(ModReports.SaveSettings(settings, path));

        var loaded = ModReports.LoadSettings(path);
        Assert.True(loaded.BarOff);
        Assert.False(ModReports.ShouldShowBar(loaded, 3, TestEndpoint));
        Assert.Equal(ModReportPrompt.None, ModReports.AfterGame(loaded, 3, crashed: false, TestEndpoint));
        // Hiding the bar never hides the crash button.
        Assert.Equal(ModReportPrompt.CrashBar, ModReports.AfterGame(loaded, 3, crashed: true, TestEndpoint));
        Assert.True(ModReports.OffersCrashReport(3, TestEndpoint));
        Assert.False(ModReports.OffersCrashReport(0, TestEndpoint));
        Assert.False(ModReports.OffersCrashReport(3, ""));
    }

    [Fact]
    public void Ask_me_after_a_game_still_opens_the_window_first_and_the_bar_is_the_default()
    {
        var on = new ModReportSettings { AskAfterGame = true };
        Assert.Equal(ModReportPrompt.Window, ModReports.AfterGame(on, 3, crashed: false, TestEndpoint));
        Assert.Equal(ModReportPrompt.Window, ModReports.AfterGame(on, 3, crashed: true, TestEndpoint));
        ModReports.MarkAsked();
        Assert.Equal(ModReportPrompt.Bar, ModReports.AfterGame(on, 3, crashed: false, TestEndpoint));

        Assert.False(new ModReportSettings().AskAfterGame, "the window stays opt in");
        Assert.Equal(ModReportPrompt.Bar, ModReports.AfterGame(new ModReportSettings(), 3, crashed: false, TestEndpoint));
    }

    [Fact]
    public void The_one_time_card_shows_until_it_is_closed_and_old_settings_files_still_read()
    {
        var path = Path.Combine(_directory, "mod-reports.json");
        File.WriteAllText(path, """{ "kind": "CoodszModReportSettings", "install": "", "askAfterGame": false, "previewSeen": true, "sent": [] }""");
        var old = ModReports.LoadSettings(path);
        Assert.True(old.PreviewSeen);
        Assert.False(old.IntroSeen);
        Assert.False(old.BarOff);
        Assert.Equal(0, old.BarClosed);
        Assert.True(ModReports.ShouldShowIntro(old, TestEndpoint));
        Assert.False(ModReports.ShouldShowIntro(old, ""), "no endpoint, no card");

        old.IntroSeen = true;
        Assert.True(ModReports.SaveSettings(old, path));
        Assert.False(ModReports.ShouldShowIntro(ModReports.LoadSettings(path), TestEndpoint), "gone for good once closed");
    }

    [Fact]
    public void The_crash_bar_replaces_anything_and_the_card_only_shows_alone()
    {
        Assert.True(ModReports.Replaces(ModReportBarKind.Intro, ModReportBarKind.None));
        Assert.False(ModReports.Replaces(ModReportBarKind.Intro, ModReportBarKind.AfterGame));
        Assert.False(ModReports.Replaces(ModReportBarKind.Intro, ModReportBarKind.Crash));
        Assert.True(ModReports.Replaces(ModReportBarKind.AfterGame, ModReportBarKind.Intro));
        Assert.False(ModReports.Replaces(ModReportBarKind.AfterGame, ModReportBarKind.Crash));
        Assert.True(ModReports.Replaces(ModReportBarKind.Crash, ModReportBarKind.Crash), "a new crash shows its own mods");
        Assert.True(ModReports.Replaces(ModReportBarKind.Crash, ModReportBarKind.AfterGame));
        Assert.False(ModReports.Replaces(ModReportBarKind.None, ModReportBarKind.None));
    }

    [Fact]
    public void The_crash_log_names_the_mod_to_suggest_as_Broken()
    {
        var extras = new StartupCrashExplain.ModName[] { new("Bannerlord.Diplomacy", "Diplomacy"), new("ImprovedGarrisons", "Improved Garrisons") };
        Assert.Equal(["ImprovedGarrisons"],
            StartupCrashExplain.NamedMods(null, "[12:00] ImprovedGarrisons could not be loaded correctly", extras));
        Assert.Equal(["Bannerlord.Diplomacy"],
            StartupCrashExplain.NamedMods(null, "System.NullReferenceException: Object reference\n   at Diplomacy.CampaignBehaviors.Tick()\n   at Bannerlord.Diplomacy.SubModule.OnTick()", extras));
        Assert.Equal(["Bannerlord.Diplomacy"],
            StartupCrashExplain.NamedMods("Exception: System.InvalidOperationException\nSource: Bannerlord.Diplomacy", "", extras));
        Assert.Empty(StartupCrashExplain.NamedMods("", "Exception in TaleWorlds.Engine", extras));
        // TAOM CR-W4H9X: the last DLL the game loaded is not an exception, though its name has the word in it.
        var taomPlus = new StartupCrashExplain.ModName[] { new("BetterExceptionWindow", "Better Exception Window") };
        Assert.Empty(StartupCrashExplain.NamedMods(null,
            @"[20:17:27.628] Loading assembly: C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\2875478172\bin\Win64_Shipping_Client\BetterExceptionWindow.dll", taomPlus));
        Assert.Empty(StartupCrashExplain.NamedMods(null, null, extras));
    }

    [Fact]
    public void The_counts_are_green_and_red_chips_and_an_easy_tip_line()
    {
        Assert.Equal("12 say works", ModReports.WorksChip(12));
        Assert.Equal("2 say broken", ModReports.BrokenChip(2));
        Assert.Null(ModReports.WorksChip(0));
        Assert.Null(ModReports.BrokenChip(0));
        Assert.Equal("Players reported: 12 say works, 2 say broken.", ModReports.CountsLine(12, 2));

        var db = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "A", "status": "Works", "works": 12, "broken": 2 },
              { "id": "Quiet", "status": "Unknown", "works": 0, "broken": 0 }
            ] }
            """)!;
        var a = CommunityCompat.Badge("A", db)!;
        Assert.Equal(("12 say works", "2 say broken"), (a.WorksChip, a.BrokenChip));
        Assert.Contains("Players reported: 12 say works, 2 say broken.", a.Tip);
        var quiet = CommunityCompat.Badge("Quiet", db)!;
        Assert.Null(quiet.WorksChip);
        Assert.Null(quiet.BrokenChip);
        Assert.Equal(a.WorksChip, a.Translated().WorksChip);
    }

    [Fact]
    public void AD1259_reports_carry_their_own_tool_name()
    {
        static ModReport Build(string tool) => ModReports.Build("0123456789abcdef0123456789abcdef", tool, "1.0.20", "v0.1.6", "v1.4.8",
            new[] { new ModReportItem { Id = "RBM", Result = ModReportItem.Broken } }, Array.Empty<ModReportMod>());
        Assert.Equal("ad1259", Build("ad1259").Tool);
        Assert.Equal("taom", Build("taom").Tool);
        Assert.Equal("coop", Build("coop").Tool);
        Assert.Equal("coop", Build("something else").Tool);
        Assert.Contains("Tool = \"ad1259\"", SourceFiles.Window(RepoRoot(), "AD1259Launcher"));
    }

    [Fact]
    public void Every_client_shows_the_report_entry_points()
    {
        foreach (var client in Clients)
        {
            var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", client, "MainWindow.xaml"));
            foreach (var slot in new[] { "ReportBarHost", "ReportModSlot", "CrashLinkSlot" })
                Assert.Contains("x:Name=\"" + slot + "\"", xaml);
            foreach (var brush in new[] { "ReportRed", "ReportRedBack", "ReportGold", "ReportGoldBack", "ReportGreen", "ReportGreenBack", "ReportInk" })
                Assert.Contains("x:Key=\"" + brush + "\"", xaml);

            var source = SourceFiles.Window(RepoRoot(), client);
            var setUp = SourceFiles.Method(source, "private void SetUpModReports(");
            Assert.True(setUp.IndexOf("ModReports.HasEndpoint()", StringComparison.Ordinal) < setUp.IndexOf("new Coodsz.Ui.ModReportBar(", StringComparison.Ordinal),
                client + " sets up reporting only with the endpoint");
            Assert.Contains("ReportModSlot.Content = Coodsz.Ui.ModReportUi.MainButton(this, _reportBar.OpenCurrent);", setUp);
            // The Mods card has no second report button; the header's Report a mod opens the same window.
            Assert.DoesNotContain("ModsPageRow", setUp);
            Assert.Contains("CrashLinkSlot.Content = Coodsz.Ui.ModReportUi.CrashLink(this, _reportBar.OpenCrash);", setUp);
            Assert.Contains("_reportBar.ShowIntroOnce()", setUp);
            Assert.Contains("SetUpModReports();", source);
            // Crash reports ride on the same bar: Send crash report, Send my logs and its link.
            Assert.Contains("CrashRequest = CrashRequest", setUp);
            Assert.Contains("SetUpCrashReports();", setUp);
            foreach (var slot in new[] { "SendLogsSlot", "SendLogsLinkSlot" })
                Assert.Contains("x:Name=\"" + slot + "\"", xaml);
        }

        // Coop and Friends binds the chips in its mod rows; the others build them per row.
        Assert.Contains("Compat.WorksChip", File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "MainWindow.xaml")));
        Assert.Contains("Compat.BrokenChip", File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "MainWindow.xaml")));
        Assert.Contains("CommunityCounts(moduleId, workshopId)", SourceFiles.Window(RepoRoot(), "TaomCoopLauncher"));
        Assert.Contains("CommunityCounts(status.Mod)", SourceFiles.Window(RepoRoot(), "AD1259Launcher"));
        Assert.Contains("ModReportWindow.cs", File.ReadAllText(Path.Combine(RepoRoot(), "src", "AD1259Launcher", "AD1259Launcher.csproj")));
    }

    [Fact]
    public void The_crash_button_opens_the_report_window_with_the_crash_mods_suggested_Broken()
    {
        var friends = SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher");
        var startup = SourceFiles.Method(friends, "private async Task WatchStartupAsync(");
        Assert.Contains("StartupCrashExplain.NamedMods(", startup);
        Assert.Contains("_reportBar?.Crash(", startup);
        Assert.Contains(".AsCrash(named, CrashLine(capture))", startup);
        Assert.Contains("request.AsCrash(CrashNamed(capture,", SourceFiles.Method(friends, "private async Task RecordSessionAsync("));

        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        Assert.Contains("request.AsCrash(CrashNamed(capture, setup), CrashLine(capture))", SourceFiles.Method(taom, "private async Task RecordSessionAsync("));
        var explain = SourceFiles.Method(taom, "private async Task ExplainEarlyExitAsync(");
        Assert.Contains("var named = CrashNamed(capture, setup);", explain);
        Assert.Contains("ShowCrashReport(setup, named, CrashLine(capture));", explain);
        Assert.Contains("ShowCrashReport(setup, new[] { found });", SourceFiles.Method(taom, "private async void FindCrash_Click("));

        var ad1259 = SourceFiles.Window(RepoRoot(), "AD1259Launcher");
        Assert.Contains("ShowCrashReport(capture);", SourceFiles.Method(ad1259, "private async Task ExplainEarlyExitAsync("));
        Assert.Contains("_ = WatchAfterGameAsync(started);", SourceFiles.Method(ad1259, "private async Task WatchJoinAsync("));
        Assert.Contains("request.AsCrash(CrashNamed(capture), CrashLine(capture))", SourceFiles.Method(ad1259, "private async Task WatchAfterGameAsync("));
        Assert.Contains("_reportBar?.AfterGame(request, crashed: !closedNormally);", SourceFiles.Method(ad1259, "private async Task WatchAfterGameAsync("));

        // The shared window: the crash bar's biggest button opens the report window, suggested mods
        // start as Broken, and the exact text is in view from the start, so one Send click sends it.
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        var crash = SourceFiles.Method(window, "private FrameworkElement BuildCrash(");
        Assert.Contains("ModReportUi.Filled(_host, CrashButton,", crash);
        Assert.Contains("size: 19", crash);
        Assert.Contains("ModReportWindow.Show(_owner, request);", crash);
        Assert.Contains("public const string CrashButton = \"Tell everyone which mod broke\";", window);
        Assert.Contains("Pick(row, ModReportItem.Broken);", window);
        Assert.Contains("stack.Children.Add(_preview);", window);
        Assert.DoesNotContain("Click Send again", window);
        Assert.DoesNotContain("SendAsync(", SourceFiles.Method(window, "public sealed class ModReportBar"));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>ModReports keeps what showed this run in static state, so these tests never run side by side.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModReportsSession
{
    public const string Name = "Mod reports session";
}
