using System.Text;
using Coodsz.CoopLink;
using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat's end of the shared Coop link: the COODSZ_ROT_LINK switch, the start
// up steps, the protocol number, the drift probe's digest (S70) and the culture and volunteer
// entries with their split for a player who entered (S16) (RotLinkRules.cs and RotSyncRules.cs,
// linked), the story popups players answer (S41, RotPopupRules.cs, linked), and the reflection calls into Coodsz.CoopLink (RotLinkCalls.cs, linked) bound against
// a stand in with Link's exact signatures and the link's real LinkMessage. That the module DLL
// never references the link is read from the built DLL.
public sealed class RealmOfThronesLinkTests
{
    // ---- switch and start up -------------------------------------------------------------------

    [Theory]
    [InlineData(null, true)]
    [InlineData("on", true)]
    [InlineData(" OFF ", false)]
    [InlineData("maybe", false)]
    public void The_link_switch_reads_like_the_story_switch(string? value, bool on)
    {
        Assert.Equal(on, RotLinkRules.On(value, out _));
    }

    [Fact]
    public void The_switch_line_says_what_stays_off_and_names_an_unknown_value()
    {
        Assert.Equal("Link: off: COODSZ_ROT_LINK=off, so the drift probe, the world value sync and the story popups stay off.", RotLinkRules.SwitchOffLine("off"));
        Assert.Contains("=maybe is neither on nor off", RotLinkRules.SwitchOffLine("maybe"));
    }

    [Fact]
    public void Without_Coop_the_boot_waits_quietly_and_asks_again_after_five_seconds()
    {
        var boot = new LinkBoot();
        Assert.True(boot.ProbeDue(0));
        boot.Probed(new List<string> { "Coop's Common assembly" });
        Assert.Equal(LinkPhase.Waiting, boot.Phase);
        Assert.Null(boot.OffReason);
        Assert.False(boot.ProbeDue(4999));
        Assert.True(boot.ProbeDue(5000));
    }

    [Fact]
    public void A_Coop_that_lacks_a_member_switches_the_link_off_with_the_members_named()
    {
        var boot = new LinkBoot();
        boot.Probed(new List<string> { "INetwork.SendImmediate(NetPeer, IMessage)" });
        Assert.Equal(LinkPhase.Off, boot.Phase);
        Assert.Equal("Coop lacks what the link needs (INetwork.SendImmediate(NetPeer, IMessage))", boot.OffReason);
        Assert.False(boot.ProbeDue(100000));
        Assert.Equal("Link: off: Coop lacks what the link needs (INetwork.SendImmediate(NetPeer, IMessage)). The drift probe, the world value sync and the story popups stay off "
            + "(each story popup gets its default answer on the server); everything else works as without the link.", RotLinkRules.OffLine(boot.OffReason!));
    }

    [Theory]
    [InlineData(false, 0, null, false, "Coodsz.CoopLink.dll is not next to this module's DLL or did not load")]
    [InlineData(true, 0, null, false, "the loaded Coodsz.CoopLink.dll is version 0, this module needs 1 or newer")]
    [InlineData(true, 1, "Link.ToPlayer", false, "the loaded Coodsz.CoopLink.dll lacks Link.ToPlayer")]
    [InlineData(true, 1, null, false, "Coodsz.CoopLink's Start failed (its line above says why)")]
    public void The_first_load_step_that_fails_is_the_reason_the_link_is_off(bool assembly, int version, string? missing, bool started, string reason)
    {
        var boot = new LinkBoot();
        boot.Probed(new List<string>());
        Assert.Equal(LinkPhase.Loading, boot.Phase);
        boot.Loaded(assembly, version, missing == null ? new List<string>() : new List<string> { missing }, started);
        Assert.Equal(LinkPhase.Off, boot.Phase);
        Assert.Equal(reason, boot.OffReason);
    }

    [Fact]
    public void A_probe_that_passes_and_a_link_that_starts_is_started()
    {
        var boot = new LinkBoot();
        boot.Probed(new List<string>());
        boot.Loaded(true, 1, new List<string>(), true);
        Assert.Equal(LinkPhase.Started, boot.Phase);
        Assert.Equal("Link: started (link v1, our protocol 1). Channels rot.probe (drift probe), rot.world (world values) and rot.story (story popups).", RotLinkRules.StartedLine(1));
    }

    [Fact]
    public void Every_message_starts_with_the_protocol_number_and_another_number_is_seen()
    {
        var args = RotLinkRules.WithProtocol(new[] { "1", "3" });
        Assert.Equal(new[] { "1", "1", "3" }, args);
        Assert.True(RotLinkRules.ReadProtocol(args, out var protocol, out var rest));
        Assert.Equal(1, protocol);
        Assert.Equal(new[] { "1", "3" }, rest);
        Assert.False(RotLinkRules.ReadProtocol(new[] { "x" }, out _, out _));
        Assert.False(RotLinkRules.ReadProtocol(Array.Empty<string>(), out _, out _));
        Assert.True(RotLinkRules.ReadProtocol(new[] { "2" }, out protocol, out _));
        Assert.Equal("Link: ignored rot.world: protocol 2, this PC speaks 1 (said once).", RotLinkRules.ProtocolLine("rot.world", true, protocol));
    }

    // A modified client picks the channel and key: the lines show them on one line and shortened, and the once per
    // session set stops growing at 64 lines with one line saying more came.
    [Fact]
    public void Lines_about_a_received_channel_stay_one_line_and_the_said_set_is_capped()
    {
        var unknown = RotLinkRules.UnknownLine("rot.x\r\n[RealmOfThrones.CoopCompat] Host: forged", "k\n" + new string('y', 200));
        Assert.DoesNotContain(unknown, c => char.IsControl(c));
        Assert.StartsWith("Link: nothing here handles rot.x??[RealmOfThrones.CoopCompat] Host: forged/k?yyy", unknown);
        Assert.Contains("...; ignored (said once).", unknown);
        Assert.DoesNotContain(RotLinkRules.ProtocolLine("rot.\u2029x", false, 0), c => c == '\u2029');
        Assert.Equal(RotLinkRules.MaxShownChars + 3, RotLinkRules.Shown(new string('z', 300)).Length);

        var said = new HashSet<string>();
        var logged = new List<string>();
        for (var i = 0; i < RotLinkRules.MaxSaid + 40; i++)
        {
            if (RotLinkRules.Once(said, RotLinkRules.UnknownLine("rot." + i, "k")) is { } line)
                logged.Add(line);
        }
        Assert.Null(RotLinkRules.Once(said, RotLinkRules.UnknownLine("rot.0", "k")));
        Assert.Equal(RotLinkRules.MaxSaid + 1, logged.Count);
        Assert.Equal(RotLinkRules.MoreSaidLine, logged[^1]);
        Assert.Equal(RotLinkRules.MaxSaid + 1, said.Count);
    }

    [Fact]
    public void Our_channels_are_under_our_prefix_and_never_the_links_own()
    {
        foreach (var name in new[] { RotLinkRules.ProbeChannel, RotLinkRules.WorldChannel, RotLinkRules.EnteredKey })
        {
            Assert.StartsWith(RotLinkRules.Prefix, name);
            Assert.True(LinkRules.IsModChannel(name));
        }
        Assert.Equal(LinkRules.MaxMessageBytes, RotLinkRules.LinkMaxBytes);
    }

    // ---- entries --------------------------------------------------------------------------------

    [Fact]
    public void An_entry_round_trips_and_a_tab_or_line_break_cannot_break_it()
    {
        var entry = RotSyncRules.Entry(RotSyncRules.SettlementCulture, "town_W1", "whitewalker");
        Assert.Equal("s\ttown_W1\twhitewalker", entry);
        Assert.True(RotSyncRules.TryParse(entry, out var kind, out var id, out var value));
        Assert.Equal(('s', "town_W1", "whitewalker"), (kind, id, value));
        Assert.Equal("s\ttown_W1", RotSyncRules.KeyOf(entry));
        Assert.Equal("h\tlord\tnord", RotSyncRules.Entry(RotSyncRules.HeroCulture, "lord\tx", "nord\nmore"));
        Assert.False(RotSyncRules.TryParse("garbage", out _, out _, out _));
        Assert.False(RotSyncRules.TryParse("s\t\tx", out _, out _, out _));
        Assert.True(RotSyncRules.TryParse("v\tnotable_1\t", out _, out _, out value));
        Assert.Equal("", value);
    }

    [Fact]
    public void Volunteer_slots_keep_their_empty_places()
    {
        var value = RotSyncRules.VolunteerValue(new[] { "nord_recruit", null, "nord_recruit", null, null, "imperial_recruit" });
        Assert.Equal("6:nord_recruit,,nord_recruit,,,imperial_recruit", value);
        Assert.Equal(new[] { "nord_recruit", null, "nord_recruit", null, null, "imperial_recruit" }, RotSyncRules.VolunteerSlots(value));
        // ChangeNotableCulture writes a fresh array of six empty slots.
        Assert.Equal(new string?[6], RotSyncRules.VolunteerSlots(RotSyncRules.VolunteerValue(new string?[6])));
    }

    [Fact]
    public void An_empty_volunteer_array_stays_empty_and_one_empty_slot_stays_one_slot()
    {
        Assert.Equal("0:", RotSyncRules.VolunteerValue(Array.Empty<string>()));
        Assert.Empty(RotSyncRules.VolunteerSlots("0:")!);
        Assert.Equal("1:", RotSyncRules.VolunteerValue(new string?[1]));
        Assert.Equal(new string?[1], RotSyncRules.VolunteerSlots("1:"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a,b")]
    [InlineData("3:a,b")]
    [InlineData("0:a")]
    [InlineData("x:a")]
    [InlineData("-1:")]
    public void A_volunteer_value_whose_count_and_slots_disagree_is_not_applied(string value)
    {
        Assert.Null(RotSyncRules.VolunteerSlots(value));
    }

    [Fact]
    public void Only_what_a_write_changed_is_sent()
    {
        var before = new Dictionary<string, string> { ["s\tcastle_1"] = "vlandia", ["s\tvillage_1"] = "vlandia", ["h\tnotable_1"] = "vlandia", ["v\tnotable_1"] = "a,b" };
        var after = new Dictionary<string, string> { ["s\tcastle_1"] = "whitewalker", ["s\tvillage_1"] = "vlandia", ["h\tnotable_1"] = "whitewalker", ["v\tnotable_1"] = "a,b", ["h\twanderer_9"] = "whitewalker" };
        Assert.Equal(new[] { "h\tnotable_1\twhitewalker", "h\twanderer_9\twhitewalker", "s\tcastle_1\twhitewalker" }, RotSyncRules.Changes(before, after));
        Assert.Empty(RotSyncRules.Changes(after, after));
    }

    [Fact]
    public void Entries_pack_one_per_line_and_unpack_the_same()
    {
        var entries = new[] { "s\ta\tx", "v\tb\tc,,d" };
        Assert.Equal(entries, RotSyncRules.Unpack(RotSyncRules.Pack(entries)));
        Assert.Empty(RotSyncRules.Unpack(null!));
        Assert.Empty(RotSyncRules.Unpack(Array.Empty<byte>()));
    }

    // ---- the split for a player who entered ---------------------------------------------------

    [Fact]
    public void A_snapshot_over_the_links_limit_is_split_into_parts_the_link_accepts_in_order()
    {
        // About 400 KB: far more settlements than Realm of Thrones has, so the split must happen.
        var entries = Enumerable.Range(0, 9000).Select(i => RotSyncRules.Entry(RotSyncRules.SettlementCulture, "settlement_with_a_long_id_" + i.ToString("D5"), "culture_" + (i % 30))).ToList();
        Assert.True(RotSyncRules.Pack(entries).Length > LinkRules.MaxMessageBytes);

        var parts = RotSyncRules.Split(entries, RotLinkRules.PartBytes, out var skipped);

        Assert.Equal(0, skipped);
        Assert.True(parts.Count >= 2);
        Assert.Equal(entries, parts.SelectMany(part => part));
        for (var i = 0; i < parts.Count; i++)
        {
            var blob = RotSyncRules.Pack(parts[i]);
            Assert.True(blob.Length <= RotLinkRules.PartBytes);
            var args = RotLinkRules.WithProtocol(new[] { (i + 1).ToString(), parts.Count.ToString() });
            Assert.True(LinkRules.CheckSize(RotLinkRules.WorldChannel, RotLinkRules.SnapshotKey, args, blob, out var bytes, out var reason), reason);
            Assert.Equal(bytes, RotLinkRules.Bytes(RotLinkRules.WorldChannel, RotLinkRules.SnapshotKey, args, blob));
        }
    }

    [Fact]
    public void A_snapshot_under_the_limit_is_one_part_and_an_entry_too_long_alone_is_left_out()
    {
        var parts = RotSyncRules.Split(new[] { "s\ta\tx", new string('y', 50), "s\tb\tz" }, 20, out var skipped);
        Assert.Equal(1, skipped);
        Assert.Equal(new[] { "s\ta\tx", "s\tb\tz" }, Assert.Single(parts));
        Assert.Single(RotSyncRules.Split(new[] { "s\ta\tx" }, RotLinkRules.PartBytes, out _));
        Assert.Empty(RotSyncRules.Split(Array.Empty<string>(), 10, out _));
    }

    [Fact]
    public void The_snapshot_fingerprint_ignores_order_and_sees_a_changed_value()
    {
        var a = new[] { "s\ta\tx", "h\tb\ty" };
        Assert.Equal(RotSyncRules.Hash(a), RotSyncRules.Hash(a.Reverse()));
        Assert.NotEqual(RotSyncRules.Hash(a), RotSyncRules.Hash(new[] { "s\ta\tx", "h\tb\tz" }));
        Assert.Equal(16, RotSyncRules.Hash(a).Length);
    }

    // Ids that are not ASCII, pinned to the value Python's FNV-1a 64 over the same UTF-8 bytes gives,
    // so the server's .NET 6 and a player's .NET Framework must agree on it.
    private static readonly string[] NotAscii = { "s\tкрепость_1\tкультура", "h\tseñor_ñ\tnord", "v\t領主\t2:a," };

    [Fact]
    public void The_fingerprint_of_ids_that_are_not_ASCII_is_a_fixed_value()
    {
        Assert.Equal("548f1cf5ccfe1c6d", RotSyncRules.Hash(NotAscii));
        Assert.Equal("548f1cf5ccfe1c6d", RotSyncRules.Hash(NotAscii.Reverse()));
    }

    [Fact]
    public void The_split_counts_UTF8_bytes_not_characters()
    {
        Assert.Equal(new[] { 38, 17, 14 }, NotAscii.Select(entry => RotSyncRules.Pack(new[] { entry }).Length));
        // By characters the first two would fit in 50 together; by bytes they do not.
        var parts = RotSyncRules.Split(NotAscii, 50, out var skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(new[] { NotAscii[0] }, parts[0]);
        Assert.Equal(new[] { NotAscii[1], NotAscii[2] }, parts[1]);
        Assert.Equal(NotAscii, RotSyncRules.Unpack(RotSyncRules.Pack(NotAscii)));
    }

    // ---- routing and the write count ----------------------------------------------------------

    [Theory]
    [InlineData("link.event", "rot.entered", false, 0, false, true, "Entered")]
    [InlineData("link.event", "rot.entered", false, 0, false, false, "NotFromServer")]
    [InlineData("link.event", "chaos.entered", false, 0, false, true, "NotFromServer")]
    [InlineData("rot.probe", "day", true, 1, true, false, "Drift")]
    [InlineData("rot.world", "set", true, 1, true, false, "WorldSet")]
    [InlineData("rot.world", "snapshot", true, 1, true, false, "WorldSnapshot")]
    [InlineData("rot.world", "snapshot", true, 2, true, false, "BadProtocol")]
    [InlineData("rot.world", "set", false, 0, true, false, "BadProtocol")]
    [InlineData("rot.world", "set", true, 1, false, false, "NotFromServer")]
    [InlineData("rot.world", "set", true, 1, true, true, "NotFromServer")]
    [InlineData("rot.world", "other", true, 1, true, false, "Unknown")]
    [InlineData("rot.probe", "set", true, 1, true, false, "Unknown")]
    [InlineData("rot.story", "open", true, 1, true, false, "Unknown")]
    [InlineData("rot.story", "ask", true, 1, true, false, "Story")]
    [InlineData("rot.story", "notice", true, 1, true, false, "Story")]
    [InlineData("rot.story", "result", true, 1, true, false, "Story")]
    [InlineData("rot.story", "ask", true, 1, false, true, "NotFromServer")]
    [InlineData("rot.story", "answer", true, 1, false, true, "StoryAnswer")]
    [InlineData("rot.story", "answer", true, 1, true, false, "NotFromServer")]
    [InlineData("rot.story", "answer", true, 1, false, false, "NotFromServer")]
    [InlineData("rot.story", "answer", true, 2, false, true, "BadProtocol")]
    public void Each_message_goes_where_its_channel_key_protocol_and_side_say(string channel, string key, bool numbered, int protocol, bool fromServer, bool onServer, string route)
    {
        Assert.Equal(route, RotLinkRules.Route(channel, key, numbered, protocol, fromServer, onServer).ToString());
        Assert.Equal(LinkRules.EventChannel, RotLinkRules.EventChannel);
    }

    [Fact]
    public void Only_the_outermost_of_nested_writes_takes_the_values_and_compares()
    {
        var depth = new WriteDepth();
        Assert.True(depth.Begin());
        Assert.False(depth.Begin());
        Assert.False(depth.Begin());
        Assert.False(depth.End());
        Assert.False(depth.End());
        Assert.True(depth.End());
        Assert.Equal(0, depth.Depth);
        Assert.True(depth.Begin());
    }

    [Fact]
    public void An_end_after_a_throw_still_closes_the_write_and_an_extra_end_does_nothing()
    {
        var depth = new WriteDepth();
        depth.Begin();
        // The writer threw: Harmony runs the finalizer, which ends it as usual.
        Assert.True(depth.End());
        Assert.False(depth.End());
        Assert.Equal(0, depth.Depth);
        // A count left open (a prefix without its finalizer) is cleared at a session's end.
        depth.Begin();
        depth.Begin();
        depth.Reset();
        Assert.True(depth.Begin());
    }

    // ---- the drift digest ---------------------------------------------------------------------

    // About the size of Realm of Thrones' world: 300 towns and castles, 80 wars, 29 events.
    private static List<string> Digest()
    {
        var entries = new List<string>();
        for (var i = 0; i < 300; i++)
        {
            entries.Add(RotSyncRules.Entry(RotSyncRules.Owner, "castle_" + i, "ROTclan_" + (i % 240)));
            entries.Add(RotSyncRules.Entry(RotSyncRules.FortCulture, "castle_" + i, "culture_" + (i % 20)));
        }
        for (var i = 0; i < 80; i++)
            entries.Add(RotSyncRules.WarEntry("kingdom_" + (i % 29), "kingdom_b" + i));
        for (var i = 0; i < 29; i++)
            entries.Add(RotSyncRules.Entry(RotSyncRules.StoryEvent, "Event" + i, "idle/0"));
        entries.Add(RotSyncRules.Entry(RotSyncRules.Count, "clans", "260 all, 243 alive"));
        return entries;
    }

    [Fact]
    public void The_digest_of_a_Realm_of_Thrones_sized_world_packs_to_a_few_KB_and_unpacks_the_same()
    {
        var digest = Digest();
        var blob = RotSyncRules.Compress(digest);
        Assert.True(blob.Length < 8 * 1024, blob.Length + " bytes");
        Assert.True(blob.Length < RotSyncRules.MaxDigestBytes);
        Assert.Equal(digest, RotSyncRules.Decompress(blob, 1024 * 1024));
        Assert.Null(RotSyncRules.Decompress(blob, 100));
        Assert.Null(RotSyncRules.Decompress(Encoding.UTF8.GetBytes("not gzip"), 1024));
    }

    [Fact]
    public void A_digest_that_is_garbage_cut_short_or_too_big_unpacked_is_not_read()
    {
        var blob = RotSyncRules.Compress(Digest());
        Assert.Null(RotSyncRules.Decompress(null!, 1024 * 1024));
        Assert.Null(RotSyncRules.Decompress(Array.Empty<byte>(), 1024 * 1024));
        Assert.Null(RotSyncRules.Decompress(Enumerable.Range(0, 400).Select(i => (byte)(i * 37)).ToArray(), 1024 * 1024));
        foreach (var cut in new[] { 10, blob.Length / 2, blob.Length - 1 })
            Assert.Null(RotSyncRules.Decompress(blob.Take(cut).ToArray(), 1024 * 1024));
        // Two megabytes of one letter pack to a few KB and must stop at the limit while unpacking.
        var bomb = RotSyncRules.Compress(new[] { new string('a', 2 * 1024 * 1024) });
        Assert.True(bomb.Length < 16 * 1024);
        Assert.Null(RotSyncRules.Decompress(bomb, 1024 * 1024));
        Assert.Empty(RotSyncRules.Decompress(RotSyncRules.Compress(Array.Empty<string>()), 1024)!);
    }

    [Fact]
    public void A_war_is_the_same_entry_whichever_side_is_named_first()
    {
        Assert.Equal(RotSyncRules.WarEntry("north", "lannister"), RotSyncRules.WarEntry("lannister", "north"));
        Assert.Equal("w\tlannister~north\twar", RotSyncRules.WarEntry("north", "lannister"));
    }

    [Fact]
    public void Equal_worlds_give_one_no_difference_line()
    {
        var digest = Digest();
        var differences = RotSyncRules.Differences(digest, digest.AsEnumerable().Reverse().ToList());
        Assert.Empty(differences);
        Assert.Equal(710, digest.Count);
        Assert.Equal("Link: drift probe day 12: no difference (710 entries compared).", RotSyncRules.DriftLine(12, digest.Count, differences));
    }

    // Live test 2026-11-11: day 25122 listed three wars "missing here" that were equal the next day;
    // the compare ran before Coop's own messages landed. Differences get a second look 5 s later.
    [Fact]
    public void A_compare_with_differences_looks_again_once_and_says_so()
    {
        Assert.True(RotSyncRules.LooksAgain(3, false));
        Assert.False(RotSyncRules.LooksAgain(0, false));
        Assert.False(RotSyncRules.LooksAgain(3, true));
        Assert.Equal(5000, RotSyncRules.SecondLookMs);
        Assert.False(RotSyncRules.SecondLookDue(14_999, 15_000));
        Assert.True(RotSyncRules.SecondLookDue(15_000, 15_000));

        // Three wars missing at the first look, there five seconds later: no difference.
        var server = new List<string> { "w\ta~b\twar", "w\ta~c\twar", "w\tb~c\twar", "o\tcastle_1\tclan_a" };
        var first = RotSyncRules.Differences(server, new List<string> { "o\tcastle_1\tclan_a" });
        Assert.Equal(3, first.Count);
        var second = RotSyncRules.Differences(server, server);
        Assert.Equal("Link: drift probe day 25122: no difference (after a second look; the first look had 3) (4 entries compared).",
            RotSyncRules.DriftLine(25122, server.Count, second, true, first.Count));
        // A first look without differences says no more than before.
        Assert.Equal("Link: drift probe day 25122: no difference (4 entries compared).", RotSyncRules.DriftLine(25122, server.Count, second));
        // What is still different is said with the same mark.
        Assert.Equal("Link: drift probe day 25122: 1 difference(s) (after a second look) in 4 entries: war a~b: missing here (server war).",
            RotSyncRules.DriftLine(25122, server.Count, new List<string> { "war a~b: missing here (server war)" }, true));
    }

    [Fact]
    public void Each_difference_says_what_the_server_has_and_what_this_PC_has()
    {
        var server = new List<string> { "o\tcastle_1\tclan_a", "w\ta~b\twar", "c\tcastle_1\tvlandia", "n\tclans\t10 all, 9 alive" };
        var here = new List<string> { "o\tcastle_1\tclan_b", "c\tcastle_1\tvlandia", "w\ta~c\twar", "n\tclans\t10 all, 9 alive" };
        Assert.Equal(new[] { "owner of castle_1: server clan_a, here clan_b", "war a~b: missing here (server war)", "war a~c: only here (war)" }, RotSyncRules.Differences(server, here));
    }

    [Fact]
    public void A_kind_one_side_cannot_read_is_one_difference_and_its_entries_are_not_compared()
    {
        var server = new List<string> { "e\tNedEvent\tactive/1", "o\tcastle_1\tclan_a" };
        var here = new List<string> { "e\t*\tno ROTEventBehavior", "o\tcastle_1\tclan_a" };
        Assert.Equal(new[] { "story event not compared: not readable on this PC" }, RotSyncRules.Differences(server, here));
        Assert.Equal(new[] { "story event not compared: not readable on the server" }, RotSyncRules.Differences(here, server));
    }

    [Fact]
    public void A_long_list_of_differences_names_twelve_and_counts_the_rest()
    {
        var differences = Enumerable.Range(0, 20).Select(i => "owner of castle_" + i + ": server a, here b").ToList();
        var line = RotSyncRules.DriftLine(3, 600, differences);
        Assert.StartsWith("Link: drift probe day 3: 20 difference(s) in 600 entries: owner of castle_0: server a, here b; ", line);
        Assert.Contains("castle_11:", line);
        Assert.DoesNotContain("castle_12:", line);
        Assert.EndsWith("; and 8 more.", line);
    }

    // ---- S16: races, scenes and the clan list ---------------------------------------------------

    [Fact]
    public void A_scene_entry_names_settlement_location_and_level_and_round_trips()
    {
        var id = RotSyncRules.SceneId("town_kings_landing", "center", 2);
        Assert.Equal("town_kings_landing/center/2", id);
        var entry = RotSyncRules.Entry(RotSyncRules.LocationScene, id, "ROT_kings_landing2");
        Assert.True(RotSyncRules.TryParse(entry, out var kind, out var parsed, out var value));
        Assert.Equal((RotSyncRules.LocationScene, id, "ROT_kings_landing2"), (kind, parsed, value));
        Assert.True(RotSyncRules.TryParseSceneId(parsed, out var settlement, out var location, out var level));
        Assert.Equal(("town_kings_landing", "center", 2), (settlement, location, level));
    }

    [Theory]
    [InlineData("town/center")]
    [InlineData("town/center/4")]
    [InlineData("town/center/-1")]
    [InlineData("town//1")]
    [InlineData("/center/1")]
    [InlineData("town/center/1/2")]
    [InlineData("town/center/x")]
    public void A_scene_id_that_is_not_settlement_location_and_level_0_to_3_is_not_applied(string id)
    {
        Assert.False(RotSyncRules.TryParseSceneId(id, out _, out _, out var level));
        Assert.Equal(-1, level);
    }

    [Theory]
    [InlineData("ROT_kings_landing2", true)]
    [InlineData("scn.town-1", true)]
    [InlineData("", false)]
    [InlineData("..\\evil", false)]
    [InlineData("a b", false)]
    public void Only_a_plain_scene_name_is_set_on_a_PC(string value, bool ok)
    {
        Assert.Equal(ok, RotSyncRules.IsSceneName(value));
        Assert.False(RotSyncRules.IsSceneName(new string('a', RotSyncRules.MaxSceneChars + 1)));
    }

    [Fact]
    public void A_race_is_applied_only_when_this_PC_knows_it()
    {
        Assert.True(RotSyncRules.TryRace("1", 3, out var race));
        Assert.Equal(1, race);
        Assert.False(RotSyncRules.TryRace("3", 3, out _));
        Assert.False(RotSyncRules.TryRace("-1", 3, out _));
        Assert.False(RotSyncRules.TryRace("x", 3, out _));
        Assert.True(RotSyncRules.TryRace("7", 0, out _));
    }

    [Fact]
    public void The_clans_a_succession_took_off_the_list_are_found_by_id_in_order()
    {
        var before = new[] { "clan_renly", "clan_a", "clan_stannis", "clan_b" };
        Assert.Equal(new[] { "clan_renly", "clan_stannis" }, RotSyncRules.RemovedClans(before, new[] { "clan_b", "clan_a" }));
        Assert.Empty(RotSyncRules.RemovedClans(before, before));
        Assert.Empty(RotSyncRules.RemovedClans(null!, null!));
        Assert.Equal("clan off the list", RotSyncRules.KindName(RotSyncRules.ClanRemoved));
        Assert.Equal("race of", RotSyncRules.KindName(RotSyncRules.HeroRace));
        Assert.Equal("scene of", RotSyncRules.KindName(RotSyncRules.LocationScene));
    }

    [Fact]
    public void Only_a_scene_name_a_write_changed_is_sent()
    {
        var before = new Dictionary<string, string> { ["l\tkl/center/0"] = "ROT_kings_landing", ["l\tkl/center/1"] = "ROT_kings_landing" };
        var after = new Dictionary<string, string> { ["l\tkl/center/0"] = "ROT_kings_landing2", ["l\tkl/center/1"] = "ROT_kings_landing" };
        Assert.Equal(new[] { "l\tkl/center/0\tROT_kings_landing2" }, RotSyncRules.Changes(before, after));
    }

    // ---- S41: story popups -----------------------------------------------------------------------

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("on", true, false)]
    [InlineData(" OFF ", false, false)]
    [InlineData("0", true, true)]
    [InlineData("no", true, true)]
    public void Only_the_value_off_turns_the_popups_off(string? value, bool on, bool notUnderstood)
    {
        Assert.Equal(on, RotPopupRules.On(value!, out var unknown));
        Assert.Equal(notUnderstood, unknown);
    }

    [Fact]
    public void The_switch_line_says_when_the_popups_are_off_and_why()
    {
        Assert.StartsWith("Story popups: on: ", RotPopupRules.SwitchLine(null!, true));
        Assert.Equal("Story popups: off (COODSZ_ROT_POPUPS=off): each story popup gets its default answer on the server as before, players see nothing.", RotPopupRules.SwitchLine("off", true));
        Assert.Contains("COODSZ_ROT_STORY", RotPopupRules.SwitchLine(null!, false));
        Assert.Contains("is not understood", RotPopupRules.SwitchLine("nope", true));
        Assert.Equal("Story popups: event NedEvent gets its default answer on the server: no player with the link is in the campaign.",
            RotPopupRules.DefaultLine("NedEvent", "no player with the link is in the campaign"));
    }

    [Fact]
    public void The_popups_whose_answer_changes_the_world_are_questions_and_the_rest_notices()
    {
        var questions = RotStoryRules.Events.Where(RotPopupRules.IsQuestion).Select(row => row.Type).ToArray();
        Assert.Equal(new[] { "BlackwaterSiegeEvent", "BlackwaterEvent", "KingsLandingEvent", "WallSiegeEvent", "BastardsSiegeEvent", "BastardsEvent", "HarrenhalSiegeEvent" }, questions);
        Assert.True(RotPopupRules.IsQuestion(null!));
    }

    [Fact]
    public void The_first_valid_answer_decides_and_every_later_one_is_ignored()
    {
        var question = new StoryQuestion(4, "HarrenhalSiegeEvent", 1, 1000);
        Assert.True(question.Open);
        Assert.Equal(AnswerVerdict.OldSeq, question.Answer(3, 0, "Ned", 2000));
        Assert.Equal(AnswerVerdict.Invalid, question.Answer(4, 1, "Ned", 2000));
        Assert.Equal(AnswerVerdict.NotAPlayer, question.Answer(4, 0, null!, 2000));
        Assert.True(question.Open);
        Assert.Equal(AnswerVerdict.Applied, question.Answer(4, 0, "Arya", 13500));
        Assert.Equal((QuestionEnd.Answered, 0, "Arya"), (question.End, question.Chosen, question.AnsweredBy));
        Assert.Equal(12.5, question.SecondsTaken);
        Assert.Equal(AnswerVerdict.Duplicate, question.Answer(4, 0, "Jon", 14000));
        Assert.Equal("Arya", question.AnsweredBy);
        Assert.False(question.Tick(999999, 0));
        Assert.False(question.SessionEnded(999999));
        Assert.Equal(QuestionEnd.Answered, question.End);
    }

    [Fact]
    public void Without_an_answer_in_60_seconds_the_default_is_taken_and_a_late_answer_is_ignored()
    {
        var question = new StoryQuestion(1, "WallSiegeEvent", 1, 0);
        Assert.Equal(60, question.SecondsLeft(0));
        Assert.Equal(60, question.SecondsLeft(1));
        Assert.Equal(1, question.SecondsLeft(59001));
        Assert.False(question.Tick(59999, 2));
        Assert.Equal(AnswerVerdict.Late, question.Answer(1, 0, "Sam", 60000));
        Assert.True(question.Open);
        Assert.True(question.Tick(60000, 2));
        Assert.Equal((QuestionEnd.TimedOut, RotPopupRules.DefaultOption), (question.End, question.Chosen));
        Assert.Null(question.AnsweredBy);
        Assert.Equal(0, question.SecondsLeft(60000));
        Assert.Equal(AnswerVerdict.Late, question.Answer(1, 0, "Sam", 60001));
        Assert.Equal(AnswerVerdict.Late, question.Answer(1, 0, null!, 60001));
        Assert.False(question.Tick(70000, 2));
    }

    [Fact]
    public void A_PC_shows_the_first_questions_again_after_the_server_restarted()
    {
        var closed = 0;
        Assert.True(RotPopupRules.IsNewQuestion(5, ref closed));
        closed = 5;
        Assert.False(RotPopupRules.IsNewQuestion(5, ref closed));
        Assert.True(RotPopupRules.IsNewQuestion(6, ref closed));
        Assert.Equal(5, closed);
        // The server restarted while the PC kept its count: question 1 shows and the count starts over.
        Assert.True(RotPopupRules.IsNewQuestion(1, ref closed));
        Assert.Equal(0, closed);
    }

    [Fact]
    public void The_same_popup_opened_again_while_the_world_waits_is_not_answered_twice_and_the_queue_has_a_cap()
    {
        Assert.Equal(TakeVerdict.Queue, RotPopupRules.Admit(false, 0));
        Assert.Equal(TakeVerdict.AlreadyTaken, RotPopupRules.Admit(true, 3));
        Assert.Equal(TakeVerdict.AlreadyTaken, RotPopupRules.Admit(true, RotPopupRules.MaxWaiting));
        Assert.Equal(TakeVerdict.Queue, RotPopupRules.Admit(false, RotPopupRules.MaxWaiting - 1));
        Assert.Equal(TakeVerdict.Full, RotPopupRules.Admit(false, RotPopupRules.MaxWaiting));
    }

    [Fact]
    public void When_the_last_linked_player_leaves_the_default_is_taken_at_once()
    {
        var question = new StoryQuestion(2, "BastardsEvent", 1, 0);
        Assert.False(question.Tick(5000, 1));
        Assert.True(question.Tick(6000, 0));
        Assert.Equal((QuestionEnd.NoPlayers, 0, 6.0), (question.End, question.Chosen, question.SecondsTaken));
    }

    [Fact]
    public void A_session_end_cancels_an_open_question_with_the_default_once()
    {
        var question = new StoryQuestion(3, "BlackwaterEvent", 2, 0);
        Assert.True(question.SessionEnded(1000));
        Assert.Equal((QuestionEnd.SessionEnded, 0), (question.End, question.Chosen));
        Assert.False(question.SessionEnded(2000));
        Assert.Equal(AnswerVerdict.Late, question.Answer(3, 1, "Bran", 1500));
    }

    [Fact]
    public void A_question_keeps_one_or_two_options()
    {
        Assert.Equal(1, new StoryQuestion(1, "X", 0, 0).Options);
        Assert.Equal(2, new StoryQuestion(1, "X", 5, 0).Options);
        var two = new StoryQuestion(1, "X", 2, 0);
        Assert.Equal(AnswerVerdict.Applied, two.Answer(1, 1, "Robb", 10));
        Assert.Equal(1, two.Chosen);
    }

    [Fact]
    public void A_player_counts_once_until_his_connection_drops_and_a_rejoin_counts_again()
    {
        var players = new LinkedPlayers();
        var ned = new object();
        var arya = new object();
        Assert.True(players.Add(ned));
        Assert.False(players.Add(ned));
        Assert.True(players.Add(arya));
        Assert.False(players.Add(null!));
        Assert.Equal(2, players.Count);
        Assert.Equal(1, players.Prune(peer => peer != ned));
        Assert.Equal(1, players.Count);
        // A check that throws keeps the player: a leaver then still ends at the timeout.
        Assert.Equal(0, players.Prune(_ => throw new InvalidOperationException()));
        Assert.True(players.Add(ned));
        players.Clear();
        Assert.Equal(0, players.Count);
    }

    [Fact]
    public void A_late_joiner_gets_the_open_question_with_the_time_left()
    {
        var question = new StoryQuestion(9, "HarrenhalSiegeEvent", 1, 10000);
        var ask = Ask(9, false);
        ask.SecondsLeft = question.SecondsLeft(35000);
        Assert.Equal(35, ask.SecondsLeft);
        var read = RotPopupRules.DecodeAsk(9, false, RotPopupRules.EncodeAsk(ask));
        Assert.Equal(35, read!.SecondsLeft);
        Assert.Equal("Story popups: late joiner got the open question 9 (event HarrenhalSiegeEvent) with 35 s left.", RotPopupRules.LateJoinerLine(9, "HarrenhalSiegeEvent", 35));
    }

    [Fact]
    public void An_ask_round_trips_with_its_text_ids_and_options()
    {
        var ask = Ask(5, false);
        var read = RotPopupRules.DecodeAsk(5, false, RotPopupRules.EncodeAsk(ask));
        Assert.NotNull(read);
        Assert.Equal((5, false, "BlackwaterSiegeEvent", 60), (read!.Seq, read.Notice, read.EventType, read.SecondsLeft));
        Assert.Equal(("ROTd0hD1DmLa", "Battle of the Blackwater"), (read.Title.Id, read.Title.Text));
        Assert.Equal(("", "Stannis assaults the walls."), (read.Lower.Id, read.Lower.Text));
        Assert.Equal(new[] { "ROTasSnYXW5:Continue..." }, read.Options.Select(o => o.Id + ":" + o.Text));
    }

    [Fact]
    public void A_notice_has_no_options_and_a_question_has_at_least_one()
    {
        var notice = Ask(6, true);
        Assert.NotNull(RotPopupRules.DecodeAsk(6, true, RotPopupRules.EncodeAsk(notice)));
        Assert.Null(RotPopupRules.DecodeAsk(6, false, RotPopupRules.EncodeAsk(notice)));
        Assert.Null(RotPopupRules.DecodeAsk(6, true, RotPopupRules.EncodeAsk(Ask(6, false))));
    }

    [Fact]
    public void An_ask_that_breaks_a_bound_or_its_shape_is_not_read()
    {
        var good = RotPopupRules.EncodeAsk(Ask(7, false));
        Assert.NotNull(RotPopupRules.DecodeAsk(7, false, good));
        Assert.Null(RotPopupRules.DecodeAsk(0, false, good));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, null!));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, good.Take(good.Count - 1).ToList()));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, good.Concat(new[] { "x" }).ToList()));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 1, "61")));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 1, "-1")));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 8, "3")));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 0, "")));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 2, "bad id")));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 3, new string('a', RotPopupRules.MaxTitleChars + 1))));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 5, new string('a', RotPopupRules.MaxTextChars + 1))));
        Assert.Null(RotPopupRules.DecodeAsk(7, false, With(good, 10, new string('a', RotPopupRules.MaxOptionChars + 1))));
    }

    [Fact]
    public void Encoding_cuts_every_text_to_its_bound_so_the_largest_ask_stays_far_under_the_links_limit()
    {
        var huge = new string('中', 100000);
        var ask = new StoryAsk
        {
            EventType = huge, SecondsLeft = 500, Title = new StoryText("x", huge), Upper = new StoryText("x", huge), Lower = new StoryText("x", huge),
            Options = { new StoryText("x", huge), new StoryText("x", huge), new StoryText("x", huge) },
        };
        var args = RotPopupRules.EncodeAsk(ask);
        var read = RotPopupRules.DecodeAsk(1, false, args);
        Assert.NotNull(read);
        Assert.Equal(RotPopupRules.WindowSeconds, read!.SecondsLeft);
        Assert.Equal(RotPopupRules.MaxOptions, read.Options.Count);
        Assert.Equal(RotPopupRules.MaxTextChars, read.Upper.Text.Length);
        var bytes = RotLinkRules.Bytes(RotPopupRules.Channel, RotPopupRules.AskKey, RotLinkRules.WithProtocol(args), null);
        Assert.InRange(bytes, 1, 64 * 1024);
        Assert.True(bytes < RotLinkRules.LinkMaxBytes);
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("1", true, 1)]
    [InlineData("2", false, -1)]
    [InlineData("-1", false, -1)]
    [InlineData("x", false, -1)]
    public void An_answer_is_one_option_number(string value, bool ok, int option)
    {
        Assert.Equal(ok, RotPopupRules.DecodeAnswer(new[] { value }, out var read));
        if (ok)
            Assert.Equal(option, read);
        Assert.Equal(new[] { "1" }, RotPopupRules.EncodeAnswer(1));
        Assert.False(RotPopupRules.DecodeAnswer(new[] { "0", "1" }, out _));
        Assert.False(RotPopupRules.DecodeAnswer(null!, out _));
    }

    [Fact]
    public void A_result_round_trips_and_gives_the_player_one_short_line()
    {
        var args = RotPopupRules.EncodeResult(QuestionEnd.Answered, 0, "Continue...", "Arya Stark");
        Assert.True(RotPopupRules.DecodeResult(args, out var end, out var option, out var text, out var who));
        Assert.Equal((QuestionEnd.Answered, 0, "Continue...", "Arya Stark"), (end, option, text, who));
        Assert.Equal("Arya Stark chose \"Continue...\" (Battle of the Blackwater).", RotPopupRules.PlayerLine(end, text, who, "Battle of the Blackwater"));
        Assert.Equal("No answer in time, \"Continue...\" was chosen.", RotPopupRules.PlayerLine(QuestionEnd.TimedOut, "Continue...", null!, null!));
        Assert.True(RotPopupRules.DecodeResult(RotPopupRules.EncodeResult(QuestionEnd.NoPlayers, 0, "Continue...", null!), out end, out _, out _, out who));
        Assert.Equal((QuestionEnd.NoPlayers, ""), (end, who));
        Assert.False(RotPopupRules.DecodeResult(new[] { "won", "0", "", "" }, out _, out _, out _, out _));
        Assert.False(RotPopupRules.DecodeResult(new[] { "answered", "5", "", "" }, out _, out _, out _, out _));
        Assert.False(RotPopupRules.DecodeResult(new[] { "answered", "0", "" }, out _, out _, out _, out _));
    }

    [Fact]
    public void A_text_id_is_found_from_the_literal_that_reads_the_same_on_the_server()
    {
        var literals = new[] { "{=ROTd0hD1DmLa}Battle of the Blackwater", "plain", "{=!}Not translated", "{=ROTasSnYXW5}Continue..." };
        string Resolve(string literal) => literal.Substring(literal.IndexOf('}') + 1);
        Assert.Equal("ROTd0hD1DmLa", RotPopupRules.IdFor("Battle of the Blackwater", literals, Resolve));
        Assert.Equal("ROTasSnYXW5", RotPopupRules.IdFor("Continue...", literals, Resolve));
        Assert.Equal("", RotPopupRules.IdFor("Not translated", literals, Resolve));
        Assert.Equal("", RotPopupRules.IdFor("Stannis won with 3 ships", literals, Resolve));
        Assert.Equal("", RotPopupRules.IdFor("Continue...", literals, _ => throw new InvalidOperationException()));
        Assert.True(RotPopupRules.TryParseLiteral("{=renlydeath1}A Shadow's Deed", out var id, out var text));
        Assert.Equal(("renlydeath1", "A Shadow's Deed"), (id, text));
        Assert.False(RotPopupRules.TryParseLiteral("{=}x", out _, out _));
        Assert.False(RotPopupRules.TryParseLiteral("{=*}x", out _, out _));
    }

    [Fact]
    public void A_PC_reads_the_id_in_its_language_with_the_servers_text_as_fallback()
    {
        Assert.Equal("{=ROTasSnYXW5}Continue...", RotPopupRules.ClientLiteral("ROTasSnYXW5", "Continue..."));
        Assert.Null(RotPopupRules.ClientLiteral("", "Continue..."));
        Assert.Null(RotPopupRules.ClientLiteral("ROTx", "Has {BRACES}"));
        Assert.Null(RotPopupRules.ClientLiteral("bad id", "x"));
    }

    [Fact]
    public void The_pause_holds_only_while_a_question_holds_it_and_never_past_its_limit()
    {
        var hold = new PauseHold();
        Assert.True(hold.Allows(0));
        hold.Begin(1000, RotPopupRules.WindowSeconds + RotPopupRules.HoldGraceSeconds);
        Assert.True(hold.Holding);
        Assert.False(hold.Allows(1000));
        Assert.False(hold.Allows(75999));
        // Even when nothing ended the question, time may run again at the limit.
        Assert.True(hold.Allows(76000));
        hold.Begin(80000, 60);
        Assert.False(hold.Allows(80001));
        hold.End();
        Assert.True(hold.Allows(80001));
        Assert.False(hold.Holding);
    }

    [Fact]
    public void The_lines_a_tester_waits_for_say_event_options_players_who_and_seconds()
    {
        Assert.Equal("Story popups: question 3 sent: event HarrenhalSiegeEvent, 1 option(s), to 2 player(s) with the link in the campaign; the world waits for at most 60 s.",
            RotPopupRules.QuestionSentLine(3, "HarrenhalSiegeEvent", 1, 2, true));
        Assert.Contains("time keeps running", RotPopupRules.QuestionSentLine(3, "X", 1, 2, false));
        Assert.Equal("Story popups: answer applied for question 3 (event HarrenhalSiegeEvent): Arya chose \"Continue...\" after 12.5 s.",
            RotPopupRules.AppliedLine(3, "HarrenhalSiegeEvent", "Arya", "Continue...", 12.5));
        Assert.Equal("Story popups: question 3 (event WallSiegeEvent) timed out with the default: no answer in 60 s, so \"Continue...\" applied after 60.0 s.",
            RotPopupRules.EndedLine(3, "WallSiegeEvent", QuestionEnd.TimedOut, "Continue...", 60));
        Assert.Contains("the last player with the link left the campaign", RotPopupRules.EndedLine(3, "X", QuestionEnd.NoPlayers, "Continue...", 1));
        Assert.Equal("Story popups: notice 4 sent: event NedEvent to 2 player(s); the server answered it at once as before.", RotPopupRules.NoticeSentLine(4, "NedEvent", 2));
        Assert.Equal("Story popups: notice 4 shown (event NedEvent).", RotPopupRules.ShownLine(Ask(4, true), 0));
        Assert.Equal("Story popups: question 5 shown with 41 s left (event BlackwaterSiegeEvent).", RotPopupRules.ShownLine(Ask(5, false), 41));
        Assert.Equal("Story popups: answer for question 3 from Jon ignored: the question was already answered.", RotPopupRules.IgnoredLine(3, "Jon", AnswerVerdict.Duplicate));
    }

    private static StoryAsk Ask(int seq, bool notice)
    {
        var ask = new StoryAsk
        {
            Seq = seq, Notice = notice, EventType = notice ? "NedEvent" : "BlackwaterSiegeEvent", SecondsLeft = 60,
            Title = new StoryText("ROTd0hD1DmLa", "Battle of the Blackwater"),
            Upper = new StoryText("ROTDX2yMuTH_BWSE", "Stannis Baratheon assaults King's Landing."),
            Lower = new StoryText("", "Stannis assaults the walls."),
        };
        if (!notice)
            ask.Options.Add(new StoryText("ROTasSnYXW5", "Continue..."));
        return ask;
    }

    private static List<string> With(List<string> args, int at, string value)
    {
        var copy = args.ToList();
        copy[at] = value;
        return copy;
    }

    // ---- the reflection calls -----------------------------------------------------------------

    [Fact]
    public void The_calls_bind_to_Links_signatures_and_hand_a_message_over_as_plain_values()
    {
        StandIn.Link.Reset();
        var calls = new RotLinkCalls();
        Assert.Empty(calls.Bind(typeof(StandIn.Link), typeof(LinkMessage)));
        Assert.Equal(1, calls.Version());

        var log = new List<string>();
        var received = new List<RotLinkMessage>();
        Assert.True(calls.Start(log.Add, () => "network", "rot.", message => received.Add(calls.Read(message))));
        StandIn.Link.Log!("hello");
        Assert.Equal(new[] { "hello" }, log);
        Assert.Equal("network", StandIn.Link.NetworkProvider!());
        Assert.Equal("rot.", StandIn.Link.Prefix);

        var sender = new object();
        StandIn.Link.Callback!(new LinkMessage(1, "rot.world", "set", 4, new[] { "1", "1", "1" }, new byte[] { 7 }, sender, true));
        var got = Assert.Single(received);
        Assert.Equal(("rot.world", "set", 4, true), (got.Channel, got.Key, got.Seq, got.FromServer));
        Assert.Equal(new[] { "1", "1", "1" }, got.Args);
        Assert.Equal(new byte[] { 7 }, got.Blob);
        Assert.Same(sender, got.Sender);

        Assert.Equal("Answered", calls.PeerState());
        Assert.Equal(3, calls.Drain(3));
        Assert.Equal(1, calls.PeerVersion(sender));
        Assert.True(calls.Subscribe(RotLinkRules.EnteredType, RotLinkRules.EnteredKey));
        Assert.True(calls.ToAll("net", "rot.probe", "day", 2, new[] { "1" }, null));
        Assert.True(calls.ToPlayer("net", sender, false, "rot.world", "snapshot", 1, new[] { "1", "1", "1" }, new byte[1]));
        Assert.True(calls.ToServer("net", "rot.story", "answer", 3, new[] { "1", "0" }, null));
        calls.SessionStarted();
        calls.SessionEnded();
        Assert.Equal(new[] { "ToAll rot.probe/day", "ToPlayer rot.world/snapshot immediate False", "ToServer rot.story/answer", "SessionStarted", "SessionEnded" }, StandIn.Link.Calls);
    }

    [Fact]
    public void A_link_whose_Start_fails_is_not_registered_and_the_boot_says_so()
    {
        StandIn.Link.Reset();
        StandIn.Link.StartResult = false;
        var calls = new RotLinkCalls();
        Assert.Empty(calls.Bind(typeof(StandIn.Link), typeof(LinkMessage)));
        Assert.False(calls.Start(_ => { }, () => null, "rot.", _ => { }));
        Assert.Null(StandIn.Link.Prefix);
        Assert.Null(StandIn.Link.Callback);

        var boot = new LinkBoot();
        boot.Probed(new List<string>());
        boot.Loaded(true, calls.Version(), new List<string>(), false);
        Assert.Equal(LinkPhase.Off, boot.Phase);
        Assert.Equal("Coodsz.CoopLink's Start failed (its line above says why)", boot.OffReason);
        StandIn.Link.Reset();
    }

    [Fact]
    public void A_link_copy_without_a_member_this_module_calls_is_named_and_not_bound()
    {
        var missing = new RotLinkCalls().Bind(typeof(StandIn.OldLink), typeof(LinkMessage));
        Assert.Equal(new[] { "OldLink.Version", "OldLink.Log", "OldLink.NetworkProvider", "OldLink.PeerState" }, missing.Take(4));
        Assert.Contains("OldLink.ToPlayer", missing);
        Assert.DoesNotContain("OldLink.Start", missing);
        Assert.Equal(new[] { "Coodsz.CoopLink.Link" }, new RotLinkCalls().Bind(null!, typeof(LinkMessage)));
    }

    // The stand in mirrors these declarations of Link.cs word for word, so a change there fails here.
    [Theory]
    [InlineData("public static int Version => 1;")]
    [InlineData("public static Action<string>? Log { get; set; }")]
    [InlineData("public static Func<object?>? NetworkProvider { get; set; }")]
    [InlineData("public static PeerLinkState PeerState =>")]
    [InlineData("public static bool Start()")]
    [InlineData("public static bool Register(string channelPrefix, Action<LinkMessage> onMessage)")]
    [InlineData("public static int Drain(int maxItems)")]
    [InlineData("public static void SessionStarted()")]
    [InlineData("public static void SessionEnded()")]
    [InlineData("public static int GetPeerLinkVersion(object? peer)")]
    [InlineData("public static bool ToAll(object network, string channel, string key, int seq, IList<string>? args, byte[]? blob)")]
    [InlineData("public static bool ToPlayer(object network, object peer, bool immediate, string channel, string key, int seq, IList<string>? args, byte[]? blob)")]
    [InlineData("public static bool ToServer(object network, string channel, string key, int seq, IList<string>? args, byte[]? blob)")]
    [InlineData("public static bool TrySubscribeByName(string fullTypeName, string key)")]
    public void The_stand_in_has_the_signatures_of_the_real_link(string declaration)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszCoopLink", "src", "Link.cs"));
        Assert.Contains(declaration, source);
    }

    // ---- the built module ---------------------------------------------------------------------

    private static string CompatDll => RealmOfThronesCompatTests.CompatDll;

    // The engine probes a SubModule DLL's references in its own bin folder and logs a "Cannot load"
    // ERROR at server boot for one it does not find, so the module never references the link.
    [RealmOfThronesCompatTests.CompiledCompatFact]
    public void The_built_module_does_not_reference_the_link_and_carries_its_probe_as_source()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(CompatDll);
        Assert.DoesNotContain(module.AssemblyReferences, reference => reference.Name == LinkProbe.AssemblyName);
        Assert.NotNull(module.GetType("Coodsz.CoopLink.LinkProbe"));
        Assert.Null(module.GetType("Coodsz.CoopLink.Link"));
    }

    [Fact]
    public void Build_puts_the_link_DLL_next_to_ours_in_both_bin_folders()
    {
        var build = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat", "Build.cmd"));
        Assert.Contains("dotnet build \"%LINK%\\CoodszCoopLink.csproj\"", build);
        Assert.Contains("copy /y \"%LINK%\\bin\\Release\\Coodsz.CoopLink.dll\" \"%SHIP%\\bin\\Win64_Shipping_Client\\\"", build);
        Assert.Contains("copy /y \"%LINK%\\bin\\Release\\Coodsz.CoopLink.dll\" \"%SHIP%\\bin\\Win64_Shipping_Server\\\"", build);
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat", "RealmOfThronesCoopCompat.csproj"));
        Assert.DoesNotContain("CoodszCoopLink.csproj", project);
        Assert.Contains("<Compile Include=\"..\\CoodszCoopLink\\src\\LinkProbe.cs\"", project);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // Link's public statics as the module calls them, recording what it was asked.
    private static class StandIn
    {
        public static class Link
        {
            public static readonly List<string> Calls = new();
            public static string? Prefix;
            public static Action<LinkMessage>? Callback;

            public static void Reset()
            {
                Calls.Clear();
                Prefix = null;
                Callback = null;
                Log = null;
                NetworkProvider = null;
                StartResult = true;
            }

            // What Start answers; false as when Coop's broker refused the subscription.
            public static bool StartResult = true;

            public static int Version => 1;
            public static Action<string>? Log { get; set; }
            public static Func<object?>? NetworkProvider { get; set; }
            public static PeerLinkState PeerState => PeerLinkState.Answered;
            public static bool Start() => StartResult;

            public static bool Register(string channelPrefix, Action<LinkMessage> onMessage)
            {
                Prefix = channelPrefix;
                Callback = onMessage;
                return true;
            }

            public static int Drain(int maxItems) => maxItems;
            public static void SessionStarted() => Calls.Add("SessionStarted");
            public static void SessionEnded() => Calls.Add("SessionEnded");
            public static int GetPeerLinkVersion(object? peer) => peer == null ? 0 : 1;

            public static bool ToAll(object network, string channel, string key, int seq, IList<string>? args, byte[]? blob)
            {
                Calls.Add("ToAll " + channel + "/" + key);
                return true;
            }

            public static bool ToPlayer(object network, object peer, bool immediate, string channel, string key, int seq, IList<string>? args, byte[]? blob)
            {
                Calls.Add("ToPlayer " + channel + "/" + key + " immediate " + immediate);
                return true;
            }

            public static bool ToServer(object network, string channel, string key, int seq, IList<string>? args, byte[]? blob)
            {
                Calls.Add("ToServer " + channel + "/" + key);
                return true;
            }

            public static bool TrySubscribeByName(string fullTypeName, string key) => true;
        }

        // A copy without most of what version 1 has.
        public static class OldLink
        {
            public static bool Start() => true;
        }
    }
}
