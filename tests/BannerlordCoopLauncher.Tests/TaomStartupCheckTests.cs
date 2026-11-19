using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomStartupCheckTests
{
    private const string LiveLine = "[DedicatedServer] FATAL during a: System.InvalidOperationException: TAOM.CoopCompat rejected AssertPreServingReady: TAOM.CoopCompat pre-serving attestation failed: module=True, resolverAction=True, wrapper=True, facegen=True, monsters=False, servingHardStop=True, faulted=False";

    [Theory]
    [InlineData(LiveLine)]
    [InlineData("TAOM.CoopCompat pre-serving attestation failed: monsters=False")]
    [InlineData("System.InvalidOperationException: TAOM.CoopCompat rejected AssertPreServingReady")]
    public void The_startup_check_stop_line_is_seen(string line) =>
        Assert.True(TaomStartupCheck.IsStopLine(line));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[DedicatedServer] FATAL during a: System.NullReferenceException: Object reference not set")]
    [InlineData("TAOM.CoopCompat pre-serving attestation passed")]
    [InlineData("[DedicatedServer] SERVING")]
    public void Other_lines_are_not_the_startup_check(string? line) =>
        Assert.False(TaomStartupCheck.IsStopLine(line));

    [Fact]
    public void The_window_report_says_the_save_is_fine_while_the_server_still_runs()
    {
        var report = TaomHostReport.Explain(LiveLine, wasServing: false, exited: false);

        Assert.NotNull(report);
        Assert.Equal(TaomStartupCheck.Text, report.Value.Status);
        Assert.Equal([TaomStartupCheck.Text], report.Value.Lines);
        Assert.Contains("Your save is fine", report.Value.Status);
        // Player text: no colons, no hyphens between words.
        Assert.DoesNotContain(":", TaomStartupCheck.Text);
        Assert.DoesNotMatch(@"\w-\w", TaomStartupCheck.Text);
    }

    // Player report CR-HZYX5: antivirus took a TAOM.CoopCompat DLL from the server bin.
    private const string MissingDllLine = "[BannerlordCoopServer] FATAL: TAOM.CoopCompat preflight failed: required TAOM.CoopCompat feature DLL is missing";

    [Fact]
    public void The_missing_compat_dll_line_is_a_startup_check_stop()
    {
        Assert.True(TaomStartupCheck.IsStopLine(MissingDllLine));
        Assert.True(TaomStartupCheck.IsMissingDllLine(MissingDllLine));
        Assert.False(TaomStartupCheck.IsMissingDllLine(LiveLine));
        Assert.False(TaomStartupCheck.IsMissingDllLine("TAOM.CoopCompat preflight passed"));
    }

    [Fact]
    public void The_missing_compat_dll_report_names_start_server_and_fix_antivirus()
    {
        var report = TaomHostReport.Explain("[BannerlordCoopServer] starting\n" + MissingDllLine + "\nengine exited with code 2", wasServing: false, exited: false);

        Assert.NotNull(report);
        Assert.True(report.Value.Failed);
        Assert.Equal(TaomStartupCheck.MissingDllText, report.Value.Status);
        Assert.Equal([TaomStartupCheck.MissingDllText], report.Value.Lines);
        Assert.Contains("Press Start server", TaomStartupCheck.MissingDllText);
        Assert.Contains("click Fix antivirus", TaomStartupCheck.MissingDllText);
        Assert.Contains("Your save is fine", TaomStartupCheck.MissingDllText);
        Assert.DoesNotContain(":", TaomStartupCheck.MissingDllText);
        Assert.DoesNotMatch(@"[a-z]-[a-z]", TaomStartupCheck.MissingDllText);
        var xaml = SourceFiles.Read("src/TaomCoopHost/MainWindow.xaml");
        Assert.Contains("Content=\"Start server\"", xaml);
        Assert.Contains("Content=\"Fix antivirus\"", xaml);
        // The attestation stop keeps its own text.
        Assert.Equal(TaomStartupCheck.Text, TaomHostReport.Explain(LiveLine, false, exited: false)!.Value.Status);
    }

    // The report reads each line on its own, like the server log watch: two phrases on different lines are no stop.
    [Fact]
    public void The_report_reads_the_log_line_by_line()
    {
        var split = "[BannerlordCoopServer] TAOM.CoopCompat preflight failed\n[Coop] a feature DLL is missing from the cache, loading it\nengine exited with code 0";
        Assert.Null(TaomStartupCheck.Report(split));
        Assert.Null(TaomStartupCheck.Report(null));
        var attestation = TaomStartupCheck.Report("loading\r\n" + LiveLine + "\r\nexit");
        Assert.Equal(TaomStartupCheck.Text, attestation!.Value.Status);
        var missing = TaomStartupCheck.Report("loading\r\n" + MissingDllLine + "\r\nexit");
        Assert.Equal(TaomStartupCheck.MissingDllText, missing!.Value.Status);
    }

    // Player report CR-XXP67: the server's TAOM.CoopCompat.dll was the one of the new TAOM download for 1.5.7, then
    // after a repair attempt its SubModule.xml was gone.
    private const string NewModuleLine = "[BannerlordCoopServer] FATAL: TAOM.CoopCompat preflight failed: TAOM.CoopCompat feature DLL hash FC45CB1294C77AAD1557B1DFB98F0122B7CD4A8D2553514482EB34A6280AE4D8 does not match the wrapper pin E0B27271032D062DE45A0E29A31C52A278EF9680A80AC11EBD1311FE6D84B21A";
    private const string ChangedLine = "[BannerlordCoopServer] FATAL: TAOM.CoopCompat preflight failed: TAOM.CoopCompat feature DLL hash 0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF does not match the wrapper pin E0B27271032D062DE45A0E29A31C52A278EF9680A80AC11EBD1311FE6D84B21A";
    private const string DescriptorLine = "[BannerlordCoopServer] FATAL: TAOM.CoopCompat preflight failed: required TAOM.CoopCompat module descriptor is missing";

    [Fact]
    public void A_changed_or_new_compat_file_gets_its_own_line()
    {
        Assert.Equal(NewTaomCompat.Text(null), TaomStartupCheck.FileText(NewModuleLine));
        Assert.Equal(NewTaomCompat.Text(null), TaomStartupCheck.FileText(NewModuleLine.ToLowerInvariant().Replace("taom.coopcompat preflight failed", "TAOM.CoopCompat preflight failed")));
        Assert.Equal(TaomStartupCheck.ChangedText, TaomStartupCheck.FileText(ChangedLine));
        Assert.Equal(TaomStartupCheck.ChangedText, TaomStartupCheck.FileText(DescriptorLine));
        // The report quoted the descriptor stop without the preflight prefix; it counts either way.
        Assert.Equal(TaomStartupCheck.ChangedText, TaomStartupCheck.FileText("[BannerlordCoopServer] FATAL: required TAOM.CoopCompat module descriptor is missing"));
        Assert.Equal(TaomStartupCheck.MissingDllText, TaomStartupCheck.FileText(MissingDllLine));
        Assert.Null(TaomStartupCheck.FileText(LiveLine));
        Assert.Null(TaomStartupCheck.FileText("[Coop] module descriptor is missing from the cache"));
        Assert.All(new[] { NewModuleLine, ChangedLine, DescriptorLine }, line => Assert.True(TaomStartupCheck.IsStopLine(line)));

        var report = TaomHostReport.Explain("[BannerlordCoopServer] starting\n" + NewModuleLine + "\nengine exited with code 2", wasServing: false, exited: false);
        Assert.Equal(NewTaomCompat.Text(null), report!.Value.Status);
        Assert.True(report.Value.Failed);
        Assert.Equal(TaomStartupCheck.ChangedText, TaomStartupCheck.Report("loading\r\n" + DescriptorLine + "\r\nexit")!.Value.Status);
        Assert.Contains("Press Start server", TaomStartupCheck.ChangedText);
        Assert.Contains("click Fix antivirus", TaomStartupCheck.ChangedText);
        Assert.Contains("Your save is fine", TaomStartupCheck.ChangedText);
        Assert.DoesNotContain(":", TaomStartupCheck.ChangedText);
        Assert.DoesNotMatch(@"[a-z]-[a-z]", TaomStartupCheck.ChangedText);
    }

    [Fact]
    public void The_host_shows_the_missing_dll_text_when_the_stop_was_a_missing_dll()
    {
        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        Assert.Contains("_startCheckFileText = null;", SourceFiles.Method(taom, "private void BeginRescueRun()"));
        Assert.Contains("if (TaomStartupCheck.FileText(line) is { } fileText)", SourceFiles.Method(taom, "private void CaptureLine(string? line)"));
        Assert.Contains("SetControl(_startCheckFileText ?? TaomStartupCheck.Text, failed: true);",
            SourceFiles.Method(taom, "private bool IsStartupCheckStop(int? exitCode)"));
    }

    [Fact]
    public void The_host_skips_the_load_mode_retry_and_resets_the_flag_on_every_start()
    {
        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        Assert.Contains("_startCheckStop = false;", SourceFiles.Method(taom, "private void BeginRescueRun()"));
        Assert.Contains("if (TaomStartupCheck.IsStopLine(line))", SourceFiles.Method(taom, "private void CaptureLine(string? line)"));
        var exited = taom[taom.IndexOf("if (IsStartupCheckStop(exitCode))", StringComparison.Ordinal)..];
        var retry = exited.IndexOf("OnRescueExit(process, exitCode, loadStep);", StringComparison.Ordinal);
        Assert.True(exited.IndexOf("else", StringComparison.Ordinal) < retry, "OnRescueExit must only run when it is no startup check stop.");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
