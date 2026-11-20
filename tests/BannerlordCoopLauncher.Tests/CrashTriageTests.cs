using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

// Crash auto triage: crash-signatures.json, the matcher, the crash screen modes and what the report
// carries. The log lines are copied from the cleaned reports of Coodsz-Crash-Reports #3 to #13.
public sealed class CrashTriageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-triage-" + Guid.NewGuid().ToString("N"));

    public CrashTriageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private static readonly CrashSignatureFile Signatures = CrashSignatureFile.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "compat-list", "crash-signatures.json")))!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static CrashEvidence Client(int? exitCode = null, string? coopClient = null, string? coopReport = null, string? rgl = null, params string[] modules) =>
        Evidence("client", exitCode, modules, (CrashLogSource.CoopClient, coopClient), (CrashLogSource.CoopReport, coopReport), (CrashLogSource.Rgl, rgl));

    private static CrashEvidence Server(int? exitCode, string? server = null, string? rgl = null, string? serverLog = null) =>
        Evidence("server", exitCode, [], (CrashLogSource.Server, server), (CrashLogSource.Rgl, rgl), (CrashLogSource.ServerLog, serverLog));

    private static CrashEvidence Evidence(string side, int? exitCode, string[] modules, params (string Source, string? Text)[] texts) => new()
    {
        Side = side,
        ExitCode = exitCode,
        Modules = modules,
        Texts = texts.Where(t => t.Text != null).ToDictionary(t => t.Source, t => (IReadOnlyList<string>)[t.Text!])
    };

    // #3: the player left the session and quit; Coop's reporter made a report for exit code -1.
    private const string CleanExitReport = "BannerlordCoop crash report\nCaptured: 2026-10-21T20:54:48.6322046-04:00\nProcess id: 26348\nRole: client\nBuild: 0.1.6+5f4d63cc0975174ab42cfe1da67fc6d7214065e7\nExit code: -1\nDump captured: no\n";
    private const string CleanExitRgl = "[20:54:01.530] Deleting Game... [20:54:01.531] OK!\n[20:54:01.532] Final_cleanup...\n[20:54:01.532] Frame# : 205035.\n[20:54:01.914] Deleting Managed Interface...\n[20:54:02.156] Checking if any managed object still lives... \n[20:54:02.156] There are no living managed objects. \n[20:54:02.156] Managed Interface deleted.\n";

    // #4: CoopModPatch's first time setup, as the Coodsz Host server window shows it.
    internal const string ModPatchWindow = "CoopModPatch   CoopModPatch first-time setup complete\nCoopModPatch   Registered for dedicated-server loading: SettlementCultureDrift\nCoopModPatch   The server was stopped intentionally; start it again to load them.\n";

    // #5: the engine died on the raw Assets folder of RaiseYourBanner.
    private const string AssetsRgl = "[20:14:36.871] Build Version: 118999\n[20:14:48.284] Loading packages $BASE/Modules/DedicatedServer.Windows/DsAssetPackages...\n[20:14:48.294] Registering items...\n[20:14:48.304] Loading packages $BASE/Modules/RaiseYourBanner/Assets...\n[20:14:48.309] Registering items...\n[20:14:48.309] Initializing items\n";

    // #8: joining a running town siege battle, then the endless loop.
    private static readonly string SiegeLoop =
        "[(8104) 22:58:07 INF Missions.Battles.BattleHostHandler] [BattleHost] Mission ready — requested host election for battle MapEvent_Created_41169\n"
        + "[(8104) 22:58:07 ERR GameInterface.Services.GameState.Patches.OnTickRobustnessPatches] Failed to run Game.OnTick\n"
        + "System.NullReferenceException: Object reference not set to an instance of an object.\n"
        + "   at TaleWorlds.MountAndBlade.DeploymentPoint.SetBreachSideDeploymentPoint()\n"
        + "   at TaleWorlds.MountAndBlade.DeploymentPoint.AfterMissionStart()\n"
        + string.Concat(Enumerable.Repeat("[(8104) 22:58:08 ERR GameInterface.Services.GameState.Patches.OnTickRobustnessPatches] Failed to run Game.OnTick\nTaleWorlds.Core.MBIllegalValueException: Number of teams is not 0.\n   at TaleWorlds.MountAndBlade.MissionCombatantsLogic.OnBehaviorInitialize_Patch1(MissionCombatantsLogic this)\n", 5));

    // #12: the server served for hours, then exit code 1 with no error.
    private const string SilentWindow = "[DedicatedServer] pulse: time=Summer 21, 1100 timeMode=StoppableFastForward players=1 parties=1517 mapEvents=7 mainParty=657.95,279.08\n[Coop] [\"ObjectManager\"] Failed to get network handle for object of type \"TaleWorlds.CampaignSystem.Clan\"\n";

    // #7: the join reached the host, then the tunnel closed with client-disposed.
    private const string MismatchClient =
        "[(20088) 20:43:05 INF Coop.Steam.SteamJoinListener] Joining Steam lobby 109775241994588021\n"
        + "[(20088) 20:43:12 INF Coop.Core.Client.CoopClient] Attempting connection to [address]:50194...\n"
        + "[(20088) 20:43:12 INF Coop.Steam.SteamTunnelClient] Steam tunnel to the host established; sendRate=8760000B/s out=0B/s in=0B/s pendingReliable=0 unacked=0 ping=0ms quality=-1.00\n"
        + "[(20088) 20:43:31 INF Coop.Steam.SteamTunnelClient] [ReceivePath] utc=2026-10-23T17:43:31.7895422Z scope=steam-client connection=576398240 localRelay=[address]:50194 virtualPort=0 reason=client-disposed counts={\"MappedReceive\":0}\n"
        + "[(20088) 20:43:31 WRN Common.GameThread] Dropping game-thread action SaveNotificationInterface: the session was cancelled before it was queued\n";

    // #13: Diplomacy without Diplomacy.CoopCompat.
    private const string DiplomacyClient = "[(14384) 04:44:41 ERR AutoSync.Hero_Gold_PropertySetPrefix] Client updated managed Hero.Gold; stack=   at System.Environment.GetStackTrace(Exception e, Boolean needFileInfo)\n"
        + "   at System.Environment.get_StackTrace()\n"
        + "   at AutoSync.Hero_Gold_PropertySetPrefix.GoldPrefix(Hero& __instance, Int32& value)\n"
        + "   at TaleWorlds.CampaignSystem.Hero.set_Gold_Patch4(Hero this, Int32 value)\n"
        + "   at Diplomacy.CampaignBehaviors.MessengerBehavior.OnMessengerSent(Hero hero) in /home/runner/work/Bannerlord.Diplomacy/Bannerlord.Diplomacy/src/Bannerlord.Diplomacy/CampaignBehaviors/MessengerBehavior.cs:line 37\n"
        + "   at TaleWorlds.CampaignSystem.MbEvent`1.InvokeList(EventHandlerRec`1 list, T t)\n"
        + "   at Diplomacy.ViewModelMixin.EncyclopediaHeroPageVMMixin.SendMessenger() in /home/runner/work/Bannerlord.Diplomacy/Bannerlord.Diplomacy/src/Bannerlord.Diplomacy/ViewModelMixin/EncyclopediaHeroPageVMMixin.cs:line 66\n"
        + "[(14384) 04:44:45 ERR GameInterface.Services.UI.Patches.ScreenManagerRobustnessPatches] Failed to run TaleWorlds.ScreenSystem.ScreenManager.Tick\n"
        + "System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation. ---> System.NullReferenceException: Object reference not set to an instance of an object.\n"
        + "   at TaleWorlds.CampaignSystem.Encounters.TownEncounter.CreateAndOpenMissionController(Location nextLocation, Location previousLocation, CharacterObject talkToChar, String playerSpecialSpawnTag)\n"
        + "   at Diplomacy.Messengers.MessengerManager.StartDialogue(Hero targetHero, Messenger messenger) in /home/runner/work/Bannerlord.Diplomacy/Bannerlord.Diplomacy/src/Bannerlord.Diplomacy/Messengers/MessengerManager.cs:line 305\n";

    [Fact]
    public void The_seeded_file_parses_and_the_built_in_copy_is_the_same()
    {
        Assert.Equal(1, Signatures.Schema);
        Assert.False(string.IsNullOrWhiteSpace(Signatures.Version));
        Assert.Equal(
            ["coop-siege-teams-loop", "server-raw-assets", "coopmodpatch-first-setup", "diplomacy-without-coopcompat", "join-closed-mod-mismatch", "client-clean-exit", "server-exit-code-1-silent", "server-exit-code-minus-1-silent", "server-steam-pipe-closed", "taom-coopcompat-startup-check", "gpu-create-texture-array", "coop-childbirth-name-null", "coop-empty-clan-banner-load", "coop-workshop-server-native-missing", "coop-old-mylittlewarband", "coop-workshop-server-template-missing", "coop-ds-mapmod-distance-cache",
                "smart-app-control-blocked", "game-bin-foreign-common-dll", "game-bin-old-harmony", "steam-join-unreachable", "onedrive-documents-coopdata", "taom-coopcompat-dll-missing", "taom-coopcompat-new-module", "taom-coopcompat-file-changed", "coop-mod-common-dll-clash", "server-client-coop-in-server-folder",
                "coop-ds-war-sails-save-mismatch", "coop-ds-war-sails-setup", "coop-ds-save-did-not-load", "coop-server-launcher-module-missing", "join-server-needs-war-sails", "join-war-sails-on-in-game",
                "rot-server-load-failed", "rot-server-module-missing", "rot-server-save-without-war-sails", "rot-server-war-sails-setup", "rot-server-save-did-not-load", "rot-server-map-crash", "rot-join-needs-war-sails"],
            Signatures.Signatures.Select(s => s.Id));
        var embedded = CrashSignatureFile.Embedded();
        Assert.Equal(Signatures.Version, embedded.Version);
        Assert.Equal(Signatures.Signatures.Select(s => s.Id), embedded.Signatures.Select(s => s.Id));

        foreach (var signature in Signatures.Signatures)
        {
            Assert.Contains(signature.Category, CrashCategory.All);
            Assert.Contains(signature.Side, new[] { "client", "server", "both" });
            // Player text: no colons in sentences and no hyphens between words.
            foreach (var text in new[] { signature.Title, signature.WhatHappened, signature.WhatToDo, signature.WhatNotToDo ?? "Optional." })
            {
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain(":", text);
                // A file name keeps its hyphen (mod-config.json).
                Assert.DoesNotMatch(@"[A-Za-z]-[A-Za-z]", text.Replace("mod-config.json", "", StringComparison.Ordinal));
            }
        }

        var siege = Signatures.Signatures.Single(s => s.Id == "coop-siege-teams-loop");
        Assert.Equal(CrashCategory.CoopBug, siege.Category);
        Assert.True(siege.ReportNeeded);
        Assert.Equal(8, siege.TrackingIssue);
        Assert.Equal("https://github.com/Bannerlord-Coop-Team/BannerlordCoop/issues/3660", siege.Upstream);
    }

    private static readonly string[] ToolLanguages = ["ar", "de", "fr", "ja", "ko", "pt", "ru", "tr", "zh-CN"];

    private static string[] PlaceholdersOf(string? text) =>
        System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\{[A-Za-z][A-Za-z0-9_]*\}").Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Every_seeded_signature_has_every_tool_language_with_the_same_placeholders()
    {
        Assert.Equal(ToolLanguages.OrderBy(l => l), BannerlordCoopLauncher.Core.Localization.Translator.Languages.Select(l => l.Code).Where(c => c != "en").OrderBy(l => l));
        foreach (var signature in Signatures.Signatures)
        {
            Assert.NotNull(signature.Translations);
            foreach (var language in ToolLanguages)
            {
                Assert.True(signature.Translations!.TryGetValue(language, out var t), signature.Id + " has no " + language);
                foreach (var (english, translated) in new[]
                {
                    (signature.Title, t!.Title), (signature.WhatHappened, t.WhatHappened), (signature.WhatToDo, t.WhatToDo), (signature.WhatNotToDo, t.WhatNotToDo)
                })
                {
                    if (english == null)
                    {
                        Assert.Null(translated);
                        continue;
                    }

                    Assert.False(string.IsNullOrWhiteSpace(translated), signature.Id + " " + language + " misses a field");
                    Assert.Equal(PlaceholdersOf(english), PlaceholdersOf(translated));
                    Assert.DoesNotContain(":", translated);
                    // A file name keeps its hyphen (mod-config.json).
                    Assert.DoesNotMatch(@"\p{L}-\p{L}", translated!.Replace("mod-config.json", "", StringComparison.Ordinal));
                    Assert.NotEqual(english, translated);
                }
            }
        }
    }

    [Fact]
    public void The_texts_the_Coop_server_shows_stay_short()
    {
        // The Server Tool's crash card: a short title, a short What happened (a path may follow on its own line) and a short What not to do.
        static int Words(string? text) => (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var server = Signatures.Signatures.Where(s => CrashSignatureMatcher.SideFits(s.Side, "server") && CrashSignatureMatcher.ToolFits(s.Tools, "coop")).ToList();
        Assert.NotEmpty(server);
        foreach (var signature in server)
        {
            Assert.True(Words(signature.Title) <= 9, signature.Id + " title");
            Assert.True(Words(signature.WhatHappened.Split('\n')[0]) <= 22, signature.Id + " what happened");
            Assert.True(Words(signature.WhatNotToDo) <= 16, signature.Id + " what not to do");
        }
    }

    [Fact]
    public void The_screen_text_follows_the_language_and_falls_back_to_English()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Server(ServerCrashExplain.AccessViolationCode, rgl: AssetsRgl))!;
        var german = match.Localized("de");
        Assert.DoesNotContain("{mod}", german.WhatHappened + german.WhatNotToDo);
        Assert.StartsWith("Der Assets Ordner von RaiseYourBanner", german.WhatHappened);
        Assert.Contains("RaiseYourBanner", german.WhatNotToDo);

        // English, an unknown language and no language are the English text.
        foreach (var language in new[] { "en", "xx", null })
            Assert.Equal(match.WhatHappened, match.Localized(language).WhatHappened);

        // A missing field or language falls back to English, with the placeholder filled.
        var file = CrashSignatureFile.Parse("""
            { "schema": 1, "version": "t", "signatures": [
              { "id": "half", "side": "server", "priority": 1, "category": "mod-assets",
                "title": "English {mod} title", "whatHappened": "English {mod} happened", "whatToDo": "English do",
                "match": { "all": [ { "file": "rgl", "regex": "Modules/(?<mod>\\w+)/Assets" } ] },
                "translations": { "fr": { "whatHappened": "Le dossier de {mod} a planté" } } }
            ] }
            """)!;
        var half = CrashSignatureMatcher.Match(file, Server(null, rgl: AssetsRgl))!;
        var french = half.Localized("fr");
        Assert.Equal("Le dossier de RaiseYourBanner a planté", french.WhatHappened);
        Assert.Equal("English RaiseYourBanner title", french.Title);
        Assert.Equal("English do", french.WhatToDo);
        Assert.Equal("English RaiseYourBanner happened", half.Localized("de").WhatHappened);

        // The report to Coodsz stays English whatever the tool language is.
        var triage = CrashReportTriage.From(match, Signatures.Version);
        Assert.Equal(match.Title, triage.Title);
        Assert.StartsWith("The Assets folder of RaiseYourBanner", triage.WhatHappened);
    }

    [Fact]
    public void A_clean_quit_with_exit_code_minus_one_is_not_a_crash()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Client(coopReport: CleanExitReport, rgl: CleanExitRgl,
            exitCode: CrashEvidence.ExitCodeFrom("client", new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.CoopReport] = [CleanExitReport] })));
        Assert.NotNull(match);
        Assert.Equal("client-clean-exit", match.Signature.Id);
        Assert.Equal(CrashCategory.NotACrash, match.Signature.Category);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Contains("Exit code -1", match.Evidence);
        Assert.Contains(match.Evidence, line => line.Contains("Managed Interface deleted"));

        // The same log that ends in the middle of a battle is not a clean quit.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1, rgl: CleanExitRgl + "[20:54:03.000] Mission tick\n")));
        // A hard crash code is not either.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1073741819, rgl: CleanExitRgl)));
    }

    [Fact]
    public void CoopModPatch_setup_stop_is_setup_not_a_crash()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Server(1, ModPatchWindow));
        Assert.Equal("coopmodpatch-first-setup", match?.Signature.Id);
        Assert.Equal(CrashCategory.Setup, match!.Signature.Category);
        Assert.Contains("Press Start server again", match.WhatToDo);
        // It wins over the silent exit 1 pattern, which matches the same run too.
        Assert.True(match.Signature.Priority > Signatures.Signatures.Single(s => s.Id == "server-exit-code-1-silent").Priority);
        // A client never gets a server pattern.
        Assert.NotEqual("coopmodpatch-first-setup", CrashSignatureMatcher.Match(Signatures, Evidence("client", 1, [], (CrashLogSource.Server, ModPatchWindow)))?.Signature.Id);
    }

    [Fact]
    public void Steam_closing_the_server_pipe_is_named_before_the_silent_exit()
    {
        // Crash report #33: the server's rgl_log_errors ended with Steam's pipe fatal assert. The exit evidence
        // (host/exit-evidence.txt) carries that tail and reads as the rgl source.
        const string evidence = "The end of the engine error log (rgl_log_errors):\n"
            + "src\\common\\pipes.cpp (879) : CClientPipe::BWriteAndReadResult: BWrite failed\n"
            + "src\\common\\pipes.cpp (879) : Fatal assert; application exiting\n";
        var match = CrashSignatureMatcher.Match(Signatures, Server(1, server: "[DedicatedServer] SERVING", rgl: evidence));
        Assert.Equal("server-steam-pipe-closed", match?.Signature.Id);
        Assert.Equal(12, match!.Signature.TrackingIssue);
        Assert.Contains("Keep Steam open", match.WhatToDo);
        Assert.True(match.Signature.Priority > Signatures.Signatures.Single(s => s.Id == "server-exit-code-1-silent").Priority);

        // Another exit code, or only one of the two lines, is not this.
        Assert.NotEqual("server-steam-pipe-closed", CrashSignatureMatcher.Match(Signatures, Server(-1, rgl: evidence))?.Signature.Id);
        Assert.NotEqual("server-steam-pipe-closed", CrashSignatureMatcher.Match(Signatures, Server(1, rgl: "Fatal assert; application exiting\n"))?.Signature.Id);
    }

    [Fact]
    public void A_core_module_missing_from_the_Coop_Workshop_server_says_to_resubscribe()
    {
        // Coop crash report #40: Start-ModdedServer.ps1 stopped on Native\SubModule.xml, and the host console wrapped the path.
        const string console = "18:19:50.466  Required core module descriptor is missing: C:\\program files \n"
            + "18:19:50.466  (x86)\\steam\\steamapps\\workshop\\content\\261550\\3770450698\\DedicatedServer\\engine\\Modules\\Native\\SubModule.xml\n";
        var match = CrashSignatureMatcher.Match(Signatures, Evidence("server", 1, [], (CrashLogSource.HostConsole, console)));
        Assert.Equal("coop-workshop-server-native-missing", match?.Signature.Id);
        Assert.Equal(CrashCategory.Setup, match!.Signature.Category);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Contains("Unsubscribe", match.WhatToDo);
        CrashEvidence Of(string tool) => new()
        {
            Side = "server",
            ExitCode = 1,
            Tool = tool,
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.HostConsole] = [console] }
        };
        Assert.Equal("coop-workshop-server-native-missing", CrashSignatureMatcher.Match(Signatures, Of("ad1259"))?.Signature.Id);
        // Burning Empires and TAOM run their own server folder, never the Workshop one.
        foreach (var tool in new[] { "be", "taom" })
            Assert.NotEqual("coop-workshop-server-native-missing", CrashSignatureMatcher.Match(Signatures, Of(tool))?.Signature.Id);

        // The host console holds everything since the tool opened: after a resubscribe and a later start that
        // crashed for another reason, the old error is far above the end and no longer counts.
        var later = console + string.Concat(Enumerable.Range(0, 100).Select(i => $"18:3{i % 10}:00.000  [DedicatedServer] line {i}\n"))
            + "18:40:00.000  The server stopped with exit code -1073741819.\n";
        Assert.NotEqual("coop-workshop-server-native-missing", CrashSignatureMatcher.Match(Signatures,
            Evidence("server", -1073741819, [], (CrashLogSource.HostConsole, later)))?.Signature.Id);

        // A server folder of its own (not the Coop Workshop item), or another start error, is not this.
        Assert.NotEqual("coop-workshop-server-native-missing", CrashSignatureMatcher.Match(Signatures,
            Evidence("server", 1, [], (CrashLogSource.HostConsole, "Required core module descriptor is missing: D:\\Server\\Modules\\Native\\SubModule.xml\n")))?.Signature.Id);
        Assert.NotEqual("coop-workshop-server-native-missing", CrashSignatureMatcher.Match(Signatures,
            Evidence("server", 1, [], (CrashLogSource.HostConsole, "Invalid Native core module version in 3770450698\\DedicatedServer\\engine\\Modules\n")))?.Signature.Id);
    }

    [Fact]
    public void An_old_My_Little_Warband_points_to_the_working_build()
    {
        // Coop crash report #46: My Little Warband v1.2.7 from 2023, built for an older game, stopped the server
        // loading the save. MrGangGang's build (Workshop 3627538517) works, and a resubscribe to the old one gives the same copy.
        const string console = "17:51:32.541  [CoopModPatch] [MyLittleWarband] patch failed: MyLittleWarband.CustomUnitsBehavior.CreateVMLayer: Method not found: 'Void TaleWorlds.Engine.GauntletUI.GauntletLayer..ctor(Int32, System.String, Boolean)'.\n"
            + "17:51:32.548  [CoopModPatch] [MyLittleWarband] patch failed: MyLittleWarband.CustomUnitsBehavior.UpdateSkill: Method not found: 'TaleWorlds.Core.CharacterSkills TaleWorlds.Core.MBCharacterSkills.get_Skills()'.\n"
            + "17:51:58.163  [DedicatedServer] HostSaveGame('The Boys') PatchAll + server container + LoadGame...\n";
        var match = CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [], (CrashLogSource.HostConsole, console)));
        Assert.Equal("coop-old-mylittlewarband", match?.Signature.Id);
        Assert.Equal(CrashCategory.ModMismatch, match!.Signature.Category);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Contains("3627538517", match.WhatToDo);
        Assert.Contains("resubscribe", match.Signature.WhatNotToDo);

        // The Server Tool's own server window has the same line without the [CoopModPatch] tag.
        Assert.Equal("coop-old-mylittlewarband", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [],
            (CrashLogSource.Server, "CoopModPatch   [MyLittleWarband] finalizer failed: MyLittleWarband.CustomUnitsBehavior.CopyCharacter: Method not found: 'TaleWorlds.Core.CharacterSkills TaleWorlds.Core.MBCharacterSkills.get_Skills()'.\n")))?.Signature.Id);

        // Only the Coop tool runs CoopModPatch.
        foreach (var tool in new[] { "be", "taom", "ad1259" })
            Assert.NotEqual("coop-old-mylittlewarband", CrashSignatureMatcher.Match(Signatures, new CrashEvidence
            {
                Side = "server",
                ExitCode = -532462766,
                Tool = tool,
                Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.HostConsole] = [console] }
            })?.Signature.Id);

        // Another My Little Warband patch that failed, or the method in another mod's line, is not this.
        Assert.NotEqual("coop-old-mylittlewarband", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [],
            (CrashLogSource.HostConsole, "[CoopModPatch] [MyLittleWarband] patch failed: MyLittleWarband.CustomUnitsBehavior.CreateVMLayer: Method not found: 'Void TaleWorlds.Engine.GauntletUI.GauntletLayer..ctor(Int32, System.String, Boolean)'.\n")))?.Signature.Id);
        Assert.NotEqual("coop-old-mylittlewarband", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [],
            (CrashLogSource.HostConsole, "[CoopModPatch] [ImprovedGarrisons] patch failed: Method not found: 'TaleWorlds.Core.CharacterSkills TaleWorlds.Core.MBCharacterSkills.get_Skills()'.\n")))?.Signature.Id);
    }

    [Fact]
    public void A_new_campaign_without_the_template_in_the_Coop_Workshop_server_says_to_resubscribe()
    {
        // Coop crash report #47: the start script copies server-data\Game Saves\default_new_game.sav into
        // Documents before every start, so the template is only missing when the Workshop copy lacks it.
        // The server window wraps the long lines.
        const string window = "Note   The fresh-campaign template is missing: C:\\Users\\[user]\\Documents\\Mount and Blade II \n"
            + "Note   Bannerlord\\CoopData\\DedicatedServer\\Game Saves\\default_new_game.sav\n"
            + "Note   In F:\\program files \n"
            + "Note   (x86)\\steam\\steamapps\\workshop\\content\\261550\\3770450698\\DedicatedServer\\Coodsz-Start-ModdedServer.ps1:2464 Zeichen:9\n";
        var match = CrashSignatureMatcher.Match(Signatures, Evidence("server", 1, [], (CrashLogSource.Server, window)));
        Assert.Equal("coop-workshop-server-template-missing", match?.Signature.Id);
        Assert.Equal(CrashCategory.Setup, match!.Signature.Category);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Contains("Unsubscribe", match.WhatToDo);

        // The Server Tool's exit text uses the same pattern, not "closed from outside".
        var lines = CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code 1.", false, window, null, [], []);
        Assert.Contains(lines, line => line.Contains("default_new_game.sav is missing from the Coop server folder", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("closed from outside", StringComparison.Ordinal));

        // Burning Empires and TAOM run their own server folder.
        foreach (var tool in new[] { "be", "taom" })
            Assert.NotEqual("coop-workshop-server-template-missing", CrashSignatureMatcher.Match(Signatures, new CrashEvidence
            {
                Side = "server",
                ExitCode = 1,
                Tool = tool,
                Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.Server] = [window] }
            })?.Signature.Id);

        // The server window keeps the lines of earlier starts: after a resubscribe, a later start that ran and
        // crashed for another reason has the old error far above its end, and that no longer counts.
        var later = window + string.Concat(Enumerable.Range(0, 100).Select(i => $"Note   [DedicatedServer] line {i}\n"))
            + "Note   [CoodszHost] Server process exited with code 1.\n";
        Assert.NotEqual("coop-workshop-server-template-missing", CrashSignatureMatcher.Match(Signatures,
            Evidence("server", 1, [], (CrashLogSource.Server, later)))?.Signature.Id);
        // The exit text reads the last window lines and then this run's own server log, which ends the same way.
        Assert.DoesNotContain(CoodszHost.Services.ServerCrashExplain.Explain("Server process exited with code 1.", false, later, null, [], []),
            line => line.Contains("default_new_game.sav is missing", StringComparison.Ordinal));

        // A server folder of its own is not the Workshop download, so resubscribing would not help.
        Assert.NotEqual("coop-workshop-server-template-missing", CrashSignatureMatcher.Match(Signatures, Evidence("server", 1, [],
            (CrashLogSource.Server, "The fresh-campaign template is missing: D:\\Server\\Game Saves\\default_new_game.sav\nIn D:\\Server\\Coodsz-Start-ModdedServer.ps1:2464\n")))?.Signature.Id);
    }

    [Fact]
    public void A_new_campaign_on_a_map_mod_that_loads_the_SandBox_distance_cache_is_a_coop_bug()
    {
        // Coop crash report #48 (CR-55WBT): Europe1100's map was read, then SandBox's own distance cache,
        // and NavigationCache.Deserialize crashed the server about 15 seconds later.
        // The server printed nothing between the cache line and the crash, so the pattern only reads the end of the run.
        const string mapMod = "[DedicatedServer] Main_map read OK, navmesh CRC=3054236814\n";
        const string cache = "[DedicatedServer] settlement distance cache: ..\\..\\Modules\\SandBox/ModuleData\\DistanceCaches\\settlements_distance_cache_Default.bin\n"
            + "[DedicatedServer Engine] Reading SettlementsDistanceCacheFilePath: ..\\..\\Modules\\SandBox/ModuleData\\DistanceCaches\\settlements_distance_cache_Default.bin\n";
        var loading = string.Concat(Enumerable.Range(0, 40).Select(i => $"[DedicatedServer] loading step @tick {i}\n")) + "[DedicatedServer] reading Main_map...\n";
        var window = loading + mapMod + cache + "[CoodszHost] Server process exited with code -532462766.\n";
        var match = CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [], (CrashLogSource.Server, window)));
        Assert.Equal("coop-ds-mapmod-distance-cache", match?.Signature.Id);
        Assert.Equal(CrashCategory.CoopBug, match!.Signature.Category);
        Assert.True(match.Signature.ReportNeeded);
        Assert.Contains("Vanilla campaign Map", match.WhatToDo);

        // The server window of the real report: its own prefixes, then the tool's old explanation after the exit line.
        var reportWindow = "DedicatedServer   reading Main_map...\r\n"
            + "DedicatedServer   Main_map read OK, navmesh CRC=3054236814\r\n"
            + "DedicatedServer   settlement distance cache: ..\\..\\Modules\\SandBox/ModuleData\\DistanceCaches\\settlements_distance_cache_Default.bin\r\n"
            + "Note   [DedicatedServer Engine] Reading SettlementsDistanceCacheFilePath: ..\\..\\Modules\\SandBox/ModuleData\\DistanceCaches\\settlements_distance_cache_Default.bin\r\n"
            + "Note   Server process exited with code -532462766.\r\n"
            + "What happened\r\nThe server stopped before it was ready for players.\r\n"
            + "What to do\r\nTurn off the extra mods you added.\r\n"
            + "What not to do\r\nNext time, do not move Bannerlord Coop.\r\n";
        Assert.Equal("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [], (CrashLogSource.Server, reportWindow)))?.Signature.Id);

        // The Server Tool's exit text uses the same pattern, with the window alone or this run's coop server log after it.
        var lines = ServerCrashExplain.Explain("Server process exited with code -532462766.", false, window, null, [], []);
        Assert.Contains(lines, line => line.Contains("travel data of the Vanilla map", StringComparison.Ordinal));
        // The [DedicatedServer] lines go to the window, not to the coop server log, which the tool reads after the window.
        // Read as one text, the log's own end hides them; the tool gives the patterns the two apart, like a crash report.
        var coopLog = string.Concat(Enumerable.Range(0, 50).Select(i => $"[Coop] Applying server time control: current=\"Pause\" requested=Pause step={i}\n"));
        var europe = new[] { "Native", "SandBox", "Coop", "Europe1100" };
        Assert.Contains(ServerCrashExplain.Explain("Server process exited with code -532462766.", false, window + "\n" + coopLog, null, [], [], 0, europe,
                new ServerCrashExplain.Apart(window, coopLog)),
            line => line.Contains("travel data of the Vanilla map", StringComparison.Ordinal));
        Assert.DoesNotContain(ServerCrashExplain.Explain("Server process exited with code -532462766.", false, window + "\n" + coopLog, null, [], [], 0, europe),
            line => line.Contains("travel data of the Vanilla map", StringComparison.Ordinal));

        // Any later crash on a map that is not the Vanilla one has the same exit code. A run that went on long past
        // the map load (More Nations Remastered has its own Main_map, so its CRC is not the Vanilla one) is not this.
        var laterCrash = loading + mapMod.Replace("3054236814", "2208118146", StringComparison.Ordinal) + cache
            + string.Concat(Enumerable.Range(0, 120).Select(i => $"[DedicatedServer] pulse: time=Spring {i}, 1084 players=1\n"))
            + "[DedicatedServer] System.NullReferenceException: Object reference not set to an instance of an object.\n"
            + "[CoodszHost] Server process exited with code -532462766.\n";
        Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [], (CrashLogSource.Server, laterCrash)))?.Signature.Id);
        Assert.DoesNotContain(ServerCrashExplain.Explain("Server process exited with code -532462766.", true, laterCrash, null, [], []),
            line => line.Contains("travel data of the Vanilla map", StringComparison.Ordinal));

        // More Nations Remastered keeps every vanilla settlement, so the vanilla cache fits it. The Server Tool now
        // gives the patterns every turned on module, not only the extras, so the map's module is seen there too.
        Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, ["MoreNationsRemastered"], (CrashLogSource.Server, window)))?.Signature.Id);
        Assert.DoesNotContain(ServerCrashExplain.Explain("Server process exited with code -532462766.", false, window, null, [], [], 0, ["Native", "SandBox", "Coop", "MoreNationsRemastered"]),
            line => line.Contains("travel data of the Vanilla map", StringComparison.Ordinal));

        // The Vanilla map's own CRC, another exit code, or a fixed Coop server that names the map's module are not this.
        var vanilla = window.Replace("CRC=3054236814", "CRC=1465536726", StringComparison.Ordinal);
        Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [], (CrashLogSource.Server, vanilla)))?.Signature.Id);
        Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", 1, [], (CrashLogSource.Server, window)))?.Signature.Id);
        Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [],
            (CrashLogSource.Server, "[DedicatedServer] campaign map from Europe1100\n" + window)))?.Signature.Id);

        // The AD1259 and Burning Empires compat modules send the cache to the map's own, so their servers print the same lines.
        foreach (var compat in new[] { "DedicatedServer.AD1259CoopCompat", "BurningEmpires.CoopCompat" })
        {
            Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, Evidence("server", -532462766, [compat], (CrashLogSource.Server, window)))?.Signature.Id);
            Assert.DoesNotContain(ServerCrashExplain.Explain("Server process exited with code -532462766.", true, window, null, [new ServerCrashExplain.ModName(compat, compat)], []),
                line => line.Contains("travel data of the Vanilla map", StringComparison.Ordinal));
        }

        // AD1259, Burning Empires and TAOM run their own setups.
        foreach (var tool in new[] { "ad1259", "be", "taom" })
            Assert.NotEqual("coop-ds-mapmod-distance-cache", CrashSignatureMatcher.Match(Signatures, new CrashEvidence
            {
                Side = "server",
                ExitCode = -532462766,
                Tool = tool,
                Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.Server] = [window] }
            })?.Signature.Id);
    }

    [Fact]
    public void A_new_campaign_on_a_map_mod_asks_once_on_a_Start_click()
    {
        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var key = MapModWarning.Key("Friends", "Europe1100");
        string[] mods = ["Native", "SandBox", "Coop", "Europe1100"];
        Assert.True(MapModWarning.ShouldAsk(false, true, true, "Europe1100", mods, asked, key));

        // Asked once for that setup and map, then never again in this run.
        asked.Add(key);
        Assert.False(MapModWarning.ShouldAsk(false, true, true, "Europe1100", mods, asked, key));
        Assert.True(MapModWarning.ShouldAsk(false, true, true, "Europe1100", mods, asked, MapModWarning.Key("Other setup", "Europe1100")));

        var fresh = new HashSet<string>();
        // The Vanilla map, a save that already exists, an automatic restart and the AD1259 build never ask.
        Assert.False(MapModWarning.ShouldAsk(false, true, true, "Vanilla", ["Native", "SandBox", "Coop"], fresh, "x"));
        // More Nations Remastered keeps every vanilla settlement, so the vanilla cache loads: the recommended setup never asks.
        Assert.False(MapModWarning.ShouldAsk(false, true, true, "MoreNationsRemastered", ["Native", "SandBox", "Coop", "MoreNationsRemastered", "MoreNationsRemasteredFix"], fresh, "y"));
        // A map id with spaces round it (an edited host.json) is the same map, as RefusesAdding already reads it.
        Assert.False(MapModWarning.ShouldAsk(false, true, true, " MoreNationsRemastered ", ["Native", "SandBox", "Coop", "MoreNationsRemastered"], fresh, "z"));
        Assert.False(MapModWarning.ShouldAsk(false, true, true, "Vanilla ", ["Native", "SandBox", "Coop"], fresh, "v"));
        Assert.False(MapModWarning.ShouldAsk(false, true, false, "Europe1100", mods, fresh, key));
        Assert.False(MapModWarning.ShouldAsk(false, false, true, "Europe1100", mods, fresh, key));
        Assert.False(MapModWarning.ShouldAsk(true, true, true, "AD1259", mods, fresh, key));
        // A compat module that fixes the distance cache is on.
        Assert.False(MapModWarning.ShouldAsk(false, true, true, "AD1259", [.. mods, "DedicatedServer.AD1259CoopCompat"], fresh, key));
        Assert.False(MapModWarning.ShouldAsk(false, true, true, "BurningEmpires", [.. mods, "BurningEmpires.CoopCompat"], fresh, key));
    }

    [Fact]
    public void An_access_violation_after_loading_a_mods_Assets_names_the_mod()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Server(ServerCrashExplain.AccessViolationCode, rgl: AssetsRgl));
        Assert.Equal("server-raw-assets", match?.Signature.Id);
        Assert.Contains("The Assets folder of RaiseYourBanner crashed the server", match!.WhatHappened);
        Assert.DoesNotContain("{mod}", match.WhatHappened + match.WhatToDo + match.Title);
        Assert.Contains(match.Evidence, line => line.Contains("RaiseYourBanner/Assets"));

        // The official DsAssetPackages, a log that went on, or another exit code are not this.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(ServerCrashExplain.AccessViolationCode, rgl: "[20:14:48.284] Loading packages $BASE/Modules/DedicatedServer.Windows/DsAssetPackages...\n[20:14:48.294] Registering items...\n")));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(ServerCrashExplain.AccessViolationCode, rgl: AssetsRgl + "[20:14:50.000] Loading Save Game\n")));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(0, rgl: AssetsRgl)));
    }

    [Fact]
    public void Diplomacy_without_its_coop_compat_is_a_missing_patch()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Client(-1, DiplomacyClient, modules: ["Coop", "Bannerlord.Diplomacy", "ImprovedGarrisons"]));
        Assert.Equal("diplomacy-without-coopcompat", match?.Signature.Id);
        Assert.Equal(CrashCategory.ModMissingPatch, match!.Signature.Category);
        Assert.Contains("3824313421", match.WhatToDo);
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1, DiplomacyClient, modules: ["Coop", "Bannerlord.Diplomacy", "Diplomacy.CoopCompat"])));

        // Diplomacy on without its coop compat is not enough: Diplomacy's code must be in the crash.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1, modules: ["Coop", "Bannerlord.Diplomacy"])));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1,
            "System.NullReferenceException: Object reference not set to an instance of an object.\n   at TaleWorlds.CampaignSystem.Encounters.TownEncounter.CreateAndOpenMissionController(Location nextLocation)\n",
            modules: ["Coop", "Bannerlord.Diplomacy"])));
        // A Diplomacy line with no host only change and no messenger is not this pattern either.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1,
            "System.NullReferenceException: Object reference not set to an instance of an object.\n   at Diplomacy.ViewModelMixin.KingdomTruceItemVmMixin.Refresh()\n",
            modules: ["Coop", "Bannerlord.Diplomacy"])));
    }

    [Fact]
    public void The_siege_teams_loop_is_a_known_coop_bug_that_still_wants_reports()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Client(-1, SiegeLoop, rgl: CleanExitRgl, modules: ["Bannerlord.Diplomacy"]));
        Assert.Equal("coop-siege-teams-loop", match?.Signature.Id);
        Assert.True(match!.Signature.ReportNeeded);
        Assert.Equal(8, match.Signature.TrackingIssue);
        Assert.Contains(match.Evidence, line => line.Contains("Number of teams is not 0"));
        Assert.Contains(match.Evidence, line => line.Contains("SetBreachSideDeploymentPoint"));
        Assert.True(match.Evidence.Count <= CrashSignatureMatcher.MaxEvidence);

        // One stray line of the loop alone is not the loop.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(null, "TaleWorlds.Core.MBIllegalValueException: Number of teams is not 0.\n")));
    }

    [Fact]
    public void Exit_code_1_with_no_error_is_a_silent_exit()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Server(1, SilentWindow, "[03:12:00.000] Saving...\n"));
        Assert.Equal("server-exit-code-1-silent", match?.Signature.Id);
        Assert.Equal(CrashCategory.SilentExit, match!.Signature.Category);
        Assert.Contains("antivirus", match.WhatToDo);

        // An exception in the last lines is not silent.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(1, SilentWindow + "System.NullReferenceException: Object reference not set to an instance of an object.\n")));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(1, SilentWindow, serverLog: "Unhandled exception in the main loop\n")));
        // A noise line with Exception in it does not count.
        Assert.Equal("server-exit-code-1-silent", CrashSignatureMatcher.Match(Signatures, Server(1, SilentWindow + "Serilog SelfLog: System.IO.IOException: The file is busy\n"))?.Signature.Id);
    }

    [Fact]
    public void Exit_code_minus_1_with_no_error_is_a_silent_exit_too()
    {
        // Stop-Process on the engine dotnet ends the server with exit code -1 (taskkill /F gives 1).
        var match = CrashSignatureMatcher.Match(Signatures, Server(-1, SilentWindow, "[03:12:00.000] Saving...\n"));
        Assert.Equal("server-exit-code-minus-1-silent", match?.Signature.Id);
        Assert.Equal(CrashCategory.SilentExit, match!.Signature.Category);
        Assert.True(match.Signature.ReportNeeded);
        Assert.Equal(12, match.Signature.TrackingIssue);
        Assert.Contains("exit code -1", match.WhatHappened);
        Assert.Contains("antivirus", match.WhatToDo);
        Assert.Contains("Exit Code -1", match.Localized("de").WhatHappened);

        // An error in the last lines is not silent.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-1, SilentWindow + "System.NullReferenceException: Object reference not set to an instance of an object.\n")));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-1, SilentWindow, serverLog: "Unhandled exception in the main loop\n")));
        // A real crash code is never a silent exit, and neither is a clean stop.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(ServerCrashExplain.AccessViolationCode, SilentWindow)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-1073740791, SilentWindow)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(0, SilentWindow)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(null, SilentWindow)));
        // On the client a -1 is still the clean exit, or nothing: the server signature never matches there.
        Assert.Equal("client-clean-exit", CrashSignatureMatcher.Match(Signatures, Client(coopReport: CleanExitReport, rgl: CleanExitRgl, exitCode: -1))?.Signature.Id);
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1, rgl: CleanExitRgl + "[20:54:03.000] Mission tick\n")));
    }

    [Fact]
    public void A_join_that_closed_right_after_connecting_is_a_mod_mismatch()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Client(-1, MismatchClient, rgl: CleanExitRgl));
        Assert.Equal("join-closed-mod-mismatch", match?.Signature.Id);
        Assert.Contains("Get all of this server's mods", match!.WhatToDo);

        // A session that reached the campaign map was a real join.
        Assert.Equal("client-clean-exit", CrashSignatureMatcher.Match(Signatures,
            Client(-1, MismatchClient + "[(20088) 20:44:00 INF GameInterface.Services.GameDebug.Patches.CharacterCreationIntroPatch] Game State is changing to MapState\n", rgl: CleanExitRgl))?.Signature.Id);
    }

    [Fact]
    public void An_unknown_crash_matches_nothing()
    {
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1073741819, "[(1) 10:00:00 ERR Some.Mod] System.InvalidOperationException: Sequence contains no elements\n",
            rgl: "[10:00:00.000] Mission tick\n", modules: ["Coop"])));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(3, "Coop fatal error while serving\n")));
        Assert.Null(CrashSignatureMatcher.Match(CrashSignatureFile.Empty, Client(-1, rgl: CleanExitRgl)));
    }

    [Fact]
    public void A_broken_pattern_in_a_downloaded_file_never_matches_and_never_throws()
    {
        var file = CrashSignatureFile.Parse("""
            { "schema": 1, "version": "x", "signatures": [
              { "id": "broken", "side": "both", "priority": 1, "category": "setup", "title": "t", "whatHappened": "w", "whatToDo": "d",
                "match": { "all": [ { "file": "rgl", "regex": "([unclosed" } ] } },
              { "id": "no-file", "side": "both", "priority": 2, "category": "setup", "title": "t", "whatHappened": "w", "whatToDo": "d",
                "match": { "all": [ { "contains": "anything" } ] } }
            ] }
            """)!;
        Assert.Null(CrashSignatureMatcher.Match(file, Client(-1, "anything", rgl: "([unclosed")));
        Assert.Null(CrashSignatureFile.Parse("{ \"schema\": 2, \"signatures\": [] }"));
        Assert.Null(CrashSignatureFile.Parse("not json"));
    }

    [Fact]
    public void The_screen_mode_follows_the_match()
    {
        var known = CrashSignatureMatcher.Match(Signatures, Client(-1, rgl: CleanExitRgl, coopReport: CleanExitReport))!;
        var coopBug = CrashSignatureMatcher.Match(Signatures, Client(-1, SiegeLoop))!;
        var silent = CrashSignatureMatcher.Match(Signatures, Server(1, SilentWindow))!;

        Assert.Equal(CrashScreenMode.KnownNoReport, CrashScreen.ModeFor("coop", CrashReason.Crash, true, known));
        Assert.Equal(CrashScreenMode.KnownSendHelps, CrashScreen.ModeFor("coop", CrashReason.Crash, true, coopBug));
        Assert.Equal(CrashScreenMode.KnownSendHelps, CrashScreen.ModeFor("coop", CrashReason.ServerCrash, true, silent));
        Assert.Equal(CrashScreenMode.Unknown, CrashScreen.ModeFor("ad1259", CrashReason.ServerCrash, true, null));
        // The TAOM tools get the same screens. Send my logs or a triage that did not run keep the old screen.
        Assert.Equal(CrashScreenMode.KnownNoReport, CrashScreen.ModeFor("taom", CrashReason.Crash, true, known));
        Assert.Equal(CrashScreenMode.Unknown, CrashScreen.ModeFor("taom", CrashReason.ServerCrash, true, null));
        Assert.Equal(CrashScreenMode.None, CrashScreen.ModeFor("taom", CrashReason.Logs, true, known));
        Assert.Equal(CrashScreenMode.None, CrashScreen.ModeFor("coop", CrashReason.Logs, true, known));
        Assert.Equal(CrashScreenMode.None, CrashScreen.ModeFor("coop", CrashReason.Crash, false, null));

        Assert.Equal(CrashScreen.NoNeedTitle, CrashScreen.Headline(CrashScreenMode.KnownNoReport, known));
        Assert.Equal(CrashScreen.KnownCoopBugTitle, CrashScreen.Headline(CrashScreenMode.KnownSendHelps, coopBug));
        Assert.Equal(CrashScreen.KnownBugTitle, CrashScreen.Headline(CrashScreenMode.KnownSendHelps, silent));
        Assert.Equal(CrashScreen.UnknownTitle, CrashScreen.Headline(CrashScreenMode.Unknown, null));
        Assert.False(CrashScreen.BigSend(CrashScreenMode.KnownNoReport));
        Assert.True(CrashScreen.BigSend(CrashScreenMode.KnownSendHelps));
        Assert.True(CrashScreen.BigSend(CrashScreenMode.Unknown));
        Assert.True(CrashScreen.BigSend(CrashScreenMode.None));

        foreach (var text in new[] { CrashScreen.NoNeedTitle, CrashScreen.KnownCoopBugTitle, CrashScreen.KnownBugTitle, CrashScreen.UnknownTitle, CrashScreen.SendAnywayText })
            Assert.DoesNotContain(":", text);
    }

    [Fact]
    public void The_window_shows_the_box_at_the_top_and_fades_Send_for_known_causes()
    {
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.Contains("CrashScreen.ModeFor(_request.Tool, _request.Reason, pack.TriageRan, pack.Match)", window);
        Assert.True(window.IndexOf("TriageBox(mode, pack.Match)", StringComparison.Ordinal) < window.IndexOf("ExtrasSection()", window.IndexOf("private void ShowConsent()", StringComparison.Ordinal), StringComparison.Ordinal));
        Assert.Contains("CrashScreen.BigSend(mode)", window);
        Assert.Contains("CrashScreen.SendAnywayText", window);
        Assert.Contains("CrashSignatureFile.FetchForScreenAsync", window);
        // Coodsz Host passes the exit code it saw to the report.
        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.CrashReports.cs"));
        Assert.Contains("exit.ExitCode", host);
        Assert.Contains("ExitCode = context.ExitCode", host);
    }

    [Fact]
    public void The_evidence_comes_from_the_packed_zip_newest_first()
    {
        var zipPath = Path.Combine(_directory, "client.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            Put(zip, "game/Coop_client.log", MismatchClient);
            Put(zip, "crash-reports/2026-10-21_20-54-18_client_26348_cf410f10/report.txt", CleanExitReport);
            Put(zip, "crash-reports/2026-10-21_20-54-18_client_26348_cf410f10/logs/Coop_client.log", "older copy");
            Put(zip, "crash-reports/2026-10-21_20-54-18_client_26348_cf410f10/logs/rgl_log_26348.txt", CleanExitRgl);
            Put(zip, "crash-reports/2026-10-21_20-54-18_client_26348_cf410f10/logs/rgl_log_errors_26348.txt", "errors");
            Put(zip, "engine-logs/rgl_log_16768.txt", "[10:00:00.000] Mission tick\n");
            Put(zip, "launcher/client-action.log", "Connect");
        }

        var evidence = CrashEvidence.FromZip(zipPath, "client", modules: ["Coop"]);
        Assert.Equal(-1, evidence.ExitCode);
        Assert.Equal([MismatchClient, "older copy"], evidence.TextsOf(CrashLogSource.CoopClient));
        Assert.Equal([CleanExitRgl, "[10:00:00.000] Mission tick\n"], evidence.TextsOf(CrashLogSource.Rgl));
        Assert.Equal("join-closed-mod-mismatch", CrashSignatureMatcher.Match(Signatures, evidence)?.Signature.Id);
        // The exit code the tool knows wins.
        Assert.Equal(5, CrashEvidence.FromZip(zipPath, "client", exitCode: 5).ExitCode);

        var serverZip = Path.Combine(_directory, "server.zip");
        using (var zip = ZipFile.Open(serverZip, ZipArchiveMode.Create))
        {
            Put(zip, "engine-logs/rgl_log_99.txt", "[1] Starting /singleplayer _MODULES_*Native*Coop*_MODULES_\n");
            Put(zip, "engine-logs/rgl_log_2776.txt", AssetsRgl);
            Put(zip, "host/server-window.txt", "Server starting");
            Put(zip, "host/console-window.txt", "21:25:39.351  [CoodszHost] Server process exited with code 1.\n20:14:39.051  [CoodszHost] Server process exited with code -1073741819.\n");
        }

        var server = CrashEvidence.FromZip(serverZip, "server");
        Assert.Equal(ServerCrashExplain.AccessViolationCode, server.ExitCode);
        Assert.Equal([AssetsRgl], server.TextsOf(CrashLogSource.Rgl));
        Assert.Equal("server-raw-assets", CrashSignatureMatcher.Match(Signatures, server)?.Signature.Id);
    }

    [Fact]
    public void The_report_carries_the_match_in_the_summary_and_report_json()
    {
        var request = new CrashReportRequest
        {
            Tool = "coop",
            Side = "client",
            Reason = CrashReason.Crash,
            ToolVersion = "1.2.64",
            Modules = [("Coop", "v0.1.6")],
            Collect = (zip, limits) =>
            {
                var builder = new LogZipBuilder(limits);
                builder.AddText("crash-reports/2026-10-23_20-43-48_client_20088_253b67a7/report.txt", CleanExitReport);
                builder.AddText("game/Coop_client.log", SiegeLoop);
                return builder.Write(zip, "notes").Skipped;
            }
        };
        var pack = CrashReports.Prepare(request, "0123456789abcdef0123456789abcdef", Path.Combine(_directory, "packs"), Signatures);
        Assert.True(pack.TriageRan);
        Assert.Equal("coop-siege-teams-loop", pack.Match?.Signature.Id);
        var triage = pack.Summary.Triage!;
        Assert.Equal("coop-siege-teams-loop", triage.Signature);
        Assert.Equal(CrashCategory.CoopBug, triage.Category);
        Assert.Equal(Signatures.Version, triage.SignaturesVersion);
        Assert.Equal(8, triage.TrackingIssue);
        Assert.True(triage.ReportNeeded);
        Assert.InRange(triage.Evidence.Count, 1, 10);

        var json = CrashReports.SummaryJson(pack.Summary);
        Assert.Contains("\"triage\"", json);
        Assert.Contains("\"signature\": \"coop-siege-teams-loop\"", json);
        using (var zip = ZipFile.OpenRead(pack.ZipPath))
        using (var reader = new StreamReader(zip.GetEntry(CrashReports.ManifestName)!.Open()))
        {
            using var manifest = JsonDocument.Parse(reader.ReadToEnd());
            var t = manifest.RootElement.GetProperty("triage");
            Assert.Equal("coop-siege-teams-loop", t.GetProperty("signature").GetString());
            Assert.Equal(CrashCategory.CoopBug, t.GetProperty("category").GetString());
            Assert.Equal(Signatures.Version, t.GetProperty("signaturesVersion").GetString());
        }

        // The player's text and screenshots keep the triage.
        var full = CrashReports.WithExtras(request, pack, "It hung on the loading screen", null);
        Assert.Equal("coop-siege-teams-loop", full.Summary.Triage?.Signature);
        Assert.Equal("coop-siege-teams-loop", full.Match?.Signature.Id);
        File.Delete(full.ZipPath);

        // An unknown crash still says which signatures version was checked. Send my logs runs only its allow list,
        // so the siege loop is not named there, but the report still says the check ran. A TAOM crash report runs it all.
        var unknown = CrashReportTriage.From(null, Signatures.Version);
        Assert.Null(unknown.Signature);
        Assert.Equal(Signatures.Version, unknown.SignaturesVersion);
        var logs = CrashReports.Prepare(new CrashReportRequest { Tool = "coop", Reason = CrashReason.Logs, Collect = request.Collect }, "", Path.Combine(_directory, "packs"), Signatures);
        Assert.True(logs.TriageRan);
        Assert.Null(logs.Match);
        Assert.Null(logs.Summary.Triage!.Signature);
        Assert.Equal(Signatures.Version, logs.Summary.Triage.SignaturesVersion);
        Assert.Equal(CrashScreenMode.None, CrashScreen.ModeFor("coop", CrashReason.Logs, logs.TriageRan, logs.Match));
        File.Delete(logs.ZipPath);
        // A tool without triage still sends none.
        var other = CrashReports.Prepare(new CrashReportRequest { Tool = "other", Reason = CrashReason.Logs, Collect = request.Collect }, "", Path.Combine(_directory, "packs"), Signatures);
        Assert.False(other.TriageRan);
        Assert.Null(other.Summary.Triage);
        Assert.DoesNotContain("\"triage\"", CrashReports.SummaryJson(other.Summary));
        File.Delete(other.ZipPath);
        var taom = CrashReports.Prepare(new CrashReportRequest { Tool = "taom", Reason = CrashReason.Crash, Collect = request.Collect }, "", Path.Combine(_directory, "packs"), Signatures);
        Assert.True(taom.TriageRan);
        File.Delete(taom.ZipPath);
    }

    [Fact]
    public void Coodsz_Host_explains_known_server_stops_from_the_same_signatures()
    {
        var setup = ServerCrashExplain.Explain("Server process exited with code 1.", false, ModPatchWindow, null, [], ["Coop"]);
        Assert.Contains(setup, line => line.StartsWith("What to do:") && line.Contains("Setup finished. Press Start server again."));

        var assets = ServerCrashExplain.Explain("Server process exited with code -1073741819.", false, "", AssetsRgl, [], ["Coop"]);
        Assert.Contains(assets, line => line.StartsWith("What happened:") && line.Contains("The Assets folder of RaiseYourBanner crashed the server"));
        Assert.Contains(assets, line => line.StartsWith("What not to do:") && line.Contains("turn RaiseYourBanner off"));

        // A failed load with exit code 1 is still a failed load, not a silent exit.
        var failed = ServerCrashExplain.Explain("Server process exited with code 1.", false, "CrashExample could not be loaded correctly",
            null, [new ServerCrashExplain.ModName("CrashExample", "Crash Example")], ["Coop"]);
        Assert.Contains(failed, line => line.Contains("Crash Example did not load"));

        // Code -1 keeps Coodsz Host's own lines; the -1 silent exit signature is for the crash screen.
        var killed = ServerCrashExplain.Explain("Server process exited with code -1.", true, SilentWindow, null, [], ["Coop"]);
        Assert.Contains(killed, line => line.StartsWith("What happened:") && line.Contains("stopped with exit code -1. That code usually means the process was ended from outside"));

    }

    // Coop crash report #53 (CR-7YGJU): Smart App Control blocked the server's startup hook; turning it off fixed it.
    private const string SmartAppControlWindow =
        "Unhandled exception. System.ArgumentException: Startup hook assembly 'C:\\SteamLibrary\\steamapps\\workshop\\content\\261550\\3770450698\\DedicatedServer\\BannerlordCoop.RuntimeBootstrap.dll' failed to load. See inner exception for details.\n"
        + " ---> System.IO.FileLoadException: Could not load file or assembly 'BannerlordCoop.RuntimeBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. An Application Control policy has blocked this file. (0x800711C7)\n";

    private const string GameBin = "\n...\\Mount & Blade II Bannerlord\\bin\\Win64_Shipping_Client\\";

    [Fact]
    public void Smart_App_Control_blocking_a_file_says_to_turn_it_off()
    {
        var match = CrashSignatureMatcher.Match(Signatures, Server(-532462766, SmartAppControlWindow));
        Assert.Equal("smart-app-control-blocked", match?.Signature.Id);
        Assert.Equal(CrashCategory.Setup, match!.Signature.Category);
        Assert.False(match.Signature.ReportNeeded);
        Assert.Contains("\nApp & browser control > Smart App Control settings\n", match.WhatToDo);
        Assert.EndsWith("Turning it back on later may need a reset of Windows on some versions, so check that first if it matters to you.", match.WhatToDo);
        Assert.DoesNotContain("can't turn", match.WhatToDo);
        Assert.Contains(match.Evidence, line => line.Contains("0x800711C7"));

        // A game that Smart App Control stops gets the same answer, from its engine log.
        Assert.Equal("smart-app-control-blocked", CrashSignatureMatcher.Match(Signatures, Client(-532462766,
            rgl: "[18:02:11.104] Could not load file or assembly 'file:///C:/SteamLibrary/steamapps/workshop/content/261550/3770450698/bin/Win64_Shipping_Client/Coop.Core.dll' or one of its dependencies. An Application Control policy has blocked this file. (Exception from HRESULT: 0x800711C7)\n"))?.Signature.Id);

        // Coodsz Host explains the stop in its console the same way.
        var lines = ServerCrashExplain.Explain("Server process exited with code -532462766.", false, SmartAppControlWindow, null, [], ["Coop"]);
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Turn off Smart App Control"));

        // The startup hook failing for another reason is not this.
        Assert.NotEqual("smart-app-control-blocked", CrashSignatureMatcher.Match(Signatures, Server(-532462766,
            "Unhandled exception. System.ArgumentException: Startup hook assembly 'C:\\Server\\BannerlordCoop.RuntimeBootstrap.dll' failed to load. See inner exception for details.\n ---> System.IO.FileNotFoundException: Could not find file.\n"))?.Signature.Id);
        // The code alone in an unrelated line is not this either.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-532462766, "[CoodszHost] Windows Update reported 0x800711C7 for a driver package\n")));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-532462766, rgl: "[18:02:11.104] HRESULT 0x800711C7 seen while reading a config\n")));
    }

    [Fact]
    public void A_foreign_Common_dll_in_the_game_bin_says_to_move_it_out()
    {
        const string log = "[(14384) 18:02:11 ERR Coop.CoopMod] System.TypeLoadException: Could not load type 'Common.Network.Session.ServerVisibility' from assembly 'Common, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null'.\n";
        var match = CrashSignatureMatcher.Match(Signatures, Client(-532462766, log));
        Assert.Equal("game-bin-foreign-common-dll", match?.Signature.Id);
        Assert.False(match!.Signature.ReportNeeded);
        Assert.StartsWith("Move the extra Common.dll out of the game's bin folder.\n\nClose the game and move this file out to your Desktop.\n", match.WhatToDo);
        // The path on its own line, the version the log names in brackets after it.
        Assert.Contains(GameBin + "Common.dll (version 3.0.0.0)\n\n", match.WhatToDo);
        // The cause is the likely one, and a folder without the file sends the player to Send my logs.
        Assert.Equal("A different Common.dll is loaded", match.Title);
        Assert.StartsWith("The game loaded a Common.dll that isn't the one Coop comes with", match.WhatHappened);
        Assert.EndsWith("If that folder has no Common.dll, send your logs.", match.WhatToDo);
        Assert.Equal("game-bin-foreign-common-dll", CrashSignatureMatcher.Match(Signatures, Client(null, rgl: log))?.Signature.Id);

        // Coop's own Common.dll is 1.0.0.0: a type missing from it is something else.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-532462766, log.Replace("Version=3.0.0.0", "Version=1.0.0.0"))));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-532462766,
            "[(14384) 18:02:11 INF Coop.CoopMod] Loaded Common, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null from Modules\\Coop\\bin\\Win64_Shipping_Client\\Common.dll\n")));
        // The dedicated server never loads the game's bin folder.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-532462766, log)));
    }

    [Fact]
    public void An_old_0Harmony_dll_in_the_game_bin_says_to_move_it_out()
    {
        const string log = "[(14384) 18:02:11 ERR Coop.CoopMod] System.MissingMethodException: Method not found: 'Void HarmonyLib.Harmony.PatchCategory(System.String)'.\n";
        var match = CrashSignatureMatcher.Match(Signatures, Client(-532462766, log));
        Assert.Equal("game-bin-old-harmony", match?.Signature.Id);
        Assert.False(match!.Signature.ReportNeeded);
        Assert.StartsWith("Move the old 0Harmony.dll out of the game's bin folder.\n\nClose the game and move this file out to your Desktop.\n", match.WhatToDo);
        Assert.Contains(GameBin + "0Harmony.dll\n\n", match.WhatToDo);
        // The crash screen has no big Send for a known cause, so the player is pointed at Send my logs.
        Assert.EndsWith("If that folder has no 0Harmony.dll, send your logs.", match.WhatToDo);
        Assert.DoesNotContain("this report", match.WhatToDo);

        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-532462766, log.Replace("PatchCategory", "PatchAll"))));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-532462766,
            "[(14384) 18:02:11 INF Coop.CoopMod] HarmonyLib.Harmony.PatchCategory is available, Harmony 2.3.3 loaded\n")));
    }

    [Fact]
    public void A_Steam_join_that_never_reaches_the_host_says_to_close_WARP()
    {
        // Coop.Core's own text (CR-CU9T7 showed it on screen), and the Steam networking timeout.
        const string unreachable = "[(20088) 20:43:31 WRN Coop.Steam.SteamJoinListener] Could not reach the co-op host through Steam. Make sure the host is still in their session, then try the invite again.\n";
        var match = CrashSignatureMatcher.Match(Signatures, Client(-1, unreachable));
        Assert.Equal("steam-join-unreachable", match?.Signature.Id);
        Assert.False(match!.Signature.ReportNeeded);
        Assert.Contains("Cloudflare WARP", match.WhatToDo);
        // Coop's own advice comes first.
        Assert.StartsWith("Check the host is still in their session and ask for a new invite.\n\n", match.WhatToDo);
        Assert.Equal(["join"], match.Signature.Labels);
        // The launcher's logs count only for the newest one: an old failure is not a later Send my logs.
        Assert.All(match.Signature.Match.Any!, rule => Assert.True(rule.NewestOnly, rule.File));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, new CrashEvidence
        {
            Side = "client",
            Texts = new Dictionary<string, IReadOnlyList<string>> { [CrashLogSource.Launcher] = ["Joined fine today\n", "Could not reach the co-op host through Steam.\n"] }
        }));
        Assert.Equal("steam-join-unreachable", CrashSignatureMatcher.Match(Signatures,
            Client(-1, "[(20088) 20:43:31 WRN Coop.Steam.SteamTunnelClient] Steam connection closed, end reason 4001 Timed out attempting to negotiate rendezvous\n"))?.Signature.Id);

        // A direct join that failed is a different problem.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1, "[(20088) 20:43:31 WRN Coop.Core.Client.CoopClient] Could not reach the co-op host at [address]:7777.\n")));
    }

    [Fact]
    public void A_Steam_join_that_failed_once_then_reached_the_host_is_not_the_Steam_cause()
    {
        // The first try failed, a retry reached the host and closed: the mods don't match, not WARP (Grok on b935a7f0).
        const string log = "[(20088) 20:43:31 WRN Coop.Steam.SteamJoinListener] Could not reach the co-op host through Steam. Make sure the host is still in their session, then try the invite again.\n"
            + "[(20088) 20:44:02 INF Coop.Steam.SteamTunnelClient] Steam tunnel to the host established\n"
            + "[(20088) 20:44:09 INF Coop.Core.Client.CoopClient] Disconnected, reason=client-disposed\n";
        Assert.Equal("join-closed-mod-mismatch", CrashSignatureMatcher.Match(Signatures, Client(-1, log))?.Signature.Id);
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Client(-1, log.Replace("reason=client-disposed", "reason=host-left"))));

        // The texture crash outranks it too, since its fix is the one that helps.
        var gpu = CrashSignatureMatcher.Match(Signatures, Client(5,
            "[(20088) 20:43:31 WRN Coop.Steam.SteamJoinListener] Could not reach the co-op host through Steam.\n",
            rgl: "[20:50:11.000] create_texture_array failed\n"));
        Assert.Equal("gpu-create-texture-array", gpu?.Signature.Id);
    }

    [Fact]
    public void Documents_in_OneDrive_with_a_save_error_says_to_stop_OneDrive_there()
    {
        // The save or file error names the CoopData path inside OneDrive on the same line.
        const string path = "C:\\Users\\[user]\\OneDrive\\Documents\\Mount and Blade II Bannerlord\\CoopData\\DedicatedServer\\Game Saves\\history";
        var failed = "[Coop] Save-History Cleanup Failed for " + path + "\n";
        var match = CrashSignatureMatcher.Match(Signatures, Server(-532462766, failed));
        Assert.Equal("onedrive-documents-coopdata", match?.Signature.Id);
        Assert.False(match!.Signature.ReportNeeded);
        Assert.Contains("\nSync and backup > Manage backup\n", match.WhatToDo);
        // The .NET file errors, with the path first or last, and a work OneDrive with its own folder name.
        Assert.Equal("onedrive-documents-coopdata", CrashSignatureMatcher.Match(Signatures, Server(1,
            "System.UnauthorizedAccessException: Access to the path '" + path.Replace("\\OneDrive\\", "\\OneDrive - Contoso\\") + "' is denied.\n"))?.Signature.Id);
        Assert.Equal("onedrive-documents-coopdata", CrashSignatureMatcher.Match(Signatures, Client(-1,
            "System.IO.IOException: The process cannot access the file '" + path + "' because it is being used by another process.\n"))?.Signature.Id);

        // Every OneDrive player has the path in normal log lines: a OneDrive path plus an unrelated save error
        // on another line, or in another log, is not this.
        var onedrive = "[DedicatedServer] Save folder " + path + "\n";
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-532462766, onedrive + "[Coop] Unable to save the campaign\n")));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-532462766, "[Coop] Save-History Cleanup Failed\n", serverLog: onedrive)));
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-532462766, onedrive + "[MCM] Unable to save settings to C:\\Users\\[user]\\OneDrive\\Documents\\Mount and Blade II Bannerlord\\Configs\\ModSettings\n")));
        // A Documents folder outside OneDrive is not this either.
        Assert.Null(CrashSignatureMatcher.Match(Signatures, Server(-532462766, failed.Replace("\\OneDrive", ""))));
    }

    [Fact]
    public void A_rule_with_several_files_holds_when_one_of_them_matches()
    {
        var file = CrashSignatureFile.Parse("""
            { "schema": 1, "version": "x", "signatures": [
              { "id": "two-logs", "side": "both", "priority": 1, "category": "setup", "title": "t", "whatHappened": "w", "whatToDo": "d",
                "match": { "all": [ { "files": ["coopClient", "rgl"], "regex": "needle (?<what>\\w+)" } ] } }
            ] }
            """)!;
        var match = CrashSignatureMatcher.Match(file, Client(-1, "nothing here\n", rgl: "a needle found\n"));
        Assert.Equal("two-logs", match?.Signature.Id);
        Assert.Equal("found", match!.Groups["what"]);
        Assert.Equal(["a needle found"], match.Evidence);
        Assert.Null(CrashSignatureMatcher.Match(file, Client(-1, "nothing here\n", rgl: "nor here\n")));
        // A tool from before the files field reads the rule without its files: it has no file and never matches.
        var old = CrashSignatureFile.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "compat-list", "crash-signatures.json")).Replace("\"files\"", "\"filesUnknown\""))!;
        Assert.Null(CrashSignatureMatcher.Match(old, Server(-532462766,
            "Save-History Cleanup Failed for C:\\Users\\[user]\\OneDrive\\Documents\\Mount and Blade II Bannerlord\\CoopData\\x\n")));
    }

    [Fact]
    public void A_known_fix_kept_from_the_screen_is_saved_as_read()
    {
        // KeepKnownFix runs for every match the window shows, the Send my logs hint too: My reports keeps the
        // fix to read again, but it never raises the unread badge or the banner.
        var store = new ReportReplyStore(Path.Combine(_directory, "replies"), "coop-client");
        store.SaveKnownFix("smart-app-control-blocked", "Windows Smart App Control blocked a Coop file\n\nTurn off Smart App Control.");
        var answer = Assert.Single(store.Answers());
        Assert.True(answer.Read);
        Assert.Equal(0, store.UnreadCount());
        Assert.Equal(0, store.UnreadReports());
        Assert.Null(ReportReplies.Banner(store.Answers()));
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.Contains("KeepKnownFix(match.Signature.Id,", window);
    }

    private static readonly string[] SendMyLogsIds =
        ["smart-app-control-blocked", "game-bin-foreign-common-dll", "game-bin-old-harmony", "steam-join-unreachable", "onedrive-documents-coopdata"];

    private CrashReportPack SendMyLogs(string tool, string side, params (string Entry, string Text)[] files) =>
        CrashReports.Prepare(new CrashReportRequest
        {
            Tool = tool,
            Side = side,
            Reason = CrashReason.Logs,
            Collect = (zip, limits) =>
            {
                var builder = new LogZipBuilder(limits);
                foreach (var (entry, text) in files)
                    builder.AddText(entry, text);
                return builder.Write(zip, "notes").Skipped;
            }
        }, "0123456789abcdef0123456789abcdef", Path.Combine(_directory, "logs-packs"), Signatures);

    [Fact]
    public void Send_my_logs_with_a_Smart_App_Control_line_shows_a_hint_and_keeps_Send()
    {
        // Server side: the Server Tool's Send my logs packs the server window.
        var pack = SendMyLogs("coop", "server", ("host/server-window.txt", SmartAppControlWindow));
        Assert.True(pack.TriageRan);
        Assert.Equal("smart-app-control-blocked", pack.Match?.Signature.Id);
        var mode = CrashScreen.ModeFor("coop", CrashReason.Logs, pack.TriageRan, pack.Match);
        Assert.Equal(CrashScreenMode.Hint, mode);
        // Send stays big, and the box never says No need to report this.
        Assert.True(CrashScreen.BigSend(mode));
        Assert.Equal(CrashScreen.HintTitle, CrashScreen.Headline(mode, pack.Match));
        Assert.NotEqual(CrashScreen.NoNeedTitle, CrashScreen.Headline(mode, pack.Match));

        // The report carries the match the same way a crash report does, in the summary and in report.json.
        Assert.Equal("smart-app-control-blocked", pack.Summary.Triage?.Signature);
        Assert.Equal(CrashCategory.Setup, pack.Summary.Triage!.Category);
        Assert.Contains("\"signature\": \"smart-app-control-blocked\"", CrashReports.SummaryJson(pack.Summary));
        using (var zip = ZipFile.OpenRead(pack.ZipPath))
        using (var reader = new StreamReader(zip.GetEntry(CrashReports.ManifestName)!.Open()))
            Assert.Contains("smart-app-control-blocked", reader.ReadToEnd());
        File.Delete(pack.ZipPath);

        // Game side, in another tool: a foreign Common.dll in Coop_client.log.
        var game = SendMyLogs("taom", "client", ("game/Coop_client.log",
            "[(14384) 18:02:11 ERR Coop.CoopMod] System.TypeLoadException: Could not load type 'Common.Network.Session.ServerVisibility' from assembly 'Common, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null'.\n"));
        Assert.Equal("game-bin-foreign-common-dll", game.Match?.Signature.Id);
        Assert.Equal(CrashScreenMode.Hint, CrashScreen.ModeFor("taom", CrashReason.Logs, game.TriageRan, game.Match));
        File.Delete(game.ZipPath);
    }

    [Fact]
    public void An_exit_code_line_in_Send_my_logs_shows_nothing()
    {
        const string console = "21:25:39.351  [CoodszHost] Server process exited with code 1.\n";
        var logs = SendMyLogs("coop", "server", ("host/console-window.txt", console), ("host/server-window.txt", "[DedicatedServer] pulse: players=1\n"));
        Assert.True(logs.TriageRan);
        Assert.Null(logs.Match);
        Assert.Null(logs.Summary.Triage!.Signature);
        Assert.Equal(CrashScreenMode.None, CrashScreen.ModeFor("coop", CrashReason.Logs, logs.TriageRan, logs.Match));
        File.Delete(logs.ZipPath);

        // The same logs as a server crash report are the silent exit.
        var crash = CrashSignatureMatcher.Match(Signatures, Evidence("server", 1, [], (CrashLogSource.HostConsole, console), (CrashLogSource.Server, "[DedicatedServer] pulse: players=1\n")));
        Assert.Equal("server-exit-code-1-silent", crash?.Signature.Id);
    }

    [Fact]
    public void The_Send_my_logs_allow_list_takes_only_marked_setup_signatures_without_exit_codes()
    {
        Assert.Equal(SendMyLogsIds, Signatures.Signatures.Where(CrashScreen.FitsSendMyLogs).Select(s => s.Id));
        Assert.Equal(SendMyLogsIds, CrashScreen.ForSendMyLogs(Signatures).Signatures.Select(s => s.Id));

        // A silent exit, a setup signature not marked for it, and a marked one that needs an exit code are all left out.
        Assert.False(CrashScreen.FitsSendMyLogs(Signatures.Signatures.Single(s => s.Id == "server-exit-code-1-silent")));
        Assert.False(CrashScreen.FitsSendMyLogs(Signatures.Signatures.Single(s => s.Id == "coop-workshop-server-native-missing")));
        var gpu = Signatures.Signatures.Single(s => s.Id == "gpu-create-texture-array");
        Assert.False(CrashScreen.FitsSendMyLogs(new CrashSignature { Id = gpu.Id, Category = gpu.Category, Match = gpu.Match, SendMyLogs = true }));
        var silent = Signatures.Signatures.Single(s => s.Id == "server-exit-code-1-silent");
        Assert.False(CrashScreen.FitsSendMyLogs(new CrashSignature { Id = silent.Id, Category = silent.Category, Match = silent.Match, SendMyLogs = true }));

        // A match from outside the list never shows in Send my logs, even if one were passed in.
        var outside = CrashSignatureMatcher.Match(Signatures, Server(1, SilentWindow));
        Assert.Equal("server-exit-code-1-silent", outside?.Signature.Id);
        Assert.Equal(CrashScreenMode.None, CrashScreen.ModeFor("coop", CrashReason.Logs, true, outside));

        // The window downloads the signatures for Send my logs too.
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.Contains("CrashScreen.RunsTriage(_request.Tool, _request.Reason)", window);
        Assert.Contains("CrashScreenMode.KnownSendHelps or CrashScreenMode.Hint", window);
    }

    private static void Put(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}

// Coodsz Host's known server stop lines follow the tool's language. Translator.Current is process
// wide, so this runs in the collection that never runs next to other tests.
[Collection(TranslatorLanguage.Name)]
public sealed class ServerCrashExplainLanguageTests
{
    [Fact]
    public void Known_server_stop_lines_use_the_current_language_and_keep_the_English_prefixes()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            var lines = ServerCrashExplain.Known(1, "Server process exited with code 1.", CrashTriageTests.ModPatchWindow, null);
            Assert.NotNull(lines);
            Assert.Contains(lines!, line => line.StartsWith("What happened:") && line.Contains("kein Absturz"));
            Assert.Contains(lines!, line => line.StartsWith("What to do:") && line.Contains("Einrichtung fertig. Drück noch einmal auf Server starten."));
            Assert.Contains(lines!, line => line.StartsWith("What not to do:") && line.Contains("schalte CoopModPatch dafür nicht aus"));

            BannerlordCoopLauncher.Core.Localization.Translator.Use(BannerlordCoopLauncher.Core.Localization.Translator.English);
            var english = ServerCrashExplain.Known(1, "Server process exited with code 1.", CrashTriageTests.ModPatchWindow, null);
            Assert.Contains(english!, line => line == "What to do: Setup finished. Press Start server again.");
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
