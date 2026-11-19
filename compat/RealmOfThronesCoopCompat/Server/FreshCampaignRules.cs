#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // FreshCampaign's plain rules, adapted from Burning Empires Coop Compat, without game types, so
    // the launcher tests can run them.
    internal static class FreshCampaignRules
    {
        // A save made with Realm of Thrones lists at least one of its modules.
        public static readonly string[] RotModuleIds = { "ROT-Core", "ROT-Content", "ROT_Map", "ROT-Dragon" };

        // The vanilla saves the nightly server copies under a new save name, with War Sails on or off.
        public static readonly string[] TemplateFileNames = { "default_new_game_warsails.sav", "default_new_game.sav" };

        public enum SaveKind
        {
            // Lists a Realm of Thrones module: loads as it is.
            RotWorld,
            // The server's untouched copy of a template: a new campaign starts in its place.
            TemplateCopy,
            // Its header is readable and lists no Realm of Thrones module, and it is not a template
            // copy (or its file cannot be found to compare): refused, because a Realm of Thrones
            // server cannot load it (it died in the distance cache before).
            OtherWorld,
            // Its header cannot be read and its file was not found, so nothing can be told: loads as
            // it is, loudly.
            NoFile,
            // Its header cannot be read and it is not a template copy: it may be a Realm of Thrones
            // save, so it loads as it is, loudly.
            Unknown,
        }

        public static bool HasRotModule(IEnumerable<string> saveModules) =>
            saveModules != null && saveModules.Any(module => RotModuleIds.Any(id => string.Equals(module, id, StringComparison.OrdinalIgnoreCase)));

        // Both template names in each folder (the server keeps them next to its exe) and next to
        // the save itself (its Game Saves folder). The save's own path is left out, so a save named
        // like a template that already holds a campaign never counts as its own template.
        public static List<string> TemplateCandidates(string savePath, params string[] folders)
        {
            var result = new List<string>();
            var saveFull = FullPath(savePath);
            var all = new List<string>(folders ?? new string[0]);
            if (!string.IsNullOrEmpty(savePath))
                all.Add(Path.GetDirectoryName(savePath));
            foreach (var folder in all)
            {
                if (string.IsNullOrEmpty(folder))
                    continue;
                foreach (var name in TemplateFileNames)
                {
                    var candidate = FullPath(Path.Combine(folder, name));
                    if (candidate == null || string.Equals(candidate, saveFull, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!result.Exists(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase)))
                        result.Add(candidate);
                }
            }
            return result;
        }

        // A save is the server's untouched copy of a template when it lists no Realm of Thrones
        // module and its bytes match one of the template files. Once the server has saved a Realm
        // of Thrones campaign under that name, both tests fail and it loads.
        public static SaveKind Classify(IEnumerable<string> saveModules, string savePath, IEnumerable<string> templates, out string template)
        {
            template = null;
            if (HasRotModule(saveModules))
                return SaveKind.RotWorld;
            // Only a readable header that lists no Realm of Thrones module can refuse a save.
            var headerRead = saveModules != null;
            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                return headerRead ? SaveKind.OtherWorld : SaveKind.NoFile;
            foreach (var candidate in templates ?? new string[0])
            {
                if (SameBytes(savePath, candidate))
                {
                    template = candidate;
                    return SaveKind.TemplateCopy;
                }
            }
            return headerRead ? SaveKind.OtherWorld : SaveKind.Unknown;
        }

        // The one line a boot log needs to tell what happened to the save.
        public static string DecisionLine(SaveKind kind, string saveName, string savePath, IEnumerable<string> saveModules, string template)
        {
            var modules = saveModules == null ? "unknown" : string.Join(", ", saveModules);
            switch (kind)
            {
                case SaveKind.RotWorld:
                    return "Fresh campaign: save '" + saveName + "' was made with Realm of Thrones (modules " + modules + "), so it loads normally and no new campaign starts.";
                case SaveKind.TemplateCopy:
                    return "Fresh campaign: save '" + saveName + "' is the server's copy of " + template + " and lists no Realm of Thrones module, "
                        + "so a new Realm of Thrones campaign starts in its place. The first server save writes it under the same name.";
                case SaveKind.NoFile:
                    return "!!! Fresh campaign: save '" + saveName + "' was not found at " + (savePath ?? "an unknown path")
                        + " and its header cannot be read, so it cannot be checked and loads as it is. If it is the server's blank save, the server will fail on Realm of Thrones' map.";
                case SaveKind.Unknown:
                    return "!!! Fresh campaign: save '" + saveName + "' (" + savePath + ") has a header that cannot be read and is not the server's blank save, "
                        + "so it loads as it is. If it was not made with Realm of Thrones, the server will fail on Realm of Thrones' map.";
                default:
                    return "!!! Fresh campaign: REFUSED save '" + saveName + "': it was not made with Realm of Thrones (modules " + modules + ", file "
                        + (string.IsNullOrEmpty(savePath) || !File.Exists(savePath) ? "not found" : savePath)
                        + ") and is not the server's blank save, so this server cannot load it. Pick a new save name to start a new Realm of Thrones campaign.";
            }
        }

        // ---- the world check after a new world is made (HeadlessCampaignManager) ----

        // Realm of Thrones 8.1.8 has 29 kingdoms and 246 clans. The kingdom floor is the whole count;
        // the clan floor is the one the fresh world report set, three below the count.
        public const int MinKingdoms = 29;
        public const int MinClans = 243;
        public const string WorldCheckVariable = "COODSZ_ROT_WORLDCHECK";

        // The settlement files ROT_Map's SubModule.xml names (path "settlements" and "ROT_settlements").
        public static readonly string[] SettlementFiles = { "settlements.xml", "ROT_settlements.xml" };

        // The Settlement nodes under the file's root, as the game reads them (a commented out
        // one does not count).
        public static int CountSettlements(TextReader text)
        {
            var count = 0;
            using (var reader = System.Xml.XmlReader.Create(text, new System.Xml.XmlReaderSettings { IgnoreComments = true, DtdProcessing = System.Xml.DtdProcessing.Ignore }))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.Depth == 1 && reader.Name == "Settlement")
                        count++;
                }
            }
            return count;
        }

        // Each floor the new world misses. expectedSettlements below 0 means it could not be
        // counted, which is said in the line but is no failure. The year and the time model only
        // count where Realm of Thrones' time model is meant to run (models and full).
        public static List<string> WorldCheckFailures(int kingdoms, int clans, int settlements, int expectedSettlements, IList<string> unownedFiefs,
            bool yearMatters, int year, bool timeModelRegistered, IList<string> hostFailures = null)
        {
            var failed = new List<string>();
            if (kingdoms < MinKingdoms)
                failed.Add("kingdoms " + kingdoms + ", expected " + MinKingdoms + " or more");
            if (clans < MinClans)
                failed.Add("clans " + clans + ", expected " + MinClans + " or more");
            if (expectedSettlements >= 0 && settlements != expectedSettlements)
                failed.Add("settlements " + settlements + ", Realm of Thrones' map defines " + expectedSettlements);
            if (unownedFiefs != null && unownedFiefs.Count > 0)
                failed.Add(unownedFiefs.Count + " towns or castles without an owner clan (" + string.Join(", ", unownedFiefs.Take(5)) + (unownedFiefs.Count > 5 ? ", ..." : "") + ")");
            if (yearMatters && !timeModelRegistered)
                failed.Add("Realm of Thrones' time model is not registered");
            if (yearMatters && year != RotHostTables.StartYear)
                failed.Add("year " + year + ", expected " + RotHostTables.StartYear);
            if (hostFailures != null)
                failed.AddRange(hostFailures);
            return failed;
        }

        // At full a failed world is held back (never marked loaded, so never served or saved),
        // unless COODSZ_ROT_WORLDCHECK=warn; below full it always goes on.
        public static bool HoldsWorld(bool passed, HostLevel asked, string worldCheckVariable) =>
            !passed && asked == HostLevel.Full && !string.Equals(worldCheckVariable?.Trim(), "warn", StringComparison.OrdinalIgnoreCase);

        // The one line a tool can wait for.
        public static string WorldCheckLine(IList<string> failed, int kingdoms, int clans, int settlements, int expectedSettlements, int year, bool held)
        {
            var numbers = "kingdoms " + kingdoms + ", clans " + clans + ", settlements " + settlements + " (the map defines "
                + (expectedSettlements >= 0 ? expectedSettlements.ToString() : "an uncounted number") + "), year " + year;
            if (failed == null || failed.Count == 0)
                return "World check passed: " + numbers + ", every town and castle has an owner clan.";
            return "!!! World check FAILED: " + string.Join("; ", failed) + " (" + numbers + "). "
                + (held ? "This world is held back: the server never finishes loading it, so it never serves or saves it (" + WorldCheckVariable + "=warn goes on anyway)."
                    : "Going on anyway; do not keep this world's save.");
        }

        // What the world check reads from the new world.
        public sealed class WorldNumbers
        {
            public int Kingdoms;
            public int Clans;
            public int Settlements;
            public IList<string> UnownedFiefs;
            public int Year;
            public bool TimeModelRegistered;
            public IList<string> HostFailures;
        }

        // Runs the world check and logs its one line. A read that throws counts as a failed check,
        // so at full an unchecked world is held back like a failed one. True when the world may
        // be marked loaded.
        public static bool RunWorldCheck(Func<WorldNumbers> read, int expectedSettlements, HostLevel asked, string worldCheckVariable, Action<string> log)
        {
            WorldNumbers numbers;
            try
            {
                numbers = read();
            }
            catch (Exception ex)
            {
                var heldOnError = HoldsWorld(false, asked, worldCheckVariable);
                log("!!! World check FAILED: the check could not run (" + ex.GetType().Name + ": " + ex.Message + "). "
                    + (heldOnError ? "This world is held back: the server never finishes loading it, so it never serves or saves it (" + WorldCheckVariable + "=warn goes on anyway)."
                        : "Going on anyway; do not keep this world's save."));
                return !heldOnError;
            }
            var failed = WorldCheckFailures(numbers.Kingdoms, numbers.Clans, numbers.Settlements, expectedSettlements, numbers.UnownedFiefs,
                RotHostRules.HostsModels(asked), numbers.Year, numbers.TimeModelRegistered, numbers.HostFailures);
            var held = HoldsWorld(failed.Count == 0, asked, worldCheckVariable);
            log(WorldCheckLine(failed, numbers.Kingdoms, numbers.Clans, numbers.Settlements, expectedSettlements, numbers.Year, held));
            return !held;
        }

        // ---- what the save load prefix does with a save ----

        public enum LoadAction
        {
            LoadAsIs,
            StartNew,
            Refuse,
        }

        // The decision for every kind of save in one place. A foreign save is refused. At models
        // and full, while Realm of Thrones' code is not ready (worldRefusal names why), nothing
        // loads or starts at all: a new world would have the game's calendar and rules, and a
        // saved Realm of Thrones world (or one whose header cannot be read) would lose its stored
        // Realm of Thrones data at the next save. A template copy starts a new world. A Realm of
        // Thrones world below full loads for tests with a loud line, because its behaviors are
        // not registered then and their saved data does not survive a save. line is the extra
        // line to log, or null.
        public static LoadAction DecideLoad(SaveKind kind, string saveName, HostLevel asked, string worldRefusal, out string line)
        {
            line = null;
            if (kind == SaveKind.OtherWorld)
                return LoadAction.Refuse;
            if (RotHostRules.HostsModels(asked) && worldRefusal != null)
            {
                line = kind == SaveKind.TemplateCopy
                    ? RotHostRules.WorldRefusalLine(saveName, worldRefusal)
                    : "!!! Fresh campaign: REFUSED to load save '" + saveName + "': " + worldRefusal + ". Loaded without Realm of Thrones' code, a Realm of Thrones world "
                        + "loses its stored Realm of Thrones data at the next save. Fix what the Host lines say; the save file is left as it is.";
                return LoadAction.Refuse;
            }
            if (kind == SaveKind.TemplateCopy)
                return LoadAction.StartNew;
            if (kind == SaveKind.RotWorld && asked != HostLevel.Full)
                line = "!!! Fresh campaign: Realm of Thrones world '" + saveName + "' loads at host level " + asked.ToString().ToLowerInvariant()
                    + ", where Realm of Thrones' behaviors are not registered: if this server saves, the Realm of Thrones data in it is lost. Do not let it save over a world you keep.";
            return LoadAction.LoadAsIs;
        }

        public const string PinnedCulture = "empire";

        // The server's own hero's culture, by id: empire when character creation offers it, else
        // the first culture with a starting point, else the first one. Never whitewalker: Realm of
        // Thrones' creation code treats that culture as a White Walker or wight start. Returns the
        // index into the offered list, or -1 when nothing can be picked, with the rule that picked.
        public static int PickCulture(IList<KeyValuePair<string, bool>> offered, out string rule)
        {
            var cultures = offered ?? new List<KeyValuePair<string, bool>>();
            bool Allowed(int i) => !string.Equals(cultures[i].Key, JoinRules.WhiteWalkerCulture, StringComparison.OrdinalIgnoreCase);
            for (var i = 0; i < cultures.Count; i++)
            {
                if (string.Equals(cultures[i].Key, PinnedCulture, StringComparison.OrdinalIgnoreCase))
                {
                    rule = "empire is offered";
                    return i;
                }
            }
            for (var i = 0; i < cultures.Count; i++)
            {
                if (Allowed(i) && cultures[i].Value)
                {
                    rule = "empire is not offered, first culture with a starting point";
                    return i;
                }
            }
            for (var i = 0; i < cultures.Count; i++)
            {
                if (Allowed(i))
                {
                    rule = "no culture has a starting point, first culture offered";
                    return i;
                }
            }
            rule = "nothing but whitewalker is offered";
            return -1;
        }

        public static bool SameBytes(string first, string second)
        {
            try
            {
                if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second) || !File.Exists(first) || !File.Exists(second))
                    return false;
                if (new FileInfo(first).Length != new FileInfo(second).Length)
                    return false;
                using (var a = File.OpenRead(first))
                using (var b = File.OpenRead(second))
                {
                    var bufferA = new byte[81920];
                    var bufferB = new byte[81920];
                    while (true)
                    {
                        var readA = ReadFull(a, bufferA);
                        var readB = ReadFull(b, bufferB);
                        if (readA != readB)
                            return false;
                        if (readA == 0)
                            return true;
                        for (var i = 0; i < readA; i++)
                        {
                            if (bufferA[i] != bufferB[i])
                                return false;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static int ReadFull(Stream stream, byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                    break;
                total += read;
            }
            return total;
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }
    }
}
