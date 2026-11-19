using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace DiplomacyCoopCompat
{
    // Makes Diplomacy safe in Bannerlord Coop (built against Coop 0.1.6 and 0.1.7, Diplomacy 1.5.3 to 1.5.5).
    //
    // How Coop works: the server runs the campaign. Vanilla actions (declare war, make peace,
    // give a settlement, gold, relations) are blocked on players' PCs and replayed from the
    // server. Players' requests only reach the server through vanilla paths (kingdom decisions,
    // the vanilla gift fief popup, alliances). Diplomacy's own state (pacts, cooldowns, rebel
    // factions, war exhaustion) is only copied to a player when they join, then drifts.
    //
    // So, only while a co-op session is running:
    // - Both sides: war exhaustion, civil wars/rebel factions, kingdom elimination and
    //   "keep fief after siege" are off. Messengers are off.
    // - Players' PCs: Diplomacy's direct buttons (declare war, make peace, break alliance,
    //   non-aggression pact, grant fief, donate gold, rebel factions) are greyed out with a
    //   reason, since they would only change that PC. Diplomacy's background logic does not
    //   run there; the server's copy decides.
    // - Server: Diplomacy questions meant for "the player" are answered in code, because
    //   nobody can click them on a server.
    // Single player and the main menu are untouched.
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "coodsz.diplomacy.coopcompat";
        private static readonly object Gate = new object();
        private static bool _patched;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
            Coop.ModulesLoaded();
        }

        private static void TryPatch(Assembly assembly)
        {
            if (_patched || assembly == null || !IsDiplomacy(assembly))
                return;
            lock (Gate)
            {
                if (_patched)
                    return;
                _patched = true;
            }

            try
            {
                var count = Patches.Apply(new Harmony(HarmonyId), assembly);
                Log("Diplomacy coop patch ready (" + count + " hooks on " + assembly.GetName().Name + ").");
            }
            catch (Exception ex)
            {
                Log("Diplomacy coop patch could not hook Diplomacy: " + ex.Message);
            }
        }

        // The Diplomacy module loader picks one DLL per game version, e.g.
        // Bannerlord.Diplomacy.1.4.8 or Bannerlord.Diplomacy.v1.4.8 (Fork).
        private static bool IsDiplomacy(Assembly assembly)
        {
            string name;
            try { name = assembly.GetName().Name ?? ""; }
            catch { return false; }
            return name.StartsWith("Bannerlord.Diplomacy.", StringComparison.Ordinal)
                && TypeIn(assembly, "Diplomacy.Settings") != null;
        }

        // Assembly.GetType still throws when the type needs a DLL that can't load (a dedicated
        // server has no UI DLLs), even with throwOnError false. Nothing here may throw into the
        // game's module loading, so a type that can't load counts as missing.
        internal static Type TypeIn(Assembly assembly, string typeName)
        {
            try { return assembly.GetType(typeName, false); }
            catch { return null; }
        }

        internal static void Log(string text)
        {
            try { Debug.Print("[Diplomacy.CoopCompat] " + text); }
            catch { }
        }
    }

    // Reads Coop's own state:
    // - GameInterface.ContainerProvider._lifetimeScope (Coop 0.1.6) or _active (Coop 0.1.7) is
    //   set while a co-op session runs (host or join) and cleared when it ends.
    // - Common.ModInformation.IsServer tells the server from a player.
    // If a future Coop renames these, the patch assumes a running session on a player's PC,
    // which is the safe side (the old v1.0.0 behaviour).
    internal static class Coop
    {
        private static bool _resolved;
        private static bool _modulesLoaded;
        private static bool _coopLoaded;
        private static FieldInfo _scope;
        private static PropertyInfo _isServer;

        public static bool InSession
        {
            get
            {
                Resolve();
                if (!_coopLoaded)
                    return false;
                if (_scope == null)
                    return true;
                try { return _scope.GetValue(null) != null; }
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

        private static void Resolve()
        {
            if (_resolved)
                return;

            Type container = null, info = null;
            var coop = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = assembly.GetName().Name ?? ""; }
                catch { continue; }
                if (name == "GameInterface")
                {
                    coop = true;
                    container = container ?? SubModule.TypeIn(assembly, "GameInterface.ContainerProvider");
                }
                else if (name == "Coop.Core" || (name == "Coop" && SubModule.TypeIn(assembly, "Coop.CoopMod") != null))
                {
                    coop = true;
                }
                if (info == null && name == "Common")
                    info = SubModule.TypeIn(assembly, "Common.ModInformation");
            }

            if (!coop)
            {
                // Before the main menu Coop may simply not be loaded yet; look again later.
                if (_modulesLoaded)
                    _resolved = true;
                return;
            }

            // Coop's Common.dll can load a moment after GameInterface; until every module is loaded, wait for it
            // instead of settling on "a player's PC" for good.
            if (info == null && !_modulesLoaded)
                return;

            _coopLoaded = true;
            const BindingFlags staticField = BindingFlags.Static | BindingFlags.NonPublic;
            try
            {
                _scope = container == null ? null
                    : container.GetField("_lifetimeScope", staticField) ?? container.GetField("_active", staticField);
            }
            catch { _scope = null; }
            try { _isServer = info == null ? null : AccessTools.Property(info, "IsServer"); }
            catch { _isServer = null; }
            if (_scope == null || _isServer == null)
                SubModule.Log("Could not read Coop's session state; assuming a player's PC in a session.");
            _resolved = true;
        }
    }

    internal static class Patches
    {
        public const string MessengerReason = "Messengers don't work in co-op. Go and talk to the lord in person.";
        public const string DirectActionReason = "In co-op, use the game's own Propose buttons. Diplomacy's direct actions would only change your PC.";
        public const string PactReason = "Non-aggression pacts don't work in co-op yet.";
        public const string GrantFiefReason = "In co-op, give fiefs with Kingdom > Fiefs > Gift.";
        public const string ReturnFiefReason = "Returning fiefs doesn't work in co-op yet.";
        public const string DonateGoldReason = "Donating gold doesn't work in co-op yet.";
        public const string FactionsReason = "Rebel factions and civil wars are off in co-op.";

        private static MethodInfo _acceptPeace;
        private static MethodInfo _peaceProposalSent;
        private static PropertyInfo _eventsInstance;

        public static int Apply(Harmony harmony, Assembly diplomacy)
        {
            var count = 0;

            // Both sides: features Coop can't sync.
            var settings = SubModule.TypeIn(diplomacy, "Diplomacy.Settings");
            count += Postfix(harmony, settings, "get_EnableWarExhaustion", nameof(FalseInSession));
            count += Postfix(harmony, settings, "get_EnableFiefFirstRight", nameof(FalseInSession));
            count += Postfix(harmony, settings, "get_EnableKingdomElimination", nameof(FalseInSession));
            // Diplomacy 1.5.4+: "Return Fief" hands a settlement back from the diplomacy screen, which would
            // only change that PC. Off in a session; the button is hidden while this setting reads false.
            count += Postfix(harmony, settings, "get_EnableFiefReturn", nameof(FalseInSession));
            count += Postfix(harmony, settings, "get_DailyChanceToStartRebelFaction", nameof(ZeroInSession));
            count += Postfix(harmony, settings, "get_DailyChanceToStartCivilWar", nameof(ZeroInSession));

            count += PatchMessengers(harmony, diplomacy);

            // Players' PCs: Diplomacy buttons that only change that PC.
            var truce = SubModule.TypeIn(diplomacy, "Diplomacy.ViewModelMixin.KingdomTruceItemVMMixin");
            count += Postfix(harmony, truce, "UpdateActionAvailability", nameof(TruceButtonsOff));
            count += Prefix(harmony, truce, "ExecuteDirectAction", nameof(BlockDirectAction));
            count += Prefix(harmony, truce, "ProposeNonAggressionPact", nameof(BlockPact));
            count += Prefix(harmony, truce, "ExecuteReturnFief", nameof(BlockReturnFief));

            var war = SubModule.TypeIn(diplomacy, "Diplomacy.ViewModelMixin.KingdomWarItemVMMixin");
            count += Postfix(harmony, war, "UpdateActionAvailability", nameof(WarButtonOff));
            count += Prefix(harmony, war, "ExecuteDirectAction", nameof(BlockDirectAction));

            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.Actions.GrantFiefAction"), "CanGrantFief", nameof(CannotGrantFief));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.ViewModelMixin.KingdomClanVMMixin"), "GrantFief", nameof(BlockGrantFief));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.ViewModelMixin.EncyclopediaHeroPageVMMixin"), "GrantFief", nameof(BlockGrantFief));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.ViewModelMixin.KingdomClanVMMixin"), "DonateGold", nameof(BlockDonateGold));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.ViewModelMixin.KingdomManagementVMMixin"), "ExecuteShowFactions", nameof(BlockFactions));

            // Players' PCs: Diplomacy's background logic stays on the server. Their saved data
            // (SyncData) still loads, so nothing is lost when the save goes back to single player.
            foreach (var behavior in new[] { "KingdomRulerRepairBehavior", "CivilWarBehavior", "DiplomaticAgreementBehavior",
                         "ExpansionismBehavior", "CooldownBehavior", "KeepFiefAfterSiegeBehavior", "WarExhaustionBehavior",
                         "FiefProvenanceBehavior", "FactionRecruitmentBehavior" })
                count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.CampaignBehaviors." + behavior), "RegisterEvents", nameof(SkipOnPlayerPc));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.CampaignBehaviors.KingdomRulerRepairBehavior"), "EnsureRuler", nameof(NoRulerRepairOnPlayerPc));

            // Server: answer questions nobody can click.
            var peace = SubModule.TypeIn(diplomacy, "Diplomacy.DiplomaticAction.WarPeace.KingdomPeaceAction");
            _acceptPeace = peace == null ? null : Safe(() => AccessTools.Method(peace, "AcceptPeace"));
            var events = SubModule.TypeIn(diplomacy, "Diplomacy.Events.DiplomacyEvents");
            _eventsInstance = events == null ? null : Safe(() => AccessTools.Property(events, "Instance"));
            _peaceProposalSent = events == null ? null : Safe(() => AccessTools.Method(events, "OnPeaceProposalSent"));
            if (_acceptPeace != null)
                count += Prefix(harmony, peace, "NotifyPlayerOfPeace", nameof(ServerAcceptsForcedPeace));
            else
                SubModule.Log("KingdomPeaceAction.AcceptPeace was not found; forced peace is left alone.");
            count += Prefix(harmony, peace, "CreatePeaceInquiry", nameof(ServerDeclinesPeaceOffer));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.DiplomaticAction.NonAggressionPact.FormNonAggressionPactAction"), "ShowPlayerInquiry", nameof(SkipOnServer));
            count += Prefix(harmony, SubModule.TypeIn(diplomacy, "Diplomacy.DiplomaticAction.NonAggressionPactAgreement"), "NotifyExpired", nameof(SkipOnServer));
            return count;
        }

        private static int PatchMessengers(Harmony harmony, Assembly diplomacy)
        {
            var messengers = SubModule.TypeIn(diplomacy, "Diplomacy.Messengers.MessengerManager");
            if (messengers == null)
            {
                SubModule.Log("Diplomacy.Messengers.MessengerManager was not found.");
                return 0;
            }

            var count = 0;
            foreach (var method in DeclaredMethods(messengers).Where(m => m.Name == "CanSendMessengerWithCost"))
            {
                try
                {
                    var parameters = method.GetParameters();
                    var withReason = parameters.Length == 3 && parameters[2].ParameterType.IsByRef
                        && parameters[2].Name == "exception"
                        && parameters[2].ParameterType.GetElementType() == typeof(TextObject);
                    if (parameters.Length == 3 && !withReason)
                    {
                        SubModule.Log("MessengerManager.CanSendMessengerWithCost has an unknown shape; left alone.");
                        continue;
                    }

                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patches), withReason ? nameof(CannotSendMessengerWithReason) : nameof(CannotSendMessenger)));
                    count++;
                }
                catch (Exception ex)
                {
                    SubModule.Log("MessengerManager.CanSendMessengerWithCost could not be hooked: " + ex.Message);
                }
            }

            count += Prefix(harmony, messengers, "SendMessengerWithCost", nameof(NoSend));
            count += Prefix(harmony, messengers, "SendMessenger", nameof(NoSend));
            count += Prefix(harmony, messengers, "UpdateMessengerPositions", nameof(SkipInSession));
            count += Prefix(harmony, messengers, "StartDialogue", nameof(SkipInSession));
            return count;
        }

        // A member lookup that throws counts as missing, so the hooks after it still go in.
        private static T Safe<T>(Func<T> lookup) where T : class
        {
            try { return lookup(); }
            catch { return null; }
        }

        // A type whose methods name a DLL the server doesn't have throws here; skip just that type.
        private static MethodInfo[] DeclaredMethods(Type type)
        {
            try { return AccessTools.GetDeclaredMethods(type).ToArray(); }
            catch (Exception ex)
            {
                SubModule.Log(type.Name + " could not be read: " + ex.Message);
                return new MethodInfo[0];
            }
        }

        private static int Prefix(Harmony harmony, Type type, string methodName, string patch) => Hook(harmony, type, methodName, patch, true);
        private static int Postfix(Harmony harmony, Type type, string methodName, string patch) => Hook(harmony, type, methodName, patch, false);

        private static int Hook(Harmony harmony, Type type, string methodName, string patch, bool prefix)
        {
            if (type == null)
            {
                SubModule.Log("A Diplomacy type for " + methodName + " was not found; left alone.");
                return 0;
            }

            var hooked = 0;
            foreach (var method in DeclaredMethods(type).Where(m => m.Name == methodName))
            {
                try
                {
                    var hook = new HarmonyMethod(typeof(Patches), patch);
                    if (prefix)
                        harmony.Patch(method, prefix: hook);
                    else
                        harmony.Patch(method, postfix: hook);
                    hooked++;
                }
                catch (Exception ex)
                {
                    SubModule.Log(type.Name + "." + methodName + " could not be hooked: " + ex.Message);
                }
            }

            if (hooked == 0)
                SubModule.Log(type.Name + "." + methodName + " was not found; left alone.");
            return hooked;
        }

        private static void Tell(string text)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(text)); }
            catch { }
        }

        // ---- Both sides ----

        private static void FalseInSession(ref bool __result)
        {
            if (Coop.InSession)
                __result = false;
        }

        private static void ZeroInSession(ref float __result)
        {
            if (Coop.InSession)
                __result = 0f;
        }

        private static bool SkipInSession() => !Coop.InSession;

        // CanSendMessengerWithCost(Hero targetHero, GoldCost diplomacyCost)
        private static bool CannotSendMessenger(ref bool __result)
        {
            if (!Coop.InSession)
                return true;
            __result = false;
            return false;
        }

        // CanSendMessengerWithCost(Hero targetHero, GoldCost diplomacyCost, out TextObject exception)
        // The reason shows as the greyed-out button's tooltip.
        private static bool CannotSendMessengerWithReason(ref bool __result, ref TextObject exception)
        {
            if (!Coop.InSession)
                return true;
            exception = new TextObject(MessengerReason);
            __result = false;
            return false;
        }

        private static bool NoSend()
        {
            if (!Coop.InSession)
                return true;
            Tell(MessengerReason);
            return false;
        }

        // ---- Players' PCs ----

        private static void TruceButtonsOff(object __instance)
        {
            if (!Coop.OnPlayerPc)
                return;
            SetButton(__instance, "IsDirectActionEnabled", "DirectActionHint", DirectActionReason);
            SetButton(__instance, "IsNonAggressionPactAvailable", "NonAggressionPactHint", PactReason);
        }

        private static void WarButtonOff(object __instance)
        {
            if (Coop.OnPlayerPc)
                SetButton(__instance, "IsDirectActionEnabled", "DirectActionHint", DirectActionReason);
        }

        // Greys a Diplomacy button out and puts the reason in its tooltip. The tooltip type
        // (HintViewModel) is read from the property so this DLL needs no extra game reference.
        private static void SetButton(object viewModel, string enabledProperty, string hintProperty, string reason)
        {
            try
            {
                var type = viewModel.GetType();
                AccessTools.Property(type, enabledProperty)?.SetValue(viewModel, false, null);
                var hint = AccessTools.Property(type, hintProperty);
                var value = hint == null ? null : NewHint(hint.PropertyType, reason);
                if (value != null)
                    hint.SetValue(viewModel, value, null);
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not grey out " + enabledProperty + ": " + ex.Message);
            }
        }

        private static object NewHint(Type hintType, string reason)
        {
            var constructor = hintType.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType == typeof(TextObject)
                    && c.GetParameters().Skip(1).All(p => p.IsOptional));
            if (constructor == null)
                return null;
            var args = constructor.GetParameters().Select((p, i) => i == 0 ? new TextObject(reason) : p.DefaultValue).ToArray();
            return constructor.Invoke(args);
        }

        private static bool BlockDirectAction() => Block(DirectActionReason);
        private static bool BlockPact() => Block(PactReason);
        private static bool BlockGrantFief() => Block(GrantFiefReason);
        private static bool BlockReturnFief() => Block(ReturnFiefReason);
        private static bool BlockDonateGold() => Block(DonateGoldReason);
        private static bool BlockFactions() => Block(FactionsReason);

        private static bool Block(string reason)
        {
            if (!Coop.OnPlayerPc)
                return true;
            Tell(reason);
            return false;
        }

        // CanGrantFief(Clan targetClan, out string reason)
        private static bool CannotGrantFief(ref bool __result, ref string reason)
        {
            if (!Coop.OnPlayerPc)
                return true;
            reason = GrantFiefReason;
            __result = false;
            return false;
        }

        private static bool SkipOnPlayerPc() => !Coop.OnPlayerPc;

        // EnsureRuler(Kingdom kingdom): on a player's PC only report, never change who rules.
        // Diplomacy also calls this from its Kingdom.AddDecision patch.
        private static bool NoRulerRepairOnPlayerPc(Kingdom kingdom, ref bool __result)
        {
            if (!Coop.OnPlayerPc)
                return true;
            __result = kingdom?.Leader?.IsAlive == true;
            return false;
        }

        // ---- Server ----

        private static bool SkipOnServer() => !Coop.OnServer;

        // NotifyPlayerOfPeace(...) is an OK-only notice: the peace happens either way.
        private static bool ServerAcceptsForcedPeace(object[] __args)
        {
            if (!Coop.OnServer)
                return true;
            try
            {
                // (kingdomMakingPeace, otherKingdom, isATie, diplomacyCost, tribute, fiefsToBeReturned, hasFiefsRemaining, shouldBeDestroyed)
                _acceptPeace.Invoke(null, new[] { __args[0], __args[1], __args[3], __args[4], __args[5], __args[7] });
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not apply peace on the server: " + (ex.InnerException ?? ex).Message);
            }
            return false;
        }

        // CreatePeaceInquiry(...) asks a player kingdom to accept an AI peace offer. Nobody can
        // answer on a server, so it is declined; players still make peace with vanilla proposals.
        private static bool ServerDeclinesPeaceOffer(object[] __args)
        {
            if (!Coop.OnServer)
                return true;
            try
            {
                var events = _eventsInstance?.GetValue(null, null);
                if (events != null)
                    _peaceProposalSent?.Invoke(events, new[] { __args[0] });
            }
            catch (Exception ex)
            {
                SubModule.Log("Could not decline a peace offer on the server: " + (ex.InnerException ?? ex).Message);
            }
            return false;
        }
    }
}
