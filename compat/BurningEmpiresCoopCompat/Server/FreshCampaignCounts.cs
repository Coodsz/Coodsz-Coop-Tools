using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace BurningEmpiresCoopCompat
{
    // After SandBox reads the campaign XMLs on the server's fresh campaign, the console says how
    // many clans, kingdoms and settlements loaded. Only objects whose XML node was read count;
    // ones only named by another node stay unready and are dropped later. No clans means the
    // Factions XML failed (HeadlessBanners is the known cause) and the start will not survive.
    internal static class FreshCampaignCounts
    {
        public static void Install(Harmony harmony)
        {
            if (!FreshCampaign.IsDedicatedServerProcess())
                return;
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
                SubModule.Log("Fresh campaign XMLs loaded: clans " + clans + " kingdoms " + kingdoms + " settlements " + settlements);
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
