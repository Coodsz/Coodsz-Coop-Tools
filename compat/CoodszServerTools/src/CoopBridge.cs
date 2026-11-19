// Coodsz Server Tools: Coop's objects, read by reflection so this module never needs Coop's DLLs.
// Used by Max clans per kingdom (ClanCap.cs) and the server rules (ServerRules.cs).

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CoodszServerTools;

internal static class CoopBridge
{
    public static void Log(string line)
    {
        // One line in the server log (Debug.Print would print it a second time).
        try { Console.WriteLine("[Coodsz Server Tools] " + line); } catch { }
    }

    /// <summary>A field or property of a Coop object, or null.</summary>
    public static object? Field(object target, string name)
    {
        var type = target.GetType();
        var field = AccessTools.Field(type, name);
        if (field != null) return field.GetValue(target);
        return AccessTools.Property(type, name)?.GetValue(target);
    }

    /// <summary>A campaign object by Coop's id, through a handler's own object manager.</summary>
    public static T? CoopObject<T>(object handler, string id) where T : class
    {
        var manager = Field(handler, "objectManager");
        return manager is null ? null : FromManager<T>(manager, id);
    }

    /// <summary>A campaign object by Coop's id, through Coop's IObjectManager.</summary>
    public static T? FromManager<T>(object manager, string? id) where T : class
    {
        if (string.IsNullOrEmpty(id)) return null;
        var method = manager.GetType().GetInterfaces().Concat(new[] { manager.GetType() })
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .FirstOrDefault(m => m.Name == "TryGetObject" && m.IsGenericMethodDefinition &&
                                 m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
        if (method is null) return null;
        var arguments = new object?[] { id, null };
        return method.MakeGenericMethod(typeof(T)).Invoke(manager, arguments) is true ? arguments[1] as T : null;
    }

    /// <summary>
    /// Coop's id for a campaign object (IObjectManager.TryGetId(object, out string)) through a handler's own object
    /// manager, or null when Coop has none. Coop's ids are its own keys, never built from a StringId.
    /// </summary>
    public static string? CoopId(object handler, object obj)
    {
        var manager = Field(handler, "objectManager");
        var method = manager?.GetType().GetInterfaces().Concat(new[] { manager.GetType() })
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .FirstOrDefault(m => m.Name == "TryGetId" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 2 &&
                                 m.GetParameters()[0].ParameterType == typeof(object) && m.GetParameters()[1].ParameterType == typeof(string).MakeByRefType());
        if (method is null) return null;
        var arguments = new object?[] { obj, null };
        return method.Invoke(manager, arguments) is true ? arguments[1] as string : null;
    }

    /// <summary>One of Coop's services from its container (GameInterface.ContainerProvider.TryResolve), or null.</summary>
    public static object? Resolve(string interfaceName)
    {
        var provider = AccessTools.TypeByName("GameInterface.ContainerProvider");
        var type = AccessTools.TypeByName(interfaceName);
        var tryResolve = provider?.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "TryResolve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        if (tryResolve is null || type is null) return null;
        var arguments = new object?[] { null };
        return tryResolve.MakeGenericMethod(type).Invoke(null, arguments) is true ? arguments[0] : null;
    }

    /// <summary>The controller id of the player whose hero this is, or null.</summary>
    public static string? ControllerOfHero(object players, string heroId)
    {
        if (Field(players, "Players") is IEnumerable all)
            foreach (var player in all)
                if (player != null && string.Equals(Field(player, "HeroId") as string, heroId, StringComparison.Ordinal))
                    return Field(player, "ControllerId") as string;
        return null;
    }

    /// <summary>Sends the player whose hero this is a System line in Coop's chat. Best effort.</summary>
    public static void TellHero(object handler, string heroId, string text)
    {
        var players = Field(handler, "playerManager");
        var controller = players is null ? null : ControllerOfHero(players, heroId);
        if (controller != null) TellController(handler, controller, text);
    }

    /// <summary>
    /// Sends one player a System line in Coop's chat (NetworkChatMessage on the System channel), through
    /// the handler's own player manager and network. Best effort: a failure is one log line.
    /// </summary>
    public static void TellController(object handler, string controller, string text)
    {
        try
        {
            var players = Field(handler, "playerManager");
            var network = Field(handler, "network");
            if (players is null || network is null || string.IsNullOrEmpty(controller)) return;

            var tryGetPeer = players.GetType().GetMethod("TryGetPeer", new[] { typeof(string), AccessTools.TypeByName("LiteNetLib.NetPeer").MakeByRefType() });
            var peerArgs = new object?[] { controller, null };
            if (tryGetPeer?.Invoke(players, peerArgs) is not true || peerArgs[1] is null) return;
            TellPeer(network, peerArgs[1]!, controller, text);
        }
        catch (Exception e)
        {
            Log("Could not tell the player why: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// Sends one connected peer a System line in Coop's chat, the way Coop's ServerChatHandler.SendSystemMessage
    /// does (network.SendImmediate with a NetworkChatMessage). An empty controller shows it in the chat tab the
    /// player has open. Throws on failure; callers catch it.
    /// </summary>
    public static void TellPeer(object network, object peer, string controller, string text)
    {
        var messageType = AccessTools.TypeByName("GameInterface.Services.Chat.Messages.NetworkChatMessage");
        var channelType = AccessTools.TypeByName("GameInterface.Services.Chat.Messages.ChatChannel");
        if (messageType is null || channelType is null) throw new MissingMemberException("NetworkChatMessage or ChatChannel");
        var message = Activator.CreateInstance(messageType, Enum.ToObject(channelType, 2), "", "Server", controller ?? "", "", text);
        var send = network.GetType().GetMethods().FirstOrDefault(m => m.Name == "SendImmediate" && m.GetParameters().Length == 2 &&
                                                                        m.GetParameters()[1].ParameterType.IsAssignableFrom(messageType));
        if (send is null) throw new MissingMethodException("SendImmediate");
        send.Invoke(network, new[] { peer, message });
    }
}
