using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BurningEmpiresCoopCompat
{
    // Coop sends a joining player's new hero as one nested package: the hero, its clan, every
    // kingdom and the clans inside them. Coop reads it back with a nesting limit of 512, which
    // fits vanilla's handful of kingdoms but not Burning Empires' 41, so the server refused the
    // player with "Failed to create player". This raises only that reading limit, on the server
    // and on players' PCs. The payload size limits Coop sets stay as they are.
    internal static class PackageDepth
    {
        private const int CoopMaxDepth = 512;
        private const int BurningEmpiresMaxDepth = 4096;

        private static bool _done;

        public static void Install(Harmony harmony)
        {
            SafeTryPatch(harmony);
            if (!_done)
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                {
                    if (!_done && e.LoadedAssembly.GetName().Name == "GameInterface")
                        SafeTryPatch(harmony);
                };
        }

        private static void SafeTryPatch(Harmony harmony)
        {
            try { TryPatch(harmony); }
            catch (Exception ex) { SubModule.Log("Package depth: could not raise Coop's package nesting limit: " + ex.Message); }
        }

        private static void TryPatch(Harmony harmony)
        {
            var serializer = AccessTools.TypeByName("GameInterface.Serialization.BinaryPackageSerializer");
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
            try
            {
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PackageDepth), nameof(Transpiler)));
            }
            catch (Exception ex)
            {
                SubModule.Log("Package depth: could not raise Coop's package nesting limit: " + ex.Message);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setter = AccessTools.PropertySetter(typeof(System.Xml.XmlDictionaryReaderQuotas), "MaxDepth");
            var list = instructions.ToList();
            var changed = 0;
            for (var i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_I4 && list[i].operand is int value && value == CoopMaxDepth
                    && list[i + 1].Calls(setter))
                {
                    list[i].operand = BurningEmpiresMaxDepth;
                    changed++;
                }
            }
            SubModule.Log(changed > 0
                ? "Package depth: Coop's package nesting limit raised from " + CoopMaxDepth + " to " + BurningEmpiresMaxDepth + "."
                : "Package depth: Coop's nesting limit of " + CoopMaxDepth + " not found; players may be refused on join.");
            return list;
        }
    }
}
