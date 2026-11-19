using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;

namespace RealmOfThronesCoopCompat
{
    // Runs Realm of Thrones' world rules on the co-op dedicated server. The engine never loads
    // ROT.SubModule there (its tags forbid it, and it would open menus, music and message boxes),
    // so this host loads ROT.dll itself and does a server version of what ROT.SubModule does,
    // from the three tables in RotHostTables: nothing runs unless a row says Host.
    // - Module load, and only there: ROT.dll is loaded from ROT-Core's client bin. The save
    //   system collects the saved types of every loaded assembly right after the modules load
    //   and the server collects them again before it loads its save; a ROT.dll loaded later
    //   would be missed, and a save loaded without its types loses Realm of Thrones' objects
    //   without a word. Then ROT.dll is compared with the tables and its patches are applied
    //   (RotHostPatches), before Coop applies its own.
    // - Game start: the four caches are cleared, the behavior objects ROT.SubModule keeps in
    //   static fields are made and stored (Realm of Thrones reads those without null checks),
    //   the behaviors are registered (the hosted ones, and with their events blocked the ones
    //   that only keep saved data) and the hosted models are added in Realm of Thrones' order,
    //   each wrapping the model registered before it, as on a player's PC.
    // - After that: villages under a bandit held castle get it as trade town, and the bandit
    //   clans get one leader. Realm of Thrones makes a new leader on every load; here one that
    //   lives is kept, so a restart adds no hero.
    // COODSZ_ROT_HOST picks how far it goes (RotHostRules.Level). Every step is logged as a
    // "Host:" line and has its own try, because the engine catches nothing around a SubModule.
    // BCL and TaleWorlds types only; everything of Harmony is in RotHostPatches.
    internal static class RotHost
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // The models this host registered, as slot and model, for the check after game start.
        private static readonly List<KeyValuePair<Type, GameModel>> HostedModels = new List<KeyValuePair<Type, GameModel>>();
        private static readonly object DailyTickOwner = new object();
        private static HostLevel _level = HostLevel.Off;
        private static Assembly _rot;
        private static HashSet<string> _blocked;
        private static int _checked;
        private static int _dailyTicked;
        private static bool _listed;

        // The level asked for hosting, but ROT.dll could not be loaded as it must be. The server
        // then has no Realm of Thrones rules, and when even the DLL is missing none of its saved types.
        internal static bool LoadFailed { get; private set; }

        // The level the host really runs at (off until OnSubModuleLoad got that far, and off again
        // when the patches failed).
        internal static HostLevel Running => _level;

        // Set when ROTCampaignTimeModel went on its slot at game start.
        internal static bool TimeModelRegistered { get; private set; }

        // What failed: when the modules load (known before any world loads) and at game start.
        private static int _patchFailures;
        private static int _bannerFailures;
        private static int _blockFailures;
        private static int _behaviorFailures;
        private static int _modelFailures;
        // Server guards (RotGuards) and story hooks (RotStory) that failed, when the modules load
        // or at a campaign start.
        private static int _guardFailures;
        private static int _storyFailures;

        // The failures known when the modules load, for the gate before a world loads or is made.
        internal static List<string> BootFailures() => RotHostRules.FailureList(_patchFailures, _bannerFailures, _blockFailures, 0, 0, _guardFailures, _storyFailures);

        // Every failure, for the world check after a new world is made.
        internal static List<string> AllFailures() => RotHostRules.FailureList(_patchFailures, _bannerFailures, _blockFailures, _behaviorFailures, _modelFailures, _guardFailures, _storyFailures);

        // ROT.dll as the host loaded it, for the server guards; null when nothing is hosted.
        internal static Assembly Rot => _rot;

        // The server guards and the story count their failures here, so a failed guard or story
        // hook is as loud as a failed patch and holds a new world back at full.
        internal static void AddGuardFailures(int count) => AddFailures(ref _guardFailures, count);

        internal static void AddStoryFailures(int count) => AddFailures(ref _storyFailures, count);

        private static void AddFailures(ref int counter, int count)
        {
            if (count <= 0)
                return;
            counter += count;
            SubModule.Log(RotHostRules.FailureLine(AllFailures()));
        }

        // Before a new world is made: whether the time model can go on at game start, from ROT.dll
        // as loaded (its type, its slot and the constructor that takes the model under it).
        internal static bool TimeModelReady(out string problem)
        {
            problem = null;
            try
            {
                if (_rot == null)
                    problem = "ROT.dll is not loaded";
                else if (!RotHostTables.ModelRows.Any(row => row.Type == RotHostTables.TimeModelType && row.Decision == HostDecision.Host))
                    problem = "the table does not host " + RotHostTables.TimeModelType;
                else
                {
                    var type = _rot.GetType(RotHostTables.TimeModelType, false);
                    var slot = type == null ? null : Slot(type);
                    if (type == null)
                        problem = RotHostTables.TimeModelType + " is not in ROT.dll";
                    else if (slot == null || type.GetConstructor(AnyInstance, null, new[] { slot }, null) == null)
                        problem = RotHostTables.TimeModelType + " has no constructor that takes the model under it";
                }
            }
            catch (Exception ex)
            {
                problem = Short(ex);
            }
            return problem == null;
        }

        public static void OnSubModuleLoad()
        {
            try
            {
                var variable = Environment.GetEnvironmentVariable(RotRules.HostVariable);
                var level = RotHostRules.Level(variable, out _);
                SubModule.Log(RotHostRules.LevelLine(variable));
                if (!RotHostRules.LoadsRot(level))
                    return;
                var modules = ActiveModules(out var coreVersion);
                // Only the tested ROT-Core version is hosted.
                var versionRefusal = RotHostRules.VersionRefusal(coreVersion, Environment.GetEnvironmentVariable(RotHostRules.AnyVersionVariable));
                if (versionRefusal != null)
                {
                    Refuse(versionRefusal);
                    return;
                }
                var rot = LoadRot(modules, coreVersion);
                if (rot == null)
                    return;
                _rot = rot;
                _level = level;
                Step("The check against the tables", () =>
                {
                    var differences = RotHostChecks.Drift(rot, coreVersion, out var behaviors, out var models, out var patchClasses);
                    SubModule.Log(RotHostRules.DriftLine(differences, behaviors, models, patchClasses));
                });
                // Models and behaviors count on the patches, so without them they stay off too.
                if (!Step("Applying Realm of Thrones' patches", () => _patchFailures = RotHostPatches.Apply(rot, modules)))
                {
                    _level = HostLevel.Off;
                    SubModule.Log(RotHostRules.Loud + "NOTHING MORE OF REALM OF THRONES IS HOSTED: its patches could not be applied (the line above).");
                    return;
                }
                if (RotHostRules.HostsBehaviors(level))
                {
                    Step("Blocking the events of the behaviors kept for their data", () => _blocked = RotHostPatches.BlockEvents(rot));
                    _blockFailures = RotHostTables.BehaviorRows.Count(row => row.Inert) - (_blocked?.Count ?? 0);
                }
                if (!Step("Applying BannerColorPersistence's patches", () => _bannerFailures = RotHostPatches.ApplyBanner(rot)))
                    _bannerFailures++;
                var failures = RotHostRules.FailureLine(BootFailures());
                if (failures != null)
                    SubModule.Log(failures);
            }
            catch (Exception ex)
            {
                _level = HostLevel.Off;
                LoadFailed = true;
                SubModule.Log(RotHostRules.Loud + "NOTHING OF REALM OF THRONES IS HOSTED: the host could not start: " + ex);
            }
        }

        // The first tick, or the first game start if that comes first.
        public static void OnApplicationTick() => FirstChecks("the first tick");

        public static void OnGameStart(Game game, IGameStarter starterObject)
        {
            FirstChecks("game start");
            if (!RotHostRules.LoadsRot(_level))
                return;
            Step("Game start", () =>
            {
                // The server builds the save system's context again before it loads its save.
                RotHostChecks.SaveDefiners(_rot, "game start");
                if (!(game?.GameType is Campaign) || !(starterObject is CampaignGameStarter starter))
                    return;
                LogMainHero("game start", early: true);
                var hosted = HostBehaviors(_rot, starter, _level, _blocked, out var fieldsSet);
                if (RotHostRules.RegistersModels(_level, fieldsSet))
                    HostModels(_rot, starter, _level, hosted);
                else if (RotHostRules.HostsModels(_level))
                {
                    _modelFailures = RotHostRules.ModelsToHost(RotHostTables.ModelRows, _level, hosted).Count;
                    SubModule.Log(RotHostRules.Loud + "NO MODEL REGISTERED: a static field of ROT.SubModule could not be set, and the models read those without a null check.");
                }
                _listed = true;
                var failures = RotHostRules.FailureLine(AllFailures());
                if (failures != null && (_behaviorFailures > 0 || _modelFailures > 0))
                    SubModule.Log(failures);
            });
        }

        public static void OnGameInitializationFinished(Game game)
        {
            if (!RotHostRules.LoadsRot(_level) || !(game?.GameType is Campaign))
                return;
            LogMainHero("the end of game initialization", early: true);
            Step("The shared patch target list", RotHostPatches.LogSharedTargets);
            if (RotHostRules.HostsModels(_level))
                Step("The model check", CheckModels);
            if (RotHostRules.HostsBehaviors(_level))
                Step("The village trade bound repair", RepairTradeBounds);
        }

        // The world is ready: a new one was created or a save was loaded.
        public static void OnAfterGameInitializationFinished(Game game)
        {
            if (!(game?.GameType is Campaign))
                return;
            // At every level: a world made while the time model was not hosted is no Realm of
            // Thrones world, and its save must not be kept.
            Step("The world year line", () => SubModule.Log(RotHostRules.WorldYearLine(CampaignTime.Now.GetYear, FreshCampaign.FreshStartActive)));
            if (!RotHostRules.LoadsRot(_level))
            {
                // Off and probe run nothing of Realm of Thrones, so a loaded Realm of Thrones world's
                // own data does not survive this server's saves.
                Step("The do not keep line", () =>
                {
                    if (!FreshCampaign.FreshStartActive && RotHostRules.IsRotYear(CampaignTime.Now.GetYear, false))
                        SubModule.Log(RotHostRules.Loud + "THIS LOADED REALM OF THRONES WORLD RUNS WITHOUT REALM OF THRONES' CODE (level " + RotHostRules.Level(Environment.GetEnvironmentVariable(RotRules.HostVariable), out _).ToString().ToLowerInvariant()
                            + "): its Realm of Thrones data does not survive a save of this server. DO NOT KEEP ITS SAVE.");
                });
                return;
            }
            LogMainHero("world ready");
            Step("The first daily tick line", () =>
            {
                _dailyTicked = 0;
                CampaignEvents.DailyTickEvent.AddNonSerializedListener(DailyTickOwner, OnDailyTick);
            });
            if (RotHostRules.HostsBehaviors(_level))
                Step("The bandit leader", EnsureBanditLeader);
        }

        // Once per process, when every module is loaded: from the first tick or the first game
        // start, whichever comes first. The engine's "before the first screen" hook is no place
        // for it: on this server it runs on every idle tick in some boots and never in others.
        private static void FirstChecks(string hook)
        {
            if (_checked != 0 || Interlocked.Exchange(ref _checked, 1) != 0 || !RotHostRules.LoadsRot(_level))
                return;
            Step("The first checks", () =>
            {
                RotHostChecks.SaveDefiners(_rot, hook);
                RotHostChecks.LibrarySubModules();
                if (RotHostRules.JitSweepOn(Environment.GetEnvironmentVariable(RotHostRules.JitVariable)))
                    RotHostChecks.JitSweep(_rot, _level);
            });
        }

        private static void OnDailyTick()
        {
            if (_dailyTicked == 0 && Interlocked.Exchange(ref _dailyTicked, 1) == 0)
                LogMainHero("the first daily tick");
        }

        // Active modules as id and full folder, and ROT-Core's version as the game prints it.
        private static List<KeyValuePair<string, string>> ActiveModules(out string coreVersion)
        {
            coreVersion = null;
            var modules = new List<KeyValuePair<string, string>>();
            foreach (var module in ModuleHelper.GetActiveModules())
            {
                if (module == null || !module.IsActive || string.IsNullOrEmpty(module.FolderPath))
                    continue;
                modules.Add(new KeyValuePair<string, string>(module.Id, Path.GetFullPath(module.FolderPath)));
                if (!string.Equals(module.Id, RotRules.CoreModuleId, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { coreVersion = module.Version.ToString(); }
                catch { }
            }
            return modules;
        }

        // Plain Assembly.LoadFrom: the engine's own loader would also try to preload every
        // assembly ROT.dll references by bare file name. All its types are listed right away,
        // which pulls MCM, UIExtenderEx and the view DLLs in through this module's resolver,
        // so a server that lacks one of them shows here and not in the middle of a tick.
        private static Assembly LoadRot(List<KeyValuePair<string, string>> modules, string coreVersion)
        {
            var path = HarmonyLocator.ModuleFile(modules, RotRules.CoreModuleId, HarmonyLocator.ClientBin, "ROT.dll");
            if (path == null || !File.Exists(path))
                return Refuse("ROT.dll not found (" + (path ?? "ROT-Core is not an active module") + ")");
            var folder = Path.GetDirectoryName(path);
            var stray = RotHostRules.StrayLibraries(Directory.GetFiles(folder, "*.dll"));
            if (stray.Count > 0)
                return Refuse(string.Join(" and ", stray) + " next to ROT.dll in " + folder + " would be used instead of the copy from its own module. Remove it from that folder");
            var rot = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => !assembly.IsDynamic && assembly.GetName().Name == "ROT") ?? Assembly.LoadFrom(path);
            Type[] types;
            try
            {
                types = rot.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return Refuse("only " + ex.Types.Count(type => type != null) + " of the " + ex.Types.Length + " types of ROT.dll load on this server ("
                    + string.Join(" | ", ex.LoaderExceptions.Where(e => e != null).Select(e => CrashTraceRules.Shorten(e.Message)).Distinct().Take(3)) + ")");
            }
            SubModule.Log("Host: ROT.dll " + rot.GetName().Version + " loaded from " + rot.Location + " (" + new FileInfo(rot.Location).Length + " bytes, " + types.Length
                + " types); ROT-Core is " + (coreVersion ?? "of an unknown version") + ".");
            return rot;
        }

        private static Assembly Refuse(string why)
        {
            LoadFailed = true;
            SubModule.Log(RotHostRules.Loud + "NOTHING OF REALM OF THRONES IS HOSTED: " + why + ".");
            return null;
        }

        // What ROT.SubModule.OnGameStart does before its models. Returns the behaviors hosted
        // with their events on; fieldsSet is false when a static field that should hold an
        // object does not. blocked names the behaviors whose RegisterEvents is blocked.
        internal static List<string> HostBehaviors(Assembly rot, CampaignGameStarter starter, HostLevel level, ICollection<string> blocked, out bool fieldsSet)
        {
            var hosted = new List<string>();
            var inert = new List<string>();
            var before = starter.CampaignBehaviors.Count;
            var caches = 0;
            foreach (var name in RotHostTables.CacheTypes)
            {
                if (Step("Resetting " + name, () => rot.GetType(name, true).GetMethod("ResetCache", AnyStatic, null, Type.EmptyTypes, null).Invoke(null, null)))
                    caches++;
            }
            var subModule = rot.GetType(RotHostTables.SubModuleType, true);
            var settings = rot.GetType(RotHostTables.SettingsType, true);
            Step("The settings line", () => LogSettings(settings));
            int fields = 0, wanted = 0, failed = 0;
            foreach (var row in RotHostRules.BehaviorsToCreate(RotHostTables.BehaviorRows, level))
            {
                if (row.Field != null)
                    wanted++;
                try
                {
                    if (row.Setting != null)
                    {
                        var setting = settings.GetProperty(row.Setting, AnyStatic) ?? throw new MissingMemberException(RotHostTables.SettingsType, row.Setting);
                        // A setting the co-op table pins decides here too, also if its getter could not be patched.
                        if (!(SettingsRules.PinnedSwitch(row.Setting) ?? (bool)setting.GetValue(null, null)))
                        {
                            // As in Realm of Thrones: with the setting off the object is not made and its field stays empty.
                            if (row.Field != null)
                                wanted--;
                            SubModule.Log("Host: " + row.Type + " is not made: " + RotHostTables.SettingsType + "." + row.Setting + " is off.");
                            continue;
                        }
                    }
                    var behavior = (CampaignBehaviorBase)Activator.CreateInstance(rot.GetType(row.Type, true), true);
                    if (row.Field != null)
                    {
                        var field = subModule.GetField(row.Field, AnyStatic) ?? throw new MissingFieldException(RotHostTables.SubModuleType, row.Field);
                        field.SetValue(null, behavior);
                        fields++;
                    }
                    if (!RotHostRules.Registers(row, level))
                        continue;
                    if (row.Decision == HostDecision.Host)
                    {
                        starter.AddBehavior(behavior);
                        hosted.Add(row.Type);
                        SubModule.Log("Host: behavior registered: " + row.Type + " (" + row.Why + ").");
                    }
                    else if (blocked != null && blocked.Contains(row.Type))
                    {
                        starter.AddBehavior(behavior);
                        inert.Add(row.Type);
                    }
                    else
                    {
                        failed++;
                        SubModule.Log("Host: behavior NOT REGISTERED: " + row.Type + ": its events could not be blocked, so the data it saves is not kept by this server.");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    SubModule.Log("Host: behavior FAILED: " + row.Type + ": " + Short(ex));
                }
            }
            fieldsSet = fields == wanted;
            _behaviorFailures = RotHostRules.HostsBehaviors(level) ? failed : 0;

            if (hosted.Contains(RotHostTables.ReplacingBehavior))
            {
                var count = starter.CampaignBehaviors.Count;
                starter.RemoveBehaviors<FindingItemOnMapBehavior>();
                SubModule.Log("Host: " + (count - starter.CampaignBehaviors.Count) + " FindingItemOnMapBehavior removed, as Realm of Thrones does: ROTDragonBehavior does its job.");
            }
            var summary = "Host: " + caches + " of " + RotHostTables.CacheTypes.Length + " caches reset; " + fields + " of " + wanted + " static fields of ROT.SubModule set; ";
            if (!RotHostRules.HostsBehaviors(level))
            {
                SubModule.Log(summary + "no behavior is registered at this level" + (failed == 0 ? "." : "; " + failed + " failed."));
                // The game saves and loads only registered behaviors, and drops the rest's data on a load.
                SubModule.Log(RotHostRules.Loud + "AT THIS LEVEL NO BEHAVIOR OF REALM OF THRONES IS REGISTERED, so a save this server writes holds none of their data "
                    + "(hero races, story state). DO NOT KEEP ITS SAVE.");
                return hosted;
            }
            var later = RotHostTables.BehaviorRows.Where(row => row.Decision == HostDecision.Later).ToList();
            var skipped = RotHostTables.BehaviorRows.Where(row => row.Decision == HostDecision.Skip).ToList();
            if (!_listed)
            {
                foreach (var row in later)
                    SubModule.Log("Host: not hosted yet: " + row.Type + ": " + row.Why + ".");
                SubModule.Log("Host: behaviors skipped by the table (" + skipped.Count + "): " + string.Join("; ", skipped.Select(row => row.Type + " (" + row.Why + ")")) + ".");
            }
            SubModule.Log("Host: registered with their events off, only so their saved data stays in the server's saves (" + inert.Count + "): " + string.Join(", ", inert) + ".");
            SubModule.Log(summary + "behaviors: " + hosted.Count + " hosted, " + inert.Count + " registered with their events off, " + failed + " failed; "
                + later.Count + " rows not hosted yet, " + skipped.Count + " skipped by the table; the game starter holds " + starter.CampaignBehaviors.Count + " behaviors (" + before + " before).");
            return hosted;
        }

        // ROT.ROTSettings answers with Realm of Thrones' own defaults while MCM has no settings
        // provider, which is what the server counts on: MCM's DLL is only a library here.
        private static void LogSettings(Type settings)
        {
            var defaults = settings.GetProperty("_defaults", AnyStatic)?.GetValue(null, null);
            var mcm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => !assembly.IsDynamic && assembly.GetName().Name == "MCMv5");
            var library = mcm == null ? "MCMv5 is not loaded" : "MCMv5 " + mcm.GetName().Version + " from " + mcm.Location;
            SubModule.Log(Equals(defaults, true)
                ? "Host: ROT.ROTSettings reads Realm of Thrones' own defaults, no MCM settings provider runs (" + library + ")."
                : RotHostRules.Loud + "ROT.ROTSettings DOES NOT READ ITS OWN DEFAULTS (_defaults is " + (defaults ?? "not there") + "): an MCM settings provider runs on this server, so "
                    + "Realm of Thrones' rules follow MCM's settings here (" + library + ").");
        }

        // Realm of Thrones' models in its own order. Each gets the model registered before it
        // for its slot (the game's own GetModel) as its one constructor argument, and AddModel
        // puts it on top of that slot, exactly as ROT.SubModule does.
        internal static void HostModels(Assembly rot, CampaignGameStarter starter, HostLevel level, ICollection<string> hostedBehaviors)
        {
            if (!RotHostRules.HostsModels(level))
                return;
            var hosted = RotHostRules.ModelsToHost(RotHostTables.ModelRows, level, hostedBehaviors);
            var getModel = typeof(CampaignGameStarter).GetMethod("GetModel");
            var addModel = typeof(CampaignGameStarter).GetMethods().First(method => method.Name == "AddModel" && method.IsGenericMethodDefinition);
            HostedModels.Clear();
            var failed = 0;
            foreach (var row in hosted)
            {
                try
                {
                    var type = rot.GetType(row.Type, true);
                    var slot = Slot(type) ?? throw new InvalidOperationException("it is no MBGameModel");
                    // One of the constructors is internal.
                    var constructor = type.GetConstructor(AnyInstance, null, new[] { slot }, null) ?? throw new MissingMethodException(row.Type, ".ctor(" + slot.Name + ")");
                    var previous = getModel.MakeGenericMethod(slot).Invoke(starter, null);
                    var model = (GameModel)constructor.Invoke(new[] { previous });
                    addModel.MakeGenericMethod(slot).Invoke(starter, new object[] { model });
                    HostedModels.Add(new KeyValuePair<Type, GameModel>(slot, model));
                    if (row.Type == RotHostTables.TimeModelType)
                        TimeModelRegistered = true;
                    // Most of them call the model under them without a null check.
                    SubModule.Log("Host: model " + row.Type + " wraps " + (previous?.GetType().FullName ?? "NOTHING (WARNING: no model was registered for " + slot.Name + " before it)")
                        + (row.PlayerCheck == null ? "" : "; hosted as is; needs a player check: " + row.PlayerCheck) + ".");
                }
                catch (Exception ex)
                {
                    failed++;
                    SubModule.Log("Host: model FAILED: " + row.Type + ": " + Short(ex));
                }
            }
            var skipped = RotHostTables.ModelRows.Where(row => row.Decision == HostDecision.Skip).ToList();
            var waiting = RotHostTables.ModelRows.Where(row => row.Decision == HostDecision.Host && !hosted.Contains(row)).ToList();
            if (!_listed)
            {
                foreach (var row in skipped)
                    SubModule.Log("Host: model skipped: " + row.Type + ": " + row.Why + ".");
                foreach (var row in waiting)
                    SubModule.Log("Host: model not hosted yet: " + row.Type + " counts on " + row.Needs + " and calls it without a null check, and that behavior is not hosted.");
            }
            _modelFailures = failed;
            SubModule.Log((failed > 0 ? RotHostRules.Loud : "Host: ") + "models: " + HostedModels.Count + " registered in Realm of Thrones' order (" + hosted.Count(row => row.PlayerCheck != null)
                + " hosted as is that need a player check), " + failed + " failed, " + waiting.Count + " not hosted yet, " + skipped.Count + " skipped by the table.");
        }

        // The game's model type a model stands in for: the T of the MBGameModel<T> it derives from.
        internal static Type Slot(Type model)
        {
            for (var type = model; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MBGameModel<>))
                    return type.GetGenericArguments()[0];
            }
            return null;
        }

        // Another module registering a model for the same slot after this host would take the
        // top place. ROT.SubModule shows a red message for that; here it is a log line.
        private static void CheckModels()
        {
            var models = Campaign.Current.Models.GetGameModels();
            var slots = HostedModels.Select(pair => pair.Key).Distinct().ToList();
            var displaced = new List<string>();
            foreach (var slot in slots)
            {
                var top = models.LastOrDefault(model => slot.IsInstanceOfType(model));
                if (!ReferenceEquals(top, HostedModels.Last(pair => pair.Key == slot).Value))
                    displaced.Add(slot.Name + " is " + (top == null ? "nothing" : top.GetType().FullName + " of " + top.GetType().Assembly.GetName().Name));
            }
            SubModule.Log(displaced.Count == 0
                ? "Host: model check: Realm of Thrones' model is on top of all " + slots.Count + " slots it was registered for."
                : "Host: model check: ANOTHER MODEL IS ON TOP OF " + displaced.Count + " OF " + slots.Count + " SLOTS: " + string.Join("; ", displaced) + ".");
        }

        // As ROT.SubModule.OnGameInitializationFinished: a village under a bandit held castle
        // has no trade town, so the castle becomes it. Repeating it changes nothing.
        private static void RepairTradeBounds()
        {
            int villages = 0, repaired = 0;
            foreach (var settlement in Settlement.All)
            {
                var village = settlement?.Village;
                var bound = village?.Bound;
                if (bound == null)
                    continue;
                villages++;
                if (bound.MapFaction != null && bound.MapFaction.IsBanditFaction && village.TradeBound == null)
                {
                    village.TradeBound = bound;
                    repaired++;
                }
            }
            SubModule.Log("Host: village trade bounds: " + repaired + " of " + villages + " villages got their bandit held castle as trade town.");
        }

        // The models hosted as is, and behaviors that wait for a server version, test the one
        // local player. Coop says the dedicated server has no main hero; reading Hero.MainHero
        // throws when there is no player character at all, and then so do they. One line at
        // each stage of the boot says what this server has.
        // At models and full a missing main hero is loud: ROTTroopUpgradeModel is hosted and its
        // DoesPartyHaveRequiredItemsForUpgrade reads Hero.MainHero without a check for every party
        // with an owner. ROTTroopRecruiter's IsHeroManageable and IsGarrisonManageable, which read
        // it too, are replaced by server guards (RotGuards).
        // Before the world is ready (early) the main hero is not there yet on every new world: the
        // sixth test's log has it missing at game start and at the end of game initialization and
        // main_hero by world ready. So it is loud only from world ready on.
        private static void LogMainHero(string when, bool early = false)
        {
            var hero = Read(() => Hero.MainHero?.StringId);
            var clan = Read(() => Clan.PlayerClan?.StringId);
            var missing = RotHostRules.HostsModels(_level) && (IsMissing(hero) || IsMissing(clan));
            var loud = missing && !early;
            SubModule.Log((loud ? RotHostRules.Loud : "Host: ") + "at " + when + " Hero.MainHero is " + hero + ", Clan.PlayerClan is " + clan
                + " and MobileParty.MainParty is " + Read(() => MobileParty.MainParty?.StringId) + "."
                + (loud ? " A hosted model that reads Hero.MainHero without a check (ROTTroopUpgradeModel.DoesPartyHaveRequiredItemsForUpgrade) would throw."
                    : missing ? " That is normal before the world is ready." : ""));
        }

        private static bool IsMissing(string read) => read == "null" || read.StartsWith("UNREADABLE", StringComparison.Ordinal);

        private static string Read(Func<string> read)
        {
            try { return read() ?? "null"; }
            catch (Exception ex) { return "UNREADABLE (" + ex.GetType().Name + ")"; }
        }

        // ROT.SubModule.OnAfterGameInitializationFinished makes a new hero the leader of every
        // bandit clan on each start and load. Here that happens only while a bandit clan has no
        // living leader from a bandit clan, so a server restart does not add a hero each time.
        // The two steps are Realm of Thrones' own: the hero is made without a clan, then moved.
        private static void EnsureBanditLeader()
        {
            var clans = Clan.BanditFactions.ToList();
            var without = clans.Count(clan => RotHostRules.NeedsBanditLeader(clan.Leader != null, clan.Leader != null && clan.Leader.IsAlive, clan.Leader?.Clan != null && clan.Leader.Clan.IsBanditFaction));
            if (without == 0)
            {
                SubModule.Log(clans.Count == 0
                    ? "Host: bandit leader: this world has no bandit clans."
                    : "Host: bandit leader: all " + clans.Count + " bandit clans have a living leader (" + clans[0].Leader.StringId + "), nothing to do.");
                return;
            }
            var template = CharacterObject.All.FirstOrDefault(character => character.Occupation == Occupation.Bandit && character.StringId.StartsWith("lord_", StringComparison.Ordinal));
            if (template == null)
            {
                SubModule.Log("Host: bandit leader: " + without + " of " + clans.Count + " bandit clans have no living leader, and this world has no bandit lord to make one from.");
                return;
            }
            var heroes = Hero.AllAliveHeroes.Count;
            var leader = HeroCreator.CreateSpecialHero(template, null, null, null, -1);
            leader.Clan = clans[0];
            foreach (var clan in clans)
                clan.SetLeader(leader);
            SubModule.Log("Host: bandit leader: " + without + " of " + clans.Count + " bandit clans had no living leader; " + leader.StringId + " (made from " + template.StringId
                + ") now leads all of them. Living heroes " + heroes + " before, " + Hero.AllAliveHeroes.Count + " now.");
        }

        // One line for a log: the exception behind a reflection call, with what it wraps.
        internal static string Short(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            var text = ex.GetType().Name + ": " + CrashTraceRules.Shorten(ex.Message);
            return ex.InnerException == null ? text : text + " <= " + ex.InnerException.GetType().Name + ": " + CrashTraceRules.Shorten(ex.InnerException.Message);
        }

        // A step that throws is logged and must never take the server down.
        private static bool Step(string name, Action step)
        {
            try
            {
                step();
                return true;
            }
            catch (Exception ex)
            {
                SubModule.Log("Host: " + name + " failed: " + ex);
                return false;
            }
        }
    }
}
