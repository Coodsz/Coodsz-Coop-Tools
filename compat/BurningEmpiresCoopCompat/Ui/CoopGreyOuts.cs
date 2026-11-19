using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BurningEmpiresCoopCompat
{
    // On a player's PC in a co-op session these Burning Empires actions only change the
    // player's own copy of the world and are never sent to the server, so they would drift or
    // vanish on the next join. Each control is disabled with a short co-op note and its execute
    // path is guarded:
    // - Government: the state religion and mainstream culture proposals (until W12 syncs them);
    // - the "Rename this settlement" menu option;
    // - the manual population transfer confirm;
    // - starting an armor crafting project;
    // - opening town management from the encyclopedia.
    // View model types are only named here, never referenced, so the server needs no UI DLLs.
    // The server and single player run unchanged.
    internal static class CoopGreyOuts
    {
        private const string Government = "BurningEmpires.BESystems.Government.BEGovernmentVM";
        private const string Rename = "BurningEmpires.BESystems.Settlements.BESettlementRenameCampaignBehavior";
        private const string Population = "BurningEmpires.BESystems.SettlementManagement.BEPopulationManagementVM";
        private const string Armor = "BurningEmpires.BESystems.Crafting.BEArmorCraftingVM";
        private const string Encyclopedia = "BurningEmpires.BESystems.Encyclopedia.BEEncyclopediaSettlementPageMixin";

        private static MethodInfo _setCanChangeReligion;
        private static MethodInfo _setReligionHint;
        private static MethodInfo _setCanChangeCulture;
        private static MethodInfo _setCultureHint;
        private static MethodInfo _setCanConfirm;
        private static MethodInfo _showPopulationError;
        private static MethodInfo _setStartEnabled;
        private static MethodInfo _setArmorStatus;
        private static MethodInfo _syncNativeMainAction;
        private static FieldInfo _armorNative;
        private static FieldInfo _armorMainActionHint;

        // One co-op tooltip per armor view model, so a refresh does not build a new one.
        private static readonly ConditionalWeakTable<object, object> ArmorHints = new ConditionalWeakTable<object, object>();

        private static string ReligionText => new TextObject("{=BECoopReligionProposalDisabled}Proposing a state religion is disabled in co-op for now.").ToString();
        private static string CultureText => new TextObject("{=BECoopCultureProposalDisabled}Proposing a mainstream culture is disabled in co-op for now.").ToString();
        private static TextObject RenameText => new TextObject("{=BECoopRenameDisabled}Renaming settlements is disabled in co-op.");
        private static string PopulationText => new TextObject("{=BECoopPopulationTransferDisabled}Moving population between settlements is disabled in co-op.").ToString();
        private static string ArmorText => new TextObject("{=BECoopArmorCraftingDisabled}Armor crafting is disabled in co-op. Smithing, smelting and refining still work.").ToString();
        private static string ManageText => new TextObject("{=BECoopRemoteManagementDisabled}Managing a town from the encyclopedia is disabled in co-op. Visit the town and use Manage town.").ToString();

        public static int Apply(Harmony harmony, Assembly burningEmpires)
        {
            var count = 0;

            var government = FindType(burningEmpires, Government);
            if (government != null)
            {
                _setCanChangeReligion = Setter(government, "CanChangeStateReligion");
                _setReligionHint = Setter(government, "ChangeStateReligionHintText");
                _setCanChangeCulture = Setter(government, "CanChangeMainstreamCulture");
                _setCultureHint = Setter(government, "ChangeMainstreamCultureHintText");
            }
            count += Patch(harmony, government, "RefreshStateReligionProposalAvailability", Type.EmptyTypes, postfix: nameof(ReligionAvailabilityPostfix));
            count += Patch(harmony, government, "RefreshMainstreamCultureProposalAvailability", Type.EmptyTypes, postfix: nameof(CultureAvailabilityPostfix));
            count += Patch(harmony, government, "ExecuteOpenStateReligionChoice", Type.EmptyTypes, prefix: nameof(ReligionExecutePrefix));
            count += Patch(harmony, government, "ExecuteOpenMainstreamCultureChoice", Type.EmptyTypes, prefix: nameof(CultureExecutePrefix));
            count += Patch(harmony, government, "CommitStateReligionProposal", new[] { typeof(string) }, prefix: nameof(ReligionExecutePrefix));
            count += Patch(harmony, government, "CommitMainstreamCultureProposal", new[] { typeof(string) }, prefix: nameof(CultureExecutePrefix));

            var rename = FindType(burningEmpires, Rename);
            count += Patch(harmony, rename, "RenameCondition", new[] { typeof(MenuCallbackArgs) }, postfix: nameof(RenameConditionPostfix));
            count += Patch(harmony, rename, "RenameConsequence", new[] { typeof(MenuCallbackArgs) }, prefix: nameof(RenameConsequencePrefix));

            var population = FindType(burningEmpires, Population);
            if (population != null)
            {
                _setCanConfirm = Setter(population, "CanConfirm");
                _showPopulationError = AccessTools.DeclaredMethod(population, "ShowError", new[] { typeof(string) });
            }
            count += Patch(harmony, population, "RefreshComputedState", Type.EmptyTypes, postfix: nameof(PopulationStatePostfix));
            count += Patch(harmony, population, "ExecuteConfirm", Type.EmptyTypes, prefix: nameof(PopulationConfirmPrefix));

            var armor = FindType(burningEmpires, Armor);
            if (armor != null)
            {
                _setStartEnabled = Setter(armor, "IsStartEnabled");
                _setArmorStatus = Setter(armor, "StatusText");
                _syncNativeMainAction = AccessTools.DeclaredMethod(armor, "SyncNativeMainAction", Type.EmptyTypes);
                _armorNative = AccessTools.DeclaredField(armor, "_native");
                _armorMainActionHint = AccessTools.DeclaredField(armor, "_mainActionHint");
            }
            count += Patch(harmony, armor, "RefreshStartState", Type.EmptyTypes, postfix: nameof(ArmorStartStatePostfix));
            count += Patch(harmony, armor, "ExecuteStart", Type.EmptyTypes, prefix: nameof(ArmorStartPrefix));

            // The mixin derives from a UIExtenderEx type; FindType logs and moves on if that
            // cannot load. The button stays visible, a click explains the vanilla way.
            var encyclopedia = FindType(burningEmpires, Encyclopedia);
            count += Patch(harmony, encyclopedia, "ExecuteOpenSettlementManagement", Type.EmptyTypes, prefix: nameof(OpenManagementPrefix));

            return count;
        }

        private static Type FindType(Assembly assembly, string name)
        {
            try
            {
                var type = assembly.GetType(name, false);
                if (type == null)
                    SubModule.Log("Co-op grey out: type " + name + " not found.");
                return type;
            }
            catch (Exception ex)
            {
                SubModule.Log("Co-op grey out: could not load " + name + ": " + ex.Message);
                return null;
            }
        }

        private static MethodInfo Setter(Type type, string property)
        {
            var setter = AccessTools.PropertySetter(type, property);
            if (setter == null)
                SubModule.Log("Co-op grey out: " + type.Name + "." + property + " setter not found.");
            return setter;
        }

        private static int Patch(Harmony harmony, Type type, string name, Type[] args, string prefix = null, string postfix = null)
        {
            if (type == null)
                return 0;
            try
            {
                var method = AccessTools.DeclaredMethod(type, name, args);
                if (method == null)
                {
                    SubModule.Log("Co-op grey out: " + type.Name + "." + name + " not found.");
                    return 0;
                }
                harmony.Patch(method,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(CoopGreyOuts), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(CoopGreyOuts), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                SubModule.Log("Co-op grey out: could not patch " + type.Name + "." + name + ": " + ex.Message);
                return 0;
            }
        }

        private static void Set(MethodInfo setter, object instance, object value)
        {
            if (setter == null || instance == null)
                return;
            try { setter.Invoke(instance, new[] { value }); }
            catch (Exception ex) { SubModule.Log("Co-op grey out: " + setter.Name + " failed: " + ex.Message); }
        }

        private static void ShowMessage(string text)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(text)); }
            catch { }
        }

        // --- Government ---
        // With Coop's decision registry the proposals go through the server, so they stay enabled.

        private static void ReligionAvailabilityPostfix(object __instance)
        {
            if (!Coop.OnPlayerPc || BeDecisionSync.Registered)
                return;
            Set(_setCanChangeReligion, __instance, false);
            Set(_setReligionHint, __instance, ReligionText);
        }

        private static void CultureAvailabilityPostfix(object __instance)
        {
            if (!Coop.OnPlayerPc || BeDecisionSync.Registered)
                return;
            Set(_setCanChangeCulture, __instance, false);
            Set(_setCultureHint, __instance, CultureText);
        }

        private static bool ReligionExecutePrefix()
        {
            if (!Coop.OnPlayerPc || BeDecisionSync.Registered)
                return true;
            ShowMessage(ReligionText);
            return false;
        }

        private static bool CultureExecutePrefix()
        {
            if (!Coop.OnPlayerPc || BeDecisionSync.Registered)
                return true;
            ShowMessage(CultureText);
            return false;
        }

        // --- Settlement rename ---

        // A true result shows the option; Burning Empires hides it with no settlement or clan.
        private static void RenameConditionPostfix(MenuCallbackArgs args, bool __result)
        {
            if (!__result || args == null || !Coop.OnPlayerPc)
                return;
            args.IsEnabled = false;
            args.Tooltip = RenameText;
        }

        private static bool RenameConsequencePrefix()
        {
            if (!Coop.OnPlayerPc)
                return true;
            ShowMessage(RenameText.ToString());
            return false;
        }

        // --- Manual population transfer ---

        // The transfer panel has no hint on its confirm button, so the note goes in its error line.
        private static void PopulationStatePostfix(object __instance)
        {
            if (!Coop.OnPlayerPc)
                return;
            Set(_setCanConfirm, __instance, false);
            Set(_showPopulationError, __instance, PopulationText);
        }

        private static bool PopulationConfirmPrefix(object __instance)
        {
            if (!Coop.OnPlayerPc)
                return true;
            Set(_showPopulationError, __instance, PopulationText);
            return false;
        }

        // --- Armor crafting ---

        // RefreshStartState already pushed its answer to the native crafting screen, so push
        // again, then swap the start button's hint for the co-op note while in armor mode.
        private static void ArmorStartStatePostfix(object __instance)
        {
            if (!Coop.OnPlayerPc)
                return;
            Set(_setStartEnabled, __instance, false);
            Set(_setArmorStatus, __instance, ArmorText);
            try
            {
                _syncNativeMainAction?.Invoke(__instance, null);
                var native = _armorNative?.GetValue(__instance);
                var hintProperty = native == null ? null : AccessTools.Property(native.GetType(), "MainActionHint");
                if (hintProperty == null || hintProperty.GetValue(native, null) != _armorMainActionHint?.GetValue(__instance))
                    return;
                var hint = ArmorHints.GetValue(__instance, _ => CreateTooltip(hintProperty.PropertyType));
                if (hint != null)
                    hintProperty.SetValue(native, hint, null);
            }
            catch (Exception ex)
            {
                SubModule.Log("Co-op grey out: armor crafting hint failed: " + ex.Message);
            }
        }

        private static object CreateTooltip(Type tooltipType)
        {
            var ctor = tooltipType.GetConstructor(new[] { typeof(Func<string>) });
            return ctor?.Invoke(new object[] { (Func<string>)(() => ArmorText) });
        }

        private static bool ArmorStartPrefix(object __instance, ref bool __result)
        {
            if (!Coop.OnPlayerPc)
                return true;
            Set(_setArmorStatus, __instance, ArmorText);
            __result = false;
            return false;
        }

        // --- Town management from the encyclopedia ---

        private static bool OpenManagementPrefix()
        {
            if (!Coop.OnPlayerPc)
                return true;
            ShowMessage(ManageText);
            return false;
        }
    }
}
