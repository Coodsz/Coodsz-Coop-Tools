using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The shared plumbing the Burning Empires server tool and client launcher need in the Core libraries:
// the updater, What's new and NEW badges, the "be" tool id in reports, the local server's extra
// data folder, the two guide sets and a low memory warning that names the right game.
[Collection(LocalServerFolders.Name)]
public sealed class BeSharedCoreTests
{
    private const string Server = "Coodsz Burning Empires Server Tool.exe";
    private const string Client = "Coodsz Burning Empires Client Launcher.exe";
    // The names the test builds had, before the two exe release.
    private const string OldServer = "Coodsz Burning Empires Server.exe";
    private const string OldClient = "Coodsz Burning Empires Client.exe";

    [Fact]
    public void The_updater_picks_the_Burning_Empires_tools_zip_and_leaves_the_other_tools_alone()
    {
        var assets = new[]
        {
            "Coodsz-Burning-Empires-Tools-v1.0.0.zip",
            "Coodsz-TAOM-Tools-v1.2.0.zip", "Coodsz-AD1259-Tools-v1.1.0.zip", "LICENSE.txt",
        };
        foreach (var (exe, successor) in new[] { (Server, Server), (OldServer, Server), (Client, Client), (OldClient, Client) })
        {
            var plan = ToolUpdate.PlanInstall(exe, assets)!;
            Assert.Equal("Coodsz-Burning-Empires-Tools-v1.0.0.zip", plan.ZipName);
            Assert.Equal(successor, plan.PayloadExe);
            Assert.True(plan.SuccessorOnly);
        }
        Assert.Equal("Coodsz-TAOM-Tools-v1.2.0.zip", ToolUpdate.PickZip("Coodsz TAOM Server Tool.exe", assets));
        Assert.Equal("Coodsz-AD1259-Tools-v1.1.0.zip", ToolUpdate.PickZip("Coodsz AD1259 Client Launcher.exe", assets));

        // A BE release never hands a BE tool a TAOM or AD1259 zip, even when its own zip is missing,
        // and the zip names the test builds looked for are not picked by name parts any more.
        foreach (var exe in new[] { Server, OldServer, Client, OldClient })
            Assert.Null(ToolUpdate.PickZip(exe, assets.Where(name => !name.Contains("Burning"))));
        Assert.Null(ToolUpdate.PickZip(OldServer, ["Coodsz-Burning-Empires-Server-v1.0.0.zip"]));
        Assert.Null(ToolUpdate.PickZip("Coodsz Burning Empires Server Tool (2).exe", assets));
    }

    [Theory]
    [InlineData("be-launcher.json")]
    [InlineData("be-server.json")]
    [InlineData("be-cheats.json")]
    [InlineData("be-bans.json")]
    public void An_update_keeps_the_Burning_Empires_settings(string file)
    {
        Assert.True(ToolUpdate.IsKeptUserFile(file));
        Assert.False(ToolUpdate.ShouldReplace(file));
    }

    [Fact]
    public void Release_What_s_new_and_badges_start_at_v1_0_0()
    {
        Assert.Equal("v1.0.16", ReleaseNotice.BeVersion);
        Assert.Equal("Coodsz/Coodsz-BurningEmpires", ReleaseNotice.BeRepo);
        Assert.Equal(ReleaseNotice.BeVersion, WhatsNew.Be[0].Version);
        Assert.NotEmpty(WhatsNew.Be[0].Lines);
        // The list still starts at the first release, v1.0.0, with its own lines.
        Assert.Equal("v1.0.0", WhatsNew.Be[^1].Version);
        Assert.NotEmpty(WhatsNew.Be[^1].Lines);
        Assert.DoesNotContain(WhatsNew.Be.SelectMany(e => e.Lines), line => line.Contains("TAOM"));
        foreach (var tool in new[] { "be-server", "be-launcher" })
        {
            var features = NewFeatures.For(tool);
            Assert.Contains(features, f => f.Id == NewFeatures.WhatsNewButton);
            Assert.Contains(features, f => f.Id == NewFeatures.Guides);
            Assert.All(features, f => Assert.False(ReleaseNotice.IsNewer(f.Version, ReleaseNotice.BeVersion)));
        }
        // A fresh install of v1.0.0 badges them, since they came with that version, and an update from
        // v1.0.0 to this version does not badge them again.
        var guides = NewFeatures.For("be-server").Single(f => f.Id == NewFeatures.Guides);
        Assert.True(NewFeatureStore.Shows(guides, "v1.0.0", previous: null, seen: false));
        Assert.False(NewFeatureStore.Shows(guides, ReleaseNotice.BeVersion, previous: "v1.0.0", seen: false));
    }

    [Fact]
    public void The_be_tool_id_survives_mod_reports_crash_reports_and_the_crash_screen()
    {
        var report = ModReports.Build("0123456789abcdef0123456789abcdef", "be", "1.0.0", "v0.1.6", "v1.4.8",
            new[] { new ModReportItem { Id = "RBM", Result = ModReportItem.Broken } }, Array.Empty<ModReportMod>());
        Assert.Equal("be", report.Tool);

        var request = new CrashReportRequest { Tool = "be", Collect = (_, _) => [] };
        Assert.Equal("be", CrashReports.BuildSummary(request, "0123456789abcdef0123456789abcdef", []).Tool);
        Assert.Contains("\"tool\":\"be\"", CrashReports.EnvironmentJson(request).Replace(" ", ""));
        Assert.True(CrashScreen.Applies("be", CrashReason.Crash));
        Assert.True(CrashScreen.Applies("be", CrashReason.ServerCrash));
        Assert.False(CrashScreen.Applies("be", CrashReason.Logs));

        // An unknown id still falls back to coop, so only the real tools reach their own repo.
        Assert.Equal("coop", CrashReports.BuildSummary(new CrashReportRequest { Tool = "bee", Collect = (_, _) => [] }, "", []).Tool);

        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.Contains("\"be\" => \"Burning Empires \" + (side == \"server\" ? \"server\" : \"client\"),", window);
    }

    [Fact]
    public void The_local_server_reads_an_extra_data_folder_before_Documents()
    {
        var paths = LocalServer.ConfigCandidates(
            new[] { "x.exe --data-dir \"c:\\TAOM-Coop-Host\\Data-v0325\"" },
            new[] { @"D:\BurningEmpires-Coop-Host\Data\", null, " ", @"c:\TAOM-Coop-Host\Data-v0325" });
        Assert.Equal(new[]
        {
            Path.Combine(@"c:\TAOM-Coop-Host\Data-v0325", "server-config.json"),
            Path.Combine(@"D:\BurningEmpires-Coop-Host\Data", "server-config.json"),
            LocalServer.ServerConfigPath,
        }, paths);

        var before = LocalServer.ExtraDataFolders;
        try
        {
            LocalServer.ExtraDataFolders = () => [@"D:\BurningEmpires-Coop-Host\Data"];
            Assert.Equal(Path.Combine(@"D:\BurningEmpires-Coop-Host\Data", "server-config.json"), LocalServer.ConfigCandidates(Array.Empty<string?>())[0]);
            // A be-server.json that cannot be read only loses the extra folder.
            LocalServer.ExtraDataFolders = () => throw new IOException("locked");
            Assert.Equal(new[] { LocalServer.ServerConfigPath }, LocalServer.ConfigCandidates(Array.Empty<string?>()));
        }
        finally
        {
            LocalServer.ExtraDataFolders = before;
        }
    }

    [Fact]
    public void Each_Burning_Empires_tool_loads_its_own_guides()
    {
        Assert.Equal(new[] { "getting-started", "inviting-friends", "saves", "backups-and-rescue", "extra-mods", "game-speed", "common-problems" },
            GuideLibrary.For(GuideSet.BeServer).Select(g => g.Id));
        Assert.Equal(new[] { "getting-started", "joining-a-friend", "mods-and-setups", "common-problems" },
            GuideLibrary.For(GuideSet.BeClient).Select(g => g.Id));
        // The TAOM sets keep their own guides.
        Assert.Equal(7, GuideLibrary.For(GuideSet.TaomServer).Count);
        Assert.Equal(4, GuideLibrary.For(GuideSet.TaomClient).Count);
        Assert.Contains(GuideLibrary.For(GuideSet.TaomServer).SelectMany(GuideLibrary.Lines), line => line.Contains("TAOM"));
    }

    [Theory]
    [InlineData(GuideSet.BeServer)]
    [InlineData(GuideSet.BeClient)]
    public void Burning_Empires_guides_keep_to_the_house_style_and_cover_the_BE_facts(GuideSet set)
    {
        var guides = GuideLibrary.For(set);
        foreach (var guide in guides)
        {
            Assert.NotEmpty(guide.Summary);
            Assert.All(guide.Sections, s => Assert.NotEmpty(s.Steps));
            foreach (var line in GuideLibrary.Lines(guide))
            {
                Assert.True(line.Length <= 160, "Too long for a step: " + line);
                Assert.False(char.IsLower(line[0]), "Starts with a small letter: " + line);
                Assert.DoesNotContain("{", line);
                Assert.DoesNotContain("Low FPS", line);
                Assert.DoesNotContain("dependency fix", line);
                Assert.DoesNotContain("TAOM.Dependencies", line);
                Assert.DoesNotContain("TAOM", line);
                // House style: no colon inside a sentence, no hyphen between words. A real folder name keeps its hyphen.
                Assert.DoesNotMatch(@":\s", line);
                Assert.DoesNotMatch(@"[A-Za-z]-[A-Za-z]", line.Replace("save-backups", ""));
            }
        }

        var text = string.Join("\n", guides.SelectMany(GuideLibrary.Lines));
        // Plain names, not Workshop or Nexus numbers, and buttons are clicked.
        Assert.Contains("Burning Empires 2.0.2 from Nexus Mods", text);
        Assert.Contains("Bannerlord Coop", text);
        Assert.DoesNotContain("7750", text);
        Assert.DoesNotContain("3770450698", text);
        Assert.DoesNotContain("BESavedata", text);
        Assert.DoesNotMatch(@"\b[Pp]ress\b", text);
        Assert.Contains("Install the main file first, then the hotfix.", text);
        Assert.Contains("Install or repair Burning Empires", text);
        Assert.Contains("Harmony, ButterLib, UIExtenderEx", text);
        Assert.Contains("black terrain", text);
        if (set == GuideSet.BeServer)
            Assert.Contains("Windows", text);
        else
            Assert.Contains("A mod list from another overhaul is turned down.", text);
        Assert.Contains(GuideLibrary.Search(guides, "black terrain"), g => g.Id == "common-problems");
    }

    [Fact]
    public void The_low_memory_warning_names_the_game_the_launcher_starts()
    {
        const ulong gb = 1024UL * 1024 * 1024;
        var low = new MemoryState(16 * gb, 1 * gb, 95);
        var shared = new MemoryState(16 * gb, 8 * gb, 50);

        // TAOM keeps its exact text, so its translations still match.
        Assert.Contains(". TAOM can freeze or close like this.", LowMemory.ClientWarning(low, serverOnThisPc: false));
        Assert.Contains(". TAOM and the server together", LowMemory.ClientWarning(shared, serverOnThisPc: true));

        var be = LowMemory.ClientWarning(low, serverOnThisPc: false, game: "Burning Empires")!;
        Assert.Contains(". Burning Empires can freeze or close like this.", be);
        Assert.DoesNotContain("TAOM", be);
        Assert.Contains(". Burning Empires and the server together", LowMemory.ClientWarning(low, serverOnThisPc: true, game: "Burning Empires"));
        Assert.DoesNotContain("TAOM", LowMemory.ClientWarning(shared, serverOnThisPc: true, game: "Burning Empires"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>Tests that change LocalServer.ExtraDataFolders, or read the server config candidates it adds to.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalServerFolders
{
    public const string Name = "Local server folders";
}
