// Coodsz Settings Sync: MCM v5 settings read and written by reflection. MCM is optional, so nothing here
// binds to its types when compiled. Values go through MCM's own property references, so its change
// notifications fire, and only in memory: nothing here ever saves a settings file.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
//
// Engine free; the launcher's tests run it against small stand ins shaped like MCM's types.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CoodszSettingsSync
{
    /// <summary>One MCM setting: its definition and the live property reference behind it.</summary>
    public sealed class McmProperty
    {
        private readonly object _reference;
        private readonly PropertyInfo? _value;

        public McmProperty(string id, string groupName, string displayName, WireKind? kind, object reference, decimal min, decimal max)
        {
            Id = id;
            GroupName = groupName;
            DisplayName = displayName;
            Kind = kind;
            Min = min;
            Max = max;
            _reference = reference;
            _value = reference.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            ValueType = reference.GetType().GetProperty("Type", BindingFlags.Public | BindingFlags.Instance)?.GetValue(reference, null) as Type
                ?? _value?.PropertyType;
        }

        public string Id { get; }
        public string GroupName { get; }
        public string DisplayName { get; }

        /// <summary>The wire kind, or null for a setting that never travels (buttons, other types).</summary>
        public WireKind? Kind { get; }

        public decimal Min { get; }
        public decimal Max { get; }
        public Type? ValueType { get; }
        public string Hint { get; set; } = "";
        public bool RequireRestart { get; set; }

        /// <summary>The dropdown's choices as text, in order; empty for other kinds.</summary>
        public List<string> Choices()
        {
            var choices = new List<string>();
            if (Kind != WireKind.Dropdown || !(RawValue is IEnumerable items)) return choices;
            foreach (object? item in items) choices.Add(item?.ToString() ?? "");
            return choices;
        }

        /// <summary>Hotkeys, interface, sound and camera settings, which each player keeps.</summary>
        public bool IsAlwaysSkipped =>
            SettingsWire.IsAlwaysSkipped(Id, GroupName, DisplayName)
            || SettingsWire.IsInputKeyType(ValueType?.FullName)
            || (ValueType != null && ValueType.IsGenericType && ValueType.GetGenericArguments().Any(t => SettingsWire.IsInputKeyType(t.FullName)));

        /// <summary>A text setting that may hold a token, password, web address or file path; see SettingsWire.IsPrivateText.</summary>
        public bool IsPrivateText(string? value) => Kind == WireKind.String && SettingsWire.IsPrivateText(value, Id, DisplayName);

        public object? RawValue => _value?.GetValue(_reference, null);

        /// <summary>The value as the wire carries it: the selected index for a dropdown.</summary>
        public object? Read()
        {
            object? raw = RawValue;
            if (Kind != WireKind.Dropdown) return raw;
            return raw == null ? null : SelectedIndexProperty(raw)?.GetValue(raw, null);
        }

        public int DropdownCount => RawValue is ICollection list ? list.Count : 0;

        /// <summary>Sets the value in memory. A dropdown moves its selected index; the list itself stays.</summary>
        public void Write(object value)
        {
            if (Kind == WireKind.Dropdown)
            {
                object? raw = RawValue;
                PropertyInfo? index = raw == null ? null : SelectedIndexProperty(raw);
                if (raw == null || index == null || !index.CanWrite) throw new InvalidOperationException("dropdown has no SelectedIndex");
                index.SetValue(raw, value, null);
                return;
            }
            if (_value == null || !_value.CanWrite) throw new InvalidOperationException("read only");
            _value.SetValue(_reference, value, null);
        }

        private static PropertyInfo? SelectedIndexProperty(object dropdown) =>
            dropdown.GetType().GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>MCM's settings provider (BaseSettingsProvider.Instance) seen through reflection.</summary>
    public sealed class McmSettings
    {
        private readonly object _provider;
        private readonly MethodInfo? _getSettings;

        public McmSettings(object provider)
        {
            _provider = provider;
            _getSettings = provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSettings" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        }

        /// <summary>MCM's live provider, or null when MCM is not loaded or has not started.</summary>
        public static McmSettings? FindLive()
        {
            Type? type = CoopProbe.FindLoaded("MCMv5", "MCM.Abstractions.BaseSettingsProvider");
            object? provider = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            return provider == null ? null : new McmSettings(provider);
        }

        public object Provider => _provider;

        /// <summary>True when the provider has GetSettings(string); MCM's always does.</summary>
        public bool CanGetSettings => _getSettings != null;

        // MCM builds every definition, with all its groups, each time SettingsDefinitions is read, so the list
        // and each definition's properties are kept until Refresh. The property references point at the live
        // settings objects, so values read through them are always current; a refresh is only needed when
        // settings objects come or go (a campaign loads) or one is not found.
        private List<KeyValuePair<string, object>>? _definitions;
        private Dictionary<string, object>? _byId;
        private readonly Dictionary<string, CachedProperties> _properties = new Dictionary<string, CachedProperties>(StringComparer.Ordinal);

        private sealed class CachedProperties
        {
            public CachedProperties(object? instance, List<McmProperty> properties)
            {
                Instance = instance == null ? null : new WeakReference(instance);
                Properties = properties;
            }

            public WeakReference? Instance { get; }
            public List<McmProperty> Properties { get; }
        }

        /// <summary>Drops the cached definitions; the next read asks MCM again.</summary>
        public void Refresh()
        {
            _definitions = null;
            _byId = null;
            _properties.Clear();
        }

        /// <summary>Each settings object's id and definition, in MCM's order.</summary>
        public List<KeyValuePair<string, object>> Definitions()
        {
            if (_definitions != null) return _definitions;
            var result = new List<KeyValuePair<string, object>>();
            var byId = new Dictionary<string, object>(StringComparer.Ordinal);
            if (_provider.GetType().GetProperty("SettingsDefinitions")?.GetValue(_provider, null) is IEnumerable all)
            {
                foreach (object? definition in all)
                {
                    if (definition == null) continue;
                    string? id = Text(definition, "SettingsId");
                    if (string.IsNullOrEmpty(id) || byId.ContainsKey(id!)) continue;
                    byId[id!] = definition;
                    result.Add(new KeyValuePair<string, object>(id!, definition));
                }
            }
            _byId = byId;
            return _definitions = result;
        }

        public object? Definition(string settingsId)
        {
            Definitions();
            return _byId != null && _byId.TryGetValue(settingsId, out object? definition) ? definition : null;
        }

        /// <summary>
        /// The settings of one object, bound to the settings object MCM holds right now. They are read from that
        /// object itself (MCM's GetSettingPropertyGroups), not from the SettingsDefinitions list: on a Coop
        /// client, CoopModPatch filters that list and drops the mods the server controls, so a lookup through it
        /// finds nothing for exactly the mods the host shares. The cache is kept only while MCM still returns the
        /// same object; when MCM hands out a new one (a reload, a campaign start), the properties are read again,
        /// so no value is ever written into an object MCM no longer uses.
        /// </summary>
        public List<McmProperty> PropertiesOf(string settingsId)
        {
            object? live = GetSettings(settingsId);
            if (_properties.TryGetValue(settingsId, out CachedProperties? cached))
            {
                object? held = cached.Instance?.Target;
                if (live == null ? cached.Instance == null : ReferenceEquals(held, live)) return cached.Properties;
                _properties.Remove(settingsId);
            }

            List<McmProperty>? properties = null;
            if (live != null && GroupsOf(live) is IEnumerable groups) properties = PropertiesOfGroups(groups);
            if (properties == null || properties.Count == 0)
            {
                object? definition = Definition(settingsId);
                if (definition == null)
                {
                    Refresh();
                    definition = Definition(settingsId);
                }
                if (definition != null) properties = Properties(definition);
            }
            if (properties == null) return new List<McmProperty>();
            _properties[settingsId] = new CachedProperties(live, properties);
            return properties;
        }

        private static MethodInfo? _groupsExtension;
        private static bool _groupsExtensionLooked;

        /// <summary>
        /// A settings object's groups as MCM builds them for its menu: MCM's own BaseSettingsExtensions
        /// .GetSettingPropertyGroups, or a GetSettingPropertyGroups method on the object. Null when neither exists.
        /// </summary>
        private static IEnumerable? GroupsOf(object settings)
        {
            try
            {
                if (!_groupsExtensionLooked)
                {
                    _groupsExtensionLooked = true;
                    _groupsExtension = CoopProbe.FindLoaded("MCMv5", "MCM.Abstractions.BaseSettingsExtensions")?
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetSettingPropertyGroups" && m.GetParameters().Length == 1);
                }
                if (_groupsExtension != null && _groupsExtension.GetParameters()[0].ParameterType.IsInstanceOfType(settings))
                    return _groupsExtension.Invoke(null, new[] { settings }) as IEnumerable;
                MethodInfo? own = settings.GetType().GetMethod("GetSettingPropertyGroups", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                return own?.Invoke(settings, null) as IEnumerable;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True when MCM lists this id but has no settings object for it yet.</summary>
        public bool ListedButNotCreated(string settingsId) => GetSettings(settingsId) == null && Definition(settingsId) != null;

        public object? GetSettings(string settingsId)
        {
            try { return _getSettings?.Invoke(_provider, new object[] { settingsId }); }
            catch (Exception) { return null; }
        }

        public SettingsScope ScopeOf(string settingsId) => ScopeOfType(GetSettings(settingsId)?.GetType());

        /// <summary>
        /// The module a settings object belongs to: the folder of the assembly its class is in
        /// (...\Modules\&lt;Id&gt;\bin\...). For a mod built on another MCM version MCM wraps the mod's own
        /// object, so the wrapped object's assembly is used. Null when it cannot be told.
        /// </summary>
        public string? ModuleIdOf(string settingsId)
        {
            object? settings = GetSettings(settingsId);
            if (settings == null) return null;
            object? wrapped = null;
            try { wrapped = settings.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance)?.GetValue(settings, null); }
            catch (Exception) { }
            return ModuleIdOfAssembly((wrapped ?? settings).GetType().Assembly);
        }

        public static string? ModuleIdOfAssembly(Assembly assembly)
        {
            string location;
            try { location = assembly.Location; }
            catch (Exception) { return null; }
            return ModuleIdOfPath(location);
        }

        public static string? ModuleIdOfPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string[] parts = path!.Split('\\', '/');
            for (int i = 0; i < parts.Length - 2; i++)
                if (parts[i].Equals("Modules", StringComparison.OrdinalIgnoreCase) && parts[i + 2].Equals("bin", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            return null;
        }

        /// <summary>Global, PerCampaign or PerSave from the settings class's MCM base class.</summary>
        public static SettingsScope ScopeOfType(Type? type)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                if (t.Namespace == null || !t.Namespace.StartsWith("MCM.Abstractions", StringComparison.Ordinal)) continue;
                switch (t.Name)
                {
                    case "GlobalSettings": return SettingsScope.Global;
                    case "PerCampaignSettings": return SettingsScope.PerCampaign;
                    case "PerSaveSettings": return SettingsScope.PerSave;
                }
            }
            return SettingsScope.Unknown;
        }

        /// <summary>Every setting of one definition, sub groups included.</summary>
        public static List<McmProperty> Properties(object definition) =>
            definition.GetType().GetProperty("SettingPropertyGroups")?.GetValue(definition, null) is IEnumerable groups
                ? PropertiesOfGroups(groups)
                : new List<McmProperty>();

        private static List<McmProperty> PropertiesOfGroups(IEnumerable groups)
        {
            var result = new List<McmProperty>();
            foreach (object? group in groups)
                if (group != null) AddGroup(group, result, 0);
            return result;
        }

        private static void AddGroup(object group, List<McmProperty> into, int depth)
        {
            if (depth > 16) return;
            string groupName = Text(group, "GroupName") ?? Text(group, "GroupNameRaw") ?? "";
            if (group.GetType().GetProperty("SettingProperties")?.GetValue(group, null) is IEnumerable properties)
            {
                foreach (object? property in properties)
                {
                    if (property == null) continue;
                    string? id = Text(property, "Id");
                    object? reference = property.GetType().GetProperty("PropertyReference")?.GetValue(property, null);
                    if (string.IsNullOrEmpty(id) || reference == null) continue;
                    string? settingType = property.GetType().GetProperty("SettingType")?.GetValue(property, null)?.ToString();
                    into.Add(new McmProperty(id!, groupName, Text(property, "DisplayName") ?? "", SettingsWire.KindFromSettingType(settingType),
                        reference, Number(property, "MinValue"), Number(property, "MaxValue"))
                    {
                        Hint = Text(property, "HintText") ?? "",
                        RequireRestart = string.Equals(Text(property, "RequireRestart"), "True", StringComparison.OrdinalIgnoreCase),
                    });
                }
            }
            if (group.GetType().GetProperty("SubGroups")?.GetValue(group, null) is IEnumerable subGroups)
                foreach (object? sub in subGroups)
                    if (sub != null) AddGroup(sub, into, depth + 1);
        }

        internal static string? Text(object target, string property)
        {
            try { return target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target, null)?.ToString(); }
            catch (Exception) { return null; }
        }

        private static decimal Number(object target, string property)
        {
            try
            {
                object? value = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target, null);
                return value == null ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return 0m; }
        }
    }

    /// <summary>One settings object as the server sends it.</summary>
    public sealed class SettingsSnapshotData
    {
        public SettingsSnapshotData(string settingsId, SettingsScope scope, bool fromFile, List<string> values)
        {
            SettingsId = settingsId;
            Scope = scope;
            FromFile = fromFile;
            Values = values;
        }

        public string SettingsId { get; }
        public SettingsScope Scope { get; }
        public bool FromFile { get; }
        public List<string> Values { get; }

        /// <summary>Same object, same values: nothing to send again.</summary>
        public bool SameAs(SettingsSnapshotData? other) =>
            other != null && other.SettingsId == SettingsId && other.Scope == Scope && other.FromFile == FromFile && other.Values.SequenceEqual(Values);
    }

    /// <summary>Server side: the host's live MCM values, minus what never travels.</summary>
    public static class SettingsCapture
    {
        public static List<SettingsSnapshotData> Capture(McmSettings mcm, KeepOwnList keepOwn)
        {
            var result = new List<SettingsSnapshotData>();
            foreach (KeyValuePair<string, object> pair in mcm.Definitions())
            {
                if (result.Count >= SettingsWire.MaxObjectsPerSession) break;
                string id = pair.Key;
                if (!SettingsWire.IsValidId(id)) continue;
                if (keepOwn.Count > 0)
                {
                    object? settings = mcm.GetSettings(id);
                    if (keepOwn.Keeps(id, mcm.ModuleIdOf(id), settings == null ? null : McmSettings.Text(settings, "FolderName"))) continue;
                }
                SettingsScope scope = mcm.ScopeOf(id);
                // PerSave values ride in the save itself; unknown kinds of settings stay on each PC.
                if (scope != SettingsScope.Global && scope != SettingsScope.PerCampaign) continue;
                if (SettingsWire.IsAlwaysSkipped(id, MaybeText(pair.Value, "DisplayName"))) continue;

                var values = new List<string>();
                foreach (McmProperty property in mcm.PropertiesOf(id))
                {
                    if (values.Count >= SettingsWire.MaxValuesPerObject) break;
                    if (property.Kind == null || property.IsAlwaysSkipped) continue;
                    string? text;
                    try { text = SettingsWire.FormatValue(property.Kind.Value, property.Read()); }
                    catch (Exception) { continue; }
                    if (text == null) continue;
                    // A token, password, web address or file path stays on the host's PC.
                    if (property.Kind == WireKind.String && property.IsPrivateText(text)) continue;
                    string? line = SettingsWire.Encode(new WireValue(property.Kind.Value, property.Id, text));
                    if (line != null) values.Add(line);
                }
                if (values.Count > 0) result.Add(new SettingsSnapshotData(id, scope, false, values));
            }
            return result;
        }

        private static string? MaybeText(object target, string property) => McmSettings.Text(target, property);
    }

    /// <summary>What one apply pass did to one settings object.</summary>
    public sealed class ApplyReport
    {
        public int Set { get; set; }
        public int Skipped { get; set; }
        public Dictionary<string, int> Reasons { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>True when the whole settings object is not in this game's MCM (yet); nothing was looked at.</summary>
        public bool ObjectMissing { get; set; }

        /// <summary>With <see cref="ObjectMissing"/>: MCM lists the id here but has no settings object for it yet.</summary>
        public bool NotCreatedYet { get; set; }

        public void Skip(string reason)
        {
            Skipped++;
            Reasons.TryGetValue(reason, out int count);
            Reasons[reason] = count + 1;
        }

        public string Describe() => ObjectMissing
            ? (NotCreatedYet ? "MCM lists it on this PC but has not made its settings object yet, skipped" : "it is not in this PC's mod settings, skipped")
            : Set.ToString(CultureInfo.InvariantCulture) + " set, " + Skipped.ToString(CultureInfo.InvariantCulture) + " skipped"
            + (Reasons.Count == 0 ? "" : " (" + string.Join(", ", Reasons.OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => r.Value.ToString(CultureInfo.InvariantCulture) + " " + ReasonText(r.Key))) + ")");

        private static string ReasonText(string reason)
        {
            switch (reason)
            {
                case "missing": return "not on this PC";
                case "kind": return "different type here";
                case "range": return "choice out of range";
                case "value": return "unreadable value";
                case "own": return "player's own";
                case "error": return "could not set";
                default: return reason;
            }
        }
    }

    /// <summary>
    /// The client's log lines that must never be skipped: one per snapshot that arrives, whatever happens to
    /// it, and one when the session ends, so the log always shows whether the host's settings came and went.
    /// </summary>
    public static class SyncNotes
    {
        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Received(string? settingsId, int scope, int values) =>
            "Settings sync: received " + (SettingsWire.IsValidId(settingsId) ? settingsId : "a settings object with an invalid id")
            + " from the host (" + ScopeText(scope) + ", " + N(values) + " value" + (values == 1 ? "" : "s") + ").";

        /// <summary>Why a received snapshot is not used, or null when it can be used.</summary>
        public static string? Rejected(bool active, int protocolVersion, string? settingsId, int scope)
        {
            if (!active) return "settings sync is off in this game, so it was not used.";
            if (!SettingsWire.IsKnownProtocol(protocolVersion))
                return "it uses settings sync version " + N(protocolVersion) + " and this add on version " + N(SettingsWire.ProtocolVersion) + ", so it was not used.";
            if (!SettingsWire.IsValidId(settingsId)) return "its id is not a valid MCM settings id, so it was not used.";
            if (scope != (int)SettingsScope.Global && scope != (int)SettingsScope.PerCampaign)
                return "only Global and PerCampaign settings are shared, so it was not used.";
            return null;
        }

        public static string ScopeText(int scope)
        {
            switch ((SettingsScope)scope)
            {
                case SettingsScope.Global: return "Global";
                case SettingsScope.PerCampaign: return "PerCampaign";
                case SettingsScope.PerSave: return "PerSave";
                default: return "unknown scope " + N(scope);
            }
        }

        public static string Applied(string settingsId, string when, ApplyReport report) =>
            "Settings sync: " + settingsId + " " + when + ": " + report.Describe()
            + (report.ObjectMissing ? "; it is tried again when it appears." : ".");

        public static string SessionEnded(int synced, int restored, int stillHeld) =>
            synced == 0
                ? "Settings sync: session ended; nothing to put back, no host value was in use. Your settings files were never changed."
                : "Settings sync: session ended; put back " + N(restored) + " of your own values. Your settings files were never changed."
                  + (stillHeld > 0 ? " Some could not be put back; they stay guarded until the game closes." : "");

        public static string SavedOwn(string settingsId) =>
            "Settings sync: a save of " + settingsId + " wrote your own values; the host's values stay out of your settings files while you are on this server.";

        public static string Held(string settingsId) =>
            "Settings sync: " + settingsId + " keeps the host's values while you are on this server. Your change is kept for after you leave.";

        public static string GaveUp(string settingsId, int minutes) =>
            "Settings sync: " + settingsId + " is still not on this PC after " + N(minutes)
            + " minutes, so it is no longer tried this session.";

        public static string NoAnswer(int seconds) =>
            "Settings sync: asked the server for the host's mod settings " + N(seconds)
            + " seconds ago and nothing came yet; your own settings stay until they arrive.";

        public const string NoSettings = "Settings sync: the host has no mod settings to share, so your own settings stay.";
    }

    /// <summary>
    /// Client side: received settings objects this game does not have yet (a mod that makes its settings
    /// object late, or only once a campaign runs). An id MCM lists but has not made yet is tried every
    /// <see cref="RetrySeconds"/>; an id MCM does not list at all (a mod this PC does not have) backs off to
    /// 5, 15 and 30 seconds, then once a minute. After <see cref="GiveUpSeconds"/> an id is no longer tried,
    /// until <see cref="Rearm"/> (a campaign loaded) gives every id a fresh start.
    /// </summary>
    public sealed class MissingObjects
    {
        public const float RetrySeconds = 5f;
        public const float GiveUpSeconds = 600f;
        private static readonly float[] BackOff = { 5f, 15f, 30f, 60f };

        private sealed class Entry
        {
            public bool Listed;
            public int Tries;
            public float Waited;
            public float Age;
        }

        private readonly Dictionary<string, Entry> _ids = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> _gaveUp = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _expired = new List<string>();

        public int Count => _ids.Count;
        public bool Contains(string id) => _ids.ContainsKey(id);

        /// <summary>True when the last miss of this id was one MCM lists but has not made yet.</summary>
        public bool IsListed(string id) => _ids.TryGetValue(id, out Entry? entry) && entry.Listed;

        /// <summary>
        /// Notes a miss, and whether MCM lists the id (<paramref name="listed"/>); true the first time, so the
        /// miss is logged once and not on every retry. An id that was given up stays given up until Rearm.
        /// </summary>
        public bool Add(string id, bool listed = false)
        {
            if (_ids.TryGetValue(id, out Entry? entry))
            {
                if (listed && !entry.Listed) entry.Waited = 0f;
                entry.Listed = listed;
                return false;
            }
            if (_gaveUp.Contains(id)) return false;
            _ids[id] = new Entry { Listed = listed };
            return true;
        }

        public void Remove(string id) => _ids.Remove(id);

        public void Clear()
        {
            _ids.Clear();
            _gaveUp.Clear();
            _expired.Clear();
        }

        /// <summary>A campaign loaded: every id gets a fresh start, also the ones given up.</summary>
        public void Rearm()
        {
            _gaveUp.Clear();
            foreach (Entry entry in _ids.Values)
            {
                entry.Tries = 0;
                entry.Waited = 0f;
                entry.Age = 0f;
            }
        }

        /// <summary>Seconds between tries for this id now.</summary>
        public float IntervalOf(string id)
        {
            if (!_ids.TryGetValue(id, out Entry? entry)) return 0f;
            return entry.Listed ? RetrySeconds : BackOff[Math.Min(entry.Tries, BackOff.Length - 1)];
        }

        /// <summary>The ids to try again now; ids past <see cref="GiveUpSeconds"/> move to <see cref="TakeExpired"/>.</summary>
        public List<string> Due(float dt)
        {
            var due = new List<string>();
            if (_ids.Count == 0) return due;
            foreach (KeyValuePair<string, Entry> pair in _ids.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
            {
                Entry entry = pair.Value;
                entry.Age += dt;
                if (entry.Age >= GiveUpSeconds)
                {
                    _ids.Remove(pair.Key);
                    _gaveUp.Add(pair.Key);
                    _expired.Add(pair.Key);
                    continue;
                }
                entry.Waited += dt;
                if (entry.Waited < IntervalOf(pair.Key)) continue;
                entry.Waited = 0f;
                entry.Tries++;
                due.Add(pair.Key);
            }
            return due;
        }

        /// <summary>The ids given up since the last call, to log each one once.</summary>
        public List<string> TakeExpired()
        {
            var expired = _expired.ToList();
            _expired.Clear();
            return expired;
        }
    }

    /// <summary>
    /// Which Coop handler owns the client's session: the handler whose start ran last on the game thread (the
    /// handler itself is the owner, like WotrClientSync). Only that handler may end the session, so a late end
    /// from an older handler leaves the new one alone.
    /// </summary>
    public sealed class SessionOwner
    {
        private object? _current;

        /// <summary>This handler's session began (game thread, in queue order).</summary>
        public void Take(object owner) => _current = owner;

        /// <summary>True when this handler still owns the session (game thread).</summary>
        public bool Owns(object owner) => ReferenceEquals(owner, _current);
    }

    /// <summary>How the client compares and stores setting values whose boxed types can differ.</summary>
    public static class SyncValues
    {
        /// <summary>
        /// True when two values are the same setting value. Numbers compare by value whatever their boxed type
        /// (a double setting against the wire's float), with a tiny tolerance for the float rounding.
        /// </summary>
        public static bool Same(object? a, object? b)
        {
            if (Equals(a, b)) return true;
            if (!IsNumber(a) || !IsNumber(b)) return false;
            // Whole numbers compare exactly; the tolerance is only for a float or double on either side.
            if (!(a is float || a is double || b is float || b is double))
                return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
            double x = Convert.ToDouble(a, CultureInfo.InvariantCulture);
            double y = Convert.ToDouble(b, CultureInfo.InvariantCulture);
            return Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        /// <summary>
        /// The decided value as the setting's own type: a double setting gets a double, read from the wire text
        /// when that agrees with the decided float (so 0.1 stays 0.1), else from the float. A long setting gets the
        /// decided int as a long. Other values stay.
        /// </summary>
        public static object AsTypeOf(Type? settingType, object value, string? wireText)
        {
            if (settingType == typeof(long) && value is int whole) return (long)whole;
            if (settingType == typeof(double) && value is float f)
            {
                if (double.TryParse(wireText, NumberStyles.Float, CultureInfo.InvariantCulture, out double exact) && (float)exact == f)
                    return exact;
                return double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
            if (settingType == typeof(float) && value is double d) return (float)d;
            return value;
        }

        private static bool IsNumber(object? value) =>
            value is float || value is double || value is int || value is long || value is decimal;
    }

    /// <summary>
    /// Client side: what this session changed. For every setting that holds a host value it keeps the
    /// player's own value and the host's, so that
    ///  - a save writes the player's values: the guard puts them back just for the save (keeping any of the
    ///    player's own edits) and puts the host's values back after it;
    ///  - the end of the session puts the player's own values back in memory.
    /// A mod that writes its own config file outside MCM's save cannot be covered by this.
    /// </summary>
    public sealed class SyncSession
    {
        internal sealed class Entry
        {
            public Entry(McmProperty property, object? player)
            {
                Property = property;
                Player = player;
            }

            public int Generation { get; set; }
            // The session that last gave it a host value; only those are held, older ones are only put back.
            public int Session { get; set; }
            public McmProperty Property { get; set; }
            public object? Player { get; set; }
            public object? Host { get; set; }
            // True once the player's own value was read, also when that value is null, so it is still put back.
            public bool HasPlayer { get; set; }
            public bool HasHost { get; set; }
        }

        /// <summary>The settings one save swapped, to put the host's values back afterwards.</summary>
        public sealed class SaveSwap
        {
            internal List<Entry> Swapped { get; } = new List<Entry>();
            internal bool Outer { get; set; }
            /// <summary>Settings this save swapped: host values kept out of the player's file.</summary>
            public int SwappedCount => Swapped.Count;
            /// <summary>True when the player had changed one of them; the host value goes back in after the save.</summary>
            public bool PlayerChanged { get; internal set; }
        }

        // settings id to (campaign generation + property id) to entry
        private readonly Dictionary<string, Dictionary<string, Entry>> _entries = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, WeakReference> _instances = new Dictionary<string, WeakReference>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private int _saveDepth;
        private int _session;
        // True while EndSave writes the host values back.
        private bool _writingHost;
        // True while BeginSave swaps the player's own values in, or Restore writes them back.
        private bool _writingPlayer;

        /// <summary>
        /// A new Coop session starts. Entries a failed Restore kept from an earlier one stay only to be put back;
        /// they are not held again unless this session's host sends that setting.
        /// </summary>
        public void BeginSession() { lock (_gate) _session++; }

        public bool IsSynced(string? settingsId)
        {
            if (string.IsNullOrEmpty(settingsId)) return false;
            lock (_gate) return _entries.TryGetValue(settingsId!, out var entries) && entries.Count > 0;
        }

        public int SyncedCount { get { lock (_gate) return _entries.Count(e => e.Value.Count > 0); } }

        /// <summary>The ids that hold a host value now.</summary>
        public List<string> SyncedIds() { lock (_gate) return _entries.Where(e => e.Value.Count > 0).Select(e => e.Key).ToList(); }

        /// <summary>The live settings object a synced id belongs to, so a save of some other copy can be told apart.</summary>
        internal void Track(string settingsId, object? instance, IDictionary<string, McmProperty>? properties = null, int generation = 0)
        {
            lock (_gate)
            {
                if (instance != null) _instances[settingsId] = new WeakReference(instance);
                if (properties == null || !_entries.TryGetValue(settingsId, out var entries)) return;
                // Every setting held in this campaign follows the object MCM uses now; the player's own value stays as recorded.
                foreach (Entry entry in entries.Values)
                    if ((entry.Generation == generation || entry.Generation == GlobalGeneration) && properties.TryGetValue(entry.Property.Id, out McmProperty? current)) entry.Property = current;
            }
        }

        /// <summary>
        /// The generation a Global setting is held under: one entry per live property for the whole session, so an
        /// apply before the campaign loads and the one after it share it (and the newer one updates its host value).
        /// PerCampaign settings are held per campaign, since each campaign makes new objects.
        /// </summary>
        public const int GlobalGeneration = -1;

        private static string Key(int generation, McmProperty property) => generation.ToString(CultureInfo.InvariantCulture) + "\n" + property.Id;

        /// <summary>
        /// Remembers the player's own value the first time a setting gets a host value in this campaign. False when
        /// the setting can not be read, so it holds no entry and the caller leaves it alone.
        /// </summary>
        internal bool RecordOriginal(string settingsId, McmProperty property, int generation)
        {
            lock (_gate)
            {
                string key = Key(generation, property);
                if (_entries.TryGetValue(settingsId, out var entries) && entries.TryGetValue(key, out Entry? held))
                {
                    // Already held: a change the player made since the host's value went in is their own value now,
                    // so a snapshot applied again does not lose it (the same rule as a save).
                    try
                    {
                        object? now = property.Read();
                        if (now != null && held.HasHost && !SyncValues.Same(now, held.Host)) held.Player = now;
                    }
                    catch (Exception) { }
                    held.Session = _session;
                    return true;
                }
                object? original;
                try { original = property.Read(); }
                catch (Exception) { return false; }
                if (entries == null) _entries[settingsId] = entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
                entries[key] = new Entry(property, original) { Generation = generation, Session = _session, HasPlayer = true };
                return true;
            }
        }

        /// <summary>
        /// Sets the host value a setting holds and returns what it held before, for <see cref="PutHostBack"/>. Set
        /// before the value is written, so a save the write sets off already sees the host value and swaps it.
        /// </summary>
        internal (object? Host, bool HasHost) SetHost(string settingsId, McmProperty property, int generation, object value)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(settingsId, out var entries) || !entries.TryGetValue(Key(generation, property), out Entry? entry)) return (null, false);
                var before = (entry.Host, entry.HasHost);
                entry.Host = value;
                entry.HasHost = true;
                return before;
            }
        }

        /// <summary>The write of a host value failed: the setting holds what it held before.</summary>
        internal void PutHostBack(string settingsId, McmProperty property, int generation, (object? Host, bool HasHost) before)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(settingsId, out var entries) || !entries.TryGetValue(Key(generation, property), out Entry? entry)) return;
                entry.Host = before.Host;
                entry.HasHost = before.HasHost;
            }
        }

        /// <summary>True when this setting already holds a host value in this campaign.</summary>
        internal bool Holds(string settingsId, McmProperty property, int generation)
        {
            lock (_gate) return _entries.TryGetValue(settingsId, out var entries) && entries.ContainsKey(Key(generation, property));
        }

        /// <summary>
        /// Before a save of <paramref name="settings"/>: puts the player's own values into the synced settings,
        /// so the save writes them. A setting the player changed since (in MCM's menu) keeps the player's change,
        /// which becomes their own value. <paramref name="block"/> is true when the save is of a copy whose values
        /// cannot be swapped, or when the save was set off while the sync itself writes host or player values (by
        /// EndSave, Restore or this swap); the caller then skips that save rather than write host values. Nested saves
        /// (the provider calls its containers, which call a format) swap only once, at the outermost one.
        /// </summary>
        public SaveSwap BeginSave(object? settings, out bool block)
        {
            block = false;
            var swap = new SaveSwap();
            lock (_gate)
            {
                // Counted first, so the EndSave that always follows stays balanced on every way out below.
                _saveDepth++;
                string? id = settings == null ? null : McmSettings.Text(settings, "Id");
                // A save set off while the host values go back in after a save, or while Restore puts the player's
                // own values back one by one, could write a host value still held by another setting of the same
                // object into the player's file, so it is skipped. The files already hold the player's values.
                if (_writingHost || _writingPlayer)
                {
                    block = true;
                    return swap;
                }
                if (_saveDepth > 1 || string.IsNullOrEmpty(id) || !_entries.TryGetValue(id!, out var entries) || entries.Count == 0) return swap;
                swap.Outer = true;
                object? live = _instances.TryGetValue(id!, out WeakReference? reference) ? reference.Target : null;
                if (live != null && !ReferenceEquals(live, settings) && !ReferenceEquals(live, Wrapped(settings)))
                {
                    block = true;
                    return swap;
                }
                var resolved = new List<string>();
                // Saves set off by the writes below would catch the object half swapped, so they are skipped.
                _writingPlayer = true;
                try
                {
                    foreach (KeyValuePair<string, Entry> item in entries.ToList())
                    {
                        Entry entry = item.Value;
                        if (!entry.HasHost) continue;
                        try
                        {
                            object? current = entry.Property.Read();
                            // A setting that reads nothing has no value to keep or swap; the entry stays as it is.
                            if (current == null) continue;
                            if (entry.Session != _session)
                            {
                                // Left from an earlier session whose Restore failed: it is only ever put back, never
                                // given the old host's value again. A value the player set since is theirs; else their
                                // own goes back in now. Either way the entry is done.
                                if (!SyncValues.Same(current, entry.Host)) entry.Player = current;
                                else if (entry.HasPlayer && !SyncValues.Same(current, entry.Player)) entry.Property.Write(entry.Player!);
                                resolved.Add(item.Key);
                                continue;
                            }
                            if (!SyncValues.Same(current, entry.Host))
                            {
                                // The player changed it themselves; their change is what gets saved and kept.
                                entry.Player = current;
                                swap.PlayerChanged = true;
                                swap.Swapped.Add(entry);
                                continue;
                            }
                            if (!entry.HasPlayer || SyncValues.Same(entry.Player, entry.Host)) continue;
                            entry.Property.Write(entry.Player!);
                            swap.Swapped.Add(entry);
                        }
                        catch (Exception)
                        {
                            // A value that cannot be put back would be saved as the host's; keep that save out.
                            block = true;
                        }
                    }
                }
                finally { _writingPlayer = false; }
                foreach (string key in resolved) entries.Remove(key);
                if (entries.Count == 0)
                {
                    _entries.Remove(id!);
                    _instances.Remove(id!);
                }
                return swap;
            }
        }

        /// <summary>After the save: the host's values go back into the settings that were swapped.</summary>
        public void EndSave(SaveSwap? swap)
        {
            lock (_gate)
            {
                if (swap == null || !swap.Outer)
                {
                    if (_saveDepth > 0) _saveDepth--;
                    return;
                }
                // The depth stays raised and saves stay off while the host values go back in, so a setting that saves
                // on every write can not start the swap again (that looped until the game crashed).
                _writingHost = true;
                try
                {
                    foreach (Entry entry in swap.Swapped)
                    {
                        try { if (entry.Host != null) entry.Property.Write(entry.Host); }
                        catch (Exception) { }
                    }
                }
                finally
                {
                    _writingHost = false;
                    if (_saveDepth > 0) _saveDepth--;
                }
            }
        }

        /// <summary>
        /// While the player is on the server the host's values win: a synced setting that changed (in MCM's menu,
        /// or from a mod's own code) gets the host's value back. The change becomes the player's own value, so a
        /// save still writes it and the end of the session puts it back. Not during a save, which swaps values
        /// itself. Returns the ids that were put back.
        /// </summary>
        public List<string> HoldHostValues()
        {
            var held = new List<string>();
            lock (_gate)
            {
                if (_saveDepth > 0) return held;
                // Copies, as Restore uses: a save the write sets off runs on this thread and can drop entries.
                foreach (KeyValuePair<string, Dictionary<string, Entry>> pair in _entries.ToList())
                {
                    foreach (Entry entry in pair.Value.Values.ToList())
                    {
                        if (!entry.HasHost || entry.Host == null || entry.Session != _session) continue;
                        try
                        {
                            object? current = entry.Property.Read();
                            // A setting that reads nothing has no value to keep or put back.
                            if (current == null || SyncValues.Same(current, entry.Host)) continue;
                            // The change is the player's own before the host's value goes in, so a save the write sets off keeps it.
                            entry.Player = current;
                            entry.HasPlayer = true;
                            try { entry.Property.Write(entry.Host); }
                            catch (Exception)
                            {
                                // Stored before a change handler threw still counts as put back.
                                if (!SyncValues.Same(entry.Property.Read(), entry.Host)) throw;
                            }
                            if (!held.Contains(pair.Key)) held.Add(pair.Key);
                        }
                        catch (Exception) { }
                    }
                }
            }
            return held;
        }

        /// <summary>
        /// Puts the player's own values back (newest change first). Settings that were put back leave the guard;
        /// any that could not be put back stay guarded, so a later save still writes the player's values.
        /// Returns how many were restored.
        /// </summary>
        public int Restore()
        {
            int restored = 0;
            lock (_gate)
            {
                _writingPlayer = true;
                try { restored = RestoreAll(); }
                finally { _writingPlayer = false; }
            }
            return restored;
        }

        // Restore's work. Saves set off by its writes are skipped (BeginSave blocks them while _writingPlayer is set);
        // the files already hold the player's values.
        private int RestoreAll()
        {
            int restored = 0;
            {
                foreach (KeyValuePair<string, Dictionary<string, Entry>> pair in _entries.ToList())
                {
                    foreach (KeyValuePair<string, Entry> item in pair.Value.ToList())
                    {
                        Entry entry = item.Value;
                        try
                        {
                            object? now = entry.Property.Read();
                            // A change the player made after the last hold (or save) is their own value; it stays.
                            if (now != null && entry.HasHost && !SyncValues.Same(now, entry.Host)) entry.Player = now;
                            else if (entry.HasPlayer && entry.HasHost && !SyncValues.Same(now, entry.Player)) entry.Property.Write(entry.Player!);
                            pair.Value.Remove(item.Key);
                            restored++;
                        }
                        catch (Exception) { }
                    }
                    if (pair.Value.Count == 0)
                    {
                        _entries.Remove(pair.Key);
                        _instances.Remove(pair.Key);
                    }
                }
            }
            return restored;
        }

        private static object? Wrapped(object? settings)
        {
            if (settings == null) return null;
            try { return settings.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance)?.GetValue(settings, null); }
            catch (Exception) { return null; }
        }
    }

    public static class SettingsApplier
    {
        /// <summary>
        /// Applies one snapshot onto the client's own settings object. Only ids the client has are touched,
        /// numbers are held to the client's limits, and a value of another type, an out of range choice or a
        /// private text value is skipped. <paramref name="generation"/> separates campaigns, whose PerCampaign
        /// objects are new ones.
        /// </summary>
        public static ApplyReport Apply(McmSettings mcm, SettingsSnapshotData snapshot, SyncSession session, int generation)
        {
            var report = new ApplyReport();
            // Always the object MCM holds right now, asked by id (never through the filtered definitions list).
            object? live = mcm.GetSettings(snapshot.SettingsId);
            bool found = live != null || (!mcm.CanGetSettings && mcm.Definition(snapshot.SettingsId) != null);
            if (!found)
            {
                // Not made yet (a mod that creates it late), or this PC does not have the mod; retried later.
                report.ObjectMissing = true;
                report.NotCreatedYet = mcm.ListedButNotCreated(snapshot.SettingsId);
                report.Skip("missing");
                return report;
            }

            var properties = new Dictionary<string, McmProperty>(StringComparer.Ordinal);
            foreach (McmProperty property in mcm.PropertiesOf(snapshot.SettingsId))
                if (!properties.ContainsKey(property.Id)) properties[property.Id] = property;
            // When MCM swapped the object, what this session holds moves to the new one, so a save and the
            // end of the session reach the object MCM really uses.
            int held = snapshot.Scope == SettingsScope.Global ? SyncSession.GlobalGeneration : generation;
            session.Track(snapshot.SettingsId, live, properties, held);

            int count = 0;
            foreach (string line in snapshot.Values)
            {
                if (++count > SettingsWire.MaxValuesPerObject) break;
                WireValue? incoming = SettingsWire.Decode(line);
                if (incoming == null) { report.Skip("value"); continue; }
                if (!properties.TryGetValue(incoming.Id, out McmProperty? property)) { report.Skip("missing"); continue; }
                // The player's own copy of a hotkey, interface or private text setting is never touched, whatever arrives.
                if (property.Kind == null || property.IsAlwaysSkipped) { report.Skip(property.Kind == null ? "kind" : "own"); continue; }
                if (property.Kind == WireKind.String && (property.IsPrivateText(incoming.Text) || property.IsPrivateText(property.Read() as string)))
                {
                    report.Skip("own");
                    continue;
                }

                int choices = property.Kind == WireKind.Dropdown ? property.DropdownCount : 0;
                if (!SettingsWire.TryDecide(incoming, property.Kind.Value, property.Min, property.Max, choices, snapshot.FromFile,
                        out object? value, out string reason) || value == null)
                {
                    report.Skip(reason.Length == 0 ? "value" : reason);
                    continue;
                }
                // The wire's float goes in as the setting's own type (a double setting gets a double), so the
                // host value held for the session compares equal to what the setting reads back.
                value = SyncValues.AsTypeOf(property.ValueType, value, incoming.Text);

                try
                {
                    // Every synced setting is held, also one that already has the host's value, so a player who
                    // changes it while joined gets the host's value back. It is only written when it differs.
                    // This runs on the game thread outside the session's lock; the session takes its own lock per call.
                    // The host value is set before the write, so a save the write sets off swaps it out, and it is put
                    // back if the write fails.
                    object? current = property.Read();
                    if (!session.RecordOriginal(snapshot.SettingsId, property, held))
                    {
                        report.Skip("error");
                        continue;
                    }
                    var before = session.SetHost(snapshot.SettingsId, property, held, value);
                    if (!SyncValues.Same(current, value))
                    {
                        try { property.Write(value); }
                        catch (Exception)
                        {
                            // A change handler can throw after the setter stored the value. Then the host value is in
                            // and stays held; only a value that really did not change gets the old host value back.
                            object? after = null;
                            try { after = property.Read(); }
                            catch (Exception) { }
                            if (!SyncValues.Same(after, value))
                            {
                                session.PutHostBack(snapshot.SettingsId, property, held, before);
                                throw;
                            }
                        }
                    }
                    report.Set++;
                }
                catch (Exception)
                {
                    report.Skip("error");
                }
            }
            return report;
        }
    }
}
