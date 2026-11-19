using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class ModLoadTestTests
{
    private const string Serving = "@DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"camp-modtest\",\"pw\":false}";
    private const string Loading = "@DS@{\"ev\":\"state\",\"phase\":\"loading\",\"save\":\"camp-modtest\",\"pw\":false}";
    private const string Fatal = "@DS@{\"ev\":\"state\",\"phase\":\"fatal\",\"save\":\"camp-modtest\",\"detail\":\"Could not load module Broken.dll\",\"pw\":false}";

    private sealed class FakeClock : IModTestClock
    {
        public DateTime UtcNow { get; private set; } = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        public Action? OnTick { get; set; }

        public Task Delay(TimeSpan time, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow += time;
            OnTick?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRun : IModTestRun
    {
        public string Log { get; set; } = "";
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }

        public Task StopAsync()
        {
            Stopped = true;
            HasExited = true;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }

    // Each start plays one script: what the log says after each poll.
    private sealed class FakeServer(FakeClock clock, Dictionary<string, Action<FakeRun, int>> scripts) : IModTestServer
    {
        public List<(TaomSetup Setup, FakeRun Run)> Starts { get; } = new();

        public Task<IModTestRun> StartAsync(TaomSetup setup, CancellationToken cancellationToken)
        {
            var mod = setup.Extras.Last().ModuleId;
            if (mod == "WontStart")
                throw new InvalidOperationException("engine\\dotnet\\dotnet.exe is missing from the server folder.");
            var run = new FakeRun { Log = "boot\n" };
            Starts.Add((setup, run));
            var tick = 0;
            clock.OnTick = () => scripts[mod](run, ++tick);
            return Task.FromResult<IModTestRun>(run);
        }
    }

    private static ModTestCandidate Mod(string id) => new(id, id + " name", "1234", "v1.0.0", Array.Empty<TaomExtraMod>());

    [Fact]
    public async Task Each_mod_runs_alone_and_passes_on_serving_or_fails_with_the_reason()
    {
        var clock = new FakeClock();
        var scripts = new Dictionary<string, Action<FakeRun, int>>
        {
            ["Good"] = (run, tick) => run.Log += tick == 3 ? Serving + "\n" : Loading + "\n",
            ["Fatal"] = (run, tick) => run.Log += tick == 2 ? Fatal + "\n" : "line\n",
            ["Exits"] = (run, tick) => { if (tick == 2) { run.HasExited = true; run.ExitCode = 3; } },
            ["Slow"] = (run, tick) => run.Log += "still loading\n",
            ["Quiet"] = (_, _) => { }
        };
        var server = new FakeServer(clock, scripts);
        var options = new ModTestOptions(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));
        var recorded = new List<ModTestResult>();
        var mods = new[] { Mod("Good"), Mod("Fatal"), Mod("Exits"), Mod("WontStart"), Mod("Slow"), Mod("Quiet") };
        var results = await ModLoadTest.RunAllAsync(mods, mod => ModLoadTest.SetupFor(mod), server, options, clock, null, recorded.Add, CancellationToken.None);

        Assert.Equal(6, results.Count);
        Assert.Equal(results, recorded);
        Assert.True(results[0].Passed);
        Assert.Equal("v1.0.0", results[0].Version);
        Assert.Equal("2026-10-18", results[0].Date);
        Assert.False(results[1].Passed);
        Assert.Contains("Could not load module Broken.dll", results[1].Reason);
        Assert.False(results[2].Passed);
        Assert.Equal("The server closed with exit code 3 before it was serving.", results[2].Reason);
        Assert.Contains("did not start", results[3].Reason);
        Assert.Equal("The server was not serving after 30 minutes.", results[4].Reason);
        Assert.Equal("The server wrote nothing for 10 minutes before it was serving.", results[5].Reason);

        // One server at a time, the TAOM core plus just that mod, and each one stopped.
        Assert.Equal(5, server.Starts.Count);
        Assert.All(server.Starts, start => Assert.Single(start.Setup.Extras));
        Assert.All(server.Starts, start => Assert.True(start.Run.Stopped && start.Run.Disposed));
        Assert.Equal(TaomStack.ServerModules.Length + 1, TaomLoadOrder.ServerOrder(server.Starts[0].Setup).Count);
    }

    [Fact]
    public async Task The_map_cache_build_does_not_count_and_stop_ends_the_run()
    {
        var clock = new FakeClock();
        var scripts = new Dictionary<string, Action<FakeRun, int>>
        {
            ["Build"] = (run, tick) => run.Log += tick < 100
                ? "Settlement to settlement cache creation index " + tick + ", total count: 1002\n"
                : Serving + "\n",
            ["Next"] = (run, _) => run.Log += "x\n"
        };
        var server = new FakeServer(clock, scripts);
        var options = new ModTestOptions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1));
        var built = await ModLoadTest.RunAllAsync([Mod("Build")], mod => ModLoadTest.SetupFor(mod), server, options, clock, null, null, CancellationToken.None);
        Assert.True(Assert.Single(built).Passed);

        using var stop = new CancellationTokenSource();
        var progress = new List<string>();
        clock.OnTick = null;
        var stopped = await ModLoadTest.RunAllAsync([Mod("Next"), Mod("Good")], mod => ModLoadTest.SetupFor(mod), server, options, new StopClock(clock, stop), new Progress<ModTestProgress>(step => progress.Add(step.Status)), null, stop.Token);
        Assert.Empty(stopped);
        Assert.True(server.Starts[^1].Run.Stopped);
        Assert.Equal(2, server.Starts.Count);
    }

    // Presses Stop on the second poll.
    private sealed class StopClock(FakeClock inner, CancellationTokenSource stop) : IModTestClock
    {
        private int _ticks;

        public DateTime UtcNow => inner.UtcNow;

        public Task Delay(TimeSpan time, CancellationToken cancellationToken)
        {
            if (++_ticks == 2)
                stop.Cancel();
            return inner.Delay(time, cancellationToken);
        }
    }

    [Fact]
    public void The_test_runs_on_a_copy_and_never_writes_the_real_save()
    {
        var saves = Path.Combine(Path.GetTempPath(), "modtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        try
        {
            File.WriteAllText(Path.Combine(saves, "camp.sav"), "real");
            File.WriteAllText(Path.Combine(saves, "camp.json"), "{}");
            var before = File.GetLastWriteTimeUtc(Path.Combine(saves, "camp.sav"));
            Assert.Equal("camp-modtest", ModLoadTest.CopyTestSave(saves, "camp"));
            Assert.Equal("real", File.ReadAllText(Path.Combine(saves, "camp-modtest.sav")));
            Assert.True(File.Exists(Path.Combine(saves, "camp-modtest.json")));
            Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(saves, "camp.sav")));
            Assert.Equal("camp-modtest", ModLoadTest.TestSaveName("camp-modtest"));
            Assert.Throws<InvalidOperationException>(() => ModLoadTest.CopyTestSave(saves, "camp-modtest"));
            Assert.Throws<InvalidOperationException>(() => ModLoadTest.CopyTestSave(saves, "missing"));
            Assert.Throws<InvalidOperationException>(() => ModLoadTest.TestSaveName(" "));

            // Afterwards only the test copy and what the server made of it go.
            File.WriteAllText(Path.Combine(saves, "camp-modtest.backup1.sav"), "x");
            File.WriteAllText(Path.Combine(saves, "camp.backup1.sav"), "x");
            Assert.Equal(0, ModLoadTest.RemoveTestSave(saves, "camp"));
            Assert.Equal(3, ModLoadTest.RemoveTestSave(saves, "camp-modtest"));
            Assert.Equal(new[] { "camp.backup1.sav", "camp.json", "camp.sav" }, Directory.GetFiles(saves).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(saves, true);
        }
    }

    [Fact]
    public void Default_mods_are_listed_workshop_mods_not_tested_in_coop_with_what_they_need()
    {
        var root = Path.Combine(Path.GetTempPath(), "modtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            FoundMod Make(string id, string workshop, string needs = "")
            {
                var directory = Path.Combine(root, id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SubModule.xml"),
                    "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/><Version value=\"v2.1\"/><DependedModules>"
                    + "<DependedModule Id=\"Native\"/><DependedModule Id=\"Bannerlord.Harmony\"/>" + needs + "</DependedModules></Module>");
                return new FoundMod(id, id, workshop, directory, "Workshop");
            }

            var onPc = new List<FoundMod>
            {
                Make("RBM", "2859251492", "<DependedModule Id=\"RTSCamera\"/>"),
                Make("RTSCamera", "3747725551"),
                Make("Bannerlord.Diplomacy", "2881380744"),
                Make("NotListed", "999999"),
                Make("BannerFix", "0")
            };
            var list = TaomCompatList.BuiltIn();
            var picked = ModLoadTest.DefaultCandidates(list, onPc);
            Assert.Equal(new[] { "Bannerlord.Diplomacy", "RBM" }, picked.Select(mod => mod.ModuleId));
            Assert.True(ModLoadTest.KnownBroken(list, picked[0]));
            var rbm = picked[1];
            Assert.Equal("v2.1", rbm.Version);
            Assert.Equal("RTSCamera", Assert.Single(rbm.Needs).ModuleId);
            var setup = ModLoadTest.SetupFor(rbm);
            Assert.Equal(new[] { "RTSCamera", "RBM" }, setup.Extras.Select(mod => mod.ModuleId));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Default_mods_also_take_listed_modules_folder_mods_with_the_mods_they_patch()
    {
        var root = Path.Combine(Path.GetTempPath(), "modtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string ModulesFolder = "From Nexus or ModDB  —  auto detected in your module folder";
            FoundMod Make(string id, string workshop, string source, string needs = "", string? name = null)
            {
                var directory = Path.Combine(root, id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SubModule.xml"),
                    "<Module><Id value=\"" + id + "\"/><Name value=\"" + (name ?? id) + "\"/><Version value=\"v1.0.8\"/><DependedModules>"
                    + "<DependedModule Id=\"Native\"/><DependedModule Id=\"TAOM\"/>" + needs + "</DependedModules></Module>");
                return new FoundMod(name ?? id, id, workshop, directory, source);
            }

            var onPc = new List<FoundMod>
            {
                Make("RBM", "2859251492", "Workshop"),
                Make("XorberaxLegacy", "2875138158", "Workshop"),
                Make("TAOM_Standalone_Compat_RBM", "0", ModulesFolder, "<DependedModule Id=\"RBM\" Optional=\"false\"/>", "RBM-TAOM-2.0.29.5"),
                Make("TAOM_XorberaxCompatibility", "0", ModulesFolder, "<DependedModule Id=\"XorberaxLegacy\"/>", "Xorberax-TAOM-2.0.29.5"),
                Make("TAOM_PlayerVoices", "0", "From Nexus or ModDB", "<DependedModule Id=\"TAOM_CompanionVoices\" Optional=\"true\"/>", "TAOM Player Voices"),
                // Not listed by module id: a Modules folder mod is not matched by its name.
                Make("SomeBannerEditor", "0", ModulesFolder, name: "Banner Editor"),
                Make("NotListedLocal", "0", ModulesFolder),
                // Coodsz tested it in Coop, and the TAOM core is never a test.
                Make("BannerFix", "0", ModulesFolder),
                Make("TAOM_Map", "0", ModulesFolder),
                // A Workshop copy without a Workshop id is still skipped.
                Make("ArmiesOfMiddleEarth", "0", "Workshop")
            };
            var list = TaomCompatList.BuiltIn();
            var picked = ModLoadTest.DefaultCandidates(list, onPc);
            // Sorted by name: RBM, RBM-TAOM-2.0.29.5, TAOM Player Voices, Xorberax-TAOM-2.0.29.5.
            Assert.Equal(new[] { "RBM", "TAOM_Standalone_Compat_RBM", "TAOM_PlayerVoices", "TAOM_XorberaxCompatibility" }, picked.Select(mod => mod.ModuleId));

            var patch = picked.Single(mod => mod.ModuleId == "TAOM_Standalone_Compat_RBM");
            Assert.Equal("RBM", Assert.Single(patch.Needs).ModuleId);
            Assert.Equal(new[] { "RBM", "TAOM_Standalone_Compat_RBM" }, ModLoadTest.SetupFor(patch).Extras.Select(mod => mod.ModuleId));
            Assert.Empty(picked.Single(mod => mod.ModuleId == "TAOM_PlayerVoices").Needs);

            var xorberax = picked.Single(mod => mod.ModuleId == "TAOM_XorberaxCompatibility");
            Assert.Equal("XorberaxLegacy", Assert.Single(xorberax.Needs).ModuleId);
            var setup = ModLoadTest.SetupFor(xorberax, new Dictionary<string, ModuleManifest>());
            Assert.Equal(new[] { "XorberaxLegacy", "TAOM_XorberaxCompatibility" }, setup.Extras.Select(mod => mod.ModuleId));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Results_keep_one_per_version_and_passes_go_into_the_list_as_server_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), "modtest-" + Guid.NewGuid().ToString("N"), ModLoadTest.ResultsFileName);
        try
        {
            ModLoadTest.Record(path, new ModTestResult { Id = "RBM", Name = "Realistic Battle Mod", Version = "v4.5.4", Result = "fail", Reason = "x", Date = "2026-10-17" });
            ModLoadTest.Record(path, new ModTestResult { Id = "RBM", Name = "Realistic Battle Mod", WorkshopId = "2859251492", Version = "v4.5.4", Result = "pass", Date = "2026-10-18" });
            ModLoadTest.Record(path, new ModTestResult { Id = "Diplo", Name = "Diplomacy", WorkshopId = "2881380744", Version = "v1.5.4", Result = "fail", Reason = "Loading forever", Date = "2026-10-18" });
            ModLoadTest.Record(path, new ModTestResult { Id = "BannerEditor", Name = "Banner Editor", Version = "v1.0.7", Result = "pass", Date = "2026-10-18" });
            ModLoadTest.Record(path, new ModTestResult { Id = "BrandNew", Name = "Brand New", WorkshopId = "4000000001", Version = "v1", Result = "pass", Date = "2026-10-18" });
            ModLoadTest.Record(path, new ModTestResult { Id = "RTSCamera", Name = "RTS Camera", Version = "v5.4.16", Result = "pass", Date = "2026-10-18" });
            var results = ModLoadTest.LoadResults(path);
            Assert.Equal(5, results.Count);
            Assert.True(results.Single(result => result.Id == "RBM").Passed);

            var current = TaomCompatList.BuiltIn();
            var merged = ModLoadTest.MergePassed(current, results, "2026-10-19");
            Assert.Equal("2026-10-19", merged.Updated);
            Assert.Equal(current.Mods.Count + 1, merged.Mods.Count);
            var rbm = merged.Find("RBM")!;
            Assert.Equal(CompatSource.ServerLoads, rbm.Source);
            Assert.Equal(CompatTag.Risky, rbm.Kind);
            Assert.EndsWith("Passed the automatic server load test on 2026-10-18 with version v4.5.4. Joining was not tested.", rbm.Note);
            var editor = merged.Find("BannerEditor")!;
            Assert.Equal("Banner Editor", editor.Name);
            Assert.Equal(CompatTag.Loads, editor.Kind);
            Assert.Equal(CompatGroup.LoadsOnServer, TaomModList.GroupOf(editor));
            Assert.Equal(CompatTag.Loads, merged.Find("BrandNew")!.Kind);
            Assert.Equal(CompatSource.CoodszCoop, merged.Find("RTSCamera")!.Source);
            Assert.Equal(CompatSource.TaomDiscord, merged.Find("Bannerlord.Diplomacy")!.Source);
            // The original list is not changed.
            Assert.Equal(TaomCompatList.Parse(ModLoadTest.Serialize(TaomCompatList.BuiltIn())).Mods.Count, current.Mods.Count);
            Assert.DoesNotContain(current.Mods, entry => entry.Id == "BrandNew");

            // A second export does not stack the note, and the file reads back the same.
            var again = ModLoadTest.MergePassed(merged, results, "2026-10-19");
            Assert.Equal(rbm.Note, again.Find("RBM")!.Note);
            var text = ModLoadTest.Serialize(again);
            Assert.Contains("\r\n    { \"id\": \"RBM\"", text);
            var back = TaomCompatList.Parse(text);
            Assert.Equal(again.Mods.Count, back.Mods.Count);
            Assert.Equal(again.Find("XorberaxLegacy")!.ClientOnlyOnServer, back.Find("XorberaxLegacy")!.ClientOnlyOnServer);
            Assert.All(back.Mods, entry => Assert.DoesNotContain(":", entry.Note));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void The_built_in_list_written_out_reads_back_the_same()
    {
        var list = TaomCompatList.BuiltIn();
        var back = TaomCompatList.Parse(ModLoadTest.Serialize(list));
        Assert.Equal(list.Mods.Count, back.Mods.Count);
        for (var i = 0; i < list.Mods.Count; i++)
        {
            var a = list.Mods[i];
            var b = back.Mods[i];
            Assert.Equal((a.Id, a.Name, a.WorkshopId, a.NexusId, a.Tag, a.Class, a.ClientOnlyOnServer, a.HasServerBuild, a.Source, a.Checked, a.Note),
                (b.Id, b.Name, b.WorkshopId, b.NexusId, b.Tag, b.Class, b.ClientOnlyOnServer, b.HasServerBuild, b.Source, b.Checked, b.Note));
        }
    }

    [Fact]
    public void The_server_tool_tests_on_the_copy_and_puts_the_config_back()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        var root = directory!.FullName;
        var xaml = File.ReadAllText(Path.Combine(root, "src", "TaomCoopHost", "MainWindow.xaml"));
        Assert.Contains("Click=\"TestMods_Click\"", xaml);
        var host = SourceFiles.Window(root, "TaomCoopHost");

        var click = SourceFiles.Method(host, "private async void TestMods_Click(");
        Assert.True(click.IndexOf("ServerIsRunning()", StringComparison.Ordinal) < click.IndexOf("ModTestWindow.Show", StringComparison.Ordinal));
        Assert.Contains("ModLoadTest.DefaultCandidates(_compat, HostMods(rescan: true)", click);

        var run = SourceFiles.Method(host, "private async Task<string> RunModTestsAsync(");
        Assert.Contains("AskServerTerms(server)", run);
        // The server is pointed at the copy only, and the real config and saves are put back or left alone.
        Assert.True(run.IndexOf("ModLoadTest.CopyTestSave(", StringComparison.Ordinal) < run.IndexOf("WriteConfigFile(configPath, testName", StringComparison.Ordinal));
        var final = run[run.IndexOf("finally", StringComparison.Ordinal)..];
        Assert.Contains("File.WriteAllText(configPath, original)", final);
        Assert.Contains("ModLoadTest.RemoveTestSave(ServerSavesDirectory(), testName)", final);
        Assert.Contains("ModLoadTest.Record(ModTestResultsPath", run);

        // The same Coodsz TAOM server path as Start server: copy, unblock and fix, then the engine.
        var start = SourceFiles.Method(host, "public async Task<IModTestRun> StartAsync(");
        Assert.Contains("TaomServerPath.Plan(setup", start);
        Assert.Contains("TaomServerPath.Stage(serverRoot, plan)", start);
        Assert.Contains("ServerModuleGuard.RejectClientXorberax(serverRoot)", start);
        Assert.Contains("TaomServerPath.Launch(serverRoot", start);
        Assert.Contains("ServerCommand.Stop()", SourceFiles.Method(host, "public async Task StopAsync("));
    }
}
