using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The crash dump of Send crash report, Send server crash report and Send my logs: only the newest
/// dump of this crash and of the right program, only while Add the crash dump is ticked, only when it
/// fits the dump budget, and the zip still under the cap. Nothing here sends anything.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class CrashDumpsTests : IDisposable
{
    private const string Install = "0123456789abcdef0123456789abcdef";
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-crash-dumps-" + Guid.NewGuid().ToString("N"));

    public CrashDumpsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // ----- Which dump -----

    [Fact]
    public void The_window_starts_30_minutes_before_the_crash_or_before_now_for_send_my_logs()
    {
        var crashed = Now.AddHours(-3);
        Assert.Equal(crashed.AddMinutes(-30), CrashDumps.Since(CrashReason.Crash, crashed, Now));
        Assert.Equal(crashed.AddMinutes(-30), CrashDumps.Since(CrashReason.ServerCrash, crashed, Now));
        // Send my logs: only a dump from the last 30 minutes, whatever crashed before.
        Assert.Equal(Now.AddMinutes(-30), CrashDumps.Since(CrashReason.Logs, crashed, Now));
        Assert.Equal(Now.AddMinutes(-30), CrashDumps.Since(CrashReason.Crash, null, Now));
    }

    [Fact]
    public void A_crash_bar_left_open_never_sends_a_later_sessions_dump()
    {
        var crashed = Now.AddHours(-3);
        Assert.Equal(crashed.AddMinutes(10), CrashDumps.Until(CrashReason.Crash, crashed));
        Assert.Equal(crashed.AddMinutes(10), CrashDumps.Until(CrashReason.ServerCrash, crashed));
        // Send my logs keeps the last 30 minutes, with no upper bound.
        Assert.Equal(DateTime.MaxValue, CrashDumps.Until(CrashReason.Logs, crashed));

        var windows = Path.Combine(_directory, "CrashDumps");
        var ours = Dump(Path.Combine(windows, "Bannerlord.exe.1.dmp"), crashed.AddMinutes(1));
        // The game was started again and crashed again two hours later, while the old bar stayed up.
        Dump(Path.Combine(windows, "Bannerlord.exe.2.dmp"), crashed.AddHours(2));
        var since = CrashDumps.Since(CrashReason.Crash, crashed, Now);
        Assert.Equal(ours, CrashDumps.LatestClientDump(null, windows, since, CrashDumps.Until(CrashReason.Crash, crashed)));
    }

    [Fact]
    public void A_full_watchdog_dump_that_is_too_big_sends_the_small_stack_only_dump_of_the_same_crash()
    {
        // The watchdog writes dump.dmp (stack only) and then the full dump into the same crash folder;
        // when dump.dmp could not be deleted first, the full one is dump_1.dmp. 937 MB in the player's report.
        var crashes = Path.Combine(_directory, "crashes");
        var folder = Path.Combine(crashes, "2026-10-26_11.55.03");
        var small = Dump(Path.Combine(folder, "dump.dmp"), Now.AddMinutes(-2), Random(20_000, 40));
        var full = ZeroDump(Path.Combine(folder, "dump_1.dmp"), CrashDumps.MaxRawBytes + 1);
        // An older crash's dump in its own folder never goes in this crash's place.
        Dump(Path.Combine(crashes, "2026-10-26_11.40.00", "dump.dmp"), Now.AddMinutes(-20), Random(1000, 41));
        Assert.Equal(full, CrashDumps.LatestClientDump(crashes, null, Now.AddMinutes(-30), Now.AddMinutes(10)));

        var pack = CrashReports.Prepare(Request(full, logBytes: 1000), Install, Path.Combine(_directory, "packs"));
        Assert.Equal(small, pack.Dump?.Path);
        Assert.DoesNotContain(CrashDumps.TooBigText, pack.Skipped);
        var request = Request(full, logBytes: 1000);
        var sent = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        using (var archive = ZipFile.OpenRead(sent.ZipPath))
        {
            Assert.Equal(Random(20_000, 40), ReadBytes(archive.GetEntry("crash-dumps/2026-10-26_11.55.03-dump.dmp")!));
            var notes = ReadText(archive, "NOTES.txt");
            Assert.Contains("It is the small stack only dump; the full dump crash-dumps/2026-10-26_11.55.03-dump_1.dmp was too big to send.", notes);
            Assert.DoesNotContain(CrashDumps.TooBigText, notes);
        }

        // A tool's own packer picks the same way.
        var zip = Path.Combine(_directory, "own.zip");
        using (ZipFile.Open(zip, ZipArchiveMode.Create)) { }
        Assert.Equal(small, CrashDumps.TryAdd(zip, full, includeDump: true).Pick?.Path);

        // Only the full dump left: the too big line, as before.
        File.Delete(small);
        var alone = CrashReports.Prepare(Request(full, logBytes: 1000), Install, Path.Combine(_directory, "packs"));
        Assert.Null(alone.Dump);
        Assert.Contains(CrashDumps.TooBigText, alone.Skipped);
    }

    [Fact]
    public void A_dump_the_watchdog_is_still_writing_is_passed_over()
    {
        var folder = Path.Combine(_directory, "crashes", "2026-10-26_11.55.03");
        var small = Dump(Path.Combine(folder, "dump.dmp"), Now.AddMinutes(-2), Random(20_000, 42));
        var full = Dump(Path.Combine(folder, "dump_1.dmp"), Now.AddMinutes(-1), Random(1000, 43));
        // The watchdog opens the dump for writing and shares it for reading only.
        using (new FileStream(full, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            Assert.True(CrashDumps.Busy(full));
            var pack = CrashReports.Prepare(Request(full, logBytes: 1000), Install, Path.Combine(_directory, "packs"));
            Assert.Equal(small, pack.Dump?.Path);
            Assert.Contains("still being written", pack.Dump!.Why);
            Assert.Empty(pack.Skipped.Where(line => line == CrashDumps.TooBigText || line == CrashDumps.LeftOutText));

            // Only the busy one: no dump and no too big line, the report still goes.
            using (new FileStream(small, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                var none = CrashReports.Prepare(Request(full, logBytes: 1000), Install, Path.Combine(_directory, "packs"));
                Assert.Null(none.Dump);
                Assert.DoesNotContain(CrashDumps.TooBigText, none.Skipped);
            }
        }

        // Written and closed: the full dump goes, with no note about a smaller one.
        Assert.False(CrashDumps.Busy(full));
        var done = CrashReports.Prepare(Request(full, logBytes: 1000), Install, Path.Combine(_directory, "packs"));
        Assert.Equal(full, done.Dump?.Path);
        Assert.Null(done.Dump!.Why);
    }

    [Fact]
    public void A_newest_dump_that_is_too_big_is_called_too_big_and_not_swapped_for_an_older_one()
    {
        var windows = Path.Combine(_directory, "CrashDumps");
        Dump(Path.Combine(windows, "Bannerlord.exe.1.dmp"), Now.AddMinutes(-20), Random(1000, 20));
        var newest = Dump(Path.Combine(windows, "Bannerlord.exe.2.dmp"), Now.AddMinutes(-5), Random(5_500_000, 21));
        Assert.Equal(newest, CrashDumps.LatestClientDump(null, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));

        var pack = CrashReports.Prepare(Request(newest, logBytes: 1000), Install, Path.Combine(_directory, "packs"));
        Assert.Null(pack.Dump);
        Assert.Contains(CrashDumps.TooBigText, pack.Skipped);

        // Over the 512 MB raw cap: too big without reading it.
        var huge = ZeroDump(Path.Combine(windows, "Bannerlord.exe.3.dmp"), CrashDumps.MaxRawBytes + 1);
        Assert.True(CrashDumps.TryMeasure(huge, CrashDumps.Budget, out var pick));
        Assert.Null(pick);
    }

    [Fact]
    public void Junctions_in_the_crash_folder_are_not_followed()
    {
        var crashes = Path.Combine(_directory, "crashes");
        Directory.CreateDirectory(crashes);
        // A game crash folder and a server crash folder that are links to folders somewhere else.
        var game = Path.Combine(_directory, "elsewhere-game");
        Dump(Path.Combine(game, "dump.dmp"), Now.AddMinutes(-1));
        var server = Path.Combine(_directory, "elsewhere-server");
        Dump(Path.Combine(server, "dump.dmp"), Now.AddMinutes(-1));
        File.WriteAllText(Path.Combine(server, "rgl_log.txt"), "Win64_Shipping_Server build");
        // A real crash folder whose subfolder is a link.
        var real = Path.Combine(crashes, "2026-10-26_11.50");
        Directory.CreateDirectory(real);
        var links = new[] { (Path.Combine(crashes, "2026-10-26_11.58"), game), (Path.Combine(crashes, "2026-10-26_11.59"), server), (Path.Combine(real, "more"), game) };
        foreach (var (link, target) in links)
            using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
                mklink.WaitForExit();
        try
        {
            if (!links.All(link => Directory.Exists(link.Item1)))
                return; // junctions are not available here

            Assert.Null(CrashDumps.LatestClientDump(crashes, null, Now.AddMinutes(-30), Now.AddMinutes(10)));
            Assert.Null(CrashDumps.LatestServerDump(crashes, null, Now.AddMinutes(-30), Now.AddMinutes(10)));
            Assert.Empty(LogZipBuilder.ServerCrashFolders(crashes));
        }
        finally
        {
            // Only the links go; the folders they point to stay for Dispose.
            foreach (var (link, _) in links.Where(link => Directory.Exists(link.Item1)))
                Directory.Delete(link);
        }
    }

    [Fact]
    public void The_client_takes_only_the_newest_game_dump_of_this_crash()
    {
        var crashes = Path.Combine(_directory, "crashes");
        var windows = Path.Combine(_directory, "CrashDumps");
        var game = Dump(Path.Combine(crashes, "2026-10-26_11.55", "dump.dmp"), Now.AddMinutes(-5));
        Dump(Path.Combine(crashes, "2026-10-26_11.50", "dump.dmp"), Now.AddMinutes(-10));
        // A dedicated server's crash folder on the same PC, newer than the game's.
        var serverFolder = Path.Combine(crashes, "2026-10-26_11.58");
        Dump(Path.Combine(serverFolder, "dump.dmp"), Now.AddMinutes(-2));
        File.WriteAllText(Path.Combine(serverFolder, "rgl_log.txt"), "Win64_Shipping_Server build");
        // Other programs' Windows dumps, newer still.
        Dump(Path.Combine(windows, "explorer.exe.4410.dmp"), Now.AddMinutes(-1));
        Dump(Path.Combine(windows, "BannerlordCoopServer.exe.9921.dmp"), Now.AddMinutes(-1));

        Assert.Equal(game, CrashDumps.LatestClientDump(crashes, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));

        // A Windows dump of Bannerlord.exe that is newer wins over the crash folder.
        var wer = Dump(Path.Combine(windows, "Bannerlord.exe.1234.dmp"), Now.AddMinutes(-3));
        Assert.Equal(wer, CrashDumps.LatestClientDump(crashes, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));
    }

    [Fact]
    public void The_server_takes_only_the_newest_server_dump_of_this_crash()
    {
        var crashes = Path.Combine(_directory, "crashes");
        var windows = Path.Combine(_directory, "CrashDumps");
        var serverFolder = Path.Combine(crashes, "2026-10-26_11.50");
        var server = Dump(Path.Combine(serverFolder, "dump.dmp"), Now.AddMinutes(-10));
        File.WriteAllText(Path.Combine(serverFolder, "rgl_log.txt"), "Win64_Shipping_Server build");
        // The game's own crash and its Windows dump are newer but not the server's.
        Dump(Path.Combine(crashes, "2026-10-26_11.58", "dump.dmp"), Now.AddMinutes(-2));
        Dump(Path.Combine(windows, "Bannerlord.exe.1234.dmp"), Now.AddMinutes(-1));

        Assert.Equal(server, CrashDumps.LatestServerDump(crashes, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));
        var wer = Dump(Path.Combine(windows, "BannerlordCoopServer.exe.77.dmp"), Now.AddMinutes(-4));
        Assert.Equal(wer, CrashDumps.LatestServerDump(crashes, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));
    }

    [Fact]
    public void The_server_engine_dump_is_a_dotnet_dump_whose_process_wrote_an_engine_log()
    {
        var windows = Path.Combine(_directory, "CrashDumps");
        var logs = Path.Combine(_directory, "logs");
        Directory.CreateDirectory(logs);
        // The engine runs as dotnet.exe, so Windows names its dump dotnet.exe.(pid).dmp.
        var engine = Dump(Path.Combine(windows, "dotnet.exe.4242.dmp"), Now.AddMinutes(-5));
        File.WriteAllText(Path.Combine(logs, "rgl_log_4242.txt"), "Win64_Shipping_Server engine log");
        File.SetLastWriteTimeUtc(Path.Combine(logs, "rgl_log_4242.txt"), Now.AddMinutes(-5).AddSeconds(-2));
        // Another .NET program's dump is newer, but no engine log has its process id.
        Dump(Path.Combine(windows, "dotnet.exe.777.dmp"), Now.AddMinutes(-1));
        // A pid whose engine log is from another day is not this engine either.
        Dump(Path.Combine(windows, "dotnet.exe.888.dmp"), Now.AddMinutes(-2));
        File.WriteAllText(Path.Combine(logs, "rgl_log_888.txt"), "Win64_Shipping_Server old engine log");
        File.SetLastWriteTimeUtc(Path.Combine(logs, "rgl_log_888.txt"), Now.AddDays(-3));

        Assert.Equal(engine, CrashDumps.LatestServerDump(null, windows, Now.AddMinutes(-30), Now.AddMinutes(10), logs));
        // The game's own log under a pid that a crashing .NET program got later does not vouch for its dump.
        Dump(Path.Combine(windows, "dotnet.exe.999.dmp"), Now.AddMinutes(-1).AddSeconds(30));
        File.WriteAllText(Path.Combine(logs, "rgl_log_999.txt"), "Win64_Shipping_Client game log");
        File.SetLastWriteTimeUtc(Path.Combine(logs, "rgl_log_999.txt"), Now.AddMinutes(-3));
        Assert.Equal(engine, CrashDumps.LatestServerDump(null, windows, Now.AddMinutes(-30), Now.AddMinutes(10), logs));
        // Without the engine log folder no dotnet dump is taken, and the client never takes one.
        Assert.Null(CrashDumps.LatestServerDump(null, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));
        Assert.Null(CrashDumps.LatestClientDump(null, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));
    }

    [Fact]
    public void A_dump_from_before_the_window_or_an_empty_one_is_never_picked()
    {
        var windows = Path.Combine(_directory, "CrashDumps");
        Dump(Path.Combine(windows, "Bannerlord.exe.1.dmp"), Now.AddMinutes(-31));
        var empty = Path.Combine(windows, "Bannerlord.exe.2.dmp");
        File.WriteAllBytes(empty, []);
        File.SetLastWriteTimeUtc(empty, Now.AddMinutes(-1));

        Assert.Null(CrashDumps.LatestClientDump(null, windows, Now.AddMinutes(-30), Now.AddMinutes(10)));
        Assert.Null(CrashDumps.LatestClientDump(Path.Combine(_directory, "missing"), Path.Combine(_directory, "missing"), Now.AddMinutes(-30), Now.AddMinutes(10)));
    }

    // ----- Packing -----

    [Fact]
    public void A_dump_that_fits_is_measured_but_only_goes_in_while_ticked()
    {
        var dumpBytes = Random(300_000, 1);
        var dump = Dump(Path.Combine(_directory, "crashes", "2026-10-26_11.55", "dump.dmp"), Now, dumpBytes);
        var request = Request(dump, logBytes: 20_000);
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));

        Assert.NotNull(pack.Dump);
        Assert.Equal("crash-dumps/2026-10-26_11.55-dump.dmp", pack.Dump!.Entry);
        Assert.Equal(dumpBytes.Length, pack.Dump.Size);
        Assert.InRange(pack.Dump.CompressedSize, 1, CrashDumps.Budget);
        // The packed zip itself never holds the dump, and the summary does not list it.
        Assert.Equal(0, DumpCount(pack.ZipPath));
        Assert.DoesNotContain(pack.Summary.Files, file => file.Name.EndsWith(".dmp", StringComparison.Ordinal));

        // Unticked: the same pack goes, without a dump.
        request.IncludeDump = false;
        var unticked = CrashReports.WithExtras(request, pack, null, null, includeDump: request.IncludeDump);
        Assert.Same(pack, unticked);
        Assert.Equal(0, DumpCount(unticked.ZipPath));

        // Ticked: exactly one dump, byte for byte, listed in the summary, the notes and report.json.
        var ticked = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        Assert.NotEqual(pack.ZipPath, ticked.ZipPath);
        using var archive = ZipFile.OpenRead(ticked.ZipPath);
        var entry = Assert.Single(archive.Entries, item => item.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(pack.Dump.Entry, entry.FullName);
        Assert.Equal(dumpBytes, ReadBytes(entry));
        Assert.Contains(ticked.Summary.Files, file => file.Name == pack.Dump.Entry && file.Size == dumpBytes.Length);
        Assert.Contains(string.Format(System.Globalization.CultureInfo.InvariantCulture, CrashDumps.AddedNote, pack.Dump.Entry), ReadText(archive, "NOTES.txt"));
        using var manifest = JsonDocument.Parse(ReadText(archive, CrashReports.ManifestName));
        var listed = manifest.RootElement.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("name").GetString() == pack.Dump.Entry);
        Assert.True(listed.GetProperty("dump").GetBoolean());
    }

    [Fact]
    public void A_dump_over_the_budget_is_left_out_with_a_note_and_no_send_it_yourself_advice()
    {
        // Random bytes do not compress, so 5.5 MB stays over the 5 MB budget.
        var dump = Dump(Path.Combine(_directory, "CrashDumps", "Bannerlord.exe.1.dmp"), Now, Random(5_500_000, 2));
        var request = Request(dump, logBytes: 20_000);
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));

        Assert.Null(pack.Dump);
        Assert.Contains(CrashDumps.TooBigText, pack.Skipped);
        Assert.Equal(0, DumpCount(pack.ZipPath));
        using (var archive = ZipFile.OpenRead(pack.ZipPath))
            Assert.Contains(CrashDumps.TooBigText, ReadText(archive, "NOTES.txt"));
        Assert.Same(pack, CrashReports.WithExtras(request, pack, null, null, includeDump: true));
        // Players are never told to send files to anyone: everything goes through Send.
        foreach (var text in new[] { CrashDumps.TooBigText, CrashDumps.TooBigWithScreenshotsText })
        {
            Assert.DoesNotContain("Save a copy", text);
            Assert.DoesNotContain("Coodsz", text);
        }
    }

    [Fact]
    public void Logs_and_the_dump_together_stay_under_the_zip_cap()
    {
        // About 9.8 MB of logs zipped and a 4 MB dump that does not compress: the logs are cut to make room.
        var dumpBytes = Random(4_000_000, 3);
        var dump = Dump(Path.Combine(_directory, "CrashDumps", "BannerlordCoopServer.exe.5.dmp"), Now, dumpBytes);
        var request = Request(dump, logBytes: 3_000_000, logs: 4);
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));

        Assert.NotNull(pack.Dump);
        Assert.True(pack.ZipBytes + pack.Dump!.CompressedSize + CrashDumps.EntryOverhead <= CrashReports.MaxZipBytes);
        Assert.Contains(pack.Skipped, line => line.StartsWith("Long logs were cut", StringComparison.Ordinal));

        var final = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        Assert.InRange(final.ZipBytes, 1, CrashReports.MaxZipBytes);
        Assert.True(final.ZipBytes <= CrashReports.WorkerMaxZipBytes);
        using var archive = ZipFile.OpenRead(final.ZipPath);
        Assert.Equal(dumpBytes, ReadBytes(archive.GetEntry(pack.Dump.Entry)!));
        Assert.Equal(1, archive.Entries.Count(entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_dump_shares_its_budget_with_the_screenshots()
    {
        var dumpFile = Dump(Path.Combine(_directory, "CrashDumps", "Bannerlord.exe.1.dmp"), Now, Random(1000, 4));
        var request = Request(null, logBytes: 1000);
        var basePack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        // A dump that takes the whole budget: one small screenshot no longer fits next to it.
        var pack = basePack with { Dump = new CrashDumpPick(dumpFile, CrashDumps.EntryName(dumpFile), 1000, CrashDumps.Budget) };
        var shot = new ReportScreenshot("a.png", OnePixelPng, ReportImageKind.Png);

        Assert.True(CrashDumps.FitsWith(pack.Dump!, 0));
        Assert.False(CrashDumps.FitsWith(pack.Dump!, shot.Size));
        var final = CrashReports.WithExtras(request, pack, null, [shot], includeDump: true);
        Assert.Equal(0, DumpCount(final.ZipPath));
        Assert.Contains(CrashDumps.TooBigWithScreenshotsText, final.Skipped);
        Assert.Contains(final.Summary.Files, file => file.Name.StartsWith(ReportExtras.ScreenshotFolder, StringComparison.Ordinal));
    }

    [Fact]
    public void A_tool_that_adds_the_dump_itself_keeps_it_whole_when_the_logs_are_cut()
    {
        // A packer that calls TryAdd on its own zip: FitToSize must not cut or drop the dump.
        var zip = Path.Combine(_directory, "own.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("big.log").Open()))
                writer.Write(RandomText(600_000, 5));
            using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
                writer.Write("notes");
        }

        var dumpBytes = Random(100_000, 6);
        var dump = Dump(Path.Combine(_directory, "CrashDumps", "Bannerlord.exe.7.dmp"), Now, dumpBytes);
        // Unticked: nothing is added and nothing is noted.
        Assert.Equal(new CrashDumpAddResult(null, null), CrashDumps.TryAdd(zip, dump, includeDump: false));
        Assert.Equal(0, DumpCount(zip));
        var added = CrashDumps.TryAdd(zip, dump, includeDump: true);
        Assert.True(added.Added);
        Assert.Null(CrashDumps.TryAdd(zip, null, includeDump: true).Pick);

        Assert.NotNull(CrashReports.FitToSize(zip, 300_000, [200_000, 100_000]));
        using var read = ZipFile.OpenRead(zip);
        Assert.Equal(dumpBytes, ReadBytes(read.GetEntry(added.Pick!.Entry)!));
        Assert.Contains(added.Pick.Entry, ReadText(read, "NOTES.txt"));
        Assert.True(new FileInfo(zip).Length <= 300_000);
    }

    [Fact]
    public void A_big_dump_is_streamed_into_the_zip_and_never_held_in_memory()
    {
        // 300 MB of zeros (sparse, so it is quick): it compresses to well under the budget, so it is
        // written, and Update mode would have held all 300 MB in a MemoryStream (measured 1.38 GB for 800 MB).
        const long size = 300L * 1024 * 1024;
        var dump = ZeroDump(Path.Combine(_directory, "CrashDumps", "Bannerlord.exe.9.dmp"), size);
        try
        {
            var request = Request(dump, logBytes: 20_000);
            var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
            Assert.NotNull(pack.Dump);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var final = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 64L * 1024 * 1024, "WithExtras allocated " + allocated / (1024 * 1024) + " MB");
            using (var archive = ZipFile.OpenRead(final.ZipPath))
                Assert.Equal(size, archive.GetEntry(pack.Dump!.Entry)!.Length);
            Assert.True(final.ZipBytes <= CrashReports.MaxZipBytes);

            // TryAdd streams the same way.
            var zip = Path.Combine(_directory, "own.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
                writer.Write("notes");
            before = GC.GetAllocatedBytesForCurrentThread();
            Assert.True(CrashDumps.TryAdd(zip, dump, includeDump: true).Added);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 64L * 1024 * 1024, "TryAdd allocated " + allocated / (1024 * 1024) + " MB");
        }
        finally
        {
            File.Delete(dump);
        }
    }

    [Fact]
    public void A_dump_that_cannot_be_added_is_left_out_with_a_note_and_the_report_still_goes()
    {
        var dump = Dump(Path.Combine(_directory, "CrashDumps", "Bannerlord.exe.1.dmp"), Now, Random(1000, 30));
        var request = Request(dump, logBytes: 1000);
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        Assert.NotNull(pack.Dump);
        File.Delete(dump);

        var final = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        Assert.Equal(0, DumpCount(final.ZipPath));
        Assert.Contains(CrashDumps.LeftOutText, final.Skipped);
        using var archive = ZipFile.OpenRead(final.ZipPath);
        Assert.Contains(CrashDumps.LeftOutText, ReadText(archive, "NOTES.txt"));
        Assert.NotNull(archive.GetEntry(CrashReports.ManifestName));
        Assert.False(File.Exists(final.ZipPath + ".dump.tmp"));
    }

    [Fact]
    public void A_dump_that_changed_after_it_was_measured_is_measured_again()
    {
        var dump = Dump(Path.Combine(_directory, "CrashDumps", "Bannerlord.exe.1.dmp"), Now, Random(1000, 31));
        var request = Request(dump, logBytes: 1000);
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));

        // A little more was written: it still fits the room kept for it, and all of it goes.
        var grown = Random(1500, 32);
        File.WriteAllBytes(dump, grown);
        var final = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        using (var archive = ZipFile.OpenRead(final.ZipPath))
            Assert.Equal(grown, ReadBytes(archive.GetEntry(pack.Dump!.Entry)!));

        // Far more was written: it no longer fits the room kept, so it is too big now.
        File.WriteAllBytes(dump, Random(200_000, 33));
        var again = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        Assert.Equal(0, DumpCount(again.ZipPath));
        Assert.Contains(CrashDumps.TooBigText, again.Skipped);
    }

    [Fact]
    public void A_failing_dump_lookup_never_stops_the_report()
    {
        var request = Request(null, logBytes: 1000);
        var failing = new CrashReportRequest
        {
            Tool = request.Tool,
            Side = request.Side,
            Reason = request.Reason,
            FileStem = request.FileStem,
            Collect = request.Collect,
            FindDump = () => throw new InvalidOperationException("the dump folder went away")
        };

        var pack = CrashReports.Prepare(failing, Install, Path.Combine(_directory, "packs"));
        Assert.Null(pack.Dump);
        Assert.Contains(CrashDumps.LeftOutText, pack.Skipped);
        using var archive = ZipFile.OpenRead(pack.ZipPath);
        Assert.Contains(CrashDumps.LeftOutText, ReadText(archive, "NOTES.txt"));
    }

    [Fact]
    public void The_dump_entry_name_passes_the_workers_file_name_check()
    {
        // crash.js FILE: plain names and folders, no drive, backslash or .. part, no Steam id.
        var worker = new Regex(@"^[\p{L}\p{N}_.\-+ ()[\]/]{1,200}$");
        foreach (var path in new[]
                 {
                     @"C:\ProgramData\Mount and Blade II Bannerlord\crashes\2026-10-26_11.55.03\dump.dmp",
                     @"C:\Users\Patricia7\AppData\Local\CrashDumps\Bannerlord.exe.12345.dmp",
                     @"C:\x\76561198000000001 crash\dump.dmp"
                 })
        {
            var name = CrashDumps.EntryName(path);
            Assert.Matches(worker, name);
            Assert.StartsWith(CrashDumps.Folder, name);
            Assert.DoesNotContain("..", name.Split('/'));
            Assert.DoesNotMatch(new Regex(@"7656119\d{10}"), name);
        }
    }

    // ----- What the player reads -----

    [Fact]
    public void The_privacy_text_and_the_checkbox_say_what_a_dump_holds()
    {
        Assert.DoesNotContain("crash dumps are left out", CrashReports.PrivacyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Add the crash dump", CrashReports.PrivacyText);
        // The cleaning is for the logs; the dump is not cleaned.
        Assert.StartsWith("Only the logs are cleaned.", CrashReports.PrivacyText);
        Assert.Contains("The crash dump is not cleaned", CrashReports.PrivacyText);
        Assert.DoesNotContain("crash dump", CrashReports.PrivacyTextNoDump, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CrashReports.PrivacyText.Replace("The crash dump is not cleaned and is only sent while Add the crash dump is ticked. ", ""), CrashReports.PrivacyTextNoDump);
        Assert.Contains("pack.Dump != null ? CrashReports.PrivacyText : CrashReports.PrivacyTextNoDump", File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs")));
        Assert.Equal("Add the crash dump", CrashDumps.CheckboxText);
        Assert.Equal("A crash dump is a snapshot of the game's memory and is not cleaned. It can hold your Windows user name, Steam id, the join password, player addresses and anything the game had open. Only Coodsz can open it.", CrashDumps.CheckboxNote);
        // Save a copy no longer says cleaned: with the dump ticked the zip holds a dump that is not.
        Assert.Equal("Saves the same zip to this PC instead of sending it. Nothing is sent.", SaveCopyTip());
        foreach (var text in new[] { CrashReports.PrivacyText, CrashDumps.CheckboxNote, CrashDumps.TooBigText, CrashDumps.TooBigWithScreenshotsText, CrashDumps.LeftOutText, SaveCopyTip(), WhatsNewLine() })
        {
            Assert.DoesNotContain("—", text);
            Assert.DoesNotContain(":", text);
            Assert.DoesNotMatch(new Regex(@"[a-z]-[a-z]"), text);
        }
    }

    [Fact]
    public void Every_language_has_the_crash_dump_lines()
    {
        var line = WhatsNewLine();
        Assert.Contains(CrashDumps.CheckboxNote.Replace(" Only Coodsz can open it.", ""), line);
        var english = new[] { CrashReports.PrivacyText, CrashDumps.CheckboxText, CrashDumps.CheckboxTip, CrashDumps.CheckboxNote, CrashDumps.TooBigText, CrashDumps.TooBigWithScreenshotsText, CrashDumps.LeftOutText, SaveCopyTip(), line };
        foreach (var code in new[] { "de", "fr", "pt", "ru", "tr", "ar" })
        {
            using var doc = JsonDocument.Parse(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", code + ".json")));
            foreach (var text in english)
                Assert.True(doc.RootElement.TryGetProperty(text, out var value) && value.GetString()!.Length > 0, code + " lacks: " + text);
            // The old wordings are renamed, not left behind.
            foreach (var old in new[] { "A crash dump is a memory snapshot.", "and it cannot be cleaned.", "Saves the same cleaned zip", "A dump is a memory snapshot that can hold" })
                Assert.DoesNotContain(doc.RootElement.EnumerateObject(), property => property.Name.Contains(old, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_tool_finds_the_dump_of_its_own_program_and_the_window_asks_each_time()
    {
        foreach (var client in new[] { "BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher" })
        {
            var request = SourceFiles.Method(SourceFiles.Window(RepoRoot(), client), "private CrashReportRequest CrashRequest(");
            Assert.Contains("FindDump = () => CrashDumps.LatestClientDump(", request);
            Assert.Contains("context.DumpSince(DateTime.UtcNow)", request);
            Assert.Contains("context.DumpUntil()", request);
            Assert.Contains("windowsDumps, dumpSince, dumpUntil)", request);
            Assert.DoesNotContain("LatestServerDump", request);
        }

        foreach (var host in new[] { "CoodszHost", "TaomCoopHost" })
        {
            var request = SourceFiles.Method(SourceFiles.Window(RepoRoot(), host), "private CrashReportRequest CrashRequest(");
            Assert.Contains("FindDump = () => CrashDumps.LatestServerDump(", request);
            Assert.Contains("context.DumpSince(DateTime.UtcNow)", request);
            Assert.Contains("context.DumpUntil()", request);
            // The engine log folder, so the engine's own dotnet.exe dumps are found too.
            Assert.Contains("windowsDumps, dumpSince, dumpUntil, ", request);
            Assert.Contains("includeDumps: false", request);
        }

        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        // The checkbox shows only with a dump, starts from the request (on) and is never saved.
        Assert.Matches(new Regex(@"if \(pack\.Dump != null\)\s+_stack\.Children\.Add\(DumpSection\(\)\);"), SourceFiles.Method(window, "private void ShowConsent("));
        var section = SourceFiles.Method(window, "private FrameworkElement DumpSection(");
        Assert.Contains("IsChecked = _request.IncludeDump", section);
        Assert.DoesNotContain("SaveSettings", section + SourceFiles.Method(window, "private void SetIncludeDump("));
        Assert.Contains("_request.IncludeDump && pack.Dump != null", SourceFiles.Method(window, "private async Task<CrashReportPack> FinalPackAsync("));
        Assert.Contains("CrashedAtUtc: DateTime.UtcNow", SourceFiles.Method(window, "public void ServerCrashed("));
        var reports = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        Assert.Contains("CrashedAtUtc: request.CrashedAtUtc", SourceFiles.Method(reports, "private CrashReportRequest CrashReportFor("));
        Assert.True(new CrashReportRequest { Collect = (_, _) => [] }.IncludeDump);
    }

    // ----- Helpers -----

    // A 1x1 PNG.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private CrashReportRequest Request(string? dump, int logBytes, int logs = 1) => new()
    {
        Tool = "coop",
        Side = "client",
        Reason = CrashReason.Crash,
        FileStem = "coodsz-client-logs",
        // Written straight into the zip (no cleaning), like a pack that is already text only.
        Collect = (zip, _) =>
        {
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            for (var i = 0; i < logs; i++)
                using (var writer = new StreamWriter(archive.CreateEntry("game/log" + i + ".txt").Open()))
                    writer.Write(RandomText(logBytes, 10 + i));
            using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
                writer.Write("notes" + Environment.NewLine + "Not included:" + Environment.NewLine + LogZipBuilder.DumpsLeftOutText);
            return [LogZipBuilder.DumpsLeftOutText];
        },
        FindDump = dump == null ? null : () => dump
    };

    private static string Dump(string path, DateTime writtenUtc, byte[]? bytes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? Encoding.ASCII.GetBytes("MDMP dump"));
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    // A big dump of zeros that takes almost no disk: NTFS marks it sparse, so only its length is set.
    private static string ZeroDump(string path, long length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
        {
            DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            stream.SetLength(length);
        }

        File.SetLastWriteTimeUtc(path, Now);
        return path;
    }

    private const uint FsctlSetSparse = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code, IntPtr inBuffer, int inSize,
        IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);

    // The WPF window and WhatsNew texts, read from the source like the other source checks.
    private static string SaveCopyTip() =>
        Regex.Match(File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs")),
            "public const string SaveCopyTip = \"(?<text>[^\"]+)\";").Groups["text"].Value;

    private static string WhatsNewLine() =>
        Regex.Match(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Updates", "WhatsNew.cs")),
            "private const string CrashDumpSend = \"(?<text>[^\"]+)\";").Groups["text"].Value;

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static string RandomText(int length, int seed)
    {
        var random = new Random(seed);
        return new string(Enumerable.Range(0, length).Select(_ => (char)random.Next(33, 126)).ToArray());
    }

    private static int DumpCount(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Count(entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string ReadText(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(name + " is not in the zip");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
