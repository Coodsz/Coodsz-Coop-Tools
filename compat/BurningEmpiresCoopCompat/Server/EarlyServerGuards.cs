using System;
using HarmonyLib;

namespace BurningEmpiresCoopCompat
{
    // Guards that must be in place before Burning Empires' own OnSubModuleLoad runs, so this
    // module loads before Burning Empires and installs them from its OnSubModuleLoad.
    // - Burning Empires hands SandBox.View its map conversation provider while it loads. On a
    //   headless dedicated server SandBox.View never makes its instance, so that call throws and
    //   takes the server down. With no instance there is nothing to hand the provider to, so the
    //   call is skipped. With an instance (every player's PC) it runs as normal.
    internal static class EarlyServerGuards
    {
        private static System.Reflection.FieldInfo _sandBoxViewInstance;

        public static void Install(Harmony harmony)
        {
            var view = AccessTools.TypeByName("SandBox.View.SandBoxViewSubModule");
            var method = view == null ? null : AccessTools.Method(view, "SetMapConversationDataProvider");
            _sandBoxViewInstance = view == null ? null : AccessTools.Field(view, "_instance");
            if (method == null || _sandBoxViewInstance == null)
            {
                SubModule.Log("Early guards: SandBoxViewSubModule.SetMapConversationDataProvider not found; guard skipped.");
                return;
            }
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(EarlyServerGuards), nameof(MapConversationPrefix)));
            }
            catch (Exception ex)
            {
                SubModule.Log("Early guards: could not guard SetMapConversationDataProvider: " + ex.Message);
            }
        }

        private static bool MapConversationPrefix()
        {
            try
            {
                if (_sandBoxViewInstance.GetValue(null) != null)
                    return true;
            }
            catch
            {
                return true;
            }
            SubModule.Log("Early guards: no SandBox view on this machine (headless server), map conversation provider skipped.");
            return false;
        }
    }
}
