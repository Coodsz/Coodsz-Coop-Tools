using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace RealmOfThronesCoopCompat
{
    // After SandBox reads the campaign XMLs on the server's fresh campaign, the log says how many
    // clans, kingdoms and settlements loaded (Realm of Thrones 8.1.8 has about 241 clans, 28
    // kingdoms and 1,066 settlements). Only objects whose XML node was read count; ones only named
    // by another node stay unready and are dropped later. No clans means the Factions XML failed
    // (missing banner colours are the known cause) and the start will not survive.
    // Adapted from Burning Empires Coop Compat.
    internal static class FreshCampaignCounts
    {
        public static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(SandBoxManager), "InitializeSandboxXMLs", new[] { typeof(bool) });
                if (target == null)
                {
                    SubModule.Log("Fresh campaign: SandBoxManager.InitializeSandboxXMLs not found; no XML counts.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(FreshCampaignCounts), nameof(InitializeSandboxXmlsPostfix)));
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign could not hook SandBoxManager.InitializeSandboxXMLs: " + ex.Message);
            }
        }

        private static void InitializeSandboxXmlsPostfix(bool __0)
        {
            if (!FreshCampaign.FreshStartActive || __0)
                return;
            try
            {
                var clans = Ready<Clan>();
                var kingdoms = Ready<Kingdom>();
                var settlements = Ready<Settlement>();
                SubModule.Log("Fresh campaign: XMLs loaded with clans " + clans + ", kingdoms " + kingdoms + ", settlements " + settlements + ".");
                if (clans == 0)
                    SubModule.Log("!!! Fresh campaign: NO CLANS LOADED. The Factions XML failed, the new campaign cannot start. See the XML load error above.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not count the loaded clans: " + ex.Message);
            }
        }

        private static int Ready<T>() where T : MBObjectBase
        {
            var count = 0;
            var list = MBObjectManager.Instance?.GetObjectTypeList<T>();
            if (list == null)
                return 0;
            foreach (var item in list)
            {
                if (item != null && item.IsReady)
                    count++;
            }
            return count;
        }
    }
}
