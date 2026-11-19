using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class ModTestViewTests
{
    private static ModTestCandidate Mod(string id, string version = "v1.0") => new(id, id + " name", "", version, Array.Empty<TaomExtraMod>());

    private static ModTestResult Result(string id, string version, string result, string date, string reason = "") =>
        new() { Id = id, Name = id + " name", Version = version, Result = result, Date = date, Reason = reason };

    [Fact]
    public void A_row_shows_the_newest_result_of_its_version_first()
    {
        var results = new[]
        {
            Result("RBM", "v1.0", "fail", "2026-10-10", "Crash"),
            Result("rbm", "v1.0", "pass", "2026-10-11"),
            Result("RBM", "v2.0", "pass", "2026-10-12")
        };

        Assert.Equal("2026-10-11", ModTestView.LastResult(results, Mod("RBM", "v1.0"))!.Date);
        // Another version only when this version was never tested.
        Assert.Equal("2026-10-12", ModTestView.LastResult(results, Mod("RBM", "v3.0"))!.Date);
        Assert.Null(ModTestView.LastResult(results, Mod("Other")));

        Assert.Equal(ModTestRowState.NotTested, ModTestView.StateOf(null));
        Assert.Equal(ModTestRowState.Passed, ModTestView.StateOf(results[1]));
        Assert.Equal(ModTestRowState.Failed, ModTestView.StateOf(results[0]));
        Assert.Equal("Not tested", ModTestView.PillText(ModTestRowState.NotTested));
        Assert.Equal("Testing", ModTestView.PillText(ModTestRowState.Testing));
    }

    [Fact]
    public void Results_of_mods_not_in_the_list_are_kept_apart_newest_first()
    {
        var results = new[]
        {
            Result("Gone", "v1", "fail", "2026-10-01"),
            Result("RBM", "v1.0", "pass", "2026-10-02"),
            Result("Old", "v1", "pass", "2026-10-03"),
            Result("Gone", "v2", "pass", "2026-10-04")
        };

        var others = ModTestView.OtherResults(results, [Mod("RBM")]);
        Assert.Equal(new[] { "Gone", "Old" }, others.Select(result => result.Id));
        Assert.Equal("v2", others[0].Version);
    }

    [Fact]
    public void Badges_follow_the_mod_list_groups()
    {
        var list = TaomCompatList.Parse("""
            {"kind":"CoodszTaomCompat","schemaVersion":3,"mods":[
              {"id":"Coop","name":"Coop mod","tag":"safe","source":"coodsz-coop"},
              {"id":"Loads","name":"Loads mod","tag":"loads","source":"server-loads"},
              {"id":"Works","name":"Works mod","tag":"reported","source":"community"},
              {"id":"Risk","name":"Risky mod","tag":"risky","source":"server-loads"},
              {"id":"Bad","name":"Broken mod","tag":"broken"}
            ]}
            """);

        Assert.Equal("Tested in Coop", ModTestView.ListBadge(list, Mod("Coop"))!.Value.Text);
        Assert.Equal("Loads on the server", ModTestView.ListBadge(list, Mod("Loads"))!.Value.Text);
        Assert.Equal("Works in TAOM", ModTestView.ListBadge(list, Mod("Works"))!.Value.Text);
        Assert.Equal("Risky", ModTestView.ListBadge(list, Mod("Risk"))!.Value.Text);
        var broken = ModTestView.ListBadge(list, Mod("Bad"))!.Value;
        Assert.Equal("Broken", broken.Text);
        Assert.Equal("red", TaomCompatList.BadgeColor(broken.Entry.Kind));
        Assert.Null(ModTestView.ListBadge(list, Mod("Unknown")));
    }

    [Fact]
    public void Count_progress_clock_and_summary_lines()
    {
        Assert.Equal("6 of 11 selected", ModTestView.SelectedLine(6, 11));
        Assert.Equal("Testing 3 of 10, RBM name", ModTestView.ProgressLine(new ModTestProgress(3, 10, Mod("RBM"), "Loading")));
        Assert.Equal("0:00", ModTestView.Clock(TimeSpan.FromSeconds(-4)));
        Assert.Equal("4:07", ModTestView.Clock(TimeSpan.FromSeconds(247)));
        Assert.Equal("1:02:03", ModTestView.Clock(new TimeSpan(1, 2, 3)));

        var mods = new[] { Mod("A"), Mod("B"), Mod("C") };
        Assert.Equal("No mod in this list was tested yet.", ModTestView.Summary(mods, []));
        var results = new[] { Result("A", "v1.0", "pass", "x"), Result("B", "v1.0", "fail", "x"), Result("Gone", "v1", "pass", "x") };
        Assert.Equal("1 passed, 1 failed, 1 not tested.", ModTestView.Summary(mods, results));
        Assert.Equal("1 passed, 1 failed.", ModTestView.Summary(mods[..2], results));
    }

    [Fact]
    public void The_window_is_a_tidy_list_with_live_rows_and_the_gold_progress_bar()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        var window = File.ReadAllText(Path.Combine(directory!.FullName, "src", "TaomCoopHost", "ModTestWindow.cs"));

        // The tool's own button styles, and Stop only while a test runs.
        Assert.Contains("MakeButton(\"Start test\", Look.Primary", window);
        Assert.Contains("MakeButton(\"Stop\", Look.Danger", window);
        Assert.Contains("_stop.IsEnabled = running;", window);
        // The same gold bar as the update and loading bars.
        Assert.Contains("Foreground = Gold, Background = GoldDark", window);
        Assert.Contains("BrushOf(0xE2, 0xC2, 0x7A)", window);
        Assert.Contains("BrushOf(0x3A, 0x34, 0x20)", window);
        // One row per mod with the list badge in the list colors and the last result; no second results list.
        Assert.Contains("ModTestView.ListBadge(compat, mod)", window);
        Assert.Contains("MainWindow.CompatColor(badge.Entry.Kind)", window);
        Assert.Contains("ModTestView.LastResult(saved, row.Mod)", window);
        Assert.DoesNotContain("WrapPanel", window);
        Assert.Contains("ModTestView.SelectedLine(", window);
        // A late progress line does not turn a finished row back to Testing.
        Assert.Contains("is not { Finished: false } row", SourceFiles.Method(window, "private void OnProgress("));
        // Closing while a test runs stops it first.
        Assert.Contains("e.Cancel = true;", SourceFiles.Method(window, "private void OnClosing("));
    }
}
