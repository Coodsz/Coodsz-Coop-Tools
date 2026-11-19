using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat's host of Realm of Thrones' own code on the dedicated server: its level
// switch, the three tables and their lookups, which behaviors and models a level takes and in which
// order, the comparison of the tables with ROT.dll and its loud lines (RotHostRules.cs and
// RotHostTables.cs, linked). What needs ROT.dll and the server's DLLs is checked by the offline
// harness, not here.
public sealed class RealmOfThronesCompatHostTests
{
    private const string Behaviors = "ROT.CampaignBehaviors.";
    private const string Models = "ROT.Models.";
    private const string Others = "ROT.HarmonyPatches.Core.OthersPatches";

    [Theory]
    [InlineData(null, "Full", false)]
    [InlineData("", "Full", false)]
    [InlineData("  ", "Full", false)]
    [InlineData("off", "Off", false)]
    [InlineData("probe", "Probe", false)]
    [InlineData("patches", "Patches", false)]
    [InlineData(" Models ", "Models", false)]
    [InlineData("FULL", "Full", false)]
    [InlineData("on", "Off", true)]
    [InlineData("1", "Off", true)]
    [InlineData("model", "Off", true)]
    public void The_variable_picks_the_level_no_value_means_full_and_an_unknown_one_means_off(string? value, string level, bool unknown)
    {
        Assert.Equal(Enum.Parse<HostLevel>(level), RotHostRules.Level(value!, out var isUnknown));
        Assert.Equal(unknown, isUnknown);
    }

    [Fact]
    public void The_level_line_says_what_was_set_and_names_the_levels_for_an_unknown_value()
    {
        Assert.Equal("Host: level full (COODSZ_ROT_HOST is not set).", RotHostRules.LevelLine(null!));
        Assert.Equal("Host: level patches (COODSZ_ROT_HOST=patches).", RotHostRules.LevelLine(" patches "));
        Assert.Equal("Host: off. COODSZ_ROT_HOST=yes is not a level (off, probe, patches, models, full).", RotHostRules.LevelLine("yes"));
    }

    [Fact]
    public void Each_level_adds_one_step_to_the_one_before()
    {
        var levels = new[] { HostLevel.Off, HostLevel.Probe, HostLevel.Patches, HostLevel.Models, HostLevel.Full };
        Assert.Equal(new[] { false, false, true, true, true }, levels.Select(RotHostRules.LoadsRot));
        Assert.Equal(new[] { false, false, false, true, true }, levels.Select(RotHostRules.HostsModels));
        Assert.Equal(new[] { false, false, false, false, true }, levels.Select(RotHostRules.HostsBehaviors));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("true", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_jit_sweep_runs_only_when_its_variable_is_1(string? value, bool on)
    {
        Assert.Equal(on, RotHostRules.JitSweepOn(value!));
    }

    // The counts the lead was given. A row that moves between host, later and skip shows here.
    [Fact]
    public void The_tables_hold_every_row_with_its_answer_and_a_reason()
    {
        int Count<T>(IEnumerable<T> rows, Func<T, HostDecision> decision, HostDecision wanted) => rows.Count(row => decision(row) == wanted);

        Assert.Equal((33, 5, 52), (Count(RotHostTables.Patches, r => r.Decision, HostDecision.Host), Count(RotHostTables.Patches, r => r.Decision, HostDecision.Later), Count(RotHostTables.Patches, r => r.Decision, HostDecision.Skip)));
        Assert.Equal(62, RotHostTables.Patches.Select(row => row.Type).Distinct().Count());
        Assert.Equal((23, 0, 4), (Count(RotHostTables.ModelRows, r => r.Decision, HostDecision.Host), Count(RotHostTables.ModelRows, r => r.Decision, HostDecision.Later), Count(RotHostTables.ModelRows, r => r.Decision, HostDecision.Skip)));
        Assert.Equal((11, 8, 6), (Count(RotHostTables.BehaviorRows, r => r.Decision, HostDecision.Host), Count(RotHostTables.BehaviorRows, r => r.Decision, HostDecision.Later), Count(RotHostTables.BehaviorRows, r => r.Decision, HostDecision.Skip)));
        Assert.All(RotHostTables.Patches, row => Assert.False(string.IsNullOrWhiteSpace(row.Why), row.Type));
        Assert.All(RotHostTables.ModelRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Why), row.Type));
        Assert.All(RotHostTables.BehaviorRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Why), row.Type));
        Assert.Equal(new[] { Models + "ROTArmyManagementCalculationModel", Models + "ROTClanFinanceModel", Models + "ROTPartySpeedModel", Models + "ROTVoiceOverModel" },
            RotHostTables.ModelRows.Where(row => row.Decision == HostDecision.Skip).Select(row => row.Type).OrderBy(type => type, StringComparer.Ordinal));
    }

    // Never on the server: the patch that replaces the game's load step, the character creation
    // swap, menus, music, missions, dragons and enlistment.
    [Theory]
    [InlineData("ROT.RealmOfThronesGameModeManagerPatch")]
    [InlineData("ROT.HarmonyPatches.Core.CharacterCreationPatches")]
    [InlineData("ROT.HarmonyPatches.Core.CharacterCreationCultureFaceGen")]
    [InlineData("ROT.HarmonyPatches.Core.MBInitialScreenBasePatch")]
    [InlineData("ROT.HarmonyPatches.Core.MBMusicManagerPatches")]
    [InlineData("ROT.HarmonyPatches.Core.EnlistmentPatches")]
    [InlineData("ROT.HarmonyPatches.Core.WarSailsPatches")]
    [InlineData("ROT.HarmonyPatches.Core.ScenePatches")]
    [InlineData("ROT.HarmonyPatches.TimeLock.MissionPatch")]
    [InlineData("ROT.HarmonyPatches.Dragon.PartyCharacterVMPatch")]
    [InlineData("ROT.Dragon.AgentPatch")]
    [InlineData("RoT_Elephants.RoTElephantPatches+ElephantCameraPatches")]
    public void What_must_never_run_on_the_server_is_skipped_by_the_table(string type)
    {
        var row = Assert.Single(RotHostTables.Patches, r => r.Type == type);
        Assert.Equal(HostDecision.Skip, row.Decision);
        Assert.Null(row.Method);
    }

    [Fact]
    public void A_class_is_applied_whole_or_one_method_at_a_time_never_both()
    {
        var split = RotHostTables.Patches.Select(row => row.Type).Distinct().Where(type => RotHostRules.IsSplit(RotHostTables.Patches, type)).ToList();
        Assert.Equal(new[] { "ROT.HarmonyPatches.Core.ROTWeaponComponetData", Others }, split);
        Assert.All(split, type => Assert.Null(RotHostRules.ClassRow(RotHostTables.Patches, type)));
        Assert.All(RotHostTables.Patches.Where(row => row.Method != null), row => Assert.False(string.IsNullOrEmpty(row.Target), row.Type + "." + row.Method));
        Assert.Equal(HostDecision.Host, RotHostRules.ClassRow(RotHostTables.Patches, "ROT.HarmonyPatches.Core.XMLParserAddons").Decision);
        Assert.Null(RotHostRules.ClassRow(RotHostTables.Patches, "ROT.HarmonyPatches.Core.NotThere"));
        // Only the weapon data constructor of that class is item data; the rest is combat in a mission.
        Assert.Equal(new[] { "patch1" }, RotHostTables.Patches.Where(row => row.Type.EndsWith("ROTWeaponComponetData") && row.Decision == HostDecision.Host).Select(row => row.Method));
    }

    // OthersPatches has two patch methods named patch11. The game method tells them apart.
    [Fact]
    public void A_method_row_is_found_by_its_name_and_the_game_method_it_patches()
    {
        const string ransom = "TaleWorlds.CampaignSystem.CampaignBehaviors.RansomOfferCampaignBehavior.ConsiderRansomPrisoner(Hero)";
        const string execution = "TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByExecution(Hero, Hero, Boolean, Boolean)";

        Assert.Equal(HostDecision.Host, RotHostRules.MethodRow(RotHostTables.Patches, Others, "patch11", ransom).Decision);
        Assert.Equal(HostDecision.Later, RotHostRules.MethodRow(RotHostTables.Patches, Others, "patch11", execution).Decision);
        Assert.Null(RotHostRules.MethodRow(RotHostTables.Patches, Others, "patch12", ransom));
        Assert.Null(RotHostRules.MethodRow(RotHostTables.Patches, Others, "patch99", ransom));
        Assert.Null(RotHostRules.MethodRow(RotHostTables.Patches, "ROT.HarmonyPatches.Core.XMLParserAddons", "RelevantSkill", "TaleWorlds.Core.ItemObject.get_RelevantSkill()"));
    }

    // ROT.SubModule patches GameTexts.FindText by hand before its PatchAll and the rest after it.
    [Fact]
    public void The_patches_by_hand_keep_their_place_before_and_after_the_classes()
    {
        var before = Assert.Single(RotHostRules.HandRows(RotHostTables.Patches, beforeClasses: true));
        var after = Assert.Single(RotHostRules.HandRows(RotHostTables.Patches, beforeClasses: false));
        Assert.Equal(("FindTextPrefix", "TaleWorlds.Core.GameTexts.FindText(String, String)"), (before.Method, before.Target));
        Assert.Equal("GetDistancePrefix", after.Method);

        Assert.True(RotHostRules.SplitTarget(after.Target, out var type, out var method));
        Assert.Equal(("NavalDLC.GameComponents.NavalDLCMapDistanceModel", "GetDistance"), (type, method));
        Assert.False(RotHostRules.SplitTarget("NoMethod", out _, out _));
        Assert.False(RotHostRules.SplitTarget("Type.(String)", out _, out _));
        Assert.False(RotHostRules.SplitTarget(null!, out _, out _));
    }

    // Realm of Thrones reads the static fields of ROT.SubModule without null checks, so their
    // objects are made at every hosting level. The game only saves registered behaviors, so one
    // that is not hosted but stores data is registered with its events off.
    [Fact]
    public void A_level_makes_the_field_objects_and_full_also_registers_the_hosted_and_the_inert_behaviors()
    {
        var fields = new[] { "StorylineWars", "DuelsBehavior", "RavenBehavior", "EventBehavior", "TownTradersBehavior", "CoreBehavior", "Recruiter", "DragonBehavior", "EnlistmentBehavior" };

        Assert.Empty(RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, HostLevel.Off));
        Assert.Empty(RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, HostLevel.Probe));
        Assert.Equal(fields, RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, HostLevel.Patches).Select(row => row.Field));
        Assert.Equal(fields, RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, HostLevel.Models).Select(row => row.Field));
        Assert.DoesNotContain(RotHostTables.BehaviorRows, row => RotHostRules.Registers(row, HostLevel.Models));

        var made = RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, HostLevel.Full);
        Assert.Equal(18, made.Count);
        Assert.All(made, row => Assert.True(RotHostRules.Registers(row, HostLevel.Full)));
        Assert.Equal(new[] { "ROTStorylineWars", "ROTEventBehavior", "ROTSpawnUniqueWanderers", "ROTTradeBoundBehavior", "ROTCoreBehavior", "ROTRaceMap", "ROTTroopRecruiter",
                "ROTDragonBehavior", "ROTOthersCampaignBehavior", "ROTBanditLeaveHideouts", "ROTRelationshipsBehavior" },
            made.Where(row => row.Decision == HostDecision.Host).Select(row => row.Type.Substring(Behaviors.Length)));
        Assert.Equal(7, made.Count(row => row.Inert));
        // Realm of Thrones' own order: the storyline wars first, the relationships last.
        Assert.Equal((Behaviors + "ROTStorylineWars", Behaviors + "ROTRelationshipsBehavior"), (made[0].Type, made[^1].Type));
        Assert.Equal(new[] { "Duels", "Ravens" }, RotHostTables.BehaviorRows.Where(row => row.Setting != null).Select(row => row.Setting));
    }

    [Fact]
    public void An_inert_behavior_is_never_a_hosted_one_and_every_field_object_is_registered_at_full()
    {
        Assert.DoesNotContain(RotHostTables.BehaviorRows, row => row.Inert && row.Decision == HostDecision.Host);
        Assert.All(RotHostTables.BehaviorRows.Where(row => row.Field != null), row => Assert.True(RotHostRules.Registers(row, HostLevel.Full), row.Type));
        // Neither hosted nor inert: not made at all.
        Assert.Equal(new[] { Behaviors + "ValyrianThiefCampaignBehavior", Behaviors + "ROTClanRecruitBehavior", Behaviors + "ROTHouseTroopsBehavior", Behaviors + "ROTEnlistmentMenuBehavior",
                Behaviors + "ROTSendToWallBehavior", Behaviors + "ROTRBMCompatibility", "ROT.ROTCharacterCreationCampaignBehavior" },
            RotHostTables.BehaviorRows.Where(row => !RotHostRules.Registers(row, HostLevel.Full)).Select(row => row.Type));
    }

    // The order decides which model wraps which: Realm of Thrones registers two models for the
    // militia slot and two for the agent stats slot, and the later one wraps the earlier one.
    [Fact]
    public void Models_are_hosted_in_realm_of_thrones_order_and_the_last_one_of_a_slot_is_on_top()
    {
        Assert.Empty(RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Patches, null!));
        var hosted = RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Models, Array.Empty<string>()).Select(row => row.Type).ToList();

        Assert.Equal(22, hosted.Count);
        Assert.Equal((Models + "ROTKingdomDecisionPermissionModel", "RoT_Elephants.RoTElephantAgentStatCalculateModel"), (hosted[0], hosted[^1]));
        Assert.True(hosted.IndexOf(Models + "ROTSettlementMilitiaModel") < hosted.IndexOf(Models + "ROTEasyCastleMilitiaModel"));
        Assert.True(hosted.IndexOf(Models + "ROTAgentStatCalculateModel") < hosted.IndexOf("RoT_Elephants.RoTElephantAgentStatCalculateModel"));
        Assert.Equal(hosted, RotHostTables.ModelRows.Select(row => row.Type).Where(hosted.Contains));

        var top = RotHostRules.TopModels(RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, Array.Empty<string>()));
        Assert.Equal(20, top.Count);
        Assert.Equal(Models + "ROTEasyCastleMilitiaModel", top["SettlementMilitiaModel"]);
        Assert.Equal("RoT_Elephants.RoTElephantAgentStatCalculateModel", top["AgentStatCalculateModel"]);
        Assert.Equal(Models + "ROTCampaignTimeModel", top["CampaignTimeModel"]);
        // The slots of the skipped models keep the game's or War Sails' model.
        Assert.All(new[] { "ClanFinanceModel", "PartySpeedModel", "ArmyManagementCalculationModel", "VoiceOverModel", "PartySizeLimitModel" }, slot => Assert.False(top.ContainsKey(slot), slot));
    }

    // Coop's clan finance casts the model under the top ClanFinanceModel to the vanilla one, which
    // fails with Realm of Thrones' wrapper over War Sails' model, so that wrapper is never hosted.
    [Fact]
    public void The_clan_finance_wrapper_is_never_hosted()
    {
        var row = Assert.Single(RotHostTables.ModelRows, r => r.Type == Models + "ROTClanFinanceModel");
        Assert.Equal(HostDecision.Skip, row.Decision);
        Assert.DoesNotContain(row, RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, RotHostTables.BehaviorRows.Select(b => b.Type).ToList()));
    }

    // ROTPartySizeLimitModel asks the campaign for ROTOthersCampaignBehavior and uses the answer
    // without a null check, and its rule is that behavior's work. Registered inert is not enough.
    [Fact]
    public void The_party_size_model_waits_until_the_white_walker_behavior_is_hosted()
    {
        const string model = Models + "ROTPartySizeLimitModel";
        const string behavior = Behaviors + "ROTOthersCampaignBehavior";
        // The behaviors hosted at full, but for this one (RealmOfThronesGuardsTests has it hosted).
        var atFull = RotHostTables.BehaviorRows.Where(row => RotHostRules.Registers(row, HostLevel.Full) && row.Decision == HostDecision.Host && row.Type != behavior).Select(row => row.Type).ToList();

        Assert.Equal(behavior, Assert.Single(RotHostTables.ModelRows, row => row.Needs != null).Needs);
        Assert.DoesNotContain(RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, atFull), row => row.Type == model);
        Assert.DoesNotContain(RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Models, null!), row => row.Type == model);
        var withBehavior = RotHostRules.ModelsToHost(RotHostTables.ModelRows, HostLevel.Full, new[] { behavior });
        Assert.Contains(withBehavior, row => row.Type == model);
        Assert.Equal(23, withBehavior.Count);
    }

    [Fact]
    public void The_models_that_test_the_one_local_player_are_marked_for_a_player_check()
    {
        Assert.Equal(new[] { "ROTKingdomDecisionPermissionModel", "ROTCombatSimulationModel", "ROTEasyCastleMilitiaModel", "ROTPartyNavigationModel", "ROTMarriageModel", "ROTTroopUpgradeModel" },
            RotHostTables.ModelRows.Where(row => row.PlayerCheck != null).Select(row => row.Type.Substring(Models.Length)));
        Assert.All(RotHostTables.ModelRows.Where(row => row.PlayerCheck != null), row => Assert.Equal(HostDecision.Host, row.Decision));
    }

    private static List<string> TableBehaviors => RotHostTables.BehaviorRows.Select(row => row.Type).ToList();
    private static List<string> TableModels => RotHostTables.ModelRows.Select(row => row.Type).ToList();
    private static List<string> TablePatchClasses => RotHostTables.Patches.Where(row => !row.ByHand).Select(row => row.Type).Distinct().ToList();
    private static List<string> TableTypes => TableBehaviors.Concat(TableModels).Concat(RotHostTables.Patches.Select(row => row.Type)).Distinct().ToList();

    [Fact]
    public void A_rot_dll_that_holds_exactly_the_tables_types_has_no_difference()
    {
        var differences = RotHostRules.Drift(TableBehaviors, TableModels, TablePatchClasses, TableTypes, "v8.1.8.0");

        Assert.Empty(differences);
        Assert.Equal("Host: ROT.dll matches the tables (25 behaviors, 27 models, 58 patch classes) and ROT-Core is v8.1.8.",
            RotHostRules.DriftLine(differences, TableBehaviors.Count, TableModels.Count, TablePatchClasses.Count));
    }

    [Fact]
    public void Every_difference_between_rot_dll_and_the_tables_is_named_in_one_loud_line()
    {
        var behaviors = TableBehaviors.Append("ROT.CampaignBehaviors.ROTNewBehavior").ToList();
        var models = TableModels.Where(type => type != Models + "ROTAgeModel").ToList();
        var patchClasses = TablePatchClasses.Append("ROT.HarmonyPatches.Core.NewPatch").ToList();
        var all = TableTypes.Where(type => type != "ROT.Dragon.AgentPatch").ToList();

        var differences = RotHostRules.Drift(behaviors, models, patchClasses, all, "v8.2.0.0");

        Assert.Equal(new[]
        {
            "ROT-Core is v8.2.0.0, not v8.1.8",
            "behavior ROT.CampaignBehaviors.ROTNewBehavior is not in the table",
            "model ROT.Models.ROTAgeModel of the table is not in ROT.dll",
            "patch class ROT.HarmonyPatches.Core.NewPatch is not in the table",
            "patch class ROT.Dragon.AgentPatch of the table is not in ROT.dll",
        }, differences);
        var line = RotHostRules.DriftLine(differences, 26, 26, 59);
        Assert.StartsWith("Host: !!! WARNING, THIS REALM OF THRONES DIFFERS FROM THE TABLES BUILT FOR v8.1.8 (5): ROT-Core is v8.2.0.0, not v8.1.8; behavior ", line);
        Assert.EndsWith("Hosting goes on with the tables; what they do not name is not hosted.", line);
    }

    [Theory]
    [InlineData("v8.1.8", true)]
    [InlineData("v8.1.8.0", true)]
    [InlineData("v8.1.80", false)]
    [InlineData("v8.1.9.0", false)]
    [InlineData("8.1.8", false)]
    [InlineData(null, false)]
    public void Only_rot_core_8_1_8_is_the_version_the_tables_were_built_for(string? version, bool expected)
    {
        Assert.Equal(expected, RotHostRules.IsExpectedCoreVersion(version!));
    }

    // The runtime looks next to ROT.dll before it asks the module's resolver.
    [Fact]
    public void A_copy_of_a_served_library_next_to_rot_dll_is_found()
    {
        var folder = @"C:\srv\engine\Modules\ROT-Core\bin\Win64_Shipping_Client\";
        Assert.Empty(RotHostRules.StrayLibraries(new[] { folder + "ROT.dll", folder + "BannerColorPersistence.dll", folder + "Newtonsoft.Json.dll", folder + "MCMv5.pdb" }));
        Assert.Equal(new[] { "mcmv5.dll", "Bannerlord.UIExtenderEx.dll" }, RotHostRules.StrayLibraries(new[] { folder + "ROT.dll", folder + "mcmv5.dll", folder + "Bannerlord.UIExtenderEx.dll" }));
        Assert.Empty(RotHostRules.StrayLibraries(null!));
        Assert.True(RotHostRules.IsLibraryAssembly("MCMv5") && RotHostRules.IsLibraryAssembly("bannerlord.uiextenderex"));
        Assert.False(RotHostRules.IsLibraryAssembly("ROT") || RotHostRules.IsLibraryAssembly("0Harmony") || RotHostRules.IsLibraryAssembly(null!));
    }

    // A world made while Realm of Thrones' time model was not registered starts in the game's own year.
    [Theory]
    [InlineData(299, true, true)]
    [InlineData(1084, true, false)]
    [InlineData(300, true, false)]
    [InlineData(299, false, true)]
    [InlineData(312, false, true)]
    [InlineData(298, false, false)]
    [InlineData(1084, false, false)]
    [InlineData(1090, false, false)]
    public void A_world_outside_realm_of_thrones_calendar_gets_a_loud_line(int year, bool newWorld, bool rotWorld)
    {
        var line = RotHostRules.WorldYearLine(year, newWorld);

        Assert.Equal(rotWorld, RotHostRules.IsRotYear(year, newWorld));
        Assert.Equal(!rotWorld, line.StartsWith("Host: !!! THIS " + (newWorld ? "NEW" : "LOADED") + " WORLD IS IN THE YEAR " + year + ".", StringComparison.Ordinal));
        Assert.Equal(!rotWorld, line.EndsWith("DO NOT KEEP ITS SAVE.", StringComparison.Ordinal));
        if (rotWorld)
            Assert.Equal("Host: the " + (newWorld ? "new" : "loaded") + " world is in the year " + year + " of Realm of Thrones' calendar.", line);
    }

    // The SubModule calls the host from its lifecycle methods before anything of Harmony is
    // certain to be there, so the host's entry class, its checks, rules and tables may not name
    // anything from 0Harmony; only RotHostPatches does. Reads the DLL Build.cmd builds.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_compiled_host_names_harmony_only_in_its_patch_class_and_the_submodule_calls_it_from_five_hooks()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);
        bool FromHarmony(Mono.Cecil.TypeReference? type)
        {
            while (type is Mono.Cecil.TypeSpecification spec)
            {
                if (spec is Mono.Cecil.GenericInstanceType generic && generic.GenericArguments.Any(FromHarmony))
                    return true;
                type = spec.ElementType;
            }
            return type != null && type.Scope is Mono.Cecil.AssemblyNameReference scope && scope.Name == "0Harmony";
        }
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        IEnumerable<Mono.Cecil.TypeReference?> Named(Mono.Cecil.TypeDefinition type)
        {
            yield return type.BaseType;
            foreach (var field in type.Fields)
                yield return field.FieldType;
            foreach (var method in type.Methods)
            {
                yield return method.ReturnType;
                foreach (var parameter in method.Parameters)
                    yield return parameter.ParameterType;
                if (!method.HasBody)
                    continue;
                foreach (var variable in method.Body.Variables)
                    yield return variable.VariableType;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is Mono.Cecil.TypeReference operandType)
                        yield return operandType;
                    if (instruction.Operand is Mono.Cecil.MemberReference member)
                        yield return member.DeclaringType;
                    if (instruction.Operand is Mono.Cecil.MethodReference called)
                    {
                        yield return called.ReturnType;
                        foreach (var parameter in called.Parameters)
                            yield return parameter.ParameterType;
                    }
                    if (instruction.Operand is Mono.Cecil.FieldReference read)
                        yield return read.FieldType;
                }
            }
        }
        bool NamesHarmony(string name) => WithNested(module.GetType("RealmOfThronesCoopCompat." + name)).SelectMany(Named).Any(FromHarmony);

        Assert.All(new[] { "RotHost", "RotHostChecks", "RotHostRules", "RotHostTables" }, name => Assert.False(NamesHarmony(name), name));
        // The check itself sees Harmony where it is used.
        Assert.True(NamesHarmony("RotHostPatches"));

        IEnumerable<string> Calls(Mono.Cecil.TypeDefinition type, string method) => WithNested(type).SelectMany(t => t.Methods)
            .Where(m => m.HasBody && (m.Name == method || m.Name.Contains("<" + method + ">")))
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<Mono.Cecil.MethodReference>().Select(m => m.DeclaringType.Name + "." + m.Name);
        var subModule = module.GetType("RealmOfThronesCoopCompat.SubModule");
        Assert.All(new[] { "OnSubModuleLoad", "OnApplicationTick", "OnGameStart", "OnGameInitializationFinished", "OnAfterGameInitializationFinished" },
            hook => Assert.Contains("RotHost." + hook, Calls(subModule, hook)));
    }
}
