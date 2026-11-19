using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Coodsz Host's "Test mods on the server": the base list, the candidates, the test module list,
// the throwaway campaign, the shared runner with Coodsz Host's flavor, the AD1259 gate and the
// community list export.
public class HostModTestTests
{
    private const string Serving = "@DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"modtest-1\",\"pw\":false}";

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "hostmodtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ModuleRow Row(string id, params string[] needs) => new() { Id = id, Name = id, Version = "v1", Dependencies = needs.ToList() };

    private static ModuleManifest Manifest(string id, params string[] needs) =>
        new(id, id, "v2.0", needs.Select(need => new ModuleLink(need, null, false)).ToList(), [], [], []);

    [Fact]
    public void The_AD1259_server_tool_does_not_get_test_mods()
    {
        Assert.False(HostModTest.Available(ad1259: true));
        Assert.True(HostModTest.Available(ad1259: false));
        // The test build is the Coodsz Host build (no AD1259 constant).
        Assert.True(HostModTest.Offered);

        // The button row is shown only where it is offered, and the click does nothing elsewhere.
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.ModTest.cs"));
        Assert.Contains("TestModsButton.Visibility = HostModTest.Offered ? Visibility.Visible : Visibility.Collapsed", code);
        Assert.Matches(new Regex(@"TestMods_Click[^{]*\{\s*if \(!HostModTest\.Offered\)\s*return;"), code);
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"TestModsButton\"", xaml);
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml.cs"));
        Assert.Contains("ShowModTestButton();", window);
        // AD1259Host.csproj builds that same source with AD1259 defined, which turns Offered off.
        var ad1259 = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AD1259Host", "AD1259Host.csproj"));
        Assert.Contains("<DefineConstants>$(DefineConstants);AD1259</DefineConstants>", ad1259);
        Assert.Contains("$(HostSource)**\\*.cs", ad1259);
    }

    [Fact]
    public void Base_is_the_core_of_the_working_list_in_its_order()
    {
        var order = HostModTest.BaseOrder(StableCoop.Order, ["Coop", "Native"]);
        Assert.Equal(HostModTest.CoreIds.Length, order.Count);
        Assert.Contains("Coop", order);
        Assert.Contains("DedicatedServer.Windows", order);
        Assert.DoesNotContain("MoreNationsRemastered", order);
        Assert.DoesNotContain("CoopNightly", order);
        // The working list order is kept: Harmony before Native, DedicatedServer.Windows right after it.
        Assert.True(order.ToList().IndexOf("Bannerlord.Harmony") < order.ToList().IndexOf("Native"));
        Assert.Equal(order.ToList().IndexOf("Native") + 1, order.ToList().IndexOf("DedicatedServer.Windows"));

        var nightly = HostModTest.BaseOrder(StableCoop.Order, ["CoopNightly", "Native"]);
        Assert.Contains("CoopNightly", nightly);
        Assert.DoesNotContain("Coop", nightly);
    }

    [Fact]
    public void The_throwaway_campaign_has_a_name_the_start_script_takes_and_is_deleted_alone()
    {
        var name = HostModTest.NewSaveName(new Random(7));
        Assert.Matches("^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$", name);
        Assert.StartsWith(HostModTest.SavePrefix, name);
        Assert.True(HostModTest.IsTestSave(name));
        Assert.False(HostModTest.IsTestSave("my campaign"));
        Assert.False(HostModTest.IsTestSave("modtest-"));

        var saves = Temp();
        foreach (var file in new[] { name + ".sav", name + ".json", name + ".backup1.sav", name + ".launcher.json", "real.sav", name + "x.sav" })
            File.WriteAllText(Path.Combine(saves, file), "x");
        Assert.Equal(4, HostModTest.RemoveTestSave(saves, name));
        Assert.True(File.Exists(Path.Combine(saves, "real.sav")));
        Assert.True(File.Exists(Path.Combine(saves, name + "x.sav")));
        // A real save is never deleted.
        Assert.Equal(0, HostModTest.RemoveTestSave(saves, "real"));
        Assert.True(File.Exists(Path.Combine(saves, "real.sav")));
    }

    [Fact]
    public void Candidates_are_mods_outside_the_working_list_with_what_they_need()
    {
        var pc = new List<FoundServerMod>
        {
            new("Diplomacy", "Bannerlord.Diplomacy", @"C:\Steam\steamapps\workshop\content\261550\2859251492\Diplomacy", "Workshop"),
            new("Patch", "DiplomacyPatch", @"C:\Game\Modules\DiplomacyPatch", "From Nexus or ModDB"),
            new("Harmony", "Bannerlord.Harmony", @"C:\Steam\steamapps\workshop\content\261550\2859188632\Harmony", "Workshop"),
            new("Alive Scenes", "AliveScenes", @"C:\Game\Modules\AliveScenes", "From Nexus or ModDB"),
            new("Server host", "DedicatedServer.Something", @"C:\Game\Modules\DS", "From Nexus or ModDB"),
            new("Story", "StoryMode", @"C:\Game\Modules\StoryMode", "From Nexus or ModDB")
        };
        var manifests = new Dictionary<string, ModuleManifest>
        {
            [@"C:\Steam\steamapps\workshop\content\261550\2859251492\Diplomacy"] = Manifest("Bannerlord.Diplomacy", "Bannerlord.Harmony", "Native", "StoryMode"),
            [@"C:\Game\Modules\DiplomacyPatch"] = Manifest("DiplomacyPatch", "Bannerlord.Diplomacy", "ServerOnlyLib"),
        };
        var server = new List<ModuleRow> { Row("ServerOnlyLib", "Native") };
        var mods = HostModTest.Candidates(pc, StableCoop.Order, server, dir => manifests.TryGetValue(dir, out var m) ? m : null);

        Assert.Equal(["Bannerlord.Diplomacy", "DiplomacyPatch"], mods.Select(mod => mod.ModuleId).OrderBy(id => id).ToArray());
        var diplomacy = mods.Single(mod => mod.ModuleId == "Bannerlord.Diplomacy");
        Assert.Equal("2859251492", diplomacy.WorkshopId);
        Assert.Equal("v2.0", diplomacy.Version);
        // Harmony is core and StoryMode an official module, so the test brings nothing else.
        Assert.Empty(diplomacy.Needs);
        var patch = mods.Single(mod => mod.ModuleId == "DiplomacyPatch");
        Assert.Equal("0", patch.WorkshopId);
        Assert.Equal(["Bannerlord.Diplomacy", "ServerOnlyLib"], patch.Needs.Select(need => need.ModuleId).ToArray());
    }

    [Fact]
    public void Broken_and_total_overhaul_mods_start_unticked()
    {
        var list = ModCompatDatabase.Parse("{ \"mods\": [ { \"id\": \"BadMod\", \"status\": \"Broken\" }, { \"id\": \"GoodMod\", \"status\": \"Works\" } ] }")!;
        ModTestCandidate Mod(string id) => new(id, id, "0", "", []);
        Assert.True(HostModTest.LeaveUnticked(list, Mod("BadMod")));
        Assert.False(HostModTest.LeaveUnticked(list, Mod("GoodMod")));
        Assert.False(HostModTest.LeaveUnticked(list, Mod("NewMod")));
    }

    [Fact]
    public void The_test_list_is_the_core_then_the_mod_after_what_it_needs_and_the_rest_off()
    {
        var installed = new List<ModuleRow>
        {
            Row("Native"), Row("Coop", "Native"), Row("Sandbox"), Row("ZMod", "AlphaLib"), Row("AlphaLib", "Native"), Row("Other"), Row("SandBoxCore"), Row("DedicatedServer.Windows")
        };
        var mod = new ModTestCandidate("ZMod", "Z", "0", "", [new TaomExtraMod { ModuleId = "AlphaLib", Name = "Alpha" }]);
        var baseOrder = new[] { "Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop" };
        var (rows, missing) = HostModTest.TestRows(installed, baseOrder, mod);

        Assert.Empty(missing);
        var on = rows.Where(row => row.Enabled).Select(row => row.Id).ToList();
        Assert.Equal(["Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop", "AlphaLib", "ZMod"], on);
        Assert.Equal(["Other"], rows.Where(row => !row.Enabled).Select(row => row.Id).ToArray());
        Assert.Equal(installed.Count, rows.Count);

        var (_, gone) = HostModTest.TestRows(installed.Where(row => row.Id != "Coop").ToList(), baseOrder, mod);
        Assert.Equal(["Coop"], gone);
    }

    private sealed class Clock : IModTestClock
    {
        public DateTime UtcNow { get; private set; } = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        public Action? OnTick { get; set; }

        public Task Delay(TimeSpan time, CancellationToken cancellationToken)
        {
            UtcNow += time;
            OnTick?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class Run : IModTestRun
    {
        public string Log { get; set; } = "";
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class Server(Clock clock, Action<Run, int> script) : IModTestServer
    {
        public Task<IModTestRun> StartAsync(TaomSetup setup, CancellationToken cancellationToken)
        {
            var run = new Run();
            var tick = 0;
            clock.OnTick = () => script(run, ++tick);
            return Task.FromResult<IModTestRun>(run);
        }
    }

    [Fact]
    public async Task The_shared_runner_uses_the_Coodsz_Host_flavor()
    {
        var mod = new ModTestCandidate("Bannerlord.Diplomacy", "Diplomacy", "2859251492", "v1.2", []);
        var flavor = HostModTest.Flavor("v0.1.6", StableCoop.Order, () => mod);
        var lines = new List<string>();
        var clock = new Clock();
        var options = new ModTestOptions(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(2));

        var passed = await ModLoadTest.RunOneAsync(mod, new TaomSetup(), new Server(clock, (run, tick) => run.Log += tick == 2 ? Serving + "\n" : "loading\n"),
            options, clock, lines.Add, CancellationToken.None, flavor);
        Assert.True(passed.Passed);
        Assert.Equal("Coop v0.1.6", passed.Base);
        Assert.Equal("", passed.Taom);
        Assert.Equal("Starting the server with the working list core and Diplomacy.", lines[0]);

        var failed = await ModLoadTest.RunOneAsync(mod, new TaomSetup(), new Server(clock, (run, tick) => { if (tick == 2) { run.HasExited = true; run.ExitCode = 7; } }),
            options, clock, null, CancellationToken.None, flavor);
        Assert.False(failed.Passed);
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }

    [Fact]
    public void Judge_takes_the_tool_s_own_exit_reason()
    {
        Assert.Equal((false, "Diplomacy broke it."), ModLoadTest.Judge("boot\n", exited: true, exitCode: 1, (_, _) => "Diplomacy broke it."));
        Assert.Equal((false, "The server closed with exit code 1 before it was serving."), ModLoadTest.Judge("boot\n", exited: true, exitCode: 1, (_, _) => null));
        Assert.Equal(true, ModLoadTest.Judge(Serving + "\n", exited: false, exitCode: null, (_, _) => "never")?.Passed);
        Assert.Null(ModLoadTest.Judge("boot\n", exited: false, exitCode: null, (_, _) => "never"));
    }

    [Fact]
    public void Coodsz_Host_results_file_is_named_for_it()
    {
        var path = Path.Combine(Temp(), HostModTest.ResultsFileName);
        ModLoadTest.Record(path, new ModTestResult { Id = "A", Result = ModTestResult.Pass, Date = "2026-10-18", Base = "Coop v0.1.6" }, HostModTest.ResultsKind);
        var text = File.ReadAllText(path);
        Assert.Contains("\"kind\": \"CoodszHostModTest\"", text);
        Assert.Contains("\"base\": \"Coop v0.1.6\"", text);
        Assert.Equal("Coop v0.1.6", Assert.Single(ModLoadTest.LoadResults(path)).Base);

        // The TAOM file stays as it was: its kind and no base field.
        var taom = Path.Combine(Temp(), ModLoadTest.ResultsFileName);
        ModLoadTest.Record(taom, new ModTestResult { Id = "A", Result = ModTestResult.Pass });
        Assert.Contains("\"kind\": \"CoodszTaomModTest\"", File.ReadAllText(taom));
        Assert.DoesNotContain("\"base\"", File.ReadAllText(taom));
    }

    private const string List =
        "{\r\n  // Coodsz community mod compatibility list.\r\n  \"mods\": [\r\n"
        + "    { \"id\": \"Adoption\", \"name\": \"Adoption\", \"status\": \"Unknown\", \"workshop\": \"2893279424\", \"category\": \"Utility\", \"works\": 0, \"broken\": 0, \"idGuess\": true },\r\n"
        + "    { \"id\": \"AliveScenes\", \"name\": \"Alive Scenes\", \"status\": \"Works\", \"coop\": \"v0.1.6\", \"note\": \"Hand kept.\", \"nexus\": 8138, \"category\": \"Gameplay\", \"works\": 0, \"broken\": 0 },\r\n"
        + "    { \"id\": \"Zed\", \"name\": \"Zed\", \"status\": \"Broken\", \"note\": \"Breaks joining.\", \"works\": 1, \"broken\": 5 }\r\n"
        + "  ]\r\n}\r\n";

    [Fact]
    public void Passed_mods_are_written_into_a_copy_of_the_community_list()
    {
        var passes = new[]
        {
            new ServerLoadPass("AdoptionMod", "Adoption", "2893279424", "v1.0", "2026-10-17"),
            new ServerLoadPass("AliveScenes", "Alive Scenes", "", "v2", "2026-10-17"),
            new ServerLoadPass("Zed", "Zed", "", "v3", "2026-10-16"),
            new ServerLoadPass("Bannerlord.Diplomacy", "Diplomacy", "2859251492", "v1.2", "2026-10-18")
        };
        var text = CompatListServerLoads.Merge(List, passes);

        Assert.StartsWith("{\r\n  // Coodsz community mod compatibility list.\r\n  \"mods\": [\r\n", text);
        Assert.EndsWith("  ]\r\n}\r\n", text);
        var lines = text.Split("\r\n").Where(line => line.StartsWith("    {", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, lines.Count);
        // Sorted by name, one entry per line, the tally workflow's key order first.
        Assert.StartsWith("    { \"id\": \"AdoptionMod\", \"name\": \"Adoption\", \"status\": \"Unknown\", \"note\": \"Passed the automatic server load test on 2026-10-17 with version v1.0. Joining was not tested.\", \"workshop\": \"2893279424\"", lines[0]);
        Assert.DoesNotContain("idGuess", lines[0]);
        Assert.EndsWith("\"source\": \"server-loads\", \"checked\": \"2026-10-17\", \"version\": \"v1.0\" },", lines[0]);
        // A hand kept verdict is left alone.
        Assert.Equal("    { \"id\": \"AliveScenes\", \"name\": \"Alive Scenes\", \"status\": \"Works\", \"coop\": \"v0.1.6\", \"note\": \"Hand kept.\", \"nexus\": 8138, \"category\": \"Gameplay\", \"works\": 0, \"broken\": 0 },", lines[1]);
        // A new mod comes in as Unknown.
        Assert.Contains("\"id\": \"Bannerlord.Diplomacy\", \"name\": \"Diplomacy\", \"status\": \"Unknown\"", lines[2]);
        Assert.Contains("\"workshop\": \"2859251492\", \"works\": 0, \"broken\": 0, \"source\": \"server-loads\"", lines[2]);
        // Loading on the server does not change the status or the counts.
        Assert.Contains("\"status\": \"Broken\", \"note\": \"Breaks joining. Passed the automatic server load test on 2026-10-16 with version v3. Joining was not tested.\", \"works\": 1, \"broken\": 5", lines[3]);

        var database = ModCompatDatabase.Parse(text)!;
        Assert.Equal(4, database.Count);
        Assert.Equal(CommunityStatus.Unknown, database.Find("AdoptionMod")!.Status);

        // A newer pass replaces the old load test sentence instead of adding a second one.
        var again = CompatListServerLoads.Merge(text, [new ServerLoadPass("Zed", "Zed", "", "v4", "2026-10-18")]);
        var zed = again.Split("\r\n").Single(line => line.Contains("\"id\": \"Zed\""));
        Assert.Contains("\"note\": \"Breaks joining. Passed the automatic server load test on 2026-10-18 with version v4. Joining was not tested.\"", zed);
        Assert.Contains("\"checked\": \"2026-10-18\", \"version\": \"v4\"", zed);
    }

    [Fact]
    public void The_export_starts_from_the_built_in_list_without_a_download()
    {
        var text = CompatListServerLoads.CurrentText(Path.Combine(Temp(), "missing.json"));
        Assert.True(ModCompatDatabase.Parse(text)!.Count > 100);
        var merged = CompatListServerLoads.Merge(text, [new ServerLoadPass("Test.Mod.Id", "Test Mod", "", "v1", "2026-10-18")]);
        Assert.Equal(ModCompatDatabase.Parse(text)!.Count + 1, ModCompatDatabase.Parse(merged)!.Count);
        Assert.Contains("// Coodsz community mod compatibility list.", merged);
    }

    [Fact]
    public void Passes_are_the_passed_results_only()
    {
        var passes = HostModTest.Passes([
            new ModTestResult { Id = "A", Name = "A", Result = ModTestResult.Pass, Version = "1", Date = "2026-10-18" },
            new ModTestResult { Id = "B", Result = ModTestResult.Fail }
        ]);
        Assert.Equal(new ServerLoadPass("A", "A", "", "1", "2026-10-18"), Assert.Single(passes));
    }
}
