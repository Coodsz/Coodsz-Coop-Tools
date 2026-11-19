using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat on the dedicated server: Burning Empires sorts each notable's
// volunteer slots with the empty ones last after its daily reroll, so a player whose recruit
// screen stayed open sent old slot indices and Coop dropped the recruits it found empty. Covers
// how the rerolled troops go back into the slots that held a troop before (VolunteerSlotRules.cs,
// linked).
public sealed class BurningEmpiresVolunteerSlotTests
{
    private static string?[] Keep(string?[] before, string?[] after) =>
        VolunteerSlotRules.KeepSlotPositions(before, after)!;

    [Fact]
    public void Rerolled_troops_go_back_into_the_slots_that_held_a_troop()
    {
        var before = new[] { null, null, null, "A", "B", "C" };
        var after = new[] { "A2", "B2", "C2", null, null, null };

        Assert.Equal(new[] { null, null, null, "A2", "B2", "C2" }, Keep(before, after));
    }

    [Fact]
    public void Extra_troops_fill_the_free_slots_in_order()
    {
        var before = new[] { null, "A", null, "B", null, null };
        var after = new[] { "A2", "B2", "C2", "D2", null, null };

        Assert.Equal(new[] { "C2", "A2", "D2", "B2", null, null }, Keep(before, after));
    }

    [Fact]
    public void Fewer_troops_fill_the_old_slots_first_and_leave_the_rest_empty()
    {
        var before = new[] { "A", null, "B", "C", null, null };
        var after = new[] { "A2", "B2", null, null, null, null };

        Assert.Equal(new[] { "A2", null, "B2", null, null, null }, Keep(before, after));
    }

    [Fact]
    public void All_empty_slots_stay_empty()
    {
        var before = new string?[6];
        var after = new string?[6];

        Assert.Equal(new string?[6], Keep(before, after));
    }

    [Fact]
    public void Slots_the_reroll_did_not_move_are_left_as_they_were()
    {
        var before = new[] { "A", null, "B", null, "C", "D" };
        var after = (string?[])before.Clone();

        Assert.Equal(before, Keep(before, after));
    }

    [Fact]
    public void No_troop_is_dropped_when_the_slot_counts_differ()
    {
        // A missing notable copy or a resized array must never lose a rerolled troop.
        var before = new[] { null, "A", null };
        var after = new[] { "A2", "B2", "C2", "D2", null };

        var kept = Keep(before, after);

        Assert.Equal(new[] { "B2", "A2", "C2", "D2", null }, kept);
        Assert.Null(VolunteerSlotRules.KeepSlotPositions<string>(null!, after));
    }
}
