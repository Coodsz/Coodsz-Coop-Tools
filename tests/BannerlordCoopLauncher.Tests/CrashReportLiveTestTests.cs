using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// What the live test of the v1.2.62 release candidate found: the worker refused the crash report
/// with 400 (the multipart part names were not quoted), the Windows user name Admin stayed in the
/// engine logs, and Send my logs packed the same logs up to three times.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class CrashReportLiveTestTests : IDisposable
{
    private const string Install = "0123456789abcdef0123456789abcdef";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-crash-live-" + Guid.NewGuid().ToString("N"));
    private readonly IReadOnlyList<string> _names = LogScrubber.PrivateNames;

    public CrashReportLiveTestTests()
    {
        Directory.CreateDirectory(_directory);
        LogScrubber.PrivateNames = ["Admin", "DESKTOP-QX42ZZ"];
    }

    public void Dispose()
    {
        LogScrubber.PrivateNames = _names;
        Directory.Delete(_directory, recursive: true);
    }

    // ----- The 400 -----

    [Fact]
    public async Task The_form_quotes_every_part_name_so_the_Cloudflare_runtime_reads_it()
    {
        using var form = CrashReports.BuildForm("{}", [0x50, 0x4b, 0x05, 0x06]);
        var body = await form.ReadAsStringAsync();
        Assert.Contains("Content-Disposition: form-data; name=\"summary\"\r\n", body);
        Assert.Contains("Content-Disposition: form-data; name=\"zip\"; filename=\"logs.zip\"\r\n", body);
        // The unquoted form .NET writes by default is what the runtime refused as a part with no name.
        Assert.DoesNotContain("name=summary", body);
        Assert.DoesNotContain("name=zip", body);
    }

    [Fact]
    public async Task A_refused_report_keeps_the_worker_reason_for_the_log()
    {
        var zip = Path.Combine(_directory, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
            writer.Write("notes");
        var pack = new CrashReportPack(zip, CrashReports.BuildSummary(new CrashReportRequest { Collect = (_, _) => [] }, Install, CrashReports.FileList(zip)), []);
        using var http = new HttpClient(new Answer(HttpStatusCode.BadRequest, "{\"ok\":false,\"error\":\"invalid file name\"}"));

        var result = await CrashReports.SendAsync(http, pack, endpoint: "https://example.test/crash");

        Assert.Equal(CrashReportSendStatus.Refused, result.Status);
        Assert.Equal("HTTP 400, the worker said: invalid file name", result.Detail);
        Assert.Contains("(400: invalid file name)", result.Message);
        Assert.Contains("Save a copy instead", result.Message);
        Assert.Null(CrashReports.ReadError("not json"));
        Assert.Null(CrashReports.ReadError("{\"ok\":false}"));
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.Contains("result.Detail ?? result.Message", SourceFiles.Method(window, "private async Task SendAsync("));
    }

    // The file names of the live test's two zips, plus names with characters the worker refuses.
    private static readonly string[] LiveNames =
    [
        "server-logs/coop-server-20261012-143749.log", "host-console/host-console-20261022-230234.log", "host/host-action.log",
        "settings/server-config.json", "server-logs/ServerSettingsLock.log", "settings/mod-config.json", "server-folder/release-info.txt",
        "server-folder/modded-server-selection.json", "server-folder/invalid-module-versions.log", "engine-logs/rgl_log_22960.txt",
        "engine-logs/rgl_log_errors_32216.txt", "engine-logs/watchdog_log_32216.txt", "host/server-window.txt", "host/console-window.txt",
        "game/BootPatches.log", "game/NoHarmony.txt", "game-logs/taom_debug_2026-10-18_00-44-35.log",
        "crash-reports/2026-10-22_21-33-54_client_31088_edea1fa1/game-binaries.json",
        "crash-reports/2026-10-22_21-33-54_client_31088_edea1fa1/logs/rgl_log_31088.txt",
        "crash-reports/2026-10-22_21-33-54_client_31088_edea1fa1/shareable.zip/logs/Coop_client.log",
        "launcher/client-action.log", "launcher/activity-log.txt",
        @"engine-logs\rgl_log_31088.txt",
        "crash-reports/My crash, #2 @home=1 it's/report.txt",
        @"C:\Users\Admin\Documents\Mount and Blade II Bannerlord\Configs\engine_config.txt",
        "server-logs/coop-server 76561198000000001 {x}.log",
        "hard-crashes/../../escape.txt",
        "game-logs/ünïcödé ログ.log",
    ];

    [Fact]
    public void The_live_file_list_packs_into_a_zip_whose_names_the_worker_takes()
    {
        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true, MaxTextBytes: CrashReports.FirstTextBytes));
        for (var i = 0; i < LiveNames.Length; i++)
            builder.AddText(LiveNames[i], "log " + i);
        var zip = Path.Combine(_directory, "live.zip");
        builder.Write(zip, "notes");
        CrashReports.AddGenerated(zip, new CrashReportRequest { Side = "server", Reason = CrashReason.ServerCrash, ToolVersion = "1.2.62", Collect = (_, _) => [] });

        var files = CrashReports.FileList(zip);
        var summary = CrashReports.BuildSummary(new CrashReportRequest
        {
            Side = "server", Reason = CrashReason.ServerCrash, ToolVersion = "1.2.62", Coop = "v0.1.6", Game = "v1.4.8", Collect = (_, _) => []
        }, Install, files);

        // The summary lists exactly the zip's names, and every one is a name the worker's FILE rule takes.
        Assert.Equal(files.Select(file => file.Name), summary.Files.Select(file => file.Name));
        var workerFile = new Regex(@"^[\p{L}\p{N}_.\-+ ()\[\]/]{1,200}$");
        foreach (var file in summary.Files)
        {
            Assert.Matches(workerFile, file.Name);
            Assert.False(file.Name.StartsWith('/'));
            Assert.DoesNotContain("..", file.Name.Split('/'));
            Assert.DoesNotContain("7656119", file.Name);
            Assert.DoesNotContain("Admin", file.Name);
        }

        Assert.Contains("engine-logs/rgl_log_31088.txt", summary.Files.Select(file => file.Name));
        Assert.Contains("C_/Users/[user]/Documents/Mount and Blade II Bannerlord/Configs/engine_config.txt", summary.Files.Select(file => file.Name));
        AssertWorkerTakes(CrashReports.SummaryJson(summary), summary.Files.Count);
    }

    [Fact]
    public void An_empty_coop_and_game_version_is_still_a_summary_the_worker_takes()
    {
        var summary = CrashReports.BuildSummary(new CrashReportRequest { Side = "server", Reason = CrashReason.ServerCrash, Collect = (_, _) => [] },
            Install, [new CrashReportFile("NOTES.txt", 10)]);
        Assert.Equal("", summary.Coop);
        Assert.Equal("", summary.Game);
        AssertWorkerTakes(CrashReports.SummaryJson(summary), 1);
    }

    [Fact]
    public void Coodsz_Host_sends_the_Coop_and_game_versions_from_its_module_list()
    {
        var source = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var request = SourceFiles.Method(source, "private CrashReportRequest CrashRequest(");
        Assert.Contains("ModuleVersion(\"Coop\")", request);
        Assert.Contains("ModuleVersion(\"Native\", \"DedicatedServer.Windows\")", request);
    }

    // Runs the summary through the worker's own validateSummary with node, when node is on this PC.
    private void AssertWorkerTakes(string summaryJson, int fileCount)
    {
        var node = NodePath();
        if (node == null)
            return;
        var script = Path.Combine(_directory, "validate.mjs");
        File.WriteAllText(script, """
            import { readFileSync } from 'node:fs';
            import { pathToFileURL } from 'node:url';
            const [,, mod, file] = process.argv;
            const { validateSummary } = await import(pathToFileURL(mod).href);
            console.log(JSON.stringify(validateSummary(JSON.parse(readFileSync(file, 'utf8')))));
            """);
        var summaryPath = Path.Combine(_directory, "summary.json");
        File.WriteAllText(summaryPath, summaryJson, new UTF8Encoding(false));
        var worker = Path.Combine(RepoRoot(), "server", "mod-reports-worker", "src", "crash.js");
        var start = new ProcessStartInfo(node) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = _directory };
        foreach (var argument in new[] { script, worker, summaryPath })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "node did not finish");
        Assert.True(process.ExitCode == 0, error);
        using var result = JsonDocument.Parse(output);
        // A string is the worker's reason for a 400; an object is a summary it takes.
        Assert.True(result.RootElement.ValueKind == JsonValueKind.Object, "the worker refused the summary: " + output);
        Assert.Equal(fileCount, result.RootElement.GetProperty("files").GetArrayLength());
    }

    private static string? NodePath()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(installed))
            return installed;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder.Trim(), "node.exe"))
            .FirstOrDefault(File.Exists);
    }

    // ----- The Windows user name -----

    [Theory]
    [InlineData("[21:15:13.606] Computer: [user] User: Admin", "[21:15:13.606] Computer: [user] User: [user]")]
    [InlineData("[23:10:50.495] Computer: DESKTOP-QX42ZZ User: Admin", "[23:10:50.495] Computer: [user] User: [user]")]
    [InlineData("[23:10:50.495] Computer: GAMING-RIG User: John Smith  ", "[23:10:50.495] Computer: [user] User: [user]  ")]
    public void The_engine_log_computer_line_loses_both_names_whatever_they_are(string line, string expected)
    {
        LogScrubber.PrivateNames = [];
        var text = "[21:15:13.600] Mount and Blade II Bannerlord\r\n[21:15:13.601] Version: v1.4.8\r\n" + line + "\r\n[21:15:13.700] Next line";
        var cleaned = LogZipBuilder.Clean(text);
        Assert.Contains("\r\n" + expected + "\r\n", cleaned);
        Assert.Contains("Version: v1.4.8", cleaned);
        Assert.Contains("Next line", cleaned);
    }

    [Theory]
    [InlineData("User=Admin", "User=[user]")]
    [InlineData("UserName=Admin;", "UserName=[user];")]
    [InlineData("USERNAME = Admin", "USERNAME = [user]")]
    [InlineData("\"userName\": \"Admin\",", "\"userName\": \"[user]\",")]
    [InlineData("ComputerName=GAMING-RIG", "ComputerName=[user]")]
    [InlineData("user: Admin joined", "user: [user] joined")]
    public void A_user_key_loses_its_value_even_a_common_name(string line, string expected)
    {
        LogScrubber.PrivateNames = [];
        Assert.Equal(expected, LogZipBuilder.Clean(line));
    }

    [Fact]
    public void This_PCs_user_name_in_a_path_is_taken_out_even_when_it_is_a_common_word()
    {
        var cleaned = LogZipBuilder.Clean(@"Loading C:\Users\Admin\Documents\Mount and Blade II Bannerlord\Configs and C:/Users/Admin/AppData and {""p"":""C:\\Users\\Admin\\x""}");
        Assert.DoesNotContain("Admin", cleaned);
        Assert.Contains(@"Users\[user]\Documents", cleaned);
        Assert.Contains("Users/[user]/AppData", cleaned);
        // A common word that is not in a path or after a user key stays.
        Assert.Contains("Loading module Admin settings", LogZipBuilder.Clean("Loading module Admin settings"));
        Assert.Contains("Admin panel opened", LogZipBuilder.Clean("Admin panel opened"));
    }

    [Fact]
    public void The_live_zips_had_no_admin_left_after_cleaning()
    {
        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true));
        builder.AddText("engine-logs/rgl_log_31088.txt",
            "[21:15:13.600] Starting\r\n[21:15:13.601] Build: 1.4.8\r\n[21:15:13.606] Computer: [user] User: Admin\r\n[21:15:14.000] Path C:\\Users\\Admin\\Documents\r\n");
        var zip = Path.Combine(_directory, "admin.zip");
        builder.Write(zip, "notes");
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry("engine-logs/rgl_log_31088.txt")!.Open());
        var text = reader.ReadToEnd();
        Assert.DoesNotContain("Admin", text);
        Assert.Contains("Computer: [user] User: [user]", text);
    }

    // ----- Duplicate logs in Send my logs -----

    [Fact]
    public void The_same_log_from_two_places_goes_in_once()
    {
        var crashCopy = Write("a/rgl_log_31088.txt", "the same engine log");
        var engineCopy = Write("b/rgl_log_31088.txt", "the same engine log");
        var other = Write("b/rgl_log_32216.txt", "another engine log");
        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true));
        builder.AddFile(crashCopy, "crash-reports/x/logs/rgl_log_31088.txt");
        builder.AddFile(engineCopy, "engine-logs/rgl_log_31088.txt");
        builder.AddFile(other, "engine-logs/rgl_log_32216.txt");
        var zip = Path.Combine(_directory, "dupes.zip");
        var result = builder.Write(zip, "notes");

        var names = CrashReports.FileList(zip).Select(file => file.Name).ToList();
        Assert.Contains("crash-reports/x/logs/rgl_log_31088.txt", names);
        Assert.DoesNotContain("engine-logs/rgl_log_31088.txt", names);
        Assert.Contains("engine-logs/rgl_log_32216.txt", names);
        Assert.Contains(result.Skipped, line => line.StartsWith("1 file was the same as another file", StringComparison.Ordinal));
    }

    [Fact]
    public void Send_my_logs_packs_only_the_newest_recent_crash_folder_and_not_its_shareable_zip_again()
    {
        var documents = Path.Combine(_directory, "Documents");
        var reports = Path.Combine(documents, "Coop Crash Reports");
        var older = MakeCrashFolder(reports, "2026-10-22_19-49-41_client_26520_213679ac", "26520", DateTime.UtcNow.AddHours(-2));
        var newest = MakeCrashFolder(reports, "2026-10-22_21-33-54_client_31088_edea1fa1", "31088", DateTime.UtcNow.AddMinutes(-5));
        var stale = MakeCrashFolder(reports, "2026-09-01_10-00-00_client_1111_aaaaaaaa", "1111", DateTime.UtcNow.AddDays(-30));
        var zip = Path.Combine(_directory, "client.zip");

        ClientLogZip.Create(zip, null, documents, Path.Combine(_directory, "ProgramData"), limits: new LogZipLimits(TextOnly: true));

        var names = CrashReports.FileList(zip).Select(file => file.Name).ToList();
        var folder = "crash-reports/" + Path.GetFileName(newest) + "/";
        Assert.Contains(folder + "report.txt", names);
        Assert.Contains(folder + "logs/rgl_log_31088.txt", names);
        // The shareable.zip adds only the file the folder lacks.
        Assert.Contains(folder + "shareable.zip/logs/Coop_client.log", names);
        Assert.DoesNotContain(folder + "shareable.zip/report.txt", names);
        Assert.DoesNotContain(folder + "shareable.zip/logs/rgl_log_31088.txt", names);
        Assert.DoesNotContain(names, name => name.Contains(Path.GetFileName(older), StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains(Path.GetFileName(stale), StringComparison.Ordinal));
    }

    // ----- One bar at a time -----

    [Fact]
    public void The_host_shows_one_red_bar_one_card_and_the_send_logs_link_on_the_Dashboard()
    {
        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        // The crash bar goes on any start the host makes and once the server serves again.
        Assert.Contains("_crashBar?.ServerStarted();", SourceFiles.Method(host, "private void StartServer("));
        Assert.Contains("_crashBar?.ServerStarted();", host.Substring(host.IndexOf("var becameServing = !_serving;", StringComparison.Ordinal), 500));
        Assert.Contains("_crashBar?.ServerStarted();", SourceFiles.Method(SourceFiles.Window(RepoRoot(), "TaomCoopHost"), "private void RefreshState("));
        // The Realm of Thrones server tool too, once the new server process runs.
        var rotStart = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "RealmOfThronesHost"), "private async Task StartServerCoreAsync(");
        Assert.True(rotStart.IndexOf("_process = process;", StringComparison.Ordinal) < rotStart.IndexOf("_crashBar?.ServerStarted();", StringComparison.Ordinal));
        // The old Open the Logs tab banner never shows next to the crash bar.
        var alert = SourceFiles.Method(host, "private void StartLogAlert(");
        Assert.Contains("_crashBar?.ShowsCrash == true ? Visibility.Collapsed : Visibility.Visible", alert);
        Assert.Contains("HideLogAlertBanner();", SourceFiles.Method(host, "private void OfferServerCrashReport("));
        // A serving line from a run that ended is not left under the Dashboard's server buttons.
        Assert.Contains("SetControl(\"Server is stopped.\");", SourceFiles.Method(host, "private void OnServerExited("));
        // Something went wrong? Send your logs under Start server on the Dashboard (the one link, no Home page since wave 2).
        var dashboard = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml")).Descendants()
            .Single(e => (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DashboardPage");
        Assert.Contains(dashboard.Descendants(), e => (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "SendLogsLinkSlot");
        Assert.Contains("SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this,", SourceFiles.Method(host, "private void SetUpCrashReports("));
        // The one time cards wait for a start without What's new.
        foreach (var file in new[] { "CrashReportWindow.cs", "ModReportWindow.cs" })
            Assert.Contains("UiLanguage.StartupPopupThisStart", SourceFiles.Method(File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", file)), "public void ShowIntroOnce("));
        // The Cheats page admin log opens on any toggle, not only a mouse click.
        var cheats = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "CheatsPage.xaml"));
        Assert.Contains("Checked=\"LogToggle_Click\" Unchecked=\"LogToggle_Click\"", cheats);
    }

    private static string MakeCrashFolder(string reports, string name, string session, DateTime writtenUtc)
    {
        var folder = Path.Combine(reports, name);
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        File.WriteAllText(Path.Combine(folder, "report.txt"), "report of " + name);
        File.WriteAllText(Path.Combine(folder, "logs", "rgl_log_" + session + ".txt"), "engine log of " + name);
        var shareable = Path.Combine(folder, "shareable.zip");
        using (var archive = ZipFile.Open(shareable, ZipArchiveMode.Create))
        {
            foreach (var (entry, text) in new[] { ("report.txt", "report of " + name + " (game copy)"), ("logs/rgl_log_" + session + ".txt", "engine log of " + name + " (game copy)"), ("logs/Coop_client.log", "client log of " + name) })
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                writer.Write(text);
            }
        }

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, writtenUtc);
        Directory.SetLastWriteTimeUtc(Path.Combine(folder, "logs"), writtenUtc);
        Directory.SetLastWriteTimeUtc(folder, writtenUtc);
        return folder;
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
