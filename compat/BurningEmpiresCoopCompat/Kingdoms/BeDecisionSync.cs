using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires adds two kingdom decisions: change the state religion and change the
    // mainstream culture. Coop only syncs decision types a mod registers with its
    // KingdomDecisionTypeRegistry, so this file:
    // - registers both types on every PC (server and players), sending the target religion or
    //   culture id; Coop refuses a join when the lists differ, so every player needs this patch;
    // - on a player's PC, when the server resolves an approved religion decision, writes the
    //   new state religion straight into Burning Empires' data. It never calls
    //   TrySetStateReligion there, which would run gender enforcement on the player's PC.
    //   The culture itself reaches players through Coop's kingdom sync; only the screen refresh
    //   is raised here.
    // The registry came after Coop 0.1.6. With an older Coop nothing is registered, Registered
    // stays false and the decisions stay greyed out.
    internal static class BeDecisionSync
    {
        private const string Government = "BurningEmpires.BESystems.Government.";
        private const string RegistryName = "GameInterface.Services.Kingdoms.KingdomDecisionTypeRegistry";

        private static readonly object Gate = new object();
        // Target id field of each registered decision type, for the writer and the reader.
        private static readonly Dictionary<Type, FieldInfo> TargetFields = new Dictionary<Type, FieldInfo>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static Assembly _burningEmpires;
        private static bool _registered;
        private static bool _done;
        private static bool _waitingForCoop;

        private static Type _religionDecision;
        private static Type _cultureDecision;
        private static PropertyInfo _religionTarget;
        private static PropertyInfo _religionApproval;
        private static PropertyInfo _cultureApproval;
        private static MethodInfo _worldDataCurrent;
        private static PropertyInfo _kingdomReligions;
        private static MethodInfo _isFormalReligion;
        private static FieldInfo _stateReligionChanged;
        private static FieldInfo _mainstreamCultureChanged;

        // True once both Burning Empires decisions are synced by Coop.
        public static bool Registered => _registered;

        // Call from OnSubModuleLoad or when Burning Empires loads, on the server and on players'
        // PCs. Safe to call more than once. When Coop's GameInterface is not loaded yet, the
        // registration waits for it.
        public static void Register(Assembly burningEmpires)
        {
            if (burningEmpires == null)
                return;
            lock (Gate)
            {
                if (_done)
                    return;
                _burningEmpires = burningEmpires;
            }

            var registry = FindRegistry(out var coopLoaded);
            if (registry != null)
            {
                RegisterWith(registry);
                return;
            }
            if (coopLoaded)
            {
                lock (Gate)
                    _done = true;
                SubModule.Log("This Coop has no kingdom decision registry (it came after Coop 0.1.6); Burning Empires' religion and culture decisions stay greyed out.");
                return;
            }

            lock (Gate)
            {
                if (_waitingForCoop)
                    return;
                _waitingForCoop = true;
            }
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
        {
            string name;
            try { name = e.LoadedAssembly.GetName().Name ?? ""; }
            catch { return; }
            if (name != "GameInterface")
                return;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            lock (Gate)
                _waitingForCoop = false;
            Register(_burningEmpires);
        }

        // coopLoaded is true when Coop's GameInterface is loaded, with or without the registry.
        private static Type FindRegistry(out bool coopLoaded)
        {
            coopLoaded = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = assembly.GetName().Name ?? ""; }
                catch { continue; }
                if (name != "GameInterface")
                    continue;
                coopLoaded = true;
                Type type = null;
                try { type = assembly.GetType(RegistryName, false); }
                catch { }
                if (type != null)
                    return type;
            }
            return null;
        }

        private static void RegisterWith(Type registry)
        {
            lock (Gate)
            {
                if (_done)
                    return;
                _done = true;
            }

            var register = registry.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Register" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            if (register == null)
            {
                SubModule.Log("Coop's kingdom decision registry has no Register method this patch knows; Burning Empires' decisions stay greyed out.");
                return;
            }

            var count = 0;
            count += RegisterType(register, "BEStateReligionChangeDecision", "_targetReligionId");
            count += RegisterType(register, "BEStateCultureChangeDecision", "_targetCultureId");
            _registered = count == 2;
            SubModule.Log(_registered
                ? "Burning Empires' religion and culture decisions are synced through Coop."
                : "Only " + count + " of Burning Empires' 2 kingdom decisions could be synced through Coop.");
        }

        private static int RegisterType(MethodInfo register, string typeName, string fieldName)
        {
            Type type = null;
            try { type = _burningEmpires.GetType(Government + typeName, false); }
            catch { }
            var field = type == null ? null : AccessTools.Field(type, fieldName);
            if (field == null || field.FieldType != typeof(string))
            {
                SubModule.Log("Could not find Burning Empires " + typeName + "." + fieldName + "; that decision is not synced.");
                return 0;
            }

            try
            {
                lock (Gate)
                    TargetFields[type] = field;
                var write = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(type, typeof(byte[])),
                    AccessTools.Method(typeof(BeDecisionSync), nameof(Write)).MakeGenericMethod(type));
                var read = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(byte[]), type),
                    AccessTools.Method(typeof(BeDecisionSync), nameof(Read)).MakeGenericMethod(type));
                register.MakeGenericMethod(type).Invoke(null, new object[] { write, read });
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not sync " + typeName + " through Coop: " + (ex.InnerException?.Message ?? ex.Message));
                return 0;
            }
        }

        // Coop sends the proposer, kingdom and timing itself; the payload is only the target id.
        private static byte[] Write<T>(T decision) where T : KingdomDecision
        {
            FieldInfo field;
            lock (Gate)
                TargetFields.TryGetValue(typeof(T), out field);
            return EncodeTarget(field?.GetValue(decision) as string);
        }

        // Built without its constructor like Coop builds the game's own decisions; Coop then
        // fills in the proposer and kingdom.
        private static T Read<T>(byte[] payload) where T : KingdomDecision
        {
            FieldInfo field;
            lock (Gate)
                TargetFields.TryGetValue(typeof(T), out field);
            if (field == null)
                return null;
            var decision = (T)FormatterServices.GetUninitializedObject(typeof(T));
            field.SetValue(decision, DecodeTarget(payload));
            return decision;
        }

        internal static byte[] EncodeTarget(string targetId) => Encoding.UTF8.GetBytes(targetId ?? string.Empty);

        internal static string DecodeTarget(byte[] payload) =>
            payload == null || payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(payload);

        // ---- Resolved decisions on players' PCs ----

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            _religionDecision = FindType(burningEmpires, Government + "BEStateReligionChangeDecision");
            _cultureDecision = FindType(burningEmpires, Government + "BEStateCultureChangeDecision");
            var religionOutcome = FindType(burningEmpires, Government + "BEStateReligionChangeDecisionOutcome");
            var cultureOutcome = FindType(burningEmpires, Government + "BEStateCultureChangeDecisionOutcome");
            var government = FindType(burningEmpires, Government + "BEKingdomGovernmentData");
            var manager = FindType(burningEmpires, "BurningEmpires.BESystems.Core.BEWorldDataManager");
            var worldData = FindType(burningEmpires, "BurningEmpires.BESystems.Core.BEWorldData");
            var assignment = FindType(burningEmpires, "BurningEmpires.BESystems.Religion.BEReligionAssignmentService");

            _religionTarget = _religionDecision == null ? null : AccessTools.Property(_religionDecision, "TargetReligionId");
            _religionApproval = religionOutcome == null ? null : AccessTools.Property(religionOutcome, "IsApproval");
            _cultureApproval = cultureOutcome == null ? null : AccessTools.Property(cultureOutcome, "IsApproval");
            _worldDataCurrent = manager == null ? null : AccessTools.PropertyGetter(manager, "Current");
            _kingdomReligions = worldData == null ? null : AccessTools.Property(worldData, "KingdomReligions");
            _isFormalReligion = assignment == null ? null : AccessTools.Method(assignment, "IsFormalReligion", new[] { typeof(string) });
            // Static field like events keep their delegate in a field of the same name.
            _stateReligionChanged = government == null ? null : AccessTools.Field(government, "StateReligionChanged");
            _mainstreamCultureChanged = government == null ? null : AccessTools.Field(government, "MainstreamCultureChanged");

            if (_religionDecision == null || _religionTarget == null || _religionApproval == null
                || _worldDataCurrent == null || _kingdomReligions == null)
            {
                SubModule.Log("Could not find Burning Empires' state religion data; players see a new state religion only after they rejoin.");
                return 0;
            }

            var method = AccessTools.Method(typeof(CampaignEventDispatcher), "OnKingdomDecisionConcluded",
                new[] { typeof(KingdomDecision), typeof(DecisionOutcome), typeof(bool) });
            if (method == null)
            {
                SubModule.Log("Could not find CampaignEventDispatcher.OnKingdomDecisionConcluded; players see a new state religion only after they rejoin.");
                return 0;
            }
            try
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(BeDecisionSync), nameof(DecisionConcludedPostfix)));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not hook CampaignEventDispatcher.OnKingdomDecisionConcluded: " + ex.Message);
                return 0;
            }
        }

        // Coop raises this on players' PCs when the server resolves a decision; the server
        // itself already ran the decision's own outcome.
        private static void DecisionConcludedPostfix(KingdomDecision __0, DecisionOutcome __1)
        {
            if (__0 == null || __1 == null || !Coop.OnPlayerPc)
                return;
            try
            {
                var type = __0.GetType();
                if (type == _religionDecision && IsApproved(__1, _religionApproval))
                    SetStateReligion(__0.Kingdom, _religionTarget.GetValue(__0, null) as string);
                else if (type == _cultureDecision && IsApproved(__1, _cultureApproval))
                    Raise(_mainstreamCultureChanged, __0.Kingdom);
            }
            catch (Exception ex)
            {
                ReportOnce("decision", "Could not apply a Burning Empires decision on this PC: " + ex);
            }
        }

        private static bool IsApproved(DecisionOutcome outcome, PropertyInfo approval)
        {
            if (approval == null || !approval.DeclaringType.IsInstanceOfType(outcome))
                return false;
            return approval.GetValue(outcome, null) is bool approved && approved;
        }

        private static void SetStateReligion(Kingdom kingdom, string religionId)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId) || string.IsNullOrWhiteSpace(religionId))
                return;
            if (_isFormalReligion != null && !(bool)_isFormalReligion.Invoke(null, new object[] { religionId }))
            {
                ReportOnce("religion " + religionId, "The server approved state religion " + religionId + ", which this PC does not know; is Burning Empires the same version?");
                return;
            }
            var data = _worldDataCurrent.Invoke(null, null);
            if (data == null)
                return;
            var religions = _kingdomReligions.GetValue(data, null) as Dictionary<string, string>;
            if (religions == null)
            {
                religions = new Dictionary<string, string>(StringComparer.Ordinal);
                _kingdomReligions.SetValue(data, religions, null);
            }
            var changed = !religions.TryGetValue(kingdom.StringId, out var old) || !string.Equals(old, religionId, StringComparison.Ordinal);
            religions[kingdom.StringId] = religionId;
            if (changed)
                Raise(_stateReligionChanged, kingdom);
        }

        // Lets an open Government screen refresh. Burning Empires' own listeners only redraw.
        private static void Raise(FieldInfo eventField, Kingdom kingdom)
        {
            if (eventField == null || kingdom == null)
                return;
            try { (eventField.GetValue(null) as Delegate)?.DynamicInvoke(kingdom); }
            catch (Exception ex)
            {
                ReportOnce(eventField.Name, "Burning Empires' " + eventField.Name + " refresh failed: " + (ex.InnerException ?? ex).Message);
            }
        }

        // ---- Helpers ----

        private static Type FindType(Assembly burningEmpires, string fullName)
        {
            Type type = null;
            try { type = burningEmpires?.GetType(fullName, false); }
            catch { }
            type = type ?? AccessTools.TypeByName(fullName);
            if (type == null)
                SubModule.Log("Could not find Burning Empires type " + fullName + ".");
            return type;
        }

        private static void ReportOnce(string key, string text)
        {
            lock (Reported)
            {
                if (Reported.Add(key))
                    SubModule.Log(text);
            }
        }
    }
}
