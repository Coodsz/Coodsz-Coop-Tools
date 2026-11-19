using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // The host's Harmony part: applies Realm of Thrones' own patches from RotHostTables under
    // Realm of Thrones' Harmony id, never with its PatchAll. A row applied whole goes through
    // Harmony's class processor, as PatchAll would do for that one class. A class whose methods
    // have rows of their own is applied one method at a time, each on the game method its
    // attributes name, and a method the table does not know is not applied. Every row has its
    // own try and its own log line, so one that fails stops nothing else.
    internal static class RotHostPatches
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly string[] Kinds = { "Prefix", "Postfix", "Transpiler", "Finalizer" };

        // NotApplied: a host row that could not be applied here for a reason the log line gives
        // (a file or a game type that is not there, a patch method the table does not know).
        private enum Outcome { Nothing, Applied, NotApplied, Failed }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Apply(Assembly rot, IList<KeyValuePair<string, string>> modules)
        {
            var harmony = new Harmony(RotHostRules.HarmonyId);
            var rows = RotHostTables.Patches;
            var outcomes = new List<Outcome>();

            // The order ROT.SubModule patches in: its first patch by hand, then its classes as
            // ROT.dll lists them (PatchAll's order), then its other patches by hand. Where two
            // of its classes prefix one game method, the one applied first runs first.
            foreach (var row in RotHostRules.HandRows(rows, beforeClasses: true))
                outcomes.Add(Guarded(row.Type + "." + row.Method, () => ApplyByHand(harmony, rot, row)));
            foreach (var type in RotHostChecks.Types(rot).Select(type => type.FullName))
            {
                var row = RotHostRules.ClassRow(rows, type);
                if (RotHostRules.IsSplit(rows, type))
                    ApplyMethods(harmony, rot, type, outcomes);
                else if (row != null && row.Decision == HostDecision.Host)
                    outcomes.Add(Guarded(type, () => ApplyClass(harmony, rot, row, modules)));
            }
            foreach (var row in RotHostRules.HandRows(rows, beforeClasses: false))
                outcomes.Add(Guarded(row.Type + "." + row.Method, () => ApplyByHand(harmony, rot, row)));

            foreach (var row in rows.Where(row => row.Decision == HostDecision.Later))
                SubModule.Log("Host: not hosted yet: " + Name(row) + ": " + row.Why + ".");
            var skipped = rows.Where(row => row.Decision == HostDecision.Skip).ToList();
            // Class names without their namespace keep this one line readable.
            SubModule.Log("Host: patches skipped by the table (" + skipped.Count + "): "
                + string.Join(", ", skipped.Select(row => row.Type.Substring(row.Type.LastIndexOf('.') + 1) + (row.Method == null ? "" : "." + row.Method))) + ".");
            var failedCount = outcomes.Count(o => o == Outcome.Failed);
            SubModule.Log((failedCount > 0 ? RotHostRules.Loud : "Host: ") + "patches: " + outcomes.Count(o => o == Outcome.Applied) + " applied, " + outcomes.Count(o => o == Outcome.Failed) + " failed, "
                + outcomes.Count(o => o == Outcome.NotApplied) + " not applied, " + rows.Count(row => row.Decision == HostDecision.Later) + " not hosted yet, "
                + skipped.Count + " skipped by the table.");
            return failedCount;
        }

        // A hosted patch class's static constructor runs before the class is patched, inside its
        // guarded step: GameTextsPatches builds a field reference in one, and if that failed every
        // GameTexts.FindText would throw in the middle of a game. A failure counts as a failed patch.
        private static void RunClassConstructor(Type type) => RuntimeHelpers.RunClassConstructor(type.TypeHandle);

        // A behavior that is not hosted but stores data stays registered, so the game saves and
        // loads that data, with this module's own prefix on its RegisterEvents so that nothing
        // else of it ever runs on the server. Returns the types whose events are blocked; one
        // that could not be blocked must not be registered.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static HashSet<string> BlockEvents(Assembly rot)
        {
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var harmony = new Harmony(MapRedirect.HarmonyId);
            var prefix = new HarmonyMethod(typeof(RotHostPatches).GetMethod(nameof(NoEvents), AnyStatic));
            foreach (var row in RotHostTables.BehaviorRows.Where(row => row.Inert))
            {
                Guarded(row.Type + ".RegisterEvents", () =>
                {
                    var registerEvents = AccessTools.DeclaredMethod(rot.GetType(row.Type, true), "RegisterEvents", Type.EmptyTypes) ?? throw new MissingMethodException(row.Type, "RegisterEvents");
                    harmony.Patch(registerEvents, prefix);
                    blocked.Add(row.Type);
                    return Outcome.Applied;
                });
            }
            var inert = RotHostTables.BehaviorRows.Count(row => row.Inert);
            SubModule.Log((blocked.Count < inert ? RotHostRules.Loud : "Host: ") + "RegisterEvents is blocked for " + blocked.Count + " of " + inert
                + " behaviors that are registered only for their saved data" + (blocked.Count < inert ? "; the others are not registered and their saved data is lost." : "."));
            return blocked;
        }

        private static bool NoEvents() => false;

        // BannerColorPersistence.dll sits next to ROT.dll. Its SubModule never loads on the
        // server, so its settings are read here; without them its prefix would throw on every call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ApplyBanner(Assembly rot)
        {
            var path = Path.Combine(Path.GetDirectoryName(rot.Location) ?? "", "BannerColorPersistence.dll");
            var banner = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "BannerColorPersistence");
            if (banner == null && !File.Exists(path))
            {
                SubModule.Log("Host: BannerColorPersistence.dll not found next to ROT.dll, so its banner colour patches are not applied.");
                return 1;
            }
            banner = banner ?? Assembly.LoadFrom(path);
            var subModule = banner.GetType(RotHostTables.BannerSubModuleType, true);
            var config = subModule.GetProperty("Config", AnyStatic);
            if (config.GetValue(null, null) == null)
                subModule.GetMethod("LoadConfig", AnyStatic).Invoke(null, null);
            if (config.GetValue(null, null) == null)
            {
                SubModule.Log("Host: BannerColorPersistence has no settings (config.json next to it is missing or unreadable), so its banner colour patches are not applied.");
                return 1;
            }
            // Its settings decide what the banner prefix does. They are read with Newtonsoft.Json,
            // and a copy of that DLL sits next to ROT.dll, so the line says which one was used.
            var settings = config.GetValue(null, null);
            var json = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "Newtonsoft.Json");
            SubModule.Log("Host: BannerColorPersistence's settings: " + string.Join(", ", settings.GetType().GetProperties().Select(property => property.Name + " " + property.GetValue(settings, null)))
                + " (Newtonsoft.Json " + (json == null ? "not loaded" : json.GetName().Version + " from " + json.Location) + ").");
            var failed = 0;
            var harmony = new Harmony(RotHostTables.BannerHarmonyId);
            foreach (var row in RotHostTables.BannerPatches.Where(row => row.Decision == HostDecision.Host))
            {
                if (Guarded(row.Type, () =>
                {
                    var type = banner.GetType(row.Type, true);
                    RunClassConstructor(type);
                    harmony.CreateClassProcessor(type).Patch();
                    SubModule.Log("Host: patch applied: " + row.Type + " (" + row.Why + ").");
                    return Outcome.Applied;
                }) == Outcome.Failed)
                    failed++;
            }
            return failed;
        }

        // For every game method Realm of Thrones and somebody else both patch (Coop, the server,
        // this module): who patches it, and each kind in the order Harmony runs it, which for
        // prefixes decides whose can skip the others. Run once Coop's patches are in.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void LogSharedTargets()
        {
            var ours = new[] { RotHostRules.HarmonyId, RotHostTables.BannerHarmonyId };
            // Coop's own test for a method so small that a detour damages it and can freeze the
            // process (GameInterface.Utils.FragileDetourGuard). Coop's guard does not cover a
            // method with a prefix or postfix, and Realm of Thrones postfixes some one line getters.
            var isFragile = Coop.FindType("GameInterface.Utils.FragileDetourGuard")?.GetMethod("IsFragile", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(MethodBase) }, null);
            var fragile = new List<string>();
            int targets = 0, shared = 0;
            foreach (var original in Harmony.GetAllPatchedMethods().ToList())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null)
                    continue;
                var all = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).ToList();
                if (!all.Any(patch => ours.Contains(patch.owner)))
                    continue;
                targets++;
                if (isFragile != null && (bool)isFragile.Invoke(null, new object[] { original }))
                    fragile.Add(Signature(original));
                if (all.All(patch => ours.Contains(patch.owner)))
                    continue;
                shared++;
                SubModule.Log("Host: shared target " + Signature(original) + ": prefixes " + Order(info.Prefixes) + "; postfixes " + Order(info.Postfixes)
                    + "; transpilers " + Order(info.Transpilers) + "; finalizers " + Order(info.Finalizers) + ".");
            }
            SubModule.Log("Host: " + shared + " of the " + targets + " game methods Realm of Thrones patches here are also patched by others.");
            SubModule.Log(isFragile == null
                ? "Host: Coop's test for methods too small to detour was not found, so the patched methods were not tested."
                : fragile.Count == 0
                    ? "Host: none of those " + targets + " game methods is too small to detour (Coop's own test)."
                    : RotHostRules.Loud + fragile.Count + " PATCHED GAME METHODS ARE TOO SMALL TO DETOUR (Coop's own test) and can freeze the server without an error: " + string.Join(", ", fragile) + ".");
        }

        // "1. owner (priority 400) Type.Method, 2. ..." by priority, then by who patched first.
        private static string Order(IEnumerable<Patch> patches)
        {
            var sorted = patches.OrderByDescending(patch => patch.priority).ThenBy(patch => patch.index).ToList();
            if (sorted.Count == 0)
                return "none";
            return string.Join(", ", sorted.Select((patch, i) => (i + 1) + ". " + patch.owner + " (priority " + patch.priority
                + (patch.before != null && patch.before.Length > 0 ? ", before " + string.Join("/", patch.before) : "")
                + (patch.after != null && patch.after.Length > 0 ? ", after " + string.Join("/", patch.after) : "")
                + ") " + patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name));
        }

        private static Outcome ApplyClass(Harmony harmony, Assembly rot, PatchRow row, IList<KeyValuePair<string, string>> modules)
        {
            if (row.NeedsFile != null)
            {
                var parts = row.NeedsFile.Split('|');
                var folder = modules.FirstOrDefault(module => string.Equals(module.Key, parts[0], StringComparison.OrdinalIgnoreCase)).Value;
                if (folder == null || !File.Exists(Path.Combine(folder, parts[1])))
                {
                    SubModule.Log("Host: patch not applied: " + row.Type + " reads " + parts[1] + " of module " + parts[0] + " without a check, and that file is not there.");
                    return Outcome.NotApplied;
                }
            }
            var type = rot.GetType(row.Type, true);
            RunClassConstructor(type);
            var replaced = harmony.CreateClassProcessor(type).Patch();
            if ((replaced?.Count ?? 0) == 0)
            {
                SubModule.Log("Host: patch not applied: " + row.Type + " patched no game method (" + row.Why + ").");
                return Outcome.NotApplied;
            }
            SubModule.Log("Host: patch applied: " + row.Type + ", " + replaced.Count + " game method(s) (" + row.Why + ").");
            return Outcome.Applied;
        }

        // As ROT.SubModule does it: a prefix on one game method found by its name and parameters.
        private static Outcome ApplyByHand(Harmony harmony, Assembly rot, PatchRow row)
        {
            RotHostRules.SplitTarget(row.Target, out var typeName, out var methodName);
            var type = Coop.FindType(typeName);
            if (type == null)
            {
                SubModule.Log("Host: patch not applied: " + Name(row) + ": " + typeName + " is not loaded here.");
                return Outcome.NotApplied;
            }
            var original = type.GetMethods(AnyDeclared).FirstOrDefault(method => method.Name == methodName && Signature(method) == row.Target);
            var prefix = rot.GetType(row.Type, true).GetMethod(row.Method, AnyStatic);
            if (original == null || prefix == null)
                throw new MissingMethodException(original == null ? row.Target : row.Type + "." + row.Method);
            RunClassConstructor(prefix.DeclaringType);
            harmony.Patch(original, new HarmonyMethod(prefix));
            SubModule.Log("Host: patch applied: " + Name(row) + " (" + row.Why + ").");
            return Outcome.Applied;
        }

        private static void ApplyMethods(Harmony harmony, Assembly rot, string typeName, List<Outcome> outcomes)
        {
            var rows = RotHostTables.Patches;
            var matched = new HashSet<PatchRow>();
            outcomes.Add(Guarded(typeName, () =>
            {
                var type = rot.GetType(typeName, true);
                var classInfo = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(type));
                if (!classInfo.methodType.HasValue)
                    classInfo.methodType = MethodType.Normal;
                foreach (var method in AccessTools.GetDeclaredMethods(type))
                    outcomes.Add(Guarded(typeName + "." + method.Name, () => ApplyMethod(harmony, classInfo, typeName, method, matched)));
                return Outcome.Nothing;
            }));
            foreach (var row in rows.Where(row => row.Type == typeName && row.Method != null && !matched.Contains(row)))
            {
                outcomes.Add(Outcome.NotApplied);
                SubModule.Log("Host: patch row " + Name(row) + " matches no patch method of this ROT.dll.");
            }
        }

        private static Outcome ApplyMethod(Harmony harmony, HarmonyMethod classInfo, string typeName, MethodInfo method, HashSet<PatchRow> matched)
        {
            var kind = Kind(method);
            if (kind == null)
                return Outcome.Nothing;
            var original = Original(classInfo.Merge(new HarmonyMethod(method)));
            var row = original == null ? null : RotHostRules.MethodRow(RotHostTables.Patches, typeName, method.Name, Signature(original));
            if (row == null)
            {
                SubModule.Log("Host: patch not applied: " + typeName + "." + method.Name + " on "
                    + (original == null ? "a game method that was not found" : Signature(original)) + " is not in the table.");
                return Outcome.NotApplied;
            }
            matched.Add(row);
            if (row.Decision != HostDecision.Host)
                return Outcome.Nothing;
            RunClassConstructor(method.DeclaringType);
            var patch = new HarmonyMethod(method);
            harmony.Patch(original, kind == Kinds[0] ? patch : null, kind == Kinds[1] ? patch : null, kind == Kinds[2] ? patch : null, kind == Kinds[3] ? patch : null);
            SubModule.Log("Host: patch applied: " + Name(row) + " (" + row.Why + ").");
            return Outcome.Applied;
        }

        // Harmony's own rule for which patch a method is: its attribute, else its name.
        private static string Kind(MethodInfo method)
        {
            var attributes = method.GetCustomAttributes(true).Select(attribute => attribute.GetType().FullName).ToList();
            return Kinds.FirstOrDefault(kind => method.Name == kind || attributes.Contains("HarmonyLib.Harmony" + kind));
        }

        // The game method a patch method's attributes name. Realm of Thrones only uses plain
        // methods, property getters and setters and constructors as targets.
        private static MethodBase Original(HarmonyMethod info)
        {
            if (info.declaringType == null)
                return null;
            switch (info.methodType ?? MethodType.Normal)
            {
                case MethodType.Normal:
                    return info.methodName == null ? null : AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                case MethodType.Getter:
                    return info.methodName == null ? null : AccessTools.DeclaredPropertyGetter(info.declaringType, info.methodName);
                case MethodType.Setter:
                    return info.methodName == null ? null : AccessTools.DeclaredPropertySetter(info.declaringType, info.methodName);
                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(info.declaringType, info.argumentTypes);
                default:
                    return null;
            }
        }

        // "Type.Method(parameter type names)", the form the table's targets are written in.
        private static string Signature(MethodBase method) =>
            method.DeclaringType?.FullName + "." + method.Name + "(" + string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name)) + ")";

        private static string Name(PatchRow row) =>
            row.Method == null ? row.Type : row.Type + "." + row.Method + (row.Target == null ? "" : " on " + row.Target);

        private static Outcome Guarded(string name, Func<Outcome> apply)
        {
            try
            {
                return apply();
            }
            catch (Exception ex)
            {
                SubModule.Log("Host: patch FAILED: " + name + ": " + RotHost.Short(ex));
                return Outcome.Failed;
            }
        }
    }
}
