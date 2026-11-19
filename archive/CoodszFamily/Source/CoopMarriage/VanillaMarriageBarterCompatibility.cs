using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;

namespace CoopMarriage;

internal static class VanillaMarriageBarterCompatibility
{
	private sealed class BarterSpouseState
	{
		internal Hero Player;

		internal Hero Spouse;
	}

	private static readonly FieldInfo HeroSpouseField = AccessTools.Field(typeof(Hero), "_spouse");

	private static bool _inMarriageBarterValidation;

	internal static void Install(Harmony harmony)
	{
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Expected O, but got Unknown
		//IL_009a: Expected O, but got Unknown
		System.Type type = AccessTools.TypeByName("TaleWorlds.CampaignSystem.CampaignBehaviors.RomanceCampaignBehavior");
		LogCapability("vanilla relative marriage condition", PatchPostfix(harmony, type, "conversation_player_eligible_for_marriage_with_hero_rltv_on_condition", "PlayerNominationConditionPostfix"));
		LogCapability("vanilla direct marriage condition", PatchPostfix(harmony, type, "conversation_player_eligible_for_marriage_with_conversation_hero_on_condition", "DirectMarriageConditionPostfix"));
		System.Type type2 = AccessTools.TypeByName("GameInterface.Services.Barters.Handlers.MarriageBarterHandler");
		MethodInfo val = ((type2 == (System.Type)null) ? null : AccessTools.Method(type2, "TryResolveMarriageContext", (System.Type[])null, (System.Type[])null));
		if (val != (MethodInfo)null)
		{
			harmony.Patch(val,
                prefix: new HarmonyMethod(typeof(VanillaMarriageBarterCompatibility), nameof(ResolveMarriageContextPrefix)),
                finalizer: new HarmonyMethod(typeof(VanillaMarriageBarterCompatibility), nameof(RestoreMarriageContext)));
		}
		LogCapability("GameInterface MarriageBarterHandler.TryResolveMarriageContext", val != null);
		System.Type type3 = AccessTools.TypeByName("GameInterface.Services.Heroes.RomanceFlow.RomanceAuthority");
		LogCapability("GameInterface RomanceAuthority.TryValidatePair", PatchPostfix(harmony, type3, "TryValidatePair", "ValidatePairPostfix"));
		LogCapability("GameInterface RomanceAuthority.TryValidateStateChange", PatchPostfix(harmony, type3, "TryValidateStateChange", "ValidateStateChangePostfix"));
		LogCapability("GameInterface RomanceAuthority.TryValidateMarriage", PatchPostfix(harmony, type3, "TryValidateMarriage", "ValidateMarriagePostfix"));
		RuntimeProof.Write("Vanilla second/third marriage barter compatibility installed");
	}

	private static bool PatchPostfix(Harmony harmony, System.Type type, string methodName, string postfixName)
	{
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected O, but got Unknown
		MethodInfo val = ((type == (System.Type)null) ? null : AccessTools.Method(type, methodName, (System.Type[])null, (System.Type[])null));
		MethodInfo val2 = AccessTools.Method(typeof(VanillaMarriageBarterCompatibility), postfixName, (System.Type[])null, (System.Type[])null);
		if (val != (MethodInfo)null && val2 != (MethodInfo)null)
		{
			harmony.Patch((MethodBase)(object)val, (HarmonyMethod)null, new HarmonyMethod(val2), (HarmonyMethod)null, (HarmonyMethod)null);
			return true;
		}
		return false;
	}

	private static void LogCapability(string name, bool available)
	{
		RuntimeProof.Write("[CoopMarriage] Optional capability " + name + " " + (available ? "available" : "unavailable; skipped"));
	}

	private static void ResolveMarriageContextPrefix(Hero playerHero, out BarterSpouseState __state)
	{
		__state = null;
		_inMarriageBarterValidation = true;
		if (playerHero != null && playerHero.Spouse != null && !(HeroSpouseField == (FieldInfo)null))
		{
			int num = MarriageFamilyBehavior.PartnerCount(playerHero);
			if (num >= 1 && num < 3)
			{
				__state = new BarterSpouseState
				{
					Player = playerHero,
					Spouse = playerHero.Spouse
				};
				HeroSpouseField.SetValue((object)playerHero, (object)null);
				RuntimeProof.Write("Marriage barter context extended for player=" + ((MBObjectBase)playerHero).StringId + "; partners=" + num);
			}
		}
	}

	private static Exception RestoreMarriageContext(BarterSpouseState __state, Exception __exception)
	{
		_inMarriageBarterValidation = false;
		if (__state != null && HeroSpouseField != (FieldInfo)null)
		{
			HeroSpouseField.SetValue((object)__state.Player, (object)__state.Spouse);
		}
        return __exception;
	}

	private static bool CanValidateCoopPair(Hero playerHero, Hero targetHero)
	{
		if (playerHero == null || targetHero == null || playerHero == targetHero)
		{
			return false;
		}
		if (!playerHero.IsAlive || !targetHero.IsAlive)
		{
			return false;
		}
		if (targetHero.Spouse != null && targetHero.Spouse != playerHero)
		{
			return false;
		}
		return MarriageFamilyBehavior.PartnerCount(playerHero) < 3;
	}

	private static void PlayerNominationConditionPostfix(ref bool __result)
	{
		if (!__result)
		{
			Hero mainHero = Hero.MainHero;
			if (mainHero != null && MarriageFamilyBehavior.PartnerCount(mainHero) < 3)
			{
				__result = Hero.OneToOneConversationHero != null;
			}
		}
	}

	private static void DirectMarriageConditionPostfix(ref bool __result)
	{
		if (!__result && CanValidateCoopPair(Hero.MainHero, Hero.OneToOneConversationHero))
		{
			__result = true;
		}
	}

	private static void ValidatePairPostfix(Hero playerHero, Hero targetHero, ref string reason, ref bool __result)
	{
		if (!__result && CanValidateCoopPair(playerHero, targetHero) && !string.IsNullOrEmpty(reason) && reason.IndexOf("unmarried", (StringComparison)5) >= 0)
		{
			RuntimeProof.Write("Coop unmarried pair restriction extended: " + ((MBObjectBase)playerHero).StringId + " + " + ((MBObjectBase)targetHero).StringId);
			reason = null;
			__result = true;
		}
	}

	private static void ValidateStateChangePostfix(Hero playerHero, Hero targetHero, Romance.RomanceLevelEnum requestedLevel, ref string reason, ref bool __result)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		if (!__result && (int)requestedLevel >= 4 && CanValidateCoopPair(playerHero, targetHero) && IsExistingSpouseRejection(reason))
		{
			RuntimeProof.Write(string.Concat(new string[6]
			{
				"Coop romance eligibility extended: ",
				((MBObjectBase)playerHero).StringId,
				" + ",
				((MBObjectBase)targetHero).StringId,
				"; level=",
				requestedLevel.ToString()
			}));
			reason = null;
			__result = true;
		}
	}

	private static void ValidateMarriagePostfix(Hero playerHero, Hero targetHero, ref string reason, ref bool __result)
	{
		if (!__result && _inMarriageBarterValidation && CanValidateCoopPair(playerHero, targetHero) && reason == "The couple has not agreed on marriage." && (int)Romance.GetRomanticLevel(playerHero, targetHero) == 1)
		{
			RuntimeProof.Write("Marriage barter retry after failed compatibility: " + ((MBObjectBase)playerHero).StringId + " + " + ((MBObjectBase)targetHero).StringId);
			reason = null;
			__result = true;
		}
		else if (!__result && CanValidateCoopPair(playerHero, targetHero) && IsExistingSpouseRejection(reason))
		{
			RuntimeProof.Write("Coop marriage eligibility extended: " + ((MBObjectBase)playerHero).StringId + " + " + ((MBObjectBase)targetHero).StringId);
			reason = null;
			__result = true;
		}
	}

	private static bool IsExistingSpouseRejection(string reason)
	{
		if (string.IsNullOrEmpty(reason))
		{
			return false;
		}
		if (reason.IndexOf("unmarried", (StringComparison)5) < 0)
		{
			return reason.IndexOf("eligible for marriage", (StringComparison)5) >= 0;
		}
		return true;
	}
}

