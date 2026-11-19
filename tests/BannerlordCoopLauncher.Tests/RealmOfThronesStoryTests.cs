using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat's story on the dedicated server: the COODSZ_ROT_STORY switch,
// ROTEventBehavior's row in the host's table, the table of every story event with its answer and
// player branches, the comparison with ROT.dll's event types and the log lines (RotStoryRules.cs,
// linked). That every event type and patched method exists and compiles on the server's DLLs is
// checked by the offline harness, not here.
public sealed class RealmOfThronesStoryTests
{
    private const string EventBehavior = "ROT.CampaignBehaviors.ROTEventBehavior";

    // The 29 events ROTEventBehavior.OnSessionLaunched adds, in its order (ROT-Core v8.1.8),
    // copied by hand: these tests do not read ROT.dll, so an event a Realm of Thrones update adds
    // is seen by the game start line (RotStoryRules.Compare against ROT.dll) and the offline
    // harness, not here.
    private static readonly string[] RotEvents =
    {
        "NedEvent", "RenlyDeathEvent", "StannisDeathEvent", "BlackwaterSiegeNotificationEvent", "BlackwaterSiegeEvent", "BlackwaterEvent", "RedWeddingEvent",
        "RooseDeathEvent", "JoffreyEvent", "EastwatchEvent", "DanyInvasionEvent", "AegonInvasionEvent", "RickardKarstarkExecutionEvent", "RiverlandsDeclareWarEvent",
        "JeorMutinyEvent", "NightKingDeathEvent", "FistEvent", "KingsLandingEvent", "WallSiegeNotificationEvent", "WallSiegeEvent", "WallEvent",
        "BastardsSiegeNotificationEvent", "BastardsSiegeEvent", "BastardsEvent", "RamsayEvent", "JonEvent", "HarrenhalSiegeNotificationEvent", "HarrenhalSiegeEvent", "HarrenhalEvent",
    };

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData(" on ", true, false)]
    [InlineData("ON", true, false)]
    [InlineData("off", false, false)]
    [InlineData(" Off ", false, false)]
    [InlineData("0", false, true)]
    [InlineData("full", false, true)]
    public void The_story_is_on_unless_the_variable_says_off_and_an_unknown_value_means_off(string? value, bool on, bool unknown)
    {
        Assert.Equal(on, RotStoryRules.On(value!, out var isUnknown));
        Assert.Equal(unknown, isUnknown);
    }

    // RotStory counts its failed hooks with the host's, next to the server guards'.
    [Fact]
    public void A_failed_story_hook_is_loud_and_holds_a_new_world_at_full()
    {
        var failures = RotHostRules.FailureList(0, 0, 0, 0, 0, 1, 3);
        var joined = string.Join("; ", failures);

        Assert.Equal(new[] { "1 server guards failed, so the rules they guard do not run", "3 story hooks failed, so the story may stand still or run a step made for one player" }, failures);
        Assert.StartsWith("Host: !!! THE HOST IS NOT COMPLETE: 1 server guards failed", RotHostRules.FailureLine(failures));
        Assert.Equal("the host is not complete (" + joined + ")", RotHostRules.WorldRefusal(HostLevel.Full, false, HostLevel.Full, true, null!, joined));
        Assert.Equal("3 story hooks failed, so the story may stand still or run a step made for one player", Assert.Single(RotHostRules.FailureList(0, 0, 0, 0, 0, story: 3)));
    }

    [Fact]
    public void The_switch_line_says_what_was_set()
    {
        Assert.StartsWith("Story: on (COODSZ_ROT_STORY is not set): ", RotStoryRules.SwitchLine(null!));
        Assert.Equal("Story: off (COODSZ_ROT_STORY=off): ROTEventBehavior stays registered with its events off, so the story stands still.", RotStoryRules.SwitchLine(" off "));
        Assert.Equal("Story: off. COODSZ_ROT_STORY=yes is neither on nor off, so ROTEventBehavior stays registered with its events off.", RotStoryRules.SwitchLine("yes"));
    }

    // With the story on the host registers ROTEventBehavior with its events on; with it off the
    // behavior stays registered with its events blocked, for its saved data, as before.
    [Fact]
    public void The_event_behavior_is_hosted_with_the_story_on_and_kept_inert_with_it_off()
    {
        var row = Assert.Single(RotHostTables.BehaviorRows, r => r.Type == EventBehavior);

        Assert.Equal(RotStoryRules.On(Environment.GetEnvironmentVariable(RotStoryRules.Variable), out _), RotStoryRules.StoryOn);
        Assert.Equal(RotStoryRules.Decision(RotStoryRules.StoryOn), row.Decision);
        Assert.Equal(!RotStoryRules.StoryOn, row.Inert);
        Assert.Equal("EventBehavior", row.Field);
        Assert.True(RotHostRules.Registers(row, HostLevel.Full));
        Assert.False(RotHostRules.Registers(row, HostLevel.Models));

        Assert.Equal(HostDecision.Host, RotStoryRules.Decision(true));
        Assert.Equal(HostDecision.Later, RotStoryRules.Decision(false));
        Assert.Contains("RotStory", RotStoryRules.Why(true));
        Assert.StartsWith("COODSZ_ROT_STORY=off: ", RotStoryRules.Why(false));
        if (Environment.GetEnvironmentVariable(RotStoryRules.Variable) == null)
            Assert.Equal((HostDecision.Host, false), (row.Decision, row.Inert));
    }

    [Fact]
    public void Every_event_of_realm_of_thrones_is_in_the_table_in_its_order_with_an_answer()
    {
        Assert.Equal(RotEvents, RotStoryRules.Events.Select(row => row.Type));
        Assert.All(RotStoryRules.Events, row => Assert.False(string.IsNullOrWhiteSpace(row.Does), row.Type));
        Assert.Equal(new[] { "BlackwaterSiegeEvent", "BlackwaterEvent", "KingsLandingEvent", "WallSiegeEvent", "BastardsSiegeEvent", "BastardsEvent", "HarrenhalSiegeEvent" },
            RotStoryRules.Events.Where(row => row.Answer == StoryAnswer.Continue).Select(row => row.Type));
        Assert.Equal(new[] { "RenlyDeathEvent", "StannisDeathEvent", "BlackwaterSiegeEvent", "RedWeddingEvent", "DanyInvasionEvent", "AegonInvasionEvent", "KingsLandingEvent",
                "WallSiegeEvent", "BastardsSiegeEvent", "HarrenhalSiegeEvent" },
            RotStoryRules.Events.Where(row => row.Players != null).Select(row => row.Type));
        // Each player branch says which way it reads on the server.
        Assert.All(RotStoryRules.Events.Where(row => row.Players != null), row => Assert.Matches("^(any player|no player): ", row.Players));
    }

    [Fact]
    public void The_patched_events_are_events_of_the_table_and_the_sieges_arm_with_continue()
    {
        var names = RotStoryRules.Events.Select(row => row.Type).ToList();

        Assert.All(RotStoryRules.Sieges, siege => Assert.Equal(StoryAnswer.Continue, Assert.Single(RotStoryRules.Events, row => row.Type == siege).Answer));
        Assert.All(RotStoryRules.Sieges, siege => Assert.StartsWith("no player: ", RotStoryRules.Events.Single(row => row.Type == siege).Players));
        Assert.All(RotStoryRules.Invasions.Concat(RotStoryRules.Successions.Select(s => s.Type)), name => Assert.Contains(name, names));
        Assert.All(RotStoryRules.Invasions.Concat(RotStoryRules.Successions.Select(s => s.Type)),
            name => Assert.StartsWith("any player: ", RotStoryRules.Events.Single(row => row.Type == name).Players));
        Assert.Equal(new[] { ("RenlyDeathEvent", "ExecuteRenly", "Stormlands"), ("StannisDeathEvent", "StartEvent", "Dragonstone") }, RotStoryRules.Successions);
    }

    [Fact]
    public void A_rot_dll_with_exactly_the_tables_events_has_no_difference_and_a_new_event_is_named()
    {
        var all = RotEvents.Select(name => "ROT.Events." + name).ToList();

        Assert.Empty(RotStoryRules.Compare(all));
        Assert.Equal(new[] { "event ROT.Events.DragonEvent is not in the story table", "event ROT.Events.FistEvent of the story table is not in ROT.dll" },
            RotStoryRules.Compare(all.Where(name => name != "ROT.Events.FistEvent").Append("ROT.Events.DragonEvent")));
        Assert.Equal(29, RotStoryRules.Compare(null!).Count);
        Assert.Equal("RenlyDeathEvent", RotStoryRules.Row("ROT.Events.RenlyDeathEvent")!.Type);
        Assert.Null(RotStoryRules.Row("ROT.Events.DragonEvent"));
        Assert.Null(RotStoryRules.Row("Other.RenlyDeathEvent"));
        Assert.Null(RotStoryRules.Row(null!));
    }

    [Fact]
    public void The_game_start_line_counts_the_table_and_the_world_and_a_difference_is_loud()
    {
        Assert.Equal("Story: 29 events known, 7 with a default answer, 22 informational, 10 with player branches; ROT.dll has 29 event types, all in the table; "
            + "this world lists 29 events (3 done, 1 active).", RotStoryRules.SummaryLine(new List<string>(), 29, 29, 3, 1));

        var loud = RotStoryRules.SummaryLine(new[] { "event ROT.Events.DragonEvent is not in the story table" }, 30, 0, 0, 0);
        Assert.StartsWith("Story: !!! 29 events known, ", loud);
        Assert.Contains("ROT.dll HAS 30 EVENT TYPES AND DIFFERS FROM THE TABLE (1): event ROT.Events.DragonEvent is not in the story table. ", loud);
        Assert.EndsWith("this world lists 0 events (0 done, 0 active).", loud);
    }

    [Fact]
    public void Each_answered_popup_gets_one_line_with_its_answer()
    {
        var siege = RotStoryRules.Row("ROT.Events.HarrenhalSiegeEvent");
        var fist = RotStoryRules.Row("ROT.Events.FistEvent");

        Assert.Equal("Story: event HarrenhalSiegeEvent started (\"Roose Bolton Besieges Harrenhal\"), answer Continue applied: "
            + "Amory Lorch and Polliver hold Harrenhal and Roose Bolton's army forms at the next hourly tick.",
            RotStoryRules.PopupLine("HarrenhalSiegeEvent", 1, " Roose Bolton Besieges Harrenhal ", siege!));
        Assert.Equal("Story: event FistEvent started, informational popup logged and closed (a popup and music only).", RotStoryRules.PopupLine("FistEvent", 1, null!, fist!));
        Assert.StartsWith("Story: event RenlyDeathEvent popup 2, informational popup logged and closed (", RotStoryRules.PopupLine("RenlyDeathEvent", 2, "", RotStoryRules.Row("ROT.Events.RenlyDeathEvent")!));
        Assert.Equal("Story: !!! event DragonEvent started (\"Dragons\") is not in the story table (another Realm of Thrones version?): answer Continue applied, as for every popup.",
            RotStoryRules.PopupLine("DragonEvent", 1, "Dragons", null!));
    }

    // SubModule installs the story right after the host loaded ROT.dll, and every Harmony hook of
    // the story catches what it throws. Reads the DLL Build.cmd builds.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_compiled_story_is_installed_after_the_host_and_its_hooks_catch_everything()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);
        var subModule = module.GetType("RealmOfThronesCoopCompat.SubModule");
        var calls = new[] { subModule }.Concat(subModule.NestedTypes).SelectMany(t => t.Methods)
            .Where(m => m.HasBody && (m.Name == "OnSubModuleLoad" || m.Name.Contains("<OnSubModuleLoad>")))
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<Mono.Cecil.MethodReference>().Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
        Assert.Contains("RotStory.Install", calls);
        Assert.True(calls.IndexOf("RotHost.OnSubModuleLoad") < calls.IndexOf("RotStory.Install"));

        var story = module.GetType("RealmOfThronesCoopCompat.RotStory");
        Assert.All(new[] { "OpenPopupPrefix", "ReceiveItemsPrefix", "SkipMusic", "PullInPrefix", "PlayerKingPrefix", "SuccessionPrefix", "SessionPostfix", "Drain", "LeftOutOfMuster", "Answer",
                "WatchSieges", "BlockEvents" },
            name => Assert.True(story.Methods.Single(m => m.Name == name).Body.ExceptionHandlers.Count > 0, name));
        // King's Landing's reward screen is skipped, a succession without a main hero can be
        // skipped, the ticks are blocked when the guard comes too late, and a new world drops the
        // popups the last one queued.
        Assert.Equal("Helpers.InventoryScreenHelper", RotStoryRules.ReceiveItemsType);
        Assert.Contains(new[] { story }.Concat(story.NestedTypes).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions),
            i => i.Operand as string == "OpenScreenAsReceiveItems");
        Assert.Equal("System.Boolean", story.Methods.Single(m => m.Name == "SuccessionPrefix").ReturnType.FullName);
        Assert.Contains(story.Methods.Single(m => m.Name == "BlockEvents").Body.Instructions, i => i.Operand as string == "OnDailyTick");
        Assert.Contains(story.Methods.Single(m => m.Name == "SessionPostfix").Body.Instructions, i => (i.Operand as Mono.Cecil.FieldReference)?.Name == "Pending");
        // The Night King's death starts its event: the guard's collapse asks the story, which
        // calls ROTEventBehavior.TriggerEvent only with its popup guard and music skip in. The
        // story test hook starts its event the same way, from the popups' server tick.
        var trigger = story.Methods.Single(m => m.Name == "Trigger");
        Assert.Contains(trigger.Body.Instructions, i => i.Operand as string == "TriggerEvent");
        Assert.Contains(trigger.Body.Instructions, i => i.Operand as string == "EventBehavior");
        foreach (var name in new[] { "StartNightKingDeath", "FireTestEvent" })
        {
            var start = story.Methods.Single(m => m.Name == name);
            Assert.True(start.Body.ExceptionHandlers.Count > 0, name);
            Assert.Contains(start.Body.Instructions, i => (i.Operand as Mono.Cecil.FieldReference)?.Name == "_startsEvents");
            Assert.Contains(start.Body.Instructions, i => (i.Operand as Mono.Cecil.MethodReference)?.Name == "Trigger");
        }
        Assert.Contains(module.GetType("RealmOfThronesCoopCompat.RotStoryPopups").Methods.Single(m => m.Name == "ServerTick").Body.Instructions,
            i => (i.Operand as Mono.Cecil.MethodReference)?.FullName.Contains("RotStory::FireTestEvent") == true);
        Assert.Contains(module.GetType("RealmOfThronesCoopCompat.RotGuardsPatches").Methods.Single(m => m.Name == "Collapse").Body.Instructions,
            i => (i.Operand as Mono.Cecil.MethodReference)?.FullName.Contains("RotStory::StartNightKingDeath") == true);
        var rules = module.GetType("RealmOfThronesCoopCompat.RotStoryRules");
        Assert.DoesNotContain(rules.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>(),
            member => member.DeclaringType?.Scope?.Name == "0Harmony");

        List<string> Calls(string name) => story.Methods.Single(m => m.Name == name).Body.Instructions.Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>()
            .Where(m => m.DeclaringType != null).Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
        // Borash stays with a player's clan that owns it: the transpiler swaps Clan.PlayerClan for
        // OwnerIfPlayer, which asks Coop.
        Assert.Contains(story.Methods.Single(m => m.Name == "BorashTranspiler").Body.Instructions, i => i.Operand as string == "OwnerIfPlayer");
        Assert.Contains(story.Methods.Single(m => m.Name == "BorashTranspiler").Body.Instructions, i => i.Operand as string == "PlayerClan");
        Assert.Contains("Coop.IsPlayerClan", Calls("OwnerIfPlayer"));
        Assert.Contains(new[] { story }.Concat(story.NestedTypes).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions), i => i.Operand as string == "Borash");
        // The muster leaves a party in a player's army out and never disbands a player's army.
        Assert.Contains("MobileParty.get_Army", Calls("LeftOutOfMuster"));
        Assert.Equal(2, Calls("LeftOutOfMuster").Count(call => call == "Coop.IsPlayerHero"));
        Assert.Contains(story.Methods.Single(m => m.Name == "MusterTranspiler").Body.Instructions, i => i.Operand as string == "DisbandUnlessPlayers");
        Assert.Contains("Coop.IsPlayerHero", Calls("DisbandUnlessPlayers"));
        Assert.Contains("DisbandArmyAction.ApplyByUnknownReason", Calls("DisbandUnlessPlayers"));
        Assert.Contains(new[] { story }.Concat(story.NestedTypes).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions), i => i.Operand as string == "MusterFinalizer");
    }

    // Borash: Realm of Thrones' own test spares it only when the single player's clan owns it.
    [Fact]
    public void Borash_stays_with_a_players_clan_as_it_does_with_the_single_player()
    {
        var dany = Assert.Single(RotStoryRules.Events, row => row.Type == RotStoryRules.BorashEvent);
        Assert.Contains("Borash", dany.Players);
        Assert.Contains("stays with a player's clan", dany.Players);
        Assert.Equal("ROT.Misc.ROTSettlements", RotStoryRules.SettlementsType);
    }

    // Every Realm of Thrones name the story looks up is in the real ROT.dll with the shape its
    // patches need: the transpilers' patterns once (the muster's IsMainParty test, its two army
    // disbands, Borash's owner test), so an update that moves them fails here and not in a game.
    [RealmOfThronesClientTests.RotDllFact]
    public void Every_name_the_story_uses_is_in_realm_of_thrones()
    {
        using var rot = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesClientTests.RotDll);
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        var types = rot.Types.SelectMany(WithNested).ToDictionary(type => type.FullName.Replace('/', '+'));
        Mono.Cecil.TypeDefinition Type(string name) => types.TryGetValue(name, out var type) ? type : throw new Xunit.Sdk.XunitException(name + " is not in ROT.dll");
        Mono.Cecil.MethodDefinition Method(string type, string name) => Assert.Single(Type(type).Methods, m => m.Name == name);
        int Count(Mono.Cecil.MethodDefinition method, string declaring, string name) => method.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference m
            && m.DeclaringType.FullName == declaring && m.Name == name);

        Assert.All(RotStoryRules.Events, row => Type(RotStoryRules.Namespace + row.Type));
        foreach (var name in new[] { "OpenPopup", "HandleDoneButton", "get_IsActive", "get_TotalEvents", "get_Title" })
            Method(RotStoryRules.BaseType, name);
        foreach (var name in new[] { "OnDailyTick", "OnHourlyTick", "OnTick", "OnSessionLaunched", "RegisterEvents", "TriggerEvent" })
            Method(RotStoryRules.BehaviorType, name);
        Assert.Contains(Type(RotStoryRules.BehaviorType).Fields, f => f.Name == "_events");
        Assert.Contains(Type("ROT.SubModule").Fields, f => f.IsStatic && f.Name == "EventBehavior");
        foreach (var siege in RotStoryRules.Sieges)
        {
            var muster = Method(RotStoryRules.Namespace + siege, "SetupSiegeAttackers");
            Assert.Equal(1, Count(muster, "TaleWorlds.CampaignSystem.Party.MobileParty", "get_IsMainParty"));
            Assert.Equal(2, Count(muster, "TaleWorlds.CampaignSystem.Actions.DisbandArmyAction", "ApplyByUnknownReason"));
            Method(RotStoryRules.Namespace + siege, "SetupPlayerEncounter");
            Assert.All(new[] { "Instance", "SiegeLeaderParty", "SiegeStarted" }, field => Assert.Contains(Type(RotStoryRules.Namespace + siege).Fields, f => f.Name == field));
        }
        Assert.All(RotStoryRules.Invasions, name => Assert.Equal("TaleWorlds.CampaignSystem.Clan", Method(RotStoryRules.Namespace + name, "IsPlayerKing").Parameters.Single().ParameterType.FullName));
        Assert.All(RotStoryRules.Successions, succession => Method(RotStoryRules.Namespace + succession.Type, succession.Method));
        Method(RotStoryRules.SettlementsType, "get_Borash");
        // The Borash step: Borash.OwnerClan, then Clan.PlayerClan, once in the invasion's day tick.
        var body = Method(RotStoryRules.Namespace + RotStoryRules.BorashEvent, "DailyTickEvent").Body.Instructions.ToList();
        Assert.Equal(1, Enumerable.Range(2, Math.Max(0, body.Count - 2)).Count(i => body[i].Operand is Mono.Cecil.MethodReference player && player.Name == "get_PlayerClan"
            && body[i - 1].Operand is Mono.Cecil.MethodReference owner && owner.Name == "get_OwnerClan"
            && body[i - 2].Operand is Mono.Cecil.MethodReference borash && borash.Name == "get_Borash"));
    }

    // The live test hook COODSZ_ROT_STORY_TEST: unset it does nothing and says nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void The_story_test_hook_is_inert_and_silent_when_unset(string? value)
    {
        Assert.Null(RotPopupRules.TestPlan(value!, "30"));
        Assert.Null(RotPopupRules.TestPlan(value!, null!));
    }

    // Only the question events (answer Continue) may be started; anything else is refused with one line.
    [Fact]
    public void The_story_test_hook_starts_only_a_question_event()
    {
        var questions = new[] { "BlackwaterSiegeEvent", "BlackwaterEvent", "KingsLandingEvent", "WallSiegeEvent", "BastardsSiegeEvent", "BastardsEvent", "HarrenhalSiegeEvent" };
        Assert.All(questions, name => Assert.Equal(name, RotPopupRules.TestPlan(" " + name + " ", null!)!.Value.Event));
        Assert.Equal("Story popups: test hook armed (COODSZ_ROT_STORY_TEST=KingsLandingEvent): the event starts once in 60 s, when no question is open. "
            + "Its real effects apply to this world.", RotPopupRules.TestPlan("KingsLandingEvent", null!)!.Value.Line);

        foreach (var refused in new[] { "NedEvent", "kingslandingevent", "ROT.Events.KingsLandingEvent", "NoSuchEvent" })
        {
            var plan = RotPopupRules.TestPlan(refused, "30")!.Value;
            Assert.Null(plan.Event);
            Assert.Equal("Story popups: !!! test hook refused: COODSZ_ROT_STORY_TEST=" + refused + " is not a story event with a question (" + string.Join(", ", questions)
                + "), so nothing fires.", plan.Line);
        }
    }

    [Theory]
    [InlineData(null, 60, "")]
    [InlineData("10", 10, "")]
    [InlineData(" 600 ", 600, "")]
    [InlineData("9", 60, " (COODSZ_ROT_STORY_TEST_DELAY=9 is not 10 to 600 seconds, so 60)")]
    [InlineData("601", 60, " (COODSZ_ROT_STORY_TEST_DELAY=601 is not 10 to 600 seconds, so 60)")]
    [InlineData("soon", 60, " (COODSZ_ROT_STORY_TEST_DELAY=soon is not 10 to 600 seconds, so 60)")]
    public void The_story_test_delay_is_10_to_600_seconds(string? delay, int seconds, string note)
    {
        var plan = RotPopupRules.TestPlan("HarrenhalSiegeEvent", delay!)!.Value;
        Assert.Equal(seconds, plan.DelaySeconds);
        Assert.Contains("the event starts once in " + seconds + " s" + note + ", when no question is open.", plan.Line);
    }

    [Fact]
    public void The_story_test_hook_says_what_trigger_event_returned()
    {
        Assert.Equal("Story popups: test hook fired BlackwaterEvent: TriggerEvent returned true (started).", RotPopupRules.TestFiredLine("BlackwaterEvent", true));
        Assert.Equal("Story popups: test hook fired BlackwaterEvent: TriggerEvent returned false (it already ran or cannot start now).", RotPopupRules.TestFiredLine("BlackwaterEvent", false));
        Assert.Equal("Story popups: !!! test hook could not fire BlackwaterEvent: TypeLoadException: x.", RotPopupRules.TestFailedLine("BlackwaterEvent", "TypeLoadException: x"));
    }
}
