#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RealmOfThronesCoopCompat
{
    // The plain rules of what travels on our link channels. No game or link types, so the launcher
    // tests can run them.
    //
    // Both channels carry entries of three fields, "kind<TAB>id<TAB>value", one per line:
    // - S16 (rot.world): s = a settlement's culture, h = a hero's culture, v = a hero's volunteer
    //   types ("count:" then the slots' character ids joined by commas, an empty slot empty). The game keeps no
    //   settlement culture in a save (Settlement.Culture has no save accessor in 1.4.8, Hero.Culture
    //   and Hero.VolunteerTypes have one), so the snapshot for a player who entered carries every
    //   settlement's culture plus the heroes a hosted write touched since the server started.
    //   Also r = a hero's race (CharacterObject.Race, the White Walker look; Coop sends it live
    //   too, the snapshot is ours), l = one of a location's four scene names (id
    //   "settlement/location/level", King's Landing for the Blackwater) and x = a clan taken off the
    //   campaign's clan list (Renly's and Stannis' successions; value "removed").
    // - S70 (rot.probe): w = a war between two factions (id "a~b", ordered), o = a town's or
    //   castle's owner clan, c = its culture, e = a Realm of Thrones story event (value "active/total"),
    //   n = counts. A kind whose id is "*" says that side could not read that kind at all.
    internal static class RotSyncRules
    {
        public const char SettlementCulture = 's';
        public const char HeroCulture = 'h';
        public const char Volunteers = 'v';
        public const char HeroRace = 'r';
        public const char LocationScene = 'l';
        public const char ClanRemoved = 'x';
        public const string Removed = "removed";
        // A location keeps a scene name per upgrade level 0 to 3 (Location._sceneNames in 1.4.8).
        public const int SceneLevels = 4;
        public const int MaxSceneChars = 128;

        public static string SceneId(string settlement, string location, int level) =>
            settlement + "/" + location + "/" + level.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static bool TryParseSceneId(string id, out string settlement, out string location, out int level)
        {
            settlement = location = null;
            level = -1;
            var parts = (id ?? "").Split('/');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0
                || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out level) || level >= SceneLevels)
            {
                level = -1;
                return false;
            }
            settlement = parts[0];
            location = parts[1];
            return true;
        }

        // A scene name a PC may set: letters, digits, '_', '-' and '.', at most MaxSceneChars.
        public static bool IsSceneName(string value) =>
            !string.IsNullOrEmpty(value) && value.Length <= MaxSceneChars && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.');

        // A race index this PC knows (raceCount from FaceGen; 0 means unknown, then any index).
        public static bool TryRace(string value, int raceCount, out int race) =>
            int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out race) && (raceCount <= 0 || race < raceCount);

        // The clans of before that are no longer in after, by id, in order.
        public static List<string> RemovedClans(IEnumerable<string> before, IEnumerable<string> after)
        {
            var now = new HashSet<string>(after ?? new string[0], StringComparer.Ordinal);
            return (before ?? new string[0]).Where(id => id != null && !now.Contains(id)).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        public const char War = 'w';
        public const char Owner = 'o';
        public const char FortCulture = 'c';
        public const char StoryEvent = 'e';
        public const char Count = 'n';
        public const string Unreadable = "*";

        // The drift digest is a few KB packed; one far bigger means something is wrong with it.
        public const int MaxDigestBytes = 64 * 1024;
        // Differences one drift line names before it only counts the rest.
        public const int DifferencesShown = 12;

        public static string Entry(char kind, string id, string value) => kind + "\t" + Clean(id) + "\t" + Clean(value);

        // Ids and values never hold a tab or a line break; one that would is cut there.
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var cut = text.IndexOfAny(new[] { '\t', '\n', '\r' });
            return cut < 0 ? text : text.Substring(0, cut);
        }

        public static bool TryParse(string line, out char kind, out string id, out string value)
        {
            kind = '\0';
            id = value = null;
            if (string.IsNullOrEmpty(line))
                return false;
            var first = line.IndexOf('\t');
            var second = first < 0 ? -1 : line.IndexOf('\t', first + 1);
            if (first != 1 || second < 0)
                return false;
            kind = line[0];
            id = line.Substring(first + 1, second - first - 1);
            value = line.Substring(second + 1);
            return id.Length > 0;
        }

        // The entry's "kind<TAB>id", which names the value it holds.
        public static string KeyOf(string line)
        {
            var second = line == null ? -1 : line.IndexOf('\t', Math.Min(2, line.Length));
            return second < 0 ? line : line.Substring(0, second);
        }

        // "count:id,id,...": the count first, so an empty array and one empty slot stay apart.
        public static string VolunteerValue(IEnumerable<string> slotIds)
        {
            var ids = (slotIds ?? new string[0]).Select(id => id ?? "").ToList();
            return ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + string.Join(",", ids);
        }

        // Each slot's character id, or null for an empty slot. Null when the value is not one
        // VolunteerValue wrote (its count and its slots disagree).
        public static string[] VolunteerSlots(string value)
        {
            var colon = (value ?? "").IndexOf(':');
            if (colon < 1 || !int.TryParse(value.Substring(0, colon), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
                return null;
            if (count == 0)
                return value.Length == colon + 1 ? new string[0] : null;
            var slots = value.Substring(colon + 1).Split(',').Select(id => id.Length == 0 ? null : id).ToArray();
            return slots.Length == count ? slots : null;
        }

        // A digest is compared only with the world of the same day: this PC may still be on the day
        // before while it catches up, and every owner change of that day would look like a drift.
        public static string DriftDayLine(int day, int hereDay) =>
            RotLinkRules.Log + "drift probe day " + day + " not compared, this PC is on day " + hereDay + ".";

        // The entries of after whose value is new or differs from before's (both keyed by KeyOf).
        // The snapshots around one hosted write, so only what it changed is sent.
        public static List<string> Changes(IDictionary<string, string> before, IDictionary<string, string> after)
        {
            var changes = new List<string>();
            foreach (var pair in after.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (before.TryGetValue(pair.Key, out var old) && old == pair.Value)
                    continue;
                changes.Add(pair.Key + "\t" + pair.Value);
            }
            return changes;
        }

        // Splits entries into parts of at most maxBytes as packed by Pack, in their order. Every
        // part stands on its own, so a PC applies each part as it arrives. An entry bigger than
        // maxBytes on its own cannot be sent and is left out (counted in skipped).
        public static List<List<string>> Split(IList<string> entries, int maxBytes, out int skipped)
        {
            skipped = 0;
            var parts = new List<List<string>>();
            var part = new List<string>();
            var size = 0;
            foreach (var entry in entries ?? new string[0])
            {
                var bytes = Encoding.UTF8.GetByteCount(entry) + 1;
                if (bytes > maxBytes)
                {
                    skipped++;
                    continue;
                }
                if (size + bytes > maxBytes && part.Count > 0)
                {
                    parts.Add(part);
                    part = new List<string>();
                    size = 0;
                }
                part.Add(entry);
                size += bytes;
            }
            if (part.Count > 0)
                parts.Add(part);
            return parts;
        }

        // One entry per line, UTF-8.
        public static byte[] Pack(IEnumerable<string> entries)
        {
            var text = new StringBuilder();
            foreach (var entry in entries ?? new string[0])
                text.Append(entry).Append('\n');
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        public static List<string> Unpack(byte[] blob) =>
            blob == null || blob.Length == 0
                ? new List<string>()
                : Encoding.UTF8.GetString(blob).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        // The drift digest is packed and then gzipped: ids repeat a lot, so it shrinks to a few KB.
        public static byte[] Compress(IEnumerable<string> entries)
        {
            var raw = Pack(entries);
            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output, CompressionMode.Compress, true))
                    zip.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        // Null when the blob is not a whole gzipped digest (a stream cut short can end without an
        // error, so the length gzip keeps in its last four bytes must match) or unpacks past maxBytes.
        public static List<string> Decompress(byte[] blob, int maxBytes)
        {
            if (blob == null || blob.Length < 18)
                return null;
            try
            {
                using (var input = new MemoryStream(blob))
                using (var zip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = zip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > maxBytes)
                            return null;
                    }
                    var stored = (uint)(blob[blob.Length - 4] | blob[blob.Length - 3] << 8 | blob[blob.Length - 2] << 16 | blob[blob.Length - 1] << 24);
                    return stored == (uint)output.Length ? Unpack(output.ToArray()) : null;
                }
            }
            catch (InvalidDataException)
            {
                return null;
            }
            catch (IOException)
            {
                // EndOfStreamException among them: a stream cut short.
                return null;
            }
        }

        // A short fingerprint of a set of entries in any order: the server sends a player a new
        // snapshot only when this differs from the one that player got last.
        public static string Hash(IEnumerable<string> entries)
        {
            ulong hash = 14695981039346656037;
            foreach (var entry in (entries ?? new string[0]).OrderBy(entry => entry, StringComparer.Ordinal))
            {
                foreach (var b in Encoding.UTF8.GetBytes(entry + "\n"))
                    hash = unchecked((hash ^ b) * 1099511628211);
            }
            return hash.ToString("x16");
        }

        // The war entry of two factions, the same whichever is named first.
        public static string WarEntry(string faction1, string faction2) =>
            string.CompareOrdinal(faction1, faction2) <= 0 ? Entry(War, faction1 + "~" + faction2, "war") : Entry(War, faction2 + "~" + faction1, "war");

        // What differs between the server's digest and this PC's, one text per difference, in key
        // order. A kind either side could not read is one difference saying so, and its entries
        // are not compared.
        public static List<string> Differences(IList<string> server, IList<string> here)
        {
            var serverMap = ToMap(server);
            var hereMap = ToMap(here);
            var unreadable = new SortedSet<char>();
            foreach (var key in serverMap.Keys.Concat(hereMap.Keys))
            {
                if (key.Length > 2 && key.Substring(2) == Unreadable)
                    unreadable.Add(key[0]);
            }
            var differences = new List<string>();
            foreach (var kind in unreadable)
            {
                var who = serverMap.ContainsKey(kind + "\t" + Unreadable) ? (hereMap.ContainsKey(kind + "\t" + Unreadable) ? "the server and this PC" : "the server") : "this PC";
                differences.Add(KindName(kind) + " not compared: not readable on " + who);
            }
            foreach (var key in serverMap.Keys.Union(hereMap.Keys).OrderBy(key => key, StringComparer.Ordinal))
            {
                if (key.Length < 3 || unreadable.Contains(key[0]))
                    continue;
                serverMap.TryGetValue(key, out var serverValue);
                hereMap.TryGetValue(key, out var hereValue);
                if (serverValue == hereValue)
                    continue;
                var what = KindName(key[0]) + " " + key.Substring(2);
                if (serverValue == null)
                    differences.Add(what + ": only here (" + hereValue + ")");
                else if (hereValue == null)
                    differences.Add(what + ": missing here (server " + serverValue + ")");
                else
                    differences.Add(what + ": server " + serverValue + ", here " + hereValue);
            }
            return differences;
        }

        private static Dictionary<string, string> ToMap(IEnumerable<string> entries)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries ?? new string[0])
            {
                if (TryParse(entry, out var kind, out var id, out var value))
                    map[kind + "\t" + id] = value;
            }
            return map;
        }

        public static string KindName(char kind)
        {
            switch (kind)
            {
                case War: return "war";
                case Owner: return "owner of";
                case FortCulture: return "culture of";
                case StoryEvent: return "story event";
                case Count: return "count";
                case SettlementCulture: return "settlement culture";
                case HeroCulture: return "hero culture";
                case Volunteers: return "volunteers of";
                case HeroRace: return "race of";
                case LocationScene: return "scene of";
                case ClanRemoved: return "clan off the list";
                default: return "kind " + kind;
            }
        }

        // The one line a tester waits for on a player's PC each in game day. secondLook: the
        // differences are what was still different about 5 seconds after the first compare, which
        // found firstLook (said when the second finds none).
        public static string DriftLine(int day, int entries, IList<string> differences, bool secondLook = false, int firstLook = 0)
        {
            var after = secondLook ? " (after a second look)" : "";
            if (differences == null || differences.Count == 0)
                return RotLinkRules.Log + "drift probe day " + day + ": no difference" + (secondLook ? " (after a second look; the first look had " + firstLook + ")" : "")
                    + " (" + entries + " entries compared).";
            var shown = differences.Take(DifferencesShown).ToList();
            return RotLinkRules.Log + "drift probe day " + day + ": " + differences.Count + " difference(s)" + after + " in " + entries + " entries: " + string.Join("; ", shown)
                + (differences.Count > shown.Count ? "; and " + (differences.Count - shown.Count) + " more" : "") + ".";
        }

        // A compare can run before Coop's own messages of that moment landed here (live test
        // 2026-11-11: three wars "missing here" that were there the next day). So a compare that
        // finds differences says nothing yet and looks again this long after, in real time.
        public const int SecondLookMs = 5000;

        // What to do after a compare: say the line now, or look again later.
        public static bool LooksAgain(int differences, bool secondLook) => differences > 0 && !secondLook;

        public static bool SecondLookDue(long now, long dueAt) => now >= dueAt;

        public static string DriftSentLine(int day, int entries, int bytes) =>
            RotLinkRules.Log + "drift probe day " + day + " sent: " + entries + " entries, " + bytes + " bytes packed.";
    }
}
