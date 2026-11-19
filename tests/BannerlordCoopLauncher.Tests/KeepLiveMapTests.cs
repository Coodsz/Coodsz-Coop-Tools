using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// CoopKeep part 7, phase 3: the live map's data (coodsz.map), its camera, layers, territory,
// legend, the plain words for what a party does, and streamer mode.
public sealed class KeepLiveMapTests
{
    // What out\map.txt looks like, cut down to a small world.
    private static readonly string[] MapFile =
    {
        "MAPKINGDOM|empire|Northern Empire|FF5A2A8C|FFE0C060",
        "MAPKINGDOM|vlandia|Vlandia|FF8C1A1A|FFF0D070",
        "MAPKINGDOM|dark|Dark Kingdom|FF101010|FF202020",
        "MAPSETTLEMENT|town_EN1|Diathma|town|500|400|clan_1|Osticos|empire|FF5A2A8C||0",
        "MAPSETTLEMENT|castle_EN1|Hyrais Castle|castle|520.5|380|clan_1|Osticos|empire|FF5A2A8C||1",
        "MAPSETTLEMENT|village_EN1_1|Ateni|village|490|410|clan_1|Osticos|empire|FF5A2A8C|town_EN1|0",
        "MAPSETTLEMENT|town_V1|Pravend|town|200|300|clan_2|Dey Meroc|vlandia|FF8C1A1A||0",
        "MAPSETTLEMENT|town_X|Lonely|town|300|700|clan_3|Free||00000000||0",
        "MAPSETTLEMENT|broken|Broken|town|abc|1|clan_3|Free||00000000||0",
        "MAPPARTY|p_1|Hero_Player2863|Coodsz|player_clan|Coodsz's Clan|empire|player|505|402|40|2|town_EN1|||0|0||FF5A2A8C",
        "MAPPARTY|p_2|Hero_Player9|Friend|player_clan2|Friend's Clan|vlandia|player|250|320|25|0||||0|0|town_V1|FF8C1A1A",
        "MAPPARTY|p_3|lord_1_1|Lucon|clan_1|Osticos|empire|lord|510|395|300|10||lord_1_1|900|0|0||FF5A2A8C",
        "MAPPARTY|p_4|lord_1_2|Zerosica|clan_1|Osticos|empire|lord|510|395|200|0||lord_1_1|0|0|0||FF5A2A8C",
        "MAPPARTY|p_5|lord_2_1|Derthert|clan_2|Dey Meroc|vlandia|lord|210|300|150|0|town_V1||0|0|0||FF8C1A1A",
        "MAPPARTY|p_6|lord_2_2|Burin|clan_2|Dey Meroc|vlandia|lord|400|500|80|0|||0|1|0||FF8C1A1A",
        "MAPPARTY|p_7|lord_2_3|Aldric|clan_2|Dey Meroc|vlandia|lord|515|381|120|0|||0|0|1||FF8C1A1A",
        "COODSZ_END",
    };

    private static MapData Map() => MapParser.Parse(MapFile);

    [Fact]
    public void Map_file_is_read_and_bad_lines_are_skipped()
    {
        var map = Map();
        Assert.Equal(3, map.Kingdoms.Count);
        Assert.Equal(5, map.Settlements.Count);
        Assert.Equal(7, map.Parties.Count);
        Assert.Equal(520.5, map.SettlementById["castle_EN1"].X);
        Assert.True(map.SettlementById["castle_EN1"].UnderSiege);
        Assert.True(map.SettlementById["castle_EN1"].IsCastle);
        Assert.Equal("town_EN1", map.SettlementById["village_EN1_1"].BoundId);
        Assert.Equal(0xFF5A2A8Cu, map.KingdomById["empire"].Color);
        var lucon = map.PartyByHero["lord_1_1"];
        Assert.True(lucon.LeadsArmy);
        Assert.Equal(900, lucon.ArmyTroops);
        Assert.True(map.PartyByHero["lord_1_2"].InOtherArmy);
        Assert.True(map.PartyByHero["Hero_Player2863"].IsPlayer);
        Assert.True(map.PartyByHero["Hero_Player2863"].InSettlement);
        Assert.False(map.IsEmpty);
        Assert.True(MapData.Empty.IsEmpty);
    }

    [Fact]
    public void A_parties_only_read_keeps_the_known_settlements()
    {
        var known = Map();
        var newer = MapParser.Parse(new[]
        {
            "MAPKINGDOM|empire|Northern Empire|FF5A2A8C|FFE0C060",
            "MAPPARTY|p_1|Hero_Player2863|Coodsz|player_clan|Coodsz's Clan|empire|player|600|450|41|0||||0|0||FF5A2A8C",
        });
        var merged = known.WithParties(newer);
        Assert.Equal(known.Settlements.Count, merged.Settlements.Count);
        Assert.Single(merged.Parties);
        Assert.Equal(600, merged.PartyByHero["Hero_Player2863"].X);
        Assert.Single(merged.Kingdoms);
        Assert.Equal("coodsz.map parties", ToolsCommand.Map(true));
        Assert.Equal("coodsz.map", ToolsCommand.Map());
    }

    [Fact]
    public async Task Client_reads_the_map_file_and_says_why_it_could_not()
    {
        var sent = new List<string>();
        var client = new KeepClient(command =>
        {
            sent.Add(command);
            return Task.FromResult<IReadOnlyList<string>>(new[] { "COODSZ_FILE|map|C:\\s\\Modules\\DedicatedServer.CoodszTools\\out\\map.txt|16", "COODSZ_END" });
        }, _ => string.Join("\n", MapFile));
        var (map, message) = await client.MapAsync(MapData.Empty, settlements: false);
        Assert.Equal("", message);
        Assert.Equal(5, map.Settlements.Count);
        // Nothing known yet, so the first read is a full one even when only parties were asked for.
        Assert.Equal("coodsz.map", sent[0]);
        await client.MapAsync(map, settlements: false);
        Assert.Equal("coodsz.map parties", sent[1]);

        var unknown = new KeepClient(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "Unknown command: coodsz.map" }), _ => null);
        var (empty, why) = await unknown.MapAsync(MapData.Empty, true);
        Assert.True(empty.IsEmpty);
        Assert.Equal(KeepClient.NoTools, why);

        var noFile = new KeepClient(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "COODSZ_END" }), _ => null);
        Assert.Equal(KeepClient.NoAnswer, (await noFile.MapAsync(MapData.Empty, true)).Message);
    }

    [Fact]
    public void Status_says_start_wait_read_or_fail_and_keeps_the_map_while_reading_again()
    {
        Assert.Equal(EncState.Stopped, LiveMap.Status(false, "", false, "", false).State);
        Assert.Equal(LiveMap.StoppedHeadline, LiveMap.Status(false, "", false, "", true).Headline);
        Assert.Equal(EncState.Waiting, LiveMap.Status(true, "The server is loading.", false, "", false).State);
        Assert.Equal(EncState.Loading, LiveMap.Status(true, "", true, "", false).State);
        // An auto refresh in progress does not hide a map that is already drawn.
        Assert.Equal(EncState.Ready, LiveMap.Status(true, "", true, "", true).State);
        Assert.Equal(EncState.Ready, LiveMap.Status(true, "", false, "a hiccup", true).State);
        var failed = LiveMap.Status(true, "", false, KeepClient.NoTools, false);
        Assert.Equal(EncState.Failed, failed.State);
        Assert.Equal(KeepClient.NoTools, failed.Line);
    }

    [Fact]
    public void Settlements_are_read_again_every_thirty_seconds()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0);
        Assert.True(LiveMap.NeedsSettlements(MapData.Empty, now, now));
        Assert.True(LiveMap.NeedsSettlements(Map(), null, now));
        Assert.False(LiveMap.NeedsSettlements(Map(), now.AddSeconds(-10), now));
        Assert.True(LiveMap.NeedsSettlements(Map(), now - LiveMap.SettlementsEvery, now));
        Assert.True(LiveMap.RefreshEvery <= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Camera_fits_the_world_flips_north_up_and_zooms_around_the_mouse()
    {
        var bounds = new MapBounds(0, 0, 1000, 500);
        var camera = new MapCamera();
        camera.Resize(800, 400);
        camera.Fit(bounds);
        Assert.Equal(0.8, camera.Scale, 6);
        Assert.Equal(1, camera.Zoom, 6);
        // North (larger y) is up on screen.
        var (_, northY) = camera.ToScreen(500, 500);
        var (_, southY) = camera.ToScreen(500, 0);
        Assert.True(northY < southY);
        Assert.Equal((400.0, 200.0), camera.ToScreen(500, 250));

        // The game point under the mouse stays under it.
        var before = camera.ToWorld(100, 50);
        camera.ZoomAt(100, 50, 2);
        var after = camera.ToWorld(100, 50);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(2, camera.Zoom, 6);

        // Zoom has limits both ways.
        camera.ZoomAt(0, 0, 1000);
        Assert.Equal(14, camera.Zoom, 6);
        camera.ZoomAt(0, 0, 0.0001);
        Assert.Equal(0.6, camera.Zoom, 6);

        camera.Pan(80, 0);
        var (cx, _) = camera.ToWorld(400, 200);
        camera.CenterOn(123, 456, 4);
        Assert.Equal((400.0, 200.0), camera.ToScreen(123, 456));
        Assert.Equal(4, camera.Zoom, 6);
        Assert.NotEqual(0, cx);
        // Centring on a point never zooms out.
        camera.CenterOn(10, 10, 2);
        Assert.Equal(4, camera.Zoom, 6);
    }

    [Fact]
    public void Bounds_cover_everything_with_a_margin()
    {
        var bounds = LiveMap.Bounds(Map());
        Assert.True(bounds.MinX < 200 && bounds.MaxX > 520.5);
        Assert.True(bounds.MinY < 300 && bounds.MaxY > 700);
        Assert.Equal(new MapBounds(0, 0, 1000, 1000), LiveMap.Bounds(MapData.Empty));
    }

    [Fact]
    public void Players_are_always_drawn_lords_only_out_on_the_map_and_not_inside_armies()
    {
        var map = Map();
        var drawn = LiveMap.PartiesToDraw(map, new MapLayers()).Select(p => p.HeroId).ToList();
        Assert.Contains("Hero_Player2863", drawn); // inside Diathma, still drawn
        Assert.Contains("Hero_Player9", drawn);
        Assert.Contains("lord_1_1", drawn);         // army leader
        Assert.DoesNotContain("lord_1_2", drawn);   // rides in Lucon's army
        Assert.DoesNotContain("lord_2_1", drawn);   // inside Pravend
        // Players come last so they are drawn on top.
        Assert.Equal("Hero_Player9", drawn[^1]);

        // Without the Lords layer the army leaders stay; without both only players are left.
        var armies = LiveMap.PartiesToDraw(map, new MapLayers(Lords: false)).Select(p => p.HeroId).ToList();
        Assert.Contains("lord_1_1", armies);
        Assert.All(LiveMap.PartiesToDraw(map, new MapLayers(Lords: false)), p => Assert.True(p.IsPlayer || p.LeadsArmy));
        var noLords = LiveMap.PartiesToDraw(map, new MapLayers(Lords: false, Armies: false));
        Assert.All(noLords, p => Assert.True(p.IsPlayer));
        Assert.Equal(2, noLords.Count);
    }

    [Fact]
    public void Layers_and_names_follow_the_zoom()
    {
        var map = Map();
        var town = map.SettlementById["town_EN1"];
        var castle = map.SettlementById["castle_EN1"];
        var village = map.SettlementById["village_EN1_1"];
        Assert.True(LiveMap.Shows(village, new MapLayers()));
        Assert.False(LiveMap.Shows(village, new MapLayers(Villages: false)));
        Assert.False(LiveMap.Shows(castle, new MapLayers(Castles: false)));
        Assert.False(LiveMap.Shows(town, new MapLayers(Towns: false)));
        Assert.True(LiveMap.ShowsName(town, 1));
        Assert.False(LiveMap.ShowsName(castle, 1));
        Assert.True(LiveMap.ShowsName(castle, 2));
        Assert.False(LiveMap.ShowsName(village, 2));
        Assert.False(LiveMap.ShowsName(village, 4));
        Assert.True(LiveMap.ShowsName(village, 5));
        Assert.False(LiveMap.ShowsLordName(3));
        Assert.True(LiveMap.ShowsLordName(6));
    }

    [Fact]
    public void Streamer_mode_hides_player_names_but_not_lords()
    {
        var map = Map();
        var coodsz = map.PartyByHero["Hero_Player2863"];
        var friend = map.PartyByHero["Hero_Player9"];
        Assert.Equal("Coodsz", LiveMap.PlayerName(map, coodsz, false));
        Assert.Equal("Player 1", LiveMap.PlayerName(map, coodsz, true));
        Assert.Equal("Player 2", LiveMap.PlayerName(map, friend, true));
        Assert.Equal("Lucon", LiveMap.PlayerName(map, map.PartyByHero["lord_1_1"], true));
    }

    [Fact]
    public void Activity_says_what_the_party_is_doing()
    {
        var map = Map();
        Assert.Equal("Staying in Diathma", LiveMap.Activity(map, map.PartyByHero["Hero_Player2863"]));
        Assert.Equal("On the way to Pravend", LiveMap.Activity(map, map.PartyByHero["Hero_Player9"]));
        Assert.Equal("In the army of Lucon", LiveMap.Activity(map, map.PartyByHero["lord_1_2"]));
        Assert.Equal("Fighting in battle", LiveMap.Activity(map, map.PartyByHero["lord_2_2"]));
        Assert.Equal("Laying siege", LiveMap.Activity(map, map.PartyByHero["lord_2_3"]));
        // Out on the map with no target: the closest town or castle.
        Assert.Equal("Near Diathma", LiveMap.Activity(map, map.PartyByHero["lord_1_1"]));
    }

    [Fact]
    public void Colours_are_the_kingdoms_and_dark_ones_are_lightened()
    {
        var map = Map();
        Assert.Equal(LiveMap.Readable(0xFF5A2A8C), LiveMap.ColorOf(map, "empire", 0));
        Assert.Equal(LiveMap.NeutralColor, LiveMap.ColorOf(map, "", 0));
        // A settlement outside any kingdom keeps its own colour when it has one.
        Assert.Equal(0xFFE0C060u, LiveMap.ColorOf(map, "", 0xFFE0C060));
        var dark = LiveMap.ColorOf(map, "dark", 0);
        var light = 0.299 * ((dark >> 16) & 0xFF) + 0.587 * ((dark >> 8) & 0xFF) + 0.114 * (dark & 0xFF);
        Assert.True(light >= 0.32 * 255);
        // Bright colours stay as they are, and every colour comes out opaque.
        Assert.Equal(0xFFE0C060u, LiveMap.Readable(0xFFE0C060));
        Assert.Equal(0xFFFFFFFFu, LiveMap.Readable(0x00FFFFFF));
    }

    [Fact]
    public void Legend_lists_kingdoms_by_fiefs_then_no_kingdom()
    {
        var legend = LiveMap.Legend(Map());
        Assert.Equal(new[] { "Northern Empire", "Vlandia", "No kingdom" }, legend.Select(r => r.Name));
        Assert.Equal(2, legend[0].Fiefs); // town and castle, not the village
        Assert.Equal(1, legend[2].Fiefs);
        var labels = LiveMap.KingdomLabels(Map());
        var empire = Assert.Single(labels, l => l.KingdomId == "empire");
        Assert.Equal(510.25, empire.X, 6);
    }

    [Fact]
    public void Territory_colours_cells_near_settlements_and_leaves_the_rest_empty()
    {
        var map = Map();
        var bounds = new MapBounds(0, 0, 1000, 1000);
        var cells = LiveMap.Territory(map, bounds, 10, 10, 80);
        Assert.Equal(100, cells.Length);
        // Row 0 is the north edge: the cell centred on (450, 450) is row 5, column 4, next to Ateni (490, 410).
        Assert.Equal(LiveMap.ColorOf(map, "empire", 0), cells[5 * 10 + 4]);
        // Pravend (200, 300) is Vlandia's: the cell centred on (250, 350) is row 6, column 2.
        Assert.Equal(LiveMap.ColorOf(map, "vlandia", 0), cells[6 * 10 + 2]);
        // Far corners are empty.
        Assert.Equal(0u, cells[0]);
        Assert.Equal(0u, cells[99]);
        Assert.Empty(LiveMap.Territory(map, bounds, 0, 0, 60));
        var reach = LiveMap.TerritoryReach(map);
        Assert.InRange(reach, 10, 200);
    }

    [Fact]
    public void Map_command_is_read_only_registered_last_and_has_no_teleport()
    {
        var root = RepoRoot();
        var module = File.ReadAllText(Path.Combine(root, "compat", "CoodszServerTools", "src", "ToolsSubModule.cs"));
        Assert.Contains("Add(\"coodsz.map\", Safe(MapCommands.Map));", module);
        Assert.Contains("HasFunctionForCommand(\"coodsz.clancap\")", module);
        var adds = module.Split('\n').Where(l => l.TrimStart().StartsWith("Add(\"coodsz.", StringComparison.Ordinal)).ToList();
        Assert.Contains("coodsz.clancap", adds[^1]);

        var map = File.ReadAllText(Path.Combine(root, "compat", "CoodszServerTools", "src", "MapCommands.cs"));
        foreach (var change in new[] { "Position =", "Position2D =", "Teleport", "SetMove", "Apply(", "SetValue", "Invoke(" })
            Assert.DoesNotContain(change, map);
        Assert.Contains("ToFile(\"map\", lines)", map);
        Assert.Equal("v1.8.1", ToolsCommand.ModuleVersion);
    }

    [Fact]
    public void Every_word_the_map_draws_itself_is_translated()
    {
        // The map draws these on its canvas and in its side panel (not walked by the translator).
        var words = new HashSet<string>(StringComparer.Ordinal)
        {
            "Town", "Castle", "Village", "Player", "Lord", "Army leader", "Troops", "Wounded", "Army", "Now", "Owner",
            "Bound to", "Parties inside", "Siege", "Under siege", "No kingdom", "Centre the map", "Open profile",
            "Open in Encyclopedia", "Travelling", "Fighting in battle", "Laying siege", "Staying in {0}", "In the army of {0}",
            "On the way to {0}", "Near {0}", "Player {0}", "{0} players and {1} lords on the map",
            "{0} has no party on the map right now (captured, in a settlement without a party, or not on the map yet).",
            "{0} is not on this map.", "The map is being read. It goes to {0} once it is drawn.", "Find a player, lord or settlement...",
            LiveMap.StoppedHeadline, LiveMap.StoppedLine, "The map could not be read", "Reading the map from the server...",
        };
        foreach (var language in BannerlordCoopLauncher.Core.Localization.Translator.Languages.Where(l => l.Code != "en"))
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language.Code);
            var missing = words.Where(w => !table.ContainsKey(w)).ToList();
            Assert.True(missing.Count == 0, language.Code + " is missing: " + string.Join(" | ", missing));
        }

        // Every activity sentence is one of those templates.
        var map = Map();
        foreach (var party in map.Parties)
        {
            var text = LiveMap.Activity(map, party);
            Assert.Contains(words, w => w == text || (w.EndsWith("{0}", StringComparison.Ordinal) && text.StartsWith(w[..^3], StringComparison.Ordinal)));
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Show_on_map_finds_a_hero_by_game_or_registry_id()
    {
        var map = Map();
        Assert.Equal("p_3", LiveMap.FindHero(map, "lord_1_1")!.PartyId);
        Assert.Equal("p_3", LiveMap.FindHero(map, "Hero_lord_1_1")!.PartyId);
        Assert.Equal("p_1", LiveMap.FindHero(map, "hero_player2863")!.PartyId);
        Assert.Null(LiveMap.FindHero(map, "nobody"));
        Assert.Null(LiveMap.FindHero(map, ""));
        var counts = LiveMap.Counts(map);
        Assert.Equal((2, 5, 1), counts);
    }

    [Fact]
    public void Streamer_names_are_the_same_on_every_page()
    {
        // Names nobody else uses, since the numbers last for the whole run.
        var first = "Restyle Tester " + Guid.NewGuid().ToString("N");
        var second = "Restyle Tester " + Guid.NewGuid().ToString("N");
        var alias = StreamerNames.Alias(first);
        Assert.Matches(@"^Player \d+$", alias);
        Assert.Equal(alias, StreamerNames.Alias(first.ToUpperInvariant()));
        Assert.Equal(alias, StreamerNames.Name(" " + first + " ", true));
        Assert.NotEqual(alias, StreamerNames.Alias(second));
        Assert.Equal(first, StreamerNames.Name(first, false));
        Assert.Equal("", StreamerNames.Name("", true));

        // The map uses the same numbers; lords keep their names.
        var map = Map();
        var coodsz = map.PartyByHero["Hero_Player2863"];
        Assert.Equal(StreamerNames.Alias("Coodsz"), LiveMap.ShownName(map, coodsz, true));
        Assert.Equal("Coodsz", LiveMap.ShownName(map, coodsz, false));
        Assert.Equal("Lucon", LiveMap.ShownName(map, map.PartyByHero["lord_1_1"], true));
    }
}
