using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Tests;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// The official BUTR MCM Workshop item 2859238197 was removed; the Workshop only has re-uploads, some of
// them old builds the dedicated server refuses (crash reports #32 and #34). Coodsz: MCM comes from Nexus
// Mods (mod 612) only, into the game's Modules folder.
public class BeMcmNexusTests
{
    private const string Mcm = "Bannerlord.MBOptionScreen";
    private static readonly string OldItem = RetiredWorkshopItems.For(Mcm)!;

    [Fact]
    public void McmIsNeverAWorkshopItem()
    {
        Assert.False(SharedModList.IsWorkshopId(BeStack.Mcm.WorkshopId));
        Assert.Equal(612, BeStack.Mcm.NexusId);
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/612", BeStack.Mcm.NexusPage);
        Assert.DoesNotContain(BeStack.WorkshopButr, mod => mod.ModuleId == Mcm);
        Assert.Equal(new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx" }, BeStack.WorkshopButr.Select(mod => mod.ModuleId));
        Assert.DoesNotContain(BeServerPath.Butr, item => item.WorkshopId == OldItem);
        // MCM stays a core module in the load order.
        Assert.True(BeStack.IsButr(Mcm));

        // Connect names MCM's Nexus Mods page instead of asking Steam for it.
        var need = BeSharedList.ButrNeed(BeStack.Mcm);
        Assert.Equal(ServerModFetch.ByHand, need.Fetch);
        Assert.Equal(BeStack.Mcm.NexusPage, need.Link);
        Assert.Equal(ServerModFetch.Workshop, BeSharedList.ButrNeed(BeStack.Harmony).Fetch);

        // A shared mod list lists MCM as a local mod.
        var book = new BeSetupBook();
        book.Ensure();
        var entries = BeSharedList.Entries(book.Find(BeSetupBook.CoreName)!, _ => null);
        Assert.Equal("0", entries.Single(entry => entry.Id == Mcm).WorkshopId);
        Assert.DoesNotContain(entries, entry => entry.WorkshopId == OldItem);
    }

    [Fact]
    public void StartTakesANewerMcmFromTheGameFolderAndNeverAnOlderOrAStaleWorkshopOne()
    {
        var root = NewRoot("mcm-nexus");
        try
        {
            var sources = HostPc(root);
            var server = Path.Combine(root, "server");
            var serverMcm = Path.Combine(BeServerPath.ModulesRoot(server), Mcm);

            // The server still has an old MCM from the removed Workshop item, with its old server build.
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.11.4");
            Write(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll"), "old server build");
            // A stale copy of the old Workshop item is too old for the server, so it is never a source.
            var workshopMcm = Module(Path.Combine(sources.SteamLibraries[0], "steamapps", "workshop", "content", "261550"), OldItem, Mcm, "v5.11.4");
            Write(Path.Combine(workshopMcm, "bin", "Win64_Shipping_Client", "MCMv5.dll"), "workshop reupload");

            var lines = BeServerPath.StageCore(server, sources);

            Assert.Contains("MCM v5.12.3 from the game's Modules folder replaced MCM v5.11.4 on the server. The old copy is in the server folder's removed-mods folder.", lines);
            Assert.Equal(BeStack.Mcm.TestedVersion, BeServerPath.VersionOf(serverMcm));
            Assert.Equal("mcm v5.12.3", File.ReadAllText(Path.Combine(serverMcm, "bin", "Win64_Shipping_Client", "MCMv5.dll")));
            // Replaced whole: the old server build that stopped the server is gone from the server copy, kept aside.
            Assert.False(Directory.Exists(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server")));
            Assert.Equal("old server build", File.ReadAllText(Path.Combine(server, BeServerPath.RemovedFolder, Mcm + "-v5.11.4", "bin", "Win64_Shipping_Server", "MCMv5.dll")));

            // The same version again is only topped up.
            Assert.Contains("MCM is already on the server.", BeServerPath.StageCore(server, sources));

            // An older MCM in the game's Modules folder never replaces a newer server copy.
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.13.0");
            Assert.Contains("The server keeps MCM v5.13.0, since the game's Modules folder has the older MCM v5.12.3.", BeServerPath.StageCore(server, sources));
            Assert.Equal("v5.13.0", BeServerPath.VersionOf(serverMcm));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AMissingOrTooOldMcmPointsAtNexusAndTheGameFolder()
    {
        var root = NewRoot("mcm-old");
        try
        {
            var sources = HostPc(root);
            Assert.Null(BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries));

            Module(Path.Combine(sources.GameRoot, "Modules"), Mcm, Mcm, "v5.11.4");
            var tooOld = BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries);
            Assert.Equal("MCM v5.11.4 in the game's Modules folder is too old for the server. Get MCM v5.12.3 or newer from Nexus Mods and put it in place of the old one, then try again. "
                + "MCM is on Nexus Mods at https://www.nexusmods.com/mountandblade2bannerlord/mods/612", tooOld);
            Assert.Contains(tooOld, BeServerPath.CoreProblems(sources));
            Assert.Throws<InvalidOperationException>(() => BeServerPath.StageCore(Path.Combine(root, "server"), sources));

            // v5.12.0 is the oldest the server loads; v5.12.0.0 is the same version.
            Module(Path.Combine(sources.GameRoot, "Modules"), Mcm, Mcm, "v5.12.0.0");
            Assert.Null(BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries));

            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            var missing = BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries)!;
            Assert.StartsWith("MCM is not on this PC. Get MCM v5.12.3 or newer from Nexus Mods and put it in the game's Modules folder", missing);
            Assert.DoesNotContain("Workshop", missing);
            Assert.DoesNotContain("subscribe", missing, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheClientsSayNexusModsForMcm()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "BurningEmpiresLauncher", "MainWindow.xaml"));
        Assert.DoesNotContain("four helper mods (Harmony, ButterLib, UIExtenderEx, MCM) come from the Steam Workshop", xaml);
        Assert.DoesNotContain("asks Steam for the four helper mods", xaml);
        Assert.Contains("MCM comes from Nexus Mods into the game's Modules folder.", xaml);

        // The client asks Steam only for the Workshop helper mods, and its MCM row points at Nexus Mods.
        var downloads = BeSharedList.ButrDownloads(BeStack.Butr);
        Assert.Equal(new ulong[] { 2859188632, 2859232415, 2859222409 }, downloads.Keys);
        Assert.Empty(BeSharedList.ButrDownloads([BeStack.Mcm]));
        Assert.Equal("missing. Get it from Nexus Mods and put it in the game's Modules folder.", BeSharedList.ButrMissingText(BeStack.Mcm));
        Assert.Equal("missing. Connect gets it from the Steam Workshop.", BeSharedList.ButrMissingText(BeStack.Harmony));
    }

    [Fact]
    public void BeGuidesAndWhatsNewNeverSayMcmIsAWorkshopItem()
    {
        // A line that names MCM next to Steam or the Workshop also says MCM comes from Nexus Mods.
        var lines = WhatsNew.Be.SelectMany(entry => entry.Lines)
            .Concat(GuideLibrary.For(GuideSet.BeClient).SelectMany(GuideLibrary.Lines))
            .Concat(GuideLibrary.For(GuideSet.BeServer).SelectMany(GuideLibrary.Lines))
            .Where(line => line.Contains("MCM", StringComparison.Ordinal)
                && (line.Contains("Workshop", StringComparison.Ordinal) || line.Contains("Steam", StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains("Nexus Mods", line));
    }

    [Fact]
    public void ServerInstallModsTakesAnMcmAlreadyOnThisPcAndNeverAsksSteam()
    {
        var root = NewRoot("mcm-install");
        try
        {
            var sources = HostPc(root);
            Assert.Empty(BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot));

            // A copy Steam downloaded before of the removed Workshop item counts when the server loads it; a stale one does not.
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            Module(Path.Combine(sources.SteamLibraries[0], "steamapps", "workshop", "content", "261550"), OldItem, Mcm, "v5.12.3");
            Assert.Empty(BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot));
            Module(Path.Combine(sources.SteamLibraries[0], "steamapps", "workshop", "content", "261550"), OldItem, Mcm, "v5.11.4");
            Assert.Equal(new[] { BeStack.Mcm }, BeServerPath.MissingButr(sources.SteamLibraries, sources.GameRoot));

            var noSteam = BeServerPath.MissingButr([Path.Combine(root, "no-steam")], sources.GameRoot);
            Assert.Equal(new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", Mcm }, noSteam.Select(mod => mod.ModuleId));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnMcmWithAnUnreadableVersionIsAProblem()
    {
        var root = NewRoot("mcm-unreadable");
        try
        {
            var sources = HostPc(root);
            Module(Path.Combine(sources.GameRoot, "Modules"), Mcm, Mcm, "latest");
            Assert.Equal("The tool cannot read the version of the MCM in the game's Modules folder (latest). Get MCM v5.12.3 or newer from Nexus Mods and put it in place of that one, then try again. "
                + "MCM is on Nexus Mods at https://www.nexusmods.com/mountandblade2bannerlord/mods/612", BeServerPath.McmProblem(sources.GameRoot, sources.SteamLibraries));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AFailedCopyPutsTheOldServerMcmBack()
    {
        var root = NewRoot("mcm-rollback");
        try
        {
            var sources = HostPc(root);
            var server = Path.Combine(root, "server");
            var serverMcm = Path.Combine(BeServerPath.ModulesRoot(server), Mcm);
            Module(BeServerPath.ModulesRoot(server), Mcm, Mcm, "v5.11.4");
            Write(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll"), "old server build");

            // A file of the new MCM that cannot be read makes the copy fail halfway.
            var locked = Write(Path.Combine(BeServerPath.McmSource(sources.GameRoot), "bin", "Win64_Shipping_Client", "zz-locked.dll"), "x");
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var error = Assert.Throws<InvalidOperationException>(() => BeServerPath.StageMcm(server, BeServerPath.McmSource(sources.GameRoot)));
                Assert.Equal("MCM could not be copied to the server, so the old copy was put back. Stop the server and try again.", error.Message);
            }

            Assert.Equal("v5.11.4", BeServerPath.VersionOf(serverMcm));
            Assert.Equal("old server build", File.ReadAllText(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll")));
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(server, BeServerPath.RemovedFolder)));

            // A server copy in use is not moved, and the reason is plain.
            using (new FileStream(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                File.Delete(locked);
                var error = Assert.Throws<InvalidOperationException>(() => BeServerPath.StageMcm(server, BeServerPath.McmSource(sources.GameRoot)));
                Assert.Equal("MCM on the server could not be replaced, because a file in it is in use. Stop the server and try again.", error.Message);
            }

            Assert.Equal("v5.11.4", BeServerPath.VersionOf(serverMcm));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheOldCopyGetsAFolderNameThatIsFreeAndValid()
    {
        var root = NewRoot("mcm-aside");
        try
        {
            Assert.Equal(Path.Combine(root, Mcm + "-v5.11.4"), BeServerPath.AsideFolder(root, "v5.11.4"));
            Assert.Equal(Path.Combine(root, Mcm + "-v5_11_4"), BeServerPath.AsideFolder(root, "v5:11|4"));
            var stamped = Path.GetFileName(BeServerPath.AsideFolder(root, "..."));
            Assert.Matches(@"^Bannerlord\.MBOptionScreen-\d{8}-\d{6}$", stamped);

            Directory.CreateDirectory(Path.Combine(root, Mcm + "-v5.11.4"));
            // One fixed time for both calls, or a tick of the clock between them gives the third a new stamp (it failed once in CI).
            var now = new DateTime(2026, 11, 19, 12, 30, 45);
            var second = BeServerPath.AsideFolder(root, "v5.11.4", now);
            Assert.EndsWith("-v5.11.4-20261119-123045", second);
            Directory.CreateDirectory(second);
            var third = BeServerPath.AsideFolder(root, "v5.11.4", now);
            Assert.Equal(second + "-2", third);
        }
        finally
        {
            Delete(root);
        }
    }
}
