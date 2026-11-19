// Coodsz War Sync: the TAOM server tool's small server part of the War of the Ring sync. It rides in
// DedicatedServer.Windows as an extra SubModule (like Coop Save Rescue), so the sealed TAOM module list and
// the saves stay as they are and Coop's join check never sees it. It loads CoodszSettingsSync.Coop.dll from
// its own bin folder in War of the Ring only mode: no settings sync, no server rules, no commands.
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace CoodszWarSync
{
    public sealed class WarSyncSubModule : MBSubModuleBase
    {
        private static CoodszSettingsSync.SettingsSyncLoader? _loader;
        private static bool _decided;
        private static bool _tickBegun;

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            Begin();
        }

        public override void OnGameInitializationFinished(TaleWorlds.Core.Game game)
        {
            base.OnGameInitializationFinished(game);
            Begin();
        }

        // The Improved Garrisons party size hand back (compat\ImprovedGarrisonsCoopCompat\PartySizeHandBack.cs), so
        // TAOM's party sizes and the tool's party settings count with Improved Garrisons on. Players run the same file
        // in the client add on. It does not depend on the War of the Ring part stepping aside.
        protected override void OnGameStart(TaleWorlds.Core.Game game, TaleWorlds.Core.IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (GameNetwork.IsDedicatedServer)
                Coodsz.IgPartySize.PartySizeHandBack.TryInstall(ServerLog);
        }

        private static void ServerLog(string line)
        {
            try { Console.WriteLine("[Coodsz] " + line); }
            catch (Exception) { }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            // Coop builds its handlers once, when the server starts hosting, and a slow server hosts before the
            // module screen callbacks above come, so the first tick decides too. One try here, so it logs once.
            if (!_decided && !_tickBegun)
            {
                _tickBegun = true;
                Begin();
            }
            _loader?.Tick(dt);
        }

        private static void Begin()
        {
            try
            {
                if (!_decided)
                {
                    if (!GameNetwork.IsDedicatedServer) return;
                    _decided = true;
                    // Every module's DLL is loaded before the first tick and the first module screen, so Coodsz Server Tools shows up here if it is in the list.
                    var names = AppDomain.CurrentDomain.GetAssemblies().Select(SafeName);
                    if (!WarSyncRules.ShouldRun(true, names))
                    {
                        Log(WarSyncRules.StepAsideLine);
                        return;
                    }
                    string bin = System.IO.Path.GetDirectoryName(typeof(WarSyncSubModule).Assembly.Location) ?? ".";
                    string module = System.IO.Path.GetFullPath(System.IO.Path.Combine(bin, "..", ".."));
                    _loader = new CoodszSettingsSync.SettingsSyncLoader(false, bin, module, Log, "StartWarOnly");
                }
                _loader?.TryLoad();
            }
            catch (Exception e)
            {
                Log("War of the Ring sync disabled: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static string? SafeName(Assembly assembly)
        {
            try { return assembly.IsDynamic ? null : assembly.GetName().Name; }
            catch (Exception) { return null; }
        }

        private static void Log(string line)
        {
            try { Console.WriteLine(WarSyncRules.LogPrefix + line); }
            catch (Exception) { }
        }
    }
}
