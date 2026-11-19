using System.Reflection;
using System.Runtime.CompilerServices;
using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat, the pieces for a player joining a Realm of Thrones server: the
// server's own hero's culture and the foreign save refusal (FreshCampaignRules), Coop's session
// flags, the client's gate on Realm of Thrones' bandit leader code and the join probe's lines
// (JoinRules), all linked.
public sealed class RealmOfThronesJoinTests
{
    private static List<KeyValuePair<string, bool>> Cultures(params (string Id, bool Start)[] cultures) =>
        cultures.Select(culture => new KeyValuePair<string, bool>(culture.Id, culture.Start)).ToList();

    [Fact]
    public void The_servers_hero_is_pinned_to_empire_and_never_a_white_walker()
    {
        Assert.Equal(2, FreshCampaignRules.PickCulture(Cultures(("whitewalker", true), ("vale", true), ("empire", false)), out var rule));
        Assert.Equal("empire is offered", rule);

        Assert.Equal(1, FreshCampaignRules.PickCulture(Cultures(("whitewalker", true), ("vale", true), ("north", true)), out rule));
        Assert.Equal("empire is not offered, first culture with a starting point", rule);

        Assert.Equal(1, FreshCampaignRules.PickCulture(Cultures(("WhiteWalker", false), ("vale", false)), out rule));
        Assert.Equal("no culture has a starting point, first culture offered", rule);

        Assert.Equal(-1, FreshCampaignRules.PickCulture(Cultures(("whitewalker", true)), out rule));
        Assert.Contains("whitewalker", rule);
        Assert.Equal(-1, FreshCampaignRules.PickCulture(null!, out _));
    }

    [Fact]
    public void A_foreign_save_is_refused_with_one_line_a_tool_can_show()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-join-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var save = Path.Combine(root, "vanilla1.sav");
            File.WriteAllBytes(save, new byte[] { 7, 7 });
            var line = FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.OtherWorld, "vanilla1", save, new[] { "Native", "SandBoxCore", "Sandbox", "NavalDLC" }, null!);
            Assert.Equal("!!! Fresh campaign: REFUSED save 'vanilla1': it was not made with Realm of Thrones (modules Native, SandBoxCore, Sandbox, NavalDLC, "
                + "file " + save + ") and is not the server's blank save, so this server cannot load it. "
                + "Pick a new save name to start a new Realm of Thrones campaign.", line);
            Assert.DoesNotContain('\n', line);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Only a readable header that lists no Realm of Thrones module refuses a save. With the header
    // unreadable a real save loads, loudly, and so does one whose file cannot be found.
    [Fact]
    public void A_save_with_an_unreadable_header_loads_with_a_loud_line()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-join-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var template = Path.Combine(root, "default_new_game_warsails.sav");
            File.WriteAllBytes(template, new byte[] { 1, 2, 3 });
            var save = Path.Combine(root, "rotworld.sav");
            File.WriteAllBytes(save, new byte[] { 9, 9, 9, 9 });
            var templates = FreshCampaignRules.TemplateCandidates(save);

            Assert.Equal(FreshCampaignRules.SaveKind.Unknown, FreshCampaignRules.Classify(null!, save, templates, out _));
            var line = FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.Unknown, "rotworld", save, null!, null!);
            Assert.StartsWith("!!! Fresh campaign: save 'rotworld' (" + save + ") has a header that cannot be read", line);
            Assert.Contains("so it loads as it is", line);
            Assert.DoesNotContain("REFUSED", line);

            var missing = Path.Combine(root, "gone.sav");
            Assert.Equal(FreshCampaignRules.SaveKind.NoFile, FreshCampaignRules.Classify(null!, missing, templates, out _));
            var noFile = FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.NoFile, "gone", missing, null!, null!);
            Assert.StartsWith("!!! Fresh campaign: save 'gone' was not found at " + missing + " and its header cannot be read", noFile);

            // A template copy is still told by its bytes alone.
            var copy = Path.Combine(root, "copy.sav");
            File.WriteAllBytes(copy, new byte[] { 1, 2, 3 });
            Assert.Equal(FreshCampaignRules.SaveKind.TemplateCopy, FreshCampaignRules.Classify(null!, copy, FreshCampaignRules.TemplateCandidates(copy), out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    // container, server flag: ready whatever else.
    [InlineData(true, true, true, false, false, false, "Ready")]
    // A Coop module is active, or this is the server: look again until both are loaded.
    [InlineData(true, true, false, false, true, true, "LookAgain")]
    [InlineData(false, false, false, false, true, true, "LookAgain")]
    [InlineData(true, false, true, true, true, false, "LookAgain")]
    // Before every module loaded nothing is settled.
    [InlineData(false, false, false, false, false, false, "LookAgain")]
    // No Coop module and no Coop assemblies once modules loaded: no Coop for the run.
    [InlineData(false, false, false, false, true, false, "NoCoop")]
    // Coop's assemblies without the names and no Coop module listed: a session is assumed.
    [InlineData(true, false, false, false, true, false, "Ready")]
    public void Coops_state_is_not_settled_while_a_coop_module_waits_for_its_assemblies(bool seen, bool container, bool flag, bool server, bool modulesLoaded, bool coopModule, string state)
    {
        Assert.Equal(state, JoinRules.ResolveCoop(seen, container, flag, server, modulesLoaded, coopModule).ToString());
    }

    // Shaped like Coop's BinaryPackageSerializer.Deserialize: a reader quota with MaxDepth 512.
    private static int CoopQuota() => new System.Xml.XmlDictionaryReaderQuotas { MaxArrayLength = 1000, MaxDepth = 512, MaxBytesPerRead = 4096 }.MaxDepth;

    private static int OtherQuota() => new System.Xml.XmlDictionaryReaderQuotas { MaxDepth = 64 }.MaxDepth;

    private static string? _depthLine;

    private static IEnumerable<HarmonyLib.CodeInstruction> DepthTranspiler(IEnumerable<HarmonyLib.CodeInstruction> instructions)
    {
        var result = PackageDepthRules.Raise(instructions, out var line);
        _depthLine = line;
        return result;
    }

    [Fact]
    public void Coops_package_nesting_limit_is_raised_and_nothing_else_changes()
    {
        var harmony = new HarmonyLib.Harmony("coodsz.tests.rot-depth." + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(512, CoopQuota());
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(RealmOfThronesJoinTests), nameof(CoopQuota)),
                transpiler: new HarmonyLib.HarmonyMethod(typeof(RealmOfThronesJoinTests), nameof(DepthTranspiler)));
            Assert.Equal(4096, CoopQuota());
            Assert.Equal("Package depth: Coop's package nesting limit raised from 512 to 4096.", _depthLine);

            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(RealmOfThronesJoinTests), nameof(OtherQuota)),
                transpiler: new HarmonyLib.HarmonyMethod(typeof(RealmOfThronesJoinTests), nameof(DepthTranspiler)));
            Assert.Equal(64, OtherQuota());
            Assert.Equal("Package depth: Coop's nesting limit of 512 not found; players may be refused on join.", _depthLine);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public void A_players_pc_is_never_the_dedicated_server(bool inSession, bool isServer, bool dedicated, bool playerPc)
    {
        Assert.Equal(playerPc, JoinRules.OnPlayerPc(inSession, isServer, dedicated));
    }

    [Fact]
    public void On_the_dedicated_server_a_missing_or_false_server_flag_is_an_error_and_the_server()
    {
        Assert.True(JoinRules.IsServer(true, null, true, out var report));
        Assert.True(report);
        Assert.True(JoinRules.IsServer(true, false, true, out report));
        Assert.True(report);
        Assert.True(JoinRules.IsServer(true, true, true, out report));
        Assert.False(report);
        // Before the session the flag is still false there; that is no error yet.
        Assert.True(JoinRules.IsServer(true, false, false, out report));
        Assert.False(report);
        // A player's PC goes by the flag and never reports.
        Assert.False(JoinRules.IsServer(false, null, true, out report));
        Assert.False(report);
        Assert.True(JoinRules.IsServer(false, true, true, out report));
    }

    [Fact]
    public void The_bandit_leader_code_is_skipped_only_in_the_servers_world_with_every_clan_led()
    {
        Assert.Equal(JoinRules.Gate.Run, JoinRules.BanditLeaderGate(false, true, true, out var line));
        Assert.Null(line);

        Assert.Equal(JoinRules.Gate.Run, JoinRules.BanditLeaderGate(true, false, true, out line));
        Assert.Contains("character creation campaign", line);
        Assert.Equal(JoinRules.Gate.Run, JoinRules.BanditLeaderGate(true, null, true, out line));
        Assert.Contains("character creation campaign", line);

        Assert.Equal(JoinRules.Gate.Skip, JoinRules.BanditLeaderGate(true, true, true, out line));
        Assert.Contains("skipped in the server's world", line);

        Assert.Equal(JoinRules.Gate.Run, JoinRules.BanditLeaderGate(true, true, false, out line));
        Assert.StartsWith("!!! Load gate: a bandit clan in the server's world has no living leader", line);
    }

    private static readonly (string Clan, string Leader, bool Alive)[] Led =
    {
        ("looters", "lord_bandit_boss", true), ("sea_raiders", "lord_bandit_boss", true), ("forest_bandits", "lord_bandit_boss", true),
    };

    [Fact]
    public void Every_bandit_clan_needs_a_living_leader()
    {
        Assert.True(JoinRules.EveryClanLed(Led));
        Assert.False(JoinRules.EveryClanLed(Led.Append(("mountain_bandits", null!, false))));
        Assert.False(JoinRules.EveryClanLed(new[] { ("looters", "lord_bandit_boss", false) }));
        // No bandit clans at all is not "every clan led": the code then runs and says so.
        Assert.False(JoinRules.EveryClanLed(Array.Empty<(string, string, bool)>()));
    }

    [Fact]
    public void The_probe_lines_read_the_same_on_both_sides()
    {
        Assert.Equal("Join probe: bandit clans 3, all with a living leader; leaders lord_bandit_boss.", JoinRules.BanditLine(Led));
        Assert.Equal("Join probe: bandit clans 4, 1 without a living leader (mountain_bandits); leaders lord_bandit_boss.",
            JoinRules.BanditLine(Led.Append(("mountain_bandits", null!, false))));
        Assert.Equal("Join probe: bandit clans 0.", JoinRules.BanditLine(null!));

        Assert.Equal("Join probe: world on Spring 1, 299 (year 299): heroes 1044, clans 246, kingdoms 29, settlements 1065.",
            JoinRules.WorldLine("Spring 1, 299", 299, 1044, 246, 29, 1065));
        Assert.Equal("Join probe: races 4: human, whitewalker, wight, giant.", JoinRules.RacesLine(new[] { "human", "whitewalker", "wight", "giant" }));
        Assert.Equal("Join probe: modules Native v1.4.8, ROT_Map v8.1.8.2.",
            JoinRules.ModulesLine(new[] { new KeyValuePair<string, string>("Native", "v1.4.8"), new KeyValuePair<string, string>("ROT_Map", "v8.1.8.2") }));
        Assert.Equal("Join probe: White Walker clan ROTclan_126 at war with ROTclan_11: yes.", JoinRules.WalkerWarLine("ROTclan_126", "ROTclan_11", true));
        Assert.Equal("Join probe: White Walker clan not found at war with ROTclan_11: unknown.", JoinRules.WalkerWarLine(null!, "ROTclan_11", null));
        Assert.Equal("Join probe: hero package 18234 bytes, culture vale, race 0.", JoinRules.PackageLine(18234, "vale", 0));
    }

    // ---- the ROT gate before a new world (RotHostRules.WorldRefusal) ----

    [Fact]
    public void At_models_and_full_no_world_is_made_without_realm_of_thrones_code()
    {
        foreach (var level in new[] { HostLevel.Models, HostLevel.Full })
        {
            Assert.Null(RotHostRules.WorldRefusal(level, false, level, true, null!));
            Assert.Contains("could not load ROT.dll", RotHostRules.WorldRefusal(level, true, HostLevel.Off, false, "ROT.dll is not loaded"));
            Assert.Contains("not running Realm of Thrones' models", RotHostRules.WorldRefusal(level, false, HostLevel.Off, true, null!));
            Assert.Contains("time model cannot be registered (no constructor)", RotHostRules.WorldRefusal(level, false, level, false, "no constructor"));
        }
        // Off, probe and patches may make a test world whatever the host managed.
        foreach (var asked in new[] { HostLevel.Off, HostLevel.Probe, HostLevel.Patches })
            Assert.Null(RotHostRules.WorldRefusal(asked, true, HostLevel.Off, false, "ROT.dll is not loaded"));

        var line = RotHostRules.WorldRefusalLine("rotworld", "the host could not load ROT.dll");
        Assert.StartsWith("!!! Fresh campaign: REFUSED to make a new world for save 'rotworld': the host could not load ROT.dll.", line);
        Assert.Contains("game's own calendar and rules", line);
    }

    // ---- the world check after a new world is made (FreshCampaignRules) ----

    [Fact]
    public void The_settlement_count_is_the_nodes_the_game_reads()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settlements>\n"
            + "  <Settlement id=\"town_S1\" owner=\"Faction.ROTclan_11\"><Components><Town id=\"t\" /></Components></Settlement>\n"
            + "  <!-- <Settlement id=\"old_one\" /> -->\n"
            + "  <Settlement id=\"village_1\"><Components><Village id=\"v\" /></Components></Settlement>\n"
            + "  <Settlement id=\"retirement_retreat\"><Components><CustomSettlementComponent id=\"c\" /></Components></Settlement>\n"
            + "</Settlements>\n";
        Assert.Equal(3, FreshCampaignRules.CountSettlements(new StringReader(xml)));
    }

    [Fact]
    public void A_realm_of_thrones_world_passes_its_floors()
    {
        var failed = FreshCampaignRules.WorldCheckFailures(29, 246, 1065, 1065, new List<string>(), true, 299, true);
        Assert.Empty(failed);
        Assert.Equal("World check passed: kingdoms 29, clans 246, settlements 1065 (the map defines 1065), year 299, every town and castle has an owner clan.",
            FreshCampaignRules.WorldCheckLine(failed, 29, 246, 1065, 1065, 299, false));
        // Below models the year is the game's own and does not count.
        Assert.Empty(FreshCampaignRules.WorldCheckFailures(29, 246, 1065, 1065, null!, false, 1084, false));
        // An uncounted map is said but fails nothing.
        Assert.Empty(FreshCampaignRules.WorldCheckFailures(29, 246, 1065, -1, null!, true, 299, true));
        Assert.Contains("the map defines an uncounted number", FreshCampaignRules.WorldCheckLine(new List<string>(), 29, 246, 1065, -1, 299, false));
    }

    [Fact]
    public void Each_missed_floor_is_named_in_one_failed_line()
    {
        var failed = FreshCampaignRules.WorldCheckFailures(8, 101, 112, 1065, new List<string> { "castle_EN2" }, true, 1084, false);
        Assert.Equal(6, failed.Count);
        var line = FreshCampaignRules.WorldCheckLine(failed, 8, 101, 112, 1065, 1084, true);
        Assert.StartsWith("!!! World check FAILED: kingdoms 8, expected 29 or more; clans 101, expected 243 or more; settlements 112, Realm of Thrones' map defines 1065; "
            + "1 towns or castles without an owner clan (castle_EN2); Realm of Thrones' time model is not registered; year 1084, expected 299 (", line);
        Assert.Contains("This world is held back", line);
        Assert.DoesNotContain('\n', line);
        Assert.Contains("Going on anyway", FreshCampaignRules.WorldCheckLine(failed, 8, 101, 112, 1065, 1084, false));
    }

    [Theory]
    [InlineData(false, "Full", null, true)]
    [InlineData(false, "Full", "warn", false)]
    [InlineData(false, "Full", " WARN ", false)]
    [InlineData(false, "Models", null, false)]
    [InlineData(false, "Patches", null, false)]
    [InlineData(false, "Off", null, false)]
    [InlineData(true, "Full", null, false)]
    public void Only_a_failed_world_at_full_is_held_back_unless_warn(bool passed, string asked, string? variable, bool held)
    {
        Assert.Equal(held, FreshCampaignRules.HoldsWorld(passed, Enum.Parse<HostLevel>(asked), variable!));
    }

    // ---- the save load decision for every kind of save (FreshCampaignRules.DecideLoad) ----

    private const string HostFailed = "the host could not load ROT.dll (the Host line above says why)";

    // A saved Realm of Thrones world with a failed host: refused at models and full, loaded for
    // tests below that with a loud line about its Realm of Thrones data.
    [Theory]
    [InlineData("Off", "LoadAsIs", true)]
    [InlineData("Probe", "LoadAsIs", true)]
    [InlineData("Patches", "LoadAsIs", true)]
    [InlineData("Models", "Refuse", true)]
    [InlineData("Full", "Refuse", true)]
    public void A_realm_of_thrones_save_never_loads_without_its_code_at_models_and_full(string level, string action, bool loud)
    {
        var asked = Enum.Parse<HostLevel>(level);
        var refusal = RotHostRules.WorldRefusal(asked, true, HostLevel.Off, false, "ROT.dll is not loaded");
        Assert.Equal(action, FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.RotWorld, "rotworld", asked, refusal, out var line).ToString());
        Assert.Equal(loud, line != null && line.StartsWith("!!! Fresh campaign: "));
        if (action == "Refuse")
            Assert.StartsWith("!!! Fresh campaign: REFUSED to load save 'rotworld': " + HostFailed + ". Loaded without Realm of Thrones' code", line);
        else
            Assert.Contains("the Realm of Thrones data in it is lost", line);
    }

    [Fact]
    public void Every_kind_of_save_goes_through_the_same_gate()
    {
        foreach (var level in new[] { HostLevel.Models, HostLevel.Full })
        {
            // Host not ready: nothing loads or starts, whatever the save is.
            foreach (var kind in new[] { FreshCampaignRules.SaveKind.TemplateCopy, FreshCampaignRules.SaveKind.Unknown, FreshCampaignRules.SaveKind.NoFile, FreshCampaignRules.SaveKind.OtherWorld })
                Assert.Equal(FreshCampaignRules.LoadAction.Refuse, FreshCampaignRules.DecideLoad(kind, "s", level, HostFailed, out _));
            FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.TemplateCopy, "s", level, HostFailed, out var newWorld);
            Assert.StartsWith("!!! Fresh campaign: REFUSED to make a new world for save 's'", newWorld);
        }
        // Host ready at full: a template copy starts a new world, a Realm of Thrones world loads quietly.
        Assert.Equal(FreshCampaignRules.LoadAction.StartNew, FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.TemplateCopy, "s", HostLevel.Full, null!, out var none));
        Assert.Null(none);
        Assert.Equal(FreshCampaignRules.LoadAction.LoadAsIs, FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.RotWorld, "s", HostLevel.Full, null!, out none));
        Assert.Null(none);
        // At models with the host ready the behaviors are still not registered: loads, loudly.
        Assert.Equal(FreshCampaignRules.LoadAction.LoadAsIs, FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.RotWorld, "s", HostLevel.Models, null!, out var models));
        Assert.Contains("host level models", models);
        // A foreign save is refused at every level; its own line is the decision line.
        Assert.Equal(FreshCampaignRules.LoadAction.Refuse, FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.OtherWorld, "s", HostLevel.Off, null!, out none));
        Assert.Null(none);
        // Below models an unreadable or missing save loads as before (its decision line is loud).
        Assert.Equal(FreshCampaignRules.LoadAction.LoadAsIs, FreshCampaignRules.DecideLoad(FreshCampaignRules.SaveKind.Unknown, "s", HostLevel.Patches, HostFailed, out _));
    }

    // ---- the world check never fails open (FreshCampaignRules.RunWorldCheck) ----

    private static FreshCampaignRules.WorldNumbers RotWorld() => new()
    {
        Kingdoms = 29, Clans = 246, Settlements = 1065, UnownedFiefs = new List<string>(), Year = 299, TimeModelRegistered = true,
    };

    [Fact]
    public void A_world_check_that_throws_is_a_failed_check()
    {
        var log = new List<string>();
        FreshCampaignRules.WorldNumbers Throws() => throw new NullReferenceException("no campaign");

        Assert.False(FreshCampaignRules.RunWorldCheck(Throws, 1065, HostLevel.Full, null!, log.Add));
        Assert.StartsWith("!!! World check FAILED: the check could not run (NullReferenceException: no campaign). This world is held back", Assert.Single(log));

        log.Clear();
        Assert.True(FreshCampaignRules.RunWorldCheck(Throws, 1065, HostLevel.Full, "warn", log.Add));
        Assert.Contains("Going on anyway", Assert.Single(log));

        log.Clear();
        Assert.True(FreshCampaignRules.RunWorldCheck(Throws, 1065, HostLevel.Patches, null!, log.Add));
        Assert.Contains("Going on anyway", Assert.Single(log));
    }

    [Fact]
    public void The_world_check_holds_a_failed_world_and_passes_a_good_one()
    {
        var log = new List<string>();
        Assert.True(FreshCampaignRules.RunWorldCheck(RotWorld, 1065, HostLevel.Full, null!, log.Add));
        Assert.StartsWith("World check passed: ", Assert.Single(log));

        log.Clear();
        FreshCampaignRules.WorldNumbers Vanilla() => new() { Kingdoms = 8, Clans = 101, Settlements = 112, UnownedFiefs = new List<string>(), Year = 1084 };
        Assert.False(FreshCampaignRules.RunWorldCheck(Vanilla, 1065, HostLevel.Full, null!, log.Add));
        Assert.StartsWith("!!! World check FAILED: kingdoms 8", Assert.Single(log));
        // Below models the year is not counted, and a failure goes on.
        log.Clear();
        Assert.True(FreshCampaignRules.RunWorldCheck(Vanilla, 1065, HostLevel.Patches, null!, log.Add));
        Assert.DoesNotContain("year 1084, expected", Assert.Single(log));
    }

    // ---- the host's own rules that feed the gates (RotHostRules) ----

    [Fact]
    public void Only_the_tested_rot_core_version_is_hosted_unless_a_test_says_so()
    {
        Assert.Null(RotHostRules.VersionRefusal("v8.1.8.0", null!));
        Assert.Null(RotHostRules.VersionRefusal("v8.1.8", null!));
        var refusal = RotHostRules.VersionRefusal("v8.2.0.0", null!);
        Assert.Equal("ROT-Core is v8.2.0.0 and this host is tested with v8.1.8 only (COODSZ_ROT_ANYVERSION=1 hosts it anyway for a test)", refusal);
        Assert.Contains("unknown version", RotHostRules.VersionRefusal(null!, null!));
        Assert.Null(RotHostRules.VersionRefusal("v8.2.0.0", " 1 "));
        Assert.NotNull(RotHostRules.VersionRefusal("v8.1.80", null!));
    }

    [Fact]
    public void A_host_that_is_not_complete_is_loud_and_holds_full_but_not_models()
    {
        Assert.Empty(RotHostRules.FailureList(0, 0, 0, 0, 0));
        Assert.Null(RotHostRules.FailureLine(RotHostRules.FailureList(0, 0, 0, 0, 0)));
        var failures = RotHostRules.FailureList(2, 1, 3, 1, 4);
        Assert.Equal(5, failures.Count);
        Assert.StartsWith("Host: !!! THE HOST IS NOT COMPLETE: 2 of Realm of Thrones' patches failed; 1 of BannerColorPersistence's", RotHostRules.FailureLine(failures));

        var joined = string.Join("; ", failures);
        Assert.Equal("the host is not complete (" + joined + ")", RotHostRules.WorldRefusal(HostLevel.Full, false, HostLevel.Full, true, null!, joined));
        // At models the host's failures alone refuse nothing; the world check names them.
        Assert.Null(RotHostRules.WorldRefusal(HostLevel.Models, false, HostLevel.Models, true, null!, joined));
        Assert.Null(RotHostRules.WorldRefusal(HostLevel.Full, false, HostLevel.Full, true, null!, ""));

        // The world check lists them, so full holds a new world.
        var log = new List<string>();
        Assert.False(FreshCampaignRules.RunWorldCheck(() => new FreshCampaignRules.WorldNumbers
        {
            Kingdoms = 29, Clans = 246, Settlements = 1065, UnownedFiefs = new List<string>(), Year = 299, TimeModelRegistered = true, HostFailures = RotHostRules.FailureList(0, 0, 0, 0, 1),
        }, 1065, HostLevel.Full, null!, log.Add));
        Assert.Contains("1 models failed", Assert.Single(log));
    }

    [Fact]
    public void A_static_field_not_set_means_no_models()
    {
        Assert.True(RotHostRules.RegistersModels(HostLevel.Full, true));
        Assert.True(RotHostRules.RegistersModels(HostLevel.Models, true));
        Assert.False(RotHostRules.RegistersModels(HostLevel.Full, false));
        Assert.False(RotHostRules.RegistersModels(HostLevel.Patches, true));
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    public void A_bandit_clan_needs_a_living_leader_from_a_bandit_clan(bool hasLeader, bool alive, bool inBanditClan, bool needs)
    {
        Assert.Equal(needs, RotHostRules.NeedsBanditLeader(hasLeader, alive, inBanditClan));
    }

    // Realm of Thrones patches under "realmofthrones.core" (ROT.SubModule.OnSubModuleLoad); the
    // host uses the same id so its patches order with Coop's and the server's as on a player's PC.
    [Fact]
    public void The_hosts_harmony_id_is_realm_of_thrones_own()
    {
        Assert.Equal("realmofthrones.core", RotHostRules.HarmonyId);
    }

    // ---- the client's creation and load rules (JoinRules) ----

    [Theory]
    [InlineData(true, false, "whitewalker", true)]
    [InlineData(true, null, "WhiteWalker", true)]
    [InlineData(true, true, "whitewalker", false)]
    [InlineData(false, false, "whitewalker", false)]
    [InlineData(true, false, "vale", false)]
    [InlineData(true, false, null, false)]
    public void Only_the_white_walker_start_is_hidden_and_only_in_a_coop_creation(bool playerPcInSession, bool? loadingSavedGame, string? culture, bool hidden)
    {
        Assert.Equal(hidden, JoinRules.HidesCulture(playerPcInSession, loadingSavedGame, culture!));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, false)]
    [InlineData(false, true, false)]
    public void The_map_sweep_is_skipped_only_in_the_servers_world_on_a_players_pc(bool playerPcInSession, bool? loadingSavedGame, bool skipped)
    {
        Assert.Equal(skipped, JoinRules.SkipsMapSweep(playerPcInSession, loadingSavedGame));
    }

    [Fact]
    public void The_timing_lines_say_seconds_since_the_session_started()
    {
        Assert.Equal(" (+81 s since the Coop session started)", JoinRules.SinceSession(81.4));
        Assert.Equal("", JoinRules.SinceSession(null));
    }

    // ---- the ship visual budget in the server's world (JoinRules.ShipVisualRule) ----

    [Fact]
    public void A_ship_is_only_built_for_a_party_this_player_can_see_and_four_a_frame()
    {
        // Before Coop's visibility sweep the save says every party is visible; only sight counts.
        Assert.Equal(JoinRules.ShipBuild.NotVisible, JoinRules.ShipVisualRule(false, false, false, true, false, 0));
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRule(false, false, false, true, true, 0));
        // After the sweep the party's own flag counts too, and a party within sight is always in view
        // (live test 2026-11-11: no ship was ever built while the flag alone decided).
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRule(false, false, true, false, true, 0));
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRule(false, false, true, true, false, 0));
        Assert.Equal(JoinRules.ShipBuild.NotVisible, JoinRules.ShipVisualRule(false, false, true, false, false, 0));
        // The mass build at the join stays blocked: before the sweep a party out of sight waits,
        // however its saved flag reads.
        Assert.Equal(JoinRules.ShipBuild.NotVisible, JoinRules.ShipVisualRule(false, false, false, true, false, 0));
        // Four new ships a frame; the fifth waits for the next frame.
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRule(false, false, true, true, true, 3));
        Assert.Equal(JoinRules.ShipBuild.OverBudget, JoinRules.ShipVisualRule(false, false, true, true, true, 4));
    }

    // The budget fails closed: a check that throws holds every new ship but the player's own, and
    // Coop's sweep only counts when it could run (it returns without work while the main party
    // has no position, and the save's "visible to all" flags would then pass every ship).
    [Fact]
    public void An_unreadable_check_holds_every_new_ship_and_an_empty_sweep_does_not_count()
    {
        Assert.Equal(JoinRules.ShipBuild.Unreadable, JoinRules.ShipVisualRuleUnreadable(false, false));
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRuleUnreadable(false, true));
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRuleUnreadable(true, false));
        Assert.True(JoinRules.SweepRan(true, true));
        Assert.False(JoinRules.SweepRan(true, false));
        Assert.False(JoinRules.SweepRan(false, true));
        // Before a real sweep every party at sea counts as visible only within sight.
        Assert.Equal(JoinRules.ShipBuild.NotVisible, JoinRules.ShipVisualRule(false, false, JoinRules.SweepRan(true, false), true, false, 0));
    }

    // The compiled budget holds only the new allocation (AddShipVisual), never ValidateIsDirty,
    // so NavalDLC's fading, blockade ships and clean up always run; a held party is marked dirty
    // again; the budget is decided once per Game, not each frame from Coop's session; and both
    // prefixes hold on a failed check instead of letting everything through.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_compiled_ship_budget_holds_only_new_ships_and_fails_closed()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);
        var budget = module.GetType("RealmOfThronesCoopCompat.ShipVisualBudget");
        var all = new[] { budget }.Concat(budget.NestedTypes).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToList();
        List<string> Calls(string name) => budget.Methods.Single(m => m.Name == name).Body.Instructions.Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>()
            .Where(m => m.DeclaringType != null).Select(m => m.DeclaringType.Name + "." + m.Name).ToList();

        Assert.Contains(all, i => i.Operand as string == "AddShipVisual");
        Assert.DoesNotContain(all, i => i.Operand as string == "ValidateIsDirty");
        Assert.Contains("MobileParty.SetNavalVisualAsDirty", Calls("Rearm"));
        Assert.Contains("ShipVisualBudget.Rearm", Calls("TickPrefix"));
        Assert.DoesNotContain("Coop.get_OnPlayerPc", Calls("TickPrefix"));
        Assert.Contains("Coop.get_OnPlayerPc", Calls("StartGame"));
        Assert.Contains("ShipVisualBudget.CheckFailed", Calls("TickPrefix"));
        Assert.Contains("ShipVisualBudget.CheckFailed", Calls("AddShipPrefix"));
        Assert.Contains("JoinRules.ShipVisualRuleUnreadable", Calls("AddShipPrefix"));
        Assert.Contains("JoinRules.ShipVisualRuleUnreadable", Calls("Rule"));
        Assert.Contains("JoinRules.SweepRan", Calls("SweepPostfix"));

        // Every server guard rests on Coop.IsPlayerHero: on the server a missing or throwing check
        // is loud and counts as a guard failure, with no Hero.MainHero fallback. And the dedicated
        // server test (asked every frame) is kept instead of walking every assembly each time.
        var coop = module.GetType("RealmOfThronesCoopCompat.Coop");
        List<string> CoopCalls(string name) => coop.Methods.Single(m => m.Name == name).Body.Instructions.Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>()
            .Where(m => m.DeclaringType != null).Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
        Assert.Equal(2, CoopCalls("IsPlayerHero").Count(call => call == "Coop.PlayerHeroCheckFailed"));
        Assert.Contains("RotHost.AddGuardFailures", CoopCalls("PlayerHeroCheckFailed"));
        Assert.Contains("Coop.PlayerHeroCheckFailed", CoopCalls("ResolveNow"));
        Assert.Contains("Coop._dedicatedProcess", CoopCalls("IsDedicatedServerProcess"));
    }

    // Every hook of the story, the client gates and disable set, the settings pins and the load
    // gate is handed to Harmony by name in its installer, and the steps that are no hook at all
    // (the Finish prefix taken off, the finance model out of the chain, duels and ravens off, the
    // client's wait raised) are still called. Removing one of them fails here: the hook bodies
    // need a game, so no rule test would notice.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void Every_hook_is_handed_to_harmony_by_its_installer()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        IEnumerable<Mono.Cecil.Cil.Instruction> Code(string type) => WithNested(module.GetType("RealmOfThronesCoopCompat." + type)).SelectMany(t => t.Methods)
            .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions);
        var hooks = new Dictionary<string, string[]>
        {
            ["RotStory"] = new[] { "OpenPopupPrefix", "TickPostfix", "FramePostfix", "SessionPostfix", "ReceiveItemsPrefix", "SkipMusic", "MusterTranspiler", "MusterPrefix",
                "MusterFinalizer", "PullInPrefix", "PlayerKingPrefix", "BorashTranspiler", "SuccessionPrefix", "NoEvents", "LeftOutOfMuster", "DisbandUnlessPlayers", "OwnerIfPlayer" },
            ["ClientGates"] = new[] { "SkipInWorld", "SkipInSession", "RunOriginalInWorld", "DailyTickTranspiler", "RosterOnServer", "ElephantPrefix" },
            ["ClientDisable"] = new[] { "MissionStartPostfix", "JoinConditionPostfix", "EnlistmentSyncDataPostfix", "StorylineDonePrefix", "EditCommandPrefix" },
            ["SettingsPatches"] = new[] { "BoolPrefix", "IntPrefix", "FloatPrefix", "RavenCheckPrefix", "SendRavenPrefix" },
            ["ClientLoadGate"] = new[] { "Prefix", "CulturePrefix", "MapSweepPrefix" },
            ["ShipVisualBudget"] = new[] { "TickPrefix", "AddShipPrefix", "SweepPostfix" },
        };
        foreach (var pair in hooks)
        {
            var strings = Code(pair.Key).Select(i => i.Operand as string).OfType<string>().ToHashSet();
            var type = module.GetType("RealmOfThronesCoopCompat." + pair.Key);
            Assert.All(pair.Value, hook => Assert.True(strings.Contains(hook) && type.Methods.Any(m => m.Name == hook) || pair.Key == "ClientGates" && strings.Contains(hook),
                pair.Key + " no longer hands " + hook + " to Harmony"));
        }
        bool Calls(string type, string method, string declaring, string name) => module.GetType("RealmOfThronesCoopCompat." + type).Methods.Single(m => m.Name == method).Body.Instructions
            .Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == declaring && m.Name == name);
        Assert.True(Calls("ClientDisable", "SyncFinish", "Harmony", "Unpatch"), "ClientDisable no longer takes RoT's Finish prefix off");
        Assert.Contains(Code("ClientDisable"), i => i.Operand as string == "_previousModel");
        Assert.True(Calls("SettingsPins", "OnCampaignBegan", "SettingsPins", "DuelsAndRavensOff"), "duels and ravens are no longer switched off at campaign start");
        Assert.Contains(Code("CoopPatience"), i => i.Operand as string == "BlockingTimeout");
    }

    // The protection switches (COODSZ_ROT_PATIENCE, COODSZ_ROT_SHIPS): only "off" turns a protection
    // off; "0" or a typo keeps it on and is said once as not understood.
    [Theory]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("on", true, false)]
    [InlineData("off", false, false)]
    [InlineData("OFF ", false, false)]
    [InlineData(" Off", false, false)]
    [InlineData("0", true, true)]
    [InlineData("of", true, true)]
    [InlineData("false", true, true)]
    public void A_protection_is_turned_off_only_by_the_exact_value_off(string? value, bool on, bool notUnderstood)
    {
        Assert.Equal(on, JoinRules.ProtectionOn(value!, out var unclear));
        Assert.Equal(notUnderstood, unclear);
        Assert.Equal(on, JoinRules.PatienceOn(value!));
        Assert.Equal(on, JoinRules.ShipsOn(value!));
    }

    [Fact]
    public void A_value_not_understood_is_said_with_what_stays_on()
    {
        Assert.Equal("Coop patience: COODSZ_ROT_PATIENCE=0 is not understood (only \"off\" turns it off), so it stays on.",
            JoinRules.NotUnderstoodLine(JoinRules.PatienceVariable, " 0 ", "Coop patience"));
        Assert.Equal("Ship visuals budget: COODSZ_ROT_SHIPS=of is not understood (only \"off\" turns it off), so it stays on.",
            JoinRules.NotUnderstoodLine(JoinRules.ShipsVariable, "of", "Ship visuals budget"));
    }

    [Fact]
    public void The_patience_off_line_names_the_switch_and_Coops_own_wait()
    {
        Assert.Equal("Coop patience: off (COODSZ_ROT_PATIENCE=off); Coop's own wait applies: 30 s.", JoinRules.PatienceOffLine(" off ", "30 s"));
        Assert.Equal("Coop patience: off (COODSZ_ROT_PATIENCE=OFF); Coop's own wait applies: unreadable.", JoinRules.PatienceOffLine("OFF", null!));
    }

    // The server's join watchdog: 15 minutes on Realm of Thrones servers, never less than Coop's own.
    [Fact]
    public void The_server_waits_fifteen_minutes_for_a_joining_player_unless_Coop_waits_longer()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), JoinRules.ServerJoinWait(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(20), JoinRules.ServerJoinWait(TimeSpan.FromMinutes(20)));
        Assert.Equal(TimeSpan.FromMinutes(15), JoinRules.ServerJoinWait(TimeSpan.Zero));
    }

    [Fact]
    public void The_server_patience_lines_name_the_limits_and_what_was_changed()
    {
        Assert.Equal("Coop patience (server): a joining player may apply nothing for 900 s before the server drops the join (Coop's own limit 300 s, JoinReplayAppliedTimeout; "
            + "2 reads in OverloadedPeerManager.AbortStalledJoiners now read ours), so a first join that compiles shaders gets in.",
            JoinRules.ServerPatienceLine(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 2, true));
        Assert.Equal("!!! Coop patience (server): OverloadedPeerManager.AbortStalledJoiners reads NetworkJoinLimits.ReplayAppliedTimeout 3 time(s), not the 2 this module knows, "
            + "so Coop changed it; the patch is taken off again, Coop's own limit of 300 s applies and a first join that compiles shaders for longer is dropped.",
            JoinRules.ServerPatienceLine(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 3, false));
        Assert.StartsWith("!!! Coop patience (server): OverloadedPeerManager.AbortStalledJoiners reads NetworkJoinLimits.ReplayAppliedTimeout 0 time(s)",
            JoinRules.ServerPatienceLine(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 0, false));
        Assert.Equal("Coop patience (server): off (COODSZ_ROT_PATIENCE=off); Coop's own join limit applies: 300 s.", JoinRules.ServerPatienceOffLine("off", TimeSpan.FromMinutes(5)));
        Assert.StartsWith("!!! Coop patience (server): Common.Network.NetworkJoinLimits not found", JoinRules.ServerPatienceMissingLine("Common.Network.NetworkJoinLimits"));
        Assert.Equal("Coop patience: NetworkJoinLimits.ReplayAppliedTimeout reads 900 s after setting it (was 300 s), so while a join catches up Coop waits that long for blocking game thread work.",
            JoinRules.ClientJoinWaitLine(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)));
        Assert.StartsWith("!!! Coop patience: NetworkJoinLimits.ReplayAppliedTimeout still reads 300 s after setting it", JoinRules.ClientJoinWaitLine(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void The_ships_off_line_says_the_crash_risk_returns()
    {
        Assert.Contains("COODSZ_ROT_SHIPS=off", JoinRules.ShipsOffLine("off"));
        Assert.Contains("crash again", JoinRules.ShipsOffLine("off"));
    }

    // The budget's decision at a Game's first map tick: a sweep seen for this Game before the map
    // opened is kept, a sweep of another Game counts for nothing, and the switch off lets every ship through.
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, true, false, true)]
    public void The_budget_keeps_a_sweep_of_this_game_and_holds_only_on_a_join_with_the_switch_on(bool sweptThisGame, bool join, bool switchOn, bool active, bool sweepDone)
    {
        Assert.Equal((active, sweepDone), JoinRules.ShipGameStart(sweptThisGame, join, switchOn));
    }

    // ---- the server's join patch on a stand in for Coop's watchdog (JoinWaitPatch, real Harmony) ----

    private static class StandInLimits
    {
        public static readonly TimeSpan ReplayAppliedTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Other = TimeSpan.FromMinutes(5);
    }

    private static class StandInOtherLimits
    {
        public static readonly TimeSpan ReplayAppliedTimeout = TimeSpan.FromMinutes(5);
    }

    // Each reads the limit the way Coop's AbortStalledJoiners does: as a value for the check, by address for the log.
    private static class StandInWatchdog
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string TwoReads(TimeSpan elapsed) =>
            (elapsed >= StandInLimits.ReplayAppliedTimeout) + "|" + StandInLimits.ReplayAppliedTimeout.TotalSeconds + "|" + StandInOtherLimits.ReplayAppliedTimeout.TotalSeconds + "|" + StandInLimits.Other.TotalSeconds;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string NoRead(TimeSpan elapsed) => (elapsed >= StandInLimits.Other) + "";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string OneRead(TimeSpan elapsed) => (elapsed >= StandInLimits.ReplayAppliedTimeout) + "";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ThreeReads(TimeSpan elapsed) =>
            (elapsed >= StandInLimits.ReplayAppliedTimeout) + "|" + StandInLimits.ReplayAppliedTimeout.TotalSeconds + "|" + (elapsed > StandInLimits.ReplayAppliedTimeout);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Twice(TimeSpan elapsed) => (elapsed >= StandInLimits.ReplayAppliedTimeout) + "|" + StandInLimits.ReplayAppliedTimeout.TotalSeconds;

        public static void Noop() { }
    }

    private static readonly FieldInfo StandInField = typeof(StandInLimits).GetField(nameof(StandInLimits.ReplayAppliedTimeout))!;

    private static MethodInfo Watchdog(string name) => typeof(StandInWatchdog).GetMethod(name)!;

    [Fact]
    public void Two_reads_are_moved_so_a_ten_minute_stall_passes_with_fifteen_and_not_with_five()
    {
        var harmony = new HarmonyLib.Harmony("test.joinwait.two");
        try
        {
            var method = Watchdog(nameof(StandInWatchdog.TwoReads));
            Assert.Equal("True|300|300|300", StandInWatchdog.TwoReads(TimeSpan.FromMinutes(10)));
            Assert.True(JoinWaitPatch.Apply(harmony, method, StandInField, TimeSpan.FromMinutes(15), out var reads));
            Assert.Equal(2, reads);
            // Only the target field is moved: one of another name and one of another type keep Coop's value.
            Assert.Equal("False|900|300|300", StandInWatchdog.TwoReads(TimeSpan.FromMinutes(10)));
            harmony.UnpatchAll(harmony.Id);
            Assert.True(JoinWaitPatch.Apply(harmony, method, StandInField, TimeSpan.FromMinutes(5), out _));
            Assert.Equal("True|300|300|300", StandInWatchdog.TwoReads(TimeSpan.FromMinutes(10)));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Theory]
    [InlineData(nameof(StandInWatchdog.NoRead), 0, "True")]
    [InlineData(nameof(StandInWatchdog.OneRead), 1, "True")]
    [InlineData(nameof(StandInWatchdog.ThreeReads), 3, "True|300|True")]
    public void Any_other_count_of_reads_takes_the_patch_off_and_leaves_the_method_as_it_was(string name, int expectedReads, string original)
    {
        var harmony = new HarmonyLib.Harmony("test.joinwait." + name);
        try
        {
            var method = Watchdog(name);
            Assert.False(JoinWaitPatch.Apply(harmony, method, StandInField, TimeSpan.FromMinutes(15), out var reads));
            Assert.Equal(expectedReads, reads);
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            Assert.True(info == null || info.Transpilers.All(patch => patch.owner != harmony.Id));
            Assert.Equal(original, (string)method.Invoke(null, new object[] { TimeSpan.FromMinutes(10) })!);
            Assert.StartsWith("!!! ", JoinRules.ServerPatienceLine(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), reads, false));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void Another_patch_on_the_same_method_reruns_the_transpiler_and_the_count_starts_again()
    {
        var harmony = new HarmonyLib.Harmony("test.joinwait.rerun");
        var other = new HarmonyLib.Harmony("test.joinwait.other");
        try
        {
            var method = Watchdog(nameof(StandInWatchdog.Twice));
            Assert.True(JoinWaitPatch.Apply(harmony, method, StandInField, TimeSpan.FromMinutes(15), out _));
            other.Patch(method, postfix: new HarmonyLib.HarmonyMethod(Watchdog(nameof(StandInWatchdog.Noop))));
            Assert.Equal(2, JoinWaitPatch.Reads);
            Assert.Equal("False|900", StandInWatchdog.Twice(TimeSpan.FromMinutes(10)));
        }
        finally
        {
            other.UnpatchAll(other.Id);
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void The_ship_line_comes_at_most_once_a_minute_and_only_when_something_happened()
    {
        Assert.False(JoinRules.ShipReportDue(59.9, 3, 0, 0));
        Assert.True(JoinRules.ShipReportDue(60, 3, 0, 0));
        Assert.True(JoinRules.ShipReportDue(600, 0, 1, 0));
        Assert.True(JoinRules.ShipReportDue(600, 0, 0, 1));
        Assert.False(JoinRules.ShipReportDue(600, 0, 0, 0));
        Assert.Equal("Ship visuals since the last line: 5 let through, 120 held back as not in view, 2 delayed by the budget; 300 wait for their ship; seeing range 7.5, "
            + "Coop's visibility sweep has run; no ship held back as not in view; you are at sea; night.",
            JoinRules.ShipMinuteLine(5, 120, 2, 300, 7.46f, true, null, 0, false, true, true));
    }

    // The ship test's proof of whether a fleet ever came near the main party.
    [Fact]
    public void The_ship_line_says_how_near_the_nearest_held_ship_came()
    {
        Assert.Equal("Ship visuals since the last line: 0 let through, 390 held back as not in view, 0 delayed by the budget; 390 wait for their ship; seeing range 12.6, "
            + "Coop's visibility sweep has run; nearest waiting ship 34.2 away (2 within 25.2), not visible; you are on land; day.",
            JoinRules.ShipMinuteLine(0, 390, 0, 390, 12.6f, true, 34.24f, 2, false, false, false));
        Assert.EndsWith("Coop's visibility sweep has not run; nearest waiting ship 13 away (1 within 25.2), visible; at sea or on land unread; day or night unread.",
            JoinRules.ShipMinuteLine(0, 1, 0, 1, 12.6f, false, 13f, 1, true, null, null));
    }

    // The engine console command against the report folders: only with COODSZ_ROT_NOREPORTS=1.
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("yes", false)]
    public void The_report_experiment_runs_only_when_set_to_1(string? value, bool on)
    {
        Assert.Equal(on, CrashTraceRules.NoReportsOn(value!));
    }

    [Fact]
    public void The_report_experiment_line_says_what_was_sent_and_what_came_back()
    {
        Assert.Equal("Crash reports: sent the engine console command \"donot_create_dumps_for_warnings\" (COODSZ_ROT_NOREPORTS=1, an experiment); the engine answered: nothing.",
            CrashTraceRules.NoReportsLine(""));
        Assert.EndsWith("the engine answered: \"Ok\".", CrashTraceRules.NoReportsLine(" Ok \n"));
        Assert.Contains("failed: TargetInvocationException", CrashTraceRules.NoReportsFailedLine("TargetInvocationException: x"));
        Assert.Contains("was not found; nothing was sent", CrashTraceRules.NoReportsMissingLine());
    }

    [Fact]
    public void A_built_ship_and_the_players_own_party_are_never_held()
    {
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRule(true, false, true, false, false, 99));
        Assert.Equal(JoinRules.ShipBuild.Build, JoinRules.ShipVisualRule(false, true, false, false, false, 99));
    }

    [Fact]
    public void Two_hundred_ships_at_sea_out_of_sight_build_nothing_and_ten_in_sight_take_three_frames()
    {
        int builtTotal = 0, frames = 0, pending = 10;
        while (pending > 0)
        {
            frames++;
            var builtThisFrame = 0;
            for (var i = 0; i < 200; i++)
                Assert.Equal(JoinRules.ShipBuild.NotVisible, JoinRules.ShipVisualRule(false, false, true, false, false, builtThisFrame));
            var waiting = pending;
            for (var i = 0; i < waiting; i++)
            {
                if (JoinRules.ShipVisualRule(false, false, true, true, true, builtThisFrame) != JoinRules.ShipBuild.Build)
                    continue;
                builtThisFrame++;
                builtTotal++;
                pending--;
            }
        }
        Assert.Equal((10, 3), (builtTotal, frames));
        Assert.StartsWith("Ship visuals: in the first 10 s on the map 10 ship visuals were let through to be built, 200 held back as not visible to this player and 6 delayed by the budget of 4 a frame; the map scene has 53189 root entities.",
            JoinRules.ShipBudgetLine(10, 200, 6, "53189"));
    }
}
