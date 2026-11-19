using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

public class SharedApplyTests
{
    [Fact]
    public void Extra_mods_move_across_and_the_working_list_stays()
    {
        var installed = new[] { "Bannerlord.Harmony", "Native", "Coop", "StoryMode", "ExtraA", "ExtraB" };
        var enabled = new[] { "Bannerlord.Harmony", "Native", "Coop", "ExtraA" };
        var shared = new[] { "Bannerlord.Harmony", "Native", "Coop", "StoryMode", "ExtraB", "MissingMod" };
        var working = new[] { "Bannerlord.Harmony", "Native", "Coop", "StoryMode" };

        var result = SharedModList.ApplyExtras(installed, enabled, shared, working, new[] { "StoryMode" });

        Assert.Equal(new[] { "Bannerlord.Harmony", "Native", "Coop", "StoryMode", "ExtraB", "ExtraA" }, result.Order);
        Assert.Equal(new[] { "ExtraB" }, result.TurnedOn);
        Assert.Equal(new[] { "ExtraA" }, result.TurnedOff);
        Assert.Equal(new[] { "MissingMod" }, result.Missing);
        Assert.Equal(new[] { "StoryMode" }, result.LeftOff);
        Assert.Contains("Coop", result.Enabled);
        Assert.DoesNotContain("StoryMode", result.Enabled);
        Assert.DoesNotContain("ExtraA", result.Enabled);
        Assert.Contains("ExtraB", result.Enabled);
    }

    [Fact]
    public void CoopNightly_in_the_file_does_not_become_a_second_coop()
    {
        var installed = new[] { "Native", "Coop" };
        var result = SharedModList.ApplyExtras(installed, installed, new[] { "Native", "CoopNightly" }, new[] { "Native", "Coop" }, Array.Empty<string>());

        Assert.Equal(new[] { "Native", "Coop" }, result.Order);
        Assert.Empty(result.TurnedOn);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void The_guide_tells_both_directions_and_where_the_file_goes()
    {
        Assert.Contains("coodsz-mod-list.json", SharedModList.Intro);
        Assert.Contains("Desktop", SharedModList.Intro);
        var toServer = SharedModList.ClientStepsFor("Coodsz Coop and Friends", "Coodsz Host");
        Assert.StartsWith("1. In Coodsz Coop and Friends, click Share mod list", toServer);
        Assert.Contains("2. In Coodsz Host, open Modules", toServer);
        Assert.Contains("Save module config", toServer);
        Assert.Contains("2. In Coodsz Coop and Friends, click Import mod list", SharedModList.ServerStepsFor("Coodsz Host", "Coodsz Coop and Friends"));
        Assert.DoesNotContain("only checked", SharedModList.ExtraModsClient + SharedModList.ExtraModsServer);
    }
}
