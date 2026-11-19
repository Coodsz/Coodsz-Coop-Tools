using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat's server guards (RotGuardsRules.cs, linked, and the rows of
// RotHostTables.cs they took out of later): on the server the main hero and the player clan are
// the server's own, so Realm of Thrones' rules for "the player" get a test for every player before
// they are hosted. The Realm of Thrones names the guards use are checked against ROT.dll and the
// guard bodies' player tests against the compiled module here; the game's and Coop's targets on
// the server build, and the order the guards run in next to Coop's own prefixes, by the offline
// harness.
public sealed class RealmOfThronesGuardsTests
{
    private const string Behaviors = "ROT.CampaignBehaviors.";
    private const string Others = "ROT.HarmonyPatches.Core.OthersPatches";

    [Fact]
    public void The_behaviors_that_waited_for_a_player_test_are_hosted_with_their_events_on()
    {
        Assert.Equal(new[] { "ROTStorylineWars", "ROTCoreBehavior", "ROTTroopRecruiter", "ROTOthersCampaignBehavior", "ROTRelationshipsBehavior" },
            RotGuardsRules.MovedBehaviors.Select(type => type.Substring(Behaviors.Length)));
        foreach (var type in RotGuardsRules.MovedBehaviors)
        {
            var row = Assert.Single(RotHostTables.BehaviorRows, r => r.Type == type);
            Assert.Equal(HostDecision.Host, row.Decision);
            // Not inert: the host would block its events at boot.
            Assert.False(row.Inert, type);
            Assert.True(RotHostRules.Registers(row, HostLevel.Full), type);
            Assert.False(RotHostRules.Registers(row, HostLevel.Models), type);
        }
        // Realm of Thrones reads these three static fields without a null check; they stay set.
        Assert.Equal(new[] { "StorylineWars", "CoreBehavior", "Recruiter" },
            RotGuardsRules.MovedBehaviors.Select(type => RotHostTables.BehaviorRows.Single(r => r.Type == type).Field).Where(field => field != null));
        // What is still not hosted: duels, the gank, traders and the like (RotStory hosts the story events).
        Assert.Equal(new[] { "ROTDuelsBehavior", "ROTGankBehavior", "ROTTownTradersBehavior", "ROTClanRecruitBehavior", "ROTHouseTroopsBehavior",
                "ROTSendToWallBehavior", "ROTEasterEggWeapons", "ROT.ROTCharacterCreationCampaignBehavior" },
            RotHostTables.BehaviorRows.Where(r => r.Decision == HostDecision.Later).Select(r => r.Type.StartsWith(Behaviors) ? r.Type.Substring(Behaviors.Length) : r.Type));
    }

    // ROTPartySizeLimitModel asks the campaign for ROTOthersCampaignBehavior without a null check.
    [Fact]
    public void The_party_size_model_is_registered_at_full_now_that_the_white_walkers_are_hosted()
    {
        var atFull = RotHostTables.BehaviorRows.Where(row => RotHostRules.Registers(row, HostLevel.Full) && row.Decision == HostDecision.Host).Select(row => row.Type).ToList();
        var model = Assert.Single(RotHostTables.ModelRows, row => row.Type == RotGuardsRules.MovedModel);

        Assert.Contains(model.Needs, atFull);
        Assert.Contains(model, RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, atFull));
        Assert.Equal(23, RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, atFull).Count);
        // At models no behavior is hosted, so it still waits there.
        Assert.DoesNotContain(model, RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Models, Array.Empty<string>()));
        Assert.Equal("ROTPartySizeLimitModel", RotHostRules.TopModels(RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, atFull))["PartySizeLimitModel"].Substring("ROT.Models.".Length));
    }

    [Theory]
    [InlineData("ROT.SubModule+RecruitPrisonersCampaignBehaviorPatch", "Host")]
    [InlineData("ROT.HarmonyPatches.Core.AIThinkPatch", "Host")]
    [InlineData("ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch", "Guard")]
    [InlineData(Others + ".patch6", "Skip")]
    [InlineData(Others + ".patch7", "Guard")]
    [InlineData(Others + ".patch9", "Skip")]
    [InlineData(Others + ".patch10", "Host")]
    [InlineData(Others + ".patch23", "Skip")]
    [InlineData(Others + ".patch27", "Guard")]
    public void Every_patch_row_with_a_player_reason_has_its_answer(string moved, string decision)
    {
        Assert.Contains(moved, RotGuardsRules.MovedPatches);
        var row = RotGuardsRules.PatchRowOf(RotHostTables.Patches, moved);
        Assert.NotNull(row);
        Assert.Equal(Enum.Parse<HostDecision>(decision), row!.Decision);
        Assert.False(string.IsNullOrWhiteSpace(row.Why));
    }

    [Fact]
    public void The_host_never_applies_a_guard_row_itself_and_only_patches_have_one()
    {
        var guarded = RotHostTables.Patches.Where(row => row.Decision == HostDecision.Guard).ToList();
        Assert.Equal(new[] { "Diplomacy+DeclareWarActionPatch", "OthersPatches.patch7", "OthersPatches.patch27" },
            guarded.Select(row => row.Type.Substring(row.Type.LastIndexOf('.') + 1) + (row.Method == null ? "" : "." + row.Method)));
        Assert.DoesNotContain(guarded, row => row.ByHand);
        Assert.Empty(RotHostRules.HandRows(RotHostTables.Patches, true).Concat(RotHostRules.HandRows(RotHostTables.Patches, false)).Where(row => row.Decision == HostDecision.Guard));
        Assert.DoesNotContain(RotHostTables.BehaviorRows, row => row.Decision == HostDecision.Guard);
        Assert.DoesNotContain(RotHostTables.ModelRows, row => row.Decision == HostDecision.Guard);
        Assert.DoesNotContain(RotHostTables.BannerPatches, row => row.Decision == HostDecision.Guard);
        // The Diplomacy class is applied whole by the host only when its row says Host.
        Assert.NotEqual(HostDecision.Host, RotHostRules.ClassRow(RotHostTables.Patches, "ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch")!.Decision);
        // The execution patch has no player reason of its own and stays later.
        Assert.Equal(HostDecision.Later, RotHostRules.MethodRow(RotHostTables.Patches, Others, "patch11",
            "TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByExecution(Hero, Hero, Boolean, Boolean)")!.Decision);
        Assert.Null(RotGuardsRules.PatchRowOf(RotHostTables.Patches, Others + ".patch99"));
    }

    [Fact]
    public void A_failed_guard_is_loud_and_holds_a_new_world_at_full()
    {
        var failures = RotHostRules.FailureList(0, 0, 0, 0, 0, 2);
        var joined = string.Join("; ", failures);

        Assert.Equal("2 server guards failed, so the rules they guard do not run", Assert.Single(failures));
        Assert.StartsWith("Host: !!! THE HOST IS NOT COMPLETE: 2 server guards failed", RotHostRules.FailureLine(failures));
        Assert.Equal("the host is not complete (" + joined + ")", RotHostRules.WorldRefusal(HostLevel.Full, false, HostLevel.Full, true, null!, joined));
        Assert.Null(RotHostRules.WorldRefusal(HostLevel.Models, false, HostLevel.Models, true, null!, joined));
        Assert.Empty(RotHostRules.FailureList(0, 0, 0, 0, 0, 0));
        var log = new List<string>();
        Assert.False(FreshCampaignRules.RunWorldCheck(() => new FreshCampaignRules.WorldNumbers
        {
            Kingdoms = 29, Clans = 246, Settlements = 1065, UnownedFiefs = new List<string>(), Year = 299, TimeModelRegistered = true, HostFailures = failures,
        }, 1065, HostLevel.Full, null!, log.Add));
        Assert.Contains("2 server guards failed", Assert.Single(log));
    }

    [Fact]
    public void The_boot_lines_name_each_moved_row_and_a_failure_is_loud()
    {
        Assert.Equal("Guards: moved from later: patch X.patch7 is now run inside a server guard: why.", RotGuardsRules.MovedLine("patch", "X.patch7", HostDecision.Guard, "why"));
        Assert.Equal("Guards: moved from later: behavior B is now hosted: w.", RotGuardsRules.MovedLine("behavior", "B", HostDecision.Host, "w"));
        Assert.Equal("Guards: 11 guards in, 0 failed; 15 rows moved from later.", RotGuardsRules.SummaryLine(11, 0, 0, 15));
        Assert.Equal("Guards: 8 guards in, 0 failed, 3 wait for Coop's types until a campaign starts; 15 rows moved from later.", RotGuardsRules.SummaryLine(8, 0, 3, 15));
        Assert.Equal("Guards: !!! 10 guards in, 1 failed; 15 rows moved from later. A Realm of Thrones rule whose guard failed does not run on this server (the lines above say which).",
            RotGuardsRules.SummaryLine(10, 1, 0, 15));
        // Boot logs a line only for a moved name that has its row in the tables: all 15 do.
        var logged = RotGuardsRules.MovedBehaviors.Count(name => RotHostTables.BehaviorRows.Any(row => row.Type == name))
            + RotHostTables.ModelRows.Count(row => row.Type == RotGuardsRules.MovedModel)
            + RotGuardsRules.MovedPatches.Count(name => RotGuardsRules.PatchRowOf(RotHostTables.Patches, name) != null);
        Assert.Equal(15, logged);
    }

    [Theory]
    [InlineData("ROTOthersCampaignBehavior.ChangeToOthersCulture", "Settlement.Culture (and its villages' too)")]
    [InlineData("ROTOthersCampaignBehavior.RevertBackFromOthersCulture", "Settlement.Culture (and its villages' too)")]
    [InlineData("ROTOthersCampaignBehavior.ChangeNotableCulture", "Hero.Culture and Hero.VolunteerTypes of the notables")]
    [InlineData("ROTOthersCampaignBehavior.EvacuateHeroes", "Hero.Culture of the wanderers it turned")]
    public void Each_culture_write_coop_does_not_send_is_one_line_with_its_site(string site, string fields)
    {
        Assert.Equal(fields, RotGuardsRules.CultureFields(site));
        Assert.Equal("Guards: culture write Coop does not send (S16): " + site + " wrote " + fields + " of castle_1 (its culture is whitewalker now).",
            RotGuardsRules.CultureWriteLine(site, "castle_1", "whitewalker"));
        Assert.Equal("Guards: culture write Coop does not send (S16): OthersPatches.patch7 wrote Hero.Culture of lord_1 (whitewalker now).",
            RotGuardsRules.HeroCultureWriteLine("OthersPatches.patch7", "lord_1", "whitewalker"));
    }

    // ROTTroopRecruiter.IsHeroManageable: an AI lord's troops are swapped for his clan's own, a
    // player's never. Arguments: hero, caravan leader, player hero, in a player clan, the server's
    // own hero, in the server's clan, the My Little Warband setting, in a kingdom a player rules.
    [Theory]
    [InlineData(true, false, false, false, false, false, false, false, true)]
    [InlineData(false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false, false, false, false)]
    [InlineData(true, false, true, true, false, false, false, false, false)]
    [InlineData(true, false, false, true, false, false, false, false, false)]
    [InlineData(true, false, false, false, true, true, false, false, false)]
    [InlineData(true, false, false, false, false, true, false, false, false)]
    [InlineData(true, false, false, false, false, false, true, false, true)]
    [InlineData(true, false, false, false, false, false, true, true, false)]
    [InlineData(true, false, false, false, false, false, false, true, true)]
    public void The_troop_recruiter_manages_ai_lords_and_never_a_player_or_a_player_clans_hero(bool hero, bool caravan, bool player, bool playerClan, bool server, bool serverClan,
        bool warband, bool playerKingdom, bool manages)
    {
        Assert.Equal(manages, RotGuardsRules.RecruiterManages(hero, caravan, player, playerClan, server, serverClan, warband, playerKingdom));
    }

    // Only the prisoner counts: a White Walker lord a player takes goes through Realm of Thrones'
    // rule and is executed, as when the player takes one in single player.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void A_capture_of_a_player_or_his_clans_hero_is_left_to_coop(bool prisonerIsPlayer, bool prisonerInPlayerClan, bool leftToCoop)
    {
        Assert.Equal(leftToCoop, RotGuardsRules.CaptureLeftToCoop(prisonerIsPlayer, prisonerInPlayerClan));
    }

    // ROTCoreBehavior.DailyTickClan without the server's hero. Arguments: has a leader, a player's
    // clan, the server's clan, gold, bandit, mercenary, string id, tier; the answer is the gold.
    [Theory]
    [InlineData(true, false, false, 10000, false, false, "clan_1", 3, 15000)]
    [InlineData(true, false, false, 0, false, false, "clan_1", 0, 0)]
    [InlineData(true, false, false, 10001, false, false, "clan_1", 3, 0)]
    [InlineData(true, true, false, 0, false, false, "clan_1", 3, 0)]
    [InlineData(true, false, true, 0, false, false, "clan_1", 3, 0)]
    [InlineData(false, false, false, 0, false, false, "clan_1", 3, 0)]
    [InlineData(true, false, false, 0, true, false, "clan_1", 3, 0)]
    [InlineData(true, false, false, 0, false, true, "clan_1", 3, 0)]
    [InlineData(true, false, false, 0, false, false, "ROTclan_107", 3, 0)]
    [InlineData(true, false, false, 0, false, false, "ROTclan_112", 3, 0)]
    [InlineData(true, false, false, 0, false, false, "ROTclan_113", 3, 0)]
    [InlineData(true, false, false, 0, false, false, "ROTclan_108", 1, 5000)]
    public void Poor_ai_clans_get_daily_gold_and_a_players_clan_never(bool leader, bool playerClan, bool serverClan, int gold, bool bandit, bool mercenary, string id, int tier,
        int given)
    {
        Assert.Equal(given, RotGuardsRules.DailyGold(leader, playerClan, serverClan, gold, bandit, mercenary, id, tier));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    public void The_night_kings_death_spares_every_player(bool playerHero, bool inPlayerClan, bool inServerClan, bool spared)
    {
        Assert.Equal(spared, RotGuardsRules.SparedAtCollapse(playerHero, inPlayerClan, inServerClan));
    }

    // Arguments: the setting, the Night's Watch on side 1 or 2, a kingdom a player's clan rules on
    // side 1 or 2, the Free Folk on side 1 or 2; the answer is the protected side or 0.
    [Theory]
    [InlineData(true, true, false, false, true, false, false, 2)]
    [InlineData(true, false, true, true, false, false, false, 1)]
    [InlineData(false, true, false, false, true, false, false, 0)]
    [InlineData(true, true, false, false, false, false, false, 0)]
    [InlineData(true, false, false, true, false, false, false, 0)]
    [InlineData(true, true, false, false, true, false, true, 0)]
    [InlineData(true, true, false, true, false, false, false, 1)]
    [InlineData(true, true, false, true, true, false, false, 2)]
    [InlineData(true, true, false, true, false, false, true, 1)]
    public void The_kings_protect_the_nights_watch_from_every_kingdom_a_player_rules(bool setting, bool watch1, bool watch2, bool player1, bool player2, bool freeFolk1, bool freeFolk2,
        int side)
    {
        Assert.Equal(side, RotGuardsRules.ProtectedPlayerSide(setting, watch1, watch2, player1, player2, freeFolk1, freeFolk2));
    }

    // The server installs the guards right after the host loaded ROT.dll and applied its patches.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_compiled_submodule_puts_the_guards_in_right_after_the_host()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);
        var subModule = module.GetType("RealmOfThronesCoopCompat.SubModule");
        var calls = new[] { subModule }.Concat(subModule.NestedTypes).SelectMany(type => type.Methods)
            .Where(method => method.HasBody && (method.Name == "OnSubModuleLoad" || method.Name.Contains("<OnSubModuleLoad>")))
            .SelectMany(method => method.Body.Instructions).Select(instruction => instruction.Operand).OfType<Mono.Cecil.MethodReference>()
            .Select(method => method.DeclaringType.Name + "." + method.Name).ToList();

        Assert.Contains("RotGuards.Install", calls);
        Assert.True(calls.IndexOf("RotHost.OnSubModuleLoad") < calls.IndexOf("RotGuards.Install"));
        Assert.Contains(module.GetType("RealmOfThronesCoopCompat.RotGuardsPatches").Methods, method => method.Name == "NoEvents");
    }

    private static IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));

    private static Mono.Cecil.ModuleDefinition Compat() =>
        Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);

    // A guard body with its lambdas and local functions (compiled as <Name>b__ and <Name>g__).
    private static List<Mono.Cecil.MethodDefinition> Body(Mono.Cecil.ModuleDefinition module, string name)
    {
        var methods = WithNested(module.GetType("RealmOfThronesCoopCompat.RotGuardsPatches")).SelectMany(type => type.Methods)
            .Where(method => method.HasBody && (method.Name == name || method.Name.Contains("<" + name + ">"))).ToList();
        Assert.True(methods.Any(method => method.Name == name), "RotGuardsPatches." + name + " is gone");
        return methods;
    }

    private static List<string> Calls(IEnumerable<Mono.Cecil.MethodDefinition> methods) => methods.SelectMany(method => method.Body.Instructions)
        .Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>().Where(m => m.DeclaringType != null).Select(m => m.DeclaringType.Name + "." + m.Name).ToList();

    private static HashSet<string> Strings(Mono.Cecil.ModuleDefinition module, string type) => WithNested(module.GetType(type)).SelectMany(t => t.Methods)
        .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions).Select(i => i.Operand as string).OfType<string>().ToHashSet();

    // Each guard body still asks Coop who the players are (or the plain rule that takes those
    // answers) and does the step it replaces, and RotGuards hands it to Harmony. The bodies need
    // a game to run, so deleting one, its player test or its wiring fails here instead.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void Every_guard_body_tests_every_player_and_is_wired_in()
    {
        using var module = Compat();
        var expected = new Dictionary<string, string[]>
        {
            ["WarDeclaredPrefix"] = new[] { "RotGuardsRules.ProtectedPlayerSide", "RotGuardsPatches.IsPlayerKingdom", "DeclareWarAction.ApplyByDefault" },
            ["UnmarriedPostfix"] = new[] { "RotGuardsPatches.KeepPlayersOut" },
            ["MarriagePrefix"] = new[] { "Coop.IsPlayerClan", "RotGuardsPatches.KeepPlayersOut" },
            ["KeepPlayersOut"] = new[] { "Coop.IsPlayerClan", "Coop.IsPlayerHero", "List`1.RemoveAll" },
            ["GoldPrefix"] = new[] { "RotGuardsRules.DailyGold", "Coop.IsPlayerClan" },
            ["ManageablePrefix"] = new[] { "RotGuardsRules.RecruiterManages", "Coop.IsPlayerHero", "Coop.IsPlayerClan" },
            ["GarrisonPrefix"] = new[] { "Coop.IsPlayerClan" },
            ["HourlyPrefix"] = new[] { "RotGuardsPatches.Collapse", "RotGuardsPatches.Leash" },
            ["Collapse"] = new[] { "RotGuardsRules.SparedAtCollapse", "Coop.IsPlayerHero", "Coop.IsPlayerClan", "KillCharacterAction.ApplyByExecution", "DestroyClanAction.Apply", "RotStory.StartNightKingDeath" },
            ["FindHeirPrefix"] = new[] { "Coop.IsPlayerClan" },
            ["OwnerChangedPrefix"] = new[] { "Coop.IsPlayerClan", "RotGuardsPatches.Twin" },
            ["CapturePrefix"] = new[] { "RotGuardsRules.CaptureLeftToCoop", "Coop.IsPlayerHero", "Coop.IsPlayerClan" },
            ["WarPrefix"] = new[] { "Coop.IsPlayerClan", "RotGuardsPatches.IsPlayerKingdom" },
            ["PeacePrefix"] = new[] { "RotGuardsPatches._peaceVetoed" },
            ["PeaceSentPrefix"] = new[] { "RotGuardsPatches._peaceVetoed" },
            ["PeaceDonePostfix"] = new[] { "RotGuardsPatches._peaceVetoed" },
            ["CompanionTownPrefix"] = new[] { "RotGuardsPatches.IsWhiteWalkerCulture" },
            ["CultureWrittenPostfix"] = new[] { "RotGuardsRules.CultureWriteLine" },
            ["AiThinkPrefix"] = new[] { "RotGuardsPatches.CoopFirst" },
            ["PrisonerPrefix"] = new[] { "RotGuardsPatches.CoopFirst" },
        };
        foreach (var pair in expected)
        {
            var calls = Calls(Body(module, pair.Key));
            Assert.All(pair.Value, call => Assert.True(calls.Contains(call), pair.Key + " no longer calls " + call));
        }
        // The helpers the bodies lean on still ask Coop.
        Assert.Contains("Coop.IsPlayerClan", Calls(Body(module, "IsPlayerKingdom")));
        Assert.Contains("Coop.get_InSession", Calls(Body(module, "CoopFirst")));
        // A refused peace sets the flag that skips Coop's postfix.
        Assert.Contains(Body(module, "PeacePrefix").SelectMany(m => m.Body.Instructions),
            i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld && (i.Operand as Mono.Cecil.FieldReference)?.Name == "_peaceVetoed");

        // Every patch body is handed to Harmony by name from RotGuards.
        var names = Strings(module, "RealmOfThronesCoopCompat.RotGuards");
        Assert.All(expected.Keys.Where(name => name.EndsWith("Prefix") || name.EndsWith("Postfix")),
            name => Assert.True(names.Contains(name), "RotGuards no longer patches with " + name));
    }

    // The Realm of Thrones names the guards look up are in the real ROT.dll, and every one of
    // them is a string in the compiled RotGuards, so the list stays the one the guards use. The
    // game's and Coop's own targets are checked by the offline harness against the server build.
    [RealmOfThronesClientTests.RotDllFact]
    public void Every_name_the_guards_use_is_in_realm_of_thrones()
    {
        using var rot = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesClientTests.RotDll);
        var types = rot.Types.SelectMany(WithNested).ToDictionary(type => type.FullName.Replace('/', '+'));
        bool Has(string type, string member) => types.TryGetValue(type, out var found)
            && (found.Methods.Any(m => m.Name == member) || found.Fields.Any(f => f.Name == member) || found.Properties.Any(p => p.Name == member));
        const string Wars = Behaviors + "ROTStorylineWars", Others2 = Behaviors + "ROTOthersCampaignBehavior", Relations = Behaviors + "ROTRelationshipsBehavior";
        const string Recruiter = Behaviors + "ROTTroopRecruiter", Places = "ROT.Misc.ROTSettlements";
        var used = new (string Type, string Member)[]
        {
            (Wars, "NightsWatchDefenders"), (Wars, "OnWarDeclared"), ("ROT.Misc.ROTKingdoms", "NightsWatch"), ("ROT.Misc.ROTKingdoms", "FreeFolk"),
            ("ROT.ROTSettings", "KingsProtectNightsWatch"), ("ROT.ROTSettings", "MyLittleWarbandAllowCustomKingdomPartyTroops"),
            (Relations, "_unmarriedHeroes"), (Relations, "DailyTick"), (Relations, "CheckNPCMarriages"), (Behaviors + "ROTCoreBehavior", "DailyTickClan"),
            (Recruiter, "IsHeroManageable"), (Recruiter, "IsGarrisonManageable"), (Recruiter, "ClearSettlementCache"), ("ROT.SubModule", "Recruiter"),
            ("ROT.Misc.ROTClans", "WhiteWalkers"), (Places, "TheWall"), (Places, "CastleBlack"), (Places, "KingsLanding"), (Places, "RedKeep"),
            ("ROT.Misc.ROTUtilities", "IsBeyondTheWall"), (Others2, "NightKing"), (Others2, "IsInvasionStarted"), (Others2, "ResolveSettlementHeir"),
            (Others2, "ChangeToOthersCulture"), (Others2, "RevertBackFromOthersCulture"), (Others2, "EvacuateHeroes"), (Others2, "ChangeNotableCulture"),
            (Others2, "FindNearestViableClan"), (Others2, "OnSettlementOwnerChanged"), (Others2, "OnHourlyTick"),
            (Others, "patch7"), (Others, "patch27"), ("ROT.HarmonyPatches.ModCompatibility.Diplomacy+DeclareWarActionPatch", "Prefix"),
        };
        Assert.All(used, pair => Assert.True(Has(pair.Type, pair.Member), pair.Type + "." + pair.Member + " is not in ROT.dll"));
        if (!File.Exists(RealmOfThronesCompatTests.CompatDll))
            return;
        using var module = Compat();
        var strings = Strings(module, "RealmOfThronesCoopCompat.RotGuards");
        Assert.All(used, pair => Assert.True(strings.Contains(pair.Member), "RotGuards does not look up " + pair.Member));
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "BannerlordCoopLauncher.sln")))
            root = root.Parent;
        return root!.FullName;
    }
}
