using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// The Cheats page: each cheat becomes a Coop SERVER command with the ids the server knows, Give and
// Take gold are worked out from the gold the host read, and wrong values are refused with a line to show.
public sealed class PlayerCheatsTests
{
    private static readonly CheatTarget Bob = new("Big Bob", "hero_7", "clan_7", "party_7", "76561198000000007", Gold: 5_000, Age: 31.6);

    private static CheatBuild Build(string id, CheatTarget? target, params (string Key, string Value)[] values) =>
        PlayerCheats.Build(PlayerCheats.Find(id)!, target, values.ToDictionary(v => v.Key, v => v.Value));

    private static string Error(string id, CheatTarget? target, params (string Key, string Value)[] values) =>
        Assert.Throws<ArgumentException>(() => Build(id, target, values)).Message;

    [Fact]
    public void Give_and_take_gold_set_the_new_total_and_undo_sets_the_old_one()
    {
        var give = Build("gold.give", Bob, ("amount", "10,000"));
        Assert.Equal("coop.debug.hero.set_gold \"Big Bob\" 15000", give.Command);
        Assert.Equal("coop.debug.hero.set_gold \"Big Bob\" 5000", give.Undo);

        var take = Build("gold.take", Bob, ("amount", "8000"));
        Assert.Equal("coop.debug.hero.set_gold \"Big Bob\" 0", take.Command);
        Assert.Equal("coop.debug.hero.set_gold \"Big Bob\" 5000", take.Undo);

        Assert.Equal(15_000, PlayerCheats.GoldAfterGive(5_000, 10_000));
        Assert.Equal(PlayerCheats.MaxGold, PlayerCheats.GoldAfterGive(PlayerCheats.MaxGold - 1, int.MaxValue));
        Assert.Equal(2_000, PlayerCheats.GoldAfterTake(5_000, 3_000));
        Assert.Equal(0, PlayerCheats.GoldAfterTake(5_000, 9_000));
    }

    [Fact]
    public void Unknown_gold_offers_only_Set_gold()
    {
        var noGold = Bob with { Gold = null };
        Assert.Equal(new[] { "gold.set" }, PlayerCheats.In(CheatCategory.Gold, noGold).Select(c => c.Id));
        Assert.Equal(new[] { "gold.give", "gold.take", "gold.set" }, PlayerCheats.In(CheatCategory.Gold, Bob).Select(c => c.Id));
        var set = Build("gold.set", noGold, ("amount", "250"));
        Assert.Equal("coop.debug.hero.set_gold \"Big Bob\" 250", set.Command);
        Assert.Null(set.Undo);
        Assert.Contains("use Set gold", Error("gold.give", noGold, ("amount", "1")));
    }

    [Fact]
    public void Clan_cheats_use_the_clan_id_and_influence_can_be_undone()
    {
        var influence = Build("clan.influence", Bob, ("amount", "100"));
        Assert.Equal("coop.debug.clan.add_influence clan_7 100", influence.Command);
        Assert.Equal("coop.debug.clan.add_influence clan_7 -100", influence.Undo);
        Assert.Equal("coop.debug.clan.add_renown clan_7 500", Build("clan.renown", Bob, ("amount", "500")).Command);
        Assert.Null(Build("clan.renown", Bob, ("amount", "500")).Undo);
        Assert.Contains("clan", Error("clan.renown", Bob with { ClanId = "" }, ("amount", "5")));
        Assert.Equal("Type a number that is not 0.", Error("clan.influence", Bob, ("amount", "0")));
    }

    [Fact]
    public void Joining_a_kingdom_uses_the_controller_id_and_needs_the_player_online()
    {
        var cheat = PlayerCheats.Find("clan.vassal")!;
        Assert.Equal("coop.debug.kingdom.force_player_vassalage 76561198000000007 empire true",
            Build("clan.vassal", Bob, ("kingdom", "empire"), ("rewards", "1")).Command);
        Assert.EndsWith(" false", Build("clan.vassal", Bob, ("kingdom", "empire"), ("rewards", "0")).Command);
        Assert.Equal("Pick a kingdom first.", Error("clan.vassal", Bob));
        Assert.Null(PlayerCheats.OfflineNote(cheat, Bob));
        Assert.NotNull(PlayerCheats.OfflineNote(cheat, Bob with { Online = false }));
        Assert.Null(PlayerCheats.OfflineNote(PlayerCheats.Find("gold.set")!, Bob with { Online = false }));
    }

    [Fact]
    public void Joining_a_kingdom_is_refused_up_front_when_the_clan_tier_is_too_low()
    {
        // Coop says "Queued vassalage" and then logs "Rejected vassal service request ... no longer eligible"
        // for a clan below ClanTierModel.VassalEligibleTier, so the page must not say Done for it.
        var error = Error("clan.vassal", Bob with { ClanTier = 0 }, ("kingdom", "empire"));
        Assert.Equal("Coop only takes a clan of tier 2 or higher as a vassal, and Big Bob's clan is tier 0. Use Set clan tier first, then join the kingdom.", error);
        Assert.Contains("Use Set clan tier first", Error("clan.vassal", Bob with { ClanTier = 1 }, ("kingdom", "empire")));

        var ok = Build("clan.vassal", Bob with { ClanTier = 2 }, ("kingdom", "empire"));
        Assert.Equal("coop.debug.kingdom.force_player_vassalage 76561198000000007 empire true", ok.Command);
        Assert.Null(ok.Note);
        Assert.Equal("asked to join kingdom empire", ok.Summary);

        // Without Coodsz Server Tools the tier is not known: it is sent, with a note that Coop can still turn it down.
        var unknown = Build("clan.vassal", Bob, ("kingdom", "empire"));
        Assert.Equal(PlayerCheats.VassalTierNote, unknown.Note);
        Assert.Contains("below " + PlayerCheats.VassalTier, PlayerCheats.VassalTierNote);
        // The pick of a kingdom still comes first.
        Assert.Equal("Pick a kingdom first.", Error("clan.vassal", Bob with { ClanTier = 0 }));

        // Both Cheats pages add the note to the Done line.
        foreach (var page in new[] { Path.Combine("CoodszHost", "Keep", "CheatsPage.xaml.cs"), Path.Combine("TaomCoopHost", "CoopCheatsPage.xaml.cs") })
            Assert.Contains("(build.Note == null ? \"\" : \" \" + Translator.T(build.Note))", File.ReadAllText(Path.Combine(RepoRoot(), "src", page)));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Skill_cheats_use_the_hero_id()
    {
        Assert.Equal("coop.debug.hero_developer.add_skill_xp hero_7 OneHanded 1000", Build("skills.xp", Bob, ("skill", "OneHanded"), ("amount", "1000")).Command);
        Assert.Equal("Pick a skill first.", Error("skills.xp", Bob, ("amount", "1000")));
        Assert.Equal("Pick a skill from the list.", Error("skills.xp", Bob, ("skill", "Flying"), ("amount", "1000")));
        var points = Build("skills.attribute", Bob, ("amount", "2"));
        Assert.Equal("coop.debug.hero_developer.add_attribute_points hero_7 2", points.Command);
        Assert.Equal("coop.debug.hero_developer.add_attribute_points hero_7 -2", points.Undo);
        Assert.Equal("coop.debug.hero_developer.add_focus_points hero_7 3", Build("skills.focus", Bob, ("amount", "3")).Command);
        var reset = Build("skills.reset", Bob);
        Assert.Equal("coop.debug.hero_developer.reset_skills hero_7", reset.Command);
        Assert.Null(reset.Undo);
        Assert.True(PlayerCheats.Find("skills.reset")!.CannotUndo);
        // Without a hero id the name is used; Coop takes either.
        Assert.Equal("coop.debug.hero_developer.reset_skills \"Big Bob\"", Build("skills.reset", Bob with { HeroId = "" }).Command);
    }

    [Fact]
    public void Party_and_hero_cheats()
    {
        Assert.Equal("coop.debug.mobile_party.add_troops \"Big Bob\"", Build("party.troops", Bob).Command);
        Assert.Equal("coop.debug.mobile_party.add_troop_xp \"Big Bob\" 500", Build("party.troopxp", Bob, ("amount", "500")).Command);
        Assert.Equal("coop.debug.crafting.add_crafted_items \"Big Bob\"", Build("party.crafted", Bob).Command);
        Assert.Equal("coop.debug.hideout.teleport_party party_7", Build("party.hideout", Bob).Command);
        var age = Build("hero.age", Bob, ("amount", "40"));
        Assert.Equal("coop.debug.hero.set_age hero_7 40", age.Command);
        Assert.Equal("coop.debug.hero.set_age hero_7 32", age.Undo);
        Assert.Null(Build("hero.age", Bob with { Age = null }, ("amount", "40")).Undo);
        Assert.Equal("coop.debug.hero.set_relation hero_7 lord_1_1 -20", Build("hero.relation", Bob, ("hero", "lord_1_1"), ("amount", "-20")).Command);
        Assert.Equal("Pick a hero other than the player.", Error("hero.relation", Bob, ("hero", "hero_7"), ("amount", "1")));
    }

    [Fact]
    public void World_cheats_need_no_player()
    {
        Assert.Equal("coop.debug.town.set_prosperity town_V1 6000", Build("world.prosperity", null, ("town", "town_V1"), ("amount", "6000")).Command);
        Assert.Equal("coop.debug.village.set_hearth village_V1_1 300", Build("world.hearth", null, ("village", "village_V1_1"), ("amount", "300")).Command);
        Assert.All(PlayerCheats.All.Where(c => c.Category == CheatCategory.World), c => Assert.True(c.NotAPlayer));
        Assert.Equal("Pick a player first.", Error("gold.set", null, ("amount", "1")));
    }

    [Fact]
    public void Numbers_are_checked()
    {
        Assert.Equal("Age must be from 18 to 90.", Error("hero.age", Bob, ("amount", "7")));
        Assert.Equal("Type a whole number for age.", Error("hero.age", Bob, ("amount", "old")));
        Assert.Equal("Type a whole number for gold.", Error("gold.set", Bob));
        Assert.Equal("Relation must be from -100 to 100.", Error("hero.relation", Bob, ("hero", "lord_1_1"), ("amount", "101")));
        Assert.True(PlayerCheats.TryNumber(" 1 000 ", out var n) && n == 1000);
        Assert.True(PlayerCheats.TryNumber("-5", out var m) && m == -5);
        Assert.All(PlayerCheats.All.SelectMany(c => c.Inputs).Where(i => i.Kind == CheatInputKind.Number),
            i => Assert.InRange(i.Default, i.Min, i.Max));
    }

    [Fact]
    public void Server_tools_cheats_send_coodsz_commands_and_undo_from_the_value_read_back()
    {
        var hurt = Bob with { HitPoints = 40, MaxHitPoints = 120, ClanTier = 2 };
        Assert.All(PlayerCheats.All.Where(c => c.Tools), c => Assert.StartsWith("coodsz.", c.CoopCommand));
        Assert.Equal("coodsz.health hero_7 120", Build("hero.heal", hurt).Command);
        Assert.Equal("Big Bob is already at full health.", Error("hero.heal", hurt with { HitPoints = 120 }));
        Assert.Equal("coodsz.health hero_7 90", Build("hero.health.give", hurt, ("amount", "50")).Command);
        Assert.Equal("coodsz.health hero_7 120", Build("hero.health.give", hurt, ("amount", "500")).Command);
        Assert.Equal("coodsz.health hero_7 1", Build("hero.health.take", hurt, ("amount", "500")).Command);
        Assert.Equal("coodsz.health hero_7 75", Build("hero.health.set", hurt, ("amount", "75")).Command);
        Assert.Equal("coodsz.clan_tier hero_7 4", Build("clan.tier", hurt, ("amount", "4")).Command);
        Assert.Equal("coodsz.renown hero_7 -100", Build("clan.renown.take", hurt, ("amount", "100")).Command);
        Assert.Equal("coodsz.heal_troops hero_7", Build("party.heal", hurt).Command);
        Assert.Contains("hero", Error("hero.health.set", hurt with { HeroId = "" }, ("amount", "5")));

        Assert.Equal("coodsz.health hero_7 40", PlayerCheats.ToolsUndo("hero.heal", new ToolsChange("hero_7", "hp", 40, 120)));
        Assert.Equal("coodsz.clan_tier hero_7 2", PlayerCheats.ToolsUndo("clan.tier", new ToolsChange("hero_7", "tier", 2, 4)));
        Assert.Equal("coodsz.renown hero_7 100", PlayerCheats.ToolsUndo("clan.renown.take", new ToolsChange("hero_7", "renown", 500, 400)));
        Assert.Null(PlayerCheats.ToolsUndo("clan.tier", new ToolsChange("hero_7", "tier", 4, 4)));
        Assert.Null(PlayerCheats.ToolsUndo("party.heal", new ToolsChange("hero_7", "wounded", 3, 0)));
    }

    [Fact]
    public void A_server_without_the_tools_module_gets_only_coop_debug_cheats()
    {
        // The AD1259 and TAOM Cheats pages stay on coop.debug, so they never offer a coodsz.* cheat.
        var without = Enum.GetValues<CheatCategory>().SelectMany(c => PlayerCheats.In(c, Bob, tools: false)).ToList();
        Assert.NotEmpty(without);
        Assert.All(without, c => Assert.StartsWith("coop.debug.", c.CoopCommand));
        Assert.DoesNotContain(without, c => c.Tools);
        Assert.Contains(without, c => c.Id == "gold.give");

        var with = Enum.GetValues<CheatCategory>().SelectMany(c => PlayerCheats.In(c, Bob)).ToList();
        Assert.Equal(PlayerCheats.All.Count, with.Count);
        Assert.Equal(new[] { "clan.influence", "clan.renown", "clan.vassal" }, PlayerCheats.In(CheatCategory.Clan, Bob, tools: false).Select(c => c.Id));
        Assert.Equal(new[] { "hero.age", "hero.relation" }, PlayerCheats.In(CheatCategory.Hero, Bob, tools: false).Select(c => c.Id));

        // Every category still has something to pick without the tools.
        Assert.Equal(Enum.GetValues<CheatCategory>(), PlayerCheats.Categories(tools: false));
    }

    [Fact]
    public void Favourites_keep_the_list_order_and_drop_what_this_server_cannot_run()
    {
        var ids = new[] { "hero.heal", "world.hearth", "gold.give", "gold.set" };
        Assert.Equal(new[] { "gold.give", "gold.set", "hero.heal", "world.hearth" }, PlayerCheats.Favourites(ids, Bob).Select(c => c.Id));
        Assert.Equal(new[] { "gold.give", "gold.set", "world.hearth" }, PlayerCheats.Favourites(ids, Bob, tools: false).Select(c => c.Id));
        Assert.Equal(new[] { "gold.set", "world.hearth" }, PlayerCheats.Favourites(ids, Bob with { Gold = null }, tools: false).Select(c => c.Id));
        Assert.False(PlayerCheats.Works(PlayerCheats.Find("party.army")!, Bob, tools: false));
        Assert.True(PlayerCheats.Works(PlayerCheats.Find("party.army")!, Bob));
    }

    [Fact]
    public void Roster_puts_connected_players_first_and_adds_the_ones_only_Coop_knows()
    {
        var coop = new[]
        {
            new CoopPlayerRow("Big Bob", "76561198000000007", "hero_7", "party_7", "clan_7"),
            new CoopPlayerRow("Ann", "76561198000000001", "hero_1", "", "clan_1"),
            new CoopPlayerRow(" ", "x", "", "", ""),
        };
        var roster = CheatRoster.Merge(new[] { "big bob", "Cara", "Cara", "" }, coop);
        Assert.Equal(new[] { "big bob", "Cara", "Ann" }, roster.Select(p => p.Name));
        Assert.Equal(new[] { true, true, false }, roster.Select(p => p.Online));
        Assert.Equal("hero_7", roster[0].HeroId);
        Assert.Null(roster[1].Coop);

        // The name Coop has goes into the command, with the ids it gave.
        var bob = CheatRoster.Target(roster[0]);
        Assert.Equal(new CheatTarget("Big Bob", "hero_7", "clan_7", "party_7", "76561198000000007"), bob);
        Assert.Equal("coop.debug.clan.add_influence clan_7 100", Build("clan.influence", bob, ("amount", "100")).Command);
        Assert.False(CheatRoster.Target(roster[2]).Online);
        Assert.Contains("clan", Error("clan.influence", CheatRoster.Target(roster[1]), ("amount", "100")));
        Assert.Equal("coop.debug.hero.set_gold Cara 500", Build("gold.set", CheatRoster.Target(roster[1]), ("amount", "500")).Command);
    }

    [Fact]
    public void Coop_list_replies_are_read()
    {
        var players = CoopReplies.Players(new[]
        {
            "Side: Server  LocalControllerId: <unknown>\nRegistered players: 2 (expected: one per client, host excluded)",
            "- PlayerName: Big Bob",
            "    ControllerId: 76561198000000007",
            "    Hero: hero_7 resolved, controlled=True",
            "    Party: party_7 resolved, controlled=True",
            "    Clan: clan_7 resolved, controlled=True",
            "- PlayerName: Ann (you)",
            "    ControllerId: 2",
            "    Hero: <none>",
            "    Party: party_2 <NOT RESOLVED>",
            "    Clan: clan_2 resolved, controlled=False",
            "PlayerObjects entries (resolved & controlled): 3",
        });
        Assert.Equal(new[]
        {
            new CoopPlayerRow("Big Bob", "76561198000000007", "hero_7", "party_7", "clan_7"),
            new CoopPlayerRow("Ann", "2", "", "party_2", "clan_2"),
        }, players);

        Assert.Equal(new[] { new CoopListItem("aserai", "Aserai"), new CoopListItem("empire", "Northern Empire") },
            CoopReplies.Kingdoms(new[] { "Name: 'Northern Empire'\n Id : 'empire'\nName: 'Aserai'\n Id : 'aserai'\n" }));
        Assert.Equal(new[] { new CoopListItem("town_A1", "Amprela"), new CoopListItem("town_B1", "Ba'lih") },
            CoopReplies.IdThenName(new[] { "ID: 'town_B1'\nName: 'Ba'lih'\nID: 'town_A1'\nName: 'Amprela'" }));
        Assert.Equal(new[] { new CoopListItem("lord_1_1", "Lucon") },
            CoopReplies.Heroes(new[] { "ID: 'lord_1_1', Name: 'Lucon', Game ID: 12, Game StringId lord_1_1", "Name: 'X' was not registered with object manager" }));
    }
}
