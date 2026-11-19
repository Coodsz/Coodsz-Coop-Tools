using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires rerolls every notable's volunteers from the settlement's population once a
    // day (BERecruitmentCampaignBehavior.OnDailyTickSettlement) and writes the slots straight
    // into Hero.VolunteerTypes. Coop only sends volunteers it saw change: its own postfix on the
    // vanilla daily update and the recruit commit. BE's reroll runs after that postfix, so the
    // players kept the vanilla troops while the server held BE's, and a recruit click picked a
    // troop the server did not have.
    // BE also sorts the slots with the empty ones last, so the postfix first puts the rerolled
    // troops back into the slots that held a troop before (VolunteerSlotRules), and a player's
    // open recruit screen still points at the right slots.
    // On the server this postfix sends the notables whose volunteers BE changed through Coop's
    // own VolunteersUpdated message, the same one Coop sends after the vanilla update. Coop
    // turns it into its volunteer snapshot for every player. Players never run this tick, and
    // single player is untouched.
    internal static class VolunteerPublish
    {
        private const string BehaviorType = "BurningEmpires.BESystems.Recruitment.BERecruitmentCampaignBehavior";
        private const string MessageType = "GameInterface.Services.MobileParties.Messages.VolunteersUpdated";
        private const string BrokerType = "Common.Messaging.MessageBroker";

        private static bool _resolved;
        private static ConstructorInfo _messageCtor;
        private static PropertyInfo _brokerInstance;
        private static MethodInfo _publish;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            try
            {
                var type = burningEmpires.GetType(BehaviorType, false);
                var method = type == null ? null : AccessTools.DeclaredMethod(type, "OnDailyTickSettlement", new[] { typeof(Settlement) });
                if (method == null)
                {
                    SubModule.Log("Volunteer sync: BERecruitmentCampaignBehavior.OnDailyTickSettlement not found.");
                    return 0;
                }
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(VolunteerPublish), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(VolunteerPublish), nameof(Postfix)));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Volunteer sync: could not patch the daily volunteer reroll: " + ex.Message);
                return 0;
            }
        }

        // Copies each notable's slots before BE rerolls them, so only real changes are sent.
        private static void Prefix(Settlement settlement, out Dictionary<Hero, CharacterObject[]> __state)
        {
            __state = null;
            if (settlement == null || !Coop.OnServer)
                return;
            __state = new Dictionary<Hero, CharacterObject[]>();
            foreach (var hero in Notables(settlement))
                __state[hero] = (CharacterObject[])hero.VolunteerTypes.Clone();
        }

        private static void Postfix(Settlement settlement, Dictionary<Hero, CharacterObject[]> __state)
        {
            if (__state == null)
                return;
            try
            {
                var changed = new Dictionary<Hero, CharacterObject[]>();
                foreach (var hero in Notables(settlement))
                {
                    __state.TryGetValue(hero, out var before);
                    // BE's sort moved the empty slots last; put them back where the players'
                    // recruit screens still have them, before anything is sent.
                    var kept = VolunteerSlotRules.KeepSlotPositions(before, hero.VolunteerTypes);
                    if (kept != null)
                        Array.Copy(kept, hero.VolunteerTypes, kept.Length);
                    if (before != null && before.SequenceEqual(hero.VolunteerTypes))
                        continue;
                    changed[hero] = (CharacterObject[])hero.VolunteerTypes.Clone();
                }
                if (changed.Count > 0)
                    Publish(changed);
            }
            catch (Exception ex)
            {
                SubModule.Log("Volunteer sync: could not send " + settlement.StringId + "'s volunteers: " + ex.Message);
            }
        }

        private static IEnumerable<Hero> Notables(Settlement settlement)
        {
            var notables = settlement.Notables;
            if (notables == null)
                yield break;
            foreach (var hero in notables)
            {
                if (hero?.VolunteerTypes != null)
                    yield return hero;
            }
        }

        private static void Publish(Dictionary<Hero, CharacterObject[]> changed)
        {
            Resolve();
            if (_messageCtor == null || _brokerInstance == null || _publish == null)
                return;
            var broker = _brokerInstance.GetValue(null, null);
            if (broker == null)
                return;
            var message = _messageCtor.Invoke(new object[] { changed });
            _publish.Invoke(broker, new[] { null, message });
        }

        // Coop's message and broker live in GameInterface and Common; both are loaded once a
        // session runs, so they are looked up on the first send.
        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;

            Type message = null, broker = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = assembly.GetName().Name ?? ""; }
                catch { continue; }
                if (name == "GameInterface")
                    message = message ?? assembly.GetType(MessageType, false);
                else if (name == "Common")
                    broker = broker ?? assembly.GetType(BrokerType, false);
                if (message != null && broker != null)
                    break;
            }

            _messageCtor = message == null ? null : AccessTools.Constructor(message, new[] { typeof(Dictionary<Hero, CharacterObject[]>) });
            _brokerInstance = broker == null ? null : AccessTools.Property(broker, "Instance");
            var publish = broker == null ? null : broker.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Publish" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            _publish = publish == null || message == null ? null : publish.MakeGenericMethod(message);
            if (_messageCtor == null || _brokerInstance == null || _publish == null)
                SubModule.Log("Volunteer sync: could not find Coop's VolunteersUpdated message; players keep the volunteers Coop sends.");
        }
    }
}
