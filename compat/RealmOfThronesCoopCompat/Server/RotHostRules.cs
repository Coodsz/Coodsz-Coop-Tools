#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // How much of Realm of Thrones the host runs, so a live test can step up one risk at a time.
    internal enum HostLevel { Off, Probe, Patches, Models, Full }

    // The plain rules of the Realm of Thrones host (RotHost): its level switch, the lookups in
    // RotHostTables, which patches, models and behaviors a level takes and in which order, the
    // comparison of the tables with what ROT.dll really holds, and its loud lines. No game
    // types, so the launcher tests can run them.
    internal static class RotHostRules
    {
        public const string HarmonyId = "realmofthrones.core";
        // The JIT sweep of every hosted method, for a live test: COODSZ_ROT_HOST_JIT=1.
        public const string JitVariable = "COODSZ_ROT_HOST_JIT";
        // Starts every line that means the host is off or the world is not to be kept.
        public const string Loud = "Host: !!! ";

        private static readonly string[] LevelNames = { "off", "probe", "patches", "models", "full" };

        // COODSZ_ROT_HOST: off, probe, patches, models or full. Not set means full. Anything
        // else means off, and unknown says so for the log.
        public static HostLevel Level(string variable, out bool unknown)
        {
            unknown = false;
            var value = variable?.Trim();
            if (string.IsNullOrEmpty(value))
                return HostLevel.Full;
            var index = Array.FindIndex(LevelNames, name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                return (HostLevel)index;
            unknown = true;
            return HostLevel.Off;
        }

        public static string LevelLine(string variable)
        {
            var level = Level(variable, out var unknown);
            if (unknown)
                return "Host: off. " + RotRules.HostVariable + "=" + CrashTraceRules.Shorten(variable.Trim()) + " is not a level (" + string.Join(", ", LevelNames) + ").";
            return "Host: level " + LevelNames[(int)level] + " (" + RotRules.HostVariable + (string.IsNullOrWhiteSpace(variable) ? " is not set" : "=" + variable.Trim()) + ").";
        }

        public static bool JitSweepOn(string variable) => string.Equals(variable?.Trim(), "1", StringComparison.Ordinal);

        // ROT.dll is loaded and patched from patches up, models are registered from models up,
        // behaviors and the two world repairs only at full.
        public static bool LoadsRot(HostLevel level) => level >= HostLevel.Patches;
        public static bool HostsModels(HostLevel level) => level >= HostLevel.Models;
        public static bool HostsBehaviors(HostLevel level) => level == HostLevel.Full;

        // "v8.1.8.0" is the module version the game prints for ROT-Core v8.1.8.
        public static bool IsExpectedCoreVersion(string version) =>
            version != null && (version == RotHostTables.CoreVersion || version.StartsWith(RotHostTables.CoreVersion + ".", StringComparison.Ordinal));

        // The runtime looks for a DLL ROT.dll needs next to ROT.dll before it asks this module's
        // resolver, so a copy of a library the resolver serves that sits there would be used
        // instead, without a word. The file names next to ROT.dll that are such copies.
        public static List<string> StrayLibraries(IEnumerable<string> filesNextToRot) =>
            (filesNextToRot ?? Enumerable.Empty<string>())
                .Select(Path.GetFileName)
                .Where(file => HarmonyLocator.ClientLibraries.Any(library => string.Equals(library.Key + ".dll", file, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        // MCM's and UIExtenderEx's DLLs are only served to ROT.dll as libraries. A SubModule
        // from one of them running on the server means their own code runs there too.
        public static bool IsLibraryAssembly(string assemblyName) =>
            HarmonyLocator.ClientLibraries.Any(library => string.Equals(library.Key, assemblyName, StringComparison.OrdinalIgnoreCase));

        // A class whose patch methods have rows of their own is applied one method at a time.
        public static bool IsSplit(IEnumerable<PatchRow> rows, string type) =>
            rows.Any(row => row.Type == type && row.Method != null && !row.ByHand);

        // The row of a class applied whole, or null.
        public static PatchRow ClassRow(IEnumerable<PatchRow> rows, string type) =>
            rows.FirstOrDefault(row => row.Type == type && row.Method == null && !row.ByHand);

        // The row of one patch method of a split class: by its name and the game method it
        // patches. Null when the table does not know it, and then it is not applied.
        public static PatchRow MethodRow(IEnumerable<PatchRow> rows, string type, string method, string target) =>
            rows.FirstOrDefault(row => row.Type == type && row.Method == method && row.Target == target && !row.ByHand);

        // The hosted patches by hand: the ones at the top of the table go before the classes,
        // as in ROT.SubModule, the others after them.
        public static List<PatchRow> HandRows(IList<PatchRow> rows, bool beforeClasses)
        {
            var top = rows.TakeWhile(row => row.ByHand).ToList();
            return (beforeClasses ? top : rows.Skip(top.Count).Where(row => row.ByHand))
                .Where(row => row.Method != null && row.Decision == HostDecision.Host).ToList();
        }

        // "Type.Method(args)" split into the type and the method, for a patch applied by hand.
        public static bool SplitTarget(string target, out string type, out string method)
        {
            type = method = null;
            var open = target?.IndexOf('(') ?? -1;
            var dot = open <= 0 ? -1 : target.LastIndexOf('.', open - 1);
            if (dot <= 0 || dot == open - 1)
                return false;
            type = target.Substring(0, dot);
            method = target.Substring(dot + 1, open - dot - 1);
            return true;
        }

        // A behavior the host registers at full: a hosted one, and one kept inert for its data.
        public static bool Registers(BehaviorRow row, HostLevel level) =>
            HostsBehaviors(level) && (row.Decision == HostDecision.Host || row.Inert);

        // The behavior objects a level makes, in Realm of Thrones' order: the ones it registers,
        // and from patches up every one a static field of ROT.SubModule must hold.
        public static List<BehaviorRow> BehaviorsToCreate(IEnumerable<BehaviorRow> rows, HostLevel level) =>
            !LoadsRot(level)
                ? new List<BehaviorRow>()
                : rows.Where(row => row.Field != null || Registers(row, level)).ToList();

        // The models a level registers, in Realm of Thrones' order. A model that counts on a
        // behavior waits until that behavior is hosted with its events on.
        public static List<ModelRow> ModelsToHost(IEnumerable<ModelRow> rows, HostLevel level, ICollection<string> hostedBehaviors) =>
            !HostsModels(level)
                ? new List<ModelRow>()
                : rows.Where(row => row.Decision == HostDecision.Host && (row.Needs == null || (hostedBehaviors != null && hostedBehaviors.Contains(row.Needs)))).ToList();

        // The model on top of each slot once those are registered: the last one wins, as in the game.
        public static Dictionary<string, string> TopModels(IEnumerable<ModelRow> hosted)
        {
            var top = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in hosted)
                top[row.Slot] = row.Type;
            return top;
        }

        // Every difference between the tables and this ROT.dll: a behavior, model or patch class
        // the tables do not know, a table row whose type is gone or is no longer what the table
        // says, and another ROT-Core version. Empty when they match.
        public static List<string> Drift(IEnumerable<string> behaviorsInDll, IEnumerable<string> modelsInDll, IEnumerable<string> patchClassesInDll,
            IEnumerable<string> allTypesInDll, string coreVersion)
        {
            var differences = new List<string>();
            if (!IsExpectedCoreVersion(coreVersion))
                differences.Add("ROT-Core is " + (coreVersion ?? "of an unknown version") + ", not " + RotHostTables.CoreVersion);
            var all = new HashSet<string>(allTypesInDll ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            Compare(differences, "behavior", behaviorsInDll, RotHostTables.BehaviorRows.Select(row => row.Type));
            Compare(differences, "model", modelsInDll, RotHostTables.ModelRows.Select(row => row.Type));
            Compare(differences, "patch class", patchClassesInDll, RotHostTables.Patches.Where(row => !row.ByHand).Select(row => row.Type));
            foreach (var type in RotHostTables.Patches.Where(row => row.ByHand).Select(row => row.Type).Distinct())
            {
                if (!all.Contains(type))
                    differences.Add("patch class " + type + " of the table is not in ROT.dll");
            }
            return differences;
        }

        private static void Compare(List<string> differences, string kind, IEnumerable<string> inDll, IEnumerable<string> inTable)
        {
            var dll = new HashSet<string>(inDll ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var table = new HashSet<string>(inTable, StringComparer.Ordinal);
            foreach (var type in dll.Where(type => !table.Contains(type)).OrderBy(type => type, StringComparer.Ordinal))
                differences.Add(kind + " " + type + " is not in the table");
            foreach (var type in table.Where(type => !dll.Contains(type)).OrderBy(type => type, StringComparer.Ordinal))
                differences.Add(kind + " " + type + " of the table is not in ROT.dll");
        }

        // One loud line with every difference. Hosting goes on either way.
        public static string DriftLine(IList<string> differences, int behaviors, int models, int patchClasses) =>
            differences.Count == 0
                ? "Host: ROT.dll matches the tables (" + behaviors + " behaviors, " + models + " models, " + patchClasses + " patch classes) and ROT-Core is " + RotHostTables.CoreVersion + "."
                : Loud + "WARNING, THIS REALM OF THRONES DIFFERS FROM THE TABLES BUILT FOR " + RotHostTables.CoreVersion + " (" + differences.Count + "): "
                    + string.Join("; ", differences) + ". Hosting goes on with the tables; what they do not name is not hosted.";

        // Another ROT-Core version is not hosted (Coodsz's call): only the tested one runs, unless
        // a test sets COODSZ_ROT_ANYVERSION=1. Differences inside that version are loud warnings.
        public const string AnyVersionVariable = "COODSZ_ROT_ANYVERSION";

        public static string VersionRefusal(string coreVersion, string anyVersionVariable) =>
            IsExpectedCoreVersion(coreVersion) || string.Equals(anyVersionVariable?.Trim(), "1", StringComparison.Ordinal)
                ? null
                : "ROT-Core is " + (coreVersion ?? "of an unknown version") + " and this host is tested with " + RotHostTables.CoreVersion + " only ("
                    + AnyVersionVariable + "=1 hosts it anyway for a test)";

        // What the host could not do, from its counts, for the loud line and the gates.
        public static List<string> FailureList(int patches, int banner, int blocked, int behaviors, int models, int guards = 0, int story = 0)
        {
            var list = new List<string>();
            if (patches > 0)
                list.Add(patches + " of Realm of Thrones' patches failed");
            if (banner > 0)
                list.Add(banner + " of BannerColorPersistence's patches failed or could not run");
            if (blocked > 0)
                list.Add(blocked + " behaviors kept for their data could not have their events blocked, so they are not registered");
            if (behaviors > 0)
                list.Add(behaviors + " behaviors failed or were not registered");
            if (models > 0)
                list.Add(models + " models failed");
            if (guards > 0)
                list.Add(guards + " server guards failed, so the rules they guard do not run");
            if (story > 0)
                list.Add(story + " story hooks failed, so the story may stand still or run a step made for one player");
            return list;
        }

        public static string FailureLine(IList<string> failures) =>
            failures == null || failures.Count == 0 ? null
                : Loud + "THE HOST IS NOT COMPLETE: " + string.Join("; ", failures) + " (the lines above say which). At full a new world is held back and a saved one is not loaded.";

        // Realm of Thrones' models read static fields of ROT.SubModule without a null check, so
        // when one that should hold an object could not be set, no model is registered.
        public static bool RegistersModels(HostLevel level, bool staticFieldsSet) => HostsModels(level) && staticFieldsSet;

        // A bandit clan needs a leader unless it has a living one who is in a bandit clan.
        public static bool NeedsBanditLeader(bool hasLeader, bool leaderAlive, bool leaderInBanditClan) =>
            !hasLeader || !leaderAlive || !leaderInBanditClan;

        // The ROT gate before the server makes or loads a world. At models and full (the level
        // asked for) a world needs Realm of Thrones' code: without ROT.dll, with the host stopped
        // below models, or without a time model that can be registered it would have the game's
        // own calendar and rules. At full, a host that is not complete (failed patches, models or
        // behaviors, hostFailures) refuses too. Null when the world may go on; else why not. Below
        // models a test world may be made; the host says loudly not to keep its save.
        public static string WorldRefusal(HostLevel asked, bool loadFailed, HostLevel running, bool timeModelReady, string timeModelProblem, string hostFailures = null)
        {
            if (!HostsModels(asked))
                return null;
            if (loadFailed)
                return "the host could not load ROT.dll (the Host line above says why)";
            if (!HostsModels(running))
                return "the host is not running Realm of Thrones' models (the Host lines above say why)";
            if (!timeModelReady)
                return "Realm of Thrones' time model cannot be registered (" + (timeModelProblem ?? "unknown") + ")";
            if (asked == HostLevel.Full && !string.IsNullOrEmpty(hostFailures))
                return "the host is not complete (" + hostFailures + ")";
            return null;
        }

        public static string WorldRefusalLine(string saveName, string reason) =>
            "!!! Fresh campaign: REFUSED to make a new world for save '" + saveName + "': " + reason + ". A world made without Realm of Thrones' code has the game's own "
            + "calendar and rules. Fix what the Host lines say, or set " + RotRules.HostVariable + " to patches or lower for a test world that is not kept.";

        // A world made while Realm of Thrones' time model was not registered starts in the
        // game's own year and is no Realm of Thrones world. A new one must be in the start year;
        // a loaded one cannot be before it and has not reached the game's own start year.
        public static bool IsRotYear(int year, bool newWorld) =>
            newWorld ? year == RotHostTables.StartYear : year >= RotHostTables.StartYear && year < RotHostTables.VanillaStartYear;

        public static string WorldYearLine(int year, bool newWorld) =>
            IsRotYear(year, newWorld)
                ? "Host: the " + (newWorld ? "new" : "loaded") + " world is in the year " + year + " of Realm of Thrones' calendar."
                : Loud + "THIS " + (newWorld ? "NEW" : "LOADED") + " WORLD IS IN THE YEAR " + year + ". A Realm of Thrones world starts in " + RotHostTables.StartYear
                    + " and the game's own in " + RotHostTables.VanillaStartYear + ", so it was made without Realm of Thrones' time model. DO NOT KEEP ITS SAVE.";
    }
}
