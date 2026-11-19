using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// CoopKeep part 7, phase 2: the Encyclopedia page's world (coodsz.world), its sections, filters,
// search and cards, and the Guides page's help pages.
public sealed class KeepEncyclopediaTests
{
    // What out\world.txt looks like, cut down to a small world.
    private static readonly string[] WorldFile =
    {
        "KINGDOM|empire|Northern Empire|empire|Empire|lord_1_1|Lucon|sturgia;vlandia|The northern third of the Empire.",
        "KINGDOM|sturgia|Sturgia|sturgia|Sturgia|lord_2_1|Raganvad|empire|",
        "KINGDOM|vlandia|Vlandia|vlandia|Vlandia|lord_3_1|Derthert|empire|",
        "KINGDOM|aserai|Aserai|aserai|Aserai|lord_4_1|Unqid||",
        "CLAN|clan_empire_north_1|Osticos|empire|Empire|empire|Northern Empire|lord_1_1|Lucon|6|5200|0|0|0|An old family.",
        "CLAN|player_faction|Coodsz's Clan|empire|Empire|empire|Northern Empire|Hero_Player2863|Coodsz|2|300|0|0|1|",
        "CLAN|looters|Looters|looters|Looters|||||0|0|1|1|0|",
        "CLAN|ghilman|Ghilman|aserai|Aserai|||gh_1|Ghilman Leader|3|900|1|0|0|",
        "LORD|lord_1_1|Lucon|clan_empire_north_1|Osticos|empire|Northern Empire|empire|Empire|62|0|30|90000|80|80|town_EN1|Diathma|Active|0|lord_1_2|||lord|Lucon rules the north.",
        "LORD|lord_1_2|Zerosica|clan_empire_north_1|Osticos|empire|Northern Empire|empire|Empire|58|1|20|1000|60|60|town_EN1|Diathma|Active|0|lord_1_1|||lord|",
        "LORD|lord_1_3|Ascyron|clan_empire_north_1|Osticos|empire|Northern Empire|empire|Empire|24|0|14|500|70|70|||Prisoner|0||lord_1_1|lord_1_2|lord|",
        "LORD|Hero_Player2863|Coodsz|player_faction|Coodsz's Clan|empire|Northern Empire|empire|Empire|25|0|12|4000|90|100|||Active|1||||player|",
        "LORD|wanderer_1|Bryn|||||vlandia|Vlandia|30|1|8|200|50|50|town_EN1|Diathma|Active|0||||wanderer|",
        "SETTLEMENT|town_EN1|Diathma|town|empire|Empire|clan_empire_north_1|Osticos|empire|Northern Empire|5000|0|80|60|120|220|90||A busy town.",
        "SETTLEMENT|castle_EN1|Hyrais Castle|castle|empire|Empire|clan_empire_north_1|Osticos|empire|Northern Empire|1200|0|70|50|60|150|40||",
        "SETTLEMENT|village_EN1_1|Ateni|village|empire|Empire|clan_empire_north_1|Osticos|empire|Northern Empire|0|450|0|0|0|0|20|town_EN1|",
        "SETTLEMENT|hideout_1|Forest Hideout|hideout|looters|Looters|||||0|0|0|0|0|0|0||",
        "TROOP|imperial_recruit|Imperial Recruit|empire|Empire|1|6|Infantry|0|0|imperial_infantryman;imperial_archer|OneHanded=20;Athletics=15|imperial_sword;leather_cap",
        "TROOP|imperial_infantryman|Imperial Infantryman|empire|Empire|2|11|Infantry|0|0||OneHanded=50;Polearm=40|imperial_sword",
        "TROOP|imperial_archer|Imperial Archer|empire|Empire|2|11|Ranged|0|1||Bow=60|",
        "TROOP|vlandian_knight|Vlandian Knight|vlandia|Vlandia|5|26|HeavyCavalry|1|0||Riding=150|",
        "COODSZ_END",
    };

    private static WorldData World() => WorldParser.Parse(WorldFile).WithItems(new[]
    {
        new GameItem("imperial_sword", "Imperial Sword", "OneHandedWeapon", 320, 2, "empire", "Empire", false, false, false, "OneHandedSword", 1.1),
        new GameItem("leather_cap", "Leather Cap", "HeadArmor", 40, 1, "", "", true, false, false, "", 0.5),
        new GameItem("grain", "Grain", "Goods", 10, 0, "", "", false, true, true, "", 10),
    });

    [Fact]
    public void World_file_is_read_into_every_section()
    {
        var world = World();
        Assert.Equal(4, world.Kingdoms.Count);
        Assert.Equal(4, world.Clans.Count);
        Assert.Equal(5, world.Heroes.Count);
        Assert.Equal(4, world.Settlements.Count);
        Assert.Equal(4, world.Troops.Count);
        Assert.Equal(3, world.Items.Count);
        Assert.Equal(new[] { "sturgia", "vlandia" }, world.KingdomById["empire"].WarIds);
        Assert.Empty(world.KingdomById["aserai"].WarIds);
        Assert.True(world.ClanById["looters"].Bandit);
        Assert.True(world.HeroById["Hero_Player2863"].Player);
        Assert.Equal("Prisoner", world.HeroById["lord_1_3"].State);
        Assert.Equal("town_EN1", world.SettlementById["village_EN1_1"].BoundId);
        Assert.Equal(new[] { ("OneHanded", 20), ("Athletics", 15) }, world.TroopById["imperial_recruit"].Skills);
        Assert.Equal(TroopClass.Cavalry, world.TroopById["vlandian_knight"].Class);
        Assert.False(world.IsEmpty);
        Assert.True(WorldData.Empty.IsEmpty);
    }

    [Fact]
    public void Short_and_unknown_lines_are_skipped()
    {
        var world = WorldParser.Parse(new[] { "[12:00] server log line", "KINGDOM|broken", "SOMETHING|a|b|c", "", "COODSZ_END" });
        Assert.True(world.IsEmpty);
    }

    [Fact]
    public void The_same_id_twice_keeps_the_first()
    {
        var world = WorldParser.Parse(new[] { "KINGDOM|empire|First|||||||", "KINGDOM|empire|Second|||||||" });
        Assert.Equal("First", world.KingdomById["empire"].Name);
    }

    [Fact]
    public void Search_finds_by_name_and_by_the_line_under_it_best_match_first()
    {
        var world = World();
        var rows = Encyclopedia.Search(EncSection.Lords, world, "osticos", null);
        Assert.Equal(new[] { "Ascyron", "Lucon", "Zerosica" }, rows.Select(r => r.Title));

        rows = Encyclopedia.Search(EncSection.Lords, world, "Lu", null);
        Assert.Equal("Lucon", rows[0].Title);
        Assert.Equal("Osticos · Northern Empire", rows[0].Subtitle);
        Assert.Equal("Lord", rows[0].Tag);

        Assert.Equal(new[] { "Ascyron", "Bryn", "Coodsz", "Lucon", "Zerosica" }, Encyclopedia.Search(EncSection.Lords, world, "", null).Select(r => r.Title));
        Assert.Empty(Encyclopedia.Search(EncSection.Lords, world, "nobody by this name", null));
    }

    [Fact]
    public void Filters_pick_kinds_kingdoms_classes_cultures_and_item_groups()
    {
        var world = World();
        Assert.Equal(new[] { "Coodsz" }, Encyclopedia.Search(EncSection.Lords, world, null, "kind:player").Select(r => r.Title));
        Assert.Equal(new[] { "Bryn" }, Encyclopedia.Search(EncSection.Lords, world, null, "kind:wanderer").Select(r => r.Title));
        Assert.Equal(4, Encyclopedia.Search(EncSection.Lords, world, null, "kingdom:empire").Count);
        Assert.Equal(new[] { "Ghilman" }, Encyclopedia.Search(EncSection.Clans, world, null, "clan:minor").Select(r => r.Title));
        Assert.Equal(new[] { "Looters" }, Encyclopedia.Search(EncSection.Clans, world, null, "clan:bandit").Select(r => r.Title));
        Assert.Equal(new[] { "Aserai" }, Encyclopedia.Search(EncSection.Kingdoms, world, null, "kingdom:peace").Select(r => r.Title));
        Assert.Equal(new[] { "Hyrais Castle" }, Encyclopedia.Search(EncSection.Settlements, world, null, "kind:castle").Select(r => r.Title));
        Assert.Equal(new[] { "Imperial Archer" }, Encyclopedia.Search(EncSection.Troops, world, null, "class:Archer").Select(r => r.Title));
        Assert.Equal(new[] { "Vlandian Knight" }, Encyclopedia.Search(EncSection.Troops, world, null, "culture:vlandia").Select(r => r.Title));
        Assert.Equal(new[] { "Grain" }, Encyclopedia.Search(EncSection.Items, world, null, "group:Food").Select(r => r.Title));

        var chips = Encyclopedia.Filters(EncSection.Settlements, world);
        Assert.Equal(Encyclopedia.All, chips[0].Key);
        Assert.True(chips.Take(5).All(c => c.Fixed));
        // Kingdom chips are game names (not translated), by name.
        Assert.Equal(new[] { "Aserai", "Northern Empire", "Sturgia", "Vlandia" }, chips.Where(c => !c.Fixed).Select(c => c.Label));
        Assert.Contains(Encyclopedia.Filters(EncSection.Items, world), c => c.Key == "group:Head" && c.Label == "Head");
        Assert.Equal(new[] { "Empire", "Vlandia" }, Encyclopedia.Filters(EncSection.Troops, world).Where(c => !c.Fixed).Select(c => c.Label));
    }

    [Fact]
    public void Every_section_has_a_name_and_a_count()
    {
        var world = World();
        foreach (var section in Encyclopedia.Sections)
        {
            Assert.NotEmpty(Encyclopedia.SectionName(section));
            Assert.Equal(Encyclopedia.Search(section, world, null, null).Count, Encyclopedia.Count(section, world));
        }

        Assert.Equal("1 entry", Encyclopedia.CountText(1));
        Assert.Equal("397 entries", Encyclopedia.CountText(397));
    }

    [Fact]
    public void A_lord_card_links_clan_kingdom_place_and_family()
    {
        var world = World();
        var lucon = Encyclopedia.Entry(world, EncSection.Lords, "lord_1_1")!;
        Assert.Equal("Lucon rules the north.", lucon.Text);
        Assert.Equal(GameImageKind.Lord, lucon.ImageKind);
        Assert.False(lucon.IsPlayer);
        Assert.Contains(lucon.Facts, f => f.Label == "Age" && f.Value == 62.ToString("N0"));
        Assert.Contains(lucon.Facts, f => f.Label == "State" && f.Value == "Active" && f.TranslateValue);
        Assert.Equal("Osticos", Links(lucon, "Clan").Single().Title);
        Assert.Equal("Northern Empire", Links(lucon, "Kingdom").Single().Title);
        Assert.Equal("Diathma", Links(lucon, "Where").Single().Title);
        Assert.Equal("Zerosica", Links(lucon, "Spouse").Single().Title);
        Assert.Equal(new[] { "Ascyron" }, Links(lucon, "Children").Select(l => l.Title));

        var son = Encyclopedia.Entry(world, EncSection.Lords, "lord_1_3")!;
        Assert.Equal(new[] { "Lucon", "Zerosica" }, Links(son, "Parents").Select(l => l.Title));
        Assert.Contains(son.Facts, f => f.Label == "State" && f.Value == "Prisoner");
        Assert.DoesNotContain(son.Facts, f => f.Label == "Where");

        Assert.True(Encyclopedia.Entry(world, EncSection.Lords, "Hero_Player2863")!.IsPlayer);
        Assert.Null(Encyclopedia.Entry(world, EncSection.Lords, "not_there"));
    }

    [Fact]
    public void Clan_kingdom_and_settlement_cards_link_each_other()
    {
        var world = World();
        var clan = Encyclopedia.Entry(world, EncSection.Clans, "clan_empire_north_1")!;
        Assert.Equal("Lucon", Links(clan, "Heroes")[0].Title);
        Assert.Equal(new[] { "Diathma", "Hyrais Castle" }, Links(clan, "Towns and castles").Select(l => l.Title));

        var empire = Encyclopedia.Entry(world, EncSection.Kingdoms, "empire")!;
        Assert.Equal("At war", empire.Tag);
        Assert.Equal(new[] { "Sturgia", "Vlandia" }, Links(empire, "At war with").Select(l => l.Title));
        Assert.Equal(new[] { "Diathma", "Hyrais Castle" }, Links(empire, "Towns and castles").Select(l => l.Title));
        Assert.Contains(empire.Facts, f => f.Label == "Villages" && f.Value == "1");

        var town = Encyclopedia.Entry(world, EncSection.Settlements, "town_EN1")!;
        Assert.Equal("Town", town.Tag);
        Assert.Equal(new[] { "Ateni" }, Links(town, "Villages").Select(l => l.Title));
        Assert.Equal(new[] { "Bryn", "Lucon", "Zerosica" }, Links(town, "Heroes here").Select(l => l.Title));
        Assert.Contains(town.Facts, f => f.Label == "Garrison" && f.Value == 220.ToString("N0"));

        var village = Encyclopedia.Entry(world, EncSection.Settlements, "village_EN1_1")!;
        Assert.Equal("Diathma", Links(village, "Bound to").Single().Title);
        Assert.Contains(village.Facts, f => f.Label == "Hearth");
        Assert.DoesNotContain(village.Facts, f => f.Label == "Garrison");
    }

    [Fact]
    public void Troop_and_item_cards_link_upgrades_and_gear()
    {
        var world = World();
        var recruit = Encyclopedia.Entry(world, EncSection.Troops, "imperial_recruit")!;
        Assert.Equal(new[] { "Imperial Infantryman", "Imperial Archer" }, Links(recruit, "Upgrades to").Select(l => l.Title));
        Assert.Equal(new[] { "Imperial Sword", "Leather Cap" }, Links(recruit, "Equipment").Select(l => l.Title));
        Assert.Contains(recruit.Facts, f => f.Label == "One Handed" && f.Value == "20");
        Assert.Equal("★", recruit.Tag);
        Assert.Equal(new[] { "Imperial Recruit" }, Links(Encyclopedia.Entry(world, EncSection.Troops, "imperial_archer")!, "Upgrades from").Select(l => l.Title));

        var sword = Encyclopedia.Entry(world, EncSection.Items, "imperial_sword")!;
        Assert.Equal(GameImageKind.Item, sword.ImageKind);
        Assert.Equal(new[] { "Imperial Recruit", "Imperial Infantryman" }, Links(sword, "Troops that carry it").Select(l => l.Title));
        var cap = Encyclopedia.Entry(world, EncSection.Items, "leather_cap")!;
        Assert.Contains(cap.Facts, f => f.Label == "Culture" && f.Value == "Neutral" && f.TranslateValue);
        Assert.Contains(cap.Facts, f => f.Label == "Civilian" && f.Value == "Yes" && f.TranslateValue);
    }

    [Fact]
    public void An_id_the_world_does_not_know_gives_no_link()
    {
        // The recruit lists a gear id that is not in the item list.
        var world = WorldParser.Parse(WorldFile);
        Assert.Null(Links(Encyclopedia.Entry(world, EncSection.Troops, "imperial_recruit")!, "Equipment").FirstOrDefault());
    }

    [Fact]
    public async Task The_client_reads_the_world_file_and_says_why_when_it_cannot()
    {
        string? asked = null;
        var client = new KeepClient(command =>
        {
            asked = command;
            IReadOnlyList<string> lines = command switch
            {
                "coodsz.world" => new[] { @"COODSZ_FILE|world|C:\S\Modules\DedicatedServer.CoodszTools\out\world.txt|21", "COODSZ_END" },
                "coodsz.items" => new[] { @"COODSZ_FILE|items|C:\S\Modules\DedicatedServer.CoodszTools\out\items.txt|1", "COODSZ_END" },
                _ => Array.Empty<string>(),
            };
            return Task.FromResult(lines);
        }, path => path.EndsWith("world.txt") ? string.Join("\n", WorldFile)
            : "ITEM|imperial_sword|Imperial Sword|OneHandedWeapon|320|2|empire|Empire|0|0|0|OneHandedSword|1.1\nCOODSZ_END");

        var (world, message) = await client.WorldAsync();
        Assert.Equal("", message);
        Assert.Equal(4, world.Kingdoms.Count);
        Assert.Single(world.Items);

        // Read once per start.
        asked = null;
        await client.WorldAsync();
        Assert.Null(asked);
        await client.WorldAsync(refresh: true);
        Assert.NotNull(asked);

        var old = new KeepClient(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "Unknown command coodsz.world" }), _ => null);
        var (empty, why) = await old.WorldAsync();
        Assert.True(empty.IsEmpty);
        Assert.Equal(KeepClient.NoTools, why);

        var loading = new KeepClient(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "COODSZ_ERROR no campaign is running." }), _ => null);
        Assert.Contains("Server online", (await loading.WorldAsync()).Message);
    }

    [Fact]
    public void Status_says_start_the_server_when_it_is_stopped()
    {
        var stopped = Encyclopedia.Status(false, "", false, "", false);
        Assert.Equal(EncState.Stopped, stopped.State);
        Assert.Equal("Start the server to browse the world", stopped.Headline);
        // A world read before the stop does not count; the server is the source.
        Assert.Equal(EncState.Stopped, Encyclopedia.Status(false, "", false, "", true).State);

        var waiting = Encyclopedia.Status(true, "Wait until the status says SERVING.", false, "", false);
        Assert.Equal(EncState.Waiting, waiting.State);
        Assert.Equal("Wait until the status says SERVING.", waiting.Line);
        Assert.Equal(EncState.Loading, Encyclopedia.Status(true, "", true, "", false).State);
        Assert.Equal(KeepClient.NoTools, Encyclopedia.Status(true, "", false, KeepClient.NoTools, false).Line);
        Assert.Equal(EncState.Failed, Encyclopedia.Status(true, "", false, "", false).State);
        Assert.Equal(EncState.Ready, Encyclopedia.Status(true, "", false, "", true).State);
    }

    [Fact]
    public void World_command_is_read_only_and_registered()
    {
        Assert.Equal("coodsz.world", ToolsCommand.World());
        var root = RepoRoot();
        var module = File.ReadAllText(Path.Combine(root, "compat", "CoodszServerTools", "src", "ToolsSubModule.cs"));
        Assert.Contains("Add(\"coodsz.world\", Safe(WorldCommands.World));", module);
        // The last command added (coodsz.clancap since v1.4.0) is the one Register checks.
        Assert.Contains("HasFunctionForCommand(\"coodsz.clancap\")", module);
        var world = File.ReadAllText(Path.Combine(root, "compat", "CoodszServerTools", "src", "WorldCommands.cs"));
        foreach (var change in new[] { "ChangeHeroGold", "AddToCounts", "Apply(", "SetValue", "Invoke(", "HitPoints =" })
            Assert.DoesNotContain(change, world);
        Assert.Contains("ToFile(\"world\", lines)", world);

        // The version the host adds to the module list matches the module.
        var subModule = File.ReadAllText(Path.Combine(root, "compat", "CoodszServerTools", "SubModule.xml"));
        Assert.Contains("<Version value=\"" + ToolsCommand.ModuleVersion + "\"/>", subModule);
        Assert.Contains("<Version value=\"" + ToolsCommand.ModuleVersion + "\"/>",
            File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "ServerTools", ToolsCommand.ModuleId, "SubModule.xml")));
    }

    [Fact]
    public void Every_fixed_word_the_Encyclopedia_shows_is_translated()
    {
        // The page translates these itself (its lists are not walked), so ExtractUiText cannot
        // always see them; every language must have them.
        var world = World();
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in Encyclopedia.Sections)
        {
            words.Add(Encyclopedia.SectionName(section));
            words.UnionWith(Encyclopedia.Filters(section, world).Where(f => f.Fixed).Select(f => f.Label));
            foreach (var row in Encyclopedia.Search(section, world, null, null))
            {
                if (row.Tag.Length > 0 && !row.Tag.StartsWith('★'))
                    words.Add(row.Tag);
                var entry = Encyclopedia.Entry(world, section, row.Id)!;
                words.UnionWith(entry.Facts.Select(f => f.Label));
                words.UnionWith(entry.Facts.Where(f => f.TranslateValue).Select(f => f.Value));
                words.UnionWith(entry.Groups.Select(g => g.Title));
            }
        }

        foreach (var state in new[] { "Active", "Prisoner", "Fugitive", "Released", "Traveling", "Disabled", "NotSpawned", "Dead", "?" })
            words.Add(Encyclopedia.StateName(state));
        foreach (var kind in new[] { "town", "castle", "village", "hideout" })
            words.Add(Encyclopedia.KindName(kind));
        foreach (var troopClass in Enum.GetValues<TroopClass>())
            words.Add(TroopCatalog.ClassName(troopClass));
        words.UnionWith(new[] { "Yes", "No", "Neutral", "1 entry", "{0} entries" });

        foreach (var language in BannerlordCoopLauncher.Core.Localization.Translator.Languages.Where(l => l.Code != "en"))
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language.Code);
            var missing = words.Where(w => !table.ContainsKey(w)).ToList();
            Assert.True(missing.Count == 0, language.Code + " is missing: " + string.Join(" | ", missing));
        }
    }

    private static IReadOnlyList<EncLink> Links(EncEntry entry, string group) =>
        entry.Groups.FirstOrDefault(g => g.Title == group)?.Links ?? (IReadOnlyList<EncLink>)Array.Empty<EncLink>();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

public sealed class KeepGuidesTests
{
    [Fact]
    public void Ten_guides_load_in_order_with_steps()
    {
        var guides = GuideLibrary.All;
        Assert.Equal(new[] { "getting-started", "inviting-friends", "saves-and-backups", "mods", "cheats", "map", "encyclopedia", "game-speed", "kingdom-size", "common-problems" }, guides.Select(g => g.Id));
        foreach (var guide in guides)
        {
            Assert.NotEmpty(guide.Summary);
            Assert.NotEmpty(guide.Sections);
            Assert.All(guide.Sections, s =>
            {
                Assert.NotEmpty(s.Heading);
                Assert.NotEmpty(s.Steps);
            });
        }
    }

    [Fact]
    public void Guide_text_is_plain_short_and_keeps_to_the_house_style()
    {
        foreach (var line in GuideLibrary.All.SelectMany(GuideLibrary.Lines))
        {
            Assert.True(line.Length <= 160, "Too long for a step: " + line);
            Assert.False(char.IsLower(line[0]), "Starts with a small letter: " + line);
            // Whole lines are translated, so no line may hold a placeholder.
            Assert.DoesNotContain("{", line);
        }
    }

    [Fact]
    public void Search_looks_in_every_line_and_in_the_shown_language()
    {
        var guides = GuideLibrary.All;
        Assert.Equal(guides.Count, GuideLibrary.Search(guides, "").Count);
        Assert.Contains(GuideLibrary.Search(guides, "firewall"), g => g.Id == "inviting-friends");
        Assert.Equal("saves-and-backups", GuideLibrary.Search(guides, "backups")[0].Id);
        Assert.Empty(GuideLibrary.Search(guides, "zzzz"));
        // A German word finds the guide when the screen shows German.
        var german = GuideLibrary.Search(guides, "Freunde", line => line == "Inviting friends" ? "Freunde einladen" : line);
        Assert.Equal("inviting-friends", german.Single().Id);
    }

    [Fact]
    public void A_broken_file_is_not_a_guide()
    {
        Assert.Null(GuideLibrary.Parse("not json"));
        Assert.Null(GuideLibrary.Parse("{\"title\":\"No id\"}"));
        var guide = GuideLibrary.Parse("{\"id\":\"a\",\"title\":\"A\",\"sections\":[{\"heading\":\"H\",\"steps\":[\"One\",\"\"]}]}")!;
        Assert.Equal(new[] { "One" }, guide.Sections[0].Steps);
        Assert.Equal("", guide.Sections[0].Tip);
    }
}
