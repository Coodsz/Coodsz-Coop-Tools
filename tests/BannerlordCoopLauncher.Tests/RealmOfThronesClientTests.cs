using System.Reflection;
using System.Runtime.CompilerServices;
using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat on players' PCs: the gates' tables and when they apply, the prefixes
// that stop Realm of Thrones' methods (run here with Harmony on stand ins for a game method and a
// Realm of Thrones patch on it), the steps for RoT's PlayerEncounter.Finish prefix, the lines and
// the OFF messages (ClientRules, linked), and the install line in the compiled module.
public sealed class RealmOfThronesClientTests
{
    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, null, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    public void World_gates_apply_only_in_the_servers_world_and_session_gates_in_any_coop_campaign(bool playerPc, bool? loadingSavedGame, bool world, bool session)
    {
        Assert.Equal(world, ClientRules.Gated(playerPc, loadingSavedGame, ClientRules.Scope.World));
        Assert.Equal(session, ClientRules.Gated(playerPc, loadingSavedGame, ClientRules.Scope.Session));
    }

    [Fact]
    public void Every_gate_names_one_method_once_with_a_reason()
    {
        var all = ClientRules.BehaviorGates.Concat(ClientRules.PatchGates).ToList();
        var keys = all.Select(gate => gate.Type + "." + gate.Method + "(" + string.Join(",", gate.Args ?? Array.Empty<string>()) + ")").ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(all, gate => Assert.False(string.IsNullOrWhiteSpace(gate.Why)));
        Assert.All(all, gate => Assert.StartsWith("ROT.", gate.Type));
        // A behavior handler is skipped, never answered for; only RoT's bool prefixes run the original.
        Assert.All(ClientRules.BehaviorGates, gate => Assert.Equal(ClientRules.Answer.Skip, gate.Answer));
        Assert.All(ClientRules.BehaviorGates, gate => Assert.Equal(ClientRules.Scope.World, gate.Scope));
        // Only RBM's XML rewrite is off in the whole session.
        Assert.All(all.Where(gate => gate.Scope == ClientRules.Scope.Session), gate => Assert.Equal("ROT.HarmonyPatches.ModCompatibility.RBM", gate.Type));
        Assert.Equal(27, ClientRules.BehaviorGates.Length);
        Assert.Equal(16, ClientRules.PatchGates.Length);
    }

    [Fact]
    public void The_world_changing_handlers_are_gated_and_the_bookkeeping_is_not()
    {
        string[] Names(IEnumerable<ClientRules.Gate> gates) => gates.Select(gate => gate.Name).ToArray();
        var behaviors = Names(ClientRules.BehaviorGates);
        var patches = Names(ClientRules.PatchGates);

        Assert.Contains("ROTEventBehavior.OnDailyTick", behaviors);
        Assert.Contains("ROTEventBehavior.TriggerEvent", behaviors);
        Assert.Contains("ROTEventBehavior.SkipEvent", behaviors);
        Assert.Contains("ROTStorylineWars.EnforceWars", behaviors);
        Assert.Contains("ROTStorylineWars.OnWarDeclared", behaviors);
        Assert.Contains("ROTSpawnUniqueWanderers.SpawnHeroes", behaviors);
        Assert.Contains("ROTTroopRecruiter.OnTroopRecruited", behaviors);
        Assert.Contains("ROTTroopRecruiter.OnDistributeLootToParty", behaviors);
        Assert.Contains("ROTEnlistmentBehavior.Enlist", behaviors);
        Assert.Contains("ROTCoreBehavior+<>c.<RegisterEvents>b__1_0", behaviors);
        // The NPC marriages and the daily gold for poor clans are the server's (its guards keep players out).
        Assert.Contains("ROTRelationshipsBehavior.CheckNPCMarriages", behaviors);
        Assert.Contains("ROTCoreBehavior.DailyTickClan", behaviors);
        // Kept running: CanHeroDie is read by local checks, OnDailyTick keeps the invasion flag and
        // the growth counter (its roster call goes through ClientGates' transpiler), the recruiter's
        // helpers feed the models, the war list's readers and every SyncData.
        Assert.DoesNotContain("ROTOthersCampaignBehavior.CanHeroDie", behaviors);
        Assert.DoesNotContain("ROTOthersCampaignBehavior.OnDailyTick", behaviors);
        Assert.DoesNotContain("ROTTroopRecruiter.GetPartyComposition", behaviors);
        Assert.DoesNotContain("ROTStorylineWars.IsWarForced", behaviors);
        Assert.DoesNotContain(behaviors, name => name.EndsWith(".SyncData"));

        Assert.Equal(ClientRules.Answer.RunOriginal, ClientRules.PatchGates.Single(gate => gate.Name == "Diplomacy+DeclareWarActionPatch.Prefix").Answer);
        Assert.Equal(ClientRules.Answer.RunOriginal, ClientRules.PatchGates.Single(gate => gate.Name == "OthersPatches.patch7").Answer);
        Assert.Equal(ClientRules.Answer.RunOriginal, ClientRules.PatchGates.Single(gate => gate.Name == "LootCollectorPatch.Prefix").Answer);
        Assert.Equal(ClientRules.Answer.RunOriginal, ClientRules.PatchGates.Single(gate => gate.Name == "MarriageCrash+Patch_MarriageOfferGeneration.Prefix").Answer);
        Assert.Equal(new[] { "TaleWorlds.CampaignSystem.Hero", "TaleWorlds.CampaignSystem.Hero", "System.Boolean", "System.Boolean" },
            ClientRules.PatchGates.Single(gate => gate.Name == "OthersPatches.patch11").Args);
        Assert.Contains("EnlistmentPatches.patch9", patches);
        Assert.Contains("OthersPatches.patch6", patches);
    }

    private sealed class StandIn
    {
        private void Handler() { }
        private static bool Handler(int count) => count > 0;
        internal static bool Prefix(string name, bool flag) => flag;
        internal static bool Prefix(string name, string other, bool flag) => flag;
        public void Tick(float dt) { }
    }

    [Fact]
    public void A_method_is_found_by_name_and_an_overload_only_with_its_parameter_types()
    {
        Assert.NotNull(ClientRules.FindMethod(typeof(StandIn), "Tick", null));
        // Overloaded and no parameter types: never a guess.
        Assert.Null(ClientRules.FindMethod(typeof(StandIn), "Handler", null));
        Assert.Null(ClientRules.FindMethod(typeof(StandIn), "Prefix", null));
        Assert.Single(ClientRules.FindMethod(typeof(StandIn), "Handler", new[] { "System.Int32" })!.GetParameters());
        Assert.Equal(3, ClientRules.FindMethod(typeof(StandIn), "Prefix", new[] { "System.String", "System.String", "System.Boolean" })!.GetParameters().Length);
        Assert.Null(ClientRules.FindMethod(typeof(StandIn), "Prefix", new[] { "System.Int32" }));
        Assert.Null(ClientRules.FindMethod(typeof(StandIn), "Missing", null));
        // Declared on the type itself, not inherited.
        Assert.Null(ClientRules.FindMethod(typeof(StandIn), "ToString", null));
    }

    // Stand ins: a game method, a Realm of Thrones bool prefix on it that vetoes one faction, and
    // a void postfix of RoT's that turns the game's answer to false.
    private static readonly List<string> Declared = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeclareWar(string faction1, string faction2) => Declared.Add(faction1 + " on " + faction2);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool RotVetoPrefix(string faction1) => faction1 != "nightswatch";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CanCapture() => true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RotCapturePostfix(ref bool __result) => __result = false;

    // The proof for the patch gates: a prefix on RoT's patch method itself makes it a no op, and the
    // game method still runs (RunOriginal) or keeps its own answer (Skip on a void postfix), only
    // while the gate says so.
    [Fact]
    public void A_gated_realm_of_thrones_patch_lets_the_game_method_run_and_only_while_gated()
    {
        var harmony = new HarmonyLib.Harmony("coodsz.tests.rot-client." + Guid.NewGuid().ToString("N"));
        var before = ClientRules.IsGated;
        HarmonyLib.HarmonyMethod Ours(string name) => new(typeof(ClientRules).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        MethodInfo Here(string name) => typeof(RealmOfThronesClientTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            harmony.Patch(Here(nameof(DeclareWar)), prefix: new HarmonyLib.HarmonyMethod(Here(nameof(RotVetoPrefix))));
            harmony.Patch(Here(nameof(CanCapture)), postfix: new HarmonyLib.HarmonyMethod(Here(nameof(RotCapturePostfix))));
            harmony.Patch(Here(nameof(RotVetoPrefix)), prefix: Ours(nameof(ClientRules.RunOriginalInWorld)));
            harmony.Patch(Here(nameof(RotCapturePostfix)), prefix: Ours(nameof(ClientRules.SkipInWorld)));

            // Outside the server's world RoT's patches work as shipped.
            ClientRules.IsGated = _ => false;
            Declared.Clear();
            DeclareWar("nightswatch", "freefolk");
            Assert.Empty(Declared);
            Assert.False(CanCapture());

            // In the server's world the war the server declared goes through and the game's answer stands.
            ClientRules.IsGated = scope => scope == ClientRules.Scope.World;
            DeclareWar("nightswatch", "freefolk");
            Assert.Equal(new[] { "nightswatch on freefolk" }, Declared);
            Assert.True(CanCapture());

            // A session gate alone does not switch a world gate.
            ClientRules.IsGated = scope => scope == ClientRules.Scope.Session;
            Assert.False(CanCapture());

            // A gate that throws counts as not gated.
            ClientRules.IsGated = _ => throw new InvalidOperationException();
            Assert.False(CanCapture());
        }
        finally
        {
            ClientRules.IsGated = before;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    // Stand in for RoTElephantAgentComponent: action ids in a static field filled by an initializer
    // (no static constructor, like RoT's and ActionIndexCache) and read in the parallel tick.
    private sealed class ElephantStandIn
    {
        private static readonly int[] Attacks = { ActionTable.Lookup("act_elephant_attack") };
        public int Seen;

        public void OnTickParallel(float dt)
        {
            if (dt >= 0f)
                Seen = Attacks[0];
        }
    }

    private static class ActionTable
    {
        public static bool Read;

        public static int Lookup(string name)
        {
            Read = true;
            return -1;
        }
    }

    private static bool ElephantPrefix() => true;

    // While modules load the elephant gate patches nothing, so the action ids are not read before
    // the game loaded its action data. Once loaded the patch goes on and runs the initializer: a
    // patch at load time would have left the ids at -1. The game's .NET Framework runs it when
    // Harmony builds the method; .NET 8 here only when the method runs, so the stand in patch runs
    // it right after, as the game does.
    [Fact]
    public void The_elephant_gate_waits_for_the_action_data_because_patching_reads_the_action_ids()
    {
        var harmony = new HarmonyLib.Harmony("coodsz.tests.rot-elephant." + Guid.NewGuid().ToString("N"));
        var prefix = new HarmonyLib.HarmonyMethod(typeof(RealmOfThronesClientTests).GetMethod(nameof(ElephantPrefix), BindingFlags.NonPublic | BindingFlags.Static)!);
        var patchedNow = false;
        void Patch(MethodInfo tick)
        {
            harmony.Patch(tick, prefix: prefix);
            RuntimeHelpers.RunClassConstructor(tick.DeclaringType!.TypeHandle);
            patchedNow = true;
        }
        try
        {
            Assert.Null(ClientRules.PatchElephantTick(typeof(ElephantStandIn), actionsLoaded: false, Patch));
            Assert.False(patchedNow);
            Assert.False(ActionTable.Read);

            Assert.True(ClientRules.PatchElephantTick(typeof(ElephantStandIn), actionsLoaded: true, Patch));
            Assert.True(patchedNow);
            Assert.True(ActionTable.Read);
            Assert.False(ClientRules.PatchElephantTick(typeof(StandIn), actionsLoaded: true, Patch));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Theory]
    [InlineData(true, false, "Remove")]
    [InlineData(true, true, "None")]
    [InlineData(false, true, "PutBack")]
    [InlineData(false, false, "None")]
    public void Realm_of_thrones_finish_prefix_is_off_for_the_session_and_back_after(bool playerPcInSession, bool removed, string step)
    {
        Assert.Equal(step, ClientRules.Finish(playerPcInSession, removed).ToString());
    }

    [Fact]
    public void Only_realm_of_thrones_finance_wrapper_leaves_the_model_chain()
    {
        Assert.True(ClientRules.IsRotFinanceWrapper("ROT.Models.ROTClanFinanceModel"));
        Assert.False(ClientRules.IsRotFinanceWrapper("NavalDLC.GameComponents.NavalDLCClanFinanceModel"));
        Assert.False(ClientRules.IsRotFinanceWrapper("TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel"));
        Assert.False(ClientRules.IsRotFinanceWrapper(null!));
    }

    [Fact]
    public void Only_realm_of_thrones_stat_models_pass_the_after_deployment_calls_on()
    {
        Assert.True(ClientRules.IsRotStatModel("ROT.Models.ROTAgentStatCalculateModel"));
        Assert.True(ClientRules.IsRotStatModel("RoT_Elephants.RoTElephantAgentStatCalculateModel"));
        // War Sails' model and the game's pass the calls on themselves or have nothing to pass.
        Assert.False(ClientRules.IsRotStatModel("NavalDLC.GameComponents.NavalAgentStatCalculateModel"));
        Assert.False(ClientRules.IsRotStatModel("SandBox.GameComponents.SandboxAgentStatCalculateModel"));
        Assert.False(ClientRules.IsRotStatModel(null!));
    }

    [Fact]
    public void The_lines_say_what_is_on_and_name_what_is_missing()
    {
        Assert.Equal("Client gates: 26 on, 0 not found.", ClientRules.SummaryLine("Client gates", 26, new List<string>()));
        Assert.Equal("!!! Client patch gates: 15 on, 1 not found (OthersPatches.patch11); those run as Realm of Thrones ships them.",
            ClientRules.SummaryLine("Client patch gates", 15, new List<string> { "OthersPatches.patch11" }));
        Assert.StartsWith("Client: Realm of Thrones' battle statics are set", ClientRules.StaticsLine(new List<string>(), true));
        Assert.Equal("!!! Client: at mission start ROT.SubModule.DragonBehavior is null, so Realm of Thrones' agent spawn and battle patches will throw in this battle.",
            ClientRules.StaticsLine(new List<string> { "ROT.SubModule.DragonBehavior" }, true));
        Assert.Contains(" are null", ClientRules.StaticsLine(new List<string> { "a", "b" }, true));
        Assert.Equal(3, ClientRules.MissionStatics.Length);
    }

    // Duels off leaves ROTDuelsBehavior.Instance null in every session; that is only loud while a
    // reader of it is not gated.
    [Fact]
    public void A_null_duels_instance_is_quiet_only_while_every_reader_is_gated()
    {
        var duels = new List<string> { ClientRules.DuelsInstance };
        var quiet = ClientRules.StaticsLine(duels, true);
        Assert.StartsWith("Client: Realm of Thrones' battle statics are set at mission start (ROT.SubModule.EnlistmentBehavior, ROT.SubModule.DragonBehavior).", quiet);
        Assert.EndsWith("ROT.CampaignBehaviors.ROTDuelsBehavior.Instance is null since duels are off; every reader of it is gated.", quiet);
        Assert.StartsWith("!!! Client: at mission start ROT.CampaignBehaviors.ROTDuelsBehavior.Instance is null", ClientRules.StaticsLine(duels, false));
        // The other two are always set by Realm of Thrones, so they stay loud.
        Assert.StartsWith("!!! Client: at mission start ROT.SubModule.EnlistmentBehavior is null", ClientRules.StaticsLine(new List<string> { "ROT.SubModule.EnlistmentBehavior", ClientRules.DuelsInstance }, true));

        var gateNames = ClientRules.BehaviorGates.Concat(ClientRules.PatchGates).Select(gate => gate.Name).ToList();
        Assert.All(ClientRules.DuelsInstanceReaders, reader => Assert.Contains(reader, gateNames));
        Assert.True(ClientRules.DuelsReadersGated(gateNames));
        Assert.False(ClientRules.DuelsReadersGated(gateNames.Where(name => name != "ROTCoreBehavior+<>c.<RegisterEvents>b__1_0").ToList()));
    }

    // Realm of Thrones is only in the game's Modules folder while it is tested; COODSZ_ROT_DLL points
    // at another copy of ROT.dll.
    internal static readonly string RotDll = Environment.GetEnvironmentVariable("COODSZ_ROT_DLL") is { Length: > 0 } path ? path
        : @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ROT-Core\bin\Win64_Shipping_Client\ROT.dll";

    public sealed class RotDllFactAttribute : FactAttribute
    {
        public RotDllFactAttribute()
        {
            if (!File.Exists(RotDll))
                Skip = "Realm of Thrones is not installed at " + RotDll + ".";
        }
    }

    // The skip gates on bool methods, where false is the right answer: the End Battle option's
    // condition (false hides the option) and the event starts (false: no event started or skipped).
    private static readonly string[] SkippedBoolGates =
        { "ROTEnlistmentMenuBehavior+<>c.<AddMenus>b__4_5", "ROTEventBehavior.SkipEvent", "ROTEventBehavior.TriggerEvent" };

    // A check that throws keeps the gates on in a session: a gate that failed open would let a
    // world rule run on a player's PC.
    [Fact]
    public void A_gate_whose_check_throws_stays_on_in_a_session()
    {
        Assert.True(ClientRules.ErrorAnswer(true, false));
        Assert.False(ClientRules.ErrorAnswer(false, true));
        Assert.True(ClientRules.ErrorAnswer(null, true));
        Assert.False(ClientRules.ErrorAnswer(null, false));
        Assert.True(ClientRules.ErrorAnswer(null, null));

        var isGated = ClientRules.IsGated;
        var onError = ClientRules.OnError;
        try
        {
            ClientRules.IsGated = _ => throw new InvalidOperationException("test");
            ClientRules.OnError = _ => ClientRules.ErrorAnswer(true, null);
            Assert.False(ClientRules.SkipInWorld());
            var result = false;
            Assert.False(ClientRules.RunOriginalInWorld(ref result));
            Assert.True(result);
            // Even the fallback throwing keeps the gate on.
            ClientRules.OnError = _ => throw new InvalidOperationException("test");
            Assert.False(ClientRules.SkipInSession());
        }
        finally
        {
            ClientRules.IsGated = isGated;
            ClientRules.OnError = onError;
        }
    }

    // Every name the client gates find by name is in the real ROT.dll once, with the shape the gate
    // needs. A Realm of Thrones update that renames a lambda shows up here, not as a quiet miss.
    [RotDllFact]
    public void Every_name_the_client_gates_use_is_in_realm_of_thrones()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RotDll);
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        var types = module.Types.SelectMany(WithNested).ToDictionary(type => type.FullName.Replace('/', '+'));
        var gates = ClientRules.BehaviorGates.Concat(ClientRules.PatchGates).ToList();
        Mono.Cecil.MethodDefinition? Find(string type, string name, string[]? args)
        {
            if (!types.TryGetValue(type, out var found))
                return null;
            var named = found.Methods.Where(method => method.Name == name);
            if (args != null)
                named = named.Where(method => method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args));
            var list = named.ToList();
            return list.Count == 1 ? list[0] : null;
        }
        bool Reads(Mono.Cecil.MethodDefinition? method, string declaring, string member) => method != null && method.HasBody
            && method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MemberReference m && m.DeclaringType?.FullName == declaring && m.Name == member);

        var skippedBools = new List<string>();
        foreach (var gate in gates)
        {
            var method = Find(gate.Type, gate.Method, gate.Args);
            Assert.True(method != null, gate.Name + " is not in ROT.dll once");
            // RunOriginal answers true for a bool prefix; a skip leaves a void method out.
            var returns = method!.ReturnType.FullName;
            if (gate.Answer == ClientRules.Answer.RunOriginal)
                Assert.True(returns == "System.Boolean", gate.Name + " returns " + returns);
            else if (returns == "System.Boolean")
                skippedBools.Add(gate.Name);
            else
                Assert.True(returns == "System.Void", gate.Name + " returns " + returns);
        }
        // A skipped bool method answers false, so only the ones whose false means "nothing done".
        Assert.Equal(SkippedBoolGates, skippedBools.OrderBy(name => name, StringComparer.Ordinal));
        // The lambdas are the ones the comments say.
        Assert.True(Reads(Find("ROT.CampaignBehaviors.ROTCoreBehavior+<>c", "<RegisterEvents>b__1_0", null), "TaleWorlds.CampaignSystem.GameMenus.GameMenu", "ExitToLast"));
        Assert.True(Reads(Find("ROT.CampaignBehaviors.ROTEnlistmentMenuBehavior+<>c", "<AddMenus>b__4_5", null), "TaleWorlds.CampaignSystem.MapEvents.MapEvent", "get_IsSiegeAssault"));
        Assert.True(Reads(Find("ROT.CampaignBehaviors.ROTOthersCampaignBehavior", "<RegisterEvents>b__16_2", null), "TaleWorlds.CampaignSystem.Hero", "SetBirthDay"));
        foreach (var reader in ClientRules.DuelsInstanceReaders)
        {
            var gate = gates.Single(g => g.Name == reader);
            Assert.True(Reads(Find(gate.Type, gate.Method, gate.Args), "ROT.CampaignBehaviors.ROTDuelsBehavior", "Instance"), reader + " does not read ROTDuelsBehavior.Instance");
        }
        // ClientGates' transpiler expects exactly one 7 argument AddToCounts call in OnDailyTick.
        var daily = Find("ROT.CampaignBehaviors.ROTOthersCampaignBehavior", "OnDailyTick", null)!;
        Assert.Equal(1, daily.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference m
            && m.DeclaringType.FullName == "TaleWorlds.CampaignSystem.Roster.TroopRoster" && m.Name == "AddToCounts" && m.Parameters.Count == 7));
        Assert.NotNull(Find("RoT_Elephants.RoTElephantAgentComponent", "OnTickParallel", null));
        // ClientDisable's hooks (its PendingHook table) and the Finish prefix it takes off.
        Assert.Equal("System.Boolean", Find("ROT.CampaignBehaviors.ROTEnlistmentBehavior", "EnlistmentJoinCondition", null)?.ReturnType.FullName);
        Assert.NotNull(Find("ROT.CampaignBehaviors.ROTEnlistmentBehavior", "SyncData", null));
        var done = Find("ROT.Screens.StorylinePopupLayer+<>c", "<ShowStorylineWarsPopup>b__6_3", null);
        Assert.True(done != null && done.Parameters.Count == 1 && done.Parameters[0].ParameterType.FullName.Contains("InquiryElement"));
        Assert.Equal("System.String", Find("ROT.Commands.EditStorylineWars", "Execute", null)?.ReturnType.FullName);
        Assert.Equal("System.Boolean", Find("ROT.CampaignBehaviors.ROTEnlistmentBehaviorPatches", "Finish", null)?.ReturnType.FullName);
        Assert.Contains(types[ClientRules.RotFinanceModel].Fields, field => field.Name == "_previousModel");
        foreach (var name in ClientRules.MissionStatics)
        {
            var cut = name.LastIndexOf('.');
            Assert.Contains(types[name.Substring(0, cut)].Fields, field => field.IsStatic && field.Name == name.Substring(cut + 1));
        }
    }

    [Fact]
    public void Every_off_feature_has_one_short_english_message()
    {
        foreach (ClientRules.Off feature in Enum.GetValues(typeof(ClientRules.Off)))
        {
            var text = ClientRules.OffTexts[feature];
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(text.Length <= 80, text);
            Assert.EndsWith(".", text);
            Assert.DoesNotContain((char)0x2014, text);
        }
        Assert.Equal(Enum.GetValues(typeof(ClientRules.Off)).Length, ClientRules.OffTexts.Count);
    }

    private static string CompatDll() => RealmOfThronesCompatTests.CompatDll;

    // The wiring in the compiled module: the player's PC branch installs the gates, and the install
    // hands the live gate to the prefixes. Deleting either would leave every rule test green.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_client_gates_are_installed_on_players_pcs_and_get_the_live_gate()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(CompatDll());
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        IEnumerable<string> Operands(Mono.Cecil.MethodDefinition method) => method.HasBody
            ? method.Body.Instructions.Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>().Where(m => m.DeclaringType != null).Select(m => m.DeclaringType.Name + "." + m.Name)
            : Enumerable.Empty<string>();

        var subModuleCalls = WithNested(module.GetType("RealmOfThronesCoopCompat.SubModule")).SelectMany(t => t.Methods).SelectMany(Operands).ToList();
        Assert.Contains("ClientGates.Install", subModuleCalls);
        var install = module.GetType("RealmOfThronesCoopCompat.ClientGates").Methods.Single(m => m.Name == "Install");
        Assert.Contains("ClientRules.IsGated", Operands(install));
        Assert.Contains("ClientDisable.Install", Operands(install));
        Assert.Contains("ClientRules.OnError", WithNested(module.GetType("RealmOfThronesCoopCompat.ClientGates")).SelectMany(t => t.Methods).SelectMany(Operands));
        // A check that throws is logged once and keeps the gates on in a session, in every place
        // that asks.
        var gates = module.GetType("RealmOfThronesCoopCompat.ClientGates");
        Assert.Contains("ClientGates.LogErrorOnce", Operands(gates.Methods.Single(m => m.Name == "IsGated")));
        Assert.Contains("ClientGates.AnswerOnError", Operands(gates.Methods.Single(m => m.Name == "RosterOnServer")));
        Assert.Contains("ClientGates.AnswerOnError", Operands(module.GetType("RealmOfThronesCoopCompat.ClientDisable").Methods.Single(m => m.Name == "World")));
    }
}
