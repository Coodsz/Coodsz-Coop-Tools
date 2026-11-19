using System.IO.Compression;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// host/exit-evidence.txt after an exit code 1 nobody asked for (crash repo issue #12): the event
// log time window and which entries count, the last minute of the engine log, and the cleaning.
[Collection(ModReportsSession.Name)]
public sealed class ServerExitEvidenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-exit-evidence-" + Guid.NewGuid().ToString("N"));
    private readonly IReadOnlyList<string> _names = LogScrubber.PrivateNames;
    private static readonly DateTime ExitAt = new(2026, 10, 25, 22, 41, 10);

    public ServerExitEvidenceTests()
    {
        Directory.CreateDirectory(_directory);
        LogScrubber.PrivateNames = ["Patricia7", "DESKTOP-QX42ZZ"];
    }

    public void Dispose()
    {
        LogScrubber.PrivateNames = _names;
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_window_runs_from_2_minutes_before_to_1_minute_after_the_exit()
    {
        Assert.True(ServerExitEvidence.InWindow(ExitAt.AddMinutes(-2), ExitAt));
        Assert.True(ServerExitEvidence.InWindow(ExitAt, ExitAt));
        Assert.True(ServerExitEvidence.InWindow(ExitAt.AddMinutes(1), ExitAt));
        Assert.False(ServerExitEvidence.InWindow(ExitAt.AddMinutes(-2).AddSeconds(-1), ExitAt));
        Assert.False(ServerExitEvidence.InWindow(ExitAt.AddMinutes(1).AddSeconds(1), ExitAt));
    }

    [Fact]
    public void Server_crashes_defender_detections_and_low_memory_count_other_programs_do_not()
    {
        var entries = new[]
        {
            new EventLogEntryInfo(ExitAt.AddSeconds(2), "Windows Error Reporting", 1001, "Information", "P1: BannerlordCoopServer.exe"),
            new EventLogEntryInfo(ExitAt.AddSeconds(-20), "Microsoft-Windows-Windows Defender", 1117, "Information", "Defender took action on C:\\Users\\Patricia7\\x.exe", ServerExitEvidence.DefenderLog),
            new EventLogEntryInfo(ExitAt.AddSeconds(-50), "Microsoft-Windows-Resource-Exhaustion-Detector", 2004, "Warning", "Windows is low on virtual memory.", ServerExitEvidence.SystemLog),
            new EventLogEntryInfo(ExitAt.AddSeconds(-5), "Application Hang", 1002, "Error", "The program dotnet.exe stopped interacting with Windows."),
            // Left out: another program's crash, a Defender update, and a server crash outside the window.
            new EventLogEntryInfo(ExitAt, "Application Error", 1000, "Error", "Faulting application name: chrome.exe"),
            new EventLogEntryInfo(ExitAt, "Microsoft-Windows-Windows Defender", 2000, "Information", "Security intelligence updated", ServerExitEvidence.DefenderLog),
            new EventLogEntryInfo(ExitAt.AddMinutes(-5), "Application Error", 1000, "Error", "Faulting application name: BannerlordCoopServer.exe")
        };

        var picked = ServerExitEvidence.Select(entries, ExitAt);

        Assert.Equal([2004L, 1117, 1002, 1001], picked.Select(entry => entry.EventId));
    }

    [Fact]
    public void The_engine_log_keeps_its_last_minute_with_stack_lines()
    {
        var log = "[22:38:00.000] old line\r\n"
            + "[22:39:59.999] just too old\r\n"
            + "[22:40:00.000] inside\r\n"
            + "   at Some.Stack.Line()\r\n"
            + "[22:41:00.000] last line\r\n";

        var minute = ServerExitEvidence.EngineLastMinute(log)!.Split(Environment.NewLine);

        Assert.Equal(["[22:40:00.000] inside", "   at Some.Stack.Line()", "[22:41:00.000] last line"], minute);
        Assert.Null(ServerExitEvidence.EngineLastMinute(""));
    }

    [Fact]
    public void The_engine_minute_runs_past_midnight()
    {
        var log = "[23:58:00.000] before\n[23:59:30.000] kept\n[00:00:20.000] after midnight\n";

        var minute = ServerExitEvidence.EngineLastMinute(log)!;

        Assert.DoesNotContain("before", minute);
        Assert.Contains("kept", minute);
        Assert.Contains("after midnight", minute);
    }

    [Fact]
    public void A_fake_exit_code_1_puts_the_cleaned_evidence_in_the_log_pack()
    {
        var entries = ServerExitEvidence.Select(
        [
            new EventLogEntryInfo(ExitAt.AddSeconds(1), "Application Error", 1000, "Error",
                "Faulting application name: BannerlordCoopServer.exe, path C:\\Users\\Patricia7\\Steam\\BannerlordCoopServer.exe on DESKTOP-QX42ZZ, player 76561198012345678 from 81.2.69.160:7777 and fe80::1c2b:3d4e:5f60:7182")
        ], ExitAt);
        var engine = ServerExitEvidence.EngineLastMinute("[22:41:05.000] Computer: DESKTOP-QX42ZZ User: Patricia7\n[22:41:09.000] pulse: players=3\n");
        var text = ServerExitEvidence.Format(ExitAt, "Coodsz Host did not end the server process and no stop was asked for.", entries, [], engine);
        var zipPath = Path.Combine(_directory, "logs.zip");

        HostLogZip.Create(zipPath, Path.Combine(_directory, "none"), Path.Combine(_directory, "none"), [], null, null, null,
            modLogsRoot: Path.Combine(_directory, "none"), extraTexts: [(ServerExitEvidence.EntryName, text)], steamLibraries: []);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(ServerExitEvidence.EntryName);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        var packed = reader.ReadToEnd();
        Assert.Contains("exit code 1 at 2026-10-25 22:41:10", packed);
        Assert.Contains("Coodsz Host did not end the server process", packed);
        Assert.Contains("Application  Error  Application Error  event 1000", packed);
        Assert.Contains("pulse: players=3", packed);
        foreach (var secret in new[] { "Patricia7", "DESKTOP-QX42ZZ", "76561198012345678", "81.2.69.160", "fe80::1c2b" })
            Assert.DoesNotContain(secret, packed);
    }

    [Fact]
    public void The_evidence_says_when_nothing_was_found_or_a_log_could_not_be_read()
    {
        var none = ServerExitEvidence.Format(ExitAt, "host line", [], [ServerExitEvidence.DefenderLog], null);
        Assert.Contains("The " + ServerExitEvidence.DefenderLog + " log could not be read.", none);
        Assert.Contains("None. Nothing in Windows says it closed the server", none);
        Assert.Contains("The engine log could not be found.", none);

        var unread = ServerExitEvidence.Format(ExitAt, "host line", null, ServerExitEvidence.Logs, null);
        Assert.Contains("The event logs could not be read.", unread);
    }

    [Fact]
    public void The_engine_logs_skip_a_game_whose_singleplayer_line_is_above_the_tail()
    {
        // Crash report #33: the player's own game log was newer and its "/singleplayer" command line sat at the
        // top of a long log, so the server's log and its rgl_log_errors fatal assert were missed.
        var server = Path.Combine(_directory, "rgl_log_19224.txt");
        File.WriteAllText(server, "[18:00:00.000] Command Args: /dedicatedcustomserver\n[18:41:05.000] server line\n");
        File.WriteAllText(Path.Combine(_directory, "rgl_log_errors_19224.txt"),
            "src\\common\\pipes.cpp (879) : CClientPipe::BWriteAndReadResult: BWrite failed\nsrc\\common\\pipes.cpp (879) : Fatal assert; application exiting\n");
        File.SetLastWriteTime(server, DateTime.Now.AddMinutes(-2));
        var game = Path.Combine(_directory, "rgl_log_28160.txt");
        File.WriteAllText(game, "[18:00:00.000] Command Args: /singleplayer /client _MODULES_*Native*_MODULES_\n" + new string('x', 500_000) + "\n[18:41:20.000] game line\n");

        var cutoff = DateTime.Now.AddMinutes(-30);
        Assert.Contains("server line", ServerCrashExplain.Read("", cutoff, _directory).EngineLog);
        var errors = ServerCrashExplain.EngineErrorsTail(_directory, cutoff);
        Assert.NotNull(errors);
        Assert.Contains("Fatal assert; application exiting", errors);

        var text = ServerExitEvidence.Format(ExitAt, "host line", [], [], "minute", errors);
        Assert.Contains("The end of the engine error log (rgl_log_errors):", text);
        Assert.Contains("BWrite failed", text);
    }

    [Fact]
    public void No_engine_error_log_leaves_that_part_out()
    {
        File.WriteAllText(Path.Combine(_directory, "rgl_log_5.txt"), "[18:00:00.000] Command Args: /dedicatedcustomserver\n");
        Assert.Null(ServerCrashExplain.EngineErrorsTail(_directory, DateTime.Now.AddMinutes(-30)));
        Assert.DoesNotContain("engine error log", ServerExitEvidence.Format(ExitAt, "host line", [], [], "minute"));
    }
}
