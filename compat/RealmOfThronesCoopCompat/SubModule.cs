using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace RealmOfThronesCoopCompat
{
    // Runs Realm of Thrones (8.1.8 for Bannerlord 1.4.8) in Bannerlord Coop. On the dedicated server:
    // its own log and a crash trace, Realm of Thrones' campaign map in place of War Sails' one, a new
    // campaign from Realm of Thrones' XML in place of the server's vanilla template save, the gates
    // that refuse a world without Realm of Thrones' code or data, Realm of Thrones' own rules
    // (RotHost, COODSZ_ROT_HOST picks how far) and guards for what the server cannot survive. On a
    // player's PC in a Coop session: the load gate and the join probe. On both, our end of the shared
    // Coop link (RotLink) and the weapon gate (WeaponItemGate). Does nothing without Realm of Thrones' map, and nothing in single player.
    public sealed class SubModule : MBSubModuleBase
    {
        private static readonly HashSet<string> SeenWatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _running;
        private static bool _server;
        private static bool _gameStartLogged;

        // The engine builds every SubModule while it loads the module DLLs, before any OnSubModuleLoad
        // and before DedicatedServer.Windows adds Coop's resolver, so the server gets our Harmony
        // resolver here. This class names no Harmony type anywhere (fields, signatures, bodies), so
        // nothing in it needs Harmony to compile; Harmony is used only from the patch classes.
        public SubModule()
        {
            RegisterHarmonyOnServer();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterHarmonyOnServer()
        {
            try
            {
                // The folder first, so a player's game never builds the module list here.
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (RotRules.IsServerBinFolder(baseDirectory) && HarmonyLocator.ShouldRegister(baseDirectory, ActiveModules().Select(module => module.Key)))
                    ServerHarmony.Register();
            }
            catch
            {
                // A failed registration only means Harmony may be missing later, which is logged then.
            }
        }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            List<KeyValuePair<string, string>> modules;
            bool server;
            try
            {
                modules = ActiveModules();
                server = Coop.IsDedicatedServerProcess();
                if (!RotRules.ShouldRun(server, modules.Select(module => module.Key)))
                    return;
            }
            catch
            {
                // Without the module list there is no telling what runs; stay out of the way.
                return;
            }

            _running = true;
            _server = server;
            Log(RotRules.StartLine(Version(), server, modules, LoadedAssemblyNames()) + " Log file " + (CompatLogFile.CurrentPath() ?? "none") + ".");
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => OnAssemblyLoad(e.LoadedAssembly);
            SafeInstall("Settings", () => SettingsPins.Install(server));
            if (!server)
            {
                // Players read the joining hero's package too. Outside a session Coop never reads one.
                SafeInstall("PackageDepth", () => PackageDepth.Install());
                SafeInstall("ClientLoadGate", () => ClientLoadGate.Install());
                SafeInstall("CoopSessionEdges", () => CoopSessionEdges.Install());
                SafeInstall("CoopPatience", () => CoopPatience.Install());
                SafeInstall("ShipVisualBudget", () => ShipVisualBudget.Install());
                SafeInstall("JoinProbe", () => JoinProbe.Install(false));
                SafeInstall("ClientGates", () => ClientGates.Install());
                SafeInstall("WeaponItemGate", () => WeaponItemGate.Install());
                return;
            }

            SafeInstall("ServerCrashTrace", ServerCrashTrace.Install);
            SafeInstall("ServerCrashTrace", ServerCrashTrace.NoWarningReports);
            // NavalDLC in the module list is the server's War Sails switch: the server refuses to
            // boot when the two disagree.
            var warSails = RotRules.HasModule(modules.Select(module => module.Key), RotRules.WarSailsModuleId);
            ServerHarmony.Register();
            if (!ServerHarmony.Available())
                return;
            // Each lambda is only compiled when it runs, so nothing here needs Harmony before it was found.
            SafeInstall("TestLoopback", () => TestLoopback.Install());
            SafeInstall("MapRedirect", () => StartMapRedirect(warSails));
            SafeInstall("FreshCampaign", () => FreshCampaign.Install());
            SafeInstall("NavalVillageGuard", () => NavalVillageGuard.Install());
            SafeInstall("RotHost", () => RotHost.OnSubModuleLoad());
            SafeInstall("RotGuards", () => RotGuards.Install());
            SafeInstall("RotStory", () => RotStory.Install());
            SafeInstall("PathQueryGuard", () => PathQueryGuard.Install());
            SafeInstall("PackageDepth", () => PackageDepth.Install());
            SafeInstall("CoopSessionEdges", () => CoopSessionEdges.Install());
            SafeInstall("JoinProbe", () => JoinProbe.Install(true));
            SafeInstall("JoinPatience", () => JoinPatience.Install());
            SafeInstall("WeaponItemGate", () => WeaponItemGate.Install());
        }

        // Kept apart so this method is only compiled once Harmony was found.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartMapRedirect(bool warSails) => MapRedirect.Install(warSails);

        // Every module's DLLs are loaded once the first screen opens. The dedicated server may never
        // open one, so its first game start counts too. The game's action data is loaded by then
        // too, so a player's PC puts on the elephant gate here.
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (!_running)
                return;
            SafeInstall("Coop state", Coop.ModulesLoaded);
            if (_server)
                RotHostProbe.Run("OnBeforeInitialModuleScreenSetAsRoot");
            else
                SafeInstall("ClientGates", ClientGates.ActionsLoaded);
        }

        private static bool _actionIdsChecked;

        // Once per run, at the first mission: the hook action ids the game cached at its start
        // against the action table now. MISMATCH means something read them before the game loaded
        // its action data, and War Sails' hooks never throw in this run.
        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            if (!_running || _actionIdsChecked)
                return;
            _actionIdsChecked = true;
            try { Log(ActionIdsLine()); }
            catch (Exception ex) { Log("Action ids could not be checked: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // Kept apart so ActionIndexCache is only named once a mission runs.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ActionIdsLine()
        {
            var ready = ActionIndexCache.act_usage_hook_ready.Index;
            var release = ActionIndexCache.act_usage_hook_release.Index;
            var readyNow = MBAnimation.GetActionCodeWithName("act_usage_hook_ready");
            var releaseNow = MBAnimation.GetActionCodeWithName("act_usage_hook_release");
            return (ready == readyNow && release == releaseNow ? "Action ids: OK" : "!!! Action ids: MISMATCH")
                + ", act_usage_hook_ready cached " + ready + " table " + readyNow
                + ", act_usage_hook_release cached " + release + " table " + releaseNow + ".";
        }

        // The Realm of Thrones host's other hooks (RotHost). It only acts on the dedicated server.
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (!_running)
                return;
            // Every frame: a hook that throws is logged once, not once per frame.
            try
            {
                if (_server)
                    RotHost.OnApplicationTick();
                JoinProbe.OnApplicationTick();
                // The shared Coop link: started here, never from an AssemblyLoad callback (RotLink).
                RotLink.Tick();
            }
            catch (Exception ex)
            {
                if (!_tickFailureLogged)
                {
                    _tickFailureLogged = true;
                    Log("The application tick hook failed (logged once): " + ex);
                }
            }
        }

        private static bool _tickFailureLogged;

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            if (_server)
                SafeInstall("RotHost", () => RotHost.OnGameInitializationFinished(game));
        }

        public override void OnAfterGameInitializationFinished(Game game, object starterObject)
        {
            base.OnAfterGameInitializationFinished(game, starterObject);
            if (_server)
                SafeInstall("RotHost", () => RotHost.OnAfterGameInitializationFinished(game));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (!_running)
                return;
            SafeInstall("Coop state", Coop.ModulesLoaded);
            if (_server)
            {
                SafeInstall("HeadlessBanners", () => HeadlessBanners.OnGameStart(game));
                RotHostProbe.Run("OnGameStart");
                SafeInstall("RotHost", () => RotHost.OnGameStart(game, gameStarterObject));
            }
            if (_gameStartLogged)
                return;
            _gameStartLogged = true;
            Log("After game start: " + RotRules.AssemblyText(LoadedAssemblyNames()) + ".");
        }

        // Campaign.OnNewGameCreated calls this right after the campaign behaviors registered their
        // events and before the session starts.
        public override void OnNewGameCreated(Game game, object initializerObject)
        {
            base.OnNewGameCreated(game, initializerObject);
            if (!_running)
                return;
            SafeInstall("Coop state", Coop.CampaignStarted);
            if (_server)
                SafeInstall("HeadlessWeather", () => HeadlessWeather.OnNewGameCreated(game));
            SafeInstall("JoinProbe", () => JoinProbe.OnCampaign(game, false));
        }

        public override void OnGameLoaded(Game game, object initializerObject)
        {
            base.OnGameLoaded(game, initializerObject);
            if (!_running)
                return;
            SafeInstall("Coop state", Coop.CampaignStarted);
            SafeInstall("JoinProbe", () => JoinProbe.OnCampaign(game, true));
        }

        // The moment the engine loads one of Realm of Thrones' DLLs, with where it came from.
        private static void OnAssemblyLoad(Assembly assembly)
        {
            try
            {
                var name = assembly?.GetName().Name;
                if (name == null || !RotRules.IsWatched(name))
                    return;
                lock (SeenWatched)
                {
                    if (!SeenWatched.Add(name))
                        return;
                }
                string location;
                try { location = assembly.Location; }
                catch { location = "?"; }
                Log(name + ".dll loaded now, from " + location + ".");
            }
            catch { }
        }

        internal static List<KeyValuePair<string, string>> ActiveModules()
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var module in ModuleHelper.GetActiveModules())
            {
                if (module == null || !module.IsActive)
                    continue;
                string version;
                try { version = module.Version.ToString(); }
                catch { version = "?"; }
                result.Add(new KeyValuePair<string, string>(module.Id, version));
            }
            return result;
        }

        private static List<string> LoadedAssemblyNames()
        {
            var names = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { names.Add(assembly.GetName().Name); }
                catch { }
            }
            return names;
        }

        private static string Version()
        {
            try { return "v" + typeof(SubModule).Assembly.GetName().Version.ToString(3); }
            catch { return "v?"; }
        }

        // A setup step that throws must never take the game down while modules load.
        private static void SafeInstall(string name, Action install)
        {
            try { install(); }
            catch (Exception ex) { Log(name + " could not be set up: " + ex); }
        }

        internal static void Log(string text)
        {
            var line = "[RealmOfThrones.CoopCompat] " + text;
            try { Debug.Print(line); }
            catch { }
            // The server window captures the console, so the line goes there too.
            try { Console.WriteLine(line); }
            catch { }
            CompatLogFile.Write(line);
        }
    }
}
