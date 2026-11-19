// Coodsz War of the Ring sync: TAOM keeps its War of the Ring phase and momentum only on the server
// (every change is authority only), so a player's top bar and peace rules read a frozen copy from the
// join save. The server sends that state to players with the Coodsz client add on whenever it changes,
// and the client puts it into TAOM's own objects. TAOM is reached only by reflection, by type and member
// name; anything missing turns the sync off with one log line. No TAOM code is copied here.
//
// Engine free; compiled into the server and client CoodszSettingsSync.Coop builds and into the launcher's tests.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CoodszSettingsSync
{
    /// <summary>The server's War of the Ring state: phase, outcome and TAOM's momentum save payload.</summary>
    public sealed class WotrSnapshot
    {
        public WotrSnapshot(int phase, int outcome, IDictionary<string, string>? momentum)
        {
            Phase = phase;
            Outcome = outcome;
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            if (momentum != null)
                foreach (KeyValuePair<string, string> entry in momentum)
                    if (entry.Key != null) copy[entry.Key] = entry.Value ?? "";
            Momentum = copy;
        }

        /// <summary>TAOM's WarPhase as a number (Peace, IsengardWar, FullWar, WarEnded in TAOM 2.0.x).</summary>
        public int Phase { get; }

        /// <summary>TAOM's WarOutcome as a number (None, FreeVictory, EvilVictory in TAOM 2.0.x).</summary>
        public int Outcome { get; }

        /// <summary>The momentum store's own save dictionary (IMomentumStateStore.Serialize).</summary>
        public IReadOnlyDictionary<string, string> Momentum { get; }

        public bool SameMomentum(WotrSnapshot? other)
        {
            if (other == null || other.Momentum.Count != Momentum.Count) return false;
            foreach (KeyValuePair<string, string> entry in Momentum)
                if (!other.Momentum.TryGetValue(entry.Key, out string? value) || !string.Equals(value, entry.Value, StringComparison.Ordinal))
                    return false;
            return true;
        }

        public bool SameAs(WotrSnapshot? other) =>
            other != null && other.Phase == Phase && other.Outcome == Outcome && SameMomentum(other);

        /// <summary>The momentum keys and values as two lists in key order, for the network message.</summary>
        public void ToLists(out List<string> keys, out List<string> values)
        {
            keys = new List<string>(Momentum.Count);
            values = new List<string>(Momentum.Count);
            foreach (KeyValuePair<string, string> entry in Momentum.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                keys.Add(entry.Key);
                values.Add(entry.Value);
            }
        }
    }

    /// <summary>The limits and checks both sides apply to a snapshot.</summary>
    public static class WotrWire
    {
        /// <summary>Bump when a member of NetworkWarOfTheRingSnapshot changes meaning.</summary>
        public const int ProtocolVersion = 1;

        public const int MaxEnumValue = 63;
        public const int MaxEntries = 4096;
        public const int MaxKeyLength = 128;
        public const int MaxValueLength = 4096;
        public const int MaxTotalLength = 1_000_000;

        /// <summary>Null when the parts make a usable snapshot, otherwise why not. Protobuf sends an empty list as null.</summary>
        public static string? Check(int protocolVersion, int phase, int outcome, IList<string>? keys, IList<string>? values)
        {
            if (protocolVersion != ProtocolVersion)
                return "it uses War of the Ring sync version " + protocolVersion.ToString(CultureInfo.InvariantCulture)
                    + " and this add on version " + ProtocolVersion.ToString(CultureInfo.InvariantCulture);
            if (phase < 0 || phase > MaxEnumValue) return "the phase " + phase.ToString(CultureInfo.InvariantCulture) + " is out of range";
            if (outcome < 0 || outcome > MaxEnumValue) return "the outcome " + outcome.ToString(CultureInfo.InvariantCulture) + " is out of range";
            int keyCount = keys?.Count ?? 0;
            int valueCount = values?.Count ?? 0;
            if (keyCount != valueCount) return "it has " + keyCount.ToString(CultureInfo.InvariantCulture) + " keys and "
                + valueCount.ToString(CultureInfo.InvariantCulture) + " values";
            if (keyCount > MaxEntries) return "it has more than " + MaxEntries.ToString(CultureInfo.InvariantCulture) + " momentum entries";
            if (keyCount == 0) return null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            for (int i = 0; i < keyCount; i++)
            {
                string? key = keys![i];
                string? value = values![i];
                if (string.IsNullOrEmpty(key) || key!.Length > MaxKeyLength) return "a momentum key is empty or too long";
                if (!seen.Add(key)) return "the momentum key " + key + " is there twice";
                if (value != null && value.Length > MaxValueLength) return "the momentum value of " + key + " is too long";
                total += key.Length + (value?.Length ?? 0);
                if (total > MaxTotalLength) return "the momentum data is larger than " + MaxTotalLength.ToString(CultureInfo.InvariantCulture) + " characters";
            }
            return null;
        }

        /// <summary>The snapshot from the message parts, or null with <paramref name="reason"/> when <see cref="Check"/> refuses them.</summary>
        public static WotrSnapshot? FromParts(int protocolVersion, int phase, int outcome, IList<string>? keys, IList<string>? values, out string? reason)
        {
            reason = Check(protocolVersion, phase, outcome, keys, values);
            if (reason != null) return null;
            var momentum = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < (keys?.Count ?? 0); i++) momentum[keys![i]] = values![i] ?? "";
            return new WotrSnapshot(phase, outcome, momentum);
        }

        /// <summary>Null when the server may send this snapshot, otherwise why not.</summary>
        public static string? CheckOutgoing(WotrSnapshot snapshot)
        {
            snapshot.ToLists(out List<string> keys, out List<string> values);
            return Check(ProtocolVersion, snapshot.Phase, snapshot.Outcome, keys, values);
        }
    }

    /// <summary>TAOM's War of the Ring objects in one campaign, as the sync needs them.</summary>
    public interface IWotrGame
    {
        int Phase { get; }
        int Outcome { get; }

        /// <summary>The momentum store's save dictionary.</summary>
        Dictionary<string, string> ReadMomentum();

        /// <summary>The object the store holds its state in now. A load replaces it, so a new one means the store was reloaded.</summary>
        object? StateToken { get; }

        /// <summary>False when this TAOM has no phase with that number.</summary>
        bool SetPhase(int phase);

        /// <summary>False when this TAOM has no outcome with that number.</summary>
        bool SetOutcome(int outcome);

        void LoadMomentum(Dictionary<string, string> momentum);
        void NotifyMomentumChanged();

        /// <summary>Shows or hides TAOM's map meter for the current state. False when this TAOM has no way to do it.</summary>
        bool RefreshMeter();

        string PhaseName(int phase);
    }

    public sealed class WotrApplyResult
    {
        public bool PhaseChanged { get; internal set; }
        public bool OutcomeChanged { get; internal set; }
        public bool MomentumChanged { get; internal set; }
        public bool MeterRefreshed { get; internal set; }

        /// <summary>The server's phase or outcome number that this TAOM does not know, or null.</summary>
        public int? UnknownPhase { get; internal set; }
        public int? UnknownOutcome { get; internal set; }

        /// <summary>Why the meter refresh threw, or null. The mirror tries it again on the next Apply.</summary>
        public string? MeterError { get; internal set; }

        public bool Any => PhaseChanged || OutcomeChanged || MomentumChanged;
    }

    /// <summary>
    /// Keeps a player's TAOM equal to the server's latest snapshot. Only what differs is set: the phase and
    /// outcome when they differ from TAOM's own, the momentum when the server's changed or TAOM's store was
    /// replaced (a load). Calling it again with the same snapshot does nothing.
    /// </summary>
    public sealed class WotrMirror
    {
        private WotrSnapshot? _applied;
        private WeakReference? _token;
        private int? _rejectedPhase;
        private int? _rejectedOutcome;
        private bool _meterPending;

        public WotrApplyResult Apply(IWotrGame game, WotrSnapshot snapshot)
        {
            var result = new WotrApplyResult();
            if (game.Phase != snapshot.Phase && snapshot.Phase != _rejectedPhase)
            {
                if (game.SetPhase(snapshot.Phase)) result.PhaseChanged = true;
                else result.UnknownPhase = _rejectedPhase = snapshot.Phase;
            }
            if (game.Outcome != snapshot.Outcome && snapshot.Outcome != _rejectedOutcome)
            {
                if (game.SetOutcome(snapshot.Outcome)) result.OutcomeChanged = true;
                else result.UnknownOutcome = _rejectedOutcome = snapshot.Outcome;
            }

            object? token = game.StateToken;
            bool replaced = token != null && !ReferenceEquals(token, _token?.Target);
            if (_applied == null || !_applied.SameMomentum(snapshot) || replaced)
            {
                game.LoadMomentum(snapshot.Momentum.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));
                game.NotifyMomentumChanged();
                object? loaded = game.StateToken;
                _token = loaded == null ? null : new WeakReference(loaded);
                result.MomentumChanged = true;
            }
            _applied = snapshot;

            // TAOM's meter needs a live map screen, so a refresh during a battle or a loading screen can throw;
            // it is tried again on each later Apply until it works, without failing the state that went in.
            if (result.Any || _meterPending)
            {
                try
                {
                    result.MeterRefreshed = game.RefreshMeter();
                    _meterPending = false;
                }
                catch (Exception e)
                {
                    Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    _meterPending = true;
                    result.MeterError = inner.GetType().Name + ": " + inner.Message;
                }
            }
            return result;
        }

        /// <summary>For a new campaign: the next Apply sets everything again.</summary>
        public void Reset()
        {
            _applied = null;
            _token = null;
            _rejectedPhase = null;
            _rejectedOutcome = null;
            _meterPending = false;
        }
    }

    /// <summary>The server's side: true once for each snapshot that differs from the last one sent.</summary>
    public sealed class WotrChangeGate
    {
        private WotrSnapshot? _last;

        public bool Changed(WotrSnapshot current)
        {
            if (current.SameAs(_last)) return false;
            _last = current;
            return true;
        }

        public void Reset() => _last = null;
    }

    /// <summary>
    /// The server's players for the war state. A player waits until one send to them worked, and goes back to
    /// waiting when a send fails, so nobody is left on an old state when the next snapshot equals the last one.
    /// Waiting players get the current snapshot on every poll; the others only when it changed.
    /// </summary>
    public sealed class WotrRecipients<TPeer> where TPeer : class
    {
        private readonly List<TPeer> _current = new List<TPeer>();
        private readonly List<TPeer> _waiting = new List<TPeer>();
        private readonly int _max;

        public WotrRecipients(int max) => _max = max;

        public int Count => _current.Count + _waiting.Count;

        public int WaitingCount => _waiting.Count;

        public void Join(TPeer peer)
        {
            if (_current.Contains(peer) || _waiting.Contains(peer) || Count >= _max) return;
            _waiting.Add(peer);
        }

        /// <summary>Drops the players <paramref name="keep"/> says are gone.</summary>
        public void Prune(Func<TPeer, bool> keep)
        {
            _current.RemoveAll(p => !keep(p));
            _waiting.RemoveAll(p => !keep(p));
        }

        /// <summary>Who gets this poll's snapshot: everyone when it changed, else only the waiting players.</summary>
        public List<TPeer> Targets(bool changed) => changed ? _current.Concat(_waiting).ToList() : _waiting.ToList();

        public void Sent(TPeer peer, bool worked)
        {
            if (worked)
            {
                if (_waiting.Remove(peer)) _current.Add(peer);
            }
            else if (_current.Remove(peer))
            {
                _waiting.Add(peer);
            }
        }

        public void Clear()
        {
            _current.Clear();
            _waiting.Clear();
        }
    }

    public enum WotrBindStatus
    {
        Ready,

        /// <summary>TAOM is not in this game: nothing to sync, ever.</summary>
        TaomNotLoaded,

        /// <summary>No campaign, or TAOM's behavior is not in it yet; ask again later.</summary>
        CampaignNotReady,

        /// <summary>TAOM is there but a type or member the sync uses is not (a TAOM update moved it).</summary>
        Missing,
    }

    /// <summary>
    /// TAOM's War of the Ring objects found by reflection: the momentum campaign behavior's state store and
    /// War of the Ring service (the same instances TAOM's behavior uses and saves), reached through the
    /// campaign's GetCampaignBehavior. Phase and outcome go in through the service's own load setters.
    /// </summary>
    public sealed class TaomWotr : IWotrGame
    {
        public const string TaomAssembly = "TAOM";
        public const string MomentumBehaviorType = "TAOM.Features.WarOfTheRingMomentum.WarOfTheRingMomentumBehavior";
        public const string StateStoreType = "TAOM.Features.WarOfTheRingMomentum.IMomentumStateStore";
        public const string ServiceType = "TAOM.Features.Diplomacy.IWarOfTheRingService";

        private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly object _behavior;
        private readonly object _store;
        private readonly object _service;
        private readonly PropertyInfo _phase;
        private readonly PropertyInfo _outcome;
        private readonly PropertyInfo _state;
        private readonly MethodInfo _serialize;
        private readonly MethodInfo _deserialize;
        private readonly MethodInfo _notify;
        private readonly MethodInfo _setPhase;
        private readonly MethodInfo _setOutcome;
        private readonly MethodInfo? _refresh;
        private readonly Type _phaseType;
        private readonly Type _outcomeType;

        private TaomWotr(object campaign, object behavior, object store, object service, PropertyInfo phase, PropertyInfo outcome,
            PropertyInfo state, MethodInfo serialize, MethodInfo deserialize, MethodInfo notify, MethodInfo setPhase, MethodInfo setOutcome,
            MethodInfo? refresh)
        {
            Campaign = campaign;
            _behavior = behavior;
            _store = store;
            _service = service;
            _phase = phase;
            _outcome = outcome;
            _state = state;
            _serialize = serialize;
            _deserialize = deserialize;
            _notify = notify;
            _setPhase = setPhase;
            _setOutcome = setOutcome;
            _refresh = refresh;
            _phaseType = phase.PropertyType;
            _outcomeType = outcome.PropertyType;
        }

        /// <summary>The campaign this was found in; a different Campaign.Current needs a new bind.</summary>
        public object Campaign { get; }

        /// <summary>False when TAOM has no map meter refresh to call; its own daily tick still shows the bar.</summary>
        public bool CanRefreshMeter => _refresh != null;

        public int Phase => Convert.ToInt32(_phase.GetValue(_service, null), CultureInfo.InvariantCulture);

        public int Outcome => Convert.ToInt32(_outcome.GetValue(_service, null), CultureInfo.InvariantCulture);

        public object? StateToken => _state.GetValue(_store, null);

        public Dictionary<string, string> ReadMomentum() =>
            _serialize.Invoke(_store, null) as Dictionary<string, string> ?? new Dictionary<string, string>();

        public bool SetPhase(int phase) => SetEnum(_setPhase, _phaseType, phase);

        public bool SetOutcome(int outcome) => SetEnum(_setOutcome, _outcomeType, outcome);

        public void LoadMomentum(Dictionary<string, string> momentum) => _deserialize.Invoke(_store, new object[] { momentum });

        public void NotifyMomentumChanged() => _notify.Invoke(_store, null);

        public bool RefreshMeter()
        {
            if (_refresh == null) return false;
            _refresh.Invoke(_behavior, null);
            return true;
        }

        public string PhaseName(int phase)
        {
            object value = Enum.ToObject(_phaseType, phase);
            return Enum.IsDefined(_phaseType, value) ? value.ToString() ?? "" : phase.ToString(CultureInfo.InvariantCulture);
        }

        private bool SetEnum(MethodInfo setter, Type enumType, int number)
        {
            object value = Enum.ToObject(enumType, number);
            if (!Enum.IsDefined(enumType, value)) return false;
            setter.Invoke(_service, new[] { value });
            return true;
        }

        /// <summary>A snapshot of this game's state.</summary>
        public WotrSnapshot Capture() => new WotrSnapshot(Phase, Outcome, ReadMomentum());

        // ---- binding ---------------------------------------------------------------------------------

        /// <summary>Binds to the live game: TAOM's types from the loaded assemblies, the behavior from Campaign.Current.</summary>
        public static WotrBindStatus BindLive(out TaomWotr? game, out string missing) =>
            TryBind(FindLoadedType, AssemblyLoaded(TaomAssembly), CurrentCampaign(), LiveBehavior, out game, out missing);

        /// <summary>
        /// Binds with the given lookups. <paramref name="findType"/> finds a type by full name in any loaded
        /// assembly; <paramref name="behaviorOf"/> returns the campaign's behavior of a type, or null.
        /// </summary>
        public static WotrBindStatus TryBind(Func<string, Type?> findType, bool taomAssemblyLoaded, object? campaign,
            Func<object, Type, object?> behaviorOf, out TaomWotr? game, out string missing)
        {
            game = null;
            missing = "";
            Type? behaviorType = findType(MomentumBehaviorType);
            Type? storeType = findType(StateStoreType);
            Type? serviceType = findType(ServiceType);
            if (behaviorType == null && storeType == null && serviceType == null && !taomAssemblyLoaded)
                return WotrBindStatus.TaomNotLoaded;

            var gaps = new List<string>();
            if (behaviorType == null) gaps.Add("WarOfTheRingMomentumBehavior");
            if (storeType == null) gaps.Add("IMomentumStateStore");
            if (serviceType == null) gaps.Add("IWarOfTheRingService");
            if (gaps.Count > 0) return Gap(gaps, out missing);

            PropertyInfo? phase = serviceType!.GetProperty("CurrentPhase", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo? outcome = serviceType.GetProperty("Outcome", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo? setPhase = phase == null ? null : OneArg(serviceType, "SetPhaseFromSave", phase.PropertyType);
            MethodInfo? setOutcome = outcome == null ? null : OneArg(serviceType, "SetOutcomeFromSave", outcome.PropertyType);
            if (phase == null || !phase.PropertyType.IsEnum || !phase.CanRead) gaps.Add("IWarOfTheRingService.CurrentPhase");
            if (outcome == null || !outcome.PropertyType.IsEnum || !outcome.CanRead) gaps.Add("IWarOfTheRingService.Outcome");
            if (setPhase == null) gaps.Add("IWarOfTheRingService.SetPhaseFromSave");
            if (setOutcome == null) gaps.Add("IWarOfTheRingService.SetOutcomeFromSave");

            PropertyInfo? state = storeType!.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo? serialize = storeType.GetMethod("Serialize", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            MethodInfo? deserialize = OneArg(storeType, "Deserialize", typeof(Dictionary<string, string>));
            MethodInfo? notify = storeType.GetMethod("NotifyMomentumChanged", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (state == null || !state.CanRead) gaps.Add("IMomentumStateStore.State");
            if (serialize == null || serialize.ReturnType != typeof(Dictionary<string, string>)) gaps.Add("IMomentumStateStore.Serialize");
            if (deserialize == null) gaps.Add("IMomentumStateStore.Deserialize");
            if (notify == null) gaps.Add("IMomentumStateStore.NotifyMomentumChanged");

            FieldInfo? storeField = FindField(behaviorType!, "_stateStore", storeType);
            FieldInfo? serviceField = FindField(behaviorType!, "_wotrService", serviceType);
            if (storeField == null) gaps.Add("WarOfTheRingMomentumBehavior._stateStore");
            if (serviceField == null) gaps.Add("WarOfTheRingMomentumBehavior._wotrService");
            if (gaps.Count > 0) return Gap(gaps, out missing);

            if (campaign == null) return WotrBindStatus.CampaignNotReady;
            object? behavior = behaviorOf(campaign, behaviorType!);
            if (behavior == null || !behaviorType!.IsInstanceOfType(behavior)) return WotrBindStatus.CampaignNotReady;
            object? store = storeField!.GetValue(behavior);
            object? service = serviceField!.GetValue(behavior);
            if (store == null || service == null) return WotrBindStatus.CampaignNotReady;

            // Optional: the behavior's own meter refresh (adds the bar in Full War, removes it after victory).
            MethodInfo? refresh = FindMethod(behaviorType!, "RefreshMapMeter");
            game = new TaomWotr(campaign, behavior, store, service, phase!, outcome!, state!, serialize!, deserialize!, notify!,
                setPhase!, setOutcome!, refresh);
            return WotrBindStatus.Ready;
        }

        private static WotrBindStatus Gap(List<string> gaps, out string missing)
        {
            missing = string.Join(", ", gaps);
            return WotrBindStatus.Missing;
        }

        private static MethodInfo? OneArg(Type type, string name, Type parameter) =>
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, new[] { parameter }, null);

        private static FieldInfo? FindField(Type type, string name, Type fieldType)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                FieldInfo? field = t.GetField(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (field != null) return fieldType.IsAssignableFrom(field.FieldType) ? field : null;
            }
            return null;
        }

        private static MethodInfo? FindMethod(Type type, string name)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                MethodInfo? method = t.GetMethod(name, InstanceAny | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null) return method;
            }
            return null;
        }

        // ---- the live game, by reflection --------------------------------------------------------------

        public static Type? FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                try
                {
                    Type? type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch (Exception) { }
            }
            return null;
        }

        public static bool AssemblyLoaded(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!assembly.IsDynamic && string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception) { }
            }
            return false;
        }

        /// <summary>Campaign.Current, or null outside a campaign or without the game.</summary>
        public static object? CurrentCampaign()
        {
            try
            {
                Type? type = Type.GetType("TaleWorlds.CampaignSystem.Campaign, TaleWorlds.CampaignSystem", false);
                return type?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>campaign.GetCampaignBehavior&lt;T&gt;() for a type known only at run time.</summary>
        public static object? LiveBehavior(object campaign, Type behaviorType)
        {
            MethodInfo? generic = campaign.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetCampaignBehavior" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            return generic?.MakeGenericMethod(behaviorType).Invoke(campaign, null);
        }
    }
}
