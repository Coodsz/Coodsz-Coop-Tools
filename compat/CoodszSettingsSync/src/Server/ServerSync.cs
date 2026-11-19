// Coodsz Settings Sync, server side (DedicatedServer.CoodszTools loads this assembly).
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CoodszSettingsSync.Messages;
using Common.Messaging;
using Common.Network;
using HarmonyLib;
using LiteNetLib;

namespace CoodszSettingsSync
{
    /// <summary>
    /// Reads the host's MCM settings and sends them to players who ask. Snapshots always go with
    /// INetwork.SendImmediate: a player asks right after Coop's module check, while Coop's join gate
    /// (ConnectionMessageQueue) still has that player in its Dropping phase, and CoopServer.Send silently
    /// drops every packet for a player in that phase. Coop's own join messages use SendImmediate for the
    /// same reason. Never use network.Send here. Every MCM read, every send and every
    /// change to the player list runs on the game thread from <see cref="Tick"/>; Coop's handler only queues
    /// the players who asked and its own start and end.
    /// </summary>
    public static class ServerSync
    {
        private const float ChangeCheckSeconds = 5f;
        private const float LiveSeconds = 3f;
        private const float RefreshSeconds = 60f;
        private const int MaxLoggedBroadcasts = 5;
        private const int MaxLoggedErrors = 5;
        private const float HandlerWaitSeconds = 60f;

        private static Action<string> _log = _ => { };
        private static string _moduleFolder = "";
        private static INetwork? _network;
        private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();
        private static readonly RequestGate<NetPeer> Asked = new RequestGate<NetPeer>(peer => peer.Id);
        // Players whose request set WantsNoSettingsAnswer, by peer id, until their answer went out.
        private static readonly ConcurrentDictionary<int, bool> WantsNone = new ConcurrentDictionary<int, bool>();
        private static readonly List<NetPeer> Players = new List<NetPeer>();
        // What the players already on the server got from a broadcast; a join answer never counts there.
        private static readonly SentLedger LastSent = new SentLedger();
        private static KeepOwnList _keepOwn = new KeepOwnList();
        private static DateTime _keepOwnStamp = DateTime.MinValue;
        private static bool _keepOwnBadNoted;
        private static McmSettings? _mcm;
        private static int _mcmGeneration = -1;
        private static float _sinceRefresh;
        private static LiveChannel? _live;
        private static HostSettingsFiles? _hostFiles;
        private static float _sinceLive = LiveSeconds;
        private static float _sinceCheck;
        private static WeakReference? _campaign;
        private static int _generation;
        private static string _source = "";
        private static int _broadcasts;
        private static int _errors;
        private static volatile bool _active;
        private static volatile bool _handlerBuilt;
        private static float _sinceStart;
        private static bool _noHandlerNoted;
        private static bool _handlerLateNoted;

        /// <summary>True once Start finished; Coop's handler does nothing before that.</summary>
        public static bool Active => _active;

        /// <summary>A line in the server console through the submodule's log (the PvP countdown uses it too).</summary>
        internal static void Log(string line) => _log(line);

        /// <summary>Called once by the submodule after the probe passed. Throws when the mark cannot be set.</summary>
        public static void Start(Action<string> log, string moduleFolder)
        {
            if (_active) return;
            _log = log ?? (_ => { });
            _moduleFolder = moduleFolder ?? "";
            MarkValidation();
            ReadKeepOwn(force: true);
            WotrServerSync.Start(_log);
            _active = true;
            _log("Settings sync is on: players with the Coodsz client add on get the host's mod settings when they join.");
        }

        /// <summary>
        /// For a server that only sends TAOM's War of the Ring state (the TAOM server tool's Coodsz War Sync):
        /// the mark goes in so the client add on asks, and its request is answered by the War of the Ring
        /// sync only. Settings stay each player's own; Active stays false, so the settings handler only says no settings are shared.
        /// </summary>
        public static void StartWarOnly(Action<string> log, string moduleFolder)
        {
            if (_active || _warOnly) return;
            _log = log ?? (_ => { });
            MarkValidation();
            WotrServerSync.Start(_log);
            _warOnly = true;
            _log("War of the Ring sync is ready: players with the Coodsz client add on get TAOM's war state when they join. Mod settings are not shared.");
        }

        private static volatile bool _warOnly;

        /// <summary>True once StartWarOnly finished: the settings handler only gives the empty answer.</summary>
        public static bool WarOnly => _warOnly;

        /// <summary>From the submodule's application tick, on the game thread. Each part fails on its own.</summary>
        public static void Tick(float dt)
        {
            if (_warOnly)
            {
                // Only the network the settings handler attached is used in this mode: it answers that no mod
                // settings are shared, so the player's game stops waiting.
                while (Pending.TryDequeue(out Action? action))
                    Safe("session", action);
                List<NetPeer> none = Asked.TakeAll();
                if (none.Count > 0) Safe("answer", () => AnswerNone(none));
                Safe("war of the ring", () => WotrServerSync.Tick(dt));
                return;
            }
            if (!_active) return;
            while (Pending.TryDequeue(out Action? action))
                Safe("session", action);
            // Coop builds its handlers once, when the server starts hosting (it sets IsServer just before), so the wait
            // counts from there; a sync that loaded after that is never asked.
            if (!_handlerBuilt && !_noHandlerNoted && global::Common.ModInformation.IsServer && (_sinceStart += dt) >= HandlerWaitSeconds)
            {
                _noHandlerNoted = true;
                _log("Settings sync: Coop never built its handler, so players will not get the host's settings. Restart the server.");
            }
            else if (_handlerBuilt && _noHandlerNoted && !_handlerLateNoted)
            {
                _handlerLateNoted = true;
                _log("Settings sync: Coop built its handler after all; players get the host's settings now.");
            }

            // Host overrides and live edits go in first, so a player who asks now gets the host's current values.
            _sinceLive += dt;
            _sinceRefresh += dt;
            if (_sinceLive >= LiveSeconds)
            {
                _sinceLive = 0f;
                Safe("host settings files", ApplyHostFiles);
                Safe("live settings", PollLive);
            }

            List<NetPeer> asked = Asked.TakeAll();
            if (asked.Count > 0) Safe("answer", () => Answer(asked));
            Safe("war of the ring", () => WotrServerSync.Tick(dt));

            _sinceCheck += dt;
            if (_sinceCheck < ChangeCheckSeconds) return;
            _sinceCheck = 0f;
            Safe("change check", () =>
            {
                Players.RemoveAll(p => p.ConnectionState != ConnectionState.Connected);
                if (Players.Count > 0 && _network != null) BroadcastChanges();
            });
        }

        private static void Safe(string what, Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                if (++_errors <= MaxLoggedErrors)
                    _log("Settings sync: " + what + " failed: " + e.GetType().Name + ": " + e.Message
                        + (_errors == MaxLoggedErrors ? ". Further errors are not logged." : ""));
            }
        }

        // ---- from Coop's handler (any thread): queued for the game thread -----------------------------

        internal static void Attach(INetwork network)
        {
            _handlerBuilt = true;
            Pending.Enqueue(() => _network = network);
        }

        internal static void Detach(INetwork network) => Pending.Enqueue(() =>
        {
            if (!ReferenceEquals(_network, network)) return;
            _network = null;
            Players.Clear();
            LastSent.Clear();
            Asked.Clear();
            WantsNone.Clear();
        });

        /// <summary>One request per player every 10 seconds, one waiting per player, 64 waiting in all.</summary>
        internal static void QueueAnswer(NetPeer peer, bool wantsNoSettingsAnswer)
        {
            // The flag is set inside the gate's lock: this runs off the game thread, and the tick's TakeAll could
            // otherwise take the request before the flag is there, so no empty answer went out.
            Asked.TryQueue(peer, DateTime.UtcNow, () => WantsNone[peer.Id] = wantsNoSettingsAnswer);
        }

        // ---- game thread -----------------------------------------------------------------------------

        private static void PollLive()
        {
            ReadKeepOwn(force: false);
            // Coodsz Host makes the live folder at Start; a server started some other way has none.
            string liveDir = Path.Combine(_moduleFolder, "live");
            if (_live == null && !Directory.Exists(liveDir)) return;
            _live ??= new LiveChannel(liveDir, _log);
            int generation = CampaignGeneration();
            _live.Poll(Mcm(generation), _keepOwn, generation);
        }

        // CoopModPatch's MCM adapter, whose McmServerReload reads the host's files into MCM on the dedicated server.
        // Names checked on 2026-10-30 with ilspycmd 8.2 in CoopModPatch v0.7.1 (Steam Workshop item 3786391685),
        // bin\Win64_Shipping_Server\Adapters\Adapter.Bannerlord.MBOptionScreen.dll: the assembly name is
        // Adapter.Bannerlord.MBOptionScreen and the class CoopModPatch.Adapters.Mcm.McmServerReload. CoopModPatch's
        // AdapterLoader.LoadAll loads the adapter with Assembly.LoadFrom from Runtime.Initialize in its SubModule's
        // OnSubModuleLoad, so it is loaded before any campaign. A later
        // version that renames them is not found, and the sync then puts the files in too (the same values, harmless).
        private const string CoopModPatchMcmAssembly = "Adapter.Bannerlord.MBOptionScreen";
        private const string CoopModPatchMcmReload = "CoopModPatch.Adapters.Mcm.McmServerReload";

        /// <summary>
        /// MCM reads its Global settings at boot from the game's own ModSettings folder, before Coop points the game at
        /// CoopData\DedicatedServer, so without this the server runs (and shares) those boot values instead of the
        /// host's. Once a campaign is loaded (its save comes from CoopData\DedicatedServer, so the redirect is in place),
        /// the host's files there are written into the live MCM, once per settings object and campaign, never over a
        /// value the host set through the live channel. HostSettingsFiles looks for the folder again while it is missing,
        /// reads the files once (again later for a file it could not read) and logs what it put in. Off when
        /// CoopModPatch's reload is loaded.
        /// </summary>
        private static void ApplyHostFiles()
        {
            int generation = CampaignGeneration();
            if (generation == 0) return;
            McmSettings? mcm = Mcm(generation);
            if (mcm == null || mcm.Definitions().Count == 0) return;
            _hostFiles ??= new HostSettingsFiles(
                () => SettingsFiles.FindCoopDataModSettings(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetEnvironmentVariable(SettingsFiles.ServerDataDirVariable)),
                () => CoopProbe.FindLoaded(CoopModPatchMcmAssembly, CoopModPatchMcmReload) != null,
                _log);
            LiveChannel? channel = _live;
            _hostFiles.Apply(mcm, generation, channel == null ? null : channel.HostSet);
        }

        /// <summary>
        /// MCM's provider with its definitions cached: read again when a campaign loads, after a host edit, and
        /// once a minute in case a mod made its settings object late. Values are always read live.
        /// </summary>
        private static McmSettings? Mcm(int generation)
        {
            if (_mcm == null)
            {
                _mcm = McmSettings.FindLive();
                _mcmGeneration = generation;
                _sinceRefresh = 0f;
                return _mcm;
            }
            if (generation != _mcmGeneration || _sinceRefresh >= RefreshSeconds || (_live != null && _live.TakeChanged()))
            {
                _mcm.Refresh();
                _mcmGeneration = generation;
                _sinceRefresh = 0f;
            }
            return _mcm;
        }

        private static void Answer(List<NetPeer> peers)
        {
            INetwork? network = _network;
            if (network == null) return;
            List<SettingsSnapshotData> snapshots = Capture();
            // The players already on the server get what changed since their last broadcast first: the answer below only
            // reaches the joining players, so it must never leave the others on older values.
            Broadcast(network, snapshots, peers);
            foreach (NetPeer peer in peers)
            {
                try
                {
                    bool wantsNone = WantsNone.TryRemove(peer.Id, out bool wants) && wants;
                    if (peer.ConnectionState != ConnectionState.Connected) continue;
                    foreach (SettingsSnapshotData snapshot in snapshots) network.SendImmediate(peer, ToMessage(snapshot));
                    // Nothing to share is an answer too, so the player's game stops waiting for one.
                    if (snapshots.Count == 0 && wantsNone)
                        network.SendImmediate(peer, new NetworkModSettingsNone { ProtocolVersion = SettingsWire.ProtocolVersion });
                    if (!Players.Contains(peer)) Players.Add(peer);
                    _log("Settings sync: sent " + Count(snapshots.Count) + " settings object" + (snapshots.Count == 1 ? "" : "s")
                        + " (" + _source + ") to player " + Count(peer.Id) + ".");
                }
                catch (Exception e)
                {
                    _log("Settings sync: sending to player " + Count(peer.Id) + " failed: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // War only mode: no settings are shared, so a player whose add on asked for it gets the empty answer. An
        // older add on gets nothing, as before.
        private static void AnswerNone(List<NetPeer> peers)
        {
            INetwork? network = _network;
            foreach (NetPeer peer in peers)
            {
                bool wantsNone = WantsNone.TryRemove(peer.Id, out bool wants) && wants;
                if (network == null || !wantsNone || peer.ConnectionState != ConnectionState.Connected) continue;
                try
                {
                    network.SendImmediate(peer, new NetworkModSettingsNone { ProtocolVersion = SettingsWire.ProtocolVersion });
                }
                catch (Exception e)
                {
                    _log("Settings sync: sending to player " + Count(peer.Id) + " failed: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private static void BroadcastChanges()
        {
            INetwork? network = _network;
            if (network == null) return;
            Broadcast(network, Capture(), new List<NetPeer>());
        }

        /// <summary>Sends the players already on the server, but the ones in <paramref name="skip"/>, each snapshot that changed since they last got one.</summary>
        private static void Broadcast(INetwork network, List<SettingsSnapshotData> snapshots, List<NetPeer> skip)
        {
            foreach (SettingsSnapshotData snapshot in LastSent.TakeChanged(snapshots))
            {
                // Only players who asked know this message; Coop's other players never get it.
                int sent = 0;
                foreach (NetPeer peer in Players.Where(p => !skip.Contains(p)).ToList())
                {
                    try
                    {
                        network.SendImmediate(peer, ToMessage(snapshot));
                        sent++;
                    }
                    catch (Exception) { }
                }
                if (++_broadcasts <= MaxLoggedBroadcasts)
                    _log("Settings sync: " + snapshot.SettingsId + " changed on the host; sent to " + Count(sent) + " player"
                        + (sent == 1 ? "" : "s") + (_broadcasts == MaxLoggedBroadcasts ? ". Further changes are not logged." : "."));
            }
        }

        private static List<SettingsSnapshotData> Capture()
        {
            ReadKeepOwn(force: false);
            try
            {
                McmSettings? mcm = Mcm(CampaignGeneration());
                if (mcm != null && mcm.Definitions().Count > 0)
                {
                    // The host's files in CoopData\DedicatedServer are already in the live MCM (ApplyHostFiles).
                    List<SettingsSnapshotData> live = SettingsCapture.Capture(mcm, _keepOwn);
                    NoteSource("the live MCM", live.Count);
                    return live;
                }

                string? root = SettingsFiles.FindModSettingsRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetEnvironmentVariable(SettingsFiles.ServerDataDirVariable));
                if (root == null)
                {
                    NoteSource("no MCM and no ModSettings folder", 0);
                    return new List<SettingsSnapshotData>();
                }
                List<SettingsSnapshotData> files = SettingsFiles.CaptureFromFiles(root, _keepOwn);
                NoteSource("MCM's provider is not running here, so the files in " + root, files.Count);
                return files;
            }
            catch (Exception e)
            {
                NoteSource("reading failed: " + e.GetType().Name + ": " + e.Message, 0);
                return new List<SettingsSnapshotData>();
            }
        }

        private static void NoteSource(string source, int objects)
        {
            if (source == _source) return;
            _source = source;
            _log("Settings sync reads " + source + " (" + Count(objects) + " settings objects to share"
                + (_keepOwn.Count > 0 ? ", " + Count(_keepOwn.Count) + " kept by players" : "") + ").");
        }

        /// <summary>
        /// Reads recipes.json again when it changed. A file that does not parse (half written, or broken) keeps
        /// the last good list, and is tried again on its next change.
        /// </summary>
        private static void ReadKeepOwn(bool force)
        {
            try
            {
                string path = Path.Combine(_moduleFolder, KeepOwnList.FileName);
                DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (!force && stamp == _keepOwnStamp) return;
                KeepOwnList? read = SettingsFiles.TryReadKeepOwn(path);
                if (read == null)
                {
                    // The stamp is not taken, so the file is read again on the next pass (it may be half written).
                    if (!_keepOwnBadNoted) _log("Settings sync: " + KeepOwnList.FileName + " could not be read; the last good list stays.");
                    _keepOwnBadNoted = true;
                    return;
                }
                _keepOwnBadNoted = false;
                _keepOwnStamp = stamp;
                _keepOwn = read;
                if (!force) _log("Settings sync: the players keep their own list changed (" + Count(_keepOwn.Count) + " entries).");
            }
            catch (Exception) { }
        }

        /// <summary>Bumps when Campaign.Current becomes a different object (a campaign loaded); read by reflection.</summary>
        private static int CampaignGeneration()
        {
            object? current = null;
            try
            {
                Type? type = Type.GetType("TaleWorlds.CampaignSystem.Campaign, TaleWorlds.CampaignSystem", false);
                current = type?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            }
            catch (Exception) { }
            object? last = _campaign != null && _campaign.IsAlive ? _campaign.Target : null;
            if (current != null && !ReferenceEquals(current, last))
            {
                _campaign = new WeakReference(current);
                _generation++;
            }
            return _generation;
        }

        private static NetworkModSettingsSnapshot ToMessage(SettingsSnapshotData snapshot) => new NetworkModSettingsSnapshot
        {
            ProtocolVersion = SettingsWire.ProtocolVersion,
            SettingsId = snapshot.SettingsId,
            Scope = (int)snapshot.Scope,
            Values = new List<string>(snapshot.Values),
            FromFile = snapshot.FromFile,
        };

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        // ---- the mark in Coop's module check answer ----------------------------------------------------

        private static void MarkValidation()
        {
            Type type = typeof(Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated);
            ConstructorInfo? constructor = type.GetConstructor(new[] { typeof(bool), typeof(string), typeof(string) });
            if (constructor == null) throw new MissingMethodException(type.FullName, ".ctor(bool, string, string)");
            new Harmony("coodsz.settings-sync.server").Patch(constructor,
                prefix: new HarmonyMethod(typeof(ServerSync).GetMethod(nameof(BeforeValidated), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        // A matching answer's Reason is never shown or read by Coop's client, so it carries the mark.
        private static void BeforeValidated(bool matches, ref string? reason)
        {
            if (matches && (_active || _warOnly)) reason = SettingsWire.ServerMark;
        }
    }
}

namespace Coop.Core.Server.Services.CoodszSettingsSync
{
    // Coop builds every IHandler under Coop.Core.Server.* when a server session starts. Every call is a
    // no-op while the sync is off (Start did not finish).
    public sealed class CoodszSettingsSyncServerHandler : IHandler
    {
        private readonly IMessageBroker _broker;
        private readonly INetwork _network;

        public CoodszSettingsSyncServerHandler(IMessageBroker broker, INetwork network)
        {
            _broker = broker;
            _network = network;
            global::CoodszSettingsSync.ServerSync.Attach(network);
            broker.Subscribe<NetworkModSettingsRequest>(HandleRequest);
        }

        private void HandleRequest(MessagePayload<NetworkModSettingsRequest> payload)
        {
            // War only mode (TAOM) answers too, but only a request that asked for the empty answer.
            bool warOnly = global::CoodszSettingsSync.ServerSync.WarOnly;
            if (!global::CoodszSettingsSync.ServerSync.Active && !warOnly) return;
            if (!global::Common.ModInformation.IsServer || !(payload.Who is NetPeer peer)) return;
            if (payload.What == null || !global::CoodszSettingsSync.SettingsWire.IsKnownProtocol(payload.What.ProtocolVersion)) return;
            if (warOnly && !payload.What.WantsNoSettingsAnswer) return;
            global::CoodszSettingsSync.ServerSync.QueueAnswer(peer, payload.What.WantsNoSettingsAnswer);
        }

        public void Dispose()
        {
            _broker.Unsubscribe<NetworkModSettingsRequest>(HandleRequest);
            global::CoodszSettingsSync.ServerSync.Detach(_network);
        }
    }
}
