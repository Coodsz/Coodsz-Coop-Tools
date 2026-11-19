// Coodsz Settings Sync: checks by name that the Coop types and members the sync uses are there, before the
// CoodszSettingsSync.Coop assembly (which is compiled against Coop) is loaded. A Coop update that moves one
// turns the sync off with one log line instead of a type load error inside the game.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
//
// Engine free; compiled into the server and client submodules and into the launcher's tests.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CoodszSettingsSync
{
    public sealed class CoopProbeResult
    {
        public CoopProbeResult(bool ready, bool notLoadedYet, IReadOnlyList<string> missing)
        {
            Ready = ready;
            NotLoadedYet = notLoadedYet;
            Missing = missing;
        }

        /// <summary>Every type and member is there.</summary>
        public bool Ready { get; }

        /// <summary>Coop's Common assembly is not loaded yet, so asking again later can still succeed.</summary>
        public bool NotLoadedYet { get; }

        public IReadOnlyList<string> Missing { get; }

        public string Describe() => Ready ? "Coop types found" : "missing " + string.Join(", ", Missing);
    }

    public static class CoopProbe
    {
        public const string AssemblyFileName = "CoodszSettingsSync.Coop.dll";

        /// <summary>
        /// Checks the Coop surface. <paramref name="find"/> returns a type by assembly simple name and full
        /// type name, or null. <paramref name="client"/> adds the client only messages.
        /// </summary>
        public static CoopProbeResult Check(bool client, Func<string, string, Type?> find)
        {
            var missing = new List<string>();

            Type? handler = find("Common", "Common.Messaging.IHandler");
            Type? broker = find("Common", "Common.Messaging.IMessageBroker");
            if (handler == null && broker == null)
                return new CoopProbeResult(false, true, new[] { "Coop's Common assembly" });

            if (handler == null) missing.Add("IHandler");
            if (broker == null) missing.Add("IMessageBroker");
            else
            {
                if (!HasMethod(broker, "Subscribe", 1)) missing.Add("IMessageBroker.Subscribe");
                if (!HasMethod(broker, "Unsubscribe", 1)) missing.Add("IMessageBroker.Unsubscribe");
            }

            Type? payload = find("Common", "Common.Messaging.MessagePayload`1");
            if (payload == null) missing.Add("MessagePayload<T>");
            else
            {
                if (payload.GetProperty("What") == null) missing.Add("MessagePayload.What");
                if (payload.GetProperty("Who") == null) missing.Add("MessagePayload.Who");
            }

            Type? network = find("Common", "Common.Network.INetwork");
            if (network == null) missing.Add("INetwork");
            else
            {
                if (!HasMethod(network, "Send", 2)) missing.Add("INetwork.Send");
                if (!HasMethod(network, "SendAll", 1)) missing.Add("INetwork.SendAll");
            }

            // Not IServerToClientCommand: newer Coop removed it, and the messages implement ICommand only.
            if (find("Common", "Common.Messaging.ICommand") == null) missing.Add("ICommand");
            Type? info = find("Common", "Common.ModInformation");
            if (info?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static) == null) missing.Add("ModInformation.IsServer");
            if (find("LiteNetLib", "LiteNetLib.NetPeer") == null) missing.Add("NetPeer");
            if (find("protobuf-net.Core", "ProtoBuf.ProtoContractAttribute") == null
                && find("protobuf-net", "ProtoBuf.ProtoContractAttribute") == null) missing.Add("ProtoContract");

            // The server marks this answer and the client reads the mark.
            if (find("Coop.Core", "Coop.Core.Server.Connections.Messages.NetworkModuleVersionsValidated") == null) missing.Add("NetworkModuleVersionsValidated");

            // Coop finds handlers by the namespace of these modules (Coop.Core.Server / Coop.Core.Client).
            if (client)
            {
                if (find("Coop.Core", "Coop.Core.Client.ClientModule") == null) missing.Add("Coop.Core.Client.ClientModule");
                if (find("GameInterface", "GameInterface.Services.GameState.Messages.CampaignReady") == null) missing.Add("CampaignReady");
            }
            // The PvP countdown's PlayerDisconnected is not required here: PvpCountdownDisconnects finds it by name and
            // only that drop is lost without it.
            else if (find("Coop.Core", "Coop.Core.Server.ServerModule") == null) missing.Add("Coop.Core.Server.ServerModule");

            return new CoopProbeResult(missing.Count == 0, false, missing);
        }

        /// <summary>ModderLords' compat module (ModderLords.Compat, DedicatedServer.ModderLordsCompat) runs in this game.</summary>
        public static bool ModderLordsLoaded() => ModderLordsLoaded(AppDomain.CurrentDomain.GetAssemblies().Select(SafeName));

        public static bool ModderLordsLoaded(IEnumerable<string?> assemblyNames) =>
            assemblyNames.Any(n => string.Equals(n, "ModderLords.CompatSync", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "ModderLords.CompatSync.Coop", StringComparison.OrdinalIgnoreCase));

        private static string? SafeName(Assembly assembly)
        {
            try { return assembly.IsDynamic ? null : assembly.GetName().Name; }
            catch (Exception) { return null; }
        }

        /// <summary>Finds a type in the assemblies already loaded in this process.</summary>
        public static Type? FindLoaded(string assemblyName, string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string? name;
                try { name = assembly.GetName().Name; }
                catch (Exception) { continue; }
                if (!string.Equals(name, assemblyName, StringComparison.Ordinal)) continue;
                try
                {
                    Type? type = assembly.GetType(typeName, false);
                    if (type != null) return type;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static bool HasMethod(Type type, string name, int parameters) =>
            AllMethods(type).Any(m => m.Name == name && m.GetParameters().Length == parameters);

        // An interface's GetMethods leaves out the methods of the interfaces it extends.
        private static IEnumerable<MethodInfo> AllMethods(Type type) =>
            type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()).SelectMany(t => t.GetMethods()) : type.GetMethods();
    }

    /// <summary>
    /// Loads CoodszSettingsSync.Coop.dll from the submodule's bin folder once the probe passes and keeps its
    /// entry points. Every failure ends in one log line and the sync staying off; nothing throws into the game.
    /// </summary>
    public sealed class SettingsSyncLoader
    {
        private const float GiveUpAfterSeconds = 60f;

        private readonly bool _client;
        private readonly string _binFolder;
        private readonly string _moduleFolder;
        private readonly Action<string> _log;
        private readonly string _startMethod;
        private readonly string _name;
        private bool _done;
        private float _waited;
        private MethodInfo? _tick;
        private Assembly? _assembly;

        /// <summary><paramref name="startMethod"/> is the bridge's entry point: Start, or StartWarOnly on a server that only sends the War of the Ring state.</summary>
        public SettingsSyncLoader(bool client, string binFolder, string moduleFolder, Action<string> log, string startMethod = "Start")
        {
            _startMethod = startMethod;
            _name = startMethod == "StartWarOnly" ? "War of the Ring sync" : "Settings sync";
            _client = client;
            _binFolder = binFolder;
            _moduleFolder = moduleFolder;
            _log = log;
        }

        public bool Active => _tick != null;

        /// <summary>
        /// A public static method of the loaded assembly, for the other parts of the server tools (the PvP countdown);
        /// null before the sync loaded, when it is off, or when the method is not there.
        /// </summary>
        public MethodInfo? Entry(string typeName, string methodName)
        {
            try { return _tick == null ? null : _assembly?.GetType(typeName, false)?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static); }
            catch (Exception) { return null; }
        }

        /// <summary>Tries to load; called when the module screen is ready and then from the tick until it settles.</summary>
        public void TryLoad(float waited = 0f)
        {
            if (_done) return;
            _waited += waited;
            try
            {
                // ModderLords has its own settings sync; two of them would apply every value twice.
                if (CoopProbe.ModderLordsLoaded())
                {
                    _done = true;
                    _log(_name + " is off here: ModderLords.Compat is loaded and syncs the mod settings itself.");
                    return;
                }

                CoopProbeResult probe = CoopProbe.Check(_client, CoopProbe.FindLoaded);
                if (!probe.Ready)
                {
                    // Coop's assemblies can load after this module's first call; keep asking for a minute.
                    if (_waited < GiveUpAfterSeconds) return;
                    _done = true;
                    _log(_name + " disabled: " + probe.Describe() + ".");
                    return;
                }

                _done = true;
                string path = Path.Combine(_binFolder, CoopProbe.AssemblyFileName);
                if (!File.Exists(path))
                {
                    _log(_name + " disabled: " + CoopProbe.AssemblyFileName + " is missing next to the module.");
                    return;
                }

                Assembly assembly = Assembly.LoadFrom(path);
                // Loads every type now, inside this try, instead of later inside Coop's own handler scan.
                assembly.GetTypes();
                Type? bridge = assembly.GetType(_client ? "CoodszSettingsSync.ClientSync" : "CoodszSettingsSync.ServerSync", false);
                MethodInfo? start = bridge?.GetMethod(_startMethod, BindingFlags.Public | BindingFlags.Static);
                MethodInfo? tick = bridge?.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static);
                if (start == null || tick == null)
                {
                    _log(_name + " disabled: " + CoopProbe.AssemblyFileName + " has no entry point.");
                    return;
                }
                start.Invoke(null, new object[] { _log, _moduleFolder });
                _tick = tick;
                _assembly = assembly;
            }
            catch (Exception e)
            {
                _done = true;
                _tick = null;
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                _log(_name + " disabled: " + inner.GetType().Name + ": " + inner.Message);
            }
        }

        /// <summary>
        /// From the submodule's application tick, on the game thread. A tick that throws is logged and the
        /// next one runs; the sync stops only after <see cref="MaxFailuresInARow"/> failures in a row.
        /// </summary>
        public void Tick(float dt)
        {
            if (!_done)
            {
                TryLoad(dt);
                return;
            }
            Action<float>? tick = _tickAction ?? (_tick == null ? (Action<float>?)null : delta => _tick.Invoke(null, new object[] { delta }));
            if (tick == null) return;
            try
            {
                tick(dt);
                _failures = 0;
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                if (++_failures < MaxFailuresInARow)
                {
                    _log(_name + ": a tick failed (" + _failures.ToString(System.Globalization.CultureInfo.InvariantCulture) + " in a row): "
                        + inner.GetType().Name + ": " + inner.Message);
                    return;
                }
                _tick = null;
                _tickAction = null;
                _log(_name + " stopped after " + MaxFailuresInARow.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " failures in a row: " + inner.GetType().Name + ": " + inner.Message);
            }
        }

        public const int MaxFailuresInARow = 5;
        private int _failures;
        private Action<float>? _tickAction;

        /// <summary>For tests: a loaded sync with this tick.</summary>
        internal void UseTick(Action<float> tick)
        {
            _done = true;
            _tickAction = tick;
        }

        public bool Running => _tick != null || _tickAction != null;
    }
}
