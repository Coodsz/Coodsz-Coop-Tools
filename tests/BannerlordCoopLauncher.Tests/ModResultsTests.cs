using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The per PC results store (mod-results.json), the second pill it gives, and the grey
// Unknown, not tested badge.
public class ModResultsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "coodsz-mod-results-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "mod-results.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static ModResult Result(string id, string version, string result, string date, string where = ModResult.ThisPc, string reason = "") =>
        new() { Id = id, Version = version, Result = result, Where = where, Date = date, Reason = reason };

    [Fact]
    public void A_missing_or_broken_file_reads_as_no_results()
    {
        Assert.Empty(ModResults.Load(FilePath));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Empty(ModResults.Load(FilePath));
        Assert.Null(ModResults.Pill("RealisticWeather", "v1.0", ModResults.Load(FilePath)));
    }

    [Fact]
    public void The_newest_result_per_mod_and_version_wins()
    {
        Assert.True(ModResults.Record(Result("RealisticWeather", "v1.0", ModResult.Crashed, "2026-10-17 20:00", reason: "Find the crashing mod blamed it."), FilePath));
        Assert.True(ModResults.Record(Result("RealisticWeather", "v1.0", ModResult.Worked, "2026-10-18 21:00"), FilePath));
        var results = ModResults.Load(FilePath);
        Assert.Single(results);

        var pill = ModResults.Pill("realisticweather", "v1.0", results)!;
        Assert.Equal(("Worked", "Worked for you", false), (pill.Kind, pill.Text, pill.Older));
        Assert.True(pill.Good);
        Assert.Contains("Seen on this PC on 2026-10-18.", pill.Tip);
        Assert.Contains("Mod version v1.0.", pill.Tip);
    }

    [Fact]
    public void A_new_mod_version_shows_the_old_result_as_an_older_version()
    {
        ModResults.Record(Result("TemplarArmor", "v1.0", ModResult.Crashed, "2026-10-10 10:00", reason: "It crashed."), FilePath);
        var pill = ModResults.Pill("TemplarArmor", "v1.1", ModResults.Load(FilePath))!;
        Assert.Equal(("Crashed", "Crashed for you, older version", true), (pill.Kind, pill.Text, pill.Older));
        Assert.False(pill.Good);
        Assert.Contains("It crashed.", pill.Tip);
        Assert.Contains("The mod on this PC is v1.1 now.", pill.Tip);

        // The result for the version on this PC wins over a newer one for another version.
        ModResults.Record(Result("TemplarArmor", "v0.9", ModResult.Worked, "2026-10-12 10:00"), FilePath);
        Assert.Equal("Crashed for you", ModResults.Pill("TemplarArmor", "v1.0", ModResults.Load(FilePath))!.Text);
        // Without a version the newest result shows as it is.
        Assert.Equal("Worked for you", ModResults.Pill("TemplarArmor", "", ModResults.Load(FilePath))!.Text);
    }

    [Fact]
    public void Server_test_results_read_as_passed_or_failed_on_your_server()
    {
        var passed = TaomModResults.FromServerTest(new ModTestResult { Id = "SwadianArmoury", Version = "v2", Result = ModTestResult.Pass, Date = "2026-10-18" });
        var failed = TaomModResults.FromServerTest(new ModTestResult { Id = "SettlementIcons", Version = "v1", Result = ModTestResult.Fail, Reason = "The server closed.", Date = "2026-10-18" });
        Assert.Equal((ModResult.Passed, ModResult.YourServer), (passed.Result, passed.Where));
        Assert.Equal("Failed the server load test. The server closed.", failed.Reason);
        ModResults.Record(new[] { passed, failed }, FilePath);
        var results = ModResults.Load(FilePath);

        var good = ModResults.Pill("SwadianArmoury", "v2", results)!;
        Assert.Equal(("Passed", "Passed on your server"), (good.Kind, good.Text));
        Assert.Contains("Tested on your server on 2026-10-18.", good.Tip);
        var bad = ModResults.Pill("SettlementIcons", "v1", results)!;
        Assert.Equal(("Failed", "Failed on your server"), (bad.Kind, bad.Text));
        Assert.False(bad.Good);
    }

    [Fact]
    public void Importing_the_server_tests_keeps_a_newer_result()
    {
        ModResults.Record(Result("SwadianArmoury", "v2", ModResult.Crashed, "2026-10-18 22:00"), FilePath);
        var older = new ModTestResult { Id = "SwadianArmoury", Version = "v2", Result = ModTestResult.Pass, Date = "2026-10-17" };
        var other = new ModTestResult { Id = "ServerSettingsLock", Version = "v1", Result = ModTestResult.Pass, Date = "2026-10-17" };
        Assert.True(ModResults.ImportServerTests(new[] { older, other }.Select(result => TaomModResults.FromServerTest(result)), FilePath));
        var results = ModResults.Load(FilePath);
        Assert.Equal("Crashed for you", ModResults.Pill("SwadianArmoury", "v2", results)!.Text);
        Assert.Equal("Passed on your server", ModResults.Pill("ServerSettingsLock", "v1", results)!.Text);
        // A second import adds nothing.
        Assert.False(ModResults.ImportServerTests(new[] { other }.Select(result => TaomModResults.FromServerTest(result)), FilePath));
    }

    [Theory]
    [InlineData(0, null, true)]
    [InlineData(-1, null, true)]
    [InlineData(null, null, true)]
    [InlineData(0, "Exception: crash report", false)]
    [InlineData(-1073741819, null, false)]
    public void A_session_only_counts_when_the_game_closed_normally(int? exitCode, string? report, bool normal) =>
        Assert.Equal(normal, ModResults.ClosedNormally(exitCode, report));

    [Fact]
    public void A_session_that_worked_marks_each_extra_mod_once()
    {
        var results = ModResults.SessionWorked(new (string, string?)[] { ("A", "v1"), ("a", "v1"), ("B", null), ("", "v1") }, "2026-10-18 12:00");
        Assert.Equal(new[] { "A", "B" }, results.Select(result => result.Id));
        Assert.All(results, result => Assert.Equal((ModResult.Worked, ModResult.ThisPc), (result.Result, result.Where)));
    }

    [Fact]
    public void Find_the_crashing_mod_results_leave_out_the_taom_core()
    {
        var passed = TaomModResults.CrashRoundPassed(new (string, string?)[] { ("TAOM", "2.0"), ("RealisticWeather", "v1") }, "2026-10-18 12:00");
        var one = Assert.Single(passed);
        Assert.Equal(("RealisticWeather", ModResult.Worked), (one.Id, one.Result));
        var found = TaomModResults.CrashFound("TemplarArmor", "v3");
        Assert.Equal((ModResult.Crashed, ModResult.ThisPc, "v3"), (found.Result, found.Where, found.Version));
        Assert.Contains("Find the crashing mod", found.Reason);
    }

    [Theory]
    [InlineData("Native", false)]
    [InlineData("SandBoxCore", false)]
    [InlineData("Coop", false)]
    [InlineData("CoopNightly", false)]
    [InlineData("DedicatedServer.Windows", false)]
    [InlineData("", false)]
    [InlineData("RealisticWeather", true)]
    public void The_unknown_badge_is_only_for_community_mods(string id, bool applies) =>
        Assert.Equal(applies, UnknownModBadge.Applies(id));

    [Theory]
    [InlineData("TAOM", false)]
    [InlineData("TAOM.CoopCompat", false)]
    [InlineData("LOTRLOME_Armory", false)]
    [InlineData("TAOM.Dependencies", false)]
    [InlineData("Native", false)]
    [InlineData("SettlementCultureDrift", true)]
    public void The_taom_tools_leave_the_core_without_a_badge(string id, bool extra) =>
        Assert.Equal(extra, TaomModResults.IsExtraMod(id));

    [Fact]
    public void A_mod_the_taom_list_does_not_know_shows_unknown_not_tested()
    {
        var list = TaomCompatList.BuiltIn();
        Assert.True(TaomModResults.ShowsUnknown(list, "SettlementIconsRedesign", "", "Settlement Icons Redesign"));
        Assert.False(TaomModResults.ShowsUnknown(list, "DismembermentPlus", null, null));
        Assert.False(TaomModResults.ShowsUnknown(list, "TAOM", null, null));
        Assert.Contains("Find the crashing mod", UnknownModBadge.Tip);
        Assert.Contains("Test mods on the server", UnknownModBadge.Tip);
    }

    [Fact]
    public void A_mod_nobody_reported_shows_unknown_not_tested_in_the_community_badge()
    {
        var db = ModCompatDatabase.Parse("""
            { "schemaVersion": 1, "mods": [
              { "id": "Mixed", "status": "Unknown", "works": 1, "broken": 1 }
            ] }
            """)!;
        var unknown = CommunityCompat.Badge("RealisticWeather", db)!;
        Assert.Equal(("Unknown", UnknownModBadge.Text), (unknown.Kind, unknown.Text));
        Assert.Contains(UnknownModBadge.CommunityTip, unknown.Tip);
        // Reports that disagree stay plain Unknown: the mod was tested.
        Assert.Equal("Unknown", CommunityCompat.Badge("Mixed", db)!.Text);
    }

    [Fact]
    public void Every_badge_place_draws_the_unknown_badge_and_the_second_pill()
    {
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root, "src" }.Concat(parts).ToArray()));

        Assert.Contains("ModPills.Mine(", Read("BannerlordCoopLauncher", "CustomModWindow.cs"));
        Assert.Contains("PickerBadge", SourceFiles.Method(SourceFiles.Window(root, "BannerlordCoopLauncher"), "private async void AddMods_Click"));
        Assert.Contains("{Binding Mine.Text}", Read("BannerlordCoopLauncher", "MainWindow.xaml"));
        Assert.Contains("{Binding Mine.Text}", Read("CoodszHost", "MainWindow.xaml"));
        Assert.Contains("{Binding Mine.Text}", Read("TaomCoopHost", "MainWindow.xaml"));
        Assert.Contains("ModResults.Pill(", Read("CoodszHost", "HostAddModsWindow.cs"));
        Assert.Contains("ModResults.Pill(", Read("TaomCoopLauncher", "ModListWindow.cs"));

        var taom = SourceFiles.Window(root, "TaomCoopLauncher");
        Assert.Contains("UnknownModBadge.Text", SourceFiles.Method(taom, "private FrameworkElement? CompatBadge"));
        Assert.Contains("ModPills.Unknown(", SourceFiles.Method(taom, "private BannerlordCoopLauncher.ModBadge? PickerBadge"));
        Assert.Contains("TaomModResults.CrashFound", SourceFiles.Method(taom, "private async void FindCrash_Click"));
        Assert.Contains("RecordSessionAsync", SourceFiles.Method(taom, "private async Task ConnectOnceAsync"));
        Assert.Contains("ModResults.Record(TaomModResults.FromServerTest", SourceFiles.Window(root, "TaomCoopHost"));
        Assert.Contains("RecordSessionAsync", SourceFiles.Method(SourceFiles.Window(root, "BannerlordCoopLauncher"), "private async Task WatchJoinAsync"));
    }

    [Fact]
    public void The_ad1259_tools_leave_out_the_unknown_badge_and_the_results()
    {
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root, "src" }.Concat(parts).ToArray()));

        // Coodsz Host (this test build has no AD1259 defined) shows them; the AD1259 build does not.
        Assert.True(CoodszHost.Services.Edition.ShowsModResults);
        Assert.Contains("public static bool ShowsModResults => !IsAD1259;", Read("CoodszHost", "Services", "Edition.cs"));
        var host = Read("CoodszHost", "MainWindow.CommunityCompat.cs");
        Assert.DoesNotContain("CommunityCompat.Badge(row.Id, CommunityCompat.Current)", host);
        Assert.DoesNotContain("CommunityCompat.Badge(row.Id, database)", host);
        Assert.Contains("row.Mine = Edition.ShowsModResults &&", host);
        Assert.Contains("if (!Edition.ShowsModResults)", SourceFiles.Method(Read("CoodszHost", "HostAddModsWindow.cs"), "private static UIElement WithBadge"));

        // Off, the community badge stays the plain Unknown it was.
        var db = ModCompatDatabase.Parse("""{ "schemaVersion": 1, "mods": [] }""")!;
        var plain = CommunityCompat.Badge("RealisticWeather", db, unknownNotTested: false)!;
        Assert.Equal("Unknown", plain.Text);
        Assert.DoesNotContain(UnknownModBadge.CommunityTip, plain.Tip);

        // The AD1259 client launcher has none of it.
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "AD1259Launcher"), "*.cs").Concat(Directory.GetFiles(Path.Combine(root, "src", "AD1259Launcher"), "*.xaml")))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ModResults", text);
            Assert.DoesNotContain("UnknownModBadge", text);
            Assert.DoesNotContain("ModPills", text);
        }

        Assert.DoesNotContain("CustomModWindow.cs", File.ReadAllText(Path.Combine(root, "src", "AD1259Launcher", "AD1259Launcher.csproj")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
