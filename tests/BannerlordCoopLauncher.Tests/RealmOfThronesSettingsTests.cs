using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat, Realm of Thrones' settings in a Coop session: the one table the server
// and every player's PC answer from (SettingsRules, linked), the host's agreement with its pins and
// the wiring in the compiled module.
public sealed class RealmOfThronesSettingsTests
{
    // Every getter of ROT.ROTSettings in ROT-Core v8.1.8.
    private static readonly string[] RotSettingsGetters =
    {
        "MyLittleWarbandAllowCustomKingdomPartyTroops", "RBMIntegration", "Events", "DebuffedMilitias", "KingsProtectNightsWatch", "NightsWatchMayNotWar",
        "FreeFolkMayNotWar", "Duels", "AllowableClanTierDifference", "Ravens", "EnableRavenAccidents", "EnableRavenRestictions", "RavenFlightTime",
        "SendRavenGoldCost", "DragonDamageScaling", "LethalDragonDamage", "FriendlyFire", "DragonHealth", "GlobalXPBonus", "PartyRoleChangeCoolDown",
        "PlayerPlateEncumbrance", "RenownPerBattle", "RelationPerXBattles", "MinimumEnemyPartySize", "FormationOrderCallouts", "EnlistWithEnemiesCooldown",
        "GearLevelOffset", "CraftingStaminaPerBattle", "FormationMarker", "MarkerBob", "MarkerHeight", "MarkerSize", "MarkerColor", "MarkerBorderColor",
        "RelationshipPenaltiesForLeaving", "RelationshipRequirements", "AllowMultipleRepurchasesofGear", "EnlistmentQuestRequirements", "DanysMusic",
        "OthersInvasion", "OthersSieging", "OthersRaiding", "OthersNecromancyMultiplier", "OthersLordConversionRate", "OthersPartySizeLimitGrowth",
        "OthersNKCanDieFromStart", "OthersAIShackles", "ROTMusicTracks",
    };

    [Fact]
    public void The_table_covers_every_settings_getter_once_and_pins_the_first_coop_version()
    {
        var rot = SettingsRules.Rows.Where(row => row.Type == SettingsRules.SettingsType).Select(row => row.Name).ToList();
        Assert.Equal(RotSettingsGetters.OrderBy(name => name), rot.OrderBy(name => name));
        Assert.Equal(rot.Count, rot.Distinct().Count());

        var pinned = SettingsRules.Rows.Where(row => row.Kind == SettingsRules.Kind.Pinned).ToDictionary(row => row.Name, row => row.Value);
        Assert.Equal(new Dictionary<string, object>
        {
            ["Duels"] = false, ["Ravens"] = false, ["PlayerPlateEncumbrance"] = true, ["RBMIntegration"] = false,
            ["MyLittleWarbandAllowCustomKingdomPartyTroops"] = false, ["Events"] = true,
        }, pinned);

        // Only what one player sees or hears keeps the player's own value; nothing of theirs is patched.
        var own = SettingsRules.Rows.Where(row => row.Kind == SettingsRules.Kind.PlayersOwn).ToList();
        Assert.Equal(new[] { "ROTMusicTracks", "DanysMusic", "FormationOrderCallouts", "FormationMarker", "MarkerBob", "MarkerHeight", "MarkerSize", "MarkerColor", "MarkerBorderColor" },
            own.Select(row => row.Name));
        Assert.All(own, row => Assert.Null(row.Value));
        Assert.All(own, row => Assert.False(row.Patched));

        // 39 ROTSettings getters and the elephant AI's attack chance, each with a value of the
        // getter's own type (RelationPerXBattles is a float getter over an int setting).
        var patched = SettingsRules.PatchedRows.ToList();
        Assert.Equal(40, patched.Count);
        Assert.All(patched, row => Assert.Contains(row.Value!.GetType(), new[] { typeof(bool), typeof(int), typeof(float) }));
        Assert.IsType<float>(patched.Single(row => row.Name == "RelationPerXBattles").Value);
        Assert.Equal(0.5f, patched.Single(row => row.Type == SettingsRules.ElephantSettingsType && row.Name == "elephantAttackChance").Value);
        // Realm of Thrones' own defaults for the world rules every PC must share.
        Assert.Equal(3f, patched.Single(row => row.Name == "OthersPartySizeLimitGrowth").Value);
        Assert.Equal(5000, patched.Single(row => row.Name == "DragonHealth").Value);
        Assert.Equal(-7, patched.Single(row => row.Name == "GearLevelOffset").Value);
        Assert.Equal(true, patched.Single(row => row.Name == "NightsWatchMayNotWar").Value);
    }

    // On the server the host makes Realm of Thrones' behaviors under ROTSettings switches. Each
    // switch it reads must be pinned off by the table, so the host and every player's PC agree
    // that no duel and no raven behavior exists in a co-op campaign.
    [Fact]
    public void The_host_makes_no_behavior_the_table_pins_off()
    {
        var switched = RotHostTables.BehaviorRows.Where(row => row.Setting != null).ToList();
        Assert.Equal(new[] { "ROT.CampaignBehaviors.ROTDuelsBehavior", "ROT.CampaignBehaviors.ROTRavenBehavior" }, switched.Select(row => row.Type));
        Assert.All(switched, row => Assert.False(SettingsRules.PinnedSwitch(row.Setting)));
        Assert.Equal(RotHostTables.SettingsType, SettingsRules.SettingsType);
        Assert.Equal(SettingsRules.DuelsBehaviorType, switched[0].Type);

        Assert.True(SettingsRules.PinnedSwitch("Events"));
        Assert.Null(SettingsRules.PinnedSwitch("OthersInvasion"));
        Assert.Null(SettingsRules.PinnedSwitch("DanysMusic"));
        Assert.Null(SettingsRules.PinnedSwitch("NoSuchSetting"));
    }

    // The one line each side logs, so the server's and a player's log compare: the same table
    // gives the same fingerprint on both. A change to any value changes the fingerprint, and
    // this test must then be changed on purpose.
    [Fact]
    public void Both_sides_log_the_same_line_for_the_same_table()
    {
        Assert.Equal("11f20ce5", SettingsRules.Fingerprint());
        Assert.Equal("Settings: in this Coop session Realm of Thrones' settings answer from the co-op table on the server: 40 of 40 getters covered, 9 left to each player; "
            + "pinned Duels false, Ravens false, PlayerPlateEncumbrance true, RBMIntegration false, MyLittleWarbandAllowCustomKingdomPartyTroops false, Events true; "
            + "elephantAttackChance holds only where MCM runs; table 11f20ce5; reads now Duels false, Ravens false, RBMIntegration false.",
            SettingsRules.SessionLine("server", 40, new string[0], "Duels false, Ravens false, RBMIntegration false"));

        var partly = SettingsRules.SessionLine("player's PC", 39, new[] { "DragonHealth (not found)" }, null);
        Assert.StartsWith("Settings: in this Coop session Realm of Thrones' settings answer from the co-op table on the player's PC: 39 of 40 getters covered (NOT covered: DragonHealth (not found)), ", partly);
        Assert.EndsWith("; elephantAttackChance holds only where MCM runs; table 11f20ce5.", partly);
        Assert.DoesNotContain('\n', partly);

        Assert.Equal("0.05", SettingsRules.Text(0.05f));
        Assert.Equal("false", SettingsRules.Text(false));
        Assert.Equal("-7", SettingsRules.Text(-7));
    }

    private static Mono.Cecil.ModuleDefinition CompiledModule()
    {
        return Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesCompatTests.CompatDll);
    }

    // The SubModule installs the settings before it splits into the player's and the server's
    // parts, so both get them; SettingsPins runs before Harmony is found on the server, so it
    // names nothing from 0Harmony; the host asks the table's pin before the getter; the getters
    // get patched; and the prefixes keep the parameter names Harmony injects by.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_compiled_module_installs_the_settings_on_both_sides_and_the_host_asks_the_pins()
    {
        using var module = CompiledModule();
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        bool Calls(Mono.Cecil.MethodDefinition method, string declaring, string name) =>
            method.HasBody && method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == declaring && m.Name == name);

        var subModule = module.GetType("RealmOfThronesCoopCompat.SubModule");
        var load = subModule.Methods.Single(m => m.Name == "OnSubModuleLoad").Body.Instructions.ToList();
        // A step handed to SafeInstall as a lambda or as a method group.
        int Lambda(string declaring, string name) => load.FindIndex(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldftn && i.Operand is Mono.Cecil.MethodReference m
            && ((m.DeclaringType.Name == declaring && m.Name == name) || Calls(m.Resolve(), declaring, name)));
        var settings = Lambda("SettingsPins", "Install");
        Assert.True(settings >= 0, "OnSubModuleLoad installs SettingsPins through SafeInstall");
        Assert.True(settings < Lambda("ClientLoadGate", "Install") && settings < Lambda("ServerCrashTrace", "Install"), "the settings are installed before the player's and the server's parts split");

        var pins = WithNested(module.GetType("RealmOfThronesCoopCompat.SettingsPins")).ToList();
        var harmonyUses = pins.SelectMany(type => type.Methods).Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions.Where(i => (i.Operand as Mono.Cecil.MemberReference)?.DeclaringType?.Scope is Mono.Cecil.AssemblyNameReference scope && scope.Name == "0Harmony")
                .Select(i => method.FullName + " " + i))
            .Concat(pins.SelectMany(type => type.Fields).Where(field => field.FieldType.Scope is Mono.Cecil.AssemblyNameReference scope && scope.Name == "0Harmony").Select(field => field.FullName));
        Assert.Empty(harmonyUses);
        Assert.True(pins[0].Methods.Single(m => m.Name == "Patch").NoInlining);

        var host = module.GetType("RealmOfThronesCoopCompat.RotHost").Methods.Single(m => m.Name == "HostBehaviors").Body.Instructions.ToList();
        var pinned = host.FindIndex(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "SettingsRules" && m.Name == "PinnedSwitch");
        var getter = host.FindIndex(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "PropertyInfo" && m.Name == "GetValue");
        Assert.True(pinned >= 0 && getter > pinned, "RotHost.HostBehaviors asks SettingsRules.PinnedSwitch before it reads the getter");

        var patches = module.GetType("RealmOfThronesCoopCompat.SettingsPatches");
        Assert.True(Calls(patches.Methods.Single(m => m.Name == "PatchGetter"), "Harmony", "Patch"), "SettingsPatches.PatchGetter puts the prefixes on the getters");
        // A getter that could not be patched when ROT.dll loaded is asked again at campaign start.
        Assert.True(Calls(patches.Methods.Single(m => m.Name == "RetryGetters"), "SettingsPatches", "PatchGetter"), "SettingsPatches.RetryGetters patches the getters left over");
        foreach (var (name, result) in new[] { ("BoolPrefix", "System.Boolean&"), ("IntPrefix", "System.Int32&"), ("FloatPrefix", "System.Single&") })
        {
            var prefix = patches.Methods.Single(m => m.Name == name);
            Assert.Equal(new[] { "__originalMethod", "__result" }, prefix.Parameters.Select(p => p.Name));
            Assert.Equal(result, prefix.Parameters[1].ParameterType.FullName);
        }
        Assert.Equal(new[] { "__args", "__result" }, patches.Methods.Single(m => m.Name == "RavenCheckPrefix").Parameters.Select(p => p.Name));
    }
}
