#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // Finds and rewrites the two calls through which the dedicated server's map scene picks its
    // Main_map. In the server source they are MainMapExtents.ModuleFor(bool), the module to read
    // Main_map from, and MainMapExtents.For(bool), the border and terrain extents. The release
    // DedicatedServer.Core.dll is obfuscated (types and methods renamed, strings encrypted), so
    // nothing here goes by name. The extents type is the value type that has a static method
    // taking one bool and returning itself, and a constructor of six floats; the module call is
    // its static method taking one bool and returning a string.
    // The calls are not replaced. Each one stays, and our hook runs right after it on the value it
    // returned. Both methods are tiny enough for the JIT to inline, which would skip a prefix on
    // them, but rewriting the caller's own IL works whether they are inlined or not.
    // Uses Harmony types only, so the launcher tests can run it on stand in methods.
    internal static class MapRedirectRules
    {
        public const int ExtentsFloats = 6;

        public static MethodInfo Called(CodeInstruction instruction) =>
            (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) ? instruction.operand as MethodInfo : null;

        // Fields a patched copy would read wrong. The obfuscator gives several fields of one type the
        // same name (legal IL), and MonoMod, which builds Harmony's patched copy, finds a field again
        // by declaring type and name only, so it takes the first field of that name. Every transpiled
        // method is checked for these before it is patched.
        public static List<string> AmbiguousFields(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<string>();
            foreach (var instruction in instructions ?? Enumerable.Empty<CodeInstruction>())
            {
                if (!(instruction.operand is FieldInfo field) || field.DeclaringType == null)
                    continue;
                var sameName = field.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Count(f => f.Name == field.Name);
                var text = field.DeclaringType.FullName + "::" + field.Name + " (" + field.FieldType.Name + ")";
                if (sameName > 1 && !result.Contains(text))
                    result.Add(text);
            }
            return result;
        }

        public static bool TakesOneBool(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
        }

        public static bool HasExtentsConstructor(Type type) =>
            type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(ctor => ctor.GetParameters().Length == ExtentsFloats && ctor.GetParameters().All(p => p.ParameterType == typeof(float)));

        public static bool IsExtentsCall(MethodInfo method) =>
            method != null && method.IsStatic && method.DeclaringType != null && method.DeclaringType.IsValueType
            && method.ReturnType == method.DeclaringType && TakesOneBool(method) && HasExtentsConstructor(method.DeclaringType);

        public static bool IsModuleCall(MethodInfo method, Type extentsType) =>
            method != null && method.IsStatic && extentsType != null && method.DeclaringType == extentsType
            && method.ReturnType == typeof(string) && TakesOneBool(method);

        // The extents type the method's calls name, or null when there is no such call (or more
        // than one candidate, which would mean the server changed and a guess could hit the
        // wrong value).
        public static Type FindExtentsType(IEnumerable<CodeInstruction> instructions)
        {
            var types = instructions.Select(Called).Where(IsExtentsCall).Select(method => method.DeclaringType).Distinct().ToList();
            return types.Count == 1 ? types[0] : null;
        }

        // The extents constructor, six floats in the record's order (border min x, min y, max x,
        // max y, max height, terrain size).
        public static ConstructorInfo ExtentsConstructor(Type extentsType) =>
            extentsType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(ctor => ctor.GetParameters().Length == ExtentsFloats && ctor.GetParameters().All(p => p.ParameterType == typeof(float)));

        // Adds moduleHook (string -> string) after every module call and extentsHook
        // (object -> object, the boxed extents) after every extents call. A null hook leaves those
        // calls alone. Labels and exception blocks stay on the original call.
        public static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, Type extentsType,
            MethodInfo moduleHook, MethodInfo extentsHook, out int moduleCalls, out int extentsCalls)
        {
            moduleCalls = 0;
            extentsCalls = 0;
            var result = new List<CodeInstruction>();
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                var method = Called(instruction);
                if (method == null || extentsType == null)
                    continue;
                if (moduleHook != null && IsModuleCall(method, extentsType))
                {
                    result.Add(new CodeInstruction(OpCodes.Call, moduleHook));
                    moduleCalls++;
                }
                else if (extentsHook != null && method.DeclaringType == extentsType && IsExtentsCall(method))
                {
                    result.Add(new CodeInstruction(OpCodes.Box, extentsType));
                    result.Add(new CodeInstruction(OpCodes.Call, extentsHook));
                    result.Add(new CodeInstruction(OpCodes.Unbox_Any, extentsType));
                    extentsCalls++;
                }
            }
            return result;
        }

        // What to patch in one server build, found and dry run before anything is patched, so a
        // changed nightly leaves the server whole instead of half redirected. Problem is null when
        // every part checks out. Install stays null when the build has no naval installer at all,
        // or when War Sails is off: the installer then returns before it reads any naval file, so
        // a reshaped installer must not stop the map redirect.
        internal sealed class Plan
        {
            public MethodInfo Load;
            public MethodInfo Install;
            public Type ExtentsType;
            public ConstructorInfo ExtentsConstructor;
            public int LoadModuleCalls;
            public int LoadExtentsCalls;
            public int InstallModuleCalls;
            public string Problem;
        }

        public static Plan MakePlan(IEnumerable<Type> serverTypes, Type mapSceneBase, Type installerType, Type gameType, bool warSails,
            MethodInfo moduleHook, MethodInfo navalHook, MethodInfo extentsHook)
        {
            var plan = new Plan();
            var scenes = serverTypes.Where(type => type != null && type.IsClass && !type.IsAbstract && type != mapSceneBase && mapSceneBase.IsAssignableFrom(type)).ToList();
            if (scenes.Count != 1)
                return Fail(plan, "found " + scenes.Count + " map scene types instead of 1");
            plan.Load = scenes[0].GetMethod("Load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (plan.Load == null)
                return Fail(plan, "map scene type " + scenes[0].FullName + " has no Load()");

            var load = PatchProcessor.GetOriginalInstructions(plan.Load);
            var ambiguous = AmbiguousFields(load);
            if (ambiguous.Count > 0)
                return Fail(plan, scenes[0].FullName + ".Load reads fields a patched copy would mistake for another of the same name (" + string.Join(", ", ambiguous) + ")");
            plan.ExtentsType = FindExtentsType(load);
            plan.ExtentsConstructor = ExtentsConstructor(plan.ExtentsType);
            if (plan.ExtentsType == null || plan.ExtentsConstructor == null)
                return Fail(plan, "no extents call found in " + scenes[0].FullName + ".Load");
            Rewrite(load, plan.ExtentsType, moduleHook, extentsHook, out plan.LoadModuleCalls, out plan.LoadExtentsCalls);
            if (plan.LoadModuleCalls == 0 || plan.LoadExtentsCalls == 0)
                return Fail(plan, scenes[0].FullName + ".Load has " + plan.LoadModuleCalls + " module calls and " + plan.LoadExtentsCalls + " extents calls");

            if (installerType == null || !warSails)
                return plan;
            plan.Install = installerType.GetMethod("Install", BindingFlags.Static | BindingFlags.Public, null, new[] { gameType }, null);
            if (plan.Install == null)
                return Fail(plan, installerType.FullName + ".Install(Game) not found");
            var install = PatchProcessor.GetOriginalInstructions(plan.Install);
            ambiguous = AmbiguousFields(install);
            if (ambiguous.Count > 0)
                return Fail(plan, installerType.FullName + ".Install reads fields a patched copy would mistake for another of the same name (" + string.Join(", ", ambiguous) + ")");
            Rewrite(install, plan.ExtentsType, navalHook, null, out plan.InstallModuleCalls, out _);
            if (plan.InstallModuleCalls == 0)
                return Fail(plan, "no module call found in " + installerType.FullName + ".Install");
            return plan;
        }

        private static Plan Fail(Plan plan, string problem)
        {
            plan.Problem = problem;
            return plan;
        }

        // Applies every step or none. A step that throws is undone itself, since a Harmony patch can
        // fail after it is already registered on the method, and then the ones before it are undone,
        // newest first. Null when all applied, else the line to log. It names any step that could
        // not be undone, since that is the one case where the server ends up half redirected.
        public static string ApplyAll(IList<(string Name, Action Apply, Action Undo)> steps)
        {
            var done = new List<(string Name, Action Apply, Action Undo)>();
            foreach (var step in steps)
            {
                try
                {
                    step.Apply();
                    done.Add(step);
                }
                catch (Exception ex)
                {
                    var stuck = new List<string>();
                    try { step.Undo(); }
                    catch { stuck.Add(step.Name); }
                    for (var i = done.Count - 1; i >= 0; i--)
                    {
                        try { done[i].Undo(); }
                        catch { stuck.Add(done[i].Name); }
                    }
                    var line = step.Name + " could not be patched (" + ex.GetType().Name + ": " + ex.Message + ")";
                    return stuck.Count == 0
                        ? line + "; nothing stays patched, so the server keeps its own map"
                        : line + "; HALF APPLIED, still patched and could not be undone: " + string.Join(", ", stuck);
                }
            }
            return null;
        }
    }

    // What the rewritten calls do at run time, and the switch that turns them on. They hand back
    // the server's own value until every patch is in, so a rewrite left behind by a failed undo
    // cannot mix Realm of Thrones' map with War Sails' parts. ApplyAndActivate is the only way to
    // turn them on. It runs once at module load and the map loads much later, so they never race.
    // No game types, so the launcher tests run these exact guards.
    internal sealed class RedirectHooks
    {
        private readonly Action<string> _log;
        private volatile bool _active;
        private bool _loggedModule;
        private bool _loggedNaval;
        private bool _loggedExtents;

        public RedirectHooks(Action<string> log)
        {
            _log = log;
        }

        public MapData Data { get; set; }
        public ConstructorInfo ExtentsConstructor { get; set; }
        public bool Active => _active;

        // Applies every step (MapRedirectRules.ApplyAll) and turns the hooks on only when all went in.
        public string ApplyAndActivate(IList<(string Name, Action Apply, Action Undo)> steps)
        {
            var failure = MapRedirectRules.ApplyAll(steps);
            if (failure == null)
                _active = true;
            return failure;
        }

        // Runs in the map scene's Load right after the server picked its Main_map module.
        public string MapModule(string original)
        {
            var data = Data;
            if (data == null || !_active)
                return original;
            if (!_loggedModule)
            {
                _loggedModule = true;
                _log("Map redirect: the server asked for Main_map from " + (original ?? "its own module") + ", reading it from " + data.Module + ".");
            }
            return data.Module;
        }

        // Runs in the naval installer right after it picked the module of its points and wind map.
        public string NavalModule(string original)
        {
            var data = Data;
            if (data == null || !_active)
                return original;
            if (!_loggedNaval)
            {
                _loggedNaval = true;
                _log("Map redirect: War Sails' naval points and wind map come from " + data.Module + " instead of " + original + ".");
            }
            return data.Module;
        }

        // Runs in the map scene's Load right after the server picked its extents, boxed.
        public object MapExtents(object original)
        {
            var data = Data;
            var constructor = ExtentsConstructor;
            if (data == null || !_active || constructor == null)
                return original;
            try
            {
                var replaced = constructor.Invoke(data.ExtentsValues);
                if (!_loggedExtents)
                {
                    _loggedExtents = true;
                    _log("Map redirect: map extents set to " + data + " (the server had " + original + ").");
                }
                return replaced;
            }
            catch (Exception ex)
            {
                _log("Map redirect: could not build the map extents, so the server keeps its own: " + ex.Message);
                return original;
            }
        }
    }
}
