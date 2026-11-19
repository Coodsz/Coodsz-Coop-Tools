// Coodsz Coop Link: checks by name, with plain reflection, every Coop member Coodsz.CoopLink.dll is compiled
// against, so a mod can load the link only when nothing is missing. A Coop update that moves one of them then
// costs one log line instead of a type load error inside the game.
//
// No reference to Coop here. Compiled into Coodsz.CoopLink.dll and meant to be compiled as source into a mod
// too (a mod cannot call into the link DLL before it decided to load it). The public API is additive.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Coodsz.CoopLink
{
    public static class LinkProbe
    {
        public const string AssemblyName = "Coodsz.CoopLink";
        public const string FileName = "Coodsz.CoopLink.dll";

        /// <summary>
        /// What the link needs and is missing in the assemblies loaded now. Empty means the link can be loaded.
        /// A list that starts with "Coop's Common assembly" means Coop is not loaded yet: ask again on a later tick.
        /// </summary>
        public static List<string> Check() => Check(FindLoaded, null);

        /// <summary>
        /// The same check over any lookup. <paramref name="find"/> returns a type by assembly simple name and
        /// full type name, or null. Members the link can do without (Coop's GameThread.RunSafe) go to
        /// <paramref name="optionalMissing"/> when given.
        /// </summary>
        public static List<string> Check(Func<string, string, Type?> find, List<string>? optionalMissing)
        {
            var missing = new List<string>();
            Type? message = find("Common", "Common.Messaging.IMessage");
            Type? broker = find("Common", "Common.Messaging.MessageBroker");
            if (message == null && broker == null)
            {
                missing.Add("Coop's Common assembly");
                return missing;
            }

            if (message == null) missing.Add("Common.Messaging.IMessage");
            Type? command = find("Common", "Common.Messaging.ICommand");
            if (command == null) missing.Add("Common.Messaging.ICommand");
            else if (message != null && !message.IsAssignableFrom(command)) missing.Add("ICommand extends IMessage");

            if (broker == null) missing.Add("Common.Messaging.MessageBroker");
            else
            {
                if (broker.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) == null) missing.Add("MessageBroker.Instance");
                if (!broker.GetMethods().Any(m => m.Name == "Subscribe" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1))
                    missing.Add("MessageBroker.Subscribe<T>(one parameter)");
            }

            Type? payload = find("Common", "Common.Messaging.MessagePayload`1");
            if (payload == null) missing.Add("Common.Messaging.MessagePayload<T>");
            else
            {
                if (payload.GetProperty("What") == null) missing.Add("MessagePayload.What");
                if (payload.GetProperty("Who") == null) missing.Add("MessagePayload.Who");
            }

            Type? network = find("Common", "Common.Network.INetwork");
            if (network == null) missing.Add("Common.Network.INetwork");
            else
            {
                if (!HasMessageMethod(network, "Send", 2)) missing.Add("INetwork.Send(NetPeer, IMessage)");
                if (!HasMessageMethod(network, "SendImmediate", 2)) missing.Add("INetwork.SendImmediate(NetPeer, IMessage)");
                if (!HasMessageMethod(network, "SendAll", 1)) missing.Add("INetwork.SendAll(IMessage)");
            }

            Type? info = find("Common", "Common.ModInformation");
            if (info?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static) == null) missing.Add("ModInformation.IsServer");
            if (find("LiteNetLib", "LiteNetLib.NetPeer") == null) missing.Add("LiteNetLib.NetPeer");
            if (find("protobuf-net.Core", "ProtoBuf.ProtoContractAttribute") == null && find("protobuf-net", "ProtoBuf.ProtoContractAttribute") == null)
                missing.Add("ProtoBuf.ProtoContractAttribute");
            if (find("protobuf-net.Core", "ProtoBuf.ProtoMemberAttribute") == null && find("protobuf-net", "ProtoBuf.ProtoMemberAttribute") == null)
                missing.Add("ProtoBuf.ProtoMemberAttribute");

            if (optionalMissing != null)
            {
                Type? gameThread = find("Common", "Common.GameThread");
                bool runSafe = gameThread != null && gameThread.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Any(m => m.Name == "RunSafe" && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(Action));
                if (!runSafe) optionalMissing.Add("Common.GameThread.RunSafe(Action, ...)");
            }
            return missing;
        }

        /// <summary>
        /// Loads the link once the probe passed. A copy another mod loaded first is used as it is (check
        /// Link.Version). Otherwise the copy in <paramref name="folder"/>, then by simple name (a server that
        /// moved our DLLs into its own bin). Null on failure, with one line through <paramref name="log"/>.
        /// </summary>
        public static Assembly? Load(string folder, Action<string>? log)
        {
            try
            {
                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!loaded.IsDynamic && string.Equals(SafeName(loaded), AssemblyName, StringComparison.Ordinal))
                        return loaded;
                }
                string path = Path.Combine(folder ?? "", FileName);
                Assembly assembly = File.Exists(path) ? Assembly.LoadFrom(path) : Assembly.Load(new AssemblyName(AssemblyName));
                // Loads every type now, inside this try, instead of later inside a scan of Coop's or the game's.
                assembly.GetTypes();
                return assembly;
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                log?.Invoke("Coop link not loaded: " + inner.GetType().Name + ": " + inner.Message);
                return null;
            }
        }

        /// <summary>Finds a type in an assembly that is already loaded. Never loads anything.</summary>
        public static Type? FindLoaded(string assemblyName, string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || !string.Equals(SafeName(assembly), assemblyName, StringComparison.Ordinal)) continue;
                try
                {
                    Type? type = assembly.GetType(typeName, false);
                    if (type != null) return type;
                }
                catch (Exception) { }
            }
            return null;
        }

        // A method of this name and parameter count whose last parameter is Coop's IMessage. An interface's
        // GetMethods leaves out the methods of the interfaces it extends, so those are walked too.
        private static bool HasMessageMethod(Type type, string name, int parameters)
        {
            IEnumerable<Type> types = type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()) : new[] { type };
            return types.SelectMany(t => t.GetMethods()).Any(m =>
            {
                ParameterInfo[] p = m.GetParameters();
                return m.Name == name && p.Length == parameters
                    && p[parameters - 1].ParameterType.FullName == "Common.Messaging.IMessage"
                    && (parameters == 1 || p[0].ParameterType.FullName == "LiteNetLib.NetPeer");
            });
        }

        private static string? SafeName(Assembly assembly)
        {
            try { return assembly.GetName().Name; }
            catch (Exception) { return null; }
        }
    }
}
