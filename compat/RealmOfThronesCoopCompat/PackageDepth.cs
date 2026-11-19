using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // Coop sends a joining player's new hero as one nested package: the hero, its clan, every
    // kingdom and the clans inside them. Coop reads it back with a nesting limit of 512
    // (GameInterface.Serialization.BinaryPackageSerializer.Deserialize(byte[])), which fits
    // vanilla's handful of kingdoms but not many more; Burning Empires' 41 got the player refused
    // with "Failed to create player", and Realm of Thrones has 28. This raises only that reading
    // limit, on the server and on players' PCs; the payload size limits Coop sets stay as they
    // are. Adapted from Burning Empires Coop Compat.
    internal static class PackageDepth
    {
        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _done;

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            SafeTryPatch();
            if (!_done)
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                {
                    try
                    {
                        if (!_done && e.LoadedAssembly.GetName().Name == "GameInterface")
                            SafeTryPatch();
                    }
                    catch { }
                };
            // Asked again at each campaign start; the first one without the type says so once.
            Coop.CampaignBegan += () =>
            {
                SafeTryPatch();
                if (_done || _missingLogged || Coop.FindType("GameInterface.ContainerProvider") == null)
                    return;
                _missingLogged = true;
                SubModule.Log("Package depth: a campaign started but Coop's BinaryPackageSerializer was not found; players may be refused on join.");
            };
        }

        private static bool _missingLogged;

        private static void SafeTryPatch()
        {
            try
            {
                lock (Gate)
                {
                    if (!_done)
                        TryPatch();
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Package depth: could not raise Coop's package nesting limit: " + ex.Message);
            }
        }

        private static void TryPatch()
        {
            var serializer = Coop.FindType("GameInterface.Serialization.BinaryPackageSerializer");
            if (serializer == null)
                return;
            _done = true;
            // There is also a generic Deserialize<T>(byte[]), so pick the plain one by hand.
            var method = AccessTools.GetDeclaredMethods(serializer).FirstOrDefault(m =>
                m.Name == "Deserialize" && !m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(byte[]));
            if (method == null)
            {
                SubModule.Log("Package depth: Coop's BinaryPackageSerializer.Deserialize not found; players may be refused on join.");
                return;
            }
            _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PackageDepth), nameof(Transpiler)));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = PackageDepthRules.Raise(instructions, out var line);
            SubModule.Log(line);
            return list;
        }
    }
}
