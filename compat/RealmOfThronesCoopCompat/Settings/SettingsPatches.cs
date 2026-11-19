using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;

namespace RealmOfThronesCoopCompat
{
    // The Harmony half of SettingsPins. A prefix on each covered getter of ROT.ROTSettings (and on
    // RoTElephantSettings.elephantAttackChance) answers the table's value while SettingsPins.Active
    // and lets the getter run otherwise. The values are looked up by the getter's metadata token in
    // tables filled before anything is patched and never changed after, so any thread may read
    // them. An offline check on .NET 6 with the server's Harmony and on .NET Framework with the
    // game's showed these prefixes still answer after the readers were compiled again (tiered JIT)
    // and called 100,000 times. On players' PCs it also blocks the raven send in a session:
    // RavenManager.CanSendRavenWithCost says no with one short reason (the encyclopedia's raven
    // button reads it), and the encyclopedia mixin's SendRaven does nothing but say so, because the
    // mixin may be registered by a single player campaign played earlier in this game run. The
    // server has no raven behavior and no encyclopedia, so it gets no raven patch. Types are found
    // by name once ROT.dll is loaded, and asked for a last time at campaign start.
    internal static class SettingsPatches
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _server;
        private static bool _tried;
        private static Dictionary<int, bool> _bools = new Dictionary<int, bool>();
        private static Dictionary<int, int> _ints = new Dictionary<int, int>();
        private static Dictionary<int, float> _floats = new Dictionary<int, float>();
        private static object _ravensOffReason;
        private static bool _sendRavenLogged;

        public static void Install(bool server)
        {
            _server = server;
            _harmony = new Harmony(MapRedirect.HarmonyId);
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly, false);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly, false);
            Coop.CampaignBegan += () =>
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    TryPatch(assembly, true);
                RetryGetters();
                lock (Gate)
                {
                    if (_tried)
                        return;
                    _tried = true;
                }
                // The server's host at off or probe does not load ROT.dll; then nothing reads these settings.
                SubModule.Log("Settings: a campaign started without ROT.dll loaded, so there are no Realm of Thrones settings to hold here.");
                SettingsPins.Covered(0, new List<string>());
            };
        }

        private static void TryPatch(Assembly assembly, bool last)
        {
            try
            {
                if (_tried || assembly == null || assembly.IsDynamic || assembly.GetName().Name != "ROT")
                    return;
                Type settings;
                try { settings = assembly.GetType(SettingsRules.SettingsType, false); }
                catch { settings = null; }
                if (settings == null && !last)
                    return;
                lock (Gate)
                {
                    if (_tried)
                        return;
                    _tried = true;
                }
                if (settings == null)
                {
                    SubModule.Log("Settings: a campaign started but " + SettingsRules.SettingsType + " does not load, so Realm of Thrones' settings are not held to the co-op table here.");
                    SettingsPins.Covered(0, SettingsRules.PatchedRows.Select(row => row.Name).ToList());
                    return;
                }
                PatchGetters(assembly, settings, last);
                if (!_server)
                    PatchRavens(assembly);
            }
            catch (Exception ex)
            {
                SubModule.Log("Settings: Realm of Thrones' settings could not be held to the co-op table: " + ex.Message);
                // There is no retry, so the session line still says the table is not in.
                if (_tried)
                    SettingsPins.Covered(0, new List<string> { "every getter (the setup failed with " + ex.GetType().Name + ")" });
            }
        }

        // A getter Harmony could not patch yet is asked again at campaign start (RetryGetters), and
        // the count goes to SettingsPins once, after that last try.
        private static readonly List<KeyValuePair<SettingsRules.Row, MethodInfo>> Retry = new List<KeyValuePair<SettingsRules.Row, MethodInfo>>();
        private static List<string> _missing = new List<string>();
        private static int _covered;

        private static void PatchGetters(Assembly rot, Type settings, bool last)
        {
            // Every getter and value first, so the tables are whole before the first prefix reads them.
            var bools = new Dictionary<int, bool>();
            var ints = new Dictionary<int, int>();
            var floats = new Dictionary<int, float>();
            var getters = new List<KeyValuePair<SettingsRules.Row, MethodInfo>>();
            var missing = new List<string>();
            foreach (var row in SettingsRules.PatchedRows)
            {
                try
                {
                    var type = row.Type == SettingsRules.SettingsType ? settings : rot.GetType(row.Type, false);
                    var flags = row.Type == SettingsRules.SettingsType ? AnyStatic : BindingFlags.Instance | BindingFlags.Public;
                    var getter = type?.GetProperty(row.Name, flags)?.GetGetMethod(true);
                    if (getter == null || getter.ReturnType != row.Value.GetType())
                    {
                        missing.Add(row.Name + (getter == null ? " (not found)" : " (returns " + getter.ReturnType.Name + ")"));
                        continue;
                    }
                    switch (row.Value)
                    {
                        case bool value: bools[getter.MetadataToken] = value; break;
                        case int value: ints[getter.MetadataToken] = value; break;
                        case float value: floats[getter.MetadataToken] = value; break;
                    }
                    getters.Add(new KeyValuePair<SettingsRules.Row, MethodInfo>(row, getter));
                }
                catch (Exception ex)
                {
                    missing.Add(row.Name + " (" + ex.GetType().Name + ")");
                }
            }
            _bools = bools;
            _ints = ints;
            _floats = floats;

            _missing = missing;
            foreach (var pair in getters)
            {
                if (!PatchGetter(pair, last))
                    Retry.Add(pair);
            }
            if (Retry.Count == 0)
                Report();
            else
                SubModule.Log("Settings: " + _covered + " of " + SettingsRules.PatchedRows.Count() + " Realm of Thrones settings getters patched, " + Retry.Count
                    + " are asked again at campaign start (" + string.Join(", ", Retry.Select(pair => pair.Key.Name)) + ").");
        }

        private static bool PatchGetter(KeyValuePair<SettingsRules.Row, MethodInfo> pair, bool last)
        {
            try
            {
                var prefix = pair.Key.Value is bool ? nameof(BoolPrefix) : pair.Key.Value is int ? nameof(IntPrefix) : nameof(FloatPrefix);
                _harmony.Patch(pair.Value, prefix: new HarmonyMethod(typeof(SettingsPatches), prefix));
                _covered++;
                return true;
            }
            catch (Exception ex)
            {
                if (!last)
                    return false;
                _missing.Add(pair.Key.Name + " (" + ex.GetType().Name + ")");
                return true;
            }
        }

        private static void RetryGetters()
        {
            try
            {
                List<KeyValuePair<SettingsRules.Row, MethodInfo>> retry;
                lock (Gate)
                {
                    if (Retry.Count == 0)
                        return;
                    retry = Retry.ToList();
                    Retry.Clear();
                }
                foreach (var pair in retry)
                    PatchGetter(pair, true);
                Report();
            }
            catch (Exception ex)
            {
                SubModule.Log("Settings: the getters could not be asked again: " + ex.Message);
            }
        }

        private static void Report()
        {
            SettingsPins.Covered(_covered, _missing);
            SubModule.Log("Settings: " + _covered + " of " + SettingsRules.PatchedRows.Count() + " Realm of Thrones settings getters answer from the co-op table in a Coop session"
                + (_missing.Count == 0 ? "" : " (" + (_missing.Count == 1 ? "NOT covered: " : "NOT covered (" + _missing.Count + "): ") + string.Join(", ", _missing) + ")")
                + "; outside a session they read MCM as always.");
        }

        private static void PatchRavens(Assembly rot)
        {
            var blocked = new List<string>();
            try
            {
                var check = rot.GetType(SettingsRules.RavenManagerType, false)?.GetMethod("CanSendRavenWithCost", AnyStatic);
                var reason = check?.GetParameters().Length == 2 ? check.GetParameters()[1].ParameterType.GetElementType() : null;
                var constructor = reason?.GetConstructor(new[] { typeof(string), typeof(Dictionary<string, object>) });
                if (constructor == null)
                    SubModule.Log("Settings: " + SettingsRules.RavenManagerType + ".CanSendRavenWithCost(Hero, out TextObject) not found, so the raven button is not blocked here.");
                else
                {
                    _ravensOffReason = constructor.Invoke(new object[] { SettingsRules.RavensOffText, null });
                    _harmony.Patch(check, prefix: new HarmonyMethod(typeof(SettingsPatches), nameof(RavenCheckPrefix)));
                    blocked.Add("the raven check");
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Settings: the raven check could not be blocked: " + ex.Message);
            }
            try
            {
                Type mixin;
                try { mixin = rot.GetType(SettingsRules.RavenMixinType, false); }
                catch { mixin = null; }
                var send = mixin?.GetMethod("SendRaven", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (send == null)
                    SubModule.Log("Settings: " + SettingsRules.RavenMixinType + ".SendRaven not found, so the encyclopedia's raven send is not blocked here.");
                else
                {
                    _harmony.Patch(send, prefix: new HarmonyMethod(typeof(SettingsPatches), nameof(SendRavenPrefix)));
                    blocked.Add("the encyclopedia's send");
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Settings: the encyclopedia's raven send could not be blocked: " + ex.Message);
            }
            if (blocked.Count > 0)
                SubModule.Log("Settings: ravens are blocked on this PC in a Coop session (" + string.Join(" and ", blocked) + ").");
        }

        private static bool BoolPrefix(MethodBase __originalMethod, ref bool __result)
        {
            try
            {
                if (!SettingsPins.Active || !_bools.TryGetValue(__originalMethod.MetadataToken, out var value))
                    return true;
                __result = value;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool IntPrefix(MethodBase __originalMethod, ref int __result)
        {
            try
            {
                if (!SettingsPins.Active || !_ints.TryGetValue(__originalMethod.MetadataToken, out var value))
                    return true;
                __result = value;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool FloatPrefix(MethodBase __originalMethod, ref float __result)
        {
            try
            {
                if (!SettingsPins.Active || !_floats.TryGetValue(__originalMethod.MetadataToken, out var value))
                    return true;
                __result = value;
                return false;
            }
            catch
            {
                return true;
            }
        }

        // CanSendRavenWithCost(Hero targetHero, out TextObject exception): the reason goes back
        // through Harmony's argument array.
        private static bool RavenCheckPrefix(object[] __args, ref bool __result)
        {
            try
            {
                if (!SettingsPins.Active)
                    return true;
                __args[1] = _ravensOffReason;
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool SendRavenPrefix()
        {
            try
            {
                if (!SettingsPins.Active)
                    return true;
                InformationManager.DisplayMessage(new InformationMessage(SettingsRules.RavensOffText));
                if (!_sendRavenLogged)
                {
                    _sendRavenLogged = true;
                    SubModule.Log("Settings: a raven send from the encyclopedia was blocked (ravens are off in co-op).");
                }
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
