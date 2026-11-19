using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BurningEmpiresCoopCompat
{
    // Reads Coop's own state:
    // - GameInterface.ContainerProvider.TryGetContainer returns true while a co-op session runs
    //   (host or join). Coop 0.1.6 keeps the scope in _lifetimeScope and later builds in _active,
    //   so the public method is the stable way to ask.
    // - Common.ModInformation.IsServer tells the server from a player.
    // If a future Coop renames these, the patch assumes a running session on a player's PC,
    // which is the safe side.
    internal static class Coop
    {
        private static bool _resolved;
        private static bool _modulesLoaded;
        private static bool _coopLoaded;
        private static bool _dedicatedServer;
        private static MethodInfo _tryGetContainer;
        private static PropertyInfo _isServer;
        private static MethodInfo _isPlayerHero;

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

        public static bool IsServer
        {
            get
            {
                Resolve();
                if (_isServer == null)
                    return false;
                try { return (bool)_isServer.GetValue(null, null); }
                catch { return false; }
            }
        }

        // Every module's DLLs are loaded by now; without Coop the answer stays "no session".
        public static void ModulesLoaded()
        {
            _modulesLoaded = true;
            Resolve();
        }

        public static bool OnPlayerPc => InSession && !IsServer;
        public static bool OnServer => InSession && IsServer;

        // The co-op server, also before its session starts. Coop only builds the session when the
        // server loads its save, but Burning Empires runs its autosave audit and pop up queue from
        // the first application tick, so the dedicated server is also told by its process.
        public static bool OnServerProcess
        {
            get
            {
                if (OnServer)
                    return true;
                // The process never stops being the server, so only a yes is kept.
                if (!_dedicatedServer)
                    _dedicatedServer = FreshCampaign.IsDedicatedServerProcess();
                return _dedicatedServer;
            }
        }

        // True when a co-op player controls this hero. Burning Empires only knows Hero.MainHero,
        // which on the server is nobody's hero. Coop's own check logs an error for null, so null
        // is answered here.
        public static bool IsPlayerHero(Hero hero)
        {
            if (hero == null || !InSession)
                return false;
            Resolve();
            if (_isPlayerHero == null)
                return hero == Hero.MainHero;
            try { return (bool)_isPlayerHero.Invoke(null, new object[] { hero }); }
            catch { return hero == Hero.MainHero; }
        }

        public static bool IsPlayerClan(Clan clan) => clan != null && IsPlayerHero(clan.Leader);

        private static void Resolve()
        {
            if (_resolved)
                return;

            Type container = null, info = null, heroExtensions = null;
            var coop = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = assembly.GetName().Name ?? ""; }
                catch { continue; }
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
                info = info ?? assembly.GetType("Common.ModInformation", false);
            }

            if (!coop)
            {
                // Before the main menu Coop may simply not be loaded yet; look again later.
                if (_modulesLoaded)
                    _resolved = true;
                return;
            }

            _coopLoaded = true;
            _tryGetContainer = container == null ? null : AccessTools.Method(container, "TryGetContainer");
            _isServer = info == null ? null : AccessTools.Property(info, "IsServer");
            _isPlayerHero = heroExtensions == null ? null : AccessTools.Method(heroExtensions, "IsPlayerHero", new[] { typeof(Hero) });
            if (_tryGetContainer == null || _isServer == null)
                SubModule.Log("Could not read Coop's session state; assuming a player's PC in a session.");
            _resolved = true;
        }
    }
}
