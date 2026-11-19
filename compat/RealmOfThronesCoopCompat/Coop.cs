using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace RealmOfThronesCoopCompat
{
    // Reads Coop's own state, adapted from Burning Empires Coop Compat:
    // - GameInterface.ContainerProvider.TryGetContainer returns true while a co-op session runs
    //   (host or join), so the public method is the stable way to ask.
    // - Common.ModInformation.IsServer tells the server from a player.
    // - GameInterface.Services.Heroes.Extensions.HeroExtensions.IsPlayerHero tells a player's hero.
    // - Common.LogicStates.ILogic.RunningState, resolved from the session's container, tells when
    //   the shared world is live: on the server once it handled CampaignReady, on a player's PC once
    //   the join save loaded (false in the throwaway character creation campaign).
    // Session edges come from postfixes on ContainerProvider.SetContainer and Clear
    // (CoopSessionEdges, installed once Harmony is there); the flags above are read live either way.
    // Plain reflection only, no Harmony, so any code path may ask, also before Harmony is found on
    // the server. While a Coop module is active it keeps looking until Coop's container and server
    // flag are loaded, and says so loudly once if a campaign starts without them (then there is no
    // session as far as this module can tell). Coop's assemblies present without those names and
    // without a Coop module listed are taken as a running session on a player's PC.
    internal static class Coop
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        private static readonly object Sync = new object();
        private static bool _resolved;
        private static bool _modulesLoaded;
        private static bool _coopLoaded;
        private static bool _dedicatedServer;
        private static MethodInfo _tryGetContainer;
        private static PropertyInfo _isServer;
        private static MethodInfo _isPlayerHero;
        private static MethodInfo _tryResolveLogic;
        private static PropertyInfo _runningState;
        private static bool _serverFlagReported;
        private static bool _missingWarned;
        private static bool _playerHeroReported;
        private static bool? _dedicatedProcess;
        private static int _dedicatedCheckedAt;
        private static bool? _coopModuleActive;

        // Raised with true when Coop sets its session container and false when it clears it.
        public static event Action<bool> SessionChanged;

        // True once the edge postfixes are in; without them a caller polls InSession.
        public static bool SessionEdgesHooked { get; internal set; }

        public static bool InSession
        {
            get
            {
                Resolve();
                if (!_coopLoaded)
                    return false;
                if (_tryGetContainer == null)
                    return true;
                try { return (bool)_tryGetContainer.Invoke(null, new object[] { null }); }
                catch { return true; }
            }
        }

        // On the dedicated server process always true; a missing or false flag in a session is logged
        // once as an error there (JoinRules.IsServer).
        public static bool IsServer
        {
            get
            {
                Resolve();
                bool? flag = null;
                if (_isServer != null)
                {
                    try { flag = (bool)_isServer.GetValue(null, null); }
                    catch { }
                }
                var dedicated = IsDedicatedServerProcess();
                var result = JoinRules.IsServer(dedicated, flag, dedicated && flag != true && InSession, out var report);
                if (report && !_serverFlagReported)
                {
                    _serverFlagReported = true;
                    SubModule.Log("!!! Coop's server flag (Common.ModInformation.IsServer) is " + (flag == null ? "missing" : "false")
                        + " on the dedicated server in a session; treating this process as the server.");
                }
                return result;
            }
        }

        // The shared world is live: the server handled CampaignReady, or a player's join save
        // loaded. False without a session or when Coop's logic cannot be read.
        public static bool WorldLive
        {
            get
            {
                if (!InSession || _tryResolveLogic == null || _runningState == null)
                    return false;
                try
                {
                    var args = new object[] { null };
                    return (bool)_tryResolveLogic.Invoke(null, args) && args[0] != null && (bool)_runningState.GetValue(args[0], null);
                }
                catch
                {
                    return false;
                }
            }
        }

        // Called by the edge postfixes.
        internal static void SessionEdge(bool started)
        {
            try
            {
                SubModule.Log("Coop session " + (started ? "started (container set)" : "ended (container cleared)") + ".");
                SessionChanged?.Invoke(started);
            }
            catch (Exception ex)
            {
                SubModule.Log("Coop session edge handler failed: " + ex.Message);
            }
        }

        // Every module's DLLs are loaded by now; without Coop the answer stays "no session".
        public static void ModulesLoaded()
        {
            _modulesLoaded = true;
            Resolve();
        }

        // A campaign starts (new or loaded): look again, and say so once when a Coop module is
        // active but Coop's types are still missing, since every gate then reads "no session".
        public static void CampaignStarted()
        {
            _modulesLoaded = true;
            Resolve();
            // The hooks that look Coop's types up by name try a last time here: when Coop's
            // assemblies first load, a type can fail to load because what it builds on is not
            // resolvable yet (boot test 6: CampaignReady before Common).
            foreach (var handler in CampaignBegan?.GetInvocationList() ?? new Delegate[0])
            {
                try { ((Action)handler)(); }
                catch (Exception ex) { SubModule.Log("A campaign start hook failed: " + ex.Message); }
            }
            if (_coopLoaded || _missingWarned || !CoopModuleActive())
                return;
            _missingWarned = true;
            SubModule.Log("!!! A campaign started with a Coop module active, but Coop's types (GameInterface.ContainerProvider, Common.ModInformation) "
                + "are not loaded, so this module sees no Coop session and its Coop gates stay off.");
        }

        // Raised at each campaign start (new or loaded), for the last lookups by name.
        public static event Action CampaignBegan;

        // A Coop or CoopNightly module is in the active module list. The list does not change, so
        // it is read once.
        private static bool CoopModuleActive()
        {
            if (_coopModuleActive == null)
            {
                try { _coopModuleActive = SubModule.ActiveModules().Any(module => RotRules.CoopModuleIds.Contains(module.Key, StringComparer.OrdinalIgnoreCase)); }
                catch { return false; }
            }
            return _coopModuleActive.Value;
        }

        public static bool OnPlayerPc => JoinRules.OnPlayerPc(InSession, IsServer, IsDedicatedServerProcess());
        public static bool OnServer => InSession && IsServer;

        // The co-op server, also before its session starts: Coop only builds the session when the
        // server loads its save, so the dedicated server is also told by its process.
        public static bool OnServerProcess
        {
            get
            {
                if (OnServer)
                    return true;
                // The process never stops being the server, so only a yes is kept.
                if (!_dedicatedServer)
                    _dedicatedServer = IsDedicatedServerProcess();
                return _dedicatedServer;
            }
        }

        // The dedicated server runs from bin\Win64_Shipping_Server, or has its host type loaded.
        // Asked every frame (the ship budget, the gates), so a yes is kept for good and a no for a
        // second: the folder never changes and the host type can only load later.
        public static bool IsDedicatedServerProcess()
        {
            if (_dedicatedProcess == true)
                return true;
            var now = Environment.TickCount;
            if (_dedicatedProcess == false && unchecked(now - _dedicatedCheckedAt) < 1000)
                return false;
            bool result;
            try
            {
                result = RotRules.IsServerBinFolder(AppDomain.CurrentDomain.BaseDirectory) || FindType("DedicatedServer.CoopServerHost") != null;
            }
            catch
            {
                result = false;
            }
            _dedicatedCheckedAt = now;
            _dedicatedProcess = result;
            return result;
        }

        // True when a co-op player controls this hero. On the server Hero.MainHero is nobody's
        // hero. Coop's own check logs an error for null, so null is answered here. Every server
        // guard rests on this, so on the server a missing or throwing check is loud, counts as a
        // guard failure and has no Hero.MainHero fallback (that would say no for every player).
        public static bool IsPlayerHero(Hero hero)
        {
            if (hero == null || !InSession)
                return false;
            if (_isPlayerHero != null)
            {
                try { return (bool)_isPlayerHero.Invoke(null, new object[] { hero }); }
                catch (Exception ex)
                {
                    if (OnServerProcess)
                    {
                        PlayerHeroCheckFailed("threw " + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message);
                        return false;
                    }
                }
            }
            else if (OnServerProcess)
            {
                PlayerHeroCheckFailed("is not found");
                return false;
            }
            // Hero.MainHero can throw where there is no player character.
            try { return hero == Hero.MainHero; }
            catch { return false; }
        }

        private static void PlayerHeroCheckFailed(string what)
        {
            if (_playerHeroReported)
                return;
            _playerHeroReported = true;
            SubModule.Log("!!! Coop's player hero check (GameInterface.Services.Heroes.Extensions.HeroExtensions.IsPlayerHero) " + what
                + ", so no hero counts as a player's on this server and the server guards cannot keep players out.");
            try { RotHost.AddGuardFailures(1); }
            catch { }
        }

        public static bool IsPlayerClan(Clan clan) => clan != null && IsPlayerHero(clan.Leader);

        internal static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }
            return null;
        }

        private static void Resolve()
        {
            if (_resolved)
                return;
            lock (Sync)
            {
                if (_resolved)
                    return;
                try
                {
                    ResolveNow();
                }
                catch (Exception ex)
                {
                    _resolved = true;
                    SubModule.Log("Could not read Coop's session state (" + ex.GetType().Name + ": " + ex.Message + "); assuming no session.");
                }
            }
        }

        private static void ResolveNow()
        {
            Type container = null, info = null, heroExtensions = null, logic = null;
            var coop = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = assembly.GetName().Name ?? ""; }
                catch { continue; }
                try
                {
                    if (name == "GameInterface")
                    {
                        coop = true;
                        container = container ?? assembly.GetType("GameInterface.ContainerProvider", false);
                        heroExtensions = heroExtensions ?? assembly.GetType("GameInterface.Services.Heroes.Extensions.HeroExtensions", false);
                    }
                    else if (name == "Coop.Core" || (name == "Coop" && assembly.GetType("Coop.CoopMod", false) != null))
                    {
                        coop = true;
                    }
                    else if (name == "Common")
                    {
                        info = info ?? assembly.GetType("Common.ModInformation", false);
                        logic = logic ?? assembly.GetType("Common.LogicStates.ILogic", false);
                    }
                }
                catch { }
            }

            // Coop's assemblies load one by one and late: on a player's PC only Coop.dll is in its
            // SubModule.xml, and the nightly server's Coop module has no SubModule at all. So while
            // a Coop module is active (or on the server) this looks again until both the container
            // and the server flag are there (JoinRules.ResolveCoop).
            var server = IsDedicatedServerProcess();
            var state = JoinRules.ResolveCoop(coop, container != null, info != null, server, _modulesLoaded, CoopModuleActive());
            if (state != JoinRules.CoopState.Ready)
            {
                if (state == JoinRules.CoopState.NoCoop)
                    _resolved = true;
                return;
            }

            _coopLoaded = true;
            _tryGetContainer = container?.GetMethod("TryGetContainer", PublicStatic);
            _isServer = info?.GetProperty("IsServer", PublicStatic);
            _isPlayerHero = heroExtensions?.GetMethod("IsPlayerHero", PublicStatic, null, new[] { typeof(Hero) }, null);
            try
            {
                var tryResolve = container?.GetMethod("TryResolve", PublicStatic);
                _tryResolveLogic = tryResolve != null && tryResolve.IsGenericMethodDefinition && logic != null ? tryResolve.MakeGenericMethod(logic) : null;
                _runningState = logic?.GetProperty("RunningState");
            }
            catch { }
            if (_tryGetContainer == null || _isServer == null)
                SubModule.Log(server
                    ? "!!! Could not read Coop's session state on the dedicated server; treating this process as the server in a session."
                    : "Could not read Coop's session state; assuming a player's PC in a session.");
            if (_tryResolveLogic == null || _runningState == null)
                SubModule.Log("Could not read Coop's logic state, so WorldLive stays false.");
            _resolved = true;
            if (server && _isPlayerHero == null)
                PlayerHeroCheckFailed("is not found");
        }
    }
}
