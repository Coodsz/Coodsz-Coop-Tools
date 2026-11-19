using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BurningEmpiresCoopCompat
{
    // Keeps the co-op server's saves safe:
    // - Branch autosaves: Burning Empires audits its autosave hook from the first application
    //   tick and turns autosaves off for the session when something looks wrong. A dedicated
    //   server has no save list screen, so a failed save list prefab or UIExtenderEx check no
    //   longer turns them off there. That check fails on every server start, so it gets a plain
    //   log line. Every other reason still turns them off and is logged as an error. The server
    //   is told by its process here, because Coop's session only starts when the save loads.
    //   This module never patches MBSaveLoad.AutoSaveCurrentGame, because that audit turns
    //   autosaves off when any other mod does.
    // - Failed restore: when Burning Empires could not restore a save's data it starts from
    //   empty data and sets a "restore blocked" flag. While that flag is set the server skips
    //   Burning Empires' save preparation, so the empty data never replaces the real data in
    //   the save slot or in BESavedata. The server logs an error at load and at every save.
    // - A startup warning when this Burning Empires build ships save migrations, because a
    //   migrated save blocks saving until a player saves it to a new slot.
    // Single player and players' PCs are untouched.
    internal static class DsSaveSafety
    {
        private const string AutoSaveCompatibilityTypeName = "BurningEmpires.BESystems.Persistence.BESaveBranchAutoSaveCompatibility";
        private const string BehaviorTypeName = "BurningEmpires.BESystems.Persistence.BEExternalSaveCampaignBehavior";
        private const string MigrationRegistryTypeName = "BurningEmpires.BESystems.Persistence.BESaveMigrationRegistry";

        private static FieldInfo _migrationRestoreBlocked;
        private static FieldInfo _historicalRestoreBlocked;
        private static bool _tickAuditReported;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            count += PatchAutoSaveAudit(harmony, burningEmpires);
            count += PatchRestoreBlocked(harmony, burningEmpires);
            WarnAboutMigrations(burningEmpires);
            return count;
        }

        // True when Burning Empires could not restore this save's data and runs on empty data.
        // WorldDataTransfer's OnBeforeSave postfix can ask this too.
        public static bool IsRestoreBlocked(object behavior)
        {
            if (behavior == null)
                return false;
            try
            {
                return (_migrationRestoreBlocked != null && (bool)_migrationRestoreBlocked.GetValue(behavior))
                    || (_historicalRestoreBlocked != null && (bool)_historicalRestoreBlocked.GetValue(behavior));
            }
            catch
            {
                return false;
            }
        }

        // ---- Branch autosave audit ----

        private static int PatchAutoSaveAudit(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, AutoSaveCompatibilityTypeName);
            if (type == null)
                return 0;

            var count = 0;
            count += Patch(harmony, AccessTools.Method(type, "DisableForSession", new[] { typeof(string) }),
                "BESaveBranchAutoSaveCompatibility.DisableForSession", prefix: Method(nameof(DisableForSessionPrefix)));
            // TickAudit runs on every application tick. Its UIExtenderEx check can throw on a
            // server without that module's assembly, which would break the tick.
            count += Patch(harmony, AccessTools.Method(type, "TickAudit", Type.EmptyTypes),
                "BESaveBranchAutoSaveCompatibility.TickAudit", finalizer: Method(nameof(TickAuditFinalizer)));
            return count;
        }

        private static bool DisableForSessionPrefix(string reason)
        {
            if (!Coop.OnServerProcess)
                return true;

            if (SaveSafetyRules.IsScreenOnlyReason(reason))
            {
                SubModule.Log(SaveSafetyRules.ScreenCheckNote(reason));
                return false;
            }
            SubModule.Log("ERROR Save safety: Burning Empires turned branch autosaves off for this session. Reason: " + reason);
            return true;
        }

        private static Exception TickAuditFinalizer(Exception __exception)
        {
            if (__exception == null || !Coop.OnServerProcess)
                return __exception;
            if (!_tickAuditReported)
            {
                _tickAuditReported = true;
                SubModule.Log("ERROR Save safety: Burning Empires' autosave audit failed on the server and was skipped; branch autosaves stay on: " + __exception);
            }
            return null;
        }

        // ---- Restore blocked ----

        private static int PatchRestoreBlocked(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, BehaviorTypeName);
            if (type == null)
                return 0;
            _migrationRestoreBlocked = AccessTools.Field(type, "_migrationRestoreBlocked");
            _historicalRestoreBlocked = AccessTools.Field(type, "_historicalRestoreBlocked");
            if (_migrationRestoreBlocked == null || _historicalRestoreBlocked == null)
            {
                SubModule.Log("Save safety: BEExternalSaveCampaignBehavior restore flags not found; save guard skipped.");
                return 0;
            }

            var count = 0;
            // OnGameLoaded already carries WorldDataTransfer's hooks; OnSessionLaunched runs
            // right after it on a load and is where the server reports a blocked restore.
            count += Patch(harmony, AccessTools.Method(type, "OnSessionLaunched", new[] { typeof(CampaignGameStarter) }),
                "BEExternalSaveCampaignBehavior.OnSessionLaunched", prefix: Method(nameof(OnSessionLaunchedPrefix)));
            count += Patch(harmony, AccessTools.Method(type, "PrepareSnapshotForSave", new[] { typeof(string) }),
                "BEExternalSaveCampaignBehavior.PrepareSnapshotForSave", prefix: Method(nameof(PrepareSnapshotForSavePrefix)));
            // First, so Burning Empires' own OnBeforeSave body is skipped before anything else.
            count += Patch(harmony, AccessTools.Method(type, "OnBeforeSave", Type.EmptyTypes),
                "BEExternalSaveCampaignBehavior.OnBeforeSave", prefix: new HarmonyMethod(typeof(DsSaveSafety), nameof(OnBeforeSavePrefix)) { priority = Priority.First });
            return count;
        }

        private static void OnSessionLaunchedPrefix(bool ____migrationRestoreBlocked, bool ____historicalRestoreBlocked)
        {
            if ((____migrationRestoreBlocked || ____historicalRestoreBlocked) && Coop.OnServer)
                SubModule.Log("ERROR Save safety: Burning Empires could not restore this save's data (" + Which(____migrationRestoreBlocked, ____historicalRestoreBlocked)
                    + ") and runs on empty data. The server will not write Burning Empires data until a good save is loaded. Its own warning is in this log.");
        }

        // Returning false makes Burning Empires cancel its prepared snapshot; the game save
        // still runs and keeps the slot's previous Burning Empires data.
        private static bool PrepareSnapshotForSavePrefix(string saveName, ref bool __result, bool ____migrationRestoreBlocked, bool ____historicalRestoreBlocked)
        {
            if (!(____migrationRestoreBlocked || ____historicalRestoreBlocked) || !Coop.OnServer)
                return true;
            SubModule.Log("ERROR Save safety: Burning Empires snapshot for \"" + saveName + "\" skipped, its data was not restored ("
                + Which(____migrationRestoreBlocked, ____historicalRestoreBlocked) + ").");
            __result = false;
            return false;
        }

        private static bool OnBeforeSavePrefix(bool ____migrationRestoreBlocked, bool ____historicalRestoreBlocked)
        {
            if (!(____migrationRestoreBlocked || ____historicalRestoreBlocked) || !Coop.OnServer)
                return true;
            SubModule.Log("ERROR Save safety: Burning Empires save preparation skipped, its data was not restored ("
                + Which(____migrationRestoreBlocked, ____historicalRestoreBlocked) + ").");
            return false;
        }

        private static string Which(bool migration, bool historical)
        {
            return migration && historical ? "load and Historical check failed" : migration ? "load failed" : "Historical check failed";
        }

        // ---- Migrations ----

        private static void WarnAboutMigrations(Assembly burningEmpires)
        {
            try
            {
                var type = FindType(burningEmpires, MigrationRegistryTypeName);
                var migrations = type == null ? null : AccessTools.Field(type, "Migrations")?.GetValue(null) as ICollection;
                if (migrations != null && migrations.Count > 0)
                    SubModule.Log("Save safety: this Burning Empires build migrates older saves (" + migrations.Count
                        + "). A migrated save blocks saving until a player saves it under a new name; on a co-op server, migrate the save in single player first.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Save safety: could not read Burning Empires' save migrations: " + ex.Message);
            }
        }

        // ---- Helpers ----

        private static Type FindType(Assembly assembly, string fullName)
        {
            var type = assembly?.GetType(fullName, false) ?? AccessTools.TypeByName(fullName);
            if (type == null)
                SubModule.Log("Save safety: type " + fullName + " not found.");
            return type;
        }

        private static HarmonyMethod Method(string name)
        {
            return new HarmonyMethod(typeof(DsSaveSafety), name);
        }

        private static int Patch(Harmony harmony, MethodInfo target, string label, HarmonyMethod prefix = null, HarmonyMethod finalizer = null)
        {
            if (target == null)
            {
                SubModule.Log("Save safety: " + label + " not found.");
                return 0;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, finalizer: finalizer);
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Save safety: could not patch " + label + ": " + ex.Message);
                return 0;
            }
        }
    }
}
