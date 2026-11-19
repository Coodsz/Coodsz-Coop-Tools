using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The player's own extras on a report: the "Tell us what happened" notes and the screenshots.
/// Notes go through the same scrubber as the logs, pictures are checked by their bytes, capped and
/// stripped of metadata, and both land in the report zip without pushing it over the size cap.
/// Sending with nothing extra keeps the pack exactly as it was.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class ReportExtrasTests : IDisposable
{
    private const string Install = "0123456789abcdef0123456789abcdef";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-report-extras-" + Guid.NewGuid().ToString("N"));
    private readonly IReadOnlyList<string> _names = LogScrubber.PrivateNames;

    public ReportExtrasTests()
    {
        Directory.CreateDirectory(_directory);
        LogScrubber.PrivateNames = ["Patricia7", "DESKTOP-QX42ZZ"];
    }

    public void Dispose()
    {
        LogScrubber.PrivateNames = _names;
        Directory.Delete(_directory, recursive: true);
    }

    // ----- Notes -----

    [Theory]
    [InlineData(@"It crashed, the log is in C:\Users\Patricia7\Documents\Mount and Blade II Bannerlord", "Patricia7")]
    [InlineData(@"It crashed, the log is in C:\Users\Zorblax9\Documents\Mount and Blade II Bannerlord", "Zorblax9")]
    [InlineData("My PC DESKTOP-QX42ZZ froze", "DESKTOP-QX42ZZ")]
    [InlineData("My friend 76561198000000000 could not join", "76561198000000000")]
    [InlineData("I hosted on 192.168.1.20 port 7210", "192.168.1.20")]
    [InlineData("The server password is Sunflower77 if you want to try", "Sunflower77")]
    [InlineData("I typed /cooppassword hunter2 in the box", "hunter2")]
    public void Notes_are_scrubbed_like_the_logs(string notes, string secret)
    {
        var cleaned = ReportExtras.CleanNotes(notes, ["Sunflower77"]);
        Assert.DoesNotContain(secret, cleaned);
        Assert.NotEqual("", cleaned);
    }

    [Fact]
    public void Notes_keep_the_players_words()
    {
        var cleaned = ReportExtras.CleanNotes("I was in a siege with three friends\nthen the game froze");
        Assert.Contains("I was in a siege with three friends", cleaned);
        Assert.Contains("then the game froze", cleaned);
    }

    [Fact]
    public void Notes_use_windows_line_ends()
    {
        var cleaned = ReportExtras.CleanNotes("first\nsecond\r\nthird\rfourth");
        Assert.Equal(["first", "second", "third", "fourth"], cleaned.Split("\r\n"));
        Assert.DoesNotContain("\n", cleaned.Replace("\r\n", ""));
    }

    [Fact]
    public void Notes_are_capped_at_2000_characters()
    {
        Assert.Equal(2000, ReportExtras.MaxNotesChars);
        var cleaned = ReportExtras.CleanNotes(new string('x', 5000));
        Assert.True(cleaned.Length <= ReportExtras.MaxNotesChars, "length " + cleaned.Length);
        Assert.True(cleaned.Length >= 1900, "length " + cleaned.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t \n ")]
    public void Blank_notes_clean_to_nothing(string? notes)
    {
        Assert.Equal("", ReportExtras.CleanNotes(notes));
        Assert.False(CrashReports.HasExtras(notes, null));
        Assert.False(CrashReports.HasExtras(notes, []));
    }

    [Fact]
    public void The_counter_shows_length_of_2000()
    {
        Assert.Equal("120 / 2000", ReportExtras.CounterText(120));
        Assert.Equal("0 / 2000", ReportExtras.CounterText(0));
    }

    [Fact]
    public void Has_extras_is_true_for_notes_or_a_screenshot()
    {
        Assert.True(CrashReports.HasExtras("it froze", null));
        Assert.True(CrashReports.HasExtras(null, [Shot("a.png", TinyPng())]));
        Assert.False(CrashReports.HasExtras(null, null));
    }

    // ----- Picture detection -----

    [Fact]
    public void Pictures_are_detected_by_their_bytes()
    {
        Assert.Equal(ReportImageKind.Png, ReportExtras.DetectImage(TinyPng()));
        Assert.Equal(ReportImageKind.Jpeg, ReportExtras.DetectImage(TinyJpeg()));
        Assert.Equal(ReportImageKind.None, ReportExtras.DetectImage(Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00\x00\x00\x00;")));
        Assert.Equal(ReportImageKind.None, ReportExtras.DetectImage(Encoding.ASCII.GetBytes("BM\x3a\x00\x00\x00\x00\x00\x00\x00\x36\x00\x00\x00")));
        Assert.Equal(ReportImageKind.None, ReportExtras.DetectImage(Encoding.UTF8.GetBytes("this is just a text file")));
        Assert.Equal(ReportImageKind.None, ReportExtras.DetectImage(ReadOnlySpan<byte>.Empty));
        Assert.Equal(ReportImageKind.None, ReportExtras.DetectImage(new byte[] { 0x89, 0x50 }));
    }

    [Fact]
    public void A_png_named_jpg_is_still_a_png_and_gets_a_png_name()
    {
        var shot = new ReportScreenshot("holiday.jpg", TinyPng(), ReportExtras.DetectImage(TinyPng()));
        Assert.Equal(ReportImageKind.Png, shot.Kind);
        Assert.Null(ReportExtras.CheckAdd([], shot));
        Assert.Equal("screenshots/screenshot-1.png", ReportExtras.ScreenshotEntryName(0, shot.Kind));
        Assert.Equal("screenshots/screenshot-3.jpg", ReportExtras.ScreenshotEntryName(2, ReportImageKind.Jpeg));
    }

    [Fact]
    public void A_text_file_named_png_is_refused()
    {
        var text = Encoding.UTF8.GetBytes("not a picture at all");
        Assert.NotNull(ReportExtras.CheckAdd([], new ReportScreenshot("shot.png", text, ReportExtras.DetectImage(text))));
        // Even when the caller claims it is a PNG, the bytes decide.
        Assert.NotNull(ReportExtras.CheckAdd([], new ReportScreenshot("shot.png", text, ReportImageKind.Png)));
    }

    [Fact]
    public void Gif_and_bmp_are_refused()
    {
        var gif = Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00\x00\x00\x00;");
        var bmp = Encoding.ASCII.GetBytes("BM\x3a\x00\x00\x00\x00\x00\x00\x00\x36\x00\x00\x00");
        Assert.NotNull(ReportExtras.CheckAdd([], new ReportScreenshot("a.gif", gif, ReportExtras.DetectImage(gif))));
        Assert.NotNull(ReportExtras.CheckAdd([], new ReportScreenshot("a.bmp", bmp, ReportExtras.DetectImage(bmp))));
    }

    // ----- Caps -----

    [Fact]
    public void The_sixth_screenshot_is_refused()
    {
        Assert.Equal(5, ReportExtras.MaxScreenshots);
        var shots = new List<ReportScreenshot>();
        for (var i = 0; i < 5; i++)
        {
            var next = Shot("s" + i + ".png", TinyPng());
            Assert.Null(ReportExtras.CheckAdd(shots, next));
            shots.Add(next);
        }

        Assert.NotNull(ReportExtras.CheckAdd(shots, Shot("s6.png", TinyPng())));
    }

    [Fact]
    public void A_single_picture_over_4_mb_is_refused()
    {
        Assert.Equal(4_000_000, ReportExtras.MaxScreenshotBytes);
        Assert.NotNull(ReportExtras.CheckAdd([], Shot("big.png", PaddedPng(4_000_001))));
        Assert.Null(ReportExtras.CheckAdd([], Shot("ok.png", PaddedPng(3_900_000))));
    }

    [Fact]
    public void Pictures_over_the_total_cap_together_are_refused()
    {
        var first = Shot("a.png", PaddedPng(2_500_000));
        var second = Shot("b.png", PaddedPng(2_500_000));
        Assert.Null(ReportExtras.CheckAdd([], first));
        Assert.True(first.Size + second.Size > ReportExtras.MaxScreenshotsTotalBytes);
        Assert.NotNull(ReportExtras.CheckAdd([first], second));
    }

    [Fact]
    public void The_total_cap_fits_under_the_worker_limit()
    {
        Assert.True(ReportExtras.MaxScreenshotsTotalBytes < CrashReports.MaxZipBytes);
        Assert.True(CrashReports.MaxZipBytes < CrashReports.WorkerMaxZipBytes);
    }

    // ----- Metadata -----

    [Fact]
    public void Png_metadata_chunks_are_stripped_and_the_picture_stays_well_formed()
    {
        var stripped = ReportExtras.StripMetadata(TinyPng(withMetadata: true));
        Assert.NotNull(stripped);
        Assert.Equal(["IHDR", "gAMA", "sRGB", "IDAT", "IEND"], PngChunks(stripped!));
        Assert.False(Contains(stripped!, "tEXt"));
        Assert.False(Contains(stripped!, "eXIf"));
        Assert.False(Contains(stripped!, "Patricia7"));
        Assert.Equal(ReportImageKind.Png, ReportExtras.DetectImage(stripped));
    }

    [Fact]
    public void Jpeg_app_segments_comments_and_trailing_bytes_are_stripped()
    {
        var stripped = ReportExtras.StripMetadata(TinyJpeg(withMetadata: true));
        Assert.NotNull(stripped);
        var markers = JpegMarkers(stripped!);
        Assert.DoesNotContain(0xE0, markers);
        Assert.DoesNotContain(0xE1, markers);
        Assert.DoesNotContain(0xFE, markers);
        Assert.Contains(0xEE, markers); // APP14 Adobe is kept for colors
        Assert.Contains(0xDB, markers);
        Assert.Contains(0xC0, markers);
        Assert.Contains(0xC4, markers);
        Assert.Contains(0xDA, markers);
        Assert.False(Contains(stripped!, "Exif"));
        Assert.False(Contains(stripped!, "Patricia7"));
        Assert.False(Contains(stripped!, "TRAILER"));
        Assert.Equal(0xFF, stripped![^2]);
        Assert.Equal(0xD9, stripped![^1]);
        Assert.Equal(ReportImageKind.Jpeg, ReportExtras.DetectImage(stripped));
    }

    [Fact]
    public void Broken_pictures_strip_to_null()
    {
        Assert.Null(ReportExtras.StripMetadata(Encoding.UTF8.GetBytes("hello")));
        var png = TinyPng();
        Assert.Null(ReportExtras.StripMetadata(png[..20]));
        var jpeg = TinyJpeg();
        Assert.Null(ReportExtras.StripMetadata(jpeg[..10]));
    }

    // ----- Packing -----

    [Fact]
    public void Nothing_added_keeps_the_same_pack_and_zip()
    {
        var request = Request(Write("game.log", "plain log line"));
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        var before = Hash(pack.ZipPath);
        var files = pack.Summary.Files.Select(file => file.Name).ToList();

        Assert.Same(pack, CrashReports.WithExtras(request, pack, null, null));
        Assert.Same(pack, CrashReports.WithExtras(request, pack, "", []));
        Assert.Same(pack, CrashReports.WithExtras(request, pack, "  \r\n ", []));
        Assert.Equal(before, Hash(pack.ZipPath));
        Assert.Equal(files, pack.Summary.Files.Select(file => file.Name).ToList());
        Assert.DoesNotContain(pack.Summary.Files, file => file.Name == ReportExtras.NotesName || file.Name.StartsWith(ReportExtras.ScreenshotFolder));
    }

    [Fact]
    public void Extras_are_packed_listed_and_scrubbed()
    {
        var request = Request(Write("game.log", "Connecting to 203.0.113.7\nSystem.InvalidOperationException: boom"));
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        var originalHash = Hash(pack.ZipPath);
        var originalFiles = pack.Summary.Files.Select(file => file.Name).ToList();
        var notes = @"It froze after Sunflower77 joined from 10.1.2.3, my folder is C:\Users\Patricia7\Desktop";
        var shots = new List<ReportScreenshot> { Shot("first.png", TinyPng(withMetadata: true)), Shot("photo.png", TinyJpeg(withMetadata: true)) };

        var extras = CrashReports.WithExtras(request, pack, notes, shots);
        try
        {
            Assert.NotSame(pack, extras);
            Assert.NotEqual(pack.ZipPath, extras.ZipPath);
            Assert.True(new FileInfo(extras.ZipPath).Length <= CrashReports.MaxZipBytes);

            using (var archive = ZipFile.OpenRead(extras.ZipPath))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToList();
                Assert.Contains("player-notes.txt", names);
                Assert.Contains("screenshots/screenshot-1.png", names);
                Assert.Contains("screenshots/screenshot-2.jpg", names);
                Assert.Contains("NOTES.txt", names);
                Assert.Contains("game/game.log", names);

                var written = Read(archive, ReportExtras.NotesName);
                foreach (var secret in new[] { "Sunflower77", "10.1.2.3", "Patricia7" })
                    Assert.DoesNotContain(secret, written);
                Assert.Contains("It froze after", written);

                var png = Bytes(archive, "screenshots/screenshot-1.png");
                Assert.Equal(["IHDR", "gAMA", "sRGB", "IDAT", "IEND"], PngChunks(png));
                Assert.False(Contains(png, "tEXt"));
                Assert.False(Contains(png, "eXIf"));
                var jpeg = Bytes(archive, "screenshots/screenshot-2.jpg");
                Assert.Equal(ReportImageKind.Jpeg, ReportExtras.DetectImage(jpeg));
                Assert.False(Contains(jpeg, "Exif"));
                Assert.DoesNotContain(0xE1, JpegMarkers(jpeg));
                Assert.DoesNotContain(0xFE, JpegMarkers(jpeg));

                using var manifest = JsonDocument.Parse(Read(archive, CrashReports.ManifestName));
                var listed = manifest.RootElement.GetProperty("files").EnumerateArray().ToDictionary(file => file.GetProperty("name").GetString()!);
                foreach (var name in new[] { "player-notes.txt", "screenshots/screenshot-1.png", "screenshots/screenshot-2.jpg" })
                {
                    Assert.True(listed.ContainsKey(name), name + " is not in report.json");
                    Assert.True(listed[name].TryGetProperty("fromPlayer", out var fromPlayer) && fromPlayer.GetBoolean(), name + " is not fromPlayer");
                    using var stream = archive.GetEntry(name)!.Open();
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), listed[name].GetProperty("sha256").GetString());
                }

                Assert.True(listed.ContainsKey("game/game.log"));
                Assert.False(listed["game/game.log"].TryGetProperty("fromPlayer", out var logFromPlayer) && logFromPlayer.GetBoolean());

                var entries = archive.Entries.ToDictionary(entry => entry.FullName, entry => entry.Length);
                Assert.Equal(entries.Keys.OrderBy(name => name, StringComparer.Ordinal), extras.Summary.Files.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal));
                foreach (var file in extras.Summary.Files)
                    Assert.Equal(entries[file.Name], file.Size);
            }

            // The original pack is untouched.
            Assert.Equal(originalHash, Hash(pack.ZipPath));
            Assert.Equal(originalFiles, pack.Summary.Files.Select(file => file.Name).ToList());
        }
        finally
        {
            File.Delete(extras.ZipPath);
        }
    }

    [Fact]
    public void Notes_only_pack_has_no_screenshots_folder()
    {
        var request = Request(Write("game.log", "line"));
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        var extras = CrashReports.WithExtras(request, pack, "It froze in the siege", null);
        try
        {
            using var archive = ZipFile.OpenRead(extras.ZipPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == ReportExtras.NotesName);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith(ReportExtras.ScreenshotFolder));
        }
        finally
        {
            File.Delete(extras.ZipPath);
        }
    }

    [Fact]
    public void The_zip_stays_under_the_cap_with_big_logs_and_4_mb_of_pictures()
    {
        var random = new Random(7);
        var big = Path.Combine(_directory, "big.log");
        using (var writer = new StreamWriter(big))
        {
            var chars = new char[10_000_000];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = (char)random.Next(33, 126);
            writer.Write(chars);
            writer.Write("LAST LINE");
        }

        var request = Request(big);
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        Assert.True(pack.ZipBytes <= CrashReports.MaxZipBytes);

        var shots = new List<ReportScreenshot> { Shot("a.png", NoisePng(1100, 600)), Shot("b.png", NoisePng(1100, 600)) };
        Assert.All(shots, shot => Assert.Null(ReportExtras.CheckAdd([], shot)));
        Assert.Null(ReportExtras.CheckAdd([shots[0]], shots[1]));
        Assert.True(shots.Sum(shot => shot.Size) > 3_900_000, "pictures " + shots.Sum(shot => shot.Size));

        var extras = CrashReports.WithExtras(request, pack, new string('n', 3000), shots);
        try
        {
            var size = new FileInfo(extras.ZipPath).Length;
            Assert.True(size <= CrashReports.MaxZipBytes, "zip " + size);
            using var archive = ZipFile.OpenRead(extras.ZipPath);
            Assert.Equal(shots[0].Size, archive.GetEntry("screenshots/screenshot-1.png")!.Length);
            Assert.Equal(shots[1].Size, archive.GetEntry("screenshots/screenshot-2.png")!.Length);
            Assert.True(archive.GetEntry(ReportExtras.NotesName)!.Length <= ReportExtras.MaxNotesChars + 16);
            Assert.EndsWith("LAST LINE", Read(archive, "game/game.log"));
            var entries = archive.Entries.ToDictionary(entry => entry.FullName, entry => entry.Length);
            Assert.Equal(entries.Keys.OrderBy(name => name, StringComparer.Ordinal), extras.Summary.Files.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal));
            using var manifest = JsonDocument.Parse(Read(archive, CrashReports.ManifestName));
            var log = manifest.RootElement.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("name").GetString() == "game/game.log");
            Assert.True(log.GetProperty("truncated").GetBoolean());
            // Each Send or Save marks its own copy of the cleaner's notes, never the packed one.
            Assert.NotNull(extras.Trace);
            Assert.NotSame(pack.Trace, extras.Trace);
        }
        finally
        {
            File.Delete(extras.ZipPath);
        }
    }

    [Fact]
    public void Save_a_copy_of_the_extras_pack_has_the_extras()
    {
        var request = Request(Write("game.log", "line"));
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        var extras = CrashReports.WithExtras(request, pack, "It froze", [Shot("a.png", TinyPng(withMetadata: true))]);
        try
        {
            var copy = Path.Combine(_directory, "copy.zip");
            CrashReports.SaveCopy(extras, copy);
            using var archive = ZipFile.OpenRead(copy);
            Assert.Contains(archive.Entries, entry => entry.FullName == ReportExtras.NotesName);
            Assert.Contains(archive.Entries, entry => entry.FullName == "screenshots/screenshot-1.png");
            Assert.Equal(Hash(extras.ZipPath), Hash(copy));
        }
        finally
        {
            File.Delete(extras.ZipPath);
        }
    }

    [Fact]
    public void The_preview_summary_lists_the_extra_files_without_changing_the_original()
    {
        var request = Request(Write("game.log", "line"));
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));
        var before = pack.Summary.Files.Select(file => file.Name).ToList();
        var shots = new List<ReportScreenshot> { Shot("a.png", TinyPng()), Shot("b.jpg", TinyJpeg()) };

        var preview = ReportExtras.PreviewSummary(pack.Summary, ReportExtras.CleanNotes("It froze"), shots);
        var names = preview.Files.Select(file => file.Name).ToList();
        Assert.Contains(ReportExtras.NotesName, names);
        Assert.Contains("screenshots/screenshot-1.png", names);
        Assert.Contains("screenshots/screenshot-2.jpg", names);
        Assert.Equal(before, pack.Summary.Files.Select(file => file.Name).ToList());

        var plain = ReportExtras.PreviewSummary(pack.Summary, "", []);
        Assert.Equal(before.OrderBy(name => name, StringComparer.Ordinal), plain.Files.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal));

        var clone = CrashReports.CloneSummary(pack.Summary);
        Assert.NotSame(pack.Summary, clone);
        clone.Files.Add(new CrashReportFile("x.txt", 1));
        Assert.Equal(before, pack.Summary.Files.Select(file => file.Name).ToList());
    }

    // ----- Helpers -----

    private CrashReportRequest Request(string log) => new()
    {
        Tool = "coop",
        Side = "client",
        Reason = CrashReason.Crash,
        ToolVersion = "1.2.62",
        Coop = "v0.1.6",
        Game = "v1.4.8",
        CrashLine = "System.InvalidOperationException: boom",
        Secrets = ["Sunflower77"],
        FileStem = "coodsz-client-logs",
        Collect = (zip, limits) =>
        {
            var builder = new LogZipBuilder(limits);
            builder.AddFile(log, "game/game.log");
            return builder.Write(zip, "notes").Skipped;
        }
    };

    private static ReportScreenshot Shot(string name, byte[] bytes) => new(name, bytes, ReportExtras.DetectImage(bytes));

    private string Write(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Read(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(name + " is not in the zip");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] Bytes(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(name + " is not in the zip");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A real 1 by 1 PNG; with metadata it also carries tEXt and eXIf chunks with private text.</summary>
    private static byte[] TinyPng(bool withMetadata = false) => BuildPng(1, 1, Deflate([0, 200, 30, 40]), withMetadata);

    /// <summary>A real PNG of random pixels stored without compression, so it is about width * height * 3 bytes.</summary>
    private static byte[] NoisePng(int width, int height)
    {
        var random = new Random(width * 31 + height);
        var raw = new byte[(width * 3 + 1) * height];
        random.NextBytes(raw);
        for (var row = 0; row < height; row++)
            raw[row * (width * 3 + 1)] = 0;
        return BuildPng(width, height, Deflate(raw, CompressionLevel.NoCompression), withMetadata: false);
    }

    /// <summary>A PNG with well formed chunks padded with an extra IDAT so it is exactly the given size.</summary>
    private static byte[] PaddedPng(int size)
    {
        var start = TinyPng();
        var iend = start.AsSpan(start.Length - 12).ToArray();
        var payload = size - start.Length - 12;
        using var memory = new MemoryStream();
        memory.Write(start, 0, start.Length - 12);
        WriteChunk(memory, "IDAT", new byte[payload]);
        memory.Write(iend);
        var bytes = memory.ToArray();
        Assert.Equal(size, bytes.Length);
        return bytes;
    }

    private static byte[] BuildPng(int width, int height, byte[] idat, bool withMetadata)
    {
        using var memory = new MemoryStream();
        memory.Write(PngSignature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(memory, "IHDR", ihdr);
        if (withMetadata)
        {
            WriteChunk(memory, "gAMA", [0, 0, 0xB1, 0x8F]);
            WriteChunk(memory, "sRGB", [0]);
            WriteChunk(memory, "tEXt", Encoding.Latin1.GetBytes("Author\0Patricia7 on DESKTOP-QX42ZZ"));
            WriteChunk(memory, "eXIf", Encoding.ASCII.GetBytes("MM\0*\0\0\0\x08Patricia7 GPS"));
            WriteChunk(memory, "iTXt", Encoding.UTF8.GetBytes("XML:com.adobe.xmp\0\0\0\0\0<x>Patricia7</x>"));
        }

        WriteChunk(memory, "IDAT", idat);
        if (withMetadata)
            WriteChunk(memory, "tIME", [0x07, 0xEA, 10, 23, 12, 0, 0]);
        WriteChunk(memory, "IEND", []);
        return memory.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        stream.Write(four);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(typeBytes.Concat(data).ToArray());
        BinaryPrimitives.WriteUInt32BigEndian(four, crc);
        stream.Write(four);
    }

    private static byte[] Deflate(byte[] data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var memory = new MemoryStream();
        using (var zlib = new ZLibStream(memory, level, leaveOpen: true))
            zlib.Write(data);
        return memory.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>The chunk types of a PNG in order, after checking each length and CRC.</summary>
    private static List<string> PngChunks(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(PngSignature));
        var types = new List<string>();
        var at = 8;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            Assert.True(at + 12 + length <= png.Length, "chunk " + type + " runs past the end");
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Crc32(png.AsSpan(at + 4, 4 + length).ToArray()), crc);
            types.Add(type);
            at += 12 + length;
        }

        Assert.Equal(png.Length, at);
        Assert.Equal("IEND", types[^1]);
        return types;
    }

    /// <summary>
    /// A tiny baseline JPEG by segment layout: SOI, (APP0 JFIF, APP1 Exif, COM, APP14 Adobe), DQT,
    /// SOF0, DHT, SOS, scan bytes, EOI and, with metadata, trailing bytes after the end.
    /// </summary>
    private static byte[] TinyJpeg(bool withMetadata = false)
    {
        using var memory = new MemoryStream();
        memory.Write([0xFF, 0xD8]);
        if (withMetadata)
        {
            Segment(memory, 0xE0, Encoding.ASCII.GetBytes("JFIF\0\x01\x01\0\0\x01\0\x01\0\0"));
            Segment(memory, 0xE1, Encoding.ASCII.GetBytes("Exif\0\0MM\0*Patricia7 GPS 51.5N"));
            Segment(memory, 0xFE, Encoding.ASCII.GetBytes("Shot by Patricia7"));
        }

        Segment(memory, 0xEE, Encoding.ASCII.GetBytes("Adobe\0\x64\0\0\0\0\x01"));
        var dqt = new byte[65];
        for (var i = 1; i < dqt.Length; i++)
            dqt[i] = 1;
        Segment(memory, 0xDB, dqt);
        Segment(memory, 0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]);
        var dht = new byte[1 + 16 + 1];
        dht[1] = 1;
        Segment(memory, 0xC4, dht);
        Segment(memory, 0xDA, [1, 1, 0, 0, 0x3F, 0]);
        memory.Write([0x12, 0x34, 0xFF, 0x00, 0x56]);
        memory.Write([0xFF, 0xD9]);
        if (withMetadata)
            memory.Write(Encoding.ASCII.GetBytes("TRAILER Patricia7"));
        return memory.ToArray();
    }

    private static void Segment(Stream stream, byte marker, byte[] data)
    {
        stream.Write([0xFF, marker]);
        Span<byte> two = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(two, (ushort)(data.Length + 2));
        stream.Write(two);
        stream.Write(data);
    }

    /// <summary>The markers of a JPEG up to its scan, after checking it starts with SOI and every segment fits.</summary>
    private static List<int> JpegMarkers(byte[] jpeg)
    {
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        var markers = new List<int>();
        var at = 2;
        while (at + 4 <= jpeg.Length)
        {
            Assert.Equal(0xFF, jpeg[at]);
            var marker = jpeg[at + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
            Assert.True(at + 2 + length <= jpeg.Length, "segment " + marker.ToString("X2") + " runs past the end");
            markers.Add(marker);
            at += 2 + length;
            if (marker == 0xDA)
                break;
        }

        Assert.Contains(0xDA, markers);
        return markers;
    }
}
