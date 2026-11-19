using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32.SafeHandles;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// crash-stack.txt: the crash stack read out of a dump already on this PC, sent as cleaned text even
/// when the dump itself is too big or Add the crash dump is unticked. Only the real read test writes a
/// dump, of a PowerShell process this test starts itself; shipped code never dumps a process.
/// </summary>
public sealed class DumpStackSummaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-crash-stack-" + Guid.NewGuid().ToString("N"));

    public DumpStackSummaryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // The slow read test lets its reader go on after the time limit; it may still be closing the dump.
        }
    }

    // ----- The text -----

    [Fact]
    public void The_text_names_the_exception_the_module_the_frames_and_the_versions()
    {
        var text = DumpStackSummary.Format(new DumpStackInfo
        {
            DumpName = "dump.dmp",
            DumpSize = 937_000_000,
            WrittenUtc = new DateTime(2026, 10, 28, 15, 8, 44, DateTimeKind.Utc),
            ExceptionCode = 0xC0000005,
            ExceptionAddress = 0x00007FFA12345678,
            ExceptionParameters = [0, 0x10],
            ThreadId = 18796,
            FaultLocation = "TaleWorlds.Native.dll+0x1A2B3C",
            ManagedExceptions = ["System.NullReferenceException: Object reference not set to an instance of an object.", "System.Exception: inner"],
            ManagedFrames = ["TaleWorlds.Core.Banner.ChangePrimaryColor(UInt32)", "[HelperMethodFrame]"],
            StackScan = ["TaleWorlds.Native.dll+0x1A2B3C", "clr.dll+0x120708"],
            OtherThreads = [("Thread 42 (managed 7)", ["System.Threading.Monitor.Wait(System.Object, Int32)"])],
            Modules = [("TaleWorlds.Native.dll", "1.4.8.0"), ("nameless.dll", null)]
        });

        Assert.StartsWith("Crash stack summary read from the crash dump on this PC.\r\n", text);
        Assert.Contains("Dump: dump.dmp, 937", text);
        Assert.Contains("written 2026-10-28 15:08:44 UTC", text);
        Assert.Contains("Exception: 0xC0000005 EXCEPTION_ACCESS_VIOLATION (reading address 0x10)", text);
        Assert.Contains("Exception address: 0x00007FFA12345678", text);
        Assert.Contains("Faulting module: TaleWorlds.Native.dll+0x1A2B3C", text);
        Assert.Contains("Faulting thread: 18796", text);
        // An access violation: the faulting thread's managed exception can be one caught earlier.
        Assert.Contains("Faulting thread's managed exception (the crash is native code, and the exception can be an old one):\r\n"
                        + "  Exception: System.NullReferenceException: Object reference not set", text);
        Assert.Contains("  Inner: System.Exception: inner", text);
        Assert.Contains("Faulting thread, managed frames (newest first):\r\n  TaleWorlds.Core.Banner.ChangePrimaryColor(UInt32)\r\n  [HelperMethodFrame]", text);
        Assert.Contains("stack scan (likely return addresses", text);
        Assert.Contains("  Thread 42 (managed 7):\r\n    System.Threading.Monitor.Wait", text);
        Assert.Contains("Loaded modules (2):\r\n  TaleWorlds.Native.dll  version 1.4.8.0\r\n  nameless.dll\r\n", text);
        Assert.DoesNotContain("stopped after", text);
        Assert.DoesNotContain("unavailable", text);
    }

    [Fact]
    public void A_huge_summary_stays_under_the_cap_and_keeps_its_top()
    {
        var text = DumpStackSummary.Format(new DumpStackInfo
        {
            DumpName = "dump.dmp",
            ExceptionCode = 0xE0434352,
            ExceptionAddress = 0x00007FFDE84D408A,
            ThreadId = 18796,
            FaultLocation = "KERNELBASE.dll+0xC408A",
            ManagedExceptions = ["System.ArgumentOutOfRangeException: Index was out of range."],
            ManagedFrames = Enumerable.Range(0, 60).Select(i => "TaleWorlds.CampaignSystem.Frame" + i + "(" + new string('x', 100) + ")").ToList(),
            StackScan = Enumerable.Range(0, 40).Select(i => "TaleWorlds.Native.dll+0x" + i.ToString("X6")).ToList(),
            OtherThreads = Enumerable.Range(0, 200).Select(i => ("Thread " + i, (IReadOnlyList<string>)Enumerable.Repeat("System.Threading.Monitor.Wait(System.Object, Int32) " + new string('y', 150), 5).ToList())).ToList(),
            Modules = Enumerable.Range(0, 500).Select(i => ("Module" + i + new string('z', 60) + ".dll", (string?)("1.4.8." + i))).ToList()
        });

        Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, DumpStackSummary.MaxBytes);
        Assert.Contains("Exception: 0xE0434352 .NET exception", text);
        Assert.Contains("Faulting module: KERNELBASE.dll+0xC408A", text);
        Assert.Contains("Managed exception: System.ArgumentOutOfRangeException", text);
        // The faulting thread's frames and stack scan all stay; the other threads go first, then the modules.
        Assert.Contains("TaleWorlds.CampaignSystem.Frame59(", text);
        Assert.Contains("TaleWorlds.Native.dll+0x000027", text);
        Assert.DoesNotContain("older frames", text);
        Assert.Contains("  (trimmed: 200 more threads)", text);
        Assert.Matches(@"  Module0z+\.dll  version 1\.4\.8\.0\r\n", text);
        Assert.Matches(@"  \(trimmed: \d+ more modules\)\r\n$", text);
    }

    [Fact]
    public void A_summary_that_fits_is_not_trimmed()
    {
        var text = DumpStackSummary.Format(new DumpStackInfo
        {
            DumpName = "dump.dmp",
            ExceptionCode = 0xC0000005,
            OtherThreads = Enumerable.Range(0, 10).Select(i => ("Thread " + i, (IReadOnlyList<string>)["System.Threading.Monitor.Wait(System.Object, Int32)"])).ToList(),
            Modules = Enumerable.Range(0, 300).Select(i => ("Module" + i + ".dll", (string?)"1.0")).ToList()
        });

        Assert.DoesNotContain("trimmed", text);
        Assert.Contains("  Module299.dll  version 1.0\r\n", text);
    }

    [Fact]
    public void A_native_crash_never_takes_another_threads_old_exception_for_the_faulting_stack()
    {
        // The faulting OS thread has no managed match; thread 7 has a current exception (maybe an old one).
        var list = new List<Tuple<uint, bool>> { Tuple.Create(5u, false), Tuple.Create(7u, true), Tuple.Create(9u, true) };
        var (faulting, excepting) = DumpStackSummary.PickThreads(list, 4242, thread => thread.Item1, thread => thread.Item2);
        Assert.Null(faulting);
        Assert.Same(list[1], excepting);

        // A managed faulting thread with its own exception: that one, nothing listed apart.
        (faulting, excepting) = DumpStackSummary.PickThreads(list, 9, thread => thread.Item1, thread => thread.Item2);
        Assert.Same(list[2], faulting);
        Assert.Null(excepting);

        // A managed faulting thread with no exception: the thread with one is still listed apart, never as the faulting one.
        (faulting, excepting) = DumpStackSummary.PickThreads(list, 5, thread => thread.Item1, thread => thread.Item2);
        Assert.Same(list[0], faulting);
        Assert.Same(list[1], excepting);

        // No exception record: no faulting thread at all.
        (faulting, excepting) = DumpStackSummary.PickThreads(list, null, thread => thread.Item1, thread => thread.Item2);
        Assert.Null(faulting);
        Assert.Same(list[1], excepting);

        var text = DumpStackSummary.Format(new DumpStackInfo
        {
            DumpName = "dump.dmp",
            ExceptionCode = 0xC0000005,
            ThreadId = 4242,
            FaultLocation = "TaleWorlds.Native.dll+0x1A2B3C",
            ManagedFrames = [],
            ManagedExceptions = ["System.InvalidOperationException: old one", "System.Exception: inner"],
            ExceptionThread = "Thread 7 (managed 3)",
            ExceptionThreadFrames = ["TaleWorlds.Core.Game.Tick(Single)"]
        });

        Assert.Contains("Faulting thread: native code, no managed frames.", text);
        Assert.DoesNotContain("Faulting thread, managed frames", text);
        Assert.DoesNotContain("Managed exception:", text);
        Assert.Contains("Thread with a managed exception (not the faulting thread, and the exception can be an old one): Thread 7 (managed 3)\r\n"
                        + "  Exception: System.InvalidOperationException: old one\r\n"
                        + "  Inner: System.Exception: inner\r\n"
                        + "    TaleWorlds.Core.Game.Tick(Single)\r\n", text);
    }

    [Fact]
    public void A_read_stopped_before_the_exception_threads_frames_still_keeps_its_exception_apart()
    {
        // The time limit hits while that thread's frames are read, on native frames (no managed method).
        var info = new DumpStackInfo { DumpName = "dump.dmp", ExceptionCode = 0xC0000005, ThreadId = 4242, ManagedFrames = [], TimedOut = true, Limit = TimeSpan.FromSeconds(20) };
        using var limit = new CancellationTokenSource();
        IEnumerable<string?> Frames()
        {
            // The exception and its thread are already in when the first frame is asked for.
            Assert.Equal("Thread 7 (managed 3)", info.ExceptionThread);
            yield return "TaleWorlds.Core.Game.Tick(Single)";
            limit.Cancel();
            yield return null;
            yield return null;
        }

        Assert.ThrowsAny<OperationCanceledException>(() =>
            DumpStackSummary.AddException(info, ["System.InvalidOperationException: old one"], "Thread 7 (managed 3)", Frames(), frame => frame, limit.Token));

        var text = DumpStackSummary.Format(info);
        Assert.Empty(info.ExceptionThreadFrames);
        Assert.DoesNotContain("Managed exception:", text);
        Assert.Contains("Thread with a managed exception (not the faulting thread, and the exception can be an old one): Thread 7 (managed 3)\r\n"
                        + "  Exception: System.InvalidOperationException: old one\r\n", text);

        // The faulting thread's own exception of a .NET exception crash: no thread named, shown as the crash.
        info.ExceptionCode = 0xE0434352;
        DumpStackSummary.AddException<string>(info, ["System.NullReferenceException: x"], null, null, frame => frame, CancellationToken.None);
        Assert.Contains("Managed exception: System.NullReferenceException: x", DumpStackSummary.Format(info));
    }

    [Fact]
    public void The_faulting_threads_own_exception_is_the_crash_only_for_a_net_exception_code()
    {
        string Text(uint? code)
        {
            var info = new DumpStackInfo { DumpName = "dump.dmp", ExceptionCode = code, ThreadId = 18796, ManagedFrames = ["TaleWorlds.Core.Game.Tick(Single)"] };
            DumpStackSummary.AddException<string>(info, ["System.InvalidOperationException: caught earlier", "System.Exception: inner"], null, null, frame => frame, CancellationToken.None);
            return DumpStackSummary.Format(info);
        }

        // A .NET exception, a code this does not know, or no exception record: the crash.
        foreach (var code in new uint?[] { 0xE0434352, 0xC0001234, null })
        {
            var text = Text(code);
            Assert.Contains("Managed exception: System.InvalidOperationException: caught earlier\r\n  Inner: System.Exception: inner\r\n", text);
            Assert.DoesNotContain("can be an old one", text);
        }

        // A native fault: the same exception, not called the crash.
        foreach (var code in new uint?[] { 0xC0000005, 0xC0000409 })
        {
            var text = Text(code);
            Assert.DoesNotContain("Managed exception:", text);
            Assert.Contains("Faulting thread's managed exception (the crash is native code, and the exception can be an old one):\r\n"
                            + "  Exception: System.InvalidOperationException: caught earlier\r\n  Inner: System.Exception: inner\r\n", text);
        }
    }

    [Fact]
    public void Without_a_runtime_the_native_part_still_goes_and_says_why()
    {
        var text = DumpStackSummary.Format(new DumpStackInfo
        {
            DumpName = "dotnet.exe.1.dmp",
            ExceptionCode = 0xE0434352,
            ManagedUnavailable = "no DAC for .NET 6.0.36 on this PC",
            StackScan = [],
            Modules = [("coreclr.dll", "6.0.3624.51421")]
        });

        Assert.Contains("Exception: 0xE0434352 .NET exception", text);
        Assert.Contains("Faulting module: not in a loaded module", text);
        Assert.Contains("Managed frames unavailable: no DAC for .NET 6.0.36 on this PC", text);
        Assert.DoesNotContain("managed frames (newest first)", text);
        Assert.Contains("  (none found)", text);
        Assert.Contains("coreclr.dll  version 6.0.3624.51421", text);
        Assert.Contains("Exception: none recorded in this dump.", DumpStackSummary.Format(new DumpStackInfo { DumpName = "x.dmp" }));
    }

    // ----- Missing, broken and slow dumps -----

    [Fact]
    public void A_missing_file_or_a_file_that_is_no_minidump_gives_no_text()
    {
        Assert.Null(DumpStackSummary.Read(null));
        Assert.Null(DumpStackSummary.Read(Path.Combine(_directory, "gone.dmp")));
        Assert.Null(DumpStackSummary.Read(Write("random.dmp", Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray())));
        // The fake dumps of the other crash report tests: MDMP and then nothing a header needs.
        Assert.Null(DumpStackSummary.Read(Write("short.dmp", Encoding.ASCII.GetBytes("MDMP dump"))));
        Assert.Null(DumpStackSummary.Read(Write("zeros.dmp", new byte[100_000])));
    }

    [Fact]
    public void A_broken_dump_keeps_its_exception_record_and_says_where_reading_stopped()
    {
        var path = Write("broken.dmp", BrokenMinidump());
        var text = DumpStackSummary.Read(path, TimeSpan.FromSeconds(20));
        // The dump is closed again even though ClrMD threw: the player can delete it.
        File.Delete(path);

        Assert.NotNull(text);
        Assert.Contains("Exception: 0xC0000005 EXCEPTION_ACCESS_VIOLATION (writing address 0xDEAD)", text);
        Assert.Contains("Faulting thread: 4242", text);
        Assert.Contains("The dump could not be read past this point: ", text);
    }

    [Fact]
    public void A_slow_read_stops_at_the_time_limit_with_what_it_has()
    {
        var path = Write("slow.dmp", BrokenMinidump());
        using var release = new ManualResetEventSlim();
        var watch = Stopwatch.StartNew();
        try
        {
            var text = DumpStackSummary.Read(path, TimeSpan.FromMilliseconds(300), stage => release.Wait(TimeSpan.FromSeconds(30)));
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "the read waited " + watch.Elapsed);
            Assert.Contains("Exception: 0xC0000005", text);
            Assert.Contains("The read stopped after 0.3 s; the parts above are all it got.", text);
            // It stopped before any frames, so a report tries the next dump of the crash.
            Assert.True(DumpStackSummary.NoStack(text!));
        }
        finally
        {
            release.Set();
        }
    }

    // ----- In the pack -----

    [Fact]
    public void The_summary_goes_into_the_zip_cleaned_like_the_logs_with_its_note()
    {
        var zip = Path.Combine(_directory, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
                writer.Write("notes");
        var dump = Write("dump.dmp", BrokenMinidump());
        var request = new CrashReportRequest { Collect = (_, _) => [], Secrets = ["hunter22"] };
        var trace = new LogZipTrace();
        var raw = "Managed exception: System.IO.IOException: C:\\Users\\Patricia7\\Documents\\x.sav from 192.168.1.20 for 76561198000000001 password hunter22\r\n"
                  + "  TaleWorlds.Core.dll  version 1.2.3.4\r\n";

        CrashReports.AddStackSummary(zip, dump, request, trace, _ => raw);

        using var read = ZipFile.OpenRead(zip);
        var text = Read(read, DumpStackSummary.FileName);
        Assert.DoesNotContain("Patricia7", text);
        Assert.DoesNotContain("192.168.1.20", text);
        Assert.DoesNotContain("76561198000000001", text);
        Assert.DoesNotContain("hunter22", text);
        Assert.Contains("TaleWorlds.Core.dll  version 1.2.3.4", text);
        Assert.True(trace.Get(DumpStackSummary.FileName).Redacted);
        Assert.Contains(DumpStackSummary.Note, Read(read, "NOTES.txt"));
        Assert.Equal("crash-stack.txt is a text summary read from the crash dump on this PC, sent with or without the dump.", DumpStackSummary.Note);
    }

    [Fact]
    public void A_repack_of_the_same_dump_does_not_read_it_again()
    {
        var dump = Write("cached.dmp", BrokenMinidump());
        var request = new CrashReportRequest { Collect = (_, _) => [] };
        var reads = 0;
        string? Reader(string path)
        {
            reads++;
            return "Exception: 0xC0000005 read " + reads + "\r\n";
        }

        string Pack(string name)
        {
            var zip = Path.Combine(_directory, name);
            using (ZipFile.Open(zip, ZipArchiveMode.Create))
            {
            }

            CrashReports.AddStackSummary(zip, dump, request, null, Reader);
            using var read = ZipFile.OpenRead(zip);
            return Read(read, DumpStackSummary.FileName);
        }

        Assert.Contains("read 1", Pack("first.zip"));
        Assert.Contains("read 1", Pack("second.zip"));
        Assert.Equal(1, reads);

        // A new dump under the same name is read again.
        File.SetLastWriteTimeUtc(dump, File.GetLastWriteTimeUtc(dump).AddMinutes(1));
        Assert.Contains("read 2", Pack("third.zip"));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_dump_read_that_fails_or_finds_nothing_adds_nothing()
    {
        var zip = Path.Combine(_directory, "pack.zip");
        using (ZipFile.Open(zip, ZipArchiveMode.Create))
        {
        }

        var dump = Write("dump.dmp", BrokenMinidump());
        var request = new CrashReportRequest { Collect = (_, _) => [] };
        CrashReports.AddStackSummary(zip, dump, request, null, _ => throw new InvalidOperationException("ClrMD tripped"));
        CrashReports.AddStackSummary(zip, dump, request, null, _ => null);
        CrashReports.AddStackSummary(zip, null, request, null, _ => "never read");

        Assert.Empty(CrashReports.FileList(zip));
    }

    [Fact]
    public void Prepare_sends_the_summary_listed_with_the_files_even_with_the_dump_unticked()
    {
        var dump = Write("CrashDumps/Bannerlord.exe.1.dmp", BrokenMinidump());
        var request = new CrashReportRequest
        {
            FileStem = "coodsz-client-logs",
            IncludeDump = false,
            Collect = (zip, _) =>
            {
                using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                using (var writer = new StreamWriter(archive.CreateEntry("game/log.txt").Open()))
                    writer.Write("log");
                return [];
            },
            FindDump = () => dump
        };

        var pack = CrashReports.Prepare(request, "0123456789abcdef0123456789abcdef", Path.Combine(_directory, "packs"));
        Assert.Contains(pack.Summary.Files, file => file.Name == DumpStackSummary.FileName && file.Size > 0);
        using (var archive = ZipFile.OpenRead(pack.ZipPath))
        {
            Assert.Contains("Exception: 0xC0000005", Read(archive, DumpStackSummary.FileName));
            Assert.Contains(DumpStackSummary.Note, Read(archive, "NOTES.txt"));
            Assert.Contains("\"name\": \"crash-stack.txt\"", Read(archive, CrashReports.ManifestName));
            // Text only: the dump itself is not in the zip.
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        }

        // Sent with Add the crash dump unticked: the same text only pack, summary and all.
        Assert.Same(pack, CrashReports.WithExtras(request, pack, null, null, includeDump: request.IncludeDump));

        // Sent with it ticked: the dump goes, and no line says it did not.
        var full = CrashReports.WithExtras(request, pack, null, null, includeDump: true);
        using (var archive = ZipFile.OpenRead(full.ZipPath))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
            var notes = Read(archive, "NOTES.txt") + Read(archive, DumpStackSummary.FileName);
            Assert.Contains(DumpStackSummary.Note, notes);
            Assert.DoesNotContain("not sent", notes);
        }

        // With no dump of this crash there is nothing to read.
        var none = CrashReports.Prepare(new CrashReportRequest { FileStem = "x", Collect = request.Collect }, "", Path.Combine(_directory, "packs"));
        Assert.DoesNotContain(none.Summary.Files, file => file.Name == DumpStackSummary.FileName);
    }

    [Fact]
    public void The_summary_is_made_when_the_full_dump_is_busy_and_the_small_one_is_too_big_to_send()
    {
        var folder = Path.Combine(_directory, "crashes", "2026-10-26_11.55.03");
        // The small dump compresses to more than the budget: random bytes after the header.
        var random = new Random(44);
        var noise = new byte[CrashDumps.Budget + 1_000_000];
        random.NextBytes(noise);
        var small = Write(Path.Combine(folder, "dump.dmp"), BrokenMinidump().Concat(noise).ToArray());
        File.SetLastWriteTimeUtc(small, DateTime.UtcNow.AddMinutes(-2));
        var full = Write(Path.Combine(folder, "dump_1.dmp"), BrokenMinidump());
        var request = new CrashReportRequest
        {
            FileStem = "coodsz-client-logs",
            Collect = (zip, _) =>
            {
                using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                using (var writer = new StreamWriter(archive.CreateEntry("game/log.txt").Open()))
                    writer.Write("log");
                return [];
            },
            FindDump = () => full
        };

        // The watchdog still writes the full dump and shares it for reading only.
        using (new FileStream(full, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            var pack = CrashReports.Prepare(request, "", Path.Combine(_directory, "packs"));
            Assert.Null(pack.Dump);
            Assert.Contains(CrashDumps.TooBigText, pack.Skipped);
            using var archive = ZipFile.OpenRead(pack.ZipPath);
            var text = Read(archive, DumpStackSummary.FileName);
            Assert.Contains("Dump: dump.dmp,", text);
            Assert.Contains("Exception: 0xC0000005", text);
        }
    }

    [Fact]
    public void An_empty_or_unreadable_newest_dump_falls_back_to_an_older_dump_of_the_same_crash()
    {
        var folder = Path.Combine(_directory, "crashes", "2026-10-26_11.56.10");
        var older = Write(Path.Combine(folder, "dump.dmp"), BrokenMinidump());
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-3));
        var middle = Write(Path.Combine(folder, "dump_1.dmp"), BrokenMinidump());
        File.SetLastWriteTimeUtc(middle, DateTime.UtcNow.AddMinutes(-2));
        var empty = Write(Path.Combine(folder, "dump_2.dmp"), []);
        var request = new CrashReportRequest { Collect = (_, _) => [] };
        var zip = Path.Combine(_directory, "pack.zip");
        using (ZipFile.Open(zip, ZipArchiveMode.Create))
        {
        }

        // The empty newest dump is never read; the next one gives no text (unreadable), so the older one goes.
        var asked = new List<string>();
        CrashReports.AddCrashStackSummary(zip, empty, request, null, path =>
        {
            asked.Add(Path.GetFileName(path));
            return path == middle ? null : "Exception: 0xC0000005 from " + Path.GetFileName(path) + "\r\n";
        });

        Assert.Equal(["dump_1.dmp", "dump.dmp"], asked);
        using var read = ZipFile.OpenRead(zip);
        Assert.Contains("from dump.dmp", Read(read, DumpStackSummary.FileName));
    }

    [Fact]
    public void A_full_dump_the_reader_timed_out_on_falls_back_to_the_small_dump_once()
    {
        var folder = Path.Combine(_directory, "crashes", "2026-10-26_11.57.20");
        var oldest = Write(Path.Combine(folder, "dump_0.dmp"), BrokenMinidump());
        File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-4));
        var small = Write(Path.Combine(folder, "dump.dmp"), BrokenMinidump());
        File.SetLastWriteTimeUtc(small, DateTime.UtcNow.AddMinutes(-3));
        var full = Write(Path.Combine(folder, "dump_1.dmp"), BrokenMinidump());
        var request = new CrashReportRequest { Collect = (_, _) => [] };
        string Pack(string name, Func<string, string?> reader)
        {
            var zip = Path.Combine(_directory, name);
            using (ZipFile.Open(zip, ZipArchiveMode.Create))
            {
            }

            CrashReports.AddCrashStackSummary(zip, full, request, null, reader);
            using var read = ZipFile.OpenRead(zip);
            return Read(read, DumpStackSummary.FileName);
        }

        string TimedOut(string path) => "Exception: 0xC0000005 from " + Path.GetFileName(path) + "\r\n(crash stack unavailable: the reader took longer than 20 s and was stopped)\r\n";

        // The full dump times out; the small one reads in time and goes.
        var asked = new List<string>();
        var text = Pack("a.zip", path =>
        {
            asked.Add(Path.GetFileName(path));
            return path == full ? TimedOut(path) : "Exception: 0xC0000005 from " + Path.GetFileName(path) + "\r\nFaulting thread, managed frames\r\n";
        });
        Assert.Equal(["dump_1.dmp", "dump.dmp"], asked);
        Assert.Contains("from dump.dmp", text);
        Assert.DoesNotContain("unavailable", text);

        // Both time out: one more dump is tried, then the full dump's header text goes.
        CrashReports.StackCache.Clear();
        asked.Clear();
        text = Pack("b.zip", path =>
        {
            asked.Add(Path.GetFileName(path));
            return TimedOut(path);
        });
        Assert.Equal(["dump_1.dmp", "dump.dmp"], asked);
        Assert.Contains("from dump_1.dmp", text);
        Assert.Contains("(crash stack unavailable: the reader took longer", text);
    }

    [Fact]
    public void A_read_that_hit_its_time_limit_with_no_frames_tries_the_small_dump_and_one_with_frames_goes()
    {
        var folder = Path.Combine(_directory, "crashes", "2026-10-26_11.58.30");
        var small = Write(Path.Combine(folder, "dump.dmp"), BrokenMinidump());
        File.SetLastWriteTimeUtc(small, DateTime.UtcNow.AddMinutes(-3));
        var full = Write(Path.Combine(folder, "dump_1.dmp"), BrokenMinidump());
        var request = new CrashReportRequest { Collect = (_, _) => [] };
        var asked = new List<string>();
        string Pack(string name, Func<string, string?> reader)
        {
            CrashReports.StackCache.Clear();
            asked.Clear();
            var zip = Path.Combine(_directory, name);
            using (ZipFile.Open(zip, ZipArchiveMode.Create))
            {
            }

            CrashReports.AddCrashStackSummary(zip, full, request, null, path =>
            {
                asked.Add(Path.GetFileName(path));
                return reader(path);
            });
            using var read = ZipFile.OpenRead(zip);
            return Read(read, DumpStackSummary.FileName);
        }

        const string Stopped = "\r\nThe read stopped after 20 s; the parts above are all it got.\r\n";
        string Header(string path) => "Exception: 0xC0000005 from " + Path.GetFileName(path) + "\r\n";
        string Frames(string path) => Header(path) + "Faulting thread, managed frames (newest first):\r\n  TaleWorlds.Core.Game.Tick()\r\n";

        // The full dump stopped at the time limit before any frames: the small dump is read and goes.
        var text = Pack("a.zip", path => path == full ? Header(path) + Stopped : Frames(path));
        Assert.Equal(["dump_1.dmp", "dump.dmp"], asked);
        Assert.Contains("from dump.dmp", text);
        Assert.DoesNotContain("The read stopped", text);

        // The full dump stopped at the time limit but got frames first: it goes, the small dump is not read.
        text = Pack("b.zip", path => Frames(path) + Stopped);
        Assert.Equal(["dump_1.dmp"], asked);
        Assert.Contains("from dump_1.dmp", text);
        Assert.Contains("TaleWorlds.Core.Game.Tick()", text);

        // A dump in between that gives no text at all does not use up the one more try (Grok review): the small dump is still read.
        var middle = Write(Path.Combine(folder, "dump_2.dmp"), BrokenMinidump());
        File.SetLastWriteTimeUtc(middle, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow);
        text = Pack("c.zip", path => path == full ? Header(path) + Stopped : path == middle ? null : Frames(path));
        Assert.Contains("dump.dmp", asked);
        Assert.Contains("from dump.dmp", text);
        Assert.DoesNotContain("The read stopped", text);
        File.Delete(middle);

        // Another thread's frames or stack scan addresses count as frames; a stack scan that found none does not.
        Assert.False(DumpStackSummary.NoStack(Header(full) + "  Other thread:\r\n    TaleWorlds.Engine.Scene.Tick()\r\n" + Stopped));
        var scan = Header(full) + "Faulting thread, stack scan (likely return addresses, newest first; some can be left over from earlier calls):\r\n";
        Assert.False(DumpStackSummary.NoStack(scan + "  TaleWorlds.Native.dll+0x1A2B\r\n" + Stopped));
        Assert.True(DumpStackSummary.NoStack(scan + "  (none found)\r\n" + Stopped));
        Assert.False(DumpStackSummary.NoStack(Header(full)));
    }

    // ----- A real dump -----

    [Fact]
    public void A_real_dump_of_a_net_framework_process_names_its_managed_method()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var dump = PowerShellDump();

        var watch = Stopwatch.StartNew();
        var text = DumpStackSummary.Read(dump);
        watch.Stop();

        Assert.NotNull(text);
        Assert.DoesNotContain("Managed frames unavailable", text);
        Assert.Contains("StartSleepCommand", text);
        Assert.Contains("powershell.exe", text);
        Assert.Contains("clr.dll  version 4.", text);
        Assert.DoesNotContain("stopped after", text);

        // The same through a reader process, as the tools read it: .NET Framework's DAC passes DacTrust.
        var fromChild = DumpStackSummary.ReadInChild(dump, helper: HelperExe);
        Assert.NotNull(fromChild);
        Assert.Contains("StartSleepCommand", fromChild);
        Assert.DoesNotContain("unavailable", fromChild);
    }

    [Fact]
    public void A_cut_or_scrambled_dump_gives_a_reason_and_never_throws()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var whole = File.ReadAllBytes(PowerShellDump());
        var cut = Write("cut.dmp", whole[..(whole.Length / 2)]);
        var random = new Random(4242);
        var scrambled = BrokenMinidump().Concat(Enumerable.Range(0, 200_000).Select(_ => (byte)random.Next(256))).ToArray();
        var noise = Write("scrambled.dmp", scrambled);

        foreach (var path in new[] { cut, noise })
        {
            var text = DumpStackSummary.Read(path);
            Assert.NotNull(text);
            Assert.Contains("Exception: ", text);
            Assert.True(text.Contains("could not be read past this point", StringComparison.Ordinal)
                        || text.Contains("Managed frames unavailable", StringComparison.Ordinal)
                        || text.Contains("no .NET runtime", StringComparison.Ordinal), text);

            var fromChild = DumpStackSummary.ReadInChild(path, helper: HelperExe);
            Assert.NotNull(fromChild);
            Assert.Contains("Exception: ", fromChild);
        }
    }

    // ----- Which DAC may load -----

    [Fact]
    public void A_dac_outside_the_dotnet_folders_or_without_microsofts_signature_is_refused()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // A dump that names its own folder: refused before anything is opened.
        Assert.NotNull(DacTrust.FolderRefusal(Write("mscordaccore.dll", [0x4D, 0x5A]), []));
        Assert.NotNull(DacTrust.FolderRefusal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App", "8.0.0", "..", "..", "..", "..", "evil", "mscordaccore.dll"), []));
        Assert.NotNull(DacTrust.FolderRefusal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "evil.dll"), []));

        // A server folder the tool runs is allowed, but an unsigned file there still is not.
        var serverDac = Write(Path.Combine("server", "engine", "dotnet", "shared", "Microsoft.NETCore.App", "8.0.0", "mscordaccore.dll"), [0x4D, 0x5A, 0, 0]);
        Assert.NotNull(DacTrust.FolderRefusal(serverDac, []));
        Assert.Null(DacTrust.FolderRefusal(serverDac, [Path.Combine(_directory, "server")]));
        Assert.Contains("signature", DacTrust.Refusal(serverDac, [Path.Combine(_directory, "server")]));
        Assert.Null(DacTrust.OpenTrusted(serverDac, [Path.Combine(_directory, "server")], out var refusal));
        Assert.NotNull(refusal);

        // The real ones on this PC pass: .NET Framework's is signed in a Windows catalog, .NET's in the file.
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "mscordacwks.dll");
        if (File.Exists(framework))
            Assert.Null(DacTrust.Refusal(framework, []));
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App");
        var core = Directory.Exists(installed) ? Directory.GetFiles(installed, "mscordaccore.dll", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (core != null)
            Assert.Null(DacTrust.Refusal(core, []));
    }

    [Fact]
    public void Only_the_signers_of_real_dacs_count_not_every_microsoft_signer()
    {
        static bool Signer(string subject)
        {
            using var key = System.Security.Cryptography.RSA.Create(2048);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(subject, key,
                System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return DacTrust.IsDacSigner(cert);
        }

        Assert.True(Signer("CN=.NET DAC, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        Assert.True(Signer("CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        Assert.True(Signer("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        // WHQL catalogs sign third party drivers under the same Microsoft root.
        Assert.False(Signer("CN=Microsoft Windows Hardware Compatibility Publisher, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        Assert.False(Signer("CN=.NET DAC, O=Microsoft Corporation Evil, C=US"));
        Assert.False(Signer("CN=.NET DAC, O=TALEWORLDS ENTERTAINMENT, C=TR"));
        Assert.False(Signer("CN=Microsoft Windows"));
    }

    [Fact]
    public void A_microsoft_signed_file_that_is_no_dac_is_refused_by_its_version_name()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // kernel32.dll is signed by Microsoft (in a Windows catalog, found by its hash wherever it lies).
        var kernel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        var server = Path.Combine(_directory, "server");
        var fake = Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.NETCore.App", "8.0.0", "mscordaccore.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(fake)!);
        File.Copy(kernel, fake);

        Assert.Null(DacTrust.FolderRefusal(fake, [server]));
        Assert.Equal("the file's version information does not name it a DAC", DacTrust.Refusal(fake, [server]));
        Assert.Null(DacTrust.OpenTrusted(fake, [server], out var refusal));
        Assert.NotNull(refusal);

        // A real DAC under another DAC's name is refused too.
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "mscordacwks.dll");
        if (File.Exists(framework))
        {
            Assert.Null(DacTrust.NameRefusal(framework));
            var renamed = Path.Combine(_directory, "mscordaccore.dll");
            File.Copy(framework, renamed);
            Assert.NotNull(DacTrust.NameRefusal(renamed));
        }
    }

    [Fact]
    public void A_network_or_device_path_is_refused_before_anything_looks_at_it()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string[] remote =
        [
            @"\\127.0.0.1\x\mscordaccore.dll",
            @"\\?\UNC\127.0.0.1\x\mscordaccore.dll",
            @"\\?\C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscordacwks.dll",
            @"\\.\C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscordacwks.dll",
            @"//127.0.0.1/x/mscordaccore.dll",
            @"\??\UNC\127.0.0.1\x\mscordaccore.dll",
            @"C:mscordaccore.dll",
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscordacwks.dll:evil",
            ""
        ];
        foreach (var path in remote)
        {
            Assert.False(DacTrust.IsLocalDrivePath(path), path);
            // The first check, so the file system is never asked about it.
            Assert.Equal("the DAC is not on a drive of this PC", DacTrust.FolderRefusal(path, [@"\\127.0.0.1\x"]));
            Assert.Null(DacTrust.OpenTrusted(path, [], out var refusal));
            Assert.Equal("the DAC is not on a drive of this PC", refusal);
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Assert.True(DacTrust.IsLocalDrivePath(Path.Combine(system, "kernel32.dll")));
        Assert.True(DacTrust.IsSafeModuleName("ntdll.dll"));
        Assert.True(DacTrust.IsSafeModuleName(Path.Combine(system, "ntdll.dll")));
        Assert.False(DacTrust.IsSafeModuleName(@"\\127.0.0.1\x\evil.dll"));
        Assert.False(DacTrust.IsSafeModuleName(@"\SystemRoot\System32\ntdll.dll"));
    }

    [Fact]
    public void A_dump_that_names_a_network_file_is_never_handed_to_clrmd()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = Write("network.dmp", MinidumpWithModule(@"\\127.0.0.1\x\evil.dll"));
        var stages = new List<string>();

        var text = DumpStackSummary.Read(path, stage: stages.Add);

        Assert.Empty(stages);
        Assert.NotNull(text);
        Assert.Contains("Exception: 0xC0000005", text);
        Assert.Contains("Faulting module: evil.dll+0x45678", text);
        Assert.Contains("Managed frames unavailable: the dump names a file outside this PC's own drives, so it was not read further", text);
        Assert.Contains("  evil.dll\r\n", text);
        Assert.DoesNotContain("127.0.0.1", text);

        // A dump with only local names goes on to ClrMD as before.
        var local = Write("local.dmp", MinidumpWithModule(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ntdll.dll")));
        stages.Clear();
        DumpStackSummary.Read(local, stage: stages.Add);
        Assert.Contains("open", stages);
    }

    [Fact]
    public void A_reason_line_keeps_the_file_name_but_not_its_folders()
    {
        var text = DumpStackSummary.Short(new FileNotFoundException(@"Could not find file 'C:\Users\Patricia7\Documents\mscordaccore.dll'. Also \\host\share\Patricia7\x.dll"));
        Assert.DoesNotContain("Patricia7", text);
        Assert.DoesNotContain("host", text);
        Assert.Contains("'mscordaccore.dll'", text);
        Assert.Contains("x.dll", text);
    }

    // ----- The reader process -----

    [Fact]
    public void The_reader_arguments_are_read_only_when_they_are_the_readers()
    {
        Assert.Null(DumpStackSummary.ParseChild([]));
        Assert.Null(DumpStackSummary.ParseChild(["--coodsz-apply-update", "plan.json"]));
        Assert.Null(DumpStackSummary.ParseChild([DumpStackSummary.ChildArgument, "a.dmp"]));
        Assert.Null(DumpStackSummary.ParseChild([DumpStackSummary.ChildArgument, " ", "out.txt"]));
        var job = DumpStackSummary.ParseChild([DumpStackSummary.ChildArgument, "a.dmp", "out.txt", @"C:\Server", ""]);
        Assert.NotNull(job);
        Assert.Equal("a.dmp", job.Dump);
        Assert.Equal("out.txt", job.Output);
        Assert.Equal([@"C:\Server"], job.ServerFolders);
        Assert.Equal("--coodsz-dump-summary", DumpStackSummary.ChildArgument);
    }

    [Fact]
    public void The_reader_writes_the_text_whole_or_nothing_for_a_file_that_is_no_dump()
    {
        var output = Path.Combine(_directory, "out.txt");
        Assert.Equal(0, DumpStackSummary.RunChild(new DumpStackSummary.ChildJob(Write("broken.dmp", BrokenMinidump()), output, [])));
        Assert.Contains("Exception: 0xC0000005", File.ReadAllText(output));
        Assert.False(File.Exists(output + ".part"));

        var none = Path.Combine(_directory, "none.txt");
        Assert.Equal(DumpStackSummary.NotADumpExit, DumpStackSummary.RunChild(new DumpStackSummary.ChildJob(Write("zeros.dmp", new byte[1000]), none, [])));
        Assert.False(File.Exists(none));
    }

    [Fact]
    public void A_reader_process_that_hangs_crashes_or_is_missing_leaves_the_exception_record_and_why()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var dump = Write("broken.dmp", BrokenMinidump());

        var read = DumpStackSummary.ReadInChild(dump, helper: HelperExe);
        Assert.Contains("Exception: 0xC0000005", read);
        Assert.Contains("The dump could not be read past this point: ", read);
        Assert.DoesNotContain("unavailable", read);
        Assert.Null(DumpStackSummary.ReadInChild(Write("zeros.dmp", new byte[100_000]), helper: HelperExe));

        var watch = Stopwatch.StartNew();
        var hung = DumpStackSummary.ReadInChild(dump, limit: TimeSpan.FromSeconds(2), helper: HelperExe, prefix: ["--hang"]);
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "waited " + watch.Elapsed);
        Assert.Contains("Exception: 0xC0000005", hung);
        Assert.Contains("(crash stack unavailable: the reader took longer than 2 s and was stopped)", hung);

        // A reader that wrote its text and then did not close: the text goes, saying it was stopped.
        var stuck = DumpStackSummary.ReadInChild(dump, limit: TimeSpan.FromSeconds(4), helper: HelperExe, prefix: ["--write-then-hang"]);
        Assert.StartsWith("Crash stack summary read from the crash dump on this PC.\r\n", stuck);
        Assert.Contains("The dump could not be read past this point: ", stuck);
        Assert.EndsWith("\r\n(the reader wrote this, then did not close and was stopped after 4 s)\r\n", stuck);
        Assert.DoesNotContain("unavailable", stuck);

        // Cut off mid write: the half .part file is never read.
        var cut = DumpStackSummary.ReadInChild(dump, limit: TimeSpan.FromSeconds(2), helper: HelperExe, prefix: ["--part-then-hang"]);
        Assert.Contains("(crash stack unavailable: the reader took longer than 2 s and was stopped)", cut);
        Assert.DoesNotContain("Excep\r\n", cut);

        var crashed = DumpStackSummary.ReadInChild(dump, helper: HelperExe, prefix: ["--crash"]);
        Assert.Contains("Exception: 0xC0000005", crashed);
        Assert.Contains("(crash stack unavailable: the reader stopped with exit code 0x", crashed);

        Assert.Contains("(crash stack unavailable: this program has no dump reader)", DumpStackSummary.ReadInChild(dump, helper: ""));
        Assert.Contains("(crash stack unavailable: the reader did not start", DumpStackSummary.ReadInChild(dump, helper: Path.Combine(_directory, "gone.exe")));
    }

    [Fact]
    public void A_stopped_readers_file_counts_only_when_it_is_a_summary_of_a_sane_size()
    {
        const string title = "Crash stack summary read from the crash dump on this PC.\r\n";
        var good = Write("out-good.txt", Encoding.UTF8.GetBytes(title + "Exception: 0xC0000005"));
        Assert.Equal(title + "Exception: 0xC0000005\r\n", DumpStackSummary.WrittenText(good));
        // Bytes that are no UTF-8 become replacement characters, nothing else.
        var odd = Write("out-odd.txt", Encoding.UTF8.GetBytes(title).Concat(new byte[] { 0xFF, 0xFE, 0x0A }).ToArray());
        Assert.Equal(title + "��\n", DumpStackSummary.WrittenText(odd));

        Assert.Null(DumpStackSummary.WrittenText(Path.Combine(_directory, "gone.txt")));
        Assert.Null(DumpStackSummary.WrittenText(Write("out-empty.txt", [])));
        Assert.Null(DumpStackSummary.WrittenText(Write("out-other.txt", Encoding.UTF8.GetBytes("MDMP not a summary\r\n"))));
        Assert.Null(DumpStackSummary.WrittenText(Write("out-huge.txt", Encoding.UTF8.GetBytes(title + new string('x', 2 * DumpStackSummary.MaxBytes)))));
    }

    [Fact]
    public void A_zip_that_cannot_take_the_summary_keeps_the_pack_as_it_was()
    {
        var zip = Path.Combine(_directory, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
                writer.Write("notes");
        var before = File.ReadAllBytes(zip);
        var dump = Write("locked.dmp", BrokenMinidump());
        var request = new CrashReportRequest { Collect = (_, _) => [] };
        var trace = new LogZipTrace();

        // Another program has the zip open for reading only (a virus scanner, say): no throw, no summary.
        using (new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(CrashReports.AddStackSummary(zip, dump, request, trace, _ => "Exception: 0xC0000005\r\n"));

        Assert.Equal(before, File.ReadAllBytes(zip));
        Assert.False(File.Exists(zip + ".stack.tmp"));

        // Free again: it goes in.
        Assert.True(CrashReports.AddStackSummary(zip, dump, request, trace, _ => "Exception: 0xC0000005\r\n"));
        using var read = ZipFile.OpenRead(zip);
        Assert.Contains("Exception: 0xC0000005", Read(read, DumpStackSummary.FileName));
    }

    // ----- Helpers -----

    // The test reader process (tests\DumpSummaryHelper), built next to the tests.
    private static string HelperExe => Path.Combine(AppContext.BaseDirectory, "DumpSummaryHelper.exe");

    // A real dump of a Windows PowerShell child this test starts (.NET Framework, like the game).
    private string PowerShellDump()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(powershell), "Windows PowerShell (.NET Framework, like the game) is needed for this test");

        using var child = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -Command \"[Console]::Out.WriteLine('ready'); [Console]::Out.Flush(); Start-Sleep -Seconds 120\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var dump = Path.Combine(_directory, "powershell.dmp");
        try
        {
            Assert.Equal("ready", child.StandardOutput.ReadLine());
            Thread.Sleep(1500);
            WriteDump(child, dump);
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
        }

        return dump;
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // A minidump with a header, a stream directory and an exception stream, and nothing ClrMD can use.
    private static byte[] BrokenMinidump()
    {
        var bytes = new byte[32 + 12 + 168];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x504D444D);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0xA793);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 1_793_000_000);
        // The directory: one exception stream.
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], 168);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], 44);
        var stream = span[44..];
        BinaryPrimitives.WriteUInt32LittleEndian(stream, 4242);
        BinaryPrimitives.WriteUInt32LittleEndian(stream[8..], 0xC0000005);
        BinaryPrimitives.WriteUInt64LittleEndian(stream[24..], 0x00007FFA12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(stream[32..], 2);
        BinaryPrimitives.WriteUInt64LittleEndian(stream[40..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(stream[48..], 0xDEAD);
        return bytes;
    }

    // BrokenMinidump's exception stream plus a module list with one module named name, which holds the exception address.
    private static byte[] MinidumpWithModule(string name)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        var bytes = new byte[32 + 24 + 168 + 4 + 108 + 4 + nameBytes.Length];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x504D444D);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0xA793);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 1_793_000_000);
        // The directory: the exception stream at 56, the module list at 224.
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], 168);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], 56);
        BinaryPrimitives.WriteUInt32LittleEndian(span[44..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(span[48..], 4 + 108);
        BinaryPrimitives.WriteUInt32LittleEndian(span[52..], 224);
        var stream = span[56..];
        BinaryPrimitives.WriteUInt32LittleEndian(stream, 4242);
        BinaryPrimitives.WriteUInt32LittleEndian(stream[8..], 0xC0000005);
        BinaryPrimitives.WriteUInt64LittleEndian(stream[24..], 0x00007FFA12345678);
        var list = span[224..];
        BinaryPrimitives.WriteUInt32LittleEndian(list, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(list[4..], 0x00007FFA12300000);
        BinaryPrimitives.WriteUInt32LittleEndian(list[12..], 0x100000);
        BinaryPrimitives.WriteUInt32LittleEndian(list[24..], 336);
        BinaryPrimitives.WriteUInt32LittleEndian(span[336..], (uint)nameBytes.Length);
        nameBytes.CopyTo(span[340..]);
        return bytes;
    }

    // Test code only: dumps the PowerShell child this test started, with its private memory so the DAC can read it.
    private static void WriteDump(Process process, string path)
    {
        const uint type = 0x1 | 0x4 | 0x20 | 0x200 | 0x800 | 0x1000; // data segs, handles, unloaded modules, private memory, memory info, thread info
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        if (!MiniDumpWriteDump(process.Handle, (uint)process.Id, file.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("MiniDumpWriteDump failed with " + Marshal.GetLastWin32Error());
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file, uint dumpType, IntPtr exception, IntPtr userStream, IntPtr callback);
}
