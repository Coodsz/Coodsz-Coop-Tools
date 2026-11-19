using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat on the dedicated server's Burning Empires map: Coop's coop_team clan
// names town_S1 as home, which Burning Empires' map does not have, and the path query for its
// stand in reached the engine with face -1 and killed the server. Covers the town such a clan
// gets (ClanHomeRules.cs), which path queries skip the engine (PathQueryRules.cs) and how far
// the server map scene's Calradia borders widen (MapBoundsRules.cs), all linked.
public sealed class BurningEmpiresServerMapTests
{
    private sealed record Place(string Id, bool IsTown, string? Culture);

    private static readonly MapBoundsRules.Bounds Calradia = new(62f, 30f, 790f, 640f);

    private static Place? Pick(IEnumerable<Place> places, string? clanCulture) =>
        ClanHomeRules.PickTown(places, p => p.IsTown, p => p.Culture!, clanCulture!);

    private static KeyValuePair<float, float> At(float x, float y) => new(x, y);

    [Fact]
    public void A_home_that_names_a_settlement_the_map_never_built_is_missing()
    {
        var standIn = new object();
        Assert.True(ClanHomeRules.IsMissingSettlement(standIn, null!));
        Assert.False(ClanHomeRules.IsMissingSettlement(standIn, new object()));
        // No home at all is how the game leaves bandits and the like, so it is not touched.
        Assert.False(ClanHomeRules.IsMissingSettlement(null!, null!));
    }

    [Fact]
    public void A_clan_gets_the_first_town_of_its_own_culture()
    {
        var places = new[]
        {
            new Place("castle_rome", false, "westroman"),
            new Place("town_rome", true, "westroman"),
            new Place("village_goth", false, "goth"),
            new Place("town_goth_a", true, "goth"),
            new Place("town_goth_b", true, "goth"),
        };

        Assert.Equal("town_goth_a", Pick(places, "goth")!.Id);
    }

    [Fact]
    public void Without_a_town_of_its_culture_the_clan_gets_the_first_town()
    {
        // coop_team is Sturgian and Burning Empires has no Sturgian towns.
        var places = new[]
        {
            new Place("village_rome", false, "sturgia"),
            new Place("town_rome", true, "westroman"),
            new Place("town_goth", true, "goth"),
        };

        Assert.Equal("town_rome", Pick(places, "sturgia")!.Id);
        Assert.Equal("town_rome", Pick(places, null)!.Id);
        Assert.Equal("town_rome", Pick(places, "")!.Id);
    }

    [Fact]
    public void A_map_with_no_towns_gives_no_home()
    {
        Assert.Null(Pick(new[] { new Place("village", false, "goth") }, "goth"));
        Assert.Null(Pick(Array.Empty<Place>(), "goth"));
        Assert.Null(ClanHomeRules.PickTown<Place>(null!, p => p.IsTown, p => p.Culture!, "goth"));
    }

    [Fact]
    public void A_path_query_with_a_missing_end_skips_the_engine()
    {
        Assert.Equal(PathQueryRules.Unsafe.NoSettlement, PathQueryRules.Check(false, false, 12));
        Assert.Equal(PathQueryRules.Unsafe.NoComponent, PathQueryRules.Check(true, false, 12));
        Assert.Equal(PathQueryRules.Unsafe.NoFace, PathQueryRules.Check(true, true, -1));
        Assert.Equal(PathQueryRules.Unsafe.None, PathQueryRules.Check(true, true, 0));
        Assert.Equal(PathQueryRules.Unsafe.None, PathQueryRules.Check(true, true, 4711));
    }

    [Fact]
    public void Either_end_of_a_pair_decides_and_a_skipped_pair_is_far_apart_over_land()
    {
        var safe = PathQueryRules.Unsafe.None;
        Assert.Equal(PathQueryRules.Unsafe.NoComponent, PathQueryRules.CheckPair(PathQueryRules.Unsafe.NoComponent, safe));
        Assert.Equal(PathQueryRules.Unsafe.NoFace, PathQueryRules.CheckPair(safe, PathQueryRules.Unsafe.NoFace));
        Assert.Equal(safe, PathQueryRules.CheckPair(safe, safe));
        Assert.Equal(float.MaxValue, PathQueryRules.SkippedDistance);
        Assert.Equal(1f, PathQueryRules.SkippedLandRatio);
    }

    [Fact]
    public void Burning_Empires_settlements_widen_the_Calradia_borders_with_a_margin()
    {
        var settlements = new[] { At(72f, 900f), At(1650f, 607f), At(800f, 1324f) };

        Assert.True(MapBoundsRules.TryWiden(Calradia, settlements, 50f, out var widened));
        Assert.Equal(22f, widened.MinX);
        Assert.Equal(30f, widened.MinY);
        Assert.Equal(1700f, widened.MaxX);
        Assert.Equal(1374f, widened.MaxY);
        Assert.True(widened.Diagonal > Calradia.Diagonal * 2);
        Assert.Equal("(22,30)-(1700,1374)", widened.ToString());
    }

    [Fact]
    public void Calradia_settlements_inside_the_borders_keep_them()
    {
        var settlements = new[] { At(100f, 60f), At(780f, 630f), At(400f, 300f) };

        Assert.False(MapBoundsRules.TryWiden(Calradia, settlements, 50f, out var kept));
        Assert.Equal(Calradia, kept);
        Assert.False(MapBoundsRules.TryWiden(Calradia, null!, 50f, out _));
        Assert.False(MapBoundsRules.TryWiden(Calradia, new[] { At(float.NaN, 2000f), At(3000f, float.PositiveInfinity) }, 50f, out _));
    }

    [Fact]
    public void Widened_borders_never_go_below_zero()
    {
        Assert.True(MapBoundsRules.TryWiden(Calradia, new[] { At(10f, 5f) }, 50f, out var widened));
        Assert.Equal(0f, widened.MinX);
        Assert.Equal(0f, widened.MinY);
        Assert.Equal(790f, widened.MaxX);
        Assert.Equal(640f, widened.MaxY);
    }

    [Fact]
    public void Path_costs_follow_the_map_diagonal_like_the_game()
    {
        // Calradia's diagonal from the server's borders, with vanilla's land to sea cost of 2.
        MapBoundsRules.PathCosts(Calradia.Diagonal, 2, out var switchCost, out var limit);
        Assert.Equal((int)(Calradia.Diagonal * 2 * 0.2f), switchCost);
        Assert.Equal(Math.Max(switchCost * 100, (int)(Calradia.Diagonal * 500f)), limit);

        MapBoundsRules.PathCosts(2000f, 2, out var bigSwitch, out var bigLimit);
        Assert.Equal(800, bigSwitch);
        Assert.Equal(1_000_000, bigLimit);
        Assert.True(bigLimit > limit);
    }

    [Fact]
    public void The_terrain_size_reaches_the_new_borders_so_the_weather_grid_stays_in_range()
    {
        Assert.Equal(1700f, MapBoundsRules.CoverTerrain(848f, 1700f));
        Assert.Equal(848f, MapBoundsRules.CoverTerrain(848f, 790f));

        // The weather grid is 32 by 32 cells over the terrain size; every settlement's cell must fit.
        var size = MapBoundsRules.CoverTerrain(848f, 1374f);
        Assert.True((int)(1324f / (size / 32f)) < 32);
        Assert.False((int)(1324f / (848f / 32f)) < 32);
    }
}
