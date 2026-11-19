// Improved Garrisons party size hand back, by Coodsz. One source file for the Join Password client module, Coodsz
// War Sync (the TAOM server) and Improved Garrisons Coop Compat (Coodsz Host's server). The rules without the game
// are in PartySizeHandBackRules.cs.
//
// Improved Garrisons adds its GarrisonpartySizeLimitModel with AddModel<PartySizeLimitModel>, so its BaseModel is
// the model that was active before it (TAOM's TaomPartySizeModel), but its GetPartyMemberSizeLimit starts from the
// game's DefaultPartySizeLimitModel and never asks BaseModel. The game uses the last model added and IG loads after
// TAOM, so TAOM's party size rules never run: its feats, troop weight, AI lord and garrison scaling, and the TAOM
// server tool's party settings, which TAOM.CoopCompat applies inside them. Every PC works out party sizes by itself,
// so this runs on the server and on every client.
//
// - IG's GetPartyMemberSizeLimit: right after its base call, the base result is swapped for the previous model's
//   result. IG's own code then keeps its special parties (transfer, guard, recruiter, template party) and adds its
//   multiplier lines on top, and the previous model's tooltip lines stay.
// - DefaultPartySizeLimitModel.CalculateGarrisonPartySizeLimit: IG does not override it, so a garrison limit asked
//   from IG's model goes to the previous model too.
// With no other mod's model before IG (plain Coop, AD1259) nothing changes. IG is found by name and never
// referenced. Any failure keeps IG's own result.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace Coodsz.IgPartySize
{
    internal static class PartySizeHandBack
    {
        private sealed class Decision
        {
            public Decision(PartySizeLimitModel? previous, bool handsBack)
            {
                Previous = previous;
                HandsBack = handsBack;
            }

            public PartySizeLimitModel? Previous { get; }
            public bool HandsBack { get; }
        }

        private static readonly object Gate = new object();
        private static bool _tried, _baseCallFound, _failLogged;
        private static Type? _igType;
        private static Func<MBGameModel<PartySizeLimitModel>, PartySizeLimitModel>? _baseModel;
        private static MethodInfo? _defaultMemberLimit;
        private static Action<string>? _log;
        private static volatile Decision? _decision;
        [ThreadStatic] private static int _depth;

        /// <summary>
        /// Patches once per game process, at a game start (every module's DLL is loaded by then and IG's model is
        /// used only later). Does nothing when IG is not loaded or something is missing, with one log line.
        /// </summary>
        public static void TryInstall(Action<string> log)
        {
            lock (Gate)
            {
                if (_tried)
                    return;
                _tried = true;
                _log = log;
            }

            try
            {
                // Another Coodsz module in this process already did it.
                if (AppDomain.CurrentDomain.GetData(PartySizeHandBackRules.ProcessKey) != null)
                    return;

                var ig = AccessTools.TypeByName(PartySizeHandBackRules.IgModelType);
                if (ig == null)
                {
                    Log(PartySizeHandBackRules.NotLoadedLine);
                    return;
                }

                var parameters = new[] { typeof(PartyBase), typeof(bool) };
                var igMemberLimit = AccessTools.DeclaredMethod(ig, "GetPartyMemberSizeLimit", parameters);
                _defaultMemberLimit = AccessTools.DeclaredMethod(typeof(DefaultPartySizeLimitModel), "GetPartyMemberSizeLimit", parameters);
                var garrisonLimit = AccessTools.DeclaredMethod(typeof(DefaultPartySizeLimitModel), "CalculateGarrisonPartySizeLimit",
                    new[] { typeof(Settlement), typeof(bool) });
                var baseModel = typeof(MBGameModel<PartySizeLimitModel>)
                    .GetProperty("BaseModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
                if (igMemberLimit == null || _defaultMemberLimit == null || garrisonLimit == null || baseModel == null)
                {
                    Log(PartySizeHandBackRules.MissingLine(igMemberLimit == null ? "Improved Garrisons' GetPartyMemberSizeLimit"
                        : baseModel == null ? "the game's MBGameModel.BaseModel" : "the game's DefaultPartySizeLimitModel methods"));
                    return;
                }

                _baseModel = (Func<MBGameModel<PartySizeLimitModel>, PartySizeLimitModel>)Delegate.CreateDelegate(
                    typeof(Func<MBGameModel<PartySizeLimitModel>, PartySizeLimitModel>), baseModel);
                _igType = ig;
                AppDomain.CurrentDomain.SetData(PartySizeHandBackRules.ProcessKey, typeof(PartySizeHandBack).Assembly.GetName().Name);

                var harmony = new Harmony(PartySizeHandBackRules.HarmonyId);
                harmony.Patch(igMemberLimit, transpiler: new HarmonyMethod(typeof(PartySizeHandBack), nameof(SwapBaseResult)));
                if (!_baseCallFound)
                {
                    // An IG update no longer starts from the game's model; the transpiler left its method as it was.
                    Log(PartySizeHandBackRules.MissingLine("the base call in Improved Garrisons' GetPartyMemberSizeLimit"));
                    return;
                }

                // The member swap is live by now, so a failure here turns off only the garrison part.
                try
                {
                    harmony.Patch(garrisonLimit, prefix: new HarmonyMethod(typeof(PartySizeHandBack), nameof(GarrisonLimitPrefix)));
                }
                catch (Exception e)
                {
                    Log(PartySizeHandBackRules.GarrisonOffLine(e.GetType().Name + ": " + e.Message));
                    return;
                }

                Log(PartySizeHandBackRules.InstalledLine);
            }
            catch (Exception e)
            {
                Log(PartySizeHandBackRules.MissingLine(e.GetType().Name + ": " + e.Message));
            }
        }

        // After IG's own base.GetPartyMemberSizeLimit(party, includeDescriptions), pass the result through
        // AfterBaseMemberLimit(result, this, party, includeDescriptions). Only the first such call is changed. Harmony
        // runs this again whenever another mod patches the same method, so the flag is per run.
        private static IEnumerable<CodeInstruction> SwapBaseResult(IEnumerable<CodeInstruction> instructions)
        {
            var after = AccessTools.Method(typeof(PartySizeHandBack), nameof(AfterBaseMemberLimit));
            var found = false;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (found || _defaultMemberLimit == null || !instruction.Calls(_defaultMemberLimit))
                    continue;
                found = true;
                _baseCallFound = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, after);
            }
        }

        private static ExplainedNumber AfterBaseMemberLimit(ExplainedNumber baseResult, DefaultPartySizeLimitModel instance, PartyBase party, bool includeDescriptions)
        {
            var previous = Previous(instance);
            if (previous == null)
                return baseResult;
            _depth++;
            try
            {
                return previous.GetPartyMemberSizeLimit(party, includeDescriptions);
            }
            catch (Exception e)
            {
                Failed(e);
                return baseResult;
            }
            finally
            {
                _depth--;
            }
        }

        private static bool GarrisonLimitPrefix(DefaultPartySizeLimitModel __instance, Settlement settlement, bool includeDescriptions, ref ExplainedNumber __result)
        {
            var previous = Previous(__instance);
            if (previous == null)
                return true;
            _depth++;
            try
            {
                __result = previous.CalculateGarrisonPartySizeLimit(settlement, includeDescriptions);
                return false;
            }
            catch (Exception e)
            {
                Failed(e);
                return true;
            }
            finally
            {
                _depth--;
            }
        }

        // The model to hand back to for this IG model, or null to keep IG's own result. A previous model that
        // somehow comes back to IG's model gets IG's own result (_depth), never a loop.
        private static PartySizeLimitModel? Previous(DefaultPartySizeLimitModel instance)
        {
            var ig = _igType;
            var baseModel = _baseModel;
            if (ig == null || baseModel == null || _depth > 0 || !ig.IsInstanceOfType(instance))
                return null;
            try
            {
                var previous = baseModel(instance);
                var decision = _decision;
                if (decision == null || !ReferenceEquals(decision.Previous, previous))
                {
                    var previousType = previous?.GetType().FullName;
                    decision = new Decision(previous, PartySizeHandBackRules.HandsBack(true, previousType));
                    _decision = decision;
                    Log(decision.HandsBack ? PartySizeHandBackRules.HandBackLine(previousType!) : PartySizeHandBackRules.GameModelLine);
                }

                return decision.HandsBack ? previous : null;
            }
            catch (Exception e)
            {
                Failed(e);
                return null;
            }
        }

        private static void Failed(Exception e)
        {
            if (_failLogged)
                return;
            _failLogged = true;
            Log(PartySizeHandBackRules.FailedLine(e.GetType().Name + ": " + e.Message));
        }

        private static void Log(string line)
        {
            try { _log?.Invoke(PartySizeHandBackRules.LogPrefix + line); }
            catch (Exception) { }
        }
    }
}
