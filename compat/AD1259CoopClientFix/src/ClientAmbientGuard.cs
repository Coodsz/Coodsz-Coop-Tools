using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

[assembly: AssemblyTitle("AD1259 - Coop Client Stability")]
[assembly: AssemblyDescription("Bounds guard for AD1259 campaign-map ambient sound terrain IDs")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace AD1259CoopClientFix
{
    public sealed class ClientSubModule : MBSubModuleBase
    {
        private static bool _installed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            if (GameNetwork.IsDedicatedServer || _installed) return;
            if (Environment.GetEnvironmentVariable("AD1259COOP_DISABLE_AMBIENT_GUARD") == "1")
            {
                ClientLog.Write("Ambient guard disabled by AD1259COOP_DISABLE_AMBIENT_GUARD.");
                return;
            }
            MethodInfo target = AccessTools.Method(typeof(MBMapScene), "TickAmbientSounds",
                new[] { typeof(Scene), typeof(int) });
            if (target == null) throw new MissingMethodException("MBMapScene.TickAmbientSounds(Scene, int)");
            var harmony = new Harmony("ad1259.coop.client.ambient-bounds");
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(AmbientTerrainGuard), "Prefix") { priority = Priority.First });
            _installed = true;
            ClientLog.Write("Installed ambient terrain bounds guard for Bannerlord 1.4.8 (26 native entries, IDs 0-25).");
            CompanionAuthorityGuard.Install(harmony);
        }
    }

    internal static class CompanionAuthorityGuard
    {
        private static FieldInfo _coop;
        private static MethodInfo _running;
        private static Func<bool> _isServer;
        private static bool _reported;
        private static bool _lookupFailureReported;

        internal static bool AllowSpawn(bool isServer, bool running)
        {
            return isServer || !running;
        }

        internal static void Install(Harmony harmony)
        {
            Type women = AccessTools.TypeByName("Women_In_Calradia.IMC_TWKhajiit");
            if (women == null) return; // Optional mod; no dependency on its assembly.
            Type coopMod = AccessTools.TypeByName("Coop.CoopMod");
            Type modInformation = AccessTools.TypeByName("Common.ModInformation");
            MethodInfo target = AccessTools.Method(women, "TrySpawnNewCompanion", Type.EmptyTypes);
            _coop = coopMod == null ? null : AccessTools.Field(coopMod, "Coop");
            _running = _coop == null ? null : AccessTools.PropertyGetter(_coop.FieldType, "Running");
            MethodInfo serverGetter = modInformation == null ? null : AccessTools.PropertyGetter(modInformation, "IsServer");
            if (target == null || _running == null || serverGetter == null)
            {
                ClientLog.Write("Women in Calradia authority guard could not resolve the Coop 0.1.6 session API.");
                return;
            }
            _isServer = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), serverGetter);
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(CompanionAuthorityGuard), "Prefix") { priority = Priority.First });
            ClientLog.Write("Installed Women in Calradia companion authority guard for active Coop client campaigns.");
        }

        private static bool Prefix()
        {
            // IsClient alone also reports true in offline singleplayer. Running
            // is true only in Coop CampaignState/MissionState; EndSession clears it.
            bool running;
            try
            {
                if (_isServer()) return true;
                object experience = _coop.GetValue(null);
                running = experience != null && (bool)_running.Invoke(experience, null);
            }
            catch (TargetInvocationException)
            {
                if (!_lookupFailureReported)
                {
                    _lookupFailureReported = true;
                    ClientLog.Write("Coop session state was unavailable; companion guard left the original behavior active.");
                }
                return true;
            }
            if (AllowSpawn(false, running)) return true;
            if (!_reported)
            {
                _reported = true;
                ClientLog.Write("Skipped Women in Calradia companion creation on an active Coop client; hero creation is authoritative on the server.");
            }
            return false;
        }
    }

    internal static class AmbientTerrainGuard
    {
        // Verified in the 1.4.8.119303 fatal dump: native ambient table has 26
        // records of 28 bytes. MapCameraView forwards FaceGroupIndex without a
        // bounds check. Custom AD1259 face groups can exceed this audio table.
        // Guard ONLY the sound call; navigation and campaign terrain are unchanged.
        internal const int AmbientEntryCount = 26;
        private static readonly HashSet<int> Reported = new HashSet<int>();

        internal static bool ShouldTick(int terrainType)
        {
            return terrainType >= 0 && terrainType < AmbientEntryCount;
        }

        private static bool Prefix(int __1)
        {
            if (ShouldTick(__1)) return true;
            lock (Reported)
            {
                if (Reported.Add(__1))
                    ClientLog.Write("Skipped out-of-range map ambient terrain ID " +
                        __1.ToString(CultureInfo.InvariantCulture) + "; native sound table accepts 0-25.");
            }
            return false;
        }
    }

    internal static class ClientLog
    {
        internal static void Write(string message)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "Client Diagnostics");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "AD1259CoopClientFix.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " +
                    message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
