using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace CoopMarriage;

[HarmonyPatch(typeof(ChangeRomanticStateAction), "Apply")]
internal static class VanillaMarriageCompatibilityPatch
{
	private static bool Prefix(Hero person1, Hero person2, Romance.RomanceLevelEnum toWhat, out bool __state)
	{
        __state = false;
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		if ((int)toWhat != 7 || !CoopCompatibility.IsAuthoritative)
		{
			return true;
		}
        if (person1 == null || person2 == null) { __state = true; return false; }
		Hero val = MarriageFamilyBehavior.ResolveRecordedPlayer(person1, person2) ?? ((!person1.IsFemale && person2.IsFemale) ? person1 : ((!person2.IsFemale && person1.IsFemale) ? person2 : person1));
		Hero val2 = ((val == person1) ? person2 : person1);
		if (MarriageFamilyBehavior.PartnerCount(val) >= 3 && !MarriageFamilyBehavior.IsSecondaryPartner(val, val2) && val2 != val.Spouse)
		{
			RuntimeProof.Write("Vanilla marriage blocked: three-partner limit reached");
            __state = true;
			return false;
		}
		return true;
	}

	private static void Postfix(Hero person1, Hero person2, Romance.RomanceLevelEnum toWhat, bool __state)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		if (!__state && person1 != null && person2 != null && (int)toWhat == 7 && CoopCompatibility.IsAuthoritative)
		{
			Hero val = MarriageFamilyBehavior.ResolveRecordedPlayer(person1, person2) ?? ((!person1.IsFemale && person2.IsFemale) ? person1 : ((!person2.IsFemale && person1.IsFemale) ? person2 : person1));
			Hero s = ((val == person1) ? person2 : person1);
			MarriageFamilyBehavior.Current?.RecordPartner(val, s);
		}
	}
}

