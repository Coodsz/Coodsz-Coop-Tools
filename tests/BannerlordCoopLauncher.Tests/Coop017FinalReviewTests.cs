using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using TaomCoop.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// The Codex final review of the Coop 0.1.7 round: the TAOM restore of Coop 0.1.6's Coop.Core.dll stays out of a
// Coop 0.1.7 folder, a local Coop 0.1.6 still gets other copies set aside, and the server repair notes translate.
[Collection(TranslatorLanguage.Name)]
public sealed class Coop017FinalReviewTests
{
    // A Coop module with an edited Coop.Core.dll and the .pre-password.bak Coop and Friends before 1.2.28 left next to
    // it. The test's own pin stands in for the released Coop 0.1.6 file, so the .bak looks pinned.
    private static (string Dll, string Pin) EditedCoop(string modules, string folder, string version)
    {
        var module = Module(modules, folder, "Coop", version);
        var dll = Write(Path.Combine(module, "bin", "Win64_Shipping_Client", "Coop.Core.dll"), "coop " + version);
        var backup = Write(dll + ".pre-password.bak", "released coop 0.1.6");
        return (dll, Sha256Files.Hash(backup));
    }

    [Theory]
    [InlineData("workshop")]
    [InlineData("modules")]
    public void A_coop_017_folder_keeps_its_own_coop_core_dll(string where)
    {
        var root = NewRoot("restore-017");
        try
        {
            var (dll, pin) = where == "workshop"
                ? EditedCoop(Path.Combine(root, "steamapps", "workshop", "content", "261550"), "3770450698", "v0.1.7")
                : EditedCoop(Path.Combine(root, "game", "Modules"), "Coop", "v0.1.7");

            var refused = Assert.Throws<InvalidOperationException>(() => CoopStaging.RestoreQualifiedDll(dll, pin));

            Assert.Equal(KitCoopServer.WithWhereToGet(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true)), refused.Message);
            Assert.Equal("coop v0.1.7", File.ReadAllText(dll));
            Assert.False(File.Exists(dll + ".coodsz-password"));
            Assert.True(CoopStaging.IsOtherCoopVersion(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)))));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_coop_016_folder_still_gets_the_released_file_back()
    {
        var root = NewRoot("restore-016");
        try
        {
            var (dll, pin) = EditedCoop(Path.Combine(root, "game", "Modules"), "Coop", "v0.1.6");

            Assert.True(CoopStaging.RestoreQualifiedDll(dll, pin));

            Assert.Equal("released coop 0.1.6", File.ReadAllText(dll));
            Assert.Equal("coop v0.1.6", File.ReadAllText(dll + ".coodsz-password"));
            Assert.False(CoopStaging.IsOtherCoopVersion(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)))));
            Assert.False(CoopStaging.RestoreQualifiedDll(dll, pin));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_client_skips_the_restore_for_another_coop_version()
    {
        var guards = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private void ApplyCrashGuards()");
        Assert.True(guards.IndexOf("CoopStaging.IsOtherCoopVersion(folder)", StringComparison.Ordinal)
            < guards.IndexOf("CoopStaging.RestoreQualifiedDll(dll)", StringComparison.Ordinal));
        Assert.Contains("CoopStaging.IsOtherCoopVersion(folder)", guards);
    }

    [Fact]
    public void A_local_coop_016_still_gets_the_other_copies_set_aside_while_the_workshop_is_newer()
    {
        var root = NewRoot("local-016");
        try
        {
            var game = Path.Combine(root, "game");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "v0.1.7");
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");

            // Only a Coop 0.1.7 in Modules: setting it aside can't help, so the Workshop's line shows.
            Assert.False(CoopStaging.HasLocalKitCoop(game));
            Assert.Equal(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7"), CoopParking.NothingToSetAsideFor(workshop, game));

            // A Coop 0.1.6 of the player's own next to it: the 0.1.7 copy is the one to set aside.
            Module(Path.Combine(game, "Modules"), "Coop-016", "Coop", "v0.1.6");
            Assert.True(CoopStaging.HasLocalKitCoop(game));
            Assert.Null(CoopParking.NothingToSetAsideFor(workshop, game));
            var extra = Assert.Single(CoopStaging.ExtraCoop(game));
            Assert.Equal("v0.1.7", extra.Version);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_github_coop_016_client_in_modules_coop_is_used_next_to_a_workshop_coop_017()
    {
        var root = NewRoot("github-016");
        try
        {
            // BannerlordCoop-v0.1.6.zip unzipped into the game's Modules folder, and Steam's Workshop on Coop 0.1.7.
            var game = Path.Combine(root, "game");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "v0.1.7");
            var coop = Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.6");
            var dll = Write(Path.Combine(coop, "bin", "Win64_Shipping_Client", "Coop.Core.dll"), "released coop 0.1.6");

            // The launcher's Coop check reads this copy, and nothing asks, moves or rewrites it.
            var active = CoopInstall.InModules(game);
            Assert.Equal(coop, active!.Directory);
            Assert.Equal(0, CoopInstall.Compare(active.Version, TaomStack.CoopVersion));
            Assert.True(CoopStaging.HasLocalKitCoop(game));
            Assert.Null(CoopParking.NothingToSetAsideFor(workshop, game));
            Assert.Empty(CoopStaging.ExtraCoop(game));
            Assert.Empty(CoopInstall.LayoutProblems(game, TaomStack.CoopVersion));
            Assert.False(CoopStaging.IsOtherCoopVersion(coop));
            Assert.False(CoopStaging.RestoreQualifiedDll(dll, Sha256Files.Hash(dll)));
            Assert.Equal("released coop 0.1.6", File.ReadAllText(dll));

            // Use Workshop Coop 0.1.6 says that copy already loads first, instead of the zip advice to move it out.
            var lines = new List<string>();
            CoopStaging.UsePlaytestCoop(workshop, game, new SyncProgress(lines));
            Assert.Equal(["Coop 0.1.6 is already in the game's Modules folder. It loads before the Workshop's Coop, so nothing needs to change."], lines);
            Assert.True(File.Exists(Path.Combine(coop, "SubModule.xml")));

            // A 0.1.7 in Modules\Coop next to the 0.1.6 in another folder still goes out, and the 0.1.6 stays (Codex).
            var own = Path.Combine(game, "Modules", "Coop-016");
            Directory.Move(coop, own);
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");
            lines.Clear();
            CoopStaging.UsePlaytestCoop(workshop, game, new SyncProgress(lines));
            Assert.False(Directory.Exists(coop));
            Assert.True(File.Exists(Path.Combine(own, "SubModule.xml")));
            Assert.StartsWith("Moved Coop", lines[0]);
            Assert.EndsWith(" out of Modules. The game now uses the Coop 0.1.6 in Modules.", lines[0]);
            Assert.Equal("Coop 0.1.6 in the game's Modules folder is ready. It loads before the Workshop's Coop.", lines[^1]);
            Directory.Move(own, coop);

            // Without that copy the button still stops with the zip advice and moves nothing.
            Directory.Delete(coop, recursive: true);
            var refused = Assert.Throws<InvalidOperationException>(() => CoopStaging.UsePlaytestCoop(workshop, game, new SyncProgress(lines)));
            Assert.Equal(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7"), refused.Message);
        }
        finally
        {
            Delete(root);
        }
    }

    // Progress<T> posts to the thread pool, so the lines would come late; this one adds them at once.
    private sealed class SyncProgress(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    [Fact]
    public void A_newer_workshop_coop_tells_a_player_to_unzip_the_github_coop_016_client_and_a_host_to_get_the_server()
    {
        var player = CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7");
        Assert.StartsWith("The Workshop Coop is Coop v0.1.7. TAOM.CoopCompat 0.3.25 needs Coop 0.1.6.", player);
        Assert.Contains("Coop's GitHub v0.1.6 release", player);
        Assert.Contains("BannerlordCoop-v0.1.6.zip", player);
        Assert.Contains("so SubModule.xml sits in Modules\\Coop", player);
        Assert.DoesNotContain("waits for", player);
        Assert.DoesNotContain(" - ", player);

        // A host gets the Coop 0.1.6 dedicated server advice (KitCoopServer.WhereToGet), never the client zip.
        var host = CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true);
        Assert.DoesNotContain("BannerlordCoop-v0.1.6.zip", host);
        Assert.DoesNotContain("waits for", host);
        var root = NewRoot("newer-host");
        try
        {
            Assert.Equal(host, CoopStaging.NewerCoopProblem(Module(root, "3770450698", "Coop", "v0.1.7"), server: true));
        }
        finally
        {
            Delete(root);
        }

        // Get Coop 0.1.6 on the Next step card shows only with this problem and opens Coop's GitHub v0.1.6 release.
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        Assert.Contains("GetCoopButton.Visibility = items.Any(item => item.GetCoop)", SourceFiles.Method(window, "private void ShowNextStep(bool connectReady)"));
        Assert.Contains("CoopVersionProblem(out newer)", SourceFiles.Method(window, "private List<AttentionItem> AttentionItems()"));
        Assert.Contains("KitCoopServer.GitHubRelease", SourceFiles.Method(window, "private void GetCoop_Click("));
        Assert.Contains("x:Name=\"GetCoopButton\" Content=\"Get Coop 0.1.6\"", SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.xaml")));
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = Translator.Entries(language);
            foreach (var key in new[] { "Get Coop 0.1.6", "Opens Coop's GitHub v0.1.6 release. Download BannerlordCoop-v0.1.6.zip and unzip it into the game's Modules folder." })
                Assert.True(table.ContainsKey(key), language + " is missing: " + key);
        }
    }

    [Fact]
    public void The_launcher_passes_the_game_folder_to_the_set_aside_gate()
    {
        var setAside = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private void SetAsideExtraCoop()");
        Assert.Contains("CoopParking.NothingToSetAsideFor(CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries()), _gameRoot)", setAside);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void Several_server_repair_notes_are_one_line_each_and_all_translate(string language)
    {
        var root = NewRoot("server-notes");
        var before = Translator.Current;
        try
        {
            // The Coop server bin and 0Harmony.dll both come from the Workshop client folder, so two notes at once.
            var item = Path.Combine(root, "workshop", "content", "261550", "3770450698");
            var server = Path.Combine(item, "DedicatedServer");
            var clientBin = Path.Combine(item, "bin", "Win64_Shipping_Client");
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("server-bytes"u8.ToArray())).ToLowerInvariant();
            Write(Path.Combine(server, "release-info.txt"), "Pinned Coop assemblies (SHA-256):\r\n  " + "Coop.Core.dll".PadRight(18) + " " + hash + "\r\n");
            Write(Path.Combine(clientBin, "Coop.Core.dll"), "server-bytes");
            Write(Path.Combine(clientBin, "0Harmony.dll"), "harmony");

            var message = CoopServerBin.Ensure(server, null)!;

            var notes = message.Split('\n');
            Assert.Equal(2, notes.Length);
            Assert.Contains(CoopServerBin.HarmonyRestored, notes);
            Translator.Use(language);
            var shown = Translator.T(message);
            foreach (var note in notes)
            {
                Assert.NotEqual(note, Translator.T(note));
                Assert.DoesNotContain(note, shown);
            }
        }
        finally
        {
            Translator.Use(before);
            Delete(root);
        }
    }

    [Fact]
    public void The_server_tool_logs_each_repair_note_on_its_own_line()
    {
        foreach (var file in new[] { "MainWindow.ServerControl.cs", "MainWindow.ModTest.cs" })
        {
            var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", file));
            Assert.Contains(".Split('\\n', StringSplitOptions.RemoveEmptyEntries))", source);
            Assert.DoesNotContain("Append(restored)", source);
        }
    }
}
