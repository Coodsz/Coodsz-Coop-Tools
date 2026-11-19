using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires features that only work for one player and have no way to reach the server
    // are switched off in a co-op session. On a player's PC they would change a world the server
    // never hears about:
    // - the champion duel, XML quests (start, givers, quest events, quest parties), the wonder
    //   stash, the temple convert line, hero conversion and the spouse child line
    // - the population console commands that change data (print, cap and stability still work)
    // Armor crafting is skipped on both sides: its events never register, so nothing ticks, while
    // SyncData still runs and saves keep their projects. The static Active stays null.
    // Single player runs unchanged.
    internal static class CoopDisabledFeatures
    {
        private const string Duel = "BurningEmpires.BESystems.Champions.BEChampionDuelBehavior";
        private const string QuestBehavior = "BurningEmpires.BESystems.QuestTemplates.BEQuestTemplateCampaignBehavior";
        private const string Quest = "BurningEmpires.BESystems.QuestTemplates.BEQuestTemplateQuest";
        private const string Wonder = "BurningEmpires.BESystems.Wonders.BEWonderMissionBehavior";
        private const string Temple = "BurningEmpires.BESystems.Religion.BETempleCampaignBehavior";
        private const string Conversion = "BurningEmpires.BESystems.Religion.BEHeroReligionConversionCampaignBehavior";
        private const string Spouse = "BurningEmpires.BESystems.Life.BESpousePregnancyDialogueBehavior";
        private const string Console = "BurningEmpires.BESystems.ConsoleCommands.BEPopulationConsoleCommands";
        private const string ArmorCrafting = "BurningEmpires.BESystems.Crafting.BEArmorCraftingCampaignBehavior";

        private const string NotInCoop = "{=!}Not available in co-op.";
        private const string ConsoleReply = "This command changes the world, so in co-op run it on the server.";
        private const string StashMessage = "The wonder stash is not available in co-op.";

        private static FieldInfo _openStashEventId;
        private static MethodInfo _stopMainAgentUsingStash;
        private static DateTime _lastStashMessage = DateTime.MinValue;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            var skip = new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(SkipOnPlayerPc));
            var falseResult = new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(FalseOnPlayerPc));
            var falseWithHint = new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(FalseWithHintOnPlayerPc));

            // Dialog lines: a false condition hides the line, a false clickable condition greys it out.
            count += Patch(harmony, burningEmpires, Duel, "CanOfferDuel", Type.EmptyTypes, postfix: falseResult);
            count += Patch(harmony, burningEmpires, Conversion, "CanOfferConversion", Type.EmptyTypes, postfix: falseResult);
            count += Patch(harmony, burningEmpires, Conversion, "CanStartConversion", new[] { typeof(TextObject).MakeByRefType() },
                postfix: new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(ConversionHintOnPlayerPc)));
            count += Patch(harmony, burningEmpires, Temple, "CanPlayerConvert", new[] { typeof(TextObject).MakeByRefType() }, postfix: falseWithHint);
            count += Patch(harmony, burningEmpires, Spouse, "CanConceiveWithConversationHero", Type.EmptyTypes, postfix: falseResult);

            // XML quests. A skipped bool method returns false ("no active objective"). The callers
            // pass fresh out locals, which stay null.
            count += Patch(harmony, burningEmpires, QuestBehavior, "StartXmlQuestFromFile", new[] { typeof(string) }, prefix: skip);
            count += Patch(harmony, burningEmpires, QuestBehavior, "TryGetActiveDuelObjective", null, prefix: skip);
            count += Patch(harmony, burningEmpires, QuestBehavior, "TryGetActivePoachersObjective", null, prefix: skip);
            count += Patch(harmony, burningEmpires, QuestBehavior, "TrySpawnGiversForCurrentLocation", Type.EmptyTypes, prefix: skip);
            // A quest carried over from single player stays frozen: none of its events listen.
            count += Patch(harmony, burningEmpires, Quest, "RegisterEvents", Type.EmptyTypes, prefix: skip);
            count += Patch(harmony, burningEmpires, Quest, "EnsurePartySpawnedNow", new[] { typeof(string) }, prefix: skip);
            count += Patch(harmony, burningEmpires, Quest, "StartBattleWithObjectiveParty", new[] { typeof(string) }, prefix: skip);

            // Wonder stash, also inside a town with an embedded wonder.
            var wonder = burningEmpires.GetType(Wonder, false);
            if (wonder != null)
            {
                _openStashEventId = AccessTools.Field(wonder, "_openStashEventId");
                _stopMainAgentUsingStash = AccessTools.Method(wonder, "StopMainAgentUsingStash", Type.EmptyTypes);
            }
            count += Patch(harmony, burningEmpires, Wonder, "OnGenericMissionEvent", null,
                prefix: new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(WonderStashPrefix)));

            // Population console commands that change data.
            var consoleReply = new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(ConsolePrefix));
            foreach (var command in new[]
            {
                "AddPopulation", "RemovePopulation", "ProcessConversion", "ProcessMigration",
                "ProcessClassMobility", "ApplyWarPopulationTest", "TestSetSettlementReligion",
            })
                count += Patch(harmony, burningEmpires, Console, command, new[] { typeof(List<string>) }, prefix: consoleReply);

            // Armor crafting: both sides in a session. SyncData is not touched.
            count += Patch(harmony, burningEmpires, ArmorCrafting, "RegisterEvents", Type.EmptyTypes,
                prefix: new HarmonyMethod(typeof(CoopDisabledFeatures), nameof(SkipInSession)));

            return count;
        }

        // args null means the method name is unique, so no overload is picked by argument types.
        private static int Patch(Harmony harmony, Assembly burningEmpires, string typeName, string methodName, Type[] args,
            HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            try
            {
                var type = burningEmpires.GetType(typeName, false);
                if (type == null)
                {
                    SubModule.Log("Co-op disable: type " + typeName + " not found.");
                    return 0;
                }
                var method = AccessTools.DeclaredMethod(type, methodName, args);
                if (method == null)
                {
                    SubModule.Log("Co-op disable: " + type.Name + "." + methodName + " not found.");
                    return 0;
                }
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Co-op disable: could not patch " + typeName + "." + methodName + ": " + ex.Message);
                return 0;
            }
        }

        private static bool SkipOnPlayerPc() => !Coop.OnPlayerPc;

        private static bool SkipInSession() => !Coop.InSession;

        private static void FalseOnPlayerPc(ref bool __result)
        {
            if (Coop.OnPlayerPc)
                __result = false;
        }

        private static void FalseWithHintOnPlayerPc(ref bool __result, ref TextObject explanation)
        {
            if (!Coop.OnPlayerPc)
                return;
            __result = false;
            explanation = new TextObject(NotInCoop);
        }

        private static void ConversionHintOnPlayerPc(ref bool __result, ref TextObject hint)
        {
            if (!Coop.OnPlayerPc)
                return;
            __result = false;
            hint = new TextObject(NotInCoop);
        }

        private static bool ConsolePrefix(ref string __result)
        {
            if (!Coop.OnPlayerPc)
                return true;
            __result = ConsoleReply;
            return false;
        }

        // Only the stash's own event is refused; other mission events go on to Burning Empires,
        // which ignores them. The player is moved off the stash so the event does not repeat.
        private static bool WonderStashPrefix(object __instance, GenericMissionEvent missionEvent)
        {
            try
            {
                if (!Coop.OnPlayerPc)
                    return true;
                var stashEventId = _openStashEventId?.GetValue(__instance) as string;
                if (!string.IsNullOrEmpty(stashEventId) && !string.Equals(missionEvent?.EventId, stashEventId, StringComparison.Ordinal))
                    return true;
                _stopMainAgentUsingStash?.Invoke(__instance, null);
                if ((DateTime.UtcNow - _lastStashMessage).TotalSeconds > 3)
                {
                    _lastStashMessage = DateTime.UtcNow;
                    InformationManager.DisplayMessage(new InformationMessage(StashMessage));
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Co-op disable: wonder stash check failed: " + ex.Message);
            }
            return false;
        }
    }
}
