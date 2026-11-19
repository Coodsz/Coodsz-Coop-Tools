using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace CoopMarriage;

internal static class EncyclopediaCompatibility
{
	internal static void Install(Harmony h)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		System.Type type = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Items.EncyclopediaFamilyMemberVM");
		MethodInfo val = ((type == (System.Type)null) ? null : AccessTools.Method(type, "RefreshValues", (System.Type[])null, (System.Type[])null));
		MethodInfo val2 = AccessTools.Method(typeof(EncyclopediaCompatibility), "Postfix", (System.Type[])null, (System.Type[])null);
		if (val != (MethodInfo)null && val2 != (MethodInfo)null)
		{
			h.Patch((MethodBase)(object)val, (HarmonyMethod)null, new HarmonyMethod(val2), (HarmonyMethod)null, (HarmonyMethod)null);
			RuntimeProof.Write("Encyclopedia family role patch installed");
		}
	}

	private static void Postfix(object __instance)
	{
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected O, but got Unknown
		if (__instance != null)
		{
			System.Type type = __instance.GetType();
			FieldInfo val = AccessTools.Field(type, "_baseHero");
			PropertyInfo val2 = AccessTools.Property(type, "Hero");
			PropertyInfo val3 = AccessTools.Property(type, "Role");
			Hero a = (val == null) ? null : val.GetValue(__instance) as Hero;
			Hero b = (val2 == null) ? null : val2.GetValue(__instance, null) as Hero;
			if (val3 != (PropertyInfo)null && val3.CanWrite && MarriageFamilyBehavior.IsSecondaryPartner(a, b))
			{
				val3.SetValue(__instance, (object)((object)new TextObject("{=coop_family_co_wife}Co-wife", (Dictionary<string, object>)null)).ToString(), (object[])null);
			}
		}
	}
}

