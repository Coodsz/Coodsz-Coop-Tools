using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat: the Burning Empires team wants no elephants in tournaments, so the
// match equipment and the prize lists drop elephant mounts and harnesses in co-op. Covers which
// mount or harness counts as elephant gear (TournamentElephantRules.cs, linked).
public sealed class BurningEmpiresTournamentElephantTests
{
    [Fact]
    public void The_elephant_mount_is_elephant_gear()
    {
        Assert.True(TournamentElephantRules.IsElephantGear("elephant", "elephant"));
    }

    [Fact]
    public void A_mount_on_the_elephant_monster_is_elephant_gear_whatever_its_id()
    {
        Assert.True(TournamentElephantRules.IsElephantGear("be_war_beast", "elephant"));
    }

    [Fact]
    public void An_elephant_harness_is_elephant_gear()
    {
        Assert.True(TournamentElephantRules.IsElephantGear("be_elephant_armor_kushan_tier_1", null));
    }

    [Fact]
    public void A_normal_horse_and_harness_are_not_elephant_gear()
    {
        Assert.False(TournamentElephantRules.IsElephantGear("sumpter_horse", "horse"));
        Assert.False(TournamentElephantRules.IsElephantGear("half_scale_barding", null));
    }

    [Fact]
    public void An_empty_slot_is_not_elephant_gear()
    {
        Assert.False(TournamentElephantRules.IsElephantGear(null, null));
        Assert.False(TournamentElephantRules.IsElephantGear("", ""));
    }

    [Fact]
    public void A_fighter_loses_an_elephant_mount_and_harness_but_keeps_a_horse_or_camel()
    {
        Assert.Equal((true, true), TournamentElephantRules.FighterSlotsToClear("be_war_elephant", "elephant", "be_elephant_armor_kushan_tier_1"));
        Assert.Equal((true, true), TournamentElephantRules.FighterSlotsToClear("be_war_elephant", "elephant", null));
        Assert.Equal((false, true), TournamentElephantRules.FighterSlotsToClear("camel", "camel", "be_elephant_armor_kushan_tier_1"));
        Assert.Equal((false, false), TournamentElephantRules.FighterSlotsToClear("sumpter_horse", "horse", "half_scale_barding"));
        Assert.Equal((false, false), TournamentElephantRules.FighterSlotsToClear("camel", "camel", "camel_saddle"));
        Assert.Equal((false, false), TournamentElephantRules.FighterSlotsToClear(null, null, null));
    }

    [Fact]
    public void The_prizes_lose_elephant_mounts_and_harnesses_and_keep_horses_camels_and_other_gear()
    {
        var prizes = new List<(string Id, string? Monster, bool MountOrHarness)>
        {
            ("be_war_elephant", "elephant", true),
            ("be_elephant_armor_kushan_tier_1", null, true),
            ("t3_aserai_horse", "horse", true),
            ("camel", "camel", true),
            ("half_scale_barding", null, true),
            ("elephant_tusk_dagger", null, false),
        };

        var kept = prizes.Where(prize => !TournamentElephantRules.IsElephantPrize(prize.Id, prize.Monster, prize.MountOrHarness)).Select(prize => prize.Id);

        Assert.Equal(new[] { "t3_aserai_horse", "camel", "half_scale_barding", "elephant_tusk_dagger" }, kept);
    }

    [Fact]
    public void The_guard_says_in_one_line_what_it_took()
    {
        Assert.Equal("Tournament elephants: took 1 elephant mount and 2 elephant harnesses off 2 fighters in this tournament match.",
            TournamentElephantRules.MatchLine(2, 1, 2));
        Assert.Equal("Tournament elephants: dropped 2 elephant prizes from the tournament prizes (be_war_elephant, be_elephant_armor_kushan_tier_1).",
            TournamentElephantRules.PrizeLine(new[] { "be_war_elephant", "be_elephant_armor_kushan_tier_1" }));
    }
}
