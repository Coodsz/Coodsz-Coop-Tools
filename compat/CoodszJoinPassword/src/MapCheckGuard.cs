using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;

namespace Coodsz.MapCheck
{
    // Bannerlord 1.4.8 runs Campaign.CheckMapUpdate while it loads a save whose map scene checksums differ
    // from this PC's, which a Coop client loading the dedicated server's save most likely always hits. It
    // works out cached values, then walks every settlement, map event, army and party and reads their move
    // targets with no null check. A NullReferenceException there crashes the client before the map (Burning
    // Empires reports CR-EH6KT, CR-A9XVH, CR-PM83C, CR-8ZXDY and CR-XG5NC, two players and two saves). On a
    // Coop client the server runs the campaign and sends positions, so a broken object is skipped and logged,
    // and a null anywhere else in the check ends only the check, logged with its full stack. Only a
    // NullReferenceException, only inside CheckMapUpdate and only while a Coop client is connected; anything
    // else still reaches the game.
    // The per object finalizers are best effort: a caller the JIT compiled with one of them inlined skips the
    // Harmony detour. The finalizer on CheckMapUpdate itself is the real safety net. The state and finalizer
    // bodies are in MapCheckGuardRules.cs (MapCheckSession), so the tests run them through Harmony too.
    internal static class MapCheckGuard
    {
        private static readonly CoopClientLink Client = new CoopClientLink();
        private static Action<string> _log;
        private static bool _missingFieldNoted;

        internal static void Install(Harmony harmony, Action<string> log)
        {
            MethodInfo check = AccessTools.Method(typeof(Campaign), "CheckMapUpdate", Type.EmptyTypes);
            MethodInfo[] targets =
            {
                AccessTools.Method(typeof(Settlement), nameof(Settlement.CheckPositionsForMapChangeAndUpdateIfNeeded), Type.EmptyTypes),
                AccessTools.Method(typeof(MapEvent), nameof(MapEvent.CheckPositionsForMapChangeAndUpdateIfNeeded), Type.EmptyTypes),
                AccessTools.Method(typeof(Army), nameof(Army.CheckPositionsForMapChangeAndUpdateIfNeeded), Type.EmptyTypes),
                AccessTools.Method(typeof(MobileParty), nameof(MobileParty.CheckPositionsForMapChangeAndUpdateIfNeeded), Type.EmptyTypes),
                AccessTools.Method(typeof(MobileParty), nameof(MobileParty.CheckAiForMapChangeAndUpdateIfNeeded), Type.EmptyTypes),
            };
            if (check == null || Array.IndexOf(targets, null) >= 0)
            {
                log("Map check guard not installed: Campaign.CheckMapUpdate or one of its calls was not found; the game version may differ.");
                return;
            }

            _log = log;
            MapCheckSession.Log = log;
            MapCheckSession.GuardOn = GuardOn;
            harmony.Patch(check, prefix: new HarmonyMethod(typeof(MapCheckGuard), nameof(BeforeMapCheck)),
                finalizer: new HarmonyMethod(typeof(MapCheckGuard), nameof(AfterMapCheck)));
            var skip = new HarmonyMethod(typeof(MapCheckGuard), nameof(SkipBroken));
            foreach (MethodInfo target in targets) harmony.Patch(target, finalizer: skip);
            log(MapCheckGuardRules.InstalledLine);
        }

        /// <summary>The Join Password module's prefix on CoopClient.Start hands over the client it starts.</summary>
        internal static void ClientStarting(object client) => Client.Started(client);

        /// <summary>Its prefix on CoopNetworkBase.Dispose drops that client, since a local shutdown leaves isConnected set.</summary>
        internal static void ClientEnding(object client) => Client.Ended(client);

        private static void BeforeMapCheck(Campaign __instance)
        {
            MapCheckSession.Enter();
            LogMapCrc(__instance);
        }

        // One line per load on a Coop client: whether the save's map CRCs match this game's map. Only a
        // mismatch runs the rest of CheckMapUpdate, so the log shows why the check ran at all.
        private static void LogMapCrc(Campaign campaign)
        {
            try
            {
                if (campaign?.MapSceneWrapper == null || !GuardOn()) return;
                uint mapXml = campaign.MapSceneWrapper.GetSceneXmlCrc();
                uint mapNavMesh = campaign.MapSceneWrapper.GetSceneNavigationMeshCrc();
                uint savedXml = Traverse.Create(campaign).Field<uint>("_campaignMapSceneXmlCrc").Value;
                uint savedNavMesh = Traverse.Create(campaign).Field<uint>("_campaignMapSceneNavigationMeshCrc").Value;
                _log?.Invoke(MapCheckGuardRules.CrcLine(savedXml, mapXml, savedNavMesh, mapNavMesh));
            }
            catch (Exception ex)
            {
                _log?.Invoke("Map check: could not read the map CRCs: " + ex.Message);
            }
        }

        // Harmony finalizer on CheckMapUpdate itself, the real safety net. A null outside the per object calls
        // (CalculateCachedValues, the map scene, another mod's patch) still ends the check, so it is swallowed
        // here too and logged in full. Returning null swallows the exception, returning it lets it through.
        private static Exception AfterMapCheck(Exception __exception) => MapCheckSession.Leave(__exception);

        // Harmony finalizer on each per object call (best effort, see above).
        private static Exception SkipBroken(Exception __exception, object __instance, MethodBase __originalMethod) =>
            MapCheckSession.Skip(__exception, () => Describe(__instance, __originalMethod));

        // A Coop client right now: not a dedicated server or a Coop server, and the client this game started
        // is connected. A failed or refused join leaves it disconnected, so a later single player load is left alone.
        private static bool GuardOn()
        {
            if (GameNetwork.IsDedicatedServer || Coodsz.EnlistGuard.TaomEnlistGuard.CoopServer()) return false;
            bool connected = Client.Connected();
            if (!connected && !_missingFieldNoted && Client.HasClient && Client.FieldMissing())
            {
                _missingFieldNoted = true;
                _log?.Invoke("Map check guard is off: Coop's CoopClient." + CoopClientLink.ConnectedField + " was not found, Coop may have changed.");
            }
            return MapCheckGuardRules.Active(false, false, connected, inMapCheck: true);
        }

        private static string Describe(object instance, MethodBase method)
        {
            string where = method?.DeclaringType?.Name + "." + method?.Name;
            try
            {
                switch (instance)
                {
                    case MobileParty party:
                        // The targets are read after the game's own CacheAiBehaviorPartyBase ran, as the crash saw them.
                        string behavior = party.DefaultBehavior.ToString();
                        string missing = method?.Name != nameof(MobileParty.CheckAiForMapChangeAndUpdateIfNeeded) ? null
                            : MapCheckGuardRules.MissingTarget(behavior, party.Ai != null, party.MoveTargetParty != null, party.TargetParty != null,
                                party.Ai?.AiBehaviorPartyBase != null, party.Ai?.AiBehaviorPartyBase?.MobileParty != null, party.TargetSettlement != null);
                        return MapCheckGuardRules.PartyLine(where, party.StringId, behavior, missing);
                    case Army army:
                        return MapCheckGuardRules.ObjectLine(where, "army", "led by " + (army.LeaderParty?.StringId ?? "nobody")
                            + " of " + (army.Kingdom?.StringId ?? "no kingdom"));
                    case MapEvent mapEvent:
                        return MapCheckGuardRules.ObjectLine(where, "map event", mapEvent.StringId);
                    case Settlement settlement:
                        return MapCheckGuardRules.ObjectLine(where, "settlement", settlement.StringId);
                }
            }
            catch (Exception e)
            {
                return MapCheckGuardRules.ObjectLine(where, "object", "(could not be read: " + e.GetType().Name + ")");
            }

            return MapCheckGuardRules.ObjectLine(where, "object", instance?.GetType().Name);
        }
    }
}
