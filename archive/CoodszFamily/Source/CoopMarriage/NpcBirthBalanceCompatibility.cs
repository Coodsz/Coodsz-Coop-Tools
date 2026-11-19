using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace CoopMarriage;

internal static class NpcBirthBalanceCompatibility
{
	internal const int MaxNpcChildren = 3;

	[HarmonyPatch(typeof(PregnancyCampaignBehavior), "RefreshSpouseVisit")]
	private static class NativeConceptionPatch
	{
		[HarmonyPrefix]
		private static bool Prefix(Hero hero)
		{
			if (!CoopCompatibility.IsAuthoritative || !AtNpcLimit(hero, hero?.Spouse))
			{
				return true;
			}
			return false;
		}
	}

	internal static bool IsNpcFamily(Hero mother, Hero father)
	{
		return mother != null && !MarriageFamilyBehavior.IsRecordedPlayer(mother) && !MarriageFamilyBehavior.IsRecordedPlayer(father);
	}

	internal static int LivingChildCount(Hero mother)
	{
		if (mother == null)
		{
			return 0;
		}
		int count = 0;
		foreach (Hero child in mother.Children)
		{
			if (child != null && child.IsAlive)
			{
				count++;
			}
		}
		return count;
	}

	internal static bool AtNpcLimit(Hero mother, Hero father)
	{
		return IsNpcFamily(mother, father) && LivingChildCount(mother) >= MaxNpcChildren;
	}
}
