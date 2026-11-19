using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;

namespace BurningEmpiresCoopCompat
{
    // The Burning Empires team does not want elephants in tournaments. A fighter's mount comes
    // only from the match equipment that SandBox's TournamentFightMissionController.PrepareForMatch
    // clones from the town culture's tournament template, right before StartMatch spawns the
    // agents (SkipMatch uses it too). The postfix takes elephant mounts and elephant harnesses
    // off that equipment, so a template that names an elephant spawns the fighter on foot.
    // BETournamentModel's reward lists take be_ mounts and armor by value, which can offer an
    // elephant harness as a prize, so elephant gear is dropped from those lists too.
    // Only while a co-op session runs; single player is untouched.
    internal static class NoTournamentElephants
    {
        private const string ControllerType = "SandBox.Tournaments.MissionLogics.TournamentFightMissionController";
        private const string ModelType = "BurningEmpires.Models.BETournamentModel";

        private static FieldInfo _match;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            try
            {
                var controller = AccessTools.TypeByName(ControllerType);
                var prepare = controller == null ? null : AccessTools.DeclaredMethod(controller, "PrepareForMatch");
                _match = controller == null ? null : AccessTools.Field(controller, "_match");
                if (prepare == null || _match == null)
                {
                    SubModule.Log("Tournament elephants: TournamentFightMissionController.PrepareForMatch or _match not found.");
                }
                else
                {
                    harmony.Patch(prepare, postfix: new HarmonyMethod(typeof(NoTournamentElephants), nameof(PrepareForMatchPostfix)));
                    count++;
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Tournament elephants: could not patch the match equipment: " + ex.Message);
            }

            try
            {
                var model = burningEmpires.GetType(ModelType, false);
                var postfix = new HarmonyMethod(typeof(NoTournamentElephants), nameof(RewardPostfix));
                foreach (var name in new[] { "GetRegularRewardItems", "GetEliteRewardItems" })
                {
                    var method = model == null ? null : AccessTools.DeclaredMethod(model, name);
                    if (method == null)
                    {
                        SubModule.Log("Tournament elephants: BETournamentModel." + name + " not found.");
                        continue;
                    }
                    harmony.Patch(method, postfix: postfix);
                    count++;
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Tournament elephants: could not patch the reward lists: " + ex.Message);
            }
            return count;
        }

        private static void PrepareForMatchPostfix(object __instance)
        {
            try
            {
                if (!Coop.InSession || !(_match.GetValue(__instance) is TournamentMatch match))
                    return;
                int fighters = 0, mounts = 0, harnesses = 0;
                foreach (var participant in match.Participants)
                {
                    var equipment = participant?.MatchEquipment;
                    if (equipment == null)
                        continue;
                    var mount = equipment[EquipmentIndex.Horse].Item;
                    var harness = equipment[EquipmentIndex.HorseHarness].Item;
                    var clear = TournamentElephantRules.FighterSlotsToClear(mount?.StringId, mount?.HorseComponent?.Monster?.StringId, harness?.StringId);
                    if (!clear.Mount && !clear.Harness)
                        continue;
                    fighters++;
                    if (clear.Mount && mount != null)
                        mounts++;
                    if (clear.Harness && harness != null)
                        harnesses++;
                    if (clear.Mount)
                        equipment[EquipmentIndex.Horse] = EquipmentElement.Invalid;
                    if (clear.Harness)
                        equipment[EquipmentIndex.HorseHarness] = EquipmentElement.Invalid;
                }
                // One line per match that had an elephant, so the log shows the guard at work.
                if (fighters > 0)
                    SubModule.Log(TournamentElephantRules.MatchLine(fighters, mounts, harnesses));
            }
            catch (Exception ex)
            {
                SubModule.Log("Tournament elephants: could not check the match equipment: " + ex.Message);
            }
        }

        private static void RewardPostfix(List<ItemObject> __result)
        {
            try
            {
                if (__result == null || !Coop.InSession)
                    return;
                var removed = new List<string>();
                __result.RemoveAll(item =>
                {
                    if (!IsElephantPrize(item))
                        return false;
                    removed.Add(item.StringId);
                    return true;
                });
                if (removed.Count > 0)
                    SubModule.Log(TournamentElephantRules.PrizeLine(removed));
            }
            catch (Exception ex)
            {
                SubModule.Log("Tournament elephants: could not check the tournament prizes: " + ex.Message);
            }
        }

        private static bool IsElephantPrize(ItemObject item) =>
            item != null && TournamentElephantRules.IsElephantPrize(item.StringId, item.HorseComponent?.Monster?.StringId,
                item.IsMountable || item.Type == ItemObject.ItemTypeEnum.HorseHarness);
    }
}
