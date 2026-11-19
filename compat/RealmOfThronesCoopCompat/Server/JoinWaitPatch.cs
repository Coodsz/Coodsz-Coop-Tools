#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // The Harmony half of JoinPatience, with no game or Coop type, so the launcher tests patch a
    // stand in for Coop's method with it. It points every read (ldsfld or ldsflda) of one static
    // field in one method at Wait. It fails closed: Coop's AbortStalledJoiners reads its limit
    // exactly ExpectedReads times (the check and the log line); any other count means Coop's method
    // changed, so the transpiler is taken off again and Coop's own limit applies.
    internal static class JoinWaitPatch
    {
        public const int ExpectedReads = JoinRules.ServerJoinReads;

        // What the patched method reads in place of the field.
        public static TimeSpan Wait = TimeSpan.FromMinutes(15);

        private static FieldInfo _target;
        private static int _reads;

        // Reads the last transpiler run moved.
        public static int Reads => _reads;

        // Patches method; true when exactly ExpectedReads reads were moved. Otherwise our
        // transpiler is taken off again (the method is as Coop wrote it) and false. Throws only
        // what Harmony throws.
        public static bool Apply(Harmony harmony, MethodBase method, FieldInfo target, TimeSpan wait, out int reads)
        {
            Wait = wait;
            _target = target;
            _reads = 0;
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(JoinWaitPatch), nameof(Transpiler)));
            reads = _reads;
            if (reads == ExpectedReads)
                return true;
            harmony.Unpatch(method, HarmonyPatchType.Transpiler, harmony.Id);
            return false;
        }

        // A list, not an iterator: a compiler made iterator type would name Harmony's CodeInstruction
        // in its interfaces, and the module's types must load without Harmony. The count starts again
        // on every run, since Harmony runs the transpilers again when another patch lands on the method.
        private static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            _reads = 0;
            var ours = typeof(JoinWaitPatch).GetField(nameof(Wait), BindingFlags.Public | BindingFlags.Static);
            var result = new List<CodeInstruction>(instructions);
            foreach (var instruction in result)
            {
                if ((instruction.opcode == OpCodes.Ldsfld || instruction.opcode == OpCodes.Ldsflda) && instruction.operand is FieldInfo field && Same(field, _target))
                {
                    instruction.operand = ours;
                    _reads++;
                }
            }
            return result;
        }

        private static bool Same(FieldInfo a, FieldInfo b) =>
            a != null && b != null && a.Name == b.Name && a.DeclaringType?.FullName == b.DeclaringType?.FullName;
    }
}
