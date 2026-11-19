using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

namespace RealmOfThronesCoopCompat
{
    // The host's checks, which only read and log: whether this ROT.dll still is what the tables
    // were written for, whether the save system knows Realm of Thrones' saved types, and for a
    // live test whether every hosted method compiles on the server. BCL and TaleWorlds types only.
    internal static class RotHostChecks
    {
        private const BindingFlags AnyDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const int MaxJitLines = 80;

        internal static Type[] Types(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type != null).ToArray(); }
        }

        // The differences between ROT.dll and the tables (RotHostRules.Drift), for one loud line.
        public static List<string> Drift(Assembly rot, string coreVersion, out int behaviors, out int models, out int patchClasses)
        {
            var types = Types(rot);
            var behaviorNames = types.Where(type => typeof(CampaignBehaviorBase).IsAssignableFrom(type)).Select(type => type.FullName).ToList();
            var modelNames = types.Where(type => typeof(GameModel).IsAssignableFrom(type)).Select(type => type.FullName).ToList();
            var patchNames = types.Where(HasHarmonyAttribute).Select(type => type.FullName).ToList();
            behaviors = behaviorNames.Count;
            models = modelNames.Count;
            patchClasses = patchNames.Count;
            return RotHostRules.Drift(behaviorNames, modelNames, patchNames, types.Select(type => type.FullName), coreVersion);
        }

        // Read from the attribute data, so no attribute is built and Harmony is not needed.
        private static bool HasHarmonyAttribute(Type type)
        {
            try { return type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Namespace == "HarmonyLib"); }
            catch { return false; }
        }

        // A saved type the save system does not know, or a definer id used twice, makes every
        // save fail without a crash. The global context is built when the modules are loaded,
        // from every loaded assembly that references the save system, so ROT.dll must be loaded
        // by then. SaveManager._definitionContext and DefinitionContext._saveableTypeDefiners
        // are private in the server's TaleWorlds.SaveSystem.dll; GotError and Errors are public.
        public static void SaveDefiners(Assembly rot, string when)
        {
            try
            {
                var wanted = Types(rot).Where(type => !type.IsAbstract && typeof(SaveableTypeDefiner).IsAssignableFrom(type))
                    .Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList();
                var context = typeof(SaveManager).GetField("_definitionContext", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as DefinitionContext;
                if (context == null)
                {
                    SubModule.Log("Host: save definers (" + when + "): the save system has no definition context yet.");
                    return;
                }
                var definers = typeof(DefinitionContext).GetField("_saveableTypeDefiners", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(context) as IEnumerable;
                if (definers == null)
                {
                    SubModule.Log("Host: save definers (" + when + "): the save system's definer list could not be read.");
                    return;
                }
                var present = new HashSet<string>(StringComparer.Ordinal);
                foreach (var definer in definers)
                {
                    if (definer != null)
                        present.Add(definer.GetType().FullName);
                }
                var errors = context.Errors.ToList();
                SubModule.Log("Host: save definers (" + when + "): " + wanted.Count(present.Contains) + " of " + wanted.Count + " Realm of Thrones definers are in the save system's context of "
                    + present.Count + " (" + string.Join(", ", wanted.Select(name => name + (present.Contains(name) ? "" : " MISSING"))) + "); "
                    + (context.GotError
                        ? "THE CONTEXT HAS " + errors.Count + " ERROR(S), SO EVERY SAVE WOULD FAIL: " + string.Join(" | ", errors.Take(5).Select(CrashTraceRules.Shorten))
                        : "the context has no errors") + ".");
            }
            catch (Exception ex)
            {
                SubModule.Log("Host: save definers (" + when + ") could not be checked: " + RotHost.Short(ex));
            }
        }

        // MCM's and UIExtenderEx's DLLs are served to ROT.dll as plain libraries. If one of their
        // own SubModules runs on the server (somebody gave that module a server bin), MCM builds
        // a settings provider and Realm of Thrones stops reading its own defaults.
        public static void LibrarySubModules()
        {
            // By reflection: the engine's Module class derives from a type of TaleWorlds.DotNet,
            // which this module does not reference.
            var module = typeof(TaleWorlds.MountAndBlade.MBSubModuleBase).Assembly.GetType("TaleWorlds.MountAndBlade.Module", true);
            var current = module.GetProperty("CurrentModule", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
            var found = ((IEnumerable)module.GetMethod("CollectSubModules", BindingFlags.Instance | BindingFlags.Public).Invoke(current, null)).Cast<object>()
                .Where(subModule => RotHostRules.IsLibraryAssembly(subModule.GetType().Assembly.GetName().Name))
                .Select(subModule => subModule.GetType().FullName + " of " + subModule.GetType().Assembly.GetName().Name + ".dll").ToList();
            SubModule.Log(found.Count == 0
                ? "Host: no SubModule of MCM or UIExtenderEx runs on this server; their DLLs are only libraries here."
                : "Host: WARNING, A SUBMODULE OF MCM OR UIEXTENDEREX RUNS ON THIS SERVER (" + string.Join(", ", found) + "). Their DLLs are meant to be only libraries here: "
                    + "with MCM running Realm of Thrones reads MCM's settings and not its own defaults.");
        }

        // Compiles every method of every hosted type (and of the types the compiler nested in
        // them) without running it, so a method that names something the server lacks shows at
        // boot and not on the day it is first called. Only with COODSZ_ROT_HOST_JIT=1.
        public static void JitSweep(Assembly rot, HostLevel level)
        {
            var names = RotHostTables.Patches.Where(row => row.Decision == HostDecision.Host).Select(row => row.Type)
                .Concat(RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, level).Select(row => row.Type))
                .Concat(RotHostTables.ModelRows.Where(row => row.Decision == HostDecision.Host && RotHostRules.HostsModels(level)).Select(row => row.Type))
                .Distinct().ToList();
            int types = 0, methods = 0, failed = 0;
            foreach (var type in names.SelectMany(name => WithNested(rot.GetType(name, false))))
            {
                types++;
                if (type.ContainsGenericParameters)
                    continue;
                foreach (var method in type.GetMethods(AnyDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AnyDeclared)))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;
                    methods++;
                    try
                    {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    }
                    catch (Exception ex)
                    {
                        if (++failed <= MaxJitLines)
                            SubModule.Log("Host: JIT: " + type.FullName + "." + method.Name + ": " + RotHost.Short(ex));
                    }
                }
            }
            SubModule.Log("Host: JIT sweep: " + (methods - failed) + " of " + methods + " methods of " + types + " hosted types compile"
                + (failed == 0 ? "." : "; " + failed + " do not" + (failed > MaxJitLines ? " (the first " + MaxJitLines + " are listed)." : ".")));
        }

        private static IEnumerable<Type> WithNested(Type type) =>
            type == null
                ? Enumerable.Empty<Type>()
                : new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested));
    }
}
