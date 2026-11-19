// Coodsz Settings Sync, client side (DedicatedServer.CoodszJoinPassword loads this assembly).
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CoodszSettingsSync.Messages;
using Common.Messaging;
using Common.Network;
using HarmonyLib;

namespace CoodszSettingsSync
{
    /// <summary>
    /// Applies the host's MCM settings on this game for one Coop session, in memory only. Global settings
    /// go in as soon as they arrive (before the save loads); PerCampaign settings, and everything again, once
    /// the campaign is ready. All MCM work runs on the game thread from <see cref="Tick"/>.
    /// No host value is set until the save guard is confirmed in place; see <see cref="InstallSaveGuard"/>.
    /// </summary>
    public static class ClientSync
    {
        private const int MaxWorkPerTick = 64;
        private const string HarmonyId = "coodsz.settings-sync.client";

        private static Action<string> _log = _ => { };
        private static readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();
        private static readonly Dictionary<string, SettingsSnapshotData> Received = new Dictionary<string, SettingsSnapshotData>(StringComparer.Ordinal);
        private static readonly SyncSession Session = new SyncSession();
        private static readonly HashSet<string> GuardNoted = new HashSet<string>(StringComparer.Ordinal);
        private static readonly MissingObjects Missing = new MissingObjects();
        private const float NoAnswerSeconds = 30f;
        private const float HoldSeconds = 2f;
        // One log line per session the first time a value is held back (by the hold or a save) and one on screen per
        // mod (HeldNotes), and one the first time a save keeps host values out of the settings files, so testers can see both.
        private static readonly HeldNotes Held = new HeldNotes();
        private static bool _saveNoted;
        private static float _holdFor;
        // True from Coop's session start until it ends; holding host values stops once the player left.
        private static bool _inSession;
        private static bool _awaiting;
        private static float _awaitingFor;
        private static McmSettings? _mcm;
        private static bool _campaignReady;
        private static int _generation;
        private static bool _guardTried;
        private static bool _guardReady;
        private static bool _offForSession;
        private static bool _noMcmNoted;
        private static volatile bool _active;
        private static volatile bool _modderLords;

        /// <summary>True once Start ran; Coop's handler does nothing until then, or when the sync turned itself off.</summary>
        public static bool Active => _active && !_modderLords;

        /// <summary>Called once by the submodule after the probe passed.</summary>
        public static void Start(Action<string> log, string moduleFolder)
        {
            if (_active) return;
            _log = log ?? (_ => { });
            _active = true;
            WotrClientSync.Start(_log);
            PvpCountdownClient.Start(_log);
            _log("Settings sync is ready: on a Coodsz server the host's mod settings apply here for that session only.");
        }

        /// <summary>From the submodule's application tick, on the game thread.</summary>
        public static void Tick(float dt)
        {
            for (int i = 0; i < MaxWorkPerTick && Work.TryDequeue(out Action? work); i++)
            {
                try { work(); }
                catch (Exception e) { _log("Settings sync: " + e.GetType().Name + ": " + e.Message); }
            }

            try
            {
                if (_awaiting)
                {
                    _awaitingFor += dt;
                    if (_awaitingFor >= NoAnswerSeconds)
                    {
                        _awaiting = false;
                        _log(SyncNotes.NoAnswer((int)NoAnswerSeconds));
                    }
                }

                // Objects the host sent that this game did not have yet: applied once they appear. MCM's list is
                // only rebuilt for an id it lists but has not made yet; the live object is always asked by id, so
                // a mod this PC does not have costs one cheap lookup per try, and the tries back off.
                foreach (string id in Missing.Due(dt))
                {
                    if (!Received.TryGetValue(id, out SettingsSnapshotData? data)) { Missing.Remove(id); continue; }
                    if (data.Scope != SettingsScope.Global && !_campaignReady) continue;
                    if (Missing.IsListed(id)) _mcm?.Refresh();
                    ApplyOne(data, "once it appeared", quietIfMissing: true);
                }
                foreach (string id in Missing.TakeExpired())
                    _log(SyncNotes.GaveUp(id, (int)(MissingObjects.GiveUpSeconds / 60f)));

                // The host's values win while joined: a change the player makes goes back to the host's value.
                _holdFor += dt;
                if (_holdFor >= HoldSeconds && _inSession && _campaignReady && _guardReady && !_offForSession && !ModderLordsTookOver())
                {
                    _holdFor = 0f;
                    foreach (string id in Session.HoldHostValues())
                        NoteHeld(id);
                }
            }
            catch (Exception e) { _log("Settings sync: " + e.GetType().Name + ": " + e.Message); }

            try { WotrClientSync.Tick(dt); }
            catch (Exception e) { _log("Settings sync: " + e.GetType().Name + ": " + e.Message); }

            try { PvpCountdownClient.Tick(dt); }
            catch (Exception e) { _log("PvP countdown: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>True while a setting of this id holds the host's value; the save guard reads it.</summary>
        public static bool IsSynced(string? settingsId) => Session.IsSynced(settingsId);

        /// <summary>ModderLords has its own sync; once it shows up (it may load after this module), this one stays off.</summary>
        private static bool ModderLordsTookOver()
        {
            if (_modderLords) return true;
            if (!CoopProbe.ModderLordsLoaded()) return false;
            _modderLords = true;
            _log("Settings sync is off: ModderLords.Compat is loaded and syncs the mod settings itself.");
            return true;
        }

        // ---- from Coop's handler (any thread) ---------------------------------------------------------

        // Which Coop handler owns the session now. Coop may dispose the old handler after the new one started, so an
        // end from an older handler must not clear the new session (the PvP countdown client does the same).
        private static readonly SessionOwner Owner = new SessionOwner();

        /// <summary>A new handler's session; the handler is the owner its <see cref="SessionEnded"/> passes back.</summary>
        internal static void SessionStarted(object owner) => Work.Enqueue(() => StartSession(owner));

        private static void StartSession(object owner)
        {
            Owner.Take(owner);
            // A late or missing end leaves the last server's values in memory: the player's own go back first.
            int restored = Session.Restore();
            if (restored > 0)
                _log("Settings sync: put back " + restored.ToString(CultureInfo.InvariantCulture) + " of your own values left from the last server.");
            Received.Clear();
            Missing.Clear();
            lock (GuardNoted)
            {
                _saveNoted = false;
                GuardNoted.Clear();
            }
            Held.NewSession();
            Session.BeginSession();
            _inSession = true;
            _awaiting = false;
            _campaignReady = false;
            _offForSession = false;
        }

        internal static void Validated(string? reason, Action sendRequest)
        {
            if (!Active || ModderLordsTookOver()) return;
            int? version = SettingsWire.ReadServerMark(reason);
            if (version == null)
            {
                _log("Settings sync: this server does not share its mod settings; your own settings stay.");
                return;
            }
            if (!SettingsWire.IsKnownProtocol(version.Value))
            {
                _log("Settings sync: the server uses settings sync version " + version.Value.ToString(CultureInfo.InvariantCulture)
                    + " and this add on version " + SettingsWire.ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "; your own settings stay.");
                return;
            }
            // Queued before the request goes out, so an answer can never be handled before the wait starts.
            Work.Enqueue(() =>
            {
                _awaiting = true;
                _awaitingFor = 0f;
            });
            sendRequest();
            _log("Settings sync: asked the server for the host's mod settings for this session.");
        }

        // Every snapshot that reaches the handler gets a line in the log, whatever happens to it next.
        internal static void Receive(NetworkModSettingsSnapshot message)
        {
            if (message == null) return;
            int count = message.Values?.Count ?? 0;
            _log(SyncNotes.Received(message.SettingsId, message.Scope, count));
            string? rejected = SyncNotes.Rejected(Active, message.ProtocolVersion, message.SettingsId, message.Scope);
            if (rejected != null)
            {
                _log("Settings sync: " + (SettingsWire.IsValidId(message.SettingsId) ? message.SettingsId : "that snapshot") + ": " + rejected);
                return;
            }
            var scope = (SettingsScope)message.Scope;
            var data = new SettingsSnapshotData(message.SettingsId, scope, message.FromFile,
                (message.Values ?? new List<string>()).Take(SettingsWire.MaxValuesPerObject).ToList());
            Work.Enqueue(() =>
            {
                _awaiting = false;
                if (!Received.ContainsKey(data.SettingsId) && Received.Count >= SettingsWire.MaxObjectsPerSession)
                {
                    _log("Settings sync: " + data.SettingsId + ": more than " + SettingsWire.MaxObjectsPerSession.ToString(CultureInfo.InvariantCulture)
                        + " settings objects came this session, so it was not used.");
                    return;
                }
                Received[data.SettingsId] = data;
                if (data.Scope == SettingsScope.Global || _campaignReady)
                    ApplyOne(data, _campaignReady ? "from the host" : "before the save loads");
                else
                    _log("Settings sync: " + data.SettingsId + " is a PerCampaign setting; it is applied once the campaign has loaded.");
            });
        }

        // The server's answer when the host has no mod settings to share: one line instead of the waiting line.
        internal static void ReceiveNone(NetworkModSettingsNone message)
        {
            if (message == null || !Active || !SettingsWire.IsKnownProtocol(message.ProtocolVersion)) return;
            Work.Enqueue(() =>
            {
                if (!_awaiting) return;
                _awaiting = false;
                _log(SyncNotes.NoSettings);
            });
        }

        internal static void CampaignReady()
        {
            if (!Active) return;
            Work.Enqueue(() =>
            {
                _campaignReady = true;
                // A second CampaignReady in the same campaign makes a new generation for its PerCampaign settings. The
                // earlier entries point at the same objects with the same host values, so a hold never flips between
                // them, and Restore and a save still end on the player's own value whichever entry comes first.
                _generation++;
                // New PerCampaign objects came with the campaign.
                _mcm?.Refresh();
                Missing.Rearm();
                if (Received.Count == 0) return;
                // A mod may have reloaded its Global ones while loading, so everything goes in again.
                foreach (SettingsSnapshotData data in Received.Values.ToList()) ApplyOne(data, "after the campaign loaded");
            });
        }

        internal static void SessionEnded(object owner) => Work.Enqueue(() =>
        {
            // An older handler disposed after the new session began: the new session keeps going untouched.
            if (!Owner.Owns(owner))
            {
                _log("Settings sync: an earlier session ended late; the current one stays as it is.");
                return;
            }
            // No more holding: a value Restore could not put back must not be pulled back to the host's after leaving.
            _inSession = false;
            if (!_active) return;
            // The player's values go back into the objects MCM uses now, also when it swapped one this campaign.
            if (_mcm != null)
            {
                foreach (string id in Session.SyncedIds())
                {
                    try
                    {
                        object? live = _mcm.GetSettings(id);
                        if (live == null) continue;
                        var properties = new Dictionary<string, McmProperty>(StringComparer.Ordinal);
                        foreach (McmProperty property in _mcm.PropertiesOf(id))
                            if (!properties.ContainsKey(property.Id)) properties[property.Id] = property;
                        Session.Track(id, live, properties, _generation);
                    }
                    catch (Exception) { }
                }
            }
            int synced = Session.SyncedCount;
            int restored = Session.Restore();
            Received.Clear();
            Missing.Clear();
            _awaiting = false;
            _campaignReady = false;
            _log(SyncNotes.SessionEnded(synced, restored, Session.SyncedCount));
        });

        // ---- game thread -----------------------------------------------------------------------------

        private static void ApplyOne(SettingsSnapshotData data, string when, bool quietIfMissing = false)
        {
            if (_offForSession)
            {
                if (!quietIfMissing) _log("Settings sync: " + data.SettingsId + " was not applied; the sync is off for this session (see above).");
                return;
            }
            if (ModderLordsTookOver()) return;
            McmSettings? mcm = Mcm();
            if (mcm == null)
            {
                if (!_noMcmNoted || !quietIfMissing) _log("Settings sync: MCM is not running in this game, so " + data.SettingsId + " was not applied.");
                _noMcmNoted = true;
                return;
            }
            // Never a host value without the guard: without it MCM could write host values into the player's files.
            if (!_guardTried) InstallSaveGuard(mcm);
            if (!_guardReady)
            {
                _offForSession = true;
                _log("Settings sync: your settings files could not be protected, so the host's mod settings are not used this session.");
                return;
            }
            ApplyReport report = SettingsApplier.Apply(mcm, data, Session, _generation);
            if (report.ObjectMissing)
            {
                // Logged the first time only; the retry in Tick applies it once the mod makes it.
                if (Missing.Add(data.SettingsId, report.NotCreatedYet) || !quietIfMissing) _log(SyncNotes.Applied(data.SettingsId, when, report));
                return;
            }
            Missing.Remove(data.SettingsId);
            _log(SyncNotes.Applied(data.SettingsId, when, report));
        }

        private static McmSettings? Mcm()
        {
            if (_mcm != null) return _mcm;
            return _mcm = McmSettings.FindLive();
        }

        // ---- save guard ------------------------------------------------------------------------------

        /// <summary>
        /// Patches every MCM save path: the provider's SaveSettings and the JSON and XML formats' Save (a
        /// container or another MCM path can reach a format without the provider). During a save of a synced
        /// object the player's own values are put in, so the file keeps them, and the host's go back after.
        /// The guard counts as ready only when the provider's patch, and the patch of each format MCM has, is
        /// confirmed in place.
        /// </summary>
        private static void InstallSaveGuard(McmSettings mcm)
        {
            _guardTried = true;
            try
            {
                var harmony = new Harmony(HarmonyId);
                var prefix = new HarmonyMethod(typeof(ClientSync).GetMethod(nameof(BeforeSave), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First };
                var finalizer = new HarmonyMethod(typeof(ClientSync).GetMethod(nameof(AfterSave), BindingFlags.NonPublic | BindingFlags.Static));

                var targets = new List<MethodInfo>();
                MethodInfo? provider = mcm.Provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "SaveSettings" && m.GetParameters().Length == 1 && !m.IsAbstract);
                if (provider == null)
                {
                    _log("Settings sync: MCM's SaveSettings was not found.");
                    return;
                }
                targets.Add(provider);
                foreach (string format in new[] { "MCM.Implementation.BaseJsonSettingsFormat", "MCM.Implementation.XmlSettingsFormat" })
                {
                    Type? type = CoopProbe.FindLoaded("MCMv5", format);
                    MethodInfo? save = type?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .FirstOrDefault(m => m.Name == "Save" && m.GetParameters().Length == 3 && !m.IsAbstract);
                    if (save != null) targets.Add(save);
                }

                foreach (MethodInfo target in targets)
                {
                    harmony.Patch(target, prefix: prefix, finalizer: finalizer);
                    Patches? info = Harmony.GetPatchInfo(target);
                    if (info == null || !info.Prefixes.Any(p => p.owner == HarmonyId) || !info.Finalizers.Any(p => p.owner == HarmonyId))
                    {
                        _log("Settings sync: the save guard did not take on " + target.DeclaringType?.Name + "." + target.Name + ".");
                        return;
                    }
                }
                _guardReady = true;
            }
            catch (Exception e)
            {
                _log("Settings sync: the save guard could not install: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Before any MCM save: the player's own values go in for a synced object. A copy that cannot be
        // swapped is not saved at all, so no host value reaches the player's files.
        private static bool BeforeSave(object __0, out SyncSession.SaveSwap? __state)
        {
            __state = null;
            try
            {
                __state = Session.BeginSave(__0, out bool block);
                if (!block)
                {
                    if (__state != null && __state.SwappedCount > 0) NoteSave(McmSettings.Text(__0, "Id"), __state.PlayerChanged);
                    return true;
                }
                string? id = McmSettings.Text(__0, "Id");
                lock (GuardNoted)
                    if (id != null && GuardNoted.Add(id)) _log("Settings sync: kept " + id + " out of your settings files while it holds the host's values.");
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // The player sees one line on screen per mod per session when a change of theirs goes back to the host's value,
        // with the mod's name from MCM where it has one and in the game's language; the log gets the first one.
        private static void NoteHeld(string? id)
        {
            if (Held.Held(id, out bool show)) _log(SyncNotes.Held(id ?? "a setting"));
            if (!show) return;
            try
            {
                string? name = null;
                try { if (id != null && _mcm?.GetSettings(id) is object live) name = McmSettings.Text(live, "DisplayName"); }
                catch (Exception) { }
                TaleWorlds.Library.InformationManager.DisplayMessage(new TaleWorlds.Library.InformationMessage(
                    HeldNotes.Text(PvpCountdownText.LanguageFor(PvpCountdownClient.GameLanguage()), HeldNotes.Name(name, id))));
            }
            catch (Exception) { }
        }

        private static void NoteSave(string? id, bool playerChanged)
        {
            bool first;
            lock (GuardNoted)
            {
                first = !_saveNoted;
                _saveNoted = true;
            }
            if (first) _log(SyncNotes.SavedOwn(id ?? "a setting"));
            if (playerChanged) NoteHeld(id);
        }

        private static Exception? AfterSave(Exception? __exception, SyncSession.SaveSwap? __state)
        {
            try { Session.EndSave(__state); }
            catch (Exception) { }
            return __exception;
        }
    }
}

namespace Coop.Core.Client.Services.CoodszSettingsSync
{
    using Coop.Core.Server.Connections.Messages;
    using GameInterface.Services.GameState.Messages;

    // Coop builds every IHandler under Coop.Core.Client.* when the client starts a session. Every call is a
    // no-op while the sync is off (Start did not run, or ModderLords took over).
    public sealed class CoodszSettingsSyncClientHandler : IHandler
    {
        private readonly IMessageBroker _broker;
        private readonly INetwork _network;

        public CoodszSettingsSyncClientHandler(IMessageBroker broker, INetwork network)
        {
            _broker = broker;
            _network = network;
            global::CoodszSettingsSync.ClientSync.SessionStarted(this);
            broker.Subscribe<NetworkModuleVersionsValidated>(HandleValidated);
            broker.Subscribe<NetworkModSettingsSnapshot>(HandleSnapshot);
            broker.Subscribe<NetworkModSettingsNone>(HandleNone);
            broker.Subscribe<CampaignReady>(HandleCampaignReady);
        }

        private void HandleValidated(MessagePayload<NetworkModuleVersionsValidated> payload)
        {
            if (!global::CoodszSettingsSync.ClientSync.Active || payload.What == null || !payload.What.Matches) return;
            global::CoodszSettingsSync.ClientSync.Validated(payload.What.Reason, () =>
                _network.SendAll(new NetworkModSettingsRequest
                {
                    ProtocolVersion = global::CoodszSettingsSync.SettingsWire.ProtocolVersion,
                    WantsWarOfTheRing = true,
                    WantsPvpCountdown = true,
                    WantsNoSettingsAnswer = true,
                }));
        }

        private void HandleNone(MessagePayload<NetworkModSettingsNone> payload)
        {
            if (global::Common.ModInformation.IsServer || payload.What == null) return;
            global::CoodszSettingsSync.ClientSync.ReceiveNone(payload.What);
        }

        private void HandleSnapshot(MessagePayload<NetworkModSettingsSnapshot> payload)
        {
            // Settings only ever flow from the host to players. Receive logs every snapshot, also when the sync is off.
            if (global::Common.ModInformation.IsServer || payload.What == null) return;
            global::CoodszSettingsSync.ClientSync.Receive(payload.What);
        }

        private void HandleCampaignReady(MessagePayload<CampaignReady> payload) => global::CoodszSettingsSync.ClientSync.CampaignReady();

        public void Dispose()
        {
            _broker.Unsubscribe<NetworkModuleVersionsValidated>(HandleValidated);
            _broker.Unsubscribe<NetworkModSettingsSnapshot>(HandleSnapshot);
            _broker.Unsubscribe<NetworkModSettingsNone>(HandleNone);
            _broker.Unsubscribe<CampaignReady>(HandleCampaignReady);
            global::CoodszSettingsSync.ClientSync.SessionEnded(this);
        }
    }
}
