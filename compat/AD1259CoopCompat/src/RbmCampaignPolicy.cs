// AD1259 Coop uses RBM's combat/AI/items with its Campaign economy disabled.
// RBM reloads its settings at the start of OnGameStart, before installing its
// Campaign patches and behaviors. Override just that loaded setting after every
// LoadConfig call; leave RBM files and every other setting untouched.

using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace AD1259CoopCompat
{
    internal static class RbmCampaignPolicy
    {
        private static FieldInfo _campaignEnabled;
        private static bool _unsupported;

        public static bool Installed { get; private set; }

        public static void TryInstall(Harmony harmony)
        {
            if (Installed || _unsupported || harmony == null || !IsTargetProfile()) return;
            try
            {
                Type config = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(assembly.GetName().Name, "RBMConfig", StringComparison.OrdinalIgnoreCase)) continue;
                    config = assembly.GetType("RBMConfig.RBMConfig", false);
                    if (config != null) break;
                }
                if (config == null) return; // optional RBM assembly not loaded yet

                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo enabled = config.GetField("rbmCampaignEnabled", flags);
                MethodInfo load = config.GetMethod("LoadConfig", flags, null, Type.EmptyTypes, null);
                if (enabled == null || enabled.FieldType != typeof(bool) || enabled.IsInitOnly ||
                    load == null || load.ReturnType != typeof(void))
                {
                    _unsupported = true;
                    Log.Error("RBM Campaign policy could not find the supported rbmCampaignEnabled field and LoadConfig() " +
                              "hook; disable RBM Campaign in its configuration before hosting this profile.");
                    return;
                }

                _campaignEnabled = enabled;
                harmony.Patch(load, postfix: new HarmonyMethod(typeof(RbmCampaignPolicy), nameof(AfterLoadConfig))
                {
                    priority = Priority.Last
                });
                Installed = true;
                Apply(); // RBM's initial module-load config read has already happened
                Log.Info("RBM Campaign economy disabled for AD1259 Coop; RBM Combat, AI and item settings are retained. " +
                         "The policy also applies after RBM reloads its configuration.");
            }
            catch (Exception e)
            {
                _unsupported = true;
                Log.Error("RBM Campaign policy could not be installed: " + FreshCampaign.Unwrap(e));
            }
        }

        private static bool IsTargetProfile()
        {
            return GameNetwork.IsDedicatedServer && MapProvider.IsModuleActive("AD1259") &&
                   MapProvider.IsModuleActive("RBM") &&
                   (MapProvider.IsModuleActive("Coop") || MapProvider.IsModuleActive("CoopNightly"));
        }

        private static void AfterLoadConfig()
        {
            if (!IsTargetProfile()) return;
            try { Apply(); }
            catch (Exception e) { Log.Error("RBM Campaign policy could not be reapplied: " + FreshCampaign.Unwrap(e)); }
        }

        private static void Apply()
        {
            // This is the same switch RBM's configuration menu controls. Do not set
            // seeding flags or rewrite an established campaign's economy.
            _campaignEnabled.SetValue(null, false);
        }
    }
}
