using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace BurningEmpiresCoopCompat
{
    // Makes Burning Empires safe in Bannerlord Coop (built against Burning Empires 2.0.2 for
    // Bannerlord 1.4.8). Same idea as Diplomacy.CoopCompat: the server runs the campaign, so
    // Burning Empires' world changing logic runs there only, its buttons that would only change
    // a player's PC are greyed out with a reason, and pop ups meant for "the player" are answered
    // by the server. Single player and the main menu are untouched.
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "coodsz.burningempires.coopcompat";
        private static readonly object Gate = new object();
        private static bool _patched;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            var harmony = new Harmony(HarmonyId);
            SafeInstall("EarlyServerGuards", () => EarlyServerGuards.Install(harmony));
            SafeInstall("FreshCampaign", () => FreshCampaign.Install(harmony));
            SafeInstall("PackageDepth", () => PackageDepth.Install(harmony));
            SafeInstall("FreshCampaignCounts", () => FreshCampaignCounts.Install(harmony));
            SafeInstall("DistanceCacheRedirect", () => DistanceCacheRedirect.Install(harmony));
            SafeInstall("MapBounds", () => MapBounds.Install(harmony));
            SafeInstall("ClanHomeFix", () => ClanHomeFix.Install(harmony));
            SafeInstall("PathQueryGuard", () => PathQueryGuard.Install(harmony));
            SafeInstall("ServerCrashTrace", () => ServerCrashTrace.Install(harmony));
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
            Coop.ModulesLoaded();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            HeadlessBanners.OnGameStart(game);
        }

        private static void TryPatch(Assembly assembly)
        {
            if (_patched || assembly == null || !IsBurningEmpires(assembly))
                return;
            lock (Gate)
            {
                if (_patched)
                    return;
                _patched = true;
            }

            try
            {
                BeDecisionSync.Register(assembly);
                var count = Patches.Apply(new Harmony(HarmonyId), assembly);
                Log("Burning Empires coop patch ready (" + count + " hooks).");
            }
            catch (Exception ex)
            {
                Log("Burning Empires coop patch could not hook Burning Empires: " + ex.Message);
            }
        }

        private static bool IsBurningEmpires(Assembly assembly)
        {
            string name;
            try { name = assembly.GetName().Name ?? ""; }
            catch { return false; }
            return name == "BurningEmpires" && assembly.GetType("BurningEmpires.SubModule", false) != null;
        }

        // A setup step that throws must never take the game down while modules load.
        private static void SafeInstall(string name, Action install)
        {
            try { install(); }
            catch (Exception ex) { Log(name + " could not be set up: " + ex); }
        }

        internal static void Log(string text)
        {
            var line = "[BurningEmpires.CoopCompat] " + text;
            try { Debug.Print(line); }
            catch { }
            // The server window captures the console, so the line goes there too.
            try { Console.WriteLine(line); }
            catch { }
            // The rgl log can lose its end or be gone, so the line also goes to a file of its own.
            CompatLogFile.Write(line);
        }
    }
}
