using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements;

namespace RealmOfThronesCoopCompat
{
    // Keeps the dedicated server alive past its naval village lookup (why: NavalVillageRules).
    // Boot test 4 died there with "An item with the same key has already been added. Key:
    // ROT_town40_village2" right after the new campaign's XML loaded. Its own guard, apart from
    // the all or nothing map redirect, and also with War Sails off: the installer type is there
    // either way. When the method is not found the server is left as it is and the log says why.
    // The patched copy runs none of the server's own key lambda (boot test 5 died building it in
    // the copy; why: NavalVillageRules.Rewrite).
    internal static class NavalVillageGuard
    {
        private const string ServerAssemblyName = "DedicatedServer.Core";
        private const string InstallerTypeName = "DedicatedServer.NavalMapSceneInstaller";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _tried;

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
        }

        private static void TryPatch(Assembly assembly)
        {
            try
            {
                if (_tried || assembly == null || assembly.GetName().Name != ServerAssemblyName)
                    return;
                lock (Gate)
                {
                    if (_tried)
                        return;
                    _tried = true;
                }
                var method = NavalVillageRules.FindMethod(assembly.GetType(InstallerTypeName, false), typeof(Village),
                    candidate => PatchProcessor.GetOriginalInstructions(candidate), out var problem);
                if (method == null)
                {
                    SubModule.Log("Naval village guard: " + problem + "; nothing patched, the server keeps its own village lookup.");
                    return;
                }
                // Dry run first: the shape must match, and what stays of the method may not read a
                // field that Harmony's copy would mistake for another of the same name.
                var rewritten = NavalVillageRules.Rewrite(PatchProcessor.GetOriginalInstructions(method), typeof(Village), Replacement, out _, out problem);
                var ambiguous = MapRedirectRules.AmbiguousFields(rewritten);
                if (problem != null || ambiguous.Count > 0)
                {
                    SubModule.Log("Naval village guard: " + (problem ?? "the method still reads fields a patched copy would mistake (" + string.Join(", ", ambiguous) + ")")
                        + "; nothing patched, the server keeps its own village lookup.");
                    return;
                }
                _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NavalVillageGuard), nameof(Transpiler)));
                SubModule.Log("Naval village guard: " + InstallerTypeName + "." + method.Name + " keeps the first village per settlement id.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Naval village guard: could not patch the server's village lookup: " + ex);
            }
        }

        private static MethodInfo Replacement => AccessTools.Method(typeof(NavalVillageGuard), nameof(Villages));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = NavalVillageRules.Rewrite(instructions, typeof(Village), Replacement, out _, out var problem);
            if (problem != null)
                SubModule.Log("Naval village guard: " + problem + " while patching; the server's own lookup stays.");
            return result;
        }

        // Stands in for Village.All.ToDictionary(village => village.Settlement.StringId), without the
        // server's own key lambda.
        public static Dictionary<string, Village> Villages(IEnumerable<Village> villages)
        {
            var skipped = new List<string>();
            var result = NavalVillageRules.FirstPerKey(villages, village => village.Settlement.StringId, skipped);
            var line = NavalVillageRules.SkippedLine(skipped);
            if (line != null)
                SubModule.Log(line);
            return result;
        }
    }
}
