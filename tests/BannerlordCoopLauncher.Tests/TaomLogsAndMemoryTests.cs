using System.IO.Compression;
using BannerlordCoopLauncher.Core.Launch;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// A TAOM player hosted the dedicated server on the same 16 GB laptop and froze at 97%
// memory. The log zip had no full server engine log, no Coop_server.log, no TAOM stall
// report and only some watchdog logs. These cover what the zip now picks and the
// low memory line.
public sealed class TaomLogsAndMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "taom-bundle-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Write(string relative, string text, DateTime? writtenUtc = null)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (writtenUtc != null)
            File.SetLastWriteTimeUtc(path, writtenUtc.Value);
        return path;
    }

    private static string ClientLog(int session) =>
        "[14:29:12.296] Loading xml file: $BASE/Modules/DedicatedServer.Windows/SubModule.xml.\n"
        + "[14:29:12.296] Command Args: /singleplayer /client /autoconnect 10.0.0.5:4200 /cooppassword hunter2 _MODULES_*Native*Coop*DedicatedServer.CoodszJoinPassword*_MODULES_\n"
        + "client session " + session + "\n";

    private const string ServerLog =
        "[14:29:12.296] Loading xml file: $BASE/Modules/DedicatedServer.Windows/SubModule.xml.\n"
        + "[14:29:12.296] Command Args: _MODULES_*Native*Coop*TAOM.CoopCompat*DedicatedServer.Windows*_MODULES_ /dedicatedcustomserver 7210 EU 0\n";

    private const string ServerWatchdog = "\tServer Build: \tTrue\nDebuggee message: #TW#crash_tags.txt#TW#Runtime#TW#Build Name#TW#Win64_Shipping_Server\n";
    private const string ClientWatchdog = "\tServer Build: \tFalse\nDebuggee message: #TW#crash_tags.txt#TW#Runtime#TW#Build Name#TW#Win64_Shipping_Client\n";

    [Fact]
    public void Engine_logs_keep_whole_sessions_and_the_server_session()
    {
        var logs = Path.Combine(_root, "logs");
        // Five game sessions, newest last. Each errors log is written after its full log,
        // which used to push the full logs out of the newest eight.
        for (var session = 1; session <= 5; session++)
        {
            var at = Now.AddHours(-10 + session);
            Write("logs/rgl_log_" + session + ".txt", ClientLog(session), at);
            Write("logs/rgl_log_errors_" + session + ".txt", "errors", at.AddMinutes(1));
            Write("logs/watchdog_log_" + session + ".txt", ClientWatchdog, at.AddMinutes(1));
        }

        // The server ran first and is the oldest of them all.
        Write("logs/rgl_log_152244.txt", ServerLog, Now.AddHours(-12));
        Write("logs/rgl_log_errors_152244.txt", "server errors", Now.AddHours(-12));
        Write("logs/watchdog_log_152244.txt", ServerWatchdog, Now.AddHours(-12));
        // A second server whose full log is gone: its watchdog still counts.
        Write("logs/rgl_log_errors_777.txt", "old server errors", Now.AddHours(-11));
        Write("logs/watchdog_log_777.txt", ServerWatchdog, Now.AddHours(-11));

        var names = ClientLogZip.EngineSessionFiles(logs).Select(Path.GetFileName).ToList();

        foreach (var session in new[] { "2", "3", "4", "5", "152244" })
        {
            Assert.Contains("rgl_log_" + session + ".txt", names);
            Assert.Contains("rgl_log_errors_" + session + ".txt", names);
            Assert.Contains("watchdog_log_" + session + ".txt", names);
        }

        Assert.Contains("watchdog_log_777.txt", names);
        Assert.Contains("rgl_log_errors_777.txt", names);
        Assert.DoesNotContain("rgl_log_1.txt", names);
        Assert.Equal("152244", ClientLogZip.SessionId("C:/x/watchdog_log_152244.txt"));
        Assert.Empty(ClientLogZip.EngineSessionFiles(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void A_client_log_naming_DedicatedServer_modules_is_not_a_server_log()
    {
        Assert.False(BannerlordCoopLauncher.Core.Support.LogZipBuilder.IsServerEngineLog(Write("a/rgl_log_1.txt", ClientLog(1))));
        Assert.True(BannerlordCoopLauncher.Core.Support.LogZipBuilder.IsServerEngineLog(Write("a/rgl_log_2.txt", ServerLog)));
        Assert.True(BannerlordCoopLauncher.Core.Support.LogZipBuilder.IsServerWatchdog(Write("a/watchdog_log_2.txt", ServerWatchdog)));
        Assert.False(BannerlordCoopLauncher.Core.Support.LogZipBuilder.IsServerWatchdog(Write("a/watchdog_log_1.txt", ClientWatchdog)));
    }

    private string StallZip(string name, DateTime writtenUtc, Action<ZipArchive>? fill = null)
    {
        var path = Path.Combine(_root, "game", "bin", "Win64_Shipping_Client", "Logs", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            if (fill != null)
                fill(zip);
            else
                Entry(zip, "report.txt", "stall");
        }

        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    [Fact]
    public void Stall_reports_are_the_newest_three_from_the_last_three_days_under_the_size_cap()
    {
        StallZip("taom_crash_old.zip", Now.AddDays(-4));
        for (var i = 1; i <= 4; i++)
            StallZip("taom_crash_" + i + ".zip", Now.AddHours(-i));
        var big = StallZip("taom_crash_big.zip", Now.AddMinutes(-5));
        using (var stream = new FileStream(big, FileMode.Open))
            stream.SetLength(ClientLogPack.StallReportBytes + 1);
        File.SetLastWriteTimeUtc(big, Now.AddMinutes(-5));

        var pick = ClientLogPack.StallReports(Path.Combine(_root, "game"), Now);

        Assert.Equal(new[] { "stall-reports/taom_crash_1", "stall-reports/taom_crash_2" }, pick.Files.Select(file => file.Entry));
        Assert.Contains(pick.Skipped, line => line.Contains("taom_crash_big.zip") && line.Contains("25 MB"));
        Assert.Empty(ClientLogPack.StallReports(Path.Combine(_root, "no-game"), Now).Files);
        Assert.Empty(ClientLogPack.StallReports(null, Now).Files);
    }

    [Fact]
    public void Server_logs_on_this_pc_are_picked_newest_first_under_the_total_cap()
    {
        var data = Path.Combine(_root, "TAOM-Coop-Host", "Data-v0325");
        var server = Path.Combine(_root, "TAOM-Coop-Host", "Server-v0325-Coop-v016-r2");
        Write("TAOM-Coop-Host/Data-v0325/logs/TAOM.CoopCompat/Coop_server.log", "coop server", Now);
        Write("TAOM-Coop-Host/Data-v0325/logs/TAOM.CoopCompat/Coop_server.20261018-033514.log", "older", Now.AddHours(-1));
        for (var i = 1; i <= 4; i++)
            Write("TAOM-Coop-Host/Data-v0325/logs/coop-server-2026101" + i + "-120000.log", "run " + i, Now.AddDays(-5 + i));
        Write("TAOM-Coop-Host/Data-v0325/logs/host-console-20261018-120000.log", "console", Now);
        Write("TAOM-Coop-Host/Server-v0325-Coop-v016-r2/engine/bin/Win64_Shipping_Server/Logs/taom_debug_2026-10-18.log", "debug", Now);
        Write("TAOM-Coop-Host/Data-v0325/server-config.json", "{ \"password\": \"secret\" }");

        var pick = ClientLogPack.ServerLogs(new[] { new TaomServerFolder(server, data) });
        var entries = pick.Files.Select(file => file.Entry).ToList();

        Assert.Contains("server/coop-compat/Coop_server.log", entries);
        Assert.Contains("server/coop-compat/Coop_server.20261018-033514.log", entries);
        Assert.Contains("server/logs/coop-server-20261014-120000.log", entries);
        Assert.Contains("server/logs/coop-server-20261012-120000.log", entries);
        Assert.DoesNotContain("server/logs/coop-server-20261011-120000.log", entries);
        Assert.Contains("server/logs/host-console-20261018-120000.log", entries);
        Assert.Contains("server/taom-debug/taom_debug_2026-10-18.log", entries);
        Assert.DoesNotContain(entries, entry => entry.Contains("server-config"));

        var capped = ClientLogPack.ServerLogs(new[] { new TaomServerFolder(server, data) }, totalBytes: 15);
        Assert.Equal(new[] { "server/coop-compat/Coop_server.log" }, capped.Files.Select(file => file.Entry));
        Assert.NotEmpty(capped.Skipped);

        Assert.Empty(ClientLogPack.ServerLogs(new[] { new TaomServerFolder(Path.Combine(_root, "none"), null) }).Files);
    }

    [Fact]
    public void Server_folders_come_from_the_default_place_and_a_taom_server_json_beside_the_launcher()
    {
        var drive = Path.Combine(_root, "drive");
        Directory.CreateDirectory(Path.Combine(drive, "TAOM-Coop-Host", HostFolders.DataFolderName, "logs"));
        var custom = Path.Combine(_root, "Elsewhere", "Data");
        Directory.CreateDirectory(custom);
        var settings = Write("Tools/TAOM Server/taom-server.json",
            "{ \"ServerRoot\": " + System.Text.Json.JsonSerializer.Serialize(Path.Combine(_root, "Elsewhere", "Server")) + ", \"DataRoot\": " + System.Text.Json.JsonSerializer.Serialize(custom) + ", \"Password\": \"secret\" }");
        Write("Tools/broken/taom-server.json", "{ not json");

        var found = TaomServerFolders.Find(new[] { drive, Path.Combine(_root, "empty-drive") },
            new[] { settings, settings, Path.Combine(_root, "Tools", "broken", "taom-server.json") });

        Assert.Equal(2, found.Count);
        Assert.Contains(found, folder => folder.DataRoot == custom);
        Assert.Contains(found, folder => folder.DataRoot == Path.Combine(drive, "TAOM-Coop-Host", HostFolders.DataFolderName));
        Assert.Null(TaomServerFolders.Read(Path.Combine(_root, "Tools", "broken", "taom-server.json")));
    }

    [Fact]
    public void The_client_zip_unpacks_stall_reports_cleaned_and_adds_the_server_logs()
    {
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(game, "bin", "Win64_Shipping_Client"));
        var documents = Path.Combine(_root, "Documents");
        var programData = Path.Combine(_root, "ProgramData");
        Write("ProgramData/logs/rgl_log_5.txt", ClientLog(5), Now);
        Write("ProgramData/logs/watchdog_log_5.txt", ClientWatchdog, Now);
        Write("ProgramData/logs/rgl_log_152244.txt", ServerLog, Now.AddHours(-2));
        Write("ProgramData/logs/watchdog_log_152244.txt", ServerWatchdog, Now.AddHours(-2));
        StallZip("taom_crash_20261018_010719_b18f3441.zip", Now.AddHours(-1), zip =>
        {
            Entry(zip, "logs/rgl_log_5.txt", "Command Args: /autoconnect 10.0.0.5:4200 password=hunter2 steam 76561198000000001");
            Entry(zip, "save001.sav", "campaign");
            Entry(zip, "../escape.txt", "kept inside the folder");
            using var dump = zip.CreateEntry("minidump.dmp").Open();
            dump.Write(new byte[] { 0x4D, 0x44, 0x4D, 0x50 });
        });
        var data = Path.Combine(_root, "Host", "Data-v0325");
        Write("Host/Data-v0325/logs/TAOM.CoopCompat/Coop_server.log", "server joined from 10.0.0.9", Now);
        var zipPath = Path.Combine(_root, "out", "taom-client-logs.zip");

        var result = ClientLogPack.Create(zipPath, game, documents, programData, servers: new[] { new TaomServerFolder(null, data) }, nowUtc: Now);

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("engine-logs/rgl_log_5.txt", names);
        Assert.Contains("engine-logs/watchdog_log_5.txt", names);
        Assert.Contains("engine-logs/rgl_log_152244.txt", names);
        Assert.Contains("engine-logs/watchdog_log_152244.txt", names);
        Assert.Contains("server/coop-compat/Coop_server.log", names);
        const string stall = "stall-reports/taom_crash_20261018_010719_b18f3441/";
        Assert.Contains(stall + "logs/rgl_log_5.txt", names);
        Assert.Contains(stall + "minidump.dmp", names);
        Assert.Contains(stall + "escape.txt", names);
        Assert.DoesNotContain(names, name => name.Contains(".sav") || name.Contains(".."));
        Assert.True(result.DumpIncluded);
        Assert.Contains(result.Skipped, line => line.Contains("save001.sav"));

        string Read(string entry)
        {
            using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
            return reader.ReadToEnd();
        }

        var inner = Read(stall + "logs/rgl_log_5.txt");
        Assert.DoesNotContain("hunter2", inner);
        Assert.DoesNotContain("10.0.0.5", inner);
        Assert.DoesNotContain("76561198000000001", inner);
        Assert.DoesNotContain("hunter2", Read("engine-logs/rgl_log_5.txt"));
        Assert.DoesNotContain("10.0.0.9", Read("server/coop-compat/Coop_server.log"));
        var notes = Read("NOTES.txt");
        Assert.Contains("stall reports", notes);
        Assert.Contains("Coop_server.log", notes);
    }

    [Fact]
    public void The_join_password_on_the_game_command_line_is_removed()
    {
        // The engine log and the watchdog log both repeat the game's command line.
        var cleaned = BannerlordCoopLauncher.Core.Support.LogZipBuilder.Clean(
            "Command Args: /client /cooppassword hunter2 _MODULES_*Native*_MODULES_\n#TW#Arguments#TW#/cooppassword \"two words\" _MODULES_*Coop*");
        Assert.DoesNotContain("hunter2", cleaned);
        Assert.DoesNotContain("two words", cleaned);
        Assert.Contains("/cooppassword [password] _MODULES_*Native*_MODULES_", cleaned);
    }

    private const ulong Gb = 1024UL * 1024 * 1024;

    [Theory]
    [InlineData(32, 16.0, 50u, false, false)]
    [InlineData(32, 1.5, 70u, false, true)]
    [InlineData(64, 3.0, 95u, false, true)]
    [InlineData(16, 8.0, 50u, true, true)]
    [InlineData(32, 12.0, 60u, true, false)]
    [InlineData(16, 8.0, 50u, false, false)]
    [InlineData(24, 8.0, 50u, true, false)]
    public void The_client_warns_on_low_memory_or_a_server_on_a_small_pc(int totalGb, double freeGb, uint load, bool server, bool warns)
    {
        var state = new MemoryState((ulong)totalGb * Gb, (ulong)(freeGb * Gb), load);
        var warning = LowMemory.ClientWarning(state, server);
        Assert.Equal(warns, warning != null);
        if (warning == null)
            return;
        Assert.Contains("page file", warning);
        Assert.Contains(totalGb + " GB", warning);
        if (server)
            Assert.Contains("another PC", warning);
    }

    [Fact]
    public void The_laptop_from_the_report_gets_the_shared_pc_warning_with_its_free_memory()
    {
        // 16 GB laptop, 338 MB free, 97% in use, with the server running beside the game.
        var warning = LowMemory.ClientWarning(new MemoryState(16 * Gb, 338UL * 1024 * 1024, 97), serverOnThisPc: true);
        Assert.NotNull(warning);
        Assert.StartsWith("Low memory: only 0.3 GB of 16 GB is free, and a dedicated server is running on this PC too.", warning);
        Assert.Null(LowMemory.ClientWarning(null, serverOnThisPc: true));
    }

    [Fact]
    public void The_server_tool_warns_at_start_on_low_memory_or_a_game_on_a_small_pc()
    {
        Assert.Null(LowMemory.ServerWarning(new MemoryState(32 * Gb, 20 * Gb, 30), gameOnThisPc: true));
        Assert.NotNull(LowMemory.ServerWarning(new MemoryState(32 * Gb, 1 * Gb, 96), gameOnThisPc: false));
        Assert.Contains("Bannerlord is running on this PC too", LowMemory.ServerWarning(new MemoryState(16 * Gb, 6 * Gb, 60), gameOnThisPc: true));
        Assert.Null(LowMemory.ServerWarning(new MemoryState(16 * Gb, 6 * Gb, 60), gameOnThisPc: false));
    }

    [WindowsFact]
    public void Memory_reads_from_windows()
    {
        var state = LowMemory.Read();
        Assert.NotNull(state);
        Assert.True(state!.Value.TotalBytes > state.Value.AvailableBytes);
        Assert.InRange(state.Value.LoadPercent, 0u, 100u);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_launcher_checks_memory_on_open_and_at_Connect_and_the_server_tool_at_Start_server()
    {
        var client = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        Assert.Contains("await CheckMemoryAsync();", SourceFiles.Method(client, "private async Task ConnectOnceAsync()"));
        Assert.Contains("LowMemory.ClientWarning(LowMemory.Read(), LowMemory.DedicatedServerRunning())", client);
        Assert.Contains("TaomServerFolders.OnThisPc(AppContext.BaseDirectory)", SourceFiles.Method(client, "private CrashReportRequest CrashRequest("));
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopLauncher", "MainWindow.xaml"));
        Assert.Matches("x:Name=\"MemoryWarning\"[^>]*ToolTip=\"[^\"]+\"", xaml);
        var host = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        Assert.Contains("LowMemory.ServerWarning(LowMemory.Read(), LowMemory.GameRunning())", SourceFiles.Method(host, "private async Task StartServerCoreAsync("));
    }
}
