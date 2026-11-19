using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;

namespace CoopMarriage;

// Keep TaleWorlds' native age/child/clan-scaled probability. This postfix only
// applies COOP Family's same-party and MCM enable/child-limit eligibility rules.
internal static class VanillaPregnancyCompatibility
{
	internal static void Install(Harmony harmony)
	{
		System.Type model = AccessTools.TypeByName("TaleWorlds.CampaignSystem.GameComponents.DefaultPregnancyModel");
		MethodInfo chance = model == null ? null : AccessTools.Method(model, "GetDailyChanceOfPregnancyForHero");
		if (chance == null)
		{
			RuntimeProof.Write("WARNING: vanilla pregnancy chance method was not found");
			return;
		}
		harmony.Patch(chance, postfix: new HarmonyMethod(AccessTools.Method(typeof(VanillaPregnancyCompatibility), nameof(ApplyCoopEligibility))));
		RuntimeProof.Write("Vanilla pregnancy rates active; COOP same-party eligibility installed");
	}

	private static void ApplyCoopEligibility(Hero hero, ref float __result)
	{
		if (hero == null || !Settings.Current.EnablePregnancies || hero.Age > 52f ||
			hero.Spouse == null || Settings.Current.MaximumChildrenPerMother <= 0 ||
			hero.Children.Count >= Settings.Current.MaximumChildrenPerMother)
		{
			__result = 0f;
			return;
		}

		// Only player/COOP marriages require both partners to be in the same party.
		// Ordinary NPC couples keep TaleWorlds' native pregnancy eligibility so the
		// mod does not suppress births throughout Calradia.
		bool coopPlayerCouple = MarriageFamilyBehavior.IsRecordedPlayer(hero) ||
			MarriageFamilyBehavior.IsRecordedPlayer(hero.Spouse);
		if (coopPlayerCouple && (hero.PartyBelongedTo == null ||
			hero.PartyBelongedTo != hero.Spouse.PartyBelongedTo))
		{
			__result = 0f;
		}
	}
}