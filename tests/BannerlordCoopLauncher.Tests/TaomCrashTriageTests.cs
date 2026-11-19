using System.IO.Compression;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// Crash auto triage in the TAOM tools: the tools field of a signature, the newestOnly rule option,
// the TAOM.CoopCompat startup check and the graphics driver error.
public sealed class TaomCrashTriageTests : IDisposable
{
    private static readonly CrashSignatureFile Signatures = CrashSignatureFile.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "compat-list", "crash-signatures.json")))!;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-taom-triage-" + Guid.NewGuid().ToString("N"));

    private const string StartupCheckLine = "[DedicatedServer] FATAL during a: System.InvalidOperationException: TAOM.CoopCompat rejected AssertPreServingReady: TAOM.CoopCompat pre-serving attestation failed: module=True, resolverAction=True, wrapper=True, facegen=True, monsters=False, servingHardStop=True, faulted=False";
    private const string GpuRgl = "[18:02:11.104] Loading terrain...\n[18:02:12.551] create_texture_array failed with error 0x887A0005\n[18:02:12.552] Exception\n";
    private const string AssetsRgl = "[20:14:48.304] Loading packages $BASE/Modules/RaiseYourBanner/Assets...\n[20:14:48.309] Registering items...\n";

    public TaomCrashTriageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static CrashEvidence Evidence(string side, string? tool, int? exitCode, string source, params string[] texts) => new()
    {
        Side = side,
        Tool = tool,
        ExitCode = exitCode,
        Texts = new Dictionary<string, IReadOnlyList<string>> { [source] = texts }
    };

    [Fact]
    public void The_TAOM_startup_check_is_known_and_still_asks_for_the_report()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Evidence("server", "taom", 2, CrashLogSource.HostConsole, "[TAOM host] Start dotnet.exe\n" + StartupCheckLine + "\n[TAOM host] Server exited."));

        Assert.NotNull(match);
        Assert.Equal("taom-coopcompat-startup-check", match!.Signature.Id);
        Assert.True(match.Signature.ReportNeeded);
        Assert.Equal(["taom-startup-check"], match.Signature.Labels);
        Assert.Contains("your save is fine", match.WhatHappened);
        Assert.Contains("Press Start server again", match.WhatToDo);
        Assert.Equal(CrashScreenMode.KnownSendHelps, CrashScreen.ModeFor("taom", CrashReason.ServerCrash, true, match));
        Assert.True(CrashScreen.BigSend(CrashScreen.ModeFor("taom", CrashReason.ServerCrash, true, match)));
        Assert.Contains(match.Evidence, line => line.Contains("pre-serving attestation failed", StringComparison.Ordinal));

        // The other wording alone is enough too.
        Assert.Equal("taom-coopcompat-startup-check", CrashSignatureMatcher.Match(Signatures,
            Evidence("server", "taom", null, CrashLogSource.HostConsole, "TAOM.CoopCompat rejected AssertPreServingReady"))?.Signature.Id);
    }

    [Fact]
    public void The_startup_check_only_counts_in_the_newest_run_and_only_for_the_TAOM_tools()
    {
        // An older run's console log (second) had it; the newest run crashed some other way.
        var older = CrashSignatureMatcher.Match(Signatures, Evidence("server", "taom", 2, CrashLogSource.HostConsole, "engine started\nunrelated crash", StartupCheckLine));
        Assert.NotEqual("taom-coopcompat-startup-check", older?.Signature.Id);

        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("server", "coop", 2, CrashLogSource.HostConsole, StartupCheckLine)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("server", "ad1259", 2, CrashLogSource.HostConsole, StartupCheckLine)));
        // Evidence without a tool (older callers) still sees every signature.
        Assert.Equal("taom-coopcompat-startup-check", CrashSignatureMatcher.Match(Signatures, Evidence("server", null, 2, CrashLogSource.HostConsole, StartupCheckLine))?.Signature.Id);
    }

    [Theory]
    [InlineData("client", "coop")]
    [InlineData("client", "taom")]
    [InlineData("client", "ad1259")]
    [InlineData("server", "taom")]
    public void A_graphics_driver_error_needs_no_report_in_every_tool(string side, string tool)
    {
        var match = CrashSignatureMatcher.Match(Signatures, Evidence(side, tool, 5, CrashLogSource.Rgl, GpuRgl));

        Assert.NotNull(match);
        Assert.Equal("gpu-create-texture-array", match!.Signature.Id);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Equal(["gpu"], match.Signature.Labels);
        Assert.Contains("graphics driver", match.WhatToDo);
        var reason = side == "server" ? CrashReason.ServerCrash : CrashReason.Crash;
        Assert.Equal(CrashScreenMode.KnownNoReport, CrashScreen.ModeFor(tool, reason, true, match));
        Assert.False(CrashScreen.BigSend(CrashScreenMode.KnownNoReport));
    }

    [Fact]
    public void The_graphics_error_needs_the_engine_exit_code_5_so_an_old_log_cannot_match_another_crash()
    {
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("client", "coop", null, CrashLogSource.Rgl, GpuRgl)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("client", "coop", -1, CrashLogSource.Rgl, GpuRgl)));
    }

    [Fact]
    public void A_graphics_error_in_an_older_engine_log_does_not_count()
    {
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("client", "taom", null, CrashLogSource.Rgl, "[18:02:11.104] Loading terrain...\n", GpuRgl)));
    }

    [Fact]
    public void Coodsz_Host_only_signatures_stay_out_of_the_TAOM_tools()
    {
        foreach (var id in new[] { "server-raw-assets", "join-closed-mod-mismatch" })
            Assert.Equal(["coop", "ad1259"], Signatures.Signatures.Single(s => s.Id == id).Tools);
        // The Burning Empires and Realm of Thrones server tools run a Coop dedicated server too, so a silent exit reads the same there.
        foreach (var id in new[] { "server-exit-code-1-silent", "server-exit-code-minus-1-silent" })
            Assert.Equal(["coop", "ad1259", "be", "rot"], Signatures.Signatures.Single(s => s.Id == id).Tools);
        Assert.Equal("server-exit-code-1-silent", CrashSignatureMatcher.Match(Signatures, Evidence("server", "be", 1, CrashLogSource.Server, "Server stopped."))?.Signature.Id);
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("server", "be", -1073741819, CrashLogSource.Rgl, AssetsRgl)));

        Assert.Equal("server-raw-assets", CrashSignatureMatcher.Match(Signatures, Evidence("server", "coop", -1073741819, CrashLogSource.Rgl, AssetsRgl))?.Signature.Id);
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("server", "taom", -1073741819, CrashLogSource.Rgl, AssetsRgl)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Evidence("server", "taom", 1, CrashLogSource.Server, "Server stopped.")));
        Assert.Equal("server-exit-code-1-silent", CrashSignatureMatcher.Match(Signatures, Evidence("server", "coop", 1, CrashLogSource.Server, "Server stopped."))?.Signature.Id);
    }

    [Fact]
    public void ToolFits_keeps_signatures_without_tools_for_every_tool()
    {
        Assert.True(CrashSignatureMatcher.ToolFits(null, "taom"));
        Assert.True(CrashSignatureMatcher.ToolFits([], "coop"));
        Assert.True(CrashSignatureMatcher.ToolFits(["taom"], null));
        Assert.True(CrashSignatureMatcher.ToolFits(["TAOM"], "taom"));
        Assert.False(CrashSignatureMatcher.ToolFits(["taom"], "coop"));
    }

    [Fact]
    public void A_TAOM_server_crash_report_zip_is_triaged_from_its_newest_host_console_log()
    {
        var zipPath = Path.Combine(_directory, "taom-host.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // HostLogPack writes the newest host-console log first.
            Put(zip, "host-console/host-console-20261024-120500.log", "[TAOM host] Start dotnet.exe\n" + StartupCheckLine + "\n[TAOM host] Server exited.\n");
            Put(zip, "host-console/host-console-20261024-110000.log", "[TAOM host] Start dotnet.exe\n[DedicatedServer] SERVING\n");
            Put(zip, "host/server-window.txt", TaomCoop.Core.TaomStartupCheck.Text);
        }

        var evidence = CrashEvidence.FromZip(zipPath, "server", 2, tool: "taom");
        Assert.Equal("taom", evidence.Tool);
        Assert.Equal("taom-coopcompat-startup-check", CrashSignatureMatcher.Match(Signatures, evidence)?.Signature.Id);
        Assert.Null(CrashSignatureMatcher.Match(Signatures, CrashEvidence.FromZip(zipPath, "server", 2, tool: "coop")));

        var request = new CrashReportRequest { Tool = "taom", Side = "server", Reason = CrashReason.ServerCrash, ExitCode = 2, Collect = (_, _) => [] };
        var (ran, match, triage) = CrashReports.Triage(zipPath, request, Signatures);
        Assert.True(ran);
        Assert.Equal("taom-coopcompat-startup-check", match?.Signature.Id);
        Assert.Equal(["taom-startup-check"], triage?.Labels);
        Assert.Equal(CrashCategory.Setup, triage?.Category);
    }

    private static void Put(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
