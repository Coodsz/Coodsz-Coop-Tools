using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Tests;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// A player report: Start server said engine\dotnet\dotnet.exe was missing and to click Prepare server.
// Prepare only checked that the Workshop Coop server had an engine folder, so a download without
// dotnet.exe "prepared" fine and Start failed again. Now Prepare refuses such a Workshop copy with the
// resubscribe step, a file taken again right after the copy names antivirus, and Start server prepares
// a folder that is not ready by itself.
public class BePrepareEngineTests
{
    [Theory]
    [InlineData("dotnet")]
    [InlineData("starter")]
    public void A_Workshop_Coop_server_without_its_engine_files_is_refused_with_the_resubscribe_step(string gone)
    {
        var root = NewRoot("prepare-engine");
        try
        {
            var workshop = WorkshopServer(root);
            File.Delete(gone == "dotnet"
                ? Path.Combine(workshop, "engine", "dotnet", "dotnet.exe")
                : Path.Combine(workshop, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.dll"));
            var server = Path.Combine(root, "server");

            var error = Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(workshop, server));

            Assert.StartsWith("The Bannerlord Coop Workshop download is incomplete.", error.Message);
            Assert.Contains(gone == "dotnet" ? @"engine\dotnet\dotnet.exe is missing" : "TaleWorlds.Starter.DotNetCore.dll is missing", error.Message);
            Assert.Contains("unsubscribe from Bannerlord Coop and subscribe again", error.Message);
            Assert.False(Directory.Exists(Path.Combine(server, "engine")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_whole_Workshop_Coop_server_still_prepares_and_the_server_folder_can_start()
    {
        var root = NewRoot("prepare-engine-ok");
        try
        {
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, server);
            Assert.Null(BannerlordCoopLauncher.Core.Launch.DedicatedEngineLaunch.MissingPiece(server));
        }
        finally
        {
            Delete(root);
        }
    }

    // A host report (CR-88BP7): the client said Coop Ready, the host had just run BannerlordCoopServer.exe from
    // the Workshop folder, then Prepare server said to subscribe to Coop. The server program was gone. It is
    // called antivirus when the watch saw it after a good Prepare or start, else an incomplete download.
    [Fact]
    public void A_Workshop_Coop_without_its_server_program_points_at_antivirus_not_at_subscribing()
    {
        var root = NewRoot("prepare-server-exe");
        try
        {
            var library = Path.Combine(root, "Steam");
            Assert.Equal(BeServerPrepare.NoWorkshopServer, BeServerPrepare.WorkshopServerProblem([library]));
            Assert.Equal(AntivirusText.NoWorkshopCoop("Prepare server"), BeServerPrepare.NoWorkshopServer);

            var item = Path.Combine(library, "steamapps", "workshop", "content", "261550", "3770450698");
            Write(Path.Combine(item, "SubModule.xml"), "<Module/>");
            var exe = Path.Combine(item, "DedicatedServer", "BannerlordCoopServer.exe");
            Write(exe, "exe");
            Write(Path.Combine(item, "DedicatedServer", "engine", "dotnet", "dotnet.exe"), "dotnet");
            Write(Path.Combine(item, "DedicatedServer", "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.dll"), "starter");
            Assert.NotNull(BeServerPrepare.FindWorkshopServer([library]));
            Assert.Null(BeServerPrepare.WorkshopServerProblem([library]));

            // A good Prepare remembers the Workshop server files, then antivirus takes the exe.
            var watch = new AntivirusWatch(Path.Combine(root, "data", AntivirusWatch.FileName));
            var server = Path.Combine(root, "server");
            watch.Remember([server, item], BeServerPrepare.WatchedFiles(server, item, null));
            File.Delete(exe);
            Assert.Null(BeServerPrepare.FindWorkshopServer([library]));
            var taken = BeServerPrepare.WorkshopServerProblem([library], watch.Gone([item]));
            Assert.Equal(AntivirusText.WorkshopTaken(@"DedicatedServer\BannerlordCoopServer.exe"), taken);
            Assert.Contains("unsubscribe from Bannerlord Coop in Steam and subscribe again", taken);

            // A start from the server folder's own copy still works then. It leaves the broken Workshop Coop out
            // of what it remembers (RememberServerFiles), so the next Prepare still says antivirus.
            Assert.NotNull(AntivirusFiles.WorkshopServerProblem(item, BeServerPrepare.PrepareButton));
            watch.Remember([server, null], BeServerPrepare.WatchedFiles(server, null, null));
            Assert.Contains(Path.GetFullPath(exe), watch.Gone([item]), StringComparer.OrdinalIgnoreCase);
            Assert.Contains("AntivirusFiles.WorkshopServerProblem(workshop, BeServerPrepare.PrepareButton) != null", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.Antivirus.cs")));

            // Never seen by the watch: an incomplete download, still not subscribe.
            Assert.Equal(AntivirusText.WorkshopIncomplete(@"DedicatedServer\BannerlordCoopServer.exe", "Prepare server"), BeServerPrepare.WorkshopServerProblem([library]));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void An_engine_file_gone_right_after_the_copy_points_at_antivirus()
    {
        var text = BeServerPrepare.EngineTakenText(@"C:\BurningEmpires-Coop-Host\Server\engine\dotnet\dotnet.exe", @"C:\BurningEmpires-Coop-Host\Server");
        Assert.Equal(AntivirusText.Taken(@"engine\dotnet\dotnet.exe", "Prepare server"), text);
        Assert.Contains("Fix antivirus", text);

        // Prepare checks the copy right after it, and only after it checked the Workshop copy.
        var prepare = SourceFiles.Read(Path.Combine("src", "BurningEmpires.Core", "BeServerPrepare.cs"));
        var workshopCheck = prepare.IndexOf("throw new InvalidOperationException(WorkshopEngineMissing(workshopServer, workshopMissing));", StringComparison.Ordinal);
        var copyCheck = prepare.IndexOf("throw new InvalidOperationException(EngineTakenText(taken, serverRoot));", StringComparison.Ordinal);
        Assert.True(workshopCheck > 0 && copyCheck > workshopCheck);
    }

    [Fact]
    public void Start_server_prepares_a_server_folder_that_is_not_ready_instead_of_sending_the_host_to_Prepare()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        var start = SourceFiles.Method(window, "private async Task StartServerCoreAsync(");
        var check = start.IndexOf("BeServerPrepare.MissingPiece(server)", StringComparison.Ordinal);
        var prepare = start.IndexOf("if (!await PrepareServerAsync(automatic: true))", StringComparison.Ordinal);
        var again = start.IndexOf("missingPiece = BeServerPrepare.MissingPiece(server);", StringComparison.Ordinal);
        var refuse = start.IndexOf("SetControl(missingPiece, failed: true);", StringComparison.Ordinal);
        Assert.True(check > 0 && prepare > check && again > prepare && refuse > again, "check, prepare, check again, then refuse");
        // Prepare server's click runs the same work after its busy check.
        Assert.Contains("await PrepareServerAsync();", SourceFiles.Method(window, "private async void Prepare_Click("));
        // Only a Start server click prepares; the automatic restarts keep the Click Prepare server line.
        Assert.Contains("missingPiece != null && prepareIfNeeded", start);
        Assert.Contains("await StartServerAsync(prepareIfNeeded: true, automatic);", SourceFiles.Method(window, "private async void Start_Click("));
        Assert.DoesNotContain("prepareIfNeeded: true", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.Rescue.cs")));
        // Prepare with Burning Empires or a helper mod still missing stops Start with that reason.
        Assert.Contains("prepared = problems.Count == 0;", SourceFiles.Method(window, "private async Task<bool> PrepareServerAsync("));
        // A Workshop download still in progress keeps a ready server folder as it is.
        Assert.Contains("done.Add(BeServerPrepare.WorkshopUnfinishedNote);", SourceFiles.Method(window, "private async Task<bool> RefreshServerFolderAsync("));
    }

    // Grok and Devin review: a Start click while Prepare server still copied ran a second Prepare on the
    // same folder, since Start only checked _startingServer.
    [Fact]
    public void Start_server_waits_while_Prepare_server_or_another_action_runs()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        var click = SourceFiles.Method(window, "private async void Start_Click(");
        var wait = click.IndexOf("if (_busy || _startingServer)", StringComparison.Ordinal);
        Assert.True(wait > 0 && wait < click.IndexOf("await StartServerAsync(prepareIfNeeded: true, automatic);", StringComparison.Ordinal));
        // Restart after Save and stop, and Last good save, start only after they cleared _busy themselves.
        foreach (var name in new[] { "private async Task SaveThenStop(", "private async void RestartLastGood_Click(" })
        {
            var method = SourceFiles.Method(window, name);
            var start = method.IndexOf("Start_Click(this, new RoutedEventArgs());", StringComparison.Ordinal);
            Assert.True(start > method.LastIndexOf("_busy = false;", StringComparison.Ordinal), name);
        }
    }

    // Grok and Devin review: Start judged a Coop update still downloading by the Workshop engine files alone,
    // so an update that already had them was copied half done, and an engine file antivirus took after the
    // download made every start wait forever.
    [Fact]
    public void Start_after_a_Coop_update_waits_only_while_Steam_downloads_and_names_a_broken_Workshop_copy()
    {
        var refresh = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost"), "private async Task<bool> RefreshServerFolderAsync(");
        var downloading = refresh.IndexOf("AntivirusFiles.SteamUpdating(", StringComparison.Ordinal);
        var problem = refresh.IndexOf("BeServerPrepare.WorkshopServerProblem(libraries, GoneWorkshopFiles(libraries))", StringComparison.Ordinal);
        var prepare = refresh.IndexOf("BeServerPrepare.Prepare(workshop, server", StringComparison.Ordinal);
        Assert.True(downloading > 0 && problem > downloading && prepare > problem, "Steam download, then the Workshop check, then the copy");
        Assert.DoesNotContain("DedicatedEngineLaunch.MissingPiece(workshop)", refresh);
        Assert.Equal(1, refresh.Split("BeServerPrepare.CoopChanged(workshop, server)").Length - 1);
    }
}
