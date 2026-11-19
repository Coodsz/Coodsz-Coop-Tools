using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The release review of crash reports: a cut UTF-16 log is still cleaned, a file whose cleaning
/// times out is left out, zip entry names are cleaned like the summary list, a failed pack leaves
/// no zip behind, both server tools pass every password they know, and an old run's exit never
/// shows the crash bar.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class CrashReportCleaningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-crash-cleaning-" + Guid.NewGuid().ToString("N"));
    private readonly IReadOnlyList<string> _names = LogScrubber.PrivateNames;

    public CrashReportCleaningTests()
    {
        Directory.CreateDirectory(_directory);
        LogScrubber.PrivateNames = ["Patricia7", "DESKTOP-QX42ZZ"];
    }

    public void Dispose()
    {
        LogScrubber.PrivateNames = _names;
        LogZipBuilder.BeforeCleanForTests = null;
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(false, 4001)]
    [InlineData(false, 4000)]
    [InlineData(true, 4001)]
    public void A_cut_UTF16_log_keeps_its_encoding_and_is_still_cleaned(bool bigEndian, long cap)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        var text = "start of the log" + Environment.NewLine + new string('x', 20_000) + Environment.NewLine
            + "Peer 192.168.1.20 joined as Patricia7 with 76561198000000001";
        var path = Path.Combine(_directory, "watchdog_log.txt");
        File.WriteAllText(path, text, encoding);

        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true, MaxTextBytes: cap));
        builder.AddFile(path, "engine/watchdog_log.txt");
        var zipPath = Path.Combine(_directory, "logs.zip");
        builder.Write(zipPath, "notes");

        using var zip = ZipFile.OpenRead(zipPath);
        var log = Read(zip, "engine/watchdog_log.txt");
        Assert.Contains("Peer [address] joined as [user] with [steam id]", log);
        foreach (var secret in new[] { "192.168.1.20", "Patricia7", "76561198000000001" })
            Assert.DoesNotContain(secret, log);
    }

    [Fact]
    public void A_file_whose_cleaning_times_out_is_left_out_not_packed_half_cleaned()
    {
        LogZipBuilder.BeforeCleanForTests = text =>
        {
            if (text.Contains("SLOW-MARKER-91", StringComparison.Ordinal))
                throw new RegexMatchTimeoutException();
        };
        var slow = Path.Combine(_directory, "slow.log");
        File.WriteAllText(slow, "SLOW-MARKER-91 Patricia7 192.168.1.20");
        var fine = Path.Combine(_directory, "fine.log");
        File.WriteAllText(fine, "all good");

        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true));
        builder.AddFile(slow, "logs/slow.log");
        builder.AddFile(fine, "logs/fine.log");
        var zipPath = Path.Combine(_directory, "logs.zip");
        var result = builder.Write(zipPath, "notes");

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Null(zip.GetEntry("logs/slow.log"));
        Assert.NotNull(zip.GetEntry("logs/fine.log"));
        Assert.Contains("slow.log could not be cleaned in time, so it was left out.", result.Skipped);
        Assert.DoesNotContain("Patricia7", Read(zip, "NOTES.txt"));
        // Clean itself never throws: the text becomes the placeholder.
        Assert.Equal(LogScrubber.TimedOutText, LogZipBuilder.Clean("SLOW-MARKER-91"));
    }

    [Fact]
    public void Zip_entry_names_are_cleaned_like_the_summary_list_also_inside_copied_zips()
    {
        var own = Path.Combine(_directory, "Patricia7-session.log");
        File.WriteAllText(own, "hello");
        var inner = Path.Combine(_directory, "taom_crash.zip");
        using (var stall = ZipFile.Open(inner, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(stall.CreateEntry("DESKTOP-QX42ZZ/report_76561198000000001.txt").Open()))
            writer.Write("stall");

        var request = new CrashReportRequest
        {
            Tool = "taom",
            Side = "client",
            Reason = CrashReason.Logs,
            FileStem = "taom-client-logs",
            Collect = (zip, limits) =>
            {
                var builder = new LogZipBuilder(limits);
                builder.AddFile(own, "logs/" + Path.GetFileName(own));
                builder.AddZipContents(inner, "taom-stall");
                return builder.Write(zip, "notes").Skipped;
            }
        };
        var pack = CrashReports.Prepare(request, "0123456789abcdef0123456789abcdef", Path.Combine(_directory, "packs"));

        List<string> names;
        using (var zip = ZipFile.OpenRead(pack.ZipPath))
            names = zip.Entries.Where(entry => entry.Name.Length > 0).Select(entry => entry.FullName).ToList();
        Assert.Contains("logs/[user]-session.log", names);
        Assert.Contains(names, name => name.StartsWith("taom-stall/[user]/report_", StringComparison.Ordinal));
        foreach (var name in names)
        {
            Assert.DoesNotContain("Patricia7", name);
            Assert.DoesNotContain("DESKTOP-QX42ZZ", name);
            Assert.DoesNotContain("76561198000000001", name);
        }

        // The consent window and the summary show exactly what is in the zip.
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal), pack.Summary.Files.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal));
        File.Delete(pack.ZipPath);
    }

    [Fact]
    public void A_pack_that_fails_leaves_no_zip_behind()
    {
        var folder = Path.Combine(_directory, "packs");
        var request = new CrashReportRequest
        {
            FileStem = "coodsz-client-logs",
            Collect = (zip, _) =>
            {
                File.WriteAllText(zip, "half");
                throw new RegexMatchTimeoutException();
            }
        };

        Assert.Throws<RegexMatchTimeoutException>(() => CrashReports.Prepare(request, "", folder));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void Both_server_tools_pass_every_password_they_know()
    {
        Assert.Equal(["box", "saved"], CrashReports.KnownSecrets("box", "", null, "saved", "box"));

        var taom = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "TaomCoopHost"), "private IReadOnlyList<string> KnownPasswords()");
        Assert.Contains("CrashReports.KnownSecrets(PasswordBox.Text, _settings.Password, configPassword)", taom);
        Assert.Contains("\"server-config.json\"", taom);
        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("Secrets = KnownPasswords(),", SourceFiles.Method(SourceFiles.Window(RepoRoot(), "TaomCoopHost"), "private CrashReportRequest CrashRequest("));
        Assert.Contains("CrashReports.KnownSecrets(new[] { PasswordBox.Text, SharedServerPassword() }", host);
        Assert.Contains(".Concat(_settings.Profiles.Select(profile => profile.Password))", host);
    }

    [Fact]
    public void An_old_run_that_exits_after_Restart_never_shows_the_crash_bar_and_a_new_start_hides_it()
    {
        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopHost").Replace("\r\n", "\n");
        var guard = taom.IndexOf("if (ReferenceEquals(_process, process))\n                {\n", StringComparison.Ordinal);
        Assert.True(guard >= 0, "the TAOM server tool offers the crash report only for the current run");
        var block = taom[guard..taom.IndexOf("RefreshState();", guard, StringComparison.Ordinal)];
        Assert.Contains("OfferServerCrashReport(process);", block);
        Assert.DoesNotContain("OfferServerCrashReport(process);", taom[(guard - 400)..guard]);
        Assert.Contains("_crashBar?.ServerStarted();", taom);

        var session = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.Contains("current = run == _run;", session);
        Assert.Contains("if (exitCode != null && current)", session);
        Assert.Contains("_crashBar?.ServerStarted();", SourceFiles.Method(SourceFiles.Window(RepoRoot(), "CoodszHost"), "private void StartServer()"));
    }

    private static string Read(ZipArchive archive, string name)
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
