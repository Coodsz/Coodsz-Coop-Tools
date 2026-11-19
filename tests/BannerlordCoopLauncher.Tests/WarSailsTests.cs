using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

// War Sails (NavalDLC) in Coop's public build: the server's new exit codes 7 to 11, their known causes, the
// Start server refusal in every server tool and the join lines for the NavalDLC refusals.
public sealed class WarSailsTests : IDisposable
{
    private static readonly CrashSignatureFile Signatures = CrashSignatureFile.Parse(SourceFiles.Read(Path.Combine("compat-list", "crash-signatures.json")))!;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-warsails-" + Guid.NewGuid().ToString("N"));

    public WarSailsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData(7, ServerExitKind.DevModuleRestageFailed)]
    [InlineData(8, ServerExitKind.ModuleMissing)]
    [InlineData(9, ServerExitKind.WarSailsSaveMismatch)]
    [InlineData(10, ServerExitKind.WarSailsSetup)]
    [InlineData(11, ServerExitKind.SaveDidNotLoad)]
    public void The_new_exit_codes_are_never_restarted(int code, ServerExitKind kind)
    {
        Assert.Equal(kind, ServerExit.Classify(code));
        // Before, they were Unknown and the crash restart started the same failing server again and again.
        Assert.False(ServerExit.IsAutoRestartable(kind));
        Assert.False(new CrashRestartPolicy().Evaluate(ServerExitInfo.From(code, wasExpected: false), DateTimeOffset.UtcNow).ShouldRestart);
    }

    [Fact]
    public void Only_a_save_that_did_not_start_loading_is_a_load_crash()
    {
        Assert.True(LoadCrashRetry.IsLoadCrash(ServerExitInfo.From(11, wasExpected: false), servedThisRun: false));
        Assert.True(LoadCrashRetry.IsLoadCrash(ServerExitInfo.From(2, wasExpected: false), servedThisRun: false));
        foreach (var code in new[] { 4, 6, 7, 8, 9, 10 })
            Assert.False(LoadCrashRetry.IsLoadCrash(ServerExitInfo.From(code, wasExpected: false), servedThisRun: false));
    }

    [Theory]
    [InlineData("[DedicatedServer] FATAL: save C:\\Saves\\camp.sav was not created with NavalDLC, and War Sails is on. A campaign can't gain NavalDLC later.", "warSails")]
    [InlineData("[DedicatedServer] FATAL: NavalDLC is in the module list, but \"warSails\" is off in C:\\CoopData\\mod-config.json.", "warSails")]
    [InlineData("[DedicatedServer] FATAL: \"warSails\" is true in C:\\CoopData\\mod-config.json, but a naval assembly is missing or failed to load", "warSails")]
    [InlineData("[DedicatedServer] FATAL: save 'camp' did not start loading (a damaged file or a Coop error, see the error above). Pick another save or check the error.", "saveDidNotLoad")]
    [InlineData("[BannerlordCoopServer] dev module re-stage failed: Coop", "devModuleRestage")]
    [InlineData("[BannerlordCoopServer] FATAL: module 'Foo' listed in modules is missing", "coopServerFatal")]
    public void The_new_fatal_lines_are_errors(string line, string key)
    {
        var known = KnownLogLines.Match(line);
        Assert.Equal(key, known?.Key);
        Assert.Equal(LogSeverity.Error, known!.Severity);
    }

    [Theory]
    [InlineData(9, "[DedicatedServer] FATAL: save C:\\Saves\\camp.sav was created with NavalDLC, and War Sails is off. A NavalDLC campaign can't load without it.", "coop-ds-war-sails-save-mismatch")]
    [InlineData(9, "@DS@{\"ev\":\"war-sails\",\"refusal\":\"save-mismatch\",\"save\":\"camp\"}", "coop-ds-war-sails-save-mismatch")]
    [InlineData(10, "[DedicatedServer] FATAL: \"warSails\" is true in C:\\CoopData\\mod-config.json, but NavalDLC isn't in the module list.", "coop-ds-war-sails-setup")]
    [InlineData(10, "[DedicatedServer] FATAL: campaign mode has no coop stack, so it can't run War Sails (NavalDLC).", "coop-ds-war-sails-setup")]
    [InlineData(11, "[DedicatedServer] FATAL: save 'camp' did not start loading (a damaged file or a Coop error, see the error above). Pick another save or check the error.", "coop-ds-save-did-not-load")]
    [InlineData(8, "[BannerlordCoopServer] FATAL: module 'Foo' listed in modules is missing", "coop-server-launcher-module-missing")]
    [InlineData(7, "[BannerlordCoopServer] dev module re-stage failed: Coop", "coop-server-launcher-module-missing")]
    public void Each_exit_has_its_known_cause_in_every_tool(int code, string line, string id)
    {
        foreach (var tool in new[] { "coop", "ad1259", "taom", "be" })
        {
            foreach (var source in new[] { CrashLogSource.Server, CrashLogSource.ServerLog, CrashLogSource.HostConsole })
                Assert.Equal(id, ServerMatch(code, tool, source, line)?.Signature.Id);
        }
    }

    [Fact]
    public void The_save_name_goes_into_the_text_and_a_normal_exit_matches_none()
    {
        var match = ServerMatch(11, "coop", CrashLogSource.Server, "[DedicatedServer] FATAL: save 'my camp' did not start loading (a damaged file or a Coop error, see the error above).");
        Assert.Contains("The save my camp did not start loading", match!.WhatHappened, StringComparison.Ordinal);

        string[] ours = ["coop-ds-war-sails-save-mismatch", "coop-ds-war-sails-setup", "coop-ds-save-did-not-load", "coop-server-launcher-module-missing"];
        Assert.DoesNotContain(ServerMatch(0, "coop", CrashLogSource.Server, "[DedicatedServer] SERVING\n[DedicatedServer] shutdown save: world saved to 'camp.sav' (10 ms)")?.Signature.Id, ours);
        // The FATAL line of another code is not one of these.
        Assert.DoesNotContain(ServerMatch(3, "coop", CrashLogSource.Server, "[DedicatedServer] FATAL: save 'camp' did not start loading")?.Signature.Id, ours);
        // The code alone is not enough either.
        Assert.DoesNotContain(ServerMatch(9, "coop", CrashLogSource.Server, "[DedicatedServer] SERVING")?.Signature.Id, ours);
    }

    [Theory]
    [InlineData("Module validation failed!\nReason: To join the server the module 'War Sails' (NavalDLC) with version 'v1.3.0' is required.", "join-server-needs-war-sails")]
    [InlineData("Module validation failed!\nReason: To join the server the module 'NavalDLC' with version 'v1.3.0' is required.", "join-server-needs-war-sails")]
    [InlineData("Module validation failed!\nReason: DLC is not supported. Please disable the following module(s): 'War Sails' (NavalDLC).", "join-war-sails-on-in-game")]
    public void The_NavalDLC_join_refusals_have_a_known_cause(string log, string id)
    {
        foreach (var tool in new[] { "coop", "ad1259", "taom", "be" })
        {
            var match = CrashSignatureMatcher.Match(Signatures, new CrashEvidence
            {
                Side = "client",
                Tool = tool,
                Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.CoopClient] = [log] }
            });
            Assert.Equal(id, match?.Signature.Id);
        }

        // Another required module is not War Sails.
        Assert.DoesNotContain(CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "client",
            Tool = "coop",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.CoopClient] = ["To join the server the module 'Diplomacy' (Bannerlord.Diplomacy) with version 'v1' is required."] }
        })?.Signature.Id, new[] { "join-server-needs-war-sails", "join-war-sails-on-in-game" });
    }

    // The lines the Coop nightly server and Realm of Thrones Coop Compat write (CoopServerHost, CampaignLoadMode,
    // LaunchPlan, SaveModulePreflight, WarSails, SaveLoadFailure and MapRedirect), with their exit codes.
    [Theory]
    [InlineData(2, "[DedicatedServer] LOAD TIMEOUT after 36001 ticks; state=<none>", "rot-server-load-failed")]
    [InlineData(2, "[DedicatedServer] game manager loaded after 412 ticks but Campaign.Current is null", "rot-server-load-failed")]
    [InlineData(8, "[BannerlordCoopServer] FATAL: module \"ROT-Dragon\" is not installed: C:\\Server\\engine\\Modules\\ROT-Dragon\\SubModule.xml not found", "rot-server-module-missing")]
    [InlineData(9, "[BannerlordCoopServer] FATAL: save C:\\Data\\Game Saves\\camp.sav was not created with NavalDLC, and War Sails is on. A campaign can't gain NavalDLC later.", "rot-server-save-without-war-sails")]
    [InlineData(9, "@DS@{\"ev\":\"war-sails\",\"refusal\":\"save-mismatch\",\"save\":\"camp\"}", "rot-server-save-without-war-sails")]
    [InlineData(10, "[DedicatedServer] FATAL: \"warSails\" is true in C:\\CoopData\\mod-config.json, but this Coop build has no War Sails.", "rot-server-war-sails-setup")]
    [InlineData(11, "[DedicatedServer] FATAL: save 'camp' did not start loading (a damaged file or a Coop error, see the error above). Pick another save or check the error.", "rot-server-save-did-not-load")]
    [InlineData(-532462766, "[RealmOfThrones.CoopCompat] Map redirect: module ROT_Map is not active; the server keeps its own map.", "rot-server-map-crash")]
    public void Each_realm_of_thrones_server_exit_has_its_own_cause(int code, string line, string id)
    {
        foreach (var source in new[] { CrashLogSource.Server, CrashLogSource.HostConsole })
            Assert.Equal(id, ServerMatch(code, "rot", source, line)?.Signature.Id);
        // The other tools never get the Realm of Thrones text, and Realm of Thrones never gets the War Sails off advice.
        Assert.False(ServerMatch(code, "be", CrashLogSource.Server, line)?.Signature.Id.StartsWith("rot-", StringComparison.Ordinal) ?? false);
        Assert.DoesNotContain(ServerMatch(code, "rot", CrashLogSource.Server, line)!.WhatToDo, "set \"warSails\" to false", StringComparison.Ordinal);
    }

    [Fact]
    public void Realm_of_thrones_signatures_fill_their_names_and_leave_a_refused_foreign_save_alone()
    {
        Assert.Contains("the module ROT-Dragon was not", ServerMatch(8, "rot", CrashLogSource.Server,
            "[BannerlordCoopServer] FATAL: module \"ROT-Dragon\" is not installed: x not found")!.WhatHappened, StringComparison.Ordinal);
        // A save Realm of Thrones Coop Compat refused (FreshCampaignRules) is not the damaged save case.
        var refused = "[RealmOfThrones.CoopCompat] !!! Fresh campaign: REFUSED save 'old': it was not made with Realm of Thrones (modules Native, file not found) and is not the server's blank save, so this server cannot load it.\n"
            + "[DedicatedServer] FATAL: save 'old' did not start loading (a damaged file or a Coop error, see the error above).";
        Assert.NotEqual("rot-server-save-did-not-load", ServerMatch(11, "rot", CrashLogSource.Server, refused)?.Signature.Id);
        // A map redirect that worked is no crash cause.
        var worked = ServerMatch(-532462766, "rot", CrashLogSource.Server, "[RealmOfThrones.CoopCompat] Map redirect: using ROT_Map from rot_map.txt.")?.Signature.Id;
        Assert.False(worked?.StartsWith("rot-", StringComparison.Ordinal) == true, worked);
        var join = CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "client",
            Tool = "rot",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.CoopClient] = ["Module validation failed!\nReason: To join the server the module 'War Sails' (NavalDLC) with version 'v1.3.0' is required."] }
        });
        Assert.Equal("rot-join-needs-war-sails", join?.Signature.Id);
        Assert.True(CrashScreen.Applies("rot", CrashReason.ServerCrash));
    }

    [Fact]
    public void The_new_causes_are_translated_into_every_language()
    {
        string[] ids = ["coop-ds-war-sails-save-mismatch", "coop-ds-war-sails-setup", "coop-ds-save-did-not-load", "coop-server-launcher-module-missing", "join-server-needs-war-sails", "join-war-sails-on-in-game"];
        foreach (var id in ids)
        {
            var signature = Signatures.Signatures.Single(item => item.Id == id);
            foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
            {
                var text = signature.Translations![language];
                Assert.False(string.IsNullOrWhiteSpace(text.Title) || string.IsNullOrWhiteSpace(text.WhatHappened) || string.IsNullOrWhiteSpace(text.WhatToDo), id + " " + language);
                Assert.Equal(signature.WhatNotToDo == null, text.WhatNotToDo == null);
            }
        }
    }

    [Theory]
    [InlineData("{ \"modOptions\": { \"warSails\": true } }", true)]
    [InlineData("{ \"ModOptions\": { \"WARSAILS\": true } }", true)]
    [InlineData("{ \"modOptions\": { \"warSails\": \"true\" } }", true)]
    [InlineData("{ \"modOptions\": { \"warSails\": \"TRUE\" } }", true)]
    [InlineData("{ \"modOptions\": { \"warSails\": 1 } }", true)]
    [InlineData("{ \"modOptions\": { \"warSails\": -2.5 } }", true)]
    [InlineData("{\n  // War Sails\n  \"modOptions\": { \"warSails\": true, },\n}", true)]
    [InlineData("{ \"modOptions\": { \"warSails\": false } }", false)]
    [InlineData("{ \"modOptions\": { \"warSails\": 0 } }", false)]
    [InlineData("{ \"modOptions\": { \"warSails\": \"yes\" } }", false)]
    [InlineData("{ \"modOptions\": { } }", false)]
    [InlineData("{ \"warSails\": true }", false)]
    [InlineData("{ \"modOptions\": { \"warSails\": True } }", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void War_Sails_is_read_the_way_the_server_reads_it(string? json, bool on) => Assert.Equal(on, WarSailsCheck.IsOn(json));

    [Fact]
    public void Start_is_refused_when_War_Sails_is_on_or_NavalDLC_is_in_the_list()
    {
        var path = Path.Combine(_directory, "mod-config.json");
        string[] plain = ["Native", "SandBoxCore", "Sandbox", "Coop", "DedicatedServer.Windows"];
        Assert.Null(WarSailsCheck.Refusal(path, plain));

        File.WriteAllText(path, "{ \"modOptions\": { \"warSails\": false } }");
        Assert.Null(WarSailsCheck.Refusal(path, plain));
        Assert.Equal(WarSailsCheck.ModuleRefusal, WarSailsCheck.Refusal(path, [.. plain, "navaldlc"]));

        File.WriteAllText(path, "{ \"modOptions\": { \"warSails\": true } }");
        var refusal = WarSailsCheck.Refusal(path, plain);
        Assert.StartsWith("This tool can't host War Sails yet.", refusal, StringComparison.Ordinal);
        Assert.Contains("Set it to false in " + path + ",", refusal, StringComparison.Ordinal);
    }

    // Each server tool checks before the engine starts: Start server after the mod-config.json repair (a capital True
    // becomes true there), and the mod test before it asks about the server terms.
    [Theory]
    [InlineData("CoodszHost", "private void StartServer()", "RepairModConfigFile();", "_session.Start(")]
    [InlineData("TaomCoopHost", "private async Task StartServerCoreAsync()", "RepairModConfig();", "Process.Start(")]
    [InlineData("BurningEmpiresHost", "private async Task StartServerCoreAsync(bool prepareIfNeeded)", "RepairModConfig();", "Process.Start(")]
    public void Every_server_tool_refuses_War_Sails_before_it_starts(string project, string startMethod, string repair, string engineStart)
    {
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        var start = SourceFiles.Method(window, startMethod);
        var check = start.IndexOf("WarSailsCheck.Refusal(", StringComparison.Ordinal);
        Assert.True(check > start.IndexOf(repair, StringComparison.Ordinal) && start.IndexOf(repair, StringComparison.Ordinal) >= 0, project);
        var launch = start.IndexOf(engineStart, StringComparison.Ordinal);
        Assert.True(launch < 0 || check < launch, project);

        var test = SourceFiles.Method(window, "private async Task<string> RunModTestsAsync(");
        var testCheck = test.IndexOf("WarSailsCheck.Refusal(", StringComparison.Ordinal);
        Assert.True(testCheck >= 0 && testCheck < test.IndexOf("AskServerTerms(", StringComparison.Ordinal), project);
    }

    [Fact]
    public void The_join_says_what_the_NavalDLC_and_version_refusals_mean()
    {
        var needs = JoinResultExplain.Explain("Module validation failed!\nReason: To join the server the module 'War Sails' (NavalDLC) with version 'v1.3.0' is required.");
        Assert.Contains(JoinResultExplain.WarSailsServer, needs);
        Assert.DoesNotContain(JoinResultExplain.WarSailsInGame, needs);

        var on = JoinResultExplain.Explain("Module validation failed!\nReason: DLC is not supported. Please disable the following module(s): 'NavalDLC'.");
        Assert.Contains(JoinResultExplain.WarSailsInGame, on);
        Assert.DoesNotContain(JoinResultExplain.WarSailsServer, on);

        var mcm = JoinResultExplain.Explain("Module validation failed!\nReason: Wrong version of module 'Mod Configuration Menu v5' (Bannerlord.MBOptionScreen) detected. Server uses 'v5.13.1', client uses 'v5.12.3'.");
        Assert.Contains("Mod Configuration Menu v5 does not match the server. The server is v5.13.1. Yours is v5.12.3. The server has a newer one than yours.", mcm);
        Assert.Contains("The host and every player need the same version of Mod Configuration Menu v5.", mcm);
        Assert.Contains(JoinResultExplain.McmSameVersion, mcm);

        var other = JoinResultExplain.Explain("Module validation failed!\nReason: Wrong version of module 'ButterLib' (Bannerlord.ButterLib) detected. Server uses 'v2.12.0.0', client uses 'v2.10.4.0'.");
        Assert.Contains("The host and every player need the same version of ButterLib.", other);
        Assert.DoesNotContain(JoinResultExplain.McmSameVersion, other);
        Assert.DoesNotContain(JoinResultExplain.WarSailsServer, other);
    }

    // Realm of Thrones runs with War Sails on (RotServerPath.FullServerOrder, warSails true in mod-config.json), so its
    // Server Tool never runs the refusal, its tools never say War Sails can't be hosted, and the signatures that say
    // so are not for its reports.
    [Fact]
    public void Realm_of_thrones_needs_war_sails_so_nothing_refuses_or_warns_against_it()
    {
        var needs = JoinResultExplain.Explain("Module validation failed!\nReason: To join the server the module 'War Sails' (NavalDLC) with version 'v1.3.0' is required.", warSailsSetup: true);
        Assert.DoesNotContain(JoinResultExplain.WarSailsServer, needs);
        var on = JoinResultExplain.Explain("Module validation failed!\nReason: DLC is not supported. Please disable the following module(s): 'NavalDLC'.", warSailsSetup: true);
        Assert.DoesNotContain(JoinResultExplain.WarSailsInGame, on);

        var host = SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");
        Assert.DoesNotContain("WarSailsCheck.Refusal(", host);
        Assert.Contains("JoinResultExplain.Explain(_logTail.Recent(60), warSailsSetup: true)", host);
        Assert.Contains("JoinResultExplain.Explain(text, warSailsSetup: true)", SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesLauncher"));
        Assert.Contains(RealmOfThrones.Core.RotStack.NavalModuleId, RealmOfThrones.Core.RotServerPath.FullServerOrder(new TaomCoop.Core.TaomSetup { Name = "Plain" }));

        foreach (var id in new[] { "join-server-needs-war-sails", "join-war-sails-on-in-game", "coop-ds-war-sails-setup", "coop-ds-war-sails-save-mismatch" })
        {
            var tools = Signatures.Signatures.Single(signature => signature.Id == id).Tools;
            Assert.NotNull(tools);
            Assert.DoesNotContain(RealmOfThrones.Core.RotRelease.ToolId, tools!);
        }
    }

    private static CrashSignatureMatch? ServerMatch(int code, string tool, string source, string line) =>
        CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "server",
            ExitCode = code,
            Tool = tool,
            Texts = new Dictionary<string, IReadOnlyList<string>> { [source] = [line] }
        });
}
