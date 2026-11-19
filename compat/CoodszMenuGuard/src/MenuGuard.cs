using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

[assembly: AssemblyTitle("Coodsz Menu Guard")]
[assembly: AssemblyDescription("Keeps a settlement menu from freezing on a party Coop synced without its data")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace CoodszMenuGuard
{
    // Coop 0.1.6 sometimes syncs a party (an AI party or another player's) to a client without its full
    // data. Entering a village or town with that
    // party inside makes GameMenuPartyItemVM.RefreshProperties throw a NullReferenceException; the
    // settlement overlay rebuilds its party list every frame, so the game throws thousands of times
    // and the screen freezes (Escape and Enter do nothing). This lets that one entry stay blank
    // instead: the error is swallowed for that party, the rest of the list and the menu work.
    // The DedicatedServer. prefix keeps this module out of Coop's join check, so servers need nothing.
    public sealed class MenuGuardSubModule : MBSubModuleBase
    {
        private static bool _installed;
        private static int _caught;

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_installed || GameNetwork.IsDedicatedServer) return;
            _installed = true;
            try
            {
                var harmony = new Harmony("coodsz.coop.menu-guard");
                var guard = new HarmonyMethod(typeof(MenuGuardSubModule), nameof(SkipBrokenParty));
                var patched = 0;
                patched += Guard(harmony, guard, "TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.GameMenuPartyItemVM", "RefreshProperties");
                patched += Guard(harmony, guard, "TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.SettlementMenuOverlayVM", "UpdatePartyList");
                Log(patched == 2
                    ? "Installed: a broken party in a settlement list is skipped instead of freezing the menu."
                    : "Installed " + patched.ToString(CultureInfo.InvariantCulture) + " of 2 guards; the game version may differ.");
            }
            catch (Exception e)
            {
                Log("Could not install: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static int Guard(Harmony harmony, HarmonyMethod finalizer, string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName, Type.EmptyTypes);
            if (method == null)
            {
                Log(typeName + "." + methodName + " was not found.");
                return 0;
            }

            harmony.Patch(method, finalizer: finalizer);
            return 1;
        }

        // Harmony finalizer: returning null swallows the exception. Only the "missing data" kind is
        // swallowed; anything else still reaches the game as before.
        private static Exception SkipBrokenParty(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            if (!(__exception is NullReferenceException)) return __exception;
            _caught++;
            if (_caught == 1 || _caught == 100 || _caught % 10000 == 0)
                Log("Skipped a party with missing data in " + __originalMethod?.DeclaringType?.Name + "." + __originalMethod?.Name
                    + " (" + _caught.ToString(CultureInfo.InvariantCulture) + " times so far). The menu kept working.");
            return null;
        }

        private static void Log(string message)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "Client Diagnostics");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "CoodszMenuGuard.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
