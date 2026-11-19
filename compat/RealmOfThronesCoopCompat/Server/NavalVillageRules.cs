#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // NavalVillageGuard's rules. The dedicated server's NavalMapSceneInstaller builds a lookup of
    // every village by its settlement id with Village.All.ToDictionary (ApplyVillageDropOffs in the
    // server source; the shipped DLL is obfuscated, so the method is found by shape). Realm of
    // Thrones' settlements.xml gives two villages another village's component id, so two
    // settlements share one Village object, Village.All holds it twice and ToDictionary throws on
    // the repeated key, after a new game and after a load. Single player and players' PCs build the
    // same shared object from the same XML, so the data stays as it is and only the lookup is
    // rewritten to call our own method, which keeps the first village per key (Rewrite).
    // Uses Harmony types only, so the launcher tests can run it on stand in methods.
    internal static class NavalVillageRules
    {
        public const int ShownIds = 5;

        // Enumerable.ToDictionary<element, string>(IEnumerable<element>, Func<element, string>).
        public static bool IsKeyedToDictionary(MethodInfo method, Type element) =>
            method != null && element != null && method.IsGenericMethod && method.DeclaringType == typeof(Enumerable)
            && method.Name == nameof(Enumerable.ToDictionary) && method.GetParameters().Length == 2
            && method.GetGenericArguments().SequenceEqual(new[] { element, typeof(string) });

        public static int CountCalls(IEnumerable<CodeInstruction> instructions, Type element) =>
            instructions.Count(instruction => IsKeyedToDictionary(MapRedirectRules.Called(instruction), element));

        // The one private static method of the type that makes exactly one such call, or null with
        // the reason. More than one candidate means the server changed, and a guess could hit the
        // wrong lookup.
        public static MethodInfo FindMethod(Type type, Type element, Func<MethodInfo, IEnumerable<CodeInstruction>> instructionsOf, out string problem)
        {
            problem = null;
            if (type == null)
            {
                problem = "the naval installer type is not there";
                return null;
            }
            var found = new List<(MethodInfo Method, int Calls)>();
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                int calls;
                try { calls = CountCalls(instructionsOf(method), element); }
                catch { continue; }
                if (calls > 0)
                    found.Add((method, calls));
            }
            if (found.Count != 1 || found[0].Calls != 1)
            {
                problem = found.Count == 0
                    ? "no private static method of " + type.FullName + " builds the village lookup"
                    : found.Count + " private static methods of " + type.FullName + " build a village lookup (" + string.Join(", ", found.Select(f => f.Method.Name + " with " + f.Calls)) + "), expected one with one call";
                return null;
            }
            return found[0].Method;
        }

        // What the C# compiler emits between Village.All and ToDictionary to build and cache the key
        // lambda: load the cached delegate, keep it if set, else load the closure singleton, take the
        // method's address, make the delegate and cache it.
        public static bool IsLambdaCacheStep(CodeInstruction instruction)
        {
            var op = instruction.opcode;
            if (op == OpCodes.Ldsfld || op == OpCodes.Dup || op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Pop
                || op == OpCodes.Ldnull || op == OpCodes.Ldftn || op == OpCodes.Stsfld || op == OpCodes.Nop)
                return true;
            return op == OpCodes.Newobj && instruction.operand is ConstructorInfo ctor && typeof(Delegate).IsAssignableFrom(ctor.DeclaringType);
        }

        // Rewrites the lookup so only the replacement runs: the static call that pushes the villages
        // stays, every lambda step between it and ToDictionary becomes a nop (labels and exception
        // blocks stay), and ToDictionary becomes a call to replacement, which takes the villages
        // alone. Boot test 5 showed why the server's own lambda cannot stay in the patched copy: the
        // obfuscated closure class has three static fields all named A (the singleton and two
        // cached delegates), and MonoMod, which builds Harmony's copy, finds a field again by its
        // declaring type and name only, so the copy read the wrong A and made the delegate on a null
        // target. Anything but that shape changes nothing and gives the reason.
        public static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, Type element, MethodInfo replacement, out int replaced, out string problem)
        {
            replaced = 0;
            problem = null;
            var list = instructions.ToList();
            var calls = Enumerable.Range(0, list.Count).Where(i => IsKeyedToDictionary(MapRedirectRules.Called(list[i]), element)).ToList();
            if (calls.Count != 1)
            {
                problem = calls.Count + " village lookup calls, expected one";
                return list;
            }
            var source = typeof(IEnumerable<>).MakeGenericType(element);
            var start = -1;
            for (var i = calls[0] - 1; i >= 0; i--)
            {
                var called = MapRedirectRules.Called(list[i]);
                if (called != null && called.IsStatic && called.GetParameters().Length == 0 && source.IsAssignableFrom(called.ReturnType))
                {
                    start = i;
                    break;
                }
                if (!IsLambdaCacheStep(list[i]))
                {
                    problem = "the lookup's key lambda has an unexpected step (" + list[i].opcode + ")";
                    return list;
                }
            }
            if (start < 0)
            {
                problem = "no call that pushes the villages before the lookup";
                return list;
            }
            for (var i = start + 1; i < calls[0]; i++)
            {
                list[i].opcode = OpCodes.Nop;
                list[i].operand = null;
            }
            list[calls[0]].opcode = OpCodes.Call;
            list[calls[0]].operand = replacement;
            replaced = 1;
            return list;
        }

        // ToDictionary that keeps the first item per key. An item whose key is null or throws is
        // left out too. Each skipped item adds its key (or why it has none) to skipped.
        public static Dictionary<string, T> FirstPerKey<T>(IEnumerable<T> items, Func<T, string> key, List<string> skipped)
        {
            var result = new Dictionary<string, T>();
            foreach (var item in items ?? Enumerable.Empty<T>())
            {
                string id;
                try { id = key(item); }
                catch (Exception ex)
                {
                    skipped.Add("(no id: " + ex.GetType().Name + ")");
                    continue;
                }
                if (id == null)
                    skipped.Add("(no id)");
                else if (result.ContainsKey(id))
                    skipped.Add(id);
                else
                    result.Add(id, item);
            }
            return result;
        }

        // The one line after the lookup is built, or null when nothing was skipped.
        public static string SkippedLine(IList<string> skipped)
        {
            if (skipped == null || skipped.Count == 0)
                return null;
            return "Naval village guard: " + skipped.Count + " village entr" + (skipped.Count == 1 ? "y" : "ies") + " left out of the server's village lookup ("
                + string.Join(", ", skipped.Take(ShownIds)) + (skipped.Count > ShownIds ? ", ..." : "") + "), the first one per settlement id kept. "
                + "Realm of Thrones' settlements.xml gives these villages another village's component, so two settlements share one village; "
                + "that is the mod's data, the same in single player, and the server goes on.";
        }
    }
}
