using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Coodsz.MapCheck;
using HarmonyLib;

namespace BannerlordCoopLauncher.Tests;

// The map check guard in the Join Password client module: Bannerlord 1.4.8's Campaign.CheckMapUpdate threw a
// NullReferenceException while a Coop client loaded the server's save (Burning Empires CR-EH6KT, CR-A9XVH,
// CR-PM83C, CR-8ZXDY, CR-XG5NC). The rule (MapCheckGuardRules.cs) is linked; the engine part is checked
// through its source, the build script, the built DLL and the game's own DLL.
public sealed class MapCheckGuardTests
{
    private static readonly string[] PerObjectCalls =
    {
        "Settlement.CheckPositionsForMapChangeAndUpdateIfNeeded",
        "MapEvent.CheckPositionsForMapChangeAndUpdateIfNeeded",
        "Army.CheckPositionsForMapChangeAndUpdateIfNeeded",
        "MobileParty.CheckPositionsForMapChangeAndUpdateIfNeeded",
        "MobileParty.CheckAiForMapChangeAndUpdateIfNeeded",
    };

    [Fact]
    public void A_null_reference_in_a_coop_client_map_check_is_swallowed()
    {
        Assert.Null(MapCheckGuardRules.Filter(new NullReferenceException(), active: true));
        Assert.Null(MapCheckGuardRules.Filter(null, active: true));
        Assert.Null(MapCheckGuardRules.Filter(null, active: false));
    }

    [Fact]
    public void Any_other_exception_still_reaches_the_game()
    {
        var invalid = new InvalidOperationException("boom");
        Assert.Same(invalid, MapCheckGuardRules.Filter(invalid, active: true));
        var argument = new ArgumentNullException("party");
        Assert.Same(argument, MapCheckGuardRules.Filter(argument, active: true));
        // Outside a Coop client's map check even a NullReferenceException is left alone.
        var missing = new NullReferenceException();
        Assert.Same(missing, MapCheckGuardRules.Filter(missing, active: false));
    }

    [Fact]
    public void Guard_acts_only_inside_a_coop_client_map_check()
    {
        Assert.True(MapCheckGuardRules.Active(dedicatedServer: false, coopServer: false, coopClientConnected: true, inMapCheck: true));
        // The dedicated server, a Coop server, single player and any time outside CheckMapUpdate are left alone.
        Assert.False(MapCheckGuardRules.Active(dedicatedServer: true, coopServer: true, coopClientConnected: false, inMapCheck: true));
        Assert.False(MapCheckGuardRules.Active(dedicatedServer: true, coopServer: false, coopClientConnected: true, inMapCheck: true));
        Assert.False(MapCheckGuardRules.Active(dedicatedServer: false, coopServer: true, coopClientConnected: true, inMapCheck: true));
        Assert.False(MapCheckGuardRules.Active(dedicatedServer: false, coopServer: false, coopClientConnected: false, inMapCheck: true));
        Assert.False(MapCheckGuardRules.Active(dedicatedServer: false, coopServer: false, coopClientConnected: true, inMapCheck: false));
    }

    [Fact]
    public void The_party_line_names_the_party_its_behavior_and_the_null_target()
    {
        var missing = MapCheckGuardRules.MissingTarget("EngageParty", hasAi: true, hasMoveTargetParty: false, hasTargetParty: false,
            hasAiBehaviorParty: true, hasShortTermTargetParty: true, hasTargetSettlement: true);
        var line = MapCheckGuardRules.PartyLine("MobileParty.CheckAiForMapChangeAndUpdateIfNeeded", "lord_WE9_l_party_1", "EngageParty", missing);
        Assert.Contains("party lord_WE9_l_party_1", line);
        Assert.Contains("EngageParty", line);
        Assert.Contains("MoveTargetParty and TargetParty are null", line);
        Assert.Contains("MobileParty.CheckAiForMapChangeAndUpdateIfNeeded", line);
        Assert.Contains("NullReferenceException", line);

        Assert.Contains("party (no id) (unknown behavior)", MapCheckGuardRules.PartyLine("MobileParty.CheckPositionsForMapChangeAndUpdateIfNeeded", null, null, null));
        Assert.Contains("settlement town_EW1", MapCheckGuardRules.ObjectLine("Settlement.CheckPositionsForMapChangeAndUpdateIfNeeded", "settlement", "town_EW1"));
        Assert.Contains("map event (no id)", MapCheckGuardRules.ObjectLine("MapEvent.CheckPositionsForMapChangeAndUpdateIfNeeded", "map event", ""));
    }

    [Theory]
    [InlineData("JoinParty", true, false, false, false, true, true, "MoveTargetParty and TargetParty are null")]
    [InlineData("EscortParty", true, false, true, true, true, true, null)]
    [InlineData("GoAroundParty", true, true, true, false, false, true, "Ai.AiBehaviorPartyBase is null")]
    [InlineData("FleeToPoint", true, true, true, true, false, true, "ShortTermTargetParty is null")]
    [InlineData("FleeToParty", true, false, true, true, true, true, "MoveTargetParty is null")]
    [InlineData("DefendSettlement", true, true, true, true, true, false, "TargetSettlement is null")]
    [InlineData("Hold", true, false, false, false, false, false, null)]
    [InlineData("Hold", false, true, true, true, true, true, "Ai is null")]
    public void The_null_target_follows_the_game_switch(string behavior, bool ai, bool moveTarget, bool target, bool aiBehavior, bool shortTerm, bool settlement, string? expected)
    {
        Assert.Equal(expected, MapCheckGuardRules.MissingTarget(behavior, ai, moveTarget, target, aiBehavior, shortTerm, settlement));
    }

    [Fact]
    public void Only_the_first_skipped_error_is_logged_in_full()
    {
        Assert.True(MapCheckGuardRules.LogsDetail(1));
        Assert.False(MapCheckGuardRules.LogsDetail(2));
        Assert.False(MapCheckGuardRules.LogsDetail(MapCheckGuardRules.MaxLines + 1));
        var error = Thrown();
        var line = MapCheckGuardRules.DetailLine(error);
        Assert.Contains("System.NullReferenceException", line);
        Assert.Contains(nameof(ReadsANull), line);
        Assert.Contains("only the first 50", MapCheckGuardRules.MoreLine(MapCheckGuardRules.MaxLines));
    }

    [Fact]
    public void A_null_outside_the_per_object_calls_ends_only_the_check_and_logs_its_stack()
    {
        // The outer finalizer on CheckMapUpdate uses the same filter, with the guard active inside the check.
        var error = Thrown();
        Assert.Null(MapCheckGuardRules.Filter(error, MapCheckGuardRules.Active(false, false, true, inMapCheck: true)));
        var line = MapCheckGuardRules.OuterLine(error);
        Assert.Contains("Campaign.CheckMapUpdate", line);
        Assert.Contains("The load goes on", line);
        Assert.Contains("map CRC fields were left as they were", line);
        // The full ToString, so the stack names the method that read the null.
        Assert.Contains(nameof(ReadsANull), line);
        Assert.Contains("System.NullReferenceException", line);
    }

    // Peaches' crash: only a save whose map CRCs differ from the client's map runs the party checks, so each load
    // says which case it was.
    [Fact]
    public void Each_load_says_whether_the_save_map_matches_the_client_map()
    {
        var differ = MapCheckGuardRules.CrcLine(11, 22, 33, 33);
        Assert.Contains("differ", differ);
        Assert.Contains("scene 11 vs 22, nav mesh 33 vs 33", differ);
        Assert.Contains("match", MapCheckGuardRules.CrcLine(11, 11, 33, 33));
        Assert.Contains("LogMapCrc(__instance);", File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszJoinPassword", "src", "MapCheckGuard.cs")));
    }

    [Fact]
    public void Module_patches_the_check_and_its_five_calls_and_ships_the_guard()
    {
        var folder = Path.Combine(RepoRoot(), "compat", "CoodszJoinPassword");
        var guard = File.ReadAllText(Path.Combine(folder, "src", "MapCheckGuard.cs"));
        Assert.Contains("AccessTools.Method(typeof(Campaign), \"CheckMapUpdate\", Type.EmptyTypes)", guard);
        foreach (var call in PerObjectCalls)
        {
            var type = call.Split('.')[0];
            Assert.Contains("AccessTools.Method(typeof(" + type + "), nameof(" + call + "), Type.EmptyTypes)", guard);
        }
        Assert.Contains("finalizer: new HarmonyMethod(typeof(MapCheckGuard), nameof(AfterMapCheck))", guard);
        Assert.Contains("harmony.Patch(target, finalizer: skip)", guard);
        Assert.Contains("MapCheckSession.Leave(__exception)", guard);
        Assert.Contains("MapCheckSession.Skip(__exception, () => Describe(__instance, __originalMethod))", guard);
        Assert.Contains("MapCheckSession.GuardOn = GuardOn", guard);
        Assert.Contains("Client.Connected()", guard);
        Assert.Contains("GameNetwork.IsDedicatedServer", guard);

        var module = File.ReadAllText(Path.Combine(folder, "src", "JoinPassword.cs"));
        Assert.Contains("Coodsz.MapCheck.MapCheckGuard.Install(harmony, Log)", module);
        Assert.Contains("Coodsz.MapCheck.MapCheckGuard.ClientStarting(__instance)", module);
        // The TAOM enlist guard keeps its own flag, set at Start and cleared at the game end.
        Assert.Contains("Coodsz.EnlistGuard.TaomEnlistGuard.CoopClientStarted = true", module);
        Assert.Contains("Coodsz.EnlistGuard.TaomEnlistGuard.CoopClientStarted = false", module);

        var build = File.ReadAllText(Path.Combine(folder, "build.ps1"));
        Assert.Contains("src\\MapCheckGuard.cs", build);
        Assert.Contains("src\\MapCheckGuardRules.cs", build);

        // The shipped DLL was rebuilt with the guard (string literals are stored as UTF-16).
        var dll = File.ReadAllBytes(Path.Combine(folder, "module", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll"));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes(MapCheckGuardRules.InstalledLine)));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes("CheckMapUpdate")));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes("Map check: skipped party ")));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes(CoopClientLink.ConnectedField)));
    }

    [Fact]
    public void The_patched_methods_exist_in_bannerlord_1_4_8()
    {
        // Read the installed game's DLL without loading it. A PC or CI runner without Bannerlord skips this;
        // the module build also fails on a renamed public call, since the guard names them with nameof.
        var dll = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll";
        if (!File.Exists(dll)) return;
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var methods = new Dictionary<string, List<MethodDefinition>>(StringComparer.Ordinal);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var key = typeName + "." + reader.GetString(method.Name);
                if (!methods.TryGetValue(key, out var list)) methods[key] = list = new List<MethodDefinition>();
                list.Add(method);
            }
        }

        AssertVoidNoArguments(reader, methods, "TaleWorlds.CampaignSystem.Campaign.CheckMapUpdate");
        AssertVoidNoArguments(reader, methods, "TaleWorlds.CampaignSystem.Settlements.Settlement.CheckPositionsForMapChangeAndUpdateIfNeeded");
        AssertVoidNoArguments(reader, methods, "TaleWorlds.CampaignSystem.MapEvents.MapEvent.CheckPositionsForMapChangeAndUpdateIfNeeded");
        AssertVoidNoArguments(reader, methods, "TaleWorlds.CampaignSystem.Army.CheckPositionsForMapChangeAndUpdateIfNeeded");
        AssertVoidNoArguments(reader, methods, "TaleWorlds.CampaignSystem.Party.MobileParty.CheckPositionsForMapChangeAndUpdateIfNeeded");
        AssertVoidNoArguments(reader, methods, "TaleWorlds.CampaignSystem.Party.MobileParty.CheckAiForMapChangeAndUpdateIfNeeded");
    }

    private static void AssertVoidNoArguments(MetadataReader reader, Dictionary<string, List<MethodDefinition>> methods, string name)
    {
        Assert.True(methods.TryGetValue(name, out var found), name + " is missing from the game DLL.");
        Assert.Contains(found!, method =>
        {
            var blob = reader.GetBlobReader(method.Signature);
            var header = blob.ReadSignatureHeader();
            if (header.IsGeneric) blob.ReadCompressedInteger();
            return header.IsInstance && blob.ReadCompressedInteger() == 0 && blob.ReadSignatureTypeCode() == SignatureTypeCode.Void;
        });
    }

    private static NullReferenceException Thrown()
    {
        try
        {
            ReadsANull(null);
            throw new InvalidOperationException("unreachable");
        }
        catch (NullReferenceException e)
        {
            return e;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int ReadsANull(string? text) => text!.Length;

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

// The map check guard's Coop client lifecycle: it acts only while the client this game started is connected,
// read from a stand in shaped like Coop 0.1.6's CoopClient (private bool isConnected on the class).
public sealed class MapCheckClientLifecycleTests
{
    private sealed class FakeCoopClient
    {
#pragma warning disable CS0414, IDE0044
        private bool isConnected;
#pragma warning restore CS0414, IDE0044

        public void Connect() => isConnected = true;

        public void Disconnect() => isConnected = false;
    }

    private class FakeNetworkBase
    {
#pragma warning disable CS0414, IDE0044
        private bool isConnected = true;
#pragma warning restore CS0414, IDE0044
    }

    private sealed class FakeDerivedClient : FakeNetworkBase { }

    private sealed class NewCoopClient { public int State; }

    [Fact]
    public void A_single_player_game_with_no_coop_start_is_left_alone()
    {
        var link = new CoopClientLink();
        Assert.False(link.Connected());
        Assert.False(link.HasClient);
    }

    [Fact]
    public void A_join_that_is_refused_or_fails_never_turns_the_guard_on_for_a_later_load()
    {
        var link = new CoopClientLink();
        var client = new FakeCoopClient();
        link.Started(client);
        // CoopClient.Start ran, the connect is pending, then the server refuses it: never connected.
        Assert.False(link.Connected());
        client.Disconnect();
        // No game ended, so the TAOM flag would still be set; a single player load now is still left alone.
        Assert.False(link.Connected());
    }

    [Fact]
    public void The_guard_is_on_while_the_save_loads_and_off_after_the_connection_ends()
    {
        var link = new CoopClientLink();
        var client = new FakeCoopClient();
        link.Started(client);
        client.Connect();
        Assert.True(link.Connected());
        client.Disconnect();
        Assert.False(link.Connected());
        // A rejoin starts the client again.
        client.Connect();
        Assert.True(link.Connected());
        link.Forget();
        Assert.False(link.Connected());
    }

    // Codex: Coop's Dispose stops polling without the disconnect callback, so isConnected stays true after a local
    // shutdown. The dispose hook drops that client, and a later single player load is left alone.
    [Fact]
    public void A_disposed_client_turns_the_guard_off_even_while_it_still_reads_connected()
    {
        var link = new CoopClientLink();
        var client = new FakeCoopClient();
        link.Started(client);
        client.Connect();
        link.Ended(new FakeCoopClient());
        Assert.True(link.Connected());
        link.Ended(client);
        Assert.False(link.Connected());
        Assert.False(link.HasClient);

        var module = File.ReadAllText(Path.Combine(MapCheckGuardTests.RepoRoot(), "compat", "CoodszJoinPassword", "src", "JoinPassword.cs"));
        Assert.Contains("CoopClientLink.DeclaredDispose(client)", module);
        Assert.Contains("MapCheckGuard.ClientEnding(__instance)", module);
    }

    public class FakeNetworkBaseWithDispose : IDisposable
    {
        public virtual void Dispose() { }
    }

    public sealed class FakeClientInheritsDispose : FakeNetworkBaseWithDispose { }

    public static object? Disposed;

    public static void RecordDispose(object __instance) => Disposed = __instance;

    // Visible join test (212ebd2c): patching the Dispose looked up on CoopClient threw "You can only patch
    // implemented methods", since CoopClient inherits it from CoopNetworkBase. The lookup now takes the declared
    // method, and Harmony patches it.
    [Fact]
    public void The_dispose_hook_patches_the_declared_dispose_of_an_inherited_client()
    {
        var declared = CoopClientLink.DeclaredDispose(typeof(FakeClientInheritsDispose));
        Assert.NotNull(declared);
        Assert.Equal(typeof(FakeNetworkBaseWithDispose), declared!.DeclaringType);
        Assert.Null(CoopClientLink.DeclaredDispose(null));

        var harmony = new Harmony("coodsz.tests.map-check.dispose." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(declared, prefix: new HarmonyMethod(typeof(MapCheckClientLifecycleTests), nameof(RecordDispose)));
            var client = new FakeClientInheritsDispose();
            client.Dispose();
            Assert.Same(client, Disposed);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            Disposed = null;
        }
    }

    [Fact]
    public void A_new_client_replaces_the_old_one()
    {
        var link = new CoopClientLink();
        var first = new FakeCoopClient();
        first.Connect();
        link.Started(first);
        var second = new FakeCoopClient();
        link.Started(second);
        Assert.False(link.Connected());
        second.Connect();
        Assert.True(link.Connected());
    }

    [Fact]
    public void The_connected_field_is_read_from_a_base_class_and_a_missing_field_keeps_the_guard_off()
    {
        Assert.True(CoopClientLink.ReadConnected(new FakeDerivedClient()));
        Assert.Null(CoopClientLink.ReadConnected(new NewCoopClient()));
        var link = new CoopClientLink();
        link.Started(new NewCoopClient());
        Assert.False(link.Connected());
        Assert.True(link.FieldMissing());
        Assert.Equal("isConnected", CoopClientLink.ConnectedField);
    }
}

// The finalizer bodies run through Harmony on stub methods with the game's shapes: a void instance method
// (the per object calls) and a void method with a prefix and a finalizer (CheckMapUpdate).
public sealed class MapCheckHarmonyTests : IDisposable
{
    private static readonly object Gate = new();
    private readonly Harmony _harmony = new("coodsz.tests.map-check." + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = new();
    private bool _guardOn = true;

    public MapCheckHarmonyTests()
    {
        Monitor.Enter(Gate);
        MapCheckSession.InMapCheck = false;
        MapCheckSession.Caught = 0;
        MapCheckSession.GuardOn = () => _guardOn;
        MapCheckSession.Log = _log.Add;
    }

    public void Dispose()
    {
        _harmony.UnpatchAll(_harmony.Id);
        MapCheckSession.InMapCheck = false;
        MapCheckSession.GuardOn = () => false;
        MapCheckSession.Log = null;
        Monitor.Exit(Gate);
    }

    // Stand ins for a party and for Campaign.CheckMapUpdate.
    public sealed class StubParty
    {
        public string Id = "";
        public Exception? Throws;
        public int Calls;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void CheckAi()
        {
            Calls++;
            if (Throws != null) throw Throws;
        }
    }

    public sealed class StubCampaign
    {
        public List<StubParty> Parties = new();
        public Exception? ThrowsAfterLoop;
        public bool Finished;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void CheckMapUpdate()
        {
            foreach (var party in Parties) party.CheckAi();
            if (ThrowsAfterLoop != null) throw ThrowsAfterLoop;
            Finished = true;
        }
    }

    // The same shapes as MapCheckGuard's patches.
    private static void BeforeMapCheck() => MapCheckSession.Enter();

    private static Exception? AfterMapCheck(Exception __exception) => MapCheckSession.Leave(__exception);

    private static Exception? SkipBroken(Exception __exception, object __instance) =>
        MapCheckSession.Skip(__exception, () => "skipped " + ((StubParty)__instance).Id);

    private void PatchAll()
    {
        _harmony.Patch(AccessTools.Method(typeof(StubCampaign), nameof(StubCampaign.CheckMapUpdate)),
            prefix: new HarmonyMethod(typeof(MapCheckHarmonyTests), nameof(BeforeMapCheck)),
            finalizer: new HarmonyMethod(typeof(MapCheckHarmonyTests), nameof(AfterMapCheck)));
        _harmony.Patch(AccessTools.Method(typeof(StubParty), nameof(StubParty.CheckAi)),
            finalizer: new HarmonyMethod(typeof(MapCheckHarmonyTests), nameof(SkipBroken)));
    }

    [Fact]
    public void A_broken_party_is_skipped_and_the_loop_goes_on()
    {
        PatchAll();
        var campaign = new StubCampaign();
        campaign.Parties.Add(new StubParty { Id = "bad_party", Throws = new NullReferenceException() });
        campaign.Parties.Add(new StubParty { Id = "good_party" });
        campaign.CheckMapUpdate();
        Assert.True(campaign.Finished);
        Assert.Equal(1, campaign.Parties[1].Calls);
        Assert.Contains("skipped bad_party", _log);
        Assert.Contains(_log, line => line.StartsWith("Map check: the first skipped error in full: System.NullReferenceException", StringComparison.Ordinal));
        Assert.False(MapCheckSession.InMapCheck);
    }

    [Fact]
    public void Another_exception_type_passes_through_and_the_flag_is_reset()
    {
        PatchAll();
        var campaign = new StubCampaign();
        campaign.Parties.Add(new StubParty { Id = "odd_party", Throws = new InvalidOperationException("not a null") });
        var thrown = Assert.Throws<InvalidOperationException>(campaign.CheckMapUpdate);
        Assert.Equal("not a null", thrown.Message);
        Assert.False(campaign.Finished);
        Assert.Empty(_log);
        Assert.False(MapCheckSession.InMapCheck);
    }

    [Fact]
    public void A_null_reference_passes_through_when_this_is_not_a_coop_client()
    {
        PatchAll();
        _guardOn = false;
        var campaign = new StubCampaign();
        campaign.Parties.Add(new StubParty { Id = "bad_party", Throws = new NullReferenceException() });
        Assert.Throws<NullReferenceException>(campaign.CheckMapUpdate);
        Assert.Empty(_log);
        Assert.False(MapCheckSession.InMapCheck);
    }

    [Fact]
    public void A_per_object_call_outside_the_check_is_left_alone()
    {
        PatchAll();
        var party = new StubParty { Id = "bad_party", Throws = new NullReferenceException() };
        Assert.Throws<NullReferenceException>(party.CheckAi);
        Assert.Empty(_log);
    }

    [Fact]
    public void The_outer_finalizer_swallows_a_null_outside_the_loop_logs_it_and_resets_the_flag()
    {
        PatchAll();
        var campaign = new StubCampaign { ThrowsAfterLoop = new NullReferenceException() };
        campaign.Parties.Add(new StubParty { Id = "good_party" });
        campaign.CheckMapUpdate();
        Assert.False(campaign.Finished);
        Assert.False(MapCheckSession.InMapCheck);
        var line = Assert.Single(_log);
        Assert.Contains("Campaign.CheckMapUpdate stopped on a NullReferenceException", line);
        Assert.Contains("map CRC fields were left as they were", line);
        Assert.Contains(nameof(StubCampaign.CheckMapUpdate), line);

        // The next run starts clean, and without an exception the flag is reset too.
        campaign.ThrowsAfterLoop = null;
        campaign.CheckMapUpdate();
        Assert.True(campaign.Finished);
        Assert.False(MapCheckSession.InMapCheck);
    }

    [Fact]
    public void The_log_is_capped_per_map_check()
    {
        PatchAll();
        var campaign = new StubCampaign();
        for (var i = 0; i < MapCheckGuardRules.MaxLines + 10; i++)
            campaign.Parties.Add(new StubParty { Id = "bad_" + i, Throws = new NullReferenceException() });
        campaign.CheckMapUpdate();
        Assert.True(campaign.Finished);
        Assert.Equal(MapCheckGuardRules.MaxLines, _log.Count(line => line.StartsWith("skipped ", StringComparison.Ordinal)));
        Assert.Contains(MapCheckGuardRules.MoreLine(MapCheckGuardRules.MaxLines), _log);
        Assert.Equal(MapCheckGuardRules.MaxLines + 10, MapCheckSession.Caught);

        // A second map check counts again from zero.
        _log.Clear();
        campaign.CheckMapUpdate();
        Assert.Contains("skipped bad_0", _log);
    }
}
