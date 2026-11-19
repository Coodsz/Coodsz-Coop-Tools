// Optional, read-only evidence for RBM's "incompatible save" warning.
// The RBM campaign assembly is deliberately not a build dependency. We inspect
// its registered behavior after the authoritative AD1259 campaign reaches the map,
// and report the flags RBM itself uses. No economy values or settings are changed.

using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AD1259CoopCompat
{
    internal static class RbmEconomyDiagnostics
    {
        internal const string BehaviorTypeName = "RBMCampaign.RBMSettlementWealthCampaignBehavior";
        private static Campaign _campaign;
        private static bool _reported;
        private static DateTime _readySinceUtc;

        public static void TryReport()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null || !GameNetwork.IsDedicatedServer) return;
            if (!ReferenceEquals(campaign, _campaign))
            {
                _campaign = campaign;
                _reported = false;
                _readySinceUtc = default(DateTime);
            }
            if (_reported) return;
            if (Env.IsSet("AD1259COOP_DISABLE_RBM_DIAGNOSTICS")) { _reported = true; return; }

            // Behavior flags can still be unset during OnAfterGameInitializationFinished.
            // Wait for character creation/session events, behavior data loading and the
            // game manager's final ready flag before drawing a conclusion.
            if (FreshCampaign.Active || MBGameManager.Current == null || !MBGameManager.Current.IsLoaded) return;
            GameStateManager states = Game.Current == null ? null : Game.Current.GameStateManager;
            if (states == null || !(states.ActiveState is MapState)) return;

            try
            {
                MapProvider provider = MapSettlements.Provider;
                if (provider == null || !string.Equals(provider.ModuleId, "AD1259", StringComparison.OrdinalIgnoreCase) ||
                    !MapProvider.IsModuleActive("RBM"))
                {
                    _reported = true;
                    return;
                }

                if (_readySinceUtc == default(DateTime)) _readySinceUtc = DateTime.UtcNow;
                if (campaign.CampaignBehaviorManager == null) return;
                bool? campaignEnabled = ReadCampaignEnabled();
                CampaignBehaviorBase behavior = campaign.GetCampaignBehaviors<CampaignBehaviorBase>()
                    .FirstOrDefault(b => b != null && b.GetType().FullName == BehaviorTypeName);
                if (behavior == null)
                {
                    if (campaignEnabled == false)
                    {
                        _reported = true;
                        Log.Info(RbmCampaignPolicy.Installed
                            ? "RBM economy (server): the AD1259 Coop policy disabled RBM Campaign; its settlement-wealth " +
                              "behavior is not registered."
                            : "RBM economy (server): RBM Campaign features are disabled in the server's loaded configuration; " +
                              "its settlement-wealth behavior is not registered.");
                        return;
                    }
                    // Give optional modules time to register late behaviors; a missing
                    // behavior means there is no seeding state we can safely inspect.
                    if ((DateTime.UtcNow - _readySinceUtc).TotalSeconds < 30) return;
                    _reported = true;
                    if (campaignEnabled == true)
                    {
                        Log.Warn("RBM economy (server): RBM Campaign is enabled, but its settlement-wealth behavior is missing " +
                                 "from the ready campaign; the campaign's seeding state is unavailable.");
                    }
                    else
                    {
                        Log.Info("RBM economy (server): RBM is active, but its settlement-wealth behavior is not registered; " +
                                 "RBM Campaign features may be disabled. The campaign's seeding state is unavailable.");
                    }
                    return;
                }

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type type = behavior.GetType();
                FieldInfo seededField = type.GetField("_campaignSeeded", flags);
                FieldInfo newField = type.GetField("_newCampaign", flags);
                _reported = true;
                if (seededField == null || newField == null || seededField.FieldType != typeof(bool) ||
                    newField.FieldType != typeof(bool))
                {
                    Log.Info("RBM economy (server): the registered RBM wealth behavior has an unsupported diagnostic shape; " +
                             "the campaign's seeding state is unavailable.");
                    return;
                }

                bool seeded = (bool)seededField.GetValue(behavior);
                bool newCampaign = (bool)newField.GetValue(behavior);
                if (RbmCampaignPolicy.Installed)
                {
                    Log.Warn("RBM economy (server): the disable policy is active, but an RBM settlement-wealth behavior " +
                             "was already registered; restart the server with the compat module enabled before hosting.");
                }
                string detail = " (_campaignSeeded=" + seeded.ToString().ToLowerInvariant() +
                                ", _newCampaign=" + newCampaign.ToString().ToLowerInvariant() +
                                ", RBM Campaign enabled=" + (campaignEnabled.HasValue
                                    ? campaignEnabled.Value.ToString().ToLowerInvariant() : "unavailable") + ").";
                if (seeded)
                {
                    Log.Info("RBM economy (server): RBM marks this campaign seeded" + detail);
                }
                else if (!newCampaign)
                {
                    Log.Warn("RBM economy (server): RBM marks this server campaign unseeded; its incompatible-save warning " +
                             "applies to the authoritative save" + detail);
                }
                else
                {
                    Log.Warn("RBM economy (server): RBM marks this as a new campaign but its persisted seeding flag is false; " +
                             "seeding cannot be confirmed" + detail);
                }
            }
            catch (Exception e)
            {
                _reported = true;
                Log.Info("RBM economy (server): diagnostic unavailable: " + FreshCampaign.Unwrap(e));
            }
        }

        private static bool? ReadCampaignEnabled()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "RBMConfig", StringComparison.OrdinalIgnoreCase)) continue;
                Type config = assembly.GetType("RBMConfig.RBMConfig", false);
                FieldInfo enabled = config == null ? null : config.GetField("rbmCampaignEnabled",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (enabled != null && enabled.FieldType == typeof(bool)) return (bool)enabled.GetValue(null);
            }
            return null;
        }
    }
}
