using CoodszSettingsSync;
using CoodszSettingsSync.Messages;
using Coodsz.ServerRules;
using ProtoBuf;

namespace BannerlordCoopLauncher.Tests;

// The PvP countdown: the response timer's seconds left that players with the Coodsz client add on see. The
// messages (SettingsSyncMessages.cs, linked, through the real protobuf-net Coop ships), the server's plan of what
// each player sees (PvpCountdowns in PvpRulesRules.cs), and the client's text table and clock (PvpCountdownRules.cs).
public sealed class PvpCountdownTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PvpRulesSettings TimerOn = new(false, true, 30);
    private static readonly object CaughtPeer = new();
    private static readonly object AttackerPeer = new();

    private static object? Peers(string id, PvpCountdownRole role) => role == PvpCountdownRole.Caught ? CaughtPeer : AttackerPeer;

    private static T RoundTrip<T>(object message)
    {
        using var stream = new MemoryStream();
        Serializer.NonGeneric.Serialize(stream, message);
        stream.Position = 0;
        return Serializer.Deserialize<T>(stream);
    }

    // ---- the messages --------------------------------------------------------------------------

    [Fact]
    public void The_countdown_messages_round_trip_through_protobuf()
    {
        var start = RoundTrip<NetworkPvpCountdown>(new NetworkPvpCountdown { ProtocolVersion = 1, SessionId = "s-42", SecondsLeft = 27, Role = 2 });
        Assert.Equal(1, start.ProtocolVersion);
        Assert.Equal("s-42", start.SessionId);
        Assert.Equal(27, start.SecondsLeft);
        Assert.Equal(2, start.Role);

        var stop = RoundTrip<NetworkPvpCountdownStop>(new NetworkPvpCountdownStop { ProtocolVersion = 1, SessionId = "s-42" });
        Assert.Equal(1, stop.ProtocolVersion);
        Assert.Equal("s-42", stop.SessionId);

        var request = RoundTrip<NetworkModSettingsRequest>(new NetworkModSettingsRequest { ProtocolVersion = 1, WantsWarOfTheRing = true, WantsPvpCountdown = true });
        Assert.True(request.WantsPvpCountdown);
        Assert.True(request.WantsWarOfTheRing);
    }

    // The request as the add on in v1.3.2 and before sends it (fields 1 and 2 only).
    [ProtoContract(SkipConstructor = true)]
    public sealed class OldRequest
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }
        [ProtoMember(2)] public bool WantsWarOfTheRing { get; set; }
    }

    [Fact]
    public void An_older_add_on_never_asks_for_the_countdown_and_an_older_server_reads_the_new_request()
    {
        var fromOld = RoundTrip<NetworkModSettingsRequest>(new OldRequest { ProtocolVersion = 1, WantsWarOfTheRing = true });
        Assert.False(fromOld.WantsPvpCountdown);
        Assert.True(fromOld.WantsWarOfTheRing);

        var readByOld = RoundTrip<OldRequest>(new NetworkModSettingsRequest { ProtocolVersion = 1, WantsWarOfTheRing = true, WantsPvpCountdown = true });
        Assert.Equal(1, readByOld.ProtocolVersion);
        Assert.True(readByOld.WantsWarOfTheRing);
    }

    [Fact]
    public void The_message_type_names_dont_collide_in_coops_type_ids()
    {
        // Coop's SerializableTypeMapper.StableId (Common v0.1.6): a collision throws while Coop collects its types.
        static int StableId(string fullName)
        {
            uint hash = 2166136261u;
            foreach (char c in fullName)
            {
                hash = (hash ^ (byte)c) * 16777619;
                hash = (hash ^ (byte)(c >> 8)) * 16777619;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
        var names = typeof(NetworkModSettingsRequest).Assembly.GetTypes()
            .Where(t => t.Namespace == "CoodszSettingsSync.Messages" && t.IsDefined(typeof(ProtoContractAttribute), false))
            .Select(t => t.FullName!).ToList();
        Assert.Contains("CoodszSettingsSync.Messages.NetworkPvpCountdown", names);
        Assert.Contains("CoodszSettingsSync.Messages.NetworkPvpCountdownStop", names);
        Assert.Equal(names.Count, names.Select(StableId).Distinct().Count());
    }

    [Fact]
    public void The_server_and_client_limits_agree()
    {
        Assert.Equal(PvpCountdownWire.CaughtRole, (int)PvpCountdownRole.Caught);
        Assert.Equal(PvpCountdownWire.AttackerRole, (int)PvpCountdownRole.Attacker);
        Assert.Equal(PvpRulesSettings.MostSeconds, PvpCountdownWire.MostSeconds);
        Assert.True(PvpCountdownWire.IsValidStart(1, "s", 300, 1));
        Assert.False(PvpCountdownWire.IsValidStart(2, "s", 30, 1));
        Assert.False(PvpCountdownWire.IsValidStart(1, "", 30, 1));
        Assert.False(PvpCountdownWire.IsValidStart(1, new string('x', 129), 30, 1));
        Assert.False(PvpCountdownWire.IsValidStart(1, "s", 0, 1));
        Assert.False(PvpCountdownWire.IsValidStart(1, "s", 301, 1));
        Assert.False(PvpCountdownWire.IsValidStart(1, "s", 30, 3));
    }

    // ---- who gets sent -------------------------------------------------------------------------

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void Only_players_whose_add_on_asked_are_sent_the_countdown()
    {
        var server = Source("compat", "CoodszSettingsSync", "src", "Server", "PvpCountdownServer.cs");
        // The handler only adds a player whose request set WantsPvpCountdown.
        Assert.Contains("!payload.What.WantsPvpCountdown", server);
        Assert.Contains("PvpCountdownServer.Subscribe(peer)", server);
        // Both sends check that list first, and go past Coop's join gate like the snapshots.
        Assert.Equal(2, CountOf(server, "network == null || !Wants(peer)"));
        Assert.Equal(2, CountOf(server, "network.SendImmediate((NetPeer)peer!, new NetworkPvpCountdown"));
        Assert.DoesNotContain("network.Send(", server);

        var client = Source("compat", "CoodszSettingsSync", "src", "Client", "ClientSync.cs");
        Assert.Contains("WantsPvpCountdown = true,", client);
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    [Fact]
    public void The_server_tools_send_through_the_loaded_sync_after_the_timeouts()
    {
        var rules = Source("compat", "CoodszServerTools", "src", "PvpRules.cs");
        Assert.Contains("sync.Entry(type, \"Countdown\")", rules);
        Assert.Contains("sync.Entry(type, \"Stop\")", rules);
        Assert.True(rules.IndexOf("try { EndExpired(); }", StringComparison.Ordinal) < rules.IndexOf("try { SendCountdowns(); }", StringComparison.Ordinal));
        Assert.Contains("\"ResponderPeer\" : \"InitiatorPeer\"", rules);
    }

    // A stand in for LiteNetLib's NetPeer: one object whose connection can drop and, as LiteNetLib may do, be reused.
    private sealed class FakePeer
    {
        public bool Connected = true;
    }

    private static PvpCountdownRecipients<FakePeer> Recipients() => new(p => p.Connected, 4);

    [Fact]
    public void A_player_who_disconnected_leaves_the_list_so_a_reused_peer_starts_without_it()
    {
        var recipients = Recipients();
        var peer = new FakePeer();
        Assert.True(recipients.Add(peer));
        Assert.True(recipients.Wants(peer));

        // Coop's PlayerDisconnected.
        recipients.Remove(peer);
        Assert.Equal(0, recipients.Count);

        // LiteNetLib hands the same object to the next player, whose add on never asked.
        Assert.False(recipients.Wants(peer));
    }

    [Fact]
    public void A_player_found_not_connected_is_dropped_even_without_the_disconnect_message()
    {
        var recipients = Recipients();
        var peer = new FakePeer();
        recipients.Add(peer);
        peer.Connected = false;
        Assert.False(recipients.Wants(peer));
        Assert.Equal(0, recipients.Count);

        // Connected again on the same object: still not asked, so still nothing.
        peer.Connected = true;
        Assert.False(recipients.Wants(peer));
    }

    [Fact]
    public void Only_connected_players_who_asked_are_listed_up_to_the_limit()
    {
        var recipients = Recipients();
        Assert.False(recipients.Add(new FakePeer { Connected = false }));
        var gone = new FakePeer();
        recipients.Add(gone);
        Assert.False(recipients.Add(gone));
        Assert.False(recipients.Wants(new FakePeer()));
        for (int i = 0; i < 3; i++) Assert.True(recipients.Add(new FakePeer()));
        Assert.False(recipients.Add(new FakePeer()));

        // A new request drops players no longer connected first, making room.
        gone.Connected = false;
        Assert.True(recipients.Add(new FakePeer()));
        Assert.Equal(4, recipients.Count);
    }

    [Fact]
    public void The_server_drops_players_on_coops_disconnect_message()
    {
        var server = Source("compat", "CoodszSettingsSync", "src", "Server", "PvpCountdownServer.cs");
        // Found by name and subscribed by reflection, so the DLL has no compiled reference to Coop's type.
        Assert.Contains("public const string TypeName = \"Common.Network.Messages.PlayerDisconnected\";", server);
        Assert.Contains("CoopProbe.FindLoaded(\"Common\", TypeName)", server);
        Assert.DoesNotContain("Subscribe<global::Common.Network.Messages.PlayerDisconnected>", server);
        Assert.Contains("_disconnects = global::CoodszSettingsSync.PvpCountdownDisconnects.Subscribe(broker);", server);
        Assert.Contains("_disconnects?.Unsubscribe(_broker);", server);
        Assert.Contains("PvpCountdownServer.Disconnected(peer)", server);
        Assert.Contains("Players.Wants(netPeer)", server);
    }

    /// <summary>
    /// The drop on PlayerDisconnected was never seen live, so the server says once that the subscription is on, and a
    /// failure in the handler or the unsubscribe is logged instead of swallowed. The built server DLLs carry the line.
    /// </summary>
    [Fact]
    public void The_server_logs_the_disconnect_subscription_and_its_failures()
    {
        const string subscribed = "PvP countdown: subscribed to Coop's Common.Network.Messages.PlayerDisconnected, so a player who leaves is dropped from the countdown right away.";
        var server = Source("compat", "CoodszSettingsSync", "src", "Server", "PvpCountdownServer.cs");
        var disconnects = server.Substring(server.IndexOf("internal sealed class PvpCountdownDisconnects", StringComparison.Ordinal));
        disconnects = disconnects.Substring(0, disconnects.IndexOf("namespace Coop.Core.Server", StringComparison.Ordinal));
        Assert.DoesNotContain("catch (Exception) { }", disconnects);
        Assert.Contains("catch (Exception e) { NoteFailure(\"unsubscribing from\", e); }", disconnects);
        Assert.Contains("catch (Exception e) { NoteFailure(\"handling\", e); }", disconnects);
        Assert.Contains("if (!_subscribedNoted)", disconnects);

        // TypeName is a const, so the compiler folds the line into one string in each built server DLL.
        var text = System.Text.Encoding.Unicode.GetBytes(subscribed);
        foreach (var dll in new[]
        {
            Path.Combine(RepoRoot(), "src", "CoodszHost", "ServerTools", "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "CoodszSettingsSync.Coop.dll"),
            Path.Combine(RepoRoot(), "src", "TaomCoopHost", "WarSync", "CoodszSettingsSync.Coop.dll"),
        })
            Assert.True(File.ReadAllBytes(dll).AsSpan().IndexOf(text) >= 0, dll + " lacks the subscribed line; run compat\\CoodszServerTools\\build.ps1.");
    }

    [Fact]
    public void A_coop_without_the_disconnect_message_keeps_the_settings_sync_on()
    {
        // Every type the server probe asks for, without PlayerDisconnected.
        var found = new Dictionary<string, Type>
        {
            ["Common.Messaging.IHandler"] = typeof(IDisposable),
            ["Common.Messaging.IMessageBroker"] = typeof(SettingsSyncFakes.FakeBroker),
            ["Common.Messaging.MessagePayload`1"] = typeof(SettingsSyncFakes.FakePayload<>),
            ["Common.Network.INetwork"] = typeof(SettingsSyncFakes.FakeNetwork),
            ["Common.Messaging.ICommand"] = typeof(IComparable),
            ["Common.ModInformation"] = typeof(SettingsSyncFakes.FakeModInformation),
            ["LiteNetLib.NetPeer"] = typeof(object),
            ["ProtoBuf.ProtoContractAttribute"] = typeof(Attribute),
            ["Coop.Core.Server.ServerModule"] = typeof(object),
            ["Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated"] = typeof(object),
        };
        var result = CoopProbe.Check(false, (_, name) => found.GetValueOrDefault(name));
        Assert.True(result.Ready, result.Describe());
    }

    [Fact]
    public void The_dll_has_no_compiled_reference_to_the_disconnect_message()
    {
        var path = Path.Combine(RepoRoot(), "src", "CoodszHost", "ServerTools", "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "CoodszSettingsSync.Coop.dll");
        using var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path));
        var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            Assert.False(reader.GetString(type.Namespace) == "Common.Network.Messages" && reader.GetString(type.Name) == "PlayerDisconnected",
                "CoodszSettingsSync.Coop.dll references PlayerDisconnected directly; a Coop rename would stop it loading.");
        }
    }

    // ---- the start and stop points -------------------------------------------------------------

    private static PvpCountdownSession Session(string phase, DateTime? changed = null, string id = "s1", string? proposal = "HostileDemand") =>
        new(id, changed ?? Now, phase, proposal);

    [Fact]
    public void A_demand_to_answer_starts_the_countdown_for_both_players_with_the_seconds_left()
    {
        var countdowns = new PvpCountdowns();
        var steps = countdowns.Plan(new[] { Session("HostileDemandPending", Now.AddSeconds(-4.5)) }, TimerOn, Now, Peers);
        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.False(s.Stop));
        Assert.All(steps, s => Assert.Equal(26, s.SecondsLeft));
        Assert.Same(CaughtPeer, steps.Single(s => s.Role == PvpCountdownRole.Caught).Peer);
        Assert.Same(AttackerPeer, steps.Single(s => s.Role == PvpCountdownRole.Attacker).Peer);
    }

    [Theory]
    [InlineData("HostileDemandPending")]
    [InlineData("ProposalPending")]
    [InlineData("WaitingForResponse")]
    public void Every_caught_players_turn_is_counted(string phase)
    {
        Assert.True(PvpCountdowns.Shows(TimerOn, phase, "HostileDemand"));
    }

    [Theory]
    [InlineData("InitialOptions", "None")]
    [InlineData("HostileDemandConfirm", "HostileDemand")]
    [InlineData("TradeActive", "Trade")]
    [InlineData("ProposalPending", "Trade")]
    [InlineData(null, null)]
    public void The_attackers_turn_and_trades_show_no_countdown(string? phase, string? proposal)
    {
        Assert.False(PvpCountdowns.Shows(TimerOn, phase, proposal));
        Assert.Empty(new PvpCountdowns().Plan(new[] { Session(phase!, proposal: proposal) }, TimerOn, Now, Peers));
    }

    [Fact]
    public void A_countdown_that_went_out_is_not_sent_again_each_tick()
    {
        var countdowns = new PvpCountdowns();
        var open = new[] { Session("HostileDemandPending") };
        foreach (var step in countdowns.Plan(open, TimerOn, Now, Peers)) countdowns.Sent(step);
        Assert.Empty(countdowns.Plan(open, TimerOn, Now.AddSeconds(1), Peers));
        Assert.Empty(countdowns.Plan(open, TimerOn, Now.AddSeconds(2), Peers));
    }

    [Fact]
    public void A_new_state_or_new_seconds_send_it_again()
    {
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending") }, TimerOn, Now, Peers)) countdowns.Sent(step);

        // Coop sent a new state (or a rule was turned on): the clock started again.
        var again = countdowns.Plan(new[] { Session("ProposalPending", Now.AddSeconds(5)) }, TimerOn, Now.AddSeconds(5), Peers);
        Assert.Equal(2, again.Count);
        Assert.All(again, s => Assert.Equal(30, s.SecondsLeft));
        foreach (var step in again) countdowns.Sent(step);

        // The host changed the seconds on the Settings page.
        var longer = countdowns.Plan(new[] { Session("ProposalPending", Now.AddSeconds(5)) }, new PvpRulesSettings(false, true, 60), Now.AddSeconds(6), Peers);
        Assert.Equal(2, longer.Count);
        Assert.All(longer, s => Assert.Equal(59, s.SecondsLeft));
    }

    [Fact]
    public void An_answer_peace_timeout_leave_or_end_sends_a_stop_to_the_same_players()
    {
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending") }, TimerOn, Now, Peers)) countdowns.Sent(step);

        // The session is gone from the response timer's clocks (EndSession, a timeout or Leave): no peer can be found now.
        var stops = countdowns.Plan(Array.Empty<PvpCountdownSession>(), TimerOn, Now.AddSeconds(3), (_, _) => null);
        Assert.Equal(2, stops.Count);
        Assert.All(stops, s => Assert.True(s.Stop));
        Assert.Same(CaughtPeer, stops.Single(s => s.Role == PvpCountdownRole.Caught).Peer);
        Assert.Same(AttackerPeer, stops.Single(s => s.Role == PvpCountdownRole.Attacker).Peer);
        Assert.Equal(0, countdowns.Shown);
        Assert.Empty(countdowns.Plan(Array.Empty<PvpCountdownSession>(), TimerOn, Now.AddSeconds(4), Peers));
    }

    [Fact]
    public void The_turn_passing_to_the_attacker_or_the_clock_running_out_sends_a_stop()
    {
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending") }, TimerOn, Now, Peers)) countdowns.Sent(step);
        Assert.All(countdowns.Plan(new[] { Session("InitialOptions", Now.AddSeconds(2)) }, TimerOn, Now.AddSeconds(2), Peers), s => Assert.True(s.Stop));

        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending", Now.AddSeconds(10)) }, TimerOn, Now.AddSeconds(10), Peers)) countdowns.Sent(step);
        var ranOut = countdowns.Plan(new[] { Session("HostileDemandPending", Now.AddSeconds(10)) }, TimerOn, Now.AddSeconds(40), Peers);
        Assert.Equal(2, ranOut.Count);
        Assert.All(ranOut, s => Assert.True(s.Stop));
    }

    // Live test 2026-10-29 21:17: with block leaving off, make_peace left "Answer in 26 s" running until the timer fired.
    [Fact]
    public void Peace_stops_the_countdown_at_once_with_block_leaving_off()
    {
        var settings = new PvpRulesSettings(false, true, 30);
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending") }, settings, Now, Peers)) countdowns.Sent(step);

        // Peace: Coop sends no new state, so the clock and phase stay as they were.
        var peace = new[] { Session("HostileDemandPending") with { PeaceMade = true } };
        var stops = countdowns.Plan(peace, settings, Now.AddSeconds(4), Peers);
        Assert.Equal(2, stops.Count);
        Assert.All(stops, s => Assert.True(s.Stop));
        Assert.Empty(countdowns.Plan(peace, settings, Now.AddSeconds(5), Peers));
    }

    [Fact]
    public void Peace_stops_the_countdown_and_leave_back_doesnt_start_it_again_with_block_leaving_on()
    {
        var settings = new PvpRulesSettings(true, true, 30);
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending") }, settings, Now, Peers)) countdowns.Sent(step);

        // Peace, and in the same tick LeaveBack sent the caught player's options again: the clock started again.
        var resent = new[] { Session("HostileDemandPending", Now.AddSeconds(4)) with { PeaceMade = true } };
        var steps = countdowns.Plan(resent, settings, Now.AddSeconds(4), Peers);
        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.True(s.Stop));
        Assert.Empty(countdowns.Plan(resent, settings, Now.AddSeconds(5), Peers));
        Assert.Empty(countdowns.Plan(resent, settings, Now.AddSeconds(20), Peers));
    }

    [Fact]
    public void War_declared_again_during_the_meeting_starts_the_countdown_again_from_the_same_clock()
    {
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(new[] { Session("HostileDemandPending") }, TimerOn, Now, Peers)) countdowns.Sent(step);
        countdowns.Plan(new[] { Session("HostileDemandPending") with { PeaceMade = true } }, TimerOn, Now.AddSeconds(4), Peers);

        // PeaceMade is asked again every tick, not kept: back at war, the clock Coop last started counts on.
        var again = countdowns.Plan(new[] { Session("HostileDemandPending") }, TimerOn, Now.AddSeconds(10), Peers);
        Assert.Equal(2, again.Count);
        Assert.All(again, s => Assert.False(s.Stop));
        Assert.All(again, s => Assert.Equal(20, s.SecondsLeft));
    }

    [Fact]
    public void A_meeting_between_players_not_at_war_still_counts_down()
    {
        // PeaceMade is only for a meeting that started at war; a friendly proposal is timed as before.
        var steps = new PvpCountdowns().Plan(new[] { Session("ProposalPending", proposal: "Peace") }, TimerOn, Now, Peers);
        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.False(s.Stop));
    }

    [Fact]
    public void The_server_tools_ask_coop_about_the_war_the_way_leave_back_does()
    {
        var rules = Source("compat", "CoodszServerTools", "src", "PvpRules.cs");
        Assert.Contains("PvpCountdowns.Shows(settings, phase, proposal) && Hostile(session) && !AtWarNow(session)", rules);
        Assert.Contains("Counted(sessions[s.SessionId], s.SessionId, s.Changed, s.Phase, settings)", rules);
    }

    [Fact]
    public void Nothing_is_sent_while_the_response_timer_is_off()
    {
        var open = new[] { Session("HostileDemandPending") };
        Assert.Empty(new PvpCountdowns().Plan(open, PvpRulesSettings.Off, Now, Peers));
        // Block leaving alone runs no countdown either.
        Assert.Empty(new PvpCountdowns().Plan(open, new PvpRulesSettings(true, false, 30), Now, Peers));

        // Turned off while one is up: only the stops go out.
        var countdowns = new PvpCountdowns();
        foreach (var step in countdowns.Plan(open, TimerOn, Now, Peers)) countdowns.Sent(step);
        var off = countdowns.Plan(open, PvpRulesSettings.Off, Now.AddSeconds(1), Peers);
        Assert.Equal(2, off.Count);
        Assert.All(off, s => Assert.True(s.Stop));
        Assert.Empty(countdowns.Plan(open, PvpRulesSettings.Off, Now.AddSeconds(2), Peers));
    }

    [Fact]
    public void A_player_coop_doesnt_know_yet_or_a_failed_send_is_tried_on_the_next_tick()
    {
        var countdowns = new PvpCountdowns();
        var open = new[] { Session("HostileDemandPending") };
        // Coop learns the caught player's peer only once their client shows the dialog.
        var first = countdowns.Plan(open, TimerOn, Now, (_, role) => role == PvpCountdownRole.Attacker ? AttackerPeer : null);
        Assert.Equal(PvpCountdownRole.Attacker, Assert.Single(first).Role);
        countdowns.Sent(first[0]);

        // The caught player's send failed (a player without the add on answers false): not reported, so tried again.
        var second = countdowns.Plan(open, TimerOn, Now.AddSeconds(1), Peers);
        Assert.Equal(PvpCountdownRole.Caught, Assert.Single(second).Role);
        Assert.Equal(29, second[0].SecondsLeft);
        Assert.Single(countdowns.Plan(open, TimerOn, Now.AddSeconds(2), Peers));
    }

    [Theory]
    [InlineData(0.0, 30)]
    [InlineData(0.4, 30)]
    [InlineData(1.0, 29)]
    [InlineData(29.5, 1)]
    [InlineData(30.0, 0)]
    [InlineData(45.0, 0)]
    public void Seconds_left_round_up(double elapsed, int expected)
    {
        Assert.Equal(expected, PvpCountdowns.SecondsLeft(Now, Now.AddSeconds(elapsed), 30));
    }

    [Fact]
    public void The_response_timers_clocks_are_listed_for_the_countdown()
    {
        var timers = new PvpTimers();
        timers.Started("a", Now);
        timers.StateChanged("a", "HostileDemandPending", Now.AddSeconds(3));
        var open = Assert.Single(timers.Open());
        Assert.Equal(("a", Now.AddSeconds(3), (string?)"HostileDemandPending"), open);
        timers.Ended("a");
        Assert.Empty(timers.Open());
    }

    // ---- the language and the text -------------------------------------------------------------

    [Theory]
    [InlineData("English", "en")]
    [InlineData("Deutsch", "de")]
    [InlineData("Français", "fr")]
    [InlineData("Português (BR)", "pt")]
    [InlineData("Русский", "ru")]
    [InlineData("Türkçe", "tr")]
    [InlineData("العربية", "ar")]
    [InlineData("Arabic", "ar")]
    [InlineData("Español (LA)", "en")]
    [InlineData("简体中文", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void The_games_language_picks_the_text(string? gameLanguage, string expected)
    {
        Assert.Equal(expected, PvpCountdownText.LanguageFor(gameLanguage));
    }

    [Fact]
    public void Every_tools_language_has_both_lines_with_the_seconds()
    {
        Assert.Equal(new[] { "ar", "de", "en", "fr", "pt", "ru", "tr" }, PvpCountdownText.Languages.OrderBy(l => l, StringComparer.Ordinal));
        foreach (var language in PvpCountdownText.Languages)
            foreach (var role in new[] { PvpCountdownWire.CaughtRole, PvpCountdownWire.AttackerRole })
            {
                var text = PvpCountdownText.For(language, role, 17);
                Assert.Contains("17", text);
                Assert.DoesNotContain(":", text);
                Assert.DoesNotContain("{", text);
            }
        Assert.NotEqual(PvpCountdownText.For("de", 1, 5), PvpCountdownText.For("de", 2, 5));
    }

    [Fact]
    public void The_english_lines_are_plain()
    {
        Assert.Equal("Answer in 12 s", PvpCountdownText.For("en", PvpCountdownWire.CaughtRole, 12));
        Assert.Equal("They have 12 s to answer", PvpCountdownText.For("en", PvpCountdownWire.AttackerRole, 12));
        Assert.Equal("Answer in 3 s", PvpCountdownText.For("xx", PvpCountdownWire.CaughtRole, 3));
        Assert.DoesNotContain("-", PvpCountdownText.For("en", 1, 1) + PvpCountdownText.For("en", 2, 1));
    }

    // ---- the client's clock --------------------------------------------------------------------

    [Fact]
    public void The_client_counts_down_by_itself_and_goes_at_zero()
    {
        var clock = new PvpCountdownClock();
        Assert.True(clock.Start(1, "s1", 3, PvpCountdownWire.CaughtRole));
        Assert.Equal("Answer in 3 s", clock.Text("en"));
        clock.Tick(0.5f, "s1", true);
        Assert.Equal(3, clock.Seconds);
        clock.Tick(0.6f, "s1", true);
        Assert.Equal(2, clock.Seconds);
        clock.Tick(2f, "s1", true);
        Assert.False(clock.Showing);
        Assert.Null(clock.Text("en"));
    }

    [Fact]
    public void A_stop_only_takes_down_its_own_session()
    {
        var clock = new PvpCountdownClock();
        clock.Start(1, "s1", 30, PvpCountdownWire.AttackerRole);
        Assert.False(clock.Stop(1, "s2"));
        Assert.True(clock.Showing);
        Assert.True(clock.Stop(1, "s1"));
        Assert.False(clock.Showing);
    }

    [Fact]
    public void The_countdown_goes_once_coops_meeting_menu_closed_but_waits_for_it_to_open()
    {
        var clock = new PvpCountdownClock();
        clock.Start(1, "s1", 30, PvpCountdownWire.CaughtRole);
        // The start can come just before Coop's menu shows this session.
        clock.Tick(1f, null, true);
        Assert.True(clock.Showing);
        clock.Tick(0.5f, "s1", true);
        clock.Tick(1.5f, null, true);
        Assert.True(clock.Showing);
        clock.Tick(0.6f, "other", true);
        Assert.False(clock.Showing);

        // Coop's menu state could not be read: only the clock and the stop take it down.
        clock.Start(1, "s2", 30, PvpCountdownWire.CaughtRole);
        clock.Tick(10f, null, false);
        Assert.True(clock.Showing);
    }

    [Fact]
    public void A_late_end_from_the_previous_handler_doesnt_clear_the_new_sessions_countdown()
    {
        var clock = new PvpCountdownClock();
        object first = new(), second = new();
        clock.SessionStarted(first);
        clock.SessionStarted(second);
        clock.Start(1, "s1", 30, PvpCountdownWire.CaughtRole);

        // The first handler's Dispose runs after the second one started.
        Assert.False(clock.SessionEnded(first));
        Assert.True(clock.Showing);

        Assert.True(clock.SessionEnded(second));
        Assert.False(clock.Showing);
        Assert.False(clock.SessionEnded(second));
    }

    [Fact]
    public void The_client_handler_passes_itself_as_the_sessions_owner()
    {
        var client = Source("compat", "CoodszSettingsSync", "src", "Client", "PvpCountdownClient.cs");
        Assert.Contains("PvpCountdownClient.SessionStarted(this);", client);
        Assert.Contains("PvpCountdownClient.SessionEnded(this);", client);
        // Started before the subscriptions, so a countdown that comes at once is not cleared by the start.
        Assert.True(client.IndexOf("PvpCountdownClient.SessionStarted(this);", StringComparison.Ordinal)
                    < client.IndexOf("broker.Subscribe<NetworkPvpCountdown>(HandleStart);", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_start_is_ignored_and_a_new_one_replaces_the_old()
    {
        var clock = new PvpCountdownClock();
        Assert.False(clock.Start(9, "s1", 30, 1));
        Assert.False(clock.Start(1, "s1", 0, 1));
        Assert.False(clock.Showing);
        clock.Start(1, "s1", 30, 1);
        clock.Start(1, "s2", 10, 2);
        Assert.Equal("s2", clock.SessionId);
        Assert.Equal("They have 10 s to answer", clock.Text("en"));
    }

    // ---- the shipped module --------------------------------------------------------------------

    [Fact]
    public void The_client_module_carries_the_countdown_screen()
    {
        var prefab = Source("compat", "CoodszJoinPassword", "module", "GUI", "Prefabs", "CoodszPvpCountdown.xml");
        Assert.Contains("Text=\"@Text\"", prefab);
        Assert.Contains("#E2C27AFF", prefab);
        Assert.Contains("VerticalAlignment=\"Bottom\"", prefab);
        var client = Source("compat", "CoodszSettingsSync", "src", "Client", "PvpCountdownClient.cs");
        Assert.Contains("public const string MovieName = \"CoodszPvpCountdown\";", client);
        // Every client launcher ships the whole module folder, GUI included.
        foreach (var project in new[] { "CoodszCoopAndFriends", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher" })
            Assert.Contains(@"compat\CoodszJoinPassword\module\**\*", Source("src", project, project + ".csproj"));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
