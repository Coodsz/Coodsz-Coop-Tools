using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Tests;

namespace BurningEmpires.Core.Tests;

// Release review group A, the exits on the script route. A Start-ModdedServer.ps1 refusal and a new
// campaign that crashed before SERVING were both retried as a save load crash, Start anyway on the save
// mods card could never start, and the exit reports waited for a line the script never prints.
public class BeScriptExitTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Window() => SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");

    private static string ExitedHandler()
    {
        var source = Window();
        var start = source.IndexOf("process.Exited += (_, _) =>", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return source[start..source.IndexOf("// A new start follows only its own state events.", start, StringComparison.Ordinal)];
    }

    [Fact]
    public void TheScriptsRefusalsCountAsARefusalAndASaveMismatchKeepsItsStatus()
    {
        // The lines the script throws before the engine starts (Start-ModdedServer.ps1).
        Assert.True(BeServerLaunch.IsScriptRefusal("Save 'BETest' was not found or is incompatible with the selected module set."));
        Assert.True(BeServerLaunch.IsScriptRefusal("Configured save 'BETest' was not found or is incompatible with the selected module set."));
        Assert.True(BeServerLaunch.IsScriptRefusal("The fresh-campaign template is missing: C:\\x\\default_new_game.sav"));
        Assert.True(BeServerLaunch.IsScriptRefusal("A server campaign named 'BETest' already exists. Choose another name."));
        Assert.False(BeServerLaunch.IsScriptRefusal("[DedicatedServer] SERVING"));
        Assert.False(BeServerLaunch.IsScriptRefusal(BeHostReport.ExitLine(1)));
        // The status the refusal sets is the one the exit report for that save sets, so neither hides the other.
        Assert.Equal(BeHostReport.SaveMismatchStatus, BeServerLaunch.ScriptRefusal("Save 'BETest' was not found or is incompatible with the selected module set."));
        var report = BeHostReport.Explain("Save 'BETest' was not found or is incompatible with the selected module set.\n", wasServing: false);
        Assert.Equal(BeHostReport.SaveMismatchStatus, report?.Status);
    }

    [Fact]
    public void AScriptRefusalGetsNoCrashBarAndNoSaveFixRetry()
    {
        var source = Window();
        var watch = SourceFiles.Method(source, "private void WatchRescueReport(string line)");
        Assert.Contains("if (!_servedThisRun && BeServerLaunch.IsScriptRefusal(line))", watch);
        Assert.Contains("_scriptRefused = false;", SourceFiles.Method(source, "private void BeginRescueRun()"));
        var stop = SourceFiles.Method(source, "private bool IsScriptRefusedStop()");
        Assert.Contains("if (_stopAsked || !_scriptRefused)", stop);

        var exited = ExitedHandler();
        var guard = exited.IndexOf("if (!IsScriptRefusedStop())", StringComparison.Ordinal);
        Assert.True(guard > 0);
        Assert.True(guard < exited.IndexOf("OfferServerCrashReport(process);", StringComparison.Ordinal));
        Assert.True(guard < exited.IndexOf("OnRescueExit(process, exitCode, loadStep);", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewCampaignThatCrashesBeforeServingIsNotRetriedAsASaveLoad()
    {
        var source = Window();
        Assert.Contains("_loadRetry.Decide(exit, _servedThisRun, _rescueRun.Mode, loadedASave: !_runCreatesCampaign)", SourceFiles.Method(source, "private LoadCrashStep DecideLoadCrash(int? exitCode)"));
        var start = SourceFiles.Method(source, "private async Task StartServerCoreAsync(");
        var begin = start.IndexOf("BeginRescueRun();", StringComparison.Ordinal);
        var set = start.IndexOf("_runCreatesCampaign = newCampaign != null;", StringComparison.Ordinal);
        Assert.True(begin > 0 && set > begin && set < start.IndexOf("process.Start()", StringComparison.Ordinal));

        // What that gives: a crash of a new campaign build before SERVING is no load mode retry.
        var crash = ServerExitInfo.From(-1073741819, false);
        Assert.Equal(LoadCrashStep.LeftToCrashRestart, new LoadCrashRetry().Decide(crash, false, RescueMode.Auto, loadedASave: false));
        Assert.Equal(LoadCrashStep.RetryInLoadMode, new LoadCrashRetry().Decide(crash, false, RescueMode.Auto, loadedASave: true));
    }

    [Fact]
    public void TheExitReportsFireOnTheLineTheToolAddsAtTheExit()
    {
        const string served = "[DedicatedServer] SERVING\n09:40:00.000  [Server] Operational | online=0\n";
        // Without the tool's line there is no exit code in the text, so no exit report.
        Assert.NotEqual("The server stopped on its own.", BeHostReport.Explain(served, wasServing: true)?.Status);
        Assert.Equal("The server stopped on its own.", BeHostReport.Explain(served + BeHostReport.ExitLine(0) + "\n", wasServing: true)?.Status);
        Assert.Equal(BeHostReport.NoErrorStatus, BeHostReport.Explain(served + BeHostReport.ExitLine(-1) + "\n", wasServing: true)?.Status);
        Assert.Null(BeHostReport.CrashLine(served + BeHostReport.ExitLine(-1)));

        var exited = ExitedHandler();
        var added = exited.IndexOf("_logTail.Add(BeHostReport.ExitLine(code));", StringComparison.Ordinal);
        Assert.True(added > 0);
        Assert.True(added < exited.IndexOf("NoteHostReport(_logTail.ToString(), exited: true);", StringComparison.Ordinal));
        Assert.Contains("consoleLog.Write(exitCode is { } loggedCode ? BeHostReport.ExitLine(loggedCode) : \"[BE host] Server exited.\");", exited);
    }

    [Fact]
    public void TheSaveModsCardHasNoStartAnywayOnTheScriptRoute()
    {
        Assert.Contains("new SaveModsCard { ShowStartAnyway = false,", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "SaveModsWindow.cs")));
        var confirm = SourceFiles.Method(Window(), "private bool ConfirmSaveMatchesSetup(string savePath)");
        Assert.DoesNotContain("\"anyway\"", confirm);
        // Only the add mods question reaches the backup and the start, so the script gets -AllowAddedModules.
        Assert.Contains("_allowAddedModules = true;", confirm);
    }
}
