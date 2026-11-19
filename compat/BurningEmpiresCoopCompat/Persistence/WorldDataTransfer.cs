using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BurningEmpiresCoopCompat
{
    // Gives joining players the server's Burning Empires world data (populations, religions,
    // relations and the rest), and keeps it on the server from one start to the next. Burning
    // Empires keeps most of it in files next to the mod (BESavedata), which Coop never sends, and
    // it only writes those files from its own save slot code, which the co-op server's saves
    // never go through.
    // - Server save: right after Burning Empires prepares for a save, the recovery copy inside
    //   the save is replaced with the full world data, marked as the server's copy
    //   (WorldDataRules), and the save drops the manifest of any older single player snapshot.
    //   Coop's join save and the server's own saves then carry everything. A save that does go
    //   through Burning Empires' save slot code still gets a new snapshot and its small copy.
    // - Server load: a save the server wrote loads its own copy and never reads BESavedata, so a
    //   restart cannot roll the world data back to an older snapshot. A save the server has not
    //   written yet, such as a single player save on its first load, loads the way Burning
    //   Empires loads it, from its snapshot.
    // - Player: while Burning Empires loads the joined game, the file manifest and save name
    //   are hidden, so it takes the copy inside the save and never reads or writes its local
    //   files. The "recovery was used" pop-up is cleared (it is logged instead), and the
    //   missing religion pass is skipped because religions come from the server.
    // Single player and the main menu are untouched.
    internal static class WorldDataTransfer
    {
        private const string BehaviorTypeName = "BurningEmpires.BESystems.Persistence.BEExternalSaveCampaignBehavior";
        private const string SlotContextTypeName = "BurningEmpires.BESystems.Persistence.BEExternalSaveSlotContext";
        private const string JsonTypeName = "BurningEmpires.BESystems.Persistence.BECompressedJson";
        private const string WorldDataManagerTypeName = "BurningEmpires.BESystems.Core.BEWorldDataManager";
        private const string StashServiceTypeName = "BurningEmpires.BESystems.Wonders.BEWonderStashService";
        private const string ReligionAssignmentTypeName = "BurningEmpires.BESystems.Religion.BEReligionAssignmentService";

        private static MethodInfo _setMinimalRecoveryJson;
        private static MethodInfo _serializeObject;
        private static MethodInfo _captureStashes;
        private static MethodInfo _clearCurrentSaveName;
        private static PropertyInfo _currentWorldData;
        private static FieldInfo _externalManifestJson;
        private static FieldInfo _pendingWarningTitle;
        private static FieldInfo _pendingWarningText;
        private static FieldInfo _deferPendingLoadWarning;
        private static FieldInfo _migrationRestoreBlocked;

        // True only while Burning Empires' load handler runs on a player's PC.
        private static bool _clientLoadRunning;

        // True only while Burning Empires' load handler runs on the server, with whether that load
        // took the server's own copy and the read status Burning Empires answered.
        private static bool _serverLoadRunning;
        private static bool _serverCopyLoad;
        private static string _serverReadStatus;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            var behavior = FindType(burningEmpires, BehaviorTypeName);
            var slotContext = FindType(burningEmpires, SlotContextTypeName);
            var json = FindType(burningEmpires, JsonTypeName);
            var worldDataManager = FindType(burningEmpires, WorldDataManagerTypeName);
            var stashService = FindType(burningEmpires, StashServiceTypeName);
            var religionAssignment = FindType(burningEmpires, ReligionAssignmentTypeName);

            if (behavior != null)
            {
                _setMinimalRecoveryJson = AccessTools.Method(behavior, "SetMinimalRecoveryJson", new[] { typeof(string) });
                _externalManifestJson = AccessTools.Field(behavior, "_externalManifestJson");
                _pendingWarningTitle = AccessTools.Field(behavior, "_pendingWarningTitle");
                _pendingWarningText = AccessTools.Field(behavior, "_pendingWarningText");
                _deferPendingLoadWarning = AccessTools.Field(behavior, "_deferPendingLoadWarningUntilTick");
                _migrationRestoreBlocked = AccessTools.Field(behavior, "_migrationRestoreBlocked");
            }
            if (json != null)
                _serializeObject = AccessTools.Method(json, "SerializeObject", new[] { typeof(object) });
            if (worldDataManager != null)
                _currentWorldData = AccessTools.Property(worldDataManager, "Current");
            if (stashService != null)
                _captureStashes = AccessTools.Method(stashService, "CaptureAll", Type.EmptyTypes);
            if (slotContext != null)
                _clearCurrentSaveName = AccessTools.Method(slotContext, "ClearCurrentSaveName", Type.EmptyTypes);

            // Server half.
            if (behavior != null && _setMinimalRecoveryJson != null && _serializeObject != null && _currentWorldData != null)
                count += Patch(harmony, AccessTools.Method(behavior, "OnBeforeSave", Type.EmptyTypes),
                    "BEExternalSaveCampaignBehavior.OnBeforeSave", postfix: Method(nameof(OnBeforeSavePostfix), Priority.Last));
            else
                SubModule.Log("World data transfer: server half skipped, Burning Empires save members not found.");

            // Player half. GetCurrentSaveName is tiny, so it is patched before OnGameLoaded:
            // otherwise the JIT could inline the unpatched copy into OnGameLoaded's patched body.
            if (slotContext != null)
                count += Patch(harmony, AccessTools.Method(slotContext, "GetCurrentSaveName", Type.EmptyTypes),
                    "BEExternalSaveSlotContext.GetCurrentSaveName", prefix: Method(nameof(GetCurrentSaveNamePrefix), Priority.First));
            else
                SubModule.Log("World data transfer: BEExternalSaveSlotContext not found.");

            // The read itself also gets a blank manifest and save name. On a player's PC that is
            // belt and braces, in case the slot context answer was inlined somewhere. On the server
            // it is how a save the server wrote loads its own copy.
            var saveService = FindType(burningEmpires, "BurningEmpires.BESystems.Persistence.BEExternalSaveService");
            var readSnapshot = saveService == null ? null : AccessTools.GetDeclaredMethods(saveService)
                .FirstOrDefault(m => m.Name == "ReadSnapshot" && m.GetParameters().Length == 6);
            if (readSnapshot != null)
                count += Patch(harmony, readSnapshot, "BEExternalSaveService.ReadSnapshot",
                    prefix: Method(nameof(ReadSnapshotPrefix), Priority.First),
                    postfix: Method(nameof(ReadSnapshotPostfix), Priority.Last));
            else
                SubModule.Log("World data transfer: BEExternalSaveService.ReadSnapshot not found.");

            if (behavior != null && _externalManifestJson != null)
            {
                count += Patch(harmony, AccessTools.Method(behavior, "OnGameLoaded", new[] { typeof(CampaignGameStarter) }),
                    "BEExternalSaveCampaignBehavior.OnGameLoaded",
                    prefix: Method(nameof(OnGameLoadedPrefix), Priority.First),
                    postfix: Method(nameof(OnGameLoadedPostfix), Priority.Last),
                    finalizer: Method(nameof(OnGameLoadedFinalizer), Priority.Last));
            }
            else
            {
                SubModule.Log("World data transfer: load hooks for players and the server skipped, BEExternalSaveCampaignBehavior members not found.");
            }

            if (religionAssignment != null)
                count += Patch(harmony, AccessTools.Method(religionAssignment, "ReconcileMissingReligions", Type.EmptyTypes),
                    "BEReligionAssignmentService.ReconcileMissingReligions", prefix: Method(nameof(SkipOnPlayerPcPrefix), Priority.First));
            else
                SubModule.Log("World data transfer: BEReligionAssignmentService not found.");

            return count;
        }

        // Server: Burning Empires has just made sure the world data has its data version, module
        // versions and save branch. Store all of it as the recovery copy inside the save, marked
        // as the server's copy. The server is told the same way here and in OnGameLoadedPrefix.
        private static void OnBeforeSavePostfix(object __instance)
        {
            if (!Coop.OnServerProcess || DsSaveSafety.IsRestoreBlocked(__instance))
                return;
            try
            {
                var data = _currentWorldData.GetValue(null, null);
                if (data == null)
                {
                    SubModule.Log("World data transfer: no Burning Empires world data on the server, join save keeps the small copy.");
                    return;
                }

                var type = data.GetType();
                var dataVersion = AccessTools.Property(type, "DataVersion");
                var moduleVersions = AccessTools.Property(type, "ModuleVersions");
                var saveBranchId = AccessTools.Property(type, "SaveBranchId");
                var campaignId = AccessTools.Property(type, "CampaignUniqueGameId");
                var contract = AccessTools.Property(type, "MinimalRecoveryReligionContractVersion");
                var heroReligions = AccessTools.Property(type, "HeroReligions");
                var kingdomReligions = AccessTools.Property(type, "KingdomReligions");
                if (dataVersion == null || moduleVersions == null || saveBranchId == null || campaignId == null
                    || contract == null || heroReligions == null || kingdomReligions == null)
                {
                    SubModule.Log("World data transfer: BEWorldData members not found, join save keeps the small copy.");
                    return;
                }

                // The player's load rejects data without these, so do not send a copy it cannot use.
                if ((int)dataVersion.GetValue(data, null) <= 0
                    || moduleVersions.GetValue(data, null) == null
                    || string.IsNullOrWhiteSpace(saveBranchId.GetValue(data, null) as string)
                    || heroReligions.GetValue(data, null) == null
                    || kingdomReligions.GetValue(data, null) == null)
                {
                    SubModule.Log("World data transfer: world data has no version, branch or religion tables yet, join save keeps the small copy.");
                    return;
                }

                // Wonder stash contents live in rosters until a save copies them in.
                try { _captureStashes?.Invoke(null, null); }
                catch (Exception ex) { SubModule.Log("World data transfer: stash capture failed: " + Unwrap(ex).Message); }

                // Contract version 1 says the religion tables are complete. The live data keeps
                // its own values; only the serialized copy carries these.
                var oldContract = (int)contract.GetValue(data, null);
                var oldCampaignId = campaignId.GetValue(data, null) as string;
                string json;
                try
                {
                    contract.SetValue(data, 1, null);
                    if (string.IsNullOrWhiteSpace(oldCampaignId))
                        campaignId.SetValue(data, Campaign.Current?.UniqueGameId ?? string.Empty, null);
                    json = _serializeObject.Invoke(null, new[] { data }) as string;
                }
                finally
                {
                    contract.SetValue(data, oldContract, null);
                    campaignId.SetValue(data, oldCampaignId, null);
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    SubModule.Log("World data transfer: world data serialized to nothing, join save keeps the small copy.");
                    return;
                }
                json = WorldDataRules.MarkServerCopy(json);
                _setMinimalRecoveryJson.Invoke(__instance, new object[] { json });
                // This copy is now the save's Burning Empires data. The manifest of an older single
                // player snapshot would only point a later load back at that snapshot.
                _externalManifestJson?.SetValue(__instance, string.Empty);
                SubModule.Log("World data transfer: full world data stored in the save (" + json.Length + " characters).");
            }
            catch (Exception ex)
            {
                SubModule.Log("World data transfer: server half failed, join save keeps the small copy: " + Unwrap(ex));
            }
        }

        // Server: note the load, so ReadSnapshot can tell a save the server wrote.
        // Player: hide the file manifest and save name, so Burning Empires finds no local
        // snapshot and loads the copy inside the save the server sent.
        private static void OnGameLoadedPrefix(object __instance)
        {
            if (Coop.OnServerProcess)
            {
                _serverLoadRunning = true;
                _serverCopyLoad = false;
                _serverReadStatus = null;
                return;
            }
            if (!Coop.OnPlayerPc)
                return;
            try
            {
                _externalManifestJson.SetValue(__instance, string.Empty);
                _clearCurrentSaveName?.Invoke(null, null);
                _clientLoadRunning = true;
            }
            catch (Exception ex)
            {
                SubModule.Log("World data transfer: could not prepare the player load: " + Unwrap(ex));
            }
        }

        // Player: the copy came from the server on purpose, so the recovery pop-up is noise.
        // Its text goes to the log, so a real load failure is still visible.
        private static void OnGameLoadedPostfix(object __instance)
        {
            if (_serverLoadRunning)
            {
                ServerLoadDone(__instance);
                return;
            }
            if (!_clientLoadRunning)
                return;
            try
            {
                var text = _pendingWarningText?.GetValue(__instance) as string;
                if (!string.IsNullOrWhiteSpace(text))
                    SubModule.Log("World data transfer: Burning Empires load message hidden on the player's PC: "
                        + (_pendingWarningTitle?.GetValue(__instance) as string) + " | " + text);
                if (_migrationRestoreBlocked != null && (bool)_migrationRestoreBlocked.GetValue(__instance))
                    SubModule.Log("World data transfer: Burning Empires could not use the server's world data on this PC.");
                else
                    SubModule.Log("World data transfer: server world data loaded (" + CountPopulations() + " settlement populations).");

                _pendingWarningTitle?.SetValue(__instance, string.Empty);
                _pendingWarningText?.SetValue(__instance, string.Empty);
                _deferPendingLoadWarning?.SetValue(__instance, false);
            }
            catch (Exception ex)
            {
                SubModule.Log("World data transfer: could not clear the recovery message: " + Unwrap(ex));
            }
        }

        // Server: say which Burning Empires data the save loaded. With the server's own copy,
        // Burning Empires' notice that no snapshot could be used is expected, so it is dropped;
        // any other warning stays and DsPopups writes it to the log. A failed load is left to
        // DsSaveSafety.
        private static void ServerLoadDone(object behavior)
        {
            try
            {
                if (DsSaveSafety.IsRestoreBlocked(behavior))
                    return;
                if (!_serverCopyLoad)
                {
                    SubModule.Log("World data transfer: this save has no world data from the server yet, so Burning Empires loaded its own data for it ("
                        + (_serverReadStatus ?? "status unknown") + ", " + CountPopulations() + " settlement populations).");
                    return;
                }
                if (WorldDataRules.IsOnlyRecoveryNotice(_pendingWarningText?.GetValue(behavior) as string))
                {
                    _pendingWarningTitle?.SetValue(behavior, string.Empty);
                    _pendingWarningText?.SetValue(behavior, string.Empty);
                    _deferPendingLoadWarning?.SetValue(behavior, false);
                }
                SubModule.Log("World data transfer: the server's own world data loaded from the save (" + CountPopulations()
                    + " settlement populations). Older Burning Empires snapshots were not read.");
            }
            catch (Exception ex)
            {
                SubModule.Log("World data transfer: could not finish the server load: " + Unwrap(ex));
            }
        }

        private static Exception OnGameLoadedFinalizer(Exception __exception)
        {
            _clientLoadRunning = false;
            _serverLoadRunning = false;
            _serverCopyLoad = false;
            _serverReadStatus = null;
            return __exception;
        }

        // Server: a save the server wrote carries its full world data. With no manifest and no
        // save name Burning Empires reads that copy, not the single player snapshot an old
        // manifest or a slot folder of the same name would hand it. A save the server has not
        // written yet is read as Burning Empires reads it.
        private static void ReadSnapshotPrefix(ref string manifestJson, string minimalRecoverySnapshotJson, ref string saveName)
        {
            if (_serverLoadRunning)
            {
                if (!WorldDataRules.IsServerCopy(minimalRecoverySnapshotJson))
                    return;
                _serverCopyLoad = true;
            }
            else if (!_clientLoadRunning || !Coop.OnPlayerPc)
            {
                return;
            }
            manifestJson = string.Empty;
            saveName = string.Empty;
        }

        private static void ReadSnapshotPostfix(object __result)
        {
            if (!_serverLoadRunning || __result == null)
                return;
            try { _serverReadStatus = AccessTools.Property(__result.GetType(), "Status")?.GetValue(__result, null)?.ToString(); }
            catch { _serverReadStatus = null; }
        }

        private static bool GetCurrentSaveNamePrefix(ref string __result)
        {
            if (!_clientLoadRunning || !Coop.OnPlayerPc)
                return true;
            __result = string.Empty;
            return false;
        }

        // Player: religions come from the server's data; this pass would invent them locally.
        private static bool SkipOnPlayerPcPrefix()
        {
            return !Coop.OnPlayerPc;
        }

        private static int CountPopulations()
        {
            try
            {
                var data = _currentWorldData?.GetValue(null, null);
                var populations = data == null ? null : AccessTools.Property(data.GetType(), "SettlementPopulations")?.GetValue(data, null) as ICollection;
                return populations?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static Type FindType(Assembly assembly, string fullName)
        {
            var type = assembly?.GetType(fullName, false) ?? AccessTools.TypeByName(fullName);
            if (type == null)
                SubModule.Log("World data transfer: type " + fullName + " not found.");
            return type;
        }

        private static HarmonyMethod Method(string name, int priority)
        {
            return new HarmonyMethod(typeof(WorldDataTransfer), name) { priority = priority };
        }

        private static int Patch(Harmony harmony, MethodInfo target, string label,
            HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null)
        {
            if (target == null)
            {
                SubModule.Log("World data transfer: " + label + " not found.");
                return 0;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("World data transfer: could not patch " + label + ": " + ex.Message);
                return 0;
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
        }
    }
}
