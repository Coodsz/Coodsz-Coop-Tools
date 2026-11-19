// AD1259 <-> Coop dedicated server: start a new campaign on a custom campaign map without a save.
//
// When the configured save does not exist, the Coop dedicated server copies
// Game Saves\default_new_game.sav (a vanilla Calradia world) under the configured name and asks Coop
// to host that copy: GameStateInterface.LoadGame -> ForceLoadSave -> SandBoxSaveHelper.LoadGameAction.
// AD1259 replaces every settlement and kingdom, so that world cannot run on its map; it crashed the
// engine while the map caches were built. (Coodsz Host's own custom-map new-campaign code in
// DedicatedServer.LauncherCheats is never armed with this server build: it hooks a 3-argument
// CoopDriver.HostSaveGameAsServer that the installed DS core does not have.)
//
//  1. A prefix on SandBoxSaveHelper.LoadGameAction recognises that bootstrap copy (byte-identical to
//     default_new_game.sav) and, when a module other than the vanilla ones provides Main_map, starts a
//     brand-new sandbox campaign instead, with the call Coop's own GameStateInterface.StartNewGame makes:
//         MBGameManager.StartNewGame(new SandBoxGameManager(() => new Campaign(CampaignGameMode.Campaign)))
//     The world is then built from the active modules' XML, exactly as in single-player.
//  2. A prefix on SandBoxGameManager.OnLoadFinished (only for that new campaign) skips the intro video
//     and completes character creation on the server (HeadlessCharacterCreation), so the campaign is on
//     the map before the DS core declares it ready and starts Coop's server.
//  3. Coop replaces ChangePlayerCharacterAction.Apply with its player-switch handling, which assumes an
//     existing main party. While the world is created there are no players, so that replacement is
//     bypassed and the game's own action runs; Coop's handling is active again before the campaign is ready.
// The DS core then autosaves the world under the configured save name, as it would have saved the
// template copy, and later starts load that save normally.
//
// Never touched: real saves (anything that is not a byte copy of the template), the template itself,
// and vanilla-map profiles (no custom map provider).
//
// Switches (environment variables):
//   AD1259COOP_DISABLE_FRESH_CAMPAIGN=1  old behaviour: the template is loaded and the world check stops the server
//   AD1259COOP_START_CULTURE=<id>        culture of the server's placeholder hero (default: first offered culture with a town)
//   AD1259COOP_NO_EXIT_ON_FAILURE=1      diagnostics only: log a failed creation but keep the process running

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

namespace AD1259CoopCompat
{
    internal static class FreshCampaign
    {
        private static bool _installed;
        private static bool _coopGuardInstalled;
        private static DateTime _startedUtc;

        /// <summary>True from the moment this patch starts a new campaign until it reaches the campaign map.</summary>
        public static bool Active { get; private set; }

        /// <summary>True once a campaign started by this patch has reached the campaign map.</summary>
        public static bool Completed { get; private set; }

        public static string SaveName { get; private set; }

        public static string ProviderId { get; private set; }

        public static void Install(Harmony harmony)
        {
            if (_installed) return;
            _installed = true;
            if (Env.IsSet("AD1259COOP_DISABLE_FRESH_CAMPAIGN"))
            {
                Log.Info("fresh-campaign support disabled by AD1259COOP_DISABLE_FRESH_CAMPAIGN.");
                return;
            }

            MethodInfo loadGameAction = AccessTools.Method(typeof(SandBoxSaveHelper), "LoadGameAction");
            MethodInfo onLoadFinished = AccessTools.Method(typeof(SandBoxGameManager), "OnLoadFinished");
            if (loadGameAction == null || onLoadFinished == null || !HeadlessCharacterCreation.IsSupported)
            {
                Log.Warn("this game build lacks SandBoxSaveHelper.LoadGameAction, SandBoxGameManager.OnLoadFinished or " +
                         "LaunchSandboxCharacterCreation; fresh-campaign support is disabled and the world check stays in charge.");
                return;
            }

            harmony.Patch(loadGameAction, prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(LoadGameActionPrefix)));
            harmony.Patch(onLoadFinished,
                prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(OnLoadFinishedPrefix)) { priority = Priority.First });
            MethodInfo registerObjects = AccessTools.Method(typeof(SandBoxSubModule), "RegisterSubModuleObjects");
            if (registerObjects != null)
            {
                harmony.Patch(registerObjects,
                    prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(RegisterSubModuleObjectsPrefix)) { priority = Priority.First });
            }
            else
            {
                Log.Warn("SandBoxSubModule.RegisterSubModuleObjects was not found; banner data cannot be prepared for a new campaign.");
            }
            InstallCreationTrace(harmony);
            Log.Info("fresh-campaign support armed: a copy of default_new_game hosted on a custom map becomes a new campaign on that map.");
        }

        // Coop's ForceLoadSave hands the configured save to the game here. Returning false skips loading the
        // template copy; the new campaign started instead takes its place.
        private static bool LoadGameActionPrefix(object[] __args)
        {
            if (Active) return true;
            // Completion exempts only the world we just created from the world guard.
            // If another save is loaded in this process, check that save independently.
            Completed = false;
            string saveName = null;
            try
            {
                saveName = ReadStringMember(__args != null && __args.Length > 0 ? __args[0] : null, "Name");
                if (string.IsNullOrWhiteSpace(saveName)) return true;

                MapProvider provider = MapProvider.Resolve();
                if (provider == null) return true;   // vanilla map: nothing to do

                string userDir = Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR");
                FreshDecision decision = FreshCampaignLogic.Decide(
                    saveName,
                    string.IsNullOrWhiteSpace(userDir) ? null : Path.Combine(userDir, "Game Saves"),
                    FreshCampaignLogic.TemplateCandidates(userDir, AppContext.BaseDirectory),
                    provider.ModuleId);
                Log.Info(decision.Message);
                if (!decision.StartFresh) return true;

                Begin(saveName, provider.ModuleId);
            }
            catch (Exception e)
            {
                Log.Error("fresh-campaign check failed for '" + saveName + "'; the save is loaded as-is: " + e);
                return true;
            }

            try
            {
                MBGameManager.StartNewGame(new SandBoxGameManager(() => new Campaign(CampaignGameMode.Campaign)));
            }
            catch (Exception e)
            {
                Fail("starting the new campaign failed", e);
            }
            return false;
        }

        private static void Begin(string saveName, string providerId)
        {
            Active = true;
            Completed = false;
            SaveName = saveName;
            ProviderId = providerId;
            _startedUtc = DateTime.UtcNow;
            _gameThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            InstallCoopGuard();
            InstallUnhandledExceptionLog();
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
            Log.Info("starting a new " + providerId + " campaign for '" + saveName + "' instead of the vanilla template; " +
                     "character creation is completed on the server, no player is needed.");
        }

        private const int MaxLoggedExceptions = 40;
        private static int _loggedExceptions;
        private static int _gameThreadId;
        private static bool _unhandledLogInstalled;
        [ThreadStatic] private static bool _inExceptionLog;

        // World creation runs thousands of mod and Coop hooks with no player present; an exception that escapes
        // ends the process with only a native crash dump. The first few game-thread exceptions are logged with
        // their stacks (Coop's log uploader's socket errors are skipped), and so is any exception that kills the
        // process, on any thread.
        private static void OnFirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (!Active || _inExceptionLog) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _gameThreadId) return;
            if (e.Exception is System.Net.Sockets.SocketException || e.Exception is IOException ||
                e.Exception.GetType().FullName == "System.Net.Http.HttpRequestException") return;
            if (System.Threading.Interlocked.Increment(ref _loggedExceptions) > MaxLoggedExceptions) return;
            _inExceptionLog = true;
            try
            {
                string stack = Environment.StackTrace;
                string[] frames = stack.Split('\n');
                if (frames.Length > 45) stack = string.Join("\n", frames.Take(45)) + "\n   ...";
                Log.Warn("exception while the new world is created (#" + _loggedExceptions + "): " +
                         e.Exception.GetType().FullName + ": " + e.Exception.Message + "\n" + stack);
            }
            catch
            {
            }
            finally
            {
                _inExceptionLog = false;
            }
        }

        private static void InstallUnhandledExceptionLog()
        {
            if (_unhandledLogInstalled) return;
            _unhandledLogInstalled = true;
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    Log.Error("unhandled .NET exception on thread " + System.Threading.Thread.CurrentThread.ManagedThreadId +
                              " (game thread " + _gameThreadId + ", new campaign " + (Active ? "being created" : "not active") +
                              "); the server stops: " + e.ExceptionObject);
                    Log.Flush();
                }
                catch
                {
                }
            };
        }

        // Replaces the new-game branch of SandBoxGameManager.OnLoadFinished (intro video, then character creation
        // driven by a screen) for the campaign started above. Saved games and every other new game run unchanged.
        private static bool OnLoadFinishedPrefix(SandBoxGameManager __instance)
        {
            if (!Active || __instance == null || __instance.LoadingSavedGame) return true;
            try
            {
                Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "the new {0} world finished loading after {1} ({2} settlements, {3} kingdoms); completing character " +
                    "creation without the intro video.", ProviderId, Elapsed(), Settlement.All.Count, Kingdom.All.Count));
                string summary = HeadlessCharacterCreation.Run(__instance);
                HeadlessCharacterCreation.MarkLoaded(__instance);
                Complete(summary);
            }
            catch (Exception e)
            {
                Fail("character creation on the server failed", e);
            }
            return false;
        }

        // The dedicated server only loads banner colours and icons after a campaign's XML is registered (its own
        // "banner palette ready" step). A saved game never needs them earlier, but a new campaign deserialises
        // every clan from XML, and Clan.Deserialize reads banner colours: without this, each clan threw a
        // NullReferenceException in BannerManager.ColorPalette and was left half-initialised, which later failed
        // in village production and crashed the engine.
        private static void RegisterSubModuleObjectsPrefix(bool isSavedCampaign)
        {
            if (!Active || isSavedCampaign) return;
            try
            {
                TaleWorlds.Core.BannerManager.Initialize();
                TaleWorlds.Core.BannerManager manager = TaleWorlds.Core.BannerManager.Instance;
                if (manager.ReadOnlyColorPalette.Count == 0 || manager.BannerIconGroups.Count == 0)
                {
                    manager.LoadBannerIcons();
                }
                if (manager.ReadOnlyColorPalette.Count == 0 || manager.BannerIconGroups.Count == 0)
                {
                    Log.Warn("the active modules' BannerIcons data left the banner manager empty; clan banners may fail to load.");
                    return;
                }
                Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "banner data prepared for the new world's clans: {0} colours, {1} icon groups.",
                    manager.ReadOnlyColorPalette.Count, manager.BannerIconGroups.Count));
            }
            catch (Exception e)
            {
                Log.Warn("could not prepare banner data for the new world: " + Unwrap(e));
            }
        }

        private static void Complete(string summary)
        {
            Active = false;
            Completed = true;
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
            Log.Info("new campaign reached the campaign map after " + Elapsed() + ": " + summary + ".");
            RbmEconomyDiagnostics.TryReport();
            Log.Info("Coop's player-switch handling is active again. The server keeps this world as '" + SaveName + "'; " +
                     AutosaveNote() + ".");
        }

        // Diagnostics only (AD1259COOP_TRACE_CREATION=1): one console line before and after every hero the new
        // world initialises, so a crash names the hero, its template and its settlement.
        internal static void InstallCreationTrace(Harmony harmony)
        {
            if (!Env.IsSet("AD1259COOP_TRACE_CREATION")) return;
            try
            {
                MethodInfo init = AccessTools.Method(typeof(HeroCreator), "InitializeHeroFromSettings");
                if (init == null) return;
                harmony.Patch(init,
                    prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(TraceInitPrefix)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(FreshCampaign), nameof(TraceInitPostfix)) { priority = Priority.Last });
                Log.Info("creation trace armed on HeroCreator.InitializeHeroFromSettings.");
            }
            catch (Exception e)
            {
                Log.Warn("creation trace not armed: " + e.Message);
            }
        }

        private static void TraceInitPrefix(Hero hero)
        {
            if (!Active || hero == null) return;
            Console.WriteLine("[AD1259CoopCompat] trace: init " + hero.StringId + " template=" +
                              (hero.Template == null ? "-" : hero.Template.StringId) + " born=" +
                              (hero.BornSettlement == null ? "-" : hero.BornSettlement.StringId));
        }

        private static void TraceInitPostfix(Hero hero)
        {
            if (!Active || hero == null) return;
            Console.WriteLine("[AD1259CoopCompat] trace: done " + hero.StringId);
        }

        internal static void Fail(string what, Exception e)
        {
            Log.Error(what + ": " + Unwrap(e));
            Log.Error("The new " + ProviderId + " campaign could not be created on the server. '" + SaveName + "' is still " +
                      "the untouched template copy, so the next start tries again.");
            if (Env.IsSet("AD1259COOP_NO_EXIT_ON_FAILURE")) return;
            Log.Error("Stopping with exit code 2 (load failure) instead of waiting for the server's load timeout.");
            Log.Flush();
            Environment.Exit(2);
        }

        // Coop replaces ChangePlayerCharacterAction.Apply with its player-switch handling, which reads the existing
        // main party. While the fresh world is created there are no players, so the game's own action runs instead.
        private static void InstallCoopGuard()
        {
            if (_coopGuardInstalled) return;
            _coopGuardInstalled = true;
            try
            {
                Type patches = AccessTools.TypeByName("GameInterface.Services.Heroes.Patches.ChangePlayerCharacterActionPatches");
                MethodInfo coopPrefix = patches == null ? null : AccessTools.Method(patches, "Prefix");
                if (coopPrefix == null || coopPrefix.ReturnType != typeof(bool))
                {
                    Log.Info("Coop's ChangePlayerCharacterAction patch was not found; no guard needed.");
                    return;
                }
                CompatSubModule.HarmonyInstance.Patch(coopPrefix,
                    prefix: new HarmonyMethod(typeof(FreshCampaign), nameof(CoopPlayerSwitchGuard)));
                Log.Info("Coop's player-switch handling is bypassed until the new campaign reaches the map.");
            }
            catch (Exception e)
            {
                Log.Warn("could not guard Coop's ChangePlayerCharacterAction patch: " + e.Message);
            }
        }

        private static bool CoopPlayerSwitchGuard(ref bool __result)
        {
            if (!Active) return true;
            __result = true;   // Coop's prefix "returns true", so the game's own ChangePlayerCharacterAction.Apply runs
            return false;
        }

        private static string AutosaveNote()
        {
            const string generic = "the server's autosave writes it to Game Saves";
            try
            {
                string userDir = Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR");
                string config = string.IsNullOrWhiteSpace(userDir) ? null : Path.Combine(userDir, "server-config.json");
                if (config == null || !File.Exists(config)) return generic;
                Match m = Regex.Match(File.ReadAllText(config), "\"autosaveMinutes\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (!m.Success) return generic;
                int minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                return minutes > 0
                    ? generic + " within " + minutes + " minute(s); stop the server only after that save, or the next start builds a new world again"
                    : "autosave is OFF (autosaveMinutes = 0): type 'save' in the server console before stopping, or the next start builds a new world again";
            }
            catch
            {
                return generic;
            }
        }

        private static string Elapsed()
        {
            return ((int)(DateTime.UtcNow - _startedUtc).TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
        }

        internal static string Unwrap(Exception e)
        {
            TargetInvocationException tie = e as TargetInvocationException;
            return (tie != null && tie.InnerException != null ? tie.InnerException : e).ToString();
        }

        private static string ReadStringMember(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null) return property.GetValue(instance, null) as string;
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(instance) as string;
        }
    }

    public sealed class FreshDecision
    {
        public bool StartFresh;
        public string Message;
    }

    /// <summary>Engine-independent part of the fresh-campaign decision, testable outside the game.</summary>
    public static class FreshCampaignLogic
    {
        public const string TemplateSaveName = "default_new_game";

        /// <summary>Where the DS core takes the template from, in its own order of preference.</summary>
        public static string[] TemplateCandidates(string userDir, string baseDirectory)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(userDir)) candidates.Add(Path.Combine(userDir, "Game Saves", TemplateSaveName + ".sav"));
            if (!string.IsNullOrWhiteSpace(baseDirectory)) candidates.Add(Path.Combine(baseDirectory, TemplateSaveName + ".sav"));
            return candidates.ToArray();
        }

        public static FreshDecision Decide(string saveName, string saveRoot, IEnumerable<string> templateCandidates, string providerId)
        {
            var decision = new FreshDecision();
            if (string.Equals(saveName, TemplateSaveName, StringComparison.OrdinalIgnoreCase))
            {
                decision.Message = "the server hosts " + TemplateSaveName + " itself; it is left alone because every new " +
                                   "campaign is copied from it.";
                return decision;
            }
            if (string.IsNullOrWhiteSpace(saveRoot))
            {
                decision.Message = "fresh-campaign check skipped: the server's Game Saves folder is unknown (BANNERLORD_USER_DIR " +
                                   "is not set); '" + saveName + "' is loaded as-is.";
                return decision;
            }

            string save = Path.Combine(saveRoot, saveName + ".sav");
            if (!File.Exists(save))
            {
                decision.Message = "fresh-campaign check: " + save + " not found; '" + saveName + "' is left to the server.";
                return decision;
            }

            string template = (templateCandidates ?? Enumerable.Empty<string>()).FirstOrDefault(File.Exists);
            if (template == null)
            {
                decision.Message = "fresh-campaign check: no " + TemplateSaveName + ".sav found; '" + saveName + "' is loaded as-is.";
                return decision;
            }

            if (!IsByteCopy(save, template))
            {
                decision.Message = "'" + saveName + "' is a real campaign save (not a copy of " + TemplateSaveName + "); loading it normally.";
                return decision;
            }

            decision.StartFresh = true;
            decision.Message = "'" + saveName + "' is the server's untouched copy of " + TemplateSaveName + " (a vanilla Calradia " +
                               "world), but " + providerId + " provides the campaign map; a new " + providerId + " campaign is " +
                               "created instead.";
            return decision;
        }

        public static bool IsByteCopy(string path, string reference)
        {
            var a = new FileInfo(path);
            var b = new FileInfo(reference);
            return a.Exists && b.Exists && a.Length == b.Length && CompatLogic.Sha256(path) == CompatLogic.Sha256(reference);
        }
    }
}
