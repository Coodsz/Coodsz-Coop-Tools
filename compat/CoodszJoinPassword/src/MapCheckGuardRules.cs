// Coodsz Join Password: the map check guard's on/off rule, exception filter, log lines, Coop connection check and
// finalizer bodies, without the game, so the launcher tests can run them, also through Harmony
// (tests\BannerlordCoopLauncher.Tests links this file). The engine part is MapCheckGuard.cs.
#nullable enable
using System;
using System.Globalization;
using System.Reflection;

namespace Coodsz.MapCheck
{
    internal static class MapCheckGuardRules
    {
        /// <summary>Lines written per map check; a broken save could hold many bad parties.</summary>
        public const int MaxLines = 50;

        public const string InstalledLine = "Installed: a missing value in the map check no longer stops a Coop client loading the server's save.";

        public static string MoreLine(int max) =>
            "Map check: more than " + max.ToString(CultureInfo.InvariantCulture) + " objects were skipped; only the first "
            + max.ToString(CultureInfo.InvariantCulture) + " are listed.";

        /// <summary>The first skipped object also logs the full exception, so its stack names the method that read the null.</summary>
        public static bool LogsDetail(int caught) => caught == 1;

        public static string DetailLine(Exception exception) =>
            "Map check: the first skipped error in full: " + exception;

        /// <summary>Whether the save's map matches this game's map. Only a mismatch runs the per object checks.</summary>
        public static string CrcLine(uint savedXml, uint mapXml, uint savedNavMesh, uint mapNavMesh) =>
            (savedXml == mapXml && savedNavMesh == mapNavMesh
                ? "Map check: the save's map CRCs match this game's map, so the party checks are skipped"
                : "Map check: the save's map CRCs differ from this game's map, so every party is checked")
            + " (scene " + savedXml.ToString(CultureInfo.InvariantCulture) + " vs " + mapXml.ToString(CultureInfo.InvariantCulture)
            + ", nav mesh " + savedNavMesh.ToString(CultureInfo.InvariantCulture) + " vs " + mapNavMesh.ToString(CultureInfo.InvariantCulture) + ").";

        /// <summary>
        /// A null outside the per object calls (the cached values, the map scene or another mod's patch) ends
        /// CheckMapUpdate itself. The line carries the full exception, so the stack names the real method.
        /// </summary>
        public static string OuterLine(Exception exception) =>
            "Map check: Campaign.CheckMapUpdate stopped on a NullReferenceException and was skipped. The save's map CRC fields were left as they were."
            + " The load goes on; the server runs the campaign. Full error: " + exception;

        /// <summary>
        /// The guard acts only while Campaign.CheckMapUpdate runs in a Coop client's load: never on a dedicated
        /// server or a Coop server, and never in single player, where no Coop client is connected right now.
        /// </summary>
        public static bool Active(bool dedicatedServer, bool coopServer, bool coopClientConnected, bool inMapCheck) =>
            !dedicatedServer && !coopServer && coopClientConnected && inMapCheck;

        /// <summary>
        /// The Harmony finalizer's result: null swallows the exception. Only a NullReferenceException is
        /// swallowed, and only while the guard is active; anything else still reaches the game.
        /// </summary>
        public static Exception? Filter(Exception? exception, bool active) =>
            exception == null || (active && exception is NullReferenceException) ? null : exception;

        /// <summary>
        /// Which move target MobileParty.CheckAiForMapChangeAndUpdateIfNeeded read without a null check for this
        /// DefaultBehavior (Bannerlord 1.4.8), or null when none of them was null.
        /// </summary>
        public static string? MissingTarget(string behavior, bool hasAi, bool hasMoveTargetParty, bool hasTargetParty,
            bool hasAiBehaviorParty, bool hasShortTermTargetParty, bool hasTargetSettlement)
        {
            if (!hasAi) return "Ai is null";
            switch (behavior)
            {
                case "EngageParty":
                case "JoinParty":
                case "EscortParty":
                    return hasMoveTargetParty || hasTargetParty ? null : "MoveTargetParty and TargetParty are null";
                case "GoAroundParty":
                    return hasAiBehaviorParty ? null : "Ai.AiBehaviorPartyBase is null";
                case "FleeToPoint":
                    return hasShortTermTargetParty ? null : "ShortTermTargetParty is null";
                case "FleeToParty":
                    return hasMoveTargetParty ? null : "MoveTargetParty is null";
                case "DefendSettlement":
                    return hasTargetSettlement ? null : "TargetSettlement is null";
                default:
                    return null;
            }
        }

        /// <summary>The line for a party: its id, its DefaultBehavior and the null target where it can tell.</summary>
        public static string PartyLine(string method, string? partyId, string? behavior, string? missing) =>
            "Map check: skipped party " + Id(partyId) + " (" + (string.IsNullOrEmpty(behavior) ? "unknown behavior" : behavior)
            + (missing == null ? "" : ", " + missing) + ") in " + method
            + " after a NullReferenceException. The load goes on; the server runs its AI and sends its position.";

        /// <summary>The line for an army, a map event or a settlement.</summary>
        public static string ObjectLine(string method, string kind, string? id) =>
            "Map check: skipped " + kind + " " + Id(id) + " in " + method
            + " after a NullReferenceException. The load goes on; the server sends its state.";

        public const int MaxIdLength = 200;

        /// <summary>The save's id on one line and at most MaxIdLength characters, since a save can hold any text.</summary>
        public static string Id(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "(no id)";
            string line = id!.Replace('\r', ' ').Replace('\n', ' ');
            return line.Length <= MaxIdLength ? line : line.Substring(0, MaxIdLength - 3) + "...";
        }
    }

    /// <summary>
    /// The Coop client this game started (CoopClient.Start) and whether it is connected right now. A join that
    /// fails or is refused never ends a game, so "Start ran once" would stay true into a later single player
    /// load; the live connection does not. Coop 0.1.6 keeps it in CoopClient's private isConnected field, set in
    /// OnPeerConnected and cleared in OnPeerDisconnected, and the save transfer and load happen while it is true.
    /// </summary>
    internal sealed class CoopClientLink
    {
        public const string ConnectedField = "isConnected";

        private WeakReference? _client;

        public void Started(object? client) => _client = client == null ? null : new WeakReference(client);

        public void Forget() => _client = null;

        /// <summary>
        /// The Coop client was disposed (CoopNetworkBase.Dispose). A local shutdown stops polling without the
        /// disconnect callback, so isConnected can stay true; the link drops that client. Another client is left alone.
        /// </summary>
        public void Ended(object? client)
        {
            if (client != null && ReferenceEquals(_client?.Target, client)) _client = null;
        }

        /// <summary>True only while the client that started is connected; false when none started or the field is gone.</summary>
        public bool Connected() => _client?.Target is object client && ReadConnected(client) == true;

        public bool HasClient => _client?.Target != null;

        /// <summary>A client started but Coop no longer has the isConnected field, so the guard stays off.</summary>
        public bool FieldMissing() => _client?.Target is object client && ReadConnected(client) == null;

        /// <summary>
        /// The client's parameterless Dispose, taken from the class that declares it. Harmony only patches a declared
        /// method: CoopClient inherits Dispose from CoopNetworkBase, and patching the lookup made on CoopClient threw
        /// "You can only patch implemented methods" in the visible join test.
        /// </summary>
        public static MethodInfo? DeclaredDispose(Type? client)
        {
            MethodInfo? found = client?.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return found?.DeclaringType?.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        }

        /// <summary>Coop's isConnected on the client or a base class; null when Coop no longer has that field.</summary>
        public static bool? ReadConnected(object client)
        {
            for (Type? type = client.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField(ConnectedField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool)) return (bool)field.GetValue(client)!;
            }
            return null;
        }
    }

    /// <summary>
    /// The state and finalizer bodies the Harmony patches call, without the game, so the tests can patch stub
    /// methods with the same shapes. GuardOn says whether this is a Coop client right now (no dedicated server,
    /// no Coop server, a connected client); InMapCheck is true only while CheckMapUpdate runs.
    /// </summary>
    internal static class MapCheckSession
    {
        public static bool InMapCheck;
        public static int Caught;
        public static Func<bool> GuardOn = () => false;
        public static Action<string>? Log;

        /// <summary>The prefix on CheckMapUpdate.</summary>
        public static void Enter()
        {
            InMapCheck = true;
            Caught = 0;
        }

        /// <summary>
        /// The finalizer on CheckMapUpdate, the real safety net: a NullReferenceException anywhere in the check
        /// ends only the check. The in-check flag is cleared whatever happened.
        /// </summary>
        public static Exception? Leave(Exception? exception)
        {
            bool active = exception != null && InMapCheck && GuardOn();
            InMapCheck = false;
            Exception? result = MapCheckGuardRules.Filter(exception, active);
            if (exception != null && result == null) Log?.Invoke(MapCheckGuardRules.OuterLine(exception));
            return result;
        }

        /// <summary>The finalizer on each per object call: skips that one object and logs it (capped).</summary>
        public static Exception? Skip(Exception? exception, Func<string> describe)
        {
            if (exception == null) return null;
            Exception? result = MapCheckGuardRules.Filter(exception, InMapCheck && GuardOn());
            if (result != null) return result;
            Caught++;
            if (Caught <= MapCheckGuardRules.MaxLines) Log?.Invoke(describe());
            else if (Caught == MapCheckGuardRules.MaxLines + 1) Log?.Invoke(MapCheckGuardRules.MoreLine(MapCheckGuardRules.MaxLines));
            if (MapCheckGuardRules.LogsDetail(Caught)) Log?.Invoke(MapCheckGuardRules.DetailLine(exception));
            return null;
        }
    }
}
