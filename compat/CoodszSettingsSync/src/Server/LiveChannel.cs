// Coodsz Settings Sync, the host side live channel: a folder Coodsz Host and the server module share
// (engine\Modules\DedicatedServer.CoodszTools\live). On each tick the module
//   1. applies the host overrides staged in overrides.json once each settings object exists (and again when a
//      campaign loads), answering in ack-overrides.json,
//   2. applies any apply-*.json edit the host sent while the server runs, answering each in ack-*.json,
//   3. rewrites settings.json whenever the described MCM settings changed.
// Values are set in memory only; Coodsz Host keeps them as host overrides and stages them at every launch.
// The change check then sends the new values to the players.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission. The file names and JSON
// shapes are the same as ModderLords' live settings channel.
//
// Engine free; the launcher's tests run it against stand ins shaped like MCM's types.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoodszSettingsSync
{
    public sealed class LiveChannel
    {
        public const int SchemaVersion = 1;
        public const string SettingsFileName = "settings.json";
        public const string RequestPrefix = "apply-";
        public const string AckPrefix = "ack-";
        public const string OverridesFileName = "overrides.json";
        public const string OverridesAckFileName = "ack-overrides.json";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _dir;
        private readonly Action<string> _log;
        private readonly Func<DateTime> _now;
        private readonly Dictionary<string, Dictionary<string, string>> _overrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly HashSet<string> _overridesApplied = new HashSet<string>(StringComparer.Ordinal);
        // settings id + tab + property id of every value a host override or edit set, which the files never replace
        private readonly HashSet<string> _hostSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Dictionary<string, object?>> _overrideResults = new List<Dictionary<string, object?>>();
        private bool _overridesLoaded;
        private DateTime _overridesStamp = DateTime.MinValue;
        private DateTime _overridesBadStamp = DateTime.MinValue;
        private int _overridesGeneration = -1;
        private bool _changed;
        private string? _lastBody;
        private int _failures;

        public LiveChannel(string dir, Action<string> log, Func<DateTime>? now = null)
        {
            _dir = dir;
            _log = log;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public string Dir => _dir;

        /// <summary>True once after a host edit or override changed a value (the caller then reads MCM's settings again).</summary>
        public bool TakeChanged()
        {
            bool changed = _changed;
            _changed = false;
            return changed;
        }

        /// <summary>True when a host override or edit set this value; the settings files then never replace it.</summary>
        public bool HostSet(string settingsId, string propertyId) => _hostSet.Contains(settingsId + "\t" + propertyId);

        private void RememberHostSet(string settingsId, IEnumerable<string> propertyIds, Dictionary<string, object?> ack)
        {
            // An edit that set nothing (its mod is not on the server) holds no value, so the files may still fill it.
            if (!(ack.TryGetValue("Ok", out object? ok) && ok is bool done && done)) return;
            var skipped = ack.TryGetValue("Skipped", out object? list) && list is List<string> names ? names : new List<string>();
            foreach (string propertyId in propertyIds)
                if (!skipped.Contains(propertyId)) _hostSet.Add(settingsId + "\t" + propertyId);
        }

        /// <summary>One pass; never throws. <paramref name="generation"/> changes when a campaign loads.</summary>
        public void Poll(McmSettings? mcm, KeepOwnList keepOwn, int generation)
        {
            try
            {
                if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
                if (mcm != null)
                {
                    ApplyOverrides(mcm, generation);
                    ProcessRequests(mcm);
                }
                WriteDescriptionIfChanged(mcm, keepOwn);
            }
            catch (Exception e)
            {
                if (_failures++ < 3) _log("Settings sync: the live settings folder failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ---- host overrides, staged at launch ----------------------------------------------------------

        private void ApplyOverrides(McmSettings mcm, int generation)
        {
            // Parsed before it counts as loaded: a broken or half written file is tried again when it changes.
            string path = Path.Combine(_dir, OverridesFileName);
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if ((!_overridesLoaded || stamp != _overridesStamp) && stamp != _overridesBadStamp)
            {
                Dictionary<string, Dictionary<string, string>>? read = stamp == DateTime.MinValue
                    ? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
                    : ReadOverrides(path);
                if (read == null)
                {
                    _overridesBadStamp = stamp;
                    _log("Settings sync: " + OverridesFileName + " could not be read; it is tried again when it changes.");
                    WriteOverridesAck(false, OverridesFileName + " could not be read");
                }
                else
                {
                    _overridesLoaded = true;
                    _overridesStamp = stamp;
                    _overrides.Clear();
                    foreach (KeyValuePair<string, Dictionary<string, string>> pair in read) _overrides[pair.Key] = pair.Value;
                    _overridesApplied.Clear();
                    _overrideResults.Clear();
                    if (_overrides.Count > 0)
                    {
                        _log("Settings sync: " + Count(_overrides.Count) + " settings object(s) have host overrides from Coodsz Host.");
                        WriteOverridesAck();
                    }
                }
            }
            if (_overrides.Count == 0) return;
            if (generation != _overridesGeneration)
            {
                // A campaign that loads may restore a mod's own values, and its PerCampaign objects are new ones.
                _overridesGeneration = generation;
                _overridesApplied.Clear();
                _overrideResults.Clear();
            }
            bool changedAny = false;
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in _overrides)
            {
                if (_overridesApplied.Contains(pair.Key) || mcm.Definition(pair.Key) == null) continue;
                Dictionary<string, object?> ack = Apply(mcm, pair.Key, pair.Value);
                RememberHostSet(pair.Key, pair.Value.Keys, ack);
                if (ack["Changed"] is int set && set > 0) _changed = true;
                _overridesApplied.Add(pair.Key);
                _overrideResults.Add(ack);
                _log("Settings sync: host override " + pair.Key + ": " + ack["Report"]);
                changedAny = true;
            }
            if (changedAny) WriteOverridesAck();
        }

        /// <summary>The overrides in the file, or null when it cannot be read or is not the expected shape.</summary>
        public static Dictionary<string, Dictionary<string, string>>? ReadOverrides(string path)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
                var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                if (!document.RootElement.TryGetProperty("Objects", out JsonElement objects)) return result;
                if (objects.ValueKind != JsonValueKind.Object) return null;
                foreach (JsonProperty settings in objects.EnumerateObject())
                {
                    if (settings.Value.ValueKind != JsonValueKind.Object) continue;
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (JsonProperty value in settings.Value.EnumerateObject())
                        values[value.Name] = value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() ?? "" : value.Value.GetRawText();
                    if (values.Count > 0) result[settings.Name] = values;
                }
                return result;
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>ack-overrides.json: each applied object's answer, and the ones still waiting for their mod.</summary>
        private void WriteOverridesAck(bool ok = true, string? report = null)
        {
            var ack = new Dictionary<string, object?>
            {
                ["Ok"] = ok,
                ["Report"] = report,
                ["Applied"] = _overrideResults.ToList(),
                ["Pending"] = _overrides.Keys.Where(k => !_overridesApplied.Contains(k)).ToList(),
            };
            WriteAtomic(Path.Combine(_dir, OverridesAckFileName), JsonSerializer.Serialize(ack, Json));
        }

        // ---- edits sent while the server runs ----------------------------------------------------------

        private void ProcessRequests(McmSettings mcm)
        {
            string[] files = Directory.GetFiles(_dir, RequestPrefix + "*.json");
            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string token = Path.GetFileNameWithoutExtension(file).Substring(RequestPrefix.Length);
                Dictionary<string, object?> ack;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                    JsonElement root = document.RootElement;
                    string settingsId = root.TryGetProperty("SettingsId", out JsonElement id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "";
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (root.TryGetProperty("Values", out JsonElement map) && map.ValueKind == JsonValueKind.Object)
                        foreach (JsonProperty value in map.EnumerateObject())
                            values[value.Name] = value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() ?? "" : value.Value.GetRawText();
                    ack = Apply(mcm, settingsId, values);
                    RememberHostSet(settingsId, values.Keys, ack);
                    if (ack["Changed"] is int set && set > 0) _changed = true;
                    _log("Settings sync: host edit " + settingsId + ": " + ack["Report"]);
                }
                catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
                {
                    ack = new Dictionary<string, object?> { ["Ok"] = false, ["Changed"] = 0, ["Report"] = e.Message };
                }
                WriteAtomic(Path.Combine(_dir, AckPrefix + token + ".json"), JsonSerializer.Serialize(ack, Json));
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                _lastBody = null;
            }
        }

        /// <summary>Sets host values in memory. Numbers are held to the setting's limits; a value that does not fit is skipped.</summary>
        public static Dictionary<string, object?> Apply(McmSettings mcm, string settingsId, IDictionary<string, string> values)
        {
            object? definition = mcm.Definition(settingsId);
            if (definition == null)
                return new Dictionary<string, object?> { ["Ok"] = false, ["SettingsId"] = settingsId, ["Changed"] = 0, ["Report"] = "not installed on the server" };
            var properties = mcm.PropertiesOf(settingsId).GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            int changed = 0;
            var skipped = new List<string>();
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!properties.TryGetValue(pair.Key, out McmProperty? property) || property.Kind == null || !TryApplyText(property, pair.Value))
                {
                    skipped.Add(pair.Key);
                    continue;
                }
                changed++;
            }
            string report = Count(changed) + " set" + (skipped.Count > 0 ? ", skipped " + string.Join(", ", skipped) : "");
            return new Dictionary<string, object?>
            {
                ["Ok"] = true,
                ["SettingsId"] = settingsId,
                ["Changed"] = changed,
                ["Report"] = report,
                ["Skipped"] = skipped,
                ["Persisted"] = "kept by Coodsz Host as a host override for every launch",
            };
        }

        /// <summary>The live text form: true/false, invariant numbers, text, and a dropdown's choice by name (or index).</summary>
        public static bool TryApplyText(McmProperty property, string text)
        {
            if (property.Kind == null) return false;
            WireKind kind = property.Kind.Value;
            string wire = (text ?? "").Trim();
            int choices = 0;
            if (kind == WireKind.Dropdown)
            {
                List<string> names = property.Choices();
                choices = names.Count;
                int byName = names.FindIndex(n => string.Equals(n, text, StringComparison.Ordinal));
                if (byName >= 0) wire = SettingsWire.FormatInt(byName);
            }
            if (kind == WireKind.String) wire = text ?? "";
            if (!SettingsWire.TryDecide(new WireValue(kind, property.Id, wire), kind, property.Min, property.Max, choices, false, out object? value, out _)
                || value == null) return false;
            return WriteDecided(property, value, wire);
        }

        /// <summary>
        /// Sets a value SettingsWire.TryDecide decided, as the setting's own type (a double setting gets the exact double
        /// of <paramref name="wire"/>). Written unless it is exactly the value already there, so even a tiny edit goes in
        /// and success always means the value is set.
        /// </summary>
        public static bool WriteDecided(McmProperty property, object value, string wire)
        {
            try
            {
                value = SyncValues.AsTypeOf(property.ValueType, value, wire);
                if (!SameExactly(property.Read(), value)) property.Write(value);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Equal values, with numbers compared as numbers whatever their boxed type (an int decided for a long setting),
        /// and exactly: whole numbers as decimals, the rest as doubles. Other values compare as they are.
        /// </summary>
        public static bool SameExactly(object? current, object? value)
        {
            if (Equals(current, value)) return true;
            if (!IsNumber(current) || !IsNumber(value)) return false;
            if (IsWhole(current) && IsWhole(value))
                return Convert.ToDecimal(current, CultureInfo.InvariantCulture) == Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return Convert.ToDouble(current, CultureInfo.InvariantCulture) == Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static bool IsWhole(object? value) =>
            value is int || value is long || value is short || value is byte || value is sbyte || value is uint || value is ulong || value is ushort;

        private static bool IsNumber(object? value) => IsWhole(value) || value is float || value is double || value is decimal;

        /// <summary>The value as the live channel shows it.</summary>
        public static string? DescribeValue(McmProperty property)
        {
            object? value;
            try { value = property.Read(); }
            catch (Exception) { return null; }
            switch (property.Kind)
            {
                case WireKind.Bool: return value is bool b ? (b ? "true" : "false") : null;
                case WireKind.Dropdown:
                    List<string> names = property.Choices();
                    return value is int index && index >= 0 && index < names.Count ? names[index] : null;
                case null: return null;
                default: return SettingsWire.FormatValue(property.Kind.Value, value);
            }
        }

        // ---- the description Coodsz Host shows ----------------------------------------------------------

        public static List<Dictionary<string, object?>> Describe(McmSettings mcm, KeepOwnList keepOwn)
        {
            var objects = new List<Dictionary<string, object?>>();
            foreach (KeyValuePair<string, object> pair in mcm.Definitions())
            {
                string id = pair.Key;
                SettingsScope scope = mcm.ScopeOf(id);
                if (scope != SettingsScope.Global && scope != SettingsScope.PerCampaign) continue;
                if (SettingsWire.IsAlwaysSkipped(id, McmSettings.Text(pair.Value, "DisplayName"))) continue;
                object? settings = mcm.GetSettings(id);
                string? folder = settings == null ? null : McmSettings.Text(settings, "FolderName");
                string? module = mcm.ModuleIdOf(id);
                var groups = new List<Dictionary<string, object?>>();
                var byGroup = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
                foreach (McmProperty property in mcm.PropertiesOf(id))
                {
                    if (property.Kind == null || property.IsAlwaysSkipped) continue;
                    string group = string.IsNullOrWhiteSpace(property.GroupName) ? "General" : property.GroupName;
                    if (!byGroup.TryGetValue(group, out List<Dictionary<string, object?>>? list))
                    {
                        byGroup[group] = list = new List<Dictionary<string, object?>>();
                        groups.Add(new Dictionary<string, object?> { ["Name"] = group, ["Properties"] = list });
                    }
                    list.Add(DescribeProperty(property));
                }
                if (groups.Count == 0) continue;
                objects.Add(new Dictionary<string, object?>
                {
                    ["SettingsId"] = id,
                    ["DisplayName"] = McmSettings.Text(pair.Value, "DisplayName") is string name && name.Length > 0 ? name : id,
                    ["Folder"] = string.IsNullOrEmpty(folder) ? null : folder,
                    ["ModuleId"] = module,
                    ["Scope"] = scope.ToString(),
                    ["PlayersOwn"] = keepOwn.Keeps(id, module, folder),
                    ["Groups"] = groups,
                });
            }
            return objects;
        }

        private static Dictionary<string, object?> DescribeProperty(McmProperty property)
        {
            string kind = property.Kind switch
            {
                WireKind.Bool => "bool",
                WireKind.Int => "int",
                WireKind.Float => "float",
                WireKind.Dropdown => "enum",
                _ => "string",
            };
            var entry = new Dictionary<string, object?>
            {
                ["Id"] = property.Id,
                ["DisplayName"] = string.IsNullOrWhiteSpace(property.DisplayName) ? property.Id : property.DisplayName,
                ["Hint"] = string.IsNullOrWhiteSpace(property.Hint) ? null : property.Hint,
                ["Kind"] = kind,
                ["Value"] = DescribeValue(property),
                ["Editable"] = true,
            };
            if ((property.Kind == WireKind.Int || property.Kind == WireKind.Float) && SettingsWire.HasRange(property.Min, property.Max))
            {
                entry["Min"] = (double)property.Min;
                entry["Max"] = (double)property.Max;
            }
            if (property.Kind == WireKind.Dropdown) entry["Choices"] = property.Choices();
            if (property.RequireRestart) entry["RequireRestart"] = true;
            return entry;
        }

        private void WriteDescriptionIfChanged(McmSettings? mcm, KeepOwnList keepOwn)
        {
            List<Dictionary<string, object?>> objects = mcm == null ? new List<Dictionary<string, object?>>() : Describe(mcm, keepOwn);
            string body = JsonSerializer.Serialize(objects, Json) + (mcm == null ? "nomcm" : "");
            if (body == _lastBody) return;
            _lastBody = body;
            var document = new Dictionary<string, object?>
            {
                ["SchemaVersion"] = SchemaVersion,
                ["WrittenAt"] = _now().ToString("o", CultureInfo.InvariantCulture),
                ["McmRunning"] = mcm != null,
                ["Objects"] = objects,
            };
            WriteAtomic(Path.Combine(_dir, SettingsFileName), JsonSerializer.Serialize(document, Json));
        }

        private static void WriteAtomic(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
