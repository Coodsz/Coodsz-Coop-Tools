using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Tests;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// Live run 4: MCM v5.12.3 only as the removed official Workshop item 2859238197, which Steam downloaded
// before, and a server folder that already had v5.12.3. The server tool still said to get MCM from Nexus
// Mods. A copy the server loads in the game's Modules folder is the source, else that Workshop copy;
// with none, a good server copy is kept; only with neither is MCM a problem.
public class BeMcmSourceTests
{
    private const string Mcm = "Bannerlord.MBOptionScreen";
    private static readonly string OldItem = RetiredWorkshopItems.For(Mcm)!;

    private static string WorkshopMcm(BeCoreSources sources, string version, int library = 0) =>
        Module(Path.Combine(sources.SteamLibraries[library], "steamapps", "workshop", "content", "261550"), OldItem, Mcm, version);

    private static string ServerMcm(string server) => Path.Combine(BeServerPath.ModulesRoot(server), Mcm);

    private static bool McmLine(string line) => line.Contains("MCM", StringComparison.Ordinal);

    [Fact]
    public void AnMcmOnlyInTheOldWorkshopItemIsTakenAndStaged()
    {
        var root = NewRoot("mcm-workshop-only");
        try
        {
            var sources = HostPc(root);
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            var workshop = WorkshopMcm(sources, "v5.12.3");
            Write(Path.Combine(workshop, "bin", "Win64_Shipping_Client", "MCMv5.dll"), "workshop mcm");
            var server = Path.Combine(root, "server");

            Assert.DoesNotContain(BeServerPath.CoreProblems(sources), McmLine);
            Assert.Empty(BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot));

            var lines = BeServerPath.StageCore(server, sources);
            Assert.Contains("Copied MCM to the server (2 files).", lines);
            Assert.Equal("v5.12.3", BeServerPath.VersionOf(ServerMcm(server)));
            Assert.Equal("workshop mcm", File.ReadAllText(Path.Combine(ServerMcm(server), "bin", "Win64_Shipping_Client", "MCMv5.dll")));

            // An old server copy is replaced whole by it, and the line names where it came from.
            Directory.Delete(ServerMcm(server), recursive: true);
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.11.4");
            Assert.Contains("MCM v5.12.3 from the old Steam Workshop item replaced MCM v5.11.4 on the server. The old copy is in the server folder's removed-mods folder.",
                BeServerPath.StageCore(server, sources));
            Assert.Equal("v5.12.3", BeServerPath.VersionOf(ServerMcm(server)));

            // It never replaces a newer server copy.
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.13.0");
            Assert.Contains("The server keeps MCM v5.13.0, since the old Steam Workshop item has the older MCM v5.12.3.", BeServerPath.StageCore(server, sources));
            Assert.Equal("v5.13.0", BeServerPath.VersionOf(ServerMcm(server)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheGameFolderCopyWinsAndTheOldWorkshopCopyIsTheFallback()
    {
        var root = NewRoot("mcm-order");
        try
        {
            var sources = HostPc(root);
            sources = sources with { SteamLibraries = [sources.SteamLibraries[0], Path.Combine(root, "steam2")] };
            var server = Path.Combine(root, "server");

            // A good copy in the game's Modules folder is taken, even when the old Workshop copy is newer.
            WorkshopMcm(sources, "v5.13.0");
            Assert.Equal(BeServerPath.McmSource(sources.GameRoot), BeServerPath.BestMcm(sources.GameRoot, sources.SteamLibraries));
            BeServerPath.StageCore(server, sources);
            Assert.Equal("v5.12.3", BeServerPath.VersionOf(ServerMcm(server)));

            // A game copy too old for the server falls back to the newest old Workshop copy in any Steam library.
            Directory.Delete(ServerMcm(server), recursive: true);
            Module(Path.Combine(sources.GameRoot, "Modules"), Mcm, Mcm, "v5.11.4");
            WorkshopMcm(sources, "v5.14.0", library: 1);
            Assert.DoesNotContain(BeServerPath.CoreProblems(sources), McmLine);
            BeServerPath.StageCore(server, sources);
            Assert.Equal("v5.14.0", BeServerPath.VersionOf(ServerMcm(server)));

            // A game copy whose version cannot be read falls back too.
            Directory.Delete(ServerMcm(server), recursive: true);
            Module(Path.Combine(sources.GameRoot, "Modules"), Mcm, Mcm, "latest");
            BeServerPath.StageCore(server, sources);
            Assert.Equal("v5.14.0", BeServerPath.VersionOf(ServerMcm(server)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AGoodServerCopyIsEnoughAndIsKept()
    {
        var root = NewRoot("mcm-server-only");
        try
        {
            var sources = HostPc(root);
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            var server = Path.Combine(root, "server");
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.12.3");
            Write(Path.Combine(ServerMcm(server), "bin", "Win64_Shipping_Server", "MCMv5.dll"), "server build");

            Assert.DoesNotContain(BeServerPath.CoreProblems(sources with { ServerRoot = server }), McmLine);
            Assert.Null(BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries, server));
            Assert.Empty(BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot, server));

            Assert.Contains("The server keeps its own MCM v5.12.3, since this PC has no other MCM the server can take.", BeServerPath.StageCore(server, sources));
            Assert.Equal("v5.12.3", BeServerPath.VersionOf(ServerMcm(server)));
            Assert.Equal("server build", File.ReadAllText(Path.Combine(ServerMcm(server), "bin", "Win64_Shipping_Server", "MCMv5.dll")));
            Assert.False(Directory.Exists(Path.Combine(server, BeServerPath.RemovedFolder)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void NoMcmAnywhereNamesNexusMods()
    {
        var root = NewRoot("mcm-nowhere");
        try
        {
            var sources = HostPc(root);
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            var server = Path.Combine(root, "server");
            Directory.CreateDirectory(BeServerPath.ModulesRoot(server));

            var expected = "MCM is not on this PC. Get MCM v5.12.3 or newer from Nexus Mods and put it in the game's Modules folder, then try again. "
                + "MCM is on Nexus Mods at https://www.nexusmods.com/mountandblade2bannerlord/mods/612";
            Assert.Equal(expected, BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries, server));
            Assert.Contains(expected, BeServerPath.CoreProblems(sources with { ServerRoot = server }));
            Assert.Equal(new[] { BeStack.Mcm }, BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot, server));
            Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => BeServerPath.StageCore(server, sources)).Message);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AServerCopyTooOldWithNoSourceIsStillAProblem()
    {
        var root = NewRoot("mcm-server-old");
        try
        {
            var sources = HostPc(root);
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            WorkshopMcm(sources, "v5.11.4");
            var server = Path.Combine(root, "server");
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.11.4");

            var expected = "MCM v5.11.4 from the old Steam Workshop item is too old for the server. Get MCM v5.12.3 or newer from Nexus Mods and put it in the game's Modules folder, then try again. "
                + "MCM is on Nexus Mods at https://www.nexusmods.com/mountandblade2bannerlord/mods/612";
            Assert.Equal(expected, BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries, server));
            Assert.Equal(new[] { BeStack.Mcm }, BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot, server));
            Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => BeServerPath.StageCore(server, sources)).Message);
            Assert.Equal("v5.11.4", BeServerPath.VersionOf(ServerMcm(server)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnUnreadableVersionIsNeverASourceOrAGoodServerCopy()
    {
        var root = NewRoot("mcm-unreadable-source");
        try
        {
            var sources = HostPc(root);
            Module(Path.Combine(sources.GameRoot, "Modules"), Mcm, Mcm, "latest");
            var server = Path.Combine(root, "server");
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "latest");

            // Nothing readable: the game copy's line still says the version cannot be read.
            Assert.StartsWith("The tool cannot read the version of the MCM in the game's Modules folder (latest).", BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries, server));
            WorkshopMcm(sources, "latest");
            Assert.StartsWith("The tool cannot read the version of the MCM in the game's Modules folder (latest).", BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries, server));

            // A readable Workshop copy is taken over the unreadable game copy, and replaces the unreadable server copy.
            WorkshopMcm(sources, "v5.12.3");
            Assert.Null(BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries, server));
            Assert.Contains("MCM v5.12.3 from the old Steam Workshop item replaced MCM latest on the server. The old copy is in the server folder's removed-mods folder.",
                BeServerPath.StageCore(server, sources));
            Assert.Equal("v5.12.3", BeServerPath.VersionOf(ServerMcm(server)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheModRowReadsMcmFromTheSameFolderStartCopies()
    {
        var root = NewRoot("mcm-chip");
        try
        {
            var sources = HostPc(root);
            var game = BeServerPath.McmSource(sources.GameRoot);

            // The game copy wins over a newer old Workshop copy.
            var workshop = WorkshopMcm(sources, "v5.13.0");
            Assert.Equal(game, BeModChips.CoreFolder(Mcm, sources));

            // With no good game copy, the row reads the Workshop copy Start takes.
            Directory.Delete(game, recursive: true);
            Assert.Equal(workshop, BeModChips.CoreFolder(Mcm, sources));

            // With no copy the server loads, the row still points at the game's Modules folder path.
            Directory.Delete(workshop, recursive: true);
            Assert.Equal(game, BeModChips.CoreFolder(Mcm, sources));
            Assert.Null(BeModChips.CoreFolder(Mcm, sources with { GameRoot = "" }));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void EqualWorkshopCopiesKeepTheFirstLibrary()
    {
        var root = NewRoot("mcm-tie");
        try
        {
            var sources = HostPc(root);
            sources = sources with { SteamLibraries = [sources.SteamLibraries[0], Path.Combine(root, "steam2")] };
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            var first = WorkshopMcm(sources, "v5.12.3");
            WorkshopMcm(sources, "v5.12.3.0", library: 1);
            Assert.Equal(first, BeServerPath.BestMcm(sources.GameRoot, sources.SteamLibraries));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ABlankGameOrServerFolderIsNoCopy()
    {
        var root = NewRoot("mcm-blank");
        try
        {
            var sources = HostPc(root);
            foreach (var blank in new[] { "", "   " })
            {
                Assert.Null(BeServerPath.BestMcm(blank, sources.SteamLibraries));
                Assert.False(BeServerPath.ServerMcmReady(blank));
                Assert.StartsWith("MCM is not on this PC.", BeServerPath.McmProblem(blank, sources.SteamLibraries, blank));
                Assert.Equal(new[] { BeStack.Mcm }, BeServerPath.MissingButr(sources.SteamLibraries, blank, blank));
            }

            var workshop = WorkshopMcm(sources, "v5.12.3");
            Assert.Equal(workshop, BeServerPath.BestMcm("   ", sources.SteamLibraries));
            Assert.Null(BeServerPath.McmProblem("   ", sources.SteamLibraries, "   "));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheServerToolPassesItsServerFolderToTheMcmCheck()
    {
        // The window cannot be built in a test, so the wiring is checked in the source.
        var window = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ModSetups.cs"));
        Assert.Contains("new(_gameRoot ?? \"\", BannerlordFinder.SteamLibraries(), CompatAddOn, UIExtenderExAddOn) { ServerRoot = _settings.ServerRoot };", window);
        Assert.Contains("BeServerPath.MissingButr(BannerlordFinder.SteamLibraries(), FindGameRoot(), _settings.ServerRoot)", window);
        Assert.Contains("var gameRoot = FindGameRoot();", SourceFiles.Method(window, "private string? McmProblem("));
        Assert.Contains("BeServerPath.McmProblem(gameRoot, BannerlordFinder.SteamLibraries(), _settings.ServerRoot)", window);
    }
}
