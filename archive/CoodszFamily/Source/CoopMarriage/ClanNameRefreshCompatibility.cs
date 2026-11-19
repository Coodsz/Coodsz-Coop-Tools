using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace CoopMarriage;

internal static class ClanNameRefreshCompatibility
{
    internal static void Install(Harmony harmony)
    {
        var handler = AccessTools.TypeByName("GameInterface.Services.Heroes.Handlers.HeroDataHandler");
        if (handler == null) return; // Native single-player has no Coop callback.
        var targets = handler.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.Name.StartsWith("<Handle_HeroChangeName>b__", StringComparison.Ordinal)
                && m.ReturnType == typeof(void) && m.GetParameters().Length == 0).ToArray();
        if (targets.Length != 1)
        {
            RuntimeProof.Write("Clan name refresh compatibility unavailable: unsupported Coop callback layout; native UI retained");
            return;
        }
        harmony.Patch(targets[0], transpiler: new HarmonyMethod(typeof(ClanNameRefreshCompatibility), nameof(Transpiler)));
        RuntimeProof.Write("Clan name refresh scoped to player clan; unrelated hero names still synchronize without rebuilding clan previews");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var heroes = __originalMethod.GetMethodBody()?.LocalVariables.Where(v => v.LocalType == typeof(Hero)).ToArray();
        var refresh = AccessTools.Method(typeof(ViewModel), nameof(ViewModel.RefreshValues), Type.EmptyTypes);
        var calls = code.Where(i => i.Calls(refresh)).ToArray();
        if (heroes == null || heroes.Length != 1 || calls.Length != 1)
        {
            RuntimeProof.Write("Clan name refresh compatibility unavailable: unexpected IL; native UI retained");
            return code;
        }
        int index = code.IndexOf(calls[0]);
        var loadHero = new CodeInstruction(OpCodes.Ldloc, heroes[0].LocalIndex);
        // The native null-check branch targets this call. Move that label to the
        // extra argument load, otherwise a branch would bypass the Hero argument.
        loadHero.labels.AddRange(calls[0].labels);
        calls[0].labels.Clear();
        loadHero.blocks.AddRange(calls[0].blocks);
        calls[0].blocks.Clear();
        code.Insert(index, loadHero);
        calls[0].opcode = OpCodes.Call;
        calls[0].operand = AccessTools.Method(typeof(ClanNameRefreshCompatibility), nameof(RefreshRelevantClan));
        return code;
    }

    private static void RefreshRelevantClan(ViewModel model, Hero changedHero)
    {
        // Keep the original name mutation, queue, thread and all network messages.
        // Only the subsequent unrelated UI redraw is removed. Real family/companion
        // renames still use the complete stock refresh, as do all other UI routes.
        if (model != null && changedHero != null && Clan.PlayerClan != null && changedHero.Clan == Clan.PlayerClan)
            model.RefreshValues();
    }
}
