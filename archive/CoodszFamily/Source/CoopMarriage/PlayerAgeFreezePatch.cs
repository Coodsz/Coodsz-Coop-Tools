using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;

namespace CoopMarriage;

// Runs only after the campaign is already playing. New-world creation reads
// every hero age, and freezing ages during that pass crashed the dedicated server.
[HarmonyPatch(typeof(Hero), "get_Age")]
internal static class PlayerAgeFreezePatch
{
	[HarmonyPostfix]
	private static void Postfix(Hero __instance, ref float __result)
	{
		if (__instance == null || Campaign.Current == null || !Campaign.Current.GameStarted)
		{
			return;
		}
		string id = ((MBObjectBase)__instance).StringId;
		bool playerHero = id != null && id.StartsWith("Hero_Player", StringComparison.Ordinal);
		if (!playerHero && !(__instance == Hero.MainHero && CoopCompatibility.IsPlayerHero(__instance)))
		{
			return;
		}
		MarriageFamilyBehavior current = MarriageFamilyBehavior.Current;
		if (current != null)
		{
			__result = current.GetOrCapturePlayerAge(__instance, __result);
		}
	}
}
