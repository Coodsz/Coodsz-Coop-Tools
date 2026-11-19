using CoodszSettingsSync;

namespace BannerlordCoopLauncher.Tests;

// Coodsz War of the Ring sync (compat/CoodszSettingsSync/src/Shared/WarOfTheRingSync.cs): the server sends
// TAOM's War of the Ring phase, outcome and momentum to players, and the client puts it into TAOM's own
// objects by reflection. These tests use stand in types shaped like TAOM's, never TAOM itself.
public sealed class WarOfTheRingSyncTests
{
    // ---- Stand ins shaped like TAOM ----

    public enum FakePhase { Peace = 0, IsengardWar = 1, FullWar = 2, WarEnded = 3 }

    public enum FakeOutcome { None = 0, FreeVictory = 1, EvilVictory = 2 }

    public interface IFakeService
    {
        FakePhase CurrentPhase { get; }
        FakeOutcome Outcome { get; }
        void SetPhaseFromSave(FakePhase phase);
        void SetOutcomeFromSave(FakeOutcome outcome);
    }

    public sealed class FakeMomentumState
    {
        public Dictionary<string, string> Values { get; } = new();
    }

    public interface IFakeStore
    {
        FakeMomentumState State { get; }
        Dictionary<string, string> Serialize();
        void Deserialize(Dictionary<string, string> data);
        void NotifyMomentumChanged();
    }

    public sealed class FakeService : IFakeService
    {
        public FakePhase CurrentPhase { get; private set; }
        public FakeOutcome Outcome { get; private set; }
        public void SetPhaseFromSave(FakePhase phase) => CurrentPhase = phase;
        public void SetOutcomeFromSave(FakeOutcome outcome) => Outcome = outcome;
    }

    public sealed class FakeStore : IFakeStore
    {
        public FakeMomentumState State { get; private set; } = new();
        public int Notified { get; private set; }
        public Dictionary<string, string> Serialize() => new(State.Values);

        // Like TAOM: a load replaces the state object.
        public void Deserialize(Dictionary<string, string> data)
        {
            var fresh = new FakeMomentumState();
            foreach (var pair in data)
                fresh.Values[pair.Key] = pair.Value;
            State = fresh;
        }

        public void NotifyMomentumChanged() => Notified++;
    }

    public sealed class FakeBehavior<TStore, TService>
        where TStore : class
        where TService : class
    {
        private readonly TStore? _stateStore;
        private readonly TService? _wotrService;

        public FakeBehavior(TStore? store, TService? service)
        {
            _stateStore = store;
            _wotrService = service;
        }

        public int Refreshed { get; private set; }
        public TStore? Store => _stateStore;
        public TService? Service => _wotrService;

        private void RefreshMapMeter() => Refreshed++;
    }

    public sealed class FakeBehaviorNoRefresh
    {
        private readonly IFakeStore _stateStore;
        private readonly IFakeService _wotrService;

        public FakeBehaviorNoRefresh(IFakeStore store, IFakeService service)
        {
            _stateStore = store;
            _wotrService = service;
        }

        public IFakeStore Store => _stateStore;
        public IFakeService Service => _wotrService;
    }

    public sealed class FakeBehaviorNoService
    {
        private readonly IFakeStore? _stateStore;
        public FakeBehaviorNoService(IFakeStore? store) => _stateStore = store;
        public IFakeStore? Store => _stateStore;
        private void RefreshMapMeter() { }
    }

    // Variants with one member missing.
    public interface IServiceNoOutcomeSetter
    {
        FakePhase CurrentPhase { get; }
        FakeOutcome Outcome { get; }
        void SetPhaseFromSave(FakePhase phase);
    }

    public interface IServiceNoCurrentPhase
    {
        FakeOutcome Outcome { get; }
        void SetPhaseFromSave(FakePhase phase);
        void SetOutcomeFromSave(FakeOutcome outcome);
    }

    public interface IStoreNoNotify
    {
        FakeMomentumState State { get; }
        Dictionary<string, string> Serialize();
        void Deserialize(Dictionary<string, string> data);
    }

    public interface IStoreNoDeserialize
    {
        FakeMomentumState State { get; }
        Dictionary<string, string> Serialize();
        void NotifyMomentumChanged();
    }

    private static readonly object Campaign = new();

    private static Func<string, Type?> Types(Type? behavior, Type? store, Type? service) => name =>
        name == TaomWotr.MomentumBehaviorType ? behavior
        : name == TaomWotr.StateStoreType ? store
        : name == TaomWotr.ServiceType ? service
        : null;

    private static (TaomWotr game, FakeBehavior<IFakeStore, IFakeService> behavior, FakeStore store, FakeService service) BindReady()
    {
        var store = new FakeStore();
        var service = new FakeService();
        var behavior = new FakeBehavior<IFakeStore, IFakeService>(store, service);
        var status = TaomWotr.TryBind(
            Types(behavior.GetType(), typeof(IFakeStore), typeof(IFakeService)),
            true, Campaign, (c, t) => c == Campaign && t == behavior.GetType() ? behavior : null,
            out var game, out var missing);
        Assert.True(status == WotrBindStatus.Ready, "bind failed: " + missing);
        Assert.NotNull(game);
        return (game!, behavior, store, service);
    }

    private static WotrSnapshot Snap(int phase, int outcome, params (string Key, string Value)[] momentum) =>
        new(phase, outcome, momentum.ToDictionary(p => p.Key, p => p.Value));

    // ---- review fixes: a throwing meter refresh, and players who missed their first snapshot ----------

    [Fact]
    public void Mirror_keeps_the_state_and_retries_the_meter_when_the_refresh_throws()
    {
        var game = new RecordingGame { ThrowOnRefresh = true };
        var mirror = new WotrMirror();
        var snapshot = Snap(2, 0, ("warStarted", "1"));

        var first = mirror.Apply(game, snapshot);
        Assert.True(first.PhaseChanged);
        Assert.True(first.MomentumChanged);
        Assert.False(first.MeterRefreshed);
        Assert.NotNull(first.MeterError);
        Assert.Equal(2, game.Phase);

        // Nothing changed, but the meter is still owed: the next Apply tries it again and nothing else.
        game.ThrowOnRefresh = false;
        var second = mirror.Apply(game, snapshot);
        Assert.False(second.Any);
        Assert.True(second.MeterRefreshed);
        Assert.Null(second.MeterError);
        Assert.Equal(1, game.LoadCalls);
        Assert.Equal(2, game.RefreshCalls);

        // Once it worked it is not called again.
        mirror.Apply(game, snapshot);
        Assert.Equal(2, game.RefreshCalls);
    }

    [Fact]
    public void A_player_whose_first_send_failed_still_gets_the_unchanged_state_on_the_next_poll()
    {
        var players = new WotrRecipients<string>(64);
        players.Join("a");
        Assert.Equal(new[] { "a" }, players.Targets(changed: true));
        players.Sent("a", worked: false);

        // The state did not change, yet "a" never got it, so "a" is still a target.
        Assert.Equal(new[] { "a" }, players.Targets(changed: false));
        players.Sent("a", worked: true);
        Assert.Empty(players.Targets(changed: false));
        Assert.Equal(new[] { "a" }, players.Targets(changed: true));
    }

    [Fact]
    public void A_player_whose_change_send_failed_goes_back_to_waiting()
    {
        var players = new WotrRecipients<string>(64);
        players.Join("a");
        players.Join("b");
        players.Sent("a", true);
        players.Sent("b", true);
        players.Sent("b", false);
        Assert.Equal(new[] { "b" }, players.Targets(changed: false));
        Assert.Equal(1, players.WaitingCount);
    }

    [Fact]
    public void Recipients_ignore_duplicates_respect_the_cap_and_prune_gone_players()
    {
        var players = new WotrRecipients<string>(2);
        players.Join("a");
        players.Join("a");
        players.Join("b");
        players.Join("c");
        Assert.Equal(2, players.Count);
        players.Sent("a", true);
        players.Prune(p => p != "a");
        Assert.Equal(new[] { "b" }, players.Targets(changed: true));
        players.Clear();
        Assert.Equal(0, players.Count);
    }

    // A recording IWotrGame for the mirror.
    private sealed class RecordingGame : IWotrGame
    {
        public int Phase { get; set; }
        public int Outcome { get; set; }
        public Dictionary<string, string> Momentum = new();
        public object? StateToken { get; set; } = new();
        public HashSet<int> KnownPhases = [0, 1, 2, 3];
        public HashSet<int> KnownOutcomes = [0, 1, 2];
        public bool CanRefresh = true;
        public bool ThrowOnRefresh;
        public int SetPhaseCalls, SetOutcomeCalls, LoadCalls, NotifyCalls, RefreshCalls;

        public Dictionary<string, string> ReadMomentum() => new(Momentum);

        public bool SetPhase(int phase)
        {
            SetPhaseCalls++;
            if (!KnownPhases.Contains(phase)) return false;
            Phase = phase;
            return true;
        }

        public bool SetOutcome(int outcome)
        {
            SetOutcomeCalls++;
            if (!KnownOutcomes.Contains(outcome)) return false;
            Outcome = outcome;
            return true;
        }

        public void LoadMomentum(Dictionary<string, string> momentum)
        {
            LoadCalls++;
            Momentum = new Dictionary<string, string>(momentum);
            StateToken = new object();
        }

        public void NotifyMomentumChanged() => NotifyCalls++;

        public bool RefreshMeter()
        {
            RefreshCalls++;
            if (ThrowOnRefresh) throw new InvalidOperationException("no map screen");
            return CanRefresh;
        }

        public string PhaseName(int phase) => "P" + phase;

        public int TotalCalls => SetPhaseCalls + SetOutcomeCalls + LoadCalls + NotifyCalls + RefreshCalls;

        public void ClearCounts() => SetPhaseCalls = SetOutcomeCalls = LoadCalls = NotifyCalls = RefreshCalls = 0;
    }

    // ---- WotrSnapshot ----

    [Fact]
    public void Snapshot_copies_momentum_and_turns_null_values_into_empty()
    {
        var source = new Dictionary<string, string> { ["gondor"] = "1.5", ["mordor"] = null! };
        var snap = new WotrSnapshot(2, 1, source);
        source["gondor"] = "99";
        source["rohan"] = "3";

        Assert.Equal(2, snap.Phase);
        Assert.Equal(1, snap.Outcome);
        Assert.Equal(2, snap.Momentum.Count);
        Assert.Equal("1.5", snap.Momentum["gondor"]);
        Assert.Equal("", snap.Momentum["mordor"]);
        Assert.Empty(new WotrSnapshot(0, 0, null).Momentum);
    }

    [Fact]
    public void Snapshot_equality()
    {
        var a = Snap(1, 0, ("a", "1"), ("b", "2"));
        var same = Snap(1, 0, ("b", "2"), ("a", "1"));
        Assert.True(a.SameAs(same));
        Assert.True(a.SameMomentum(same));
        Assert.True(a.SameAs(a));

        Assert.False(a.SameAs(null));
        Assert.False(a.SameMomentum(null));
        Assert.False(a.SameAs(Snap(2, 0, ("a", "1"), ("b", "2"))));
        Assert.False(a.SameAs(Snap(1, 1, ("a", "1"), ("b", "2"))));

        var otherValue = Snap(1, 0, ("a", "1"), ("b", "3"));
        Assert.False(a.SameMomentum(otherValue));
        Assert.False(a.SameAs(otherValue));
        Assert.False(a.SameMomentum(Snap(1, 0, ("a", "1"))));
        Assert.False(a.SameMomentum(Snap(1, 0, ("a", "1"), ("c", "2"))));
        // Keys are case sensitive.
        Assert.False(a.SameMomentum(Snap(1, 0, ("A", "1"), ("b", "2"))));

        Assert.True(new WotrSnapshot(0, 0, null).SameAs(new WotrSnapshot(0, 0, new Dictionary<string, string>())));
    }

    [Fact]
    public void ToLists_is_ordinal_key_order_and_round_trips()
    {
        var snap = Snap(3, 2, ("b", "2"), ("a", "1"), ("A", "0"), ("_x", ""));
        snap.ToLists(out var keys, out var values);
        Assert.Equal(new[] { "A", "_x", "a", "b" }, keys);
        Assert.Equal(new[] { "0", "", "1", "2" }, values);

        var back = WotrWire.FromParts(WotrWire.ProtocolVersion, snap.Phase, snap.Outcome, keys, values, out var reason);
        Assert.Null(reason);
        Assert.NotNull(back);
        Assert.True(snap.SameAs(back));
    }

    [Fact]
    public void ToLists_of_empty_momentum_gives_empty_lists()
    {
        new WotrSnapshot(0, 0, null).ToLists(out var keys, out var values);
        Assert.Empty(keys);
        Assert.Empty(values);
    }

    // ---- WotrWire ----

    [Fact]
    public void Wire_constants()
    {
        Assert.Equal(1, WotrWire.ProtocolVersion);
        Assert.Equal(63, WotrWire.MaxEnumValue);
        Assert.Equal(4096, WotrWire.MaxEntries);
        Assert.Equal(128, WotrWire.MaxKeyLength);
        Assert.Equal(4096, WotrWire.MaxValueLength);
        Assert.Equal(1_000_000, WotrWire.MaxTotalLength);
    }

    [Fact]
    public void Check_accepts_a_normal_message_and_null_lists()
    {
        const int v = WotrWire.ProtocolVersion;
        Assert.Null(WotrWire.Check(v, 2, 1, ["a", "b"], ["1", "2"]));
        Assert.Null(WotrWire.Check(v, 0, 0, null, null));
        Assert.Null(WotrWire.Check(v, 0, 0, null, []));
        Assert.Null(WotrWire.Check(v, 0, 0, [], null));
        Assert.Null(WotrWire.Check(v, 0, 0, ["a"], [""]));

        var snap = WotrWire.FromParts(v, 1, 0, null, null, out var reason);
        Assert.Null(reason);
        Assert.NotNull(snap);
        Assert.Equal(1, snap!.Phase);
        Assert.Empty(snap.Momentum);
    }

    [Fact]
    public void Check_accepts_the_exact_limits()
    {
        const int v = WotrWire.ProtocolVersion;
        Assert.Null(WotrWire.Check(v, WotrWire.MaxEnumValue, WotrWire.MaxEnumValue, null, null));
        Assert.Null(WotrWire.Check(v, 0, 0, [new string('k', WotrWire.MaxKeyLength)], [new string('v', WotrWire.MaxValueLength)]));
        var keys = Enumerable.Range(0, WotrWire.MaxEntries).Select(i => "k" + i).ToList();
        var values = Enumerable.Repeat("1", WotrWire.MaxEntries).ToList();
        Assert.Null(WotrWire.Check(v, 0, 0, keys, values));
    }

    public static TheoryData<string, int, int, int, string[]?, string[]?> Refusals()
    {
        var data = new TheoryData<string, int, int, int, string[]?, string[]?>
        {
            { "wrong protocol", 2, 0, 0, null, null },
            { "protocol zero", 0, 0, 0, null, null },
            { "negative phase", 1, -1, 0, null, null },
            { "phase too big", 1, 64, 0, null, null },
            { "negative outcome", 1, 0, -1, null, null },
            { "outcome too big", 1, 0, 64, null, null },
            { "more keys than values", 1, 0, 0, ["a", "b"], ["1"] },
            { "more values than keys", 1, 0, 0, ["a"], ["1", "2"] },
            { "null keys with values", 1, 0, 0, null, ["1"] },
            { "keys with null values", 1, 0, 0, ["a"], null },
            { "empty key", 1, 0, 0, [""], ["1"] },
            { "too long key", 1, 0, 0, [new string('k', 129)], ["1"] },
            { "duplicate key", 1, 0, 0, ["a", "b", "a"], ["1", "2", "3"] },
            { "too long value", 1, 0, 0, ["a"], [new string('v', 4097)] },
        };
        var many = Enumerable.Range(0, 4097).Select(i => "k" + i).ToArray();
        data.Add("too many entries", 1, 0, 0, many, Enumerable.Repeat("1", 4097).ToArray());
        // 300 entries of 4096 chars: every entry is fine, the total is over 1,000,000.
        var bigKeys = Enumerable.Range(0, 300).Select(i => "k" + i).ToArray();
        data.Add("total too large", 1, 0, 0, bigKeys, Enumerable.Repeat(new string('v', 4096), 300).ToArray());
        return data;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Check_refuses(string why, int protocol, int phase, int outcome, string[]? keys, string[]? values)
    {
        var reason = WotrWire.Check(protocol, phase, outcome, keys, values);
        Assert.False(string.IsNullOrWhiteSpace(reason), why);

        var snap = WotrWire.FromParts(protocol, phase, outcome, keys, values, out var fromPartsReason);
        Assert.Null(snap);
        Assert.False(string.IsNullOrWhiteSpace(fromPartsReason), why);
    }

    [Fact]
    public void CheckOutgoing_passes_a_normal_snapshot_and_refuses_one_the_client_would_drop()
    {
        Assert.Null(WotrWire.CheckOutgoing(Snap(2, 1, ("gondor", "12.5"))));
        Assert.Null(WotrWire.CheckOutgoing(new WotrSnapshot(0, 0, null)));
        Assert.NotNull(WotrWire.CheckOutgoing(Snap(64, 0)));
        Assert.NotNull(WotrWire.CheckOutgoing(Snap(0, -1)));
        Assert.NotNull(WotrWire.CheckOutgoing(Snap(0, 0, (new string('k', 129), "1"))));
        Assert.NotNull(WotrWire.CheckOutgoing(Snap(0, 0, ("", "1"))));
        Assert.NotNull(WotrWire.CheckOutgoing(Snap(0, 0, ("a", new string('v', 4097)))));
    }

    // ---- WotrMirror ----

    [Fact]
    public void Mirror_first_apply_sets_only_what_differs_and_loads_momentum()
    {
        var game = new RecordingGame { Phase = 2, Outcome = 0 };
        var mirror = new WotrMirror();

        var result = mirror.Apply(game, Snap(2, 1, ("a", "1")));

        Assert.Equal(0, game.SetPhaseCalls); // already equal
        Assert.Equal(1, game.SetOutcomeCalls);
        Assert.Equal(1, game.LoadCalls);
        Assert.Equal(1, game.NotifyCalls);
        Assert.Equal(1, game.RefreshCalls);
        Assert.False(result.PhaseChanged);
        Assert.True(result.OutcomeChanged);
        Assert.True(result.MomentumChanged);
        Assert.True(result.MeterRefreshed);
        Assert.True(result.Any);
        Assert.Null(result.UnknownPhase);
        Assert.Null(result.UnknownOutcome);
        Assert.Equal(1, game.Outcome);
        Assert.Equal("1", game.Momentum["a"]);
    }

    [Fact]
    public void Mirror_first_apply_loads_even_empty_momentum()
    {
        var game = new RecordingGame { Momentum = new() { ["stale"] = "5" } };
        var result = new WotrMirror().Apply(game, new WotrSnapshot(0, 0, null));
        Assert.Equal(1, game.LoadCalls);
        Assert.Empty(game.Momentum);
        Assert.True(result.MomentumChanged);
    }

    [Fact]
    public void Mirror_repeat_apply_calls_nothing()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();
        mirror.Apply(game, Snap(1, 0, ("a", "1")));
        game.ClearCounts();

        var result = mirror.Apply(game, Snap(1, 0, ("a", "1")));

        Assert.Equal(0, game.TotalCalls);
        Assert.False(result.Any);
        Assert.False(result.PhaseChanged);
        Assert.False(result.OutcomeChanged);
        Assert.False(result.MomentumChanged);
        Assert.False(result.MeterRefreshed);
    }

    [Fact]
    public void Mirror_changes_only_the_part_that_moved()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();
        mirror.Apply(game, Snap(0, 0, ("a", "1")));

        game.ClearCounts();
        var phaseOnly = mirror.Apply(game, Snap(1, 0, ("a", "1")));
        Assert.True(phaseOnly.PhaseChanged);
        Assert.False(phaseOnly.MomentumChanged);
        Assert.Equal(1, game.SetPhaseCalls);
        Assert.Equal(0, game.SetOutcomeCalls);
        Assert.Equal(0, game.LoadCalls);
        Assert.Equal(0, game.NotifyCalls);
        Assert.Equal(1, game.RefreshCalls);

        game.ClearCounts();
        var momentumOnly = mirror.Apply(game, Snap(1, 0, ("a", "2")));
        Assert.False(momentumOnly.PhaseChanged);
        Assert.True(momentumOnly.MomentumChanged);
        Assert.Equal(0, game.SetPhaseCalls);
        Assert.Equal(1, game.LoadCalls);
        Assert.Equal(1, game.NotifyCalls);
        Assert.Equal(1, game.RefreshCalls);
        Assert.Equal("2", game.Momentum["a"]);
    }

    [Fact]
    public void Mirror_resets_a_phase_the_game_changed_by_itself()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();
        mirror.Apply(game, Snap(2, 0));
        game.Phase = 0; // e.g. a local load put the old phase back
        game.ClearCounts();

        var result = mirror.Apply(game, Snap(2, 0));

        Assert.True(result.PhaseChanged);
        Assert.Equal(1, game.SetPhaseCalls);
        Assert.Equal(2, game.Phase);
        Assert.Equal(0, game.LoadCalls);
    }

    [Fact]
    public void Mirror_reloads_momentum_when_the_store_state_object_was_replaced()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();
        mirror.Apply(game, Snap(1, 0, ("a", "1")));

        // A save load replaced the store's state behind the mirror's back.
        game.StateToken = new object();
        game.Momentum = new() { ["a"] = "old" };
        game.ClearCounts();

        var result = mirror.Apply(game, Snap(1, 0, ("a", "1")));

        Assert.True(result.MomentumChanged);
        Assert.Equal(1, game.LoadCalls);
        Assert.Equal(1, game.NotifyCalls);
        Assert.Equal(1, game.RefreshCalls);
        Assert.Equal("1", game.Momentum["a"]);

        // The token after that load is remembered: no reload again.
        game.ClearCounts();
        mirror.Apply(game, Snap(1, 0, ("a", "1")));
        Assert.Equal(0, game.TotalCalls);
    }

    [Fact]
    public void Mirror_reports_an_unknown_phase_once_and_does_not_retry_it()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();

        var first = mirror.Apply(game, Snap(9, 0));
        Assert.Equal(9, first.UnknownPhase);
        Assert.False(first.PhaseChanged);
        Assert.Equal(1, game.SetPhaseCalls);
        Assert.Equal(0, game.Phase);

        game.ClearCounts();
        var second = mirror.Apply(game, Snap(9, 0));
        Assert.Null(second.UnknownPhase);
        Assert.Equal(0, game.SetPhaseCalls);

        // A different known phase goes through.
        game.ClearCounts();
        var third = mirror.Apply(game, Snap(3, 0));
        Assert.True(third.PhaseChanged);
        Assert.Equal(3, game.Phase);
    }

    [Fact]
    public void Mirror_reports_an_unknown_outcome_once()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();

        var first = mirror.Apply(game, Snap(0, 7));
        Assert.Equal(7, first.UnknownOutcome);
        Assert.False(first.OutcomeChanged);
        Assert.Equal(1, game.SetOutcomeCalls);

        game.ClearCounts();
        var second = mirror.Apply(game, Snap(0, 7));
        Assert.Null(second.UnknownOutcome);
        Assert.Equal(0, game.SetOutcomeCalls);
    }

    [Fact]
    public void Mirror_still_calls_refresh_once_when_the_game_cannot_refresh()
    {
        var game = new RecordingGame { CanRefresh = false };
        var result = new WotrMirror().Apply(game, Snap(1, 0, ("a", "1")));
        Assert.Equal(1, game.RefreshCalls);
        Assert.False(result.MeterRefreshed);
        Assert.True(result.Any);
    }

    [Fact]
    public void Mirror_reset_sets_everything_again()
    {
        var game = new RecordingGame();
        var mirror = new WotrMirror();
        mirror.Apply(game, Snap(9, 0, ("a", "1")));
        game.ClearCounts();

        mirror.Reset();
        var result = mirror.Apply(game, Snap(9, 0, ("a", "1")));

        Assert.True(result.MomentumChanged);
        Assert.Equal(1, game.LoadCalls);
        Assert.Equal(1, game.NotifyCalls);
        Assert.Equal(1, game.RefreshCalls);
        // A new campaign tries the refused phase again.
        Assert.Equal(1, game.SetPhaseCalls);
        Assert.Equal(9, result.UnknownPhase);
    }

    // ---- WotrChangeGate ----

    [Fact]
    public void Change_gate_fires_once_per_different_snapshot()
    {
        var gate = new WotrChangeGate();
        Assert.True(gate.Changed(Snap(0, 0)));
        Assert.False(gate.Changed(Snap(0, 0)));
        Assert.True(gate.Changed(Snap(1, 0)));
        Assert.False(gate.Changed(Snap(1, 0)));
        Assert.True(gate.Changed(Snap(1, 0, ("a", "1"))));
        Assert.False(gate.Changed(Snap(1, 0, ("a", "1"))));
        Assert.True(gate.Changed(Snap(1, 0, ("a", "2"))));
        Assert.True(gate.Changed(Snap(1, 2, ("a", "2"))));
        Assert.True(gate.Changed(Snap(0, 0)));

        gate.Reset();
        Assert.True(gate.Changed(Snap(0, 0)));
        Assert.False(gate.Changed(Snap(0, 0)));
    }

    // ---- TaomWotr.TryBind ----

    [Fact]
    public void Type_names_match_taom()
    {
        Assert.Equal("TAOM.Features.WarOfTheRingMomentum.WarOfTheRingMomentumBehavior", TaomWotr.MomentumBehaviorType);
        Assert.Equal("TAOM.Features.WarOfTheRingMomentum.IMomentumStateStore", TaomWotr.StateStoreType);
        Assert.Equal("TAOM.Features.Diplomacy.IWarOfTheRingService", TaomWotr.ServiceType);
    }

    [Fact]
    public void Bind_without_taom_is_taom_not_loaded()
    {
        var status = TaomWotr.TryBind(_ => null, false, Campaign, (_, _) => new object(), out var game, out _);
        Assert.Equal(WotrBindStatus.TaomNotLoaded, status);
        Assert.Null(game);

        // Also before any campaign exists.
        Assert.Equal(WotrBindStatus.TaomNotLoaded, TaomWotr.TryBind(_ => null, false, null, (_, _) => null, out _, out _));
    }

    [Fact]
    public void Bind_with_taom_loaded_but_types_gone_is_missing()
    {
        var status = TaomWotr.TryBind(_ => null, true, Campaign, (_, _) => null, out var game, out var missing);
        Assert.Equal(WotrBindStatus.Missing, status);
        Assert.Null(game);
        Assert.False(string.IsNullOrWhiteSpace(missing));
    }

    [Fact]
    public void Bind_with_one_type_gone_names_it()
    {
        var behavior = typeof(FakeBehavior<IFakeStore, IFakeService>);
        var status = TaomWotr.TryBind(Types(behavior, null, typeof(IFakeService)), true, Campaign, (_, _) => null, out var game, out var missing);
        Assert.Equal(WotrBindStatus.Missing, status);
        Assert.Null(game);
        Assert.Contains("IMomentumStateStore", missing);
    }

    public static TheoryData<string, Type, Type, Type> MissingMembers() => new()
    {
        { "SetOutcomeFromSave", typeof(FakeBehavior<IFakeStore, IServiceNoOutcomeSetter>), typeof(IFakeStore), typeof(IServiceNoOutcomeSetter) },
        { "CurrentPhase", typeof(FakeBehavior<IFakeStore, IServiceNoCurrentPhase>), typeof(IFakeStore), typeof(IServiceNoCurrentPhase) },
        { "NotifyMomentumChanged", typeof(FakeBehavior<IStoreNoNotify, IFakeService>), typeof(IStoreNoNotify), typeof(IFakeService) },
        { "Deserialize", typeof(FakeBehavior<IStoreNoDeserialize, IFakeService>), typeof(IStoreNoDeserialize), typeof(IFakeService) },
        { "_wotrService", typeof(FakeBehaviorNoService), typeof(IFakeStore), typeof(IFakeService) },
    };

    [Theory]
    [MemberData(nameof(MissingMembers))]
    public void Bind_with_a_member_gone_is_missing_and_names_it(string member, Type behavior, Type store, Type service)
    {
        var instance = behavior == typeof(FakeBehaviorNoService) ? (object)new FakeBehaviorNoService(new FakeStore()) : null;
        var status = TaomWotr.TryBind(Types(behavior, store, service), true, Campaign, (_, _) => instance, out var game, out var missing);
        Assert.Equal(WotrBindStatus.Missing, status);
        Assert.Null(game);
        Assert.Contains(member, missing);
    }

    [Fact]
    public void Bind_before_the_campaign_is_ready()
    {
        var types = Types(typeof(FakeBehavior<IFakeStore, IFakeService>), typeof(IFakeStore), typeof(IFakeService));
        Assert.Equal(WotrBindStatus.CampaignNotReady, TaomWotr.TryBind(types, true, null, (_, _) => throw new InvalidOperationException("no campaign to ask"), out var g1, out _));
        Assert.Null(g1);
        Assert.Equal(WotrBindStatus.CampaignNotReady, TaomWotr.TryBind(types, true, Campaign, (_, _) => null, out var g2, out _));
        Assert.Null(g2);
    }

    [Fact]
    public void Bind_ready_reads_and_writes_through_taom_objects()
    {
        var (game, behavior, store, service) = BindReady();
        Assert.Same(Campaign, game.Campaign);
        Assert.True(game.CanRefreshMeter);

        service.SetPhaseFromSave(FakePhase.IsengardWar);
        service.SetOutcomeFromSave(FakeOutcome.EvilVictory);
        store.Deserialize(new() { ["gondor"] = "3", ["mordor"] = "7" });
        Assert.Equal(1, game.Phase);
        Assert.Equal(2, game.Outcome);
        Assert.Equal(new Dictionary<string, string> { ["gondor"] = "3", ["mordor"] = "7" }, game.ReadMomentum());
        Assert.Same(store.State, game.StateToken);

        Assert.True(game.SetPhase(2));
        Assert.Equal(FakePhase.FullWar, service.CurrentPhase);
        Assert.False(game.SetPhase(9));
        Assert.Equal(FakePhase.FullWar, service.CurrentPhase);

        Assert.True(game.SetOutcome(1));
        Assert.Equal(FakeOutcome.FreeVictory, service.Outcome);
        Assert.False(game.SetOutcome(5));
        Assert.Equal(FakeOutcome.FreeVictory, service.Outcome);

        var before = game.StateToken;
        game.LoadMomentum(new() { ["rohan"] = "1" });
        Assert.NotSame(before, game.StateToken);
        Assert.Same(store.State, game.StateToken);
        Assert.Equal("1", store.State.Values["rohan"]);
        Assert.Single(store.State.Values);

        game.NotifyMomentumChanged();
        Assert.Equal(1, store.Notified);

        Assert.True(game.RefreshMeter());
        Assert.Equal(1, behavior.Refreshed);

        Assert.Contains("FullWar", game.PhaseName(2));

        var snap = game.Capture();
        Assert.Equal(2, snap.Phase);
        Assert.Equal(1, snap.Outcome);
        Assert.Equal("1", snap.Momentum["rohan"]);
    }

    [Fact]
    public void Bind_without_refresh_map_meter_is_still_ready()
    {
        var store = new FakeStore();
        var service = new FakeService();
        var behavior = new FakeBehaviorNoRefresh(store, service);
        var status = TaomWotr.TryBind(Types(typeof(FakeBehaviorNoRefresh), typeof(IFakeStore), typeof(IFakeService)),
            true, Campaign, (_, _) => behavior, out var game, out var missing);

        Assert.True(status == WotrBindStatus.Ready, "bind failed: " + missing);
        Assert.NotNull(game);
        Assert.False(game!.CanRefreshMeter);
        Assert.False(game.RefreshMeter());
        Assert.True(game.SetPhase(3));
        Assert.Equal(FakePhase.WarEnded, service.CurrentPhase);
    }

    // ---- End to end ----

    [Fact]
    public void Server_snapshot_lands_in_the_client_game()
    {
        var server = BindReady();
        server.service.SetPhaseFromSave(FakePhase.FullWar);
        server.service.SetOutcomeFromSave(FakeOutcome.None);
        server.store.Deserialize(new() { ["gondor"] = "12.5", ["mordor"] = "40", ["isengard"] = "" });

        var client = BindReady();
        Assert.Equal(0, client.game.Phase);

        // Server side.
        var gate = new WotrChangeGate();
        var captured = server.game.Capture();
        Assert.True(gate.Changed(captured));
        Assert.Null(WotrWire.CheckOutgoing(captured));
        captured.ToLists(out var keys, out var values);

        // Client side.
        var received = WotrWire.FromParts(WotrWire.ProtocolVersion, captured.Phase, captured.Outcome, keys, values, out var reason);
        Assert.Null(reason);
        Assert.NotNull(received);
        var mirror = new WotrMirror();
        var result = mirror.Apply(client.game, received!);

        Assert.True(result.PhaseChanged);
        Assert.True(result.MomentumChanged);
        Assert.True(result.MeterRefreshed);
        Assert.True(server.game.Capture().SameAs(client.game.Capture()));
        Assert.Equal(FakePhase.FullWar, client.service.CurrentPhase);
        Assert.Equal("12.5", client.store.State.Values["gondor"]);
        Assert.Equal(1, client.store.Notified);
        Assert.Equal(1, client.behavior.Refreshed);

        // Nothing changed on the server: the gate stays shut and a repeat apply does nothing.
        Assert.False(gate.Changed(server.game.Capture()));
        var again = mirror.Apply(client.game, received!);
        Assert.False(again.Any);
        Assert.Equal(1, client.behavior.Refreshed);
        Assert.Equal(1, client.store.Notified);

        // A client save load replaces the store state: the same snapshot is put back.
        client.store.Deserialize(new() { ["gondor"] = "0" });
        var reload = mirror.Apply(client.game, received!);
        Assert.True(reload.MomentumChanged);
        Assert.Equal("12.5", client.store.State.Values["gondor"]);
        Assert.Equal(2, client.behavior.Refreshed);
    }
}
