using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Tests;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// Release review group A, the server tool's script route. Prepare server copied the Workshop's
// Start-ModdedServer.ps1 as it is, which has no -AllowAddedModules (every Test mods and Add mods start
// stopped at PowerShell's parameter check), no any letter -NewSaveName rule and no Launcher Cheats.
// BE now lays Coodsz Host's own files over the server folder on Prepare and on every start.
public class BeServerScriptRouteTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Window() => SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");

    [Fact]
    public void PrepareLaysCoodszHostsOwnScriptAndLauncherSubmodulesOverTheWorkshopCopy()
    {
        var root = NewRoot("lay-over");
        try
        {
            var hostPayload = RepairPayload(root);
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");

            var result = BeServerPrepare.Prepare(workshop, server, null, hostPayload);

            var script = File.ReadAllText(Path.Combine(server, BeServerPrepare.ScriptName));
            Assert.Equal(File.ReadAllText(Path.Combine(hostPayload, BeServerPrepare.ScriptName)), script);
            // The switch Start passes after Add mods and on every Test mods start.
            Assert.Contains("[switch]$AllowAddedModules", script);
            // The script that runs is the one the name check reads, so YÜZÜK TEST is a fine new campaign.
            Assert.Null(BeServerLaunch.NewCampaignProblem("YÜZÜK TEST", Path.Combine(root, "saves"), BeServerLaunch.ReadServerScript(server)));
            var xml = File.ReadAllText(Path.Combine(server, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml"));
            Assert.Contains("DedicatedServer.LauncherCheats.dll", xml);
            Assert.Contains("DedicatedServer.LauncherBattleSize.dll", xml);
            Assert.NotNull(result.Repaired);
            Assert.Contains(BeServerPrepare.ToolName, result.Repaired);
            Assert.Contains(result.Repaired, result.Lines);
            Assert.Null(BeServerPrepare.MissingPiece(server));

            // Prepare again: the Workshop script is never put back, so it is not replaced a second time.
            BeServerPrepare.Prepare(workshop, server, null, hostPayload);
            Assert.Equal(script, File.ReadAllText(Path.Combine(server, BeServerPrepare.ScriptName)));
            Assert.False(File.Exists(Path.Combine(server, BeServerPrepare.ScriptName + ".coodsz-old")));
            Assert.Contains("DedicatedServer.LauncherCheats.dll", File.ReadAllText(Path.Combine(server, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml")));
        }
        finally
        {
            Delete(root);
        }
    }

    // raven (CR-X7WJQ): after a clean resubscribe Prepare server said "The Coop dedicated server was not found.
    // Subscribe..." because the Workshop build ships no Start-ModdedServer.ps1, while the tool lays its own over.
    [Fact]
    public void ACleanThinWorkshopDownloadPreparesWithTheToolsOwnScript()
    {
        var root = NewRoot("thin");
        try
        {
            var hostPayload = RepairPayload(root);
            var workshop = ThinWorkshopServer(root);
            var server = Path.Combine(root, "server");

            BeServerPrepare.Prepare(workshop, server, null, hostPayload);

            Assert.Equal(File.ReadAllText(Path.Combine(hostPayload, BeServerPrepare.ScriptName)), File.ReadAllText(Path.Combine(server, BeServerPrepare.ScriptName)));
            Assert.True(File.Exists(Path.Combine(server, BeServerPrepare.RuntimeBootstrap)));
            Assert.True(File.Exists(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll")));
            Assert.Null(BeServerPrepare.MissingPiece(server));

            // With no tool copy to lay over, the Workshop script is still needed.
            var refused = Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(workshop, Path.Combine(root, "server2")));
            Assert.Equal(BeServerPrepare.NoWorkshopServer, refused.Message);

            // A tool copy without its script (antivirus took it) is the tool's problem, not a missing subscription.
            var emptyPayload = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            var noToolScript = Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(workshop, Path.Combine(root, "server3"), null, emptyPayload));
            Assert.Equal(BeServerPrepare.ToolScriptMissing(emptyPayload), noToolScript.Message);
            Assert.Contains("Fix antivirus", noToolScript.Message);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AFolderPreparedWithTheWorkshopScriptGetsTheToolsCopyAndKeepsTheOldOne()
    {
        var root = NewRoot("lay-over-old");
        try
        {
            var hostPayload = RepairPayload(root);
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, server);
            var workshopScript = File.ReadAllText(Path.Combine(server, BeServerPrepare.ScriptName));

            var repaired = BeServerPrepare.LayOverRepair(server, hostPayload);

            Assert.NotNull(repaired);
            Assert.Contains("Updated Start-ModdedServer.ps1", repaired);
            Assert.Contains("[switch]$AllowAddedModules", File.ReadAllText(Path.Combine(server, BeServerPrepare.ScriptName)));
            Assert.Equal(workshopScript, File.ReadAllText(Path.Combine(server, BeServerPrepare.ScriptName + ".coodsz-old")));
            // The next start finds nothing to change.
            Assert.Null(BeServerPrepare.LayOverRepair(server, hostPayload));
            // No copy next to the tool: the folder stays as it is.
            Assert.Null(BeServerPrepare.LayOverRepair(server, Path.Combine(root, "no-payload")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void CoodszHostAndBurningEmpiresShareOneRepairAndOneSetOfFiles()
    {
        // Coodsz Host keeps its own entry point and wording; the work is in the shared class.
        var host = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "DedicatedServerRepair.cs"));
        Assert.Contains("ServerFolderRepair.Ensure(serverRoot, payloadRoot, Edition.Name)", host);
        Assert.DoesNotContain("File.Copy", host);

        // BE ships Coodsz Host's files, not a third copy, without Coodsz Host's policy and update installer.
        var csproj = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "BurningEmpiresHost.csproj"));
        Assert.Contains("<CoodszPayload Include=\"..\\CoodszHost\\DedicatedServerRepair\\**\\*\"", csproj);
        Assert.Contains("Payload=\"DedicatedServerRepair\"", csproj);
        Assert.Contains("DedicatedServerRepair\\server-module-policy.json", csproj);
        Assert.False(Directory.Exists(Path.Combine(RepoRoot(), "src", "BurningEmpiresHost", ServerFolderRepair.FolderName)));
        Assert.Contains("(Get-PayloadPairs (Join-Path $privateRoot 'DedicatedServerRepair') '')", SourceFiles.Read(Path.Combine("tools", "Make-BurningEmpires-Release.ps1")));
    }

    [Fact]
    public void PrepareStartAndTestModsLayTheFilesOverTheServerFolder()
    {
        var source = Window();
        Assert.Contains("ServerFolderRepair.DefaultPayload", SourceFiles.Method(source, "private async Task<bool> PrepareServerAsync("));
        Assert.Contains("BeServerPrepare.LayOverRepair(serverRoot)", SourceFiles.Method(source, "public async Task<IModTestRun> StartAsync("));

        var refresh = SourceFiles.Method(source, "private async Task<bool> RefreshServerFolderAsync(string server)");
        Assert.Contains("BeServerPrepare.CoopChanged(workshop, server)", refresh);
        Assert.Contains("BeServerPrepare.LayOverRepair(server)", refresh);

        // The files go in before the new campaign name is checked against the script, and the Coop
        // copy after a Coop update runs before Coop Save Rescue edits DedicatedServer.Windows (host-10).
        var start = SourceFiles.Method(source, "private async Task StartServerCoreAsync(");
        var refreshAt = start.IndexOf("await RefreshServerFolderAsync(server)", StringComparison.Ordinal);
        Assert.True(refreshAt > 0);
        Assert.True(refreshAt < start.IndexOf("InstallRescue(server)", StringComparison.Ordinal));
        Assert.True(refreshAt < start.IndexOf("BeServerLaunch.ReadServerScript(server)", StringComparison.Ordinal));
        Assert.DoesNotContain("BeServerPrepare.CoopChanged(", SourceFiles.Method(source, "private async Task<BeServerStart?> PrepareBeServerAsync("));
    }
}
