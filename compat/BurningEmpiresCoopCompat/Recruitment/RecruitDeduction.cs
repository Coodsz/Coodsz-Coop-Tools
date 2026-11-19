using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BurningEmpiresCoopCompat
{
    // In single player Burning Empires takes one adult from the settlement's population for each
    // recruit, from the vanilla OnTroopRecruited event and from its own RecruitmentVM.OnDone
    // patch. In co-op a player's recruit click is sent to the server, and Coop commits it in
    // TroopRosterInterface.HandleOnRecruitmentDone without either of those: it empties the
    // volunteer slot and adds the troop straight to the party. So player recruits never cost the
    // town any people while AI recruits did.
    // On the server this wraps that commit. The prefix notes which cart slots still hold a
    // volunteer, the postfix counts the slots Coop emptied (none when the player could not pay)
    // and calls BE's own DeductRecruits for each troop, from the notable's settlement, like the
    // vanilla event would. Only players' requests reach that method, and single player is
    // untouched.
    internal static class RecruitDeduction
    {
        private const string InterfaceType = "GameInterface.Services.TroopRosters.Interfaces.TroopRosterInterface";
        private const string PopulationService = "BurningEmpires.BESystems.Recruitment.BERecruitmentPopulationService";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _patched;
        private static MethodInfo _deductRecruits;
        private static FieldInfo _objectManager;
        private static MethodInfo _tryGetHero;
        private static MethodInfo _tryGetCharacter;
        private static FieldInfo _heroId, _characterId, _troopIndex;

        private sealed class Slot
        {
            public Hero Notable;
            public int Index;
            public CharacterObject Troop;
        }

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var service = burningEmpires.GetType(PopulationService, false);
            _deductRecruits = service == null ? null : AccessTools.Method(service, "DeductRecruits",
                new[] { typeof(Settlement), typeof(CharacterObject), typeof(int) });
            if (_deductRecruits == null)
            {
                SubModule.Log("Recruit deduction: BERecruitmentPopulationService.DeductRecruits not found.");
                return 0;
            }

            _harmony = harmony;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (TryPatch(assembly))
                    return 1;
            }
            // Coop's GameInterface can load after Burning Empires; patch it when it does.
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly);
            return 0;
        }

        private static bool TryPatch(Assembly assembly)
        {
            if (_patched || assembly == null)
                return _patched;
            string name;
            try { name = assembly.GetName().Name ?? ""; }
            catch { return false; }
            if (name != "GameInterface")
                return false;
            lock (Gate)
            {
                if (_patched)
                    return true;
                _patched = true;
            }

            try
            {
                var type = assembly.GetType(InterfaceType, false);
                var method = type == null ? null : AccessTools.DeclaredMethod(type, "HandleOnRecruitmentDone");
                _objectManager = type == null ? null : AccessTools.Field(type, "objectManager");
                if (method == null || _objectManager == null)
                {
                    SubModule.Log("Recruit deduction: Coop's TroopRosterInterface.HandleOnRecruitmentDone not found; player recruits won't lower population.");
                    return true;
                }
                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(RecruitDeduction), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(RecruitDeduction), nameof(Postfix)));
                SubModule.Log("Recruit deduction: hooked Coop's recruitment commit.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Recruit deduction: could not patch Coop's recruitment commit: " + ex.Message);
            }
            return true;
        }

        // __args[1] is Coop's TroopInfo[] cart (hero id, troop id, slot index), read by name.
        private static void Prefix(object __instance, object[] __args, out List<Slot> __state)
        {
            __state = null;
            if (!Coop.OnServer)
                return;
            try
            {
                if (!(__args.Length > 1 && __args[1] is Array cart) || cart.Length == 0)
                    return;
                var objectManager = _objectManager.GetValue(__instance);
                if (objectManager == null || !ResolveCoop(objectManager, cart.GetType().GetElementType()))
                    return;

                __state = new List<Slot>();
                foreach (var entry in cart)
                {
                    var heroId = _heroId.GetValue(entry) as string;
                    var troopId = _characterId.GetValue(entry) as string;
                    var notable = Find(objectManager, _tryGetHero, heroId) as Hero;
                    var troop = Find(objectManager, _tryGetCharacter, troopId) as CharacterObject;
                    var index = (int)_troopIndex.GetValue(entry);
                    var slots = notable?.VolunteerTypes;
                    if (troop == null || slots == null)
                        continue;
                    // Coop skips an entry whose slot is empty on the server, so the player loses
                    // that recruit; log why, so a live test shows which slots moved.
                    if (index < 0 || index >= slots.Length || slots[index] == null)
                    {
                        SubModule.Log("Recruit deduction: Coop will skip " + heroId + " slot " + index + ", the player sent "
                            + troopId + " but the server slot holds " + (index < 0 || index >= slots.Length ? "nothing (out of range)" : "null")
                            + "; slots " + Describe(slots) + ".");
                        continue;
                    }
                    // Coop adds the player's troop anyway, whatever the server's slot holds.
                    if (slots[index] != troop)
                        SubModule.Log("Recruit deduction: " + heroId + " slot " + index + " holds " + slots[index].StringId
                            + " on the server but the player sent " + troopId + "; Coop adds " + troopId + "; slots " + Describe(slots) + ".");
                    __state.Add(new Slot { Notable = notable, Index = index, Troop = troop });
                }
            }
            catch (Exception ex)
            {
                __state = null;
                SubModule.Log("Recruit deduction: could not read the recruit cart: " + ex.Message);
            }
        }

        private static void Postfix(List<Slot> __state)
        {
            if (__state == null || __state.Count == 0)
                return;
            try
            {
                // A slot Coop emptied is a recruit it committed. When the player could not pay
                // Coop returns before the commit and every slot is still full.
                var committed = __state.Where(slot => slot.Notable.VolunteerTypes[slot.Index] == null)
                    .GroupBy(slot => new { Settlement = slot.Notable.CurrentSettlement ?? slot.Notable.HomeSettlement, slot.Troop });
                foreach (var group in committed)
                {
                    var settlement = group.Key.Settlement;
                    if (settlement == null)
                        continue;
                    var count = group.Count();
                    var deducted = (bool)_deductRecruits.Invoke(null, new object[] { settlement, group.Key.Troop, count });
                    SubModule.Log("Recruit deduction: " + count + " " + group.Key.Troop.StringId + " from " + settlement.StringId
                        + (deducted ? ", population lowered." : ", no matching adult population."));
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Recruit deduction: could not lower population: " + ex.Message);
            }
        }

        private static string Describe(CharacterObject[] slots) =>
            "[" + string.Join(", ", slots.Select(slot => slot?.StringId ?? "null")) + "]";

        private static object Find(object objectManager, MethodInfo tryGet, string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            var args = new object[] { id, null };
            return (bool)tryGet.Invoke(objectManager, args) ? args[1] : null;
        }

        // Coop's IObjectManager.TryGetObject<T>(string id, out T obj) and the TroopInfo fields.
        private static bool ResolveCoop(object objectManager, Type troopInfo)
        {
            if (_tryGetHero != null)
                return true;
            var generic = objectManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "TryGetObject" && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
            if (generic == null || troopInfo == null)
                return false;
            _heroId = AccessTools.Field(troopInfo, "RecruiterHeroId");
            _characterId = AccessTools.Field(troopInfo, "CharacterObjectId");
            _troopIndex = AccessTools.Field(troopInfo, "TroopIndex");
            if (_heroId == null || _characterId == null || _troopIndex == null)
                return false;
            _tryGetCharacter = generic.MakeGenericMethod(typeof(CharacterObject));
            _tryGetHero = generic.MakeGenericMethod(typeof(Hero));
            return true;
        }
    }
}
