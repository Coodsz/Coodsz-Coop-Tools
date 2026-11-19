using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Library;

namespace CoopMarriage;

[HarmonyPatch(typeof(FightTournamentGame), "GetParticipantCharacters")]
internal static class TournamentFamilyCompatibility
{
	private static void Postfix(FightTournamentGame __instance, Settlement settlement,
		ref MBList<CharacterObject> __result)
	{
		MarriageFamilyBehavior.Current?.AddTournamentFamily(__instance, __result);
	}
}
