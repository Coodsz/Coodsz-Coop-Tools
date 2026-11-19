using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // Tells Coop.cs when a session starts and ends: postfixes on GameInterface.ContainerProvider's
    // SetContainer and Clear, found by name once GameInterface is loaded (on the server it only
    // loads when the server starts Coop). The postfixes only pass the edge on. When a name is
    // missing, nothing is patched, the log says so and callers keep reading Coop.InSession.
    // Kept apart from Coop.cs, which names nothing from Harmony.
    internal static class CoopSessionEdges
    {
        private const string ContainerTypeName = "GameInterface.ContainerProvider";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _tried;

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly, false);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly, false);
            // A type that does not load yet while Coop's assemblies come in one by one is asked for
            // again at campaign start, and only then given up on.
            Coop.CampaignBegan += () =>
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    TryPatch(assembly, true);
            };
        }

        private static void TryPatch(Assembly assembly, bool last)
        {
            try
            {
                if (_tried || assembly == null || assembly.GetName().Name != "GameInterface")
                    return;
                Type container;
                lock (Gate)
                {
                    if (_tried)
                        return;
                    try { container = assembly.GetType(ContainerTypeName, false); }
                    catch { container = null; }
                    if (container == null && !last)
                        return;
                    _tried = true;
                }
                var set = container?.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(m => m.Name == "SetContainer" && m.GetParameters().Length == 1);
                var clear = container?.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (set == null || clear == null)
                {
                    SubModule.Log("Coop session edges: " + ContainerTypeName + (container == null ? " not found" : ".SetContainer or .Clear not found")
                        + "; session starts and ends are read by polling instead.");
                    return;
                }
                _harmony.Patch(set, postfix: new HarmonyMethod(typeof(CoopSessionEdges), nameof(SetContainerPostfix)));
                _harmony.Patch(clear, postfix: new HarmonyMethod(typeof(CoopSessionEdges), nameof(ClearPostfix)));
                Coop.SessionEdgesHooked = true;
                SubModule.Log("Coop session edges: watching ContainerProvider.SetContainer and Clear.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Coop session edges could not be watched, so they are read by polling: " + ex.Message);
            }
        }

        private static void SetContainerPostfix() => Coop.SessionEdge(true);

        private static void ClearPostfix() => Coop.SessionEdge(false);
    }
}
