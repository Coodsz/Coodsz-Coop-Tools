using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RealmOfThronesCoopCompat
{
    // Holds Realm of Thrones' settings to one table (SettingsRules) inside a Coop session, on the
    // server and on every player's PC, and switches duels and ravens really off there:
    // - SettingsPatches puts a prefix on each covered getter; the prefix asks Active, a cached bool,
    //   because some getters are read per agent or per party. Active is always true in the
    //   dedicated server process (it only ever runs co-op, and makes its world before Coop's
    //   session starts) and else follows Coop's session edges, or a poll once a second when the
    //   edges could not be watched.
    // - At each campaign start in a session: Realm of Thrones reads ROTDuelsBehavior.Instance
    //   without a null check (ROTCoreBehavior's siege menu, three DuelPatches on every battle
    //   encounter), so an inert ROTDuelsBehavior goes there. It is never registered, so it runs
    //   nothing. A duel or raven behavior left in ROT.SubModule's statics by an earlier single
    //   player campaign is dropped.
    // - One line when the table starts to answer, with what it pins and how many getters it
    //   covers, so the server's and a player's log compare.
    // No Harmony here (it is called before Harmony is found on the server); the patches are in
    // SettingsPatches.
    internal static class SettingsPins
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int PollMilliseconds = 1000;

        private static bool _serverProcess;
        private static volatile bool _session;
        private static volatile bool _polled;
        private static int _lastPoll;
        private static bool _lineLogged;
        private static int _covered = -1;
        private static List<string> _missing = new List<string>();

        public static void Install(bool server)
        {
            _serverProcess = server;
            if (server)
            {
                // The server's resolver may not be in yet; this module's server branch registers it too.
                ServerHarmony.Register();
                if (!ServerHarmony.Available())
                {
                    SubModule.Log("Settings: Harmony was not found, so Realm of Thrones' settings are not held to the co-op table on this server.");
                    return;
                }
            }
            Patch(server);
            // After the patches' own campaign start hook, so a getter they found late is in first.
            Coop.SessionChanged += OnSessionChanged;
            Coop.CampaignBegan += OnCampaignBegan;
        }

        // Kept apart so this class is compiled without Harmony.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Patch(bool server) => SettingsPatches.Install(server);

        // The table answers: on the dedicated server always, elsewhere inside a Coop session.
        internal static bool Active
        {
            get
            {
                if (_serverProcess)
                    return true;
                if (!Coop.SessionEdgesHooked)
                {
                    var now = Environment.TickCount;
                    if (!_polled || unchecked(now - _lastPoll) >= PollMilliseconds)
                    {
                        _lastPoll = now;
                        _polled = true;
                        _session = InSessionNow();
                    }
                }
                return _session;
            }
        }

        // Called by SettingsPatches once the getters are patched, or once their setup failed; the
        // first call wins.
        internal static void Covered(int covered, List<string> missing)
        {
            if (_covered >= 0)
                return;
            _covered = covered;
            _missing = missing ?? new List<string>();
        }

        private static bool InSessionNow()
        {
            try { return Coop.InSession; }
            catch { return false; }
        }

        private static void OnSessionChanged(bool started)
        {
            try
            {
                _session = started;
                if (started)
                {
                    LogLine();
                    return;
                }
                if (_lineLogged && !_serverProcess)
                    SubModule.Log("Settings: the Coop session ended, so Realm of Thrones' settings read this PC's own MCM values again.");
                _lineLogged = false;
            }
            catch (Exception ex)
            {
                SubModule.Log("Settings: the session edge could not be handled: " + ex.Message);
            }
        }

        private static void OnCampaignBegan()
        {
            if (!_serverProcess && !Coop.SessionEdgesHooked)
                _session = InSessionNow();
            if (!Active)
                return;
            LogLine();
            DuelsAndRavensOff();
        }

        private static void LogLine()
        {
            if (_lineLogged || _covered < 0)
                return;
            _lineLogged = true;
            SubModule.Log(SettingsRules.SessionLine(_serverProcess ? "server" : "player's PC", _covered, _missing, ReadsNow()));
        }

        // What Realm of Thrones itself reads now for the three switches the host and ROT.SubModule
        // decide by, through the patched getters.
        private static string ReadsNow()
        {
            try
            {
                var settings = Rot()?.GetType(SettingsRules.SettingsType, false);
                if (settings == null)
                    return null;
                return string.Join(", ", new[] { "Duels", "Ravens", "RBMIntegration" }.Select(name =>
                    name + " " + SettingsRules.Text(settings.GetProperty(name, AnyStatic)?.GetValue(null, null))));
            }
            catch (Exception ex)
            {
                return "unreadable (" + ex.GetType().Name + ")";
            }
        }

        // An inert ROTDuelsBehavior in ROTDuelsBehavior.Instance whenever duels read off or it is
        // empty, and no stale duel or raven behavior in ROT.SubModule when the settings say off.
        private static void DuelsAndRavensOff()
        {
            try
            {
                var rot = Rot();
                var settings = rot?.GetType(SettingsRules.SettingsType, false);
                var subModule = rot?.GetType(SettingsRules.RotSubModuleType, false);
                if (settings == null || subModule == null)
                {
                    SubModule.Log("Settings: ROT.ROTSettings or ROT.SubModule is not loaded, so duels and ravens are not checked in this campaign.");
                    return;
                }
                var notes = new List<string>();
                var duelsOff = Equals(settings.GetProperty("Duels", AnyStatic)?.GetValue(null, null), false);
                if (duelsOff && Drop(subModule, "DuelsBehavior"))
                    notes.Add("a duel behavior left from an earlier campaign was dropped");
                var duels = rot.GetType(SettingsRules.DuelsBehaviorType, true);
                var instance = duels.GetField("Instance", AnyStatic);
                // With duels read on and a real behavior in Instance it stays; an empty Instance
                // gets the inert one either way, so a battle encounter never throws.
                if (duelsOff || instance?.GetValue(null) == null)
                {
                    // Its constructor only stores itself in Instance.
                    var inert = Activator.CreateInstance(duels, true);
                    notes.Insert(0, instance != null && ReferenceEquals(instance.GetValue(null), inert)
                        ? "an inert ROTDuelsBehavior is in ROTDuelsBehavior.Instance (never registered, it runs nothing)"
                        : "!!! ROTDuelsBehavior.Instance DID NOT TAKE THE INERT DUEL BEHAVIOR, so a battle encounter can throw");
                }
                if (!duelsOff)
                    notes.Add("!!! DUELS READ ON, so duels are not switched off in this campaign");
                if (Equals(settings.GetProperty("Ravens", AnyStatic)?.GetValue(null, null), false))
                {
                    if (Drop(subModule, "RavenBehavior"))
                        notes.Add("a raven behavior left from an earlier campaign was dropped");
                }
                else
                {
                    notes.Add("!!! RAVENS READ ON, so ravens are not switched off in this campaign");
                }
                SubModule.Log("Settings: duels and ravens in this co-op campaign: " + string.Join("; ", notes) + ".");
            }
            catch (Exception ex)
            {
                SubModule.Log("Settings: duels and ravens could not be switched off in this campaign: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Empties a static behavior field of ROT.SubModule; true when it held one.
        private static bool Drop(Type subModule, string name)
        {
            var field = subModule.GetField(name, AnyStatic);
            if (field?.GetValue(null) == null)
                return false;
            field.SetValue(null, null);
            return true;
        }

        private static Assembly Rot() =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => !assembly.IsDynamic && Name(assembly) == "ROT");

        private static string Name(Assembly assembly)
        {
            try { return assembly.GetName().Name; }
            catch { return null; }
        }
    }
}
