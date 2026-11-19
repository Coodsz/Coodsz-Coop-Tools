#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RealmOfThronesCoopCompat
{
    // Every call into Coodsz.CoopLink.dll, by reflection. This DLL never references the link: the
    // engine probes the references of a SubModule DLL in its own bin folder and logs a "Cannot load"
    // ERROR at server boot for one it cannot find there. So the link is loaded by hand once
    // LinkProbe passed (RotLink), and these calls find its public static members by name and
    // signature. Each member the link's README lists for version 1 that this module uses is looked
    // up once in Bind; one missing switches the link off with its name. Our callback takes the
    // link's LinkMessage as object (a delegate may take a base type of its parameter) and reads its
    // properties by name. No game types, so the launcher tests can bind it.
    internal sealed class RotLinkCalls
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        private PropertyInfo _version;
        private PropertyInfo _log;
        private PropertyInfo _networkProvider;
        private PropertyInfo _peerState;
        private MethodInfo _start;
        private MethodInfo _register;
        private MethodInfo _drain;
        private MethodInfo _sessionStarted;
        private MethodInfo _sessionEnded;
        private MethodInfo _peerVersion;
        private MethodInfo _toAll;
        private MethodInfo _toPlayer;
        private MethodInfo _toServer;
        private MethodInfo _subscribe;
        private Type _messageType;
        private PropertyInfo _channel, _key, _seq, _args, _blob, _sender, _fromServer;

        // The link's members this module needs that the given types lack, by name. Empty means bound.
        public List<string> Bind(Type link, Type message)
        {
            var missing = new List<string>();
            if (link == null || message == null)
            {
                missing.Add(link == null ? "Coodsz.CoopLink.Link" : "Coodsz.CoopLink.LinkMessage");
                return missing;
            }
            _messageType = message;
            var list = typeof(IList<string>);
            _version = Property(link, "Version", typeof(int), missing);
            _log = Property(link, "Log", typeof(Action<string>), missing);
            _networkProvider = Property(link, "NetworkProvider", typeof(Func<object>), missing);
            _peerState = link.GetProperty("PeerState", PublicStatic);
            if (_peerState == null || !_peerState.PropertyType.IsEnum)
                missing.Add(link.Name + ".PeerState");
            _start = Method(link, "Start", typeof(bool), missing);
            _register = Method(link, "Register", typeof(bool), missing, typeof(string), typeof(Action<>).MakeGenericType(message));
            _drain = Method(link, "Drain", typeof(int), missing, typeof(int));
            _sessionStarted = Method(link, "SessionStarted", typeof(void), missing);
            _sessionEnded = Method(link, "SessionEnded", typeof(void), missing);
            _peerVersion = Method(link, "GetPeerLinkVersion", typeof(int), missing, typeof(object));
            _toAll = Method(link, "ToAll", typeof(bool), missing, typeof(object), typeof(string), typeof(string), typeof(int), list, typeof(byte[]));
            _toPlayer = Method(link, "ToPlayer", typeof(bool), missing, typeof(object), typeof(object), typeof(bool), typeof(string), typeof(string), typeof(int), list, typeof(byte[]));
            _toServer = Method(link, "ToServer", typeof(bool), missing, typeof(object), typeof(string), typeof(string), typeof(int), list, typeof(byte[]));
            _subscribe = Method(link, "TrySubscribeByName", typeof(bool), missing, typeof(string), typeof(string));
            _channel = Property(message, "Channel", typeof(string), missing, false);
            _key = Property(message, "Key", typeof(string), missing, false);
            _seq = Property(message, "Seq", typeof(int), missing, false);
            _args = Property(message, "Args", typeof(IReadOnlyList<string>), missing, false);
            _blob = Property(message, "Blob", typeof(byte[]), missing, false);
            _sender = Property(message, "Sender", typeof(object), missing, false);
            _fromServer = Property(message, "FromServer", typeof(bool), missing, false);
            return missing;
        }

        public int Version() => (int)_version.GetValue(null, null);

        // Adds our handler to Link.Log and gives the link Coop's network, then starts it and
        // registers our prefix. False when Start or Register said no.
        public bool Start(Action<string> log, Func<object> network, string prefix, Action<object> onMessage)
        {
            _log.SetValue(null, Delegate.Combine((Delegate)_log.GetValue(null, null), log), null);
            _networkProvider.SetValue(null, network, null);
            if (!(bool)_start.Invoke(null, null))
                return false;
            var callback = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(_messageType), onMessage.Target, onMessage.Method);
            return (bool)_register.Invoke(null, new object[] { prefix, callback });
        }

        public int Drain(int max) => (int)_drain.Invoke(null, new object[] { max });

        public void SessionStarted() => _sessionStarted.Invoke(null, null);

        public void SessionEnded() => _sessionEnded.Invoke(null, null);

        // NoSession, Waiting, Answered or NoAnswer (the link's PeerLinkState by name).
        public string PeerState() => _peerState.GetValue(null, null)?.ToString();

        public int PeerVersion(object peer) => (int)_peerVersion.Invoke(null, new[] { peer });

        public bool Subscribe(string typeName, string key) => (bool)_subscribe.Invoke(null, new object[] { typeName, key });

        public bool ToAll(object network, string channel, string key, int seq, IList<string> args, byte[] blob) =>
            (bool)_toAll.Invoke(null, new object[] { network, channel, key, seq, args, blob });

        public bool ToPlayer(object network, object peer, bool immediate, string channel, string key, int seq, IList<string> args, byte[] blob) =>
            (bool)_toPlayer.Invoke(null, new object[] { network, peer, immediate, channel, key, seq, args, blob });

        public bool ToServer(object network, string channel, string key, int seq, IList<string> args, byte[] blob) =>
            (bool)_toServer.Invoke(null, new object[] { network, channel, key, seq, args, blob });

        // One message as plain values.
        public RotLinkMessage Read(object message) => new RotLinkMessage(
            (string)_channel.GetValue(message, null), (string)_key.GetValue(message, null), (int)_seq.GetValue(message, null),
            (IReadOnlyList<string>)_args.GetValue(message, null), (byte[])_blob.GetValue(message, null), _sender.GetValue(message, null), (bool)_fromServer.GetValue(message, null));

        private static PropertyInfo Property(Type type, string name, Type propertyType, List<string> missing, bool isStatic = true)
        {
            var property = type.GetProperty(name, isStatic ? PublicStatic : BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != propertyType)
            {
                missing.Add(type.Name + "." + name);
                return null;
            }
            return property;
        }

        private static MethodInfo Method(Type type, string name, Type returns, List<string> missing, params Type[] parameters)
        {
            var method = type.GetMethod(name, PublicStatic, null, parameters, null);
            if (method == null || method.ReturnType != returns)
            {
                missing.Add(type.Name + "." + name);
                return null;
            }
            return method;
        }
    }

    // A link message as plain values, so nothing past RotLinkCalls names a link type.
    internal sealed class RotLinkMessage
    {
        public RotLinkMessage(string channel, string key, int seq, IReadOnlyList<string> args, byte[] blob, object sender, bool fromServer)
        {
            Channel = channel ?? "";
            Key = key ?? "";
            Seq = seq;
            Args = args ?? new string[0];
            Blob = blob ?? new byte[0];
            Sender = sender;
            FromServer = fromServer;
        }

        public string Channel { get; }
        public string Key { get; }
        public int Seq { get; }
        public IReadOnlyList<string> Args { get; }
        public byte[] Blob { get; }
        public object Sender { get; }
        public bool FromServer { get; }
    }
}
