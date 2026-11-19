using Coodsz.ServerRules;

namespace BannerlordCoopLauncher.Tests;

// My Little Warband recruits: the cart rewrite before Coop's HandleOnRecruitmentDone and the troop swap after it,
// as the Coodsz Server Tools module runs them on the server (MlwRecruitRules.cs, linked). A small fake stands in for
// Coop's object manager (Coop ids to objects and back, so the ids on the wire are Coop's, not StringIds), the
// notables' slots, the party roster and Coop's own commit.
public sealed class MlwRecruitTests
{
    private const string Notable = "Hero_notable_1";

    private sealed class Troop
    {
        public Troop(string stringId) => StringId = stringId;
        public string StringId { get; }
    }

    private static readonly Troop Recruit = new("imperial_recruit");
    private static readonly Troop Vigla = new("imperial_vigla_recruit");
    private static readonly Troop Infantry = new("imperial_infantryman");
    private static readonly Troop Basic = new("_basic_root_p01");
    private static readonly Troop Basic2 = new("_basic_root_p010");
    private static readonly Troop Elite = new("_elite_root_p0111");
    private static readonly Troop Made = new("_basic_root_p0101");

    // Coop's ids as they arrive: the registry's "CharacterObject_" + StringId for most.
    private const string RecruitId = "CharacterObject_imperial_recruit";
    private const string ViglaId = "CharacterObject_imperial_vigla_recruit";
    private const string InfantryId = "CharacterObject_imperial_infantryman";
    private const string BasicId = "CharacterObject__basic_root_p01";
    private const string Basic2Id = "CharacterObject__basic_root_p010";
    private const string EliteId = "CharacterObject__elite_root_p0111";
    private const string MadeId = "CharacterObject__basic_root_p0101";

    private sealed class FakeCoop : IMlwRecruitWorld
    {
        public readonly Dictionary<string, Troop> Objects = new();
        public readonly Dictionary<Troop, string> Ids = new();
        public readonly Dictionary<string, Troop?[]> Slots = new();

        public FakeCoop()
        {
            Register(RecruitId, Recruit);
            Register(ViglaId, Vigla);
            Register(InfantryId, Infantry);
            Register(BasicId, Basic);
            Register(Basic2Id, Basic2);
            Register(EliteId, Elite);
            Register(MadeId, Made);
        }

        public void Register(string id, Troop troop)
        {
            Objects[id] = troop;
            Ids[troop] = id;
        }

        public void Forget(Troop troop)
        {
            Objects.Remove(Ids[troop]);
            Ids.Remove(troop);
        }

        public Troop? Resolve(string? id) => id != null && Objects.TryGetValue(id, out var troop) ? troop : null;

        public bool TryCartTroop(string troopId, out bool custom)
        {
            var troop = Resolve(troopId);
            custom = troop != null && MlwRecruitRules.IsCustomId(troop.StringId);
            return troop != null;
        }

        public int SlotCount(string heroId) => Slots.TryGetValue(heroId, out var s) ? s.Length : 0;

        public bool TrySlotTroop(string heroId, int index, out string? troopId, out bool custom)
        {
            var troop = Slots[heroId][index];
            troopId = troop != null && Ids.TryGetValue(troop, out var id) ? id : null;
            custom = troop != null && MlwRecruitRules.IsCustomId(troop.StringId);
            return troop != null;
        }

        public bool SameTroop(string troopId, string heroId, int index) => ReferenceEquals(Resolve(troopId), Slots[heroId][index]);

        // HandleOnRecruitmentDone: resolve both ids, the cart troop must be the slot's troop object, each slot once,
        // all or nothing on gold; each recruit empties its slot, adds one healthy troop and the perk xp.
        public void Commit(Roster roster, MlwCartEntry[] cart, bool enoughGold = true, int perkXp = 0)
        {
            var taken = new HashSet<(string, int)>();
            var valid = new List<(string Hero, int Index, Troop Troop)>();
            foreach (var (heroId, troopId, index) in cart)
            {
                var troop = Resolve(troopId);
                if (heroId is null || troop is null || index < 0 || index >= SlotCount(heroId)) continue;
                if (!ReferenceEquals(Slots[heroId][index], troop) || !taken.Add((heroId, index))) continue;
                valid.Add((heroId, index, troop));
            }

            if (!enoughGold) return;
            foreach (var (heroId, index, troop) in valid)
            {
                Slots[heroId][index] = null;
                roster.AddTroop(troop, 1, perkXp);
            }
        }
    }

    // The party roster, by object: a Coop id goes to the object it resolves to, like the module's PartyRoster.
    private sealed class Roster : IMlwRoster
    {
        private readonly FakeCoop _coop;
        public readonly Dictionary<Troop, (int Count, int Xp)> Stacks = new();
        public readonly List<string> Calls = new();
        public Troop? ThrowOn;

        public Roster(FakeCoop coop) => _coop = coop;

        public int Count(string troopId) => Get(Troop(troopId)).Count;
        public int Xp(string troopId) => Get(Troop(troopId)).Xp;

        public void Add(string troopId, int count, int xpChange)
        {
            var troop = Troop(troopId);
            if (troop == ThrowOn) throw new InvalidOperationException("roster");
            Calls.Add(troop.StringId + (count > 0 ? " +" : " ") + count + " xp " + xpChange);
            AddTroop(troop, count, xpChange);
        }

        public void AddTroop(Troop troop, int count, int xpChange)
        {
            var (c, x) = Get(troop);
            c += count;
            if (c == 0) Stacks.Remove(troop);
            else Stacks[troop] = (c, Math.Max(0, x + xpChange));
        }

        public (int Count, int Xp) Get(Troop troop) => Stacks.TryGetValue(troop, out var s) ? s : (0, 0);

        private Troop Troop(string id) => _coop.Resolve(id) ?? throw new KeyNotFoundException(id);
    }

    private static FakeCoop Coop(params Troop?[] slots)
    {
        var coop = new FakeCoop();
        coop.Slots[Notable] = slots;
        return coop;
    }

    private static MlwCartEntry[] Rewrite(FakeCoop coop, List<MlwMapped> mapped, params MlwCartEntry[] cart) =>
        MlwRecruitRules.Rewrite(cart, coop, mapped, out _);

    // What the prefix does: rewrite, then note each mapped normal troop's count and xp.
    private static (List<MlwMapped> Mapped, Dictionary<string, (int, int)> Before, MlwCartEntry[] Cart) Prepare(FakeCoop coop, Roster roster, params MlwCartEntry[] cart)
    {
        var mapped = new List<MlwMapped>();
        var rewritten = Rewrite(coop, mapped, cart);
        var before = mapped.Select(m => m.NativeId).Distinct().ToDictionary(id => id, id => (roster.Count(id), roster.Xp(id)));
        return (mapped, before, rewritten);
    }

    [Fact]
    public void A_custom_entry_is_rewritten_to_the_Coop_id_of_the_slots_normal_troop()
    {
        var coop = Coop(Recruit, Vigla);
        var mapped = new List<MlwMapped>();
        var cart = Rewrite(coop, mapped, new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, EliteId, 1));
        Assert.Equal(new MlwCartEntry(Notable, RecruitId, 0), cart[0]);
        Assert.Equal(new MlwCartEntry(Notable, ViglaId, 1), cart[1]);
        Assert.Equal(new[] { new MlwMapped(0, Notable, 0, RecruitId, BasicId), new MlwMapped(1, Notable, 1, ViglaId, EliteId) }, mapped);
    }

    [Fact]
    public void Nothing_depends_on_the_form_of_Coops_ids()
    {
        // Remapped ids with no StringId in them: the custom troop is still known by its object, and the rewritten
        // entry carries the id Coop gave the slot troop.
        var coop = Coop(Recruit);
        coop.Forget(Recruit);
        coop.Forget(Basic);
        coop.Register("CharacterObject_4711", Recruit);
        coop.Register("remapped_77", Basic);
        var roster = new Roster(coop);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, "remapped_77", 0));
        Assert.Equal(new MlwCartEntry(Notable, "CharacterObject_4711", 0), cart[0]);
        coop.Commit(roster, cart);
        Assert.Equal(1, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((1, 0), roster.Get(Basic));
        Assert.Equal((0, 0), roster.Get(Recruit));
    }

    [Fact]
    public void A_bare_StringId_Coop_does_not_know_is_left_and_hinted()
    {
        var coop = Coop(Recruit);
        var mapped = new List<MlwMapped>();
        var cart = new[] { new MlwCartEntry(Notable, "_basic_root_p01", 0) };
        Assert.Equal(cart, MlwRecruitRules.Rewrite(cart, coop, mapped, out var hint));
        Assert.True(hint);
        Assert.Empty(mapped);

        // Without CoopModPatch on the server its troops are not there at all.
        coop.Forget(Basic);
        cart = new[] { new MlwCartEntry(Notable, BasicId, 0) };
        Assert.Equal(cart, MlwRecruitRules.Rewrite(cart, coop, mapped, out hint));
        Assert.True(hint);
        MlwRecruitRules.Rewrite(new[] { new MlwCartEntry(Notable, "Unknown_thing", 0) }, coop, mapped, out hint);
        Assert.False(hint);
        Assert.Empty(mapped);
    }

    [Fact]
    public void A_slot_troop_Coop_has_no_id_for_is_left_to_Coop()
    {
        var coop = Coop(Recruit);
        coop.Forget(Recruit);
        var mapped = new List<MlwMapped>();
        var cart = new[] { new MlwCartEntry(Notable, BasicId, 0) };
        Assert.Equal(cart, Rewrite(coop, mapped, cart));
        Assert.Empty(mapped);
    }

    [Fact]
    public void An_empty_or_custom_slot_is_left_to_Coop()
    {
        var coop = Coop(null, Made);
        var mapped = new List<MlwMapped>();
        var cart = new[] { new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, Basic2Id, 1) };
        Assert.Equal(cart, Rewrite(coop, mapped, cart));
        Assert.Empty(mapped);
    }

    [Fact]
    public void A_troop_that_is_not_from_My_Little_Warband_is_untouched()
    {
        Assert.False(MlwRecruitRules.IsCustomId("imperial_recruit"));
        Assert.False(MlwRecruitRules.IsCustomId(null));
        Assert.True(MlwRecruitRules.IsCustomId("_elite_root_p03"));
        var mapped = new List<MlwMapped>();
        var cart = new[] { new MlwCartEntry(Notable, RecruitId, 0), new MlwCartEntry(Notable, InfantryId, 1) };
        Assert.Equal(cart, Rewrite(Coop(Recruit, Recruit), mapped, cart));
        Assert.Empty(mapped);
    }

    [Fact]
    public void An_unknown_notable_or_a_slot_out_of_range_is_left_to_Coop()
    {
        var mapped = new List<MlwMapped>();
        var cart = new[] { new MlwCartEntry("Hero_nobody", BasicId, 0), new MlwCartEntry(null, BasicId, 0), new MlwCartEntry(Notable, BasicId, 2), new MlwCartEntry(Notable, BasicId, -1) };
        Assert.Equal(cart, Rewrite(Coop(Recruit, Recruit), mapped, cart));
        Assert.Empty(mapped);
    }

    [Fact]
    public void The_same_slot_twice_maps_only_the_first_entry()
    {
        var coop = Coop(Recruit, Recruit);
        coop.Slots["Hero_notable_2"] = new Troop?[] { Recruit };
        var mapped = new List<MlwMapped>();
        var cart = Rewrite(coop, mapped,
            new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, Basic2Id, 0),
            new MlwCartEntry(Notable, Basic2Id, 1), new MlwCartEntry("Hero_notable_2", Basic2Id, 0));
        Assert.Equal(new[] { 0, 2, 3 }, mapped.Select(m => m.Position));
        Assert.Equal(Basic2Id, cart[1].TroopId);
    }

    [Fact]
    public void A_slot_Coop_takes_as_it_is_earlier_in_the_cart_is_not_mapped_again()
    {
        var mapped = new List<MlwMapped>();
        Rewrite(Coop(Recruit), mapped, new MlwCartEntry(Notable, RecruitId, 0), new MlwCartEntry(Notable, BasicId, 0));
        Assert.Empty(mapped);
    }

    [Fact]
    public void A_slot_Coop_takes_as_it_is_is_matched_by_the_troop_not_by_the_id_text()
    {
        // Coop also resolves a compacted id (no type prefix) to the same troop, so that entry takes the slot.
        var coop = Coop(Recruit);
        coop.Objects["imperial_recruit"] = Recruit;
        var mapped = new List<MlwMapped>();
        Rewrite(coop, mapped, new MlwCartEntry(Notable, "imperial_recruit", 0), new MlwCartEntry(Notable, BasicId, 0));
        Assert.Empty(mapped);
    }

    [Fact]
    public void A_slot_that_changed_since_the_screen_opened_gives_the_custom_troop_for_the_troop_now_there()
    {
        // The player saw a recruit in slot 0 (shown as a custom troop); the daily update put an infantryman there.
        var coop = Coop(Infantry);
        var roster = new Roster(coop);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, BasicId, 0));
        Assert.Equal(new MlwCartEntry(Notable, InfantryId, 0), cart[0]);
        coop.Commit(roster, cart);
        Assert.Equal(1, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((1, 0), roster.Get(Basic));
        Assert.Equal((0, 0), roster.Get(Infantry));
    }

    [Fact]
    public void After_Coop_recruits_the_normal_troop_is_swapped_for_the_custom_one()
    {
        var coop = Coop(Recruit, Recruit, Vigla);
        var roster = new Roster(coop);
        var (mapped, before, cart) = Prepare(coop, roster,
            new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, Basic2Id, 1), new MlwCartEntry(Notable, EliteId, 2));
        coop.Commit(roster, cart);
        Assert.Equal(3, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((0, 0), roster.Get(Recruit));
        Assert.Equal((0, 0), roster.Get(Vigla));
        Assert.Equal(1, roster.Get(Basic).Count);
        Assert.Equal(1, roster.Get(Basic2).Count);
        Assert.Equal(1, roster.Get(Elite).Count);
    }

    [Fact]
    public void The_custom_troop_is_added_before_the_normal_one_is_taken_away()
    {
        var coop = Coop(Recruit);
        var roster = new Roster(coop);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, BasicId, 0));
        coop.Commit(roster, cart);
        MlwRecruitRules.Swap(mapped, before, coop, roster);
        Assert.Equal(new[] { "_basic_root_p01 +1 xp 0", "imperial_recruit -1 xp 0" }, roster.Calls);

        // A failure on the custom troop leaves the party with the normal troop Coop gave it.
        coop = Coop(Recruit);
        roster = new Roster(coop) { ThrowOn = Basic };
        (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, BasicId, 0));
        coop.Commit(roster, cart);
        Assert.Throws<InvalidOperationException>(() => MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal(1, roster.Get(Recruit).Count);
    }

    [Fact]
    public void Not_enough_gold_means_nothing_is_swapped()
    {
        var coop = Coop(Recruit, Recruit);
        var roster = new Roster(coop);
        roster.AddTroop(Recruit, 4, 0);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, BasicId, 1));
        coop.Commit(roster, cart, enoughGold: false);
        Assert.Equal(0, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((4, 0), roster.Get(Recruit));
        Assert.Equal((0, 0), roster.Get(Basic));
    }

    [Fact]
    public void Recruit_xp_moves_with_the_custom_troop_and_the_old_stack_keeps_its_own()
    {
        // The party had 5 recruits with 300 xp; Coop adds 2 more (one plain, one mapped) with 25 perk xp each.
        var coop = Coop(Recruit, Recruit);
        var roster = new Roster(coop);
        roster.AddTroop(Recruit, 5, 300);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, RecruitId, 0), new MlwCartEntry(Notable, BasicId, 1));
        coop.Commit(roster, cart, perkXp: 25);
        Assert.Equal((7, 350), roster.Get(Recruit));
        Assert.Equal(1, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((6, 325), roster.Get(Recruit));
        Assert.Equal((1, 25), roster.Get(Basic));
    }

    [Fact]
    public void Two_mapped_slots_of_one_normal_troop_each_take_their_own_perk_xp()
    {
        var coop = Coop(Recruit, Recruit);
        var roster = new Roster(coop);
        roster.AddTroop(Recruit, 3, 90);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, Basic2Id, 1));
        coop.Commit(roster, cart, perkXp: 20);
        Assert.Equal((5, 130), roster.Get(Recruit));
        Assert.Equal(2, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((3, 90), roster.Get(Recruit));
        Assert.Equal((1, 20), roster.Get(Basic));
        Assert.Equal((1, 20), roster.Get(Basic2));
    }

    [Fact]
    public void An_xp_remainder_stays_on_the_normal_stack()
    {
        // Two gained with 51 xp between them: 25 moves with each, the 1 left over stays with the normal stack.
        var coop = Coop(Recruit, Recruit);
        var roster = new Roster(coop);
        roster.AddTroop(Recruit, 1, 0);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, BasicId, 0), new MlwCartEntry(Notable, BasicId, 1));
        coop.Commit(roster, cart, perkXp: 25);
        roster.AddTroop(Recruit, 0, 1);
        Assert.Equal((3, 51), roster.Get(Recruit));
        Assert.Equal(2, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((2, 50), roster.Get(Basic));
        Assert.Equal((1, 1), roster.Get(Recruit));
    }

    [Fact]
    public void A_plain_entry_Coop_skips_does_not_count_as_gained()
    {
        // Slot 1 holds an infantryman, so Coop skips the plain recruit entry for it; only the mapped one commits.
        var coop = Coop(Recruit, Infantry);
        var roster = new Roster(coop);
        var (mapped, before, cart) = Prepare(coop, roster, new MlwCartEntry(Notable, RecruitId, 1), new MlwCartEntry(Notable, BasicId, 0));
        coop.Commit(roster, cart, perkXp: 30);
        Assert.Equal((1, 30), roster.Get(Recruit));
        Assert.Equal(1, MlwRecruitRules.Swap(mapped, before, coop, roster));
        Assert.Equal((1, 30), roster.Get(Basic));
        Assert.Equal((0, 0), roster.Get(Recruit));
        Assert.Equal((0, 0), roster.Get(Infantry));
    }

    [Fact]
    public void A_missing_target_is_named()
    {
        var found = new object();
        Assert.Null(MlwRecruitRules.FirstMissing(("handler", found), ("troop info", found)));
        Assert.Equal("handler", MlwRecruitRules.FirstMissing(("handler", null), ("troop info", null)));
        Assert.Equal("troop info", MlwRecruitRules.FirstMissing(("handler", found), ("troop info", null)));
    }
}
