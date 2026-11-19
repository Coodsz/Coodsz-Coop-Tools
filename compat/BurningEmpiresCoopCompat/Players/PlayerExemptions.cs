using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires spares only Hero.MainHero and Clan.PlayerClan from its rules (gender
    // rights, commander swaps, champion owners, heir choice, government buttons, religion).
    // In co-op every player must get the same treatment, and on the server Hero.MainHero is
    // nobody's hero (or throws when there is no player character at all). This file:
    // - treats co-op player heroes and their clans like the main clan in the gender rights
    //   checks, on both sides, so the server and the players agree;
    // - lets Hero.CanLeadParty keep its vanilla answer for player clan members;
    // - on the server, keeps gender enforcement, champion ownership, heir choice and the
    //   player ruler check away from player clans;
    // - on the server, gives player heroes the "None" religion like the main hero;
    // - swaps Hero.MainHero reads in those methods for a null safe read.
    // Outside a co-op session every hook leaves Burning Empires alone.
    internal static class PlayerExemptions
    {
        private const string Religion = "BurningEmpires.BESystems.Religion.";

        private static MethodInfo _worldDataCurrent;
        private static MethodInfo _tryGetHeroReligion;
        private static MethodInfo _setReligion;

        // Vanilla Hero.CanLeadParty answer, saved by the first postfix for the last one.
        [ThreadStatic] private static bool _vanillaCanLeadParty;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;

            var rights = FindType(burningEmpires, Religion + "BEReligionGenderRightsService");
            count += Patch(harmony, rights, "IsPlayerClanMember", new[] { typeof(Hero) }, prefix: nameof(IsPlayerClanMemberPrefix));
            count += Patch(harmony, rights, "IsEligibleToCommand", new[] { typeof(Hero), typeof(Kingdom) }, prefix: nameof(EligiblePrefix));
            count += Patch(harmony, rights, "IsEligibleToRule", new[] { typeof(Hero), typeof(Kingdom) }, prefix: nameof(EligiblePrefix));
            count += Patch(harmony, rights, "IsEligibleToHoldOffice", new[] { typeof(Hero), typeof(Kingdom) }, prefix: nameof(EligiblePrefix));

            count += PatchCanLeadParty(harmony);
            var restricted = FindType(burningEmpires, "BurningEmpires.Hero_CanLeadParty_Patch");
            count += Patch(harmony, restricted, "IsRestrictedCommander", new[] { typeof(Hero) }, transpiler: nameof(SafeMainHeroTranspiler));

            var enforcement = FindType(burningEmpires, Religion + "BEGenderRuleEnforcementService");
            count += Patch(harmony, enforcement, "ReplaceOrDisbandIneligibleCommanders", new[] { typeof(Kingdom) },
                transpiler: nameof(SafeMainHeroTranspiler), finalizer: nameof(EnforcementFinalizer));
            count += PatchLambdas(harmony, enforcement, "ReplaceOrDisbandIneligibleCommanders");

            var heir = FindType(burningEmpires, Religion + "BEChangeClanLeaderAdultMalePreferencePatch");
            count += Patch(harmony, heir, "Prefix", new[] { typeof(Clan) }, prefix: nameof(HeirPrefix));

            var champions = FindType(burningEmpires, "BurningEmpires.BESystems.Champions.BEChampionCampaignBehavior");
            count += Patch(harmony, champions, "IsEligibleOwner", new[] { typeof(Hero) },
                prefix: nameof(EligibleOwnerPrefix), transpiler: nameof(SafeMainHeroTranspiler));

            var government = FindType(burningEmpires, "BurningEmpires.BESystems.Government.BEKingdomGovernmentData");
            count += Patch(harmony, government, "IsPlayerRuler", new[] { typeof(Kingdom) }, prefix: nameof(IsPlayerRulerPrefix));

            var assignment = FindType(burningEmpires, Religion + "BEReligionAssignmentService");
            var worldData = FindType(burningEmpires, "BurningEmpires.BESystems.Core.BEWorldData");
            var manager = FindType(burningEmpires, "BurningEmpires.BESystems.Core.BEWorldDataManager");
            _worldDataCurrent = manager == null ? null : AccessTools.PropertyGetter(manager, "Current");
            if (assignment != null && worldData != null)
            {
                _tryGetHeroReligion = AccessTools.Method(assignment, "TryGetHeroReligion",
                    new[] { worldData, typeof(Hero), typeof(string).MakeByRefType() });
                _setReligion = AccessTools.Method(assignment, "SetReligion", new[] { worldData, typeof(Hero), typeof(string) });
            }
            if (_worldDataCurrent == null || _tryGetHeroReligion == null || _setReligion == null)
                SubModule.Log("Could not find Burning Empires religion storage; Burning Empires won't assign religions to player heroes.");
            count += Patch(harmony, assignment, "AssignReligionIfMissing", new[] { typeof(Hero), typeof(bool) },
                prefix: nameof(AssignReligionPrefix), transpiler: nameof(SafeMainHeroTranspiler));
            count += Patch(harmony, assignment, "TryAssignRuntimeReligionFromCurrentSettlement", new[] { typeof(Hero) },
                prefix: nameof(TryAssignRuntimeReligionPrefix), transpiler: nameof(SafeMainHeroTranspiler));

            return count;
        }

        // True for the local main hero, the local player clan, and every co-op player's hero
        // and clan. On the server the local main hero is nobody's, so only co-op players count
        // there, which keeps the server's lists the same as the players' lists.
        private static bool IsPlayerClanMember(Hero hero)
        {
            if (hero == null)
                return false;
            if (Coop.OnServer)
                return Coop.IsPlayerHero(hero) || Coop.IsPlayerClan(hero.Clan);
            if (hero == SafeMainHero())
                return true;
            var clan = hero.Clan;
            if (clan != null && clan == SafePlayerClan())
                return true;
            return Coop.IsPlayerHero(hero) || Coop.IsPlayerClan(clan);
        }

        // Hero.MainHero reads CharacterObject.PlayerCharacter.HeroObject, which throws on a
        // server with no player character. Outside a session it is the normal read.
        public static Hero SafeMainHero()
        {
            if (!Coop.InSession)
                return Hero.MainHero;
            try { return CharacterObject.PlayerCharacter?.HeroObject; }
            catch { return null; }
        }

        private static Clan SafePlayerClan()
        {
            try { return Clan.PlayerClan; }
            catch { return null; }
        }

        // --- Gender rights (both sides) ---

        private static bool IsPlayerClanMemberPrefix(Hero hero, ref bool __result)
        {
            if (!Coop.InSession)
                return true;
            __result = IsPlayerClanMember(hero);
            return false;
        }

        // IsPlayerClanMember is small enough to be inlined, so the public checks answer for
        // player clans themselves.
        private static bool EligiblePrefix(Hero hero, ref bool __result)
        {
            if (hero == null || !Coop.InSession || !IsPlayerClanMember(hero))
                return true;
            __result = true;
            return false;
        }

        // --- Hero.CanLeadParty (both sides) ---

        private static int PatchCanLeadParty(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(Hero), "CanLeadParty", Type.EmptyTypes);
            if (method == null)
            {
                SubModule.Log("Could not find Hero.CanLeadParty; player clan commanders follow Burning Empires' rule.");
                return 0;
            }
            try
            {
                var first = new HarmonyMethod(typeof(PlayerExemptions), nameof(CanLeadPartyFirst)) { priority = Priority.First };
                var last = new HarmonyMethod(typeof(PlayerExemptions), nameof(CanLeadPartyLast)) { priority = Priority.Last };
                harmony.Patch(method, postfix: first);
                harmony.Patch(method, postfix: last);
                return 2;
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not hook Hero.CanLeadParty: " + ex.Message);
                return 0;
            }
        }

        private static void CanLeadPartyFirst(bool __result)
        {
            _vanillaCanLeadParty = __result;
        }

        private static void CanLeadPartyLast(Hero __instance, ref bool __result)
        {
            // Cheap check first: this runs for every hero the AI looks at.
            if (__result == _vanillaCanLeadParty || !Coop.InSession)
                return;
            if (IsPlayerClanMember(__instance))
                __result = _vanillaCanLeadParty;
        }

        // --- Gender enforcement (server) ---

        // Player led parties pass IsEligibleToCommand above, so they are never swapped or
        // disbanded. If the sweep still throws on the server, log it so the religion change
        // that called it completes.
        private static Exception EnforcementFinalizer(Exception __exception)
        {
            if (__exception == null || !Coop.OnServer)
                return __exception;
            SubModule.Log("Burning Empires gender enforcement failed on the server and was skipped: " + __exception);
            return null;
        }

        // --- Heir choice (server) ---

        // Burning Empires' own prefix picks an adult male heir for every clan except the main
        // clan. Player clans keep the vanilla heir choice.
        private static bool HeirPrefix(Clan clan, ref bool __result)
        {
            if (!Coop.OnServer || !Coop.IsPlayerClan(clan))
                return true;
            __result = true;
            return false;
        }

        // --- Champions (server) ---

        private static bool EligibleOwnerPrefix(Hero hero, ref bool __result)
        {
            if (hero == null || !Coop.OnServer)
                return true;
            if (!Coop.IsPlayerHero(hero) && !Coop.IsPlayerClan(hero.Clan))
                return true;
            __result = false;
            return false;
        }

        // --- Government (server) ---

        private static bool IsPlayerRulerPrefix(Kingdom kingdom, ref bool __result)
        {
            if (!Coop.OnServer)
                return true;
            var ruling = kingdom?.RulingClan;
            __result = ruling != null && ruling.Kingdom == kingdom && Coop.IsPlayerClan(ruling);
            return false;
        }

        // --- Religion assignment (server) ---

        private static bool AssignReligionPrefix(Hero hero)
        {
            if (!Coop.OnServer || !Coop.IsPlayerHero(hero))
                return true;
            GiveNoReligionIfMissing(hero);
            return false;
        }

        private static bool TryAssignRuntimeReligionPrefix(Hero hero, ref bool __result)
        {
            if (!Coop.OnServer || !Coop.IsPlayerHero(hero))
                return true;
            __result = GiveNoReligionIfMissing(hero);
            return false;
        }

        // Same as Burning Empires does for the main hero: keep a religion already set,
        // otherwise store "None". Returns false when the world data is not ready.
        private static bool GiveNoReligionIfMissing(Hero hero)
        {
            if (_worldDataCurrent == null || _tryGetHeroReligion == null || _setReligion == null)
                return false;
            if (string.IsNullOrWhiteSpace(hero.StringId))
                return false;
            try
            {
                var data = _worldDataCurrent.Invoke(null, null);
                if (data == null)
                    return false;
                var args = new object[] { data, hero, null };
                if ((bool)_tryGetHeroReligion.Invoke(null, args))
                    return true;
                _setReligion.Invoke(null, new object[] { data, hero, "None" });
                return true;
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not set the religion of player hero " + hero.StringId + ": " + ex.Message);
                return false;
            }
        }

        // --- Null safe Hero.MainHero ---

        private static IEnumerable<CodeInstruction> SafeMainHeroTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.MainHero));
            var safe = AccessTools.Method(typeof(PlayerExemptions), nameof(SafeMainHero));
            foreach (var instruction in instructions)
            {
                if (getter != null && instruction.Calls(getter))
                    yield return new CodeInstruction(OpCodes.Call, safe).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                else
                    yield return instruction;
            }
        }

        // The party filter inside ReplaceOrDisbandIneligibleCommanders is a compiler made
        // lambda; its name contains the method name.
        private static int PatchLambdas(Harmony harmony, Type owner, string methodName)
        {
            if (owner == null)
                return 0;
            var count = 0;
            var transpiler = new HarmonyMethod(typeof(PlayerExemptions), nameof(SafeMainHeroTranspiler));
            var types = new[] { owner }.Concat(owner.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public));
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (!method.Name.StartsWith("<" + methodName + ">", StringComparison.Ordinal) || method.ContainsGenericParameters)
                        continue;
                    try
                    {
                        harmony.Patch(method, transpiler: transpiler);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        SubModule.Log("Could not hook " + type.Name + "." + method.Name + ": " + ex.Message);
                    }
                }
            }
            if (count == 0)
                SubModule.Log("Could not find the party filter of " + owner.Name + "." + methodName + ".");
            return count;
        }

        // --- Helpers ---

        private static Type FindType(Assembly burningEmpires, string fullName)
        {
            Type type = null;
            try { type = burningEmpires?.GetType(fullName, false); }
            catch { }
            type = type ?? AccessTools.TypeByName(fullName);
            if (type == null)
                SubModule.Log("Could not find Burning Empires type " + fullName + ".");
            return type;
        }

        private static int Patch(Harmony harmony, Type type, string name, Type[] parameters,
            string prefix = null, string transpiler = null, string finalizer = null)
        {
            if (type == null)
                return 0;
            var method = AccessTools.Method(type, name, parameters);
            if (method == null)
            {
                SubModule.Log("Could not find Burning Empires " + type.Name + "." + name + ".");
                return 0;
            }
            try
            {
                harmony.Patch(method,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(PlayerExemptions), prefix) { priority = Priority.High },
                    transpiler: transpiler == null ? null : new HarmonyMethod(typeof(PlayerExemptions), transpiler),
                    finalizer: finalizer == null ? null : new HarmonyMethod(typeof(PlayerExemptions), finalizer));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not hook Burning Empires " + type.Name + "." + name + ": " + ex.Message);
                return 0;
            }
        }
    }
}
