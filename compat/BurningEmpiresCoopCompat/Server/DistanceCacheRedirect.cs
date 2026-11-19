using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Map.DistanceCache;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ModuleManager;

namespace BurningEmpiresCoopCompat
{
    // The settlement distance cache on the dedicated server.
    // The game reads ModuleData\DistanceCaches\settlements_distance_cache_Default.bin from the
    // last active module that has one, so on a PC Burning Empires' own cache wins. The dedicated
    // server takes the first module that has one, which is SandBox, and SandBox's cache only
    // knows Calradia's settlements. On the server only, NavigationCache<Settlement>.Deserialize
    // is pointed at the file the game would have picked (DistanceCacheRules). Players' PCs never
    // get the hook.
    internal static class DistanceCacheRedirect
    {
        private const string SandBoxModuleId = "Sandbox";

        public static void Install(Harmony harmony)
        {
            if (!FreshCampaign.IsDedicatedServerProcess())
                return;
            try
            {
                var target = AccessTools.Method(typeof(NavigationCache<Settlement>), "Deserialize", new[] { typeof(string) });
                if (target == null)
                {
                    SubModule.Log("Distance cache: NavigationCache.Deserialize not found; the server reads the cache it picks.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(DistanceCacheRedirect), nameof(DeserializePrefix)));
                SubModule.Log("Distance cache: dedicated server cache pick ready.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Distance cache could not hook NavigationCache.Deserialize: " + ex.Message);
            }
        }

        private static void DeserializePrefix(ref string __0)
        {
            try
            {
                var sandBox = ModuleHelper.GetModuleInfo(SandBoxModuleId)?.FolderPath;
                if (!DistanceCacheRules.IsInModuleCaches(__0, sandBox))
                    return;
                var picked = DistanceCacheRules.Redirect(__0, sandBox, ActiveModuleFolders(), File.Exists);
                if (picked == null)
                {
                    SubModule.Log("Distance cache: no module after SandBox has its own " + Path.GetFileName(__0)
                        + ", so SandBox's is read. Burning Empires' settlements may be missing from it: " + __0);
                    return;
                }
                SubModule.Log("Distance cache: server picked " + __0 + ", reading " + picked + " instead (the last module with one, as the game does).");
                __0 = picked;
            }
            catch (Exception ex)
            {
                SubModule.Log("Distance cache: could not check the server's cache pick, so it is read as it is: " + ex.Message);
            }
        }

        private static List<string> ActiveModuleFolders()
        {
            var result = new List<string>();
            foreach (var module in ModuleHelper.GetActiveModules())
            {
                if (module != null && module.IsActive && !string.IsNullOrEmpty(module.FolderPath))
                    result.Add(module.FolderPath);
            }
            return result;
        }
    }
}
