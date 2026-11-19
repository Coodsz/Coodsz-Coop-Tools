using System.IO.Compression;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Tests;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// The crash dump in the Burning Empires Send crash report, Send server crash report and Send my logs:
// the same Add the crash dump checkbox as the TAOM tools (the shared consent window), the game's dump
// in the client, and in the server tool the server's dump. The BE server runs as
// engine\dotnet\dotnet.exe, so its Windows dump is dotnet.exe.(pid).dmp, and it is taken only when the
// engine wrote rgl_log_(pid).txt in ProgramData\...\logs at that time. Nothing here sends anything.
[Collection(ModReportsSession.Name)]
public class BeCrashDumpTests
{
    private const string Install = "0123456789abcdef0123456789abcdef";
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheClientAndHostRequestsFindTheDumpTheWayTheTaomToolsDo()
    {
        // The windows cannot be built in a test, so only their wiring is read; the lookups they call are tested below.
        var client = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher"), "private CrashReportRequest CrashRequest(");
        Assert.Contains("FindDump = () => CrashDumps.LatestClientDump(ClientLogZip.ProgramDataCrashes(programData), windowsDumps, dumpSince, dumpUntil)", client);
        var host = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresHost"), "private CrashReportRequest CrashRequest(");
        Assert.Contains("FindDump = () => BeHostLogPack.LatestDump(programData, windowsDumps, dumpSince, dumpUntil)", host);
        foreach (var request in new[] { client, host })
        {
            Assert.Contains("var dumpSince = context.DumpSince(DateTime.UtcNow);", request);
            Assert.Contains("var dumpUntil = context.DumpUntil();", request);
            // The packers never put a dump in on their own: the consent window adds it on Send or
            // Save, only while Add the crash dump is ticked.
            Assert.DoesNotContain("TryAdd", request);
        }
    }

    [Fact]
    public void TheHostTakesTheEnginesOwnDotnetDumpByItsEngineLog()
    {
        var root = NewRoot("be-dumps-engine");
        try
        {
            var programData = Path.Combine(root, "programdata");
            var windows = Path.Combine(root, "CrashDumps");
            var engineLogs = Path.Combine(programData, "logs");
            var since = CrashDumps.Since(CrashReason.ServerCrash, Now, Now);
            var until = CrashDumps.Until(CrashReason.ServerCrash, Now);
            // The game's crash folder and Windows dump are not the server's.
            Dump(Path.Combine(programData, "crashes", "2026-10-26_11.58", "dump.dmp"), Now.AddMinutes(-2));
            Dump(Path.Combine(windows, "Bannerlord.exe.1234.dmp"), Now.AddMinutes(-1));
            // Another .NET program's dump with no engine log, and one whose id an old engine run used hours ago.
            Dump(Path.Combine(windows, "dotnet.exe.999.dmp"), Now.AddMinutes(-1));
            Log(Path.Combine(engineLogs, "rgl_log_777.txt"), Now.AddHours(-3));
            Dump(Path.Combine(windows, "dotnet.exe.777.dmp"), Now.AddMinutes(-1));
            // An engine log in the server's data folder (BANNERLORD_USER_DIR\logs) is not where the engine writes it.
            Log(Path.Combine(root, "data", "logs", "rgl_log_555.txt"), Now.AddMinutes(-2));
            Dump(Path.Combine(windows, "dotnet.exe.555.dmp"), Now.AddMinutes(-1));
            Assert.Null(BeHostLogPack.LatestDump(programData, windows, since, until));

            // The engine of this crash (the server or a mod load test run): its log and its dump.
            Log(Path.Combine(engineLogs, "rgl_log_4242.txt"), Now.AddMinutes(-5));
            var engine = Dump(Path.Combine(windows, "dotnet.exe.4242.dmp"), Now.AddMinutes(-4));
            Assert.Equal(engine, BeHostLogPack.LatestDump(programData, windows, since, until));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheHostTakesTheServersCrashFolderDump()
    {
        var root = NewRoot("be-dumps-folder");
        try
        {
            var programData = Path.Combine(root, "programdata");
            var server = ServerCrash(programData, "2026-10-26_11.50", Now.AddMinutes(-10));
            Dump(Path.Combine(programData, "crashes", "2026-10-26_11.58", "dump.dmp"), Now.AddMinutes(-2));

            Assert.Equal(server, BeHostLogPack.LatestDump(programData, Path.Combine(root, "CrashDumps"),
                CrashDumps.Since(CrashReason.ServerCrash, Now, Now), CrashDumps.Until(CrashReason.ServerCrash, Now)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ACrashTakesDumpsFrom30MinutesBeforeTo10MinutesAfterBothEdgesIncluded()
    {
        var root = NewRoot("be-dumps-edges");
        try
        {
            var programData = Path.Combine(root, "programdata");
            var windows = Path.Combine(root, "CrashDumps");
            var since = CrashDumps.Since(CrashReason.ServerCrash, Now, Now);
            var until = CrashDumps.Until(CrashReason.ServerCrash, Now);
            Assert.Equal(Now.AddMinutes(-30), since);
            Assert.Equal(Now.AddMinutes(10), until);

            // Just outside on both sides: nothing.
            EngineDump(programData, windows, 1, since.AddSeconds(-1));
            EngineDump(programData, windows, 2, until.AddSeconds(1));
            Assert.Null(BeHostLogPack.LatestDump(programData, windows, since, until));

            var first = EngineDump(programData, windows, 3, since);
            Assert.Equal(first, BeHostLogPack.LatestDump(programData, windows, since, until));
            var last = EngineDump(programData, windows, 4, until);
            Assert.Equal(last, BeHostLogPack.LatestDump(programData, windows, since, until));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void SendMyLogsOnlyTakesADumpFromTheLast30Minutes()
    {
        var root = NewRoot("be-dumps-window");
        try
        {
            var programData = Path.Combine(root, "programdata");
            var windows = Path.Combine(root, "CrashDumps");
            // Send my logs three hours after a crash: the last 30 minutes, edge included, and no upper limit.
            var since = CrashDumps.Since(CrashReason.Logs, Now.AddHours(-3), Now);
            var until = CrashDumps.Until(CrashReason.Logs, Now.AddHours(-3));
            Assert.Equal(Now.AddMinutes(-30), since);
            Assert.Equal(DateTime.MaxValue, until);

            ServerCrash(programData, "2026-10-26_11.28", Now.AddMinutes(-32));
            EngineDump(programData, windows, 4242, Now.AddMinutes(-31));
            Dump(Path.Combine(windows, "Bannerlord.exe.1234.dmp"), Now.AddMinutes(-31));
            Assert.Null(BeHostLogPack.LatestDump(programData, windows, since, until));
            Assert.Null(CrashDumps.LatestClientDump(ClientLogZip.ProgramDataCrashes(programData), windows, since, until));

            var engine = EngineDump(programData, windows, 4243, Now.AddMinutes(-30));
            var game = Dump(Path.Combine(windows, "Bannerlord.exe.1235.dmp"), Now.AddMinutes(-30));
            Assert.Equal(engine, BeHostLogPack.LatestDump(programData, windows, since, until));
            Assert.Equal(game, CrashDumps.LatestClientDump(ClientLogZip.ProgramDataCrashes(programData), windows, since, until));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AServerDumpThatFitsGoesInOnlyWhileTicked()
    {
        var root = NewRoot("be-dumps-fits");
        try
        {
            var windows = Path.Combine(root, "CrashDumps");
            var bytes = Random(200_000, 1);
            EngineDump(Path.Combine(root, "programdata"), windows, 4242, Now.AddMinutes(-1), bytes);
            var request = HostRequest(root, windows);
            var pack = CrashReports.Prepare(request, Install, Path.Combine(root, "packs"));

            Assert.NotNull(pack.Dump);
            Assert.Equal("crash-dumps/CrashDumps-dotnet.exe.4242.dmp", pack.Dump!.Entry);
            Assert.Equal(0, DumpCount(pack.ZipPath));

            request.IncludeDump = false;
            Assert.Same(pack, CrashReports.WithExtras(request, pack, null, null, includeDump: request.IncludeDump));

            var ticked = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
            using var archive = ZipFile.OpenRead(ticked.ZipPath);
            var entry = Assert.Single(archive.Entries, item => item.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(pack.Dump.Entry, entry.FullName);
            Assert.Equal(bytes.Length, entry.Length);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ATooBigServerDumpIsLeftOutWithTheTooBigNote()
    {
        var root = NewRoot("be-dumps-big");
        try
        {
            var windows = Path.Combine(root, "CrashDumps");
            // Random bytes do not compress, so 5.5 MB stays over the 5 MB budget.
            EngineDump(Path.Combine(root, "programdata"), windows, 4242, Now.AddMinutes(-1), Random(5_500_000, 2));
            var request = HostRequest(root, windows);
            var pack = CrashReports.Prepare(request, Install, Path.Combine(root, "packs"));

            Assert.Null(pack.Dump);
            Assert.Contains(CrashDumps.TooBigText, pack.Skipped);
            Assert.Equal(0, DumpCount(pack.ZipPath));
            using (var archive = ZipFile.OpenRead(pack.ZipPath))
            {
                using var reader = new StreamReader(archive.GetEntry("NOTES.txt")!.Open());
                Assert.Contains(CrashDumps.TooBigText, reader.ReadToEnd());
            }

            Assert.Same(pack, CrashReports.WithExtras(request, pack, null, null, includeDump: true));
        }
        finally
        {
            Delete(root);
        }
    }

    // The BE server tool's request with its own packer and dump lookup, for a crash seen at Now.
    private static CrashReportRequest HostRequest(string root, string windows)
    {
        var programData = Path.Combine(root, "programdata");
        var data = Path.Combine(root, "data");
        Write(Path.Combine(BeHostFolders.Logs(data), "Coop_server.log"), "coop server log line");
        var since = CrashDumps.Since(CrashReason.ServerCrash, Now, Now);
        var until = CrashDumps.Until(CrashReason.ServerCrash, Now);
        return new CrashReportRequest
        {
            Tool = "be",
            Side = "server",
            Reason = CrashReason.ServerCrash,
            FileStem = "be-host-logs",
            Collect = (zip, limits) => BeHostLogPack.Create(zip, Path.Combine(root, "server"), data, programData, "window", includeDumps: false, windowsDumpRoot: windows, limits: limits, steamLibraries: []).Skipped,
            FindDump = () => BeHostLogPack.LatestDump(programData, windows, since, until)
        };
    }

    // An engine run: its rgl_log_(pid).txt in ProgramData\...\logs and its Windows dump, written together.
    private static string EngineDump(string programData, string windows, int pid, DateTime writtenUtc, byte[]? bytes = null)
    {
        Log(Path.Combine(programData, "logs", "rgl_log_" + pid + ".txt"), writtenUtc);
        return Dump(Path.Combine(windows, "dotnet.exe." + pid + ".dmp"), writtenUtc, bytes);
    }

    // A dedicated server's crash folder in ProgramData: its log names Win64_Shipping_Server.
    private static string ServerCrash(string programData, string folder, DateTime writtenUtc)
    {
        var crash = Path.Combine(programData, "crashes", folder);
        Write(Path.Combine(crash, "rgl_log.txt"), "Win64_Shipping_Server build");
        return Dump(Path.Combine(crash, "dump.dmp"), writtenUtc);
    }

    private static void Log(string path, DateTime writtenUtc)
    {
        // A dedicated server engine log, as the shared lookup now asks for (LogZipBuilder.IsServerEngineLog).
        Write(path, "Win64_Shipping_Server engine log");
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    private static string Dump(string path, DateTime writtenUtc, byte[]? bytes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? "MDMP dump"u8.ToArray());
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static int DumpCount(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Count(entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
