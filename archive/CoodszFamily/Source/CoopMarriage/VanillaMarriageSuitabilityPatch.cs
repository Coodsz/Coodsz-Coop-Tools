using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace CoopMarriage;

[HarmonyPatch(typeof(DefaultMarriageModel), "IsCoupleSuitableForMarriage")]
internal static class VanillaMarriageSuitabilityPatch
{
	private sealed class SpouseState
	{
		internal Hero Player;

		internal Hero Spouse;
	}

	private static readonly FieldInfo SpouseField = AccessTools.Field(typeof(Hero), "_spouse");

	private static void Prefix(Hero __0, Hero __1, out SpouseState __state)
	{
		__state = null;
		Hero val = ((__0 == Hero.MainHero) ? __0 : ((__1 == Hero.MainHero) ? __1 : MarriageFamilyBehavior.ResolveRecordedPlayer(__0, __1)));
		Hero val2 = ((val == __0) ? __1 : __0);
		if (val != null && val2 != null && !(SpouseField == (FieldInfo)null) && MarriageFamilyBehavior.PartnerCount(val) < 3 && (val2.Spouse == null || val2.Spouse == val) && val.Spouse != null)
		{
			__state = new SpouseState
			{
				Player = val,
				Spouse = val.Spouse
			};
			SpouseField.SetValue((object)val, (object)null);
		}
	}

	private static System.Exception Finalizer(SpouseState __state, System.Exception __exception)
	{
		if (__state != null && SpouseField != (FieldInfo)null)
		{
			SpouseField.SetValue((object)__state.Player, (object)__state.Spouse);
		}
        return __exception;
	}
}
