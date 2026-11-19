using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// Crash report #32: the server window still showed an earlier run, and its Steamworks error was named as the
// cause of a start the old MCM check had refused. The crash line now comes from this run's lines only.
public sealed class ServerCrashLineTests
{
    [Fact]
    public void The_crash_bar_reads_only_this_runs_lines()
    {
        var reports = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.CrashReports.cs"));
        Assert.Contains("CrashReports.FindCrashLine(string.Join('\\n', _runLines))", reports);
        Assert.DoesNotContain("FindCrashLine(ColoredConsole.Read(ServerLog))", reports);

        var log = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerLog.cs"));
        // The shown line: after streamer masking and after the formatter drops startup noise.
        var append = log.IndexOf("private void Append(string line)", StringComparison.Ordinal);
        var note = log.IndexOf("NoteRunLine(line);", append, StringComparison.Ordinal);
        Assert.True(append > 0 && note > log.IndexOf("line = ForScreen(line);", append, StringComparison.Ordinal)
            && note > log.IndexOf("is not LogLineFormatter.Entry entry", append, StringComparison.Ordinal));
        var control = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerControl.cs"));
        var start = control.IndexOf("private void StartServer()", StringComparison.Ordinal);
        // First after the two refusals (a running server, a mod copy), which start no run.
        Assert.True(start > 0 && control.IndexOf("_runLines.Clear();", start, StringComparison.Ordinal) is var clear && clear > start && clear < start + 400,
            "Start server clears the run lines first.");
    }

    [Fact]
    public void The_refused_start_is_the_crash_line_once_the_old_run_is_gone()
    {
        const string oldRun = "Note 01:06:49.186 System.InvalidOperationException: Steamworks GameServer is not initialized.";
        const string thisRun = "Mod Configuration Menu v5.11.4 is too old for the dedicated server and crashes it.";
        Assert.Contains("Steamworks", CrashReports.FindCrashLine(oldRun + "\n" + thisRun));
        Assert.Contains("too old for the dedicated server", CrashReports.FindCrashLine(thisRun));
    }
}
