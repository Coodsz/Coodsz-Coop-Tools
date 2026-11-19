// Coodsz Settings Sync, server side files: the host's "players keep their own" list and the host's
// Configs\ModSettings\Global JSON files, which go into the dedicated server's live MCM (HostSettingsFiles), or
// are sent as they are when MCM's provider does not run there.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
// Reading his recipes.json PlayersOwnSettings list follows his.
//
// Engine free; the launcher's tests compile this file as is.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoodszSettingsSync
{
    public static class SettingsFiles
    {
        /// <summary>
        /// Reads recipes.json the way ModderLords does: only its "PlayersOwnSettings" array of module ids (the rest
        /// of a ModderLords recipe is ignored here). Missing or broken means an empty list.
        /// </summary>
        public static KeepOwnList ReadKeepOwn(string path) => TryReadKeepOwn(path) ?? new KeepOwnList();

        /// <summary>
        /// Like <see cref="ReadKeepOwn"/>, but null when the file is there and cannot be read or parsed (it may be
        /// half written), so the caller keeps its last good list. A missing file, or one without the field, is an
        /// empty list: every mod locked to the host.
        /// </summary>
        public static KeepOwnList? TryReadKeepOwn(string path)
        {
            try
            {
                if (!File.Exists(path)) return new KeepOwnList();
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!document.RootElement.TryGetProperty(KeepOwnList.Field, out JsonElement list)) return new KeepOwnList();
                if (list.ValueKind != JsonValueKind.Array) return null;
                return new KeepOwnList(list.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? ""));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The host's ModSettings folder: Coodsz Host copies each setup's files into
        /// CoopData\DedicatedServer\Configs\ModSettings before Start. Falls back to the game's own one. The dedicated
        /// server's MCM does not read the CoopData one: it loads its Global settings at boot from the game's own
        /// folder, before Coop points the game's files at CoopData\DedicatedServer. The Realm of Thrones Server Tool's
        /// start names its own server data folder in <see cref="ServerDataDirVariable"/> (<paramref name="serverDataDirectory"/>),
        /// so Configs\ModSettings there comes first instead of Coodsz Host's. MCM's provider does not run on that server,
        /// so with no such folder players get the MCM files saved on the host's PC, the game's own Configs\ModSettings.
        /// </summary>
        public static string? FindModSettingsRoot(string documents, string? serverDataDirectory = null)
        {
            string own = CoopDataModSettingsPath(documents, serverDataDirectory);
            // The Realm of Thrones server's folder only counts once it holds a file: that folder is Coop's data folder, and an
            // empty Configs\ModSettings made there by anything would otherwise leave players with nothing.
            if (!string.IsNullOrWhiteSpace(serverDataDirectory) && !HasFiles(own)) own = "";
            string[] candidates =
            {
                own,
                Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModSettings"),
            };
            return candidates.FirstOrDefault(path => path.Length > 0 && Directory.Exists(path));
        }

        /// <summary>
        /// Only the Realm of Thrones and TAOM Server Tools' starts set this, to their own server data folder. Coodsz Host never
        /// does, so its server keeps reading Documents. TAOM's start also sets BANNERLORD_USER_DIR, which is why that one is not read.
        /// </summary>
        public const string ServerDataDirVariable = "COODSZ_SERVER_DATA_DIR";

        /// <summary>CoopData\DedicatedServer\Configs\ModSettings, the folder Coodsz Host fills, or null when it is not there.</summary>
        public static string? FindCoopDataModSettings(string documents, string? serverDataDirectory = null)
        {
            string path = CoopDataModSettingsPath(documents, serverDataDirectory);
            return Directory.Exists(path) ? path : null;
        }

        private static bool HasFiles(string folder)
        {
            try { return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }

        private static string CoopDataModSettingsPath(string documents, string? serverDataDirectory) =>
            string.IsNullOrWhiteSpace(serverDataDirectory)
                ? Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "Configs", "ModSettings")
                : Path.Combine(serverDataDirectory!.Trim(), "Configs", "ModSettings");

        /// <summary>
        /// Every Global\&lt;folder&gt;\&lt;id&gt;.json file as a snapshot. MCM names the file after the settings id and
        /// the keys after the property ids. A dropdown is stored as its index, so it travels as a whole number
        /// with FromFile set, and the client takes it only for a dropdown it really has. <paramref name="unread"/> hears
        /// of each file that could not be read or parsed (its path and why); that file is left out. <paramref name="leftOut"/>
        /// hears of each settings object, value or file in the folder that is left out on purpose (its id or file path, and why).
        /// </summary>
        public static List<SettingsSnapshotData> CaptureFromFiles(string modSettingsRoot, KeepOwnList keepOwn, Action<string, string>? unread = null,
            Action<string, string>? leftOut = null)
        {
            var result = new List<SettingsSnapshotData>();
            // Every id a file was found for, read or not: a second file for the id of a locked one is left out too, so a
            // reread keeps the locked file's last good read instead of taking the second file in its place.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string global = Path.Combine(modSettingsRoot, "Global");
            if (!Directory.Exists(global)) return result;
            foreach (string file in Directory.GetFiles(global, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (result.Count >= SettingsWire.MaxObjectsPerSession)
                {
                    leftOut?.Invoke(id, "more than " + SettingsWire.MaxObjectsPerSession + " settings files");
                    break;
                }
                string folder = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
                // The files do not say which module they belong to; the settings id and its folder name are matched.
                if (!SettingsWire.IsValidId(id))
                {
                    leftOut?.Invoke(file, "a file name that is no usable settings id");
                    continue;
                }
                if (keepOwn.Keeps(id, folder)) continue;
                if (SettingsWire.IsAlwaysSkipped(id))
                {
                    leftOut?.Invoke(id, "a hotkey, interface, sound or camera settings object");
                    continue;
                }
                if (!seen.Add(id))
                {
                    leftOut?.Invoke(file, "a second file for settings id " + id + (result.Any(r => r.SettingsId == id)
                        ? ", the first one is used"
                        : ", left out too since the first one could not be read or has no values"));
                    continue;
                }
                List<string>? values = ReadValues(file, id, leftOut, out string? why);
                if (values == null) unread?.Invoke(file, why ?? "unreadable");
                if (values != null && values.Count > 0) result.Add(new SettingsSnapshotData(id, SettingsScope.Global, true, values));
            }
            return result;
        }

        private static List<string>? ReadValues(string file, string id, Action<string, string>? leftOut, out string? why)
        {
            why = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    why = "not a JSON object";
                    return null;
                }
                var values = new List<string>();
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    string name = id + "." + property.Name;
                    if (values.Count >= SettingsWire.MaxValuesPerObject)
                    {
                        leftOut?.Invoke(name, "more than " + SettingsWire.MaxValuesPerObject + " values in one file");
                        break;
                    }
                    if (SettingsWire.IsAlwaysSkipped(property.Name))
                    {
                        leftOut?.Invoke(name, "a hotkey, interface, sound or camera setting");
                        continue;
                    }
                    WireValue? value = ToWire(property.Name, property.Value, out string? reason);
                    string? line = value == null ? null : SettingsWire.Encode(value);
                    if (line != null) values.Add(line);
                    else leftOut?.Invoke(name, reason ?? "a value that can't travel");
                }
                return values;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                why = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private static WireValue? ToWire(string id, JsonElement element, out string? reason)
        {
            reason = null;
            switch (element.ValueKind)
            {
                case JsonValueKind.True: return new WireValue(WireKind.Bool, id, SettingsWire.FormatBool(true));
                case JsonValueKind.False: return new WireValue(WireKind.Bool, id, SettingsWire.FormatBool(false));
                case JsonValueKind.String:
                    string text = element.GetString() ?? "";
                    // A token, password, web address or file path stays on the host's PC.
                    if (text.Length > SettingsWire.MaxStringLength) reason = "text longer than " + SettingsWire.MaxStringLength + " characters";
                    else if (SettingsWire.IsPrivateText(text, id)) reason = "text that looks private (a token, password, web address or file path)";
                    return reason != null ? null : new WireValue(WireKind.String, id, text);
                case JsonValueKind.Number:
                    // "3" is a whole number (an int or a dropdown index); "3.0" or "0.5" is a float.
                    string raw = element.GetRawText();
                    if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && element.TryGetInt32(out int whole))
                        return new WireValue(WireKind.Int, id, SettingsWire.FormatInt(whole));
                    if (element.TryGetDouble(out double number) && !double.IsInfinity(number))
                        return new WireValue(WireKind.Float, id, SettingsWire.FormatFloat(number));
                    reason = "a number out of range";
                    return null;
                default:
                    reason = "not a plain value (a list or an object)";
                    return null;
            }
        }
    }

    /// <summary>
    /// On the dedicated server: puts the values in the host's CoopData\DedicatedServer ModSettings files into the live
    /// MCM, so the server, its players and Coodsz Host's Mod settings page all have the host's values. Each Global
    /// settings object gets them once per campaign, as soon as MCM has made it; PerCampaign and PerSave objects ride with
    /// the campaign and are never touched. A value the host set through the live channel is newer than the file and
    /// stays. A file value goes in only when its kind fits the setting, held to the setting's limits, as the value
    /// SettingsWire.TryDecide decided (a dropdown by its index, never by a choice's name).
    ///
    /// The folder is looked for again about once a minute until it is there. The files are read once; a file that
    /// cannot be read (locked, half written) is logged once and the folder is read again about once a minute, up to
    /// <see cref="MaxReads"/> reads. Off for good when another mod already reloads the files (CoopModPatch).
    /// </summary>
    public sealed class HostSettingsFiles
    {
        /// <summary>Passes between two looks at a missing folder or two reads of a folder with a bad file (about a minute at 3 seconds a pass).</summary>
        public const int RetryPasses = 20;
        public const int MaxReads = 10;
        private const int MaxObjectFailures = 3;

        public readonly struct Result
        {
            public Result(int objects, int values, int changed, int skipped)
            {
                Objects = objects;
                Values = values;
                Changed = changed;
                Skipped = skipped;
            }

            /// <summary>Global settings objects that got their file values in this pass.</summary>
            public int Objects { get; }
            /// <summary>Values set from the files (the same value as before counts too).</summary>
            public int Values { get; }
            /// <summary>Of those, values that were different before.</summary>
            public int Changed { get; }
            /// <summary>File values that could not go in (named in the log line).</summary>
            public int Skipped { get; }
        }

        private readonly Func<string?> _findRoot;
        private readonly Func<bool> _otherReload;
        private readonly Action<string> _log;
        private readonly Func<string, Action<string, string>, List<SettingsSnapshotData>> _read;
        private readonly int _retryPasses;
        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _unreadNoted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _objectFailures = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _leftOutNoted = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _leftOutNew = new List<string>();
        private List<SettingsSnapshotData>? _files;
        private bool _otherChecked;
        private bool _missingNoted;
        private bool _readComplete;
        private int _wait;
        private int _generation = -1;

        /// <param name="findRoot">The CoopData ModSettings folder, or null while it is not there.</param>
        /// <param name="otherReload">True when another mod reloads the files into MCM itself.</param>
        /// <param name="log">A line in the server console.</param>
        /// <param name="read">Reads the folder; its second argument hears of each file it could not read.</param>
        /// <param name="retryPasses">Passes between two looks or reads.</param>
        public HostSettingsFiles(Func<string?> findRoot, Func<bool> otherReload, Action<string> log,
            Func<string, Action<string, string>, List<SettingsSnapshotData>>? read = null, int retryPasses = RetryPasses)
        {
            _findRoot = findRoot;
            _otherReload = otherReload;
            _log = log;
            _read = read ?? ((folder, unread) => SettingsFiles.CaptureFromFiles(folder, new KeepOwnList(), unread, NoteLeftOut));
            _retryPasses = Math.Max(1, retryPasses);
        }

        /// <summary>The folder, once found.</summary>
        public string? Root { get; private set; }

        /// <summary>True when another mod reloads the files, so this does nothing.</summary>
        public bool Off { get; private set; }

        /// <summary>
        /// How many times the folder was read. Reads, like the read being complete, does not reset when a campaign
        /// loads: a new campaign puts the cached values in again without reading the folder, and after the give up at
        /// <see cref="MaxReads"/> the folder is not read again until the server restarts.
        /// </summary>
        public int Reads { get; private set; }

        /// <summary>One pass, from the server's tick. <paramref name="generation"/> changes when a campaign loads, and the files then go in again.</summary>
        public Result Apply(McmSettings mcm, int generation, Func<string, string, bool>? hostSet)
        {
            if (Off) return default;
            if (generation != _generation)
            {
                _generation = generation;
                _done.Clear();
                _objectFailures.Clear();
            }
            if (!_otherChecked)
            {
                _otherChecked = true;
                if (_otherReload())
                {
                    Off = true;
                    _log("Settings sync: CoopModPatch reloads the host's mod settings files into MCM here, so the sync leaves them to it.");
                    return default;
                }
                _log("Settings sync: CoopModPatch's MCM reload was not found, so the sync puts the host's mod settings files into MCM.");
            }
            if (_wait > 0) _wait--;
            if (Root == null)
            {
                if (_wait > 0) return default;
                Root = _findRoot();
                if (Root == null)
                {
                    _wait = _retryPasses;
                    if (!_missingNoted)
                        _log("Settings sync: there is no CoopData\\DedicatedServer\\Configs\\ModSettings folder yet, so the server keeps the mod settings MCM read at boot. It is looked for again about once a minute.");
                    _missingNoted = true;
                    return default;
                }
            }
            ReadIfDue();
            if (_files == null) return default;

            int objects = 0, values = 0, changed = 0;
            var skipped = new List<string>();
            foreach (SettingsSnapshotData file in _files)
            {
                string id = file.SettingsId;
                if (_done.Contains(id)) continue;
                int set = 0, different = 0;
                var objectSkipped = new List<string>();
                bool isGlobal;
                try
                {
                    // A settings object MCM has not made yet is tried again on the next pass.
                    if (mcm.Definition(id) == null || mcm.GetSettings(id) == null) continue;
                    isGlobal = mcm.ScopeOf(id) == SettingsScope.Global;
                    if (isGlobal) ApplyObject(mcm, id, file, hostSet, ref set, ref different, objectSkipped);
                }
                catch (Exception e)
                {
                    // Tried again on the next passes; after a few failures the object keeps the values it has.
                    _objectFailures.TryGetValue(id, out int failures);
                    _objectFailures[id] = ++failures;
                    Exception inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    if (failures == 1 || failures == MaxObjectFailures)
                        LogNamed("Settings sync: putting the host's file values into " + id + " failed: " + inner.GetType().Name + ": " + inner.Message
                            + (failures == MaxObjectFailures ? ". It keeps the values it has for this campaign." : ". It is tried again.")
                            + (objectSkipped.Count == 0 ? "" : " Before that, " + Count(objectSkipped.Count) + " kept the value MCM read at boot:"),
                            objectSkipped);
                    if (failures >= MaxObjectFailures) _done.Add(id);
                    continue;
                }
                // Done only after a whole walk, so an object that threw part way is tried again.
                _done.Add(id);
                // Not an applied object when there was nothing to do: every value was set by the host through the live channel.
                if (!isGlobal || (set == 0 && objectSkipped.Count == 0)) continue;
                objects++;
                values += set;
                changed += different;
                skipped.AddRange(objectSkipped);
            }
            if (objects > 0)
                LogNamed("Settings sync: put " + Count(values) + " values (" + Count(changed) + " changed) of " + Count(objects) + " settings object"
                    + (objects == 1 ? "" : "s") + " from the host's files in " + Root + " into the server's MCM."
                    + (skipped.Count == 0 ? "" : " " + Count(skipped.Count) + " kept the value MCM read at boot:"), skipped);
            return new Result(objects, values, changed, skipped.Count);
        }

        // Every file value that can't go in is named in the pass's log line, with why.
        private static void ApplyObject(McmSettings mcm, string id, SettingsSnapshotData file, Func<string, string, bool>? hostSet,
            ref int set, ref int different, List<string> skipped)
        {
            var properties = mcm.PropertiesOf(id).GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (string line in file.Values)
            {
                WireValue? value = SettingsWire.Decode(line);
                // A value the host set through the live channel is newer than the file; keeping it is no failure.
                if (value == null || (hostSet != null && hostSet(id, value.Id))) continue;
                string name = id + "." + value.Id;
                if (!properties.TryGetValue(value.Id, out McmProperty? property) || property.Kind == null)
                {
                    skipped.Add(name + " (not a setting MCM shares on this server)");
                    continue;
                }
                WireKind kind = property.Kind.Value;
                // The file's kind must fit the setting: a whole number fits a float, and is a dropdown's index.
                if (!SettingsWire.TryDecide(value, kind, property.Min, property.Max, kind == WireKind.Dropdown ? property.DropdownCount : 0, true,
                        out object? decided, out string reason) || decided == null)
                {
                    skipped.Add(name + " (" + (reason == "kind" ? "a different type here" : reason == "range" ? "a choice out of range" : "an unreadable value") + ")");
                    continue;
                }
                // One setting whose getter throws is named and left as it is; the others still go in.
                object? before;
                try { before = property.Read(); }
                catch (Exception)
                {
                    skipped.Add(name + " (could not be read)");
                    continue;
                }
                if (!LiveChannel.WriteDecided(property, decided, value.Text))
                {
                    skipped.Add(name + " (could not be set)");
                    continue;
                }
                set++;
                try { if (!LiveChannel.SameExactly(before, property.Read())) different++; }
                catch (Exception) { }
            }
        }

        /// <summary>Names per log line; a longer list goes on in more lines, so every name is logged and no line gets huge.</summary>
        public const int NamesPerLine = 10;

        // The head line takes the first names; the rest follow in "continued" lines. Every name is logged.
        private void LogNamed(string head, List<string> names)
        {
            if (names.Count == 0)
            {
                _log(head);
                return;
            }
            for (int start = 0; start < names.Count; start += NamesPerLine)
            {
                string batch = string.Join(", ", names.Skip(start).Take(NamesPerLine)) + ".";
                _log(start == 0
                    ? head + " " + batch
                    : "Settings sync: continued (" + Count(start + 1) + " to " + Count(Math.Min(start + NamesPerLine, names.Count)) + " of "
                        + Count(names.Count) + "): " + batch);
            }
        }

        // Values the files hold that are left out on purpose; one line per read names the new ones.
        private void NoteLeftOut(string name, string why)
        {
            if (_leftOutNoted.Add(name)) _leftOutNew.Add(name + " (" + why + ")");
        }

        // The first read, then again about once a minute while a file could not be read, up to MaxReads reads.
        private void ReadIfDue()
        {
            if (_readComplete || Reads >= MaxReads || (Reads > 0 && _wait > 0)) return;
            Reads++;
            _wait = _retryPasses;
            int unread = 0;
            var unreadIds = new HashSet<string>(StringComparer.Ordinal);
            List<SettingsSnapshotData>? read = null;
            try
            {
                read = _read(Root!, (path, why) =>
                {
                    unread++;
                    // MCM names the file after the settings id.
                    string id = Path.GetFileNameWithoutExtension(path);
                    unreadIds.Add(id);
                    // Logged once, and again on the last read, so a file locked on every read still says it was the last try.
                    bool last = Reads >= MaxReads;
                    if (_unreadNoted.Add(path) || last)
                        _log("Settings sync: the host's settings file " + path + " could not be read (" + why + ")"
                            + (!last ? "; it is read again later."
                                : _files != null && _files.Any(f => string.Equals(f.SettingsId, id, StringComparison.Ordinal))
                                    ? "; this was the last try, so the server keeps its last good read."
                                    : "; this was the last try, so it keeps the values MCM read at boot."));
                });
            }
            catch (Exception e)
            {
                unread++;
                _log("Settings sync: reading the host's settings files in " + Root + " failed: " + e.GetType().Name + ": " + e.Message
                    + (Reads < MaxReads ? "; tried again later." : "."));
            }
            if (_leftOutNew.Count > 0)
            {
                LogNamed("Settings sync: " + Count(_leftOutNew.Count) + " values in the host's settings files are not put into the server's MCM and keep the value MCM read at boot:",
                    _leftOutNew);
                _leftOutNew.Clear();
            }
            if (read != null)
            {
                // Merged by settings id: a file that could not be read this time keeps its last good read. A file that is
                // simply gone (the host deleted it) is not carried over, so its old values never go in again.
                if (_files != null)
                    foreach (SettingsSnapshotData earlier in _files)
                        if (read.Count < SettingsWire.MaxObjectsPerSession && unreadIds.Contains(earlier.SettingsId)
                            && !read.Any(r => string.Equals(r.SettingsId, earlier.SettingsId, StringComparison.Ordinal))) read.Add(earlier);
                _files = read;
                if (read.Count == 0 && unread == 0)
                    _log("Settings sync: " + Root + " has no Global settings files with values, so the server keeps the mod settings MCM read at boot.");
            }
            if (unread == 0)
            {
                _readComplete = true;
                return;
            }
            if (Reads >= MaxReads)
                _log("Settings sync: gave up reading the host's settings files after " + Count(Reads) + " tries; the settings in the files that could not be read keep their last good read, or the values MCM read at boot when there was none.");
        }

        private static string Count(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
