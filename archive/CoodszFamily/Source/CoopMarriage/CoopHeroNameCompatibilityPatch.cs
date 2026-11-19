using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace CoopMarriage;

// Coop serializes Hero.SetName before Bannerlord has finished constructing a
// newborn hero. Bannerlord may omit firstName on that path, while Coop's
// HeroChangeNameData calls ToString() on both names without a null check.
// Normalize only the missing half before Coop observes the call.
[HarmonyPatch(typeof(Hero), nameof(Hero.SetName))]
[HarmonyPriority(Priority.First)]
internal static class CoopHeroNameCompatibilityPatch
{
	private static void Prefix(ref TextObject fullName, ref TextObject firstName)
	{
		if (fullName == null && firstName != null)
		{
			fullName = firstName;
			RuntimeProof.Write("Normalized missing newborn full name before Coop synchronization");
		}
		else if (firstName == null && fullName != null)
		{
			firstName = fullName;
			RuntimeProof.Write("Normalized missing newborn first name before Coop synchronization");
		}
	}
}
