using BannerlordCoopLauncher.Tests;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// The release review fixes on the Burning Empires client: a stale Modules Coop next to the
// Workshop Coop, the lone hotfix and lone main file, friendly names and the Delete tooltip.
public sealed class BeClientReviewFixTests
{
    [Fact]
    public void A_stale_modules_coop_is_moved_aside_once_the_workshop_coop_updates()
    {
        var root = NewRoot("coop-stale");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "v0.1.7");
            // TAOM's ParkExtraCoop keeps this one because it is 0.1.6, and the game loads it first.
            Module(modules, "Coop", "Coop", "v0.1.6");
            Module(modules, "CoopNightly", "CoopNightly", "v0.0.9");
            Module(modules, "BurningEmpires", "BurningEmpires", "v2.0.2");

            var moved = BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName));

            Assert.Equal(2, moved.Count);
            Assert.Contains(Path.Combine(game, "Coop-kept-0.1.6"), moved);
            Assert.True(File.Exists(Path.Combine(game, "Coop-kept-0.1.6", "SubModule.xml")));
            Assert.False(Directory.Exists(Path.Combine(modules, "Coop")));
            Assert.False(Directory.Exists(Path.Combine(modules, "CoopNightly")));
            Assert.True(Directory.Exists(Path.Combine(modules, "BurningEmpires")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_modules_coop_with_the_workshop_version_stays_and_a_second_copy_goes()
    {
        var root = NewRoot("coop-same");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "v0.1.6");
            Module(modules, "Coop", "Coop", "v0.1.6");
            Module(modules, "Coop copy", "Coop", "v0.1.6");

            var moved = BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName));

            Assert.Single(moved);
            Assert.True(Directory.Exists(Path.Combine(modules, "Coop")));
            Assert.False(Directory.Exists(Path.Combine(modules, "Coop copy")));
            Assert.Empty(BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void An_unreadable_workshop_coop_moves_nothing_and_says_why()
    {
        var root = NewRoot("coop-unreadable");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            Module(modules, "Coop", "Coop", "v0.1.6");
            // A half downloaded Workshop Coop: SubModule.xml without a version, then one that is locked.
            var workshop = Path.Combine(root, "workshop", "3770450698");
            Directory.CreateDirectory(workshop);
            var xml = Path.Combine(workshop, "SubModule.xml");
            File.WriteAllText(xml, "<Module>\n  <Id value=\"Coop\"/>\n</Module>\n");

            var garbled = Assert.Throws<BeCoopCopyException>(() => BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
            Assert.Equal(BeCoopCopy.WorkshopUnreadable, garbled.Message);
            Assert.True(Directory.Exists(Path.Combine(modules, "Coop")));

            Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "v0.1.6");
            using (new FileStream(xml, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var locked = Assert.Throws<BeCoopCopyException>(() => BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
                Assert.Equal(BeCoopCopy.WorkshopUnreadable, locked.Message);
            }

            Assert.True(Directory.Exists(Path.Combine(modules, "Coop")));
            Assert.Empty(BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_client_logs_an_unreadable_workshop_coop_in_plain_words()
    {
        var client = SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher");
        Assert.Contains("catch (BeCoopCopyException ex)", SourceFiles.Method(client, "private void ApplyCrashGuards()"));
        var useCoop = SourceFiles.Method(client, "private async void UseCoop_Click(");
        Assert.DoesNotContain("ModuleIdentityReader.Read(", useCoop);
        Assert.True(useCoop.IndexOf("BeCoopCopy.WorkshopVersion(workshop)", StringComparison.Ordinal) > useCoop.IndexOf("await RunAsync(", StringComparison.Ordinal));

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            Assert.True(json.Contains("\"" + BeCoopCopy.WorkshopUnreadable + "\":", StringComparison.Ordinal), language + " is missing the unreadable Workshop Coop line");
        }
    }

    [Fact]
    public void The_client_follows_the_workshop_coop_instead_of_the_taom_pin()
    {
        var client = SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher");
        Assert.DoesNotContain("CoopStaging.ParkExtraCoop(", client);
        Assert.Contains("BeCoopCopy.ParkBesideWorkshop(_gameRoot, workshop, _setAsideMarker)", SourceFiles.Method(client, "private void ApplyCrashGuards()"));
        Assert.Contains("BeCoopCopy.ParkBesideWorkshop(gameRoot, workshop, marker)", client);
    }

    [Fact]
    public void A_lone_hotfix_asks_for_the_main_file_and_a_lone_main_file_is_refused_before_the_question()
    {
        var install = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher"), "private async void InstallOrRepair_Click(");
        Assert.Contains("BeArchive.SetProblem(found, gameRoot)", install);
        Assert.Contains("setProblem == BeArchive.MainFileMissing", install);
        Assert.Contains("PickBeDownload(contentProblem, archives, hotfixOnly)", install);
        var refuse = install.IndexOf("else if (setProblem != null)", StringComparison.Ordinal);
        Assert.True(refuse > 0);
        Assert.True(refuse < install.IndexOf("BeUiText.DownloadQuestion(", StringComparison.Ordinal));

        // Cancel stops: nothing is unpacked, so a lone hotfix never goes in alone, and no browser opens.
        var pick = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher"), "private IReadOnlyList<string>? PickBeDownload(");
        Assert.Contains("return null;", pick);
    }

    [Fact]
    public void Elephants_and_mcm_show_their_names_instead_of_module_ids()
    {
        var client = SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher");
        Assert.Contains("return BeStack.ElephantsName;", SourceFiles.Method(client, "private static string CoreName("));
        Assert.Contains("\"Turned the old Workshop copy of \" + ButrName(moduleId)", SourceFiles.Method(client, "private void RestoreRetiredMcm()"));
    }

    [Fact]
    public void The_delete_tooltip_names_the_one_locked_setup_in_every_language()
    {
        const string tip = "Deletes this setup. Your mods stay installed. The locked Burning Empires setup cannot be deleted.";
        var xaml = SourceFiles.Read(Path.Combine("src", "BurningEmpiresLauncher", "MainWindow.xaml"));
        Assert.Contains("ToolTip=\"" + tip + "\"", xaml);
        Assert.DoesNotContain("two locked setups", xaml);

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            foreach (var key in new[]
            {
                tip,
                BeArchive.MainFileMissing,
                BeArchive.HotfixMissing,
                "{0} The Burning Empires main file was not found.",
                "{0} from {1}. Your Burning Empires save data is not touched.",
                "{0}No Burning Empires download was found. Download the main file and the {1} hotfix from Nexus Mods into Downloads, then click Install or repair Burning Empires again."
            })
                Assert.True(json.Contains("\"" + key + "\":", StringComparison.Ordinal), language + " is missing " + key);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
