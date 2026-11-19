using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BurningEmpiresCoopCompat
{
    // On a player's PC in a co-op session the server owns the world. Burning Empires still runs
    // its campaign behaviours there, so a client would spawn its own bandits and wanderers, move
    // champions, convert and migrate people and roll religions on its own, and drift away from
    // the server. Each method below returns at once on a player's PC. The bookkeeping listeners
    // (hero created, killed, occupation changed, the migration cache reset, champion TryEnable)
    // keep running so BE's lookups still work. The server and single player run unchanged.
    internal static class ClientBehaviorGates
    {
        private sealed class Gate
        {
            public readonly string Type;
            public readonly string Method;
            public readonly Type[] Args;

            public Gate(string type, string method, params Type[] args)
            {
                Type = type;
                Method = method;
                Args = args;
            }
        }

        private static readonly Gate[] Gates =
        {
            new Gate("BurningEmpires.CampaignBehaviors.BEWandererCampaignBehavior", "DailyTick"),
            new Gate("BurningEmpires.CampaignBehaviors.BEWandererCampaignBehavior", "WeeklyTick"),
            new Gate("BurningEmpires.CampaignBehaviors.BEWandererCampaignBehavior", "TryInitializeHistoricalRoster"),
            new Gate("BurningEmpires.CampaignBehaviors.BEBanditSpawnCampaignBehavior", "DailyTick"),
            new Gate("BurningEmpires.BESystems.Champions.BEChampionCampaignBehavior", "ReconcileAllSlots", typeof(bool)),
            new Gate("BurningEmpires.BESystems.Champions.BEChampionCampaignBehavior", "ReconcileReleasedChampions"),
            new Gate("BurningEmpires.BESystems.Champions.BEChampionCampaignBehavior", "ReconcileChampionAttachment", typeof(string), typeof(Hero), typeof(Hero)),
            new Gate("BurningEmpires.BESystems.Champions.BEChampionCampaignBehavior", "ReleaseSlot", typeof(string), typeof(Clan)),
            new Gate("BurningEmpires.BESystems.Champions.BEChampionCampaignBehavior", "TryResolveOwnerAfterDeath", typeof(string), typeof(Hero)),
            new Gate("BurningEmpires.BESystems.Demography.BEClassMobilityCampaignBehavior", "OnDailyTick"),
            new Gate("BurningEmpires.BESystems.Migration.BEMigrationCampaignBehavior", "OnDailyTick"),
            new Gate("BurningEmpires.BESystems.Conversion.BEPopulationConversionCampaignBehavior", "OnDailyTick"),
            new Gate("BurningEmpires.BESystems.Religion.BEReligionLifecycleCampaignBehavior", "OnHeroCreated", typeof(Hero), typeof(bool)),
            new Gate("BurningEmpires.BESystems.Religion.BEReligionLifecycleCampaignBehavior", "OnHourlyTick"),
            new Gate("BurningEmpires.CampaignBehaviors.BEMercenaryClanLoadRepairBehavior", "OnGameLoadFinished"),
            new Gate("BurningEmpires.BESystems.Recruitment.BERecruitmentCampaignBehavior", "OnMobilePartyCreated", typeof(MobileParty)),
            new Gate("BurningEmpires.BESystems.QuestCharacters.BEQuestCharacterSelectionCampaignBehavior", "OnTick", typeof(float)),
            new Gate("BurningEmpires.BESystems.HistoricalCampaign.BEHistoricalCampaignGoalQuest", "DailyTick"),
            new Gate("BurningEmpires.BESystems.HistoricalCampaign.BEHistoricalCampaignGoalQuest", "OnPlayerCharacterChanged", typeof(Hero), typeof(Hero), typeof(MobileParty), typeof(bool)),
            new Gate("BurningEmpires.BESystems.Relations.BEFirstMeetingRelationCampaignBehavior", "OnConversationEnded", typeof(IEnumerable<CharacterObject>)),
        };

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var prefix = new HarmonyMethod(typeof(ClientBehaviorGates), nameof(SkipOnPlayerPc));
            var count = 0;
            foreach (var gate in Gates)
            {
                try
                {
                    var type = burningEmpires.GetType(gate.Type, false);
                    if (type == null)
                    {
                        SubModule.Log("Client gate: type " + gate.Type + " not found.");
                        continue;
                    }
                    var method = AccessTools.DeclaredMethod(type, gate.Method, gate.Args);
                    if (method == null)
                    {
                        SubModule.Log("Client gate: " + type.Name + "." + gate.Method + " not found.");
                        continue;
                    }
                    harmony.Patch(method, prefix: prefix);
                    count++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("Client gate: could not patch " + gate.Type + "." + gate.Method + ": " + ex.Message);
                }
            }
            return count;
        }

        // Returning false skips the original. A skipped bool method returns false, which for
        // ReconcileChampionAttachment means "not attached", the harmless answer.
        private static bool SkipOnPlayerPc() => !Coop.OnPlayerPc;
    }
}
