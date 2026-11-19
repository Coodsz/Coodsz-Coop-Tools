using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

[assembly: AssemblyTitle("Coodsz Join Password")]
[assembly: AssemblyDescription("Sends the launcher's Coop password on the first auto-connect")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace CoodszJoinPassword
{
    // Coop 0.1.6's /autoconnect builds its client config from the address alone, so the first connect
    // to a password-protected server sends an empty password and is refused as "incorrect". The Coodsz
    // launchers pass the password in COODSZ_COOP_PASSWORD; this fills an empty Config.Token from it just
    // before CoopClient.Start connects. Coop.Core.dll is not edited (TAOM.CoopCompat requires the
    // released file). The DedicatedServer. prefix keeps this module out of Coop's join check.
    public sealed class JoinPasswordSubModule : MBSubModuleBase
    {
        internal const string VariableName = "COODSZ_COOP_PASSWORD";
        private static bool _installed;
        private static bool _filledNoted;
        private static CoodszSettingsSync.SettingsSyncLoader _settingsSync;

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            _settingsSync?.Tick(dt);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_installed || GameNetwork.IsDedicatedServer) return;
            _installed = true;
            var harmony = new Harmony("coodsz.coop.join-password");
            try
            {
                JoinBaselineFix.Install(harmony, Log);
            }
            catch (Exception e)
            {
                Log("Join baseline fix could not install: " + e.GetType().Name + ": " + e.Message);
            }

            // Map check guard (MapCheckGuard.cs): a null in Campaign.CheckMapUpdate no longer crashes the load
            // of the server's save. It only acts in a Coop client's load.
            try
            {
                Coodsz.MapCheck.MapCheckGuard.Install(harmony, Log);
            }
            catch (Exception e)
            {
                Log("Map check guard could not install: " + e.GetType().Name + ": " + e.Message);
            }

            // Settings sync: on a Coodsz server the host's MCM settings apply here for that session, in
            // memory only. CoodszSettingsSync.Coop.dll is loaded only after the probe finds Coop's types.
            try
            {
                string bin = Path.GetDirectoryName(typeof(JoinPasswordSubModule).Assembly.Location) ?? ".";
                _settingsSync = new CoodszSettingsSync.SettingsSyncLoader(true, bin, Path.GetFullPath(Path.Combine(bin, "..", "..")), Log);
                _settingsSync.TryLoad();
            }
            catch (Exception e)
            {
                Log("Settings sync disabled: " + e.GetType().Name + ": " + e.Message);
            }

            try
            {
                Type client = AccessTools.TypeByName("Coop.Core.Client.CoopClient")
                    ?? Type.GetType("Coop.Core.Client.CoopClient, Coop.Core", false);
                MethodInfo start = client == null ? null : AccessTools.Method(client, "Start", Type.EmptyTypes);
                if (start == null)
                {
                    Log("CoopClient.Start was not found; the launcher password is not applied.");
                    return;
                }

                harmony.Patch(start,
                    prefix: new HarmonyMethod(typeof(JoinPasswordSubModule), nameof(BeforeStart)) { priority = Priority.First });
                Log("Installed: an empty Coop connect password is filled from the launcher.");

                // Coop's client inherits Dispose from CoopNetworkBase. A local shutdown never calls the disconnect
                // callback, so the map check guard drops the client here instead of trusting its isConnected.
                MethodInfo dispose = Coodsz.MapCheck.CoopClientLink.DeclaredDispose(client);
                if (dispose != null)
                    harmony.Patch(dispose, prefix: new HarmonyMethod(typeof(JoinPasswordSubModule), nameof(BeforeDispose)));
                else
                    Log("CoopClient.Dispose was not found; the map check guard keeps a disposed client until it disconnects.");
            }
            catch (Exception e)
            {
                Log("Could not install: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Improved Garrisons party size hand back (compat\ImprovedGarrisonsCoopCompat\PartySizeHandBack.cs). The
        // servers run the same file, since every PC works out party sizes by itself.
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (GameNetwork.IsDedicatedServer) return;
            Coodsz.IgPartySize.PartySizeHandBack.TryInstall(Log);
        }

        // TAOM enlist guard (TaomEnlistGuard.cs): after the campaign is built, on a Coop client with TAOM only.
        public override void OnAfterGameInitializationFinished(Game game, object starterObject)
        {
            base.OnAfterGameInitializationFinished(game, starterObject);
            if (GameNetwork.IsDedicatedServer) return;
            Coodsz.EnlistGuard.TaomEnlistGuard.OnCampaignReady(Log);
        }

        // A single player game started after leaving a Coop session must not keep the enlist guard.
        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            Coodsz.EnlistGuard.TaomEnlistGuard.CoopClientStarted = false;
        }

        private static void BeforeDispose(object __instance) => Coodsz.MapCheck.MapCheckGuard.ClientEnding(__instance);

        private static void BeforeStart(object __instance)
        {
            // This game is a Coop client from here on; the TAOM enlist guard only runs in such a game. The map
            // check guard also needs this client to be connected at the time of the check.
            Coodsz.EnlistGuard.TaomEnlistGuard.CoopClientStarted = true;
            Coodsz.MapCheck.MapCheckGuard.ClientStarting(__instance);
            try
            {
                string password = Environment.GetEnvironmentVariable(VariableName);
                if (string.IsNullOrEmpty(password)) return;
                object config = AccessTools.Property(__instance.GetType(), "Config")?.GetValue(__instance, null);
                PropertyInfo token = config?.GetType().GetProperty("Token", BindingFlags.Instance | BindingFlags.Public);
                if (token == null || !token.CanRead || !token.CanWrite) return;
                if (!string.IsNullOrEmpty(token.GetValue(config, null) as string)) return;
                token.SetValue(config, password, null);
                if (_filledNoted) return;
                _filledNoted = true;
                // The password itself is never written to the log.
                Log("Filled the connect password from the launcher (" + password.Length.ToString(CultureInfo.InvariantCulture) + " characters).");
            }
            catch (Exception e)
            {
                Log("Could not fill the password: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Log(string message)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "Client Diagnostics");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "CoodszJoinPassword.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
