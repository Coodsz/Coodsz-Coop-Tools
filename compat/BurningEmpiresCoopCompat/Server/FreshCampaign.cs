using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Load;

namespace BurningEmpiresCoopCompat
{
    // Starts Burning Empires as a new campaign on the co-op dedicated server.
    // The Coodsz dedicated server hosts a save name. When no save has that name yet it copies
    // default_new_game.sav, a plain Calradia save, under it and loads it. A loaded save fills its
    // settlements from the XML by index instead of adding new ones, and Burning Empires'
    // settlements are not in that save, so Settlement.Deserialize throws, the rest of Burning
    // Empires' settlements.xml is skipped and the server later crashes in the distance cache.
    // On the dedicated server only, SandBoxSaveHelper.LoadGameAction (the one place Coop's
    // server loads its save) checks the save first. A byte copy of the template with no Burning
    // Empires module in its metadata is not loaded: a new campaign starts instead, as the
    // "Burning Empires: Sandbox" menu entry does, with character creation run headless
    // (HeadlessCreation). The server's first save then writes a real Burning Empires campaign
    // under the same name, which loads normally from then on. Any other save loads as before.
    // Single player and players' PCs never get the hook.
    internal static class FreshCampaign
    {
        private const string HistoricalStartupTypeName = "BurningEmpires.BESystems.HistoricalCampaign.BEHistoricalCampaignStartupContext";

        // Set once the server starts a new campaign in place of the template copy, so the
        // fresh start fixes (HeadlessBanners, FreshCampaignCounts) only run on that path.
        public static bool FreshStartActive { get; private set; }

        public static void Install(Harmony harmony)
        {
            if (!IsDedicatedServerProcess())
                return;
            try
            {
                var target = AccessTools.Method(typeof(SandBoxSaveHelper), "LoadGameAction",
                    new[] { typeof(SaveGameFileInfo), typeof(Action<LoadResult>), typeof(Action) });
                if (target == null)
                {
                    SubModule.Log("Fresh campaign: SandBoxSaveHelper.LoadGameAction not found; the server loads its save as before.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(LoadGameActionPrefix)));
                SubModule.Log("Fresh campaign: dedicated server save check ready.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign could not hook the server's save load: " + ex.Message);
            }
        }

        // Coop.OnServer is not known at module load, so the process is told by its bin folder,
        // or by the dedicated server's own host type when that is already loaded.
        internal static bool IsDedicatedServerProcess()
        {
            if (FreshCampaignRules.IsServerBinFolder(AppDomain.CurrentDomain.BaseDirectory))
                return true;
            return FindType("DedicatedServer.CoopServerHost") != null;
        }

        private static bool LoadGameActionPrefix(SaveGameFileInfo __0)
        {
            try
            {
                // Coop sets ModInformation.IsServer and its container before it loads the save.
                if (__0 == null || !Coop.OnServer || !IsTemplateCopy(__0))
                    return true;
                StartNewCampaign(__0.Name);
                return false;
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not start a new campaign, so the save loads as before: " + ex);
                return true;
            }
        }

        private static bool IsTemplateCopy(SaveGameFileInfo save)
        {
            string[] modules = null;
            try { modules = TaleWorlds.Core.MetaDataExtensions.GetModules(save.MetaData); }
            catch { }
            if (FreshCampaignRules.HasModule(modules, FreshCampaignRules.BurningEmpiresModuleId))
                return false;

            var savePath = SavePath(save.Name);
            var templates = FreshCampaignRules.TemplateCandidates(savePath, AppDomain.CurrentDomain.BaseDirectory, DsGameSavesFolder());
            if (FreshCampaignRules.IsTemplateCopy(modules, savePath, templates))
                return true;

            if (modules != null)
                SubModule.Log("Fresh campaign: save '" + save.Name + "' was made without Burning Empires and is not the server's template copy, "
                    + "so it is loaded as it is. Burning Empires cannot add its settlements to it; host a new save name for a new Burning Empires campaign.");
            return false;
        }

        private static void StartNewCampaign(string saveName)
        {
            SubModule.Log("Fresh campaign: save '" + saveName + "' is the server's copy of default_new_game.sav with no Burning Empires data; "
                + "starting a new Burning Empires campaign instead. The first server save writes it under the same name.");
            ResetHistoricalStartup();
            PublishGameLoaded(saveName);
            FreshStartActive = true;
            MBGameManager.StartNewGame(new HeadlessCampaignManager());
        }

        private static string SavePath(string saveName)
        {
            try { return FileHelper.GetFileFullPath(FileDriver.GetSaveFilePath(saveName + ".sav")); }
            catch { return null; }
        }

        // The dedicated server's Game Saves folder, from its own config (DsConfig.UserDir).
        private static string DsGameSavesFolder()
        {
            try
            {
                var config = FindType("DedicatedServer.DsConfig");
                var userDir = config == null ? null : AccessTools.Property(config, "UserDir")?.GetValue(null, null) as string;
                return string.IsNullOrEmpty(userDir) ? null : System.IO.Path.Combine(userDir, FileDriver.SaveDirectoryName);
            }
            catch
            {
                return null;
            }
        }

        // Burning Empires' own Sandbox entry clears its Historical Campaign startup first.
        private static void ResetHistoricalStartup()
        {
            try
            {
                var type = FindType(HistoricalStartupTypeName);
                AccessTools.Method(type, "Reset", Type.EmptyTypes)?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not clear Burning Empires' Historical Campaign startup: " + ex.Message);
            }
        }

        // A loaded save publishes Coop's GameLoaded with its name, which sets the save name the
        // server shuts down to and reads that save's co-op session file. The new campaign does
        // the same so the server goes on exactly as after a load.
        private static void PublishGameLoaded(string saveName)
        {
            try
            {
                var broker = FindType("Common.Messaging.MessageBroker");
                var message = FindType("GameInterface.Services.Heroes.Messages.GameLoaded");
                var instance = broker == null ? null : AccessTools.Property(broker, "Instance")?.GetValue(null, null);
                var publish = broker?.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Publish" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                if (instance == null || message == null || publish == null)
                {
                    SubModule.Log("Fresh campaign: Coop's GameLoaded message not found; the server's default shutdown save name stays unset.");
                    return;
                }
                publish.MakeGenericMethod(message).Invoke(instance, new[] { null, Activator.CreateInstance(message, saveName) });
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not publish Coop's GameLoaded: " + ex.Message);
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }
            return null;
        }
    }
}
