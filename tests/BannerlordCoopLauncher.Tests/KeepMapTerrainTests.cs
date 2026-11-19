using BannerlordCoopLauncher.Core.Keep;

namespace BannerlordCoopLauncher.Tests;

// The real shape of the campaign map (coodsz.terrain): the run length rows, the paper map's kinds
// of the game's terrain types, the read a few rows at a time, and the copy kept on this PC.
public sealed class KeepMapTerrainTests
{
    private static MapData World() => MapParser.Parse(new[]
    {
        "MAPKINGDOM|vlandia|Vlandia|FF8C1A1A|FFF0D070",
        "MAPSETTLEMENT|town_V1|Pravend|town|200|300|clan_2|Dey Meroc|vlandia|FF8C1A1A||0",
        "MAPSETTLEMENT|town_V2|Sargot|town|260|330|clan_2|Dey Meroc|vlandia|FF8C1A1A||0",
    });

    // A 4 x 3 grid over 0..400 x 0..300: sea in the west, plain and forest, a river, cells with no mesh by the sea and inland.
    private static readonly string[] Rows =
    {
        "TROW|0|t2b2",
        "TROW|1|sbel",
        "TROW|2|s.b.",
    };

    private static string Header(int from, int count) => "TERRAIN|0|0|400|300|4|3|" + from + "|" + count + "|12";

    [Fact]
    public void Rows_are_read_as_game_terrain_types_with_north_first()
    {
        var terrain = TerrainParser.Parse(new[] { Header(0, 3) }.Concat(Rows).Append("COODSZ_END"))!;
        Assert.True(terrain.IsComplete);
        Assert.Equal(new MapBounds(0, 0, 400, 300), terrain.Bounds);
        Assert.Equal(100, terrain.CellSize);
        // Row 0 is the north edge (y 200..300).
        Assert.Equal(TerrainKind.DeepWater, terrain.KindAt(50, 250));
        Assert.Equal(TerrainKind.Land, terrain.KindAt(350, 250));
        Assert.Equal(TerrainKind.Water, terrain.KindAt(50, 150));
        Assert.Equal(TerrainKind.Forest, terrain.KindAt(250, 150));
        Assert.Equal(TerrainKind.River, terrain.KindAt(350, 150));
        // No mesh next to the sea is open sea; no mesh inland is land nobody can walk on.
        Assert.Equal(TerrainKind.DeepWater, terrain.KindAt(150, 50));
        Assert.Equal(TerrainKind.Impassable, terrain.KindAt(350, 50));
        Assert.Equal(TerrainKind.Unknown, terrain.KindAt(-10, 50));
        Assert.Equal(TerrainKind.Unknown, terrain.KindAt(50, 301));
    }

    [Fact]
    public void Game_terrain_types_map_to_water_land_and_tints()
    {
        Assert.Equal(TerrainKind.Land, MapTerrain.Kind(1));
        Assert.Equal(TerrainKind.Desert, MapTerrain.Kind(2));
        Assert.Equal(TerrainKind.Snow, MapTerrain.Kind(3));
        Assert.Equal(TerrainKind.Mountain, MapTerrain.Kind(7));
        Assert.Equal(TerrainKind.Water, MapTerrain.Kind(8));
        Assert.Equal(TerrainKind.Water, MapTerrain.Kind(10));
        Assert.Equal(TerrainKind.River, MapTerrain.Kind(11));
        Assert.Equal(TerrainKind.Water, MapTerrain.Kind(18));
        Assert.Equal(TerrainKind.DeepWater, MapTerrain.Kind(19));
        Assert.Equal(TerrainKind.Beach, MapTerrain.Kind(20));
        Assert.Equal(TerrainKind.Impassable, MapTerrain.Kind(MapTerrain.NoFace));
        Assert.True(MapTerrain.IsWater(TerrainKind.DeepWater));
        Assert.False(MapTerrain.IsWater(TerrainKind.River));
    }

    [Fact]
    public void Broken_rows_and_headers_are_not_taken()
    {
        Assert.Null(TerrainParser.Parse(new[] { "TERRAIN|0|0|0|300|4|3|0|3|1" }));
        Assert.Null(TerrainParser.Parse(Rows));
        // A row with too many cells is left unread.
        var terrain = TerrainParser.Parse(new[] { Header(0, 3), "TROW|0|b9", "TROW|1|b4" })!;
        Assert.False(terrain.IsRowRead(0));
        Assert.True(terrain.IsRowRead(1));
        Assert.Equal(0, TerrainParser.NextRow(terrain));
        // A second answer for another grid does not mix in.
        Assert.Null(TerrainParser.Parse(new[] { "TERRAIN|0|0|400|300|8|6|0|6|1" }, terrain));
    }

    [Fact]
    public void Written_grid_reads_back_the_same()
    {
        var terrain = TerrainParser.Parse(new[] { Header(0, 3) }.Concat(Rows))!;
        var again = TerrainParser.Parse(TerrainParser.Write(terrain).Split('\n'))!;
        Assert.True(again.IsComplete);
        Assert.Equal(terrain.Cells, again.Cells);
        Assert.Equal(terrain.Bounds, again.Bounds);
    }

    [Fact]
    public async Task Client_reads_the_terrain_a_few_rows_at_a_time_and_keeps_a_copy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-terrain-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sent = new List<string>();
            var file = "";
            var client = new KeepClient(command =>
            {
                sent.Add(command);
                var parts = command.Split(' ');
                var from = int.Parse(parts[2]);
                // The server answers two rows at most, like a chunked read.
                var count = Math.Min(2, 3 - from);
                file = string.Join("\n", new[] { Header(from, count) }.Concat(Rows.Skip(from).Take(count)).Append("COODSZ_END"));
                return Task.FromResult<IReadOnlyList<string>>(new[] { @"COODSZ_FILE|terrain|C:\s\out\terrain.txt|3", "COODSZ_END" });
            }, _ => file);

            var map = World();
            var (terrain, message) = await client.TerrainAsync(map, folder);
            Assert.Equal("", message);
            Assert.NotNull(terrain);
            Assert.True(terrain!.IsComplete);
            Assert.Equal(2, sent.Count);
            Assert.Equal("coodsz.terrain " + KeepClient.TerrainColumns + " 0 " + KeepClient.TerrainRowsPerRead, sent[0]);
            Assert.StartsWith("coodsz.terrain " + KeepClient.TerrainColumns + " 2 ", sent[1]);

            // The same world again comes from the copy, without asking the server.
            var (cached, _) = await client.TerrainAsync(map, folder);
            Assert.Equal(2, sent.Count);
            Assert.Equal(terrain.Cells, cached!.Cells);
            Assert.StartsWith("terrain-", TerrainCache.Key(map));
            Assert.Equal("", TerrainCache.Key(MapData.Empty));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Old_tools_give_no_terrain_and_no_reason_and_a_missing_answer_says_why()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-terrain-" + Guid.NewGuid().ToString("N"));
        var old = new KeepClient(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "Unknown command: coodsz.terrain" }), _ => null);
        var (none, why) = await old.TerrainAsync(World(), folder);
        Assert.Null(none);
        Assert.Equal("", why);

        var silent = new KeepClient(_ => Task.FromResult<IReadOnlyList<string>>(new[] { "COODSZ_END" }), _ => null);
        Assert.Equal(KeepClient.NoAnswer, (await silent.TerrainAsync(World(), folder)).Message);
        Assert.False(Directory.Exists(folder));

        Assert.Equal("coodsz.terrain 600 0 60", ToolsCommand.Terrain(600, 0, 60));
        Assert.Equal("coodsz.terrain 2000 0 1", ToolsCommand.Terrain(99999, -5, 0));
    }

    [Fact]
    public void Terrain_command_is_read_only_and_registered_last()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var src = Path.Combine(dir!.FullName, "compat", "CoodszServerTools", "src");
        var module = File.ReadAllText(Path.Combine(src, "ToolsSubModule.cs"));
        Assert.Contains("Add(\"coodsz.terrain\", Safe(TerrainCommands.Terrain));", module);
        var terrain = File.ReadAllText(Path.Combine(src, "TerrainCommands.cs"));
        foreach (var change in new[] { "Position =", "Teleport", "Apply(", "SetValue", "Invoke(", "AddNewEntity", "SetSceneLevels" })
            Assert.DoesNotContain(change, terrain);
        Assert.Contains("ToFile(\"terrain\", lines)", terrain);
    }
}
