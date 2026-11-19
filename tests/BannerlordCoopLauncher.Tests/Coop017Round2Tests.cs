using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using BurningEmpires.Core;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Round 2 of the Coop 0.1.7 review (Claude and Devin): a lost picked server folder never downgrades the Burning
// Empires server, TAOM names a Modules Coop as such, and every get-it line stays a whole translated text.
public sealed class Coop017Round2Tests
{
    // A Coop server package unzipped by hand, the same layout as the Workshop DedicatedServer.
    private static string ManualServer(string root, string folder, string coopVersion)
    {
        var workshop = WorkshopServer(Path.Combine(root, folder));
        var server = Path.Combine(root, folder, "CoopServer");
        Directory.Move(workshop, server);
        Module(Path.Combine(server, "engine", "Modules"), "Coop", "Coop", coopVersion);
        Write(Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop core " + coopVersion);
        return server;
    }

    [Fact]
    public void A_picked_server_folder_deleted_after_prepare_never_downgrades_the_server()
    {
        var root = NewRoot("be-picked-gone");
        try
        {
            var workshop = WorkshopServer(root);
            var picked = ManualServer(root, "manual", "v0.1.7");
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(picked, server, picked: true);
            Directory.Delete(Path.GetDirectoryName(picked)!, recursive: true);

            // The source falls back to the Workshop (0.1.6), and the host is told why.
            Assert.True(BeServerPrepare.PickedGone(picked));
            Assert.Equal(new BeServerSource(workshop, Picked: false), BeServerPrepare.Source(picked, workshop));
            // Start server's Workshop re-copy sees a changed Coop, but the server's own is newer, so it stays.
            Assert.True(BeServerPrepare.CoopChanged(workshop, server));
            Assert.True(BeServerPrepare.ServerIsNewerThan(server, workshop));
            Assert.Contains("v0.1.7", BeServerPrepare.KeptNewerNote(server, workshop));

            var refresh = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost"), "private async Task<bool> RefreshServerFolderAsync(");
            var keep = refresh.IndexOf("BeServerPrepare.ServerIsNewerThan(server, workshop)", StringComparison.Ordinal);
            Assert.True(keep >= 0 && keep < refresh.IndexOf("BeServerPrepare.Prepare(workshop, server", StringComparison.Ordinal));
            Assert.Contains("ForgetGonePick(NoteStart);", refresh);
            Assert.Contains("ForgetGonePick(NoteLog);", SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost"), "private async Task<bool> PrepareServerAsync("));
            Assert.False(BeServerPrepare.PickedGone(""));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void An_unreadable_coop_version_is_unknown_not_older()
    {
        var root = NewRoot("be-unknown-version");
        try
        {
            var workshop = WorkshopServer(root);
            Module(Path.Combine(workshop, "engine", "Modules"), "Coop", "Coop", "v0.1.8");
            var picked = ManualServer(root, "manual", "garbled");

            // A picked server whose version can't be read stays the source.
            Assert.Equal(new BeServerSource(picked, Picked: true), BeServerPrepare.Source(picked, workshop));
            Assert.False(CoopInstall.KnownNewer("v0.1.8", "garbled"));
            Assert.False(CoopInstall.KnownNewer("", "v0.1.6"));
            Assert.True(CoopInstall.KnownNewer("v0.1.7", "0.1.6"));

            // A server folder with an unreadable version is not called newer, so Start updates it as before.
            var server = ManualServer(root, "server", "garbled");
            Assert.False(BeServerPrepare.ServerIsNewerThan(server, workshop));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_park_with_an_unreadable_workshop_version_keeps_the_newest_modules_coop()
    {
        var root = NewRoot("be-park-garbled");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "garbled");
            Module(modules, "Coop", "Coop", "v0.1.7");
            Module(modules, "Coop old", "Coop", "v0.1.6");

            var moved = BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName));

            Assert.Equal([Path.Combine(game, "Coop old-kept-0.1.6")], moved);
            Assert.True(Directory.Exists(Path.Combine(modules, "Coop")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Choose_coop_server_waits_for_a_running_server_and_says_when_the_workshop_is_newer()
    {
        var pick = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost"), "private void PickCoopServer_Click(");
        var guard = pick.IndexOf("if (ServerIsRunning())", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < pick.IndexOf("_settings.CoopServerFolder = ", StringComparison.Ordinal));
        Assert.Contains("The Steam Workshop Coop server is newer than the one in ", pick);
    }

    [Fact]
    public void Be_host_reports_send_the_servers_own_coop()
    {
        var root = NewRoot("be-report-coop");
        try
        {
            var server = ManualServer(root, "manual", "v0.1.7");
            Assert.Equal("v0.1.7", BeServerPrepare.ReportCoopVersion(server));
            Assert.Equal(BeStack.CoopVersion, BeServerPrepare.ReportCoopVersion(Path.Combine(root, "none")));
            Assert.Equal(BeStack.CoopVersion, BeServerPrepare.ReportCoopVersion(null));
            var host = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
            Assert.DoesNotContain("Coop = BeStack.CoopVersion,", host);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Taom_names_a_modules_coop_and_points_at_use_workshop_coop()
    {
        var taom = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        var check = SourceFiles.Method(taom, "private string? CoopVersionProblem()");
        Assert.Contains("InGameModules(xml)", check);
        Assert.Contains("CoopStaging.ModulesCoopNotPinned(", check);
        Assert.Contains("Open Settings and click Use Workshop Coop 0.1.6.", TaomCoop.Core.CoopStaging.ModulesCoopNotPinned("v0.1.7", "Coop"));
        Assert.Contains("InGameModules(xml) ? \"Coop \" + version + \" (Modules)\" : \"Coop \" + version + \" (Workshop)\"", taom);
        // The button the text names is real, on the Settings page since the redesign (wave 7).
        var xaml = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.xaml"));
        Assert.Contains("Content=\"Use Workshop Coop 0.1.6\"", xaml);
        Assert.True(xaml.IndexOf("Content=\"Use Workshop Coop 0.1.6\"", StringComparison.Ordinal) > xaml.IndexOf("x:Name=\"SettingsPage\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Get_it_lines_go_on_their_own_line_and_follow_the_one_flag()
    {
        // Coop 0.1.7 is on the Workshop since 2026-10-30, so the flag is off and every "not on Steam yet" line is gone.
        Assert.False(CoopInstall.NotOnSteamYet);
        Assert.Equal("a\nb", CoopInstall.AddLine("a", "b"));
        Assert.Equal("a", CoopInstall.AddLine("a", null));
        Assert.Null(CoopInstall.ClientGetLineNow);
        Assert.Equal("Click Install mods, then subscribe in Steam.", CoopInstall.ClientMissingLine);
        Assert.DoesNotContain("not on Steam yet", DedicatedServerFinder.MissingMessage);
        Assert.Null(CoopInstall.ServerGetLineNow("x"));

        // No get-it line is joined onto another sentence.
        foreach (var file in new[] { "src/BannerlordCoopLauncher.Core/AD1259/AD1259ModScan.cs", "src/BurningEmpiresHost/MainWindow.ServerControl.cs" })
        {
            var source = SourceFiles.Read(file);
            Assert.DoesNotContain("+ CoopInstall.ClientGetLine", source);
            Assert.DoesNotContain("\" \" + CoopInstall.ServerGetLine", source);
        }
    }

    [Fact]
    public void Ad1259_server_without_coop_names_the_workshop_again()
    {
        // With Coop 0.1.7 on the Workshop, the Workshop server has it, so the server download line is gone.
        var rows = AD1259ToolsTests.CompleteServerRows();
        rows.RemoveAll(row => row.Id == "Coop");
        var problem = AD1259Server.Problem(rows)!;
        Assert.Contains("Bannerlord Coop v0.1.7 (missing; get it from Steam Workshop)", problem);
        Assert.DoesNotContain(CoopInstall.ServerGetLine("Set server folder"), problem);
        Assert.DoesNotContain("BannerlordCoop-DedicatedServer", problem);

        var translations = BannerlordCoopLauncher.Core.Localization.Translator.Entries("de");
        Assert.True(translations.ContainsKey("{0} {1} (missing)"));
        Assert.True(translations.ContainsKey(BeServerPrepare.PickedGoneNote));
    }
}
