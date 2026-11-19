using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace RealmOfThronesCoopCompat
{
    // Players' PCs only. Realm of Thrones' SubModule.OnAfterGameInitializationFinished makes a
    // special hero and makes it the leader of every bandit clan, on every start and load. On a
    // player's PC in the server's world that hero exists only there. A bool prefix decides with
    // JoinRules.BanditLeaderGate. The server's world is told from the throwaway character creation
    // campaign of a join by SandBoxGameManager.LoadingSavedGame. Realm of Thrones' SubModule is an
    // ordinary engine loaded SubModule on players' PCs, so its type is found by name once ROT.dll
    // is loaded; nothing names it at compile time. Never installed on the server.
    internal static class ClientLoadGate
    {
        private const string RotSubModuleType = "ROT.SubModule";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _tried;

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            PatchGame();
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly, false);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly, false);
            // A type that does not load yet is asked for again at campaign start (still before
            // OnAfterGameInitializationFinished runs), and only then given up on.
            Coop.CampaignBegan += () =>
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    TryPatch(assembly, true);
            };
        }

        private static void TryPatch(Assembly assembly, bool last)
        {
            try
            {
                if (_tried || assembly == null || assembly.GetName().Name != "ROT")
                    return;
                Type type;
                try { type = assembly.GetType(RotSubModuleType, false); }
                catch { type = null; }
                if (type == null && !last)
                    return;
                lock (Gate)
                {
                    if (_tried)
                        return;
                    _tried = true;
                }
                if (type == null)
                {
                    SubModule.Log("Load gate: a campaign started but ROT.SubModule does not load; Realm of Thrones' bandit leader code is not checked.");
                    return;
                }
                var target = type.GetMethod("OnAfterGameInitializationFinished", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(Game), typeof(object) }, null);
                if (target == null)
                {
                    SubModule.Log("Load gate: ROT.SubModule.OnAfterGameInitializationFinished not found; Realm of Thrones' bandit leader code runs as it is.");
                    return;
                }
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(ClientLoadGate), nameof(Prefix)));
                SubModule.Log("Load gate: ROT.SubModule.OnAfterGameInitializationFinished is checked on this PC in a Coop session.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Load gate could not hook Realm of Thrones' SubModule: " + ex.Message);
            }
        }

        // The game's own methods, there from the start: the culture list of character creation
        // (Realm of Thrones' creation behavior adds every main culture through it,
        // ROTCharacterCreationCampaignBehavior.InitializeCharacterCreationCultures) and the map
        // update sweep of a loaded campaign. Each its own try and line.
        private static void PatchGame()
        {
            try
            {
                var add = typeof(TaleWorlds.CampaignSystem.CharacterCreationContent.CharacterCreationContent).GetMethod("AddCharacterCreationCulture",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(CultureObject), typeof(int), typeof(int) }, null);
                if (add == null)
                    SubModule.Log("Load gate: CharacterCreationContent.AddCharacterCreationCulture not found; the White Walker start stays offered.");
                else
                {
                    _harmony.Patch(add, prefix: new HarmonyMethod(typeof(ClientLoadGate), nameof(CulturePrefix)));
                    SubModule.Log("Load gate: character creation in a Coop session does not offer the White Walker start.");
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Load gate could not hook character creation's cultures: " + ex.Message);
            }
            try
            {
                var sweep = typeof(Campaign).GetMethod("CheckMapUpdate", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (sweep == null)
                    SubModule.Log("Load gate: Campaign.CheckMapUpdate not found; a join may re-place every party on this PC.");
                else
                {
                    _harmony.Patch(sweep, prefix: new HarmonyMethod(typeof(ClientLoadGate), nameof(MapSweepPrefix)));
                    SubModule.Log("Load gate: the map update sweep is skipped in the server's world (it stores no map CRCs).");
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Load gate could not hook Campaign.CheckMapUpdate: " + ex.Message);
            }
        }

        private static bool _cultureLogged;

        private static bool CulturePrefix(CultureObject __0)
        {
            try
            {
                if (!JoinRules.HidesCulture(Coop.OnPlayerPc, (Game.Current?.GameManager as SandBoxGameManager)?.LoadingSavedGame, __0?.StringId))
                    return true;
                if (!_cultureLogged)
                {
                    _cultureLogged = true;
                    SubModule.Log("Load gate: character creation does not offer the " + __0.StringId + " culture (the White Walker and wight start is off in co-op).");
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool _sweepLogged;

        private static bool MapSweepPrefix(Campaign __instance)
        {
            try
            {
                if (!JoinRules.SkipsMapSweep(Coop.OnPlayerPc, (Game.Current?.GameManager as SandBoxGameManager)?.LoadingSavedGame))
                    return true;
                if (!_sweepLogged)
                {
                    _sweepLogged = true;
                    var stored = typeof(Campaign).GetField("_campaignMapSceneXmlCrc", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
                    SubModule.Log("Load gate: the map update sweep is skipped in the server's world (stored scene CRC " + (stored ?? "unreadable")
                        + "); parties keep the places the server gave them.");
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool Prefix(Game __0)
        {
            try
            {
                if (!(__0?.GameType is Campaign) || !Coop.OnPlayerPc)
                    return true;
                var manager = __0.GameManager as SandBoxGameManager;
                var decision = JoinRules.BanditLeaderGate(true, manager?.LoadingSavedGame, JoinRules.EveryClanLed(BanditClans()), out var line);
                if (line != null)
                    SubModule.Log(line);
                return decision == JoinRules.Gate.Run;
            }
            catch (Exception ex)
            {
                SubModule.Log("Load gate: could not check the bandit leaders, so Realm of Thrones' code runs: " + ex.Message);
                return true;
            }
        }

        // Each bandit clan with its leader and whether the leader is alive.
        internal static (string Clan, string Leader, bool Alive)[] BanditClans() =>
            Clan.BanditFactions.Where(clan => clan != null)
                .Select(clan => (clan.StringId, clan.Leader?.StringId, clan.Leader != null && clan.Leader.IsAlive)).ToArray();
    }
}
