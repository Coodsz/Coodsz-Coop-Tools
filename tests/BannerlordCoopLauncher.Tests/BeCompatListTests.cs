using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BurningEmpires.Core;
using TaomCoop.Core;
using Xunit.Abstractions;

namespace BannerlordCoopLauncher.Tests;

public class BeCompatListTests
{
    private readonly ITestOutputHelper _output;

    public BeCompatListTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_built_in_list_knows_burning_empires_and_marks_every_other_overhaul_broken()
    {
        var list = BeCompatList.BuiltIn();
        Assert.Equal(BeCompatList.Kind, list.FileKind);
        Assert.Equal(BeStack.BeVersionText, list.Be);
        Assert.Equal(CompatTag.Safe, list.Find("BurningEmpires")!.Kind);
        Assert.Equal(BeStack.BeNexusId, list.Find("BurningEmpires")!.NexusId);
        Assert.NotNull(list.Find(BeStack.ElephantsModuleId));
        Assert.NotNull(list.Find(BeStack.CompatModuleId));
        foreach (var other in new[] { "TAOM", "TAOM.Dependencies", "AD1259", "Europe1100", "TOR_Core", "BannerKings" })
            Assert.Equal(CompatTag.Broken, list.Find(other)!.Kind);
        Assert.Equal(CompatTag.Broken, list.Find("NotAnId", "3496296180")!.Kind);
        Assert.Equal(CompatTag.Broken, list.Find(null, null, "Realm of Thrones")!.Kind);
        Assert.Null(list.Find("Burning Empires", null, "Burning Empires"));
        Assert.All(list.Mods, entry => Assert.NotEqual(CompatTag.Unknown, entry.Kind));
        Assert.All(list.Mods, entry => Assert.DoesNotContain(":", entry.Note));
    }

    [Fact]
    public void The_list_reads_only_its_own_kind_and_has_no_taom_places()
    {
        Assert.Throws<InvalidOperationException>(() => BeCompatList.Parse("{ \"kind\": \"CoodszTaomCompat\", \"schemaVersion\": 2, \"mods\": [] }"));
        Assert.Throws<InvalidOperationException>(() => TaomCompatList.Parse("{ \"kind\": \"CoodszBeCompat\", \"schemaVersion\": 2, \"mods\": [] }"));
        Assert.DoesNotContain("bannerlord.eu", BeCompatList.OnlineUrl);
        Assert.DoesNotContain("TAOM", BeCompatList.OnlineUrl);
        Assert.Equal("Reported on the Burning Empires Discord in Burning Empires singleplayer, not tested in Coop", BeCompatSource.Label(BeCompatSource.BeDiscord));
        Assert.Equal("Said by the Burning Empires developer", BeCompatSource.Label(BeCompatSource.BeDev));
        Assert.DoesNotContain("Theboyys", BeCompatSource.Label("taom-dev"));
        Assert.Equal("Works in Burning Empires, not tested in Coop", BeCompatList.BadgeText(CompatTag.Reported));
        Assert.Equal("Works in Burning Empires, not tested in Coop", BeModList.Title(CompatGroup.WorksInTaom));
    }

    [Fact]
    public void A_cached_older_list_loses_to_the_built_in_one()
    {
        var folder = Directory.CreateTempSubdirectory("be-compat-").FullName;
        try
        {
            var cache = Path.Combine(folder, BeCompatList.FileName);
            File.WriteAllText(cache, "{ \"kind\": \"CoodszBeCompat\", \"schemaVersion\": 2, \"updated\": \"2000-01-01\", \"mods\": [] }");
            Assert.Equal(BeCompatList.BuiltIn().Mods.Count, BeCompatList.Load(cache).Mods.Count);
            File.WriteAllText(cache, "{ \"kind\": \"CoodszBeCompat\", \"schemaVersion\": 2, \"updated\": \"2999-01-01\", \"mods\": [] }");
            Assert.Empty(BeCompatList.Load(cache).Mods);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_load_test_export_writes_a_burning_empires_list()
    {
        var current = BeCompatList.BuiltIn();
        var passed = new ModTestResult { Id = "NewMod", Name = "New mod", WorkshopId = "987654", Version = "v1.2.0", Result = ModTestResult.Pass, Date = "2026-10-26" };
        var failed = new ModTestResult { Id = "BadMod", Name = "Bad mod", Result = ModTestResult.Fail, Date = "2026-10-26" };
        var merged = BeModTest.MergePassed(current, new[] { passed, failed }, "2026-10-27");
        Assert.Equal("2026-10-27", merged.Updated);
        Assert.Equal(CompatTag.Loads, merged.Find("NewMod")!.Kind);
        Assert.Null(merged.Find("BadMod"));
        Assert.Equal(current.Mods.Count, BeCompatList.BuiltIn().Mods.Count);

        var text = BeModTest.Serialize(merged);
        Assert.Contains("\"kind\": \"CoodszBeCompat\"", text);
        Assert.Contains("\r\n  \"be\": \"2.0.2\",\r\n", text);
        Assert.DoesNotContain("\"taom\"", text);
        Assert.Equal(merged.Mods.Count, BeCompatList.Parse(text).Mods.Count);
        Assert.Equal("modtest-be-mod-compat.json", BeModTest.ExportFileName);
        Assert.Equal("CoodszBeModTest", BeModTest.ResultsKind);

        var stamped = new ModTestResult();
        BeModTest.Flavor().Stamp(stamped);
        Assert.Equal("Burning Empires 2.0.2", stamped.Base);
        Assert.Equal("", stamped.Taom);
        Assert.Contains("Burning Empires core", BeModTest.Flavor().StartLine(new ModTestCandidate("M", "My mod", "0", "", Array.Empty<TaomExtraMod>())));
    }

    [Fact]
    public void The_load_test_skips_the_core_elephants_and_mods_coodsz_tested()
    {
        var list = BeCompatList.Parse("{ \"kind\": \"CoodszBeCompat\", \"schemaVersion\": 2, \"mods\": ["
            + "{ \"id\": \"Tested\", \"tag\": \"safe\", \"source\": \"coodsz-coop\" },"
            + "{ \"id\": \"Reported\", \"tag\": \"reported\", \"source\": \"be-discord\" },"
            + "{ \"id\": \"BurningEmpires_Elephants\", \"tag\": \"everyone\" } ] }");
        var onPc = new[]
        {
            new FoundMod("Tested", "Tested", "0", "C:\\nowhere\\Tested", "Game"),
            new FoundMod("Reported", "Reported", "0", "C:\\nowhere\\Reported", "Game"),
            new FoundMod("Elephants", BeStack.ElephantsModuleId, "0", "C:\\nowhere\\Elephants", "Game"),
            new FoundMod("Harmony", "Bannerlord.Harmony", "2859188632", "C:\\nowhere\\Harmony", "Workshop")
        };
        var picked = BeModTest.DefaultCandidates(list, onPc, new Dictionary<string, BannerlordCoopLauncher.Core.Parsing.ModuleManifest>());
        Assert.Equal("Reported", Assert.Single(picked).ModuleId);
        var setup = BeModTest.SetupFor(picked[0]);
        Assert.Equal(BeStack.ServerModules.Append("Reported"), BeLoadOrder.ServerOrder(setup));
    }

    [Fact]
    public void Burning_empires_results_are_kept_apart_from_other_tools_results()
    {
        var folder = Directory.CreateTempSubdirectory("be-results-").FullName;
        try
        {
            var path = Path.Combine(folder, "mod-results.json");
            // TAOM's tool records a pass under the plain module id.
            ModResults.Record(new ModResult { Id = "SharedMod", Version = "v1", Result = ModResult.Passed, Where = ModResult.YourServer, Date = "2026-10-20" }, path);
            Assert.Null(BeModResults.Pill("SharedMod", "v1", path: path));

            Assert.True(BeModResults.Record(BeModResults.CrashFound("SharedMod", "v1", "2026-10-21"), path));
            var pill = BeModResults.Pill("SharedMod", "v1", path: path);
            Assert.Equal("Crashed", pill!.Kind);
            // The other tools still see only their own pass.
            Assert.Equal("Passed", ModResults.Pill("SharedMod", "v1", ModResults.Load(path))!.Kind);
            Assert.Equal("be:SharedMod", BeModResults.Key("SharedMod"));
            Assert.Equal("SharedMod", BeModResults.ModuleIdOf("be:SharedMod"));
            Assert.Equal("be:X", Assert.Single(BeModResults.Keyed(new[] { new ModResult { Id = "be:X" } })).Id);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_core_and_elephants_get_no_unknown_badge()
    {
        var list = BeCompatList.BuiltIn();
        Assert.False(BeModResults.ShowsUnknown(list, "BurningEmpires", null, null));
        Assert.False(BeModResults.ShowsUnknown(list, BeStack.ElephantsModuleId, null, null));
        Assert.False(BeModResults.ShowsUnknown(list, "Bannerlord.ButterLib", null, null));
        Assert.True(BeModResults.ShowsUnknown(list, "SomethingNew", "1", "Something new"));
        Assert.Empty(BeModNotes.For(new TaomSetup()));
        var withBsr = new TaomSetup { Name = "Mine" };
        Assert.Null(BeLoadOrder.Add(withBsr, "BSR", BeModNotes.BattleSizeResizedId, null));
        Assert.Contains("Battle size", Assert.Single(BeModNotes.For(withBsr)));
    }

    [Fact]
    public void The_hashes_match_on_their_recorded_ends_and_the_bundled_uiextenderex_is_2_13_3()
    {
        Assert.True(BeStack.HashMatches("C22A980C" + new string('0', 52) + "EF3C", "C22A980C...EF3C"));
        Assert.False(BeStack.HashMatches("C22A980D" + new string('0', 52) + "EF3C", "C22A980C...EF3C"));
        Assert.False(BeStack.HashMatches("C22A980CEF3C", "C22A980C...EF3C"));
        // The full hashes, the same ones the server path and Install or repair check.
        Assert.True(BeStack.HashMatches(BeStack.BeDllSha256.ToLowerInvariant(), BeServerPath.BeDllSha256));
        Assert.Equal(64, BeStack.DistanceCacheProbeDllSha256.Length);
        Assert.False(BeStack.HashMatches("C22A980C" + new string('0', 52) + "EF3C", BeStack.BeDllSha256));
        Assert.True(BeStack.HashMatches(BeStack.ServerUiExtenderExSha256.ToLowerInvariant(), BeStack.ServerUiExtenderExSha256));

        var dll = Path.Combine(RepoRoot(), BeStack.ServerUiExtenderExBundlePath);
        if (!File.Exists(dll))
        {
            _output.WriteLine("Skipped the bundled UIExtenderEx check: " + dll + " is not there yet. Put the official UIExtenderEx 2.13.3 dll there.");
            return;
        }

        Assert.Equal(BeStack.ServerUiExtenderExBytes, new FileInfo(dll).Length);
        Assert.True(BeStack.HashMatches(Sha256Files.Hash(dll), BeStack.ServerUiExtenderExSha256));
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "BannerlordCoopLauncher.sln")))
            folder = folder.Parent;
        return folder?.FullName ?? AppContext.BaseDirectory;
    }
}
