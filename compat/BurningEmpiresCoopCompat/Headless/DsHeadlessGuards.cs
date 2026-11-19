using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BurningEmpiresCoopCompat
{
    // The co-op server has no player of its own, so Burning Empires code that leans on
    // Hero.MainHero or on the map view needs a few guards there:
    // - Wanderers: the daily kill and spawn checks compare a settlement with
    //   Hero.MainHero.CurrentSettlement. That read goes through a null safe helper, and any
    //   settlement holding a co-op player's party counts as "the player is here", so wanderers
    //   are not killed or spawned in front of a player.
    // - Pregnancy: Burning Empires swaps the game's spouse visit for its own copy, which only
    //   knows Hero.MainHero. On the server the game's own RefreshSpouseVisit runs instead, so
    //   Coop's nearby check applies. Burning Empires' adult age change stays.
    // - Cosmetic ships on the map do nothing on the server, which never draws the map.
    // - The optional DistanceCacheProbe diagnostics stay off on the server.
    // Players' PCs and single player keep the normal behaviour.
    internal static class DsHeadlessGuards
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static MethodInfo _vanillaRefreshSpouseVisit;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            count += PatchWanderers(harmony, burningEmpires);
            count += PatchSpouseVisit(harmony, burningEmpires);
            count += PatchCosmeticShips(harmony, burningEmpires);
            count += PatchDistanceCacheProbe(harmony, burningEmpires);
            return count;
        }

        // ---- Wanderers ----

        private static int PatchWanderers(Harmony harmony, Assembly burningEmpires)
        {
            var type = burningEmpires.GetType("BurningEmpires.CampaignBehaviors.BEWandererCampaignBehavior", false);
            if (type == null)
            {
                SubModule.Log("Headless guards: BEWandererCampaignBehavior not found; wanderer guard skipped.");
                return 0;
            }

            var mainHero = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.MainHero));
            var currentSettlement = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.CurrentSettlement));
            var transpiler = new HarmonyMethod(typeof(DsHeadlessGuards), nameof(WandererTranspiler));
            var count = 0;

            // The two methods and the lambdas the compiler made from them (named like
            // <TryKillRandomCompanion>b__0_0, either on the class or on a nested class).
            var types = new List<Type> { type };
            types.AddRange(AllNestedTypes(type));
            foreach (var t in types)
            {
                foreach (var method in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;
                    if (method.Name.IndexOf("TryKillRandomCompanion", StringComparison.Ordinal) < 0
                        && method.Name.IndexOf("TrySpawnNewRandomCompanion", StringComparison.Ordinal) < 0)
                        continue;
                    try
                    {
                        if (!HasMainHeroSettlementRead(method, mainHero, currentSettlement))
                            continue;
                        harmony.Patch(method, transpiler: transpiler);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        SubModule.Log("Headless guards: could not guard " + t.Name + "." + method.Name + ": " + ex.Message);
                    }
                }
            }

            if (count == 0)
                SubModule.Log("Headless guards: no Hero.MainHero settlement read found in the wanderer code; guard skipped.");
            return count;
        }

        private static IEnumerable<Type> AllNestedTypes(Type type)
        {
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return nested;
                foreach (var inner in AllNestedTypes(nested))
                    yield return inner;
            }
        }

        private static bool HasMainHeroSettlementRead(MethodBase method, MethodInfo mainHero, MethodInfo currentSettlement)
        {
            var code = PatchProcessor.GetOriginalInstructions(method);
            var reads = 0;
            for (var i = 0; i + 1 < code.Count; i++)
            {
                if (!code[i].Calls(mainHero) || !code[i + 1].Calls(currentSettlement))
                    continue;
                if (!IsComparedSettlement(code, i))
                {
                    SubModule.Log("Headless guards: " + method.DeclaringType?.Name + "." + method.Name + " reads Hero.MainHero.CurrentSettlement in a new way; left unguarded.");
                    return false;
                }
                reads++;
            }
            return reads > 0;
        }

        // Only "x == Hero.MainHero.CurrentSettlement" (or !=) can take the dup: a settlement is pushed
        // right before the read and the comparison comes right after it. Any other read, like a null
        // check after a Burning Empires update, would get broken IL, so that method stays unpatched.
        private static bool IsComparedSettlement(IList<CodeInstruction> code, int i)
        {
            if (i < 1 || i + 2 >= code.Count)
                return false;
            // A jump straight to the CurrentSettlement read would skip the dup.
            if (code[i + 1].labels.Count > 0 || code[i + 1].blocks.Count > 0)
                return false;
            var compare = code[i + 2].opcode;
            if (compare != OpCodes.Ceq && compare != OpCodes.Beq && compare != OpCodes.Beq_S
                && compare != OpCodes.Bne_Un && compare != OpCodes.Bne_Un_S)
                return false;
            var push = code[i - 1];
            if (push.operand is MethodInfo pushed)
                return (push.opcode == OpCodes.Call || push.opcode == OpCodes.Callvirt) && typeof(Settlement).IsAssignableFrom(pushed.ReturnType);
            if (push.operand is FieldInfo field)
                return (push.opcode == OpCodes.Ldfld || push.opcode == OpCodes.Ldsfld) && typeof(Settlement).IsAssignableFrom(field.FieldType);
            return push.IsLdloc() || push.IsLdarg();
        }

        // "x != Hero.MainHero.CurrentSettlement" pushes x first, so when Hero.MainHero is read
        // x is on top of the stack. The pair becomes "dup; call SettlementToAvoid(x)".
        private static IEnumerable<CodeInstruction> WandererTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var mainHero = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.MainHero));
            var currentSettlement = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.CurrentSettlement));
            var helper = AccessTools.Method(typeof(DsHeadlessGuards), nameof(SettlementToAvoid));
            var code = instructions.ToList();
            // HasMainHeroSettlementRead already checked this; a read that doesn't fit leaves the method as it is.
            for (var i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].Calls(mainHero) && code[i + 1].Calls(currentSettlement) && !IsComparedSettlement(code, i))
                    return code;
            }
            for (var i = 0; i + 1 < code.Count; i++)
            {
                if (!code[i].Calls(mainHero) || !code[i + 1].Calls(currentSettlement))
                    continue;
                // Keep labels and blocks on the first instruction so jumps still land here.
                code[i].opcode = OpCodes.Dup;
                code[i].operand = null;
                code[i + 1].opcode = OpCodes.Call;
                code[i + 1].operand = helper;
                i++;
            }
            return code;
        }

        // Returns the settlement the comparison should treat as "where the player is". When the
        // candidate itself holds a co-op player's party, the candidate is returned, so the check
        // sees "same settlement" and leaves it alone.
        public static Settlement SettlementToAvoid(Settlement candidate)
        {
            if (!Coop.OnServer)
                return Hero.MainHero.CurrentSettlement;

            try
            {
                if (candidate != null && HoldsPlayerParty(candidate))
                    return candidate;
            }
            catch (Exception ex)
            {
                ReportOnce("wanderer player check", ex);
            }
            return PlayerExemptions.SafeMainHero()?.CurrentSettlement;
        }

        private static bool HoldsPlayerParty(Settlement settlement)
        {
            foreach (var party in settlement.Parties)
            {
                if (party != null && Coop.IsPlayerHero(party.LeaderHero))
                    return true;
            }
            return false;
        }

        // ---- Pregnancy spouse visit ----

        private static int PatchSpouseVisit(Harmony harmony, Assembly burningEmpires)
        {
            var type = burningEmpires.GetType("BurningEmpires.BESystems.Life.BEPregnancyDailyTickPatch", false);
            var method = type == null ? null : AccessTools.Method(type, "RefreshSpouseVisit", new[] { typeof(PregnancyCampaignBehavior), typeof(Hero) });
            _vanillaRefreshSpouseVisit = AccessTools.Method(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit", new[] { typeof(Hero) });
            if (method == null || _vanillaRefreshSpouseVisit == null)
            {
                SubModule.Log("Headless guards: RefreshSpouseVisit not found (Burning Empires or the game); spouse visit guard skipped.");
                return 0;
            }

            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(DsHeadlessGuards), nameof(RefreshSpouseVisitPrefix)));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Headless guards: could not guard RefreshSpouseVisit: " + ex.Message);
                return 0;
            }
        }

        // Burning Empires names its first parameter "_", so the arguments are taken by position.
        private static bool RefreshSpouseVisitPrefix(PregnancyCampaignBehavior __0, Hero __1)
        {
            if (!Coop.OnServer)
                return true;

            if (__0 == null || __1 == null || __1.Spouse == null)
                return false;
            try
            {
                _vanillaRefreshSpouseVisit.Invoke(__0, new object[] { __1 });
            }
            catch (Exception ex)
            {
                ReportOnce("RefreshSpouseVisit", ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex);
            }
            return false;
        }

        // ---- Cosmetic ships ----

        private static int PatchCosmeticShips(Harmony harmony, Assembly burningEmpires)
        {
            var type = burningEmpires.GetType("BurningEmpires.CampaignBehaviors.BEBlocCosmeticShipCampaignBehavior", false);
            if (type == null)
            {
                SubModule.Log("Headless guards: BEBlocCosmeticShipCampaignBehavior not found; ship guard skipped.");
                return 0;
            }

            var prefix = new HarmonyMethod(typeof(DsHeadlessGuards), nameof(SkipOnServer));
            var targets = new[]
            {
                AccessTools.Method(type, "OnTick", new[] { typeof(float) }),
                AccessTools.Method(type, "OnPartyVisibilityChanged", new[] { typeof(PartyBase) }),
                AccessTools.Method(type, "OnMobilePartyRaftStateChanged", new[] { typeof(MobileParty) }),
            };
            var names = new[] { "OnTick", "OnPartyVisibilityChanged", "OnMobilePartyRaftStateChanged" };

            var count = 0;
            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                {
                    SubModule.Log("Headless guards: BEBlocCosmeticShipCampaignBehavior." + names[i] + " not found.");
                    continue;
                }
                try
                {
                    harmony.Patch(targets[i], prefix: prefix);
                    count++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("Headless guards: could not guard BEBlocCosmeticShipCampaignBehavior." + names[i] + ": " + ex.Message);
                }
            }
            return count;
        }

        private static bool SkipOnServer() => !Coop.OnServer;

        // ---- DistanceCacheProbe (optional diagnostics) ----

        // The probe reads Burning Empires' setting through its own small gate class, which may
        // be inlined, so both getters are forced to false on the server.
        private static int PatchDistanceCacheProbe(Harmony harmony, Assembly burningEmpires)
        {
            var settings = burningEmpires.GetType("BurningEmpires.ModConfigurationMenu.BurningEmpiresLogSettings", false);
            var getters = new List<MethodInfo>();
            if (settings != null)
                getters.Add(AccessTools.PropertyGetter(settings, "ShouldRunDistanceCacheProbe"));
            var gate = AccessTools.TypeByName("BurningEmpires.DistanceCacheProbe.Diagnostics.Settings.DiagnosticSettingsGate");
            if (gate != null)
                getters.Add(AccessTools.PropertyGetter(gate, "ShouldRunDistanceCacheProbe"));

            var postfix = new HarmonyMethod(typeof(DsHeadlessGuards), nameof(ProbeOffOnServer));
            var count = 0;
            foreach (var getter in getters)
            {
                if (getter == null)
                    continue;
                try
                {
                    harmony.Patch(getter, postfix: postfix);
                    count++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("Headless guards: could not guard " + getter.DeclaringType?.Name + ".ShouldRunDistanceCacheProbe: " + ex.Message);
                }
            }
            if (count == 0)
                SubModule.Log("Headless guards: ShouldRunDistanceCacheProbe not found; probe guard skipped.");
            return count;
        }

        private static void ProbeOffOnServer(ref bool __result)
        {
            if (__result && Coop.OnServer)
                __result = false;
        }

        private static void ReportOnce(string key, Exception ex)
        {
            lock (Reported)
            {
                if (Reported.Add(key))
                    SubModule.Log("Headless guards: " + key + " failed on the server and was skipped: " + ex);
            }
        }
    }
}
