using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RealmOfThronesCoopCompat
{
    // Log only, for the first client join tests, on the server and on players' PCs (JoinRules
    // holds the lines, worded alike on both sides so the two logs compare line by line):
    // - when a campaign starts (new or loaded): the race table and the active modules;
    // - on a player's PC: the size of the hero package Coop sends at the end of character creation,
    //   with the hero's culture and race, or the exception that stopped it (postfix and finalizer on
    //   Coop's internal HeroInterface.PackageMainHero, found by name);
    // - when Coop says the world is ready (its CampaignReady message: on the server after the load,
    //   on a player's PC when the joined world reaches the map): the date and counts and the bandit
    //   leaders, and on the server the White Walkers' war and the map CRCs the campaign stores.
    // Each line in its own try/catch; nothing here changes the game.
    internal static class JoinProbe
    {
        private const string HeroInterfaceType = "GameInterface.Services.Heroes.Interfaces.HeroInterface";
        private const string CampaignReadyType = "GameInterface.Services.GameState.Messages.CampaignReady";

        private static readonly object Gate = new object();
        // Coop's broker holds subscribers weakly, so the listener stays rooted here.
        private static readonly ReadyListener Listener = new ReadyListener();
        private static Harmony _harmony;
        private static bool _server;
        private static bool _subscribed;
        private static bool _packagePatched;

        public static void Install(bool server)
        {
            _server = server;
            _harmony = new Harmony(MapRedirect.HarmonyId);
            // Only Coop's own two assemblies are worth a look; any other load is ignored.
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
            {
                string name;
                try { name = e.LoadedAssembly.GetName().Name; }
                catch { return; }
                if (name == "Common" || name == "GameInterface")
                    TryHook(false);
            };
            // A type can fail to load while Coop's assemblies come in one by one (boot test 6:
            // CampaignReady before Common was resolvable), so it asks again when the session starts,
            // and gives up only when a campaign has started and a name is still missing.
            Coop.SessionChanged += started =>
            {
                if (!started)
                    return;
                _session = System.Diagnostics.Stopwatch.StartNew();
                Line(() => "Join probe: Coop session started; Coop's blocking game thread wait is " + CoopPatience.CurrentWaitText() + ".");
                TryHook(false);
            };
            Coop.CampaignBegan += () => TryHook(true);
            TryHook(false);
        }

        // A campaign started, new or loaded.
        // Seconds since the Coop session started, for the timing lines (null before it started).
        private static System.Diagnostics.Stopwatch _session;

        private static double? SinceSession() => _session?.Elapsed.TotalSeconds;

        public static void OnCampaign(Game game, bool loaded)
        {
            // A player's single player campaign is none of the probe's business.
            if (!(game?.GameType is Campaign) || (!_server && !Coop.InSession))
                return;
            Line(() => "Join probe: a campaign " + (loaded ? "loaded" : "started new")
                + (_server ? "" : (loaded ? " (the server's world)" : " (the character creation campaign)")) + JoinRules.SinceSession(SinceSession()) + ".");
            Line(() =>
            {
                var races = FaceGen.GetRaceNames();
                return JoinRules.RacesLine(races);
            });
            Line(() => JoinRules.ModulesLine(SubModule.ActiveModules()));
        }

        private static void TryHook(bool last)
        {
            try
            {
                if (_subscribed && (_server || _packagePatched))
                    return;
                lock (Gate)
                {
                    if (!_subscribed)
                        _subscribed = Subscribe();
                    if (!_server && !_packagePatched)
                        _packagePatched = PatchPackage();
                    // A campaign has started and a name is still missing: Coop changed, so the
                    // probe gives up on it with one line.
                    if (last && (!_subscribed || (!_server && !_packagePatched)))
                    {
                        SubModule.Log("Join probe: a campaign started but " + (!_subscribed ? "Coop's CampaignReady message" : "Coop's HeroInterface") + " was not found; the probe gives up on it.");
                        _subscribed = true;
                        _packagePatched = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _subscribed = true;
                _packagePatched = true;
                SubModule.Log("Join probe could not hook Coop: " + ex.Message);
            }
        }

        // Subscribes the listener to Coop's CampaignReady once Common and GameInterface are loaded.
        private static bool Subscribe()
        {
            // Common first: CampaignReady builds on its types and does not load without it.
            var broker = Coop.FindType("Common.Messaging.MessageBroker");
            var payload = broker == null ? null : Coop.FindType("Common.Messaging.MessagePayload`1");
            var ready = payload == null ? null : Coop.FindType(CampaignReadyType);
            if (broker == null || ready == null || payload == null)
                return false;
            var instance = broker.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            var subscribe = broker.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == "Subscribe" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            // The broker's singleton may not be built yet: asked again later, and given up on at
            // campaign start like the rest.
            if (instance == null || subscribe == null)
                return false;
            var payloadType = payload.MakeGenericType(ready);
            var handler = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(payloadType), Listener,
                typeof(ReadyListener).GetMethod(nameof(ReadyListener.OnReady)).MakeGenericMethod(payloadType));
            subscribe.MakeGenericMethod(ready).Invoke(instance, new object[] { handler });
            SubModule.Log("Join probe: listening for Coop's CampaignReady.");
            return true;
        }

        private static bool PatchPackage()
        {
            var type = Coop.FindType(HeroInterfaceType);
            if (type == null)
                return false;
            var target = type.GetMethod("PackageMainHero", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (target == null)
            {
                SubModule.Log("Join probe: HeroInterface.PackageMainHero not found; no hero package line.");
                return true;
            }
            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(JoinProbe), nameof(PackagePostfix)),
                finalizer: new HarmonyMethod(typeof(JoinProbe), nameof(PackageFinalizer)));
            SubModule.Log("Join probe: watching Coop's hero package.");
            return true;
        }

        private static void PackagePostfix(byte[] __result)
        {
            Line(() =>
            {
                var hero = Hero.MainHero;
                return JoinRules.PackageLine(__result?.Length ?? 0, hero?.Culture?.StringId, hero?.CharacterObject?.Race ?? -1);
            });
        }

        // Void, so an exception goes on as before; it is only logged.
        private static void PackageFinalizer(Exception __exception)
        {
            if (__exception != null)
                Line(() => "!!! Join probe: Coop could not package the hero: " + __exception.GetType().Name + ": " + CrashTraceRules.Shorten(__exception.Message));
        }

        internal static void OnWorldReady()
        {
            var game = Game.Current;
            if (!(game?.GameType is Campaign campaign))
                return;
            if (!_server && (game.GameManager as SandBoxGameManager)?.LoadingSavedGame != true)
            {
                SubModule.Log("Join probe: the character creation campaign reached the map" + JoinRules.SinceSession(SinceSession()) + "; its world is not counted.");
                return;
            }
            if (!_server)
                SubModule.Log("Join probe: Coop's CampaignReady in the server's world" + JoinRules.SinceSession(SinceSession()) + ".");
            Line(() =>
            {
                var now = CampaignTime.Now;
                return JoinRules.WorldLine(now.ToString(), now.GetYear, Hero.AllAliveHeroes.Count, Clan.All.Count, Kingdom.All.Count, Settlement.All.Count);
            });
            Line(() => JoinRules.BanditLine(ClientLoadGate.BanditClans()));
            if (!_server)
                return;
            Line(() =>
            {
                var walkers = Clan.All.FirstOrDefault(clan => clan?.StringId == JoinRules.WhiteWalkerClanId)
                    ?? Clan.All.FirstOrDefault(clan => clan?.Culture?.StringId == JoinRules.WhiteWalkerCulture);
                var npc = Clan.All.FirstOrDefault(clan => clan?.StringId == JoinRules.SampleNpcClanId);
                bool? atWar = walkers == null || npc == null ? (bool?)null : FactionManager.IsAtWarAgainstFaction(walkers, npc);
                return JoinRules.WalkerWarLine(walkers?.StringId, JoinRules.SampleNpcClanId + (npc == null ? " (not found)" : ""), atWar);
            });
            Line(() =>
            {
                var xml = typeof(Campaign).GetField("_campaignMapSceneXmlCrc", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(campaign);
                var navMesh = typeof(Campaign).GetField("_campaignMapSceneNavigationMeshCrc", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(campaign);
                return "Join probe: stored map CRCs: scene " + (xml ?? "unreadable") + ", navmesh " + (navMesh ?? "unreadable") + ".";
            });
        }

        private static void Line(Func<string> text)
        {
            try { SubModule.Log(text()); }
            catch (Exception ex) { SubModule.Log("Join probe: a line failed: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // The world lines walk the campaign's lists, so they run on the game thread: at once only
        // when Coop's GameThread says this is it (both of Coop's publishers are on it today: the
        // server's host loop and the map screen postfix on a player's PC), else queued with Coop's
        // own GameThread.RunSafe, else at our next application tick. The path is logged once.
        private static Action _pending;
        private static bool _pathLogged;

        private static void OnGameThread(Action action)
        {
            bool? onIt = null;
            MethodInfo runSafe = null;
            try
            {
                var gameThread = Coop.FindType("Common.GameThread");
                var instance = gameThread?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                onIt = instance == null ? (bool?)null : gameThread.GetProperty("IsGameThread")?.GetValue(instance, null) as bool?;
                runSafe = gameThread?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "RunSafe" && m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == typeof(Action));
            }
            catch { }
            if (onIt == true)
            {
                LogPath("on the game thread, at once");
                action();
            }
            else if (runSafe != null)
            {
                LogPath("queued with Coop's GameThread.RunSafe");
                runSafe.Invoke(null, new object[] { action, false, "Realm of Thrones join probe", null, null });
            }
            else
            {
                LogPath("at this module's next application tick");
                _pending = action;
            }
        }

        // Called from the SubModule's tick, which is the game thread.
        public static void OnApplicationTick()
        {
            var pending = Interlocked.Exchange(ref _pending, null);
            pending?.Invoke();
        }

        private static void LogPath(string path)
        {
            if (_pathLogged)
                return;
            _pathLogged = true;
            SubModule.Log("Join probe: the world ready lines run " + path + ".");
        }

        // An instance method, because Coop's broker drops a subscriber whose target is gone.
        private sealed class ReadyListener
        {
            public void OnReady<T>(T payload)
            {
                try
                {
                    // The hook stays after a session ends; a later single player campaign is not ours.
                    if (!_server && !Coop.InSession)
                        return;
                    OnGameThread(() =>
                    {
                        try { OnWorldReady(); }
                        catch (Exception ex) { SubModule.Log("Join probe: world ready lines failed: " + ex.Message); }
                    });
                }
                catch (Exception ex)
                {
                    SubModule.Log("Join probe: world ready lines failed: " + ex.Message);
                }
            }
        }
    }
}
