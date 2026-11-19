#nullable disable
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // PackageDepth's rewrite, with Harmony types only, so the launcher tests can run it on a stand in.
    internal static class PackageDepthRules
    {
        public const int CoopMaxDepth = 512;
        public const int RaisedMaxDepth = 4096;

        // Every "ldc.i4 512" right before XmlDictionaryReaderQuotas.MaxDepth's setter becomes 4096;
        // anything else stays. The line says what happened either way.
        public static List<CodeInstruction> Raise(IEnumerable<CodeInstruction> instructions, out string line)
        {
            var setter = AccessTools.PropertySetter(typeof(System.Xml.XmlDictionaryReaderQuotas), "MaxDepth");
            var list = instructions.ToList();
            var changed = 0;
            for (var i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_I4 && list[i].operand is int value && value == CoopMaxDepth && list[i + 1].Calls(setter))
                {
                    list[i].operand = RaisedMaxDepth;
                    changed++;
                }
            }
            line = changed > 0
                ? "Package depth: Coop's package nesting limit raised from " + CoopMaxDepth + " to " + RaisedMaxDepth + "."
                : "Package depth: Coop's nesting limit of " + CoopMaxDepth + " not found; players may be refused on join.";
            return list;
        }
    }
}
