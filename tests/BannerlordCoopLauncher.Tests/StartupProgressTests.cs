using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class StartupProgressTests
{
    [Fact]
    public void Map_cache_build_shows_the_newest_step()
    {
        // Lines from a real TAOM host log (new campaign, TAOM_Map distance cache rejected).
        const string log =
            "22:57:47.644  [DedicatedServer] distance cache rejected (..\\..\\Modules\\TAOM_Map/ModuleData\\DistanceCaches\\settlements_distance_cache_Default.bin): navigation CRC 0xAAB3625A != 0x7F5C0413\n"
            + "22:57:47.645  [DedicatedServer] generating native distance cache for 1002 campaign settlements; first startup may take several minutes\n"
            + "23:13:17.268  [DedicatedServer Engine] Neighbor cache progress for navigation Default, current index: 0  - total count: 221\n"
            + "23:18:27.863  [DedicatedServer Engine] Neighbor cache progress for navigation Default, current index: 35  - total count: 221\n";

        var text = StartupProgress.Describe(log);

        Assert.NotNull(text);
        Assert.StartsWith("Building the map cache: 36 of 221 (16%).", text);
        Assert.Contains("Do not stop the server", text);
    }

    [Fact]
    public void Distance_generation_before_the_first_step_still_says_it_is_building()
    {
        var text = StartupProgress.Describe("[DedicatedServer] generating native distance cache for 1002 campaign settlements");
        Assert.StartsWith("Building the map's travel distances.", text);
    }

    [Fact]
    public void Normal_loading_has_no_progress_text()
    {
        Assert.Null(StartupProgress.Describe("[DedicatedServer] join port: UDP 4200"));
        Assert.Null(StartupProgress.Describe(""));
    }

    // Lines from coop-server-20261017-232448.log, a live new TAOM campaign.
    private const string SettlementLog =
        "[DedicatedServer] distance cache rejected (..\\..\\Modules\\TAOM_Map/ModuleData\\DistanceCaches\\settlements_distance\n"
        + "[DedicatedServer] distance cache for 1002 campaign settlements; first startup may take several minutes\n"
        + "[DedicatedServer Engine] Settlement to settlement cache creation index 386,    total count: 1002     Default\n"
        + "[DedicatedServer Engine] Settlement to settlement cache creation index 387,    total count: 1002     Default\n";

    [Fact]
    public void Settlement_cache_build_shows_the_newest_step()
    {
        var text = StartupProgress.Describe(SettlementLog);

        Assert.NotNull(text);
        Assert.StartsWith("Building the distances between settlements: 388 of 1002 (38%).", text);
        Assert.Contains("Do not stop the server", text);
    }

    [Fact]
    public void Settlement_cache_fills_the_first_part_of_the_bar()
    {
        var step = StartupProgress.Measure(SettlementLog);

        Assert.NotNull(step);
        Assert.Equal("settlements", step.Stage);
        Assert.InRange(step.Fraction!.Value, 0.21, 0.22);
    }

    [Fact]
    public void Neighbor_cache_fills_the_rest_of_the_bar()
    {
        var step = StartupProgress.Measure(SettlementLog
            + "[DedicatedServer Engine] Neighbor cache progress for navigation Default, current index: 220  - total count: 221\n");

        Assert.NotNull(step);
        Assert.Equal("neighbors", step.Stage);
        Assert.Equal(0.95, step.Fraction!.Value, 3);
    }

    [Fact]
    public void State_events_give_fixed_steps_and_serving_fills_the_bar()
    {
        Assert.Equal(0.03, StartupProgress.Measure("@DS@{\"ev\":\"state\",\"phase\":\"boot\"}\n")!.Fraction);
        Assert.Equal(0.08, StartupProgress.Measure("@DS@{\"ev\":\"state\",\"phase\":\"loading\",\"save\":\"a\"}\n")!.Fraction);
        Assert.Equal(1, StartupProgress.Measure(SettlementLog + "@DS@{\"ev\":\"state\",\"phase\":\"serving\"}\n")!.Fraction);
    }

    [Fact]
    public void Unknown_start_is_indeterminate_and_fatal_hides_the_bar()
    {
        var unknown = StartupProgress.Measure("[DedicatedServer] join port: UDP 4200\n");
        Assert.NotNull(unknown);
        Assert.Null(unknown.Fraction);
        Assert.Null(StartupProgress.Measure("@DS@{\"ev\":\"state\",\"phase\":\"fatal\",\"detail\":\"x\"}\n"));
        Assert.Null(StartupProgress.Measure(""));
    }
}
