// Improved Garrisons Coop Compat (DedicatedServer.IGCoopCompat), by Coodsz.
//
// Built against Improved Garrisons 4.2.0.7 (ImprovedGarrisons) and CoopModPatch 0.7.1 (CoopModPatch),
// on a Bannerlord Coop dedicated server. Neither mod is referenced: their types are found by name at
// run time and patched with Harmony, and every hook is optional. None of their code is copied here.
//
// The problem: Improved Garrisons keeps per town settings only for towns the main hero's clan owns.
// - GarrisonBehavior.InitializeSettlements (run by IG's save loading) drops the settings of every town
//   whose owner clan is not Hero.MainHero.Clan.
// - GarrisonBehavior.onSettlementOwnerChanged drops a town's settings when the new owner is not
//   Hero.MainHero.
// On the dedicated server the main hero owns nothing, so every player's town settings are dropped, and
// CoopModPatch then writes that empty state over its own settings file. On the server the load clean up
// is skipped, and an owner change drops the town's settings only when the town goes to another clan
// (a clan member taking it keeps them), so a new owner never inherits the old owner's garrison setup.
// CoopModPatch seeds IG's settings from its own file on its first poll, which can run before IG's
// SaveWriterManager.LoadSaveFile replaces the data; after that load CoopModPatch's RestoreAll runs again.
//
// CoopModPatch's IGBridge.Execute runs any player's IG request for any town. The owner check refuses a
// request for a town another clan owns, checks every target of a copy, narrows a copy to all to the
// requester clan's own garrisons, and refuses senders that are not a known player with a clan. Only
// when Coop's player list cannot be reached by reflection are requests let through.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisonsCoopCompat;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        Hooks.TryApply(lastTry: false);
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        Hooks.TryApply(lastTry: false);
    }

    // The dedicated server may never show the main menu; a game start is before IG loads its save data.
    protected override void OnGameStart(TaleWorlds.Core.Game game, TaleWorlds.Core.IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);
        Hooks.TryApply(lastTry: true);
        // Improved Garrisons builds on another mod's party size model loaded before it (PartySizeHandBack.cs, the
        // same file players run in the client add on). With plain Coop it changes nothing.
        if (Server.IsDedicated)
            Coodsz.IgPartySize.PartySizeHandBack.TryInstall(Log);
    }

    internal static void Log(string line)
    {
        // One line in the server log (Debug.Print would print it a second time).
        try { Console.WriteLine("[IGCoopCompat] " + line); } catch { }
    }
}

internal static class Hooks
{
    public const int HookCount = 4;
    private const string HarmonyId = "coodsz.igcoopcompat";
    private const string GarrisonBehaviorType = "ImprovedGarrisons.SaveSystem.GarrisonBehavior";
    private const string BridgeType = "CoopModPatch.Adapters.ImprovedGarrisons.IGBridge";
    private const string SaveWriterType = "ImprovedGarrisons.SaveSystem.SaveWriter.SaveWriterManager";
    private const string SettingsStoreType = "CoopModPatch.Adapters.ImprovedGarrisons.IGSettingsStore";

    private static readonly Harmony Harmony = new(HarmonyId);
    private static bool _initialize, _ownerChanged, _execute, _reload, _readyLogged, _notServerLogged;

    public static void TryApply(bool lastTry)
    {
        try
        {
            if (!Server.IsDedicated)
            {
                if (lastTry && !_notServerLogged)
                {
                    _notServerLogged = true;
                    SubModule.Log("not a dedicated server; nothing patched.");
                }
                return;
            }

            var behavior = AccessTools.TypeByName(GarrisonBehaviorType);
            _initialize = _initialize || Patch(behavior, "InitializeSettlements", 0, nameof(SkipOnServer));
            _ownerChanged = _ownerChanged || Patch(behavior, "onSettlementOwnerChanged", 6, nameof(OwnerChanged));
            _execute = _execute || Patch(AccessTools.TypeByName(BridgeType), "Execute", 2, nameof(OwnerCheck));
            _reload = _reload || Patch(AccessTools.TypeByName(SaveWriterType), "LoadSaveFile", 1, null, nameof(ReapplyCoopModPatchSettings));

            var count = (_initialize ? 1 : 0) + (_ownerChanged ? 1 : 0) + (_execute ? 1 : 0) + (_reload ? 1 : 0);
            if (_readyLogged || (count < HookCount && !lastTry))
                return;
            _readyLogged = true;
            if (!_initialize) SubModule.Log("Improved Garrisons GarrisonBehavior.InitializeSettlements not found; town settings may still be dropped on load.");
            if (!_ownerChanged) SubModule.Log("Improved Garrisons GarrisonBehavior.onSettlementOwnerChanged not found; town settings may still be dropped when a town changes hands.");
            if (!_execute) SubModule.Log("CoopModPatch IGBridge.Execute not found; the town owner check is off.");
            if (!_reload) SubModule.Log("Improved Garrisons SaveWriterManager.LoadSaveFile not found; CoopModPatch's town settings are not put back after IG loads its save data.");
            SubModule.Log("ready (" + count + " of " + HookCount + " hooks)");
        }
        catch (Exception ex)
        {
            if (!_readyLogged)
            {
                _readyLogged = true;
                SubModule.Log("could not start: " + ex.GetBaseException().Message);
            }
        }
    }

    private static bool Patch(Type? type, string name, int parameters, string? prefix, string? postfix = null)
    {
        if (type == null)
            return false;
        var original = AccessTools.GetDeclaredMethods(type)
            .FirstOrDefault(method => method.Name == name && !method.IsAbstract && method.GetParameters().Length == parameters);
        if (original == null)
            return false;
        Harmony.Patch(original,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(Hooks), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(Hooks), postfix));
        return true;
    }

    // IG's clean ups compare towns to the server's own hero, who owns nothing on the dedicated server.
    private static bool SkipOnServer() => !Server.IsDedicated;

    private static bool _restoreMissingLogged;

    // After IG's SaveWriterManager.LoadSaveFile(IGSaveFilePath) on the server. CoopModPatch seeds IG's town
    // settings from its ig-settings.txt on its first poll, but IG may load its own save data after that,
    // which replaces IGSaveData.Instance and so drops the seeded settings; CoopModPatch's next poll would
    // then save the empty state over its file. CoopModPatch's own IGSettingsStore.RestoreAll() seeds them
    // again into the data IG just loaded.
    private static void ReapplyCoopModPatchSettings()
    {
        if (!Server.IsDedicated)
            return;
        try
        {
            var store = AccessTools.TypeByName(SettingsStoreType);
            var restore = store == null ? null : AccessTools.GetDeclaredMethods(store)
                .FirstOrDefault(method => method.Name == "RestoreAll" && method.IsStatic && method.GetParameters().Length == 0);
            if (restore == null)
            {
                if (!_restoreMissingLogged)
                {
                    _restoreMissingLogged = true;
                    SubModule.Log("CoopModPatch IGSettingsStore.RestoreAll not found; IG's town settings were not put back after IG loaded its save data.");
                }
                return;
            }
            restore.Invoke(null, null);
            SubModule.Log("re-applied CoopModPatch town settings after IG loaded its save data");
        }
        catch (Exception ex)
        {
            SubModule.Log("could not re-apply CoopModPatch town settings: " + ex.GetBaseException().Message);
        }
    }

    private static bool _settingsUnreachableLogged;

    // Runs instead of IG's GarrisonBehavior.onSettlementOwnerChanged(settlement, openToClaim, newOwner,
    // oldOwner, capturer, detail) on the server. IG keys its per town settings by the town's display name
    // in GarrisonBehavior.SettlementSettingsData (IGSaveData.Instance). The entry goes only when the town
    // changes clan; if the settings cannot be reached nothing is removed.
    private static bool OwnerChanged(object __instance, object[] __args)
    {
        if (!Server.IsDedicated)
            return true;
        try
        {
            if (__args == null || __args.Length < 4 || __args[0] is not Settlement settlement || settlement.Town == null)
                return false;
            var newClan = (__args[2] as Hero)?.Clan ?? settlement.OwnerClan;
            var oldClan = (__args[3] as Hero)?.Clan;
            if (!OwnerRules.DropsSettings(newClan, oldClan))
                return false;
            if (Coop.Member(__instance, "SettlementSettingsData") is not System.Collections.IDictionary settings)
            {
                SettingsUnreachable("Improved Garrisons SettlementSettingsData");
                return false;
            }
            var key = settlement.Town.Name?.ToString();
            if (key != null && settings.Contains(key))
            {
                settings.Remove(key);
                SubModule.Log("cleared the Improved Garrisons settings of " + key + "; it went from clan " + (oldClan?.StringId ?? "none") + " to clan " + (newClan?.StringId ?? "none") + ".");
            }
        }
        catch (Exception ex)
        {
            SettingsUnreachable(ex.GetBaseException().Message);
        }
        return false;
    }

    private static void SettingsUnreachable(string why)
    {
        if (_settingsUnreachableLogged)
            return;
        _settingsUnreachableLogged = true;
        SubModule.Log("could not reach the Improved Garrisons town settings (" + why + "); a town changing hands keeps its settings.");
    }

    // Requests that change no town: the client asking for its own data again, and ending its own transfer.
    private static readonly HashSet<string> NoTownChange = new(StringComparer.Ordinal) { "SyncTown", "TransferAbort" };
    private static bool _unreachableLogged, _nullPeerLogged, _checkFailedLogged;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> LoggedPeers = new();

    // Runs before CoopModPatch's IGBridge.Execute(IGActionRequest req, NetPeer peer). False skips the request.
    // TransferBegin and the Order requests only move the requester's own troops, so the source town check
    // below is all they get.
    private static bool OwnerCheck(object[] __args)
    {
        try
        {
            if (!Server.IsDedicated || __args == null || __args.Length < 2 || __args[0] is not { } request)
                return true;
            var kind = Coop.Member(request, "Kind") as string;
            if (kind != null && NoTownChange.Contains(kind))
                return true;
            var id = Coop.Member(request, "SettlementId") as string;
            var settlement = string.IsNullOrEmpty(id) ? null : MBObjectManager.Instance?.GetObject<Settlement>(id);
            if (settlement?.Town == null)
                return true; // CoopModPatch answers an unknown town itself.

            var peer = __args[1];
            switch (Coop.ClanOf(peer, out var clan))
            {
                case ClanLookup.Unreachable:
                    if (!_unreachableLogged)
                    {
                        _unreachableLogged = true;
                        SubModule.Log("could not reach Coop's player list; Improved Garrisons requests are let through without the owner check.");
                    }
                    return true;
                case ClanLookup.NoClan:
                    UnknownPlayer(peer);
                    Coop.Reply(peer, "IG: the server could not find your clan, so this action was not run.");
                    return false;
            }

            var town = settlement.Name?.ToString() ?? id!;
            var owner = settlement.OwnerClan;
            if (owner != clan)
                return Refuse(peer, kind, town, clan!, owner?.StringId ?? "no clan", "IG: only the clan that owns " + town + " can change its garrison.");

            if (kind == "CopySpecific")
            {
                var foreign = OwnerRules.FirstForeignTarget(OwnerRules.SplitIds(Coop.Member(request, "StrArg") as string), TargetOwner, clan!);
                if (foreign != null)
                {
                    var target = MBObjectManager.Instance?.GetObject<Settlement>(foreign);
                    var name = target?.Name?.ToString() ?? foreign;
                    return Refuse(peer, kind, name, clan!, target?.OwnerClan?.StringId ?? "no clan", "IG: you can only copy settings to your own clan's garrisons, and " + name + " is not one of them.");
                }
            }
            else if (kind == "CopyAll")
            {
                return RewriteCopyAll(request, peer, settlement, town, clan!);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!_checkFailedLogged)
            {
                _checkFailedLogged = true;
                SubModule.Log("the town owner check failed, so requests are let through: " + ex.GetBaseException().Message);
            }
            return true;
        }
    }

    private static (bool Known, object? Owner) TargetOwner(string id)
    {
        var settlement = MBObjectManager.Instance?.GetObject<Settlement>(id);
        return settlement?.Town == null ? (false, null) : (true, settlement.OwnerClan);
    }

    private static bool Refuse(object? peer, string? kind, string town, Clan clan, string owner, string reply)
    {
        SubModule.Log("refused " + (kind ?? "a request") + " for " + town + " from clan " + clan.StringId + "; the town belongs to " + owner + ".");
        Coop.Reply(peer, reply);
        return false;
    }

    // IG's copy to all walks every entry in its settings, which on the server holds every player's towns.
    // The request becomes a CopySpecific (the shape CoopModPatch's own client sends: SettlementId is the
    // source, StrArg the target ids joined by ';') listing only the requester clan's towns or castles.
    private static bool RewriteCopyAll(object request, object? peer, Settlement source, string town, Clan clan)
    {
        var mode = Coop.Member(request, "StrArg") as string;
        var targets = OwnerRules.CopyAllTargets(
            Settlement.All.Where(s => s.Town != null && (s.IsTown || s.IsCastle)).Select(s => (s.StringId, s.IsCastle, (object?)s.OwnerClan)),
            source.StringId, mode, clan);
        var what = mode == "castles" ? "castles" : "towns";
        if (targets.Count == 0)
        {
            Coop.Reply(peer, "IG: your clan has no other " + what + " to copy the settings of " + town + " to.");
            return false;
        }
        try
        {
            if (Coop.SetMember(request, "StrArg", string.Join(";", targets)) && Coop.SetMember(request, "Kind", "CopySpecific"))
                return true;
        }
        catch { }
        SubModule.Log("could not turn a copy to all " + what + " request into a copy to " + targets.Count + " " + what + "; refused it.");
        Coop.Reply(peer, "IG: copy to all is not available on this server. Use copy to specific garrisons instead.");
        return false;
    }

    private static void UnknownPlayer(object? peer)
    {
        if (peer == null)
        {
            if (_nullPeerLogged)
                return;
            _nullPeerLogged = true;
        }
        else
        {
            if (LoggedPeers.TryGetValue(peer, out _))
                return;
            LoggedPeers.Add(peer, new object());
        }
        SubModule.Log("refused an Improved Garrisons request from " + (peer?.ToString() ?? "an unknown sender") + "; it is not a known player with a clan.");
    }
}

// Whether this process is the Coop dedicated server: the game's own flag, or Coop's Common.ModInformation.IsServer.
internal static class Server
{
    private static bool _known;

    public static bool IsDedicated
    {
        get
        {
            if (_known)
                return true;
            try
            {
                var coopServer = AccessTools.TypeByName("Common.ModInformation")?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is true;
                _known = GameNetwork.IsDedicatedServer || coopServer;
            }
            catch { }
            return _known;
        }
    }
}

internal enum ClanLookup { Found, NoClan, Unreachable }

// Coop's player registry and CoopModPatch's reply, read by reflection so this module never needs their DLLs.
internal static class Coop
{
    /// <summary>Sets a public or private property or field of an object. False when it is not there.</summary>
    public static bool SetMember(object target, string name, object? value)
    {
        var type = target.GetType();
        var property = AccessTools.Property(type, name);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            return true;
        }
        var field = AccessTools.Field(type, name);
        if (field == null)
            return false;
        field.SetValue(target, value);
        return true;
    }

    private const uint Red = 0xFFD32F2C;

    /// <summary>A public or private field or property of an object, or null.</summary>
    public static object? Member(object target, string name)
    {
        var type = target.GetType();
        var property = AccessTools.Property(type, name);
        if (property != null)
            return property.GetValue(target);
        return AccessTools.Field(type, name)?.GetValue(target);
    }

    /// <summary>
    /// The clan of the player behind a peer: Coop's IPlayerManager.TryGetPlayer(peer) gives the player,
    /// its ClanId goes through Coop's IObjectManager (or the campaign's clans by id).
    /// Unreachable means Coop's player manager itself could not be found (reflection); NoClan means the
    /// peer is not a known player or that player has no clan.
    /// </summary>
    public static ClanLookup ClanOf(object? peer, out Clan? clan)
    {
        clan = null;
        var players = Resolve("GameInterface.Services.Players.IPlayerManager");
        if (players == null)
            return ClanLookup.Unreachable;
        // The TryGetPlayer(NetPeer, out Player) overload; Coop also has others (by controller id).
        var tryGetPlayer = Methods(players).FirstOrDefault(method => method.Name == "TryGetPlayer" && method.GetParameters() is { Length: 2 } p &&
                                                                     p[0].ParameterType.FullName == "LiteNetLib.NetPeer" && p[1].ParameterType.IsByRef);
        if (tryGetPlayer == null)
            return ClanLookup.Unreachable;
        if (peer == null || !tryGetPlayer.GetParameters()[0].ParameterType.IsInstanceOfType(peer))
            return ClanLookup.NoClan;
        var arguments = new object?[] { peer, null };
        if (tryGetPlayer.Invoke(players, arguments) is not true || arguments[1] is not { } player)
            return ClanLookup.NoClan;
        if (Member(player, "ClanId") is not string clanId || clanId.Length == 0)
            return ClanLookup.NoClan;
        clan = FindClan(clanId);
        return clan == null ? ClanLookup.NoClan : ClanLookup.Found;
    }

    private static Clan? FindClan(string clanId)
    {

        var objects = Resolve("GameInterface.Services.ObjectManager.IObjectManager");
        if (objects != null)
        {
            var tryGetObject = Methods(objects).FirstOrDefault(method => method.Name == "TryGetObject" && method.IsGenericMethodDefinition &&
                                                                         method.GetParameters() is { Length: 2 } p && p[0].ParameterType == typeof(string));
            if (tryGetObject != null)
            {
                var lookup = new object?[] { clanId, null };
                if (tryGetObject.MakeGenericMethod(typeof(Clan)).Invoke(objects, lookup) is true && lookup[1] is Clan found)
                    return found;
            }
        }
        return Clan.All.FirstOrDefault(clan => clan.StringId == clanId);
    }

    /// <summary>Tells the player why, through CoopModPatch's own IGBridge.Ack(peer, text, color). Best effort.</summary>
    public static void Reply(object? peer, string text)
    {
        try
        {
            var bridge = AccessTools.TypeByName("CoopModPatch.Adapters.ImprovedGarrisons.IGBridge");
            var ack = bridge == null ? null : AccessTools.GetDeclaredMethods(bridge)
                .FirstOrDefault(method => method.Name == "Ack" && method.IsStatic && method.GetParameters() is { Length: 3 } p &&
                                          p[1].ParameterType == typeof(string) && p[2].ParameterType == typeof(uint));
            ack?.Invoke(null, new[] { peer, text, (object)Red });
        }
        catch { }
    }

    private static IEnumerable<MethodInfo> Methods(object service) =>
        service.GetType().GetInterfaces().Append(service.GetType()).SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public));

    /// <summary>One of Coop's services from GameInterface.ContainerProvider.TryResolve, or null.</summary>
    private static object? Resolve(string interfaceName)
    {
        var provider = AccessTools.TypeByName("GameInterface.ContainerProvider");
        var type = AccessTools.TypeByName(interfaceName);
        var tryResolve = provider?.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "TryResolve" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
        if (tryResolve == null || type == null)
            return null;
        var arguments = new object?[] { null };
        return tryResolve.MakeGenericMethod(type).Invoke(null, arguments) is true ? arguments[0] : null;
    }
}
