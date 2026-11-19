using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires asks "the player" through pop ups. The co-op server has no screen and no
    // player of its own, so a pop up there waits forever for a click nobody can make:
    // - Error, load warning and migration pop ups: the server writes their title and text to
    //   the log and drops them instead of opening them.
    // - Founding a kingdom: the server goes straight to the game's own kingdom creation, as if
    //   the religion choice had already been approved.
    // - Captives after a raid or siege: a co-op player's share is sold (the host default), so
    //   the gold goes through the game's GiveGoldAction like any other gold change.
    // - Historical saves (both sides in a session): the identity check expects Hero.MainHero to
    //   be the historical lord, which is never true in co-op, so it passes and the placeholder
    //   cleanup still runs. The server logs an error, because the historical lord's clan stays
    //   without a player.
    // Single player and the main menu are untouched.
    internal static class DsPopups
    {
        private const string ErrorServiceTypeName = "BurningEmpires.BESystems.Core.BEUserFacingErrorService";
        private const string ExternalSaveTypeName = "BurningEmpires.BESystems.Persistence.BEExternalSaveCampaignBehavior";
        private const string MigrationSessionTypeName = "BurningEmpires.BESystems.Persistence.BESaveMigrationSession";
        private const string FoundingReligionTypeName = "BurningEmpires.BESystems.Government.BEKingdomFoundingReligionService";
        private const string WarPopulationTypeName = "BurningEmpires.BESystems.SettlementEvents.BEWarPopulationCampaignBehavior";
        private const string PlaceholderRemovalTypeName = "BurningEmpires.BESystems.HistoricalCampaign.BEHistoricalPlaceholderRemovalService";

        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static PropertyInfo _errorTitle;
        private static PropertyInfo _errorBody;
        private static FieldInfo _replayingApprovedCreation;
        private static MethodInfo _sellCaptives;
        private static MethodInfo _cloneCaptives;
        private static MethodInfo _logDisposition;
        private static PropertyInfo _transactionEventId;
        private static PropertyInfo _transactionOrigin;
        private static PropertyInfo _transactionCaptives;
        private static MethodInfo _transactionTryCommit;
        private static object _choiceState;
        private static int _lastMigrationVersionLogged = -1;

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;
            count += PatchErrorService(harmony, burningEmpires);
            count += PatchLoadWarning(harmony, burningEmpires);
            count += PatchMigrationSession(harmony, burningEmpires);
            count += PatchKingdomFounding(harmony, burningEmpires);
            count += PatchWarPopulation(harmony, burningEmpires);
            count += PatchHistoricalIdentity(harmony, burningEmpires);
            return count;
        }

        // ---- Error pop ups ----

        private static int PatchErrorService(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, ErrorServiceTypeName);
            var pending = type?.GetNestedType("PendingError", BindingFlags.NonPublic | BindingFlags.Public);
            if (pending != null)
            {
                _errorTitle = AccessTools.Property(pending, "Title");
                _errorBody = AccessTools.Property(pending, "Body");
            }
            if (type == null || AccessTools.Field(type, "PendingErrors") == null || AccessTools.Field(type, "SyncRoot") == null)
            {
                SubModule.Log("Server pop ups: BEUserFacingErrorService members not found; error pop ups stay as they are.");
                return 0;
            }
            return Patch(harmony, AccessTools.Method(type, "Pump", Type.EmptyTypes), "BEUserFacingErrorService.Pump",
                prefix: Method(nameof(ErrorPumpPrefix)));
        }

        // Runs every application tick, so the queue is looked at before asking Coop anything.
        // The server is told by its process, so errors from before Coop's session starts (the
        // session starts when the save loads) are logged too.
        private static bool ErrorPumpPrefix(object ___SyncRoot, object ___PendingErrors)
        {
            var queue = ___PendingErrors as ICollection;
            if (queue == null || queue.Count == 0 || !Coop.OnServerProcess)
                return true;

            var drained = new List<object>();
            lock (___SyncRoot)
            {
                foreach (var item in queue)
                    drained.Add(item);
                AccessTools.Method(___PendingErrors.GetType(), "Clear")?.Invoke(___PendingErrors, null);
            }
            foreach (var item in drained)
            {
                SubModule.Log("Server pop ups: Burning Empires error (no pop up on the server): "
                    + (_errorTitle?.GetValue(item, null) as string) + " | " + (_errorBody?.GetValue(item, null) as string));
            }
            return false;
        }

        // ---- Load and save warnings ----

        private static int PatchLoadWarning(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, ExternalSaveTypeName);
            if (type == null || AccessTools.Field(type, "_pendingWarningTitle") == null || AccessTools.Field(type, "_pendingWarningText") == null)
            {
                SubModule.Log("Server pop ups: BEExternalSaveCampaignBehavior warning fields not found; load warnings stay as they are.");
                return 0;
            }
            return Patch(harmony, AccessTools.Method(type, "ShowPendingWarning", Type.EmptyTypes), "BEExternalSaveCampaignBehavior.ShowPendingWarning",
                prefix: Method(nameof(ShowPendingWarningPrefix)));
        }

        private static bool ShowPendingWarningPrefix(ref string ____pendingWarningTitle, ref string ____pendingWarningText)
        {
            if (string.IsNullOrWhiteSpace(____pendingWarningText) || !Coop.OnServer)
                return true;

            SubModule.Log("Server pop ups: Burning Empires save warning (no pop up on the server): "
                + Localize(____pendingWarningTitle) + " | " + Localize(____pendingWarningText));
            ____pendingWarningTitle = string.Empty;
            ____pendingWarningText = string.Empty;
            return false;
        }

        // ---- Migration Save As prompt ----

        private static int PatchMigrationSession(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, MigrationSessionTypeName);
            if (type == null || AccessTools.Field(type, "_requiresSaveAs") == null || AccessTools.Field(type, "_inquiryOpen") == null
                || AccessTools.Field(type, "_sourceVersion") == null)
            {
                SubModule.Log("Server pop ups: BESaveMigrationSession members not found; the migration prompt stays as it is.");
                return 0;
            }
            return Patch(harmony, AccessTools.Method(type, "Pump", Type.EmptyTypes), "BESaveMigrationSession.Pump",
                prefix: Method(nameof(MigrationPumpPrefix)));
        }

        // Nobody on the server can type a new save name. Burning Empires keeps ordinary saves
        // blocked until a migrated campaign is saved to a new slot, and that protection stays:
        // the prompt is only logged, once per loaded data version.
        private static bool MigrationPumpPrefix(bool ____requiresSaveAs, bool ____inquiryOpen, int ____sourceVersion)
        {
            if (!____requiresSaveAs || ____inquiryOpen || !Coop.OnServer)
                return true;

            if (_lastMigrationVersionLogged != ____sourceVersion)
            {
                _lastMigrationVersionLogged = ____sourceVersion;
                SubModule.Log("ERROR Server pop ups: Burning Empires migrated this save from data version " + ____sourceVersion
                    + " and blocks saving until it is saved to a new slot, which needs a player. Load the save in single player,"
                    + " save it under a new name, and host that save.");
            }
            return false;
        }

        // ---- Kingdom founding religion choice ----

        private static int PatchKingdomFounding(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, FoundingReligionTypeName);
            _replayingApprovedCreation = type == null ? null : AccessTools.Field(type, "_isReplayingApprovedCreation");
            if (_replayingApprovedCreation == null)
            {
                SubModule.Log("Server pop ups: BEKingdomFoundingReligionService._isReplayingApprovedCreation not found; founding guard skipped.");
                return 0;
            }

            // Runs before Burning Empires' own prefix, which reads the flag.
            var prefix = new HarmonyMethod(typeof(DsPopups), nameof(CreateKingdomPrefix)) { priority = Priority.First };
            var finalizer = new HarmonyMethod(typeof(DsPopups), nameof(CreateKingdomFinalizer)) { priority = Priority.First };
            var count = 0;
            foreach (var method in typeof(KingdomManager).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.Name != "CreateKingdom" || method.IsAbstract)
                    continue;
                count += Patch(harmony, method, "KingdomManager.CreateKingdom", prefix: prefix, finalizer: finalizer);
            }
            if (count == 0)
                SubModule.Log("Server pop ups: KingdomManager.CreateKingdom not found; founding guard skipped.");
            return count;
        }

        private static void CreateKingdomPrefix(out bool __state)
        {
            __state = false;
            if (!Coop.OnServer)
                return;
            try
            {
                if ((bool)_replayingApprovedCreation.GetValue(null))
                    return;
                _replayingApprovedCreation.SetValue(null, true);
                __state = true;
            }
            catch (Exception ex)
            {
                ReportOnce("founding religion flag", ex);
            }
        }

        private static Exception CreateKingdomFinalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                try { _replayingApprovedCreation.SetValue(null, false); }
                catch (Exception ex) { ReportOnce("founding religion flag reset", ex); }
            }
            return __exception;
        }

        // ---- War population captives ----

        private static int PatchWarPopulation(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, WarPopulationTypeName);
            if (type == null)
                return 0;

            _sellCaptives = AccessTools.Method(type, "SellCaptives");
            _cloneCaptives = AccessTools.Method(type, "CloneCaptives");
            _logDisposition = AccessTools.Method(type, "LogDisposition");
            var transaction = type.GetNestedType("PlayerDispositionTransaction", BindingFlags.NonPublic | BindingFlags.Public);
            var state = type.GetNestedType("PlayerDispositionState", BindingFlags.NonPublic | BindingFlags.Public);
            if (transaction != null)
            {
                _transactionEventId = AccessTools.Property(transaction, "EventId");
                _transactionOrigin = AccessTools.Property(transaction, "Origin");
                _transactionCaptives = AccessTools.Property(transaction, "Captives");
                _transactionTryCommit = AccessTools.Method(transaction, "TryCommit");
            }
            if (state != null && Enum.IsDefined(state, "Choice"))
                _choiceState = Enum.Parse(state, "Choice");

            if (_sellCaptives == null || _cloneCaptives == null)
            {
                SubModule.Log("Server pop ups: BEWarPopulationCampaignBehavior sale members not found; captive guard skipped.");
                return 0;
            }

            var count = 0;
            // A co-op player's share lands in the NPC path on the server (Hero.MainHero is the
            // server's own hero), where the hero's traits would decide. Sell it instead.
            count += Patch(harmony, AccessTools.Method(type, "ResolveNpcDisposition"), "BEWarPopulationCampaignBehavior.ResolveNpcDisposition",
                prefix: Method(nameof(ResolveNpcDispositionPrefix)));
            // The server's own hero takes the player path; nobody can answer that pop up.
            count += Patch(harmony, AccessTools.Method(type, "QueuePlayerDisposition"), "BEWarPopulationCampaignBehavior.QueuePlayerDisposition",
                prefix: Method(nameof(QueuePlayerDispositionPrefix)));
            // QueuePlayerDisposition is small enough to be inlined, so the pop up itself is
            // guarded too.
            if (_transactionOrigin != null && _transactionCaptives != null && _transactionTryCommit != null && _choiceState != null)
                count += Patch(harmony, AccessTools.Method(type, "ShowPlayerDisposition"), "BEWarPopulationCampaignBehavior.ShowPlayerDisposition",
                    prefix: Method(nameof(ShowPlayerDispositionPrefix)));
            else
                SubModule.Log("Server pop ups: PlayerDispositionTransaction members not found; captive pop up guard skipped.");
            return count;
        }

        private static bool ResolveNpcDispositionPrefix(string eventId, Settlement origin, Hero hero, object captives)
        {
            if (!Coop.OnServer || !Coop.IsPlayerHero(hero))
                return true;
            SellForHost(eventId, origin, hero, captives);
            return false;
        }

        private static bool QueuePlayerDispositionPrefix(string eventId, Settlement origin, object captives)
        {
            if (!Coop.OnServer)
                return true;
            try
            {
                SellForHost(eventId, origin, PlayerExemptions.SafeMainHero(), _cloneCaptives.Invoke(null, new[] { captives }));
            }
            catch (Exception ex)
            {
                ReportOnce("captive sale", Unwrap(ex));
            }
            return false;
        }

        private static bool ShowPlayerDispositionPrefix(object transaction)
        {
            if (!Coop.OnServer)
                return true;
            try
            {
                if (transaction != null && (bool)_transactionTryCommit.Invoke(transaction, new[] { _choiceState }))
                {
                    SellForHost(_transactionEventId?.GetValue(transaction, null) as string, _transactionOrigin.GetValue(transaction, null) as Settlement,
                        PlayerExemptions.SafeMainHero(), _transactionCaptives.GetValue(transaction, null));
                }
            }
            catch (Exception ex)
            {
                ReportOnce("captive pop up", Unwrap(ex));
            }
            return false;
        }

        // Burning Empires' own sale: captives go to nearby slave markets, and the gold is given
        // with GiveGoldAction, which Coop syncs.
        private static void SellForHost(string eventId, Settlement origin, Hero seller, object captives)
        {
            if (captives == null)
                return;
            try
            {
                var sale = _sellCaptives.Invoke(null, new[] { origin, seller, captives, (object)false });
                var gold = sale == null ? 0 : (int)(AccessTools.Property(sale.GetType(), "Gold")?.GetValue(sale, null) ?? 0);
                var receipt = sale == null ? null : AccessTools.Property(sale.GetType(), "Receipt")?.GetValue(sale, null);
                // Burning Empires pays 10 denars a person, so the head count follows from the gold.
                var amount = gold / 10;
                _logDisposition?.Invoke(null, new[] { eventId, seller, origin, "sold", (object)amount, receipt, (object)gold, (object)0.0 });
                SubModule.Log("Server pop ups: captives from " + (origin?.Name?.ToString() ?? "?") + " sold for "
                    + (seller?.Name?.ToString() ?? "nobody") + " (" + gold + " denars).");
            }
            catch (Exception ex)
            {
                ReportOnce("captive sale", Unwrap(ex));
            }
        }

        // ---- Historical saves ----

        private static int PatchHistoricalIdentity(Harmony harmony, Assembly burningEmpires)
        {
            var type = FindType(burningEmpires, PlaceholderRemovalTypeName);
            if (type == null)
                return 0;

            var count = 0;
            count += Patch(harmony, AccessTools.Method(type, "ValidateHistoricalLoadedGame", new[] { typeof(string) }),
                "BEHistoricalPlaceholderRemovalService.ValidateHistoricalLoadedGame", prefix: Method(nameof(ValidateHistoricalLoadedGamePrefix)));
            count += Patch(harmony, AccessTools.Method(type, "TryValidateLoadedHistoricalIdentity"),
                "BEHistoricalPlaceholderRemovalService.TryValidateLoadedHistoricalIdentity", prefix: Method(nameof(TryValidateLoadedHistoricalIdentityPrefix)));
            return count;
        }

        // Only logs; the game's own check and the placeholder cleanup still run.
        private static void ValidateHistoricalLoadedGamePrefix(string candidateId)
        {
            if (!string.IsNullOrWhiteSpace(candidateId) && Coop.OnServer)
                SubModule.Log("ERROR Server pop ups: this is a Burning Empires Historical save (candidate " + candidateId
                    + "). Co-op loads it, but the historical lord's clan has no player. Host a save started with \"Burning Empires: Sandbox\".");
        }

        private static bool TryValidateLoadedHistoricalIdentityPrefix(ref bool __result, ref string error)
        {
            if (!Coop.InSession)
                return true;
            error = string.Empty;
            __result = true;
            return false;
        }

        // ---- Helpers ----

        private static string Localize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            try { return new TextObject(text).ToString(); }
            catch { return text; }
        }

        private static Type FindType(Assembly assembly, string fullName)
        {
            var type = assembly?.GetType(fullName, false) ?? AccessTools.TypeByName(fullName);
            if (type == null)
                SubModule.Log("Server pop ups: type " + fullName + " not found.");
            return type;
        }

        private static HarmonyMethod Method(string name)
        {
            return new HarmonyMethod(typeof(DsPopups), name);
        }

        private static int Patch(Harmony harmony, MethodInfo target, string label, HarmonyMethod prefix = null, HarmonyMethod finalizer = null)
        {
            if (target == null)
            {
                SubModule.Log("Server pop ups: " + label + " not found.");
                return 0;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, finalizer: finalizer);
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Server pop ups: could not patch " + label + ": " + ex.Message);
                return 0;
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
        }

        private static void ReportOnce(string key, Exception ex)
        {
            lock (Reported)
            {
                if (Reported.Add(key))
                    SubModule.Log("Server pop ups: " + key + " failed on the server and was skipped: " + ex);
            }
        }
    }
}
