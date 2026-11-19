using Coodsz.CoopLink;
using ProtoBuf;

namespace BannerlordCoopLauncher.Tests
{
    // Coodsz.CoopLink: the parts that need no Coop (LinkCore.cs, LinkProbe.cs, linked) and its two wire types
    // (LinkWire.cs, linked, over the stand ins for Coop's message interfaces and the real protobuf-net Coop
    // ships). The harness in the scratchpad runs the rest against the shipped Coop DLLs.
    public sealed class CoodszCoopLinkTests
    {
        private readonly List<string> _log = new();
        private readonly List<object?> _welcomes = new();
        private int _hellos;
        private bool _isServer;
        private long _now = 1000;

        private LinkHub NewHub() => new(_log.Add, () => _isServer, peer => _welcomes.Add(peer), () => { _hellos++; return true; }, () => _now);

        private static LinkMessage Msg(string channel, string key = "", int seq = 0, object? sender = null, bool fromServer = true, int version = 1) =>
            new(version, channel, key, seq, null, null, sender, fromServer);

        // ---- rules -----------------------------------------------------------------------------

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void A_ToClient_counts_only_on_a_players_PC_and_a_ToServer_only_on_the_server(bool toClient, bool isServer, bool accepted)
        {
            Assert.Equal(accepted, LinkRules.Accepts(toClient, isServer));
        }

        // Ids computed by Coop's own SerializableTypeMapper.StableId on the shipped Common.dll (harness step 5).
        [Theory]
        [InlineData("Coodsz.CoopLink.ToClient", 1603751682)]
        [InlineData("Coodsz.CoopLink.ToServer", 201837486)]
        [InlineData("RealmOfThronesCoopCompat.Net.RotToClient", 836388601)]
        [InlineData("Common.Messaging.MessagePayload`1", 1111801786)]
        [InlineData("", 18652613)]
        public void The_wire_id_matches_Coops_rule(string fullName, int id)
        {
            Assert.Equal(id, LinkRules.WireId(fullName));
        }

        [Fact]
        public void The_wire_ids_of_the_two_types_are_the_ones_the_protocol_is_built_on()
        {
            Assert.Equal(LinkRules.WireId(typeof(ToClient).FullName!), LinkRules.WireId("Coodsz.CoopLink.ToClient"));
            Assert.Equal(LinkRules.WireId(typeof(ToServer).FullName!), LinkRules.WireId("Coodsz.CoopLink.ToServer"));
        }

        [Fact]
        public void A_message_up_to_256_KB_passes_and_one_byte_more_is_refused()
        {
            var args = new List<string> { "ab", "ü" }; // 2 + 2 UTF-8 bytes
            int blob = LinkRules.MaxMessageBytes - "rot.x".Length - "k".Length - 4;
            Assert.True(LinkRules.CheckSize("rot.x", "k", args, new byte[blob], out int bytes, out _));
            Assert.Equal(LinkRules.MaxMessageBytes, bytes);
            Assert.False(LinkRules.CheckSize("rot.x", "k", args, new byte[blob + 1], out _, out string reason));
            Assert.Contains("too large", reason);
        }

        [Fact]
        public void Too_many_args_are_refused_even_when_small()
        {
            Assert.True(LinkRules.CheckSize("c", "k", Enumerable.Repeat("", LinkRules.MaxArgs).ToList(), null, out _, out _));
            Assert.False(LinkRules.CheckSize("c", "k", Enumerable.Repeat("", LinkRules.MaxArgs + 1).ToList(), null, out _, out string reason));
            Assert.Contains("too many args", reason);
        }

        [Theory]
        [InlineData("rot.event", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("link.hello", false)]
        [InlineData("link.", false)]
        [InlineData("linkx", true)]
        public void Mods_cannot_use_the_links_own_channels(string? channel, bool allowed)
        {
            Assert.Equal(allowed, LinkRules.IsModChannel(channel));
        }

        // ---- the wire ------------------------------------------------------------------------------

        [Fact]
        public void The_member_numbers_are_the_protocol()
        {
            var message = new ToServer { Version = 1, Channel = "c", Key = "k", Seq = 2, Args = new List<string> { "a" }, Blob = new byte[] { 7 } };
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, message);
            // 1:varint 1, 2:"c", 3:"k", 4:varint 2, 5:"a", 6:bytes {7}
            Assert.Equal("08-01-12-01-63-1A-01-6B-20-02-2A-01-61-32-01-07", BitConverter.ToString(stream.ToArray()));
        }

        [Fact]
        public void Both_types_round_trip_through_protobuf()
        {
            var to = RoundTrip<ToClient>(new ToClient { Version = 1, Channel = "rot.event", Key = "open", Seq = -5, Args = new List<string> { "Dragon", "" }, Blob = new byte[] { 1, 2, 3 } });
            Assert.Equal(("rot.event", "open", -5, 1), (to.Channel, to.Key, to.Seq, to.Version));
            Assert.Equal(new[] { "Dragon", "" }, to.Args);
            Assert.Equal(new byte[] { 1, 2, 3 }, to.Blob);
            var empty = RoundTrip<ToServer>(new ToServer());
            Assert.Null(empty.Channel);
            Assert.Equal(0, empty.Seq);
        }

        private static T RoundTrip<T>(T message)
        {
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, message);
            stream.Position = 0;
            return Serializer.Deserialize<T>(stream);
        }

        // ---- channel registry and queue ------------------------------------------------------------

        [Fact]
        public void Messages_reach_the_longest_matching_prefix_in_arrival_order_with_their_sender()
        {
            LinkHub hub = NewHub();
            var got = new List<string>();
            var peer = new object();
            Assert.True(hub.Register("rot.", m => got.Add("rot:" + m.Key)));
            Assert.True(hub.Register("rot.event", m => got.Add("event:" + m.Key + ":" + (m.Sender == peer))));
            hub.Receive(Msg("rot.event", "a", sender: peer));
            hub.Receive(Msg("rot.settings", "b"));
            hub.Receive(Msg("rot.event.x", "c", sender: peer));
            Assert.Equal(0, got.Count);
            Assert.Equal(3, hub.Drain(100));
            Assert.Equal(new[] { "event:a:True", "rot:b", "event:c:True" }, got);
        }

        [Fact]
        public void Drain_hands_over_at_most_max_items_and_keeps_the_rest()
        {
            LinkHub hub = NewHub();
            var got = new List<int>();
            hub.Register("c.", m => got.Add(m.Seq));
            for (int i = 1; i <= 5; i++) hub.Receive(Msg("c.x", seq: i));
            Assert.Equal(2, hub.Drain(2));
            Assert.Equal(3, hub.Queued);
            Assert.Equal(3, hub.Drain(10));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, got);
        }

        [Fact]
        public void Registering_a_prefix_again_replaces_it_and_logs()
        {
            LinkHub hub = NewHub();
            var got = new List<string>();
            hub.Register("c.", m => got.Add("old"));
            hub.Register("c.", m => got.Add("new"));
            hub.Receive(Msg("c.x"));
            hub.Drain(10);
            Assert.Equal(new[] { "new" }, got);
            Assert.Contains(_log, l => l.Contains("replaced"));
        }

        [Fact]
        public void The_links_own_prefix_and_empty_prefixes_cannot_be_registered()
        {
            LinkHub hub = NewHub();
            Assert.False(hub.Register("link.hello", m => { }));
            Assert.False(hub.Register("", m => { }));
            Assert.False(hub.RegisterNetworkThread("link.", m => true));
        }

        [Fact]
        public void An_unknown_channel_is_counted_every_time_and_logged_once()
        {
            LinkHub hub = NewHub();
            hub.Receive(Msg("nobody.a"));
            hub.Receive(Msg("nobody.a"));
            hub.Receive(Msg("nobody.b"));
            hub.Drain(10);
            Assert.Equal(3, hub.UnknownChannelCount);
            Assert.Equal(2, _log.Count(l => l.Contains("No callback")));
        }

        // A modified client can make up any number of routes: only the first 64 are kept and logged, then one line.
        [Fact]
        public void Unknown_routes_past_the_cap_are_counted_but_neither_kept_nor_logged_one_by_one()
        {
            LinkHub hub = NewHub();
            for (int i = 0; i < LinkRules.MaxUnknownLogged + 50; i++)
                hub.Receive(Msg("nobody." + i));
            hub.Receive(Msg("nobody.0"));
            hub.Drain(1000);
            Assert.Equal(LinkRules.MaxUnknownLogged + 51, hub.UnknownChannelCount);
            Assert.Equal(LinkRules.MaxUnknownLogged, _log.Count(l => l.Contains("No callback")));
            Assert.Single(_log, l => l.StartsWith("More unknown routes came", StringComparison.Ordinal));
        }

        [Fact]
        public void A_route_from_the_other_side_is_logged_on_one_line_and_shortened()
        {
            LinkHub hub = NewHub();
            hub.Receive(Msg("nobody.x\r\n12:00 Server: forged\u2028line\t" + new string('y', 200)));
            hub.Drain(10);
            string line = Assert.Single(_log, l => l.StartsWith("No callback", StringComparison.Ordinal));
            Assert.DoesNotContain(line, c => char.IsControl(c) || c == '\u2028');
            Assert.Contains("nobody.x??12:00 Server: forged?line?yyy", line);
            Assert.Contains("...\"", line);
            Assert.Equal(LinkRules.MaxShownChars + 3, LinkRules.Shown(new string('z', 500)).Length);
            Assert.Equal("", LinkRules.Shown(null));
            Assert.Equal(new string('z', LinkRules.MaxShownChars), LinkRules.Shown(new string('z', LinkRules.MaxShownChars)));
        }

        // The send limits hold on receive too, so a modified client can't fill the queue with oversized messages.
        [Fact]
        public void A_received_message_over_the_limits_is_dropped_before_the_queue_and_logged_once()
        {
            LinkHub hub = NewHub();
            var got = new List<string>();
            hub.Register("c.", m => got.Add(m.Key));
            Assert.Equal(0, hub.Receive(new LinkMessage(1, "c.x", "big", 0, null, new byte[LinkRules.MaxMessageBytes + 1], null, true)));
            Assert.Equal(0, hub.Receive(new LinkMessage(1, "c.x", "many", 0, Enumerable.Repeat("", LinkRules.MaxArgs + 1).ToList(), null, null, true)));
            Assert.Equal(0, hub.Queued);
            Assert.NotEqual(0, hub.Receive(Msg("c.x", "fine")));
            hub.Drain(10);
            Assert.Equal(new[] { "fine" }, got);
            Assert.Single(_log, l => l.Contains("was dropped: message too large"));
        }

        [Fact]
        public void A_callback_that_throws_is_logged_and_the_next_message_still_arrives()
        {
            LinkHub hub = NewHub();
            var got = new List<string>();
            hub.Register("c.", m => { if (m.Key == "bad") throw new InvalidOperationException("boom"); got.Add(m.Key); });
            hub.Receive(Msg("c.x", "bad"));
            hub.Receive(Msg("c.x", "good"));
            Assert.Equal(2, hub.Drain(10));
            Assert.Equal(new[] { "good" }, got);
            Assert.Contains(_log, l => l.Contains("boom"));
        }

        [Fact]
        public void A_network_thread_callback_can_take_a_message_before_the_queue()
        {
            LinkHub hub = NewHub();
            var onGame = new List<string>();
            var onNetwork = new List<string>();
            hub.Register("s.", m => onGame.Add(m.Key));
            hub.RegisterNetworkThread("s.", m => { onNetwork.Add(m.Key); return m.Key == "take"; });
            Assert.Equal(0, hub.Receive(Msg("s.x", "take")));
            Assert.NotEqual(0, hub.Receive(Msg("s.x", "pass")));
            hub.Drain(10);
            Assert.Equal(new[] { "take", "pass" }, onNetwork);
            Assert.Equal(new[] { "pass" }, onGame);
        }

        [Fact]
        public void Lifecycle_events_are_routed_by_their_key_so_two_mods_can_forward_the_same_event()
        {
            LinkHub hub = NewHub();
            var got = new List<string>();
            hub.Register("rot.", m => got.Add("rot " + m.Key));
            hub.Register("chaos.", m => got.Add("chaos " + m.Key));
            hub.Receive(new LinkMessage(0, LinkRules.EventChannel, "rot.entered", 0, null, null, null, false));
            hub.Receive(new LinkMessage(0, LinkRules.EventChannel, "chaos.entered", 0, null, null, null, false));
            hub.Drain(10);
            Assert.Equal(new[] { "rot rot.entered", "chaos chaos.entered" }, got);
        }

        [Fact]
        public void DrainThrough_hands_over_up_to_its_own_message_and_never_twice()
        {
            LinkHub hub = NewHub();
            var got = new List<string>();
            hub.Register("c.", m => got.Add(m.Key));
            long a = hub.Receive(Msg("c.x", "a"));
            long b = hub.Receive(Msg("c.x", "b"));
            long c = hub.Receive(Msg("c.x", "c"));
            Assert.Equal(1, hub.DrainThrough(a));
            Assert.Equal(2, hub.Drain(10));
            Assert.Equal(0, hub.DrainThrough(b));
            Assert.Equal(0, hub.DrainThrough(c));
            Assert.Equal(new[] { "a", "b", "c" }, got);
        }

        [Fact]
        public void A_Drain_from_inside_a_callback_does_nothing()
        {
            LinkHub hub = NewHub();
            int inner = -1;
            hub.Register("c.", m => inner = hub.Drain(10));
            hub.Receive(Msg("c.x"));
            hub.Receive(Msg("c.x"));
            Assert.Equal(1, hub.Drain(1));
            Assert.Equal(0, inner);
            Assert.Equal(1, hub.Queued);
        }

        [Fact]
        public void A_full_queue_drops_new_messages_and_logs_once()
        {
            LinkHub hub = NewHub();
            for (int i = 0; i < LinkHub.MaxQueued; i++) Assert.NotEqual(0, hub.Receive(Msg("c.x")));
            Assert.Equal(0, hub.Receive(Msg("c.x")));
            Assert.Equal(0, hub.Receive(Msg("c.x")));
            Assert.Equal(LinkHub.MaxQueued, hub.Queued);
            Assert.Equal(1, _log.Count(l => l.Contains("queue is full")));
        }

        [Fact]
        public void A_session_end_drops_the_queue_and_the_handshake_state()
        {
            LinkHub hub = NewHub();
            _isServer = true;
            var peer = new object();
            hub.SessionStarted();
            hub.Receive(Msg("c.x", sender: peer, fromServer: false, version: 3));
            Assert.Equal(3, hub.PeerVersion(peer));
            hub.SessionEnded();
            Assert.Equal(0, hub.Queued);
            Assert.Equal(0, hub.PeerVersion(peer));
            Assert.Equal(PeerLinkState.NoSession, hub.PeerState);
        }

        // ---- handshake -----------------------------------------------------------------------------

        [Fact]
        public void The_server_answers_a_hello_to_its_sender_and_remembers_the_version()
        {
            LinkHub hub = NewHub();
            _isServer = true;
            var peer = new object();
            hub.SessionStarted();
            Assert.Equal(PeerLinkState.Waiting, hub.PeerState);
            Assert.Equal(0, hub.Receive(Msg(LinkRules.HelloChannel, LinkRules.HelloKey, sender: peer, fromServer: false, version: 1)));
            Assert.Same(peer, Assert.Single(_welcomes));
            Assert.Equal(1, hub.PeerVersion(peer));
            Assert.Equal(PeerLinkState.Answered, hub.PeerState);
            Assert.Equal(0, hub.Queued);
        }

        [Fact]
        public void A_client_says_hello_every_3_seconds_until_answered()
        {
            LinkHub hub = NewHub();
            hub.Drain(1);
            Assert.Equal(0, _hellos); // no session yet
            hub.SessionStarted();
            hub.Drain(1);
            Assert.Equal(1, _hellos);
            _now += LinkHub.HelloIntervalMs - 1;
            hub.Drain(1);
            Assert.Equal(1, _hellos);
            _now += 1;
            hub.Drain(1);
            Assert.Equal(2, _hellos);
            Assert.Equal(PeerLinkState.Waiting, hub.PeerState);
            hub.Receive(Msg(LinkRules.HelloChannel, LinkRules.WelcomeKey, fromServer: true, version: 1));
            Assert.Equal(1, hub.ServerVersion);
            Assert.Equal(PeerLinkState.Answered, hub.PeerState);
            _now += 10 * LinkHub.HelloIntervalMs;
            hub.Drain(1);
            Assert.Equal(2, _hellos);
        }

        [Fact]
        public void A_client_gives_up_after_20_hellos()
        {
            LinkHub hub = NewHub();
            hub.SessionStarted();
            for (int i = 0; i < 30; i++)
            {
                hub.Drain(1);
                _now += LinkHub.HelloIntervalMs;
            }
            Assert.Equal(LinkHub.MaxHelloTries, _hellos);
            Assert.Equal(PeerLinkState.NoAnswer, hub.PeerState);
            Assert.Contains(_log, l => l.Contains("has not answered"));
        }

        [Fact]
        public void A_welcome_that_comes_before_the_mods_session_start_is_kept()
        {
            LinkHub hub = NewHub();
            hub.SayHelloNow();
            hub.Receive(Msg(LinkRules.HelloChannel, LinkRules.WelcomeKey, fromServer: true, version: 1));
            hub.SessionStarted();
            Assert.Equal(PeerLinkState.Answered, hub.PeerState);
            hub.Drain(1);
            Assert.Equal(1, _hellos);
        }

        [Fact]
        public void Any_message_from_the_server_counts_as_an_answer()
        {
            LinkHub hub = NewHub();
            hub.SessionStarted();
            hub.Receive(Msg("rot.event", version: 2));
            Assert.Equal(2, hub.ServerVersion);
        }

        // ---- TrySubscribeByName's memory -----------------------------------------------------------

        [Fact]
        public void A_type_name_not_found_is_reported_once_however_often_a_mod_asks_again()
        {
            var memory = new SubscribeMemory();
            Assert.True(memory.FirstMiss("Coop.Core.Server.Connections.Messages.NetworkPlayerCampaignEntered"));
            for (int i = 0; i < 10; i++)
                Assert.False(memory.FirstMiss("Coop.Core.Server.Connections.Messages.NetworkPlayerCampaignEntered"));
            Assert.True(memory.FirstMiss("Some.Other.Message"));
        }

        [Fact]
        public void A_type_is_subscribed_once_and_a_failed_subscribe_can_be_tried_again()
        {
            var memory = new SubscribeMemory();
            Assert.True(memory.Claim(typeof(string)));
            Assert.False(memory.Claim(typeof(string)));
            memory.Unclaim(typeof(string));
            Assert.True(memory.Claim(typeof(string)));
            Assert.False(memory.TryGetKeys(typeof(string), out _));
            Assert.False(memory.TryGetKeys(typeof(int), out _));
        }

        [Fact]
        public void A_subscribe_that_keeps_failing_is_reported_once_per_type_name()
        {
            var memory = new SubscribeMemory();
            Assert.True(memory.FirstFailure("Coop.Core.Some.Message"));
            for (int i = 0; i < 10; i++)
                Assert.False(memory.FirstFailure("Coop.Core.Some.Message"));
            Assert.True(memory.FirstFailure("Coop.Core.Other.Message"));
            // A miss and a failure of the same name are said once each.
            Assert.True(memory.FirstMiss("Coop.Core.Some.Message"));
        }

        [Fact]
        public void An_unclaim_after_a_failed_subscribe_keeps_the_key_another_caller_added_meanwhile()
        {
            var memory = new SubscribeMemory();
            Assert.True(memory.Claim(typeof(string)));
            // Another mod adds its key while the first caller's subscribe is failing.
            Assert.True(memory.AddKey(typeof(string), "chaos.entered"));
            memory.Unclaim(typeof(string));
            Assert.True(memory.TryGetKeys(typeof(string), out string[] keys));
            Assert.Equal(new[] { "chaos.entered" }, keys);
            // The next call may subscribe again and the key is still there.
            Assert.True(memory.Claim(typeof(string)));
            Assert.True(memory.AddKey(typeof(string), "rot.entered"));
            Assert.True(memory.TryGetKeys(typeof(string), out keys));
            Assert.Equal(new[] { "chaos.entered", "rot.entered" }, keys);
        }

        [Fact]
        public void The_same_key_is_added_once_and_two_mods_keys_both_stay()
        {
            var memory = new SubscribeMemory();
            memory.Claim(typeof(string));
            Assert.True(memory.AddKey(typeof(string), "rot.entered"));
            Assert.False(memory.AddKey(typeof(string), "rot.entered"));
            Assert.True(memory.AddKey(typeof(string), "chaos.entered"));
            Assert.True(memory.TryGetKeys(typeof(string), out string[] keys));
            Assert.Equal(new[] { "rot.entered", "chaos.entered" }, keys);
        }

        // ---- probe ---------------------------------------------------------------------------------

        private static readonly Dictionary<string, Type> FullCoop = new()
        {
            ["Common.Messaging.IMessage"] = typeof(Common.Messaging.IMessage),
            ["Common.Messaging.ICommand"] = typeof(Common.Messaging.ICommand),
            ["Common.Messaging.MessageBroker"] = typeof(Common.Messaging.MessageBroker),
            ["Common.Messaging.MessagePayload`1"] = typeof(Common.Messaging.MessagePayload<>),
            ["Common.Network.INetwork"] = typeof(Common.Network.INetwork),
            ["Common.ModInformation"] = typeof(Common.ModInformation),
            ["Common.GameThread"] = typeof(Common.GameThread),
            ["LiteNetLib.NetPeer"] = typeof(LiteNetLib.NetPeer),
            ["ProtoBuf.ProtoContractAttribute"] = typeof(ProtoContractAttribute),
            ["ProtoBuf.ProtoMemberAttribute"] = typeof(ProtoMemberAttribute),
        };

        private static Func<string, string, Type?> Without(params string[] names) =>
            (assembly, type) => names.Contains(type) ? null : FullCoop.TryGetValue(type, out Type? t) ? t : null;

        [Fact]
        public void The_probe_passes_when_every_member_is_there()
        {
            var optional = new List<string>();
            Assert.Empty(LinkProbe.Check(Without(), optional));
            Assert.Empty(optional);
        }

        [Fact]
        public void The_probe_names_what_is_missing()
        {
            Assert.Equal(new[] { "Common.Network.INetwork" }, LinkProbe.Check(Without("Common.Network.INetwork"), null));
            Assert.Equal(new[] { "LiteNetLib.NetPeer" }, LinkProbe.Check(Without("LiteNetLib.NetPeer"), null));
            var optional = new List<string>();
            Assert.Empty(LinkProbe.Check(Without("Common.GameThread"), optional));
            Assert.Single(optional);
        }

        [Fact]
        public void The_probe_says_Coop_is_not_loaded_yet_when_Common_is_missing()
        {
            Assert.Equal(new[] { "Coop's Common assembly" }, LinkProbe.Check((a, t) => null, null));
        }

        [Fact]
        public void The_probe_finds_a_changed_send_signature()
        {
            var changed = new Dictionary<string, Type>(FullCoop) { ["Common.Network.INetwork"] = typeof(Common.Network.Changed.INetwork) };
            List<string> missing = LinkProbe.Check((a, t) => changed.TryGetValue(t, out Type? x) ? x : null, null);
            Assert.Equal(new[] { "INetwork.SendImmediate(NetPeer, IMessage)" }, missing);
        }
    }
}

// Shapes of the Coop members the probe checks, by name only, for the probe tests. IMessage and ICommand come
// from CoopMessagingStandIns.cs, NetPeer from the LiteNetLib the test project already gets.
namespace Common.Messaging
{
    public sealed class MessageBroker
    {
        public static MessageBroker? Instance => null;

        public void Subscribe<T>(Action<MessagePayload<T>> subscription) where T : IMessage { }
    }

    public sealed class MessagePayload<T>
    {
        public object? Who { get; set; }

        public T? What { get; }
    }
}

namespace Common.Network
{
    public interface INetwork
    {
        void Send(LiteNetLib.NetPeer peer, Common.Messaging.IMessage message);
        void SendImmediate(LiteNetLib.NetPeer peer, Common.Messaging.IMessage message);
        void SendAll(Common.Messaging.IMessage message);
    }
}

namespace Common.Network.Changed
{
    public interface INetwork
    {
        void Send(LiteNetLib.NetPeer peer, Common.Messaging.IMessage message);
        void SendImmediate(LiteNetLib.NetPeer peer, object message);
        void SendAll(Common.Messaging.IMessage message);
    }
}

namespace Common
{
    public static class ModInformation
    {
        public static bool IsServer { get; set; }
    }

    public static class GameThread
    {
        public static void RunSafe(Action action, bool blocking = false, string? context = null) { }
    }
}
