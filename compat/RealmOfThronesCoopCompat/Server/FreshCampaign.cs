using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Load;

namespace RealmOfThronesCoopCompat
{
    // Starts Realm of Thrones as a new campaign on the co-op dedicated server, adapted from Burning
    // Empires Coop Compat. When no save has the hosted name yet, the nightly server copies its
    // vanilla template (default_new_game_warsails.sav, or default_new_game.sav without War Sails)
    // under that name and loads it. A loaded save fills its settlements from the XML by index
    // instead of adding new ones, so Realm of Thrones' settlements were skipped (boot test 3:
    // ArgumentOutOfRangeException in Settlement.Deserialize), the world kept Calradia's, and the
    // server died reading Realm of Thrones' distance cache.
    // On the dedicated server only, SandBoxSaveHelper.LoadGameAction (where Coop's server loads
    // its save, GameStateInterface.ForceLoadSave) checks the save first (FreshCampaignRules). A
    // byte copy of a template with no Realm of Thrones module in its metadata is not loaded: a
    // new campaign starts in its place from Realm of Thrones' XML, with character creation run
    // headless (HeadlessCreation). It starts inside the load call, so the server has its game
    // manager when the host call returns, as it checks right then. The server's first save then
    // writes a real Realm of Thrones campaign under the same name, which loads normally from
    // then on. A save whose readable header lists no Realm of Thrones module and that is not a
    // template copy is refused with one line; one whose header cannot be read loads with a loud
    // line; any other save loads as before.
    internal static class FreshCampaign
    {
        // Set once the server starts a new campaign in place of the template copy, so the fresh
        // start fixes (HeadlessBanners, FreshCampaignCounts) only run on that path.
        public static bool FreshStartActive { get; private set; }

        private static bool _noSessionLogged;

        public static void Install()
        {
            var harmony = new Harmony(MapRedirect.HarmonyId);
            var target = AccessTools.Method(typeof(SandBoxSaveHelper), "LoadGameAction",
                new[] { typeof(SaveGameFileInfo), typeof(Action<LoadResult>), typeof(Action) });
            if (target == null)
            {
                SubModule.Log("Fresh campaign: SandBoxSaveHelper.LoadGameAction not found; the server loads its save as before.");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(LoadGameActionPrefix)));
            SubModule.Log("Fresh campaign: the server's save load is checked for its vanilla template copy.");
            FreshCampaignCounts.Install(harmony);
            CountRotSettlements();
        }

        // How many settlements Realm of Thrones' map defines, counted from the active ROT_Map
        // module's XML when the modules load; the world check compares the new world with it.
        // -1 when it cannot be counted.
        public static int ExpectedSettlements { get; private set; } = -1;

        private static void CountRotSettlements()
        {
            try
            {
                var folder = TaleWorlds.ModuleManager.ModuleHelper.GetModuleInfo(RotRules.MapModuleId)?.FolderPath;
                var files = FreshCampaignRules.SettlementFiles.Select(name => folder == null ? null : System.IO.Path.Combine(folder, "ModuleData", name))
                    .Where(path => path != null && System.IO.File.Exists(path)).ToList();
                if (files.Count == 0)
                {
                    SubModule.Log("Fresh campaign: Realm of Thrones' settlements.xml not found in " + (folder ?? "the ROT_Map module") + "; the world check cannot compare the settlement count.");
                    return;
                }
                var count = 0;
                foreach (var file in files)
                {
                    using (var reader = System.IO.File.OpenText(file))
                        count += FreshCampaignRules.CountSettlements(reader);
                }
                ExpectedSettlements = count;
                SubModule.Log("Fresh campaign: Realm of Thrones' map defines " + count + " settlements (" + string.Join(", ", files) + ").");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not count Realm of Thrones' settlements: " + ex.Message);
            }
        }

        private static bool LoadGameActionPrefix(SaveGameFileInfo __0)
        {
            if (__0 == null)
                return true;
            // Not known what it is (no Coop session, or the check failed): treated as a save whose
            // header cannot be read, so it never starts a new world but the ROT gate still applies.
            var kind = FreshCampaignRules.SaveKind.Unknown;
            try
            {
                // Coop sets ModInformation.IsServer and its container before it loads the save.
                if (Coop.OnServer)
                    kind = Check(__0);
                else if (!_noSessionLogged)
                {
                    _noSessionLogged = true;
                    SubModule.Log("Fresh campaign: Coop's server session is not running while save '" + __0.Name + "' loads, so it is not checked; only the ROT gate applies.");
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("!!! Fresh campaign: could not check save '" + __0.Name + "' (" + ex.GetType().Name + ": " + ex.Message + "), so it counts as one whose header cannot be read.");
            }

            // The ROT gate, for every kind of save (FreshCampaignRules.DecideLoad). A refusal starts
            // nothing, so the server reports a save that did not start loading (exit 11 on server
            // builds from 2026-11-11, a load timeout before).
            var asked = RotHostRules.Level(Environment.GetEnvironmentVariable(RotRules.HostVariable), out _);
            string refusal;
            try
            {
                var ready = RotHost.TimeModelReady(out var timeProblem);
                refusal = RotHostRules.WorldRefusal(asked, RotHost.LoadFailed, RotHost.Running, ready, timeProblem, string.Join("; ", RotHost.BootFailures()));
            }
            catch (Exception ex)
            {
                // A gate that cannot be checked refuses where it counts.
                refusal = "the ROT gate could not be checked (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
            var action = FreshCampaignRules.DecideLoad(kind, __0.Name, asked, refusal, out var line);
            if (line != null)
                SubModule.Log(line);
            if (action == FreshCampaignRules.LoadAction.Refuse)
            {
                try { Console.Out.Flush(); }
                catch { }
                return false;
            }
            if (action == FreshCampaignRules.LoadAction.LoadAsIs)
                return true;

            try
            {
                SubModule.Log("Fresh campaign: starting a new Realm of Thrones campaign for save '" + __0.Name + "'.");
                PublishGameLoaded(__0.Name);
                FreshStartActive = true;
                MBGameManager.StartNewGame(new HeadlessCampaignManager());
                SubModule.Log("Fresh campaign: the new campaign's game manager is in place; it loads from Realm of Thrones' XML now.");
                return false;
            }
            catch (Exception ex)
            {
                // Loading the blank vanilla save instead is the known crash on Realm of Thrones' map,
                // so nothing loads and the server reports a save that did not start loading.
                FreshStartActive = false;
                SubModule.Log("!!! Fresh campaign: could not start a new campaign for save '" + __0.Name + "', and its blank vanilla copy is not loaded instead: " + ex);
                return false;
            }
        }

        private static FreshCampaignRules.SaveKind Check(SaveGameFileInfo save)
        {
            string[] modules = null;
            try { modules = TaleWorlds.Core.MetaDataExtensions.GetModules(save.MetaData); }
            catch { }
            var savePath = SavePath(save.Name);
            var templates = FreshCampaignRules.TemplateCandidates(savePath, AppDomain.CurrentDomain.BaseDirectory);
            var kind = FreshCampaignRules.Classify(modules, savePath, templates, out var template);
            SubModule.Log(FreshCampaignRules.DecisionLine(kind, save.Name, savePath, modules, template));
            return kind;
        }

        // The server points the game's user folder at its data dir, so this is <data dir>\Game Saves.
        private static string SavePath(string saveName)
        {
            try { return FileHelper.GetFileFullPath(FileDriver.GetSaveFilePath(saveName + ".sav")); }
            catch { return null; }
        }

        // A loaded save publishes Coop's GameLoaded with its name (a postfix on SaveManager.Load,
        // which the skipped load never reaches). That sets the save name the server shuts down to
        // and reads the save's co-op session file, so the new campaign publishes it too and the
        // server goes on exactly as after a load.
        private static void PublishGameLoaded(string saveName)
        {
            try
            {
                var broker = Coop.FindType("Common.Messaging.MessageBroker");
                var message = Coop.FindType("GameInterface.Services.Heroes.Messages.GameLoaded");
                var instance = broker?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                var publish = broker?.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Publish" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                if (instance == null || message == null || publish == null)
                {
                    SubModule.Log("Fresh campaign: Coop's GameLoaded message not found; the server's shutdown save name stays unset.");
                    return;
                }
                publish.MakeGenericMethod(message).Invoke(instance, new[] { null, Activator.CreateInstance(message, saveName) });
                SubModule.Log("Fresh campaign: Coop told the campaign for save '" + saveName + "' is loaded.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Fresh campaign: could not publish Coop's GameLoaded: " + ex.Message);
            }
        }
    }
}
